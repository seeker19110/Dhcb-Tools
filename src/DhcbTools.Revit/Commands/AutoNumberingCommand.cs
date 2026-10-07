using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace DhcbTools.Revit.Commands;

/// <summary>Đánh số qua form chung: xem trước, duyệt kết quả, lưu cấu hình.</summary>
[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public sealed class AutoNumberingCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        => CommandRunner.Run(commandData, "AutoNumbering");
}
