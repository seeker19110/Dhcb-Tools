using System.Net;
using System.Text;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using DhcbTools.Core;
using DhcbTools.Core.AutoNumbering;
using DhcbTools.Core.Export;
using DhcbTools.Core.Health;
using DhcbTools.Core.MEPF;
using DhcbTools.Core.ModelCleanup;
using DhcbTools.Core.ParameterSync;
using DhcbTools.Core.ProjectInit;
using DhcbTools.Core.Query;

namespace DhcbTools.Revit.Bridge;

/// <summary>
/// HTTP Bridge cho phép agent AI (hoặc bất kỳ HTTP client nào) gửi lệnh vào Revit
/// đang chạy và nhận kết quả JSON trả về — không cần mở UI, không cần click chuột.
///
/// Luồng thực thi:
///   POST http://localhost:8765/execute  { "command": "AutoNumbering", "config": {...} }
///   → HttpListener (background thread) nhận request
///   → đưa vào CommandQueue, gọi ExternalEvent.Raise()
///   → Revit main thread chạy IExternalEventHandler.Execute()
///   → CommandResult → JSON → HTTP response
///
///   POST http://localhost:8765/query  { "query": "document_info" }
///   → tương tự nhưng trả về dữ liệu đọc (không transaction ghi)
///
///   GET  http://localhost:8765/health  → { "status": "ok", "port": 8765 }
///
/// Khởi động: App.cs gọi DhcbHttpBridge.Start() khi add-in load.
/// Dừng:      App.cs gọi DhcbHttpBridge.Stop() khi add-in unload.
/// </summary>
public sealed class DhcbHttpBridge : IDisposable
{
    public const int Port = 8765;

    private readonly HttpListener _listener = new();
    private readonly ExternalEvent _externalEvent;
    private readonly BridgeEventHandler _handler;
    private CancellationTokenSource? _cts;
    private Task? _listenTask;
    private bool _disposed;

    public DhcbHttpBridge()
    {
        _handler = new BridgeEventHandler();
        _externalEvent = ExternalEvent.Create(_handler);
    }

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
                ctx = await _listener.GetContextAsync();
            }
            catch
            {
                break; // listener đã stop
            }

            _ = Task.Run(() => HandleRequest(ctx), ct);
        }
    }

    private void HandleRequest(HttpListenerContext ctx)
    {
        var req = ctx.Request;
        var res = ctx.Response;

        // ── Health check ──────────────────────────────────────────
        if (req.HttpMethod == "GET" && req.Url?.AbsolutePath == "/health")
        {
            WriteJson(res, 200, new { status = "ok", port = Port, app = "Revit" });
            return;
        }

        // ── Chỉ chấp nhận POST ────────────────────────────────────
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

        // ── POST /execute ─────────────────────────────────────────
        if (path == "/execute")
        {
            HandleExecute(res, body);
            return;
        }

        // ── POST /query ───────────────────────────────────────────
        if (path == "/query")
        {
            HandleQuery(res, body);
            return;
        }

        WriteJson(res, 404, new { error = $"Endpoint không tồn tại: {path}. Dùng /execute hoặc /query." });
    }

    // ──────────────────────────────────────────────────────────────
    // /execute — ghi vào mô hình (qua ExternalEvent)
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

        // Marshal sang Revit main thread qua ExternalEvent
        var tcs = new TaskCompletionSource<CommandResult>();
        _handler.EnqueueCommand(bridgeReq, tcs);
        _externalEvent.Raise();

        CommandResult result;
        try
        {
            result = tcs.Task.Wait(30_000) ? tcs.Task.Result
                : CommandResult.Fail("Timeout: Revit không xử lý trong 30 giây.");
        }
        catch (System.Exception ex)
        {
            result = CommandResult.Fail($"Lỗi thực thi: {ex.Message}");
        }

        WriteJson(res, result.Success ? 200 : 500, new
        {
            success              = result.Success,
            summary              = result.Summary,
            affectedElementCount = result.AffectedElementCount,
            messages             = result.Messages,
            errors               = result.Errors,
        });
    }

    // ──────────────────────────────────────────────────────────────
    // /query — đọc ngữ cảnh (qua ExternalEvent, không ghi)
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
        _handler.EnqueueQuery(queryReq, tcs);
        _externalEvent.Raise();

        object queryResult;
        try
        {
            queryResult = tcs.Task.Wait(30_000) ? tcs.Task.Result
                : new { error = "Timeout: Revit không xử lý trong 30 giây." };
        }
        catch (System.Exception ex)
        {
            queryResult = new { error = $"Lỗi truy vấn: {ex.Message}" };
        }

        WriteJson(res, 200, queryResult);
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
        catch { /* client đã ngắt */ }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
        _externalEvent.Dispose();
        _listener.Close();
    }
}

// ──────────────────────────────────────────────────────────────
// IExternalEventHandler: chạy trên Revit main thread
// ──────────────────────────────────────────────────────────────

internal sealed class BridgeEventHandler : IExternalEventHandler
{
    private readonly System.Collections.Concurrent.ConcurrentQueue<(BridgeRequest Req, TaskCompletionSource<CommandResult> Tcs)>
        _commandQueue = new();

    private readonly System.Collections.Concurrent.ConcurrentQueue<(QueryRequest Req, TaskCompletionSource<object> Tcs)>
        _queryQueue = new();

    public void EnqueueCommand(BridgeRequest req, TaskCompletionSource<CommandResult> tcs)
        => _commandQueue.Enqueue((req, tcs));

    public void EnqueueQuery(QueryRequest req, TaskCompletionSource<object> tcs)
        => _queryQueue.Enqueue((req, tcs));

    public string GetName() => "DHCB HTTP Bridge";

    public void Execute(UIApplication app)
    {
        // Xử lý lệnh ghi
        while (_commandQueue.TryDequeue(out var item))
        {
            var (req, tcs) = item;
            try
            {
                var doc = app.ActiveUIDocument?.Document;
                if (doc is null)
                {
                    tcs.SetResult(CommandResult.Fail("Không có document nào đang mở trong Revit."));
                    continue;
                }
                tcs.SetResult(DispatchCommand(doc, req));
            }
            catch (System.Exception ex)
            {
                tcs.SetException(ex);
            }
        }

        // Xử lý truy vấn đọc
        while (_queryQueue.TryDequeue(out var item))
        {
            var (req, tcs) = item;
            try
            {
                var doc = app.ActiveUIDocument?.Document;
                if (doc is null)
                {
                    tcs.SetResult(new { error = "Không có document nào đang mở trong Revit." });
                    continue;
                }
                tcs.SetResult(RevitQueryHandler.Handle(doc, req));
            }
            catch (System.Exception ex)
            {
                tcs.SetResult(new { error = ex.Message });
            }
        }
    }

    private static CommandResult DispatchCommand(Document doc, BridgeRequest req)
    {
        var configJson = req.Config?.ToString() ?? "{}";

        return req.Command.ToUpperInvariant() switch
        {
            "PARAMETEREXPORT" => new ParameterExportCommand().Execute(
                doc, Deserialize<ParameterExportConfig>(configJson)),

            "PARAMETERIMPORT" => new ParameterImportCommand().Execute(
                doc, Deserialize<ParameterImportConfig>(configJson)),

            "REMOVEUNUSEDVIEWS" or "CLEANUP" => new RemoveUnusedViewsCommand().Execute(
                doc, Deserialize<CleanupConfig>(configJson)),

            "AUTONUMBERING" or "AUTONUMBER" => new AutoNumberingCommand().Execute(
                doc, Deserialize<AutoNumberingConfig>(configJson)),

            // ── Phase 1: Export + Health ──────────────────────────
            "BATCHEXPORT" or "EXPORT" => new BatchExportCommand().Execute(
                doc, Deserialize<ExportConfig>(configJson)),

            "HEALTHREPORT" or "HEALTH" => new HealthReportCommand().Execute(
                doc, Deserialize<HealthReportConfig>(configJson)),

            // ── Phase 2: Project Init ─────────────────────────────
            "PROJECTINFO" => new ProjectInfoCommand().Execute(
                doc, Deserialize<ProjectInfoConfig>(configJson)),

            "LEVELSETUP" or "CREATELEVELS" => new LevelSetupCommand().Execute(
                doc, Deserialize<LevelSetupConfig>(configJson)),

            "GRIDSETUP" or "CREATEGRIDS" => new GridSetupCommand().Execute(
                doc, Deserialize<GridSetupConfig>(configJson)),

            "FAMILYLOADER" or "LOADFAMILIES" => new FamilyLoaderCommand().Execute(
                doc, Deserialize<FamilyLoaderConfig>(configJson)),

            // ── Phase 3: MEPF ─────────────────────────────────────
            "SLEEVE" or "SLEEVES" => new SleeveCommand().Execute(
                doc, Deserialize<SleeveConfig>(configJson)),

            "ELEVATIONTAG" or "SETELEV" => new ElevationTagCommand().Execute(
                doc, Deserialize<ElevationTagConfig>(configJson)),

            "HANGER" or "HANGERS" => new HangerCommand().Execute(
                doc, Deserialize<HangerConfig>(configJson)),

            "CONNECTORCHECK" or "CHECKCONNECTORS" => new ConnectorCheckerCommand().Execute(
                doc, Deserialize<ConnectorCheckerConfig>(configJson)),

            "PIPESPLIT" or "SPLITPIPES" => new PipeSplitterCommand().Execute(
                doc, Deserialize<PipeSplitterConfig>(configJson)),

            _ => CommandResult.Fail($"Lệnh không xác định: \"{req.Command}\". " +
                 "Hợp lệ: ParameterExport, ParameterImport, Cleanup, AutoNumbering, " +
                 "BatchExport, HealthReport, ProjectInfo, LevelSetup, GridSetup, FamilyLoader, " +
                 "Sleeve, ElevationTag, Hanger, ConnectorCheck, PipeSplit."),
        };
    }

    private static T Deserialize<T>(string json)
    {
        var result = JsonConvert.DeserializeObject<T>(json);
        if (result is null)
            throw new InvalidOperationException($"Không thể deserialize config thành {typeof(T).Name}.");
        return result;
    }
}

// ──────────────────────────────────────────────────────────────
// DTOs
// ──────────────────────────────────────────────────────────────

public sealed class BridgeRequest
{
    [JsonProperty("command")]
    public string Command { get; set; } = string.Empty;

    [JsonProperty("config")]
    public JObject? Config { get; set; }
}
