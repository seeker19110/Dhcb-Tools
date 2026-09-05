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

        /// <summary>
        /// Giới hạn số trạng thái mở rộng để không thành "hố đen thời gian" (mục 6.1). <c>null</c> (mặc định)
        /// = tự chọn theo cỡ bài toán: số trạng thái của lưới (ô × 7 hướng tới, vì phạt rẽ tách trạng thái
        /// theo hướng), kẹp trong [<see cref="AutoBudgetMin"/>, <see cref="AutoBudgetMax"/>].
        /// <para>
        /// Trước đây cố định 400.000 bất kể lưới to nhỏ. Đo trên hộp 30 × 30 m một lớp cao độ, ba tường so le:
        /// tuyến tối ưu cần 459.000 trạng thái — trần cũ thua ở 400.000 dù hai điểm nối thông và cả bài toán
        /// chỉ mất 0,3 s. Hết ngân sách sớm hơn 15 % so với chỗ có lời giải là kiểu thất bại tệ nhất: không
        /// nói gì về mô hình, chỉ nói về một hằng số.
        /// </para>
        /// </summary>
        public int? MaxExpandedNodes { get; set; }

        /// <summary>Sàn ngân sách tự động — bằng trần cố định cũ, để lưới nhỏ không bị siết chặt hơn trước.</summary>
        public const int AutoBudgetMin = 400_000;

        /// <summary>
        /// Trần ngân sách tự động. Mỗi trạng thái mở rộng giữ một mục trong bảng đã thăm và một số trong hàng
        /// đợi; đo được ~100 byte/trạng thái, nên 2 triệu là ~200 MB tạm thời — chấp nhận được trong tiến
        /// trình Revit, cao hơn thì không. Muốn hơn thì đặt tay <see cref="MaxExpandedNodes"/>.
        /// </summary>
        public const int AutoBudgetMax = 2_000_000;

        /// <summary>Số hướng tới một trạng thái có thể mang: 6 trục + "chưa có hướng" ở điểm đầu.</summary>
        public const int StatesPerCell = 7;

        /// <summary>Ngân sách thật sự dùng cho một lưới <paramref name="gridCells"/> ô.</summary>
        public int EffectiveMaxExpandedNodes(long gridCells)
            => MaxExpandedNodes ?? (int)Math.Max(AutoBudgetMin, Math.Min(AutoBudgetMax, gridCells * StatesPerCell));

        /// <summary>
        /// Trần số ô của lưới (không phải số ô mở rộng). Lưới được raster hoá trước nên bộ nhớ tỉ lệ với số
        /// ô; vượt trần thì từ chối NGAY thay vì chạy hàng chục giây rồi mới báo thua.
        /// </summary>
        public long MaxCells { get; set; } = 16_000_000;
    }

    public sealed class PathResult
    {
        public bool Found { get; set; }

        /// <summary>Đường đi đã rút gọn: chỉ giữ điểm đổi hướng (polyline) — đầu vào cho routing mức A.</summary>
        public List<Point3> Polyline { get; } = new List<Point3>();

        public int ExpandedNodes { get; set; }

        /// <summary>Ngân sách node mở rộng đã áp dụng (tự chọn theo lưới hoặc do người gọi đặt).</summary>
        public int MaxExpandedNodes { get; set; }

        public int Turns { get; set; }

        public string? Reason { get; set; }

        /// <summary>Tổng số ô của lưới tìm kiếm — để người đọc biết bài toán to cỡ nào.</summary>
        public long GridCells { get; set; }

        /// <summary>Số ô trống nối thông được với điểm đầu (chỉ tính khi thất bại, bằng flood-fill).</summary>
        public int ReachableCells { get; set; }

        /// <summary>
        /// Khi thất bại: điểm cuối có nối thông với điểm đầu không (bỏ qua mọi phạt). <c>true</c> nghĩa là
        /// tuyến CÓ tồn tại, chỉ là hết ngân sách tìm kiếm — khác hẳn với bị kết cấu bịt kín.
        /// </summary>
        public bool GoalConnected { get; set; }
    }

    /// <summary>
    /// A* trên lưới 3D bước đều (mục 6.1), 6 hướng trục (không đi chéo — duct/ống không chạy chéo), phạt rẽ và
    /// phạt đi gần kết cấu. Phạm vi bị chặn bởi hộp bao tìm kiếm để khả thi. Kết quả là polyline, không dựng thẳng.
    ///
    /// Chướng ngại được RASTER HOÁ vào lưới bit một lần trước khi chạy: tra ô bị chặn là O(1) thay vì quét
    /// tuyến tính cả danh sách hộp cho từng ô (trên model thật, 546 hộp × 400.000 ô × 6 hướng là hàng tỉ phép
    /// thử). Heuristic có cộng phạt rẽ nhưng vẫn là chặn dưới nên tuyến trả về vẫn tối ưu.
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
            if (obstacles == null)
            {
                throw new ArgumentNullException(nameof(obstacles));
            }

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

            var cells = (long)size[0] * size[1] * size[2];
            result.GridCells = cells;
            var budget = options.EffectiveMaxExpandedNodes(cells);
            result.MaxExpandedNodes = budget;
            if (cells > options.MaxCells)
            {
                result.Reason = $"Hộp tìm kiếm quá lớn so với bước lưới: {cells:N0} ô (trần {options.MaxCells:N0}) — tăng bước lưới hoặc thu hẹp hộp.";
                return result;
            }

            if (!InBounds(s, size) || !InBounds(g, size))
            {
                result.Reason = "Điểm đầu/cuối nằm ngoài hộp tìm kiếm.";
                return result;
            }

            var grid = new OccupancyGrid(searchBounds, size, step, obstacles, options.ClearanceMm,
                options.NearObstaclePenalty > 0 ? options.ClearanceMm * 2 : (double?)null);

            var goalIndex = grid.Index(g);
            if (grid.IsBlocked(grid.Index(s)) || grid.IsBlocked(goalIndex))
            {
                result.Reason = "Điểm đầu hoặc cuối nằm trong chướng ngại (kể cả khoảng hở).";
                return result;
            }

            // Trạng thái = (ô, hướng tới) để tính phạt rẽ đúng.
            // Mỗi trạng thái chỉ là một số nguyên (chỉ số ô × 7 + hướng); ô suy ngược bằng OccupancyGrid.FromIndex
            // khi cần. Không cấp phát int[] cho từng node trong hàng đợi lẫn bảng đã thăm — với ngân sách
            // 2 triệu node thì đó là hàng triệu mảng nhỏ, đo được ~85 MB cho 460.000 node ở bản cũ.
            var open = new PriorityQueue<long>();
            var visited = new Dictionary<long, (double G, long Prev)>();

            long Key(int index, int dir) => ((long)index * 7) + (dir + 1);

            var startKey = Key(grid.Index(s), -1);
            visited[startKey] = (0, -1);
            open.Enqueue(startKey, Heuristic(s, -1, g, options.TurnPenalty));
            var expanded = 0;

            while (open.Count > 0)
            {
                var currentKey = open.Dequeue();
                var cell = grid.FromIndex((int)(currentKey / 7));
                var dir = (int)(currentKey % 7) - 1;
                expanded++;
                if (expanded > budget)
                {
                    result.ExpandedNodes = expanded;
                    Diagnose(grid, s, goalIndex, options.AllowVertical, result);
                    result.Reason = $"Vượt giới hạn {budget:N0} ô mở rộng — thu hẹp hộp tìm kiếm, tăng bước lưới, hoặc đặt maxExpandedNodes cao hơn."
                        + (result.GoalConnected
                            ? " Hai điểm CÓ nối thông nhau (flood-fill), nên đây là hết ngân sách chứ không phải bị chặn."
                            : $" Hai điểm KHÔNG nối thông nhau: điểm đầu chỉ ra tới {result.ReachableCells:N0} ô trống — tuyến không tồn tại, tăng ngân sách cũng vô ích.")
                        + VerticalHint(options, size[2]);
                    return result;
                }

                if (cell[0] == g[0] && cell[1] == g[1] && cell[2] == g[2])
                {
                    Reconstruct(currentKey, visited, grid, searchBounds, step, result);
                    result.Found = true;
                    result.ExpandedNodes = expanded;
                    return result;
                }

                var currentG = visited[currentKey].G;
                for (var d = 0; d < Directions.Length; d++)
                {
                    if (!options.AllowVertical && Directions[d][2] != 0)
                    {
                        continue;
                    }

                    var next = new[] { cell[0] + Directions[d][0], cell[1] + Directions[d][1], cell[2] + Directions[d][2] };
                    if (!InBounds(next, size))
                    {
                        continue;
                    }

                    var nextIndex = grid.Index(next);
                    if (grid.IsBlocked(nextIndex))
                    {
                        continue;
                    }

                    var cost = 1.0;
                    if (dir >= 0 && dir != d)
                    {
                        cost += options.TurnPenalty;
                    }

                    if (options.NearObstaclePenalty > 0 && grid.IsNear(nextIndex))
                    {
                        cost += options.NearObstaclePenalty;
                    }

                    var nextKey = Key(nextIndex, d);
                    var tentative = currentG + cost;
                    if (visited.TryGetValue(nextKey, out var existing) && existing.G <= tentative)
                    {
                        continue;
                    }

                    visited[nextKey] = (tentative, currentKey);
                    open.Enqueue(nextKey, tentative + Heuristic(next, d, g, options.TurnPenalty));
                }
            }

            result.ExpandedNodes = expanded;
            Diagnose(grid, s, goalIndex, options.AllowVertical, result);
            result.Reason = $"Không có đường đi trong hộp tìm kiếm: điểm đầu chỉ ra tới {result.ReachableCells:N0} / {cells:N0} ô — "
                + "nới `searchMarginMm`, hoặc chọn hai điểm trong cùng không gian trần kỹ thuật.";
            return result;
        }

        /// <summary>
        /// Số lớp cao độ là hệ số nhân của không gian tìm kiếm: A* trên lưới phải "tràn" qua mọi ô có chi phí
        /// ước lượng thấp hơn tuyến tối ưu, và mỗi lớp Z thêm vào là tràn thêm chừng ấy lần. Đo trên hộp
        /// 30 × 30 m ba tường so le: một lớp thì 460.000 node xong trong 0,4 s; 61 lớp thì 20 triệu node,
        /// 30 s, 1,8 GB vẫn chưa xong. Khi hết ngân sách mà đang cho đi dọc Z, nói thẳng đây là đòn bẩy lớn nhất.
        /// </summary>
        private static string VerticalHint(PathFinderOptions options, int zLayers)
            => options.AllowVertical && zLayers > 1
                ? $" Lưới đang có {zLayers} lớp cao độ — tuyến nằm ngang thì tắt allowVertical, hoặc thu searchMarginZMm về đúng khoảng trần kỹ thuật."
                : string.Empty;

        /// <summary>
        /// Flood-fill 6 hướng từ điểm đầu trên đúng lưới đã raster hoá — bỏ qua phạt rẽ và mọi ngân sách.
        /// Trả lời câu hỏi duy nhất người dùng cần khi thất bại: tuyến có tồn tại không, hay chỉ là hết giờ.
        /// </summary>
        private static void Diagnose(OccupancyGrid grid, int[] start, int goalIndex, bool allowVertical, PathResult result)
        {
            var seen = new bool[grid.Count];
            var stack = new Stack<int[]>();
            seen[grid.Index(start)] = true;
            stack.Push(start);
            var count = 1;

            while (stack.Count > 0)
            {
                var cell = stack.Pop();
                for (var d = 0; d < Directions.Length; d++)
                {
                    if (!allowVertical && Directions[d][2] != 0)
                    {
                        continue;
                    }

                    var next = new[] { cell[0] + Directions[d][0], cell[1] + Directions[d][1], cell[2] + Directions[d][2] };
                    if (!InBounds(next, grid.Size))
                    {
                        continue;
                    }

                    var index = grid.Index(next);
                    if (seen[index] || grid.IsBlocked(index))
                    {
                        continue;
                    }

                    seen[index] = true;
                    count++;
                    stack.Push(next);
                }
            }

            result.ReachableCells = count;
            result.GoalConnected = seen[goalIndex];
        }

        private static void Reconstruct(long key, Dictionary<long, (double G, long Prev)> visited, OccupancyGrid grid, Box3 b, double step, PathResult result)
        {
            var cells = new List<int[]>();
            for (var k = key; k >= 0; k = visited[k].Prev)
            {
                cells.Add(grid.FromIndex((int)(k / 7)));
            }

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

        /// <summary>
        /// Manhattan CỘNG số lần rẽ bắt buộc × phạt rẽ. Vẫn là chặn dưới của chi phí thật (còn ngần ấy trục
        /// phải đi và ít nhất ngần ấy chỗ phải đổi hướng) nên A* giữ nguyên tính tối ưu — nhưng với
        /// <c>TurnPenalty = 20</c> thì Manhattan trần trụi bỏ sót phần lớn chi phí và A* thoái hoá gần thành
        /// Dijkstra, chính là lý do lưới 100 mm chạm trần 400.000 ô trên model thật.
        /// </summary>
        private static double Heuristic(int[] a, int dir, int[] goal, double turnPenalty)
        {
            var dx = goal[0] - a[0];
            var dy = goal[1] - a[1];
            var dz = goal[2] - a[2];
            var distance = (double)(Math.Abs(dx) + Math.Abs(dy) + Math.Abs(dz));
            if (turnPenalty <= 0 || distance == 0)
            {
                return distance;
            }

            var axes = (dx != 0 ? 1 : 0) + (dy != 0 ? 1 : 0) + (dz != 0 ? 1 : 0);
            var turns = axes - 1; // đi hết một trục rồi sang trục kế thì tốn một lần rẽ.
            if (dir >= 0)
            {
                // Hướng đang đi trùng một trục còn phải đi và đúng chiều thì không tốn thêm lần rẽ nào.
                var delta = Directions[dir][0] != 0 ? dx : Directions[dir][1] != 0 ? dy : dz;
                var sign = Directions[dir][0] + Directions[dir][1] + Directions[dir][2];
                if (Math.Sign(delta) != sign)
                {
                    turns++;
                }
            }

            return distance + (turns * turnPenalty);
        }

        private static bool InBounds(int[] c, int[] size) => c[0] >= 0 && c[1] >= 0 && c[2] >= 0 && c[0] < size[0] && c[1] < size[1] && c[2] < size[2];

        private static int[] ToCell(Point3 p, Box3 b, double step) => new[]
        {
            (int)Math.Round((p.X - b.MinX) / step),
            (int)Math.Round((p.Y - b.MinY) / step),
            (int)Math.Round((p.Z - b.MinZ) / step),
        };

        private static Point3 FromCell(int[] c, Box3 b, double step) => new Point3(b.MinX + c[0] * step, b.MinY + c[1] * step, b.MinZ + c[2] * step);

        /// <summary>
        /// Lưới bit "ô này có bị chặn không". Raster hoá từng hộp chướng ngại vào dải ô của nó (chi phí tỉ lệ
        /// với thể tích vật cản) thay vì hỏi từng ô xem có nằm trong hộp nào không (chi phí ô × vật cản).
        /// Điều kiện chặn giữ nguyên định nghĩa cũ: TÂM ô nằm trong hộp đã nới ra <c>clearance</c>.
        /// </summary>
        private sealed class OccupancyGrid
        {
            private readonly bool[] _blocked;
            private readonly bool[]? _near;
            private readonly int _strideY;
            private readonly int _strideX;

            public OccupancyGrid(Box3 bounds, int[] size, double step, IReadOnlyList<Box3> obstacles, double clearance, double? nearClearance)
            {
                Size = size;
                Count = size[0] * size[1] * size[2];
                _strideY = size[2];
                _strideX = size[1] * size[2];
                _blocked = new bool[Count];
                _near = nearClearance.HasValue ? new bool[Count] : null;

                for (var i = 0; i < obstacles.Count; i++)
                {
                    Rasterize(_blocked, obstacles[i], bounds, size, step, clearance);
                    if (_near != null)
                    {
                        Rasterize(_near, obstacles[i], bounds, size, step, nearClearance!.Value);
                    }
                }
            }

            public int[] Size { get; }

            public int Count { get; }

            public int Index(int[] c) => (c[0] * _strideX) + (c[1] * _strideY) + c[2];

            /// <summary>Nghịch đảo của <see cref="Index"/>.</summary>
            public int[] FromIndex(int index)
            {
                var x = index / _strideX;
                var rest = index - (x * _strideX);
                return new[] { x, rest / _strideY, rest % _strideY };
            }

            public bool IsBlocked(int index) => _blocked[index];

            public bool IsNear(int index) => _near != null && _near[index];

            private void Rasterize(bool[] target, Box3 box, Box3 bounds, int[] size, double step, double clearance)
            {
                // Tâm ô i theo trục X là bounds.MinX + i*step; ô bị phủ khi tâm rơi vào [Min-clr, Max+clr].
                var x0 = Lower(box.MinX - clearance, bounds.MinX, step);
                var x1 = Upper(box.MaxX + clearance, bounds.MinX, step, size[0]);
                if (x0 > x1) return;
                var y0 = Lower(box.MinY - clearance, bounds.MinY, step);
                var y1 = Upper(box.MaxY + clearance, bounds.MinY, step, size[1]);
                if (y0 > y1) return;
                var z0 = Lower(box.MinZ - clearance, bounds.MinZ, step);
                var z1 = Upper(box.MaxZ + clearance, bounds.MinZ, step, size[2]);
                if (z0 > z1) return;

                for (var x = x0; x <= x1; x++)
                {
                    var bx = x * _strideX;
                    for (var y = y0; y <= y1; y++)
                    {
                        var row = bx + (y * _strideY);
                        for (var z = z0; z <= z1; z++)
                        {
                            target[row + z] = true;
                        }
                    }
                }
            }

            // Nới 1e-9 để biên đúng bằng tâm ô không rơi ra ngoài vì sai số dấu phẩy động.
            private static int Lower(double value, double origin, double step)
                => Math.Max(0, (int)Math.Ceiling(((value - origin) / step) - 1e-9));

            private static int Upper(double value, double origin, double step, int count)
                => Math.Min(count - 1, (int)Math.Floor(((value - origin) / step) + 1e-9));
        }

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
