using System;
using System.Collections.Generic;

namespace DhcbTools.Shared.Logic.Mep
{
    /// <summary>
    /// Rút gọn polyline từ <see cref="PathFinder3D"/> (điểm lưới 100 mm) thành các đoạn thẳng dài: bỏ điểm thẳng hàng
    /// và điểm trùng, để mỗi đoạn thành một model line → một đoạn duct/pipe trong <c>RouteFromLines</c>.
    /// </summary>
    public static class PolylineSimplifier
    {
        public static List<Point3> Simplify(IReadOnlyList<Point3> points, double tolerance = 1e-6)
        {
            var result = new List<Point3>();
            if (points == null || points.Count == 0)
            {
                return result;
            }

            result.Add(points[0]);
            for (var i = 1; i < points.Count; i++)
            {
                var p = points[i];
                var last = result[result.Count - 1];
                if (Distance(last, p) <= tolerance)
                {
                    continue; // trùng
                }

                if (result.Count >= 2)
                {
                    var prev = result[result.Count - 2];
                    if (Collinear(prev, last, p, tolerance))
                    {
                        result[result.Count - 1] = p; // kéo dài đoạn hiện tại
                        continue;
                    }
                }

                result.Add(p);
            }

            return result;
        }

        /// <summary>Các đoạn (đầu, cuối) từ polyline đã rút gọn.</summary>
        public static List<(Point3 Start, Point3 End)> ToSegments(IReadOnlyList<Point3> points)
        {
            var simplified = Simplify(points);
            var segs = new List<(Point3, Point3)>();
            for (var i = 1; i < simplified.Count; i++)
            {
                segs.Add((simplified[i - 1], simplified[i]));
            }
            return segs;
        }

        public static double Length(IReadOnlyList<Point3> points)
        {
            var total = 0.0;
            for (var i = 1; i < points.Count; i++)
            {
                total += Distance(points[i - 1], points[i]);
            }
            return total;
        }

        public static double Distance(Point3 a, Point3 b)
            => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y) + (a.Z - b.Z) * (a.Z - b.Z));

        private static bool Collinear(Point3 a, Point3 b, Point3 c, double tolerance)
        {
            // |AB × BC| ≈ 0 và cùng hướng (tích vô hướng > 0) để không gộp đoạn quay đầu.
            double ux = b.X - a.X, uy = b.Y - a.Y, uz = b.Z - a.Z;
            double vx = c.X - b.X, vy = c.Y - b.Y, vz = c.Z - b.Z;
            var cx = uy * vz - uz * vy;
            var cy = uz * vx - ux * vz;
            var cz = ux * vy - uy * vx;
            var cross = Math.Sqrt(cx * cx + cy * cy + cz * cz);
            var dot = ux * vx + uy * vy + uz * vz;
            var lenU = Math.Sqrt(ux * ux + uy * uy + uz * uz);
            var lenV = Math.Sqrt(vx * vx + vy * vy + vz * vz);
            if (lenU == 0 || lenV == 0) return true;
            return cross / (lenU * lenV) <= tolerance && dot > 0;
        }
    }
}
