using System;
using System.Text;

namespace DhcbTools.Shared.Logic
{
    /// <summary>Thay chuỗi thường (không regex) cho lệnh <c>TextReplace</c> — tách ra để có test trên CI.</summary>
    public static class TextReplace
    {
        /// <summary>
        /// Thay MỌI lần xuất hiện của <paramref name="find"/>, có/không phân biệt hoa thường. Tự viết vì
        /// <c>string.Replace(…, StringComparison)</c> không có trên net48 (AutoCAD ≤ 2024). Khớp không chồng lấn,
        /// quét trái sang phải. <paramref name="find"/> rỗng trả nguyên <paramref name="value"/> — vòng lặp
        /// <c>IndexOf("")</c> luôn trúng tại chỗ nên nếu không chặn là treo vô hạn.
        /// </summary>
        public static string ReplaceAll(string? value, string? find, string? replace, bool ignoreCase)
        {
            if (string.IsNullOrEmpty(value) || string.IsNullOrEmpty(find))
            {
                return value ?? string.Empty;
            }

            replace ??= string.Empty;
            var comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            var sb = new StringBuilder(value!.Length);
            var index = 0;
            while (index < value.Length)
            {
                var hit = value.IndexOf(find!, index, comparison);
                if (hit < 0)
                {
                    sb.Append(value, index, value.Length - index);
                    break;
                }

                sb.Append(value, index, hit - index).Append(replace);
                index = hit + find!.Length;
            }

            return sb.ToString();
        }
    }
}
