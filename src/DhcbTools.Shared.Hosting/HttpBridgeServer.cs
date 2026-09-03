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
    /// Timeout: quá <see cref="Timeout"/> → đánh dấu Abandoned rồi trả 504; phía thực thi bỏ qua việc đã bỏ.
    /// </summary>
    public sealed class HttpBridgeServer : IDisposable
    {
        private readonly HttpListener _listener = new HttpListener();
        private readonly AuthLockout _lockout = new AuthLockout();
        private readonly BridgeJobStore _jobs = new BridgeJobStore();
        private CancellationTokenSource? _cts;
        private Task? _listenTask;
        private bool _disposed;

        public HttpBridgeServer(int port, string appName, string version)
        {
            Port = port;
            AppName = appName;
            Version = version;
        }

        public int Port { get; }

        public string AppName { get; }

        public string Version { get; }

        /// <summary>Thời gian tối đa chờ luồng UI xử lý một request.</summary>
        public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Trần cho <c>timeoutSeconds</c> của từng request (giai đoạn 10.5). Có trần để một client
        /// không giữ hàng đợi vô hạn — Revit chỉ có một luồng, lệnh nào cũng phải nhường chỗ.
        /// </summary>
        public TimeSpan MaxTimeout { get; set; } = TimeSpan.FromMinutes(10);

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
            Token = BridgeTokenStore.LoadOrCreate(tokenPath);

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

            try
            {
                if (req.HttpMethod == "GET" && path == "/health")
                {
                    WriteJson(res, 200, new { status = "ok", app = AppName, version = Version });
                    return;
                }

                if (_lockout.IsLocked)
                {
                    WriteJson(res, 429, new { error = "locked" });
                    return;
                }

                var contentType = req.HttpMethod == "GET" ? "application/json" : req.ContentType;
                if (!BridgeAuth.IsAuthorized(Token, req.Headers["Authorization"], contentType))
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

                string body;
                using (var reader = new StreamReader(req.InputStream, req.ContentEncoding ?? Encoding.UTF8))
                {
                    body = reader.ReadToEnd();
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
                try { WriteJson(res, 500, new { error = ex.Message }); } catch { /* client đã ngắt */ }
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
            var item = new BridgeWorkItem<BridgeRequest, CommandResult>(request);
            var result = Await(item, item.Completion.Task, () => ExecuteAsync(item),
                () => CommandResult.Fail("Timeout: " + AppName + " không xử lý trong " + (int)timeout.TotalSeconds
                    + " giây. Lệnh đã bị huỷ, không chạy. Lệnh chạy lâu (SleeveAuto, AutoRoute) thì gửi kèm"
                    + " \"timeoutSeconds\" lớn hơn."),
                ex => CommandResult.Fail("Lỗi thực thi: " + ex.Message), out var timedOut, timeout);

            // Cùng hình dạng với kết quả trả qua /progress — client không phải viết hai đường đọc.
            WriteJson(res, timedOut ? 504 : result.Success ? 200 : 500, Describe(result));
        }

        /// <summary>
        /// Nhận lệnh rồi trả ngay <c>202</c> kèm id; kết quả để lại trong <see cref="Jobs"/>.
        /// Không đặt timeout cho việc chạy nền: client đã không ngồi chờ thì cũng không có ai để trả 504,
        /// và huỷ giữa chừng một lệnh đang mở transaction còn nguy hiểm hơn là để nó chạy nốt.
        /// </summary>
        private void HandleExecuteAsync(HttpListenerResponse res, BridgeRequest request)
        {
            var job = _jobs.Add(request.Command, DateTime.UtcNow);
            var item = new BridgeWorkItem<BridgeRequest, CommandResult>(request);

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
            });
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
                status = job.Status == BridgeJobStatus.Running ? "running" : job.Status == BridgeJobStatus.Done ? "done" : "error",
                elapsedMs = job.ElapsedMs(now),
                result = job.Result,
                error = job.Error,
            });
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
                () => new { error = "Timeout: " + AppName + " không xử lý trong " + (int)Timeout.TotalSeconds + " giây." },
                ex => new { error = "Lỗi truy vấn: " + ex.Message }, out var timedOut);

            WriteJson(res, timedOut ? 504 : 200, result);
        }

        private void HandleChat(HttpListenerResponse res, string body)
        {
            if (Chat == null)
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

            WriteJson(res, 200, Chat(chat.Text));
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

        private TResult Await<TRequest, TResult>(
            BridgeWorkItem<TRequest, TResult> item,
            Task<TResult> task,
            Func<Task> dispatch,
            Func<TResult> onTimeout,
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
                item.MarkAbandoned();
                timedOut = true;
                _ = dispatchTask; // để lại chạy nền, kết quả bị bỏ
                return onTimeout();
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
            var bytes = Encoding.UTF8.GetBytes(json);
            res.StatusCode = statusCode;
            res.ContentType = "application/json; charset=utf-8";
            res.ContentLength64 = bytes.Length;
            try
            {
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
