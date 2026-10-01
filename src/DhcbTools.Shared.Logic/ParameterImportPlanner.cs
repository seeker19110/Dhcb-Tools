using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace DhcbTools.Shared.Logic
{
    /// <summary>Một ô CSV của <c>ParameterImport</c> đã đọc xong đối chiếu với mô hình, CHƯA ghi gì.</summary>
    public sealed class ParameterCell
    {
        public ParameterCell(int row, long elementId, string parameter, string value, bool sameAsModel, bool typeLevel)
        {
            Row = row;
            ElementId = elementId;
            Parameter = parameter ?? string.Empty;
            Value = value ?? string.Empty;
            SameAsModel = sameAsModel;
            TypeLevel = typeLevel;
        }

        /// <summary>Số dòng trong file CSV (đếm từ 1, dòng tiêu đề là 1) — để thông báo chỉ đúng chỗ.</summary>
        public int Row { get; }

        /// <summary>Phần tử SẼ bị ghi: chính phần tử của dòng, hoặc TYPE của nó khi tham số nằm ở type.</summary>
        public long ElementId { get; }

        public string Parameter { get; }

        public string Value { get; }

        /// <summary>Giá trị trong CSV trùng giá trị của mô hình TRƯỚC khi nhập (không phải sau các dòng trước).</summary>
        public bool SameAsModel { get; }

        /// <summary>Tham số nằm ở type: một lần ghi đổi mọi phần tử cùng type.</summary>
        public bool TypeLevel { get; }
    }

    /// <summary>Kế hoạch ghi: mỗi (phần tử, tham số) tối đa MỘT lần ghi.</summary>
    public sealed class ParameterImportPlan
    {
        public List<ParameterCell> Writes { get; } = new List<ParameterCell>();

        /// <summary>Số ô không cần ghi: trùng mô hình, hoặc là bản sao giá trị cũ của một tham số type đã đổi ở dòng khác.</summary>
        public int Unchanged { get; set; }

        /// <summary>Ô mang giá trị mới mà lại mâu thuẫn với ô khác cùng (phần tử, tham số) — không ghi ô nào trong nhóm.</summary>
        public List<string> Conflicts { get; } = new List<string>();

        /// <summary>Ghi chú cho kỹ sư: đổi tham số type là đổi cả loạt phần tử.</summary>
        public List<string> Notes { get; } = new List<string>();
    }

    /// <summary>
    /// Quyết định ô nào của CSV được ghi vào mô hình (lệnh <c>ParameterImport</c>). Tách khỏi Revit để test được.
    /// <para>
    /// Lỗi đã sửa (audit 2026-10-01 vòng 2): bản cũ ghi từng dòng và so với giá trị HIỆN TẠI của mô hình. Tham số
    /// type xuất ra ở MỌI dòng của các phần tử cùng type; kỹ sư sửa một dòng thì dòng đó ghi giá trị mới vào type,
    /// rồi các dòng sau — vẫn mang giá trị cũ của bản xuất — thấy "khác hiện tại" và ghi đè lại. Kết quả phụ thuộc
    /// thứ tự dòng, bản xem trước đếm cả hai lần ghi và báo "sẽ cập nhật 2" cho một việc không xảy ra.
    /// </para>
    /// <para>
    /// Quy tắc mới, không phụ thuộc thứ tự: so với giá trị TRƯỚC khi nhập; trong một nhóm cùng (phần tử, tham số),
    /// ô trùng giá trị cũ là bản sao của bản xuất (bỏ qua), đúng MỘT giá trị mới thì ghi một lần, từ hai giá trị mới
    /// khác nhau trở lên là xung đột — không ghi, báo rõ dòng nào mang giá trị nào.
    /// </para>
    /// </summary>
    public static class ParameterImportPlanner
    {
        public static ParameterImportPlan Plan(IEnumerable<ParameterCell> cells)
        {
            if (cells == null)
            {
                throw new ArgumentNullException(nameof(cells));
            }

            var plan = new ParameterImportPlan();
            var groups = new Dictionary<(long, string), List<ParameterCell>>();
            var order = new List<(long, string)>();
            foreach (var cell in cells)
            {
                var key = (cell.ElementId, cell.Parameter);
                if (!groups.TryGetValue(key, out var list))
                {
                    list = new List<ParameterCell>();
                    groups[key] = list;
                    order.Add(key);
                }

                list.Add(cell);
            }

            foreach (var key in order)
            {
                var group = groups[key];
                var changes = group.Where(c => !c.SameAsModel).ToList();
                var distinct = changes.Select(c => c.Value).Distinct(StringComparer.Ordinal).ToList();
                if (distinct.Count == 0)
                {
                    plan.Unchanged += group.Count;
                    continue;
                }

                var first = changes[0];
                if (distinct.Count > 1)
                {
                    plan.Conflicts.Add(
                        $"Xung đột ở tham số {Describe(first)}: "
                        + string.Join("; ", changes.Select(c => $"dòng {Row(c)} = \"{c.Value}\""))
                        + " — không ghi giá trị nào, sửa CSV cho thống nhất rồi nhập lại.");
                    continue;
                }

                plan.Writes.Add(first);
                plan.Unchanged += group.Count - 1;
                if (first.TypeLevel && group.Count > 1)
                {
                    var others = changes.Count > 1
                        ? $"dòng {string.Join(", ", changes.Select(Row))}"
                        : $"dòng {Row(first)}";
                    plan.Notes.Add(
                        $"Tham số {Describe(first)} đổi thành \"{first.Value}\" theo {others} — áp cho MỌI phần tử cùng type; "
                        + $"{group.Count - changes.Count} dòng khác còn giá trị cũ của bản xuất, bỏ qua.");
                }
            }

            return plan;
        }

        private static string Row(ParameterCell cell) => cell.Row.ToString(CultureInfo.InvariantCulture);

        private static string Describe(ParameterCell cell) =>
            $"\"{cell.Parameter}\" của {(cell.TypeLevel ? "type" : "phần tử")} {cell.ElementId.ToString(CultureInfo.InvariantCulture)}";
    }
}
