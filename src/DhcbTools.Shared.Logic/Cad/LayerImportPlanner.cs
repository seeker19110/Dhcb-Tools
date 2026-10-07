using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace DhcbTools.Shared.Logic.Cad
{
    /// <summary>Kế hoạch nhập layer: mỗi tên layer tối đa MỘT dòng được áp.</summary>
    public sealed class LayerImportPlan
    {
        /// <summary>Dòng sẽ áp, theo thứ tự xuất hiện đầu tiên trong CSV (số dòng 1-based như Excel).</summary>
        public List<(int Row, LayerCsvRow Layer)> Rows { get; } = new List<(int Row, LayerCsvRow Layer)>();

        /// <summary>Dòng lặp giống hệt một dòng trước đó — bỏ qua, nói ra cho kỹ sư biết.</summary>
        public List<string> Notes { get; } = new List<string>();

        /// <summary>Tên layer có nhiều dòng mang giá trị khác nhau — không áp dòng nào của tên đó.</summary>
        public List<string> Conflicts { get; } = new List<string>();
    }

    /// <summary>
    /// Chọn dòng CSV nào của <c>LayerImport</c> được áp khi một tên layer xuất hiện nhiều lần.
    /// <para>
    /// Lỗi đã sửa (audit 2026-10-01, việc để lại): bản cũ áp từng dòng theo thứ tự. Hai dòng cùng layer (tên AutoCAD
    /// không phân biệt hoa thường: "Wall" và "WALL" là một) thì dòng sau ghi đè dòng trước — kết quả phụ thuộc thứ tự
    /// dòng; với layer chưa có, xem trước báo "tạo mới" hai lần còn chạy thật tạo một lần rồi "cập nhật" lần hai.
    /// </para>
    /// <para>
    /// Quy tắc mới, không phụ thuộc thứ tự: các dòng giống hệt nhau (bản sao chép dán) thì áp dòng đầu, bỏ bản lặp
    /// kèm ghi chú; khác nhau ở bất kỳ ô nào — kể cả một dòng để trống ô mà dòng kia có giá trị — là xung đột:
    /// không áp dòng nào của layer đó, báo dòng nào mâu thuẫn. Không tự trộn hai dòng.
    /// </para>
    /// </summary>
    public static class LayerImportPlanner
    {
        public static LayerImportPlan Plan(IEnumerable<(int Row, LayerCsvRow Layer)> rows)
        {
            if (rows == null)
            {
                throw new ArgumentNullException(nameof(rows));
            }

            var groups = new Dictionary<string, List<(int Row, LayerCsvRow Layer)>>(StringComparer.OrdinalIgnoreCase);
            var order = new List<string>();
            foreach (var entry in rows)
            {
                if (!groups.TryGetValue(entry.Layer.Name, out var list))
                {
                    list = new List<(int Row, LayerCsvRow Layer)>();
                    groups[entry.Layer.Name] = list;
                    order.Add(entry.Layer.Name);
                }

                list.Add(entry);
            }

            var plan = new LayerImportPlan();
            foreach (var name in order)
            {
                var group = groups[name];
                var first = group[0];
                if (group.Skip(1).Any(other => !SameValues(first.Layer, other.Layer)))
                {
                    plan.Conflicts.Add(
                        $"Layer \"{first.Layer.Name}\" có {group.Count} dòng mang giá trị khác nhau "
                        + $"(dòng {string.Join(", ", group.Select(g => Number(g.Row)))}) — không nhập dòng nào của layer này; "
                        + "sửa CSV cho thống nhất rồi nhập lại.");
                    continue;
                }

                plan.Rows.Add(first);
                foreach (var duplicate in group.Skip(1))
                {
                    plan.Notes.Add(
                        $"Dòng {Number(duplicate.Row)}: layer \"{duplicate.Layer.Name}\" lặp lại dòng {Number(first.Row)} "
                        + "với cùng giá trị — bỏ qua bản lặp.");
                }
            }

            return plan;
        }

        /// <summary>
        /// Cùng giá trị ở mọi cột lệnh nhập dùng. Linetype so không phân biệt hoa thường (AutoCAD tra tên linetype như
        /// vậy); Description so đúng từng ký tự, và ô trống (null = giữ nguyên) khác chuỗi rỗng (= xoá mô tả).
        /// </summary>
        private static bool SameValues(LayerCsvRow a, LayerCsvRow b) =>
            a.ColorAci == b.ColorAci
            && a.ColorRgb == b.ColorRgb
            && string.Equals(a.Linetype, b.Linetype, StringComparison.OrdinalIgnoreCase)
            && a.LineWeight == b.LineWeight
            && a.Plottable == b.Plottable
            && string.Equals(a.Description, b.Description, StringComparison.Ordinal);

        private static string Number(int row) => row.ToString(CultureInfo.InvariantCulture);
    }
}
