using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace DhcbTools.Revit.Commands;

/// <summary>Dọn view/sheet qua form chung, xem trước đầy đủ trước khi xoá.</summary>
[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public sealed class RemoveUnusedViewsCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        => CommandRunner.Run(commandData, "RemoveUnusedViews");
}
