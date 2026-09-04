using DhcbTools.Core.AutoCAD.Attributes;
using DhcbTools.Core.AutoCAD.AutoNumbering;
using DhcbTools.Core.AutoCAD.DrawingCleanup;
using DhcbTools.Core.AutoCAD.LayerSync;
using DhcbTools.Core.AutoCAD.LayerTools;
using DhcbTools.Core.AutoCAD.Reporting;
using DhcbTools.Core.AutoCAD.TextTools;
using DhcbTools.Core.AutoCAD.Testing;
using DhcbTools.Shared.Logic.Ai;
using System.Text.RegularExpressions;
using Autodesk.AutoCAD.DatabaseServices;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DhcbTools.Core.AutoCAD;

/// <summary>
/// Bảng tra lệnh AutoCAD theo tên (khoá = <c>ICoreCommand.CommandName</c>, nhận cả bí danh trong
/// <see cref="CommandCatalog"/>). Đối xứng với <c>RevitCommandTable</c> bên Revit: một điểm dispatch
/// duy nhất cho HTTP Bridge, lệnh Ribbon và lớp AI — test <c>CommandCatalogTests</c> đối chiếu file
/// này với catalog.
/// </summary>
public static class AcadCommandTable
{
    public static CommandResult Dispatch(Database db, string command, string configJson)
    {
        var descriptor = CommandCatalog.Find(CommandCatalog.AutoCad, command);
        var name = (descriptor?.Name ?? command).ToUpperInvariant();

        // Config hỏng/thiếu trường bắt buộc là lỗi của người gọi, không phải sự cố hệ thống:
        // trả CommandResult.Fail có thông báo đọc được thay vì ném stack trace .NET ra Bridge/agent.
        try
        {
            return name switch
            {
                "LAYEREXPORT" => new LayerExportCommand().Execute(db, Deserialize<LayerExportConfig>(configJson)),
                "LAYERIMPORT" => new LayerImportCommand().Execute(db, Deserialize<LayerImportConfig>(configJson)),
                "DRAWINGCLEANUP" => new DrawingCleanupCommand().Execute(db, Deserialize<CleanupConfig>(configJson)),
                "AUTONUMBERING" => new AutoNumberingCommand().Execute(db, Deserialize<AutoNumberingConfig>(configJson)),
                "ATTRIBUTEEXPORT" => new AttributeExportCommand().Execute(db, Deserialize<AttributeExportConfig>(configJson)),
                "ATTRIBUTEIMPORT" => new AttributeImportCommand().Execute(db, Deserialize<AttributeImportConfig>(configJson)),
                "TEXTREPLACE" => new TextReplaceCommand().Execute(db, Deserialize<TextReplaceConfig>(configJson)),
                "LAYERSTANDARDCHECK" => new LayerStandardCheckCommand().Execute(db, Deserialize<LayerStandardCheckConfig>(configJson)),
                "GRIDEXTRACT" => new GridExtractCommand().Execute(db, Deserialize<GridExtractConfig>(configJson)),
                "XREFAUDIT" => new XrefAuditCommand().Execute(db, Deserialize<XrefAuditConfig>(configJson)),
                "LAYERTRANSLATE" => new LayerTranslateCommand().Execute(db, Deserialize<LayerTranslateConfig>(configJson)),
                "DRAWINGCOMPARE" => new DrawingCompareCommand().Execute(db, Deserialize<DrawingCompareConfig>(configJson)),
                "BLOCKQUANTITY" => new BlockQuantityCommand().Execute(db, Deserialize<BlockQuantityConfig>(configJson)),
                "ATTRIBUTEINCREMENT" => new AttributeIncrementCommand().Execute(db, Deserialize<AttributeIncrementConfig>(configJson)),
                "CADLAYERMAP" => new CadLayerMapCommand().Execute(db, Deserialize<CadLayerMapConfig>(configJson)),
                "RUNTESTS" => new RunTestsCommand().Execute(db, Deserialize<RunTestsConfig>(configJson)),

                _ => CommandResult.Fail($"Lệnh không xác định: \"{command}\". Hợp lệ: {string.Join(", ", CommandCatalog.Names(CommandCatalog.AutoCad))}."),
            };
        }
        catch (ConfigException ex)
        {
            return CommandResult.Fail(ex.Message);
        }
    }

    /// <summary>
    /// Trường lạ trong config là lỗi, không phải bỏ qua: agent gõ <c>outputPatch</c> thay vì <c>outputPath</c>
    /// mà lệnh vẫn chạy với giá trị mặc định thì không ai biết vì sao file không ra.
    /// </summary>
    private static readonly JsonSerializerSettings StrictSettings = new()
    {
        MissingMemberHandling = MissingMemberHandling.Error,
    };

    private static readonly Regex MissingMemberName = new("member '(?<name>[^']+)'", RegexOptions.Compiled);

    private static T Deserialize<T>(string json)
    {
        try
        {
            var text = string.IsNullOrWhiteSpace(json) ? "{}" : json;

            // RunTests ép "dryRun" vào MỌI ca kiểm (kể cả lệnh chỉ đọc như LayerExport) — đó là cờ an toàn
            // của runner chứ không phải lỗi gõ, nên bỏ qua riêng nó khi config không có DryRun.
            if (typeof(T).GetProperty("DryRun") is null && JsonConvert.DeserializeObject<JObject>(text) is { } obj && obj.Remove("dryRun"))
            {
                text = obj.ToString(Formatting.None);
            }

            var result = JsonConvert.DeserializeObject<T>(text, StrictSettings);
            if (result is null)
            {
                throw new ConfigException($"Không thể deserialize config thành {typeof(T).Name}.");
            }

            // Newtonsoft dựng object bằng reflection nên đi vòng qua `required` của compiler: thiếu
            // trường thì property là null và lệnh nổ NullReferenceException trần trụi. Chặn ở đây.
            RequiredConfig.ThrowIfIncomplete(result, typeof(T).Name);
            return result;
        }
        catch (JsonSerializationException ex) when (MissingMemberName.Match(ex.Message) is { Success: true } m)
        {
            var known = string.Join(", ", typeof(T).GetProperties().Select(p => char.ToLowerInvariant(p.Name[0]) + p.Name.Substring(1)));
            throw new ConfigException(
                $"Config cho {typeof(T).Name} có trường không tồn tại: \"{m.Groups["name"].Value}\". Trường hợp lệ: {known}.", ex);
        }
        catch (JsonException ex)
        {
            throw new ConfigException($"Config cho {typeof(T).Name} không hợp lệ: {ex.Message}", ex);
        }
    }
}
