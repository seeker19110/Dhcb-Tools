using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json;

namespace DhcbTools.Shared.Logic.Checks
{
    /// <summary>
    /// Quy tắc ngưỡng cho "checkset" mô hình (mục 7.7, học từ Autodesk Model Checker): một số đo của mô hình
    /// (số warning, số view chưa đặt, dung lượng MB, số family in-place, số link thiếu…) so với ngưỡng.
    /// Cùng file JSON với <see cref="ParameterRule"/>: <c>{"metric":"warnings","max":200,"severity":"error"}</c>.
    /// </summary>
    public sealed class ThresholdRule
    {
        /// <summary>Tên số đo. Revit: warnings, unplacedViews, fileSizeMb, inPlaceFamilies, missingLinks, elements, sheetsWithoutViews, unusedFamilies. AutoCAD: layers, emptyLayers, xrefsMissing, fileSizeMb, entities.</summary>
        [JsonProperty("metric")]
        public string Metric { get; set; } = string.Empty;

        [JsonProperty("max")]
        public double? Max { get; set; }

        [JsonProperty("min")]
        public double? Min { get; set; }

        [JsonProperty("severity")]
        public string Severity { get; set; } = "error";

        [JsonProperty("description")]
        public string? Description { get; set; }

        /// <summary>Lý do vi phạm hoặc null nếu đạt.</summary>
        public string? Check(double value)
        {
            if (Max.HasValue && value > Max.Value)
            {
                return NumericText.Format(value, 1) + " > ngưỡng tối đa " + NumericText.Format(Max.Value, 1);
            }

            if (Min.HasValue && value < Min.Value)
            {
                return NumericText.Format(value, 1) + " < ngưỡng tối thiểu " + NumericText.Format(Min.Value, 1);
            }

            return null;
        }

        /// <summary>Đọc các quy tắc ngưỡng từ cùng file JSON của RuleChecker (mảng hoặc {"thresholds":[...]}, hoặc phần tử có "metric" trong "rules").</summary>
        public static List<ThresholdRule> Parse(string json)
        {
            json = json.Trim();
            var result = new List<ThresholdRule>();
            Newtonsoft.Json.Linq.JToken root;
            try
            {
                root = Newtonsoft.Json.Linq.JToken.Parse(json);
            }
            catch (JsonException)
            {
                return result;
            }

            IEnumerable<Newtonsoft.Json.Linq.JToken> items;
            if (root is Newtonsoft.Json.Linq.JArray arr)
            {
                items = arr;
            }
            else if (root is Newtonsoft.Json.Linq.JObject obj)
            {
                var list = new List<Newtonsoft.Json.Linq.JToken>();
                if (obj["thresholds"] is Newtonsoft.Json.Linq.JArray t) list.AddRange(t);
                if (obj["rules"] is Newtonsoft.Json.Linq.JArray r) list.AddRange(r);
                items = list;
            }
            else
            {
                return result;
            }

            foreach (var item in items)
            {
                if (item is Newtonsoft.Json.Linq.JObject o && o["metric"] != null)
                {
                    var rule = o.ToObject<ThresholdRule>();
                    if (rule != null && !string.IsNullOrWhiteSpace(rule.Metric))
                    {
                        result.Add(rule);
                    }
                }
            }

            return result;
        }

        /// <summary>Đánh giá toàn bộ quy tắc trên bảng số đo; số đo thiếu → ghi cảnh báo, không vi phạm.</summary>
        public static List<RuleViolation> Evaluate(IEnumerable<ThresholdRule> rules, IDictionary<string, double> metrics, List<string> notes)
        {
            var violations = new List<RuleViolation>();
            foreach (var rule in rules)
            {
                if (!metrics.TryGetValue(rule.Metric, out var value))
                {
                    notes.Add("Số đo \"" + rule.Metric + "\" không có — bỏ qua ngưỡng.");
                    continue;
                }

                var reason = rule.Check(value);
                if (reason != null)
                {
                    violations.Add(new RuleViolation("Model", "-", rule.Metric, "threshold", value.ToString(CultureInfo.InvariantCulture), reason + (rule.Description != null ? " — " + rule.Description : string.Empty), rule.Severity));
                }
            }

            return violations;
        }
    }
}
