using System;
using System.Collections.Generic;

namespace DhcbTools.Shared.Logic.Mep
{
    /// <summary>
    /// Số học ống dốc và kick-90 (P2 giai đoạn 7, học từ Naviate MEP / Victaulic): độ dốc tối thiểu theo đường kính,
    /// độ hạ theo chiều dài, hình học jog (kick) bằng hai cút cùng góc. Đơn vị mm và phần trăm, thuần để test.
    /// </summary>
    public static class SlopeMath
    {
        /// <summary>
        /// Độ dốc tối thiểu (%) cho ống thoát nước trọng lực theo đường kính danh nghĩa — bảng theo TCVN 4474 / IPC 704.1:
        /// DN ≤ 65: 2 % · DN 75–100: 1 % (khuyến nghị 2 % cho DN 75) · DN ≥ 125: 0,5 % (thực tế dùng 1 %).
        /// Trả về giá trị "an toàn" thường dùng trong hồ sơ Việt Nam.
        /// </summary>
        public static double MinSlopePercent(double diameterMm)
        {
            if (diameterMm <= 0) throw new ArgumentOutOfRangeException(nameof(diameterMm));
            if (diameterMm <= 65) return 2.0;
            if (diameterMm <= 80) return 2.0;
            if (diameterMm <= 100) return 1.0;
            if (diameterMm <= 150) return 1.0;
            return 0.5;
        }

        /// <summary>Độ hạ (mm) cho đoạn dài <paramref name="lengthMm"/> với dốc <paramref name="slopePercent"/> %.</summary>
        public static double DropMm(double lengthMm, double slopePercent)
        {
            if (lengthMm < 0) throw new ArgumentOutOfRangeException(nameof(lengthMm));
            return lengthMm * slopePercent / 100.0;
        }

        /// <summary>Độ dốc (%) thực tế của một đoạn từ chênh cao và chiều dài ngang.</summary>
        public static double SlopePercent(double horizontalMm, double dropMm)
        {
            if (horizontalMm <= 0) return 0;
            return dropMm / horizontalMm * 100.0;
        }

        /// <summary>
        /// Kiểm tra một đoạn có đạt dốc tối thiểu không; dung sai 0,05 % cho sai số hình học Revit.
        /// Trả về lý do khi không đạt, null khi đạt.
        /// </summary>
        public static string? CheckSlope(double horizontalMm, double dropMm, double requiredPercent, double tolerancePercent = 0.05)
        {
            var actual = SlopePercent(horizontalMm, dropMm);
            if (dropMm < 0)
            {
                return "dốc ngược " + NumericText.Format(Math.Abs(actual), 2) + " %";
            }

            if (actual + tolerancePercent < requiredPercent)
            {
                return "dốc " + NumericText.Format(actual, 2) + " % < " + NumericText.Format(requiredPercent, 2) + " % yêu cầu";
            }

            return null;
        }

        /// <summary>
        /// Hình học kick (jog) bằng hai cút góc <paramref name="elbowAngleDeg"/> (45° mặc định, 90° = kick-90 vuông):
        /// trả về chiều dài đoạn chéo và chiều dài chiếm dọc trục để dịch ống ngang một khoảng <paramref name="offsetMm"/>.
        /// Với 90°: đoạn chéo = offset, chiếm dọc trục = 0 (chỉ hai cút vuông).
        /// </summary>
        public static KickGeometry Kick(double offsetMm, double elbowAngleDeg = 45)
        {
            if (offsetMm <= 0) throw new ArgumentOutOfRangeException(nameof(offsetMm));
            if (elbowAngleDeg <= 0 || elbowAngleDeg > 90) throw new ArgumentOutOfRangeException(nameof(elbowAngleDeg));
            var rad = elbowAngleDeg * Math.PI / 180.0;
            var diagonal = offsetMm / Math.Sin(rad);
            var along = Math.Abs(elbowAngleDeg - 90) < 1e-9 ? 0 : offsetMm / Math.Tan(rad);
            return new KickGeometry(offsetMm, elbowAngleDeg, diagonal, along);
        }

        /// <summary>
        /// Chiều dài tối thiểu của đoạn ống để chứa một kick: hai cút (mỗi cút chiếm ~1,5×D dọc trục với cút 45°) + đoạn chéo
        /// chiếu lên trục + đoạn thẳng còn lại mỗi bên ≥ <paramref name="minStraightMm"/>.
        /// </summary>
        public static double MinPipeLengthForKick(double offsetMm, double diameterMm, double elbowAngleDeg = 45, double minStraightMm = 100)
        {
            var k = Kick(offsetMm, elbowAngleDeg);
            var fittingAlong = 2 * 1.5 * diameterMm;
            return k.AlongAxisMm + fittingAlong + 2 * minStraightMm;
        }

        /// <summary>
        /// Cao độ tim (mm) của các điểm dọc tuyến khi hạ dần từ đầu về cuối theo dốc — dùng để đặt Z cho từng đoạn nối tiếp.
        /// <paramref name="segmentLengthsMm"/> theo thứ tự dòng chảy.
        /// </summary>
        public static List<double> ElevationsAlong(double startElevationMm, IReadOnlyList<double> segmentLengthsMm, double slopePercent)
        {
            var result = new List<double>(segmentLengthsMm.Count + 1) { startElevationMm };
            var z = startElevationMm;
            foreach (var len in segmentLengthsMm)
            {
                z -= DropMm(len, slopePercent);
                result.Add(z);
            }
            return result;
        }
    }

    public sealed class KickGeometry
    {
        public KickGeometry(double offsetMm, double elbowAngleDeg, double diagonalMm, double alongAxisMm)
        {
            OffsetMm = offsetMm;
            ElbowAngleDeg = elbowAngleDeg;
            DiagonalMm = diagonalMm;
            AlongAxisMm = alongAxisMm;
        }

        public double OffsetMm { get; }

        public double ElbowAngleDeg { get; }

        /// <summary>Chiều dài đoạn chéo giữa hai cút.</summary>
        public double DiagonalMm { get; }

        /// <summary>Khoảng chiếm dọc trục ống ban đầu.</summary>
        public double AlongAxisMm { get; }
    }
}
