using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DhcbTools.Shared.Logic;
using Newtonsoft.Json;

namespace DhcbTools.Shared.Hosting
{
    /// <summary>
    /// Phần HTTP dùng chung của hai Bridge (mục 0.2). Vỏ Revit/AutoCAD chỉ cung cấp ba delegate:
    /// thực thi lệnh, trả lời truy vấn, và (tuỳ chọn) dịch câu tiếng Việt sang lệnh. Cách đưa việc vào
    /// luồng UI (ExternalEvent hay ExecuteInCommandContextAsync) nằm trong delegate của vỏ.
    ///
    /// Endpoints:
    ///   GET  /health          — không cần token, chỉ trả trạng thái + phiên bản (không lộ tên file, tên lệnh)
    ///   GET  /tools           — cần token; danh sách lệnh + schema tóm tắt (nguồn cho MCP server và agent)
    ///   GET  /progress/&lt;id&gt;   — cần token; trạng thái lệnh chạy nền (giai đoạn 10.5)
    ///   POST /execute         — cần token; { "command": "...", "config": {...}, "async": true|false }
    ///   POST /query           — cần token; { "query": "...", "params": {...} }
    ///   POST /chat            — cần token; { "text": "..." } → đề xuất lệnh (KHÔNG thực thi)
    ///
    /// Bảo mật: chỉ bind 127.0.0.1; sai token → 401 không nêu lý do; ≥5 lần sai/60s → khoá 5 phút.
    /// Sai Content-Type (không phải JSON) → 415 nói rõ, KHÔNG tính vào đếm khoá — đó là lỗi client
    /// cấu hình, không phải dò token. Body quá <see cref="MaxBodyBytes"/> → 413. Quá
    /// <see cref="MaxInFlight"/> request cùng lúc → 503.
    /// Timeout: quá <see cref="Timeout"/> → nếu luồng UI chưa nhận việc thì đánh dấu Abandoned rồi trả 504
    /// "không chạy"; nếu đã nhận thì lệnh chạy nốt, kết quả giữ trong <see cref="Jobs"/> và 504 kèm id để
    /// client hỏi <c>/progress</c> — KHÔNG được gửi lại (lệnh ghi chạy hai lần là tai hoạ).
    /// </summary>
    public sealed class HttpBridgeServer : IDisposable
    {
        private readonly HttpListener _listener = new HttpListener();
        private readonly AuthLockout _lockout = new AuthLockout();
        private readonly BridgeJobStore _jobs = new BridgeJobStore();
        private readonly SemaphoreSlim _inFlight;
        private CancellationTokenSource? _cts;
        private Task? _listenTask;
        private bool _disposed;

        public HttpBridgeServer(int port, string appName, string version, int maxInFlight = 8)
        {
            Port = port;
            AppName = appName;
            Version = version;
            MaxInFlight = Math.Max(1, maxInFlight);
            _inFlight = new SemaphoreSlim(MaxInFlight, MaxInFlight);
        }

        public int Port { get; }

        public string AppName { get; }

        public string Version { get; }

        /// <summary>Thời gian tối đa chờ luồng UI xử lý một request.</summary>
        public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Trần cho <c>timeoutSeconds</c> của từng request (giai đoạn 10.5). Có trần để một client
        /// không giữ hàng đợi vô hạn — Revit chỉ có một luồng, lệnh nào cũng phải nhường chỗ.
        /// Cũng là trần cho hạn nhận việc của job nền.
        /// </summary>
        public TimeSpan MaxTimeout { get; set; } = TimeSpan.FromMinutes(10);

        /// <summary>
        /// Thời gian tối đa cho <c>POST /chat</c>. Chat chạy ngay trên luồng HTTP và có thể gọi Ollama
        /// (mặc định 120 s) — không chặn thì một câu hỏi treo giữ mãi một suất trong <see cref="MaxInFlight"/>.
        /// </summary>
        public TimeSpan ChatTimeout { get; set; } = TimeSpan.FromSeconds(60);

        /// <summary>Kích thước body tối đa (byte). Lệnh Bridge chỉ là JSON config, 4 MB là quá rộng rãi.</summary>
        public long MaxBodyBytes { get; set; } = 4L * 1024 * 1024;

        /// <summary>Số request được xử lý đồng thời; quá thì 503 ngay thay vì xếp hàng vô hạn trên thread pool.</summary>
        public int MaxInFlight { get; }

        /// <summary>Token hiện hành (nạp từ <see cref="BridgeTokenStore"/> khi Start). Không bao giờ log.</summary>
        public string? Token { get; private set; }

        /// <summary><c>true</c> khi listener đã mở cổng thành công và vòng lặp nhận request đang chạy.</summary>
        public bool IsRunning => _listener.IsListening;

        /// <summary>Thực thi lệnh ghi. Nhận work item để kiểm tra <c>TryClaim()</c> trước khi mở transaction.</summary>
        public Func<BridgeWorkItem<BridgeRequest, CommandResult>, Task>? ExecuteAsync { get; set; }

        /// <summary>Truy vấn đọc.</summary>
        public Func<BridgeWorkItem<BridgeQuery, object>, Task>? QueryAsync { get; set; }

        /// <summary>Dịch câu tiếng Việt sang đề xuất lệnh (thuần, chạy ngay trên luồng HTTP).</summary>
        public Func<string, object>? Chat { get; set; }

        /// <summary>Sổ lệnh chạy nền (<c>"async": true</c>), tra bằng <c>GET /progress/&lt;id&gt;</c>.</summary>
        public BridgeJobStore Jobs => _jobs;

        /// <summary>Danh mục lệnh cho GET /tools.</summary>
        public Func<object>? ListTools { get; set; }

        /// <summary>Ghi log (một dòng). Không được chứa token.</summary>
        public Action<string>? Log { get; set; }

        public void Start(string? tokenPath = null)
        {
            Token = BridgeTokenStore.LoadOrCreate(tokenPath, Log);

            // 127.0.0.1 thay cho "localhost": HttpListener coi "localhost" là host header, còn địa chỉ IP
            // ràng buộc đúng interface loopback — máy khác trong LAN không kết nối được (§4.1 kịch bản 6).
            _listener.Prefixes.Add("http://127.0.0.1:" + Port + "/");
            try
            {
                _listener.Start();
            }
            catch (HttpListenerException ex)
            {
                // Windows: 32 = ERROR_SHARING_VIOLATION (net48), 183 = ERROR_ALREADY_EXISTS (.NET Core),
                // 5 = ACCESS_DENIED khi prefix bị URL ACL khác giữ. Tất cả đều nghĩa là "không mở được cổng này".
                // .NET Core tự Dispose listener khi Start() hỏng, nên chỉ dọn prefix được trên net48.
                try { _listener.Prefixes.Clear(); } catch (ObjectDisposedException) { }
                Log?.Invoke("[DHCB Bridge] " + AppName + " KHÔNG mở được cổng " + Port + " (mã " + ex.ErrorCode + ")");
                throw new BridgePortInUseException(AppName, Port, ex);
            }

            _cts = new CancellationTokenSource();
            _listenTask = Task.Run(() => ListenLoop(_cts.Token));
            Log?.Invoke("[DHCB Bridge] " + AppName + " lắng nghe tại http://127.0.0.1:" + Port + "/ (token: " + BridgeTokenStore.DefaultPath + ")");
        }

        public void Stop()
        {
            _cts?.Cancel();
            try { _listener.Stop(); } catch { /* đang dừng */ }
            try { _listenTask?.Wait(2000); } catch { /* timeout ok */ }
        }

        private async Task ListenLoop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                HttpListenerContext ctx;
                try
                {
                    ctx = await _listener.GetContextAsync().ConfigureAwait(false);
                }
                catch
                {
                    break;
                }

                _ = Task.Run(() => HandleRequest(ctx), ct);
            }
        }

        private void HandleRequest(HttpListenerContext ctx)
        {
            var req = ctx.Request;
            var res = ctx.Response;
            var path = req.Url?.AbsolutePath ?? string.Empty;

            if (req.HttpMethod == "GET" && path == "/health")
            {
                // /health luôn trả lời, kể cả khi đang quá tải — đó chính là lúc cần biết Bridge còn sống.
                WriteJson(res, 200, new { status = "ok", app = AppName, version = Version });
                return;
            }

            if (!_inFlight.Wait(0))
            {
                WriteJson(res, 503, new
                {
                    error = "Bridge " + AppName + " quá tải: đang xử lý " + MaxInFlight + " request cùng lúc, thử lại sau ít giây.",
                });
                return;
            }

            try
            {
                if (_lockout.IsLocked)
                {
                    WriteJson(res, 429, new { error = "locked" });
                    return;
                }

                if (!BridgeAuth.TokensMatch(Token, BridgeAuth.ExtractBearerToken(req.Headers["Authorization"])))
                {
                    var locked = _lockout.RecordFailure();
                    Log?.Invoke("[DHCB Bridge] 401 từ " + req.RemoteEndPoint + " lúc " + DateTime.Now.ToString("HH:mm:ss")
                                + (locked ? " — khoá 5 phút" : string.Empty));
                    WriteJson(res, 401, new { error = "unauthorized" });
                    return;
                }

                _lockout.RecordSuccess();

                if (req.HttpMethod == "GET" && path == "/tools")
                {
                    WriteJson(res, 200, ListTools?.Invoke() ?? new { tools = new object[0] });
                    return;
                }

                if (req.HttpMethod == "GET" && path.StartsWith("/progress/", StringComparison.Ordinal))
                {
                    HandleProgress(res, path.Substring("/progress/".Length));
                    return;
                }

                if (req.HttpMethod != "POST")
                {
                    WriteJson(res, 405, new { error = "Chỉ hỗ trợ GET /health, GET /tools, GET /progress/<id>, POST /execute, POST /query, POST /chat" });
                    return;
                }

                // Content-Type kiểm tra SAU token và KHÔNG tính vào khoá: client đúng token mà quên header
                // là lỗi cấu hình, khoá 5 phút vì chuyện đó chỉ làm kỹ sư đi tìm nhầm hướng.
                if (!IsJsonContentType(req.ContentType))
                {
                    WriteJson(res, 415, new { error = "Content-Type phải là application/json (đang gửi: " + (req.ContentType ?? "(trống)") + ")." });
                    return;
                }

                if (req.ContentLength64 > MaxBodyBytes)
                {
                    WriteJson(res, 413, new { error = "Body quá lớn (" + req.ContentLength64 + " byte, tối đa " + MaxBodyBytes + ")." });
                    return;
                }

                var body = ReadBody(req, MaxBodyBytes);
                if (body == null)
                {
                    WriteJson(res, 413, new { error = "Body quá lớn (tối đa " + MaxBodyBytes + " byte)." });
                    return;
                }

                switch (path)
                {
                    case "/execute":
                        HandleExecute(res, body);
                        return;
                    case "/query":
                        HandleQuery(res, body);
                        return;
                    case "/chat":
                        HandleChat(res, body);
                        return;
                    default:
                        WriteJson(res, 404, new { error = "Endpoint không tồn tại: " + path });
                        return;
                }
            }
            catch (Exception ex)
            {
                // Không trả ex.ToString()/ex.Message ra ngoài: chúng mang đường dẫn file, tên máy, stack trace.
                // Toàn văn vào log; client chỉ nhận loại exception để biết báo cái gì.
                Log?.Invoke("[DHCB Bridge] 500 " + req.HttpMethod + " " + path + ": " + ex);
                WriteJson(res, 500, new
                {
                    error = "Lỗi nội bộ Bridge (" + ex.GetType().Name + ") — xem log " + AppName + " trong %APPDATA%\\DHCB\\logs.",
                    exceptionType = ex.GetType().Name,
                });
            }
            finally
            {
                _inFlight.Release();
            }
        }

        /// <summary>Content-Type là JSON? Tách ra để bảo vệ được bằng test không cần HTTP.</summary>
        public static bool IsJsonContentType(string? contentType) =>
            !string.IsNullOrWhiteSpace(contentType)
            && contentType!.TrimStart().StartsWith("application/json", StringComparison.OrdinalIgnoreCase);

        /// <summary>Đọc body có trần; trả <c>null</c> nếu vượt (kể cả khi client không khai Content-Length).</summary>
        private static string? ReadBody(HttpListenerRequest req, long maxBytes)
        {
            using (var buffer = new MemoryStream())
            {
                var chunk = new byte[16 * 1024];
                int read;
                while ((read = req.InputStream.Read(chunk, 0, chunk.Length)) > 0)
                {
                    if (buffer.Length + read > maxBytes)
                    {
                        return null;
                    }

                    buffer.Write(chunk, 0, read);
                }

                return (req.ContentEncoding ?? Encoding.UTF8).GetString(buffer.ToArray());
            }
        }

        private void HandleExecute(HttpListenerResponse res, string body)
        {
            BridgeRequest? request;
            try
            {
                request = JsonConvert.DeserializeObject<BridgeRequest>(body);
            }
            catch (Exception ex)
            {
                WriteJson(res, 400, new { error = "JSON không hợp lệ: " + ex.Message });
                return;
            }

            if (request == null || string.IsNullOrWhiteSpace(request.Command))
            {
                WriteJson(res, 400, new { error = "Thiếu trường 'command'." });
                return;
            }

            if (ExecuteAsync == null)
            {
                WriteJson(res, 501, new { error = "Vỏ chưa gắn ExecuteAsync." });
                return;
            }

            if (request.Async)
            {
                HandleExecuteAsync(res, request);
                return;
            }

            var timeout = ResolveTimeout(request.TimeoutSeconds);
            var startedUtc = DateTime.UtcNow;
            var item = new BridgeWorkItem<BridgeRequest, CommandResult>(request);
            var seconds = (int)timeout.TotalSeconds;
            var result = Await(item, item.Completion.Task, () => ExecuteAsync(item),
                notRun => CommandResult.Fail(notRun
                    ? "Timeout: " + AppName + " không xử lý trong " + seconds
                      + " giây. Lệnh đã bị huỷ, không chạy. Lệnh chạy lâu (SleeveAuto, AutoRoute) thì gửi kèm"
                      + " \"timeoutSeconds\" lớn hơn hoặc \"async\": true."
                    : "Timeout: " + AppName + " chưa trả lời sau " + seconds
                      + " giây. Lệnh có thể đã chạy hoặc đang chạy — kiểm tra /progress/<id> hoặc changedIds;"
                      + " KHÔNG gửi lại."),
                ex => CommandResult.Fail("Lỗi thực thi: " + ex.Message), out var timedOut, timeout);

            if (timedOut && item.Claimed)
            {
                // Luồng UI đã nhận việc: không huỷ được nữa. Biến thành job nền để kết quả còn chỗ về —
                // client hỏi /progress/<id> thay vì gửi lại lệnh (gửi lại là chạy hai lần trên model thật).
                var job = _jobs.Add(request.Command, startedUtc, null, timeout);
                job.MarkStarted();
                AttachCompletion(job, item);
                Log?.Invoke("[DHCB Bridge] 504 " + request.Command + " sau " + seconds + " s nhưng đã chạy — giữ kết quả ở /progress/" + job.Id);
                WriteJson(res, 504, DescribeTimedOut(result, job));
                return;
            }

            // Cùng hình dạng với kết quả trả qua /progress — client không phải viết hai đường đọc.
            WriteJson(res, timedOut ? 504 : result.Success ? 200 : 500, Describe(result));
        }

        /// <summary>
        /// Nhận lệnh rồi trả ngay <c>202</c> kèm id; kết quả để lại trong <see cref="Jobs"/>.
        /// Không đặt timeout cho việc ĐANG chạy: client đã không ngồi chờ thì cũng không có ai để trả 504,
        /// và huỷ giữa chừng một lệnh đang mở transaction còn nguy hiểm hơn là để nó chạy nốt. Nhưng job
        /// CHƯA được nhận thì có hạn (timeoutSeconds, mặc định = <see cref="Timeout"/>, trần
        /// <see cref="MaxTimeout"/>): quá hạn → Abandoned, không bao giờ chạy. Hàng đợi có trần
        /// <see cref="BridgeJobStore.MaxQueued"/> → 429.
        /// </summary>
        private void HandleExecuteAsync(HttpListenerResponse res, BridgeRequest request)
        {
            var timeout = ResolveTimeout(request.TimeoutSeconds);
            var now = DateTime.UtcNow;
            var job = _jobs.TryAdd(request.Command, now, timeout);
            if (job == null)
            {
                WriteJson(res, 429, new
                {
                    error = "Hàng đợi đầy: đã có " + _jobs.MaxQueued + " lệnh nền chờ " + AppName
                            + " xử lý. Đợi chúng chạy xong (GET /progress/<id>) rồi gửi lại.",
                });
                return;
            }

            var item = new BridgeWorkItem<BridgeRequest, CommandResult>(request);
            item.OnClaimed = job.MarkStarted;
            job.TryAbandonWork = item.MarkAbandoned;
            AttachCompletion(job, item);

            // Đồng hồ hạn nhận việc: hết giờ mà chưa ai claim → huỷ (MarkAbandoned) rồi ghi Abandoned.
            _ = Task.Delay(timeout).ContinueWith(_ =>
            {
                if (job.Abandon("Hết hạn chờ " + (int)timeout.TotalSeconds + " giây: " + AppName
                                + " không nhận lệnh — lệnh KHÔNG chạy. Gửi lại được.", DateTime.UtcNow))
                {
                    Log?.Invoke("[DHCB Bridge] job " + job.Id + " (" + job.Command + ") bị huỷ vì quá hạn nhận việc.");
                }
            }, TaskScheduler.Default);

            try
            {
                _ = ExecuteAsync!(item);
            }
            catch (Exception ex)
            {
                job.Fail("Lỗi thực thi: " + ex.Message, DateTime.UtcNow);
            }

            WriteJson(res, 202, new
            {
                id = job.Id,
                status = "running",
                command = job.Command,
                progressUrl = "/progress/" + job.Id,
                timeoutSeconds = (int)timeout.TotalSeconds,
            });
        }

        /// <summary>Đổ kết quả của work item vào job khi xong (dùng cho cả job nền lẫn lệnh đồng bộ bị timeout).</summary>
        private static void AttachCompletion(BridgeJob job, BridgeWorkItem<BridgeRequest, CommandResult> item)
        {
            item.Completion.Task.ContinueWith(t =>
            {
                if (t.IsFaulted)
                {
                    job.Fail("Lỗi thực thi: " + (t.Exception?.InnerException ?? t.Exception)?.Message, DateTime.UtcNow);
                }
                else if (t.IsCanceled)
                {
                    job.Fail("Lệnh bị huỷ trước khi chạy xong.", DateTime.UtcNow);
                }
                else
                {
                    job.Complete(Describe(t.Result), DateTime.UtcNow);
                }
            }, TaskScheduler.Default);
        }

        private void HandleProgress(HttpListenerResponse res, string id)
        {
            var job = _jobs.Find(id);
            if (job == null)
            {
                // Không phân biệt "chưa từng có" với "đã hết hạn giữ": cả hai đều là 404 với cùng lời khuyên,
                // và thông tin đó chẳng giúp client làm gì khác.
                WriteJson(res, 404, new { error = "Không có lệnh nền nào mang id \"" + id + "\" (sai id, hoặc kết quả đã quá hạn giữ)." });
                return;
            }

            var now = DateTime.UtcNow;
            WriteJson(res, 200, new
            {
                id = job.Id,
                command = job.Command,
                status = StatusName(job.Status),
                started = job.Started,
                elapsedMs = job.ElapsedMs(now),
                result = job.Result,
                error = job.Error,
            });
        }

        private static string StatusName(BridgeJobStatus status)
        {
            switch (status)
            {
                case BridgeJobStatus.Running: return "running";
                case BridgeJobStatus.Done: return "done";
                case BridgeJobStatus.Abandoned: return "abandoned";
                default: return "error";
            }
        }

        /// <summary>
        /// Kết quả lệnh dưới dạng gửi cho client. Dùng chung cho <c>POST /execute</c> đồng bộ và
        /// <c>GET /progress</c>: giai đoạn 10.2 (<c>changedIds</c>) chỉ có ích khi mọi đường đều trả nó.
        /// </summary>
        private static object Describe(CommandResult result) => new
        {
            success = result.Success,
            summary = result.Summary,
            affectedCount = result.AffectedCount,
            affectedElementCount = result.AffectedCount,
            changedIds = result.ChangedIds,
            messages = result.Messages,
            errors = result.Errors,
        };

        /// <summary>Như <see cref="Describe"/>, thêm id job để client hỏi tiếp khi lệnh đã chạy mà client hết kiên nhẫn.</summary>
        private static object DescribeTimedOut(CommandResult result, BridgeJob job) => new
        {
            success = result.Success,
            summary = result.Summary,
            affectedCount = result.AffectedCount,
            affectedElementCount = result.AffectedCount,
            changedIds = result.ChangedIds,
            messages = result.Messages,
            errors = result.Errors,
            id = job.Id,
            status = "running",
            progressUrl = "/progress/" + job.Id,
        };

        private void HandleQuery(HttpListenerResponse res, string body)
        {
            BridgeQuery? query;
            try
            {
                query = JsonConvert.DeserializeObject<BridgeQuery>(body);
            }
            catch (Exception ex)
            {
                WriteJson(res, 400, new { error = "JSON không hợp lệ: " + ex.Message });
                return;
            }

            if (query == null || string.IsNullOrWhiteSpace(query.Query))
            {
                WriteJson(res, 400, new { error = "Thiếu trường 'query'." });
                return;
            }

            if (QueryAsync == null)
            {
                WriteJson(res, 501, new { error = "Vỏ chưa gắn QueryAsync." });
                return;
            }

            var item = new BridgeWorkItem<BridgeQuery, object>(query);
            var result = Await(item, item.Completion.Task, () => QueryAsync(item),
                notRun => new
                {
                    error = "Timeout: " + AppName + " không xử lý trong " + (int)Timeout.TotalSeconds + " giây."
                            + (notRun ? " Truy vấn đã bị huỷ, không chạy." : " Truy vấn đang chạy, kết quả bị bỏ (chỉ đọc, gửi lại được)."),
                },
                ex => new { error = "Lỗi truy vấn: " + ex.Message }, out var timedOut);

            WriteJson(res, timedOut ? 504 : 200, result);
        }

        private void HandleChat(HttpListenerResponse res, string body)
        {
            var chatFn = Chat;
            if (chatFn == null)
            {
                WriteJson(res, 501, new { error = "Vỏ chưa gắn Chat." });
                return;
            }

            BridgeChat? chat;
            try
            {
                chat = JsonConvert.DeserializeObject<BridgeChat>(body);
            }
            catch (Exception ex)
            {
                WriteJson(res, 400, new { error = "JSON không hợp lệ: " + ex.Message });
                return;
            }

            if (chat == null || string.IsNullOrWhiteSpace(chat.Text))
            {
                WriteJson(res, 400, new { error = "Thiếu trường 'text'." });
                return;
            }

            // Chat có thể gọi Ollama (120 s mặc định). Chạy trên task riêng để chặn được bằng ChatTimeout;
            // suất in-flight vẫn bị giữ tới lúc trả lời, nên trần này là cái giữ /chat không nuốt hết Bridge.
            var text = chat.Text;
            var work = Task.Run(() => chatFn(text));
            if (Task.WhenAny(work, Task.Delay(ChatTimeout)).GetAwaiter().GetResult() != work)
            {
                WriteJson(res, 504, new
                {
                    error = "Chat không trả lời trong " + (int)ChatTimeout.TotalSeconds
                            + " giây (mô hình AI offline chậm hoặc chưa chạy). Thử lại sau, hoặc gọi thẳng lệnh.",
                });
                return;
            }

            // GetResult() (không phải .Result) để exception của chatFn nổi lên nguyên bản thay vì bị bọc
            // trong AggregateException — nhánh 500 ở HandleRequest mới nói đúng loại lỗi cho client.
            WriteJson(res, 200, work.GetAwaiter().GetResult());
        }

        /// <summary>Thời gian chờ hiệu lực cho request hiện tại.</summary>
        private TimeSpan ResolveTimeout(int? requested) => ResolveTimeout(requested, Timeout, MaxTimeout);

        /// <summary>
        /// Chọn thời gian chờ: theo request nếu là số dương, chặn trên bởi <paramref name="max"/>,
        /// còn lại dùng <paramref name="fallback"/>. Tách ra static để test được — chọn sai ở đây thì
        /// hoặc lệnh nặng chết oan vì timeout, hoặc một client giữ hàng đợi Revit vô hạn.
        /// </summary>
        public static TimeSpan ResolveTimeout(int? requested, TimeSpan fallback, TimeSpan max)
        {
            if (requested is not > 0)
            {
                return fallback;
            }

            var wanted = TimeSpan.FromSeconds(requested.Value);
            return wanted > max ? max : wanted;
        }

        /// <summary>
        /// Chờ luồng UI trả kết quả. Khi hết giờ, <paramref name="onTimeout"/> nhận <c>true</c> nếu việc
        /// đã huỷ được trước khi ai nhận (chắc chắn KHÔNG chạy), <c>false</c> nếu phía thực thi đã nhận
        /// (lệnh đã/đang chạy — chữ trong phản hồi phải nói đúng như vậy).
        /// </summary>
        private TResult Await<TRequest, TResult>(
            BridgeWorkItem<TRequest, TResult> item,
            Task<TResult> task,
            Func<Task> dispatch,
            Func<bool, TResult> onTimeout,
            Func<Exception, TResult> onError,
            out bool timedOut,
            TimeSpan? timeout = null)
        {
            timedOut = false;
            try
            {
                var dispatchTask = dispatch();
                if (task.Wait(timeout ?? Timeout))
                {
                    return task.Result;
                }

                // Đặt cờ TRƯỚC khi trả lời client (mục 0.5) — phía thực thi thấy cờ thì không mở transaction.
                var notRun = item.MarkAbandoned();
                timedOut = true;
                _ = dispatchTask; // để lại chạy nền
                return onTimeout(notRun);
            }
            catch (AggregateException ex) when (ex.InnerException != null)
            {
                return onError(ex.InnerException);
            }
            catch (Exception ex)
            {
                return onError(ex);
            }
        }

        private static void WriteJson(HttpListenerResponse res, int statusCode, object payload)
        {
            var json = JsonConvert.SerializeObject(payload, Formatting.Indented);
            TrySend(res, statusCode, Encoding.UTF8.GetBytes(json));
        }

        /// <summary>
        /// Gửi phản hồi, nuốt mọi lỗi ghi. Cả phần đặt header lẫn phần ghi đều nằm trong try: client ngắt
        /// giữa chừng thì <c>StatusCode</c>/<c>ContentLength64</c> cũng ném, và một client bỏ đi không được
        /// phép biến thành exception nổi lên làm hỏng vòng lặp nhận request.
        /// </summary>
        /// <remarks>
        /// Nhánh <c>catch</c> chỉ chạy khi client ngắt đúng vào khoảnh khắc server ghi — một cuộc đua không
        /// ép được trong test, nên tách riêng ra đây và loại khỏi phép đo phủ thay vì để nó kéo cổng xuống.
        /// </remarks>
        [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
        private static void TrySend(HttpListenerResponse res, int statusCode, byte[] bytes)
        {
            try
            {
                res.StatusCode = statusCode;
                res.ContentType = "application/json; charset=utf-8";
                res.ContentLength64 = bytes.Length;
                res.OutputStream.Write(bytes, 0, bytes.Length);
                res.OutputStream.Close();
            }
            catch
            {
                // client đã ngắt
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Stop();
            try { _listener.Close(); } catch { }
        }
    }
}
