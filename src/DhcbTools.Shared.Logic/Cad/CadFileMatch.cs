using System;
using System.IO;

namespace DhcbTools.Shared.Logic.Cad
{
    /// <summary>
    /// So tên bản vẽ CAD đã có trong mô hình với file sắp link. Tách riêng khỏi lệnh Revit để test được
    /// trên CI: đây là chỗ một dòng sai làm lệnh <b>im lặng không làm gì</b>, chứ không ném lỗi nào.
    /// </summary>
    public static class CadFileMatch
    {
        /// <summary>
        /// Hai tên có phải cùng một bản vẽ không.
        /// <para>
        /// So theo <b>tên file</b> chứ không theo đường dẫn: cùng một bản vẽ chép sang thư mục khác vẫn là
        /// cùng bản vẽ, link hai lần là hai bộ hình học chồng nhau. Nhưng so <b>đúng cả tên, kể cả đuôi mở
        /// rộng</b>: <c>tuyen-ong.dwg</c> và <c>tuyen-ong.dxf</c> là hai bản vẽ khác nhau, và "có chứa" thì
        /// còn sai cả chiều ngược lại (<c>tuyen-ong-giua.dxf</c> chứa <c>tuyen-ong</c>).
        /// </para>
        /// <para>
        /// <paramref name="allowMissingExtension"/> chỉ bật cho tên <b>không lấy được từ đường dẫn file</b>
        /// — bản vẽ được <i>import</i> chứ không link thì Revit chỉ còn tên element kiểu, mà tên đó mất đuôi.
        /// Ở đó thà nhận nhầm <c>.dwg</c> với <c>.dxf</c> cùng tên còn hơn link chồng lên bản vẽ đã có.
        /// Bật nó cho cả bản vẽ link chính là lỗi tìm ra 2026-09-05: lệnh báo "đã có" cho một file chưa
        /// bao giờ vào mô hình (xem <c>docs/bang-chung-test.md</c> §31).
        /// </para>
        /// </summary>
        public static bool SameDrawing(string? existingName, string? candidatePath, bool allowMissingExtension = false)
        {
            var existing = (existingName ?? string.Empty).Trim();
            var candidate = Path.GetFileName((candidatePath ?? string.Empty).Trim());
            if (existing.Length == 0 || candidate.Length == 0)
            {
                return false;
            }

            if (string.Equals(existing, candidate, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (!allowMissingExtension || Path.HasExtension(existing))
            {
                return false;
            }

            var stem = Path.GetFileNameWithoutExtension(candidate);
            return stem.Length > 0 && string.Equals(existing, stem, StringComparison.OrdinalIgnoreCase);
        }
    }
}
