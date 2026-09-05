using System;
using System.Collections.Generic;
using System.Linq;
using DhcbTools.Shared.Logic.Mep;

namespace DhcbTools.Shared.Logic.Cad
{
    /// <summary>Loại đường lấy từ DWG. Cung giữ nguyên hình, không băm thành đoạn thẳng.</summary>
    public enum CadCurveKind
    {
        Line,
        Arc,
    }

    /// <summary>
    /// Một đường đọc được từ DWG, đã đưa về toạ độ file chủ. Đơn vị **mm** — lớp thuần này không đoán
    /// đơn vị, người gọi quy đổi trước.
    /// </summary>
    public sealed class CadCurve
    {
        public CadCurve(string layer, Point3 start, Point3 end, CadCurveKind kind = CadCurveKind.Line, Point3? middle = null)
        {
            Layer = layer ?? string.Empty;
            Start = start;
            End = end;
            Kind = kind;
            Middle = middle;
        }

        public string Layer { get; }

        public Point3 Start { get; }

        public Point3 End { get; }

        public CadCurveKind Kind { get; }

        /// <summary>Điểm giữa cung — cần để phân biệt hai cung cùng hai đầu mút nhưng cong ngược nhau.</summary>
        public Point3? Middle { get; }

        public double Length => Start.DistanceTo(End);
    }

    /// <summary>Tuỳ chọn lọc. Mặc định là thứ chạy được trên một bản vẽ mặt bằng bình thường.</summary>
    public sealed class CadCurveFilterOptions
    {
        /// <summary>Layer được lấy; rỗng = mọi layer. Hỗ trợ wildcard <c>*</c>, <c>?</c>, <c>~</c> như <see cref="LayerMapEntry"/>.</summary>
        public List<string> IncludeLayers { get; set; } = new List<string>();

        /// <summary>Layer bị loại, xét **sau** danh sách lấy (ví dụ lấy <c>M-*</c> nhưng bỏ <c>M-*-TEXT</c>).</summary>
        public List<string> ExcludeLayers { get; set; } = new List<string>();

        /// <summary>Bỏ đoạn ngắn hơn ngưỡng này (mm). Bản vẽ CAD đầy đoạn 0.1 mm còn sót từ trim/extend.</summary>
        public double MinLengthMm { get; set; } = 50;

        /// <summary>Dung sai gộp đầu mút (mm): hai đầu cách nhau dưới ngưỡng coi như một điểm.</summary>
        public double WeldToleranceMm { get; set; } = 1.0;

        /// <summary>Nối các đoạn thẳng thẳng hàng nối tiếp thành một đoạn dài.</summary>
        public bool MergeCollinear { get; set; } = true;

        /// <summary>Lấy cả cung tròn (giữ nguyên hình) hay chỉ đoạn thẳng.</summary>
        public bool IncludeArcs { get; set; } = true;

        /// <summary>Ép mọi điểm về một cao độ (mm). null = giữ Z của bản vẽ.</summary>
        public double? FlattenToZMm { get; set; }
    }

    /// <summary>Đường đã lọc + **vì sao** những đường khác bị bỏ.</summary>
    public sealed class CadCurveFilterResult
    {
        public List<CadCurve> Curves { get; } = new List<CadCurve>();

        public int SkippedByLayer { get; set; }

        public int SkippedShort { get; set; }

        public int SkippedDuplicate { get; set; }

        public int SkippedArc { get; set; }

        /// <summary>Số đoạn bị nuốt vào đoạn khác khi nối thẳng hàng (không mất hình, chỉ bớt phần tử).</summary>
        public int MergedCollinear { get; set; }

        /// <summary>Layer đã lấy → số đường lấy được, để kỹ sư đối chiếu với bản vẽ.</summary>
        public Dictionary<string, int> ByLayer { get; } = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Một dòng tiếng Việt nói hết: lấy bao nhiêu, bỏ bao nhiêu vì lý do gì.</summary>
        public string Summary()
        {
            var parts = new List<string> { Curves.Count + " đường giữ lại" };
            if (SkippedByLayer > 0) parts.Add(SkippedByLayer + " sai layer");
            if (SkippedShort > 0) parts.Add(SkippedShort + " quá ngắn");
            if (SkippedDuplicate > 0) parts.Add(SkippedDuplicate + " trùng");
            if (SkippedArc > 0) parts.Add(SkippedArc + " cung (đang tắt)");
            if (MergedCollinear > 0) parts.Add(MergedCollinear + " đoạn gộp thẳng hàng");
            return string.Join(", ", parts);
        }
    }

    /// <summary>
    /// Chọn ra những đường trong DWG **đáng thành model line**: đúng layer, đủ dài, không trùng nhau, và
    /// nối lại những đoạn vốn là một tuyến thẳng bị CAD cắt vụn.
    /// <para>
    /// Ba thứ này quyết định lệnh dùng được hay không, và cả ba đều thuần: một mặt bằng thật có hàng nghìn
    /// đường, trong đó **đường vẽ chồng hai lần** (rất phổ biến khi copy giữa các bản vẽ) sẽ thành hai
    /// model line nằm đè nhau — <c>RouteFromLines</c> dựng ra hai ống chồng lên nhau mà không ai nhìn thấy
    /// trên mặt bằng.
    /// </para>
    /// </summary>
    public static class CadCurveFilter
    {
        public static CadCurveFilterResult Filter(IEnumerable<CadCurve> curves, CadCurveFilterOptions? options = null)
        {
            var opt = options ?? new CadCurveFilterOptions();
            var result = new CadCurveFilterResult();
            var tolerance = Math.Max(opt.WeldToleranceMm, 1e-9);
            var kept = new List<CadCurve>();
            var seen = new List<CadCurve>();

            foreach (var curve in curves ?? Enumerable.Empty<CadCurve>())
            {
                if (curve == null) continue;

                if (!LayerAllowed(curve.Layer, opt))
                {
                    result.SkippedByLayer++;
                    continue;
                }

                if (curve.Kind == CadCurveKind.Arc && !opt.IncludeArcs)
                {
                    result.SkippedArc++;
                    continue;
                }

                var flat = Flatten(curve, opt.FlattenToZMm);

                // Cung ngắn vẫn có thể là cút hợp lệ, nhưng đoạn thẳng ngắn hơn ngưỡng thì gần như luôn là
                // rác trim/extend. Đo bằng dây cung cho cả hai để chỉ có một quy tắc.
                if (flat.Length < opt.MinLengthMm)
                {
                    result.SkippedShort++;
                    continue;
                }

                if (seen.Any(s => SameCurve(s, flat, tolerance)))
                {
                    result.SkippedDuplicate++;
                    continue;
                }

                seen.Add(flat);
                kept.Add(flat);
            }

            var final = opt.MergeCollinear ? MergeChains(kept, tolerance, result) : kept;

            foreach (var curve in final)
            {
                result.Curves.Add(curve);
                result.ByLayer.TryGetValue(curve.Layer, out var count);
                result.ByLayer[curve.Layer] = count + 1;
            }

            return result;
        }

        /// <summary>Layer có được lấy không: phải khớp danh sách lấy (nếu có) và không khớp danh sách loại.</summary>
        public static bool LayerAllowed(string layer, CadCurveFilterOptions options)
        {
            var name = layer ?? string.Empty;
            if (options.IncludeLayers.Count > 0 && !options.IncludeLayers.Any(p => MatchesPattern(p, name)))
            {
                return false;
            }

            return !options.ExcludeLayers.Any(p => MatchesPattern(p, name));
        }

        private static bool MatchesPattern(string pattern, string layer)
            => new LayerMapEntry(pattern ?? string.Empty, string.Empty).Matches(layer);

        private static CadCurve Flatten(CadCurve curve, double? z)
        {
            if (z == null) return curve;
            return new CadCurve(
                curve.Layer,
                new Point3(curve.Start.X, curve.Start.Y, z.Value),
                new Point3(curve.End.X, curve.End.Y, z.Value),
                curve.Kind,
                curve.Middle == null ? (Point3?)null : new Point3(curve.Middle.Value.X, curve.Middle.Value.Y, z.Value));
        }

        /// <summary>
        /// Hai đường coi là một khi cùng layer, cùng loại và trùng hai đầu mút (kể cả **vẽ ngược chiều** —
        /// trên bản vẽ trông y hệt nhau). Cung phải trùng cả điểm giữa, vì hai cung ngược chiều cong
        /// chung hai đầu mút là hai hình khác nhau.
        /// </summary>
        public static bool SameCurve(CadCurve a, CadCurve b, double tolerance)
            => string.Equals(a.Layer, b.Layer, StringComparison.OrdinalIgnoreCase) && SameShape(a, b, tolerance);

        /// <summary>
        /// Như <see cref="SameCurve"/> nhưng **bỏ qua layer**: dùng khi so đường CAD với model line đã có
        /// trong mô hình — model line mang tên line style của Revit, không mang tên layer của DWG, nên so
        /// cả layer thì lần chạy nào cũng tưởng là chưa có và đẻ ra một bản sao chồng lên bản cũ.
        /// </summary>
        public static bool SameShape(CadCurve a, CadCurve b, double tolerance)
        {
            if (a.Kind != b.Kind) return false;

            var forward = Near(a.Start, b.Start, tolerance) && Near(a.End, b.End, tolerance);
            var backward = Near(a.Start, b.End, tolerance) && Near(a.End, b.Start, tolerance);
            if (!forward && !backward) return false;

            if (a.Kind == CadCurveKind.Arc)
            {
                if (a.Middle == null || b.Middle == null) return a.Middle == null && b.Middle == null;
                return Near(a.Middle.Value, b.Middle.Value, tolerance);
            }

            return true;
        }

        /// <summary>
        /// Nối các đoạn thẳng nối tiếp và thẳng hàng thành một đoạn. Chỉ nối tại đầu mút mà **đúng hai
        /// đoạn** gặp nhau: chỗ ba đoạn gặp nhau là ngã ba của tuyến, nối qua đó là xoá mất nhánh.
        /// </summary>
        private static List<CadCurve> MergeChains(List<CadCurve> curves, double tolerance, CadCurveFilterResult result)
        {
            var lines = curves.Where(c => c.Kind == CadCurveKind.Line).ToList();
            var others = curves.Where(c => c.Kind != CadCurveKind.Line).ToList();
            var merged = new List<CadCurve>();
            var used = new bool[lines.Count];

            for (var i = 0; i < lines.Count; i++)
            {
                if (used[i]) continue;
                used[i] = true;

                var layer = lines[i].Layer;
                var points = new List<Point3> { lines[i].Start, lines[i].End };
                var pieces = 1;

                // Kéo dài về hai phía tới khi không còn đoạn nào nối tiếp được.
                var grew = true;
                while (grew)
                {
                    grew = false;
                    for (var j = 0; j < lines.Count; j++)
                    {
                        if (used[j]) continue;
                        var other = lines[j];
                        if (!string.Equals(other.Layer, layer, StringComparison.OrdinalIgnoreCase)) continue;

                        var head = points[0];
                        var tail = points[points.Count - 1];

                        if (Near(tail, other.Start, tolerance) && DegreeAt(lines, tail, layer, tolerance) == 2)
                        {
                            points.Add(other.End);
                        }
                        else if (Near(tail, other.End, tolerance) && DegreeAt(lines, tail, layer, tolerance) == 2)
                        {
                            points.Add(other.Start);
                        }
                        else if (Near(head, other.End, tolerance) && DegreeAt(lines, head, layer, tolerance) == 2)
                        {
                            points.Insert(0, other.Start);
                        }
                        else if (Near(head, other.Start, tolerance) && DegreeAt(lines, head, layer, tolerance) == 2)
                        {
                            points.Insert(0, other.End);
                        }
                        else
                        {
                            continue;
                        }

                        used[j] = true;
                        pieces++;
                        grew = true;
                    }
                }

                var simplified = PolylineSimplifier.Simplify(points, tolerance);
                for (var k = 1; k < simplified.Count; k++)
                {
                    merged.Add(new CadCurve(layer, simplified[k - 1], simplified[k]));
                }

                result.MergedCollinear += pieces - (simplified.Count - 1);
            }

            merged.AddRange(others);
            return merged;
        }

        /// <summary>
        /// Số đoạn cùng layer chạm vào một điểm, **đếm cả đoạn đã nhận vào chuỗi**: đúng 2 là điểm nối
        /// bình thường, từ 3 trở lên là ngã ba của tuyến. Bỏ qua đoạn đã dùng thì ngã ba mà hai nhánh đã
        /// vào chuỗi trước sẽ trông như điểm nối thường — và nhánh thứ ba bị nuốt mất.
        /// </summary>
        private static int DegreeAt(List<CadCurve> lines, Point3 point, string layer, double tolerance)
        {
            var count = 0;
            for (var i = 0; i < lines.Count; i++)
            {
                if (!string.Equals(lines[i].Layer, layer, StringComparison.OrdinalIgnoreCase)) continue;
                if (Near(lines[i].Start, point, tolerance) || Near(lines[i].End, point, tolerance)) count++;
            }

            return count;
        }

        private static bool Near(Point3 a, Point3 b, double tolerance) => a.DistanceTo(b) <= tolerance;
    }
}
