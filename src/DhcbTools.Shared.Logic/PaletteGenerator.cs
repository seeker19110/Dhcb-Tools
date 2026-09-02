using System;
using System.Collections.Generic;
using DhcbTools.Shared.Logic.Mep;

namespace DhcbTools.Shared.Logic
{
    /// <summary>
    /// Sinh bảng màu phân biệt cho N giá trị (mục 7.4, học từ Colour Splasher): quay vòng hue với bước vàng
    /// (golden angle) để các màu kề nhau khác xa, độ bão hoà/sáng cố định để đọc được trên nền trắng và xám Revit.
    /// Ổn định: cùng danh sách giá trị → cùng màu; cùng giá trị ở lần chạy khác → cùng màu nếu dùng <see cref="ForValue"/>.
    /// </summary>
    public static class PaletteGenerator
    {
        private const double GoldenAngle = 137.50776405003785;

        /// <summary>Màu thứ <paramref name="index"/> trong dãy.</summary>
        public static Rgb ByIndex(int index, double saturation = 0.65, double lightness = 0.5)
        {
            if (index < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            var hue = (index * GoldenAngle) % 360.0;
            return HslToRgb(hue, saturation, lightness);
        }

        /// <summary>Màu theo giá trị (băm ổn định) — cùng chuỗi luôn cùng màu dù thứ tự khác.</summary>
        public static Rgb ForValue(string? value, double saturation = 0.65, double lightness = 0.5)
        {
            var text = value ?? string.Empty;
            unchecked
            {
                var hash = 2166136261u;
                foreach (var c in text)
                {
                    hash = (hash ^ c) * 16777619u;
                }

                return HslToRgb(hash % 360u, saturation, lightness);
            }
        }

        /// <summary>Gán màu cho danh sách giá trị riêng biệt theo thứ tự xuất hiện (màu kề nhau khác xa).</summary>
        public static Dictionary<string, Rgb> Assign(IEnumerable<string?> values, IDictionary<string, string>? fixedHex = null)
        {
            var result = new Dictionary<string, Rgb>(StringComparer.OrdinalIgnoreCase);
            var i = 0;
            foreach (var raw in values)
            {
                var v = raw ?? string.Empty;
                if (result.ContainsKey(v))
                {
                    continue;
                }

                if (fixedHex != null && fixedHex.TryGetValue(v, out var hex) && SystemNaming.TryParseHex(hex, out var fixedRgb))
                {
                    result[v] = fixedRgb;
                    continue;
                }

                result[v] = ByIndex(i++);
            }

            return result;
        }

        public static Rgb HslToRgb(double h, double s, double l)
        {
            h = ((h % 360) + 360) % 360;
            s = Math.Max(0, Math.Min(1, s));
            l = Math.Max(0, Math.Min(1, l));
            var c = (1 - Math.Abs(2 * l - 1)) * s;
            var x = c * (1 - Math.Abs((h / 60.0) % 2 - 1));
            var m = l - c / 2;
            double r, g, b;
            if (h < 60) { r = c; g = x; b = 0; }
            else if (h < 120) { r = x; g = c; b = 0; }
            else if (h < 180) { r = 0; g = c; b = x; }
            else if (h < 240) { r = 0; g = x; b = c; }
            else if (h < 300) { r = x; g = 0; b = c; }
            else { r = c; g = 0; b = x; }
            return new Rgb((byte)Math.Round((r + m) * 255), (byte)Math.Round((g + m) * 255), (byte)Math.Round((b + m) * 255));
        }

        /// <summary>Khoảng cách màu đơn giản (Euclid RGB) để test "màu kề nhau khác xa".</summary>
        public static double Distance(Rgb a, Rgb b)
            => Math.Sqrt(Math.Pow(a.R - b.R, 2) + Math.Pow(a.G - b.G, 2) + Math.Pow(a.B - b.B, 2));
    }
}
