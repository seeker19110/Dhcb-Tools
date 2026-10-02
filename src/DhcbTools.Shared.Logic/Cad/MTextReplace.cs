using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace DhcbTools.Shared.Logic.Cad
{
    /// <summary>Kết quả thay chuỗi trên nội dung MText.</summary>
    public sealed class MTextReplaceResult
    {
        public MTextReplaceResult(string contents, int replaced, int skipped)
        {
            Contents = contents;
            Replaced = replaced;
            Skipped = skipped;
        }

        /// <summary><c>Contents</c> sau khi thay — trùng chuỗi gốc khi không thay chỗ nào.</summary>
        public string Contents { get; }

        /// <summary>Số chỗ khớp đã thay.</summary>
        public int Replaced { get; }

        /// <summary>Số chỗ khớp trên chữ hiển thị nhưng vắt qua mã định dạng — KHÔNG thay, kỹ sư sửa tay.</summary>
        public int Skipped { get; }
    }

    /// <summary>
    /// Tìm/thay trên <b>chữ hiển thị</b> của MText, không bao giờ sửa vào mã định dạng.
    /// <para>
    /// Lỗi đã sửa (để lại từ audit 2026-09-23): <c>TextReplace</c> thay thẳng trên <c>MText.Contents</c> — chuỗi CÓ mã
    /// định dạng (<c>\P</c> xuống dòng, <c>{\H2.5x;…}</c> chiều cao, <c>{\fArial|b1;…}</c> font…). Regex <c>\d+</c> vì thế
    /// đổi luôn chữ số trong <c>\H2.5x;</c> (chữ đổi cỡ), tìm "PL" trúng <c>\PLine</c> và biến mã xuống dòng thành
    /// <c>\X…</c> — hỏng định dạng, không báo gì.
    /// </para>
    /// <para>
    /// Cách làm: tách <c>Contents</c> thành chữ hiển thị (kèm vị trí của từng ký tự trong <c>Contents</c>) và mã định
    /// dạng; so khớp trên chữ hiển thị (<c>\P</c> thành <c>\n</c>, nên <c>^</c>/<c>$</c> của regex nhiều dòng theo đoạn).
    /// Chỗ khớp nằm gọn trong một đoạn chữ liền (không có mã nào chen giữa) thì thay; vắt qua mã — hoặc chứa ký tự do
    /// mã sinh ra như xuống dòng, phân số xếp chồng — thì để nguyên và đếm vào <see cref="MTextReplaceResult.Skipped"/>.
    /// Chuỗi thay được chèn như chữ: <c>\</c>, <c>{</c>, <c>}</c> được escape, xuống dòng thành <c>\P</c>.
    /// </para>
    /// </summary>
    public static class MTextReplace
    {
        /// <summary>Ký tự hiển thị thay cho phân số xếp chồng <c>\S…;</c> và ký tự <c>\M+…</c> — không khớp chữ thường.</summary>
        private const char Placeholder = '￼';

        /// <summary>Một ký tự hiển thị và đoạn <c>Contents</c> sinh ra nó.</summary>
        private readonly struct Unit
        {
            public Unit(char ch, int start, int end, bool editable)
            {
                Ch = ch;
                Start = start;
                End = end;
                Editable = editable;
            }

            public char Ch { get; }

            public int Start { get; }

            public int End { get; }

            /// <summary>Chữ thật (kể cả <c>\\</c>, <c>\{</c>, <c>\~</c>, <c>\U+XXXX</c>) — thay được; ký tự do mã sinh ra thì không.</summary>
            public bool Editable { get; }
        }

        /// <summary>Chữ hiển thị của MText (<c>\P</c> → xuống dòng) — để báo cáo và kiểm tra.</summary>
        public static string DisplayText(string? contents) => Display(Parse(contents ?? string.Empty));

        /// <summary>Thay chuỗi thường (không regex), khớp không chồng lấn, quét trái sang phải — như <see cref="TextReplace.ReplaceAll"/>.</summary>
        public static MTextReplaceResult ReplaceAll(string? contents, string? find, string? replace, bool ignoreCase)
        {
            var source = contents ?? string.Empty;
            if (string.IsNullOrEmpty(find))
            {
                return new MTextReplaceResult(source, 0, 0);
            }

            var units = Parse(source);
            var display = Display(units);
            var comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            var matches = new List<(int Index, int Length, string Replacement)>();
            var index = 0;
            while (index < display.Length)
            {
                var hit = display.IndexOf(find!, index, comparison);
                if (hit < 0)
                {
                    break;
                }

                matches.Add((hit, find!.Length, replace ?? string.Empty));
                index = hit + find!.Length;
            }

            return Rebuild(source, units, matches);
        }

        /// <summary>
        /// Thay theo regex trên chữ hiển thị; <paramref name="replacement"/> hiểu <c>$1</c>… như <see cref="Regex.Replace(string, string)"/>.
        /// Regex quá trần thời gian thì <see cref="RegexMatchTimeoutException"/> ném ra cho lệnh gọi xử lý.
        /// </summary>
        public static MTextReplaceResult Replace(string? contents, Regex regex, string? replacement)
        {
            if (regex == null)
            {
                throw new ArgumentNullException(nameof(regex));
            }

            var source = contents ?? string.Empty;
            var units = Parse(source);
            var matches = regex.Matches(Display(units))
                .Cast<Match>()
                .Select(m => (m.Index, m.Length, m.Result(replacement ?? string.Empty)))
                .ToList();
            return Rebuild(source, units, matches);
        }

        private static string Display(List<Unit> units) => new string(units.Select(u => u.Ch).ToArray());

        /// <summary>Tách <c>Contents</c> theo bảng mã định dạng MText của AutoCAD.</summary>
        private static List<Unit> Parse(string s)
        {
            var units = new List<Unit>(s.Length);
            var i = 0;
            while (i < s.Length)
            {
                var c = s[i];
                if (c == '{' || c == '}')
                {
                    i++; // nhóm định dạng, không hiển thị
                    continue;
                }

                if (c != '\\')
                {
                    units.Add(new Unit(c, i, i + 1, true));
                    i++;
                    continue;
                }

                if (i + 1 >= s.Length)
                {
                    units.Add(new Unit('\\', i, i + 1, true)); // gạch chéo lẻ cuối chuỗi: chữ
                    i++;
                    continue;
                }

                var code = s[i + 1];
                switch (code)
                {
                    case '\\':
                    case '{':
                    case '}':
                        units.Add(new Unit(code, i, i + 2, true));
                        i += 2;
                        break;
                    case '~':
                        units.Add(new Unit(' ', i, i + 2, true)); // khoảng trắng không ngắt
                        i += 2;
                        break;
                    case 'P':
                    case 'N':
                        units.Add(new Unit('\n', i, i + 2, false)); // xuống đoạn / sang cột
                        i += 2;
                        break;
                    case 'U' when i + 7 <= s.Length && s[i + 2] == '+'
                                   && int.TryParse(s.Substring(i + 3, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var cp):
                        units.Add(new Unit((char)cp, i, i + 7, true));
                        i += 7;
                        break;
                    case 'M' when i + 8 <= s.Length && s[i + 2] == '+':
                        units.Add(new Unit(Placeholder, i, i + 8, false)); // ký tự đa byte theo font shape
                        i += 8;
                        break;
                    case 'S':
                        {
                            var end = ToSemicolon(s, i + 2);
                            units.Add(new Unit(Placeholder, i, end, false)); // phân số xếp chồng
                            i = end;
                            break;
                        }

                    case 'A':
                    case 'C':
                    case 'c':
                    case 'F':
                    case 'f':
                    case 'H':
                    case 'Q':
                    case 'T':
                    case 'W':
                    case 'p':
                        i = ToSemicolon(s, i + 2); // mã có tham số, kết thúc bằng ';'
                        break;
                    default:
                        // \L \l \O \o \K \k \X và mã lạ: bật/tắt định dạng, không hiển thị. Mã lạ coi như mã cho chắc:
                        // không thay vắt qua nó.
                        i += 2;
                        break;
                }
            }

            return units;
        }

        /// <summary>Vị trí ngay sau dấu ';' kết thúc mã; thiếu ';' thì mã kéo tới hết chuỗi.</summary>
        private static int ToSemicolon(string s, int from)
        {
            var semicolon = s.IndexOf(';', from);
            return semicolon < 0 ? s.Length : semicolon + 1;
        }

        private static MTextReplaceResult Rebuild(string source, List<Unit> units, List<(int Index, int Length, string Replacement)> matches)
        {
            var sb = new StringBuilder(source.Length);
            var position = 0;
            var replaced = 0;
            var skipped = 0;
            foreach (var (index, length, replacement) in matches)
            {
                int start;
                int end;
                if (length == 0)
                {
                    // Regex khớp rỗng (^, \b…): chèn ngay trước ký tự hiển thị kế tiếp — tức là sau mọi mã định dạng
                    // đứng trước nó, nên chữ chèn vào mang định dạng của chữ ngay sau.
                    start = end = index < units.Count ? units[index].Start
                        : units.Count > 0 ? units[units.Count - 1].End
                        : source.Length;
                }
                else if (Contiguous(units, index, length))
                {
                    start = units[index].Start;
                    end = units[index + length - 1].End;
                }
                else
                {
                    skipped++;
                    continue;
                }

                sb.Append(source, position, start - position).Append(Escape(replacement));
                position = end;
                replaced++;
            }

            if (replaced == 0)
            {
                return new MTextReplaceResult(source, 0, skipped);
            }

            sb.Append(source, position, source.Length - position);
            return new MTextReplaceResult(sb.ToString(), replaced, skipped);
        }

        /// <summary>Mọi ký tự của chỗ khớp là chữ thật và liền nhau trong <c>Contents</c> — không mã nào chen giữa.</summary>
        private static bool Contiguous(List<Unit> units, int index, int length)
        {
            for (var k = index; k < index + length; k++)
            {
                if (!units[k].Editable || (k > index && units[k - 1].End != units[k].Start))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Chuỗi thay chèn vào như CHỮ: không để <c>\</c>, <c>{</c>, <c>}</c> thành mã; xuống dòng thành <c>\P</c>.</summary>
        private static string Escape(string text) =>
            text.Replace("\\", "\\\\")
                .Replace("{", "\\{")
                .Replace("}", "\\}")
                .Replace("\r\n", "\\P")
                .Replace("\n", "\\P")
                .Replace("\r", "\\P");
    }
}
