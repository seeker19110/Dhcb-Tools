using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace DhcbTools.Shared.Logic.Setout
{
    /// <summary>Tuỳ chọn đặt tên/mô tả cho <see cref="SetoutPlanner"/>.</summary>
    public sealed class SetoutPlanOptions
    {
        /// <summary>Mẫu tên điểm của phần tử — token <c>{Code}</c>, <c>{Category}</c>, <c>{Family}</c>, <c>{Type}</c>, <c>{Level}</c>, <c>{Mark}</c>, <c>{Id}</c>, <c>{Kind}</c>, bộ đếm <c>{n:000}</c> (đếm riêng theo mã).</summary>
        public string NamePattern { get; set; } = "{Code}{n:000}";

        /// <summary>Mẫu tên cho điểm giao trục — mặc định chính cặp trục (<c>A-1</c>).</summary>
        public string GridNamePattern { get; set; } = "{Grid}";

        public string DescriptionPattern { get; set; } = "{Category} {Level}";

        /// <summary>Giới hạn tên điểm của máy toàn đạc (Leica/Trimble: 16). 0 = không cắt.</summary>
        public int MaxNameLength { get; set; } = 16;

        public int CounterStart { get; set; } = 1;
    }

    /// <summary>Kết quả đặt tên: danh sách điểm theo thứ tự ghi ra file, kèm ghi chú cho kỹ sư.</summary>
    public sealed class SetoutPlan
    {
        public List<SetoutPoint> Points { get; } = new List<SetoutPoint>();

        public List<string> Notes { get; } = new List<string>();

        /// <summary>Số tên bị cắt vì dài quá <see cref="SetoutPlanOptions.MaxNameLength"/>.</summary>
        public int Truncated { get; set; }

        /// <summary>Số tên phải thêm hậu tố vì trùng.</summary>
        public int Renamed { get; set; }

        public Dictionary<string, int> CountByCode { get; } = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Sắp xếp điểm (tầng → mã → phần tử → vị trí trên phần tử), đặt tên theo mẫu, làm sạch tên cho máy
    /// toàn đạc và bảo đảm <b>không có hai điểm cùng tên</b> — trên máy, hai điểm cùng tên là chọn nhầm
    /// điểm mà không ai biết. Thuần chuỗi/số, test được không cần Revit.
    /// </summary>
    public static class SetoutPlanner
    {
        public static SetoutPlan Plan(IReadOnlyList<SetoutSource> sources, SetoutPlanOptions? options = null)
        {
            options = options ?? new SetoutPlanOptions();
            var plan = new SetoutPlan();
            if (sources == null || sources.Count == 0)
            {
                return plan;
            }

            var ordered = sources
                .Select((source, index) => new { Source = source, Index = index })
                .OrderBy(t => t.Source.Level, NaturalComparer.Instance)
                .ThenBy(t => CodeOf(t.Source), StringComparer.OrdinalIgnoreCase)
                .ThenBy(t => t.Source.ElementId)
                .ThenBy(t => KindRank(t.Source.Kind))
                .ThenBy(t => t.Index)
                .Select(t => t.Source)
                .ToList();

            var namePattern = new NamePattern(options.NamePattern) { CounterStart = options.CounterStart };
            var gridPattern = new NamePattern(options.GridNamePattern) { CounterStart = options.CounterStart };
            var descriptionPattern = new NamePattern(options.DescriptionPattern);

            var counters = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var source in ordered)
            {
                var code = CodeOf(source);
                counters.TryGetValue(code, out var index);
                counters[code] = index + 1;

                var values = TokenValues(source, code);
                var pattern = source.Grid.Length > 0 ? gridPattern : namePattern;
                var name = Sanitize(pattern.Apply(index, values));
                if (name.Length == 0)
                {
                    // Mẫu chỉ toàn token rỗng (ví dụ {Mark} mà phần tử không có Mark) — vẫn phải có tên.
                    name = code + (index + options.CounterStart).ToString("000", CultureInfo.InvariantCulture);
                }

                if (options.MaxNameLength > 0 && name.Length > options.MaxNameLength)
                {
                    name = Shorten(name, options.MaxNameLength);
                    plan.Truncated++;
                }

                var unique = Unique(name, used, options.MaxNameLength);
                if (!string.Equals(unique, name, StringComparison.Ordinal))
                {
                    plan.Renamed++;
                }

                used.Add(unique);

                plan.Points.Add(new SetoutPoint(unique, source.EastMm, source.NorthMm, source.ElevationMm)
                {
                    Description = Collapse(descriptionPattern.Apply(index, values)),
                    Code = code,
                    Category = source.Category,
                    Level = source.Level,
                    Kind = source.Kind,
                    ElementId = source.ElementId,
                });

                plan.CountByCode.TryGetValue(code, out var count);
                plan.CountByCode[code] = count + 1;
            }

            if (plan.Truncated > 0)
            {
                plan.Notes.Add(plan.Truncated + " tên điểm dài quá " + options.MaxNameLength
                    + " ký tự (giới hạn tên điểm của máy toàn đạc) nên đã bỏ bớt phần giữa, giữ cả đầu lẫn đuôi và đánh dấu \"..\" — rút ngắn namePattern nếu muốn tên đọc liền mạch.");
            }

            if (plan.Renamed > 0)
            {
                plan.Notes.Add(plan.Renamed + " tên điểm trùng nhau đã thêm hậu tố _2, _3… — trên máy hai điểm cùng tên là chọn nhầm mà không ai biết.");
            }

            return plan;
        }

        /// <summary>Mã ngắn của điểm: khai sẵn ở nguồn, hoặc theo category.</summary>
        public static string CodeOf(SetoutSource source) =>
            source.Code.Length > 0 ? source.Code : SetoutCodes.For(source.Category);

        /// <summary>
        /// Làm sạch tên cho máy toàn đạc: bỏ dấu tiếng Việt, khoảng trắng → <c>_</c>, giữ chữ/số và
        /// <c>_ - . +</c>. Dấu phẩy hay nháy kép trong tên là hỏng cả dòng CSV trên máy không hiểu RFC 4180.
        /// </summary>
        public static string Sanitize(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return string.Empty;
            }

            var sb = new StringBuilder(raw!.Length);
            var pendingUnderscore = false;
            foreach (var ch in StripDiacritics(raw.Trim()))
            {
                if (char.IsWhiteSpace(ch))
                {
                    pendingUnderscore = sb.Length > 0;
                    continue;
                }

                if (char.IsLetterOrDigit(ch) && ch < 128 || ch == '_' || ch == '-' || ch == '.' || ch == '+')
                {
                    if (pendingUnderscore)
                    {
                        sb.Append('_');
                        pendingUnderscore = false;
                    }

                    sb.Append(ch);
                }
            }

            return sb.ToString();
        }

        /// <summary>Bỏ dấu: <c>Cột trục A</c> → <c>Cot truc A</c>. Đ/đ không phải dấu tổ hợp nên xử riêng.</summary>
        public static string StripDiacritics(string text)
        {
            var normalized = text.Replace('đ', 'd').Replace('Đ', 'D').Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(normalized.Length);
            foreach (var ch in normalized)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                {
                    sb.Append(ch);
                }
            }

            return sb.ToString().Normalize(NormalizationForm.FormC);
        }

        /// <summary>Mô tả: một dòng, không dấu phẩy/nháy — phần mềm máy thường tách cột bằng dấu phẩy mà không hiểu nháy kép.</summary>
        public static string Collapse(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            var parts = text!.Replace(',', ';').Replace('"', '\'')
                .Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            return string.Join(" ", parts);
        }

        /// <summary>
        /// Rút tên về <paramref name="maxLength"/> ký tự bằng cách <b>bỏ ở giữa</b>, giữ cả đầu lẫn đuôi,
        /// đánh dấu chỗ bỏ bằng <c>..</c>.
        /// <para>
        /// Vì sao không cắt đuôi: tên điểm gần như luôn là tên ghép — giao trục là <c>TrụcA-TrụcB</c>,
        /// mẫu thường dùng là <c>{Level}-{Grid}</c> — nên <b>phần phân biệt nằm ở đuôi</b>, còn phần đầu
        /// (tầng, block) giống nhau ở hàng trăm điểm. Vòng chạy thật trên Snowdon Towers 2026-09-05 cho ra
        /// <c>Block_35_Left-Bl</c>, <c>Block_35_Left-B.</c>, <c>Block_35_Left-X_</c>: đúng 16 ký tự, đúng
        /// là duy nhất, nhưng trên máy toàn đạc thì trắc đạc không biết đó là giao trục nào —
        /// tên duy nhất mà không đọc được thì cũng chọn nhầm điểm như tên trùng.
        /// </para>
        /// <para>
        /// Dấu <c>..</c> nằm trong bộ ký tự mà <see cref="Sanitize"/> cho phép, nên tên rút gọn vẫn nạp
        /// được vào máy.
        /// </para>
        /// </summary>
        internal static string Shorten(string name, int maxLength)
        {
            if (maxLength <= 0 || name.Length <= maxLength)
            {
                return name;
            }

            // Quá ngắn để chứa cả dấu ..: không còn chỗ cho đuôi, đành cắt đuôi như cũ.
            if (maxLength < 5)
            {
                return name.Substring(0, maxLength);
            }

            var keep = maxLength - 2;
            var head = (keep + 1) / 2;      // lẻ thì ưu tiên phần đầu
            var tail = keep - head;
            return name.Substring(0, head) + ".." + name.Substring(name.Length - tail, tail);
        }

        private static string Unique(string name, HashSet<string> used, int maxLength)
        {
            if (!used.Contains(name))
            {
                return name;
            }

            for (var k = 2; ; k++)
            {
                var suffix = "_" + k.ToString(CultureInfo.InvariantCulture);
                var stem = name;
                if (maxLength > 0 && stem.Length + suffix.Length > maxLength)
                {
                    stem = stem.Substring(0, Math.Max(1, maxLength - suffix.Length));
                }

                var candidate = stem + suffix;
                if (!used.Contains(candidate))
                {
                    return candidate;
                }
            }
        }

        private static Dictionary<string, string> TokenValues(SetoutSource source, string code) =>
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "Code", code },
                { "Category", source.Category },
                { "Family", source.Family },
                { "Type", source.TypeName },
                { "Level", source.Level },
                { "Mark", source.Mark },
                { "Id", source.ElementId == 0 ? string.Empty : source.ElementId.ToString(CultureInfo.InvariantCulture) },
                { "Kind", source.Kind },
                { "Grid", source.Grid },
            };

        private static int KindRank(string kind)
        {
            switch ((kind ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "tim": return 0;
                case "đầu": return 1;
                case "giữa": return 2;
                case "cuối": return 3;
                case "tâm hộp bao": return 4;
                case "giao trục": return 5;
                default: return 9;
            }
        }
    }
}
