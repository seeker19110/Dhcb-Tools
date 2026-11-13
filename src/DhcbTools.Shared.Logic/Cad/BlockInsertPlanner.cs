using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace DhcbTools.Shared.Logic.Cad
{
    /// <summary>Một lần đặt block: vị trí, tỉ lệ, góc (độ), layer và giá trị attribute theo tag.</summary>
    public sealed class BlockInsertion
    {
        public double[]? Position { get; set; }

        /// <summary>Tỉ lệ đều X=Y=Z (mặc định 1).</summary>
        public double? Scale { get; set; }

        /// <summary>Góc xoay, độ, ngược chiều kim đồng hồ (mặc định 0).</summary>
        public double? Rotation { get; set; }

        /// <summary>Layer đặt block (tạo nếu chưa có). Rỗng = layer hiện hành.</summary>
        public string? Layer { get; set; }

        /// <summary>Tag → giá trị. Tag không có trong định nghĩa block là lỗi; tag thiếu thì lấy giá trị mặc định.</summary>
        public Dictionary<string, string>? Attributes { get; set; }
    }

    public sealed class BlockInsertPlan
    {
        public List<BlockInsertion> Rows { get; } = new List<BlockInsertion>();

        public List<string> Errors { get; } = new List<string>();
    }

    /// <summary>
    /// Đọc và kiểm danh sách vị trí đặt block của lệnh <c>BlockInsert</c> — từ JSON (agent) hoặc CSV
    /// <c>X,Y[,Z],Rotation,Scale,Layer,&lt;TAG&gt;…</c> (kỹ sư lập trong Excel): mọi cột không thuộc nhóm toạ độ/tỉ lệ
    /// được hiểu là tag attribute. Lỗi trả theo số dòng/số thứ tự để sửa một lần.
    /// </summary>
    public static class BlockInsertPlanner
    {
        public const int MaxInsertions = 10000;

        private static readonly HashSet<string> Reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "X", "Y", "Z", "Rotation", "Scale", "Layer",
        };

        public static BlockInsertPlan Validate(IReadOnlyList<BlockInsertion>? insertions)
        {
            var plan = new BlockInsertPlan();
            if (insertions == null || insertions.Count == 0)
            {
                plan.Errors.Add("Không có vị trí đặt block nào (insertions rỗng).");
                return plan;
            }

            if (insertions.Count > MaxInsertions)
            {
                plan.Errors.Add($"Quá {MaxInsertions} vị trí một lần ({insertions.Count}) — tách lô.");
                return plan;
            }

            for (var i = 0; i < insertions.Count; i++)
            {
                Check(insertions[i], $"#{i + 1}", plan);
            }

            return plan;
        }

        public static BlockInsertPlan FromCsv(IEnumerable<string[]> records)
        {
            var plan = new BlockInsertPlan();
            string[]? header = null;
            var row = 0;
            foreach (var record in records)
            {
                row++;
                if (header == null)
                {
                    header = record.Select(h => (h ?? string.Empty).Trim()).ToArray();
                    if (!header.Contains("X", StringComparer.OrdinalIgnoreCase) || !header.Contains("Y", StringComparer.OrdinalIgnoreCase))
                    {
                        plan.Errors.Add("CSV thiếu cột X hoặc Y (cần X,Y[,Z],Rotation,Scale,Layer,<TAG>…).");
                        return plan;
                    }

                    continue;
                }

                if (record.All(string.IsNullOrWhiteSpace)) continue;

                string Cell(string name)
                {
                    var index = Array.FindIndex(header, h => h.Equals(name, StringComparison.OrdinalIgnoreCase));
                    return index >= 0 && index < record.Length ? (record[index] ?? string.Empty).Trim() : string.Empty;
                }

                var label = $"Dòng {row}";
                var x = Number(Cell("X"));
                var y = Number(Cell("Y"));
                var zText = Cell("Z");
                var z = zText.Length == 0 ? 0 : Number(zText);
                if (x == null || y == null || z == null)
                {
                    plan.Errors.Add($"{label}: X/Y/Z không phải số (dùng dấu chấm thập phân).");
                    continue;
                }

                var insertion = new BlockInsertion
                {
                    Position = zText.Length == 0 ? new[] { x.Value, y.Value } : new[] { x.Value, y.Value, z.Value },
                    Layer = Cell("Layer").Length == 0 ? null : Cell("Layer"),
                };

                var bad = false;
                foreach (var (name, setter) in new (string, Action<double>)[] { ("Rotation", v => insertion.Rotation = v), ("Scale", v => insertion.Scale = v) })
                {
                    var text = Cell(name);
                    if (text.Length == 0) continue;
                    var value = Number(text);
                    if (value == null)
                    {
                        plan.Errors.Add($"{label}: {name} \"{text}\" không phải số.");
                        bad = true;
                        continue;
                    }

                    setter(value.Value);
                }

                for (var c = 0; c < header.Length; c++)
                {
                    if (header[c].Length == 0 || Reserved.Contains(header[c])) continue;
                    var value = c < record.Length ? record[c] ?? string.Empty : string.Empty;
                    if (value.Length == 0) continue;
                    insertion.Attributes ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    insertion.Attributes[header[c]] = value;
                }

                if (bad) continue;
                Check(insertion, label, plan);
            }

            if (header == null) plan.Errors.Add("CSV rỗng.");
            else if (plan.Rows.Count == 0 && plan.Errors.Count == 0) plan.Errors.Add("CSV không có dòng vị trí nào.");
            if (plan.Rows.Count > MaxInsertions) plan.Errors.Add($"Quá {MaxInsertions} vị trí một lần — tách file.");
            return plan;
        }

        private static void Check(BlockInsertion? insertion, string label, BlockInsertPlan plan)
        {
            if (insertion == null)
            {
                plan.Errors.Add($"{label}: rỗng (null).");
                return;
            }

            var before = plan.Errors.Count;
            if (!EntityEditPlanner.IsPoint(insertion.Position)) plan.Errors.Add($"{label}: cần position [x, y] hoặc [x, y, z].");
            if (insertion.Scale != null && !(insertion.Scale > 0 && !double.IsInfinity(insertion.Scale.Value))) plan.Errors.Add($"{label}: scale phải > 0.");
            if (insertion.Rotation != null && (double.IsNaN(insertion.Rotation.Value) || double.IsInfinity(insertion.Rotation.Value))) plan.Errors.Add($"{label}: rotation phải là số.");
            if (insertion.Layer != null && insertion.Layer.Trim().Length == 0) plan.Errors.Add($"{label}: layer rỗng — bỏ trường để dùng layer hiện hành.");
            if (insertion.Attributes != null && insertion.Attributes.Keys.Any(string.IsNullOrWhiteSpace)) plan.Errors.Add($"{label}: attribute có tag rỗng.");
            if (plan.Errors.Count == before && !plan.Rows.Contains(insertion)) plan.Rows.Add(insertion);
        }

        private static double? Number(string text) =>
            double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && !double.IsNaN(value) && !double.IsInfinity(value)
                ? value
                : (double?)null;
    }
}
