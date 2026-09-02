using System.Collections.Concurrent;
using System.Reflection;
using Autodesk.Revit.UI;
using DhcbTools.Core;
using DhcbTools.Core.Query;
using DhcbTools.Shared.Hosting;
using DhcbTools.Shared.Logic.Ai;
using Newtonsoft.Json;

namespace DhcbTools.Revit.Bridge;

/// <summary>
/// HTTP Bridge Revit (port 8765) — phần HTTP/xác thực/timeout nằm trong <see cref="HttpBridgeServer"/> dùng chung với
/// AutoCAD (mục 0.2). Phần riêng của Revit ở đây chỉ còn: marshal về main thread qua <see cref="ExternalEvent"/>
/// và dispatch qua <see cref="RevitCommandTable"/>.
///
///   GET  /health · GET /tools · POST /execute · POST /query · POST /chat (đề xuất lệnh từ tiếng Việt, không chạy)
/// Token: %APPDATA%\DHCB\bridge-token.txt (mục 0.1). Lệnh client bỏ đi vì timeout không được chạy (mục 0.5).
/// </summary>
public sealed class DhcbHttpBridge : IDisposable
{
    public const int Port = 8765;

    private readonly HttpBridgeServer _server;
    private readonly ExternalEvent _externalEvent;
    private readonly BridgeEventHandler _handler;
    private bool _disposed;

    public DhcbHttpBridge()
    {
        _handler = new BridgeEventHandler();
        _externalEvent = ExternalEvent.Create(_handler);
        _server = new HttpBridgeServer(Port, "Revit", DhcbVersion.Of(Assembly.GetExecutingAssembly()))
        {
            ExecuteAsync = item =>
            {
                _handler.Commands.Enqueue(item);
                _externalEvent.Raise();
                return Task.CompletedTask;
            },
            QueryAsync = item =>
            {
                _handler.Queries.Enqueue(item);
                _externalEvent.Raise();
                return Task.CompletedTask;
            },
            Chat = text => CommandIntentParser.Parse(text, CommandCatalog.Revit).ToPayload(),
            ListTools = () => CommandCatalog.Describe(CommandCatalog.Revit),
            Log = line => DhcbLog.Write("Revit", line),
        };
    }

    public string? TokenPath => Shared.Hosting.BridgeTokenStore.DefaultPath;

    public void Start() => _server.Start();

    public void Stop() => _server.Stop();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _server.Dispose();
        _externalEvent.Dispose();
    }
}

/// <summary>Chạy trên main thread Revit. Kiểm tra <c>TryClaim()</c> trước khi chạy để bỏ việc client đã timeout.</summary>
internal sealed class BridgeEventHandler : IExternalEventHandler
{
    public ConcurrentQueue<BridgeWorkItem<BridgeRequest, CommandResult>> Commands { get; } = new();

    public ConcurrentQueue<BridgeWorkItem<BridgeQuery, object>> Queries { get; } = new();

    public string GetName() => "DHCB HTTP Bridge";

    public void Execute(UIApplication app)
    {
        while (Commands.TryDequeue(out var item))
        {
            if (!item.TryClaim())
            {
                // Lỗi #7: client đã bỏ đi — không mở transaction.
                continue;
            }

            try
            {
                var doc = app.ActiveUIDocument?.Document;
                if (doc is null)
                {
                    item.Completion.TrySetResult(CommandResult.Fail("Không có document nào đang mở trong Revit."));
                    continue;
                }

                item.Completion.TrySetResult(DispatchWithFailurePolicy(doc, item.Request.Command, item.Request.ConfigJson));
            }
            catch (Exception ex)
            {
                item.Completion.TrySetException(ex);
            }
        }

        while (Queries.TryDequeue(out var item))
        {
            if (!item.TryClaim())
            {
                continue;
            }

            try
            {
                var uiDoc = app.ActiveUIDocument;
                if (uiDoc?.Document is null)
                {
                    item.Completion.TrySetResult(new { error = "Không có document nào đang mở trong Revit." });
                    continue;
                }

                var request = new QueryRequest
                {
                    Query = item.Request.Query,
                    Params = JsonConvert.DeserializeObject<QueryParams>(item.Request.ParamsJson) ?? new QueryParams(),
                };

                // Qua UiQueryHandler: nó xử lý selection/show_elements/active_view (cần UIDocument)
                // rồi chuyển phần còn lại xuống RevitQueryHandler.
                item.Completion.TrySetResult(UiQueryHandler.Handle(uiDoc, request));
            }
            catch (Exception ex)
            {
                item.Completion.TrySetResult(new { error = ex.Message });
            }
        }
    }

    /// <summary>
    /// Không có kỹ sư ngồi máy để bấm hộp thoại cảnh báo qua Bridge, nên bỏ Warning (giữ lại mô tả) và để
    /// Error rollback như bình thường — xem <see cref="FailurePolicy.SuppressWarnings"/>.
    /// </summary>
    private static CommandResult DispatchWithFailurePolicy(Autodesk.Revit.DB.Document doc, string command, string configJson)
    {
        using var _ = CoreContext.Use(FailurePolicy.SuppressWarnings);
        CoreContext.SuppressedWarnings.Clear();
        var result = RevitCommandTable.Dispatch(doc, command, configJson);
        foreach (var warning in CoreContext.SuppressedWarnings)
        {
            result.Messages.Add("[Cảnh báo Revit bỏ qua] " + warning);
        }

        return result;
    }
}
