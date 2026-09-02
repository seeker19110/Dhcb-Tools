using System.Reflection;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.UI;
using DhcbTools.Core.Updaters;
using DhcbTools.Revit.Batch;
using DhcbTools.Revit.Bridge;
using DhcbTools.Shared.Hosting;

namespace DhcbTools.Revit;

/// <summary>
/// Điểm vào của add-in: dựng Ribbon tab "DHCB Tools" với 6 panel phủ toàn bộ lệnh trong
/// <see cref="Core.RevitCommandTable"/>, khởi động HTTP Bridge (port 8765), đăng ký
/// <see cref="ElevationUpdater"/> (chỉ khi settings.json bật) và nối hook batch chạy đêm.
/// </summary>
public sealed class App : IExternalApplication
{
    private const string TabName = "DHCB Tools";
    private const string Ns = "DhcbTools.Revit.Commands.";

    private DhcbHttpBridge? _bridge;
    private ElevationUpdater? _elevationUpdater;

    public Result OnStartup(UIControlledApplication application)
    {
        DhcbLog.Prune("Revit");
        DhcbLog.Write("Revit", $"Add-in khởi động — phiên bản {DhcbVersion.Of(Assembly.GetExecutingAssembly())}, "
                             + $"Revit {application.ControlledApplication.VersionNumber}.");

        application.CreateRibbonTab(TabName);
        var path = Assembly.GetExecutingAssembly().Location;

        // ── Panel 1: Nền tảng ─────────────────────────────────────
        var core = application.CreateRibbonPanel(TabName, "Nền tảng");
        Add(core, path, "DhcbParameterExport", "Xuất tham số\nra CSV", "ParameterExportCommand",
            "Xuất tham số của các phần tử theo category ra file CSV.");
        Add(core, path, "DhcbParameterImport", "Nhập tham số\ntừ CSV", "ParameterImportCommand",
            "Đọc lại file CSV đã chỉnh và ghi giá trị tham số vào mô hình.");
        core.AddSeparator();
        Add(core, path, "DhcbCleanupViews", "Dọn view/sheet\nthừa", "RemoveUnusedViewsCommand",
            "Xoá view không đặt trên sheet và sheet rỗng.");
        Add(core, path, "DhcbAutoNumbering", "Đánh số\ntự động", "AutoNumberingCommand",
            "Đánh số hàng loạt một category theo vị trí hình học.");

        // ── Panel 2: Xuất & Báo cáo ───────────────────────────────
        var export = application.CreateRibbonPanel(TabName, "Xuất & Báo cáo");
        Add(export, path, "DhcbBatchExport", "Xuất PDF/DWG\nhàng loạt", "BatchExportCommand",
            "Xuất tất cả sheet ra PDF + DWG vào thư mục chọn.");
        Add(export, path, "DhcbHealthReport", "Health\nReport", "HealthReportCommand",
            "Báo cáo HTML: warning, view thừa, connector hở, in-place family.");
        Group(export, path, "DhcbExportMore", "Xuất\nkhác", "Xuất warning và schedule ra file.",
            ("DhcbWarningsExport", "Xuất warning", "WarningsExportRibbonCommand",
                "Xuất toàn bộ warning của mô hình ra file để phân tích."),
            ("DhcbScheduleExport", "Xuất schedule", "ScheduleExportRibbonCommand",
                "Xuất các schedule ra CSV/Excel."));

        // ── Panel 3: Khởi tạo dự án ───────────────────────────────
        var init = application.CreateRibbonPanel(TabName, "Khởi tạo dự án");
        Add(init, path, "DhcbProjectInit", "Khởi tạo\ndự án", "ProjectInitCommand",
            "Tạo Level, View Plan và thông tin dự án từ cấu hình JSON.");
        Group(init, path, "DhcbInitParts", "Level &\ntrục", "Tạo riêng từng phần: level, trục, family.",
            ("DhcbLevelSetup", "Tạo Level", "LevelSetupRibbonCommand",
                "Tạo Level và view plan tương ứng từ danh sách trong config."),
            ("DhcbGridSetup", "Tạo trục", "GridSetupRibbonCommand",
                "Tạo lưới trục từ toạ độ khai báo trong config."),
            ("DhcbGridFromCsv", "Trục từ CSV", "GridFromCsvRibbonCommand",
                "Dựng trục từ CSV của Excel hoặc lệnh GridExtract bên AutoCAD."),
            ("DhcbFamilyLoader", "Nạp family", "FamilyLoaderRibbonCommand",
                "Nạp hàng loạt family từ danh sách đường dẫn."));
        Group(init, path, "DhcbInitTemplate", "Template &\nhồ sơ", "Tạo file từ template, chuyển standards, tạo sheet.",
            ("DhcbProjectFromTemplate", "File từ template", "ProjectFromTemplateRibbonCommand",
                "Tạo file dự án mới từ template chuẩn của công ty."),
            ("DhcbTransferStandards", "Chuyển standards", "TransferStandardsRibbonCommand",
                "Copy tiêu chuẩn (type, filter, view template) từ file nguồn."),
            ("DhcbSheetBatchCreate", "Tạo sheet hàng loạt", "SheetBatchCreateRibbonCommand",
                "Tạo sheet hàng loạt theo danh sách số hiệu và tên."));

        // ── Panel 4: MEPF ─────────────────────────────────────────
        var mepf = application.CreateRibbonPanel(TabName, "MEPF");
        Add(mepf, path, "DhcbSleeve", "Sleeve\ntự động", "SleeveAutoCommand",
            "Đặt sleeve/opening tại giao cắt MEP với tường/sàn tự động.");
        Add(mepf, path, "DhcbElevTag", "Gán cao\nđộ MEP", "ElevationTagAutoCommand",
            "Điền cao độ đáy/đỉnh/tim vào tham số cho toàn bộ MEP.");
        Add(mepf, path, "DhcbConnectorCheck", "Kiểm tra\nConnector hở", "ConnectorCheckerAutoCommand",
            "Tìm và liệt kê connector MEP chưa kết nối. Tạo 3D view khoanh vùng.");
        mepf.AddSeparator();
        Group(mepf, path, "DhcbRouting", "Đi\ntuyến", "Dựng tuyến ống/duct: theo line, theo phòng, hoặc tự tìm đường.",
            ("DhcbRouteFromLines", "Đi ống theo line (A)", "RouteFromLinesRibbonCommand",
                "Dựng ống/duct từ model line vẽ tay (routing mức A)."),
            ("DhcbDevicePlacement", "Rải thiết bị (B)", "DevicePlacementRibbonCommand",
                "Rải thiết bị theo phòng và mẫu bố trí (routing mức B)."),
            ("DhcbAutoRoute", "Đi ống tự động (C)", "AutoRouteRibbonCommand",
                "Tìm đường A* 3D giữa hai điểm, né chướng ngại (routing mức C)."));
        Group(mepf, path, "DhcbSizing", "Tiết\ndiện", "Đề xuất tiết diện ra CSV để duyệt, rồi áp vào mô hình.",
            ("DhcbSizingProposal", "Đề xuất tiết diện", "SizingProposalRibbonCommand",
                "Tính tiết diện ống/duct theo lưu lượng, xuất CSV để duyệt."),
            ("DhcbApplySizing", "Áp tiết diện", "ApplySizingRibbonCommand",
                "Đọc CSV đã duyệt và áp tiết diện vào mô hình."));
        Group(mepf, path, "DhcbPipeTools", "Ống &\ngiá đỡ", "Giá đỡ, chia ống, độ dốc, kick.",
            ("DhcbHangerAuto", "Giá đỡ tự động", "HangerAutoRibbonCommand",
                "Rải hanger dọc ống/ducting theo bước khai báo."),
            ("DhcbPipeSplitter", "Chia ống", "PipeSplitterRibbonCommand",
                "Cắt ống dài thành đoạn theo chiều dài thương mại."),
            ("DhcbSlopePipes", "Đặt độ dốc ống", "SlopePipesRibbonCommand",
                "Gán độ dốc cho tuyến ống thoát theo tiêu chuẩn."),
            ("DhcbPipeKick", "Kick ống", "PipeKickRibbonCommand",
                "Chèn đoạn kick (lệch trục) để né chướng ngại."));
        Group(mepf, path, "DhcbSystemTools", "Hệ\nthống", "Màu, tên hệ, đánh số theo dòng chảy, bóc khối lượng.",
            ("DhcbSystemColor", "Màu theo hệ", "SystemColorRibbonCommand",
                "Tô màu và tạo filter theo hệ thống MEP."),
            ("DhcbSystemName", "Đặt tên hệ", "SystemNameRibbonCommand",
                "Chuẩn hoá System Name theo quy tắc đặt tên."),
            ("DhcbFlowNumbering", "Đánh số theo dòng", "FlowNumberingRibbonCommand",
                "Đánh số thiết bị theo thứ tự dòng chảy trong hệ."),
            ("DhcbSystemBom", "Bóc khối lượng", "SystemBomRibbonCommand",
                "Bóc khối lượng theo hệ, xuất bảng spool/BOM."));

        // ── Panel 5: Hồ sơ & Style ────────────────────────────────
        var sheets = application.CreateRibbonPanel(TabName, "Hồ sơ & Style");
        Group(sheets, path, "DhcbSheetTools", "Sheet &\nrevision", "Đổi tên sheet, gán revision, copy viewport.",
            ("DhcbSheetRename", "Đổi tên sheet", "SheetRenameRibbonCommand",
                "Đổi tên/số hiệu sheet hàng loạt theo mẫu."),
            ("DhcbRevisionOnSheets", "Gán revision", "RevisionOnSheetsRibbonCommand",
                "Gán revision cho nhóm sheet được chọn."),
            ("DhcbViewportCopy", "Copy viewport", "ViewportCopyRibbonCommand",
                "Copy bố trí viewport từ sheet mẫu sang các sheet khác."));
        Add(sheets, path, "DhcbColorByParameter", "Tô màu theo\ntham số", "ColorByParameterRibbonCommand",
            "Tô màu phần tử theo giá trị một tham số (kiểu Colour Splasher).");
        Group(sheets, path, "DhcbStyleTools", "Dọn &\nkiểm kê", "Dọn style thừa, kiểm kê family.",
            ("DhcbStylePurge", "Dọn style", "StylePurgeRibbonCommand",
                "Xoá text style, dimension style, line pattern không dùng."),
            ("DhcbFamilyAudit", "Kiểm kê family", "FamilyAuditRibbonCommand",
                "Rà soát family: trùng lặp, nặng, in-place, không dùng."));

        // ── Panel 6: Kiểm tra & AI ────────────────────────────────
        var checks = application.CreateRibbonPanel(TabName, "Kiểm tra & AI");
        Add(checks, path, "DhcbParameterRuleCheck", "Kiểm tra\ntham số", "ParameterRuleCheckRibbonCommand",
            "Đối chiếu tham số với bộ quy tắc trong parameter-rules.json.");
        Add(checks, path, "DhcbClashDetection", "Kiểm tra\nva chạm", "ClashDetectionRibbonCommand",
            "Dò va chạm nội bộ, xuất HTML + 3D view, đọc clash-accepted.json.");
        checks.AddSeparator();
        Group(checks, path, "DhcbAiTools", "AI\noffline", "Lớp AI chạy offline: map layer CAD, đọc thuyết minh.",
            ("DhcbCadLayerMap", "Map layer CAD", "CadLayerMapRibbonCommand",
                "Đề xuất map layer bản vẽ CAD sang type Revit (AI offline)."),
            ("DhcbSpecToConfig", "Thuyết minh sang config", "SpecToConfigRibbonCommand",
                "Trích tham số kỹ thuật từ thuyết minh thành file config (AI offline)."));

        application.ControlledApplication.ApplicationInitialized += (sender, _) =>
        {
            // Phiên batch: chạy job rồi để runner đóng Revit — không dựng Bridge/updater cho phiên đó.
            if (sender is Application app && BatchStartupHook.RunIfRequested(app))
            {
                return;
            }

            try
            {
                _bridge = new DhcbHttpBridge();
                _bridge.Start();
                DhcbLog.Write("Revit", $"HTTP Bridge nghe ở 127.0.0.1:{DhcbHttpBridge.Port}.");
            }
            catch (Exception ex)
            {
                // Instance Revit thứ hai: cổng 8765 đã bị instance đầu giữ. Không ném ra khỏi event
                // handler (Revit nuốt hoặc crash) — báo rõ để người dùng biết Bridge đang trỏ instance nào.
                _bridge?.Dispose();
                _bridge = null;
                DhcbLog.Error("Revit", "khởi động HTTP Bridge", ex);
                TaskDialog.Show("DHCB Tools — HTTP Bridge", ex.Message);
            }

            RegisterElevationUpdater(application);
        };

        return Result.Succeeded;
    }

    public Result OnShutdown(UIControlledApplication application)
    {
        _elevationUpdater?.Unregister();
        _bridge?.Stop();
        _bridge?.Dispose();
        return Result.Succeeded;
    }

    /// <summary>Mục 4.1: mặc định TẮT, chỉ bật khi settings.json khai báo rõ.</summary>
    private void RegisterElevationUpdater(UIControlledApplication application)
    {
        var settings = UpdaterSettings.Load();
        if (!settings.IsEnabled(ElevationUpdater.Name))
        {
            return;
        }

        try
        {
            _elevationUpdater = new ElevationUpdater(application.ActiveAddInId, config: null, settings.MaxExecuteMs)
            {
                OnDisabled = reason => TaskDialog.Show("DHCB Tools", reason),
            };
            _elevationUpdater.Register();
        }
        catch (Exception ex)
        {
            _elevationUpdater = null;
            TaskDialog.Show("DHCB Tools", "Không đăng ký được ElevationUpdater: " + ex.Message);
        }
    }

    private static void Add(RibbonPanel panel, string path, string id, string text, string className, string tip)
        => panel.AddItem(new PushButtonData(id, text, path, Ns + className) { ToolTip = tip });

    /// <summary>Nhóm nhiều lệnh vào một nút xổ xuống — panel Revit không chứa nổi 16 nút phẳng.</summary>
    private static void Group(
        RibbonPanel panel, string path, string id, string text, string tip,
        params (string Id, string Text, string ClassName, string Tip)[] items)
    {
        var pulldown = (PulldownButton)panel.AddItem(new PulldownButtonData(id, text) { ToolTip = tip });
        foreach (var item in items)
        {
            pulldown.AddPushButton(new PushButtonData(item.Id, item.Text, path, Ns + item.ClassName) { ToolTip = item.Tip });
        }
    }
}
