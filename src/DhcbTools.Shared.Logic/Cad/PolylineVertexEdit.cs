using System;
using System.Collections.Generic;
using System.Linq;

namespace DhcbTools.Shared.Logic.Cad
{
    /// <summary>Một đỉnh LWPOLYLINE: toạ độ, bulge của đoạn TỪ đỉnh này tới đỉnh sau, bề rộng đầu/cuối đoạn đó.</summary>
    public readonly struct PlineVertex : IEquatable<PlineVertex>
    {
        public PlineVertex(double x, double y, double bulge = 0, double startWidth = 0, double endWidth = 0)
        {
            X = x;
            Y = y;
            Bulge = bulge;
            StartWidth = startWidth;
            EndWidth = endWidth;
        }

        public double X { get; }

        public double Y { get; }

        public double Bulge { get; }

        public double StartWidth { get; }

        public double EndWidth { get; }

        public bool Equals(PlineVertex other) =>
            X == other.X && Y == other.Y && Bulge == other.Bulge && StartWidth == other.StartWidth && EndWidth == other.EndWidth;

        public override bool Equals(object? obj) => obj is PlineVertex other && Equals(other);

        public override int GetHashCode() => (X, Y, Bulge, StartWidth, EndWidth).GetHashCode();

        public override string ToString() => $"({X}, {Y}) b={Bulge} w={StartWidth}/{EndWidth}";
    }

    /// <summary>Vị trí trên polyline: đoạn thứ <see cref="Segment"/> (0-based), tỉ lệ <see cref="Fraction"/> ∈ [0, 1] dọc đoạn.</summary>
    public readonly struct PlineLocation
    {
        public PlineLocation(int segment, double fraction, double x, double y, double distance)
        {
            Segment = segment;
            Fraction = fraction;
            X = x;
            Y = y;
            Distance = distance;
        }

        public int Segment { get; }

        public double Fraction { get; }

        /// <summary>Điểm chiếu trên polyline.</summary>
        public double X { get; }

        public double Y { get; }

        /// <summary>Khoảng cách từ điểm người gọi đưa tới điểm chiếu — lớn thì nên báo, có thể trỏ nhầm đối tượng.</summary>
        public double Distance { get; }

        public double Parameter => Segment + Fraction;
    }

    /// <summary>Kết quả cắt: đỉnh mới (đủ bulge/bề rộng) hoặc lỗi đọc được; kèm điểm chiếu của from/to để báo cáo.</summary>
    public sealed class PlineTrimResult
    {
        public List<PlineVertex> Vertices { get; } = new List<PlineVertex>();

        public string? Error { get; set; }

        public PlineLocation? Start { get; set; }

        public PlineLocation? End { get; set; }

        public bool Ok => Error == null;
    }

    /// <summary>
    /// Sửa đỉnh LWPOLYLINE ngay trên đối tượng (giữ handle, layer, xdata) thay vì xoá rồi vẽ lại — việc phiên bóc
    /// khối lượng 2026-11-13 phải làm bằng COM. Tính thuần hình học (không API Autodesk) để có test: cắt giữ đoạn
    /// giữa hai điểm chiếu <c>from</c>/<c>to</c>, đoạn cung chia đúng bulge, bề rộng nội suy tuyến tính.
    /// </summary>
    public static class PolylineVertexEdit
    {
        private const double Eps = 1e-9;

        /// <summary>Đỉnh mới từ danh sách điểm (đoạn thẳng, bề rộng đều <paramref name="width"/>).</summary>
        public static List<PlineVertex> FromPoints(IEnumerable<double[]> points, double width) =>
            points.Select(p => new PlineVertex(p[0], p[1], 0, width, width)).ToList();

        /// <summary>Bề rộng đều của polyline — <c>null</c> khi các đoạn rộng khác nhau.</summary>
        public static double? UniformWidth(IReadOnlyList<PlineVertex> vertices, bool closed)
        {
            var segments = SegmentCount(vertices.Count, closed);
            if (segments == 0)
            {
                return 0;
            }

            var w = vertices[0].StartWidth;
            for (var i = 0; i < segments; i++)
            {
                if (Math.Abs(vertices[i].StartWidth - w) > Eps || Math.Abs(vertices[i].EndWidth - w) > Eps)
                {
                    return null;
                }
            }

            return w;
        }

        /// <summary>
        /// Cắt polyline hở: giữ phần từ điểm chiếu của <paramref name="from"/> (bỏ trống = đầu) tới điểm chiếu của
        /// <paramref name="to"/> (bỏ trống = cuối), theo chiều vẽ. Polyline kín thì từ chối (không rõ giữ phía nào).
        /// </summary>
        public static PlineTrimResult Trim(IReadOnlyList<PlineVertex> vertices, bool closed, double[]? from, double[]? to)
        {
            var result = new PlineTrimResult();
            if (vertices == null || vertices.Count < 2)
            {
                result.Error = "polyline phải có ít nhất 2 đỉnh.";
                return result;
            }

            if (closed)
            {
                result.Error = "polyline khép kín — không rõ giữ phía nào; mở (closed=false qua replacePoints) rồi cắt.";
                return result;
            }

            if (from == null && to == null)
            {
                result.Error = "cần from và/hoặc to.";
                return result;
            }

            var last = vertices.Count - 2;
            var start = from == null ? new PlineLocation(0, 0, vertices[0].X, vertices[0].Y, 0) : Project(vertices, from);
            var end = to == null ? new PlineLocation(last, 1, vertices[last + 1].X, vertices[last + 1].Y, 0) : Project(vertices, to);
            result.Start = start;
            result.End = end;

            // Chuẩn hoá: from nằm ở cuối đoạn i = đầu đoạn i+1 (Project gặp hoà ở đỉnh thì chọn đoạn TRƯỚC với f = 1).
            // Chiều ngược lại không cần: cũng vì quy tắc hoà đó, to không bao giờ ra f = 0 trên đoạn i > 0.
            int i0 = start.Segment, i1 = end.Segment;
            double f0 = start.Fraction, f1 = end.Fraction;
            if (f0 >= 1 - Eps && i0 < last)
            {
                i0++;
                f0 = 0;
            }

            if (i0 > i1 || (i0 == i1 && f1 - f0 <= Eps))
            {
                result.Error = "from phải nằm trước to theo chiều vẽ polyline, và hai điểm không trùng nhau.";
                return result;
            }

            if (start.Parameter <= Eps && end.Parameter >= last + 1 - Eps)
            {
                result.Error = "from/to rơi đúng hai đầu polyline — không có gì để cắt.";
                return result;
            }

            for (var k = i0; k <= i1; k++)
            {
                var a = k == i0 ? f0 : 0;
                var b = k == i1 ? f1 : 1;
                var (x, y) = PointAt(vertices, k, a);
                var v = vertices[k];
                result.Vertices.Add(new PlineVertex(x, y, SubBulge(v.Bulge, b - a), WidthAt(v, a), WidthAt(v, b)));
            }

            var (ex, ey) = PointAt(vertices, i1, f1);
            result.Vertices.Add(new PlineVertex(ex, ey));
            return result;
        }

        /// <summary>Chiếu điểm lên polyline hở — đoạn gần nhất (hoà thì đoạn trước).</summary>
        public static PlineLocation Project(IReadOnlyList<PlineVertex> vertices, double[] point)
        {
            PlineLocation? best = null;
            for (var i = 0; i < vertices.Count - 1; i++)
            {
                var candidate = ProjectOnSegment(vertices, i, point[0], point[1]);
                if (best == null || candidate.Distance < best.Value.Distance - Eps)
                {
                    best = candidate;
                }
            }

            return best!.Value;
        }

        private static int SegmentCount(int vertexCount, bool closed) =>
            vertexCount < 2 ? 0 : closed ? vertexCount : vertexCount - 1;

        private static bool IsArc(PlineVertex p, PlineVertex q) => Math.Abs(p.Bulge) >= Eps && Hypot(q.X - p.X, q.Y - p.Y) >= Eps;

        private static double WidthAt(PlineVertex v, double f) => v.StartWidth + ((v.EndWidth - v.StartWidth) * f);

        /// <summary>Bulge của cung con chiếm tỉ lệ <paramref name="portion"/> góc ở tâm của cung có bulge <paramref name="bulge"/>.</summary>
        public static double SubBulge(double bulge, double portion) =>
            Math.Abs(bulge) < Eps ? 0 : Math.Tan(4 * Math.Atan(bulge) * portion / 4);

        /// <summary>Điểm tại tỉ lệ f của đoạn i (cung: tỉ lệ theo góc ở tâm, tức theo chiều dài cung).</summary>
        public static (double X, double Y) PointAt(IReadOnlyList<PlineVertex> vertices, int i, double f)
        {
            var p = vertices[i];
            var q = vertices[(i + 1) % vertices.Count];
            if (!IsArc(p, q))
            {
                return (p.X + ((q.X - p.X) * f), p.Y + ((q.Y - p.Y) * f));
            }

            var (cx, cy, r, a0, sweep) = Arc(p, q);
            var a = a0 + (sweep * f);
            return (cx + (r * Math.Cos(a)), cy + (r * Math.Sin(a)));
        }

        private static PlineLocation ProjectOnSegment(IReadOnlyList<PlineVertex> vertices, int i, double x, double y)
        {
            var p = vertices[i];
            var q = vertices[i + 1];
            double f;
            if (!IsArc(p, q))
            {
                double dx = q.X - p.X, dy = q.Y - p.Y;
                var len2 = (dx * dx) + (dy * dy);
                f = len2 < Eps ? 0 : Clamp((((x - p.X) * dx) + ((y - p.Y) * dy)) / len2);
            }
            else
            {
                var (cx, cy, _, a0, sweep) = Arc(p, q);
                var angle = Math.Atan2(y - cy, x - cx);
                var along = sweep > 0 ? Normalize(angle - a0) : Normalize(a0 - angle);
                var total = Math.Abs(sweep);
                if (along <= total)
                {
                    f = along / total;
                }
                else
                {
                    // Ngoài cung: về đầu mút gần hơn.
                    var (sx, sy) = (p.X, p.Y);
                    var (ex, ey) = (q.X, q.Y);
                    f = Hypot(x - sx, y - sy) <= Hypot(x - ex, y - ey) ? 0 : 1;
                }
            }

            var (px, py) = PointAt(vertices, i, f);
            return new PlineLocation(i, f, px, py, Hypot(x - px, y - py));
        }

        /// <summary>Tâm, bán kính, góc đầu và góc quét (có dấu, dương = ngược chiều kim đồng hồ) của đoạn cung p→q.</summary>
        private static (double Cx, double Cy, double R, double A0, double Sweep) Arc(PlineVertex p, PlineVertex q)
        {
            double dx = q.X - p.X, dy = q.Y - p.Y;
            var chord = Hypot(dx, dy);
            var b = p.Bulge;
            var sweep = 4 * Math.Atan(b);
            // Tâm nằm trên trung trực, lệch về bên trái chiều p→q một đoạn chord·(1−b²)/(4b).
            var offset = chord * (1 - (b * b)) / (4 * b);
            var nx = -dy / chord;
            var ny = dx / chord;
            var cx = ((p.X + q.X) / 2) + (nx * offset);
            var cy = ((p.Y + q.Y) / 2) + (ny * offset);
            var r = Hypot(p.X - cx, p.Y - cy);
            return (cx, cy, r, Math.Atan2(p.Y - cy, p.X - cx), sweep);
        }

        private static double Normalize(double angle)
        {
            var twoPi = 2 * Math.PI;
            angle %= twoPi;
            return angle < 0 ? angle + twoPi : angle;
        }

        private static double Clamp(double f) => f < 0 ? 0 : f > 1 ? 1 : f;

        private static double Hypot(double a, double b) => Math.Sqrt((a * a) + (b * b));
    }
}
