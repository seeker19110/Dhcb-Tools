using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using DhcbTools.Core.ModelCleanup;

namespace DhcbTools.Revit.Commands;

/// <summary>
/// Vỏ desktop cho <see cref="RemoveUnusedViewsCommand"/> (Core). Luôn chạy DryRun trước để kỹ sư
/// xác nhận danh sách, sau đó mới hỏi có muốn xoá thật không — tránh xoá nhầm hàng loạt.
/// </summary>
[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public sealed class RemoveUnusedViewsCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        var document = commandData.Application.ActiveUIDocument.Document;
        var command = new Core.ModelCleanup.RemoveUnusedViewsCommand();

        var previewConfig = new CleanupConfig { DryRun = true };
        var preview = command.Execute(document, previewConfig);

        if (preview.AffectedElementCount == 0)
        {
            Feedback.Show("Dọn view/sheet thừa", preview);
            return Result.Succeeded;
        }

        var confirm = new TaskDialog("Dọn view/sheet thừa")
        {
            MainInstruction = preview.Summary,
            MainContent = string.Join(Environment.NewLine, preview.Messages.Take(30)) +
                          (preview.Messages.Count > 30 ? $"\n... và {preview.Messages.Count - 30} mục khác." : string.Empty),
            CommonButtons = TaskDialogCommonButtons.Ok | TaskDialogCommonButtons.Cancel,
            MainIcon = TaskDialogIcon.TaskDialogIconWarning,
        };
        confirm.MainContent += "\n\nBấm OK để xoá thật, Cancel để huỷ.";

        if (confirm.Show() != TaskDialogResult.Ok)
        {
            return Result.Cancelled;
        }

        var realConfig = new CleanupConfig { DryRun = false };
        var result = command.Execute(document, realConfig);

        Feedback.Show("Dọn view/sheet thừa", result);
        return result.Success ? Result.Succeeded : Result.Failed;
    }
}
