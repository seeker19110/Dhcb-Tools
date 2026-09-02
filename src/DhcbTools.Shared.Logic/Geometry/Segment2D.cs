using System;

namespace DhcbTools.Shared.Logic.Geometry
{
    /// <summary>Đoạn thẳng 2D (đơn vị tuỳ người gọi, thường là mm). Bất biến.</summary>
    public sealed class Segment2D
    {
        public Segment2D(double x1, double y1, double x2, double y2)
        {
            X1 = x1;
            Y1 = y1;
            X2 = x2;
            Y2 = y2;
        }

        public double X1 { get; }

        public double Y1 { get; }

        public double X2 { get; }

        public double Y2 { get; }

        public double Length => Math.Sqrt((X2 - X1) * (X2 - X1) + (Y2 - Y1) * (Y2 - Y1));

        /// <summary>Góc hướng trong [0, 180) độ — đoạn thẳng không có chiều.</summary>
        public double AngleDeg
        {
            get
            {
                var a = Math.Atan2(Y2 - Y1, X2 - X1) * 180.0 / Math.PI;
                a %= 180.0;
                if (a < 0)
                {
                    a += 180.0;
                }

                if (Math.Abs(a - 180.0) < 1e-9)
                {
                    a = 0;
                }

                return a;
            }
        }

        public double MidX => (X1 + X2) / 2.0;

        public double MidY => (Y1 + Y2) / 2.0;
    }
}
