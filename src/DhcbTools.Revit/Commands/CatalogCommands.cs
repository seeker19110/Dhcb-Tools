using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using DhcbTools.Core;
using Newtonsoft.Json.Linq;

namespace DhcbTools.Revit.Commands;

// Các nút Ribbon config-driven: mỗi class một dòng, toàn bộ luồng (config → xem trước → xác nhận → chạy) ở CommandRunner.
// Tên lệnh truyền vào đúng bằng CommandName của Core (khoá tra cứu của Bridge và batch runner — mục 0.3).

[Transaction(TransactionMode.Manual)] [Regeneration(RegenerationOption.Manual)]
public sealed class HangerAutoCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData c, ref string m, ElementSet e) => CommandRunner.Run(c, "HangerAuto",
        new JObject { ["hangerFamilyName"] = "<tên FamilySymbol hanger>", ["spacingMm"] = 3000, ["offsetMm"] = 200, ["dryRun"] = true });
}

[Transaction(TransactionMode.Manual)] [Regeneration(RegenerationOption.Manual)]
public sealed class PipeSplitterCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData c, ref string m, ElementSet e) => CommandRunner.Run(c, "PipeSplitter",
        new JObject { ["maxSegmentMm"] = 6000, ["categories"] = new JArray("Pipe", "Duct"), ["dryRun"] = true });
}

[Transaction(TransactionMode.Manual)] [Regeneration(RegenerationOption.Manual)]
public sealed class RouteFromLinesCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData c, ref string m, ElementSet e) => CommandRunner.Run(c, "RouteFromLines",
        new JObject
        {
            ["lineStyleName"] = "DHCB-Route", ["elementType"] = "Duct", ["typeName"] = "<Duct type>", ["systemType"] = "Supply Air",
            ["sizeMm"] = new JObject { ["width"] = 400, ["height"] = 200 }, ["offsetMm"] = 3200, ["connectToNearestMm"] = 300, ["dryRun"] = true,
        });
}

[Transaction(TransactionMode.Manual)] [Regeneration(RegenerationOption.Manual)]
public sealed class DevicePlacementCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData c, ref string m, ElementSet e) => CommandRunner.Run(c, "DevicePlacement",
        new JObject
        {
            ["deviceFamily"] = "<Family: Type>", ["roomFilter"] = new JObject { ["levelName"] = "", ["nameContains"] = "" },
            ["pattern"] = new JObject { ["type"] = "grid", ["spacingXMm"] = 3000, ["spacingYMm"] = 3000, ["marginMm"] = 1500 },
            ["maxCoverageRadiusMm"] = 2300, ["dryRun"] = true,
        });
}

[Transaction(TransactionMode.Manual)] [Regeneration(RegenerationOption.Manual)]
public sealed class SizingProposalCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData c, ref string m, ElementSet e) => CommandRunner.Run(c, "SizingProposal",
        new JObject { ["outputPath"] = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "DHCB_Sizing.csv"), ["maxPaPerM"] = 1.0, ["maxDuctVelocityMs"] = 8.0, ["maxPipeVelocityMs"] = 2.0 });
}

[Transaction(TransactionMode.Manual)] [Regeneration(RegenerationOption.Manual)]
public sealed class ApplySizingCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData c, ref string m, ElementSet e) => CommandRunner.Run(c, "ApplySizing",
        new JObject { ["inputPath"] = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "DHCB_Sizing.csv"), ["dryRun"] = true });
}

[Transaction(TransactionMode.Manual)] [Regeneration(RegenerationOption.Manual)]
public sealed class SystemColorCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData c, ref string m, ElementSet e) => CommandRunner.Run(c, "SystemColor",
        new JObject { ["colors"] = new JObject { ["Supply Air"] = "#0070C0", ["Return Air"] = "#FF00FF", ["Exhaust Air"] = "#7F7F00", ["Domestic Cold Water"] = "#00B0F0", ["Sprinkler"] = "#FF0000" }, ["viewTemplateName"] = "<view template>", ["dryRun"] = true });
}

[Transaction(TransactionMode.Manual)] [Regeneration(RegenerationOption.Manual)]
public sealed class SystemNameCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData c, ref string m, ElementSet e) => CommandRunner.Run(c, "SystemName",
        new JObject { ["discipline"] = "MEC", ["zone"] = "", ["padWidth"] = 2, ["onlyDefaultNames"] = true, ["dryRun"] = true });
}

[Transaction(TransactionMode.Manual)] [Regeneration(RegenerationOption.Manual)]
public sealed class FlowNumberingCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData c, ref string m, ElementSet e)
    {
        // Nguồn = phần tử đang chọn nếu có, để không phải tra ElementId tay.
        var uidoc = c.Application.ActiveUIDocument;
        var selected = uidoc?.Selection.GetElementIds().FirstOrDefault();
        var defaults = new JObject { ["sourceElementId"] = selected is null ? 0 : Core.RevitCompat.IdValue(selected), ["parameterName"] = "Mark", ["prefix"] = "", ["padWidth"] = 2, ["dryRun"] = true };
        if (selected is not null)
        {
            var path = ConfigStore.PathFor("FlowNumbering");
            var existing = File.Exists(path) ? ConfigStore.Load("FlowNumbering") : null;
            if (existing is not null)
            {
                existing["sourceElementId"] = Core.RevitCompat.IdValue(selected);
                File.WriteAllText(path, existing.ToString(Newtonsoft.Json.Formatting.Indented));
            }
        }
        return CommandRunner.Run(c, "FlowNumbering", defaults);
    }
}

[Transaction(TransactionMode.Manual)] [Regeneration(RegenerationOption.Manual)]
public sealed class ProjectFromTemplateCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData c, ref string m, ElementSet e) => CommandRunner.Run(c, "ProjectFromTemplate",
        new JObject { ["templatePath"] = "<P:/Standards/DHCB_ARC.rte>", ["outputPath"] = "<P:/{projectCode}/{projectCode}-{discipline}-R{revitVersion}.rvt>", ["projectCode"] = "PRJ", ["discipline"] = "ARC", ["createCentral"] = true, ["worksets"] = new JArray("Shared Levels and Grids", "Kiến trúc", "Kết cấu", "MEP", "Liên kết CAD"), ["dryRun"] = true });
}

[Transaction(TransactionMode.Manual)] [Regeneration(RegenerationOption.Manual)]
public sealed class TransferStandardsCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData c, ref string m, ElementSet e) => CommandRunner.Run(c, "TransferStandards",
        new JObject { ["sourcePath"] = "<P:/Standards/DHCB_Standards.rvt>", ["categories"] = new JArray("ViewTemplates", "Filters", "Materials", "TextTypes", "DimensionTypes"), ["dryRun"] = true });
}

[Transaction(TransactionMode.Manual)] [Regeneration(RegenerationOption.Manual)]
public sealed class GridFromCsvCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData c, ref string m, ElementSet e) => CommandRunner.Run(c, "GridFromCsv",
        new JObject { ["gridCsvPath"] = "<dhcb_grids.csv từ AutoCAD DHCB_GRID_EXTRACT hoặc Excel>", ["levelCsvPath"] = "", ["renameByRule"] = false, ["dryRun"] = true });
}

[Transaction(TransactionMode.Manual)] [Regeneration(RegenerationOption.Manual)]
public sealed class SheetBatchCreateCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData c, ref string m, ElementSet e) => CommandRunner.Run(c, "SheetBatchCreate",
        new JObject { ["inputPath"] = "<sheets.csv: SheetNumber,SheetName,TitleBlockType,ViewsToPlace>", ["placement"] = "center", ["dryRun"] = true });
}

[Transaction(TransactionMode.Manual)] [Regeneration(RegenerationOption.Manual)]
public sealed class ParameterRuleCheckCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData c, ref string m, ElementSet e) => CommandRunner.Run(c, "ParameterRuleCheck",
        new JObject { ["rulesPath"] = Path.Combine(ConfigStore.Directory, "parameter-rules.json"), ["outputPath"] = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "DHCB_RuleCheck.html"), ["create3dView"] = true });
}

[Transaction(TransactionMode.Manual)] [Regeneration(RegenerationOption.Manual)]
public sealed class ClashDetectionCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData c, ref string m, ElementSet e) => CommandRunner.Run(c, "ClashDetection",
        new JObject { ["categoriesA"] = new JArray("Ducts", "Pipes"), ["categoriesB"] = new JArray("Structural Framing", "Structural Columns"), ["outputPath"] = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "DHCB_Clash.html"), ["acceptedPath"] = Path.Combine(ConfigStore.Directory, "clash-accepted.json"), ["create3dView"] = true });
}

[Transaction(TransactionMode.Manual)] [Regeneration(RegenerationOption.Manual)]
public sealed class CadLayerMapCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData c, ref string m, ElementSet e) => CommandRunner.Run(c, "CadLayerMap",
        new JObject { ["layersCsvPath"] = "<dhcb_layers.csv từ AutoCAD DHCB_LAYER_EXPORT>", ["outputPath"] = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "DHCB_LayerMap.csv"), ["useOllama"] = false });
}

[Transaction(TransactionMode.Manual)] [Regeneration(RegenerationOption.Manual)]
public sealed class SpecToConfigCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData c, ref string m, ElementSet e) => CommandRunner.Run(c, "SpecToConfig",
        new JObject { ["inputPath"] = "<thuyet-minh.txt>", ["outputPath"] = Path.Combine(ConfigStore.Directory, "project-init-from-spec.json") });
}

// ── Giai đoạn 7: hồ sơ & style (học từ pyRevit/DiRoots/Ideate/Colour Splasher) ──────────────────

[Transaction(TransactionMode.Manual)] [Regeneration(RegenerationOption.Manual)]
public sealed class SheetRenameCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData c, ref string m, ElementSet e) => CommandRunner.Run(c, "SheetRename",
        new JObject { ["target"] = "Sheets", ["numberPattern"] = "", ["namePattern"] = "", ["find"] = "", ["replace"] = "", ["filterContains"] = "", ["orderBy"] = "Number", ["counterStart"] = 1, ["dryRun"] = true });
}

[Transaction(TransactionMode.Manual)] [Regeneration(RegenerationOption.Manual)]
public sealed class RevisionOnSheetsCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData c, ref string m, ElementSet e) => CommandRunner.Run(c, "RevisionOnSheets",
        new JObject { ["revisionSequence"] = 1, ["sheetNumberContains"] = "", ["sheetNumbers"] = new JArray(), ["remove"] = false, ["dryRun"] = true });
}

[Transaction(TransactionMode.Manual)] [Regeneration(RegenerationOption.Manual)]
public sealed class StylePurgeCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData c, ref string m, ElementSet e) => CommandRunner.Run(c, "StylePurge",
        new JObject { ["kinds"] = new JArray("ViewTemplates", "Filters", "LinePatterns", "FillPatterns", "TextTypes", "DimensionTypes"), ["keepNameContains"] = new JArray("DHCB"), ["dryRun"] = true });
}

[Transaction(TransactionMode.Manual)] [Regeneration(RegenerationOption.Manual)]
public sealed class ColorByParameterCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData c, ref string m, ElementSet e) => CommandRunner.Run(c, "ColorByParameter",
        new JObject { ["viewName"] = "", ["categories"] = new JArray(), ["parameterName"] = "<tên tham số, ví dụ Fire Rating>", ["fixedColors"] = new JObject(), ["legendCsvPath"] = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "DHCB_ColorLegend.csv"), ["reset"] = false, ["dryRun"] = true });
}

[Transaction(TransactionMode.Manual)] [Regeneration(RegenerationOption.Manual)]
public sealed class FamilyAuditCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData c, ref string m, ElementSet e) => CommandRunner.Run(c, "FamilyAudit",
        new JObject { ["outputPath"] = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "DHCB_Families.csv"), ["renamePattern"] = "", ["find"] = "", ["replace"] = "", ["categories"] = new JArray(), ["dryRun"] = true });
}

[Transaction(TransactionMode.Manual)] [Regeneration(RegenerationOption.Manual)]
public sealed class WarningsExportCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData c, ref string m, ElementSet e) => CommandRunner.Run(c, "WarningsExport",
        new JObject { ["outputPath"] = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "DHCB_Warnings.csv") });
}

// ── P2 giai đoạn 7 ────────────────────────────────────────────────────────────────────────────────

[Transaction(TransactionMode.Manual)] [Regeneration(RegenerationOption.Manual)]
public sealed class SlopePipesCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData c, ref string m, ElementSet e) => CommandRunner.Run(c, "SlopePipes",
        new JObject { ["slopePercent"] = null, ["systemContains"] = "Sanitary", ["levelName"] = "", ["lowerEnd"] = "End", ["checkOnly"] = false, ["dryRun"] = true });
}

[Transaction(TransactionMode.Manual)] [Regeneration(RegenerationOption.Manual)]
public sealed class PipeKickCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData c, ref string m, ElementSet e)
    {
        var sel = c.Application.ActiveUIDocument.Selection.GetElementIds().FirstOrDefault();
        return CommandRunner.Run(c, "PipeKick",
            new JObject { ["elementId"] = sel != null ? RevitCompat.IdValue(sel).ToString() : "<Id ống>", ["offsetMm"] = 300, ["offsetDirection"] = "Up", ["elbowAngleDeg"] = 45, ["distanceFromStartMm"] = 500, ["dryRun"] = true });
    }
}

[Transaction(TransactionMode.Manual)] [Regeneration(RegenerationOption.Manual)]
public sealed class SystemBomCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData c, ref string m, ElementSet e) => CommandRunner.Run(c, "SystemBom",
        new JObject { ["outputPath"] = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "DHCB_BOM.csv"), ["systemContains"] = "", ["spoolParameter"] = "", ["stockLengthMm"] = 6000, ["wastePercent"] = 5 });
}

[Transaction(TransactionMode.Manual)] [Regeneration(RegenerationOption.Manual)]
public sealed class AutoRouteCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData c, ref string m, ElementSet e) => CommandRunner.Run(c, "AutoRoute",
        new JObject
        {
            ["startMm"] = new JObject { ["x"] = 0, ["y"] = 0, ["z"] = 3200 }, ["endMm"] = new JObject { ["x"] = 12000, ["y"] = 6000, ["z"] = 3200 },
            ["searchMarginMm"] = 3000, ["stepMm"] = 100, ["clearanceMm"] = 100, ["turnPenalty"] = 20, ["allowVertical"] = true,
            ["obstacleCategories"] = new JArray(), ["lineStyleName"] = "DHCB-Route", ["buildRoute"] = false,
            ["routeConfig"] = new JObject { ["elementType"] = "Duct", ["typeName"] = "", ["systemType"] = "Supply Air", ["sizeMm"] = new JObject { ["width"] = 400, ["height"] = 200 } },
            ["dryRun"] = true,
        });
}

[Transaction(TransactionMode.Manual)] [Regeneration(RegenerationOption.Manual)]
public sealed class ScheduleExportCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData c, ref string m, ElementSet e) => CommandRunner.Run(c, "ScheduleExport",
        new JObject { ["outputFolder"] = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "DHCB_Schedules"), ["nameContains"] = "", ["names"] = new JArray(), ["includeHeader"] = true });
}

[Transaction(TransactionMode.Manual)] [Regeneration(RegenerationOption.Manual)]
public sealed class ViewportCopyCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData c, ref string m, ElementSet e) => CommandRunner.Run(c, "ViewportCopy",
        new JObject { ["sourceSheetNumber"] = "A-101", ["targetSheetNumbers"] = new JArray(), ["targetSheetContains"] = "A-1", ["pinAfterCopy"] = true, ["dryRun"] = true });
}

/// <summary>Chạy một job batch ngay trong phiên Revit đang mở (không cần console) — job JSON chọn trong config.</summary>
[Transaction(TransactionMode.Manual)] [Regeneration(RegenerationOption.Manual)]
public sealed class RunBatchJobCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData c, ref string m, ElementSet e)
    {
        var path = Path.Combine(ConfigStore.Directory, "batch-job.json");
        if (!File.Exists(path))
        {
            Directory.CreateDirectory(ConfigStore.Directory);
            File.WriteAllText(path, new Shared.Logic.Batch.BatchJob
            {
                Name = "Job mẫu",
                OutputFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "DHCB", "batch", "{yyyy-MM-dd}"),
                SaveMode = Shared.Logic.Batch.SaveMode.None,
                Files = { new Shared.Logic.Batch.BatchJobFile { Path = "<P:/DuAn/ARC.rvt>" } },
                Steps = { new Shared.Logic.Batch.BatchJobStep { Command = "HealthReport", Config = new JObject { ["outputPath"] = "{outputFolder}/{fileName}-health.html" } } },
            }.ToJson());
            TaskDialog.Show("DHCB - Batch", "Đã tạo job mẫu, sửa rồi bấm lại:\n" + path);
            return Result.Cancelled;
        }

        Shared.Logic.Batch.BatchJob job;
        try
        {
            job = Shared.Logic.Batch.BatchJob.Load(path);
        }
        catch (Exception ex)
        {
            TaskDialog.Show("DHCB - Batch", ex.Message);
            return Result.Failed;
        }

        var confirm = new TaskDialog("DHCB - Batch")
        {
            MainInstruction = $"Chạy job \"{job.Name}\": {job.Files.Count} file × {job.Steps.Count} step, saveMode={job.SaveMode}?",
            MainContent = "Các file sẽ được mở/đóng tự động trong phiên Revit này. Không thao tác trong lúc chạy.",
            CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
        };
        if (confirm.Show() != TaskDialogResult.Yes) return Result.Cancelled;

        var logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "DHCB", "logs", DateTime.Now.ToString("yyyy-MM-dd"));
        var runLog = Path.Combine(logDir, "run.jsonl");
        var runner = new Core.Batch.BatchJobRunner(c.Application.Application);
        var code = runner.Run(job, runLog);
        var entries = Shared.Logic.Batch.RunLog.ReadAll(runLog);
        var report = Path.Combine(logDir, "report.html");
        File.WriteAllText(report, Shared.Logic.Batch.BatchReport.Render(job.Name, entries, DateTime.Now), System.Text.Encoding.UTF8);
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(report) { UseShellExecute = true }); } catch { /* ignore */ }

        TaskDialog.Show("DHCB - Batch", $"Xong, mã thoát {code}. Log: {runLog}");
        return code == 0 ? Result.Succeeded : Result.Failed;
    }
}
