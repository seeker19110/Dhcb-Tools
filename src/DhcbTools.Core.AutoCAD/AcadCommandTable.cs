using DhcbTools.Core.AutoCAD.AutoNumbering;
using DhcbTools.Core.AutoCAD.DrawingCleanup;
using DhcbTools.Core.AutoCAD.LayerSync;
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

        return name switch
        {
            "LAYEREXPORT" => new LayerExportCommand().Execute(db, Deserialize<LayerExportConfig>(configJson)),
            "LAYERIMPORT" => new LayerImportCommand().Execute(db, Deserialize<LayerImportConfig>(configJson)),
            "DRAWINGCLEANUP" => new DrawingCleanupCommand().Execute(db, Deserialize<CleanupConfig>(configJson)),
            "AUTONUMBERING" => new AutoNumberingCommand().Execute(db, Deserialize<AutoNumberingConfig>(configJson)),

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
