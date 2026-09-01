using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using DhcbTools.Core.ParameterSync;
using Microsoft.Win32;

namespace DhcbTools.Revit.Commands;

/// <summary>Vỏ desktop cho <see cref="ParameterImportCommand"/> (Core).</summary>
[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public sealed class ParameterImportCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        var document = commandData.Application.ActiveUIDocument.Document;

        var dialog = new OpenFileDialog
        {
            Title = "DHCB Tools - Chọn file CSV đã chỉnh sửa",
            Filter = "CSV (*.csv)|*.csv",
        };
        if (dialog.ShowDialog() != true)
        {
            return Result.Cancelled;
        }

        var confirm = new TaskDialog("Nhập tham số từ CSV")
        {
            MainInstruction = "Xem trước thay đổi trước khi ghi vào mô hình?",
            MainContent = "Chọn \"Xem trước\" để chỉ liệt kê thay đổi, không ghi vào mô hình.",
            CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No | TaskDialogCommonButtons.Cancel,
        };
        var answer = confirm.Show();
        if (answer == TaskDialogResult.Cancel)
        {
            return Result.Cancelled;
        }

        var config = new ParameterImportConfig
        {
            InputPath = dialog.FileName,
            DryRun = answer == TaskDialogResult.Yes,
        };

        var command = new Core.ParameterSync.ParameterImportCommand();
        var result = command.Execute(document, config);

        Feedback.Show("Nhập tham số từ CSV", result);
        return result.Success ? Result.Succeeded : Result.Failed;
    }
}
