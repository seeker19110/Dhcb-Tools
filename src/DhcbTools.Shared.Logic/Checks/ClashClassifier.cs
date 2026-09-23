using System;
using DhcbTools.Shared.Logic.Bcf;

namespace DhcbTools.Shared.Logic.Checks
{
    /// <summary>
    /// Loại va chạm phát hiện được trong mô hình.
    /// </summary>
    public enum ClashType
    {
        /// <summary>Không va chạm: không giao nhau và khoảng hở đủ. Không ghi thành topic BCF.</summary>
        None,

        /// <summary>Va chạm cứng: hai phần tử giao nhau sâu hơn dung sai thi công.</summary>
        HardClash,

        /// <summary>Va chạm mềm: không giao nhau nhưng khoảng hở cách nhiệt / bảo trì bị vi phạm.</summary>
        SoftClash,

        /// <summary>Có giao nhau nhưng nông hơn dung sai thi công — ghi nhận, không cần dời tuyến.</summary>
        ToleranceFlaw
    }

    /// <summary>
    /// Chi tiết va chạm sau khi được phân loại và tính toán góc nhìn BCF 3D.
    /// Toạ độ tâm va chạm giữ ở <b>mm</b> (như mô hình); camera BCF trả ở <b>mét</b> vì
    /// <see cref="BcfWriter"/> ghi mét và không đoán đơn vị.
    /// </summary>
    public sealed class ClassifiedClash
    {
        /// <summary>Khoảng lùi camera khỏi tâm va chạm (mm).</summary>
        public const double CameraOffsetMm = 2500;

        public ClassifiedClash(
            long idA,
            string categoryA,
            long idB,
            string categoryB,
            ClashType clashType,
            double xMm,
            double yMm,
            double zMm,
            double overlapVolumeMm3,
            double distanceMm,
            string recommendation,
            double? penetrationDepthMm = null)
        {
            IdA = idA;
            CategoryA = categoryA ?? string.Empty;
            IdB = idB;
            CategoryB = categoryB ?? string.Empty;
            ClashType = clashType;
            XMm = xMm;
            YMm = yMm;
            ZMm = zMm;
            OverlapVolumeMm3 = overlapVolumeMm3;
            DistanceMm = distanceMm;
            PenetrationDepthMm = penetrationDepthMm;
            Recommendation = recommendation ?? string.Empty;

            // Camera phối cảnh nhìn chéo 45° từ phía +X,+Y, hơi cao hơn tâm — cùng góc mọi topic để so được.
            CameraM = new BcfPoint(
                (xMm + CameraOffsetMm) / 1000.0,
                (yMm + CameraOffsetMm) / 1000.0,
                (zMm + (CameraOffsetMm * 0.8)) / 1000.0);
            TargetM = new BcfPoint(xMm / 1000.0, yMm / 1000.0, zMm / 1000.0);
            ViewDirection = new BcfPoint(TargetM.X - CameraM.X, TargetM.Y - CameraM.Y, TargetM.Z - CameraM.Z).Normalised();
        }

        public long IdA { get; }
        public string CategoryA { get; }
        public long IdB { get; }
        public string CategoryB { get; }
        public ClashType ClashType { get; }
        public double XMm { get; }
        public double YMm { get; }
        public double ZMm { get; }
        public double OverlapVolumeMm3 { get; }
        public double DistanceMm { get; }

        /// <summary>Độ sâu xuyên (mm) nếu bên gọi đo được; null = suy từ thể tích giao.</summary>
        public double? PenetrationDepthMm { get; }

        public string Recommendation { get; }

        /// <summary>Tâm va chạm — mét, đúng đơn vị BCF.</summary>
        public BcfPoint TargetM { get; }

        /// <summary>Vị trí camera — mét, đúng đơn vị BCF.</summary>
        public BcfPoint CameraM { get; }

        /// <summary>Hướng nhìn đơn vị từ camera vào tâm.</summary>
        public BcfPoint ViewDirection { get; }

        /// <summary>Có đáng ghi thành topic BCF không (mọi loại trừ <see cref="ClashType.None"/>).</summary>
        public bool IsIssue => ClashType != ClashType.None;

        public string Key => $"{CategoryA}_{IdA}_vs_{CategoryB}_{IdB}";
    }

    /// <summary>
    /// Bộ phân loại va chạm và tính toán góc nhìn BCF 3D.
    /// </summary>
    public static class ClashClassifier
    {
        /// <summary>
        /// Phân loại một cặp phần tử.
        /// </summary>
        /// <param name="overlapVolumeMm3">Thể tích giao (mm³); ≤ 0 = không giao.</param>
        /// <param name="distanceMm">Khoảng hở nhỏ nhất giữa hai phần tử (mm); 0 khi giao nhau.</param>
        /// <param name="toleranceMm">Dung sai thi công (mm) — độ sâu xuyên nhỏ hơn ngưỡng này chỉ là sai số.</param>
        /// <param name="requiredClearanceMm">Khoảng hở bắt buộc (mm).</param>
        /// <param name="penetrationDepthMm">
        /// Độ sâu xuyên đo được (mm). Không có thì suy bằng căn bậc ba của thể tích giao — đây là xấp xỉ:
        /// một vết cà 5 mm trên mặt ống 200×200 có thể tích 200.000 mm³, căn bậc ba ≈ 58 mm; bên gọi có
        /// hình học nên truyền độ sâu thật để phân loại đúng.
        /// </param>
        public static ClassifiedClash Classify(
            long idA,
            string categoryA,
            long idB,
            string categoryB,
            double xMm,
            double yMm,
            double zMm,
            double overlapVolumeMm3,
            double distanceMm,
            double toleranceMm = 5.0,
            double requiredClearanceMm = 100.0,
            double? penetrationDepthMm = null)
        {
            ClashType type;
            string rec;

            if (overlapVolumeMm3 > 0)
            {
                var depth = penetrationDepthMm ?? Math.Pow(overlapVolumeMm3, 1.0 / 3.0);
                if (depth > toleranceMm)
                {
                    type = ClashType.HardClash;
                    rec = $"Yêu cầu dời tuyến hoặc đục lỗ mở cho {categoryA} #{idA} giao cắt {categoryB} #{idB}. "
                          + $"Thể tích giao: {overlapVolumeMm3 / 1e9:F4} m³, xuyên sâu {depth:F0} mm (> dung sai {toleranceMm:F0} mm).";
                }
                else
                {
                    type = ClashType.ToleranceFlaw;
                    rec = $"Giao nhau {depth:F1} mm giữa {categoryA} #{idA} và {categoryB} #{idB} — nằm trong dung sai thi công {toleranceMm:F0} mm, ghi nhận không cần dời tuyến.";
                }
            }
            else if (distanceMm < requiredClearanceMm)
            {
                type = ClashType.SoftClash;
                rec = $"Vi phạm khoảng hở an toàn ({distanceMm:F0}mm < {requiredClearanceMm:F0}mm) giữa {categoryA} #{idA} và {categoryB} #{idB}. Cần điều chỉnh khoảng hở cách nhiệt.";
            }
            else
            {
                type = ClashType.None;
                rec = "Không va chạm: không giao nhau và khoảng hở đủ.";
            }

            return new ClassifiedClash(idA, categoryA, idB, categoryB, type, xMm, yMm, zMm, overlapVolumeMm3, distanceMm, rec, penetrationDepthMm);
        }
    }
}
