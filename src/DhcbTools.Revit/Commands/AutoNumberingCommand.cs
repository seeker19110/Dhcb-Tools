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
        var document = CommandRunner.RequireDocument(commandData, "Đánh số tự động");
        if (document is null)
        {
            return Result.Cancelled;
        }

        var window = new UI.AutoNumberingWindow();
        // Cùng lý do với CommandRunner: không có chủ thì form rơi xuống dưới khung Revit, trông như Revit treo.
        new System.Windows.Interop.WindowInteropHelper(window).Owner = commandData.Application.MainWindowHandle;
        if (window.ShowDialog() != true || window.Config is null)
        {
            return Result.Cancelled;
        }

        // Đường Ribbon duy nhất ghi mô hình mà không qua bước xem trước: bỏ tick "xem trước" là ghi thẳng.
        // Hỏi một lần trước khi ghi, như mọi lệnh khác hỏi "Chạy thật?".
        if (!window.Config.DryRun)
        {
            var confirm = TaskDialog.Show("Đánh số tự động",
                $"Sẽ ghi số vào tham số \"{window.Config.ParameterName}\" của mọi phần tử \"{window.Config.Category}\" — không xem trước. Ghi vào mô hình?",
                TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No, TaskDialogResult.No);
            if (confirm != TaskDialogResult.Yes)
            {
                return Result.Cancelled;
            }
        }

        var result = new Core.AutoNumbering.AutoNumberingCommand().Execute(document, window.Config);
        Feedback.Show("Đánh số tự động", result);
        return result.Success ? Result.Succeeded : Result.Failed;
#endif
    }
}
