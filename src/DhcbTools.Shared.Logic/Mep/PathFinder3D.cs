using System;
using System.Collections.Generic;

namespace DhcbTools.Shared.Logic.Mep
{
    /// <summary>Hộp chướng ngại (mm) theo trục toạ độ.</summary>
    public sealed class Box3
    {
        public Box3(double minX, double minY, double minZ, double maxX, double maxY, double maxZ)
        {
            MinX = Math.Min(minX, maxX);
            MinY = Math.Min(minY, maxY);
            MinZ = Math.Min(minZ, maxZ);
            MaxX = Math.Max(minX, maxX);
            MaxY = Math.Max(minY, maxY);
            MaxZ = Math.Max(minZ, maxZ);
        }

        public double MinX { get; }
        public double MinY { get; }
        public double MinZ { get; }
        public double MaxX { get; }
        public double MaxY { get; }
        public double MaxZ { get; }

        public bool Contains(double x, double y, double z, double clearance = 0)
            => x >= MinX - clearance && x <= MaxX + clearance
            && y >= MinY - clearance && y <= MaxY + clearance
            && z >= MinZ - clearance && z <= MaxZ + clearance;
    }

    public sealed class PathFinderOptions
    {
        /// <summary>Bước lưới (mm). Mặc định 100 theo đặc tả 6.1.</summary>
        public double StepMm { get; set; } = 100;

        /// <summary>Khoảng hở tối thiểu tới chướng ngại (mm) — bán kính ống + cách nhiệt + dung sai lắp.</summary>
        public double ClearanceMm { get; set; } = 100;

        /// <summary>Chi phí phạt mỗi lần đổi hướng (tính bằng số bước) — mỗi lần rẽ là một fitting.</summary>
        public double TurnPenalty { get; set; } = 20;

        /// <summary>Phạt khi đi sát chướng ngại (trong vùng 2×clearance) — ưu tiên tuyến "thoáng".</summary>
        public double NearObstaclePenalty { get; set; } = 2;

        /// <summary>Cho phép đi theo Z (đổi cao độ). Tắt để buộc tuyến nằm ngang một cao độ.</summary>
        public bool AllowVertical { get; set; } = true;

        /// <summary>Giới hạn số ô mở rộng để không thành "hố đen thời gian" (mục 6.1).</summary>
        public int MaxExpandedNodes { get; set; } = 400_000;
    }

    public sealed class PathResult
    {
        public bool Found { get; set; }

        /// <summary>Đường đi đã rút gọn: chỉ giữ điểm đổi hướng (polyline) — đầu vào cho routing mức A.</summary>
        public List<Point3> Polyline { get; } = new List<Point3>();

        public int ExpandedNodes { get; set; }

        public int Turns { get; set; }

        public string? Reason { get; set; }
    }

    /// <summary>
    /// A* trên lưới 3D bước đều (mục 6.1), 6 hướng trục (không đi chéo — duct/ống không chạy chéo), phạt rẽ và
    /// phạt đi gần kết cấu. Phạm vi bị chặn bởi hộp bao tìm kiếm để khả thi. Kết quả là polyline, không dựng thẳng.
    /// </summary>
    public static class PathFinder3D
    {
        private static readonly int[][] Directions =
        {
            new[] { 1, 0, 0 }, new[] { -1, 0, 0 },
            new[] { 0, 1, 0 }, new[] { 0, -1, 0 },
            new[] { 0, 0, 1 }, new[] { 0, 0, -1 },
        };

        public static PathResult FindPath(Point3 start, Point3 goal, IReadOnlyList<Box3> obstacles, Box3 searchBounds, PathFinderOptions? options = null)
        {
            options = options ?? new PathFinderOptions();
            if (options.StepMm <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(options), "Bước lưới phải > 0.");
            }

            var result = new PathResult();
            var step = options.StepMm;

            var s = ToCell(start, searchBounds, step);
            var g = ToCell(goal, searchBounds, step);
            var size = new[]
            {
                (int)Math.Ceiling((searchBounds.MaxX - searchBounds.MinX) / step) + 1,
                (int)Math.Ceiling((searchBounds.MaxY - searchBounds.MinY) / step) + 1,
                (int)Math.Ceiling((searchBounds.MaxZ - searchBounds.MinZ) / step) + 1,
            };

            if (!InBounds(s, size) || !InBounds(g, size))
            {
                result.Reason = "Điểm đầu/cuối nằm ngoài hộp tìm kiếm.";
                return result;
            }

            if (Blocked(s, searchBounds, step, obstacles, options.ClearanceMm) || Blocked(g, searchBounds, step, obstacles, options.ClearanceMm))
            {
                result.Reason = "Điểm đầu hoặc cuối nằm trong chướng ngại (kể cả khoảng hở).";
                return result;
            }

            // Trạng thái = (ô, hướng tới) để tính phạt rẽ đúng.
            var open = new PriorityQueue<(int[] Cell, int Dir)>();
            var gScore = new Dictionary<long, double>();
            var cameFrom = new Dictionary<long, (long Prev, int[] Cell)>();

            long Key(int[] c, int dir) => ((((long)c[0] * size[1] + c[1]) * size[2] + c[2]) * 7) + (dir + 1);

            var startKey = Key(s, -1);
            gScore[startKey] = 0;
            open.Enqueue((s, -1), Heuristic(s, g));
            var expanded = 0;

            while (open.Count > 0)
            {
                var (cell, dir) = open.Dequeue();
                var currentKey = Key(cell, dir);
                expanded++;
                if (expanded > options.MaxExpandedNodes)
                {
                    result.Reason = "Vượt giới hạn " + options.MaxExpandedNodes + " ô — thu hẹp hộp tìm kiếm hoặc tăng bước lưới.";
                    result.ExpandedNodes = expanded;
                    return result;
                }

                if (cell[0] == g[0] && cell[1] == g[1] && cell[2] == g[2])
                {
                    Reconstruct(currentKey, s, cameFrom, searchBounds, step, result);
                    result.Found = true;
                    result.ExpandedNodes = expanded;
                    return result;
                }

                var currentG = gScore[currentKey];
                for (var d = 0; d < Directions.Length; d++)
                {
                    if (!options.AllowVertical && Directions[d][2] != 0)
                    {
                        continue;
                    }

                    var next = new[] { cell[0] + Directions[d][0], cell[1] + Directions[d][1], cell[2] + Directions[d][2] };
                    if (!InBounds(next, size) || Blocked(next, searchBounds, step, obstacles, options.ClearanceMm))
                    {
                        continue;
                    }

                    var cost = 1.0;
                    if (dir >= 0 && dir != d)
                    {
                        cost += options.TurnPenalty;
                    }

                    if (options.NearObstaclePenalty > 0 && Blocked(next, searchBounds, step, obstacles, options.ClearanceMm * 2))
                    {
                        cost += options.NearObstaclePenalty;
                    }

                    var nextKey = Key(next, d);
                    var tentative = currentG + cost;
                    if (gScore.TryGetValue(nextKey, out var existing) && existing <= tentative)
                    {
                        continue;
                    }

                    gScore[nextKey] = tentative;
                    cameFrom[nextKey] = (currentKey, next);
                    open.Enqueue((next, d), tentative + Heuristic(next, g));
                }
            }

            result.Reason = "Không có đường đi trong hộp tìm kiếm.";
            result.ExpandedNodes = expanded;
            return result;
        }

        private static void Reconstruct(long key, int[] startCell, Dictionary<long, (long Prev, int[] Cell)> cameFrom, Box3 b, double step, PathResult result)
        {
            var cells = new List<int[]>();
            var k = key;
            while (cameFrom.TryGetValue(k, out var entry))
            {
                cells.Add(entry.Cell);
                k = entry.Prev;
            }

            cells.Add(startCell);
            cells.Reverse();

            // Rút gọn: giữ điểm đầu, điểm cuối và điểm đổi hướng.
            var pts = new List<Point3> { FromCell(cells[0], b, step) };
            for (var i = 2; i < cells.Count; i++)
            {
                var prev = new[] { cells[i - 1][0] - cells[i - 2][0], cells[i - 1][1] - cells[i - 2][1], cells[i - 1][2] - cells[i - 2][2] };
                var now = new[] { cells[i][0] - cells[i - 1][0], cells[i][1] - cells[i - 1][1], cells[i][2] - cells[i - 1][2] };
                if (prev[0] != now[0] || prev[1] != now[1] || prev[2] != now[2])
                {
                    pts.Add(FromCell(cells[i - 1], b, step));
                    result.Turns++;
                }
            }

            if (cells.Count > 1)
            {
                pts.Add(FromCell(cells[cells.Count - 1], b, step));
            }

            result.Polyline.AddRange(pts);
        }

        private static double Heuristic(int[] a, int[] b) => Math.Abs(a[0] - b[0]) + Math.Abs(a[1] - b[1]) + Math.Abs(a[2] - b[2]);

        private static bool InBounds(int[] c, int[] size) => c[0] >= 0 && c[1] >= 0 && c[2] >= 0 && c[0] < size[0] && c[1] < size[1] && c[2] < size[2];

        private static bool Blocked(int[] c, Box3 b, double step, IReadOnlyList<Box3> obstacles, double clearance)
        {
            var p = FromCell(c, b, step);
            for (var i = 0; i < obstacles.Count; i++)
            {
                if (obstacles[i].Contains(p.X, p.Y, p.Z, clearance))
                {
                    return true;
                }
            }
            return false;
        }

        private static int[] ToCell(Point3 p, Box3 b, double step) => new[]
        {
            (int)Math.Round((p.X - b.MinX) / step),
            (int)Math.Round((p.Y - b.MinY) / step),
            (int)Math.Round((p.Z - b.MinZ) / step),
        };

        private static Point3 FromCell(int[] c, Box3 b, double step) => new Point3(b.MinX + c[0] * step, b.MinY + c[1] * step, b.MinZ + c[2] * step);

        /// <summary>Hàng đợi ưu tiên tối thiểu (netstandard2.0 không có System.Collections.Generic.PriorityQueue).</summary>
        private sealed class PriorityQueue<T>
        {
            private readonly List<(double Priority, T Item)> _heap = new List<(double, T)>();

            public int Count => _heap.Count;

            public void Enqueue(T item, double priority)
            {
                _heap.Add((priority, item));
                var i = _heap.Count - 1;
                while (i > 0)
                {
                    var parent = (i - 1) / 2;
                    if (_heap[parent].Priority <= _heap[i].Priority)
                    {
                        break;
                    }

                    (_heap[parent], _heap[i]) = (_heap[i], _heap[parent]);
                    i = parent;
                }
            }

            public T Dequeue()
            {
                var top = _heap[0].Item;
                var last = _heap[_heap.Count - 1];
                _heap.RemoveAt(_heap.Count - 1);
                if (_heap.Count > 0)
                {
                    _heap[0] = last;
                    var i = 0;
                    while (true)
                    {
                        var l = 2 * i + 1;
                        var r = l + 1;
                        var smallest = i;
                        if (l < _heap.Count && _heap[l].Priority < _heap[smallest].Priority)
                        {
                            smallest = l;
                        }

                        if (r < _heap.Count && _heap[r].Priority < _heap[smallest].Priority)
                        {
                            smallest = r;
                        }

                        if (smallest == i)
                        {
                            break;
                        }

                        (_heap[smallest], _heap[i]) = (_heap[i], _heap[smallest]);
                        i = smallest;
                    }
                }
                return top;
            }
        }
    }
}
