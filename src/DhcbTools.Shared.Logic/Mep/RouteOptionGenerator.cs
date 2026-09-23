using System;
using System.Collections.Generic;
using System.Globalization;
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

        /// <summary>Điểm tổng hợp 0–1 theo thước đo TUYỆT ĐỐI (Manhattan, số rẽ tối thiểu, khoảng hở yêu cầu) — không phải so tương đối trong lô.</summary>
        public double Score { get; }

        public string Summary => string.Format(
            CultureInfo.InvariantCulture,
            "{0}: Dài {1:F2}m, {2} rẽ, Khoảng hở min {3:F0}mm (Điểm: {4:F2})",
            Title, LengthMm / 1000.0, TurnCount, MinObsDistanceMm, Score);
    }

    /// <summary>
    /// Bộ sinh và đánh giá đa phương án tuyến MEP: chạy A* ba lần với ba cấu hình phạt (ngắn nhất, ít rẽ,
    /// thoáng nhất), chấm điểm theo thước đo tuyệt đối và trả về theo điểm giảm dần.
    /// </summary>
    public static class RouteOptionGenerator
    {
        /// <summary>Trọng số điểm: dài / rẽ / khoảng hở.</summary>
        public const double LengthWeight = 0.4;
        public const double TurnWeight = 0.4;
        public const double ClearanceWeight = 0.2;

        /// <summary>Hệ số phạt rẽ cho phương án "ít rẽ nhất" (đặc tả §2.2: ×3).</summary>
        public const double LeastTurnsPenaltyFactor = 3.0;

        /// <summary>Hệ số phạt gần chướng ngại cho phương án "thoáng nhất".</summary>
        public const double MaxClearancePenaltyFactor = 4.0;

        /// <summary>
        /// Sinh ra danh sách các phương án tuyến MEP ứng viên với cấu hình phạt khác nhau và chấm điểm tổng hợp.
        /// Hai chiến lược cho ra cùng một đường gấp khúc thì gộp làm một phương án, tiêu đề ghi cả hai.
        /// </summary>
        /// <param name="searchMarginMm">Nới hộp tìm kiếm quanh đoạn đầu–cuối theo XY (mm).</param>
        /// <param name="searchMarginZMm">Nới hộp tìm kiếm theo Z (mm).</param>
        public static IReadOnlyList<RouteCandidateOption> GenerateCandidates(
            Point3 start,
            Point3 goal,
            IReadOnlyList<Box3>? obstacles,
            PathFinderOptions baseOptions,
            double searchMarginMm = 1500,
            double searchMarginZMm = 1500)
        {
            if (baseOptions == null) throw new ArgumentNullException(nameof(baseOptions));
            obstacles ??= Array.Empty<Box3>();

            var strategies = new[]
            {
                (Id: "OPT-1", Title: "Tuyến ngắn nhất", Strategy: RouteStrategy.Shortest, TurnPen: baseOptions.TurnPenalty, NearObsPen: baseOptions.NearObstaclePenalty),
                (Id: "OPT-2", Title: "Tuyến ít rẽ nhất", Strategy: RouteStrategy.LeastTurns, TurnPen: baseOptions.TurnPenalty * LeastTurnsPenaltyFactor, NearObsPen: baseOptions.NearObstaclePenalty),
                (Id: "OPT-3", Title: "Tuyến an toàn nhất", Strategy: RouteStrategy.MaxClearance, TurnPen: baseOptions.TurnPenalty, NearObsPen: baseOptions.NearObstaclePenalty * MaxClearancePenaltyFactor),
            };

            var bounds = AutoRoutePlanner.SearchBounds(start, goal, searchMarginMm, searchMarginZMm);
            var raw = new List<(string Id, string Title, RouteStrategy Strategy, List<Point3> Points, double Length, int Turns, int MinTurns, double Manhattan, double MinDist)>();

            foreach (var s in strategies)
            {
                var opt = new PathFinderOptions
                {
                    StepMm = baseOptions.StepMm,
                    ClearanceMm = baseOptions.ClearanceMm,
                    AllowVertical = baseOptions.AllowVertical,
                    MaxExpandedNodes = baseOptions.MaxExpandedNodes,
                    TurnPenalty = s.TurnPen,
                    NearObstaclePenalty = s.NearObsPen,
                };

                var path = PathFinder3D.FindPath(start, goal, obstacles, bounds, opt);
                if (path == null || !path.Found || path.Polyline.Count < 2)
                {
                    continue;
                }

                var len = path.LengthMm > 0 ? path.LengthMm : CalculateLength(path.Polyline);
                var turns = path.Turns > 0 || path.Polyline.Count <= 2 ? path.Turns : CalculateTurns(path.Polyline);
                raw.Add((s.Id, s.Title, s.Strategy, path.Polyline, len, turns, path.MinTurns, path.ManhattanMm, CalculateMinObstacleDistance(path.Polyline, obstacles)));
            }

            if (raw.Count == 0)
            {
                return Array.Empty<RouteCandidateOption>();
            }

            // Gộp theo HÌNH HỌC: cùng đường gấp khúc thì là cùng một phương án, dù chiến lược khác nhau.
            var unique = raw
                .GroupBy(r => PolylineKey(r.Points))
                .Select(g =>
                {
                    var first = g.First();
                    var title = g.Count() == 1 ? first.Title : string.Join(" = ", g.Select(x => x.Title));
                    return (first.Id, Title: title, first.Strategy, first.Points, first.Length, first.Turns, first.MinTurns, first.Manhattan, first.MinDist);
                })
                .ToList();

            var candidates = new List<RouteCandidateOption>();
            foreach (var r in unique)
            {
                // Thước đo tuyệt đối: 1,00 = không thể ngắn hơn / ít rẽ hơn / khoảng hở ≥ yêu cầu.
                var lenScore = r.Length <= 0 ? 0 : Math.Min(1.0, (r.Manhattan > 0 ? r.Manhattan : r.Length) / r.Length);
                var turnScore = r.Turns <= 0 ? 1.0 : Math.Min(1.0, Math.Max(1, r.MinTurns) / (double)r.Turns);
                var required = Math.Max(1.0, baseOptions.ClearanceMm);
                var distScore = Math.Min(1.0, r.MinDist / required);

                var total = (LengthWeight * lenScore) + (TurnWeight * turnScore) + (ClearanceWeight * distScore);
                candidates.Add(new RouteCandidateOption(r.Id, r.Title, r.Strategy, r.Points, r.Length, r.Turns, r.MinDist, total));
            }

            return candidates.OrderByDescending(c => c.Score).ThenBy(c => c.OptionId, StringComparer.Ordinal).ToList();
        }

        public static double CalculateLength(IReadOnlyList<Point3>? points)
        {
            if (points == null || points.Count < 2) return 0;
            double total = 0;
            for (var i = 0; i < points.Count - 1; i++)
            {
                var dx = points[i + 1].X - points[i].X;
                var dy = points[i + 1].Y - points[i].Y;
                var dz = points[i + 1].Z - points[i].Z;
                total += Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
            }

            return total;
        }

        /// <summary>
        /// Đếm số lần đổi HƯỚNG (so vector đơn vị), không phải số lần đổi độ dài đoạn — một đường thẳng
        /// chia thành nhiều đoạn dài ngắn khác nhau vẫn là 0 rẽ.
        /// </summary>
        public static int CalculateTurns(IReadOnlyList<Point3>? points)
        {
            if (points == null || points.Count < 3) return 0;
            var turns = 0;
            for (var i = 1; i < points.Count - 1; i++)
            {
                if (!TryUnit(points[i - 1], points[i], out var ux, out var uy, out var uz)
                    || !TryUnit(points[i], points[i + 1], out var vx, out var vy, out var vz))
                {
                    continue; // đoạn dài 0: không có hướng để so
                }

                if (Math.Abs(ux - vx) > 1e-6 || Math.Abs(uy - vy) > 1e-6 || Math.Abs(uz - vz) > 1e-6)
                {
                    turns++;
                }
            }

            return turns;
        }

        private static bool TryUnit(Point3 a, Point3 b, out double x, out double y, out double z)
        {
            x = b.X - a.X;
            y = b.Y - a.Y;
            z = b.Z - a.Z;
            var len = Math.Sqrt((x * x) + (y * y) + (z * z));
            if (len < 1e-9)
            {
                return false;
            }

            x /= len;
            y /= len;
            z /= len;
            return true;
        }

        private static string PolylineKey(IReadOnlyList<Point3> points) =>
            string.Join("|", points.Select(p => string.Format(CultureInfo.InvariantCulture, "{0:0.#},{1:0.#},{2:0.#}", p.X, p.Y, p.Z)));

        private static double CalculateMinObstacleDistance(IReadOnlyList<Point3> points, IReadOnlyList<Box3> obstacles)
        {
            if (obstacles.Count == 0) return double.MaxValue;
            var minDist = double.MaxValue;
            foreach (var pt in points)
            {
                foreach (var box in obstacles)
                {
                    var dx = Math.Max(0, Math.Max(box.MinX - pt.X, pt.X - box.MaxX));
                    var dy = Math.Max(0, Math.Max(box.MinY - pt.Y, pt.Y - box.MaxY));
                    var dz = Math.Max(0, Math.Max(box.MinZ - pt.Z, pt.Z - box.MaxZ));
                    var dist = Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
                    if (dist < minDist) minDist = dist;
                }
            }

            return minDist;
        }
    }
}
