using System;
using System.Collections.Generic;
using System.Linq;

namespace DhcbTools.Shared.Logic.Setout
{
    /// <summary>Trạng thái độ lệch trắc đạc công trường.</summary>
    public enum SetoutDeviationStatus
    {
        InTolerance,
        WarningThreshold,
        OutOfTolerance,

        /// <summary>Điểm thiết kế chưa có số đo hiện trường — KHÔNG được coi là đạt.</summary>
        NotSurveyed
    }

    /// <summary>Kết quả phân tích độ lệch điểm trắc đạc thực tế so với thiết kế BIM.</summary>
    public sealed class PointDeviationRecord
    {
        public PointDeviationRecord(
            string pointCode,
            string elementName,
            double designX, double designY, double designZ,
            double actualX, double actualY, double actualZ,
            double maxAllowedToleranceMm)
            : this(pointCode, elementName, designX, designY, designZ, maxAllowedToleranceMm)
        {
            ActualX = actualX;
            ActualY = actualY;
            ActualZ = actualZ;

            DeltaX = ActualX - DesignX;
            DeltaY = ActualY - DesignY;
            DeltaZ = ActualZ - DesignZ;
            TotalDistanceErrorMm = Math.Sqrt((DeltaX * DeltaX) + (DeltaY * DeltaY) + (DeltaZ * DeltaZ));
            PlanarErrorMm = Math.Sqrt((DeltaX * DeltaX) + (DeltaY * DeltaY));

            if (TotalDistanceErrorMm <= maxAllowedToleranceMm)
                Status = SetoutDeviationStatus.InTolerance;
            else if (TotalDistanceErrorMm <= maxAllowedToleranceMm * 1.5)
                Status = SetoutDeviationStatus.WarningThreshold;
            else
                Status = SetoutDeviationStatus.OutOfTolerance;
        }

        /// <summary>Bản ghi cho điểm chưa đo: toạ độ thực tế và độ lệch là NaN, trạng thái <see cref="SetoutDeviationStatus.NotSurveyed"/>.</summary>
        private PointDeviationRecord(string pointCode, string elementName, double designX, double designY, double designZ, double maxAllowedToleranceMm)
        {
            PointCode = pointCode ?? string.Empty;
            ElementName = elementName ?? string.Empty;
            DesignX = designX;
            DesignY = designY;
            DesignZ = designZ;
            MaxAllowedToleranceMm = maxAllowedToleranceMm;
            ActualX = ActualY = ActualZ = double.NaN;
            DeltaX = DeltaY = DeltaZ = double.NaN;
            TotalDistanceErrorMm = PlanarErrorMm = double.NaN;
            Status = SetoutDeviationStatus.NotSurveyed;
        }

        internal static PointDeviationRecord NotSurveyed(string pointCode, string elementName, double x, double y, double z, double tol) =>
            new PointDeviationRecord(pointCode, elementName, x, y, z, tol);

        public string PointCode { get; }
        public string ElementName { get; }
        public double DesignX { get; }
        public double DesignY { get; }
        public double DesignZ { get; }
        public double ActualX { get; }
        public double ActualY { get; }
        public double ActualZ { get; }

        public double DeltaX { get; }
        public double DeltaY { get; }
        public double DeltaZ { get; }
        public double TotalDistanceErrorMm { get; }
        public double PlanarErrorMm { get; }
        public double MaxAllowedToleranceMm { get; }
        public SetoutDeviationStatus Status { get; }

        /// <summary>Có số đo hiện trường không.</summary>
        public bool HasSurvey => Status != SetoutDeviationStatus.NotSurveyed;
    }

    /// <summary>Bộ phân tích độ lệch trắc đạc công trường so với mô hình thiết kế.</summary>
    public static class SetoutDeviationAnalyzer
    {
        /// <summary>
        /// Mỗi điểm thiết kế cho đúng một bản ghi, kể cả khi chưa đo (trạng thái NotSurveyed) — điểm chưa đo
        /// không được biến mất khỏi báo cáo. Mã điểm trùng trong file đo thì lấy lần đo SAU CÙNG (đo lại).
        /// </summary>
        public static IReadOnlyList<PointDeviationRecord> AnalyzeDeviations(
            IReadOnlyList<(string Code, string Name, double X, double Y, double Z)>? designPoints,
            IReadOnlyList<(string Code, double X, double Y, double Z)>? actualSurveyPoints,
            double maxToleranceMm = 10.0)
        {
            if (designPoints == null || actualSurveyPoints == null) return Array.Empty<PointDeviationRecord>();

            var actualMap = new Dictionary<string, (string Code, double X, double Y, double Z)>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in actualSurveyPoints)
            {
                actualMap[(p.Code ?? string.Empty).Trim()] = p;
            }

            var results = new List<PointDeviationRecord>(designPoints.Count);
            foreach (var dp in designPoints)
            {
                var code = (dp.Code ?? string.Empty).Trim();
                results.Add(actualMap.TryGetValue(code, out var ap)
                    ? new PointDeviationRecord(code, dp.Name, dp.X, dp.Y, dp.Z, ap.X, ap.Y, ap.Z, maxToleranceMm)
                    : PointDeviationRecord.NotSurveyed(code, dp.Name, dp.X, dp.Y, dp.Z, maxToleranceMm));
            }

            return results;
        }
    }
}
