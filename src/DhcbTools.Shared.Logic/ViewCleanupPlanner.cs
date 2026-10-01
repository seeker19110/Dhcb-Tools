using System;
using System.Collections.Generic;
using System.Linq;

namespace DhcbTools.Shared.Logic
{
    /// <summary>Một view Revit dưới dạng dữ liệu thuần, đủ để quyết định có được xoá trong lệnh dọn view thừa không.</summary>
    public sealed class ViewCleanupItem
    {
        public ViewCleanupItem(long id, string name, bool isPlaced, IEnumerable<long>? dependentIds = null, string? keepReason = null)
        {
            Id = id;
            Name = name ?? string.Empty;
            IsPlaced = isPlaced;
            DependentIds = dependentIds?.ToList() ?? new List<long>();
            KeepReason = keepReason;
        }

        public long Id { get; }

        public string Name { get; }

        /// <summary>Có mặt trên sheet — qua viewport, schedule hay panel schedule.</summary>
        public bool IsPlaced { get; }

        /// <summary>View phụ thuộc (dependent view) của view này: Revit xoá chúng CÙNG view chính.</summary>
        public IReadOnlyList<long> DependentIds { get; }

        /// <summary>Lý do vỏ buộc giữ (view đang mở, xoá kéo theo khung nhìn trên sheet…); <c>null</c> = không có.</summary>
        public string? KeepReason { get; }
    }

    /// <summary>Kết quả lập kế hoạch: view sẽ xoá, và view chưa đặt trên sheet nhưng vẫn giữ kèm lý do.</summary>
    public sealed class ViewCleanupPlan
    {
        public List<ViewCleanupItem> Delete { get; } = new List<ViewCleanupItem>();

        /// <summary>
        /// View không nằm trên sheet mà vẫn giữ, kèm lý do — để bản xem trước nói được vì sao danh sách ngắn hơn
        /// "mọi view chưa đặt". View giữ vì khớp <c>keepViewNameContains</c> không vào đây: người dùng tự chọn.
        /// </summary>
        public List<KeyValuePair<ViewCleanupItem, string>> Kept { get; } = new List<KeyValuePair<ViewCleanupItem, string>>();
    }

    /// <summary>
    /// Quyết định view nào của <c>RemoveUnusedViews</c> được xoá. Tách khỏi Revit để test được, như
    /// <see cref="CleanupDecider"/> bên AutoCAD.
    /// <para>
    /// Lỗi đã sửa (audit 2026-10-01): bản cũ coi "không có viewport" là thừa rồi <c>Delete</c> thẳng. View CHÍNH
    /// của một mặt bằng chia vùng thường không nằm trên sheet nào — các view PHỤ THUỘC (vùng A, vùng B) mới nằm
    /// trên sheet — mà Revit xoá view chính là xoá luôn mọi view phụ thuộc, kể cả cái đang nằm trên sheet. Nay
    /// view chính chỉ bị xoá khi mọi view phụ thuộc của nó cũng nằm trong danh sách xoá.
    /// </para>
    /// </summary>
    public static class ViewCleanupPlanner
    {
        public static ViewCleanupPlan Plan(IEnumerable<ViewCleanupItem> views, IEnumerable<string>? keepNameContains)
        {
            if (views == null)
            {
                throw new ArgumentNullException(nameof(views));
            }

            var all = views.ToList();
            var byId = new Dictionary<long, ViewCleanupItem>();
            foreach (var view in all)
            {
                byId[view.Id] = view;
            }

            var keep = (keepNameContains ?? Enumerable.Empty<string>()).Where(k => !string.IsNullOrEmpty(k)).ToList();
            var plan = new ViewCleanupPlan();
            var candidates = new HashSet<long>();
            foreach (var view in all)
            {
                if (view.IsPlaced || keep.Any(k => view.Name.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    continue;
                }

                if (view.KeepReason != null)
                {
                    plan.Kept.Add(new KeyValuePair<ViewCleanupItem, string>(view, view.KeepReason));
                    continue;
                }

                candidates.Add(view.Id);
            }

            // Lặp tới khi ổn định: bỏ một view chính khỏi danh sách có thể làm view chính khác (nếu Revit sau này cho
            // lồng nhiều tầng) mất điều kiện. Một tầng như hiện nay thì vòng thứ hai không đổi gì.
            bool changed;
            do
            {
                changed = false;
                foreach (var view in all.Where(v => candidates.Contains(v.Id)))
                {
                    var reason = BlockingDependent(view, candidates, byId);
                    if (reason != null)
                    {
                        candidates.Remove(view.Id);
                        plan.Kept.Add(new KeyValuePair<ViewCleanupItem, string>(view, reason));
                        changed = true;
                    }
                }
            }
            while (changed);

            plan.Delete.AddRange(all.Where(v => candidates.Contains(v.Id)));
            return plan;
        }

        /// <summary>Lý do không xoá được view chính vì một view phụ thuộc phải giữ, hoặc <c>null</c>.</summary>
        private static string? BlockingDependent(ViewCleanupItem view, HashSet<long> candidates, Dictionary<long, ViewCleanupItem> byId)
        {
            foreach (var dependentId in view.DependentIds)
            {
                if (candidates.Contains(dependentId))
                {
                    continue;
                }

                if (!byId.TryGetValue(dependentId, out var dependent))
                {
                    return "có view phụ thuộc #" + dependentId + " không thuộc diện dọn — xoá view chính là xoá luôn nó";
                }

                return dependent.IsPlaced
                    ? "view phụ thuộc \"" + dependent.Name + "\" đang nằm trên sheet — xoá view chính là xoá luôn nó"
                    : "view phụ thuộc \"" + dependent.Name + "\" được giữ — xoá view chính là xoá luôn nó";
            }

            return null;
        }
    }
}
