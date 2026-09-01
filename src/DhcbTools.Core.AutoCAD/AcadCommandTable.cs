using Autodesk.AutoCAD.DatabaseServices;
using DhcbTools.Core.AutoCAD.Attributes;
using DhcbTools.Core.AutoCAD.AutoNumbering;
using DhcbTools.Core.AutoCAD.Compare;
using DhcbTools.Core.AutoCAD.DrawingCleanup;
using DhcbTools.Core.AutoCAD.LayerSync;
using DhcbTools.Core.AutoCAD.Standards;
using DhcbTools.Core.AutoCAD.Text;
using DhcbTools.Shared.Logic.Ai;
using Newtonsoft.Json;

namespace DhcbTools.Core.AutoCAD;

/// <summary>Bảng tra lệnh AutoCAD theo tên/bí danh — dùng chung cho Bridge, DHCB_RUN (batch accoreconsole) và lớp AI.</summary>
public static class AcadCommandTable
{
    public static CommandResult Dispatch(Database db, string command, string configJson)
    {
        var descriptor = CommandCatalog.Find(CommandCatalog.AutoCad, command);
        var name = (descriptor?.Name ?? command).ToUpperInvariant();

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
            _ => CommandResult.Fail($"Lệnh không xác định: \"{command}\". Hợp lệ: {string.Join(", ", CommandCatalog.Names(CommandCatalog.AutoCad))}."),
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
