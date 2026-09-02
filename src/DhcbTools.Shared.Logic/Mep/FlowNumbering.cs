using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace DhcbTools.Shared.Logic.Mep
{
    /// <summary>Kết quả đánh số theo dòng chảy cho một phần tử.</summary>
    public sealed class FlowLabel<TKey>
    {
        public FlowLabel(TKey key, string label, int depth)
        {
            Key = key;
            Label = label;
            Depth = depth;
        }

        public TKey Key { get; }

        public string Label { get; }

        /// <summary>Số cạnh từ nguồn.</summary>
        public int Depth { get; }
    }

    /// <summary>
    /// Mục 3.5: đánh số theo thứ tự dòng chảy trên connector graph. Đầu vào là danh sách cạnh (id, id) và
    /// phần tử nguồn; đầu ra là nhãn phân cấp: trục chính 1,2,3…; gặp nhánh thì <c>{Số nhánh}.{Số trong nhánh}</c>,
    /// nhánh của nhánh là <c>1.2.1</c>… Không cần Revit — Core chỉ việc dựng cạnh từ ConnectorManager.
    /// </summary>
    public static class FlowNumbering
    {
        /// <param name="edges">Cạnh vô hướng giữa hai phần tử nối nhau.</param>
        /// <param name="source">Phần tử nguồn (AHU, tủ điện, bơm) — không nhận nhãn.</param>
        /// <param name="prefix">Tiền tố nhãn.</param>
        /// <param name="padWidth">Đệm 0 cho số trong nhánh.</param>
        /// <param name="depthFirst">true: đi hết một nhánh rồi mới sang nhánh kế (DFS); false: theo lớp (BFS).</param>
        /// <param name="tieBreaker">Sắp thứ tự các phần tử kề (ví dụ theo toạ độ) để kết quả ổn định; null = thứ tự cạnh đầu vào.</param>
        public static List<FlowLabel<TKey>> Assign<TKey>(
            IEnumerable<Tuple<TKey, TKey>> edges,
            TKey source,
            string prefix = "",
            int padWidth = 0,
            bool depthFirst = true,
            IComparer<TKey>? tieBreaker = null)
            where TKey : notnull
        {
            if (edges == null)
            {
                throw new ArgumentNullException(nameof(edges));
            }

            var adjacency = new Dictionary<TKey, List<TKey>>();
            void Add(TKey a, TKey b)
            {
                if (!adjacency.TryGetValue(a, out var list))
                {
                    list = new List<TKey>();
                    adjacency[a] = list;
                }

                if (!list.Contains(b))
                {
                    list.Add(b);
                }
            }

            foreach (var e in edges)
            {
                if (e.Item1.Equals(e.Item2))
                {
                    continue;
                }

                Add(e.Item1, e.Item2);
                Add(e.Item2, e.Item1);
            }

            if (!adjacency.ContainsKey(source))
            {
                throw new ArgumentException("Phần tử nguồn không nằm trong đồ thị.", nameof(source));
            }

            if (tieBreaker != null)
            {
                foreach (var list in adjacency.Values)
                {
                    list.Sort(tieBreaker);
                }
            }

            var result = new List<FlowLabel<TKey>>();
            var visited = new HashSet<TKey> { source };
            var branchCounter = 0;

            // Mỗi "chuỗi" = một đường đi thẳng: (đỉnh bắt đầu, tiền tố nhánh, độ sâu, số bắt đầu).
            var pending = new List<Tuple<TKey, string, int, int>> { Tuple.Create(source, string.Empty, 0, 1) };

            while (pending.Count > 0)
            {
                var job = depthFirst ? pending[pending.Count - 1] : pending[0];
                pending.RemoveAt(depthFirst ? pending.Count - 1 : 0);

                var current = job.Item1;
                var branchPrefix = job.Item2;
                var depth = job.Item3;
                var n = job.Item4;

                while (true)
                {
                    var next = adjacency[current].Where(x => !visited.Contains(x)).ToList();
                    if (next.Count == 0)
                    {
                        break;
                    }

                    // Nhánh đầu tiên tiếp tục trục hiện tại; các nhánh còn lại được cấp số nhánh mới.
                    var branches = new List<Tuple<TKey, string, int, int>>();
                    for (var i = 1; i < next.Count; i++)
                    {
                        var branchNode = next[i];
                        visited.Add(branchNode);
                        branchCounter++;
                        var newPrefix = (branchPrefix.Length == 0 ? string.Empty : branchPrefix + ".") + branchCounter.ToString(CultureInfo.InvariantCulture);
                        result.Add(new FlowLabel<TKey>(branchNode, Format(prefix, newPrefix, 1, padWidth), depth + 1));
                        branches.Add(Tuple.Create(branchNode, newPrefix, depth + 1, 2));
                    }

                    // DFS: đẩy ngược để nhánh nhỏ được xử lý trước; BFS: nối vào cuối.
                    if (depthFirst)
                    {
                        for (var i = branches.Count - 1; i >= 0; i--)
                        {
                            pending.Add(branches[i]);
                        }
                    }
                    else
                    {
                        pending.AddRange(branches);
                    }

                    var main = next[0];
                    visited.Add(main);
                    result.Add(new FlowLabel<TKey>(main, Format(prefix, branchPrefix, n, padWidth), depth + 1));
                    n++;
                    depth++;
                    current = main;
                }
            }

            return result;
        }

        private static string Format(string prefix, string branchPrefix, int number, int padWidth)
        {
            var num = number.ToString(CultureInfo.InvariantCulture).PadLeft(Math.Max(0, padWidth), '0');
            return prefix + (branchPrefix.Length == 0 ? num : branchPrefix + "." + num);
        }
    }
}
