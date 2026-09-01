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

namespace DhcbTools.AutoCAD.Bridge;

/// <summary>
/// HTTP Bridge cho AutoCAD — cùng giao thức với Revit Bridge (port 8766).
///
/// AutoCAD marshal sang main thread qua
/// Application.DocumentManager.ExecuteInCommandContextAsync()
/// — đây là cách chính thức của Autodesk từ AutoCAD 2014+ (AcMgd managed).
///
/// POST http://localhost:8766/execute  { "command": "LayerExport", "config": {...} }
/// </summary>
public sealed class DhcbHttpBridge : IDisposable
{
    public const int Port = 8766;

    private readonly HttpListener _listener = new();
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

        // Marshal sang AutoCAD main thread
        var tcs = new TaskCompletionSource<CommandResult>();

        Application.DocumentManager.ExecuteInCommandContextAsync(async _ =>
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc is null)
            {
                tcs.SetResult(CommandResult.Fail("Không có drawing nào đang mở trong AutoCAD."));
                return;
            }

            try
            {
                var result = DispatchCommand(doc.Database, bridgeReq);
                tcs.SetResult(result);
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }

            await Task.CompletedTask;
        }, null);

        CommandResult cmdResult;
        try
        {
            cmdResult = tcs.Task.Wait(30_000) ? tcs.Task.Result
                : CommandResult.Fail("Timeout: AutoCAD không xử lý trong 30 giây.");
        }
        catch (Exception ex)
        {
            cmdResult = CommandResult.Fail($"Lỗi thực thi: {ex.Message}");
        }

        WriteJson(res, cmdResult.Success ? 200 : 500, new
        {
            success = cmdResult.Success,
            summary = cmdResult.Summary,
            affectedCount = cmdResult.AffectedCount,
            messages = cmdResult.Messages,
            errors = cmdResult.Errors,
        });
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
        {
            throw new InvalidOperationException($"Không thể deserialize config thành {typeof(T).Name}.");
        }
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

public sealed class BridgeRequest
{
    [JsonProperty("command")]
    public string Command { get; set; } = string.Empty;

    [JsonProperty("config")]
    public JObject? Config { get; set; }
}
