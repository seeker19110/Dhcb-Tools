using System;
using System.Collections.Generic;
using System.Linq;

namespace DhcbTools.Shared.Logic.Geometry
{
    /// <summary>Một trục đã gom: hướng, vị trí (X cho trục dọc, Y cho trục ngang) và đoạn bao phủ.</summary>
    public sealed class GridLine
    {
        public GridLine(bool isVertical, double position, double start, double end, int segmentCount)
        {
            IsVertical = isVertical;
            Position = position;
            Start = start;
            End = end;
            SegmentCount = segmentCount;
        }

        /// <summary>Trục dọc (song song Y, vị trí đo theo X) — thường đặt tên chữ; ngang thì số.</summary>
        public bool IsVertical { get; }

        /// <summary>X (trục dọc) hoặc Y (trục ngang), trung bình các đoạn gom vào.</summary>
        public double Position { get; }

        /// <summary>Đầu/cuối theo phương còn lại (min/max của mọi đoạn).</summary>
        public double Start { get; }

        public double End { get; }

        public int SegmentCount { get; }

        public string Name { get; set; } = string.Empty;
    }

    /// <summary>
    /// Gom các đoạn thẳng (từ layer AXIS của bản CAD) thành trục (mục 2.3): chỉ nhận đoạn gần thẳng đứng/ngang,
    /// gom các đoạn cùng hướng có vị trí lệch dưới dung sai thành một trục, bỏ đoạn quá ngắn (nét bubble, gạch nối).
    /// </summary>
    public static class GridClustering
    {
        public static List<GridLine> Cluster(
            IEnumerable<Segment2D> segments,
            double positionTolerance = 50.0,
            double angleToleranceDeg = 2.0,
            double minLength = 500.0)
        {
            if (segments == null)
            {
                throw new ArgumentNullException(nameof(segments));
            }

            if (positionTolerance < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(positionTolerance));
            }

            var verticals = new List<Segment2D>();
            var horizontals = new List<Segment2D>();

            foreach (var s in segments)
            {
                if (s.Length < minLength)
                {
                    continue;
                }

                var a = s.AngleDeg;
                if (Math.Abs(a - 90.0) <= angleToleranceDeg)
                {
                    verticals.Add(s);
                }
                else if (a <= angleToleranceDeg || 180.0 - a <= angleToleranceDeg)
                {
                    horizontals.Add(s);
                }
            }

            var result = new List<GridLine>();
            result.AddRange(ClusterOneDirection(verticals, true, positionTolerance));
            result.AddRange(ClusterOneDirection(horizontals, false, positionTolerance));
            return result;
        }

        private static IEnumerable<GridLine> ClusterOneDirection(List<Segment2D> segs, bool vertical, double tol)
        {
            var ordered = segs
                .Select(s => new { Seg = s, Pos = vertical ? s.MidX : s.MidY })
                .OrderBy(t => t.Pos)
                .ToList();

            var i = 0;
            while (i < ordered.Count)
            {
                var group = new List<Segment2D> { ordered[i].Seg };
                var anchor = ordered[i].Pos;
                var j = i + 1;
                while (j < ordered.Count && ordered[j].Pos - anchor <= tol)
                {
                    group.Add(ordered[j].Seg);
                    j++;
                }

                var pos = group.Average(s => vertical ? s.MidX : s.MidY);
                var start = group.Min(s => vertical ? Math.Min(s.Y1, s.Y2) : Math.Min(s.X1, s.X2));
                var end = group.Max(s => vertical ? Math.Max(s.Y1, s.Y2) : Math.Max(s.X1, s.X2));
                yield return new GridLine(vertical, pos, start, end, group.Count);
                i = j;
            }
        }
    }
}
