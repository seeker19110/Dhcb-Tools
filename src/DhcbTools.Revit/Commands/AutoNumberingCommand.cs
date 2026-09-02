using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace DhcbTools.Revit.Commands;

/// <summary>Vỏ desktop cho <see cref="Core.AutoNumbering.AutoNumberingCommand"/>: cửa sổ WPF; khi build không WPF thì dùng config JSON.</summary>
[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public sealed class AutoNumberingCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
#if DHCB_SKIP_WPF
        return CommandRunner.Run(commandData, "AutoNumbering");
#else
        var document = commandData.Application.ActiveUIDocument.Document;

        var window = new UI.AutoNumberingWindow();
        if (window.ShowDialog() != true || window.Config is null)
        {
            return Result.Cancelled;
        }

        var result = new Core.AutoNumbering.AutoNumberingCommand().Execute(document, window.Config);
        Feedback.Show("Đánh số tự động", result);
        return result.Success ? Result.Succeeded : Result.Failed;
#endif
    }
}
