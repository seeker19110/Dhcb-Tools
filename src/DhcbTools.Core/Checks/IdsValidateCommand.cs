using System.Text;
using Autodesk.Revit.DB;
using DhcbTools.Shared.Logic;
using DhcbTools.Shared.Logic.Checks;
using DhcbTools.Shared.Logic.Ids;

namespace DhcbTools.Core.Checks;

/// <summary>
/// Mục 11.1 — kiểm mô hình theo file <b>IDS 1.0</b> của chủ đầu tư / tư vấn thẩm tra.
/// </summary>
public sealed class IdsValidateConfig
{
    /// <summary>File <c>.ids</c> (XML) khai yêu cầu thông tin. Bắt buộc.</summary>
    public required string IdsPath { get; init; }

    /// <summary>File HTML báo cáo. Bắt buộc.</summary>
    public required string OutputPath { get; init; }

    /// <summary>CSV cùng nội dung để lọc trong Excel (tuỳ chọn).</summary>
    public string? CsvPath { get; init; }

    /// <summary>Category cần kiểm; rỗng = mọi phần tử có thể xuất IFC trong mô hình.</summary>
    public List<string> Categories { get; init; } = new List<string>();

    /// <summary>Chỉ kiểm tầng này (rỗng = mọi tầng).</summary>
    public string? LevelName { get; init; }
}

/// <summary>
/// Kiểm <b>thẳng trên mô hình Revit</b>, không vòng qua IFC: kỹ sư sửa ngay tại chỗ phần tử sai, thay vì
/// xuất file rồi đọc lỗi ở một cái id không mở được trong Revit.
/// <para>
/// Ranh giới phải nói rõ: DHCB đọc mô hình Revit theo <b>ánh xạ Revit → IFC</b> (category, tham số
/// <c>IfcExportAs</c>, property set do bộ xuất sinh ra). Đó là cùng ánh xạ mà bộ xuất IFC dùng, nhưng
/// <b>không phải chính file IFC</b> — nên kết luận ở đây là "mô hình sẽ đạt khi xuất", chứ không thay cho
/// một lượt kiểm trên file đã nộp (mục 11.4 quyết định có mở sang đường đó không).
/// </para>
/// </summary>
public sealed class IdsValidateCommand : ICoreCommand<IdsValidateConfig>
{
    /// <summary>Tên lệnh trong bảng điều phối.</summary>
    public string CommandName => "IdsValidate";

    /// <summary>Chạy lệnh.</summary>
    public CommandResult Execute(Document document, IdsValidateConfig config)
    {
        if (!File.Exists(config.IdsPath))
        {
            return CommandResult.Fail($"E-PATH-MISSING: không tìm thấy file IDS \"{config.IdsPath}\".");
        }

        var xml = File.ReadAllText(config.IdsPath);
        IReadOnlyList<IdsSpecification> specifications;
        try
        {
            specifications = IdsSpec.Parse(xml);
        }
        catch (IdsParseException ex)
        {
            return CommandResult.Fail("File IDS không dùng được: " + ex.Message);
        }

        // §39: bộ đọc cố ý dễ tính (bỏ qua namespace, thứ tự thẻ) nên một file "gần đúng" vẫn kiểm được ở
        // DHCB — nhưng IfcTester/Solibri kiểm theo XSD sẽ từ chối. Cảnh báo, không chặn: kỹ sư vẫn có kết
        // quả để sửa mô hình, và biết trước file IDS sẽ không được bên kia nhận.
        var schemaWarnings = IdsSchemaLint.Check(xml);

        ICollection<ElementId> categoryIds = new List<ElementId>();
        if (config.Categories.Count > 0)
        {
            categoryIds = ParameterSync.ParameterExportCommand.ResolveCategoryIds(document, config.Categories, out var unknown);
            if (unknown.Count > 0)
            {
                return CommandResult.Fail("Category không có: " + string.Join(", ", unknown) + ".");
            }
        }

        var collector = new FilteredElementCollector(document).WhereElementIsNotElementType();
        var raw = categoryIds.Count > 0
            ? collector.WherePasses(new ElementMulticategoryFilter(categoryIds.ToList())).ToElements()
            : collector.ToElements();

        var elements = new List<IIdsElement>();
        foreach (var element in raw)
        {
            if (element.Category == null || element.Category.CategoryType != CategoryType.Model)
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(config.LevelName)
                && !string.Equals(LevelNameOf(document, element), config.LevelName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            elements.Add(new RevitIdsElement(document, element));
        }

        var precondition = Precondition.NonEmptyInput(
            CommandName, "phần tử mô hình trong phạm vi", elements.Count,
            "Kiểm lại categories/levelName, hoặc mở đúng file có nhóm phần tử mà bộ IDS nhắm tới.");
        var blocked = CommandResult.Ok(string.Empty);
        if (RevitPrecondition.Blocks(precondition, blocked))
        {
            return blocked;
        }

        var check = IdsEvaluator.Check(specifications, elements);
        var result = CommandResult.Ok(string.Empty);

        Directory.CreateDirectory(Path.GetDirectoryName(config.OutputPath) ?? ".");
        File.WriteAllText(
            config.OutputPath,
            IdsReport.Html(document.Title, config.IdsPath, IdsReport.RevitScopeNote, check, schemaWarnings),
            new UTF8Encoding(true));

        if (!string.IsNullOrWhiteSpace(config.CsvPath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(config.CsvPath!) ?? ".");
            File.WriteAllText(config.CsvPath!, IdsReport.Csv(check), new UTF8Encoding(true));
        }

        result.Messages.AddRange(IdsReport.Messages(check, schemaWarnings));
        result.Summary = IdsReport.Summary(check, schemaWarnings) + $" → \"{config.OutputPath}\".";
        result.AffectedCount = check.FailureCount;
        return result;
    }

    private static string LevelNameOf(Document document, Element element)
    {
        var level = document.GetElement(element.LevelId) as Level;
        return level?.Name ?? string.Empty;
    }
}
