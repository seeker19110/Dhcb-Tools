using Autodesk.Revit.DB;
using DhcbTools.Shared.Logic;
using DhcbTools.Shared.Logic.Ai;

namespace DhcbTools.Core.Ai;

/// <summary>Mục 5.1 — map layer CAD → Revit type, offline.</summary>
public sealed class CadLayerMapConfig
{
    /// <summary>CSV do AutoCAD <c>LayerExport</c> sinh (cột đầu là Name), hoặc file .txt mỗi dòng một layer.</summary>
    public required string LayersCsvPath { get; init; }

    public required string OutputPath { get; init; }

    /// <summary>Lọc type theo category (rỗng = Walls, Doors, Windows, Floors, Columns, Structural Columns, Structural Framing, Ducts, Pipes, Cable Trays, Conduits).</summary>
    public List<string> TypeCategories { get; init; } = new List<string>();

    /// <summary>Dùng model local (Ollama, %APPDATA%\DHCB\ai.json) để tinh chỉnh — vẫn lọc type có thật.</summary>
    public bool UseOllama { get; init; } = false;

    public double MinConfidence { get; init; } = 0.3;
}

/// <summary>Lấy danh mục type có thật trong mô hình, chạy heuristic (và tuỳ chọn model local), ghi CSV để duyệt.</summary>
public sealed class CadLayerMapCommand : ICoreCommand<CadLayerMapConfig>
{
    public string CommandName => "CadLayerMap";

    private static readonly BuiltInCategory[] DefaultCategories =
    {
        BuiltInCategory.OST_Walls, BuiltInCategory.OST_Doors, BuiltInCategory.OST_Windows, BuiltInCategory.OST_Floors, BuiltInCategory.OST_Columns,
        BuiltInCategory.OST_StructuralColumns, BuiltInCategory.OST_StructuralFraming, BuiltInCategory.OST_DuctCurves, BuiltInCategory.OST_PipeCurves,
        BuiltInCategory.OST_CableTray, BuiltInCategory.OST_Conduit, BuiltInCategory.OST_Ceilings, BuiltInCategory.OST_Roofs, BuiltInCategory.OST_Stairs,
    };

    public CommandResult Execute(Document document, CadLayerMapConfig config)
    {
        if (!File.Exists(config.LayersCsvPath))
        {
            return CommandResult.Fail($"Không tìm thấy \"{config.LayersCsvPath}\".");
        }

        var layers = ReadLayers(config.LayersCsvPath);
        if (layers.Count == 0)
        {
            return CommandResult.Fail("File layer rỗng.");
        }

        var filter = config.TypeCategories.Count == 0
            ? new ElementMulticategoryFilter(DefaultCategories.ToList())
            : new ElementMulticategoryFilter(ParameterSync.ParameterExportCommand.ResolveCategoryIds(document, config.TypeCategories, out _).ToList());
        var types = new FilteredElementCollector(document).WhereElementIsElementType().WherePasses(filter).Cast<ElementType>()
            .Select(t => t.FamilyName + ": " + t.Name).Distinct().OrderBy(t => t).ToList();
        if (types.Count == 0)
        {
            return CommandResult.Fail("Mô hình không có type nào trong các category đã chọn.");
        }

        var result = CommandResult.Ok(string.Empty);
        var mappings = LayerMappingSuggester.Suggest(layers, types, config.MinConfidence);
        var source = "heuristic offline";

        if (config.UseOllama)
        {
            var settings = LocalAiSettings.Load();
            var client = new OllamaClient(settings);
            if (!client.IsUsable)
            {
                result.Messages.Add("Model local chưa bật/không hợp lệ (ai.json: enabled, endpoint loopback, model) — dùng heuristic.");
            }
            else
            {
                var rejected = new List<string>();
                var fromModel = client.SuggestLayerMappings(layers, types, rejected);
                if (fromModel == null)
                {
                    result.Messages.Add("Model local không trả lời — dùng heuristic.");
                }
                else
                {
                    // Trộn: model có kết quả thì ưu tiên, nhưng chỉ khi heuristic không "chắc" hơn.
                    var byLayer = fromModel.ToDictionary(m => m.Layer, m => m, StringComparer.OrdinalIgnoreCase);
                    mappings = mappings.Select(h => byLayer.TryGetValue(h.Layer, out var m) && (h.RevitType == null || m.Confidence >= h.Confidence) ? m : h).ToList();
                    result.Messages.AddRange(rejected.Select(r => "Model: " + r));
                    source = $"model local {settings.Model} + heuristic";
                }
            }
        }

        File.WriteAllText(config.OutputPath, LayerMappingSuggester.ToCsv(mappings), CsvText.Utf8WithBom);
        var review = mappings.Count(m => m.NeedsReview);
        result.Summary = $"Đã gợi ý map {mappings.Count} layer → type ({review} cần kỹ sư xem, nguồn: {source}) → \"{config.OutputPath}\".";
        result.AffectedCount = mappings.Count;
        result.Messages.AddRange(mappings.Where(m => m.NeedsReview).Take(50).Select(m => $"[Xem] {m.Layer} → {m.RevitType ?? "?"} ({m.Confidence:F2}): {m.Reason}"));
        return result;
    }

    internal static List<string> ReadLayers(string path)
    {
        var lines = File.ReadAllLines(path, CsvText.Utf8WithBom).Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
        if (lines.Count == 0) return new List<string>();

        var first = CsvText.SplitLine(lines[0]);
        var isCsv = first.Count > 1;
        var start = isCsv && first[0].Equals("Name", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        return lines.Skip(start).Select(l => CsvText.SplitLine(l)[0].Trim()).Where(l => l.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }
}

/// <summary>Mục 5.2 — thuyết minh (.txt/.md, PDF đã đổi sang text) → config ProjectInit.</summary>
public sealed class SpecToConfigConfig
{
    public required string InputPath { get; init; }

    public required string OutputPath { get; init; }
}

public sealed class SpecToConfigCommand : ICoreCommand<SpecToConfigConfig>
{
    public string CommandName => "SpecToConfig";

    public CommandResult Execute(Document document, SpecToConfigConfig config)
    {
        if (!File.Exists(config.InputPath))
        {
            return CommandResult.Fail($"Không tìm thấy \"{config.InputPath}\".");
        }

        if (config.InputPath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            return CommandResult.Fail("Hãy đổi PDF sang text trước (scripts/dhcb_ai.py spec --pdf ... hoặc pdftotext), lệnh này nhận .txt/.md.");
        }

        var extraction = SpecTextExtractor.Extract(File.ReadAllText(config.InputPath));
        var dir = Path.GetDirectoryName(config.OutputPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(config.OutputPath, extraction.ToProjectInitJson(), System.Text.Encoding.UTF8);

        var result = CommandResult.Ok($"Trích {extraction.Levels.Count} tầng, {extraction.Systems.Count} hệ, {extraction.Standards.Count} tiêu chuẩn → \"{config.OutputPath}\" (dryRun=true, duyệt rồi chạy LevelSetup/ProjectInfo).", extraction.Levels.Count);
        result.Messages.AddRange(extraction.Levels.Select(l => $"{l.Name}: {NumericText.Format(l.ElevationMm, 0)} mm  ← \"{l.SourceLine}\""));
        result.Messages.AddRange(extraction.Warnings.Select(w => "[Cảnh báo] " + w));
        return result;
    }
}
