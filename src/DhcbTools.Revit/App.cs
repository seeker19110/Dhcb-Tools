using System.Reflection;
using Autodesk.Revit.UI;

namespace DhcbTools.Revit;

/// <summary>
/// Điểm vào của add-in: tạo Ribbon tab "DHCB Tools" và các nút lệnh nền tảng.
/// Theo kiến trúc Core/vỏ: lớp này chỉ dựng UI, mọi xử lý thật nằm ở DhcbTools.Core.
/// </summary>
public sealed class App : IExternalApplication
{
    private const string TabName = "DHCB Tools";

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

        return Result.Succeeded;
    }

    public Result OnShutdown(UIControlledApplication application) => Result.Succeeded;
}
