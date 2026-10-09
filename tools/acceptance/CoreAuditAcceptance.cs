using Autodesk.AutoCAD.ApplicationServices.Core;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using DhcbTools.Core.AutoCAD.Attributes;
using DhcbTools.Core.AutoCAD.Reporting;
using DhcbTools.Core.AutoCAD.LayerTools;
using DhcbTools.Shared.Hosting;
using DhcbTools.Shared.Logic;
using Newtonsoft.Json;

[assembly: CommandClass(typeof(DhcbTools.Acceptance.CoreAuditAcceptance))]
namespace DhcbTools.Acceptance;

/// <summary>Kiểm import/đơn vị trên database riêng trong RAM; không sửa DWG đang mở.</summary>
public sealed class CoreAuditAcceptance
{
    [CommandMethod("DHCB_CORE_AUDIT", CommandFlags.Modal)]
    public void Run()
    {
        var editor = Application.DocumentManager.MdiActiveDocument.Editor;
        var response = editor.GetString(new PromptStringOptions("\nReport JSON path: ") { AllowSpaces = true });
        if (response.Status != PromptStatus.OK) return;
        var report = Path.GetFullPath(response.StringResult);
        var output = Path.GetDirectoryName(report)!;
        Directory.CreateDirectory(output);
        var results = new List<object>();
        var allPassed = true;
        void Check(string name, Func<bool> verify)
        {
            try
            {
                var passed = verify();
                allPassed &= passed;
                results.Add(new { name, passed });
            }
            catch (System.Exception ex)
            {
                allPassed = false;
                results.Add(new { name, passed = false, error = ex.ToString() });
            }
        }

        foreach (var unitCase in new[] { (UnitsValue.Millimeters, 10.0), (UnitsValue.Meters, 10000.0), (UnitsValue.Inches, 254.0), (UnitsValue.Undefined, 10.0) })
        {
            Check("GridExtract mm and unique names: " + unitCase.Item1, () =>
            {
                using var db = new Database(true, true) { Insunits = unitCase.Item1 };
                using (var tx = db.TransactionManager.StartTransaction())
                {
                    var layers = (LayerTable)tx.GetObject(db.LayerTableId, OpenMode.ForWrite);
                    var layer = new LayerTableRecord { Name = "AXIS" };
                    var layerId = layers.Add(layer); tx.AddNewlyCreatedDBObject(layer, true);
                    var ms = (BlockTableRecord)tx.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
                    foreach (var entity in new Entity[] {
                        new Line(new Point3d(0, 0, 0), new Point3d(10, 0, 0)),
                        new Line(new Point3d(0, 20, 0), new Point3d(10, 20, 0)),
                        new DBText { TextString = "AXIS-2", Position = Point3d.Origin, Height = 0.1 } })
                    { entity.LayerId = layerId; ms.AppendEntity(entity); tx.AddNewlyCreatedDBObject(entity, true); }
                    tx.Commit();
                }
                var changes = 0;
                db.ObjectModified += (_, _) => changes++;
                var path = Path.Combine(output, "grid-" + unitCase.Item1 + ".csv");
                var result = new GridExtractCommand().Execute(db, new GridExtractConfig { OutputPath = path });
                var rows = CsvText.ReadRecords(path).ToList();
                return result.IsComplete && changes == 0 && rows.Count == 3 && rows[1][0] == "AXIS-2" && rows[2][0] == "AXIS-3"
                    && NumericText.TryParseDouble(rows[1][3], out var x) && Math.Abs(x - unitCase.Item2) < 1e-8;
            });
        }

        Check("GridExtract ray keeps start and direction", () =>
        {
            using var db = new Database(true, true) { Insunits = UnitsValue.Meters };
            using (var tx = db.TransactionManager.StartTransaction())
            {
                var ms = (BlockTableRecord)tx.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
                var ray = new Ray { BasePoint = new Point3d(2, 3, 0), UnitDir = Vector3d.XAxis };
                ms.AppendEntity(ray); tx.AddNewlyCreatedDBObject(ray, true); tx.Commit();
            }
            var path = Path.Combine(output, "ray.csv");
            var result = new GridExtractCommand().Execute(db, new GridExtractConfig { GridLayer = "0", OutputPath = path });
            var row = CsvText.ReadRecords(path).Skip(1).Single();
            return result.IsComplete && NumericText.TryParseDouble(row[1], out var start) && start == 2000
                && NumericText.TryParseDouble(row[3], out var end) && end == 12000;
        });

        Check("LayerTranslate refuses ambiguous source before creating layers", () =>
        {
            using var db = new Database(true, true);
            var changes = 0;
            db.ObjectAppended += (_, _) => changes++;
            db.ObjectModified += (_, _) => changes++;
            var path = Path.Combine(output, "layer-map-conflict.csv");
            File.WriteAllText(path, "Source,Target\n0,QA-TARGET-A\n0,QA-TARGET-B\n");
            foreach (var preview in new[] { true, false })
            {
                var result = new LayerTranslateCommand().Execute(db, new LayerTranslateConfig { MapCsvPath = path, DryRun = preview });
                if (result.Success || result.AffectedCount != 0 || result.Errors.Count == 0 || changes != 0) return false;
            }
            return true;
        });

        using (var db = new Database(true, true))
        {
            ObjectId insert;
            using (var tx = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tx.GetObject(db.BlockTableId, OpenMode.ForWrite);
                var definition = new BlockTableRecord { Name = "CORE-AUDIT" };
                var blockId = bt.Add(definition); tx.AddNewlyCreatedDBObject(definition, true);
                var ms = (BlockTableRecord)tx.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
                var br = new BlockReference(Point3d.Origin, blockId);
                insert = ms.AppendEntity(br); tx.AddNewlyCreatedDBObject(br, true);
                foreach (var tag in new[] { "MARK", "OTHER" })
                {
                    var attr = new AttributeReference { Tag = tag, TextString = "original", Position = Point3d.Origin, Height = 1 };
                    br.AttributeCollection.AppendAttribute(attr); tx.AddNewlyCreatedDBObject(attr, true);
                }
                tx.Commit();
            }
            var handle = insert.Handle.ToString();
            string Value(string tag)
            {
                using var tx = db.TransactionManager.StartTransaction();
                var br = (BlockReference)tx.GetObject(insert, OpenMode.ForRead);
                foreach (ObjectId id in br.AttributeCollection)
                {
                    var attr = (AttributeReference)tx.GetObject(id, OpenMode.ForRead);
                    if (attr.Tag == tag) return attr.TextString;
                }
                throw new InvalidOperationException("Missing attribute " + tag);
            }
            var path = Path.Combine(output, "attributes.csv");
            void Csv(params (string Tag, string Value)[] cells) => File.WriteAllText(path,
                "BlockName,Handle,AttributeTag,AttributeValue\n" + string.Join("\n", cells.Select(c => CsvText.JoinLine(new[] { "CORE-AUDIT", handle, c.Tag, c.Value }))), CsvText.Utf8WithBom);
            CommandResult Import(bool preview) => new AttributeImportCommand().Execute(db, new AttributeImportConfig { InputPath = path, DryRun = preview });
            Csv(("MARK", "changed"), ("mark", "original"));
            Check("Conflicting CSV preview refuses target", () => { var r = Import(true); return !r.IsComplete && r.AffectedCount == 0 && r.Errors.Count == 1 && Value("MARK") == "original"; });
            Check("Conflicting CSV commit refuses target", () => { var r = Import(false); return !r.IsComplete && r.AffectedCount == 0 && r.Errors.Count == 1 && Value("MARK") == "original"; });
            Csv(("MARK", "changed"), ("mark", "changed"));
            Check("Identical duplicate preview counts once", () => { var r = Import(true); return r.IsComplete && r.AffectedCount == 1 && Value("MARK") == "original"; });
            Check("Identical duplicate commit counts once", () => { var r = Import(false); return r.IsComplete && r.AffectedCount == 1 && Value("MARK") == "changed"; });
            Check("Identical duplicate repeated import is no-op", () => { var r = Import(false); return r.IsComplete && r.AffectedCount == 0; });
            Csv(("MARK", "bad-a"), ("mark", "bad-b"), ("OTHER", "independent"));
            Check("Conflict preview still plans independent target", () => { var r = Import(true); return !r.IsComplete && r.AffectedCount == 1 && Value("OTHER") == "original"; });
            Check("Conflict commit preserves target and applies independent", () => { var r = Import(false); return !r.IsComplete && r.AffectedCount == 1 && Value("MARK") == "changed" && Value("OTHER") == "independent"; });
        }
        File.WriteAllText(report, JsonConvert.SerializeObject(new { allPassed, results }, Formatting.Indented));
        editor.WriteMessage("\nDHCB_CORE_AUDIT " + (allPassed ? "PASS" : "FAIL"));
    }
}
