using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace DhcbTools.Revit.Commands;

/// <summary>
/// Vỏ desktop cho <see cref="Core.ParameterSync.ParameterExportCommand"/>: form động cho chọn
/// category (combo từ mô hình), tham số và nơi lưu CSV. Trước đây vỏ này cố định 4 category và 3 tham
/// số — kỹ sư không đổi được gì ngoài đường dẫn.
/// </summary>
[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public sealed class ParameterExportCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        => CommandRunner.Run(commandData, "ParameterExport");
}
