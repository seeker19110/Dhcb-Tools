using System;
using System.Globalization;

namespace DhcbTools.Shared.Logic.Cad
{
    /// <summary>
    /// Đọc ô lineweight trong CSV của <c>LayerTranslate</c> thành giá trị số của enum
    /// <c>Autodesk.AutoCAD.DatabaseServices.LineWeight</c> (1/100 mm; ByLayer = −1, ByBlock = −2, Default = −3).
    /// Core chỉ còn ép kiểu và kiểm <c>Enum.IsDefined</c> — cách viết được chấp nhận có test ở đây.
    /// </summary>
    public static class LineWeightText
    {
        public const int ByLayer = -1;
        public const int ByBlock = -2;
        public const int Default = -3;

        /// <summary>Giá trị lineweight AutoCAD hợp lệ (1/100 mm) theo enum <c>LineWeight</c>.</summary>
        public static readonly int[] Defined =
        {
            Default, ByBlock, ByLayer,
            0, 5, 9, 13, 15, 18, 20, 25, 30, 35, 40, 50, 53, 60, 70, 80, 90, 100, 106, 120, 140, 158, 200, 211,
        };

        /// <summary>
        /// Chấp nhận: số nguyên (25 → 0,25 mm), tên enum (<c>LineWeight025</c>, <c>ByLayer</c>, <c>ByBlock</c>,
        /// <c>ByLineWeightDefault</c>/<c>Default</c>), hoặc số thập phân mm (<c>0.25</c> → 25). Chỉ trả true cho
        /// giá trị có trong <see cref="Defined"/>.
        /// </summary>
        public static bool TryParse(string? text, out int value)
        {
            value = Default;
            var t = (text ?? string.Empty).Trim();
            if (t.Length == 0)
            {
                return false;
            }

            if (t.Equals("Default", StringComparison.OrdinalIgnoreCase) || t.Equals("ByLineWeightDefault", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (t.Equals("ByLayer", StringComparison.OrdinalIgnoreCase))
            {
                value = ByLayer;
                return true;
            }

            if (t.Equals("ByBlock", StringComparison.OrdinalIgnoreCase))
            {
                value = ByBlock;
                return true;
            }

            if (int.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out var whole))
            {
                value = whole;
                return IsDefined(whole);
            }

            if (t.StartsWith("LineWeight", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(t.Substring("LineWeight".Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out var named))
            {
                value = named;
                return IsDefined(named);
            }

            // "0.25" / "0,25" mm → 25: người xuất từ Excel hay gõ theo mm thật.
            if (NumericText.TryParseDouble(t, out var mm))
            {
                var hundredths = (int)Math.Round(mm * 100);
                if (Math.Abs((mm * 100) - hundredths) < 1e-6 && IsDefined(hundredths))
                {
                    value = hundredths;
                    return true;
                }
            }

            return false;
        }

        public static bool IsDefined(int value) => Array.IndexOf(Defined, value) >= 0;
    }
}
