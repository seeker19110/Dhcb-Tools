using System.Text;
using Autodesk.AutoCAD.DatabaseServices;
using DhcbTools.Shared.Logic;
using DhcbTools.Shared.Logic.Ai;

namespace DhcbTools.Core.AutoCAD.LayerTools;

/// <summary>
/// Gợi ý map layer CAD hiện có trong drawing → Revit type từ danh sách type cho trước, dùng
/// heuristic offline (<see cref="LayerMappingSuggester"/>) — không viết lại logic khớp, tái sử dụng
/// class dùng chung với lệnh SpecToConfig/LayerMap bên Revit.
/// UseOllama chỉ là cờ dự phòng: nếu bật nhưng chưa cấu hình được model local, lệnh vẫn chạy
/// heuristic thay vì throw lỗi (dự án chưa dây Ollama vào Core.AutoCAD).
/// </summary>
public sealed class CadLayerMapCommand : ICoreCommand<CadLayerMapConfig>
{
    public string CommandName => "CadLayerMap";

    public CommandResult Execute(Database database, CadLayerMapConfig config)
    {
        if (!File.Exists(config.RevitTypesPath))
        {
            return CommandResult.Fail($"Không tìm thấy file danh sách type: \"{config.RevitTypesPath}\".");
        }

        var revitTypes = File.ReadAllLines(config.RevitTypesPath)
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (revitTypes.Count == 0)
        {
            return CommandResult.Fail("File danh sách type rỗng.");
        }

        var layerNames = new List<string>();

        using (var transaction = database.TransactionManager.StartTransaction())
        {
            var layerTable = (LayerTable)transaction.GetObject(database.LayerTableId, OpenMode.ForRead);
            foreach (ObjectId layerId in layerTable)
            {
                var layer = (LayerTableRecord)transaction.GetObject(layerId, OpenMode.ForRead);
                layerNames.Add(layer.Name);
            }

            transaction.Commit();
        }

        // UseOllama: dự phòng cho tương lai — chưa có client Ollama dây vào Core.AutoCAD nên luôn
        // rơi về heuristic offline, không throw.
        var suggestions = LayerMappingSuggester.Suggest(layerNames, revitTypes);

        var sb = new StringBuilder();
        sb.AppendLine("CadLayer,SuggestedRevitType,Confidence");
        var needsReview = 0;

        foreach (var mapping in suggestions)
        {
            sb.Append(CsvText.JoinLine(new[]
            {
                mapping.Layer,
                mapping.RevitType ?? string.Empty,
                NumericText.Format(mapping.Confidence, 2),
            })).Append('\n');

            if (mapping.NeedsReview)
            {
                needsReview++;
            }
        }

        File.WriteAllText(config.OutputPath, sb.ToString(), CsvText.Utf8WithBom);

        return CommandResult.Ok(
            $"Đã gợi ý map cho {suggestions.Count} layer ({needsReview} cần xem lại) ra \"{config.OutputPath}\".",
            suggestions.Count);
    }
}
