using System;
using System.Collections.Generic;
using System.Linq;

namespace DhcbTools.Shared.Logic.Mep
{
    /// <summary>
    /// Các tiêu chí ưu tiên cho phương án tuyến MEP.
    /// </summary>
    public enum RouteStrategy
    {
        /// <summary>Ưu tiên chiều dài tuyến ngắn nhất.</summary>
        Shortest,
        /// <summary>Ưu tiên giảm tối đa số lần rẽ (fitting).</summary>
        LeastTurns,
        /// <summary>Ưu tiên giữ khoảng cách xa nhất với chướng ngại vật (thoáng nhất).</summary>
        MaxClearance
    }

    /// <summary>
    /// Một phương án tuyến đường ứng viên kèm bảng điểm breakdown.
    /// </summary>
    public sealed class RouteCandidateOption
    {
        public RouteCandidateOption(
            string optionId,
            string title,
            RouteStrategy strategy,
            IReadOnlyList<Point3> points,
            double lengthMm,
            int turnCount,
            double minObsDistanceMm,
            double score)
        {
            OptionId = optionId ?? throw new ArgumentNullException(nameof(optionId));
            Title = title ?? throw new ArgumentNullException(nameof(title));
            Strategy = strategy;
            Points = points ?? throw new ArgumentNullException(nameof(points));
            LengthMm = lengthMm;
            TurnCount = turnCount;
            MinObsDistanceMm = minObsDistanceMm;
            Score = Math.Max(0.0, Math.Min(1.0, score));
        }

        public string OptionId { get; }
        public string Title { get; }
        public RouteStrategy Strategy { get; }
        public IReadOnlyList<Point3> Points { get; }
        public double LengthMm { get; }
        public int TurnCount { get; }
        public double MinObsDistanceMm { get; }
        public double Score { get; }

        public string Summary => $"{Title}: Dài {LengthMm / 1000.0:F2}m, {TurnCount} rẽ, Khoảng hở min {MinObsDistanceMm:F0}mm (Điểm: {Score:F2})";
    }

    /// <summary>
    /// Bộ sinh và đánh giá đa phương án tuyến MEP.
    /// </summary>
    public static class RouteOptionGenerator
    {
        /// <summary>
        /// Sinh ra danh sách các phương án tuyến MEP ứng viên với cấu hình phạt khác nhau và chấm điểm tổng hợp.
        /// </summary>
        public static IReadOnlyList<RouteCandidateOption> GenerateCandidates(
            Point3 start,
            Point3 goal,
            IReadOnlyList<Box3> obstacles,
            PathFinderOptions baseOptions)
        {
            if (baseOptions == null) throw new ArgumentNullException(nameof(baseOptions));
            obstacles = obstacles ?? Array.Empty<Box3>();

            var strategies = new[]
            {
                (Id: "OPT-1", Title: "Tuyến ngắn nhất", Strategy: RouteStrategy.Shortest, TurnPen: baseOptions.TurnPenalty, NearObsPen: baseOptions.NearObstaclePenalty),
                (Id: "OPT-2", Title: "Tuyến ít rẽ nhất", Strategy: RouteStrategy.LeastTurns, TurnPen: baseOptions.TurnPenalty * 3.5, NearObsPen: baseOptions.NearObstaclePenalty),
                (Id: "OPT-3", Title: "Tuyến an toàn nhất", Strategy: RouteStrategy.MaxClearance, TurnPen: baseOptions.TurnPenalty, NearObsPen: baseOptions.NearObstaclePenalty * 4.0)
            };

            var rawResults = new List<(string Id, string Title, RouteStrategy Strategy, List<Point3> Points, double Length, int Turns, double MinDist)>();

            foreach (var s in strategies)
            {
                var opt = new PathFinderOptions
                {
                    StepMm = baseOptions.StepMm,
                    ClearanceMm = baseOptions.ClearanceMm,
                    AllowVertical = baseOptions.AllowVertical,
                    MaxExpandedNodes = baseOptions.MaxExpandedNodes,
                    TurnPenalty = s.TurnPen,
                    NearObstaclePenalty = s.NearObsPen
                };

                var bounds = AutoRoutePlanner.SearchBounds(start, goal, 1500, 1500);
                var pathResult = PathFinder3D.FindPath(start, goal, obstacles, bounds, opt);
                if (pathResult != null && pathResult.Found && pathResult.Polyline != null && pathResult.Polyline.Count > 1)
                {
                    double len = pathResult.LengthMm > 0 ? pathResult.LengthMm : CalculateLength(pathResult.Polyline);
                    int turns = pathResult.Turns;
                    double minDist = CalculateMinObstacleDistance(pathResult.Polyline, obstacles);
                    rawResults.Add((s.Id, s.Title, s.Strategy, pathResult.Polyline, len, turns, minDist));
                }
            }

            if (rawResults.Count == 0)
                return Array.Empty<RouteCandidateOption>();

            // Deduplicate routes based on total length & turns
            var uniqueResults = rawResults
                .GroupBy(r => $"{Math.Round(r.Length, 1)}_{r.Turns}")
                .Select(g => g.First())
                .ToList();

            double minLen = uniqueResults.Min(r => r.Length);
            double maxLen = uniqueResults.Max(r => r.Length);
            int minTurns = uniqueResults.Min(r => r.Turns);
            int maxTurns = uniqueResults.Max(r => r.Turns);
            double maxDist = uniqueResults.Max(r => r.MinDist);

            var candidates = new List<RouteCandidateOption>();
            foreach (var r in uniqueResults)
            {
                // Score formula: length weight 40%, turns weight 40%, clearance weight 20%
                double lenScore = maxLen > minLen ? 1.0 - (r.Length - minLen) / (maxLen - minLen) : 1.0;
                double turnScore = maxTurns > minTurns ? 1.0 - (double)(r.Turns - minTurns) / (maxTurns - minTurns) : 1.0;
                double distScore = maxDist > 0 ? Math.Min(1.0, r.MinDist / Math.Max(1.0, maxDist)) : 1.0;

                double totalScore = 0.4 * lenScore + 0.4 * turnScore + 0.2 * distScore;
                candidates.Add(new RouteCandidateOption(r.Id, r.Title, r.Strategy, r.Points, r.Length, r.Turns, r.MinDist, totalScore));
            }

            return candidates.OrderByDescending(c => c.Score).ToList();
        }

        public static double CalculateLength(IReadOnlyList<Point3> points)
        {
            if (points == null || points.Count < 2) return 0;
            double total = 0;
            for (int i = 0; i < points.Count - 1; i++)
            {
                double dx = points[i + 1].X - points[i].X;
                double dy = points[i + 1].Y - points[i].Y;
                double dz = points[i + 1].Z - points[i].Z;
                total += Math.Sqrt(dx * dx + dy * dy + dz * dz);
            }
            return total;
        }

        public static int CalculateTurns(IReadOnlyList<Point3> points)
        {
            if (points == null || points.Count < 3) return 0;
            int turns = 0;
            for (int i = 1; i < points.Count - 1; i++)
            {
                double v1x = points[i].X - points[i - 1].X;
                double v1y = points[i].Y - points[i - 1].Y;
                double v1z = points[i].Z - points[i - 1].Z;

                double v2x = points[i + 1].X - points[i].X;
                double v2y = points[i + 1].Y - points[i].Y;
                double v2z = points[i + 1].Z - points[i].Z;

                // Check direction change
                if (Math.Abs(v1x - v2x) > 1e-3 || Math.Abs(v1y - v2y) > 1e-3 || Math.Abs(v1z - v2z) > 1e-3)
                    turns++;
            }
            return turns;
        }

        private static double CalculateMinObstacleDistance(IReadOnlyList<Point3> points, IReadOnlyList<Box3> obstacles)
        {
            if (obstacles == null || obstacles.Count == 0) return 9999;
            double minDist = 9999;
            foreach (var pt in points)
            {
                foreach (var box in obstacles)
                {
                    double dx = Math.Max(0, Math.Max(box.MinX - pt.X, pt.X - box.MaxX));
                    double dy = Math.Max(0, Math.Max(box.MinY - pt.Y, pt.Y - box.MaxY));
                    double dz = Math.Max(0, Math.Max(box.MinZ - pt.Z, pt.Z - box.MaxZ));
                    double dist = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                    if (dist < minDist) minDist = dist;
                }
            }
            return minDist;
        }
    }
}
