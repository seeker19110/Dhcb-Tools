using System;
using System.Collections.Generic;
using System.Globalization;

namespace DhcbTools.Shared.Logic.Checks
{
    /// <summary>
    /// Loại va chạm phát hiện được trong mô hình.
    /// </summary>
    public enum ClashType
    {
        /// <summary>Va chạm cứng (thể tích hình học giao nhau thực tế vượt quá dung sai).</summary>
        HardClash,
        /// <summary>Va chạm mềm (khoảng hở cách nhiệt / không gian bảo trì bị vi phạm).</summary>
        SoftClash,
        /// <summary>Sai số trong ngưỡng dung sai cho phép (bỏ qua).</summary>
        ToleranceFlaw
    }

    /// <summary>
    /// Chi tiết va chạm sau khi được phân loại và tính toán góc nhìn BCF 3D.
    /// </summary>
    public sealed class ClassifiedClash
    {
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
            string recommendation)
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
            Recommendation = recommendation ?? string.Empty;

            // Compute 3D BCF Viewpoint Camera Position (looking down at 45 degree angle from +X,+Y,+Z)
            double offset = 2500; // 2.5m view offset
            CameraX = XMm + offset;
            CameraY = YMm + offset;
            CameraZ = ZMm + offset * 0.8;

            ViewDirX = (XMm - CameraX);
            ViewDirY = (YMm - CameraY);
            ViewDirZ = (ZMm - CameraZ);

            double norm = Math.Sqrt(ViewDirX * ViewDirX + ViewDirY * ViewDirY + ViewDirZ * ViewDirZ);
            if (norm > 1e-6)
            {
                ViewDirX /= norm;
                ViewDirY /= norm;
                ViewDirZ /= norm;
            }
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
        public string Recommendation { get; }

        public double CameraX { get; }
        public double CameraY { get; }
        public double CameraZ { get; }

        public double ViewDirX { get; }
        public double ViewDirY { get; }
        public double ViewDirZ { get; }

        public string Key => $"{CategoryA}_{IdA}_vs_{CategoryB}_{IdB}";
    }

    /// <summary>
    /// Bộ phân loại va chạm và tính toán góc nhìn BCF 3D.
    /// </summary>
    public static class ClashClassifier
    {
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
            double requiredClearanceMm = 100.0)
        {
            ClashType type;
            string rec;

            if (overlapVolumeMm3 > toleranceMm * toleranceMm * toleranceMm)
            {
                type = ClashType.HardClash;
                rec = $"Yêu cầu dời tuyến hoặc đục lỗ mở cho {categoryA} #{idA} giao cắt {categoryB} #{idB}. Thể tích giao: {overlapVolumeMm3 / 1e9:F4} m³.";
            }
            else if (distanceMm < requiredClearanceMm)
            {
                type = ClashType.SoftClash;
                rec = $"Vi phạm khoảng hở an toàn ({distanceMm:F0}mm < {requiredClearanceMm:F0}mm) giữa {categoryA} #{idA} và {categoryB} #{idB}. Cần điều chỉnh khoảng hở cách nhiệt.";
            }
            else
            {
                type = ClashType.ToleranceFlaw;
                rec = "Va chạm nằm trong ngưỡng dung sai cho phép.";
            }

            return new ClassifiedClash(idA, categoryA, idB, categoryB, type, xMm, yMm, zMm, overlapVolumeMm3, distanceMm, rec);
        }
    }
}
