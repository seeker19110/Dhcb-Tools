using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace DhcbTools.Shared.Logic.Mep
{
    /// <summary>Màu RGB 0–255.</summary>
    public struct Rgb
    {
        public Rgb(byte r, byte g, byte b)
        {
            R = r;
            G = g;
            B = b;
        }

        public byte R { get; }

        public byte G { get; }

        public byte B { get; }

        public override string ToString() => "#" + R.ToString("X2") + G.ToString("X2") + B.ToString("X2");
    }

    /// <summary>
    /// Mục 3.4: phân giải mã màu hex và sinh System Name theo quy tắc
    /// <c>{Discipline}-{SystemAbbreviation}-{Zone}-{Number}</c>. Không cần Revit.
    /// </summary>
    public static class SystemNaming
    {
        private static readonly Regex Hex = new Regex("^#?([0-9a-fA-F]{6}|[0-9a-fA-F]{3})$", RegexOptions.Compiled);

        /// <summary>"#0070C0", "0070C0", "#07C" → RGB. Sai định dạng → false.</summary>
        public static bool TryParseHex(string? text, out Rgb color)
        {
            color = default;
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            var m = Hex.Match(text!.Trim());
            if (!m.Success)
            {
                return false;
            }

            var hex = m.Groups[1].Value;
            if (hex.Length == 3)
            {
                hex = new string(new[] { hex[0], hex[0], hex[1], hex[1], hex[2], hex[2] });
            }

            color = new Rgb(
                byte.Parse(hex.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                byte.Parse(hex.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                byte.Parse(hex.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
            return true;
        }

        /// <summary>Viết tắt mặc định cho các hệ thường gặp (khoá không phân biệt hoa thường).</summary>
        public static readonly IReadOnlyDictionary<string, string> DefaultAbbreviations = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Supply Air"] = "SA",
            ["Return Air"] = "RA",
            ["Exhaust Air"] = "EA",
            ["Fresh Air"] = "FA",
            ["Domestic Cold Water"] = "CW",
            ["Domestic Hot Water"] = "HW",
            ["Sanitary"] = "SAN",
            ["Storm"] = "ST",
            ["Fire Protection Wet"] = "FP",
            ["Sprinkler"] = "SP",
            ["Hydronic Supply"] = "CHS",
            ["Hydronic Return"] = "CHR",
            ["Power"] = "PWR",
            ["Lighting"] = "LTG",
            ["Data"] = "DATA",
        };

        /// <summary>
        /// Sinh tên hệ: các phần rỗng bị bỏ (không để lại "--"), số đệm theo <paramref name="padWidth"/>.
        /// Ví dụ: ("MEC","SA","Z1",3) → "MEC-SA-Z1-003".
        /// </summary>
        public static string Build(string? discipline, string? abbreviation, string? zone, int number, int padWidth = 2, string separator = "-")
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(discipline))
            {
                parts.Add(discipline!.Trim());
            }

            if (!string.IsNullOrWhiteSpace(abbreviation))
            {
                parts.Add(abbreviation!.Trim());
            }

            if (!string.IsNullOrWhiteSpace(zone))
            {
                parts.Add(zone!.Trim());
            }

            parts.Add(number.ToString(CultureInfo.InvariantCulture).PadLeft(Math.Max(0, padWidth), '0'));
            return string.Join(separator, parts);
        }

        /// <summary>Viết tắt cho tên loại hệ: ưu tiên bảng người dùng, rồi bảng mặc định, rồi tự sinh từ chữ cái đầu.</summary>
        public static string Abbreviate(string systemTypeName, IDictionary<string, string>? userMap = null)
        {
            if (string.IsNullOrWhiteSpace(systemTypeName))
            {
                return "SYS";
            }

            if (userMap != null && userMap.TryGetValue(systemTypeName, out var user))
            {
                return user;
            }

            if (DefaultAbbreviations.TryGetValue(systemTypeName, out var known))
            {
                return known;
            }

            var initials = string.Empty;
            foreach (var word in systemTypeName.Split(new[] { ' ', '-', '_' }, StringSplitOptions.RemoveEmptyEntries))
            {
                initials += char.ToUpperInvariant(word[0]);
            }

            return initials.Length == 0 ? "SYS" : initials.Length > 4 ? initials.Substring(0, 4) : initials;
        }
    }
}
