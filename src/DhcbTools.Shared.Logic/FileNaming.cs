using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace DhcbTools.Shared.Logic
{
    /// <summary>
    /// Đặt tên file đầu ra cho lệnh xuất hàng loạt. Tách khỏi <c>BatchExportCommand</c> vì đây là chỗ
    /// dễ sinh tên rỗng/trùng/không hợp lệ nhất, mà lại kiểm chứng được không cần Revit.
    /// </summary>
    public static class FileNaming
    {
        /// <summary>Tên thay thế khi chuỗi đầu vào rỗng hoặc chỉ gồm ký tự không hợp lệ.</summary>
        public const string Fallback = "unnamed";

        /// <summary>
        /// Thay ký tự không hợp lệ trong tên file bằng "_". Ký tự không hợp lệ được lấy theo
        /// <see cref="Path.GetInvalidFileNameChars"/> và bổ sung tập ký tự cấm của Windows để tên sinh
        /// trên máy Linux (CI, batch runner) vẫn dùng được khi copy sang Windows.
        /// </summary>
        public static string Sanitize(string? name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return Fallback;
            }

            var invalid = new HashSet<char>(Path.GetInvalidFileNameChars());
            foreach (var c in "<>:\"/\\|?*")
            {
                invalid.Add(c);
            }

            var sb = new StringBuilder(name.Length);
            foreach (var c in name)
            {
                sb.Append(invalid.Contains(c) || c < ' ' ? '_' : c);
            }

            // Windows bỏ dấu cách và dấu chấm ở cuối tên file — cắt luôn để tên trên đĩa đúng như tên đã log.
            var trimmed = sb.ToString().Trim().TrimEnd('.', ' ');
            return trimmed.Length == 0 ? Fallback : trimmed;
        }

        /// <summary>
        /// Áp mẫu tên file. Token hỗ trợ: {SheetNumber}, {SheetName}, {ProjectNumber}.
        /// Mỗi giá trị được sanitize riêng TRƯỚC khi ghép, để dấu "/" trong số bản vẽ ("A/01")
        /// không tạo thành thư mục con ngoài ý muốn.
        /// </summary>
        public static string ApplyPattern(string pattern, string sheetNumber, string sheetName, string projectNumber)
        {
            if (string.IsNullOrEmpty(pattern))
            {
                pattern = "{SheetNumber}-{SheetName}";
            }

            var applied = pattern
                .Replace("{SheetNumber}", Sanitize(sheetNumber))
                .Replace("{SheetName}", Sanitize(sheetName))
                .Replace("{ProjectNumber}", Sanitize(projectNumber));

            // Sanitize lần cuối để bắt ký tự không hợp lệ nằm ngay trong bản thân mẫu.
            return Sanitize(applied);
        }

        /// <summary>
        /// Thêm hậu tố " (2)", " (3)"… khi tên đã tồn tại trong lô xuất, để hai bản vẽ trùng tên
        /// không ghi đè nhau — bản cũ ghi đè âm thầm.
        /// </summary>
        public static string MakeUnique(string name, ISet<string> usedNames)
        {
            if (usedNames == null)
            {
                throw new ArgumentNullException(nameof(usedNames));
            }

            var candidate = Sanitize(name);
            var suffix = 2;
            while (!usedNames.Add(candidate.ToUpperInvariant()))
            {
                candidate = Sanitize(name) + " (" + suffix.ToString(System.Globalization.CultureInfo.InvariantCulture) + ")";
                suffix++;
            }

            return candidate;
        }
    }
}
