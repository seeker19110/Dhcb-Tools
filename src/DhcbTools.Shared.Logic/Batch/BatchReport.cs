using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace DhcbTools.Shared.Logic.Batch
{
    /// <summary>
    /// Báo cáo HTML tổng hợp sau mỗi lần chạy (mục 1.4): bảng file × step, ô xanh/đỏ, bấm mở chi tiết.
    /// Thuần chuỗi — dùng <see cref="HtmlText.Escape"/> cho mọi văn bản đến từ mô hình.
    /// </summary>
    public static class BatchReport
    {
        private const char KeySeparator = '\u001F';

        public static string Render(string jobName, IReadOnlyList<RunLogEntry> entries, DateTime generatedAt)
        {
            var files = entries.Select(e => e.File).Distinct().ToList();
            var commands = entries.Select(e => e.Command).Distinct().ToList();
            var byKey = new Dictionary<string, RunLogEntry>();
            foreach (var e in entries)
            {
                byKey[e.File + KeySeparator + e.Command] = e; // lần chạy sau ghi đè lần trước
            }

            var ok = entries.Count(e => e.Success && !e.Skipped);
            var failed = entries.Count(e => !e.Success && !e.Skipped);
            var skipped = entries.Count(e => e.Skipped);

            var sb = new StringBuilder();
            sb.Append("<!DOCTYPE html><html lang=\"vi\"><head><meta charset=\"utf-8\"><title>")
              .Append(HtmlText.Escape(jobName)).Append(" — DHCB batch</title><style>")
              .Append("body{font-family:Segoe UI,Arial,sans-serif;margin:24px;color:#222}")
              .Append("table{border-collapse:collapse}td,th{border:1px solid #ccc;padding:6px 10px;vertical-align:top}")
              .Append("th{background:#f3f3f3}.ok{background:#d9f2d9}.fail{background:#f8d0d0}.skip{background:#eee;color:#666}")
              .Append("details summary{cursor:pointer}pre{white-space:pre-wrap;font-size:12px;margin:4px 0 0}")
              .Append(".kpi{display:inline-block;margin-right:24px}")
              .Append("</style></head><body>");
            sb.Append("<h1>").Append(HtmlText.Escape(jobName)).Append("</h1>");
            sb.Append("<p>Tạo lúc ").Append(generatedAt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)).Append(" · ")
              .Append("<span class=\"kpi\">Thành công: ").Append(ok).Append("</span>")
              .Append("<span class=\"kpi\">Lỗi: ").Append(failed).Append("</span>")
              .Append("<span class=\"kpi\">Bỏ qua: ").Append(skipped).Append("</span></p>");

            sb.Append("<table><thead><tr><th>File</th>");
            foreach (var c in commands)
            {
                sb.Append("<th>").Append(HtmlText.Escape(c)).Append("</th>");
            }
            sb.Append("</tr></thead><tbody>");

            foreach (var f in files)
            {
                sb.Append("<tr><td>").Append(HtmlText.Escape(f)).Append("</td>");
                foreach (var c in commands)
                {
                    if (!byKey.TryGetValue(f + KeySeparator + c, out var e))
                    {
                        sb.Append("<td class=\"skip\">—</td>");
                        continue;
                    }

                    var cls = e.Skipped ? "skip" : e.Success ? "ok" : "fail";
                    sb.Append("<td class=\"").Append(cls).Append("\"><details><summary>")
                      .Append(HtmlText.Escape(e.Summary))
                      .Append(" <small>(").Append(e.ElapsedMs).Append(" ms)</small></summary>");
                    if (e.Messages.Count > 0)
                    {
                        sb.Append("<pre>").Append(HtmlText.Escape(string.Join("\n", e.Messages.Take(500)))).Append("</pre>");
                    }
                    if (e.Errors.Count > 0)
                    {
                        sb.Append("<pre style=\"color:#a00\">").Append(HtmlText.Escape(string.Join("\n", e.Errors))).Append("</pre>");
                    }
                    sb.Append("</details></td>");
                }
                sb.Append("</tr>");
            }

            sb.Append("</tbody></table></body></html>");
            return sb.ToString();
        }
    }
}
