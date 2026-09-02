using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using DhcbTools.Core.AutoCAD.AutoNumbering;
using DhcbTools.Core.AutoCAD.DrawingCleanup;
using DhcbTools.Core.AutoCAD.LayerSync;

namespace DhcbTools.AutoCAD.Commands;

/// <summary>
/// Vỏ mỏng gọi vào Core — tương đương các class Commands trong DhcbTools.Revit.
/// Ba lệnh tương ứng với 3 lệnh Revit:
///   DHCB_LAYER_EXPORT    ↔ ParameterExport
///   DHCB_LAYER_IMPORT    ↔ ParameterImport
///   DHCB_CLEANUP         ↔ RemoveUnusedViews
///   DHCB_AUTONUMBER      ↔ AutoNumbering
/// </summary>
public sealed class DhcbCommands
{
    // ──────────────────────────────────────────────
    // Lệnh 1: Xuất layer ra CSV
    // ──────────────────────────────────────────────
    [CommandMethod("DHCB_LAYER_EXPORT", CommandFlags.Modal)]
    public void LayerExport()
    {
        var doc = Application.DocumentManager.MdiActiveDocument;
        if (doc is null) return;

        var ed = doc.Editor;

        // Hỏi đường dẫn output
        var outputOpt = new PromptStringOptions("\nĐường dẫn file CSV xuất ra [Enter = Desktop\\dhcb_layers.csv]: ")
        {
            AllowSpaces = true,
            DefaultValue = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "dhcb_layers.csv")
        };
        var outputResult = ed.GetString(outputOpt);
        if (outputResult.Status != PromptStatus.OK) return;

        var config = new LayerExportConfig
        {
            OutputPath = string.IsNullOrWhiteSpace(outputResult.StringResult)
                ? outputOpt.DefaultValue
                : outputResult.StringResult,
        };

        var command = new LayerExportCommand();
        var result = command.Execute(doc.Database, config);

        PrintResult(ed, result);
    }

    // ──────────────────────────────────────────────
    // Lệnh 2: Nhập layer từ CSV
    // ──────────────────────────────────────────────
    [CommandMethod("DHCB_LAYER_IMPORT", CommandFlags.Modal)]
    public void LayerImport()
    {
        var doc = Application.DocumentManager.MdiActiveDocument;
        if (doc is null) return;

        var ed = doc.Editor;

        var inputOpt = new PromptStringOptions("\nĐường dẫn file CSV đầu vào: ") { AllowSpaces = true };
        var inputResult = ed.GetString(inputOpt);
        if (inputResult.Status != PromptStatus.OK || string.IsNullOrWhiteSpace(inputResult.StringResult)) return;

        var dryOpt = new PromptKeywordOptions("\nChế độ [Xemtrước/Thật] <Xemtrước>: ");
        dryOpt.Keywords.Add("Xemtrước");
        dryOpt.Keywords.Add("Thật");
        dryOpt.AllowNone = true;
        var dryResult = ed.GetKeywords(dryOpt);
        var isDryRun = dryResult.Status != PromptStatus.OK || dryResult.StringResult != "Thật";

        var config = new LayerImportConfig
        {
            InputPath = inputResult.StringResult,
            DryRun = isDryRun,
            CreateMissing = true,
        };

        var command = new LayerImportCommand();
        var result = command.Execute(doc.Database, config);

        PrintResult(ed, result);
    }

    // ──────────────────────────────────────────────
    // Lệnh 3: Dọn dẹp drawing
    // ──────────────────────────────────────────────
    [CommandMethod("DHCB_CLEANUP", CommandFlags.Modal)]
    public void DrawingCleanup()
    {
        var doc = Application.DocumentManager.MdiActiveDocument;
        if (doc is null) return;

        var ed = doc.Editor;

        var dryOpt = new PromptKeywordOptions("\nChế độ [Xemtrước/Thật] <Xemtrước>: ");
        dryOpt.Keywords.Add("Xemtrước");
        dryOpt.Keywords.Add("Thật");
        dryOpt.AllowNone = true;
        var dryResult = ed.GetKeywords(dryOpt);
        var isDryRun = dryResult.Status != PromptStatus.OK || dryResult.StringResult != "Thật";

        var config = new CleanupConfig
        {
            DryRun = isDryRun,
            RemoveEmptyLayers = true,
            PurgeUnusedBlocks = true,
            PurgeUnusedLinetypes = true,
        };

        var command = new DrawingCleanupCommand();
        var result = command.Execute(doc.Database, config);

        PrintResult(ed, result);
    }

    // ──────────────────────────────────────────────
    // Lệnh 4: Đánh số hàng loạt Block
    // ──────────────────────────────────────────────
    [CommandMethod("DHCB_AUTONUMBER", CommandFlags.Modal)]
    public void AutoNumber()
    {
        var doc = Application.DocumentManager.MdiActiveDocument;
        if (doc is null) return;

        var ed = doc.Editor;

        var blockOpt = new PromptStringOptions("\nTên Block cần đánh số: ") { AllowSpaces = false };
        var blockResult = ed.GetString(blockOpt);
        if (blockResult.Status != PromptStatus.OK || string.IsNullOrWhiteSpace(blockResult.StringResult)) return;

        var attrOpt = new PromptStringOptions("\nTên Attribute Tag [Enter = MARK]: ")
        {
            AllowSpaces = false,
            DefaultValue = "MARK"
        };
        var attrResult = ed.GetString(attrOpt);
        if (attrResult.Status != PromptStatus.OK) return;

        var prefixOpt = new PromptStringOptions("\nTiền tố [Enter = không có]: ")
        {
            AllowSpaces = false,
            DefaultValue = ""
        };
        var prefixResult = ed.GetString(prefixOpt);

        var dryOpt = new PromptKeywordOptions("\nChế độ [Xemtrước/Thật] <Xemtrước>: ");
        dryOpt.Keywords.Add("Xemtrước");
        dryOpt.Keywords.Add("Thật");
        dryOpt.AllowNone = true;
        var dryResult = ed.GetKeywords(dryOpt);
        var isDryRun = dryResult.Status != PromptStatus.OK || dryResult.StringResult != "Thật";

        var config = new AutoNumberingConfig
        {
            BlockName = blockResult.StringResult,
            AttributeTag = string.IsNullOrWhiteSpace(attrResult.StringResult) ? "MARK" : attrResult.StringResult,
            Prefix = prefixResult.Status == PromptStatus.OK ? prefixResult.StringResult : string.Empty,
            DryRun = isDryRun,
        };

        var command = new AutoNumberingCommand();
        var result = command.Execute(doc.Database, config);

        PrintResult(ed, result);
    }

    // ──────────────────────────────────────────────
    // Helper
    // ──────────────────────────────────────────────
    private static void PrintResult(Editor ed, Core.AutoCAD.CommandResult result)
    {
        ed.WriteMessage($"\n{(result.Success ? "✓" : "✗")} {result.Summary}\n");
        foreach (var msg in result.Messages)
        {
            ed.WriteMessage($"  • {msg}\n");
        }
        foreach (var err in result.Errors)
        {
            ed.WriteMessage($"  ! {err}\n");
        }
    }
}
