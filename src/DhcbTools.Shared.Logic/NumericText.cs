using System.Globalization;

namespace DhcbTools.Shared.Logic
{
    /// <summary>
    /// Định dạng và đọc số cho round-trip CSV. Lỗi #1 trong docs/progress.md: export ghi bằng
    /// InvariantCulture (dấu chấm) còn import đọc theo culture hệ thống (máy tiếng Việt dùng dấu phẩy),
    /// nên mọi giá trị Double xuất ra không nhập ngược được mà không báo lỗi.
    /// Quy ước từ nay: GHI luôn Invariant, ĐỌC chấp nhận cả hai dấu thập phân.
    /// </summary>
    public static class NumericText
    {
        /// <summary>Ghi Double ra chuỗi round-trip được, không phụ thuộc culture máy.</summary>
        public static string Format(double value)
        {
            return value.ToString("R", CultureInfo.InvariantCulture);
        }

        /// <summary>Ghi Double với số chữ số thập phân cố định, không phụ thuộc culture máy.</summary>
        public static string Format(double value, int decimals)
        {
            return value.ToString("F" + decimals.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
        }

        /// <summary>Ghi Integer ra chuỗi, không phụ thuộc culture máy.</summary>
        public static string Format(int value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Đọc Double từ ô CSV. Chấp nhận "1234.5" (Invariant) lẫn "1234,5" (kỹ sư gõ tay trong Excel
        /// tiếng Việt). Vì ô CSV đã tách theo dấu phẩy nên một ô không bao giờ chứa dấu phẩy phân nhóm
        /// hàng nghìn trừ khi được bọc nháy — trường hợp đó ta coi dấu phẩy là dấu thập phân.
        /// </summary>
        public static bool TryParseDouble(string? text, out double value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            var trimmed = text.Trim();

            if (double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            {
                return true;
            }

            // Chỉ đổi khi có đúng một dấu phẩy và không có dấu chấm — tránh nuốt nhầm "1,234.5".
            if (trimmed.IndexOf('.') < 0)
            {
                var firstComma = trimmed.IndexOf(',');
                if (firstComma >= 0 && trimmed.IndexOf(',', firstComma + 1) < 0)
                {
                    var normalized = trimmed.Replace(',', '.');
                    return double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
                }
            }

            return false;
        }

        /// <summary>Đọc Integer từ ô CSV, không phụ thuộc culture máy.</summary>
        public static bool TryParseInt(string? text, out int value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            return int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }
    }
}
