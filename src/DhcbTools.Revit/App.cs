using System.Reflection;
using Autodesk.Revit.UI;
using DhcbTools.Revit.Bridge;

namespace DhcbTools.Revit;

/// <summary>
/// Điểm vào của add-in: tạo Ribbon tab "DHCB Tools" và các nút lệnh nền tảng.
/// Đồng thời khởi động HTTP Bridge (port 8765) để agent AI có thể gửi lệnh trực tiếp.
/// </summary>
public sealed class App : IExternalApplication
{
    private const string TabName = "DHCB Tools";
    private DhcbHttpBridge? _bridge;

    public Result OnStartup(UIControlledApplication application)
    {
        application.CreateRibbonTab(TabName);

        var panel = application.CreateRibbonPanel(TabName, "Nền tảng");
        var assemblyPath = Assembly.GetExecutingAssembly().Location;

        panel.AddItem(new PushButtonData(
            "DhcbParameterExport",
            "Xuất tham số\nra CSV",
            assemblyPath,
            typeof(Commands.ParameterExportCommand).FullName)
        {
            ToolTip = "Xuất tham số của các phần tử theo category ra file CSV để chỉnh sửa bằng Excel.",
        });

        panel.AddItem(new PushButtonData(
            "DhcbParameterImport",
            "Nhập tham số\ntừ CSV",
            assemblyPath,
            typeof(Commands.ParameterImportCommand).FullName)
        {
            ToolTip = "Đọc lại file CSV đã chỉnh sửa và ghi giá trị tham số vào mô hình.",
        });

        panel.AddSeparator();

        panel.AddItem(new PushButtonData(
            "DhcbCleanupViews",
            "Dọn view/sheet\nthừa",
            assemblyPath,
            typeof(Commands.RemoveUnusedViewsCommand).FullName)
        {
            ToolTip = "Xoá view không đặt trên sheet và sheet rỗng. Mặc định chạy ở chế độ xem trước.",
        });

        panel.AddItem(new PushButtonData(
            "DhcbAutoNumbering",
            "Đánh số\ntự động",
            assemblyPath,
            typeof(Commands.AutoNumberingCommand).FullName)
        {
            ToolTip = "Đánh số hàng loạt một category (cửa, phòng, thiết bị...) theo vị trí hình học.",
        });

        // Khởi động HTTP Bridge — agent AI gửi lệnh qua http://localhost:8765/execute
        // UIApplication chỉ available sau ApplicationInitialized; Bridge sẽ lazy-get từ
        // DocumentManager khi ExternalEvent được raise lần đầu.
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
