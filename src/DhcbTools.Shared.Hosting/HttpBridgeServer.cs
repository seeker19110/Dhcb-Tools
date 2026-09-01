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
    ///   POST /execute         — cần token; { "command": "...", "config": {...} }
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

        /// <summary>Token hiện hành (nạp từ <see cref="BridgeTokenStore"/> khi Start). Không bao giờ log.</summary>
        public string? Token { get; private set; }

        /// <summary>Thực thi lệnh ghi. Nhận work item để kiểm tra <c>TryClaim()</c> trước khi mở transaction.</summary>
        public Func<BridgeWorkItem<BridgeRequest, CommandResult>, Task>? ExecuteAsync { get; set; }

        /// <summary>Truy vấn đọc.</summary>
        public Func<BridgeWorkItem<BridgeQuery, object>, Task>? QueryAsync { get; set; }

        /// <summary>Dịch câu tiếng Việt sang đề xuất lệnh (thuần, chạy ngay trên luồng HTTP).</summary>
        public Func<string, object>? Chat { get; set; }

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
            _listener.Start();

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

                if (req.HttpMethod != "POST")
                {
                    WriteJson(res, 405, new { error = "Chỉ hỗ trợ GET /health, GET /tools, POST /execute, POST /query, POST /chat" });
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

            var item = new BridgeWorkItem<BridgeRequest, CommandResult>(request);
            var result = Await(item, item.Completion.Task, () => ExecuteAsync(item),
                () => CommandResult.Fail("Timeout: " + AppName + " không xử lý trong " + (int)Timeout.TotalSeconds + " giây. Lệnh đã bị huỷ, không chạy."),
                ex => CommandResult.Fail("Lỗi thực thi: " + ex.Message), out var timedOut);

            WriteJson(res, timedOut ? 504 : result.Success ? 200 : 500, new
            {
                success = result.Success,
                summary = result.Summary,
                affectedCount = result.AffectedCount,
                affectedElementCount = result.AffectedCount,
                messages = result.Messages,
                errors = result.Errors,
            });
        }

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

        private TResult Await<TRequest, TResult>(
            BridgeWorkItem<TRequest, TResult> item,
            Task<TResult> task,
            Func<Task> dispatch,
            Func<TResult> onTimeout,
            Func<Exception, TResult> onError,
            out bool timedOut)
        {
            timedOut = false;
            try
            {
                var dispatchTask = dispatch();
                if (task.Wait(Timeout))
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
