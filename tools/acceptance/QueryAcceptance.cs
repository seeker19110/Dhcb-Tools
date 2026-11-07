using System.Diagnostics;
using Autodesk.AutoCAD.ApplicationServices.Core;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using DhcbTools.Core.AutoCAD.Query;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

[assembly: CommandClass(typeof(DhcbTools.Acceptance.QueryAcceptance))]
namespace DhcbTools.Acceptance;

/// <summary>Fixture 30.000 entity trong database riêng; không sửa/lưu bản vẽ đang mở.</summary>
public sealed class QueryAcceptance
{
    // Chỉ gọi trên bản sao fixture; thiết lập page setup và paper layout để kiểm xuất PDF.
    [CommandMethod("DHCB_PDF_SETUP", CommandFlags.Modal)]
    public void PdfSetup()
    {
        var db = Application.DocumentManager.MdiActiveDocument.Database;
        using var tx = db.TransactionManager.StartTransaction();
        var validator = PlotSettingsValidator.Current;
        var setup = new PlotSettings(true) { PlotSettingsName = "QA Model 1 100" };
        validator.SetPlotConfigurationName(setup, "DWG To PDF.pc3", null);
        validator.RefreshLists(setup);
        var paper = validator.GetCanonicalMediaNameList(setup).Cast<string>()
            .First(n => n.StartsWith("ISO_A3_", StringComparison.Ordinal) && n.Contains("420.00"));
        validator.SetCanonicalMediaName(setup, paper);
        validator.SetPlotPaperUnits(setup, PlotPaperUnit.Millimeters);
        validator.SetPlotRotation(setup, PlotRotation.Degrees090);
        validator.SetPlotType(setup, PlotType.Extents);
        validator.SetPlotCentered(setup, true);
        validator.SetUseStandardScale(setup, false);
        validator.SetCustomPrintScale(setup, new CustomScale(1, 100));
        setup.AddToPlotSettingsDictionary(db);
        tx.AddNewlyCreatedDBObject(setup, true);
        var layoutId = LayoutManager.Current.CreateLayout("QA A3");
        var layout = (Layout)tx.GetObject(layoutId, OpenMode.ForWrite);
        var paperSpace = (BlockTableRecord)tx.GetObject(layout.BlockTableRecordId, OpenMode.ForWrite);
        foreach (ObjectId id in paperSpace)
        {
            var obj = tx.GetObject(id, OpenMode.ForRead);
            if (obj is Viewport vp && vp.Number == 1) continue;
            obj.UpgradeOpen(); obj.Erase();
        }
        var text = new DBText { TextString = "QA-LAYOUT-1-1", Position = new Point3d(30, 50, 0), Height = 3.5 };
        paperSpace.AppendEntity(text); tx.AddNewlyCreatedDBObject(text, true);
        validator.SetPlotConfigurationName(layout, "DWG To PDF.pc3", paper);
        validator.SetPlotPaperUnits(layout, PlotPaperUnit.Millimeters);
        validator.SetPlotRotation(layout, PlotRotation.Degrees090);
        validator.SetPlotType(layout, PlotType.Layout);
        validator.SetUseStandardScale(layout, false);
        validator.SetCustomPrintScale(layout, new CustomScale(1, 1));
        tx.Commit();
        Application.DocumentManager.MdiActiveDocument.Editor.WriteMessage("\nDHCB_PDF_SETUP PASS");
    }

    [CommandMethod("DHCB_ACCEPTANCE", CommandFlags.Modal)]
    public void Run()
    {
        var editor = Application.DocumentManager.MdiActiveDocument.Editor;
        var response = editor.GetString(new PromptStringOptions("\nReport JSON path: ") { AllowSpaces = true });
        if (response.Status != PromptStatus.OK) return;
        var results = new List<object>();
        using var db = new Database(true, true);
        using (var tx = db.TransactionManager.StartTransaction())
        {
            var ms = (BlockTableRecord)tx.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
            for (var i = 0; i < 25000; i++)
            {
                var line = new Line(new Point3d(i, 0, 0), new Point3d(i, 10, 0));
                ms.AppendEntity(line); tx.AddNewlyCreatedDBObject(line, true);
            }
            for (var i = 0; i < 5000; i++)
            {
                var text = new DBText { TextString = "BENCH-" + i, Position = new Point3d(i, 20, 0), Height = 2.5 };
                ms.AppendEntity(text); tx.AddNewlyCreatedDBObject(text, true);
            }
            var formatted = new MText { Contents = @"{\C1;ROOM-42}", Location = new Point3d(10, 30, 7), TextHeight = 1 };
            ms.AppendEntity(formatted); tx.AddNewlyCreatedDBObject(formatted, true);
            tx.Commit();
        }
        var allPassed = true;
        void Check(string name, QueryRequest request, Func<JObject, bool> verify)
        {
            var watch = Stopwatch.StartNew();
            var data = JObject.FromObject(AcadQueryHandler.Handle(db, request));
            watch.Stop();
            var passed = verify(data);
            allPassed &= passed;
            results.Add(new { name, passed, elapsedMs = watch.Elapsed.TotalMilliseconds, data });
        }
        Check("bounded default", new QueryRequest { Query = "entities" },
            j => (int)j["count"]! == 2000 && (bool)j["hasMore"]! && (int)j["nextOffset"]! == 2000);
        Check("line page with filters", new QueryRequest { Query = "entities", Params = new AcadQueryParams { EntityType = "Line", Limit = 7, Offset = 17 } },
            j => (int)j["count"]! == 7 && (double)j["entities"]![0]!["start"]!["x"]! == 17 && (int)j["nextOffset"]! == 24);
        Check("final page", new QueryRequest { Query = "entities", Params = new AcadQueryParams { EntityType = "Line", Limit = 5, Offset = 24998 } },
            j => (int)j["count"]! == 2 && !(bool)j["hasMore"]! && j["nextOffset"]!.Type == JTokenType.Null);
        Check("text filtered offset", new QueryRequest { Query = "text", Params = new AcadQueryParams { Limit = 3, Offset = 2 } },
            j => (int)j["count"]! == 3 && (string?)j["texts"]![0]!["text"] == "BENCH-2" && (int)j["nextOffset"]! == 5);
        Check("default text limit", new QueryRequest { Query = "text" }, j => (int)j["count"]! == 2000 && (bool)j["hasMore"]!);
        Check("text search applies before offset", new QueryRequest { Query = "text", Params = new AcadQueryParams { TextContains = "bench-49", Limit = 3, Offset = 2 } },
            j => (int)j["count"]! == 3 && (string?)j["texts"]![0]!["text"] == "BENCH-491" && (int)j["nextOffset"]! == 5);
        Check("text search handles and display", new QueryRequest { Query = "text", Params = new AcadQueryParams { TextContains = "BeNcH-4999" } },
            j => (int)j["count"]! == 1 && (string?)j["texts"]![0]!["displayText"] == "BENCH-4999" && !string.IsNullOrEmpty((string?)j["texts"]![0]!["handle"]));
        Check("formatted text searches visible characters", new QueryRequest { Query = "text", Params = new AcadQueryParams { TextContains = "room-42", LayerContains = "0" } },
            j => (int)j["count"]! == 1 && (string?)j["texts"]![0]!["displayText"] == "ROOM-42" && (double)j["texts"]![0]!["z"]! == 7);
        Check("formatting codes do not match text search", new QueryRequest { Query = "text", Params = new AcadQueryParams { TextContains = @"\C1;" } },
            j => (int)j["count"]! == 0 && !(bool)j["hasMore"]!);
        Check("text layer/search filters combine", new QueryRequest { Query = "text", Params = new AcadQueryParams { TextContains = "room", LayerContains = "NOT-FOUND" } },
            j => (int)j["count"]! == 0);
        Check("Text alias uses typed native filter", new QueryRequest { Query = "entities", Params = new AcadQueryParams { EntityType = "Text", Limit = 5, Offset = 4998 } },
            j => (int)j["count"]! == 2 && (string?)j["entities"]![0]!["type"] == "DBText" && !(bool)j["hasMore"]!);
        Check("unknown entity type remains a valid empty query", new QueryRequest { Query = "entities", Params = new AcadQueryParams { EntityType = "DOES-NOT-EXIST" } },
            j => (int)j["count"]! == 0);
        Check("layer page", new QueryRequest { Query = "layers", Params = new AcadQueryParams { Limit = 1 } }, j => (int)j["count"]! == 1);
        Check("empty definitions", new QueryRequest { Query = "blocks" }, j => (int)j["count"]! == 0 && !(bool)j["hasMore"]!);
        Check("empty inserts", new QueryRequest { Query = "inserts" }, j => (int)j["count"]! == 0 && !(bool)j["hasMore"]!);
        Check("negative offset rejected", new QueryRequest { Query = "entities", Params = new AcadQueryParams { Offset = -1 } }, j => j["error"] != null);
        Check("oversized page rejected", new QueryRequest { Query = "entities", Params = new AcadQueryParams { Limit = 10001 } }, j => j["error"] != null);
        Check("null params rejected", new QueryRequest { Query = "text", Params = null! }, j => j["error"] != null);
        Check("empty query rejected", new QueryRequest { Query = " " }, j => j["error"] != null);
        Check("oversized handle set rejected", new QueryRequest { Query = "entity_geometry", Params = new AcadQueryParams { Handles = Enumerable.Repeat("1", 10001).ToList() } }, j => j["error"] != null);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(response.StringResult))!);
        File.WriteAllText(response.StringResult, JsonConvert.SerializeObject(new { allPassed, fixtureEntities = 30001, results }, Formatting.Indented));
        editor.WriteMessage("\nDHCB_ACCEPTANCE " + (allPassed ? "PASS" : "FAIL"));
    }
}
