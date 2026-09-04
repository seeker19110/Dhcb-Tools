using System.Globalization;
using System.Text;
using Autodesk.Revit.DB;
using DhcbTools.Core.Checks;
using DhcbTools.Core.MEPF;
using DhcbTools.Shared.Logic;
using DhcbTools.Shared.Logic.Checks;
using DhcbTools.Shared.Logic.Progress;

namespace DhcbTools.Core.Progress;

/// <summary>
/// Đề xuất B1: báo cáo tiến độ thi công đọc thẳng từ mô hình — % theo <b>số lượng</b> và theo
/// <b>chiều dài</b>, gộp theo tầng / hệ / category, kèm chuỗi luỹ kế theo tuần. Chỉ đọc.
/// </summary>
public sealed class ProgressReportConfig
{
    /// <summary>File HTML báo cáo.</summary>
    public required string OutputPath { get; init; }

    /// <summary>CSV cùng nội dung để đưa vào bảng tiến độ của ban chỉ huy (tuỳ chọn).</summary>
    public string? CsvPath { get; init; }

    /// <summary>Category cần tính; rỗng = nhóm MEP + thiết bị mặc định.</summary>
    public List<string> Categories { get; init; } = new List<string>();

    /// <summary>Gộp theo: <c>Level</c> (mặc định) | <c>System</c> | <c>Category</c>.</summary>
    public string GroupBy { get; init; } = "Level";

    /// <summary>Tên tham số trạng thái; rỗng = từ điển <c>constructionStatus</c>.</summary>
    public string? StatusParameter { get; init; }

    /// <summary>Tham số ngày để dựng chuỗi theo tuần; rỗng = từ điển <c>constructionDate</c>.</summary>
    public string? DateParameter { get; init; }

    public string? LevelName { get; init; }

    public string? SystemContains { get; init; }
}

public sealed class ProgressReportCommand : ICoreCommand<ProgressReportConfig>
{
    public string CommandName => "ProgressReport";

    private static readonly BuiltInCategory[] DefaultCategories =
    {
        BuiltInCategory.OST_PipeCurves, BuiltInCategory.OST_DuctCurves, BuiltInCategory.OST_CableTray, BuiltInCategory.OST_Conduit,
        BuiltInCategory.OST_MechanicalEquipment, BuiltInCategory.OST_PlumbingFixtures, BuiltInCategory.OST_Sprinklers,
        BuiltInCategory.OST_ElectricalEquipment, BuiltInCategory.OST_ElectricalFixtures, BuiltInCategory.OST_LightingFixtures,
        BuiltInCategory.OST_DuctTerminal,
    };

    public CommandResult Execute(Document document, ProgressReportConfig config)
    {
        var groupBy = (config.GroupBy ?? "Level").Trim();
        if (!groupBy.Equals("Level", StringComparison.OrdinalIgnoreCase)
            && !groupBy.Equals("System", StringComparison.OrdinalIgnoreCase)
            && !groupBy.Equals("Category", StringComparison.OrdinalIgnoreCase))
        {
            return CommandResult.Fail($"groupBy \"{config.GroupBy}\" không hợp lệ. Hợp lệ: Level (tầng), System (hệ), Category.");
        }

        ICollection<ElementId> categoryIds;
        if (config.Categories.Count > 0)
        {
            categoryIds = ParameterSync.ParameterExportCommand.ResolveCategoryIds(document, config.Categories, out var unknown);
            if (unknown.Count > 0)
            {
                return CommandResult.Fail("Category không có: " + string.Join(", ", unknown) + ".");
            }
        }
        else
        {
            categoryIds = DefaultCategories.Select(c => new ElementId(c)).ToList();
        }

        var elements = new FilteredElementCollector(document)
            .WhereElementIsNotElementType()
            .WherePasses(new ElementMulticategoryFilter(categoryIds.ToList()))
            .ToElements();

        var items = new List<StatusItem>();
        var withStatusParameter = 0;
        var unreadable = new List<string>();
        var filtered = 0;

        foreach (var element in elements)
        {
            var levelName = LevelNameOf(document, element);
            if (!string.IsNullOrWhiteSpace(config.LevelName)
                && !string.Equals(levelName, config.LevelName, StringComparison.OrdinalIgnoreCase))
            {
                filtered++;
                continue;
            }

            var system = MepParams.SystemNameOrType(element);
            if (!string.IsNullOrWhiteSpace(config.SystemContains)
                && system.IndexOf(config.SystemContains!, StringComparison.OrdinalIgnoreCase) < 0)
            {
                filtered++;
                continue;
            }

            var group = groupBy.Equals("System", StringComparison.OrdinalIgnoreCase)
                ? (system.Length == 0 ? "(không hệ)" : system)
                : groupBy.Equals("Category", StringComparison.OrdinalIgnoreCase)
                    ? element.Category?.Name ?? "(không category)"
                    : (levelName.Length == 0 ? "(không tầng)" : levelName);

            var stage = ConstructionStage.ChuaCoDuLieu;
            var parameter = RevitCompat.Lookup(element, "constructionStatus", config.StatusParameter);
            if (parameter != null)
            {
                withStatusParameter++;
                var text = parameter.StorageType == StorageType.String
                    ? parameter.AsString()
                    : parameter.AsValueString();

                if (!ConstructionStatusValue.TryParse(text, out stage))
                {
                    // Giá trị lạ trong mô hình KHÔNG được lặng lẽ tính là "chưa lắp": nói ra để kỹ sư sửa.
                    stage = ConstructionStage.ChuaCoDuLieu;
                    if (unreadable.Count < 20)
                    {
                        unreadable.Add($"{RevitCompat.IdValue(element.Id)}: \"{text}\"");
                    }
                }
            }

            double lengthMm = 0;
            if (element is MEPCurve)
            {
                var lengthParameter = element.get_Parameter(BuiltInParameter.CURVE_ELEM_LENGTH);
                if (lengthParameter != null && lengthParameter.HasValue)
                {
                    lengthMm = RevitCompat.FtToMm(lengthParameter.AsDouble());
                }
            }

            items.Add(new StatusItem(group, stage, lengthMm, DateOf(element, config), RevitCompat.IdValue(element.Id)));
        }

        var result = CommandResult.Ok(string.Empty);
        var precondition = Precondition.NonEmptyInput(
            CommandName, "phần tử nào theo categories/levelName/systemContains", items.Count,
            "Kiểm lại bộ lọc; tra category có thật bằng query elements.");
        if (RevitPrecondition.Blocks(precondition, result))
        {
            return result;
        }

        // Không phần tử nào MANG tham số trạng thái = chưa gắn shared parameter, không phải "chưa lắp gì".
        if (withStatusParameter == 0)
        {
            return CommandResult.Fail(
                RevitCompat.LookupFailed("constructionStatus", config.StatusParameter)
                + $" Không phần tử nào trong {items.Count} phần tử của phạm vi có tham số này, nên báo cáo sẽ là "
                + "0 % cho mọi nhóm — con số đó nói về tham số chứ không nói về công trường. Gắn shared parameter "
                + "cho các category cần theo dõi, hoặc chạy DictionaryLearn để lấy tên thật của dự án.");
        }

        var rows = StatusRoll.By(items);
        var total = StatusRoll.Total(items);
        var series = WeeklyProgress.Series(items);

        var groupHeader = groupBy.Equals("System", StringComparison.OrdinalIgnoreCase) ? "Hệ"
            : groupBy.Equals("Category", StringComparison.OrdinalIgnoreCase) ? "Category" : "Tầng";

        RevitCompat.EnsureParentDirectory(config.OutputPath);
        File.WriteAllText(config.OutputPath, BuildHtml(document, config, groupHeader, rows, total, series, unreadable), Encoding.UTF8);

        if (!string.IsNullOrWhiteSpace(config.CsvPath))
        {
            RevitCompat.EnsureParentDirectory(config.CsvPath);
            var csvRows = rows.ToList();
            csvRows.Add(total);
            File.WriteAllText(config.CsvPath!, ProgressCsv.WriteReport(csvRows, groupHeader), CsvText.Utf8WithBom);
        }

        result.Summary =
            $"Tiến độ {NumericText.Format(total.PercentAtLeast(ConstructionStage.DaLap), 1)}% đã lắp trở lên "
            + $"({total.CountAtLeast(ConstructionStage.DaLap)}/{total.Total} cấu kiện"
            + (total.HasLength ? $", {NumericText.Format(total.PercentByLengthAtLeast(ConstructionStage.DaLap), 1)}% theo chiều dài" : string.Empty)
            + $"), {rows.Count} nhóm theo {groupHeader.ToLowerInvariant()} → \"{config.OutputPath}\".";
        result.AffectedCount = items.Count;

        result.Messages.Add($"Đã nghiệm thu: {total.CountOf(ConstructionStage.DaNghiemThu)} · đã lắp: {total.CountOf(ConstructionStage.DaLap)} "
            + $"· đang lắp: {total.CountOf(ConstructionStage.DangLap)} · chưa lắp: {total.CountOf(ConstructionStage.ChuaLap)}.");

        if (total.NoDataCount > 0)
        {
            result.Messages.Add($"{total.NoDataCount}/{total.Total} cấu kiện chưa ai ghi nhận trạng thái — "
                + "vẫn nằm trong mẫu số của phần trăm (chưa nhập thì chưa lắp).");
        }

        if (series.ReachedWithoutDate > 0)
        {
            result.Messages.Add($"{series.ReachedWithoutDate} cấu kiện đã lắp nhưng không có ngày nên không lên được biểu đồ tuần.");
        }

        if (unreadable.Count > 0)
        {
            result.Messages.Add($"Giá trị trạng thái không đọc được ở {unreadable.Count} phần tử (đếm như chưa có dữ liệu): "
                + string.Join("; ", unreadable));
        }

        if (filtered > 0)
        {
            result.Messages.Add($"{filtered} phần tử ngoài bộ lọc tầng/hệ.");
        }

        if (!string.IsNullOrWhiteSpace(config.CsvPath))
        {
            result.Messages.Add($"CSV: \"{config.CsvPath}\".");
        }

        return result;
    }

    private static DateTime? DateOf(Element element, ProgressReportConfig config)
    {
        var parameter = RevitCompat.Lookup(element, "constructionDate", config.DateParameter);
        if (parameter == null || !parameter.HasValue)
        {
            return null;
        }

        var text = parameter.StorageType == StorageType.String ? parameter.AsString() : parameter.AsValueString();
        return ProgressCsv.TryParseDate(text, out var date) ? date : (DateTime?)null;
    }

    private static string LevelNameOf(Document document, Element element)
    {
        try
        {
            if (element.LevelId != null && element.LevelId != ElementId.InvalidElementId
                && document.GetElement(element.LevelId) is Level direct)
            {
                return direct.Name;
            }

            var parameter = RevitCompat.Lookup(element, "level")
                ?? element.get_Parameter(BuiltInParameter.RBS_START_LEVEL_PARAM)
                ?? element.get_Parameter(BuiltInParameter.FAMILY_LEVEL_PARAM)
                ?? element.get_Parameter(BuiltInParameter.SCHEDULE_LEVEL_PARAM);

            if (parameter?.StorageType == StorageType.ElementId && document.GetElement(parameter.AsElementId()) is Level viaParameter)
            {
                return viaParameter.Name;
            }
        }
        catch (Exception)
        {
        }

        return string.Empty;
    }

    private static string BuildHtml(
        Document document, ProgressReportConfig config, string groupHeader,
        List<StatusRollRow> rows, StatusRollRow total, ProgressSeries series, List<string> unreadable)
    {
        var sb = new StringBuilder();
        sb.Append("<!DOCTYPE html><html lang=\"vi\"><head><meta charset=\"utf-8\"><title>Tiến độ thi công — ")
          .Append(HtmlText.Escape(document.Title)).Append("</title><style>")
          .Append("body{font-family:Segoe UI,Arial,sans-serif;margin:24px;color:#222}")
          .Append("table{border-collapse:collapse;margin:12px 0}td,th{border:1px solid #ccc;padding:4px 10px;text-align:right}")
          .Append("th{background:#f3f3f3}td:first-child,th:first-child{text-align:left}")
          .Append(".bar{display:inline-block;height:12px;background:#2e7d32;vertical-align:middle}")
          .Append(".bar-bg{display:inline-block;width:120px;height:12px;background:#e0e0e0;vertical-align:middle}")
          .Append(".note{color:#8a6d3b;background:#fcf8e3;padding:8px 12px;border-left:4px solid #d9c07a;margin:8px 0}")
          .Append("</style></head><body>");

        sb.Append("<h1>Tiến độ thi công — ").Append(HtmlText.Escape(document.Title)).Append("</h1>")
          .Append("<p>Lập lúc ").Append(DateTime.Now.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture))
          .Append(" · gộp theo ").Append(HtmlText.Escape(groupHeader.ToLowerInvariant()))
          .Append(" · ").Append(total.Total).Append(" cấu kiện trong phạm vi</p>");

        sb.Append("<p><strong>").Append(NumericText.Format(total.PercentAtLeast(ConstructionStage.DaLap), 1))
          .Append("% đã lắp trở lên</strong> (").Append(total.CountAtLeast(ConstructionStage.DaLap)).Append('/').Append(total.Total)
          .Append(" cấu kiện)");
        if (total.HasLength)
        {
            sb.Append(" · <strong>").Append(NumericText.Format(total.PercentByLengthAtLeast(ConstructionStage.DaLap), 1))
              .Append("% theo chiều dài</strong> (").Append(NumericText.Format(total.LengthMmAtLeast(ConstructionStage.DaLap) / 1000.0, 1))
              .Append('/').Append(NumericText.Format(total.TotalLengthMm / 1000.0, 1)).Append(" m)");
        }

        sb.Append(" · ").Append(NumericText.Format(total.PercentAtLeast(ConstructionStage.DaNghiemThu), 1)).Append("% đã nghiệm thu</p>");

        if (total.NoDataCount > 0)
        {
            sb.Append("<div class=\"note\">").Append(total.NoDataCount).Append('/').Append(total.Total)
              .Append(" cấu kiện <strong>chưa ai ghi nhận trạng thái</strong>. Chúng vẫn nằm trong mẫu số: chưa nhập thì chưa lắp. "
                    + "Phần trăm ở trên vì thế là tiến độ thật của phạm vi, không phải tiến độ của riêng phần đã nhập.</div>");
        }

        sb.Append("<h2>Theo ").Append(HtmlText.Escape(groupHeader.ToLowerInvariant())).Append("</h2><table><thead><tr><th>")
          .Append(HtmlText.Escape(groupHeader)).Append("</th><th>Tổng</th>");
        foreach (var stage in ConstructionStatusValue.Stages)
        {
            sb.Append("<th>").Append(HtmlText.Escape(ConstructionStatusValue.CanonicalOf(stage))).Append("</th>");
        }

        sb.Append("<th>Chưa có dữ liệu</th><th>% đã lắp</th><th>% theo chiều dài</th><th></th></tr></thead><tbody>");

        foreach (var row in rows.Concat(new[] { total }))
        {
            var percent = row.PercentAtLeast(ConstructionStage.DaLap);
            sb.Append("<tr><td>").Append(HtmlText.Escape(row.Group == total.Group ? "Tổng" : row.Group))
              .Append("</td><td>").Append(row.Total).Append("</td>");
            foreach (var stage in ConstructionStatusValue.Stages)
            {
                sb.Append("<td>").Append(row.CountOf(stage)).Append("</td>");
            }

            sb.Append("<td>").Append(row.NoDataCount).Append("</td>")
              .Append("<td>").Append(NumericText.Format(percent, 1)).Append("</td>")
              .Append("<td>").Append(row.HasLength ? NumericText.Format(row.PercentByLengthAtLeast(ConstructionStage.DaLap), 1) : "—").Append("</td>")
              .Append("<td><span class=\"bar-bg\"><span class=\"bar\" style=\"width:")
              .Append(NumericText.Format(percent * 1.2, 0)).Append("px\"></span></span></td></tr>");
        }

        sb.Append("</tbody></table>");

        sb.Append("<h2>Luỹ kế theo tuần (đã lắp trở lên)</h2>");
        if (series.Weeks.Count == 0)
        {
            sb.Append("<p>Không có cấu kiện nào vừa đạt mức đã lắp vừa có ngày ghi nhận, nên chưa dựng được chuỗi theo tuần.</p>");
        }
        else
        {
            sb.Append("<table><thead><tr><th>Tuần bắt đầu</th><th>Trong tuần</th><th>Luỹ kế</th><th>% luỹ kế</th><th></th></tr></thead><tbody>");
            foreach (var week in series.Weeks)
            {
                sb.Append("<tr><td>").Append(week.Label).Append("</td><td>").Append(week.Added)
                  .Append("</td><td>").Append(week.Cumulative).Append("</td><td>")
                  .Append(NumericText.Format(week.CumulativePercent, 1)).Append("</td>")
                  .Append("<td><span class=\"bar-bg\"><span class=\"bar\" style=\"width:")
                  .Append(NumericText.Format(week.CumulativePercent * 1.2, 0)).Append("px\"></span></span></td></tr>");
            }

            sb.Append("</tbody></table>");
        }

        if (series.ReachedWithoutDate > 0)
        {
            sb.Append("<div class=\"note\">").Append(series.ReachedWithoutDate)
              .Append(" cấu kiện đã lắp nhưng <strong>không có ngày</strong> nên không nằm trên đường luỹ kế — "
                    + "đường tuần vì thế thấp hơn tổng ở bảng trên.</div>");
        }

        if (unreadable.Count > 0)
        {
            sb.Append("<div class=\"note\">Giá trị trạng thái không đọc được ở ").Append(unreadable.Count)
              .Append(" phần tử, đếm như chưa có dữ liệu: ").Append(HtmlText.Escape(string.Join("; ", unreadable))).Append("</div>");
        }

        sb.Append("<p style=\"color:#666;font-size:12px\">DHCB Tools · tham số trạng thái: ")
          .Append(HtmlText.Escape(config.StatusParameter ?? "(theo từ điển constructionStatus)"))
          .Append("</p></body></html>");

        return sb.ToString();
    }
}
