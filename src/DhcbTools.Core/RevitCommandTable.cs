using Autodesk.Revit.DB;
using DhcbTools.Core.Ai;
using DhcbTools.Core.AutoNumbering;
using DhcbTools.Core.Checks;
using DhcbTools.Core.Export;
using DhcbTools.Core.Health;
using DhcbTools.Core.MEPF;
using DhcbTools.Core.ModelCleanup;
using DhcbTools.Core.ParameterSync;
using DhcbTools.Core.Progress;
using DhcbTools.Core.ProjectInit;
using DhcbTools.Core.Sheets;
using DhcbTools.Core.Styles;
using DhcbTools.Core.Testing;
using DhcbTools.Shared.Logic.Ai;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

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

        // Config hỏng/thiếu trường bắt buộc là lỗi của người gọi, không phải sự cố hệ thống:
        // trả CommandResult.Fail có thông báo đọc được thay vì ném stack trace .NET ra Bridge/agent.
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        CommandResult result;
        try
        {
            result = name switch
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
                "SETOUTEXPORT" => new SetoutExportCommand().Execute(doc, Deserialize<SetoutExportConfig>(configJson)),
                "CONSTRUCTIONSTATUS" => new ConstructionStatusCommand().Execute(doc, Deserialize<ConstructionStatusConfig>(configJson)),
                "PROGRESSREPORT" => new ProgressReportCommand().Execute(doc, Deserialize<ProgressReportConfig>(configJson)),

                "PARAMETERRULECHECK" => new ParameterRuleCheckCommand().Execute(doc, Deserialize<ParameterRuleCheckConfig>(configJson)),
                "CLASHDETECTION" => new ClashDetectionCommand().Execute(doc, Deserialize<ClashDetectionConfig>(configJson)),

                "CADLAYERMAP" => new CadLayerMapCommand().Execute(doc, Deserialize<CadLayerMapConfig>(configJson)),
                "DICTIONARYLEARN" => new DictionaryLearnCommand().Execute(doc, Deserialize<DictionaryLearnConfig>(configJson)),
                "SPECTOCONFIG" => new SpecToConfigCommand().Execute(doc, Deserialize<SpecToConfigConfig>(configJson)),
                "USAGEREPORT" => new UsageReportCommand().Execute(doc, Deserialize<UsageReportConfig>(configJson)),
                "RUNTESTS" => new RunTestsCommand().Execute(doc, Deserialize<RunTestsConfig>(configJson)),

                _ => CommandResult.Fail($"Lệnh không xác định: \"{command}\". Hợp lệ: {string.Join(", ", CommandCatalog.Names(CommandCatalog.Revit))}."),
            };
        }
        catch (ConfigException ex)
        {
            result = CommandResult.Fail(ex.Message);
        }

        stopwatch.Stop();
        LogRun("Revit", descriptor?.Name ?? command, result, configJson, stopwatch.ElapsedMilliseconds);
        return result;
    }

    /// <summary>
    /// Tắt ghi số liệu sử dụng — <c>RunTests</c> bật cờ này khi chạy bộ ca kiểm, nếu không hàng chục
    /// lần chạy của bộ test sẽ trộn vào số liệu "lệnh nào kỹ sư dùng thật".
    /// </summary>
    public static bool LogUsage { get; set; } = true;

    /// <summary>
    /// Ghi một dòng số liệu cho MỌI lần chạy lệnh. Đây là chỗ hội tụ duy nhất của cả bốn đường vào
    /// (Ribbon, Bridge, batch, lớp AI), nên đặt ở đây là đủ cho toàn bộ sản phẩm.
    /// <para>
    /// Trước đây log chỉ ghi khi lệnh NÉM exception, nên câu hỏi quyết định giai đoạn 10/11 — "lệnh nào
    /// kỹ sư dùng hằng tuần, lệnh nào bấm rồi bỏ" — không có dữ liệu nào trả lời được ngoài trí nhớ.
    /// </para>
    /// </summary>
    private static void LogRun(string app, string command, CommandResult result, string configJson, long ms)
    {
        if (!LogUsage)
        {
            return;
        }

        try
        {
            DhcbLog.Write(app, Shared.Logic.Usage.UsageLog.Format(command, result.Success, IsDryRun(configJson), result.AffectedCount, ms));
        }
        catch (Exception)
        {
            // Ghi số liệu không bao giờ được phép làm hỏng lệnh vừa chạy xong.
        }
    }

    private static bool IsDryRun(string configJson)
    {
        try
        {
            return !string.IsNullOrWhiteSpace(configJson)
                && JToken.Parse(configJson) is JObject obj
                && obj.Properties().FirstOrDefault(p => string.Equals(p.Name, "dryRun", StringComparison.OrdinalIgnoreCase))
                    ?.Value.Type == JTokenType.Boolean
                && obj.Properties().First(p => string.Equals(p.Name, "dryRun", StringComparison.OrdinalIgnoreCase)).Value.Value<bool>();
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Trường lạ là LỖI, không bỏ qua im lặng: gõ sai "sheetNumber" thành "sheetNumbers" thì lệnh
    /// chạy với mặc định và vẫn báo thành công — đúng loại lỗi im lặng mà giai đoạn 8.1 đi dọn.
    /// </summary>
    private static readonly JsonSerializerSettings Strict = new JsonSerializerSettings
    {
        MissingMemberHandling = MissingMemberHandling.Error,
    };

    private static T Deserialize<T>(string json)
    {
        try
        {
            var text = string.IsNullOrWhiteSpace(json) ? "{}" : json;

            // "dryRun" là khoá chung mọi vỏ (form, CommandRunner, RunTests, Bridge) đều gắn vào cho MỌI
            // lệnh; lệnh chỉ đọc không có property DryRun thì bỏ khoá này trước khi kiểm nghiêm.
            if (typeof(T).GetProperty("DryRun") == null && JToken.Parse(text) is JObject obj)
            {
                var dryRun = obj.Properties().FirstOrDefault(p => string.Equals(p.Name, "dryRun", StringComparison.OrdinalIgnoreCase));
                if (dryRun != null)
                {
                    dryRun.Remove();
                    text = obj.ToString();
                }
            }

            var result = JsonConvert.DeserializeObject<T>(text, Strict);
            if (result is null)
            {
                throw new ConfigException($"Không thể deserialize config thành {typeof(T).Name}.");
            }

            // Newtonsoft dựng object bằng reflection nên đi vòng qua `required` của compiler: thiếu
            // trường thì property là null và lệnh nổ NullReferenceException trần trụi. Chặn ở đây.
            RequiredConfig.ThrowIfIncomplete(result, typeof(T).Name);
            return result;
        }
        catch (JsonSerializationException ex) when (ex.Message.StartsWith("Could not find member", StringComparison.Ordinal))
        {
            // Dạng thông báo của Newtonsoft: Could not find member 'xyz' on object of type 'Foo'. Path '...'.
            var start = ex.Message.IndexOf('\'');
            var end = start >= 0 ? ex.Message.IndexOf('\'', start + 1) : -1;
            var unknown = start >= 0 && end > start ? ex.Message.Substring(start + 1, end - start - 1) : "?";
            var valid = typeof(T).GetProperties()
                .Where(p => p.CanWrite || p.SetMethod != null)
                .Select(p => char.ToLowerInvariant(p.Name[0]) + p.Name.Substring(1))
                .OrderBy(n => n, StringComparer.Ordinal);
            throw new ConfigException(
                $"E-CONFIG-UNKNOWN: config cho {typeof(T).Name} có trường không tồn tại \"{unknown}\". "
                + "Trường hợp lệ: " + string.Join(", ", valid) + ".", ex);
        }
        catch (JsonException ex)
        {
            throw new ConfigException($"Config cho {typeof(T).Name} không hợp lệ: {ex.Message}", ex);
        }
    }
}
