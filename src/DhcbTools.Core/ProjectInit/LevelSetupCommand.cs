using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;

namespace DhcbTools.Core.ProjectInit
{
    public sealed class LevelSetupCommand : ICoreCommand<LevelSetupConfig>
    {
        private const double MmToFeet = 1.0 / 304.8;
        public string CommandName => "LevelSetup";

        public CommandResult Execute(Document doc, LevelSetupConfig config)
        {
            var existingNames = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Level lvl in new FilteredElementCollector(doc).OfClass(typeof(Level)).ToElements())
                existingNames.Add(lvl.Name);

            ViewFamilyType floorPlanVft = null;
            foreach (ViewFamilyType vft in new FilteredElementCollector(doc)
                         .OfClass(typeof(ViewFamilyType)).ToElements().Cast<ViewFamilyType>())
            {
                if (vft.ViewFamily == ViewFamily.FloorPlan) { floorPlanVft = vft; break; }
            }

            var messages = new StringBuilder();
            int created = 0;

            using (var tx = new Transaction(doc, "DHCB - Tao tang va view plan"))
            {
                tx.GetFailureHandlingOptions().SetFailuresPreprocessor(new SilentFailuresPreprocessor());
                tx.Start();
                try
                {
                    foreach (var def in config.Levels)
                    {
                        if (config.SkipExisting && existingNames.Contains(def.Name))
                        { messages.AppendLine("[Skip] " + def.Name); continue; }

                        double elevFeet = def.ElevationMm * MmToFeet;
                        Level level = Level.Create(doc, elevFeet);
                        level.Name = def.Name;
                        created++;
                        messages.AppendLine("[Create] Level: " + def.Name + " @ " + def.ElevationMm + " mm");

                        if (def.CreateFloorPlan && floorPlanVft != null)
                        {
                            ViewPlan view = ViewPlan.Create(doc, floorPlanVft.Id, level.Id);
                            view.Name = def.Name;
                            if (!string.IsNullOrEmpty(def.ViewTemplateName))
                            {
                                foreach (View v in new FilteredElementCollector(doc)
                                             .OfClass(typeof(View)).ToElements().Cast<View>())
                                {
                                    if (v.IsTemplate && string.Equals(v.Name, def.ViewTemplateName, StringComparison.OrdinalIgnoreCase))
                                    { view.ViewTemplateId = v.Id; break; }
                                }
                            }
                        }
                    }

                    if (config.DryRun) { tx.RollBack(); return CommandResult.Ok("[Dry Run] Would create " + created + " level(s)." + Environment.NewLine + messages, created); }
                    tx.Commit();
                }
                catch (System.Exception ex) { tx.RollBack(); return CommandResult.Fail("Error: " + ex.Message); }
            }
            return CommandResult.Ok("Created " + created + " level(s)." + Environment.NewLine + messages, created);
        }
    }
}