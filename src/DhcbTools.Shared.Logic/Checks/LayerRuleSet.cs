using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json;

namespace DhcbTools.Shared.Logic.Checks
{
    /// <summary>Một quy tắc đặt tên layer — layer hợp lệ nếu tên khớp ít nhất một pattern.</summary>
    public sealed class LayerNamingRule
    {
        public string Pattern { get; set; } = string.Empty;

        public string Description { get; set; } = string.Empty;
    }

    /// <summary>Dạng file bọc <c>{"rules":[...]}</c> — chính là dạng của <c>configs/layer-rules.sample.json</c>.</summary>
    public sealed class LayerRulesFile
    {
        public List<LayerNamingRule>? Rules { get; set; }
    }

    /// <summary>
    /// Đọc bộ quy tắc layer và dựng báo cáo HTML cho <c>LayerStandardCheck</c>. Tách khỏi Core.AutoCAD để
    /// file quy tắc hỏng/rỗng và HTML escape có test trên CI (trước đây chỉ chạy được trong accoreconsole).
    /// </summary>
    public static class LayerRuleSet
    {
        /// <summary>
        /// Chấp nhận cả hai dạng: mảng thuần <c>[{...}]</c> và object bọc <c>{"rules":[{...}]}</c>. Trả null
        /// khi JSON là <c>null</c> hoặc object không có <c>rules</c>; ném <see cref="JsonException"/> khi JSON hỏng.
        /// </summary>
        public static List<LayerNamingRule>? Parse(string json)
        {
            if (json == null) throw new ArgumentNullException(nameof(json));

            var trimmed = json.TrimStart();
            if (trimmed.StartsWith("{", StringComparison.Ordinal))
            {
                var wrapper = JsonConvert.DeserializeObject<LayerRulesFile>(json);
                return wrapper?.Rules;
            }

            return JsonConvert.DeserializeObject<List<LayerNamingRule>>(json);
        }

        /// <summary>Báo cáo HTML: quy tắc áp dụng + bảng layer hợp lệ / không đúng chuẩn.</summary>
        public static string Html(IReadOnlyList<string> allLayers, IEnumerable<string> invalidLayers, IReadOnlyList<LayerNamingRule> rules)
        {
            allLayers ??= Array.Empty<string>();
            rules ??= Array.Empty<LayerNamingRule>();
            var invalid = new HashSet<string>(invalidLayers ?? Array.Empty<string>(), StringComparer.Ordinal);

            var sb = new StringBuilder();
            sb.Append("<!DOCTYPE html><html><head><meta charset=\"utf-8\"><title>Kiểm tra chuẩn layer</title>")
              .Append("<style>body{font-family:Arial,sans-serif;margin:24px}table{border-collapse:collapse;width:100%}")
              .Append("th,td{border:1px solid #ccc;padding:6px 10px;text-align:left}th{background:#f0f0f0}")
              .Append(".invalid{background:#ffdddd;color:#a00}.valid{background:#eaffea}</style></head><body>");

            sb.Append("<h1>Báo cáo kiểm tra chuẩn layer</h1>");
            sb.Append("<p>Tổng số layer: ").Append(allLayers.Count).Append(" — Không đúng chuẩn: <b>").Append(invalid.Count).Append("</b></p>");

            sb.Append("<h2>Quy tắc áp dụng</h2><ul>");
            foreach (var rule in rules)
            {
                sb.Append("<li><code>").Append(HtmlText.Escape(rule.Pattern)).Append("</code> — ").Append(HtmlText.Escape(rule.Description)).Append("</li>");
            }
            sb.Append("</ul>");

            sb.Append("<h2>Danh sách layer</h2><table><tr><th>Tên layer</th><th>Trạng thái</th></tr>");
            foreach (var name in allLayers)
            {
                var isInvalid = invalid.Contains(name);
                sb.Append("<tr class=\"").Append(isInvalid ? "invalid" : "valid").Append("\"><td>")
                  .Append(HtmlText.Escape(name)).Append("</td><td>")
                  .Append(isInvalid ? "Không đúng chuẩn" : "Hợp lệ").Append("</td></tr>");
            }
            sb.Append("</table></body></html>");

            return sb.ToString();
        }
    }
}
