using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace DhcbTools.Shared.Logic.Ai
{
    /// <summary>Đề xuất lệnh từ câu tiếng Việt (mục 5.4). KHÔNG thực thi — chỉ trả về để kỹ sư xác nhận.</summary>
    public sealed class CommandIntent
    {
        public CommandIntent(string? command, JObject config, double confidence, string explanation, IReadOnlyList<string> alternatives)
        {
            Command = command;
            Config = config;
            Confidence = confidence;
            Explanation = explanation;
            Alternatives = alternatives;
        }

        /// <summary>Tên lệnh trong whitelist <see cref="CommandCatalog"/>, hoặc null nếu không hiểu.</summary>
        public string? Command { get; }

        /// <summary>Config đề xuất — luôn có <c>dryRun:true</c> với lệnh ghi.</summary>
        public JObject Config { get; }

        public double Confidence { get; }

        public string Explanation { get; }

        public IReadOnlyList<string> Alternatives { get; }

        public object ToPayload() => new
        {
            command = Command,
            config = Config,
            confidence = Confidence,
            explanation = Explanation,
            alternatives = Alternatives,
            requiresConfirmation = true,
        };
    }

    /// <summary>
    /// Dịch câu lệnh tiếng Việt/Anh sang (lệnh, config) bằng từ khoá + trích số liệu — offline, không model.
    /// Mô hình ngôn ngữ (nếu có, qua Ollama) chỉ được dùng để CHỌN trong whitelist, không được sinh lệnh mới;
    /// vì vậy đầu ra của parser này cũng là "lưới an toàn" cho đầu ra của model.
    /// </summary>
    public static class CommandIntentParser
    {
        /// <summary>
        /// Số kèm đơn vị tuỳ chọn. Nhận cả "2.000"/"2,000" (nghìn kiểu Việt/Âu) lẫn "2,5"/"2.5" (thập phân) —
        /// phân định ở <see cref="TryParseNumber"/>. Đơn vị phải là từ trọn vẹn: "2 max" không được đọc thành "2 m".
        /// </summary>
        private static readonly Regex Number = new Regex(@"(?<![\w.,])(?<v>\d+(?:[.,]\d+)*)\s*(?<u>mm|cm|mét|met|m)?(?![\p{L}\d])", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>Từ đứng ngay trước một số khiến số đó KHÔNG phải kích thước: "tầng 2", "level 3", "số 5".</summary>
        private static readonly Regex OrdinalWord = new Regex(@"(?:^|[^\p{L}])(?:tang|lau|level|floor|so|no)$", RegexOptions.Compiled);

        private static readonly Regex Quoted = new Regex("[\"“”'‘’]([^\"“”'‘’]{1,80})[\"“”'‘’]", RegexOptions.Compiled);

        private static readonly Regex PathLike = new Regex(@"(?<p>[A-Za-z]:[\\/][^\s\""']+|/[^\s\""']+|[\w\-. ]+\.(?:csv|json|html|txt|rvt|rte|dwg|pdf))", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>
        /// Danh sách ≤ <paramref name="max"/> lệnh ứng viên theo điểm từ khoá — đầu vào cho model local (giới hạn ~8 tool
        /// một lượt với model 7–14B). Không khớp gì thì trả các lệnh hay dùng nhất để model vẫn có cái để chọn.
        /// </summary>
        public static List<CommandDescriptor> Candidates(string text, string app, int max = 8)
        {
            var normalized = LayerMappingSuggester.RemoveDiacritics(text ?? string.Empty).ToLowerInvariant();
            var scored = new List<(CommandDescriptor Cmd, double Score)>();
            foreach (var cmd in CommandCatalog.For(app))
            {
                var best = 0.0;
                foreach (var kw in cmd.Keywords.Concat(new[] { cmd.Name }).Concat(cmd.Aliases))
                {
                    var k = LayerMappingSuggester.RemoveDiacritics(kw).ToLowerInvariant();
                    if (k.Length >= 3 && normalized.Contains(k))
                    {
                        best = Math.Max(best, Math.Min(1.0, 0.5 + k.Length / 30.0));
                    }
                    else
                    {
                        // Điểm phụ theo số từ chung để có thứ hạng khi không khớp trọn cụm.
                        var words = k.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        var hit = words.Count(w => w.Length >= 3 && normalized.Contains(w));
                        if (hit > 0)
                        {
                            best = Math.Max(best, 0.2 * hit / words.Length);
                        }
                    }
                }

                scored.Add((cmd, best));
            }

            return scored.OrderByDescending(s => s.Score).ThenBy(s => s.Cmd.Name, StringComparer.Ordinal)
                .Select(s => s.Cmd).GroupBy(c => c.Name).Select(g => g.First()).Take(Math.Max(1, max)).ToList();
        }

        public static CommandIntent Parse(string text, string app)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return new CommandIntent(null, new JObject(), 0, "Câu lệnh rỗng.", Array.Empty<string>());
            }

            var normalized = LayerMappingSuggester.RemoveDiacritics(text).ToLowerInvariant();
            var scored = new List<(CommandDescriptor Cmd, double Score, string Hit)>();

            foreach (var cmd in CommandCatalog.For(app))
            {
                var best = 0.0;
                var hit = string.Empty;
                foreach (var kw in cmd.Keywords.Concat(new[] { cmd.Name }).Concat(cmd.Aliases))
                {
                    var k = LayerMappingSuggester.RemoveDiacritics(kw).ToLowerInvariant();
                    if (k.Length < 3)
                    {
                        continue;
                    }

                    if (normalized.Contains(k))
                    {
                        // Từ khoá dài, đặc thù → điểm cao hơn từ khoá ngắn.
                        var s = Math.Min(1.0, 0.5 + k.Length / 30.0);
                        if (s > best)
                        {
                            best = s;
                            hit = kw;
                        }
                    }
                }

                if (best > 0)
                {
                    scored.Add((cmd, best, hit));
                }
            }

            if (scored.Count == 0)
            {
                return new CommandIntent(null, new JObject(), 0,
                    "Không nhận ra lệnh nào. Các lệnh có thể dùng: " + string.Join(", ", CommandCatalog.Names(app)) + ".",
                    Array.Empty<string>());
            }

            // OrderBy ổn định (List.Sort thì không): hai lệnh đồng điểm luôn ra cùng thứ tự giữa các lần chạy.
            scored = scored.OrderByDescending(s => s.Score).ThenBy(s => s.Cmd.Name, StringComparer.Ordinal).ToList();
            var top = scored[0];
            var alternatives = scored.Skip(1).Take(3).Select(s => s.Cmd.Name).Distinct().ToList();

            var config = BuildConfig(top.Cmd, text, normalized);
            var confidence = top.Score;
            if (scored.Count > 1 && Math.Abs(scored[1].Score - top.Score) < 0.05)
            {
                confidence *= 0.7; // hai lệnh sát điểm → giảm tin cậy, kỹ sư phải chọn
            }

            var explanation = "Nhận ra \"" + top.Hit + "\" → " + top.Cmd.Name + " (" + top.Cmd.Description + ")."
                              + (top.Cmd.WritesModel ? " Lệnh sửa mô hình: chạy xem trước (dryRun) trước, xác nhận rồi mới chạy thật." : string.Empty);

            return new CommandIntent(top.Cmd.Name, config, Math.Round(confidence, 2), explanation, alternatives);
        }

        private static JObject BuildConfig(CommandDescriptor cmd, string original, string normalized)
        {
            var cfg = new JObject();
            if (cmd.WritesModel)
            {
                cfg["dryRun"] = true;
            }

            var quoted = Quoted.Matches(original).Cast<Match>().Select(m => m.Groups[1].Value.Trim()).ToList();
            var paths = PathLike.Matches(original).Cast<Match>().Select(m => m.Groups["p"].Value.Trim()).ToList();
            var numbers = ExtractLengthsMm(original);

            foreach (var field in cmd.ConfigFields.Keys)
            {
                switch (field)
                {
                    case "outputPath":
                    case "inputPath":
                    case "layersCsvPath":
                    case "gridCsvPath":
                    case "rulesPath":
                    case "templatePath":
                    case "sourcePath":
                        var p = paths.FirstOrDefault(x => x.EndsWith(FieldExtension(field), StringComparison.OrdinalIgnoreCase)) ?? paths.FirstOrDefault();
                        if (p != null)
                        {
                            cfg[field] = p;
                            paths.Remove(p);
                        }
                        break;
                    case "spacingMm":
                    case "maxSegmentMm":
                    case "clearanceMm":
                    case "offsetMm":
                        if (numbers.Count > 0)
                        {
                            cfg[field] = numbers[0];
                        }
                        break;
                    case "padWidth":
                        var pad = Regex.Match(normalized, @"(\d)\s*(?:chu so|ch[uữ] s[oố]|digits?)");
                        if (pad.Success)
                        {
                            cfg[field] = int.Parse(pad.Groups[1].Value, CultureInfo.InvariantCulture);
                        }
                        break;
                    case "prefix":
                        var pre = Regex.Match(original, @"(?:tiền tố|tien to|prefix)\s*[:=]?\s*[\""']?([A-Za-z0-9\-_.]{1,10})", RegexOptions.IgnoreCase);
                        if (pre.Success)
                        {
                            cfg[field] = pre.Groups[1].Value;
                        }
                        break;
                    case "category":
                    case "blockName":
                    case "deviceFamily":
                    case "hangerFamilyName":
                    case "sleeveFamilyName":
                    case "typeName":
                    case "viewTemplateName":
                        if (quoted.Count > 0)
                        {
                            cfg[field] = quoted[0];
                            quoted.RemoveAt(0);
                        }
                        else if (field == "category")
                        {
                            var cat = GuessCategory(normalized);
                            if (cat != null)
                            {
                                cfg[field] = cat;
                            }
                        }
                        break;
                    case "parameterName":
                    case "attributeTag":
                        var par = Regex.Match(original, @"(?:tham số|tham so|parameter|attribute|tag)\s*[:=]?\s*[\""']?([A-Za-z][A-Za-z0-9 _\-]{0,30})", RegexOptions.IgnoreCase);
                        if (par.Success)
                        {
                            cfg[field] = par.Groups[1].Value.Trim();
                        }
                        break;
                    case "formats":
                        var fmts = new JArray();
                        if (normalized.Contains("pdf")) fmts.Add("Pdf");
                        if (normalized.Contains("dwg")) fmts.Add("Dwg");
                        if (normalized.Contains("ifc")) fmts.Add("Ifc");
                        if (normalized.Contains("nwc")) fmts.Add("Nwc");
                        if (fmts.Count > 0)
                        {
                            cfg[field] = fmts;
                        }
                        break;
                    case "elementType":
                        if (normalized.Contains("duct") || normalized.Contains("ong gio")) cfg[field] = "Duct";
                        else if (normalized.Contains("tray") || normalized.Contains("mang cap")) cfg[field] = "CableTray";
                        else if (normalized.Contains("conduit") || normalized.Contains("ong luon")) cfg[field] = "Conduit";
                        else if (normalized.Contains("pipe") || normalized.Contains("ong")) cfg[field] = "Pipe";
                        break;
                    case "createMissing":
                        if (normalized.Contains("tao layer") || normalized.Contains("create")) cfg[field] = true;
                        break;
                }
            }

            return cfg;
        }

        /// <summary>
        /// Các số đo (mm) trong câu, theo thứ tự xuất hiện. Bỏ số đứng ngay sau tầng/level/số ("tầng 2" không phải 2 mm),
        /// quy đổi đơn vị: m/mét → ×1000, cm → ×10, mm hoặc không đơn vị → giữ nguyên.
        /// </summary>
        public static List<double> ExtractLengthsMm(string text)
        {
            var result = new List<double>();
            if (string.IsNullOrEmpty(text))
            {
                return result;
            }

            foreach (Match m in Number.Matches(text))
            {
                var before = LayerMappingSuggester.RemoveDiacritics(text.Substring(0, m.Index)).TrimEnd().ToLowerInvariant();
                if (OrdinalWord.IsMatch(before))
                {
                    continue;
                }

                if (!TryParseNumber(m.Groups["v"].Value, out var v))
                {
                    continue;
                }

                var u = LayerMappingSuggester.RemoveDiacritics(m.Groups["u"].Value).ToLowerInvariant();
                if (u == "m" || u == "met")
                {
                    v *= 1000;
                }
                else if (u == "cm")
                {
                    v *= 10;
                }

                result.Add(v);
            }

            return result;
        }

        /// <summary>
        /// Đọc số viết kiểu Việt/Âu lẫn Anh. Quy tắc: dấu phân cách theo sau ĐÚNG 3 chữ số và không có dấu nào khác
        /// là dấu nghìn ("2.000" = "2,000" = 2000); còn lại là dấu thập phân ("2,5" = "2.5" = 2,5). Nhiều dấu:
        /// dấu cuối là thập phân nếu nhóm sau nó không đủ 3 chữ số hoặc khác loại với các dấu trước ("1.000,5"),
        /// nếu không thì tất cả là dấu nghìn ("1.234.567").
        /// </summary>
        public static bool TryParseNumber(string? text, out double value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            var t = text!.Trim();
            var separators = new List<int>();
            for (var i = 0; i < t.Length; i++)
            {
                if (t[i] == '.' || t[i] == ',')
                {
                    separators.Add(i);
                }
            }

            string normalized;
            if (separators.Count == 0)
            {
                normalized = t;
            }
            else if (separators.Count == 1)
            {
                var sep = separators[0];
                var after = t.Length - sep - 1;
                normalized = after == 3 ? t.Remove(sep, 1) : t.Substring(0, sep) + "." + t.Substring(sep + 1);
            }
            else
            {
                var last = separators[separators.Count - 1];
                var lastGroup = t.Length - last - 1;
                var sameKind = true;
                for (var i = 1; i < separators.Count; i++)
                {
                    if (t[separators[i]] != t[separators[0]])
                    {
                        sameKind = false;
                    }
                }

                var lastIsDecimal = lastGroup != 3 || !sameKind;
                var sb = new System.Text.StringBuilder(t.Length);
                for (var i = 0; i < t.Length; i++)
                {
                    if (t[i] == '.' || t[i] == ',')
                    {
                        if (i == last && lastIsDecimal)
                        {
                            sb.Append('.');
                        }

                        continue;
                    }

                    sb.Append(t[i]);
                }

                normalized = sb.ToString();
            }

            return double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        private static string FieldExtension(string field)
        {
            switch (field)
            {
                case "rulesPath": return ".json";
                case "templatePath": return ".rte";
                case "sourcePath": return ".rvt";
                case "outputPath": return ".csv";
                default: return ".csv";
            }
        }

        private static string? GuessCategory(string normalized)
        {
            if (normalized.Contains("cua so") || normalized.Contains("window")) return "Windows";
            if (normalized.Contains("cua") || normalized.Contains("door")) return "Doors";
            if (normalized.Contains("phong") || normalized.Contains("room")) return "Rooms";
            if (normalized.Contains("tuong") || normalized.Contains("wall")) return "Walls";
            if (normalized.Contains("cot") || normalized.Contains("column")) return "Structural Columns";
            if (normalized.Contains("dam") || normalized.Contains("beam")) return "Structural Framing";
            if (normalized.Contains("thiet bi") || normalized.Contains("equipment")) return "Mechanical Equipment";
            return null;
        }
    }
}
