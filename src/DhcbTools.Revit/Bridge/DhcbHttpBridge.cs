using System.Net;
using System.Text;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using DhcbTools.Core;
using DhcbTools.Core.AutoNumbering;
using DhcbTools.Core.ModelCleanup;
using DhcbTools.Core.ParameterSync;

namespace DhcbTools.Revit.Bridge;

/// <summary>
/// HTTP Bridge cho phép agent AI (hoặc bất kỳ HTTP client nào) gửi lệnh vào Revit
/// đang chạy và nhận kết quả JSON trả về — không cần mở UI, không cần click chuột.
///
/// Luồng:
///   POST http://localhost:8765/execute  { "command": "AutoNumbering", "config": {...} }
///   → HttpListener (background thread) nhận request
///   → đưa vào CommandQueue, gọi ExternalEvent.Raise()
///   → Revit main thread chạy IExternalEventHandler.Execute()
///   → CommandResult → JSON → HTTP response
///
/// Khởi động: App.cs gọi DhcbHttpBridge.Start(uiApp) khi add-in load.
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

        if (req.HttpMethod != "POST" || req.Url?.AbsolutePath != "/execute")
        {
            WriteJson(res, 404, new { error = "Chỉ hỗ trợ POST /execute" });
            return;
        }

        string body;
        using (var reader = new System.IO.StreamReader(req.InputStream, req.ContentEncoding))
        {
            body = reader.ReadToEnd();
        }

        BridgeRequest? bridgeReq;
        try
        {
            bridgeReq = JsonConvert.DeserializeObject<BridgeRequest>(body);
        }
        catch (Exception ex)
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
        _handler.Enqueue(bridgeReq, tcs);
        _externalEvent.Raise();

        // Chờ kết quả (timeout 30s)
        CommandResult result;
        try
        {
            result = tcs.Task.Wait(30_000) ? tcs.Task.Result
                : CommandResult.Fail("Timeout: Revit không xử lý trong 30 giây.");
        }
        catch (Exception ex)
        {
            result = CommandResult.Fail($"Lỗi thực thi: {ex.Message}");
        }

        WriteJson(res, result.Success ? 200 : 500, new
        {
            success = result.Success,
            summary = result.Summary,
            affectedElementCount = result.AffectedElementCount,
            messages = result.Messages,
            errors = result.Errors,
        });
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
    private readonly System.Collections.Concurrent.ConcurrentQueue<(BridgeRequest Req, TaskCompletionSource<CommandResult> Tcs)> _queue = new();

    public void Enqueue(BridgeRequest req, TaskCompletionSource<CommandResult> tcs)
        => _queue.Enqueue((req, tcs));

    public string GetName() => "DHCB HTTP Bridge";

    public void Execute(UIApplication app)
    {
        while (_queue.TryDequeue(out var item))
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

                var result = DispatchCommand(doc, req);
                tcs.SetResult(result);
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
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

            _ => CommandResult.Fail($"Lệnh không xác định: \"{req.Command}\". " +
                 "Các lệnh hợp lệ: ParameterExport, ParameterImport, Cleanup, AutoNumbering."),
        };
    }

    private static T Deserialize<T>(string json)
    {
        var result = JsonConvert.DeserializeObject<T>(json);
        if (result is null)
        {
            throw new InvalidOperationException($"Không thể deserialize config thành {typeof(T).Name}.");
        }
        return result;
    }
}

// ──────────────────────────────────────────────────────────────
// DTO
// ──────────────────────────────────────────────────────────────

public sealed class BridgeRequest
{
    [JsonProperty("command")]
    public string Command { get; set; } = string.Empty;

    /// <summary>Config tự do — được deserialize theo từng lệnh.</summary>
    [JsonProperty("config")]
    public JObject? Config { get; set; }
}
