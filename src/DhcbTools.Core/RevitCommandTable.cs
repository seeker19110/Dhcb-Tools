using Autodesk.Revit.DB;
using DhcbTools.Core.Ai;
using DhcbTools.Core.AutoNumbering;
using DhcbTools.Core.Checks;
using DhcbTools.Core.Export;
using DhcbTools.Core.Health;
using DhcbTools.Core.MEPF;
using DhcbTools.Core.ModelCleanup;
using DhcbTools.Core.ParameterSync;
using DhcbTools.Core.ProjectInit;
using DhcbTools.Core.Sheets;
using DhcbTools.Core.Styles;
using DhcbTools.Shared.Logic.Ai;
using Newtonsoft.Json;

namespace DhcbTools.Core;

/// <summary>
/// Bảng tra lệnh Revit theo tên (khoá = <c>ICoreCommand.CommandName</c>, nhận cả bí danh trong
/// <see cref="CommandCatalog"/>). Dùng chung cho HTTP Bridge, batch runner và lớp AI — một chỗ duy nhất,
/// test <c>CommandCatalogTests</c> đối chiếu file này với catalog.
/// </summary>
public static class RevitCommandTable
{
    public static CommandResult Dispatch(Document doc, string command, string configJson)
    {
        var descriptor = CommandCatalog.Find(CommandCatalog.Revit, command);
        var name = (descriptor?.Name ?? command).ToUpperInvariant();

        return name switch
        {
            "PARAMETEREXPORT" => new ParameterExportCommand().Execute(doc, Deserialize<ParameterExportConfig>(configJson)),
            "PARAMETERIMPORT" => new ParameterImportCommand().Execute(doc, Deserialize<ParameterImportConfig>(configJson)),
            "REMOVEUNUSEDVIEWS" => new RemoveUnusedViewsCommand().Execute(doc, Deserialize<CleanupConfig>(configJson)),
            "AUTONUMBERING" => new AutoNumberingCommand().Execute(doc, Deserialize<AutoNumberingConfig>(configJson)),
            "BATCHEXPORT" => new BatchExportCommand().Execute(doc, Deserialize<ExportConfig>(configJson)),
            "HEALTHREPORT" => new HealthReportCommand().Execute(doc, Deserialize<HealthReportConfig>(configJson)),
            "PROJECTINFO" => new ProjectInfoCommand().Execute(doc, Deserialize<ProjectInfoConfig>(configJson)),
            "LEVELSETUP" => new LevelSetupCommand().Execute(doc, Deserialize<LevelSetupConfig>(configJson)),
            "GRIDSETUP" => new GridSetupCommand().Execute(doc, Deserialize<GridSetupConfig>(configJson)),
            "FAMILYLOADER" => new FamilyLoaderCommand().Execute(doc, Deserialize<FamilyLoaderConfig>(configJson)),

            "SLEEVEAUTO" => new SleeveCommand().Execute(doc, Deserialize<SleeveConfig>(configJson)),
            "ELEVATIONTAG" => new ElevationTagCommand().Execute(doc, Deserialize<ElevationTagConfig>(configJson)),
            "HANGERAUTO" => new HangerCommand().Execute(doc, Deserialize<HangerConfig>(configJson)),
            "PIPESPLITTER" => new PipeSplitterCommand().Execute(doc, Deserialize<PipeSplitterConfig>(configJson)),
            "CONNECTORCHECKER" => new ConnectorCheckerCommand().Execute(doc, Deserialize<ConnectorCheckerConfig>(configJson)),
            "ROUTEFROMLINES" => new RouteFromLinesCommand().Execute(doc, Deserialize<RouteFromLinesConfig>(configJson)),
            "DEVICEPLACEMENT" => new DevicePlacementCommand().Execute(doc, Deserialize<DevicePlacementConfig>(configJson)),
            "SIZINGPROPOSAL" => new SizingProposalCommand().Execute(doc, Deserialize<SizingProposalConfig>(configJson)),
            "APPLYSIZING" => new ApplySizingCommand().Execute(doc, Deserialize<ApplySizingConfig>(configJson)),
            "SYSTEMCOLOR" => new SystemColorCommand().Execute(doc, Deserialize<SystemColorConfig>(configJson)),
            "SYSTEMNAME" => new SystemNameCommand().Execute(doc, Deserialize<SystemNameConfig>(configJson)),
            "FLOWNUMBERING" => new FlowNumberingCommand().Execute(doc, Deserialize<FlowNumberingConfig>(configJson)),

            "PROJECTFROMTEMPLATE" => new ProjectFromTemplateCommand().Execute(doc, Deserialize<ProjectFromTemplateConfig>(configJson)),
            "TRANSFERSTANDARDS" => new TransferStandardsCommand().Execute(doc, Deserialize<TransferStandardsConfig>(configJson)),
            "GRIDFROMCSV" => new GridFromCsvCommand().Execute(doc, Deserialize<GridFromCsvConfig>(configJson)),
            "SHEETBATCHCREATE" => new SheetBatchCreateCommand().Execute(doc, Deserialize<SheetBatchCreateConfig>(configJson)),

            "SHEETRENAME" => new SheetRenameCommand().Execute(doc, Deserialize<SheetRenameConfig>(configJson)),
            "REVISIONONSHEETS" => new RevisionOnSheetsCommand().Execute(doc, Deserialize<RevisionOnSheetsConfig>(configJson)),
            "WARNINGSEXPORT" => new WarningsExportCommand().Execute(doc, Deserialize<WarningsExportConfig>(configJson)),
            "STYLEPURGE" => new StylePurgeCommand().Execute(doc, Deserialize<StylePurgeConfig>(configJson)),
            "COLORBYPARAMETER" => new ColorByParameterCommand().Execute(doc, Deserialize<ColorByParameterConfig>(configJson)),
            "FAMILYAUDIT" => new FamilyAuditCommand().Execute(doc, Deserialize<FamilyAuditConfig>(configJson)),

            "SLOPEPIPES" => new SlopePipesCommand().Execute(doc, Deserialize<SlopePipesConfig>(configJson)),
            "PIPEKICK" => new PipeKickCommand().Execute(doc, Deserialize<PipeKickConfig>(configJson)),
            "SYSTEMBOM" => new SystemBomCommand().Execute(doc, Deserialize<SystemBomConfig>(configJson)),
            "AUTOROUTE" => new AutoRouteCommand().Execute(doc, Deserialize<AutoRouteConfig>(configJson)),
            "SCHEDULEEXPORT" => new ScheduleExportCommand().Execute(doc, Deserialize<ScheduleExportConfig>(configJson)),
            "VIEWPORTCOPY" => new ViewportCopyCommand().Execute(doc, Deserialize<ViewportCopyConfig>(configJson)),

            "PARAMETERRULECHECK" => new ParameterRuleCheckCommand().Execute(doc, Deserialize<ParameterRuleCheckConfig>(configJson)),
            "CLASHDETECTION" => new ClashDetectionCommand().Execute(doc, Deserialize<ClashDetectionConfig>(configJson)),

            "CADLAYERMAP" => new CadLayerMapCommand().Execute(doc, Deserialize<CadLayerMapConfig>(configJson)),
            "SPECTOCONFIG" => new SpecToConfigCommand().Execute(doc, Deserialize<SpecToConfigConfig>(configJson)),

            _ => CommandResult.Fail($"Lệnh không xác định: \"{command}\". Hợp lệ: {string.Join(", ", CommandCatalog.Names(CommandCatalog.Revit))}."),
        };
    }

    private static T Deserialize<T>(string json)
    {
        try
        {
            var result = JsonConvert.DeserializeObject<T>(string.IsNullOrWhiteSpace(json) ? "{}" : json);
            if (result is null)
            {
                throw new InvalidOperationException($"Không thể deserialize config thành {typeof(T).Name}.");
            }
            return result;
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Config cho {typeof(T).Name} không hợp lệ: {ex.Message}", ex);
        }
    }
}
