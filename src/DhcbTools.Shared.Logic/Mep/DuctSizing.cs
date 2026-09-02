using System;
using System.Collections.Generic;
using System.Linq;

namespace DhcbTools.Shared.Logic.Mep
{
    /// <summary>Kết quả đề xuất kích thước cho một đoạn (mục 3.3): chỉ là đề xuất, kỹ sư duyệt.</summary>
    public sealed class SizingSuggestion
    {
        public SizingSuggestion(double suggestedMm, double velocityMs, string reason)
        {
            SuggestedMm = suggestedMm;
            VelocityMs = velocityMs;
            Reason = reason;
        }

        /// <summary>Đường kính (ống, duct tròn) hoặc chiều rộng (duct chữ nhật, khi cố định chiều cao), mm.</summary>
        public double SuggestedMm { get; }

        /// <summary>Vận tốc tại kích thước đề xuất (m/s).</summary>
        public double VelocityMs { get; }

        public string Reason { get; }
    }

    /// <summary>
    /// Sizing duct theo phương pháp ma sát đều (equal friction) — Darcy–Weisbach với hệ số ma sát tính theo
    /// Colebrook (xấp xỉ Swamee–Jain) cho không khí ở 20 °C. Bảng kích thước chuẩn theo SMACNA/TCVN 5687
    /// (tròn: 100…1600 mm; chữ nhật: cạnh 100…2000 bước 50/100).
    /// Nguồn: ASHRAE Fundamentals ch. 21 (Duct Design), SMACNA HVAC Duct Construction Standards.
    /// </summary>
    public static class DuctSizing
    {
        public const double AirDensity = 1.204;          // kg/m³ ở 20 °C
        public const double AirViscosity = 1.516e-5;     // m²/s
        public const double GalvanizedRoughness = 0.09e-3; // m (ASHRAE: 0.09 mm thép tráng kẽm)

        public static readonly double[] StandardRoundMm =
        {
            100, 125, 150, 160, 200, 250, 300, 315, 350, 400, 450, 500, 560, 600, 630, 700, 800, 900, 1000, 1120, 1250, 1400, 1600,
        };

        public static readonly double[] StandardRectangularSideMm =
        {
            100, 150, 200, 250, 300, 350, 400, 450, 500, 550, 600, 700, 800, 900, 1000, 1100, 1200, 1400, 1600, 1800, 2000,
        };

        /// <summary>Tổn thất ma sát (Pa/m) của duct tròn đường kính d (m) với lưu lượng q (m³/s).</summary>
        public static double FrictionPaPerM(double flowM3s, double diameterM, double roughnessM = GalvanizedRoughness)
        {
            if (diameterM <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(diameterM));
            }

            var area = Math.PI * diameterM * diameterM / 4.0;
            var v = flowM3s / area;
            if (v <= 0)
            {
                return 0;
            }

            var re = v * diameterM / AirViscosity;
            double f;
            if (re < 2300)
            {
                f = 64.0 / re;
            }
            else
            {
                // Swamee–Jain — sai số < 2 % so với Colebrook trong dải 5e3 < Re < 1e8.
                var term = roughnessM / (3.7 * diameterM) + 5.74 / Math.Pow(re, 0.9);
                f = 0.25 / Math.Pow(Math.Log10(term), 2);
            }

            return f * (AirDensity * v * v / 2.0) / diameterM;
        }

        /// <summary>Đường kính tương đương (mm) của duct chữ nhật a×b (Huebscher): De = 1.30·(ab)^0.625/(a+b)^0.25.</summary>
        public static double EquivalentDiameterMm(double widthMm, double heightMm)
        {
            if (widthMm <= 0 || heightMm <= 0)
            {
                throw new ArgumentOutOfRangeException("Kích thước duct phải > 0.");
            }

            return 1.30 * Math.Pow(widthMm * heightMm, 0.625) / Math.Pow(widthMm + heightMm, 0.25);
        }

        /// <summary>
        /// Đề xuất đường kính duct tròn nhỏ nhất trong bảng chuẩn sao cho ma sát ≤ <paramref name="maxPaPerM"/>
        /// (mặc định 1 Pa/m — giá trị thông dụng cho ống gió chính) và vận tốc ≤ <paramref name="maxVelocityMs"/>.
        /// </summary>
        public static SizingSuggestion SuggestRound(double flowLps, double maxPaPerM = 1.0, double maxVelocityMs = 8.0)
        {
            if (flowLps <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(flowLps), "Lưu lượng phải > 0.");
            }

            var q = flowLps / 1000.0;
            foreach (var dMm in StandardRoundMm)
            {
                var d = dMm / 1000.0;
                var v = q / (Math.PI * d * d / 4.0);
                var pa = FrictionPaPerM(q, d);
                if (pa <= maxPaPerM && v <= maxVelocityMs)
                {
                    return new SizingSuggestion(dMm, Math.Round(v, 2), "Ma sát " + NumericText.Format(pa, 2) + " Pa/m ≤ " + NumericText.Format(maxPaPerM, 2) + ", v = " + NumericText.Format(v, 2) + " m/s");
                }
            }

            var last = StandardRoundMm.Last();
            var vLast = q / (Math.PI * (last / 1000.0) * (last / 1000.0) / 4.0);
            return new SizingSuggestion(last, Math.Round(vLast, 2), "Vượt bảng chuẩn — cần tách nhánh hoặc dùng chữ nhật");
        }

        /// <summary>
        /// Đề xuất chiều rộng duct chữ nhật khi cố định chiều cao (thường bị trần khống chế): chọn cạnh chuẩn
        /// nhỏ nhất có đường kính tương đương ≥ đường kính tròn đề xuất; giới hạn tỉ số cạnh ≤ 4.
        /// </summary>
        public static SizingSuggestion SuggestRectangularWidth(double flowLps, double fixedHeightMm, double maxPaPerM = 1.0, double maxVelocityMs = 8.0)
        {
            if (fixedHeightMm <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(fixedHeightMm));
            }

            var round = SuggestRound(flowLps, maxPaPerM, maxVelocityMs);
            var q = flowLps / 1000.0;
            foreach (var w in StandardRectangularSideMm)
            {
                if (w / fixedHeightMm > 4.0)
                {
                    break;
                }

                if (EquivalentDiameterMm(w, fixedHeightMm) >= round.SuggestedMm)
                {
                    var v = q / (w / 1000.0 * fixedHeightMm / 1000.0);
                    return new SizingSuggestion(w, Math.Round(v, 2), "De = " + NumericText.Format(EquivalentDiameterMm(w, fixedHeightMm), 0) + " mm ≥ tròn " + NumericText.Format(round.SuggestedMm, 0) + " mm; " + round.Reason);
                }
            }

            return new SizingSuggestion(0, 0, "Không có cạnh chuẩn nào đủ với chiều cao " + NumericText.Format(fixedHeightMm, 0) + " mm (tỉ số cạnh > 4) — tăng chiều cao hoặc tách nhánh");
        }
    }

    /// <summary>
    /// Sizing ống nước theo vận tốc tối đa (mục 3.3): chọn DN nhỏ nhất trong bảng có v ≤ vmax.
    /// Đường kính trong lấy theo ống thép đen SCH40 (ASME B36.10) — đủ thiên an toàn cho PPR/HDPE cùng DN.
    /// Vận tốc giới hạn thông dụng: cấp nước 1.5–2.5 m/s, chữa cháy ≤ 3–5 m/s (TCVN 4513, NFPA 13).
    /// </summary>
    public static class PipeSizing
    {
        /// <summary>DN (mm) → đường kính trong (mm) SCH40.</summary>
        public static readonly IReadOnlyList<KeyValuePair<double, double>> Sch40InnerDiameterMm = new List<KeyValuePair<double, double>>
        {
            new KeyValuePair<double, double>(15, 15.8),
            new KeyValuePair<double, double>(20, 20.9),
            new KeyValuePair<double, double>(25, 26.6),
            new KeyValuePair<double, double>(32, 35.1),
            new KeyValuePair<double, double>(40, 40.9),
            new KeyValuePair<double, double>(50, 52.5),
            new KeyValuePair<double, double>(65, 62.7),
            new KeyValuePair<double, double>(80, 77.9),
            new KeyValuePair<double, double>(100, 102.3),
            new KeyValuePair<double, double>(125, 128.2),
            new KeyValuePair<double, double>(150, 154.1),
            new KeyValuePair<double, double>(200, 202.7),
            new KeyValuePair<double, double>(250, 254.5),
            new KeyValuePair<double, double>(300, 303.2),
        };

        public static double VelocityMs(double flowLps, double innerDiameterMm)
        {
            if (innerDiameterMm <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(innerDiameterMm));
            }

            var d = innerDiameterMm / 1000.0;
            return flowLps / 1000.0 / (Math.PI * d * d / 4.0);
        }

        public static SizingSuggestion SuggestDn(double flowLps, double maxVelocityMs = 2.0, double minVelocityMs = 0.0)
        {
            if (flowLps <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(flowLps), "Lưu lượng phải > 0.");
            }

            if (maxVelocityMs <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxVelocityMs));
            }

            foreach (var kv in Sch40InnerDiameterMm)
            {
                var v = VelocityMs(flowLps, kv.Value);
                if (v <= maxVelocityMs)
                {
                    var reason = "v = " + NumericText.Format(v, 2) + " m/s ≤ " + NumericText.Format(maxVelocityMs, 2);
                    if (minVelocityMs > 0 && v < minVelocityMs)
                    {
                        reason += " (dưới v_min " + NumericText.Format(minVelocityMs, 2) + " — nguy cơ lắng cặn)";
                    }
                    return new SizingSuggestion(kv.Key, Math.Round(v, 2), reason);
                }
            }

            var last = Sch40InnerDiameterMm.Last();
            return new SizingSuggestion(last.Key, Math.Round(VelocityMs(flowLps, last.Value), 2), "Vượt bảng DN — cần tách tuyến");
        }
    }
}
