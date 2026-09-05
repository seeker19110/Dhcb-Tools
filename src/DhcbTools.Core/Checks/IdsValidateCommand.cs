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

        IReadOnlyList<IdsSpecification> specifications;
        try
        {
            specifications = IdsSpec.Parse(File.ReadAllText(config.IdsPath));
        }
        catch (IdsParseException ex)
        {
            return CommandResult.Fail("File IDS không dùng được: " + ex.Message);
        }

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
        File.WriteAllText(config.OutputPath, Html(document, config, check), new UTF8Encoding(true));

        if (!string.IsNullOrWhiteSpace(config.CsvPath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(config.CsvPath!) ?? ".");
            File.WriteAllText(config.CsvPath!, Csv(check), new UTF8Encoding(true));
        }

        foreach (var spec in check.Specifications)
        {
            var head = $"{spec.Name}: {spec.Passed}/{spec.Applicable} đạt";
            result.Messages.Add(spec.NoApplicableElements
                ? $"{spec.Name}: KHÔNG phần tử nào lọt bộ lọc — con số này nói về bộ lọc hoặc về mô hình thiếu nhóm đó, không phải \"đạt\"."
                : head + (spec.Failures.Count > 0 ? $", {spec.Failures.Count} phần tử không đạt" : string.Empty));
        }

        var failedSpecs = check.Specifications.Count(s => s.Failures.Count > 0);
        result.Summary =
            $"Kiểm {check.ElementCount} phần tử theo {check.Specifications.Count} specification: "
            + $"{check.FailureCount} phần tử không đạt ở {failedSpecs} specification"
            + (check.EmptySpecificationCount > 0 ? $", {check.EmptySpecificationCount} specification không có phần tử nào để kiểm" : string.Empty)
            + $" → \"{config.OutputPath}\".";
        result.AffectedCount = check.FailureCount;

        foreach (var failure in check.Specifications.SelectMany(s => s.Failures).Take(20))
        {
            result.Messages.Add($"{failure.Specification} — {failure.Element}: {failure.Reason}");
        }

        return result;
    }

    private static string LevelNameOf(Document document, Element element)
    {
        var level = document.GetElement(element.LevelId) as Level;
        return level?.Name ?? string.Empty;
    }

    private static string Csv(IdsCheckResult check)
    {
        var sb = new StringBuilder();
        sb.Append(CsvText.JoinLine(new[] { "Specification", "Phần tử", "Không đạt vì" })).Append("\r\n");
        foreach (var failure in check.Specifications.SelectMany(s => s.Failures))
        {
            sb.Append(CsvText.JoinLine(new[] { failure.Specification, failure.Element, failure.Reason })).Append("\r\n");
        }

        return sb.ToString();
    }

    private static string Html(Document document, IdsValidateConfig config, IdsCheckResult check)
    {
        var sb = new StringBuilder();
        sb.Append("<!DOCTYPE html><html lang=\"vi\"><head><meta charset=\"utf-8\"><title>DHCB — Kiểm IDS</title>")
          .Append("<style>body{font-family:Segoe UI,Arial,sans-serif;margin:24px;color:#222}")
          .Append("table{border-collapse:collapse;width:100%;margin:12px 0}th,td{border:1px solid #ccc;padding:6px 8px;text-align:left;vertical-align:top}")
          .Append("th{background:#f2f2f2}.dat{color:#0a7d28}.truot{color:#b00020}.trong{color:#a06000}</style></head><body>")
          .Append("<h1>Kiểm mô hình theo IDS</h1><p><b>Mô hình:</b> ")
          .Append(HtmlText.Escape(document.Title)).Append("<br><b>File IDS:</b> ")
          .Append(HtmlText.Escape(config.IdsPath)).Append("<br><b>Số phần tử soi:</b> ")
          .Append(check.ElementCount.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append("</p>");

        sb.Append("<p>Kiểm <b>trên mô hình Revit</b> theo ánh xạ Revit → IFC (category, <code>IfcExportAs</code>, "
                  + "property set của bộ xuất). Kết luận vì thế là <i>mô hình sẽ đạt khi xuất</i>, không thay cho một "
                  + "lượt kiểm trên chính file IFC đã nộp.</p>");

        sb.Append("<h2>Tổng hợp</h2><table><tr><th>Specification</th><th>Áp dụng cho</th><th>Đạt</th><th>Không đạt</th></tr>");
        foreach (var spec in check.Specifications)
        {
            sb.Append("<tr><td>").Append(HtmlText.Escape(spec.Name));
            if (spec.Description.Length > 0)
            {
                sb.Append("<br><small>").Append(HtmlText.Escape(spec.Description)).Append("</small>");
            }

            sb.Append("</td><td>");
            sb.Append(spec.NoApplicableElements
                ? "<span class=\"trong\">0 phần tử — không kiểm được gì</span>"
                : spec.Applicable.ToString(System.Globalization.CultureInfo.InvariantCulture) + " phần tử");
            sb.Append("</td><td class=\"dat\">").Append(spec.Passed.ToString(System.Globalization.CultureInfo.InvariantCulture))
              .Append("</td><td class=\"truot\">").Append(spec.Failures.Count.ToString(System.Globalization.CultureInfo.InvariantCulture))
              .Append("</td></tr>");
        }

        sb.Append("</table>");

        foreach (var spec in check.Specifications.Where(s => s.Failures.Count > 0))
        {
            sb.Append("<h2>").Append(HtmlText.Escape(spec.Name)).Append("</h2><table><tr><th>Phần tử</th><th>Không đạt vì</th></tr>");
            foreach (var failure in spec.Failures)
            {
                sb.Append("<tr><td>").Append(HtmlText.Escape(failure.Element)).Append("</td><td>")
                  .Append(HtmlText.Escape(failure.Reason)).Append("</td></tr>");
            }

            sb.Append("</table>");
            if (spec.Failures.Count >= IdsEvaluator.MaxFailuresPerSpecification)
            {
                sb.Append("<p><i>Danh sách cắt ở ")
                  .Append(IdsEvaluator.MaxFailuresPerSpecification.ToString(System.Globalization.CultureInfo.InvariantCulture))
                  .Append(" phần tử — sửa nhóm này rồi chạy lại.</i></p>");
            }
        }

        sb.Append("</body></html>");
        return sb.ToString();
    }
}
