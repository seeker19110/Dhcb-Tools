using System.Collections.Generic;

namespace DhcbTools.Shared.Logic.Mep
{
    /// <summary>
    /// Phần quyết định còn lại của <c>ElevationTag</c> ngoài <see cref="MepLayout.Elevations"/>: lọc category
    /// (dùng chung luật với <see cref="SleevePlanner.CategoryIncluded"/>) và toàn bộ Summary/Messages —
    /// nhất là câu "không gán được cho phần tử nào", thứ trước đây bị báo như thành công ("0/N").
    /// </summary>
    public static class ElevationTagPlanner
    {
        /// <summary>Category có được xét không theo <c>categories</c> trong config (rỗng = tất cả).</summary>
        public static bool CategoryIncluded(string builtInCategoryName, IReadOnlyList<string>? filter)
            => SleevePlanner.CategoryIncluded(SleevePlanner.ShortCategoryName(builtInCategoryName), filter);

        public static string PreviewSummary(int count) => $"[Xem trước] Sẽ gán cao độ cho {count} phần tử MEP.";

        public static string PreviewLine(long elementId, double bottomMm, double topMm, double centreMm)
            => $"  {elementId}: đáy={bottomMm:F1}mm, đỉnh={topMm:F1}mm, tim={centreMm:F1}mm";

        public static string WriteSummary(int updated, int planned) => $"Đã gán cao độ cho {updated}/{planned} phần tử MEP.";

        /// <summary>
        /// Không phần tử nào ghi được = dự án không có tham số cao độ nào trong từ điển. Đây là LỖI,
        /// không phải "0/N thành công" — người gọi đặt Success=false và kèm gợi ý tra tham số.
        /// </summary>
        public static bool NothingWritten(int updated, int planned) => updated == 0 && planned > 0;

        public static string NothingWrittenSummary(int planned) => $"Không gán được cao độ cho phần tử nào trong {planned} phần tử.";

        public static string SetFailedLine(string? parameterName, long elementId, string reason)
            => $"Không gán được {parameterName} cho {elementId}: {reason}";
    }
}
