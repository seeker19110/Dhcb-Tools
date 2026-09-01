using System;
using System.Collections.Generic;
using System.Linq;

namespace DhcbTools.Shared.Logic.Mep
{
    /// <summary>Điểm 2D (mm).</summary>
    public struct Point2
    {
        public Point2(double x, double y)
        {
            X = x;
            Y = y;
        }

        public double X { get; }

        public double Y { get; }

        public double DistanceTo(Point2 o) => Math.Sqrt((X - o.X) * (X - o.X) + (Y - o.Y) * (Y - o.Y));

        public override string ToString() => "(" + NumericText.Format(X, 0) + ", " + NumericText.Format(Y, 0) + ")";
    }

    /// <summary>Tham số rải thiết bị đầu cuối theo lưới (mục 3.2).</summary>
    public sealed class GridPatternOptions
    {
        public double SpacingX { get; set; } = 3000;

        public double SpacingY { get; set; } = 3000;

        /// <summary>Khoảng cách tối thiểu từ thiết bị tới tường/biên phòng.</summary>
        public double Margin { get; set; } = 1500;

        /// <summary>Bán kính phủ của một thiết bị; ≤ 0 = không kiểm tra phủ.</summary>
        public double CoverageRadius { get; set; } = 2300;

        /// <summary>Bước lưới kiểm tra phủ (mm) — nhỏ hơn thì chính xác hơn, chậm hơn.</summary>
        public double CoverageCheckStep { get; set; } = 500;
    }

    /// <summary>Kết quả rải: điểm đặt + các điểm chèn thêm để phủ đủ + điểm chưa phủ được.</summary>
    public sealed class DevicePlacementPlan
    {
        public List<Point2> Points { get; } = new List<Point2>();

        public List<Point2> AddedForCoverage { get; } = new List<Point2>();

        public List<Point2> Uncovered { get; } = new List<Point2>();

        public List<string> Messages { get; } = new List<string>();
    }

    /// <summary>
    /// Sinh lưới điểm bên trong đa giác phòng, cách biên tối thiểu margin, loại điểm rơi vào lỗ (cột, hộp kỹ thuật),
    /// rồi kiểm tra phủ: điểm nào trong phòng cách mọi thiết bị hơn bán kính phủ thì chèn thêm thiết bị tại đó.
    /// Đây là phần dễ sai nhất của routing mức B và test được hoàn toàn không cần Revit.
    /// </summary>
    public static class DevicePattern
    {
        public static DevicePlacementPlan GridInPolygon(IReadOnlyList<Point2> boundary, GridPatternOptions options, IEnumerable<IReadOnlyList<Point2>>? holes = null)
        {
            if (boundary == null || boundary.Count < 3)
            {
                throw new ArgumentException("Biên phòng cần ít nhất 3 đỉnh.", nameof(boundary));
            }

            if (options.SpacingX <= 0 || options.SpacingY <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(options), "Khoảng cách lưới phải > 0.");
            }

            var holeList = holes?.ToList() ?? new List<IReadOnlyList<Point2>>();
            var plan = new DevicePlacementPlan();

            var minX = boundary.Min(p => p.X);
            var maxX = boundary.Max(p => p.X);
            var minY = boundary.Min(p => p.Y);
            var maxY = boundary.Max(p => p.Y);

            var width = maxX - minX;
            var height = maxY - minY;
            if (width < 2 * options.Margin || height < 2 * options.Margin)
            {
                // Phòng quá hẹp so với margin: đặt một thiết bị tại trọng tâm nếu nằm trong phòng.
                var c = Centroid(boundary);
                if (Contains(boundary, c) && !InAnyHole(holeList, c))
                {
                    plan.Points.Add(c);
                }
                plan.Messages.Add("Phòng hẹp hơn 2×margin — đặt một thiết bị tại tâm.");
                return plan;
            }

            // Căn lưới vào giữa để mép hai bên đối xứng (không lệch về một góc).
            var usableW = width - 2 * options.Margin;
            var usableH = height - 2 * options.Margin;
            var nx = Math.Max(1, (int)Math.Floor(usableW / options.SpacingX) + 1);
            var ny = Math.Max(1, (int)Math.Floor(usableH / options.SpacingY) + 1);
            var startX = minX + options.Margin + (usableW - (nx - 1) * options.SpacingX) / 2.0;
            var startY = minY + options.Margin + (usableH - (ny - 1) * options.SpacingY) / 2.0;

            for (var i = 0; i < nx; i++)
            {
                for (var j = 0; j < ny; j++)
                {
                    var p = new Point2(startX + i * options.SpacingX, startY + j * options.SpacingY);
                    if (!Contains(boundary, p) || InAnyHole(holeList, p))
                    {
                        continue;
                    }

                    if (DistanceToBoundary(boundary, p) < options.Margin - 1e-6)
                    {
                        continue;
                    }

                    plan.Points.Add(p);
                }
            }

            if (options.CoverageRadius > 0)
            {
                CheckCoverage(boundary, holeList, options, plan);
            }

            return plan;
        }

        private static void CheckCoverage(IReadOnlyList<Point2> boundary, List<IReadOnlyList<Point2>> holes, GridPatternOptions o, DevicePlacementPlan plan)
        {
            var minX = boundary.Min(p => p.X);
            var maxX = boundary.Max(p => p.X);
            var minY = boundary.Min(p => p.Y);
            var maxY = boundary.Max(p => p.Y);
            var step = Math.Max(50, o.CoverageCheckStep);

            var samples = new List<Point2>();
            for (var x = minX + step / 2; x < maxX; x += step)
            {
                for (var y = minY + step / 2; y < maxY; y += step)
                {
                    var p = new Point2(x, y);
                    if (Contains(boundary, p) && !InAnyHole(holes, p))
                    {
                        samples.Add(p);
                    }
                }
            }

            // Lặp: điểm chưa phủ xa nhất → chèn thiết bị mới tại đó (kéo vào trong margin nếu cần) cho tới khi phủ hết.
            var guard = 0;
            while (guard++ < 500)
            {
                var uncovered = samples.Where(s => !plan.Points.Any(d => d.DistanceTo(s) <= o.CoverageRadius)).ToList();
                if (uncovered.Count == 0)
                {
                    break;
                }

                // Chọn điểm chưa phủ xa thiết bị nhất; đặt thiết bị mới tại vị trí đó nhưng lùi khỏi biên theo margin.
                var worst = uncovered.OrderByDescending(s => plan.Points.Count == 0 ? 0 : plan.Points.Min(d => d.DistanceTo(s))).First();
                var candidate = PullInside(boundary, worst, o.Margin);
                if (!Contains(boundary, candidate) || InAnyHole(holes, candidate) || plan.Points.Any(d => d.DistanceTo(candidate) < 1))
                {
                    plan.Uncovered.AddRange(uncovered);
                    plan.Messages.Add("Không đặt thêm được thiết bị để phủ " + uncovered.Count + " điểm — kiểm tra tay.");
                    break;
                }

                plan.Points.Add(candidate);
                plan.AddedForCoverage.Add(candidate);
                plan.Messages.Add("Chèn thêm thiết bị tại " + candidate + " để phủ điểm " + worst + ".");
            }
        }

        private static Point2 PullInside(IReadOnlyList<Point2> boundary, Point2 p, double margin)
        {
            var d = DistanceToBoundary(boundary, p);
            if (d >= margin)
            {
                return p;
            }

            // Dời về phía trọng tâm một đoạn (margin - d), đủ để cách biên ≥ margin với phòng lồi.
            var c = Centroid(boundary);
            var dx = c.X - p.X;
            var dy = c.Y - p.Y;
            var len = Math.Sqrt(dx * dx + dy * dy);
            if (len < 1e-9)
            {
                return p;
            }

            var shift = margin - d;
            return new Point2(p.X + dx / len * shift, p.Y + dy / len * shift);
        }

        private static bool InAnyHole(List<IReadOnlyList<Point2>> holes, Point2 p) => holes.Any(h => h.Count >= 3 && Contains(h, p));

        /// <summary>Điểm nằm trong đa giác (ray casting; điểm trên biên coi là trong).</summary>
        public static bool Contains(IReadOnlyList<Point2> polygon, Point2 p)
        {
            var inside = false;
            var n = polygon.Count;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                var pi = polygon[i];
                var pj = polygon[j];
                if (DistancePointToSegment(p, pi, pj) < 1e-6)
                {
                    return true;
                }

                if ((pi.Y > p.Y) != (pj.Y > p.Y))
                {
                    var xInt = (pj.X - pi.X) * (p.Y - pi.Y) / (pj.Y - pi.Y) + pi.X;
                    if (p.X < xInt)
                    {
                        inside = !inside;
                    }
                }
            }
            return inside;
        }

        /// <summary>Khoảng cách ngắn nhất từ điểm tới biên đa giác.</summary>
        public static double DistanceToBoundary(IReadOnlyList<Point2> polygon, Point2 p)
        {
            var best = double.MaxValue;
            var n = polygon.Count;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                best = Math.Min(best, DistancePointToSegment(p, polygon[i], polygon[j]));
            }
            return best;
        }

        public static double DistancePointToSegment(Point2 p, Point2 a, Point2 b)
        {
            var dx = b.X - a.X;
            var dy = b.Y - a.Y;
            var len2 = dx * dx + dy * dy;
            if (len2 < 1e-12)
            {
                return p.DistanceTo(a);
            }

            var t = ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / len2;
            t = Math.Max(0, Math.Min(1, t));
            return p.DistanceTo(new Point2(a.X + t * dx, a.Y + t * dy));
        }

        public static Point2 Centroid(IReadOnlyList<Point2> polygon)
        {
            double a = 0, cx = 0, cy = 0;
            var n = polygon.Count;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                var cross = polygon[j].X * polygon[i].Y - polygon[i].X * polygon[j].Y;
                a += cross;
                cx += (polygon[j].X + polygon[i].X) * cross;
                cy += (polygon[j].Y + polygon[i].Y) * cross;
            }

            if (Math.Abs(a) < 1e-9)
            {
                return new Point2(polygon.Average(p => p.X), polygon.Average(p => p.Y));
            }

            a *= 0.5;
            return new Point2(cx / (6 * a), cy / (6 * a));
        }

        /// <summary>Diện tích đa giác (mm²), luôn dương.</summary>
        public static double Area(IReadOnlyList<Point2> polygon)
        {
            double a = 0;
            var n = polygon.Count;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                a += polygon[j].X * polygon[i].Y - polygon[i].X * polygon[j].Y;
            }
            return Math.Abs(a) / 2.0;
        }
    }
}
