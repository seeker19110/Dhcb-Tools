using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace DhcbTools.Shared.Logic.Cad
{
    /// <summary>Một polyline cần vẽ: layer, bề rộng, màu ACI (chỉ dùng khi phải tạo layer), đóng/mở, các đỉnh 2D.</summary>
    public sealed class PolylineSpec
    {
        public PolylineSpec(int row, string id, string layer, double width, short? colorAci, bool closed, IReadOnlyList<(double X, double Y)> points)
        {
            Row = row;
            Id = id;
            Layer = layer;
            Width = width;
            ColorAci = colorAci;
            Closed = closed;
            Points = points;
        }

        public int Row { get; }

        /// <summary>Mã do người lập file đặt (tuyến, đoạn…) — chỉ để đối chiếu trong báo cáo.</summary>
        public string Id { get; }

        public string Layer { get; }

        public double Width { get; }

        public short? ColorAci { get; }

        public bool Closed { get; }

        public IReadOnlyList<(double X, double Y)> Points { get; }

        /// <summary>Chiều dài hình học (kể cả cạnh đóng nếu Closed).</summary>
        public double Length
        {
            get
            {
                var total = 0.0;
                for (var i = 1; i < Points.Count; i++)
                {
                    total += Distance(Points[i - 1], Points[i]);
                }

                if (Closed && Points.Count > 2)
                {
                    total += Distance(Points[Points.Count - 1], Points[0]);
                }

                return total;
            }
        }

        private static double Distance((double X, double Y) a, (double X, double Y) b) => Math.Sqrt(((a.X - b.X) * (a.X - b.X)) + ((a.Y - b.Y) * (a.Y - b.Y)));
    }

    public sealed class PolylineImportPlan
    {
        public List<PolylineSpec> Rows { get; } = new List<PolylineSpec>();

        public List<string> Notes { get; } = new List<string>();

        /// <summary>Màu cho layer phải tạo (dòng đầu tiên có màu); cùng layer khác màu thì báo và không tự chọn.</summary>
        public Dictionary<string, short> TargetColors { get; } = new Dictionary<string, short>(StringComparer.OrdinalIgnoreCase);

        public List<string> ColorConflicts { get; } = new List<string>();
    }

    /// <summary>
    /// Đọc CSV <c>Id,Layer,Width,Color,Closed,Vertices</c> của lệnh <c>PolylineImport</c>. Ô <c>Vertices</c> là danh sách
    /// đỉnh <c>x y;x y;…</c> (đơn vị bản vẽ, dấu chấm thập phân). Dùng để vẽ lại kết quả phân tích ngoài AutoCAD —
    /// ví dụ tim ống bóc khối lượng — mà không phải tự tay vẽ.
    /// </summary>
    public static class PolylineImportPlanner
    {
        public static PolylineImportPlan Plan(IEnumerable<string[]> records)
        {
            if (records == null)
            {
                throw new ArgumentNullException(nameof(records));
            }

            var plan = new PolylineImportPlan();
            var row = 0;
            foreach (var cells in records)
            {
                row++;
                if (row == 1)
                {
                    continue;
                }

                var id = Cell(cells, 0) ?? string.Empty;
                var layer = Cell(cells, 1);
                var vertices = Cell(cells, 5);
                if (layer == null && vertices == null)
                {
                    continue;
                }

                if (layer == null)
                {
                    plan.Notes.Add($"Bỏ qua dòng {N(row)}: thiếu tên layer.");
                    continue;
                }

                var width = 0.0;
                var widthText = Cell(cells, 2);
                if (widthText != null && (!double.TryParse(widthText, NumberStyles.Float, CultureInfo.InvariantCulture, out width)
                                          || width < 0 || double.IsInfinity(width) || double.IsNaN(width)))
                {
                    plan.Notes.Add($"Bỏ qua dòng {N(row)}: bề rộng \"{widthText}\" không hợp lệ.");
                    continue;
                }

                short? color = null;
                var colorText = Cell(cells, 3);
                if (colorText != null)
                {
                    if (short.TryParse(colorText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var aci) && aci >= 1 && aci <= 255)
                    {
                        color = aci;
                    }
                    else
                    {
                        plan.Notes.Add($"Dòng {N(row)}: màu \"{colorText}\" không phải ACI 1–255 — bỏ qua màu.");
                    }
                }

                var closedText = Cell(cells, 4);
                var closed = closedText != null && (closedText == "1" || string.Equals(closedText, "true", StringComparison.OrdinalIgnoreCase));

                if (!TryParsePoints(vertices, out var points))
                {
                    plan.Notes.Add($"Bỏ qua dòng {N(row)}: danh sách đỉnh không đọc được (cần \"x y;x y;…\").");
                    continue;
                }

                if (points.Count < 2)
                {
                    plan.Notes.Add($"Bỏ qua dòng {N(row)}: cần ít nhất 2 đỉnh.");
                    continue;
                }

                plan.Rows.Add(new PolylineSpec(row, id, layer, width, color, closed, points));
            }

            foreach (var target in plan.Rows.Where(r => r.ColorAci.HasValue).GroupBy(r => r.Layer, StringComparer.OrdinalIgnoreCase))
            {
                var colors = target.Select(r => r.ColorAci!.Value).Distinct().ToList();
                if (colors.Count > 1)
                {
                    plan.ColorConflicts.Add($"Layer \"{target.Key}\" được ghi nhiều màu khác nhau ({string.Join(", ", colors)}) — tạo layer với màu mặc định.");
                    continue;
                }

                plan.TargetColors[target.Key] = colors[0];
            }

            return plan;
        }

        public static bool TryParsePoints(string? text, out List<(double X, double Y)> points)
        {
            points = new List<(double X, double Y)>();
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            foreach (var part in text!.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var xy = part.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (xy.Length != 2
                    || !double.TryParse(xy[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x)
                    || !double.TryParse(xy[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y)
                    || double.IsNaN(x) || double.IsInfinity(x) || double.IsNaN(y) || double.IsInfinity(y))
                {
                    points.Clear();
                    return false;
                }

                // Hai đỉnh liền nhau trùng nhau làm polyline có cạnh dài 0 — gộp lại.
                if (points.Count > 0 && Math.Abs(points[points.Count - 1].X - x) < 1e-9 && Math.Abs(points[points.Count - 1].Y - y) < 1e-9)
                {
                    continue;
                }

                points.Add((x, y));
            }

            return points.Count > 0;
        }

        private static string N(int row) => row.ToString(CultureInfo.InvariantCulture);

        private static string? Cell(IReadOnlyList<string> cells, int index)
        {
            if (cells == null || cells.Count <= index)
            {
                return null;
            }

            var value = cells[index].Trim();
            return value.Length == 0 ? null : value;
        }
    }
}
