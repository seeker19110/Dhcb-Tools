using System;

namespace DhcbTools.Shared.Logic.Mep
{
    /// <summary>
    /// Phần quyết định của <c>PipeKick</c> ngoài <see cref="SlopeMath.Kick"/>: ống đủ dài không, điểm bắt đầu có nằm trong
    /// ống không, và vector hướng dịch (Up/Down/Left/Right) từ hướng ống trên mặt bằng. Core chỉ còn tách ống và dựng cút.
    /// </summary>
    public static class KickPlanner
    {
        /// <summary>Ngưỡng coi ống là thẳng đứng thuần (hình chiếu ngang của vector đơn vị nhỏ hơn giá trị này).</summary>
        public const double VerticalTolerance = 1e-9;

        /// <summary>Lý do không kick được; null khi được. Giữ nguyên thông điệp đã chạy thật trong Revit.</summary>
        public static string? Validate(double lengthMm, double diameterMm, double offsetMm, double elbowAngleDeg, double distanceFromStartMm)
        {
            var geom = SlopeMath.Kick(offsetMm, elbowAngleDeg);
            var minLen = SlopeMath.MinPipeLengthForKick(offsetMm, diameterMm, elbowAngleDeg);
            if (lengthMm < minLen)
            {
                return $"Ống dài {lengthMm:F0} mm, cần ≥ {minLen:F0} mm để đặt kick {offsetMm} mm với cút {elbowAngleDeg}°.";
            }

            if (distanceFromStartMm + geom.AlongAxisMm + 3 * diameterMm > lengthMm)
            {
                return "distanceFromStartMm quá lớn: kick không nằm trong ống.";
            }

            return null;
        }

        /// <summary>
        /// Vector đơn vị hướng dịch. <paramref name="dirX"/>/<paramref name="dirY"/> là hướng ống trên mặt bằng.
        /// Left = Z × hướng ngang, Right = hướng ngang × Z; ống thẳng đứng thì Left/Right lấy ±Y. Hướng lạ → Up.
        /// </summary>
        public static (double X, double Y, double Z) OffsetDirection(string? direction, double dirX, double dirY)
        {
            var len = Math.Sqrt(dirX * dirX + dirY * dirY);
            switch ((direction ?? string.Empty).ToUpperInvariant())
            {
                case "DOWN":
                    return (0, 0, -1);
                case "LEFT":
                    return len > VerticalTolerance ? (-dirY / len, dirX / len, 0) : (0, 1, 0);
                case "RIGHT":
                    return len > VerticalTolerance ? (dirY / len, -dirX / len, 0) : (0, -1, 0);
                default:
                    return (0, 0, 1);
            }
        }

        /// <summary>Với kick-90 (không có đoạn chéo) vẫn cần một đoạn giữa ngắn để đặt hai cút: max(D, 50 mm).</summary>
        public static double MiddleLengthMm(double diameterMm, double alongAxisMm)
            => alongAxisMm > 1e-6 ? alongAxisMm : Math.Max(diameterMm, 50);

        public static string Note(string direction, double offsetMm, double elbowAngleDeg, double diagonalMm, double distanceFromStartMm)
            => $"Kick {direction} {offsetMm} mm, cút {elbowAngleDeg}°: đoạn chéo {diagonalMm:F0} mm, bắt đầu cách đầu ống {distanceFromStartMm} mm.";

        public static string PreviewSummary(string elementId) => $"[Xem trước] Sẽ chia ống {elementId} thành 3 đoạn + 2 cút.";

        public static string WriteSummary(long id0, long id1, long id2, int elbowsMade)
            => $"Đã kick ống: 3 đoạn ({id0}, {id1}, {id2}), {elbowsMade}/2 cút dựng được.";
    }
}
