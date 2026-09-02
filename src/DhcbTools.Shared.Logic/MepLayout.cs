using System;
using System.Collections.Generic;

namespace DhcbTools.Shared.Logic
{
    /// <summary>
    /// Số học đặt hanger và cắt ống — phần dễ sai nhất của nhóm MEPF nhưng hoàn toàn không cần Revit:
    /// đầu vào là chiều dài đoạn, đầu ra là các vị trí theo chiều dài cung.
    /// Đơn vị: feet (đơn vị nội bộ của Revit), người gọi tự đổi từ mm.
    /// </summary>
    public static class MepLayout
    {
        /// <summary>Hệ số đổi feet ↔ mm dùng chung cho cả solution.</summary>
        public const double FeetToMm = 304.8;

        /// <summary>Đổi mm sang feet.</summary>
        public static double MmToFeet(double millimetres) => millimetres / FeetToMm;

        /// <summary>Đổi feet sang mm.</summary>
        public static double FeetToMillimetres(double feet) => feet * FeetToMm;

        /// <summary>Hệ số đổi foot² ↔ m² dùng chung cho cả solution (0.3048²) — trước đây RevitQueryHandler
        /// dùng 0.0929 (làm tròn) còn DevicePlacementCommand dùng 0.09290304, cho hai kết quả m² khác nhau
        /// trên cùng một Room.Area.</summary>
        public const double SqFtToSqm = 0.09290304;

        /// <summary>Đổi foot² (Room.Area, Revit trả về feet vuông) sang m².</summary>
        public static double SquareFeetToSquareMetres(double squareFeet) => squareFeet * SqFtToSqm;

        /// <summary>
        /// Vị trí đặt hanger dọc một đoạn ống: đặt tại spacing/2, 3·spacing/2, … và LUÔN có ít nhất
        /// một hanger cho đoạn ngắn.
        ///
        /// Sửa lỗi so với bản cũ trong <c>HangerCommand</c>: bản cũ kiểm tra
        /// <c>if (plan.Count == 0 || lengthFt &lt; spacingFt)</c> trên danh sách plan DÙNG CHUNG cho mọi
        /// phần tử, nên (a) đoạn dài hơn spacing/2 nhưng ngắn hơn spacing bị đặt hai hanger chồng nhau,
        /// và (b) điều kiện <c>plan.Count == 0</c> chỉ đúng cho phần tử đầu tiên.
        /// </summary>
        /// <param name="lengthFt">Chiều dài đoạn (feet). ≤ 0 trả về danh sách rỗng.</param>
        /// <param name="spacingFt">Khoảng cách tối đa giữa hai hanger (feet). Phải &gt; 0.</param>
        public static List<double> HangerPositions(double lengthFt, double spacingFt)
        {
            if (spacingFt <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(spacingFt), "Khoảng cách hanger phải lớn hơn 0.");
            }

            var positions = new List<double>();
            if (lengthFt <= 0 || double.IsNaN(lengthFt))
            {
                return positions;
            }

            var pos = spacingFt / 2.0;
            while (pos < lengthFt)
            {
                positions.Add(pos);
                pos += spacingFt;
            }

            // Đoạn ngắn hơn nửa khoảng cách vẫn phải được đỡ: đặt đúng một hanger ở giữa.
            if (positions.Count == 0)
            {
                positions.Add(lengthFt / 2.0);
            }

            return positions;
        }

        /// <summary>
        /// Vị trí cắt một đoạn MEP dài thành các đoạn không quá <paramref name="maxSegmentFt"/>.
        /// Trả về danh sách rỗng khi đoạn đã đủ ngắn (trong dung sai).
        /// </summary>
        /// <param name="lengthFt">Chiều dài đoạn (feet).</param>
        /// <param name="maxSegmentFt">Chiều dài tối đa mỗi đoạn sau khi cắt (feet). Phải &gt; 0.</param>
        /// <param name="toleranceFt">
        /// Dung sai để không cắt ra một mẩu thừa siêu ngắn ở cuối (mặc định 10 mm).
        /// </param>
        public static List<double> SplitPositions(double lengthFt, double maxSegmentFt, double toleranceFt = 10.0 / FeetToMm)
        {
            if (maxSegmentFt <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxSegmentFt), "Chiều dài đoạn tối đa phải lớn hơn 0.");
            }

            var positions = new List<double>();
            if (lengthFt <= maxSegmentFt + toleranceFt || double.IsNaN(lengthFt))
            {
                return positions;
            }

            var pos = maxSegmentFt;
            while (pos < lengthFt - toleranceFt)
            {
                positions.Add(pos);
                pos += maxSegmentFt;
            }

            return positions;
        }

        /// <summary>
        /// Cao độ đáy/đỉnh/tim (mm) từ Z nhỏ nhất và lớn nhất của bounding box (feet).
        /// </summary>
        public static ElevationSet Elevations(double minZFt, double maxZFt)
        {
            if (maxZFt < minZFt)
            {
                var swap = minZFt;
                minZFt = maxZFt;
                maxZFt = swap;
            }

            return new ElevationSet(
                FeetToMillimetres(minZFt),
                FeetToMillimetres(maxZFt),
                FeetToMillimetres((minZFt + maxZFt) / 2.0));
        }

        /// <summary>
        /// Hai hộp bao có giao nhau không (bước lọc thô trước khi kiểm tra Solid).
        /// Chạm biên đúng bằng dung sai vẫn tính là giao.
        /// </summary>
        public static bool BoundingBoxesIntersect(
            double aMinX, double aMinY, double aMinZ, double aMaxX, double aMaxY, double aMaxZ,
            double bMinX, double bMinY, double bMinZ, double bMaxX, double bMaxY, double bMaxZ,
            double toleranceFt = 0.0)
        {
            return aMinX - toleranceFt <= bMaxX && bMinX - toleranceFt <= aMaxX
                && aMinY - toleranceFt <= bMaxY && bMinY - toleranceFt <= aMaxY
                && aMinZ - toleranceFt <= bMaxZ && bMinZ - toleranceFt <= aMaxZ;
        }
    }

    /// <summary>Bộ ba cao độ (mm) của một phần tử MEP.</summary>
    public sealed class ElevationSet
    {
        public ElevationSet(double bottomMm, double topMm, double centreMm)
        {
            BottomMm = bottomMm;
            TopMm = topMm;
            CentreMm = centreMm;
        }

        public double BottomMm { get; }

        public double TopMm { get; }

        public double CentreMm { get; }
    }
}
