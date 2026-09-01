using System.Reflection;
using Autodesk.Revit.UI;
using DhcbTools.Revit.Bridge;

namespace DhcbTools.Revit;

/// <summary>
/// Điểm vào của add-in: tạo Ribbon tab "DHCB Tools" với đầy đủ 4 panel chức năng.
/// Đồng thời khởi động HTTP Bridge (port 8765) để agent AI có thể gửi lệnh trực tiếp.
/// </summary>
public sealed class App : IExternalApplication
{
    private const string TabName = "DHCB Tools";
    private DhcbHttpBridge? _bridge;

    public Result OnStartup(UIControlledApplication application)
    {
        application.CreateRibbonTab(TabName);
        var assemblyPath = Assembly.GetExecutingAssembly().Location;

        // ── Panel 1: Nền tảng ─────────────────────────────────────
        var panelCore = application.CreateRibbonPanel(TabName, "Nền tảng");

        panelCore.AddItem(new PushButtonData(
            "DhcbParameterExport", "Xuất tham số\nra CSV", assemblyPath,
            typeof(Commands.ParameterExportCommand).FullName)
        { ToolTip = "Xuất tham số của các phần tử theo category ra file CSV." });

        panelCore.AddItem(new PushButtonData(
            "DhcbParameterImport", "Nhập tham số\ntừ CSV", assemblyPath,
            typeof(Commands.ParameterImportCommand).FullName)
        { ToolTip = "Đọc lại file CSV đã chỉnh và ghi giá trị tham số vào mô hình." });

        panelCore.AddSeparator();

        panelCore.AddItem(new PushButtonData(
            "DhcbCleanupViews", "Dọn view/sheet\nthừa", assemblyPath,
            typeof(Commands.RemoveUnusedViewsCommand).FullName)
        { ToolTip = "Xoá view không đặt trên sheet và sheet rỗng." });

        panelCore.AddItem(new PushButtonData(
            "DhcbAutoNumbering", "Đánh số\ntự động", assemblyPath,
            typeof(Commands.AutoNumberingCommand).FullName)
        { ToolTip = "Đánh số hàng loạt một category theo vị trí hình học." });

        // ── Panel 2: Xuất & Báo cáo (Phase 1) ────────────────────
        var panelExport = application.CreateRibbonPanel(TabName, "Xuất & Báo cáo");

        panelExport.AddItem(new PushButtonData(
            "DhcbBatchExport", "Xuất PDF/DWG\nhàng loạt", assemblyPath,
            typeof(Commands.BatchExportCommand).FullName)
        { ToolTip = "Xuất tất cả sheet ra PDF + DWG vào thư mục chọn." });

        panelExport.AddItem(new PushButtonData(
            "DhcbHealthReport", "Health\nReport", assemblyPath,
            typeof(Commands.HealthReportCommand).FullName)
        { ToolTip = "Tạo báo cáo HTML về trạng thái mô hình: warning, view thừa, open connectors, in-place family." });

        // ── Panel 3: Khởi tạo dự án (Phase 2) ────────────────────
        var panelInit = application.CreateRibbonPanel(TabName, "Khởi tạo dự án");

        panelInit.AddItem(new PushButtonData(
            "DhcbProjectInit", "Khởi tạo\ndự án", assemblyPath,
            typeof(Commands.ProjectInitCommand).FullName)
        { ToolTip = "Tạo Level, View Plan và thông tin dự án từ cấu hình JSON." });

        // ── Panel 4: MEPF (Phase 3) ───────────────────────────────
        var panelMepf = application.CreateRibbonPanel(TabName, "MEPF");

        panelMepf.AddItem(new PushButtonData(
            "DhcbSleeve", "Sleeve\ntự động", assemblyPath,
            typeof(Commands.SleeveAutoCommand).FullName)
        { ToolTip = "Đặt sleeve/opening tại giao cắt MEP × Tường/Sàn tự động." });

        panelMepf.AddItem(new PushButtonData(
            "DhcbElevTag", "Gán cao\nđộ MEP", assemblyPath,
            typeof(Commands.ElevationTagAutoCommand).FullName)
        { ToolTip = "Điền cao độ đáy/đỉnh/tim vào tham số cho toàn bộ MEP." });

        panelMepf.AddItem(new PushButtonData(
            "DhcbConnectorCheck", "Kiểm tra\nConnector hở", assemblyPath,
            typeof(Commands.ConnectorCheckerAutoCommand).FullName)
        { ToolTip = "Tìm và liệt kê connector MEP chưa kết nối. Tạo 3D view khoanh vùng." });

        panelMepf.AddItem(new PushButtonData(
            "DhcbHanger", "Đặt\nhanger", assemblyPath,
            typeof(Commands.HangerAutoCommand).FullName)
        { ToolTip = "Đặt hanger theo khoảng cách đều dọc ống/duct/cable tray." });

        panelMepf.AddItem(new PushButtonData(
            "DhcbPipeSplit", "Chia ống\ntheo cây", assemblyPath,
            typeof(Commands.PipeSplitterAutoCommand).FullName)
        { ToolTip = "Cắt các đoạn ống/duct dài hơn chiều dài cây tiêu chuẩn (mặc định 6m)." });

        // ── HTTP Bridge ───────────────────────────────────────────
        _bridge = new DhcbHttpBridge();
        application.ControlledApplication.ApplicationInitialized += (_, _) =>
        {
            _bridge.Start();
        };

        return Result.Succeeded;
    }

    public Result OnShutdown(UIControlledApplication application)
    {
        _bridge?.Stop();
        _bridge?.Dispose();
        return Result.Succeeded;
    }
}
