using System.Collections.Generic;
using System.Linq;

namespace DhcbTools.Shared.Logic.Mep
{
    /// <summary>
    /// Phần quyết định thuần của lệnh <c>RouteFromLines</c> (tách 2026-09-06, §56): đoạn tuyến có ép cao độ,
    /// đếm fitting theo bậc đỉnh, quy tắc xoá line và thành công, và mọi câu Summary. Dựng Duct/Pipe/fitting qua
    /// API Revit ở lại <c>Core</c>. Chuỗi giữ nguyên từng ký tự — bộ <c>write-mep</c> (§55) chốt bằng
    /// <c>summaryContains</c>.
    /// </summary>
    public static class RouteBuildPlanner
    {
        public sealed class FittingCounts
        {
            public int Elbows { get; set; }

            public int Tees { get; set; }

            public int Crosses { get; set; }

            public int Total => Elbows + Tees + Crosses;
        }

        /// <summary>
        /// Đoạn tuyến từ hai đầu line; <paramref name="zOverride"/> có giá trị thì ép cả hai đầu về cao độ đó
        /// (line vẽ trên mặt bằng, kỹ sư khai <c>offsetMm</c> so với tầng).
        /// </summary>
        public static RouteSegment<TKey> Segment<TKey>(TKey key, Point3 p0, Point3 p1, double? zOverride)
        {
            if (zOverride.HasValue)
            {
                p0 = new Point3(p0.X, p0.Y, zOverride.Value);
                p1 = new Point3(p1.X, p1.Y, zOverride.Value);
            }

            return new RouteSegment<TKey>(key, p0, p1);
        }

        public static string NoLinesMessage(string lineStyleName) => $"Không có model/detail line nào dùng line style \"{lineStyleName}\".";

        public static string NoTypeMessage(string? typeName, string elementType)
            => $"Không tìm thấy type \"{typeName}\" cho {elementType} (hợp lệ: Duct, Pipe, CableTray, Conduit).";

        /// <summary>Đỉnh nào cần fitting gì — đỉnh bậc 1 (đầu hở) và đỉnh thẳng hàng không cần.</summary>
        public static List<(RouteNode Node, FittingKind Kind)> FittingPlan<TKey>(RouteGraph<TKey> graph)
        {
            return graph.Nodes.Select(n => (Node: n, Kind: graph.FittingAt(n.Id))).Where(t => t.Kind != FittingKind.None).ToList();
        }

        public static FittingCounts Count(IEnumerable<(RouteNode Node, FittingKind Kind)> plan)
        {
            var c = new FittingCounts();
            foreach (var (_, kind) in plan)
            {
                switch (kind)
                {
                    case FittingKind.Elbow: c.Elbows++; break;
                    case FittingKind.Tee: c.Tees++; break;
                    case FittingKind.Cross: c.Crosses++; break;
                }
            }

            return c;
        }

        public static string PreviewSummary(int edges, string elementType, string typeName, FittingCounts f, int rejected)
        {
            return $"[Xem trước] Sẽ dựng {edges} đoạn {elementType} ({typeName}), " +
                   $"{f.Elbows} elbow, {f.Tees} tee, {f.Crosses} cross; {rejected} đoạn bị bỏ.";
        }

        public static string PreviewEdgeLine<TKey>(RouteEdge<TKey> e, RouteGraph<TKey> graph)
        {
            return $"Đoạn {e.Key}: {graph.Nodes[e.StartNode].Position} → {graph.Nodes[e.EndNode].Position} (ft)";
        }

        /// <summary>
        /// Chỉ xoá line khi mọi đoạn và fitting đều dựng được: có lỗi thì line ở lại làm dấu cho kỹ sư nối tay,
        /// và chạy lại lệnh sau khi sửa vẫn còn đầu vào.
        /// </summary>
        public static bool ShouldDeleteLines(bool deleteLines, int errorCount) => deleteLines && errorCount == 0;

        public static string FinalSummary(int created, int edges, string elementType, int fittingsOk, int fittingsFailed, int autoConnected)
        {
            return $"Đã dựng {created}/{edges} đoạn {elementType}, {fittingsOk} fitting OK, {fittingsFailed} fitting lỗi, {autoConnected} mối nối tự động.";
        }

        /// <summary>Thành công khi dựng được ít nhất một đoạn — fitting lỗi được đếm và báo riêng, không làm cả lệnh thất bại.</summary>
        public static bool IsSuccess(int created) => created > 0;

        public static string MissingCurvesMessage(Point3 position, FittingKind kind)
            => $"Đỉnh {position}: thiếu đoạn để dựng {kind} — để hở, nối tay.";

        public static string NoConnectorMessage(Point3 position) => $"Đỉnh {position}: không tìm được connector — để hở.";

        public static string NoFittingForDegreeMessage(Point3 position, int degree)
            => $"Đỉnh {position}: {degree} nhánh — không có fitting, để hở.";

        public static string FittingFailedMessage(Point3 position, FittingKind kind, string reason, IEnumerable<string> curveIds)
            => $"Đỉnh {position}: {kind} thất bại ({reason}) — connector để hở, kỹ sư nối tay. Đoạn: {string.Join(", ", curveIds)}";
    }
}
