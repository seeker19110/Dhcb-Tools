using System;
using System.Collections.Generic;

namespace DhcbTools.Shared.Logic.Mep
{
    /// <summary>
    /// Trừ lỗ mở khỏi hộp chướng ngại — routing mức D. Tường/sàn vẫn là hộp bao (mức C), nhưng mỗi lỗ mở
    /// (shaft, opening, lỗ chờ) đục đi một hộp con, phần còn lại được cắt thành tối đa 6 hộp quanh lỗ.
    /// <para>
    /// Làm ở tầng hộp chứ không ở tầng lưới để <see cref="PathFinder3D"/> không phải biết gì về lỗ mở:
    /// bộ tìm đường vẫn chỉ nhận danh sách <see cref="Box3"/>. Khoảng hở (clearance) vì thế tự áp lên mép
    /// lỗ — tuyến giữ đúng khoảng hở với mép lỗ như với mọi cạnh vật cản khác, tức là lỗ phải rộng hơn
    /// 2 × clearance mới đi lọt. Đó là điều đúng về mặt thi công.
    /// </para>
    /// </summary>
    public static class BoxSubtract
    {
        /// <summary>
        /// <paramref name="box"/> trừ đi mọi <paramref name="holes"/>. Lỗ không giao với hộp thì bỏ qua; lỗ nuốt
        /// trọn hộp thì trả về rỗng. Kết quả là các hộp rời nhau (không chồng), phủ đúng phần còn lại.
        /// </summary>
        public static List<Box3> Minus(Box3 box, IReadOnlyList<Box3> holes)
        {
            if (box == null)
            {
                throw new ArgumentNullException(nameof(box));
            }

            var pieces = new List<Box3> { box };
            if (holes == null || holes.Count == 0)
            {
                return pieces;
            }

            foreach (var hole in holes)
            {
                var next = new List<Box3>(pieces.Count + 5);
                foreach (var piece in pieces)
                {
                    Cut(piece, hole, next);
                }

                pieces = next;
            }

            return pieces;
        }

        /// <summary>Có giao nhau với thể tích dương không (chạm mép không tính).</summary>
        public static bool Overlaps(Box3 a, Box3 b)
            => a.MinX < b.MaxX && b.MinX < a.MaxX
            && a.MinY < b.MaxY && b.MinY < a.MaxY
            && a.MinZ < b.MaxZ && b.MinZ < a.MaxZ;

        /// <summary>Cắt <paramref name="piece"/> theo <paramref name="hole"/>: phần ngoài lỗ đưa vào <paramref name="output"/>.</summary>
        private static void Cut(Box3 piece, Box3 hole, List<Box3> output)
        {
            if (!Overlaps(piece, hole))
            {
                output.Add(piece);
                return;
            }

            // Sáu lát quanh lỗ, theo thứ tự X → Y → Z, mỗi lát lấy trọn chiều còn lại của phần chưa cắt.
            var x0 = Math.Max(piece.MinX, hole.MinX);
            var x1 = Math.Min(piece.MaxX, hole.MaxX);
            var y0 = Math.Max(piece.MinY, hole.MinY);
            var y1 = Math.Min(piece.MaxY, hole.MaxY);
            var z0 = Math.Max(piece.MinZ, hole.MinZ);
            var z1 = Math.Min(piece.MaxZ, hole.MaxZ);

            Emit(output, piece.MinX, piece.MinY, piece.MinZ, x0, piece.MaxY, piece.MaxZ);   // bên trái lỗ (X nhỏ)
            Emit(output, x1, piece.MinY, piece.MinZ, piece.MaxX, piece.MaxY, piece.MaxZ);   // bên phải lỗ (X lớn)
            Emit(output, x0, piece.MinY, piece.MinZ, x1, y0, piece.MaxZ);                   // trước lỗ (Y nhỏ)
            Emit(output, x0, y1, piece.MinZ, x1, piece.MaxY, piece.MaxZ);                   // sau lỗ (Y lớn)
            Emit(output, x0, y0, piece.MinZ, x1, y1, z0);                                   // dưới lỗ
            Emit(output, x0, y0, z1, x1, y1, piece.MaxZ);                                   // trên lỗ
        }

        private static void Emit(List<Box3> output, double minX, double minY, double minZ, double maxX, double maxY, double maxZ)
        {
            if (maxX - minX > 1e-9 && maxY - minY > 1e-9 && maxZ - minZ > 1e-9)
            {
                output.Add(new Box3(minX, minY, minZ, maxX, maxY, maxZ));
            }
        }
    }
}
