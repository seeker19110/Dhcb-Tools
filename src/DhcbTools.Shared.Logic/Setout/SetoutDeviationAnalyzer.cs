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
        OutOfTolerance
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
        {
            PointCode = pointCode ?? string.Empty;
            ElementName = elementName ?? string.Empty;
            DesignX = designX;
            DesignY = designY;
            DesignZ = designZ;
            ActualX = actualX;
            ActualY = actualY;
            ActualZ = actualZ;

            DeltaX = ActualX - DesignX;
            DeltaY = ActualY - DesignY;
            DeltaZ = ActualZ - DesignZ;
            TotalDistanceErrorMm = Math.Sqrt(DeltaX * DeltaX + DeltaY * DeltaY + DeltaZ * DeltaZ);
            PlanarErrorMm = Math.Sqrt(DeltaX * DeltaX + DeltaY * DeltaY);
            MaxAllowedToleranceMm = maxAllowedToleranceMm;

            if (TotalDistanceErrorMm <= maxAllowedToleranceMm)
                Status = SetoutDeviationStatus.InTolerance;
            else if (TotalDistanceErrorMm <= maxAllowedToleranceMm * 1.5)
                Status = SetoutDeviationStatus.WarningThreshold;
            else
                Status = SetoutDeviationStatus.OutOfTolerance;
        }

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
    }

    /// <summary>Bộ phân tích độ lệch trắc đạc công trường so với mô hình thiết kế.</summary>
    public static class SetoutDeviationAnalyzer
    {
        public static IReadOnlyList<PointDeviationRecord> AnalyzeDeviations(
            IReadOnlyList<(string Code, string Name, double X, double Y, double Z)> designPoints,
            IReadOnlyList<(string Code, double X, double Y, double Z)> actualSurveyPoints,
            double maxToleranceMm = 10.0)
        {
            if (designPoints == null || actualSurveyPoints == null) return Array.Empty<PointDeviationRecord>();

            var actualMap = actualSurveyPoints.ToDictionary(p => p.Code, p => p, StringComparer.OrdinalIgnoreCase);
            var results = new List<PointDeviationRecord>();

            foreach (var dp in designPoints)
            {
                if (actualMap.TryGetValue(dp.Code, out var ap))
                {
                    results.Add(new PointDeviationRecord(
                        dp.Code, dp.Name,
                        dp.X, dp.Y, dp.Z,
                        ap.X, ap.Y, ap.Z,
                        maxToleranceMm));
                }
            }

            return results;
        }
    }
}
