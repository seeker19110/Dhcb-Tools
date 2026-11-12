using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace DhcbTools.Shared.Logic.Cad
{
    /// <summary>Một dòng đã duyệt của CSV <c>EntityLayerAssign</c>: entity (handle hex) → layer đích.</summary>
    public sealed class EntityLayerAssignment
    {
        public EntityLayerAssignment(int row, long handle, string handleText, string layer, short? colorAci)
        {
            Row = row;
            Handle = handle;
            HandleText = handleText;
            Layer = layer;
            ColorAci = colorAci;
        }

        /// <summary>Số dòng 1-based như Excel.</summary>
        public int Row { get; }

        public long Handle { get; }

        /// <summary>Handle viết hoa, bỏ số 0 đầu — dạng AutoCAD hiển thị.</summary>
        public string HandleText { get; }

        public string Layer { get; }

        /// <summary>Màu ACI cho layer đích nếu lệnh phải TẠO layer đó; layer đã có thì không đổi màu.</summary>
        public short? ColorAci { get; }
    }

    public sealed class EntityLayerAssignPlan
    {
        public List<EntityLayerAssignment> Rows { get; } = new List<EntityLayerAssignment>();

        /// <summary>Dòng bỏ qua có lý do (handle sai định dạng, tên layer trống, dòng lặp…).</summary>
        public List<string> Notes { get; } = new List<string>();

        /// <summary>Một handle bị gán hai layer khác nhau — không áp dòng nào của handle đó.</summary>
        public List<string> Conflicts { get; } = new List<string>();

        /// <summary>Một layer đích được ghi hai màu khác nhau — không tự chọn.</summary>
        public List<string> ColorConflicts { get; } = new List<string>();

        /// <summary>Màu ACI cho từng layer đích (theo dòng đầu tiên có màu).</summary>
        public Dictionary<string, short> TargetColors { get; } = new Dictionary<string, short>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Đọc CSV <c>Handle,Layer[,Color]</c> của lệnh <c>EntityLayerAssign</c>: chuyển từng entity chỉ đích danh sang
    /// layer khác. Dùng khi một layer chứa lẫn nhiều hệ (tag DP/GAS chung một layer IDEN, DIM chung một layer) —
    /// <c>LayerTranslate</c> chỉ đổi được cả layer.
    /// <para>
    /// Quy tắc giống <see cref="LayerImportPlanner"/>: không phụ thuộc thứ tự dòng. Dòng lặp y hệt áp một lần; cùng
    /// handle khác layer là xung đột, không áp dòng nào của handle đó; cùng layer đích khác màu cũng là xung đột màu.
    /// </para>
    /// </summary>
    public static class EntityLayerAssignPlanner
    {
        public static EntityLayerAssignPlan Plan(IEnumerable<string[]> records)
        {
            if (records == null)
            {
                throw new ArgumentNullException(nameof(records));
            }

            var plan = new EntityLayerAssignPlan();
            var byHandle = new Dictionary<long, List<EntityLayerAssignment>>();
            var order = new List<long>();
            var rowNumber = 0;
            foreach (var cells in records)
            {
                rowNumber++;
                if (rowNumber == 1)
                {
                    continue; // dòng tiêu đề
                }

                var handleText = Cell(cells, 0);
                var layer = Cell(cells, 1);
                if (handleText == null && layer == null)
                {
                    continue;
                }

                if (handleText == null || !TryParseHandle(handleText, out var handle))
                {
                    plan.Notes.Add($"Bỏ qua dòng {N(rowNumber)}: handle \"{handleText}\" không phải số hex.");
                    continue;
                }

                if (layer == null)
                {
                    plan.Notes.Add($"Bỏ qua dòng {N(rowNumber)}: thiếu tên layer đích cho handle {Text(handle)}.");
                    continue;
                }

                short? color = null;
                var colorText = Cell(cells, 2);
                if (colorText != null)
                {
                    if (short.TryParse(colorText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var aci) && aci >= 1 && aci <= 255)
                    {
                        color = aci;
                    }
                    else
                    {
                        plan.Notes.Add($"Dòng {N(rowNumber)}: màu \"{colorText}\" không phải ACI 1–255 — bỏ qua màu.");
                    }
                }

                if (!byHandle.TryGetValue(handle, out var list))
                {
                    list = new List<EntityLayerAssignment>();
                    byHandle[handle] = list;
                    order.Add(handle);
                }

                list.Add(new EntityLayerAssignment(rowNumber, handle, Text(handle), layer, color));
            }

            foreach (var handle in order)
            {
                var group = byHandle[handle];
                var first = group[0];
                if (group.Skip(1).Any(g => !string.Equals(g.Layer, first.Layer, StringComparison.OrdinalIgnoreCase)))
                {
                    plan.Conflicts.Add(
                        $"Handle {first.HandleText} được gán nhiều layer khác nhau (dòng {string.Join(", ", group.Select(g => N(g.Row)))}) "
                        + "— không chuyển entity này.");
                    continue;
                }

                plan.Rows.Add(first);
                foreach (var duplicate in group.Skip(1))
                {
                    plan.Notes.Add($"Dòng {N(duplicate.Row)}: handle {first.HandleText} lặp lại dòng {N(first.Row)} — bỏ qua bản lặp.");
                }
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

        public static bool TryParseHandle(string text, out long handle)
        {
            handle = 0;
            var trimmed = text.Trim();
            if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                trimmed = trimmed.Substring(2);
            }

            return trimmed.Length > 0 && trimmed.Length <= 16
                && long.TryParse(trimmed, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out handle)
                && handle > 0;
        }

        private static string Text(long handle) => handle.ToString("X", CultureInfo.InvariantCulture);

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
