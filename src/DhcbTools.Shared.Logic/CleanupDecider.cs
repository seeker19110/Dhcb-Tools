using System;
using System.Collections.Generic;

namespace DhcbTools.Shared.Logic
{
    /// <summary>
    /// Bảng quyết định "có được xoá không" cho DrawingCleanup (mục 0.4, lỗi #6). Tách khỏi AutoCAD để test
    /// được: layer hiện hành, layer hệ thống ("0", "Defpoints"), linetype hệ thống ("Continuous",
    /// "ByLayer", "ByBlock") và mọi thứ còn đang được dùng đều KHÔNG bao giờ bị xoá.
    /// </summary>
    public static class CleanupDecider
    {
        private static readonly HashSet<string> SystemLayers = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "0", "Defpoints" };

        private static readonly HashSet<string> SystemLinetypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Continuous", "ByLayer", "ByBlock" };

        /// <summary>Layer hệ thống của AutoCAD — không bao giờ xoá.</summary>
        public static bool IsSystemLayer(string? name) => name != null && SystemLayers.Contains(name);

        /// <summary>Linetype hệ thống của AutoCAD — không bao giờ xoá.</summary>
        public static bool IsSystemLinetype(string? name) => name != null && SystemLinetypes.Contains(name);

        /// <summary>Text style hệ thống — "Standard" luôn tồn tại trong mọi DWG, xoá là hỏng file.</summary>
        public static bool IsSystemTextStyle(string? name) =>
            name != null && name.Equals("Standard", StringComparison.OrdinalIgnoreCase);

        /// <summary>Dimension style hệ thống — như trên.</summary>
        public static bool IsSystemDimStyle(string? name) =>
            name != null && name.Equals("Standard", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// RegApp hệ thống: mọi tên bắt đầu bằng "ACAD" hoặc "AcDb" là của chính Autodesk
        /// (ACAD, ACAD_MLEADERVER, AcadAnnotativeDecomposition, AcDbBlockRepETag…).
        /// Chạy thật trên AutoCAD 2026 (2026-09-03) cho thấy bản vẽ mẫu có AcadAnnoAV, AcadAnnoPO,
        /// AcadAnnotativeDecomposition đang "không dùng" — về lý thuyết purge được, nhưng đây là dữ liệu
        /// nội bộ của tính năng annotative nên để nguyên: cái đáng dọn là rác của add-in bên thứ ba
        /// (AVE_*, CONTENT*, AFM*, RAK…), không phải vài byte của Autodesk.
        /// </summary>
        public static bool IsSystemRegApp(string? name) =>
            name != null
            && (name.StartsWith("ACAD", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("AcDb", StringComparison.OrdinalIgnoreCase));

        /// <summary>Có nên xoá một layer/linetype không.</summary>
        /// <param name="name">Tên đối tượng.</param>
        /// <param name="isUsed">Đang được entity, layer definition, block definition hoặc xref tham chiếu.</param>
        /// <param name="isCurrent">Là layer hiện hành (CLAYER) / linetype hiện hành (CELTYPE).</param>
        /// <param name="isSystem">Là đối tượng hệ thống (xem <see cref="IsSystemLayer"/>, <see cref="IsSystemLinetype"/>).</param>
        /// <param name="keepPatterns">Chuỗi con trong tên (không phân biệt hoa thường) buộc giữ lại.</param>
        public static bool ShouldErase(string? name, bool isUsed, bool isCurrent, bool isSystem, IEnumerable<string>? keepPatterns = null)
        {
            if (string.IsNullOrEmpty(name) || isUsed || isCurrent || isSystem)
            {
                return false;
            }

            if (keepPatterns != null)
            {
                foreach (var pattern in keepPatterns)
                {
                    if (!string.IsNullOrEmpty(pattern) && name!.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return false;
                    }
                }
            }

            // Layer của xref có dạng "TenXref|TenLayer" — thuộc file khác, không được xoá ở đây.
            return name!.IndexOf('|') < 0;
        }
    }
}
