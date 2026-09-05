using System;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DhcbTools.Shared.Logic.Ai
{
    /// <summary>
    /// Đổi giữa <b>giá trị JSON trong config</b> và <b>chuỗi hiện trong ô nhập của form</b>.
    /// <para>
    /// Tách khỏi vỏ WPF để có test trên CI: đây là chỗ một dòng sai làm lệnh <b>không chạy được từ
    /// Ribbon</b> mà bộ ca kiểm (gửi thẳng JSON) vẫn xanh. Bản đầu ghép <i>mọi</i> mảng bằng "; ", nên
    /// trường nhận JSON thô (<c>levels</c>, <c>grids</c>, <c>colors</c>) hiện ra thành
    /// <c>{…}; {…}</c> — không còn là JSON, và khi đọc lại thì form báo "trông như JSON nhưng không đọc
    /// được" (tìm ra khi bấm tay 2026-09-05, xem <c>docs/bang-chung-test.md</c> §34).
    /// </para>
    /// </summary>
    public static class FormValueText
    {
        /// <summary>Dấu ngăn dùng khi hiện một danh sách chuỗi.</summary>
        public const string ListSeparator = "; ";

        /// <summary>
        /// Chuỗi để đổ vào ô nhập.
        /// <paramref name="isList"/> = trường nhận <b>danh sách chuỗi</b> (ghép bằng <c>;</c>);
        /// mọi trường hợp còn lại giữ nguyên hình dáng JSON để người dùng sửa rồi gửi lại đúng cấu trúc.
        /// </summary>
        public static string Display(JToken? value, bool isList)
        {
            if (value == null || value.Type == JTokenType.Null)
            {
                return string.Empty;
            }

            if (isList && value.Type == JTokenType.Array)
            {
                return string.Join(ListSeparator, value.Select(v => v.ToString()));
            }

            if (value.Type == JTokenType.Array || value.Type == JTokenType.Object)
            {
                return value.ToString(Formatting.Indented);
            }

            return value.ToString();
        }

        /// <summary>
        /// Đọc ô số thành JSON. Trả <b>số nguyên</b> khi người dùng gõ số nguyên — không phải "1.0".
        /// <para>
        /// Vì sao quan trọng: property kiểu <c>int</c> nhận JSON <c>1.0</c> thì Newtonsoft từ chối
        /// (<i>Input string '1.0' is not a valid integer</i>) và lệnh không chạy được từ Ribbon, dù người
        /// dùng gõ đúng "1" (bắt được khi bấm tay 2026-09-05 ở <c>RevisionOnSheets</c>, §34).
        /// </para>
        /// <para>Nhận cả dấu phẩy thập phân: máy tiếng Việt gõ "1,5" mà JSON chỉ hiểu dấu chấm.</para>
        /// </summary>
        public static JToken? Number(string? text)
        {
            var trimmed = (text ?? string.Empty).Trim();
            if (trimmed.Length == 0)
            {
                return null;
            }

            var normalised = trimmed.Replace(',', '.');
            if (long.TryParse(normalised, NumberStyles.Integer, CultureInfo.InvariantCulture, out var whole))
            {
                return new JValue(whole);
            }

            if (double.TryParse(normalised, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
            {
                // "3.0" người dùng gõ tay cũng là số nguyên với property int.
                return number == Math.Floor(number) && Math.Abs(number) < 9.2e18
                    ? new JValue((long)number)
                    : new JValue(number);
            }

            return null;
        }

        /// <summary>Chuỗi này có phải người dùng đang gõ JSON thô không (để form thử đọc bằng JSON).</summary>
        public static bool LooksLikeJson(string? text)
        {
            var trimmed = (text ?? string.Empty).TrimStart();
            return trimmed.StartsWith("{", StringComparison.Ordinal)
                   || trimmed.StartsWith("[", StringComparison.Ordinal);
        }
    }
}
