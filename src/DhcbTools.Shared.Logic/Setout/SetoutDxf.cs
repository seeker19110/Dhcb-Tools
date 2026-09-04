using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace DhcbTools.Shared.Logic.Setout
{
    /// <summary>
    /// DXF ASCII tối thiểu (chỉ section ENTITIES — đủ cho AutoCAD và phần mềm máy toàn đạc đời cũ nhập
    /// điểm): mỗi điểm một <c>POINT</c> trên layer <c>DHCB-&lt;mã&gt;</c> và một <c>TEXT</c> tên điểm trên
    /// layer <c>DHCB-&lt;mã&gt;-TEN</c>. X = Đông, Y = Bắc, Z = cao độ, cùng đơn vị với CSV.
    /// </summary>
    public static class SetoutDxf
    {
        public static string Write(IReadOnlyList<SetoutPoint> points, bool metres, int decimals = 3, double textHeightMm = 250)
        {
            var sb = new StringBuilder();
            sb.Append("  0\r\nSECTION\r\n  2\r\nENTITIES\r\n");

            var height = Scale(textHeightMm, metres);
            var offset = height * 0.3;
            foreach (var point in points)
            {
                var layer = "DHCB-" + (point.Code.Length == 0 ? "PT" : point.Code);
                var x = Number(Scale(point.EastMm, metres), decimals);
                var y = Number(Scale(point.NorthMm, metres), decimals);
                var z = Number(Scale(point.ElevationMm, metres), decimals);

                sb.Append("  0\r\nPOINT\r\n  8\r\n").Append(layer)
                  .Append("\r\n 10\r\n").Append(x)
                  .Append("\r\n 20\r\n").Append(y)
                  .Append("\r\n 30\r\n").Append(z).Append("\r\n");

                sb.Append("  0\r\nTEXT\r\n  8\r\n").Append(layer).Append("-TEN")
                  .Append("\r\n 10\r\n").Append(Number(Scale(point.EastMm, metres) + offset, decimals))
                  .Append("\r\n 20\r\n").Append(Number(Scale(point.NorthMm, metres) + offset, decimals))
                  .Append("\r\n 30\r\n").Append(z)
                  .Append("\r\n 40\r\n").Append(Number(height, decimals))
                  .Append("\r\n  1\r\n").Append(point.Name).Append("\r\n");
            }

            sb.Append("  0\r\nENDSEC\r\n  0\r\nEOF\r\n");
            return sb.ToString();
        }

        private static double Scale(double mm, bool metres) => metres ? mm / 1000.0 : mm;

        private static string Number(double value, int decimals)
        {
            var rounded = Math.Round(value, Math.Max(0, decimals), MidpointRounding.AwayFromZero);
            if (rounded == 0)
            {
                rounded = 0;
            }

            return rounded.ToString("F" + Math.Max(0, decimals).ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
        }
    }
}
