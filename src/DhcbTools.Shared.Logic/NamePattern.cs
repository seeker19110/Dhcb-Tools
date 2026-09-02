using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace DhcbTools.Shared.Logic
{
    /// <summary>
    /// Đổi tên theo mẫu (mục 7.1/7.5/7.11 — học từ pyRevit Sheets, DiRoots FamilyReviser, Lee Mac BATTE):
    /// token <c>{Token}</c> lấy từ từ điển người gọi, bộ đếm <c>{n}</c>/<c>{n:000}</c>, tìm/thay theo regex trên kết quả,
    /// tiền tố/hậu tố. Thuần chuỗi để test không cần Revit/AutoCAD.
    /// </summary>
    public sealed class NamePattern
    {
        private static readonly Regex Token = new Regex(@"\{(?<name>[A-Za-z_][\w ]*)(?::(?<fmt>[^}]+))?\}", RegexOptions.Compiled);

        public NamePattern(string pattern)
        {
            Pattern = pattern ?? throw new ArgumentNullException(nameof(pattern));
        }

        /// <summary>Mẫu, ví dụ <c>"A-{Level}-{n:00}"</c> hoặc <c>"{Name}"</c>.</summary>
        public string Pattern { get; }

        /// <summary>Regex tìm trên kết quả sau khi thay token (rỗng = không tìm/thay).</summary>
        public string? Find { get; set; }

        public string Replace { get; set; } = string.Empty;

        public bool FindIsRegex { get; set; } = true;

        public bool CaseSensitive { get; set; }

        public string Prefix { get; set; } = string.Empty;

        public string Suffix { get; set; } = string.Empty;

        public int CounterStart { get; set; } = 1;

        public int CounterStep { get; set; } = 1;

        /// <summary>Có token nào trong mẫu không (để quyết định đọc giá trị nguồn).</summary>
        public static IEnumerable<string> TokensIn(string pattern)
        {
            foreach (Match m in Token.Matches(pattern ?? string.Empty))
            {
                yield return m.Groups["name"].Value.Trim();
            }
        }

        /// <summary>Áp mẫu cho một phần tử thứ <paramref name="index"/> (0-based) với giá trị token của nó.</summary>
        public string Apply(int index, IDictionary<string, string>? values)
        {
            var counter = CounterStart + index * CounterStep;
            var text = Token.Replace(Pattern, m =>
            {
                var name = m.Groups["name"].Value.Trim();
                var fmt = m.Groups["fmt"].Success ? m.Groups["fmt"].Value : null;

                if (name.Equals("n", StringComparison.OrdinalIgnoreCase) || name.Equals("counter", StringComparison.OrdinalIgnoreCase))
                {
                    return fmt == null ? counter.ToString(CultureInfo.InvariantCulture) : counter.ToString(fmt, CultureInfo.InvariantCulture);
                }

                if (values != null && values.TryGetValue(name, out var v))
                {
                    return ApplyTextFormat(v ?? string.Empty, fmt);
                }

                return m.Value; // token lạ giữ nguyên để người dùng thấy
            });

            if (!string.IsNullOrEmpty(Find))
            {
                var options = CaseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase;
                var regex = new Regex(FindIsRegex ? Find! : Regex.Escape(Find!), options);
                text = regex.Replace(text, FindIsRegex ? Replace : Replace.Replace("$", "$$"));
            }

            return Prefix + text + Suffix;
        }

        /// <summary>Định dạng văn bản: <c>upper</c>, <c>lower</c>, <c>title</c>, <c>trim</c>, <c>left:N</c>, <c>right:N</c>.</summary>
        private static string ApplyTextFormat(string value, string? fmt)
        {
            if (string.IsNullOrEmpty(fmt))
            {
                return value;
            }

            var f = fmt!.Trim();
            if (f.Equals("upper", StringComparison.OrdinalIgnoreCase)) return value.ToUpperInvariant();
            if (f.Equals("lower", StringComparison.OrdinalIgnoreCase)) return value.ToLowerInvariant();
            if (f.Equals("trim", StringComparison.OrdinalIgnoreCase)) return value.Trim();
            if (f.Equals("title", StringComparison.OrdinalIgnoreCase)) return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(value.ToLowerInvariant());
            if (f.StartsWith("left:", StringComparison.OrdinalIgnoreCase) && int.TryParse(f.Substring(5), out var l)) return value.Length <= l ? value : value.Substring(0, l);
            if (f.StartsWith("right:", StringComparison.OrdinalIgnoreCase) && int.TryParse(f.Substring(6), out var r)) return value.Length <= r ? value : value.Substring(value.Length - r);
            return value;
        }

        /// <summary>
        /// Áp cho cả danh sách và **chống trùng**: tên trùng nhau trong lô hoặc trùng với <paramref name="reserved"/> (tên đang
        /// tồn tại ở phần tử KHÔNG được đổi) nhận hậu tố " (2)", " (3)"…; trả về danh sách lý do cho những tên bị đổi thêm.
        /// </summary>
        public List<string> ApplyAll(IReadOnlyList<IDictionary<string, string>?> items, ISet<string>? reserved, out List<string> notes)
        {
            notes = new List<string>();
            var used = new HashSet<string>(reserved ?? new HashSet<string>(), StringComparer.OrdinalIgnoreCase);
            var result = new List<string>(items.Count);
            for (var i = 0; i < items.Count; i++)
            {
                var name = Apply(i, items[i]);
                var unique = name;
                var k = 2;
                while (!used.Add(unique))
                {
                    unique = name + " (" + k.ToString(CultureInfo.InvariantCulture) + ")";
                    k++;
                }

                if (!string.Equals(unique, name, StringComparison.Ordinal))
                {
                    notes.Add("\"" + name + "\" trùng → \"" + unique + "\".");
                }

                result.Add(unique);
            }

            return result;
        }
    }
}
