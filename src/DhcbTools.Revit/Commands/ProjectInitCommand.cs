using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System.Collections.Generic;

namespace DhcbTools.Revit.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public sealed class ProjectInitCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var doc = commandData.Application.ActiveUIDocument.Document;

            var td = new TaskDialog("DHCB Tools - Khoi tao du an")
            {
                MainInstruction = "Khoi tao du an (Xem truoc)",
                MainContent = "Lenh se tao level va view plan tu cau hinh mac dinh." +
                              " Dung HTTP Bridge (POST /execute) de truyen config JSON day du.",
                CommonButtons = TaskDialogCommonButtons.Ok | TaskDialogCommonButtons.Cancel,
            };
            if (td.Show() != TaskDialogResult.Ok) return Result.Cancelled;

            var config = new Core.ProjectInit.LevelSetupConfig
            {
                DryRun = true,
                Levels = new List<Core.ProjectInit.LevelDefinition>
                {
                    new Core.ProjectInit.LevelDefinition { Name = "Tang Ham",  ElevationMm = -3500 },
                    new Core.ProjectInit.LevelDefinition { Name = "Tang Tret", ElevationMm = 0     },
                    new Core.ProjectInit.LevelDefinition { Name = "Tang 1",    ElevationMm = 3800  },
                    new Core.ProjectInit.LevelDefinition { Name = "Tang 2",    ElevationMm = 7600  },
                    new Core.ProjectInit.LevelDefinition { Name = "Tang Mai",  ElevationMm = 11400 },
                },
            };

            var result = new Core.ProjectInit.LevelSetupCommand().Execute(doc, config);
            Feedback.Show("Khoi tao du an (Xem truoc)", result);
            return result.Success ? Result.Succeeded : Result.Failed;
        }
    }
}
