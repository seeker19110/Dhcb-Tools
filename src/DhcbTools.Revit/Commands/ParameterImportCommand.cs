using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace DhcbTools.Revit.Commands;

/// <summary>Nhập CSV qua form chung: xem trước trọn vẹn rồi mới cho ghi.</summary>
[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public sealed class ParameterImportCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        => CommandRunner.Run(commandData, "ParameterImport");
}
