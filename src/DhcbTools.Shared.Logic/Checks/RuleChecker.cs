using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;

namespace DhcbTools.Shared.Logic.Checks
{
    /// <summary>Một quy tắc kiểm tra tham số (mục 4.2). Dùng chung cho Revit (category/parameter) và AutoCAD (layer/attribute).</summary>
    public sealed class ParameterRule
    {
        /// <summary>Category Revit hoặc nhóm đối tượng AutoCAD ("Layer", "Block:DOOR"...).</summary>
        [JsonProperty("category")]
        public string Category { get; set; } = string.Empty;

        [JsonProperty("parameter")]
        public string Parameter { get; set; } = string.Empty;

        /// <summary>Bắt buộc có giá trị (không rỗng).</summary>
        [JsonProperty("required")]
        public bool Required { get; set; }

        /// <summary>Regex .NET giá trị phải khớp (bỏ qua khi rỗng).</summary>
        [JsonProperty("pattern")]
        public string? Pattern { get; set; }

        /// <summary>Danh sách giá trị cho phép (bỏ qua khi rỗng).</summary>
        [JsonProperty("allowedValues")]
        public List<string> AllowedValues { get; set; } = new List<string>();

        /// <summary>Mô tả hiển thị trong báo cáo.</summary>
        [JsonProperty("description")]
        public string? Description { get; set; }

        /// <summary>Mức: "error" (mặc định) hoặc "warning".</summary>
        [JsonProperty("severity")]
        public string Severity { get; set; } = "error";

        private Regex? _compiled;

        internal Regex? CompiledPattern
        {
            get
            {
                if (_compiled == null && !string.IsNullOrEmpty(Pattern))
                {
                    _compiled = new Regex(Pattern!, RegexOptions.CultureInvariant);
                }
                return _compiled;
            }
        }
    }

    public sealed class RuleViolation
    {
        public RuleViolation(string category, string elementId, string elementName, string parameter, string? value, string reason, string severity)
        {
            Category = category;
            ElementId = elementId;
            ElementName = elementName;
            Parameter = parameter;
            Value = value;
            Reason = reason;
            Severity = severity;
        }

        public string Category { get; }

        public string ElementId { get; }

        public string ElementName { get; }

        public string Parameter { get; }

        public string? Value { get; }

        public string Reason { get; }

        public string Severity { get; }
    }

    /// <summary>Đọc bộ quy tắc từ JSON (mảng hoặc {"rules":[...]}) và khớp từng giá trị. Thuần.</summary>
    public static class RuleChecker
    {
        public static List<ParameterRule> ParseRules(string json)
        {
            json = json.Trim();
            if (json.StartsWith("[", StringComparison.Ordinal))
            {
                return JsonConvert.DeserializeObject<List<ParameterRule>>(json) ?? new List<ParameterRule>();
            }

            var wrapper = JsonConvert.DeserializeAnonymousType(json, new { rules = new List<ParameterRule>() });
            return wrapper?.rules ?? new List<ParameterRule>();
        }

        /// <summary>Kiểm tra một giá trị theo một quy tắc. Trả về lý do vi phạm, hoặc null nếu hợp lệ.</summary>
        public static string? Check(ParameterRule rule, string? value)
        {
            var empty = string.IsNullOrWhiteSpace(value);
            if (rule.Required && empty)
            {
                return "thiếu giá trị";
            }

            if (empty)
            {
                return null; // không bắt buộc và để trống → hợp lệ
            }

            if (rule.AllowedValues.Count > 0 && !rule.AllowedValues.Any(a => string.Equals(a, value, StringComparison.OrdinalIgnoreCase)))
            {
                return "không nằm trong danh sách cho phép";
            }

            var regex = rule.CompiledPattern;
            if (regex != null && !regex.IsMatch(value!))
            {
                return "không khớp mẫu " + rule.Pattern;
            }

            return null;
        }

        /// <summary>Gom vi phạm theo category → số lượng, để báo cáo và để lớp AI tóm tắt.</summary>
        public static Dictionary<string, int> CountByCategory(IEnumerable<RuleViolation> violations)
        {
            var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var v in violations)
            {
                result[v.Category] = result.TryGetValue(v.Category, out var n) ? n + 1 : 1;
            }
            return result;
        }

        /// <summary>Báo cáo HTML cùng khuôn HealthReport.</summary>
        public static string RenderHtml(string title, IReadOnlyList<RuleViolation> violations, int checkedCount)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("<!DOCTYPE html><html lang=\"vi\"><head><meta charset=\"utf-8\"><title>").Append(HtmlText.Escape(title)).Append("</title>")
              .Append("<style>body{font-family:Segoe UI,Arial,sans-serif;margin:24px}table{border-collapse:collapse}td,th{border:1px solid #ccc;padding:4px 8px}th{background:#f3f3f3}.error{color:#a00}.warning{color:#a60}</style></head><body>");
            sb.Append("<h1>").Append(HtmlText.Escape(title)).Append("</h1>");
            sb.Append("<p>Đã kiểm ").Append(checkedCount).Append(" giá trị, ").Append(violations.Count).Append(" vi phạm.</p>");

            foreach (var kv in CountByCategory(violations).OrderByDescending(k => k.Value))
            {
                sb.Append("<li>").Append(HtmlText.Escape(kv.Key)).Append(": ").Append(kv.Value).Append("</li>");
            }

            sb.Append("<table><thead><tr><th>Category</th><th>Id</th><th>Tên</th><th>Tham số</th><th>Giá trị</th><th>Lý do</th></tr></thead><tbody>");
            foreach (var v in violations)
            {
                sb.Append("<tr class=\"").Append(HtmlText.Escape(v.Severity)).Append("\"><td>").Append(HtmlText.Escape(v.Category))
                  .Append("</td><td>").Append(HtmlText.Escape(v.ElementId))
                  .Append("</td><td>").Append(HtmlText.Escape(v.ElementName))
                  .Append("</td><td>").Append(HtmlText.Escape(v.Parameter))
                  .Append("</td><td>").Append(HtmlText.Escape(v.Value ?? string.Empty))
                  .Append("</td><td>").Append(HtmlText.Escape(v.Reason)).Append("</td></tr>");
            }
            sb.Append("</tbody></table></body></html>");
            return sb.ToString();
        }
    }
}
