using System;
using System.Collections.Generic;
using System.Linq;

namespace DhcbTools.Shared.Logic.Mep
{
    /// <summary>Điểm 3D đơn giản (feet hoặc mm — tuỳ người gọi, chỉ cần nhất quán).</summary>
    public struct Point3
    {
        public Point3(double x, double y, double z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public double X { get; }

        public double Y { get; }

        public double Z { get; }

        public double DistanceTo(Point3 other)
        {
            var dx = X - other.X;
            var dy = Y - other.Y;
            var dz = Z - other.Z;
            return Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        public override string ToString() => "(" + NumericText.Format(X, 1) + ", " + NumericText.Format(Y, 1) + ", " + NumericText.Format(Z, 1) + ")";
    }

    /// <summary>Một đoạn tuyến đầu vào: khoá của đường vẽ tay + hai đầu mút.</summary>
    public sealed class RouteSegment<TKey>
    {
        public RouteSegment(TKey key, Point3 start, Point3 end)
        {
            Key = key;
            Start = start;
            End = end;
        }

        public TKey Key { get; }

        public Point3 Start { get; }

        public Point3 End { get; }
    }

    /// <summary>Đỉnh của graph tuyến: vị trí gộp + các cạnh nối vào.</summary>
    public sealed class RouteNode
    {
        public RouteNode(int id, Point3 position)
        {
            Id = id;
            Position = position;
        }

        public int Id { get; }

        public Point3 Position { get; }

        public List<int> EdgeIds { get; } = new List<int>();

        /// <summary>Bậc đỉnh: 1 đầu hở, 2 elbow (hoặc nối thẳng), 3 tee, 4 cross, hơn nữa là lỗi.</summary>
        public int Degree => EdgeIds.Count;
    }

    public sealed class RouteEdge<TKey>
    {
        public RouteEdge(int id, TKey key, int startNode, int endNode)
        {
            Id = id;
            Key = key;
            StartNode = startNode;
            EndNode = endNode;
        }

        public int Id { get; }

        public TKey Key { get; }

        public int StartNode { get; }

        public int EndNode { get; }

        public int OtherNode(int node) => node == StartNode ? EndNode : StartNode;
    }

    /// <summary>Loại fitting cần dựng tại một đỉnh (mục 3.1 bước 3).</summary>
    public enum FittingKind
    {
        None,
        Elbow,
        Tee,
        Cross,
        Unsupported,
    }

    /// <summary>
    /// Graph tuyến MEP dựng từ các đoạn thẳng vẽ tay (mục 3.1): gộp đầu mút trong dung sai thành đỉnh, phân loại
    /// bậc đỉnh → fitting, phát hiện chu trình (báo lỗi và loại cạnh đóng chu trình), thứ tự duyệt từ đỉnh gốc.
    /// Hoàn toàn không cần Revit.
    /// </summary>
    public sealed class RouteGraph<TKey>
    {
        private RouteGraph()
        {
        }

        public List<RouteNode> Nodes { get; } = new List<RouteNode>();

        public List<RouteEdge<TKey>> Edges { get; } = new List<RouteEdge<TKey>>();

        /// <summary>Cạnh bị loại vì đóng chu trình hoặc suy biến (hai đầu trùng nhau).</summary>
        public List<RouteEdge<TKey>> Rejected { get; } = new List<RouteEdge<TKey>>();

        public List<string> Warnings { get; } = new List<string>();

        public static RouteGraph<TKey> Build(IEnumerable<RouteSegment<TKey>> segments, double tolerance)
        {
            if (segments == null)
            {
                throw new ArgumentNullException(nameof(segments));
            }

            if (tolerance < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(tolerance));
            }

            var graph = new RouteGraph<TKey>();
            var edgeId = 0;
            var candidates = new List<RouteEdge<TKey>>();

            foreach (var seg in segments)
            {
                if (seg.Start.DistanceTo(seg.End) <= tolerance)
                {
                    graph.Rejected.Add(new RouteEdge<TKey>(-1, seg.Key, -1, -1));
                    graph.Warnings.Add("Đoạn " + seg.Key + " suy biến (hai đầu trùng nhau) — bỏ qua.");
                    continue;
                }

                var a = graph.FindOrAddNode(seg.Start, tolerance);
                var b = graph.FindOrAddNode(seg.End, tolerance);
                if (a == b)
                {
                    graph.Rejected.Add(new RouteEdge<TKey>(-1, seg.Key, a, b));
                    graph.Warnings.Add("Đoạn " + seg.Key + " ngắn hơn dung sai — bỏ qua.");
                    continue;
                }

                candidates.Add(new RouteEdge<TKey>(edgeId++, seg.Key, a, b));
            }

            // Union-Find để loại cạnh đóng chu trình (mục 3.1 bước 1: chu trình thì báo lỗi rõ và bỏ nhánh đó).
            var parent = Enumerable.Range(0, graph.Nodes.Count).ToArray();
            int Find(int x)
            {
                while (parent[x] != x)
                {
                    parent[x] = parent[parent[x]];
                    x = parent[x];
                }
                return x;
            }

            foreach (var e in candidates)
            {
                var ra = Find(e.StartNode);
                var rb = Find(e.EndNode);
                if (ra == rb)
                {
                    graph.Rejected.Add(e);
                    graph.Warnings.Add("Đoạn " + e.Key + " đóng chu trình tại " + graph.Nodes[e.StartNode].Position + " → " + graph.Nodes[e.EndNode].Position + " — bỏ qua, kỹ sư nối tay.");
                    continue;
                }

                parent[ra] = rb;
                graph.Edges.Add(e);
                graph.Nodes[e.StartNode].EdgeIds.Add(e.Id);
                graph.Nodes[e.EndNode].EdgeIds.Add(e.Id);
            }

            foreach (var n in graph.Nodes.Where(n => n.Degree > 4))
            {
                graph.Warnings.Add("Đỉnh " + n.Position + " có " + n.Degree + " nhánh — không có fitting phù hợp, cần tách tay.");
            }

            return graph;
        }

        private int FindOrAddNode(Point3 p, double tolerance)
        {
            for (var i = 0; i < Nodes.Count; i++)
            {
                if (Nodes[i].Position.DistanceTo(p) <= tolerance)
                {
                    return i;
                }
            }

            Nodes.Add(new RouteNode(Nodes.Count, p));
            return Nodes.Count - 1;
        }

        /// <summary>Fitting cần dựng tại đỉnh theo bậc. Bậc 2 nhưng hai cạnh thẳng hàng (góc &lt; <paramref name="straightToleranceDeg"/>) → None (nối thẳng, dùng union/coupling).</summary>
        public FittingKind FittingAt(int nodeId, double straightToleranceDeg = 1.0)
        {
            var node = Nodes[nodeId];
            switch (node.Degree)
            {
                case 0:
                case 1:
                    return FittingKind.None;
                case 2:
                    return AngleAt(nodeId) < straightToleranceDeg ? FittingKind.None : FittingKind.Elbow;
                case 3:
                    return FittingKind.Tee;
                case 4:
                    return FittingKind.Cross;
                default:
                    return FittingKind.Unsupported;
            }
        }

        /// <summary>Góc đổi hướng (độ) tại đỉnh bậc 2: 0 = thẳng, 90 = vuông góc.</summary>
        public double AngleAt(int nodeId)
        {
            var node = Nodes[nodeId];
            if (node.Degree != 2)
            {
                return double.NaN;
            }

            var e1 = Edges.First(e => e.Id == node.EdgeIds[0]);
            var e2 = Edges.First(e => e.Id == node.EdgeIds[1]);
            var p0 = node.Position;
            var p1 = Nodes[e1.OtherNode(nodeId)].Position;
            var p2 = Nodes[e2.OtherNode(nodeId)].Position;

            var v1 = Normalize(p1.X - p0.X, p1.Y - p0.Y, p1.Z - p0.Z);
            var v2 = Normalize(p2.X - p0.X, p2.Y - p0.Y, p2.Z - p0.Z);
            var dot = -(v1[0] * v2[0] + v1[1] * v2[1] + v1[2] * v2[2]); // đảo một véc-tơ để "thẳng" = 0°
            dot = Math.Max(-1.0, Math.Min(1.0, dot));
            return Math.Acos(dot) * 180.0 / Math.PI;
        }

        private static double[] Normalize(double x, double y, double z)
        {
            var len = Math.Sqrt(x * x + y * y + z * z);
            return len < 1e-12 ? new[] { 0.0, 0.0, 0.0 } : new[] { x / len, y / len, z / len };
        }

        /// <summary>Số thành phần liên thông (mỗi thành phần = một tuyến độc lập).</summary>
        public int ComponentCount
        {
            get
            {
                var seen = new HashSet<int>();
                var count = 0;
                for (var i = 0; i < Nodes.Count; i++)
                {
                    if (seen.Contains(i) || Nodes[i].Degree == 0)
                    {
                        continue;
                    }

                    count++;
                    foreach (var n in TraverseNodes(i))
                    {
                        seen.Add(n);
                    }
                }
                return count;
            }
        }

        /// <summary>Thứ tự dựng cạnh theo BFS từ đỉnh gốc (mặc định: đỉnh bậc 1 đầu tiên — đầu tuyến), để nối tuần tự có thể auto-connect.</summary>
        public List<RouteEdge<TKey>> EdgesInBuildOrder(int? rootNode = null)
        {
            var result = new List<RouteEdge<TKey>>();
            var visitedEdges = new HashSet<int>();
            var visitedNodes = new HashSet<int>();

            IEnumerable<int> roots = rootNode.HasValue
                ? new[] { rootNode.Value }
                : Nodes.Where(n => n.Degree == 1).Select(n => n.Id).Concat(Nodes.Where(n => n.Degree > 1).Select(n => n.Id));

            foreach (var root in roots)
            {
                if (visitedNodes.Contains(root))
                {
                    continue;
                }

                var queue = new Queue<int>();
                queue.Enqueue(root);
                visitedNodes.Add(root);
                while (queue.Count > 0)
                {
                    var n = queue.Dequeue();
                    foreach (var eid in Nodes[n].EdgeIds)
                    {
                        if (!visitedEdges.Add(eid))
                        {
                            continue;
                        }

                        var e = Edges.First(x => x.Id == eid);
                        result.Add(e);
                        var other = e.OtherNode(n);
                        if (visitedNodes.Add(other))
                        {
                            queue.Enqueue(other);
                        }
                    }
                }
            }

            return result;
        }

        private IEnumerable<int> TraverseNodes(int start)
        {
            var seen = new HashSet<int> { start };
            var stack = new Stack<int>();
            stack.Push(start);
            while (stack.Count > 0)
            {
                var n = stack.Pop();
                yield return n;
                foreach (var eid in Nodes[n].EdgeIds)
                {
                    var other = Edges.First(x => x.Id == eid).OtherNode(n);
                    if (seen.Add(other))
                    {
                        stack.Push(other);
                    }
                }
            }
        }
    }
}
