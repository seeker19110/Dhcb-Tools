using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace DhcbTools.Shared.Logic.Ids
{
    /// <summary>
    /// Báo cáo HTML/CSV của một lượt kiểm IDS — dùng chung cho đường Revit (<c>IdsValidate</c>) và đường
    /// file IFC (<c>--verify-ids</c>), để hai đường ra <b>cùng một dạng báo cáo</b> và người đọc so được từng dòng.
    /// </summary>
    public static class IdsReport
    {
        /// <summary>Kiểm trên mô hình Revit — câu ranh giới nói kết luận là "sẽ đạt khi xuất".</summary>
        public const string RevitScopeNote =
            "Kiểm <b>trên mô hình Revit</b> theo ánh xạ Revit → IFC (category, <code>IfcExportAs</code>, "
            + "property set của bộ xuất). Kết luận vì thế là <i>mô hình sẽ đạt khi xuất</i>, không thay cho một "
            + "lượt kiểm trên chính file IFC đã nộp.";

        /// <summary>Kiểm trên file IFC — đúng thứ bên thẩm tra làm.</summary>
        public const string IfcScopeNote =
            "Kiểm <b>trên chính file IFC</b> — cùng đầu vào mà IfcTester/Solibri đọc, nên kết luận so được với "
            + "họ từng dòng. Phần tử ghi theo số hiệu <code>#id</code> trong file; muốn sửa thì tìm ngược về Revit "
            + "qua GlobalId hoặc Tag.";

        /// <summary>Dòng tổng kết dùng chung cho summary lệnh và dòng cuối console.</summary>
        public static string Summary(IdsCheckResult check, IReadOnlyList<string> schemaWarnings)
        {
            var failedSpecs = check.Specifications.Count(s => s.Failed > 0);
            return $"Kiểm {check.ElementCount} phần tử theo {check.Specifications.Count} specification: "
                   + $"{check.FailureCount} phần tử không đạt ở {failedSpecs} specification"
                   + (check.EmptySpecificationCount > 0 ? $", {check.EmptySpecificationCount} specification không có phần tử nào để kiểm" : string.Empty)
                   + (schemaWarnings.Count > 0 ? $"; file IDS lệch chuẩn ở {schemaWarnings.Count} chỗ (xem cảnh báo)" : string.Empty);
        }

        /// <summary>Các dòng thông điệp: cảnh báo lệch chuẩn (nếu có), rồi một dòng mỗi specification, rồi tối đa 20 phần tử không đạt.</summary>
        public static IEnumerable<string> Messages(IdsCheckResult check, IReadOnlyList<string> schemaWarnings)
        {
            if (schemaWarnings.Count > 0)
            {
                yield return $"⚠ File IDS lệch chuẩn IDS 1.0 ở {schemaWarnings.Count} chỗ — DHCB vẫn kiểm, nhưng IfcTester/Solibri có thể từ chối file này:";
                foreach (var warning in schemaWarnings)
                {
                    yield return "   • " + warning;
                }
            }

            foreach (var spec in check.Specifications)
            {
                var head = $"{spec.Name}: {spec.Passed}/{spec.Applicable} đạt";
                yield return spec.NoApplicableElements
                    ? $"{spec.Name}: KHÔNG phần tử nào lọt bộ lọc — con số này nói về bộ lọc hoặc về mô hình thiếu nhóm đó, không phải \"đạt\"."
                    : head + (spec.Failed > 0 ? $", {spec.Failed} phần tử không đạt" : string.Empty);
            }

            foreach (var failure in check.Specifications.SelectMany(s => s.Failures).Take(20))
            {
                yield return $"{failure.Specification} — {failure.Element}: {failure.Reason}";
            }
        }

        /// <summary>CSV: một dòng mỗi phần tử không đạt.</summary>
        public static string Csv(IdsCheckResult check)
        {
            var sb = new StringBuilder();
            sb.Append(CsvText.JoinLine(new[] { "Specification", "Phần tử", "Không đạt vì" })).Append("\r\n");
            foreach (var failure in check.Specifications.SelectMany(s => s.Failures))
            {
                sb.Append(CsvText.JoinLine(new[] { failure.Specification, failure.Element, failure.Reason })).Append("\r\n");
            }

            return sb.ToString();
        }

        /// <summary>Báo cáo HTML.</summary>
        /// <param name="modelTitle">Tên mô hình / file IFC.</param>
        /// <param name="idsPath">Đường dẫn file IDS.</param>
        /// <param name="scopeNote">Câu ranh giới (HTML) — <see cref="RevitScopeNote"/> hoặc <see cref="IfcScopeNote"/>.</param>
        /// <param name="check">Kết quả kiểm.</param>
        /// <param name="schemaWarnings">Cảnh báo lệch chuẩn từ <see cref="IdsSchemaLint"/>.</param>
        public static string Html(string modelTitle, string idsPath, string scopeNote, IdsCheckResult check, IReadOnlyList<string> schemaWarnings)
        {
            var sb = new StringBuilder();
            sb.Append("<!DOCTYPE html><html lang=\"vi\"><head><meta charset=\"utf-8\"><title>DHCB — Kiểm IDS</title>")
              .Append("<style>body{font-family:Segoe UI,Arial,sans-serif;margin:24px;color:#222}")
              .Append("table{border-collapse:collapse;width:100%;margin:12px 0}th,td{border:1px solid #ccc;padding:6px 8px;text-align:left;vertical-align:top}")
              .Append("th{background:#f2f2f2}.dat{color:#0a7d28}.truot{color:#b00020}.trong{color:#a06000}</style></head><body>")
              .Append("<h1>Kiểm mô hình theo IDS</h1><p><b>Mô hình:</b> ")
              .Append(HtmlText.Escape(modelTitle)).Append("<br><b>File IDS:</b> ")
              .Append(HtmlText.Escape(idsPath)).Append("<br><b>Số phần tử soi:</b> ")
              .Append(check.ElementCount.ToString(CultureInfo.InvariantCulture)).Append("</p>");

            sb.Append("<p>").Append(scopeNote).Append("</p>");

            if (schemaWarnings.Count > 0)
            {
                sb.Append("<h2 class=\"trong\">⚠ File IDS lệch chuẩn IDS 1.0</h2><p>DHCB vẫn kiểm được, nhưng IfcTester/Solibri "
                          + "đối chiếu theo XSD sẽ <b>từ chối file này</b> — sửa trước khi nộp cho bên thẩm tra.</p><ul>");
                foreach (var warning in schemaWarnings)
                {
                    sb.Append("<li>").Append(HtmlText.Escape(warning)).Append("</li>");
                }

                sb.Append("</ul>");
            }

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
                    : spec.Applicable.ToString(CultureInfo.InvariantCulture) + " phần tử");
                sb.Append("</td><td class=\"dat\">").Append(spec.Passed.ToString(CultureInfo.InvariantCulture))
                  .Append("</td><td class=\"truot\">").Append(spec.Failed.ToString(CultureInfo.InvariantCulture))
                  .Append("</td></tr>");
            }

            sb.Append("</table>");

            foreach (var spec in check.Specifications.Where(s => s.Failed > 0))
            {
                sb.Append("<h2>").Append(HtmlText.Escape(spec.Name)).Append("</h2><table><tr><th>Phần tử</th><th>Không đạt vì</th></tr>");
                foreach (var failure in spec.Failures)
                {
                    sb.Append("<tr><td>").Append(HtmlText.Escape(failure.Element)).Append("</td><td>")
                      .Append(HtmlText.Escape(failure.Reason)).Append("</td></tr>");
                }

                sb.Append("</table>");
                if (spec.FailuresTruncated)
                {
                    sb.Append("<p><i>Danh sách cắt ở ")
                      .Append(spec.Failures.Count.ToString(CultureInfo.InvariantCulture))
                      .Append(" trên ").Append(spec.Failed.ToString(CultureInfo.InvariantCulture))
                      .Append(" phần tử không đạt — sửa nhóm này rồi chạy lại.</i></p>");
                }
            }

            sb.Append("</body></html>");
            return sb.ToString();
        }
    }
}
