using System.Reflection;
using Autodesk.AutoCAD.ApplicationServices;
using DhcbTools.Core.AutoCAD;
using DhcbTools.Core.AutoCAD.Query;
using DhcbTools.Shared.Hosting;
using DhcbTools.Shared.Logic.Ai;
using Newtonsoft.Json;

namespace DhcbTools.AutoCAD.Bridge;

/// <summary>
/// HTTP Bridge AutoCAD (port 8766) — phần HTTP/xác thực/timeout nằm trong <see cref="HttpBridgeServer"/>
/// dùng chung với Revit (mục 0.2). Trước đây vỏ AutoCAD tự viết HTTP server riêng, và vì thế **không có
/// xác thực nào**: mọi tiến trình local đều POST /execute sửa được bản vẽ đang mở. Nay dùng chung server
/// nên có token, khoá khi dò token, bind 127.0.0.1, cùng với /tools và /chat vốn chỉ Revit mới có.
///
/// Phần riêng của AutoCAD ở đây chỉ còn: marshal về main thread qua
/// <c>Application.DocumentManager.ExecuteInCommandContextAsync()</c> và dispatch qua
/// <see cref="AcadCommandTable"/>.
///
///   GET /health · GET /tools · POST /execute · POST /query · POST /chat (đề xuất lệnh, KHÔNG chạy)
/// Token: %APPDATA%\DHCB\bridge-token.txt — cùng file với Bridge Revit.
/// </summary>
public sealed class DhcbHttpBridge : IDisposable
{
    public const int Port = 8766;

    private readonly HttpBridgeServer _server;
    private readonly BridgeCommitGuard _commits = new(BridgeCommitGuard.DefaultDirectory("autocad"));
    private bool _disposed;

    public DhcbHttpBridge()
    {
        _server = new HttpBridgeServer(Port, "AutoCAD", DhcbVersion.Of(Assembly.GetExecutingAssembly()))
        {
            ExecuteAsync = item => RunOnAutoCadThread(
                item,
                database =>
                {
                    var context = TrackDocument(database);
                    using var executionScope = item.Execution.Enter();
                    return _commits.Execute("autocad", item.Request, BridgeDocumentContext.IdFor(context),
                        () => BridgeDocumentContext.RevisionFor(context),
                        request => AcadCommandTable.Dispatch(database, request.Command, request.ConfigJson));
                },
                message => CommandResult.Fail(message)),

            // Giai đoạn 10.1: truy vấn cần Editor (selection, show_entities, active_layout) đi qua vỏ;
            // còn lại rơi xuống AcadQueryHandler thuần Database như cũ.
            QueryAsync = item => RunOnAutoCadDocument(
                item,
                document => BridgeDocumentContext.ValidateQuery(item.Request, BridgeDocumentContext.IdFor(TrackDocument(document.Database))) is { } error
                    ? (object)new { error } : AcadUiQueryHandler.Handle(document, ToQueryRequest(item.Request)),
                message => (object)new { error = message }),

            Chat = text => CommandIntentParser.Parse(text, CommandCatalog.AutoCad).ToPayload(),
            ListTools = () => CommandCatalog.Describe(CommandCatalog.AutoCad),
            Log = line => DhcbLog.Write("AutoCAD", line),
        };
    }

    public string? TokenPath => BridgeTokenStore.DefaultPath;

    /// <summary>Theo dõi revision cho cả Ribbon và Bridge, kể cả khi Bridge bị tắt.</summary>
    internal static object TrackDocument(Autodesk.AutoCAD.DatabaseServices.Database database) => AcadDocumentContexts.For(database);

    public void Start() => _server.Start();

    public void Stop() => _server.Stop();

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _server.Dispose();
    }

    /// <summary>
    /// Đưa việc vào command context của AutoCAD — bắt buộc, vì Database chỉ được đụng tới trên luồng
    /// chính. Kiểm tra <c>TryClaim()</c> trước khi chạy để không mở transaction cho request mà client
    /// đã bỏ vì timeout (mục 0.5).
    /// </summary>
    private static async Task RunOnAutoCadThread<TRequest, TResult>(
        BridgeWorkItem<TRequest, TResult> item,
        Func<Autodesk.AutoCAD.DatabaseServices.Database, TResult> work,
        Func<string, TResult> onError)
    {
        TResult result = default!;
        var hasResult = false;
        try
        {
            await Application.DocumentManager.ExecuteInCommandContextAsync(
            _ =>
            {
                if (!item.TryClaim())
                {
                    return Task.CompletedTask;
                }

                try
                {
                    var document = Application.DocumentManager.MdiActiveDocument;
                    result = document is null
                        ? onError("Không có drawing nào đang mở trong AutoCAD.")
                        : work(document.Database);
                    hasResult = true;
                }
                catch (Exception ex)
                {
                    result = onError($"Lỗi khi chạy trong AutoCAD: {ex.Message}");
                    hasResult = true;
                }

                return Task.CompletedTask;
            },
            null);
        }
        finally
        {
            // Reply only after AutoCAD has unwound its command context, so an immediate next
            // request cannot enter a nested command. Preserve a known Core result on cleanup faults.
            if (hasResult) item.Completion.TrySetResult(result);
        }
    }

    /// <summary>
    /// Như <see cref="RunOnAutoCadThread"/> nhưng đưa cả <see cref="Document"/> cho việc cần
    /// <c>Editor</c> (chọn, zoom, layout đang mở) — Core cố ý chỉ nhận <c>Database</c> để còn chạy
    /// được trong <c>accoreconsole</c>, nơi không có Editor nào cả.
    /// </summary>
    private static async Task RunOnAutoCadDocument<TRequest, TResult>(
        BridgeWorkItem<TRequest, TResult> item,
        Func<Document, TResult> work,
        Func<string, TResult> onError)
    {
        TResult result = default!;
        var hasResult = false;
        try
        {
            await Application.DocumentManager.ExecuteInCommandContextAsync(
            _ =>
            {
                if (!item.TryClaim())
                {
                    return Task.CompletedTask;
                }

                try
                {
                    var document = Application.DocumentManager.MdiActiveDocument;
                    result = document is null
                        ? onError("Không có drawing nào đang mở trong AutoCAD.")
                        : work(document);
                    hasResult = true;
                }
                catch (Exception ex)
                {
                    result = onError($"Lỗi khi chạy trong AutoCAD: {ex.Message}");
                    hasResult = true;
                }

                return Task.CompletedTask;
            },
            null);
        }
        finally
        {
            // Reply only after AutoCAD has unwound its command context, so an immediate next
            // request cannot enter a nested command. Preserve a known Core result on cleanup faults.
            if (hasResult) item.Completion.TrySetResult(result);
        }
    }

    /// <summary>Chuyển body dùng chung sang kiểu truy vấn của AutoCAD.</summary>
    private static QueryRequest ToQueryRequest(BridgeQuery query) => new()
    {
        Query = query.Query,
        Params = JsonConvert.DeserializeObject<AcadQueryParams>(query.ParamsJson) ?? new AcadQueryParams(),
    };
}
