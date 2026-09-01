using Autodesk.Revit.UI;
using DhcbTools.Core;

namespace DhcbTools.Revit.Commands;

/// <summary>Hiển thị <see cref="CommandResult"/> của Core bằng TaskDialog — dùng chung cho mọi lệnh vỏ desktop.</summary>
internal static class Feedback
{
    public static void Show(string title, CommandResult result)
    {
        var dialog = new TaskDialog(title)
        {
            MainInstruction = result.Summary,
            MainIcon = result.Success ? TaskDialogIcon.TaskDialogIconInformation : TaskDialogIcon.TaskDialogIconError,
        };

        if (result.Messages.Count > 0)
        {
            dialog.ExpandedContent = string.Join(Environment.NewLine, result.Messages.Take(200));
        }

        if (result.Errors.Count > 0)
        {
            dialog.MainContent = string.Join(Environment.NewLine, result.Errors);
        }

        dialog.Show();
    }
}
