using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace DhcbTools.Revit.Commands
{
    /// <summary>
    /// Nút "Khởi tạo dự án": mở form động của <c>LevelSetup</c> (danh sách tầng + view plan) qua
    /// <see cref="CommandRunner"/>. Trước đây nút này chạy 5 tầng viết cứng và luôn DryRun — không
    /// bao giờ tạo được gì thật từ Ribbon.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public sealed class ProjectInitCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
            => CommandRunner.Run(commandData, "LevelSetup");
    }
}
