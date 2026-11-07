using System.Diagnostics;
using Autodesk.AutoCAD.ApplicationServices.Core;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using DhcbTools.Core.AutoCAD.TextTools;
using DhcbTools.Shared.Hosting;
using DhcbTools.Shared.Logic.Cad;
using Newtonsoft.Json;

[assembly: CommandClass(typeof(DhcbTools.Acceptance.TextAcceptance))]
namespace DhcbTools.Acceptance;

/// <summary>Runs preview/commit against disposable in-memory databases, never the user's drawing.</summary>
public sealed class TextAcceptance
{
    [CommandMethod("DHCB_TEXT_ACCEPTANCE", CommandFlags.Modal)]
    public void Run()
    {
        var editor = Application.DocumentManager.MdiActiveDocument.Editor;
        var response = editor.GetString(new PromptStringOptions("\nReport JSON path: ") { AllowSpaces = true });
        if (response.Status != PromptStatus.OK) return;
        var results = new List<object>();
        var allPassed = true;
        void Check(string name, Func<bool> verify)
        {
            var watch = Stopwatch.StartNew();
            try
            {
                var passed = verify();
                allPassed &= passed;
                results.Add(new { name, passed, elapsedMs = watch.Elapsed.TotalMilliseconds });
            }
            catch (System.Exception ex)
            {
                allPassed = false;
                results.Add(new { name, passed = false, error = ex.ToString() });
            }
        }
        var command = new TextReplaceCommand();
        foreach (var scope in new[] { TextReplaceScope.All, TextReplaceScope.ModelSpace, TextReplaceScope.PaperSpace, TextReplaceScope.BlockDefinitions })
        {
            Check("scope preview/commit: " + scope, () =>
            {
                using var db = new Database(true, true);
                var ids = new List<ObjectId>();
                using (var tx = db.TransactionManager.StartTransaction())
                {
                    var bt = (BlockTable)tx.GetObject(db.BlockTableId, OpenMode.ForWrite);
                    var definition = new BlockTableRecord { Name = "TEXT-SCOPE" };
                    var defId = bt.Add(definition); tx.AddNewlyCreatedDBObject(definition, true);
                    foreach (var owner in new[] { SymbolUtilityServices.GetBlockModelSpaceId(db), SymbolUtilityServices.GetBlockPaperSpaceId(db), defId })
                        ids.Add(Append(tx, owner, new DBText { TextString = "OLD", Height = 1 }));
                    tx.Commit();
                }
                var count = scope == TextReplaceScope.All ? 3 : 1;
                var preview = command.Execute(db, new TextReplaceConfig { Find = "OLD", Replace = "NEW", Scope = scope });
                if (!preview.IsComplete || preview.AffectedCount != count || ids.Any(id => Text(db, id) != "OLD")) return false;
                var written = command.Execute(db, new TextReplaceConfig { Find = "OLD", Replace = "NEW", Scope = scope, DryRun = false });
                var chosen = scope == TextReplaceScope.ModelSpace ? 0 : scope == TextReplaceScope.PaperSpace ? 1 : 2;
                return written.IsComplete && written.AffectedCount == count
                    && ids.Select((id, i) => Text(db, id) == (scope == TextReplaceScope.All || i == chosen ? "NEW" : "OLD")).All(v => v)
                    && command.Execute(db, new TextReplaceConfig { Find = "OLD", Replace = "NEW", Scope = scope }).AffectedCount == 0;
            });
        }
        Check("layer/tag filters include inherited layer 0 and leave ordinary text alone", () =>
        {
            using var db = new Database(true, true);
            ObjectId mark, other, plain;
            using (var tx = db.TransactionManager.StartTransaction())
            {
                var layers = (LayerTable)tx.GetObject(db.LayerTableId, OpenMode.ForWrite);
                var layer = new LayerTableRecord { Name = "QA-TARGET" };
                var layerId = layers.Add(layer); tx.AddNewlyCreatedDBObject(layer, true);
                var bt = (BlockTable)tx.GetObject(db.BlockTableId, OpenMode.ForWrite);
                var definition = new BlockTableRecord { Name = "TEXT-TAGS" };
                var blockId = bt.Add(definition); tx.AddNewlyCreatedDBObject(definition, true);
                var msId = SymbolUtilityServices.GetBlockModelSpaceId(db);
                var br = new BlockReference(Point3d.Origin, blockId) { LayerId = layerId };
                Append(tx, msId, br);
                var a = new AttributeReference { Tag = "MARK", TextString = "OLD", Height = 1 };
                mark = br.AttributeCollection.AppendAttribute(a); tx.AddNewlyCreatedDBObject(a, true);
                var b = new AttributeReference { Tag = "OTHER", TextString = "OLD", Height = 1 };
                other = br.AttributeCollection.AppendAttribute(b); tx.AddNewlyCreatedDBObject(b, true);
                plain = Append(tx, msId, new DBText { TextString = "OLD", LayerId = layerId, Height = 1 });
                tx.Commit();
            }
            var preview = command.Execute(db, new TextReplaceConfig { Find = "OLD", Replace = "NEW", AttributeTag = "mark", LayerContains = "target" });
            var result = command.Execute(db, new TextReplaceConfig { Find = "OLD", Replace = "NEW", AttributeTag = "mark", LayerContains = "target", DryRun = false });
            return preview.IsComplete && preview.AffectedCount == 1 && result.IsComplete && result.AffectedCount == 1
                && Text(db, mark) == "NEW" && Text(db, other) == "OLD" && Text(db, plain) == "OLD";
        });
        foreach (var scope in new[] { TextReplaceScope.ModelSpace, TextReplaceScope.BlockDefinitions })
        {
            Check("MText attribute preserves formatting/paragraphs: " + scope, () =>
            {
                using var db = new Database(true, true);
                ObjectId definitionId, attributeId;
                using (var tx = db.TransactionManager.StartTransaction())
                {
                    var bt = (BlockTable)tx.GetObject(db.BlockTableId, OpenMode.ForWrite);
                    var btr = new BlockTableRecord { Name = "TEXT-MULTILINE" };
                    var blockId = bt.Add(btr); tx.AddNewlyCreatedDBObject(btr, true);
                    var definition = new AttributeDefinition { Tag = "MARK", TextString = "OLD", Height = 1, IsMTextAttributeDefinition = true };
                    definitionId = Append(tx, blockId, definition);
                    using (var mt = definition.MTextAttributeDefinition)
                    {
                        mt.Contents = @"{\C1;OLD}\Psecond OLD";
                        definition.MTextAttributeDefinition = mt;
                    }
                    var br = new BlockReference(Point3d.Origin, blockId);
                    Append(tx, SymbolUtilityServices.GetBlockModelSpaceId(db), br);
                    var attr = new AttributeReference();
                    attr.SetAttributeFromBlock(definition, br.BlockTransform);
                    attributeId = br.AttributeCollection.AppendAttribute(attr); tx.AddNewlyCreatedDBObject(attr, true);
                    tx.Commit();
                }
                var preview = command.Execute(db, new TextReplaceConfig { Find = "OLD", Replace = "NEW", AttributeTag = "MARK", Scope = scope });
                var written = command.Execute(db, new TextReplaceConfig { Find = "OLD", Replace = "NEW", AttributeTag = "MARK", Scope = scope, DryRun = false });
                using var read = db.TransactionManager.StartTransaction();
                var changedId = scope == TextReplaceScope.ModelSpace ? attributeId : definitionId;
                using var changed = changedId == attributeId
                    ? ((AttributeReference)read.GetObject(changedId, OpenMode.ForRead)).MTextAttribute
                    : ((AttributeDefinition)read.GetObject(changedId, OpenMode.ForRead)).MTextAttributeDefinition;
                var unchangedId = changedId == attributeId ? definitionId : attributeId;
                using var unchanged = unchangedId == attributeId
                    ? ((AttributeReference)read.GetObject(unchangedId, OpenMode.ForRead)).MTextAttribute
                    : ((AttributeDefinition)read.GetObject(unchangedId, OpenMode.ForRead)).MTextAttributeDefinition;
                return preview.IsComplete && preview.AffectedCount == 1 && written.IsComplete && written.AffectedCount == 1
                    && MTextReplace.DisplayText(changed.Contents) == "NEW\nsecond NEW" && changed.Contents.Contains(@"\C1;")
                    && MTextReplace.DisplayText(unchanged.Contents) == "OLD\nsecond OLD";
            });
        }
        Check("dynamic field objects remain linked and are reported", () =>
        {
            using var db = new Database(true, true);
            ObjectId id, fieldId;
            using (var tx = db.TransactionManager.StartTransaction())
            {
                var text = new DBText { TextString = "OLD", Height = 1 };
                id = Append(tx, SymbolUtilityServices.GetBlockModelSpaceId(db), text);
                var field = new Field(@"%<\AcVar Ctab>%");
                fieldId = text.SetField(field); tx.AddNewlyCreatedDBObject(field, true);
                tx.Commit();
            }
            var result = command.Execute(db, new TextReplaceConfig { Find = ".", Replace = "X", UseRegex = true, DryRun = false });
            using var read = db.TransactionManager.StartTransaction();
            var textAfter = (DBText)read.GetObject(id, OpenMode.ForRead);
            return !result.IsComplete && result.AffectedCount == 0 && result.Messages.Any(s => s.Contains("field"))
                && textAfter.HasFields && textAfter.GetField() == fieldId;
        });
        Check("regex total budget aborts the entire plan before writing", () =>
        {
            using var db = new Database(true, true);
            ObjectId first;
            using (var tx = db.TransactionManager.StartTransaction())
            {
                var ms = SymbolUtilityServices.GetBlockModelSpaceId(db);
                first = Append(tx, ms, new DBText { TextString = "OLD", Height = 1 });
                Append(tx, ms, new DBText { TextString = new string('a', 5000) + "!", Height = 1 });
                tx.Commit();
            }
            var result = command.Execute(db, new TextReplaceConfig { Find = "OLD|(a+)+$", Replace = "NEW", UseRegex = true, TimeBudgetSeconds = 1, DryRun = false });
            return !result.Success && result.AffectedCount == 0 && result.Summary.Contains("E-TEXT-BUDGET") && Text(db, first) == "OLD";
        });
        Check("preview details are bounded while counts remain complete", () =>
        {
            using var db = new Database(true, true);
            using (var tx = db.TransactionManager.StartTransaction())
            {
                var ms = SymbolUtilityServices.GetBlockModelSpaceId(db);
                for (var i = 0; i < 5000; i++) Append(tx, ms, new DBText { TextString = "OLD", Height = 1 });
                tx.Commit();
            }
            var result = command.Execute(db, new TextReplaceConfig { Find = "OLD", Replace = "NEW", MaxPreviewItems = 3 });
            return result.IsComplete && result.AffectedCount == 5000 && result.Messages.Count == 4 && result.Messages.Last().Contains("4997");
        });
        Check("type switches and unmatched layer filter", () =>
        {
            using var db = new Database(true, true);
            using (var tx = db.TransactionManager.StartTransaction())
            {
                var ms = SymbolUtilityServices.GetBlockModelSpaceId(db);
                Append(tx, ms, new DBText { TextString = "OLD", Height = 1 });
                Append(tx, ms, new MText { Contents = "OLD", TextHeight = 1 });
                tx.Commit();
            }
            var onlyText = command.Execute(db, new TextReplaceConfig { Find = "OLD", IncludeMText = false });
            var onlyMtext = command.Execute(db, new TextReplaceConfig { Find = "OLD", IncludeDbText = false });
            var noLayer = command.Execute(db, new TextReplaceConfig { Find = "OLD", LayerContains = "NOT-FOUND" });
            return onlyText.IsComplete && onlyText.AffectedCount == 1 && onlyMtext.IsComplete && onlyMtext.AffectedCount == 1
                && noLayer.IsComplete && noLayer.AffectedCount == 0;
        });
        Check("invalid settings cannot write", () =>
        {
            using var db = new Database(true, true);
            var invalid = new[]
            {
                new TextReplaceConfig { Find = "OLD", Scope = (TextReplaceScope)99, DryRun = false },
                new TextReplaceConfig { Find = "OLD", TimeBudgetSeconds = 0, DryRun = false },
                new TextReplaceConfig { Find = "OLD", MaxPreviewItems = -1, DryRun = false },
                new TextReplaceConfig { Find = "OLD", IncludeDbText = false, IncludeMText = false, IncludeAttributes = false, DryRun = false },
                new TextReplaceConfig { Find = "OLD", AttributeTag = "MARK", IncludeAttributes = false, DryRun = false },
            };
            return invalid.All(c => !command.Execute(db, c).Success);
        });
        var report = Path.GetFullPath(response.StringResult);
        Directory.CreateDirectory(Path.GetDirectoryName(report)!);
        File.WriteAllText(report, JsonConvert.SerializeObject(new { allPassed, results }, Formatting.Indented));
        editor.WriteMessage("\nDHCB_TEXT_ACCEPTANCE " + (allPassed ? "PASS" : "FAIL"));
    }

    private static ObjectId Append(Transaction tx, ObjectId owner, Entity entity)
    {
        var block = (BlockTableRecord)tx.GetObject(owner, OpenMode.ForWrite);
        var id = block.AppendEntity(entity); tx.AddNewlyCreatedDBObject(entity, true);
        return id;
    }
    private static string Text(Database db, ObjectId id)
    {
        using var tx = db.TransactionManager.StartTransaction();
        return ((DBText)tx.GetObject(id, OpenMode.ForRead)).TextString;
    }
}
