using System.Reflection;
using Autodesk.Revit.UI;
using DhcbTools.Core.Updaters;
using DhcbTools.Revit.Bridge;
using DhcbTools.Shared.Logic.Batch;

namespace DhcbTools.Revit;

/// <summary>
/// Điểm vào add-in: Ribbon "DHCB Tools" (6 panel), HTTP Bridge (8765, có token), IUpdater cao độ (tắt mặc định,
/// bật trong %APPDATA%\DHCB\settings.json), và hook batch: nếu có %APPDATA%\DHCB\pending-job.json (do
/// DhcbTools.BatchRunner ghi) thì chạy job ngay khi Revit khởi động xong rồi thoát — đây là cách chạy đêm không người trực.
/// </summary>
public sealed class App : IExternalApplication
{
    private const string TabName = "DHCB Tools";
    private DhcbHttpBridge? _bridge;
    private ElevationUpdater? _updater;

    public static string PendingJobPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DHCB", "pending-job.json");

    public static string BatchDonePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DHCB", "batch-done.json");

    public Result OnStartup(UIControlledApplication application)
    {
        application.CreateRibbonTab(TabName);
        var asm = Assembly.GetExecutingAssembly().Location;

        var core = application.CreateRibbonPanel(TabName, "Nền tảng");
        Add(core, asm, "DhcbParameterExport", "Xuất tham số\nra CSV", typeof(Commands.ParameterExportCommand), "Xuất tham số của các phần tử theo category ra file CSV.");
        Add(core, asm, "DhcbParameterImport", "Nhập tham số\ntừ CSV", typeof(Commands.ParameterImportCommand), "Đọc lại CSV đã chỉnh và ghi giá trị tham số vào mô hình.");
        core.AddSeparator();
        Add(core, asm, "DhcbCleanupViews", "Dọn view/sheet\nthừa", typeof(Commands.RemoveUnusedViewsCommand), "Xoá view không đặt trên sheet và sheet rỗng.");
        Add(core, asm, "DhcbAutoNumbering", "Đánh số\ntự động", typeof(Commands.AutoNumberingCommand), "Đánh số hàng loạt một category theo vị trí hình học.");

        var export = application.CreateRibbonPanel(TabName, "Xuất & Kiểm tra");
        Add(export, asm, "DhcbBatchExport", "Xuất PDF/DWG\nhàng loạt", typeof(Commands.BatchExportCommand), "Xuất sheet ra PDF/DWG/IFC/NWC.");
        Add(export, asm, "DhcbHealthReport", "Health\nReport", typeof(Commands.HealthReportCommand), "Báo cáo HTML: warning, view thừa, connector hở, in-place family.");
        Add(export, asm, "DhcbRuleCheck", "Kiểm tra\ntham số", typeof(Commands.ParameterRuleCheckCommand), "Tham số thiếu / sai quy tắc đặt tên theo bộ quy tắc JSON → HTML.");
        Add(export, asm, "DhcbClash", "Va chạm\nnội bộ", typeof(Commands.ClashDetectionCommand), "Clash giữa hai nhóm category → HTML + 3D view; bỏ qua cặp đã chấp nhận.");

        var init = application.CreateRibbonPanel(TabName, "Dự án & Hồ sơ");
        Add(init, asm, "DhcbProjectInit", "Khởi tạo\ndự án", typeof(Commands.ProjectInitCommand), "Level, Grid, family, project info từ config JSON.");
        Add(init, asm, "DhcbFromTemplate", "File từ\ntemplate", typeof(Commands.ProjectFromTemplateCommand), "Tạo file mới từ template chuẩn, bật worksharing, tạo workset.");
        Add(init, asm, "DhcbTransfer", "Transfer\nstandards", typeof(Commands.TransferStandardsCommand), "Chuyển view template, filter, material… từ file chuẩn.");
        Add(init, asm, "DhcbGridCsv", "Trục/Level\ntừ CSV", typeof(Commands.GridFromCsvCommand), "Trục từ CSV (Excel hoặc trích từ CAD bằng DHCB_GRID_EXTRACT), level từ CSV.");
        Add(init, asm, "DhcbSheets", "Tạo sheet\nhàng loạt", typeof(Commands.SheetBatchCreateCommand), "Sheet + đặt view từ bảng CSV.");

        var docs = application.CreateRibbonPanel(TabName, "Hồ sơ & Style");
        Add(docs, asm, "DhcbSheetRename", "Đổi tên\nsheet/view", typeof(Commands.SheetRenameCommand), "Đổi số/tên sheet hoặc view theo mẫu token + regex, chống trùng (pyRevit Sheets).");
        Add(docs, asm, "DhcbRevision", "Revision\nlên sheet", typeof(Commands.RevisionOnSheetsCommand), "Gán/bỏ một revision trên nhiều sheet.");
        Add(docs, asm, "DhcbStylePurge", "Purge\nstyle", typeof(Commands.StylePurgeCommand), "Xoá view template, filter, pattern, text/dim type không được tham chiếu (Ideate StyleManager).");
        Add(docs, asm, "DhcbColorBy", "Tô màu\ntheo tham số", typeof(Commands.ColorByParameterCommand), "Tô màu phần tử trong view theo giá trị tham số + chú giải CSV (Colour Splasher).");
        Add(docs, asm, "DhcbFamilyAudit", "Kiểm kê\nfamily", typeof(Commands.FamilyAuditCommand), "Family/type: instance, in-place, không dùng → CSV; đổi tên theo mẫu (FamilyReviser).");
        Add(docs, asm, "DhcbWarnings", "Warning\n→ CSV", typeof(Commands.WarningsExportCommand), "Xuất warning kèm ElementId/category để lọc trong Excel (Ideate Explorer).");
        docs.AddSeparator();
        Add(docs, asm, "DhcbScheduleExport", "Schedule\n→ CSV", typeof(Commands.ScheduleExportCommand), "Xuất schedule ra CSV đúng cột/hàng đang hiển thị (SheetLink).");
        Add(docs, asm, "DhcbViewportCopy", "Copy\nlegend/schedule", typeof(Commands.ViewportCopyCommand), "Copy legend/schedule từ một sheet sang nhiều sheet, cùng vị trí (pyRevit).");

        var mepf = application.CreateRibbonPanel(TabName, "MEPF");
        Add(mepf, asm, "DhcbSleeve", "Sleeve\ntự động", typeof(Commands.SleeveAutoCommand), "Sleeve/opening tại giao cắt MEP × tường/sàn.");
        Add(mepf, asm, "DhcbElevTag", "Gán cao\nđộ MEP", typeof(Commands.ElevationTagAutoCommand), "Cao độ đáy/đỉnh/tim vào tham số.");
        Add(mepf, asm, "DhcbHanger", "Hanger\ntự động", typeof(Commands.HangerAutoCommand), "Đặt hanger theo khoảng cách chuẩn dọc ống/duct/tray.");
        Add(mepf, asm, "DhcbPipeSplit", "Chia\nống", typeof(Commands.PipeSplitterCommand), "Chia ống/duct theo chiều dài cây (3 m / 6 m).");
        Add(mepf, asm, "DhcbConnectorCheck", "Connector\nhở", typeof(Commands.ConnectorCheckerAutoCommand), "Liệt kê connector chưa nối, tạo 3D view khoanh vùng.");
        mepf.AddSeparator();
        Add(mepf, asm, "DhcbRouteA", "Routing\ntheo line", typeof(Commands.RouteFromLinesCommand), "Mức A: dựng duct/pipe/tray + fitting từ model line vẽ tay.");
        Add(mepf, asm, "DhcbRouteB", "Rải thiết bị\ntheo phòng", typeof(Commands.DevicePlacementCommand), "Mức B: sprinkler/miệng gió theo lưới trong phòng, kiểm tra phủ.");
        Add(mepf, asm, "DhcbSizing", "Đề xuất\nsizing", typeof(Commands.SizingProposalCommand), "Kích thước duct/pipe theo lưu lượng → CSV để duyệt.");
        Add(mepf, asm, "DhcbApplySizing", "Áp\nsizing", typeof(Commands.ApplySizingCommand), "Áp lại CSV sizing đã duyệt.");
        Add(mepf, asm, "DhcbSysColor", "Màu\ntheo hệ", typeof(Commands.SystemColorCommand), "Filter + màu theo hệ trong view template.");
        Add(mepf, asm, "DhcbSysName", "Tên\nhệ", typeof(Commands.SystemNameCommand), "System Name theo quy tắc {Discipline}-{Abbr}-{Zone}-{N}.");
        Add(mepf, asm, "DhcbFlowNum", "Đánh số\ntheo tuyến", typeof(Commands.FlowNumberingCommand), "Đánh số theo thứ tự dòng chảy từ phần tử đang chọn.");
        mepf.AddSeparator();
        Add(mepf, asm, "DhcbSlope", "Ống\ndốc", typeof(Commands.SlopePipesCommand), "Đặt/kiểm tra dốc ống thoát nước theo % hoặc bảng tối thiểu theo DN (Naviate).");
        Add(mepf, asm, "DhcbKick", "Kick\nống", typeof(Commands.PipeKickCommand), "Kick/jog ống đang chọn bằng hai cút 45°/90°.");
        Add(mepf, asm, "DhcbBom", "BOM\ntheo hệ", typeof(Commands.SystemBomCommand), "Khối lượng ống/fitting theo hệ và spool → CSV (Victaulic).");
        Add(mepf, asm, "DhcbRouteC", "Tìm tuyến\ntự động", typeof(Commands.AutoRouteCommand), "Mức C: A* né chướng ngại giữa hai điểm → model line → dựng duct/pipe (eVolve).");

        var ai = application.CreateRibbonPanel(TabName, "AI offline & Batch");
#if !DHCB_SKIP_WPF
        Add(ai, asm, "DhcbAiChat", "Ra lệnh\ntiếng Việt", typeof(UI.AiChatCommand), "Nói việc cần làm bằng tiếng Việt → đề xuất lệnh + config, xem trước rồi chạy. Không cần internet.");
#endif
        Add(ai, asm, "DhcbLayerMap", "Map layer\nCAD→Type", typeof(Commands.CadLayerMapCommand), "Gợi ý map layer CAD → Revit type (heuristic offline, tuỳ chọn model local Ollama).");
        Add(ai, asm, "DhcbSpec", "Thuyết minh\n→ config", typeof(Commands.SpecToConfigCommand), "Trích tầng/cao độ/hệ/tiêu chuẩn từ file thuyết minh → config khởi tạo dự án.");
        Add(ai, asm, "DhcbBatch", "Chạy job\nbatch", typeof(Commands.RunBatchJobCommand), "Chạy job JSON (nhiều file × nhiều lệnh) ngay trong phiên này, ra log + báo cáo HTML.");

        _bridge = new DhcbHttpBridge();
        application.ControlledApplication.ApplicationInitialized += (sender, _) =>
        {
            try { _bridge.Start(); }
            catch (Exception ex) { TaskDialog.Show("DHCB Tools", "Không khởi động được HTTP Bridge: " + ex.Message); }

            RegisterUpdater(application);
            RunPendingJob(sender as Autodesk.Revit.ApplicationServices.Application);
        };

        return Result.Succeeded;
    }

    private void RegisterUpdater(UIControlledApplication application)
    {
        try
        {
            var settings = UpdaterSettings.Load();
            if (!settings.IsEnabled(ElevationUpdater.Name))
            {
                return;
            }

            _updater = new ElevationUpdater(application.ActiveAddInId, maxExecuteMs: settings.MaxExecuteMs)
            {
                OnDisabled = msg => TaskDialog.Show("DHCB Tools", msg),
            };
            _updater.Register();
        }
        catch (Exception ex)
        {
            TaskDialog.Show("DHCB Tools", "Không đăng ký được ElevationUpdater: " + ex.Message);
        }
    }

    /// <summary>Batch không người trực: BatchRunner ghi pending-job.json rồi mở Revit; add-in chạy job, ghi batch-done.json và thoát.</summary>
    private static void RunPendingJob(Autodesk.Revit.ApplicationServices.Application? app)
    {
        if (app is null || !File.Exists(PendingJobPath))
        {
            return;
        }

        var exitCode = 2;
        try
        {
            var pending = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(PendingJobPath));
            var jobPath = (string?)pending["jobPath"] ?? string.Empty;
            var runLog = (string?)pending["runLogPath"] ?? Path.Combine(Path.GetDirectoryName(PendingJobPath)!, "run.jsonl");
            var maxMinutes = (int?)pending["maxMinutes"] ?? 480;
            var dryRun = (bool?)pending["dryRun"] ?? false;

            var job = BatchJob.Load(jobPath);
            var runner = new Core.Batch.BatchJobRunner(app);
            exitCode = runner.Run(job, runLog, maxMinutes, dryRun);
        }
        catch (Exception ex)
        {
            try { File.WriteAllText(Path.Combine(Path.GetDirectoryName(PendingJobPath)!, "batch-error.txt"), ex.ToString()); } catch { /* ignore */ }
        }
        finally
        {
            try { File.Delete(PendingJobPath); } catch { /* ignore */ }
            try { File.WriteAllText(BatchDonePath, "{\"exitCode\":" + exitCode + ",\"time\":\"" + DateTime.Now.ToString("o") + "\"}"); } catch { /* ignore */ }
            // Thoát Revit để Task Scheduler thu hồi license/máy. Không có API "Exit" cho add-in; kết thúc tiến trình là cách ổn định nhất.
            Environment.Exit(exitCode);
        }
    }

    private static void Add(RibbonPanel panel, string asm, string name, string text, Type command, string tooltip)
    {
        panel.AddItem(new PushButtonData(name, text, asm, command.FullName) { ToolTip = tooltip });
    }

    public Result OnShutdown(UIControlledApplication application)
    {
        _updater?.Unregister();
        _bridge?.Stop();
        _bridge?.Dispose();
        return Result.Succeeded;
    }
}
