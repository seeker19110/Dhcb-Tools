using System;
using System.Collections.Generic;
using System.Linq;
using DhcbTools.Shared.Logic.Mep;

namespace DhcbTools.Shared.Logic.Geometry
{
    /// <summary>
    /// Băm không gian đều (uniform grid) cho hộp bao: chèn N hộp một lần, rồi mỗi truy vấn "hộp nào chạm hộp
    /// này" chỉ xem các ô lưới hộp truy vấn phủ thay vì duyệt cả N. Dùng làm pha thô (broad phase) cho
    /// ClashDetection (A × B), SleeveAuto (ống × tường/sàn), ModelLinesFromCad (đường mới × đường đã có) —
    /// ba chỗ trước đây là vòng lặp n×m thuần: 50.000 ống × 20.000 tường = 10⁹ phép so hộp trên luồng UI.
    /// Đơn vị toạ độ tuỳ bên gọi (feet của Revit hay mm) — chỉ cần <c>cellSize</c> cùng đơn vị.
    /// </summary>
    public sealed class BoxSpatialHash<T>
    {
        // Cap work per box, including degenerate/extreme model bounds. Oversized boxes retain
        // exact intersection behavior through the overflow list instead of expanding millions of cells.
        private const long MaxCellsPerBox = 4096;
        private const long MinCell = -(1L << 20);
        private const long MaxCell = (1L << 20) - 1;
        private readonly List<int> _overflow = new List<int>();
        private readonly Dictionary<long, List<int>> _cells = new Dictionary<long, List<int>>();
        private readonly List<(Box3 Box, T Item)> _items = new List<(Box3, T)>();
        private readonly double _cellSize;

        /// <param name="cellSize">Cạnh ô lưới, cùng đơn vị với hộp. Xem <see cref="SuggestCellSize"/>.</param>
        public BoxSpatialHash(double cellSize)
        {
            if (double.IsNaN(cellSize) || double.IsInfinity(cellSize) || cellSize <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(cellSize), "Cạnh ô lưới phải là số dương hữu hạn.");
            }

            _cellSize = cellSize;
        }

        /// <summary>Số hộp đã chèn.</summary>
        public int Count => _items.Count;

        /// <summary>Cạnh ô lưới đang dùng.</summary>
        public double CellSize => _cellSize;

        /// <summary>
        /// Chọn cạnh ô theo cỡ hộp điển hình (trung vị cạnh lớn nhất của các hộp): ô quá nhỏ thì một hộp phủ
        /// hàng trăm ô, ô quá lớn thì mỗi ô lại chứa cả nghìn hộp. Rỗng → <paramref name="fallback"/>.
        /// </summary>
        public static double SuggestCellSize(IEnumerable<Box3> boxes, double fallback = 1.0)
        {
            var extents = boxes.Select(b => Math.Max(Math.Max(b.MaxX - b.MinX, b.MaxY - b.MinY), b.MaxZ - b.MinZ))
                .Where(e => e > 0 && !double.IsInfinity(e))
                .OrderBy(e => e)
                .ToList();
            if (extents.Count == 0)
            {
                return fallback;
            }

            var median = extents[extents.Count / 2];
            return Math.Max(median, fallback * 1e-6);
        }

        /// <summary>Dựng chỉ mục từ một danh sách; phần tử không có hộp (selector trả null) bị bỏ qua.</summary>
        public static BoxSpatialHash<T> Build(IEnumerable<T> items, Func<T, Box3?> boxOf, double? cellSize = null)
        {
            var list = items.Select(i => (Item: i, Box: boxOf(i))).Where(p => p.Box != null).ToList();
            var index = new BoxSpatialHash<T>(cellSize ?? SuggestCellSize(list.Select(p => p.Box!)));
            foreach (var pair in list)
            {
                index.Insert(pair.Box!, pair.Item);
            }

            return index;
        }

        /// <summary>Chèn một hộp. Hộp suy biến (điểm) cũng chèn được.</summary>
        public void Insert(Box3 box, T item)
        {
            if (box == null) throw new ArgumentNullException(nameof(box));
            var id = _items.Count;
            _items.Add((box, item));
            if (!TryCellRange(box, 0, out var range))
            {
                _overflow.Add(id);
                return;
            }

            ForEachCell(range, key =>
            {
                if (!_cells.TryGetValue(key, out var bucket))
                {
                    bucket = new List<int>();
                    _cells[key] = bucket;
                }

                bucket.Add(id);
            });
        }

        /// <summary>Chèn một điểm (hộp suy biến).</summary>
        public void InsertPoint(double x, double y, double z, T item) => Insert(new Box3(x, y, z, x, y, z), item);

        /// <summary>
        /// Mọi phần tử có hộp CHẠM hộp truy vấn (nới thêm <paramref name="tolerance"/> mỗi phía). Mỗi phần tử
        /// trả đúng một lần dù phủ nhiều ô; thứ tự theo thứ tự chèn để kết quả tái lập được.
        /// </summary>
        public List<T> Query(Box3 box, double tolerance = 0)
        {
            if (box == null) throw new ArgumentNullException(nameof(box));
            if (!TryCellRange(box, tolerance, out var range))
            {
                // A broad query costs O(N), independent of its coordinate volume. Keep the exact
                // predicate even for infinity, NaN or a tolerance which reverses the expanded bounds.
                var linear = new List<T>();
                foreach (var entry in _items)
                {
                    if (Intersects(entry.Box, box, tolerance)) linear.Add(entry.Item);
                }

                return linear;
            }

            var hits = new SortedSet<int>();
            foreach (var id in _overflow)
            {
                if (Intersects(_items[id].Box, box, tolerance)) hits.Add(id);
            }

            ForEachCell(range, key =>
            {
                if (_cells.TryGetValue(key, out var bucket))
                {
                    foreach (var id in bucket)
                    {
                        if (Intersects(_items[id].Box, box, tolerance))
                        {
                            hits.Add(id);
                        }
                    }
                }
            });

            var result = new List<T>(hits.Count);
            foreach (var id in hits)
            {
                result.Add(_items[id].Item);
            }

            return result;
        }

        /// <summary>Mọi phần tử có hộp cách điểm ≤ <paramref name="tolerance"/> (theo từng trục).</summary>
        public List<T> QueryPoint(double x, double y, double z, double tolerance) =>
            Query(new Box3(x, y, z, x, y, z), tolerance);

        internal static bool Intersects(Box3 a, Box3 b, double tol) =>
            a.MinX <= b.MaxX + tol && a.MaxX >= b.MinX - tol
            && a.MinY <= b.MaxY + tol && a.MaxY >= b.MinY - tol
            && a.MinZ <= b.MaxZ + tol && a.MaxZ >= b.MinZ - tol;

        private bool TryCellRange(Box3 box, double tolerance,
            out (long X0, long X1, long Y0, long Y1, long Z0, long Z1) range)
        {
            range = default;
            if (!TryCell(box.MinX - tolerance, out var x0) || !TryCell(box.MaxX + tolerance, out var x1)
                || !TryCell(box.MinY - tolerance, out var y0) || !TryCell(box.MaxY + tolerance, out var y1)
                || !TryCell(box.MinZ - tolerance, out var z0) || !TryCell(box.MaxZ + tolerance, out var z1)
                || x1 < x0 || y1 < y0 || z1 < z0)
            {
                return false;
            }

            var nx = x1 - x0 + 1;
            var ny = y1 - y0 + 1;
            var nz = z1 - z0 + 1;
            // Division avoids overflow in nx * ny * nz, and includes both boundary cells.
            if (nx > MaxCellsPerBox / ny || nx * ny > MaxCellsPerBox / nz)
            {
                return false;
            }

            range = (x0, x1, y0, y1, z0, z1);
            return true;
        }

        private static void ForEachCell((long X0, long X1, long Y0, long Y1, long Z0, long Z1) range,
            Action<long> visit)
        {
            for (var x = range.X0; x <= range.X1; x++)
            {
                for (var y = range.Y0; y <= range.Y1; y++)
                {
                    for (var z = range.Z0; z <= range.Z1; z++)
                    {
                        visit(Key(x, y, z));
                    }
                }
            }
        }

        private bool TryCell(double value, out long cell)
        {
            var coordinate = Math.Floor(value / _cellSize);
            cell = 0;
            // Never clamp coordinates: unrepresentable/infinite boxes need the exact fallback.
            // The range also prevents collisions when packing each signed coordinate into 21 bits.
            if (double.IsNaN(coordinate) || double.IsInfinity(coordinate)
                || coordinate < MinCell || coordinate > MaxCell)
            {
                return false;
            }

            cell = (long)coordinate;
            return true;
        }

        private static long Key(long cx, long cy, long cz)
        {
            const long mask = (1L << 21) - 1;
            return ((cx & mask) << 42) | ((cy & mask) << 21) | (cz & mask);
        }
    }
}
