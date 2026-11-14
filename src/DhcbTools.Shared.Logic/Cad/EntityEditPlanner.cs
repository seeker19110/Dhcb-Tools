using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace DhcbTools.Shared.Logic.Cad
{
    /// <summary>
    /// Một thao tác hình học của lệnh <c>EntityEdit</c> — JSON phẳng để agent/MCP gửi thẳng. Trường nào dùng tuỳ
    /// <see cref="Op"/>; trường thừa với op đó bị <see cref="EntityEditPlanner"/> báo chứ không lặng lẽ bỏ qua.
    /// Toạ độ là mảng <c>[x, y]</c> hoặc <c>[x, y, z]</c> theo đơn vị bản vẽ; góc tính bằng độ, ngược chiều kim đồng hồ.
    /// </summary>
    public sealed class EntityOperation
    {
        /// <summary>line, polyline, rectangle, circle, arc, ellipse, text, mtext, hatch, dimension, point (tạo) —
        /// move, copy, array, rotate, scale, mirror, offset, erase, explode, set, trim, replacePoints (sửa theo handle).</summary>
        public string Op { get; set; } = string.Empty;

        /// <summary>Nhãn đặt cho đối tượng tạo ra (kể cả bản sao) — op sau trỏ lại bằng <c>"@nhãn"</c> trong <see cref="Handles"/>.</summary>
        public string? Id { get; set; }

        /// <summary>Layer cho đối tượng tạo ra (tạo layer nếu chưa có). Rỗng = layer hiện hành. <c>set</c>: layer mới của
        /// đối tượng; <c>explode</c>: layer cho mọi mảnh không khớp <see cref="LayerMap"/>.</summary>
        public string? Layer { get; set; }

        /// <summary>Màu ACI 1–255 cho đối tượng tạo ra (rỗng = ByLayer). <c>set</c> nhận thêm 0 = ByBlock, 256 = ByLayer.</summary>
        public short? Color { get; set; }

        /// <summary>Explode: layer gốc → layer đích cho các mảnh (vd <c>{"0": "M-PIPE"}</c>); layer không có trong bảng
        /// thì theo <see cref="Layer"/>, không có <see cref="Layer"/> thì giữ nguyên.</summary>
        public Dictionary<string, string>? LayerMap { get; set; }

        public List<string>? Handles { get; set; }

        public double[]? From { get; set; }

        public double[]? To { get; set; }

        public List<double[]>? Points { get; set; }

        public bool? Closed { get; set; }

        public double? Width { get; set; }

        public double[]? Center { get; set; }

        public double? Radius { get; set; }

        public double? StartAngle { get; set; }

        public double? EndAngle { get; set; }

        /// <summary>Ellipse: vector bán trục lớn tính từ tâm.</summary>
        public double[]? MajorAxis { get; set; }

        /// <summary>Ellipse: tỉ số bán trục nhỏ / bán trục lớn (0 &lt; ratio ≤ 1).</summary>
        public double? Ratio { get; set; }

        public double[]? Position { get; set; }

        public string? Text { get; set; }

        public double? Height { get; set; }

        /// <summary>Text/MText: tên text style (rỗng = style hiện hành). Chữ tiếng Việt cần style có font Unicode (Arial…).</summary>
        public string? Style { get; set; }

        /// <summary>Text/MText: góc xoay (độ). Rotate: dùng <see cref="Angle"/>.</summary>
        public double? Rotation { get; set; }

        /// <summary>Hatch: tên mẫu (mặc định SOLID).</summary>
        public string? Pattern { get; set; }

        /// <summary>Hatch: tỉ lệ mẫu (mặc định 1).</summary>
        public double? PatternScale { get; set; }

        public double[]? Delta { get; set; }

        /// <summary>Copy: số bản sao, bản thứ i dời i×delta (mặc định 1).</summary>
        public int? Count { get; set; }

        public int? Rows { get; set; }

        public int? Columns { get; set; }

        public double? RowSpacing { get; set; }

        public double? ColumnSpacing { get; set; }

        public double[]? BasePoint { get; set; }

        public double? Angle { get; set; }

        public double? Factor { get; set; }

        /// <summary>Mirror: giữ bản gốc (mặc định true — như MIRROR trả lời "No" khi hỏi xoá nguồn).</summary>
        public bool? KeepOriginal { get; set; }

        /// <summary>Offset: khoảng cách; dấu quyết định phía (theo quy ước <c>GetOffsetCurves</c> của AutoCAD).</summary>
        public double? Distance { get; set; }
    }

    /// <summary>Loại op đã chuẩn hoá — mã <see cref="EntityEditPlanner"/> đã kiểm đủ trường cho từng loại.</summary>
    public enum EntityOpKind
    {
        Line,
        Polyline,
        Rectangle,
        Circle,
        Arc,
        Ellipse,
        Text,
        MText,
        Hatch,
        Dimension,
        Point,
        Move,
        Copy,
        Array,
        Rotate,
        Scale,
        Mirror,
        Offset,
        Erase,
        Explode,
        Set,
        Trim,
        ReplacePoints,
    }

    /// <summary>Một op đã kiểm: số thứ tự (1-based), loại, và bản gốc để vỏ AutoCAD đọc giá trị.</summary>
    public sealed class EntityEditStep
    {
        public EntityEditStep(int index, EntityOpKind kind, EntityOperation source)
        {
            Index = index;
            Kind = kind;
            Source = source;
        }

        public int Index { get; }

        public EntityOpKind Kind { get; }

        public EntityOperation Source { get; }

        public bool Creates => EntityEditPlanner.IsCreate(Kind);

        /// <summary>Op này sinh đối tượng mới (tạo hoặc sao chép) — nhãn <see cref="EntityOperation.Id"/> có nghĩa.</summary>
        public bool Produces => Creates || Kind == EntityOpKind.Copy || Kind == EntityOpKind.Array || Kind == EntityOpKind.Offset
                                || Kind == EntityOpKind.Explode
                                || (Kind == EntityOpKind.Mirror && Source.KeepOriginal != false);

        public string Label => $"#{Index} {Kind.ToString().ToLowerInvariant()}";
    }

    public sealed class EntityEditPlan
    {
        public List<EntityEditStep> Steps { get; } = new List<EntityEditStep>();

        /// <summary>Lỗi kiểm tra — có bất kỳ lỗi nào thì không chạy op nào (cả lô là một giao dịch).</summary>
        public List<string> Errors { get; } = new List<string>();

        public bool IsValid => Errors.Count == 0 && Steps.Count > 0;
    }

    /// <summary>
    /// Kiểm tra lô thao tác hình học của <c>EntityEdit</c> trước khi đụng tới bản vẽ: op lạ, thiếu trường, số không hữu
    /// hạn, bán kính âm, nhãn <c>@x</c> trỏ tới op chưa chạy… đều thành lỗi đọc được kèm số thứ tự op. Cả lô là
    /// một giao dịch nên kiểm hết rồi mới chạy — agent sửa một lần rồi gửi lại, không bị nửa vời trong bản vẽ.
    /// </summary>
    public static class EntityEditPlanner
    {
        /// <summary>Trần số op mỗi lô — vài nghìn đối tượng một lần là đủ cho agent, quá nữa nên tách lô.</summary>
        public const int MaxOperations = 5000;

        /// <summary>Trần số bản sao của copy/array — chặn lỗi gõ kiểu rows=1000, columns=1000.</summary>
        public const int MaxCopies = 10000;

        private static readonly Dictionary<string, EntityOpKind> Kinds = new Dictionary<string, EntityOpKind>(StringComparer.OrdinalIgnoreCase)
        {
            ["line"] = EntityOpKind.Line,
            ["polyline"] = EntityOpKind.Polyline,
            ["pline"] = EntityOpKind.Polyline,
            ["rectangle"] = EntityOpKind.Rectangle,
            ["rect"] = EntityOpKind.Rectangle,
            ["circle"] = EntityOpKind.Circle,
            ["arc"] = EntityOpKind.Arc,
            ["ellipse"] = EntityOpKind.Ellipse,
            ["text"] = EntityOpKind.Text,
            ["mtext"] = EntityOpKind.MText,
            ["hatch"] = EntityOpKind.Hatch,
            ["dimension"] = EntityOpKind.Dimension,
            ["dim"] = EntityOpKind.Dimension,
            ["point"] = EntityOpKind.Point,
            ["move"] = EntityOpKind.Move,
            ["copy"] = EntityOpKind.Copy,
            ["array"] = EntityOpKind.Array,
            ["rotate"] = EntityOpKind.Rotate,
            ["scale"] = EntityOpKind.Scale,
            ["mirror"] = EntityOpKind.Mirror,
            ["offset"] = EntityOpKind.Offset,
            ["erase"] = EntityOpKind.Erase,
            ["delete"] = EntityOpKind.Erase,
            ["explode"] = EntityOpKind.Explode,
            ["set"] = EntityOpKind.Set,
            ["trim"] = EntityOpKind.Trim,
            ["replacePoints"] = EntityOpKind.ReplacePoints,
        };

        /// <summary>Tên op hợp lệ (tên chính, không kể bí danh) — dùng trong thông báo lỗi.</summary>
        public static string ValidOps => string.Join(", ", Enum.GetNames(typeof(EntityOpKind)).Select(n => n.ToLowerInvariant()));

        public static bool IsCreate(EntityOpKind kind) => kind <= EntityOpKind.Point;

        public static EntityEditPlan Plan(IReadOnlyList<EntityOperation>? operations)
        {
            var plan = new EntityEditPlan();
            if (operations == null || operations.Count == 0)
            {
                plan.Errors.Add("Không có thao tác nào (operations rỗng).");
                return plan;
            }

            if (operations.Count > MaxOperations)
            {
                plan.Errors.Add($"Quá {MaxOperations} thao tác một lô ({operations.Count}) — tách thành nhiều lần gọi.");
                return plan;
            }

            var labels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < operations.Count; i++)
            {
                var op = operations[i];
                var index = i + 1;
                if (op == null)
                {
                    plan.Errors.Add($"#{index}: thao tác rỗng (null).");
                    continue;
                }

                if (!Kinds.TryGetValue((op.Op ?? string.Empty).Trim(), out var kind))
                {
                    plan.Errors.Add($"#{index}: op \"{op.Op}\" không hỗ trợ. Hợp lệ: {ValidOps}.");
                    continue;
                }

                var step = new EntityEditStep(index, kind, op);
                var before = plan.Errors.Count;
                Validate(step, labels, plan.Errors);

                if (!string.IsNullOrWhiteSpace(op.Id))
                {
                    var label = op.Id!.Trim();
                    if (!step.Produces)
                    {
                        plan.Errors.Add($"{step.Label}: \"id\" chỉ dùng cho op sinh đối tượng mới (tạo, copy, array, offset, mirror giữ gốc, explode).");
                    }
                    else if (label.StartsWith("@", StringComparison.Ordinal) || label.Length > 64)
                    {
                        plan.Errors.Add($"{step.Label}: id \"{label}\" không hợp lệ (không bắt đầu bằng @, tối đa 64 ký tự).");
                    }
                    else if (!labels.Add(label))
                    {
                        plan.Errors.Add($"{step.Label}: id \"{label}\" trùng với op trước.");
                    }
                }

                if (plan.Errors.Count == before)
                {
                    plan.Steps.Add(step);
                }
            }

            if (plan.Errors.Count > 0)
            {
                plan.Steps.Clear();
            }

            return plan;
        }

        private static void Validate(EntityEditStep step, HashSet<string> labels, List<string> errors)
        {
            var op = step.Source;
            var name = step.Label;
            void Need(bool ok, string message)
            {
                if (!ok) errors.Add($"{name}: {message}");
            }

            if (IsCreate(step.Kind))
            {
                Need(op.Handles == null || op.Handles.Count == 0, "op tạo mới không nhận \"handles\".");
                Need(op.Color == null || (op.Color >= 1 && op.Color <= 255), "color phải là ACI 1–255.");
                if (op.Layer != null && op.Layer.Trim().Length == 0)
                {
                    errors.Add($"{name}: layer rỗng — bỏ trường để dùng layer hiện hành.");
                }
            }
            else
            {
                var takesLayer = step.Kind == EntityOpKind.Set || step.Kind == EntityOpKind.Explode;
                var takesColor = step.Kind == EntityOpKind.Set;
                Need((op.Layer == null || takesLayer) && (op.Color == null || takesColor),
                    "op này không đặt layer/color — dùng op \"set\" (hoặc EntityLayerAssign) để đổi layer/màu.");
                if (takesLayer && op.Layer != null && op.Layer.Trim().Length == 0)
                {
                    errors.Add($"{name}: layer rỗng — bỏ trường nếu không đổi layer.");
                }
                if (op.Handles == null || op.Handles.Count == 0)
                {
                    errors.Add($"{name}: thiếu \"handles\" (handle hex hoặc @nhãn của op trước).");
                }
                else
                {
                    foreach (var handle in op.Handles)
                    {
                        var text = (handle ?? string.Empty).Trim();
                        if (text.StartsWith("@", StringComparison.Ordinal))
                        {
                            Need(labels.Contains(text.Substring(1)), $"\"{text}\" chưa được op nào trước đó đặt id.");
                        }
                        else
                        {
                            Need(HandleText.TryParse(text, out _), $"\"{text}\" không phải handle hex.");
                        }
                    }
                }
            }

            switch (step.Kind)
            {
                case EntityOpKind.Line:
                    Need(IsPoint(op.From) && IsPoint(op.To), "cần from và to dạng [x, y].");
                    Need(!IsPoint(op.From) || !IsPoint(op.To) || Distance(op.From!, op.To!) > 0, "from trùng to — đoạn dài 0.");
                    break;
                case EntityOpKind.Polyline:
                    Need(op.Points != null && op.Points.Count >= 2 && op.Points.All(IsPoint), "cần points ≥ 2 đỉnh dạng [[x, y], …].");
                    Need(op.Width == null || (IsFinite(op.Width.Value) && op.Width >= 0), "width phải ≥ 0.");
                    break;
                case EntityOpKind.Rectangle:
                    Need(IsPoint(op.From) && IsPoint(op.To), "cần from và to là hai góc đối [x, y].");
                    Need(!IsPoint(op.From) || !IsPoint(op.To) || (op.From![0] != op.To![0] && op.From[1] != op.To[1]), "hai góc phải khác nhau cả x lẫn y.");
                    Need(op.Width == null || (IsFinite(op.Width.Value) && op.Width >= 0), "width phải ≥ 0.");
                    break;
                case EntityOpKind.Circle:
                    Need(IsPoint(op.Center), "cần center [x, y].");
                    Need(Positive(op.Radius), "radius phải > 0.");
                    break;
                case EntityOpKind.Arc:
                    Need(IsPoint(op.Center), "cần center [x, y].");
                    Need(Positive(op.Radius), "radius phải > 0.");
                    Need(Finite(op.StartAngle) && Finite(op.EndAngle), "cần startAngle và endAngle (độ).");
                    break;
                case EntityOpKind.Ellipse:
                    Need(IsPoint(op.Center), "cần center [x, y].");
                    Need(IsPoint(op.MajorAxis) && Math.Abs(op.MajorAxis![0]) + Math.Abs(op.MajorAxis[1]) > 0, "cần majorAxis [dx, dy] khác 0.");
                    Need(op.Ratio != null && op.Ratio > 0 && op.Ratio <= 1, "ratio phải trong (0, 1].");
                    break;
                case EntityOpKind.Text:
                case EntityOpKind.MText:
                    Need(IsPoint(op.Position), "cần position [x, y].");
                    Need(!string.IsNullOrEmpty(op.Text), "cần text.");
                    Need(op.Height == null || Positive(op.Height), "height phải > 0.");
                    Need(op.Rotation == null || Finite(op.Rotation), "rotation phải là số.");
                    Need(op.Width == null || (step.Kind == EntityOpKind.MText && Positive(op.Width)), "width chỉ dùng cho mtext và phải > 0.");
                    Need(op.Style == null || op.Style.Trim().Length > 0, "style rỗng — bỏ trường để dùng style hiện hành.");
                    break;
                case EntityOpKind.Hatch:
                    Need(op.Points != null && op.Points.Count >= 3 && op.Points.All(IsPoint), "cần points ≥ 3 đỉnh của biên kín.");
                    Need(op.PatternScale == null || Positive(op.PatternScale), "patternScale phải > 0.");
                    break;
                case EntityOpKind.Dimension:
                    Need(IsPoint(op.From) && IsPoint(op.To), "cần from và to — hai điểm gốc đường gióng [x, y].");
                    Need(!IsPoint(op.From) || !IsPoint(op.To) || Distance(op.From!, op.To!) > 0, "from trùng to — kích thước dài 0.");
                    Need(op.Position == null || IsPoint(op.Position), "position (điểm đặt đường kích thước) phải dạng [x, y].");
                    Need(op.Distance == null || Finite(op.Distance), "distance phải là số.");
                    Need(op.Position == null || op.Distance == null, "chỉ dùng một trong position hoặc distance (cả hai bỏ trống = đường kích thước đi qua trung điểm from–to).");
                    Need(op.Height == null || Positive(op.Height), "height (cỡ chữ kích thước) phải > 0.");
                    Need(op.Style == null || op.Style.Trim().Length > 0, "style (dim style) rỗng — bỏ trường để dùng dim style hiện hành.");
                    Need(op.Text == null || op.Text.Length > 0, "text rỗng — bỏ trường để hiện số đo thật.");
                    break;
                case EntityOpKind.Point:
                    Need(IsPoint(op.Position), "cần position [x, y].");
                    break;
                case EntityOpKind.Move:
                    Need(IsPoint(op.Delta), "cần delta [dx, dy].");
                    break;
                case EntityOpKind.Copy:
                    Need(IsPoint(op.Delta), "cần delta [dx, dy].");
                    Need(op.Count == null || (op.Count >= 1 && op.Count * Math.Max(1, op.Handles?.Count ?? 1) <= MaxCopies), $"count phải 1… và tổng bản sao ≤ {MaxCopies}.");
                    break;
                case EntityOpKind.Array:
                    var rows = op.Rows ?? 1;
                    var columns = op.Columns ?? 1;
                    Need(rows >= 1 && columns >= 1 && rows * columns > 1, "cần rows/columns ≥ 1 và rows×columns > 1.");
                    Need((long)rows * columns * Math.Max(1, op.Handles?.Count ?? 1) <= MaxCopies, $"tổng bản sao vượt {MaxCopies}.");
                    Need(rows == 1 || Finite(op.RowSpacing), "rows > 1 cần rowSpacing.");
                    Need(columns == 1 || Finite(op.ColumnSpacing), "columns > 1 cần columnSpacing.");
                    break;
                case EntityOpKind.Rotate:
                    Need(IsPoint(op.BasePoint), "cần basePoint [x, y].");
                    Need(Finite(op.Angle), "cần angle (độ).");
                    break;
                case EntityOpKind.Scale:
                    Need(IsPoint(op.BasePoint), "cần basePoint [x, y].");
                    Need(Positive(op.Factor), "factor phải > 0.");
                    break;
                case EntityOpKind.Mirror:
                    Need(IsPoint(op.From) && IsPoint(op.To), "cần from và to — hai điểm của trục đối xứng.");
                    Need(!IsPoint(op.From) || !IsPoint(op.To) || Distance(op.From!, op.To!) > 0, "trục đối xứng có hai điểm trùng nhau.");
                    break;
                case EntityOpKind.Offset:
                    Need(Finite(op.Distance) && op.Distance != 0, "cần distance khác 0.");
                    break;
                case EntityOpKind.Erase:
                    break;
                case EntityOpKind.Explode:
                    if (op.LayerMap != null)
                    {
                        Need(op.LayerMap.Count > 0, "layerMap rỗng — bỏ trường nếu không ánh xạ layer.");
                        Need(op.LayerMap.All(p => p.Key.Trim().Length > 0 && !string.IsNullOrWhiteSpace(p.Value)),
                            "layerMap có tên layer rỗng (cả khoá lẫn giá trị phải có chữ).");
                    }

                    break;
                case EntityOpKind.Set:
                    Need(op.Layer != null || op.Color != null || op.Width != null, "cần ít nhất một trong layer, color, width.");
                    Need(op.Color == null || (op.Color >= 0 && op.Color <= 256), "color phải là ACI 0–256 (0 = ByBlock, 256 = ByLayer).");
                    Need(op.Width == null || (IsFinite(op.Width.Value) && op.Width >= 0), "width (bề rộng đều polyline) phải ≥ 0.");
                    break;
                case EntityOpKind.Trim:
                    Need(op.From != null || op.To != null, "cần from (đầu mới) và/hoặc to (cuối mới) dạng [x, y].");
                    Need((op.From == null || IsPoint(op.From)) && (op.To == null || IsPoint(op.To)), "from/to phải dạng [x, y].");
                    Need(!IsPoint(op.From) || !IsPoint(op.To) || Distance(op.From!, op.To!) > 0, "from trùng to — không còn gì để giữ.");
                    break;
                case EntityOpKind.ReplacePoints:
                    Need(op.Handles == null || op.Handles.Count == 1, "replacePoints sửa đúng một polyline mỗi op (handles một phần tử).");
                    Need(op.Points != null && op.Points.Count >= 2 && op.Points.All(IsPoint), "cần points ≥ 2 đỉnh dạng [[x, y], …].");
                    Need(op.Width == null || (IsFinite(op.Width.Value) && op.Width >= 0), "width phải ≥ 0.");
                    break;
            }

            if (step.Kind != EntityOpKind.Explode && op.LayerMap != null)
            {
                errors.Add($"{name}: \"layerMap\" chỉ dùng cho explode.");
            }
        }

        /// <summary>Mảng 2 hoặc 3 số hữu hạn.</summary>
        public static bool IsPoint(double[]? p) => p != null && (p.Length == 2 || p.Length == 3) && p.All(IsFinite);

        public static double Z(double[] p) => p.Length == 3 ? p[2] : 0;

        private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

        private static bool Finite(double? value) => value != null && IsFinite(value.Value);

        private static bool Positive(double? value) => Finite(value) && value > 0;

        private static double Distance(double[] a, double[] b) =>
            Math.Sqrt(((a[0] - b[0]) * (a[0] - b[0])) + ((a[1] - b[1]) * (a[1] - b[1])) + ((Z(a) - Z(b)) * (Z(a) - Z(b))));

        /// <summary>Độ → radian (AutoCAD API dùng radian).</summary>
        public static double Radians(double degrees) => degrees * Math.PI / 180.0;

        /// <summary>Định dạng điểm cho báo cáo.</summary>
        public static string Format(double[] p) =>
            "(" + string.Join(", ", p.Select(v => v.ToString("0.###", CultureInfo.InvariantCulture))) + ")";
    }
}
