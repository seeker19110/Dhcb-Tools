using System.Text;
using Autodesk.AutoCAD.DatabaseServices;
using DhcbTools.Shared.Logic;
using DhcbTools.Shared.Logic.Ai;

namespace DhcbTools.Core.AutoCAD.LayerTools;

/// <summary>
/// Gợi ý map layer CAD hiện có trong drawing → Revit type từ danh sách type cho trước, dùng
/// heuristic offline (<see cref="LayerMappingSuggester"/>) — không viết lại logic khớp, tái sử dụng
/// class dùng chung với lệnh SpecToConfig/LayerMap bên Revit.
/// <c>useOllama</c>: nếu bật và <c>%APPDATA%\DHCB\ai.json</c> có model local hợp lệ thì hỏi model rồi trộn với
/// heuristic (cùng cách với lệnh LayerMap bên Revit); không có model thì rơi về heuristic và NÓI rõ trong
/// Messages — trước đây vỏ hỏi "Dùng Ollama?" nhưng core bỏ qua cờ này.
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

        var suggestions = LayerMappingSuggester.Suggest(layerNames, revitTypes);
        var notes = new List<string>();
        var source = "heuristic offline";

        if (config.UseOllama)
        {
            var settings = LocalAiSettings.Load();
            var client = new OllamaClient(settings);
            if (!client.IsUsable)
            {
                notes.Add("Model local chưa bật/không hợp lệ (ai.json: enabled, endpoint loopback, model) — dùng heuristic.");
            }
            else
            {
                var rejected = new List<string>();
                var fromModel = client.SuggestLayerMappings(layerNames, revitTypes, rejected);
                if (fromModel == null)
                {
                    notes.Add("Model local không trả lời (" + (client.LastError ?? "không rõ lý do") + ") — dùng heuristic.");
                }
                else
                {
                    // Trộn như bên Revit: model có kết quả thì ưu tiên, trừ khi heuristic đã chắc hơn.
                    var byLayer = fromModel
                        .GroupBy(m => m.Layer, StringComparer.OrdinalIgnoreCase)
                        .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
                    suggestions = suggestions
                        .Select(h => byLayer.TryGetValue(h.Layer, out var m) && (h.RevitType == null || m.Confidence >= h.Confidence) ? m : h)
                        .ToList();
                    notes.AddRange(rejected.Select(r => "Model: " + r));
                    source = $"model local {settings.Model} + heuristic";
                }
            }
        }

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

        AcadHelpers.EnsureParentDirectory(config.OutputPath);
        File.WriteAllText(config.OutputPath, sb.ToString(), CsvText.Utf8WithBom);

        var result = CommandResult.Ok(
            $"Đã gợi ý map cho {suggestions.Count} layer ({needsReview} cần xem lại, nguồn: {source}) ra \"{config.OutputPath}\".",
            suggestions.Count);
        result.Messages.AddRange(notes);
        return result;
    }
}
