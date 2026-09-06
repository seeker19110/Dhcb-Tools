using System.Collections.Generic;
using System.Linq;

namespace DhcbTools.Shared.Logic.Cad
{
    /// <summary>
    /// Câu chữ cho <c>AutoNumbering</c>/<c>AttributeIncrement</c> khi kỹ sư khai sai tên block hay tag (đóng vai kỹ sư
    /// AutoCAD, §57): bản cũ chỉ nói "Không tìm thấy Block" mà không nói bản vẽ CÓ block gì — người dùng phải chạy
    /// <c>BlockQuantity</c> riêng để tra; và xem trước nói "Sẽ gán N giá trị" trong khi chạy thật mới lộ block không
    /// có attribute đó. Nay lỗi kèm danh sách, và xem trước cảnh báo trước.
    /// </summary>
    public static class BlockMessages
    {
        public const int MaxNames = 10;

        /// <summary>Không có block tên <paramref name="wanted"/>: liệt kê block có thật theo số lượng giảm dần.</summary>
        public static string BlockNotFound(string wanted, IReadOnlyList<KeyValuePair<string, int>> present)
        {
            var head = $"Không tìm thấy Block \"{wanted}\" trong Model Space.";
            if (present.Count == 0)
            {
                return head + " Model Space không có block reference nào.";
            }

            var top = present.OrderByDescending(p => p.Value).ThenBy(p => p.Key, System.StringComparer.OrdinalIgnoreCase)
                .Take(MaxNames).Select(p => $"{p.Key} ×{p.Value}");
            var more = present.Count > MaxNames ? $" … và {present.Count - MaxNames} tên nữa" : string.Empty;
            return head + $" Block có trong bản vẽ ({present.Count} tên): {string.Join(", ", top)}{more}.";
        }

        /// <summary>
        /// Xem trước: <paramref name="missing"/>/<paramref name="total"/> block không có attribute <paramref name="tag"/>.
        /// Trả <c>null</c> khi không thiếu. Tag rỗng nghĩa là "attribute đầu tiên" nên thiếu = block không có attribute nào.
        /// </summary>
        public static string? AttributeMissingWarning(int missing, int total, string? tag, IReadOnlyList<string> tagsPresent)
        {
            if (missing <= 0)
            {
                return null;
            }

            var which = string.IsNullOrEmpty(tag) ? "không có attribute nào" : $"không có attribute \"{tag}\"";
            var tags = tagsPresent.Count == 0
                ? "block này không có attribute nào"
                : "tag có thật: " + string.Join(", ", tagsPresent.Distinct(System.StringComparer.OrdinalIgnoreCase).OrderBy(t => t, System.StringComparer.OrdinalIgnoreCase));
            return $"{missing}/{total} block {which} — {tags}. Chạy thật sẽ bỏ qua những block này.";
        }
    }
}
