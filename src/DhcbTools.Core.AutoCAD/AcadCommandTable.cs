using DhcbTools.Core.AutoCAD.Attributes;
using DhcbTools.Core.AutoCAD.AutoNumbering;
using DhcbTools.Core.AutoCAD.DrawingCleanup;
using DhcbTools.Core.AutoCAD.LayerSync;
using DhcbTools.Core.AutoCAD.LayerTools;
using DhcbTools.Core.AutoCAD.Reporting;
using DhcbTools.Core.AutoCAD.TextTools;
using DhcbTools.Core.AutoCAD.Testing;
using DhcbTools.Shared.Logic.Ai;
using Autodesk.AutoCAD.DatabaseServices;
using Newtonsoft.Json;

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

    private static T Deserialize<T>(string json)
    {
        try
        {
            var result = JsonConvert.DeserializeObject<T>(string.IsNullOrWhiteSpace(json) ? "{}" : json);
            if (result is null)
            {
                throw new ConfigException($"Không thể deserialize config thành {typeof(T).Name}.");
            }

            // Newtonsoft dựng object bằng reflection nên đi vòng qua `required` của compiler: thiếu
            // trường thì property là null và lệnh nổ NullReferenceException trần trụi. Chặn ở đây.
            RequiredConfig.ThrowIfIncomplete(result, typeof(T).Name);
            return result;
        }
        catch (JsonException ex)
        {
            throw new ConfigException($"Config cho {typeof(T).Name} không hợp lệ: {ex.Message}", ex);
        }
    }
}
