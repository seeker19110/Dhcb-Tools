using System;
using System.Collections.Generic;

namespace DhcbTools.Shared.Logic.Mep
{
    /// <summary>
    /// Phần quyết định còn lại của <c>HangerAuto</c> ngoài <see cref="MepLayout.HangerPositions"/>/<see cref="MepLayout.IsNearAny"/>:
    /// có phải xoay hanger theo hướng tuyến không, góc xoay, và toàn bộ Summary/Messages. Core chỉ còn
    /// đặt family instance.
    /// </summary>
    public static class HangerPlanner
    {
        /// <summary>Ngưỡng coi hướng tuyến là "dọc trục X" — không cần xoay.</summary>
        public const double AxisTolerance = 0.01;

        /// <summary>Số loại lý do lỗi tối đa ghi vào Messages.</summary>
        public const int MaxFailureReasons = 5;

        /// <summary>Hướng tuyến lệch khỏi trục X (theo mặt bằng) thì phải xoay hanger cho khớp.</summary>
        public static bool NeedsRotation(double dirX, double dirY)
            => Math.Abs(dirX) > AxisTolerance || Math.Abs(dirY) > AxisTolerance;

        /// <summary>Góc xoay quanh trục Z (radian) để trục X của family trùng hướng tuyến trên mặt bằng.</summary>
        public static double RotationAngle(double dirX, double dirY) => Math.Atan2(dirY, dirX);

        public static string SkipNote(int skippedExisting)
            => skippedExisting > 0 ? $" Bỏ qua, đã có hanger: {skippedExisting} vị trí." : string.Empty;

        public static string PreviewSummary(int planned, int elementCount, int skippedExisting)
            => $"[Xem trước] Sẽ đặt {planned} hanger trên {elementCount} phần tử MEP." + SkipNote(skippedExisting);

        public static string PreviewLine(double xMm, double yMm, double zMm, double dirX, double dirY, double dirZ)
            => $"  → ({xMm:F0}, {yMm:F0}, {zMm:F0}) mm  dir=({dirX:F2},{dirY:F2},{dirZ:F2})";

        public static string WriteSummary(int placed, int elementCount, int skippedExisting, int failed, int planned)
        {
            var summary = $"Đã đặt {placed} hanger trên {elementCount} phần tử MEP." + SkipNote(skippedExisting);
            if (failed > 0)
            {
                summary += $" {failed}/{planned} vị trí đặt lỗi.";
            }

            return summary;
        }

        /// <summary>Thêm lý do lỗi nếu chưa có và chưa vượt trần — nuốt im lặng thì "0 hanger" không ai biết vì sao.</summary>
        public static void AddDistinctReason(List<string> reasons, string message)
        {
            if (reasons == null)
            {
                throw new ArgumentNullException(nameof(reasons));
            }

            if (reasons.Count < MaxFailureReasons && !reasons.Contains(message))
            {
                reasons.Add(message);
            }
        }

        /// <summary>Dòng Messages liệt kê lý do lỗi; null khi không có lỗi.</summary>
        public static string? FailureReasonsLine(int failed, IReadOnlyList<string>? reasons)
            => failed > 0
                ? $"{failed} vị trí không đặt được hanger. Lý do (tối đa {MaxFailureReasons} loại): " + string.Join(" | ", reasons ?? Array.Empty<string>())
                : null;
    }
}
