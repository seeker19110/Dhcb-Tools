using Autodesk.Revit.DB;
using DhcbTools.Shared.Logic;
using DhcbTools.Shared.Logic.Ai;

namespace DhcbTools.Core.Ai;

/// <summary>Soi tên tham số thật trong mô hình rồi đề xuất/ghi <c>dictionary.json</c> của dự án.</summary>
public sealed class DictionaryLearnConfig
{
    /// <summary>Category cần soi; rỗng = bộ mặc định phủ kiến trúc + MEPF.</summary>
    public List<string> Categories { get; init; } = new List<string>();

    /// <summary>Số phần tử lấy mẫu cho mỗi category — đủ để biết tham số nào có dữ liệu thật.</summary>
    public int SampleSize { get; init; } = 200;

    /// <summary>File từ điển sẽ trộn vào; rỗng = <c>%APPDATA%\DHCB\dictionary.json</c>.</summary>
    public string? OutputPath { get; init; }

    /// <summary>CSV để duyệt trong Excel; rỗng = không ghi.</summary>
    public string? ReportPath { get; init; }

    /// <summary>Chỉ đề xuất, không đụng vào file từ điển. Mặc định bật như mọi lệnh khác.</summary>
    public bool DryRun { get; init; } = true;

    /// <summary>Dưới ngưỡng này thì báo "không thấy" thay vì đề xuất bừa.</summary>
    public double MinConfidence { get; init; } = DictionarySuggester.MinConfidence;

    /// <summary>Nhận cả dòng cần kỹ sư xem (độ tin cậy dưới 0,7). Mặc định tắt — chỉ ghi dòng chắc chắn.</summary>
    public bool AcceptLowConfidence { get; init; } = false;
}

/// <summary>
/// Giai đoạn 9.2 bỏ được tên tham số cứng trong mã, nhưng đổi lại kỹ sư phải tự sửa JSON trong
/// <c>%APPDATA%</c> mỗi lần vấp <c>E-PARAM-MISSING</c> — ma sát đã đo được trên dự án thật
/// (<c>docs/progress.md</c> §21). Lệnh này làm phần việc đó bằng máy: đọc tên tham số CÓ THẬT trong
/// mô hình đang mở, đối chiếu với từng khoá logic của từ điển, và đề xuất tên đúng của dự án.
/// <para>
/// Ràng buộc an toàn, giống <c>CadLayerMap</c>: chỉ đề xuất tên có thật; <c>dryRun</c> mặc định bật;
/// khi ghi thì <b>trộn</b> chứ không ghi đè — mọi tên kỹ sư đã khai đều giữ nguyên.
/// </para>
/// </summary>
public sealed class DictionaryLearnCommand : ICoreCommand<DictionaryLearnConfig>
{
    public string CommandName => "DictionaryLearn";

    private static readonly BuiltInCategory[] DefaultCategories =
    {
        BuiltInCategory.OST_Walls, BuiltInCategory.OST_Floors, BuiltInCategory.OST_Doors, BuiltInCategory.OST_Windows,
        BuiltInCategory.OST_Rooms, BuiltInCategory.OST_StructuralColumns, BuiltInCategory.OST_StructuralFraming,
        BuiltInCategory.OST_DuctCurves, BuiltInCategory.OST_PipeCurves, BuiltInCategory.OST_CableTray,
        BuiltInCategory.OST_Conduit, BuiltInCategory.OST_DuctFitting, BuiltInCategory.OST_PipeFitting,
        BuiltInCategory.OST_PipeAccessory, BuiltInCategory.OST_MechanicalEquipment, BuiltInCategory.OST_PlumbingFixtures,
        BuiltInCategory.OST_Sprinklers, BuiltInCategory.OST_GenericModel,
    };

    public CommandResult Execute(Document document, DictionaryLearnConfig config)
    {
        var categoryIds = config.Categories.Count == 0
            ? DefaultCategories.Select(c => new ElementId(c)).ToList()
            : ParameterSync.ParameterExportCommand.ResolveCategoryIds(document, config.Categories, out _).ToList();

        if (categoryIds.Count == 0)
        {
            return CommandResult.Fail("Không có category nào hợp lệ: " + string.Join(", ", config.Categories));
        }

        var candidates = ReadCandidates(document, categoryIds, Math.Max(1, config.SampleSize));
        if (candidates.Count == 0)
        {
            // Không có phần tử nào để soi thì mọi đề xuất đều là bịa — báo thẳng thay vì trả bảng rỗng
            // trông như "dự án này không thiếu gì".
            return CommandResult.Fail(
                "Không đọc được tham số nào: mô hình không có phần tử nào thuộc các category đã chọn. "
                + "Hãy mở đúng file mô hình (không phải file rỗng/template) hoặc chỉ định categories.");
        }

        var dictionary = ParameterDictionary.Load(config.OutputPath);
        var suggestions = DictionarySuggester.Suggest(dictionary.Keys, dictionary, candidates, config.MinConfidence);

        var daCo = suggestions.Count(s => s.Status == SuggestionStatus.DaCo);
        var deXuat = suggestions.Where(s => s.IsProposal).ToList();
        var khongThay = suggestions.Where(s => s.Status == SuggestionStatus.KhongThay).ToList();
        var nhan = deXuat.Where(s => config.AcceptLowConfidence || !s.NeedsReview).ToList();

        var result = CommandResult.Ok(string.Empty);
        result.AffectedCount = nhan.Count;

        if (!string.IsNullOrWhiteSpace(config.ReportPath))
        {
            RevitCompat.EnsureParentDirectory(config.ReportPath!);
            File.WriteAllText(config.ReportPath!, DictionarySuggester.ToCsv(suggestions), CsvText.Utf8WithBom);
        }

        var path = string.IsNullOrWhiteSpace(config.OutputPath) ? ParameterDictionary.DefaultPath : config.OutputPath!;

        if (config.DryRun)
        {
            result.Summary =
                $"Xem trước: soi {candidates.Count} tên tham số trên {categoryIds.Count} category — "
                + $"{daCo} khoá mô hình đã có sẵn, {nhan.Count} khoá sẽ ghi vào \"{path}\", "
                + $"{deXuat.Count - nhan.Count} khoá cần kỹ sư xem, {khongThay.Count} khoá không tìm được tên nào.";
        }
        else
        {
            string? cu = null;
            try
            {
                cu = File.Exists(path) ? File.ReadAllText(path) : null;
            }
            catch (Exception ex)
            {
                return CommandResult.Fail($"Không đọc được từ điển hiện có \"{path}\": {ex.Message}");
            }

            string moi;
            try
            {
                moi = DictionarySuggester.Merge(cu, nhan);
            }
            catch (InvalidOperationException ex)
            {
                // Ghi đè một file JSON hỏng là xoá mất khai báo của kỹ sư — dừng, để họ tự sửa.
                return CommandResult.Fail(ex.Message + $" Hãy sửa hoặc đổi tên \"{path}\" rồi chạy lại.");
            }

            RevitCompat.EnsureParentDirectory(path);
            if (cu != null)
            {
                // Bản lưu trước khi trộn — đường lùi duy nhất nếu đề xuất sai.
                File.WriteAllText(path + ".bak", cu, System.Text.Encoding.UTF8);
            }

            File.WriteAllText(path, moi, System.Text.Encoding.UTF8);
            result.Summary =
                $"Đã ghi {nhan.Count} tên tham số của dự án vào \"{path}\""
                + (cu != null ? $" (bản cũ giữ ở \"{path}.bak\")" : string.Empty)
                + $"; {daCo} khoá mô hình đã có sẵn, {khongThay.Count} khoá vẫn chưa có tên nào.";
        }

        result.Messages.AddRange(nhan.Select(s => $"[Ghi] {s.Key} → \"{s.Name}\" ({s.Confidence:F2}): {s.Reason}"));
        result.Messages.AddRange(deXuat.Where(s => !nhan.Contains(s))
            .Select(s => $"[Xem] {s.Key} → \"{s.Name}\" ({s.Confidence:F2}): {s.Reason}"));
        result.Messages.AddRange(khongThay
            .Select(s => $"[Thiếu] {s.Key}: {s.Reason}. Lệnh cần khoá này sẽ báo E-PARAM-MISSING."));

        if (!string.IsNullOrWhiteSpace(config.ReportPath))
        {
            result.Messages.Add($"Bảng đầy đủ để duyệt: \"{config.ReportPath}\".");
        }

        return result;
    }

    /// <summary>
    /// Đọc tên tham số thật kèm số phần tử có giá trị. Mức độ được điền là thứ phân biệt tham số dùng
    /// thật với tham số tồn tại mà rỗng toàn dự án — chính là lớp lỗi từ điển sinh ra để chặn.
    /// </summary>
    private static List<ParameterCandidate> ReadCandidates(Document document, List<ElementId> categoryIds, int sampleSize)
    {
        var elements = new FilteredElementCollector(document)
            .WhereElementIsNotElementType()
            .WherePasses(new ElementMulticategoryFilter(categoryIds))
            .ToList();

        // Lấy mẫu theo từng category để một category đông (tường) không nuốt hết hạn ngạch của
        // category thưa nhưng quan trọng (thiết bị MEP).
        var sample = elements
            .Where(e => e.Category is not null)
            .GroupBy(e => e.Category!.Name)
            .SelectMany(g => g.Take(sampleSize))
            .ToList();

        var stats = new Dictionary<string, (string Category, string StorageType, int Filled, int Total)>(
            StringComparer.OrdinalIgnoreCase);

        void Collect(Element element, string categoryName)
        {
            foreach (Parameter parameter in element.Parameters)
            {
                var name = parameter.Definition?.Name;
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                var filled = HasValue(parameter) ? 1 : 0;
                if (stats.TryGetValue(name!, out var cu))
                {
                    stats[name!] = (cu.Category, cu.StorageType, cu.Filled + filled, cu.Total + 1);
                }
                else
                {
                    stats[name!] = (categoryName, parameter.StorageType.ToString(), filled, 1);
                }
            }
        }

        foreach (var element in sample)
        {
            var categoryName = element.Category?.Name ?? "?";
            Collect(element, categoryName);

            Element? type = null;
            try
            {
                type = document.GetElement(element.GetTypeId());
            }
            catch (Exception)
            {
                // Phần tử không có type (Room, Level…) — bỏ qua, không phải lỗi.
            }

            if (type != null)
            {
                Collect(type, categoryName);
            }
        }

        return stats
            .Select(kv => new ParameterCandidate(kv.Key, kv.Value.Category, kv.Value.StorageType, kv.Value.Filled, kv.Value.Total))
            .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool HasValue(Parameter parameter)
    {
        if (!parameter.HasValue)
        {
            return false;
        }

        return parameter.StorageType switch
        {
            StorageType.String => !string.IsNullOrWhiteSpace(parameter.AsString()),
            StorageType.ElementId => parameter.AsElementId() != ElementId.InvalidElementId,
            _ => true,
        };
    }
}
