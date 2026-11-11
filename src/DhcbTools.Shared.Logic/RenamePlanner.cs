using System;
using System.Collections.Generic;
using System.Linq;

namespace DhcbTools.Shared.Logic
{
    /// <summary>Một phần tử định đổi tên: phạm vi chống trùng, tên hiện tại và giá trị token của nó.</summary>
    public sealed class RenameItem
    {
        public RenameItem(string scope, string name, IDictionary<string, string>? values = null)
        {
            Scope = scope ?? string.Empty;
            Name = name ?? string.Empty;
            Values = values ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!Values.ContainsKey("Name"))
            {
                Values["Name"] = Name;
            }
        }

        /// <summary>Tên chỉ cần không trùng trong cùng phạm vi (ví dụ type trong cùng family).</summary>
        public string Scope { get; }

        public string Name { get; }

        public IDictionary<string, string> Values { get; }
    }

    /// <summary>
    /// Kế hoạch đổi tên hàng loạt cho lệnh ElementRename (view template, filter, type, material): áp
    /// <see cref="NamePattern"/> theo TỪNG phạm vi — tên trùng với phần tử ngoài lô cùng phạm vi, hoặc trùng nhau trong lô,
    /// được thêm hậu tố như <see cref="NamePattern.ApplyAll"/>. Bộ đếm {n} chạy lại từ đầu ở mỗi phạm vi.
    /// </summary>
    public static class RenamePlanner
    {
        /// <summary>Các loại phần tử lệnh ElementRename nhận.</summary>
        public static readonly string[] Kinds = { "ViewTemplates", "Filters", "Types", "Materials" };

        /// <summary>Chuẩn hoá tên loại (không phân biệt hoa thường); giá trị lạ trả null.</summary>
        public static string? NormalizeKind(string? kind) =>
            Kinds.FirstOrDefault(k => string.Equals(k, (kind ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// Tên mới cho từng phần tử (cùng thứ tự với <paramref name="items"/>). <paramref name="others"/> là tên đang có của
        /// phần tử KHÔNG nằm trong lô, kèm phạm vi. <paramref name="notes"/> nhận lý do của tên bị thêm hậu tố.
        /// </summary>
        public static List<string> Plan(
            IReadOnlyList<RenameItem> items, IEnumerable<(string Scope, string Name)> others, NamePattern pattern, out List<string> notes)
        {
            notes = new List<string>();
            var result = new string[items.Count];
            var reservedByScope = others
                .GroupBy(o => o.Scope ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => new HashSet<string>(g.Select(o => o.Name), StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);

            var groups = items.Select((item, index) => (item, index)).GroupBy(x => x.item.Scope, StringComparer.OrdinalIgnoreCase);
            foreach (var group in groups)
            {
                var members = group.ToList();
                reservedByScope.TryGetValue(group.Key, out var reserved);
                var names = pattern.ApplyAll(members.Select(m => (IDictionary<string, string>?)m.item.Values).ToList(), reserved, out var groupNotes);
                var prefix = group.Key.Length == 0 ? string.Empty : group.Key + ": ";
                notes.AddRange(groupNotes.Select(n => prefix + n));
                for (var i = 0; i < members.Count; i++)
                {
                    result[members[i].index] = names[i];
                }
            }

            return result.ToList();
        }

        /// <summary>
        /// Thứ tự đổi an toàn khi tên mới của phần tử này là tên cũ của phần tử khác trong lô (A→B, B→A): trả về
        /// true nếu cần đi hai vòng qua tên tạm.
        /// </summary>
        public static bool NeedsTemporaryNames(IReadOnlyList<RenameItem> items, IReadOnlyList<string> newNames)
        {
            var oldNames = new HashSet<string>(
                items.Select((item, i) => (item, i)).Where(x => !string.Equals(x.item.Name, newNames[x.i], StringComparison.Ordinal)).Select(x => x.item.Scope + "\u0001" + x.item.Name),
                StringComparer.OrdinalIgnoreCase);
            return items.Select((item, i) => (item, i))
                .Any(x => !string.Equals(x.item.Name, newNames[x.i], StringComparison.Ordinal) && oldNames.Contains(x.item.Scope + "\u0001" + newNames[x.i]));
        }
    }
}
