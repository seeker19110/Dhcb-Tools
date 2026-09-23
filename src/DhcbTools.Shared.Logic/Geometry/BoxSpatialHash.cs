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
            ForEachCell(box, 0, key =>
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
            var hits = new SortedSet<int>();
            ForEachCell(box, tolerance, key =>
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

        private void ForEachCell(Box3 box, double tolerance, Action<long> visit)
        {
            var x0 = Cell(box.MinX - tolerance);
            var x1 = Cell(box.MaxX + tolerance);
            var y0 = Cell(box.MinY - tolerance);
            var y1 = Cell(box.MaxY + tolerance);
            var z0 = Cell(box.MinZ - tolerance);
            var z1 = Cell(box.MaxZ + tolerance);
            for (var x = x0; x <= x1; x++)
            {
                for (var y = y0; y <= y1; y++)
                {
                    for (var z = z0; z <= z1; z++)
                    {
                        visit(Key(x, y, z));
                    }
                }
            }
        }

        private long Cell(double v)
        {
            var c = Math.Floor(v / _cellSize);
            // Kẹp trong ±2^20 ô: hộp vô hạn/NaN không được biến vòng lặp ô thành vô tận.
            return double.IsNaN(c) ? 0 : (long)Math.Max(-(1L << 20), Math.Min(1L << 20, c));
        }

        private static long Key(long cx, long cy, long cz)
        {
            const long mask = (1L << 21) - 1;
            return ((cx & mask) << 42) | ((cy & mask) << 21) | (cz & mask);
        }
    }
}
