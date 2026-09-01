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
