using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using DhcbTools.Revit.UI;

namespace DhcbTools.Revit.Commands;

/// <summary>Vỏ desktop cho <see cref="AutoNumberingCommand"/> (Core): mở cửa sổ WPF nhập cấu hình.</summary>
[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public sealed class AutoNumberingCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        var document = commandData.Application.ActiveUIDocument.Document;

        var window = new AutoNumberingWindow();
        if (window.ShowDialog() != true || window.Config is null)
        {
            return Result.Cancelled;
        }

        var command = new Core.AutoNumbering.AutoNumberingCommand();
        var result = command.Execute(document, window.Config);

        Feedback.Show("Đánh số tự động", result);
        return result.Success ? Result.Succeeded : Result.Failed;
    }
}
