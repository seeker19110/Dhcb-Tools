using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using DhcbTools.Core.MEPF;

namespace DhcbTools.Revit.Commands;

[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public sealed class SleeveAutoCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        var doc = commandData.Application.ActiveUIDocument.Document;
        var td = new TaskDialog("DHCB - Sleeve tự động")
        {
            MainInstruction = "Chạy lệnh Sleeve tự động",
            MainContent = "Lệnh sẽ tìm giao cắt MEP × Tường/Sàn và đặt sleeve.\n" +
                         "Dùng HTTP Bridge để truyền tên family sleeve và cấu hình đầy đủ.\n\n" +
                         "Nhấn OK để xem trước (DryRun=true).",
            CommonButtons = TaskDialogCommonButtons.Ok | TaskDialogCommonButtons.Cancel,
        };
        if (td.Show() != TaskDialogResult.Ok) return Result.Cancelled;
        var config = new SleeveConfig
        {
            SleeveFamilyName = "M_Generic Model",  // placeholder — configure via HTTP Bridge
            DryRun = true,
        };
        var result = new SleeveCommand().Execute(doc, config);
        Feedback.Show("Sleeve tự động", result);
        return result.Success ? Result.Succeeded : Result.Failed;
    }
}

[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public sealed class ElevationTagAutoCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        var doc = commandData.Application.ActiveUIDocument.Document;
        var config = new ElevationTagConfig { DryRun = true };
        var result = new ElevationTagCommand().Execute(doc, config);
        Feedback.Show("Gán cao độ MEP", result);
        return result.Success ? Result.Succeeded : Result.Failed;
    }
}

[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public sealed class ConnectorCheckerAutoCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        var doc = commandData.Application.ActiveUIDocument.Document;
        var config = new ConnectorCheckerConfig { Create3dView = true };
        var result = new ConnectorCheckerCommand().Execute(doc, config);
        Feedback.Show("Kiểm tra connector hở", result);
        return result.Success ? Result.Succeeded : Result.Failed;
    }
}

[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public sealed class HangerAutoCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        var doc = commandData.Application.ActiveUIDocument.Document;
        var td = new TaskDialog("DHCB - Đặt hanger")
        {
            MainInstruction = "Đặt hanger dọc ống/duct/cable tray",
            MainContent = "Lệnh đặt hanger theo khoảng cách đều (mặc định 3000mm) dọc trục phần tử.\n" +
                          "Dùng HTTP Bridge để truyền tên family hanger, khoảng cách và category cụ thể.\n\n" +
                          "Nhấn OK để xem trước (DryRun=true).",
            CommonButtons = TaskDialogCommonButtons.Ok | TaskDialogCommonButtons.Cancel,
        };
        if (td.Show() != TaskDialogResult.Ok) return Result.Cancelled;

        var config = new HangerConfig
        {
            HangerFamilyName = "M_Generic Model",  // placeholder — cấu hình đầy đủ qua HTTP Bridge
            DryRun = true,
        };
        var result = new HangerCommand().Execute(doc, config);
        Feedback.Show("Đặt hanger", result);
        return result.Success ? Result.Succeeded : Result.Failed;
    }
}

[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public sealed class PipeSplitterAutoCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        var doc = commandData.Application.ActiveUIDocument.Document;
        var td = new TaskDialog("DHCB - Chia ống theo cây")
        {
            MainInstruction = "Chia ống/duct dài thành từng cây",
            MainContent = "Lệnh cắt các đoạn dài hơn 6000mm thành nhiều cây.\n" +
                          "Dùng HTTP Bridge để đổi chiều dài cây, category và family coupling.\n\n" +
                          "Nhấn OK để xem trước (DryRun=true).",
            CommonButtons = TaskDialogCommonButtons.Ok | TaskDialogCommonButtons.Cancel,
        };
        if (td.Show() != TaskDialogResult.Ok) return Result.Cancelled;

        var config = new PipeSplitterConfig { DryRun = true };
        var result = new PipeSplitterCommand().Execute(doc, config);
        Feedback.Show("Chia ống theo cây", result);
        return result.Success ? Result.Succeeded : Result.Failed;
    }
}
