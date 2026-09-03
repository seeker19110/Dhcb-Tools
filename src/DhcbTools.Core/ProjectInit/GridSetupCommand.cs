using System;
using System.Collections.Generic;
using System.Text;
using Autodesk.Revit.DB;

namespace DhcbTools.Core.ProjectInit
{
    public sealed class GridSetupCommand : ICoreCommand<GridSetupConfig>
    {
        public string CommandName => "GridSetup";

        public CommandResult Execute(Document doc, GridSetupConfig config)
        {
            var existingNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Grid g in new FilteredElementCollector(doc).OfClass(typeof(Grid)).ToElements())
                existingNames.Add(g.Name);

            var messages = new StringBuilder();
            int created = 0;

            using (var tx = new Transaction(doc, "DHCB - Tao luoi truc"))
            {
                RevitCompat.ApplyFailurePolicy(tx);
                tx.Start();
                try
                {
                    foreach (var def in config.Grids)
                    {
                        if (config.SkipExisting && existingNames.Contains(def.Name))
                        { messages.AppendLine("[Bỏ qua, đã có] " + def.Name); continue; }

                        double pos   = RevitCompat.MmToFt(def.PositionMm);
                        double start = RevitCompat.MmToFt(def.StartMm);
                        double end   = RevitCompat.MmToFt(def.EndMm);

                        Line line = def.Orientation == GridOrientation.Vertical
                            ? Line.CreateBound(new XYZ(pos, start, 0), new XYZ(pos, end, 0))
                            : Line.CreateBound(new XYZ(start, pos, 0), new XYZ(end, pos, 0));

                        Grid grid = Grid.Create(doc, line);
                        grid.Name = def.Name;
                        created++;
                        messages.AppendLine("[Tạo] Trục: " + def.Name + " (" + def.Orientation + ") @ " + def.PositionMm + " mm");
                    }

                    if (config.DryRun) { tx.RollBack(); return CommandResult.Ok("[Xem trước] Sẽ tạo " + created + " trục." + Environment.NewLine + messages, created); }
                    tx.Commit();
                }
                catch (System.Exception ex) { tx.RollBack(); return CommandResult.Fail("Lỗi khi tạo trục, đã hoàn tác: " + ex.Message); }
            }
            return CommandResult.Ok("Đã tạo " + created + " trục." + Environment.NewLine + messages, created);
        }
    }
}