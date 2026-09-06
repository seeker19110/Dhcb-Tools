using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace DhcbTools.Shared.Logic.Progress
{
    /// <summary>Cách gộp của báo cáo tiến độ.</summary>
    public enum ProgressGroupBy
    {
        Level,
        System,
        Category,
    }

    /// <summary>
    /// Phần quyết định và phần dựng báo cáo của lệnh <c>ProgressReport</c>, tách khỏi Revit.
    /// <para>
    /// Vì sao: báo cáo này đi lên bàn ban chỉ huy công trường và lệnh vẫn mang nhãn 🧪 "chưa chạy thật";
    /// toàn bộ HTML, Summary, và các câu cảnh báo (0 % vì thiếu tham số ≠ 0 % vì chưa lắp) trước đây
    /// nằm trong Core, chỉ kiểm được khi mở Revit. Ở đây mọi thứ nhận số/chuỗi/StatusRollRow và trả
    /// chuỗi, nên từng câu có assert trên CI. Core chỉ còn đọc tham số phần tử.
    /// </para>
    /// </summary>
    public static class ProgressReportLogic
    {
        /// <summary>Số giá trị trạng thái không đọc được tối đa liệt kê.</summary>
        public const int MaxUnreadableListed = 20;

        // ── Config ──────────────────────────────────────────────────────────────

        /// <summary><c>groupBy</c>: Level (mặc định khi rỗng), System, Category — sai thì báo rõ.</summary>
        public static bool TryParseGroupBy(string? raw, out ProgressGroupBy groupBy, out string error)
        {
            var text = (raw ?? "Level").Trim();
            if (text.Length == 0)
            {
                text = "Level";
            }

            error = string.Empty;
            if (text.Equals("Level", StringComparison.OrdinalIgnoreCase))
            {
                groupBy = ProgressGroupBy.Level;
                return true;
            }

            if (text.Equals("System", StringComparison.OrdinalIgnoreCase))
            {
                groupBy = ProgressGroupBy.System;
                return true;
            }

            if (text.Equals("Category", StringComparison.OrdinalIgnoreCase))
            {
                groupBy = ProgressGroupBy.Category;
                return true;
            }

            groupBy = ProgressGroupBy.Level;
            error = $"groupBy \"{raw}\" không hợp lệ. Hợp lệ: Level (tầng), System (hệ), Category.";
            return false;
        }

        /// <summary>Tiêu đề cột nhóm trong HTML/CSV.</summary>
        public static string GroupHeader(ProgressGroupBy groupBy)
        {
            switch (groupBy)
            {
                case ProgressGroupBy.System: return "Hệ";
                case ProgressGroupBy.Category: return "Category";
                default: return "Tầng";
            }
        }

        /// <summary>
        /// Tên nhóm của một phần tử. Thiếu thông tin thì vào nhóm "(không …)" thay vì rơi khỏi báo cáo —
        /// phần tử không có tầng vẫn là phần tử phải lắp.
        /// </summary>
        public static string GroupOf(ProgressGroupBy groupBy, string? levelName, string? systemName, string? categoryName)
        {
            switch (groupBy)
            {
                case ProgressGroupBy.System:
                    return StringGuard.IsBlank(systemName) ? "(không hệ)" : systemName!;
                case ProgressGroupBy.Category:
                    return StringGuard.IsBlank(categoryName) ? "(không category)" : categoryName!;
                default:
                    return StringGuard.IsBlank(levelName) ? "(không tầng)" : levelName!;
            }
        }

        /// <summary>Lỗi khi tên category không có trong mô hình.</summary>
        public static string UnknownCategoriesError(IReadOnlyList<string> unknown)
            => "Category không có: " + string.Join(", ", unknown ?? Array.Empty<string>()) + ".";

        /// <summary>Một dòng cho danh sách giá trị trạng thái không đọc được: <c>id: "giá trị"</c>.</summary>
        public static string UnreadableEntry(long elementId, string? text) => $"{elementId}: \"{text}\"";

        /// <summary>
        /// Không phần tử nào MANG tham số trạng thái = chưa gắn shared parameter, không phải "chưa lắp gì".
        /// Báo cáo 0 % lúc đó nói về tham số chứ không nói về công trường — phải chặn, không được xuất.
        /// </summary>
        public static string NoStatusParameterMessage(string lookupHint, int itemCount)
            => lookupHint
               + $" Không phần tử nào trong {itemCount} phần tử của phạm vi có tham số này, nên báo cáo sẽ là "
               + "0 % cho mọi nhóm — con số đó nói về tham số chứ không nói về công trường. Gắn shared parameter "
               + "cho các category cần theo dõi, hoặc chạy DictionaryLearn để lấy tên thật của dự án.";

        // ── Summary & Messages ──────────────────────────────────────────────────

        /// <summary>Summary: % đã lắp trở lên theo số lượng (và theo chiều dài nếu có), số nhóm, file ra.</summary>
        public static string Summary(StatusRollRow total, int groupCount, ProgressGroupBy groupBy, string outputPath)
        {
            if (total == null)
            {
                throw new ArgumentNullException(nameof(total));
            }

            return $"Tiến độ {NumericText.Format(total.PercentAtLeast(ConstructionStage.DaLap), 1)}% đã lắp trở lên "
                   + $"({total.CountAtLeast(ConstructionStage.DaLap)}/{total.Total} cấu kiện"
                   + (total.HasLength ? $", {NumericText.Format(total.PercentByLengthAtLeast(ConstructionStage.DaLap), 1)}% theo chiều dài" : string.Empty)
                   + $"), {groupCount} nhóm theo {GroupHeader(groupBy).ToLowerInvariant()} → \"{outputPath}\".";
        }

        /// <summary>Các dòng Messages sau Summary, đúng thứ tự cũ.</summary>
        public static List<string> Notes(
            StatusRollRow total, ProgressSeries series, IReadOnlyList<string> unreadable, int filtered, string? csvPath)
        {
            if (total == null)
            {
                throw new ArgumentNullException(nameof(total));
            }

            if (series == null)
            {
                throw new ArgumentNullException(nameof(series));
            }

            var notes = new List<string>
            {
                $"Đã nghiệm thu: {total.CountOf(ConstructionStage.DaNghiemThu)} · đã lắp: {total.CountOf(ConstructionStage.DaLap)} "
                + $"· đang lắp: {total.CountOf(ConstructionStage.DangLap)} · chưa lắp: {total.CountOf(ConstructionStage.ChuaLap)}.",
            };

            if (total.NoDataCount > 0)
            {
                notes.Add($"{total.NoDataCount}/{total.Total} cấu kiện chưa ai ghi nhận trạng thái — "
                          + "vẫn nằm trong mẫu số của phần trăm (chưa nhập thì chưa lắp).");
            }

            if (series.ReachedWithoutDate > 0)
            {
                notes.Add($"{series.ReachedWithoutDate} cấu kiện đã lắp nhưng không có ngày nên không lên được biểu đồ tuần.");
            }

            if (unreadable != null && unreadable.Count > 0)
            {
                notes.Add($"Giá trị trạng thái không đọc được ở {unreadable.Count} phần tử (đếm như chưa có dữ liệu): "
                          + string.Join("; ", unreadable));
            }

            if (filtered > 0)
            {
                notes.Add($"{filtered} phần tử ngoài bộ lọc tầng/hệ.");
            }

            if (!StringGuard.IsBlank(csvPath))
            {
                notes.Add($"CSV: \"{csvPath}\".");
            }

            return notes;
        }

        // ── HTML ────────────────────────────────────────────────────────────────

        /// <summary>
        /// Báo cáo HTML. <paramref name="now"/> tiêm vào để test ghim được dòng "Lập lúc";
        /// <paramref name="statusParameterLabel"/> là tên tham số người dùng khai hoặc null (theo từ điển).
        /// </summary>
        public static string Html(
            string? documentTitle, string? statusParameterLabel, ProgressGroupBy groupBy,
            IReadOnlyList<StatusRollRow> rows, StatusRollRow total, ProgressSeries series,
            IReadOnlyList<string> unreadable, DateTime now)
        {
            if (rows == null)
            {
                throw new ArgumentNullException(nameof(rows));
            }

            if (total == null)
            {
                throw new ArgumentNullException(nameof(total));
            }

            if (series == null)
            {
                throw new ArgumentNullException(nameof(series));
            }

            var groupHeader = GroupHeader(groupBy);
            var title = HtmlText.Escape(documentTitle ?? string.Empty);
            var sb = new StringBuilder();
            sb.Append("<!DOCTYPE html><html lang=\"vi\"><head><meta charset=\"utf-8\"><title>Tiến độ thi công — ")
              .Append(title).Append("</title><style>")
              .Append("body{font-family:Segoe UI,Arial,sans-serif;margin:24px;color:#222}")
              .Append("table{border-collapse:collapse;margin:12px 0}td,th{border:1px solid #ccc;padding:4px 10px;text-align:right}")
              .Append("th{background:#f3f3f3}td:first-child,th:first-child{text-align:left}")
              .Append(".bar{display:inline-block;height:12px;background:#2e7d32;vertical-align:middle}")
              .Append(".bar-bg{display:inline-block;width:120px;height:12px;background:#e0e0e0;vertical-align:middle}")
              .Append(".note{color:#8a6d3b;background:#fcf8e3;padding:8px 12px;border-left:4px solid #d9c07a;margin:8px 0}")
              .Append("</style></head><body>");

            sb.Append("<h1>Tiến độ thi công — ").Append(title).Append("</h1>")
              .Append("<p>Lập lúc ").Append(now.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture))
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

            if (unreadable != null && unreadable.Count > 0)
            {
                sb.Append("<div class=\"note\">Giá trị trạng thái không đọc được ở ").Append(unreadable.Count)
                  .Append(" phần tử, đếm như chưa có dữ liệu: ").Append(HtmlText.Escape(string.Join("; ", unreadable))).Append("</div>");
            }

            sb.Append("<p style=\"color:#666;font-size:12px\">DHCB Tools · tham số trạng thái: ")
              .Append(HtmlText.Escape(statusParameterLabel ?? "(theo từ điển constructionStatus)"))
              .Append("</p></body></html>");

            return sb.ToString();
        }
    }
}
