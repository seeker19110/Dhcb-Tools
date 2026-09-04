using System;
using System.Collections.Generic;

namespace DhcbTools.Shared.Logic.Geometry
{
    /// <summary>Một trục thẳng có tên (đơn vị tuỳ người gọi, thường là mm).</summary>
    public sealed class NamedSegment2D
    {
        public NamedSegment2D(string name, Segment2D segment)
        {
            Name = name ?? string.Empty;
            Segment = segment ?? throw new ArgumentNullException(nameof(segment));
        }

        public string Name { get; }

        public Segment2D Segment { get; }
    }

    /// <summary>Giao điểm hai trục — tên là cặp trục, trục chữ đứng trước trục số (<c>A-1</c>).</summary>
    public sealed class GridIntersection
    {
        public GridIntersection(string gridA, string gridB, double x, double y)
        {
            GridA = gridA;
            GridB = gridB;
            X = x;
            Y = y;
        }

        public string GridA { get; }

        public string GridB { get; }

        public double X { get; }

        public double Y { get; }

        public string Name => GridA + "-" + GridB;
    }

    /// <summary>
    /// Giao điểm của các trục thẳng — điểm trắc đạc cắm đầu tiên trên mọi công trình. Chỉ nhận giao
    /// điểm nằm <b>trên cả hai đoạn</b> (cộng dung sai) — hai trục song song hay chỉ cắt nhau ngoài
    /// phạm vi vẽ không sinh điểm, vì điểm đó không có trên bản vẽ nào để đối chiếu.
    /// </summary>
    public static class GridIntersections
    {
        public static List<GridIntersection> Find(IReadOnlyList<NamedSegment2D> grids, double toleranceMm = 1.0)
        {
            var result = new List<GridIntersection>();
            if (grids == null)
            {
                return result;
            }

            for (var i = 0; i < grids.Count; i++)
            {
                for (var j = i + 1; j < grids.Count; j++)
                {
                    if (!Intersect(grids[i].Segment, grids[j].Segment, toleranceMm, out var x, out var y))
                    {
                        continue;
                    }

                    var a = grids[i].Name;
                    var b = grids[j].Name;
                    if (IsNumber(a) && !IsNumber(b))
                    {
                        var t = a;
                        a = b;
                        b = t;
                    }

                    result.Add(new GridIntersection(a, b, x, y));
                }
            }

            return result;
        }

        /// <summary>Giao điểm hai đoạn (tham số hoá); false khi song song/trùng hoặc giao điểm nằm ngoài một trong hai đoạn quá dung sai.</summary>
        public static bool Intersect(Segment2D a, Segment2D b, double tolerance, out double x, out double y)
        {
            x = 0;
            y = 0;
            double rx = a.X2 - a.X1, ry = a.Y2 - a.Y1;
            double sx = b.X2 - b.X1, sy = b.Y2 - b.Y1;
            var lenR = Math.Sqrt(rx * rx + ry * ry);
            var lenS = Math.Sqrt(sx * sx + sy * sy);
            if (lenR < 1e-9 || lenS < 1e-9)
            {
                return false;
            }

            var cross = rx * sy - ry * sx;
            if (Math.Abs(cross) < 1e-9 * lenR * lenS)
            {
                return false; // song song hoặc trùng
            }

            double qx = b.X1 - a.X1, qy = b.Y1 - a.Y1;
            var t = (qx * sy - qy * sx) / cross;
            var u = (qx * ry - qy * rx) / cross;

            var tolT = Math.Max(0, tolerance) / lenR;
            var tolU = Math.Max(0, tolerance) / lenS;
            if (t < -tolT || t > 1 + tolT || u < -tolU || u > 1 + tolU)
            {
                return false;
            }

            x = a.X1 + t * rx;
            y = a.Y1 + t * ry;
            return true;
        }

        private static bool IsNumber(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return false;
            }

            foreach (var c in name)
            {
                if (!char.IsDigit(c))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
