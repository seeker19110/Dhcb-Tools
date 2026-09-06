using System;
using System.Collections.Generic;
using System.Linq;

namespace DhcbTools.Shared.Logic.Mep
{
    /// <summary>
    /// Phần quyết định thuần của lệnh <c>AutoRoute</c> (tách 2026-09-06, §56, cùng cách §49–§51): hộp tìm kiếm,
    /// hộp bao qua tám đỉnh đã biến đổi, lỗ mở nào được đục, và mọi câu chữ trong Summary/Messages. Phần Revit
    /// (thu thập phần tử, transform link, vẽ model line) ở lại <c>Core</c>. Chuỗi giữ nguyên từng ký tự so với
    /// bản trước khi tách — bộ <c>autoroute</c> (13 ca) chốt bằng <c>summaryContains</c>.
    /// </summary>
    public static class AutoRoutePlanner
    {
        /// <summary>Số dòng lỗ mở tối đa in vào Messages.</summary>
        public const int MaxOpeningLines = 30;

        /// <summary>Hộp tìm kiếm: bao hai điểm, nới <paramref name="marginXy"/> theo mặt bằng và <paramref name="marginZ"/> theo cao độ.</summary>
        public static Box3 SearchBounds(Point3 start, Point3 goal, double marginXy, double marginZ)
        {
            return new Box3(Math.Min(start.X, goal.X) - marginXy, Math.Min(start.Y, goal.Y) - marginXy, Math.Min(start.Z, goal.Z) - marginZ,
                            Math.Max(start.X, goal.X) + marginXy, Math.Max(start.Y, goal.Y) + marginXy, Math.Max(start.Z, goal.Z) + marginZ);
        }

        /// <summary>Tám đỉnh của một hộp — đầu vào cho phép biến đổi của link (xoay thì biến đổi min/max cho ra hộp sai).</summary>
        public static IEnumerable<Point3> Corners(double minX, double minY, double minZ, double maxX, double maxY, double maxZ)
        {
            for (var i = 0; i < 8; i++)
            {
                yield return new Point3((i & 1) == 0 ? minX : maxX, (i & 2) == 0 ? minY : maxY, (i & 4) == 0 ? minZ : maxZ);
            }
        }

        /// <summary>Hộp bao của một tập điểm (dùng sau khi biến đổi tám đỉnh).</summary>
        public static Box3 BoundsOf(IEnumerable<Point3> points)
        {
            double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;
            var any = false;
            foreach (var p in points)
            {
                any = true;
                minX = Math.Min(minX, p.X); maxX = Math.Max(maxX, p.X);
                minY = Math.Min(minY, p.Y); maxY = Math.Max(maxY, p.Y);
                minZ = Math.Min(minZ, p.Z); maxZ = Math.Max(maxZ, p.Z);
            }

            if (!any)
            {
                throw new ArgumentException("Không có điểm nào để lấy hộp bao.", nameof(points));
            }

            return new Box3(minX, minY, minZ, maxX, maxY, maxZ);
        }

        /// <summary>
        /// Insert của vật chủ có được coi là lỗ để đi qua không: cửa đi/cửa sổ chỉ khi bật; tường nhúng
        /// (curtain wall trong tường chủ) và liên kết kết cấu là vật đặc, không phải lỗ.
        /// </summary>
        public static bool IsPassableOpening(bool isDoorOrWindow, bool includeDoorsWindows, bool isEmbeddedWall, bool isStructuralConnection)
        {
            if (isDoorOrWindow && !includeDoorsWindows) return false;
            if (isEmbeddedWall || isStructuralConnection) return false;
            return true;
        }

        /// <summary>Lỗ có nằm trên phần vật chủ trong hộp tìm kiếm không — insert ở đầu kia bức tường dài không phải lỗ ở đây.</summary>
        public static bool HoleTouchesHost(Box3? hole, Box3 host) => hole != null && BoxSubtract.Overlaps(hole, host);

        public static string OpeningLine(string? insertCategory, Box3 hole, string? hostCategory, long hostId)
        {
            return $"{insertCategory ?? "?"} {hole.MaxX - hole.MinX:F0}×{hole.MaxY - hole.MinY:F0}×{hole.MaxZ - hole.MinZ:F0} "
                   + $"tại ({(hole.MinX + hole.MaxX) / 2:F0},{(hole.MinY + hole.MaxY) / 2:F0},{(hole.MinZ + hole.MaxZ) / 2:F0}) "
                   + $"trên {hostCategory ?? "?"} {hostId}";
        }

        /// <summary>Vật cản cuối cùng: có lỗ thì hộp bị đục thành vài mảnh, không thì nguyên hộp.</summary>
        public static List<Box3> ObstaclePieces(Box3 box, IReadOnlyList<Box3> holes)
        {
            return holes.Count > 0 ? BoxSubtract.Minus(box, holes) : new List<Box3> { box };
        }

        public static string LinkUnloadedLine(string linkName) => $"{linkName}: chưa nạp (unloaded) — bỏ qua";

        public static string LinkNoCategoryLine(string linkName) => $"{linkName}: không có category vật cản nào";

        public static string LinkCountLine(string linkName, int added) => $"{linkName}: {added} vật cản";

        /// <summary>Link có được xét không theo bộ lọc tên (rỗng = mọi link).</summary>
        public static bool LinkSelected(string linkName, IReadOnlyList<string> nameContains)
        {
            return nameContains.Count == 0 || nameContains.Any(f => linkName.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        public static string SourceText(int inDocument, int inLinks, bool respectOpenings, int openings)
        {
            return $"{inDocument} trong file + {inLinks} từ model liên kết"
                   + (respectOpenings ? $", {openings} lỗ mở đã đục" : ", mức C: không đục lỗ mở");
        }

        /// <summary>
        /// Thua: <c>path.Reason</c> đã nói rõ bị bịt kín hay hết ngân sách — hai thứ cần cách chữa khác hẳn nhau,
        /// nên không nuốt; kèm cỡ lưới để người đọc biết bước lưới có hợp với hộp không.
        /// </summary>
        public static string FailSummary(PathResult path, int elements, string source, double stepMm)
        {
            return $"Không tìm được tuyến ({path.Reason}). Đã xét {elements} chướng ngại ({source}), "
                   + $"lưới {path.GridCells:N0} ô bước {stepMm:F0} mm, mở rộng {path.ExpandedNodes:N0}/{path.MaxExpandedNodes:N0} node.";
        }

        public static string FoundMessage(PathResult path, int elements, string source, int segmentCount)
        {
            return $"{elements} chướng ngại trong hộp tìm kiếm ({source}), {path.ExpandedNodes} node, {path.Turns} lần rẽ, {segmentCount} đoạn, tổng {PolylineSimplifier.Length(path.Polyline) / 1000:F1} m.";
        }

        /// <summary>
        /// Tuyến đi qua một không gian TRỐNG là kết quả vô nghĩa nhưng trông y hệt kết quả tốt — nói ra ngay.
        /// Trả <c>null</c> khi có vật cản.
        /// </summary>
        public static string? NoObstacleWarning(int elements, bool includeLinkedModels)
        {
            if (elements > 0) return null;
            return includeLinkedModels
                ? "KHÔNG có vật cản nào trong hộp tìm kiếm, kể cả từ model liên kết — tuyến này chỉ là đường nối hai điểm. Kiểm lại link đã nạp chưa."
                : "KHÔNG có vật cản nào và includeLinkedModels đang tắt — bật lên nếu dầm/cột/tường nằm ở model liên kết.";
        }

        public static string SegmentLine(Point3 start, Point3 end)
        {
            return $"({start.X:F0},{start.Y:F0},{start.Z:F0}) → ({end.X:F0},{end.Y:F0},{end.Z:F0})";
        }

        /// <summary>Dòng chi tiết chung cho cả hai nhánh: từng link đóng góp bao nhiêu vật cản, và lỗ mở nào đã đục (tối đa 30 dòng).</summary>
        public static IEnumerable<string> ObstacleDetails(IReadOnlyList<string> linkSummary, IReadOnlyList<string> openingLog)
        {
            foreach (var line in linkSummary)
            {
                yield return "  Link — " + line;
            }

            foreach (var line in openingLog.Take(MaxOpeningLines))
            {
                yield return "  Lỗ mở — " + line;
            }

            if (openingLog.Count > MaxOpeningLines)
            {
                yield return $"  … và {openingLog.Count - MaxOpeningLines} lỗ mở nữa.";
            }
        }

        /// <summary>Số vật cản phải nằm trong Summary chứ không chỉ Messages: báo cáo batch chỉ in Summary.</summary>
        public static string PreviewSummary(int segmentCount, PathResult path, int elements, string source)
        {
            return $"[Xem trước] Tuyến {segmentCount} đoạn, {path.QualityText()}, né {elements} vật cản ({source}).";
        }

        public static string WrittenSummary(int created, string lineStyleName, PathResult path, int elements, string source)
        {
            return $"Đã vẽ {created} model line (line style \"{lineStyleName}\"), {path.QualityText()}, né {elements} vật cản ({source}).";
        }
    }
}
