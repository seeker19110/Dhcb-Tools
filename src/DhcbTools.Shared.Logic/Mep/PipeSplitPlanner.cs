using System;
using System.Collections.Generic;
using System.Linq;

namespace DhcbTools.Shared.Logic.Mep
{
    /// <summary>
    /// Phần quyết định còn lại của <c>PipeSplitter</c> ngoài <see cref="MepLayout.SplitPositions"/>: category
    /// nào cắt được (Revit chỉ có BreakCurve cho Pipe/Duct — CableTray/Conduit chỉ liệt kê), và toàn bộ
    /// Summary/Messages. Core chỉ còn gọi BreakCurve.
    /// </summary>
    public static class PipeSplitPlanner
    {
        /// <summary>Pipe và Duct có API BreakCurve; CableTray/Conduit không — chỉ báo cáo, KHÔNG tính vào tổng sẽ cắt.</summary>
        public static bool IsSplittable(string? category)
            => IsPipe(category) || (category ?? string.Empty).IndexOf("Duct", StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>Pipe → PlumbingUtils.BreakCurve; còn lại (Duct) → MechanicalUtils.BreakCurve.</summary>
        public static bool IsPipe(string? category)
            => (category ?? string.Empty).IndexOf("Pipe", StringComparison.OrdinalIgnoreCase) >= 0;

        public static string FormatPointMm(double xMm, double yMm, double zMm) => $"({xMm:F0},{yMm:F0},{zMm:F0})mm";

        public static string PreviewSummary(int splittableCount, int totalSplits, int reportOnlyCount)
            => $"[Xem trước] Sẽ cắt {splittableCount} phần tử, tạo {totalSplits} điểm cắt."
               + (reportOnlyCount > 0
                   ? $" {reportOnlyCount} CableTray/Conduit quá dài chỉ liệt kê (Revit không có API cắt), không tính vào tổng."
                   : string.Empty);

        public static string PreviewLine(string category, long elementId, bool splittable, IReadOnlyList<(double X, double Y, double Z)> pointsMm)
        {
            pointsMm ??= Array.Empty<(double, double, double)>();
            return $"  {category} {elementId}{(splittable ? string.Empty : " [chỉ báo cáo]")}: {pointsMm.Count} điểm cắt tại "
                   + string.Join(", ", pointsMm.Select(p => FormatPointMm(p.X, p.Y, p.Z)));
        }

        public static string WriteSummary(int splitsDone, int splittableCount, int failureCount, int reportOnlyCount)
            => $"Đã cắt {splitsDone} điểm trên {splittableCount} phần tử MEP"
               + (failureCount > 0 ? $", {failureCount} điểm cắt lỗi" : string.Empty)
               + (reportOnlyCount > 0 ? $"; {reportOnlyCount} CableTray/Conduit quá dài chỉ báo cáo (không có API cắt)" : string.Empty)
               + ".";

        public static string ReportOnlyLine(string category, long elementId, int pointCount)
            => $"  {category} {elementId} [chỉ báo cáo]: cần {pointCount} điểm cắt, cắt tay.";

        public static string BreakReturnedNothing(string category, long elementId, double xMm, double yMm, double zMm)
            => $"{category} {elementId}: BreakCurve không tạo được đoạn mới tại {FormatPointMm(xMm, yMm, zMm)}.";

        public static string BreakFailed(string category, long elementId, double xMm, double yMm, double zMm, string reason)
            => $"{category} {elementId}: không cắt được tại {FormatPointMm(xMm, yMm, zMm)} — {reason}";
    }
}
