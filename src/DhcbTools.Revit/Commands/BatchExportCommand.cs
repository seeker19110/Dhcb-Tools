using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace DhcbTools.Revit.Commands;

/// <summary>
/// Vỏ desktop cho <see cref="Core.Export.BatchExportCommand"/>. Đi qua <see cref="CommandRunner"/> như
/// mọi nút khác: form động cho chọn thư mục, định dạng, lọc sheet, mẫu tên file; luôn xem trước
/// (liệt kê sheet) rồi mới xuất thật. Trước đây nút này cố định PDF + DWG vào Documents và xuất ngay,
/// không có bước xem trước — trái nguyên tắc DryRun của cả bộ.
/// </summary>
[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public sealed class BatchExportCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        => CommandRunner.Run(commandData, "BatchExport");
}
