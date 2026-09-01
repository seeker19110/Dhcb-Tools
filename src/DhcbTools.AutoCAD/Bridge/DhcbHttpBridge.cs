using System.Reflection;
using Autodesk.AutoCAD.ApplicationServices.Core;
using DhcbTools.Core.AutoCAD;
using DhcbTools.Core.AutoCAD.Query;
using DhcbTools.Shared.Logic.Ai;
using Newtonsoft.Json;

namespace DhcbTools.AutoCAD.Bridge;

/// <summary>
/// HTTP Bridge AutoCAD (port 8766) — cùng giao thức, cùng <see cref="HttpBridgeServer"/> với Revit. Phần riêng: marshal
/// về luồng UI bằng <c>ExecuteInCommandContextAsync</c> và dispatch qua <see cref="AcadCommandTable"/>.
/// </summary>
public sealed class DhcbHttpBridge : IDisposable
{
    public const int Port = 8766;

    private readonly HttpBridgeServer _server;
    private bool _disposed;

    public DhcbHttpBridge()
    {
        _server = new HttpBridgeServer(Port, "AutoCAD", Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0")
        {
            ExecuteAsync = item =>
            {
                Application.DocumentManager.ExecuteInCommandContextAsync(_ =>
                {
                    if (!item.TryClaim())
                    {
                        return Task.CompletedTask; // client đã timeout (lỗi #7)
                    }

                    var doc = Application.DocumentManager.MdiActiveDocument;
                    if (doc is null)
                    {
                        item.Completion.TrySetResult(CommandResult.Fail("Không có drawing nào đang mở trong AutoCAD."));
                        return Task.CompletedTask;
                    }

                    try
                    {
                        using var lock_ = doc.LockDocument();
                        item.Completion.TrySetResult(AcadCommandTable.Dispatch(doc.Database, item.Request.Command, item.Request.ConfigJson));
                    }
                    catch (Exception ex)
                    {
                        item.Completion.TrySetException(ex);
                    }

                    return Task.CompletedTask;
                }, null);
                return Task.CompletedTask;
            },
            QueryAsync = item =>
            {
                Application.DocumentManager.ExecuteInCommandContextAsync(_ =>
                {
                    if (!item.TryClaim())
                    {
                        return Task.CompletedTask;
                    }

                    var doc = Application.DocumentManager.MdiActiveDocument;
                    if (doc is null)
                    {
                        item.Completion.TrySetResult(new { error = "Không có drawing nào đang mở trong AutoCAD." });
                        return Task.CompletedTask;
                    }

                    try
                    {
                        var request = new QueryRequest
                        {
                            Query = item.Request.Query,
                            Params = JsonConvert.DeserializeObject<AcadQueryParams>(item.Request.ParamsJson) ?? new AcadQueryParams(),
                        };
                        item.Completion.TrySetResult(AcadQueryHandler.Handle(doc.Database, request));
                    }
                    catch (Exception ex)
                    {
                        item.Completion.TrySetResult(new { error = ex.Message });
                    }

                    return Task.CompletedTask;
                }, null);
                return Task.CompletedTask;
            },
            Chat = text => CommandIntentParser.Parse(text, CommandCatalog.AutoCad).ToPayload(),
            ListTools = () => CommandCatalog.Describe(CommandCatalog.AutoCad),
        };
    }

    public void Start() => _server.Start();

    public void Stop() => _server.Stop();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _server.Dispose();
    }
}
