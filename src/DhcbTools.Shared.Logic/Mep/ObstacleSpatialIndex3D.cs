using System;
using System.Collections.Generic;

namespace DhcbTools.Shared.Logic.Mep
{
    /// <summary>
    /// Chỉ mục không gian 3D (Spatial Hash Grid) cho tập hợp các hình hộp chướng ngại <see cref="Box3"/>.
    /// Tối ưu thời gian kiểm tra va chạm từ O(N) xuống O(1) trung bình cho mỗi nút mở rộng của A*.
    /// </summary>
    public sealed class ObstacleSpatialIndex3D
    {
        private readonly double _cellSize;
        private readonly Dictionary<(int X, int Y, int Z), List<int>> _grid = new Dictionary<(int X, int Y, int Z), List<int>>();
        private readonly IReadOnlyList<Box3> _obstacles;

        public ObstacleSpatialIndex3D(IReadOnlyList<Box3> obstacles, double cellSizeMm = 500)
        {
            _obstacles = obstacles ?? throw new ArgumentNullException(nameof(obstacles));
            _cellSize = Math.Max(cellSizeMm, 50);

            for (int i = 0; i < _obstacles.Count; i++)
            {
                var box = _obstacles[i];
                int minX = (int)Math.Floor(box.MinX / _cellSize);
                int maxX = (int)Math.Floor(box.MaxX / _cellSize);
                int minY = (int)Math.Floor(box.MinY / _cellSize);
                int maxY = (int)Math.Floor(box.MaxY / _cellSize);
                int minZ = (int)Math.Floor(box.MinZ / _cellSize);
                int maxZ = (int)Math.Floor(box.MaxZ / _cellSize);

                for (int x = minX; x <= maxX; x++)
                {
                    for (int y = minY; y <= maxY; y++)
                    {
                        for (int z = minZ; z <= maxZ; z++)
                        {
                            var key = (x, y, z);
                            if (!_grid.TryGetValue(key, out var list))
                            {
                                list = new List<int>();
                                _grid[key] = list;
                            }
                            list.Add(i);
                        }
                    }
                }
            }
        }

        /// <summary>Số lượng chướng ngại vật trong chỉ mục.</summary>
        public int Count => _obstacles.Count;

        /// <summary>
        /// Kiểm tra một điểm (x, y, z) có nằm trong bất kỳ chướng ngại nào (tính cả clearance) hay không.
        /// </summary>
        public bool IsBlocked(double x, double y, double z, double clearance = 0)
        {
            int gx = (int)Math.Floor(x / _cellSize);
            int gy = (int)Math.Floor(y / _cellSize);
            int gz = (int)Math.Floor(z / _cellSize);

            var key = (gx, gy, gz);
            if (!_grid.TryGetValue(key, out var indices))
                return false;

            for (int i = 0; i < indices.Count; i++)
            {
                if (_obstacles[indices[i]].Contains(x, y, z, clearance))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Lấy tất cả chỉ số chướng ngại vật có khả năng va chạm gần vùng (x, y, z) với bán kính clearance.
        /// </summary>
        public HashSet<int> GetCandidateIndices(double x, double y, double z, double clearance = 0)
        {
            var result = new HashSet<int>();
            int minX = (int)Math.Floor((x - clearance) / _cellSize);
            int maxX = (int)Math.Floor((x + clearance) / _cellSize);
            int minY = (int)Math.Floor((y - clearance) / _cellSize);
            int maxY = (int)Math.Floor((y + clearance) / _cellSize);
            int minZ = (int)Math.Floor((z - clearance) / _cellSize);
            int maxZ = (int)Math.Floor((z + clearance) / _cellSize);

            for (int cx = minX; cx <= maxX; cx++)
            {
                for (int cy = minY; cy <= maxY; cy++)
                {
                    for (int cz = minZ; cz <= maxZ; cz++)
                    {
                        if (_grid.TryGetValue((cx, cy, cz), out var list))
                        {
                            for (int i = 0; i < list.Count; i++)
                                result.Add(list[i]);
                        }
                    }
                }
            }
            return result;
        }
    }
}
