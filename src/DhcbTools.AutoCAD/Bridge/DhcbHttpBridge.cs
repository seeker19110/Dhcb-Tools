using System.Net;
using System.Text;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using DhcbTools.Core.AutoCAD;
using DhcbTools.Core.AutoCAD.AutoNumbering;
using DhcbTools.Core.AutoCAD.DrawingCleanup;
using DhcbTools.Core.AutoCAD.LayerSync;
using DhcbTools.Core.AutoCAD.Query;
using DhcbTools.Shared.Logic;

namespace DhcbTools.AutoCAD.Bridge;

/// <summary>
/// HTTP Bridge cho AutoCAD — cùng giao thức với Revit Bridge (port 8766).
///
/// AutoCAD marshal sang main thread qua
/// Application.DocumentManager.ExecuteInCommandContextAsync()
/// — cách chính thức của Autodesk từ AutoCAD 2014+ (AcMgd managed).
///
/// Endpoints:
///   GET  http://localhost:8766/health  → { "status": "ok", "version": "..." } (không cần token)
///   POST http://localhost:8766/execute { "command": "LayerExport", "config": {...} }
///   POST http://localhost:8766/query   { "query": "drawing_info" }
/// </summary>
public sealed class DhcbHttpBridge : IDisposable
{
    public const int Port = 8766;
    private const string Version = "0.1";

    private readonly HttpListener _listener = new();
    private readonly string _token = BridgeTokenStore.LoadOrCreate();
    private readonly BridgeRateLimiter _rateLimiter = new();
    private CancellationTokenSource? _cts;
    private Task? _listenTask;
    private bool _disposed;

    public void Start()
    {
        _listener.Prefixes.Add($"http://localhost:{Port}/");
        _listener.Start();

        _cts = new CancellationTokenSource();
        _listenTask = Task.Run(() => ListenLoop(_cts.Token));
    }

    public void Stop()
    {
        _cts?.Cancel();
        try { _listener.Stop(); } catch { }
        try { _listenTask?.Wait(2000); } catch { }
    }

    private async Task ListenLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            HttpListenerContext ctx;
            try
            {
                ctx = await _listener.GetContextAsync();
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

        // ── Health check (không cần token) ────────────────────────
        if (req.HttpMethod == "GET" && req.Url?.AbsolutePath == "/health")
        {
            WriteJson(res, 200, new { status = "ok", version = Version });
            return;
        }

        // ── Chặn brute-force trước khi so token (lỗi #8) ──────────
        if (_rateLimiter.IsLockedOut())
        {
            WriteJson(res, 429, new { error = "too_many_attempts" });
            return;
        }

        // ── Xác thực token + Content-Type (lỗi #8) ────────────────
        if (!BridgeAuth.IsAuthorized(_token, req.Headers["Authorization"], req.ContentType))
        {
            _rateLimiter.RecordFailure();
            WriteJson(res, 401, new { error = "unauthorized" });
            return;
        }

        if (req.HttpMethod != "POST")
        {
            WriteJson(res, 405, new { error = "Chỉ hỗ trợ GET /health, POST /execute, POST /query" });
            return;
        }

        string body;
        using (var reader = new System.IO.StreamReader(req.InputStream, req.ContentEncoding))
        {
            body = reader.ReadToEnd();
        }

        var path = req.Url?.AbsolutePath ?? string.Empty;

        if (path == "/execute")
        {
            HandleExecute(res, body);
            return;
        }

        if (path == "/query")
        {
            HandleQuery(res, body);
            return;
        }

        WriteJson(res, 404, new { error = $"Endpoint không tồn tại: {path}. Dùng /execute hoặc /query." });
    }

    // ──────────────────────────────────────────────────────────────
    // /execute
    // ──────────────────────────────────────────────────────────────
    private void HandleExecute(HttpListenerResponse res, string body)
    {
        BridgeRequest? bridgeReq;
        try
        {
            bridgeReq = JsonConvert.DeserializeObject<BridgeRequest>(body);
        }
        catch (System.Exception ex)
        {
            WriteJson(res, 400, new { error = $"JSON không hợp lệ: {ex.Message}" });
            return;
        }

        if (bridgeReq is null || string.IsNullOrWhiteSpace(bridgeReq.Command))
        {
            WriteJson(res, 400, new { error = "Thiếu trường 'command'." });
            return;
        }

        var tcs = new TaskCompletionSource<CommandResult>();
        // Cờ huỷ: client hết thời gian chờ thì không chạy lệnh nữa, tránh sửa drawing sau lưng (lỗi #7).
        var cancelled = new System.Threading.CancellationTokenSource();

        Application.DocumentManager.ExecuteInCommandContextAsync(async _ =>
        {
            if (cancelled.IsCancellationRequested)
            {
                return;
            }

            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc is null)
            {
                tcs.SetResult(CommandResult.Fail("Không có drawing nào đang mở trong AutoCAD."));
                return;
            }

            try
            {
                tcs.SetResult(DispatchCommand(doc.Database, bridgeReq));
            }
            catch (System.Exception ex)
            {
                tcs.SetException(ex);
            }

            await Task.CompletedTask;
        }, null);

        CommandResult cmdResult;
        try
        {
            if (tcs.Task.Wait(30_000))
            {
                cmdResult = tcs.Task.Result;
            }
            else
            {
                cancelled.Cancel();
                cmdResult = CommandResult.Fail("Timeout: AutoCAD không xử lý trong 30 giây; lệnh đã bị huỷ, drawing không bị thay đổi.");
            }
        }
        catch (System.Exception ex)
        {
            cmdResult = CommandResult.Fail($"Lỗi thực thi: {ex.Message}");
        }

        WriteJson(res, cmdResult.Success ? 200 : 500, new
        {
            success      = cmdResult.Success,
            summary      = cmdResult.Summary,
            affectedCount = cmdResult.AffectedCount,
            messages     = cmdResult.Messages,
            errors       = cmdResult.Errors,
        });
    }

    // ──────────────────────────────────────────────────────────────
    // /query
    // ──────────────────────────────────────────────────────────────
    private void HandleQuery(HttpListenerResponse res, string body)
    {
        QueryRequest? queryReq;
        try
        {
            queryReq = JsonConvert.DeserializeObject<QueryRequest>(body);
        }
        catch (System.Exception ex)
        {
            WriteJson(res, 400, new { error = $"JSON không hợp lệ: {ex.Message}" });
            return;
        }

        if (queryReq is null || string.IsNullOrWhiteSpace(queryReq.Query))
        {
            WriteJson(res, 400, new { error = "Thiếu trường 'query'." });
            return;
        }

        var tcs = new TaskCompletionSource<object>();
        var cancelled = new System.Threading.CancellationTokenSource();

        Application.DocumentManager.ExecuteInCommandContextAsync(async _ =>
        {
            if (cancelled.IsCancellationRequested)
            {
                return;
            }

            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc is null)
            {
                tcs.SetResult(new { error = "Không có drawing nào đang mở trong AutoCAD." });
                return;
            }

            try
            {
                tcs.SetResult(AcadQueryHandler.Handle(doc.Database, queryReq));
            }
            catch (System.Exception ex)
            {
                tcs.SetResult(new { error = ex.Message });
            }

            await Task.CompletedTask;
        }, null);

        object queryResult;
        try
        {
            if (tcs.Task.Wait(30_000))
            {
                queryResult = tcs.Task.Result;
            }
            else
            {
                cancelled.Cancel();
                queryResult = new { error = "Timeout: AutoCAD không xử lý trong 30 giây; truy vấn đã bị huỷ." };
            }
        }
        catch (System.Exception ex)
        {
            queryResult = new { error = $"Lỗi truy vấn: {ex.Message}" };
        }

        WriteJson(res, 200, queryResult);
    }

    private static CommandResult DispatchCommand(Database db, BridgeRequest req)
    {
        var configJson = req.Config?.ToString() ?? "{}";

        return req.Command.ToUpperInvariant() switch
        {
            "LAYEREXPORT" => new LayerExportCommand().Execute(
                db, Deserialize<LayerExportConfig>(configJson)),

            "LAYERIMPORT" => new LayerImportCommand().Execute(
                db, Deserialize<LayerImportConfig>(configJson)),

            "CLEANUP" or "DRAWINGCLEANUP" => new DrawingCleanupCommand().Execute(
                db, Deserialize<CleanupConfig>(configJson)),

            "AUTONUMBERING" or "AUTONUMBER" => new AutoNumberingCommand().Execute(
                db, Deserialize<AutoNumberingConfig>(configJson)),

            _ => CommandResult.Fail($"Lệnh không xác định: \"{req.Command}\". " +
                 "Các lệnh hợp lệ: LayerExport, LayerImport, Cleanup, AutoNumbering."),
        };
    }

    private static T Deserialize<T>(string json)
    {
        var result = JsonConvert.DeserializeObject<T>(json);
        if (result is null)
            throw new InvalidOperationException($"Không thể deserialize config thành {typeof(T).Name}.");
        return result;
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
        catch { }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
        _listener.Close();
    }
}

// ──────────────────────────────────────────────────────────────
// Token persistence + rate limit — phần "vỏ" (I/O, trạng thái) của lỗi #8.
// Logic thuần (sinh/so khớp token) nằm ở DhcbTools.Shared.Logic.BridgeAuth, đã có test.
// Trùng với DhcbTools.Revit/Bridge/DhcbHttpBridge.cs — sẽ gộp khi có DhcbTools.Shared.Hosting
// (xem docs/dac-ta-tinh-nang.md §0.2).
// ──────────────────────────────────────────────────────────────

internal static class BridgeTokenStore
{
    public static string TokenFilePath => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DHCB", "bridge-token.txt");

    public static string LoadOrCreate()
    {
        var path = TokenFilePath;
        try
        {
            if (System.IO.File.Exists(path))
            {
                var existing = System.IO.File.ReadAllText(path).Trim();
                if (existing.Length >= 32)
                {
                    return existing;
                }
            }
        }
        catch (System.IO.IOException)
        {
            // Không đọc được thì sinh mới bên dưới.
        }

        var token = BridgeAuth.GenerateToken();
        var directory = System.IO.Path.GetDirectoryName(path)!;
        System.IO.Directory.CreateDirectory(directory);
        System.IO.File.WriteAllText(path, token);
        return token;
    }
}

/// <summary>Khoá tạm 5 phút sau 5 lần sai token trong 60 giây, chặn dò token bằng brute-force.</summary>
internal sealed class BridgeRateLimiter
{
    private static readonly TimeSpan FailureWindow = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(5);
    private const int MaxFailuresBeforeLockout = 5;

    private readonly object _gate = new();
    private readonly Queue<DateTime> _recentFailures = new();
    private DateTime? _lockedUntilUtc;

    public bool IsLockedOut()
    {
        lock (_gate)
        {
            return _lockedUntilUtc is { } until && DateTime.UtcNow < until;
        }
    }

    public void RecordFailure()
    {
        lock (_gate)
        {
            var now = DateTime.UtcNow;
            _recentFailures.Enqueue(now);
            while (_recentFailures.Count > 0 && now - _recentFailures.Peek() > FailureWindow)
            {
                _recentFailures.Dequeue();
            }

            if (_recentFailures.Count >= MaxFailuresBeforeLockout)
            {
                _lockedUntilUtc = now + LockoutDuration;
                _recentFailures.Clear();
            }
        }
    }
}

public sealed class BridgeRequest
{
    [JsonProperty("command")]
    public string Command { get; set; } = string.Empty;

    [JsonProperty("config")]
    public JObject? Config { get; set; }
}
