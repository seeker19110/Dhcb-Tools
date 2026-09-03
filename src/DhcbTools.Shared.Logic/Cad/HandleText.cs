using System;
using System.Globalization;

namespace DhcbTools.Shared.Logic.Cad
{
    /// <summary>
    /// Đọc "handle" của đối tượng AutoCAD từ chuỗi. Handle là số hex trong file DWG (ví dụ <c>1A3</c>),
    /// và là **định danh bền** duy nhất của một entity — khác <c>ObjectId</c> vốn chỉ là con trỏ trong
    /// một phiên, mở lại file là đổi.
    /// <para>
    /// Tách ra đây (không tham chiếu API Autodesk) vì đây là chỗ dễ sai mà lại không test được nếu nằm
    /// trong handler: agent gửi xuống lúc thì <c>"1A3"</c>, lúc <c>"0x1A3"</c>, lúc <c>"(1A3)"</c> do
    /// copy từ chỗ khác. Nhận sai một cái là truy vấn trả rỗng mà không báo gì.
    /// </para>
    /// </summary>
    public static class HandleText
    {
        /// <summary>
        /// Đọc chuỗi handle thành số. Chấp nhận tiền tố <c>0x</c>, dấu ngoặc, khoảng trắng thừa và
        /// chữ hoa/thường. Trả <c>false</c> cho chuỗi rỗng hoặc không phải hex.
        /// </summary>
        public static bool TryParse(string? text, out long value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            var s = text!.Trim().Trim('(', ')', '<', '>').Trim();
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                s = s.Substring(2);
            }

            if (s.Length == 0)
            {
                return false;
            }

            return long.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
        }

        /// <summary>Viết handle ra chuỗi theo đúng cách AutoCAD hiển thị: hex viết hoa, không tiền tố.</summary>
        public static string ToText(long value) => value.ToString("X", CultureInfo.InvariantCulture);
    }
}
