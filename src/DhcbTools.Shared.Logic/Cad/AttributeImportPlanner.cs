using System;
using System.Collections.Generic;
using System.Linq;

namespace DhcbTools.Shared.Logic.Cad
{
    /// <summary>CSV attribute: mỗi cặp Handle/Tag được áp tối đa một lần.</summary>
    public sealed class AttributeImportPlan
    {
        public List<(int Row, string Handle, string Tag, string Value)> Rows { get; } = new List<(int, string, string, string)>();
        public List<string> Notes { get; } = new List<string>();
        public List<string> Conflicts { get; } = new List<string>();
    }

    /// <summary>Không để dòng CSV cuối ghi đè dòng trước hoặc khiến xem trước khác lượt ghi thật.</summary>
    public static class AttributeImportPlanner
    {
        public static AttributeImportPlan Plan(IEnumerable<(int Row, string Handle, string Tag, string Value)> rows)
        {
            if (rows == null) throw new ArgumentNullException(nameof(rows));
            var groups = new Dictionary<string, List<(int Row, string Handle, string Tag, string Value)>>(StringComparer.OrdinalIgnoreCase);
            var order = new List<string>();
            foreach (var entry in rows)
            {
                // Canonical handle unifies 1A, 0x1a and (1A). A length prefix keeps arbitrary tags unambiguous.
                var handle = HandleText.TryParse(entry.Handle, out var raw) ? HandleText.ToText(raw) : entry.Handle;
                var key = handle.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + handle + entry.Tag;
                if (!groups.TryGetValue(key, out var group))
                {
                    group = new List<(int, string, string, string)>();
                    groups.Add(key, group);
                    order.Add(key);
                }
                group.Add(entry);
            }
            var plan = new AttributeImportPlan();
            foreach (var key in order)
            {
                var group = groups[key];
                var first = group[0];
                if (group.Skip(1).Any(other => !string.Equals(first.Value, other.Value, StringComparison.Ordinal)))
                {
                    plan.Conflicts.Add($"Handle \"{first.Handle}\", tag \"{first.Tag}\" có giá trị CSV mâu thuẫn (dòng {string.Join(", ", group.Select(g => g.Row))}) — không nhập cặp này; sửa CSV rồi nhập lại.");
                    continue;
                }
                plan.Rows.Add(first);
                foreach (var duplicate in group.Skip(1))
                    plan.Notes.Add($"Dòng {duplicate.Row}: Handle \"{duplicate.Handle}\", tag \"{duplicate.Tag}\" lặp dòng {first.Row} với cùng giá trị — bỏ qua bản lặp.");
            }
            return plan;
        }
    }
}
