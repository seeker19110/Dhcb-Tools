using System;
using System.Collections.Generic;

namespace DhcbTools.Shared.Logic.Mep
{
    /// <summary>Một đoạn ống thẳng đưa vào <see cref="SlopePlanner"/>: hai đầu mút (mm, hệ nội bộ) và đường kính.</summary>
    public sealed class SlopePipeInput
    {
        public SlopePipeInput(long id, double x0, double y0, double z0, double x1, double y1, double z1, double diameterMm)
        {
            Id = id;
            X0 = x0;
            Y0 = y0;
            Z0 = z0;
            X1 = x1;
            Y1 = y1;
            Z1 = z1;
            DiameterMm = diameterMm;
        }

        public long Id { get; }

        public double X0 { get; }

        public double Y0 { get; }

        public double Z0 { get; }

        public double X1 { get; }

        public double Y1 { get; }

        public double Z1 { get; }

        public double DiameterMm { get; }
    }

    /// <summary>Kết luận cho một ống: dốc yêu cầu, lý do chưa đạt (null = đạt) và cao độ hai đầu sau khi đặt dốc.</summary>
    public sealed class SlopePipePlan
    {
        public SlopePipePlan(long id, double diameterMm, double horizontalMm, double requiredPercent, string? issue, double newZ0Mm, double newZ1Mm)
        {
            Id = id;
            DiameterMm = diameterMm;
            HorizontalMm = horizontalMm;
            RequiredPercent = requiredPercent;
            Issue = issue;
            NewZ0Mm = newZ0Mm;
            NewZ1Mm = newZ1Mm;
        }

        public long Id { get; }

        public double DiameterMm { get; }

        public double HorizontalMm { get; }

        public double RequiredPercent { get; }

        /// <summary>Lý do chưa đạt dốc; null khi đã đạt.</summary>
        public string? Issue { get; }

        public bool NeedsFix => Issue != null;

        /// <summary>Cao độ đầu 0 (mm) sau khi đặt dốc — đầu không bị hạ giữ nguyên.</summary>
        public double NewZ0Mm { get; }

        /// <summary>Cao độ đầu 1 (mm) sau khi đặt dốc — đầu không bị hạ giữ nguyên.</summary>
        public double NewZ1Mm { get; }
    }

    /// <summary>
    /// Phần quyết định của <c>SlopePipes</c>: ống nào bỏ qua vì gần thẳng đứng, dốc yêu cầu theo đường kính hay theo
    /// config, đạt/chưa đạt, và đầu nào hạ xuống bao nhiêu. Core chỉ còn đọc đầu mút ống và gán <c>LocationCurve</c>.
    /// </summary>
    public static class SlopePlanner
    {
        /// <summary>Ống được coi là bỏ qua im lặng khi hình chiếu ngang ngắn hơn ngưỡng này (mm) — ống đứng thuần.</summary>
        public const double MinHorizontalMm = 0.001;

        /// <summary>"Start" (không phân biệt hoa thường) = hạ đầu 0; mọi giá trị khác = hạ đầu 1 (mặc định).</summary>
        public static bool LowerStart(string? lowerEnd)
            => string.Equals(lowerEnd, "Start", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Lập kế hoạch cho một ống. Trả về null khi ống bị bỏ qua (đứng thuần hoặc gần thẳng đứng), kèm
        /// <paramref name="skipMessage"/> để ghi vào Messages — chỉ ống đứng thuần mới im lặng vì nó không phải ống dốc.
        /// </summary>
        public static SlopePipePlan? Plan(SlopePipeInput pipe, double? slopePercent, bool lowerStart, double maxAngleFromHorizontalDeg, out string? skipMessage)
        {
            if (pipe == null)
            {
                throw new ArgumentNullException(nameof(pipe));
            }

            skipMessage = null;
            var dx = pipe.X1 - pipe.X0;
            var dy = pipe.Y1 - pipe.Y0;
            var horizontal = Math.Sqrt(dx * dx + dy * dy);
            if (horizontal < MinHorizontalMm)
            {
                return null;
            }

            var angle = Math.Atan2(Math.Abs(pipe.Z1 - pipe.Z0), horizontal);
            if (angle > maxAngleFromHorizontalDeg * Math.PI / 180.0)
            {
                skipMessage = pipe.Id + ": bỏ qua (gần thẳng đứng).";
                return null;
            }

            var required = slopePercent ?? SlopeMath.MinSlopePercent(pipe.DiameterMm);
            var drop = lowerStart ? pipe.Z1 - pipe.Z0 : pipe.Z0 - pipe.Z1;
            var issue = SlopeMath.CheckSlope(horizontal, drop, required);
            var newDrop = SlopeMath.DropMm(horizontal, required);
            var newZ0 = lowerStart ? pipe.Z1 - newDrop : pipe.Z0;
            var newZ1 = lowerStart ? pipe.Z1 : pipe.Z0 - newDrop;
            return new SlopePipePlan(pipe.Id, pipe.DiameterMm, horizontal, required, issue, newZ0, newZ1);
        }

        /// <summary>Lập kế hoạch cho cả danh sách; ống bỏ qua vì gần thẳng đứng được ghi vào <paramref name="messages"/>.</summary>
        public static List<SlopePipePlan> PlanAll(IEnumerable<SlopePipeInput> pipes, double? slopePercent, bool lowerStart, double maxAngleFromHorizontalDeg, List<string> messages)
        {
            if (pipes == null)
            {
                throw new ArgumentNullException(nameof(pipes));
            }

            if (messages == null)
            {
                throw new ArgumentNullException(nameof(messages));
            }

            var result = new List<SlopePipePlan>();
            foreach (var pipe in pipes)
            {
                var plan = Plan(pipe, slopePercent, lowerStart, maxAngleFromHorizontalDeg, out var skip);
                if (plan != null)
                {
                    result.Add(plan);
                }
                else if (skip != null)
                {
                    messages.Add(skip);
                }
            }

            return result;
        }

        public static string CheckSummary(int planned, int toFix) => $"Kiểm {planned} ống: {toFix} chưa đạt dốc.";

        public static string CheckLine(SlopePipePlan plan)
            => $"{plan.Id} DN{plan.DiameterMm:F0}: {plan.Issue}";

        public static string PreviewSummary(int toFix, int planned, bool lowerStart)
            => $"[Xem trước] Sẽ đặt dốc cho {toFix}/{planned} ống (hạ {(lowerStart ? "đầu" : "cuối")}).";

        public static string PreviewLine(SlopePipePlan plan)
            => $"{plan.Id}: {plan.Issue} → đặt {plan.RequiredPercent:0.##} %";

        public static string WriteSummary(int done, int toFix) => $"Đã đặt dốc {done}/{toFix} ống.";

        public static string WriteError(long id, string message)
            => $"{id}: {message} (ống đã nối fitting hai đầu có thể không dịch được — tách đoạn trước).";
    }
}
