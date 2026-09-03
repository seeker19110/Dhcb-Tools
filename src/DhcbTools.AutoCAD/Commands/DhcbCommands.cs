using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using DhcbTools.Core.AutoCAD.Attributes;
using DhcbTools.Core.AutoCAD.AutoNumbering;
using DhcbTools.Core.AutoCAD.DrawingCleanup;
using DhcbTools.Core.AutoCAD.LayerSync;
using DhcbTools.Core.AutoCAD.LayerTools;
using DhcbTools.Core.AutoCAD.Reporting;
using DhcbTools.Core.AutoCAD.TextTools;

namespace DhcbTools.AutoCAD.Commands;

/// <summary>
/// Vỏ mỏng gọi vào Core — tương đương các class Commands trong DhcbTools.Revit.
/// Bốn lệnh đầu tương ứng với 4 lệnh Revit ban đầu, 11 lệnh sau là các lệnh AutoCAD riêng:
///   DHCB_LAYER_EXPORT       ↔ ParameterExport
///   DHCB_LAYER_IMPORT       ↔ ParameterImport
///   DHCB_CLEANUP            ↔ RemoveUnusedViews
///   DHCB_AUTONUMBER         ↔ AutoNumbering
///   DHCB_ATTR_EXPORT        ↔ AttributeExport
///   DHCB_ATTR_IMPORT        ↔ AttributeImport
///   DHCB_TEXT_REPLACE       ↔ TextReplace
///   DHCB_LAYER_CHECK        ↔ LayerStandardCheck
///   DHCB_GRID_EXTRACT       ↔ GridExtract
///   DHCB_XREF_AUDIT         ↔ XrefAudit
///   DHCB_LAYER_TRANSLATE    ↔ LayerTranslate
///   DHCB_DRAWING_COMPARE    ↔ DrawingCompare
///   DHCB_BLOCK_QUANTITY     ↔ BlockQuantity
///   DHCB_ATTR_INCREMENT     ↔ AttributeIncrement
///   DHCB_LAYER_MAP          ↔ CadLayerMap
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

        // Purge sâu (text style / dim style / regapp) hỏi riêng: đây là nhóm dễ làm hỏng bản vẽ nhất
        // nếu bản vẽ có XData của add-in khác, nên mặc định TẮT thay vì lặng lẽ bật.
        var deepOpt = new PromptKeywordOptions("\nPurge sâu text style/dim style/regapp [Có/Không] <Không>: ");
        deepOpt.Keywords.Add("Có");
        deepOpt.Keywords.Add("Không");
        deepOpt.AllowNone = true;
        var deepResult = ed.GetKeywords(deepOpt);
        var deep = deepResult.Status == PromptStatus.OK && deepResult.StringResult == "Có";

        var config = new CleanupConfig
        {
            DryRun = isDryRun,
            RemoveEmptyLayers = true,
            PurgeUnusedBlocks = true,
            PurgeUnusedLinetypes = true,
            PurgeUnusedTextStyles = deep,
            PurgeUnusedDimStyles = deep,
            PurgeRegApps = deep,
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
    // Lệnh 5: Xuất attribute ra CSV
    // ──────────────────────────────────────────────
    [CommandMethod("DHCB_ATTR_EXPORT", CommandFlags.Modal)]
    public void AttributeExport()
    {
        var doc = Application.DocumentManager.MdiActiveDocument;
        if (doc is null) return;

        var ed = doc.Editor;

        var blockOpt = new PromptStringOptions("\nTên Block cần xuất [Enter = mọi block có attribute]: ")
        {
            AllowSpaces = false,
            DefaultValue = string.Empty
        };
        var blockResult = ed.GetString(blockOpt);
        if (blockResult.Status != PromptStatus.OK) return;

        var outputOpt = new PromptStringOptions("\nĐường dẫn file CSV xuất ra [Enter = Desktop\\dhcb_attributes.csv]: ")
        {
            AllowSpaces = true,
            DefaultValue = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "dhcb_attributes.csv")
        };
        var outputResult = ed.GetString(outputOpt);
        if (outputResult.Status != PromptStatus.OK) return;

        var config = new AttributeExportConfig
        {
            BlockName = string.IsNullOrWhiteSpace(blockResult.StringResult) ? null : blockResult.StringResult,
            OutputPath = string.IsNullOrWhiteSpace(outputResult.StringResult) ? outputOpt.DefaultValue : outputResult.StringResult,
        };

        var command = new AttributeExportCommand();
        var result = command.Execute(doc.Database, config);

        PrintResult(ed, result);
    }

    // ──────────────────────────────────────────────
    // Lệnh 6: Nhập attribute từ CSV
    // ──────────────────────────────────────────────
    [CommandMethod("DHCB_ATTR_IMPORT", CommandFlags.Modal)]
    public void AttributeImport()
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

        var config = new AttributeImportConfig
        {
            InputPath = inputResult.StringResult,
            DryRun = isDryRun,
        };

        var command = new AttributeImportCommand();
        var result = command.Execute(doc.Database, config);

        PrintResult(ed, result);
    }

    // ──────────────────────────────────────────────
    // Lệnh 7: Tìm/thay văn bản hàng loạt
    // ──────────────────────────────────────────────
    [CommandMethod("DHCB_TEXT_REPLACE", CommandFlags.Modal)]
    public void TextReplace()
    {
        var doc = Application.DocumentManager.MdiActiveDocument;
        if (doc is null) return;

        var ed = doc.Editor;

        var findOpt = new PromptStringOptions("\nChuỗi/regex cần tìm: ") { AllowSpaces = true };
        var findResult = ed.GetString(findOpt);
        if (findResult.Status != PromptStatus.OK || string.IsNullOrWhiteSpace(findResult.StringResult)) return;

        var replaceOpt = new PromptStringOptions("\nThay bằng [Enter = rỗng]: ") { AllowSpaces = true, DefaultValue = string.Empty };
        var replaceResult = ed.GetString(replaceOpt);
        if (replaceResult.Status != PromptStatus.OK) return;

        var regexOpt = new PromptKeywordOptions("\nDùng regex? [Co/Khong] <Khong>: ");
        regexOpt.Keywords.Add("Co");
        regexOpt.Keywords.Add("Khong");
        regexOpt.AllowNone = true;
        var regexResult = ed.GetKeywords(regexOpt);
        var useRegex = regexResult.Status == PromptStatus.OK && regexResult.StringResult == "Co";

        var dryOpt = new PromptKeywordOptions("\nChế độ [Xemtrước/Thật] <Xemtrước>: ");
        dryOpt.Keywords.Add("Xemtrước");
        dryOpt.Keywords.Add("Thật");
        dryOpt.AllowNone = true;
        var dryResult = ed.GetKeywords(dryOpt);
        var isDryRun = dryResult.Status != PromptStatus.OK || dryResult.StringResult != "Thật";

        var config = new TextReplaceConfig
        {
            Find = findResult.StringResult,
            Replace = replaceResult.StringResult ?? string.Empty,
            UseRegex = useRegex,
            DryRun = isDryRun,
        };

        var command = new TextReplaceCommand();
        var result = command.Execute(doc.Database, config);

        PrintResult(ed, result);
    }

    // ──────────────────────────────────────────────
    // Lệnh 8: Kiểm tra chuẩn layer → HTML
    // ──────────────────────────────────────────────
    [CommandMethod("DHCB_LAYER_CHECK", CommandFlags.Modal)]
    public void LayerStandardCheck()
    {
        var doc = Application.DocumentManager.MdiActiveDocument;
        if (doc is null) return;

        var ed = doc.Editor;

        var rulesOpt = new PromptStringOptions("\nĐường dẫn file JSON quy tắc: ") { AllowSpaces = true };
        var rulesResult = ed.GetString(rulesOpt);
        if (rulesResult.Status != PromptStatus.OK || string.IsNullOrWhiteSpace(rulesResult.StringResult)) return;

        var outputOpt = new PromptStringOptions("\nĐường dẫn file HTML xuất ra [Enter = Desktop\\dhcb_layer_check.html]: ")
        {
            AllowSpaces = true,
            DefaultValue = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "dhcb_layer_check.html")
        };
        var outputResult = ed.GetString(outputOpt);
        if (outputResult.Status != PromptStatus.OK) return;

        var config = new LayerStandardCheckConfig
        {
            RulesPath = rulesResult.StringResult,
            OutputPath = string.IsNullOrWhiteSpace(outputResult.StringResult) ? outputOpt.DefaultValue : outputResult.StringResult,
        };

        var command = new LayerStandardCheckCommand();
        var result = command.Execute(doc.Database, config);

        PrintResult(ed, result);
    }

    // ──────────────────────────────────────────────
    // Lệnh 9: Trích trục từ layer AXIS
    // ──────────────────────────────────────────────
    [CommandMethod("DHCB_GRID_EXTRACT", CommandFlags.Modal)]
    public void GridExtract()
    {
        var doc = Application.DocumentManager.MdiActiveDocument;
        if (doc is null) return;

        var ed = doc.Editor;

        var layerOpt = new PromptStringOptions("\nTên layer trục [Enter = AXIS]: ") { AllowSpaces = false, DefaultValue = "AXIS" };
        var layerResult = ed.GetString(layerOpt);
        if (layerResult.Status != PromptStatus.OK) return;

        var outputOpt = new PromptStringOptions("\nĐường dẫn file CSV xuất ra [Enter = Desktop\\dhcb_grids.csv]: ")
        {
            AllowSpaces = true,
            DefaultValue = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "dhcb_grids.csv")
        };
        var outputResult = ed.GetString(outputOpt);
        if (outputResult.Status != PromptStatus.OK) return;

        var config = new GridExtractConfig
        {
            GridLayer = string.IsNullOrWhiteSpace(layerResult.StringResult) ? "AXIS" : layerResult.StringResult,
            OutputPath = string.IsNullOrWhiteSpace(outputResult.StringResult) ? outputOpt.DefaultValue : outputResult.StringResult,
        };

        var command = new GridExtractCommand();
        var result = command.Execute(doc.Database, config);

        PrintResult(ed, result);
    }

    // ──────────────────────────────────────────────
    // Lệnh 10: Kiểm tra Xref
    // ──────────────────────────────────────────────
    [CommandMethod("DHCB_XREF_AUDIT", CommandFlags.Modal)]
    public void XrefAudit()
    {
        var doc = Application.DocumentManager.MdiActiveDocument;
        if (doc is null) return;

        var ed = doc.Editor;

        var outputOpt = new PromptStringOptions("\nĐường dẫn file CSV xuất ra [Enter = không ghi file, chỉ xem trong lệnh]: ")
        {
            AllowSpaces = true,
            DefaultValue = string.Empty
        };
        var outputResult = ed.GetString(outputOpt);
        if (outputResult.Status != PromptStatus.OK) return;

        var config = new XrefAuditConfig
        {
            OutputPath = string.IsNullOrWhiteSpace(outputResult.StringResult) ? null : outputResult.StringResult,
        };

        var command = new XrefAuditCommand();
        var result = command.Execute(doc.Database, config);

        PrintResult(ed, result);
    }

    // ──────────────────────────────────────────────
    // Lệnh 11: Chuyển layer theo bảng map (LAYTRANS)
    // ──────────────────────────────────────────────
    [CommandMethod("DHCB_LAYER_TRANSLATE", CommandFlags.Modal)]
    public void LayerTranslate()
    {
        var doc = Application.DocumentManager.MdiActiveDocument;
        if (doc is null) return;

        var ed = doc.Editor;

        var mapOpt = new PromptStringOptions("\nĐường dẫn file CSV bảng map (Source,Target,...): ") { AllowSpaces = true };
        var mapResult = ed.GetString(mapOpt);
        if (mapResult.Status != PromptStatus.OK || string.IsNullOrWhiteSpace(mapResult.StringResult)) return;

        var deleteOpt = new PromptKeywordOptions("\nXoá layer nguồn rỗng sau khi chuyển? [Co/Khong] <Khong>: ");
        deleteOpt.Keywords.Add("Co");
        deleteOpt.Keywords.Add("Khong");
        deleteOpt.AllowNone = true;
        var deleteResult = ed.GetKeywords(deleteOpt);
        var deleteEmptySource = deleteResult.Status == PromptStatus.OK && deleteResult.StringResult == "Co";

        var dryOpt = new PromptKeywordOptions("\nChế độ [Xemtrước/Thật] <Xemtrước>: ");
        dryOpt.Keywords.Add("Xemtrước");
        dryOpt.Keywords.Add("Thật");
        dryOpt.AllowNone = true;
        var dryResult = ed.GetKeywords(dryOpt);
        var isDryRun = dryResult.Status != PromptStatus.OK || dryResult.StringResult != "Thật";

        var config = new LayerTranslateConfig
        {
            MapCsvPath = mapResult.StringResult,
            DeleteEmptySource = deleteEmptySource,
            DryRun = isDryRun,
        };

        var command = new LayerTranslateCommand();
        var result = command.Execute(doc.Database, config);

        PrintResult(ed, result);
    }

    // ──────────────────────────────────────────────
    // Lệnh 12: So sánh bản vẽ (mức layer)
    // ──────────────────────────────────────────────
    [CommandMethod("DHCB_DRAWING_COMPARE", CommandFlags.Modal)]
    public void DrawingCompare()
    {
        var doc = Application.DocumentManager.MdiActiveDocument;
        if (doc is null) return;

        var ed = doc.Editor;

        var otherOpt = new PromptStringOptions("\nĐường dẫn file DWG khác để so sánh: ") { AllowSpaces = true };
        var otherResult = ed.GetString(otherOpt);
        if (otherResult.Status != PromptStatus.OK || string.IsNullOrWhiteSpace(otherResult.StringResult)) return;

        var outputOpt = new PromptStringOptions("\nĐường dẫn file báo cáo (.csv hoặc .html) [Enter = Desktop\\dhcb_compare.csv]: ")
        {
            AllowSpaces = true,
            DefaultValue = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "dhcb_compare.csv")
        };
        var outputResult = ed.GetString(outputOpt);
        if (outputResult.Status != PromptStatus.OK) return;

        var config = new DrawingCompareConfig
        {
            OtherPath = otherResult.StringResult,
            OutputPath = string.IsNullOrWhiteSpace(outputResult.StringResult) ? outputOpt.DefaultValue : outputResult.StringResult,
            MoveToleranceMm = 0,
        };

        var command = new DrawingCompareCommand();
        var result = command.Execute(doc.Database, config);

        PrintResult(ed, result);
    }

    // ──────────────────────────────────────────────
    // Lệnh 13: Thống kê Block (BOM)
    // ──────────────────────────────────────────────
    [CommandMethod("DHCB_BLOCK_QUANTITY", CommandFlags.Modal)]
    public void BlockQuantity()
    {
        var doc = Application.DocumentManager.MdiActiveDocument;
        if (doc is null) return;

        var ed = doc.Editor;

        var filterOpt = new PromptStringOptions("\nTên block chứa chuỗi [Enter = mọi block]: ")
        {
            AllowSpaces = false,
            DefaultValue = string.Empty
        };
        var filterResult = ed.GetString(filterOpt);
        if (filterResult.Status != PromptStatus.OK) return;

        var groupOpt = new PromptStringOptions("\nTag attribute để nhóm [Enter = không nhóm]: ")
        {
            AllowSpaces = false,
            DefaultValue = string.Empty
        };
        var groupResult = ed.GetString(groupOpt);
        if (groupResult.Status != PromptStatus.OK) return;

        var outputOpt = new PromptStringOptions("\nĐường dẫn file CSV xuất ra [Enter = Desktop\\dhcb_block_bom.csv]: ")
        {
            AllowSpaces = true,
            DefaultValue = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "dhcb_block_bom.csv")
        };
        var outputResult = ed.GetString(outputOpt);
        if (outputResult.Status != PromptStatus.OK) return;

        var config = new BlockQuantityConfig
        {
            BlockNameContains = string.IsNullOrWhiteSpace(filterResult.StringResult) ? null : filterResult.StringResult,
            GroupByAttribute = string.IsNullOrWhiteSpace(groupResult.StringResult) ? null : groupResult.StringResult,
            OutputPath = string.IsNullOrWhiteSpace(outputResult.StringResult) ? outputOpt.DefaultValue : outputResult.StringResult,
        };

        var command = new BlockQuantityCommand();
        var result = command.Execute(doc.Database, config);

        PrintResult(ed, result);
    }

    // ──────────────────────────────────────────────
    // Lệnh 14: Gán attribute tăng dần theo mẫu (BATTE)
    // ──────────────────────────────────────────────
    [CommandMethod("DHCB_ATTR_INCREMENT", CommandFlags.Modal)]
    public void AttributeIncrement()
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

        var patternOpt = new PromptStringOptions("\nMẫu [Enter = P-{n:000}]: ")
        {
            AllowSpaces = false,
            DefaultValue = "P-{n:000}"
        };
        var patternResult = ed.GetString(patternOpt);
        if (patternResult.Status != PromptStatus.OK) return;

        var startOpt = new PromptIntegerOptions("\nSố bắt đầu [Enter = 1]: ") { DefaultValue = 1, AllowNegative = false };
        var startResult = ed.GetInteger(startOpt);

        var dryOpt = new PromptKeywordOptions("\nChế độ [Xemtrước/Thật] <Xemtrước>: ");
        dryOpt.Keywords.Add("Xemtrước");
        dryOpt.Keywords.Add("Thật");
        dryOpt.AllowNone = true;
        var dryResult = ed.GetKeywords(dryOpt);
        var isDryRun = dryResult.Status != PromptStatus.OK || dryResult.StringResult != "Thật";

        var config = new AttributeIncrementConfig
        {
            BlockName = blockResult.StringResult,
            AttributeTag = string.IsNullOrWhiteSpace(attrResult.StringResult) ? "MARK" : attrResult.StringResult,
            Pattern = string.IsNullOrWhiteSpace(patternResult.StringResult) ? "P-{n:000}" : patternResult.StringResult,
            StartNumber = startResult.Status == PromptStatus.OK ? startResult.Value : 1,
            DryRun = isDryRun,
        };

        var command = new AttributeIncrementCommand();
        var result = command.Execute(doc.Database, config);

        PrintResult(ed, result);
    }

    // ──────────────────────────────────────────────
    // Lệnh 15: Gợi ý map layer CAD → Revit type
    // ──────────────────────────────────────────────
    [CommandMethod("DHCB_LAYER_MAP", CommandFlags.Modal)]
    public void CadLayerMap()
    {
        var doc = Application.DocumentManager.MdiActiveDocument;
        if (doc is null) return;

        var ed = doc.Editor;

        var typesOpt = new PromptStringOptions("\nĐường dẫn file .txt danh sách Revit type: ") { AllowSpaces = true };
        var typesResult = ed.GetString(typesOpt);
        if (typesResult.Status != PromptStatus.OK || string.IsNullOrWhiteSpace(typesResult.StringResult)) return;

        var outputOpt = new PromptStringOptions("\nĐường dẫn file CSV mapping xuất ra [Enter = Desktop\\dhcb_layer_map.csv]: ")
        {
            AllowSpaces = true,
            DefaultValue = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "dhcb_layer_map.csv")
        };
        var outputResult = ed.GetString(outputOpt);
        if (outputResult.Status != PromptStatus.OK) return;

        var ollamaOpt = new PromptKeywordOptions("\nDùng model local (Ollama) nếu có? [Co/Khong] <Khong>: ");
        ollamaOpt.Keywords.Add("Co");
        ollamaOpt.Keywords.Add("Khong");
        ollamaOpt.AllowNone = true;
        var ollamaResult = ed.GetKeywords(ollamaOpt);
        var useOllama = ollamaResult.Status == PromptStatus.OK && ollamaResult.StringResult == "Co";

        var config = new CadLayerMapConfig
        {
            RevitTypesPath = typesResult.StringResult,
            OutputPath = string.IsNullOrWhiteSpace(outputResult.StringResult) ? outputOpt.DefaultValue : outputResult.StringResult,
            UseOllama = useOllama,
        };

        var command = new CadLayerMapCommand();
        var result = command.Execute(doc.Database, config);

        PrintResult(ed, result);
    }

    // ──────────────────────────────────────────────
    // Helper
    // ──────────────────────────────────────────────
    private static void PrintResult(Editor ed, DhcbTools.Shared.Hosting.CommandResult result)
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
