using System;
using System.Collections.Generic;
using System.Linq;

namespace DhcbTools.Shared.Logic.Setout
{
    /// <summary>
    /// Phần quyết định còn lại của lệnh <c>SetoutExport</c> ngoài <see cref="SetoutPlanner"/>/<see cref="SetoutCsv"/>:
    /// đọc config, chọn chiều transform Survey, chọn điểm trên đường, và toàn bộ Summary/Messages.
    /// <para>
    /// Vì sao tách: lệnh này đưa toạ độ ra hiện trường — sai một chữ số là đục lại bê tông — mà nửa
    /// logic của nó (validate, chọn chiều transform, lời cảnh báo) nằm trong Core, chỉ kiểm được khi mở
    /// Revit thật và vẫn mang nhãn 🧪 "chưa chạy thật". Đưa về đây để mỗi câu cảnh báo, mỗi nhánh chọn
    /// chiều có assert trên CI.
    /// </para>
    /// </summary>
    public static class SetoutExportLogic
    {
        /// <summary>Số Id tối đa liệt kê khi báo Id không có trong mô hình.</summary>
        public const int MaxListedIds = 20;

        /// <summary>Cảnh báo khi không chiều nào của GetTotalTransform khớp GetProjectPosition.</summary>
        public const string DirectionUnverifiedNote =
            "Không đối chiếu được chiều của GetTotalTransform với GetProjectPosition — toạ độ Survey có thể sai, kiểm lại một điểm bằng Spot Coordinate trước khi đưa ra hiện trường.";

        /// <summary>Cảnh báo khi hệ Survey trùng hệ nội bộ (mô hình chưa khai toạ độ chung).</summary>
        public const string SurveyEqualsInternalNote =
            "Hệ Survey trùng hệ nội bộ (mô hình chưa khai toạ độ chung) — toạ độ ra file là toạ độ Revit, không phải toạ độ khảo sát; đối chiếu với tổ trắc đạc trước khi dùng.";

        // ── Config ──────────────────────────────────────────────────────────────

        /// <summary>
        /// <c>curvePoints</c>: Ends (hai đầu — mặc định khi rỗng), Mid (điểm giữa), Both. Sai thì báo rõ, không đoán.
        /// </summary>
        public static bool TryParseCurvePoints(string? raw, out bool ends, out bool mid, out string error)
        {
            var text = (raw ?? "Ends").Trim();
            if (text.Length == 0)
            {
                text = "Ends";
            }

            error = string.Empty;
            if (text.Equals("Ends", StringComparison.OrdinalIgnoreCase))
            {
                ends = true;
                mid = false;
                return true;
            }

            if (text.Equals("Mid", StringComparison.OrdinalIgnoreCase))
            {
                ends = false;
                mid = true;
                return true;
            }

            if (text.Equals("Both", StringComparison.OrdinalIgnoreCase))
            {
                ends = true;
                mid = true;
                return true;
            }

            ends = false;
            mid = false;
            error = $"curvePoints \"{raw}\" không hợp lệ. Hợp lệ: Ends (hai đầu), Mid (điểm giữa), Both.";
            return false;
        }

        /// <summary><c>coordinateSystem</c>: Survey/Shared (toạ độ chung — mặc định khi rỗng) hoặc Internal.</summary>
        public static bool TryParseCoordinateSystem(string? raw, out bool useSurvey, out string error)
        {
            var text = (raw ?? "Survey").Trim();
            if (text.Length == 0)
            {
                text = "Survey";
            }

            error = string.Empty;
            useSurvey = text.Equals("Survey", StringComparison.OrdinalIgnoreCase) || text.Equals("Shared", StringComparison.OrdinalIgnoreCase);
            if (useSurvey || text.Equals("Internal", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            error = $"coordinateSystem \"{raw}\" không hợp lệ. Hợp lệ: Survey (toạ độ chung) hoặc Internal (gốc nội bộ).";
            return false;
        }

        /// <summary>
        /// Điểm chèn family và tâm hình học lệch nhau quá ngưỡng này (mm) thì coi là "cột lệch tâm": họ
        /// "Rectangular Column (Off Center)" của Snowdon lệch 100–305 mm — §52. Dưới ngưỡng là sai số làm tròn.
        /// </summary>
        public const double OffCentreToleranceMm = 1.0;

        /// <summary><c>pointMode</c>: Centre (tâm hình học — mặc định khi rỗng) hoặc Insertion (điểm chèn family).</summary>
        public static bool TryParsePointMode(string? raw, out bool useCentre, out string error)
        {
            var text = (raw ?? "Centre").Trim();
            if (text.Length == 0)
            {
                text = "Centre";
            }

            error = string.Empty;
            useCentre = text.Equals("Centre", StringComparison.OrdinalIgnoreCase) || text.Equals("Center", StringComparison.OrdinalIgnoreCase);
            if (useCentre || text.Equals("Insertion", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            error = $"pointMode \"{raw}\" không hợp lệ. Hợp lệ: Centre (tâm hình học) hoặc Insertion (điểm chèn family).";
            return false;
        }

        /// <summary>
        /// Điểm chèn (LocationPoint) có lệch tâm hình học (tâm hộp bao trên mặt bằng) không. Với họ cột
        /// "Off Center" điểm chèn nằm ở mép, mà thứ trắc đạc cắm là tim — xuất điểm chèn là cắm sai cột.
        /// </summary>
        public static bool IsOffCentre(double insertionXMm, double insertionYMm, double centreXMm, double centreYMm, out double offsetMm)
        {
            offsetMm = Math.Sqrt(Math.Pow(centreXMm - insertionXMm, 2) + Math.Pow(centreYMm - insertionYMm, 2));
            return offsetMm > OffCentreToleranceMm;
        }

        /// <summary>Dòng Messages về phần tử lệch tâm; null khi không có.</summary>
        public static string? OffCentreNote(int offCentre, double maxOffsetMm, bool useCentre)
        {
            if (offCentre <= 0)
            {
                return null;
            }

            return useCentre
                ? $"{offCentre} phần tử có điểm chèn family lệch tâm hình học (tối đa {maxOffsetMm:F0} mm) — CSV lấy TÂM HÌNH HỌC (tim thật). Muốn điểm chèn: pointMode: Insertion."
                : $"{offCentre} phần tử có điểm chèn family lệch tâm hình học (tối đa {maxOffsetMm:F0} mm) — CSV đang lấy ĐIỂM CHÈN theo pointMode: Insertion, không phải tim.";
        }

        /// <summary>
        /// Các điểm lấy trên một đường theo <c>curvePoints</c>: nhãn và tham số [0, 1] trên đường, đúng thứ tự
        /// đầu → giữa → cuối. Thứ tự này quyết định số thứ tự {n} trong tên điểm, đổi là đổi tên trên máy.
        /// </summary>
        public static List<(string Kind, double T)> CurveAnchorKinds(bool ends, bool mid)
        {
            var kinds = new List<(string, double)>();
            if (ends)
            {
                kinds.Add(("đầu", 0.0));
            }

            if (mid)
            {
                kinds.Add(("giữa", 0.5));
            }

            if (ends)
            {
                kinds.Add(("cuối", 1.0));
            }

            return kinds;
        }

        // ── Hệ toạ độ ───────────────────────────────────────────────────────────

        /// <summary>
        /// Chọn ứng viên transform đầu tiên đưa MỌI điểm dò về đúng vị trí mong đợi (trong dung sai).
        /// Đây là cách tự kiểm chiều của <c>GetTotalTransform()</c> bằng <c>GetProjectPosition</c> thay vì
        /// tin vào trí nhớ về API. Trả -1 khi không ứng viên nào khớp — người gọi phải cảnh báo.
        /// </summary>
        public static int ChooseMatchingTransform(
            IReadOnlyList<Func<(double X, double Y, double Z), (double X, double Y, double Z)>> candidates,
            IReadOnlyList<(double X, double Y, double Z)> probes,
            IReadOnlyList<(double X, double Y, double Z)> expected,
            double tolerance)
        {
            if (candidates == null)
            {
                throw new ArgumentNullException(nameof(candidates));
            }

            if (probes == null || expected == null || probes.Count != expected.Count)
            {
                throw new ArgumentException("Số điểm dò và số điểm mong đợi phải bằng nhau.", nameof(expected));
            }

            for (var c = 0; c < candidates.Count; c++)
            {
                var ok = true;
                for (var i = 0; i < probes.Count; i++)
                {
                    var p = candidates[c](probes[i]);
                    var dx = p.X - expected[i].X;
                    var dy = p.Y - expected[i].Y;
                    var dz = p.Z - expected[i].Z;
                    if (Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz)) > tolerance)
                    {
                        ok = false;
                        break;
                    }
                }

                if (ok)
                {
                    return c;
                }
            }

            return -1;
        }

        /// <summary>
        /// Dòng Messages về site: gốc nội bộ ở đâu trong hệ Survey, True North xoay bao nhiêu; kèm cảnh báo
        /// khi hai hệ trùng nhau (mô hình chưa khai toạ độ chung) — trường hợp toạ độ ra file trông như
        /// toạ độ khảo sát nhưng không phải.
        /// </summary>
        public static List<string> SiteNotes(string? siteName, double eastMm, double northMm, double elevationMm, double angleDegrees)
        {
            var notes = new List<string>
            {
                $"Site \"{siteName}\": gốc nội bộ ở E={eastMm:F0} N={northMm:F0} Z={elevationMm:F0} mm, True North xoay {angleDegrees:F4}°.",
            };
            if (Math.Abs(eastMm) < 0.5 && Math.Abs(northMm) < 0.5 && Math.Abs(elevationMm) < 0.5 && Math.Abs(angleDegrees) < 1e-6)
            {
                notes.Add(SurveyEqualsInternalNote);
            }

            return notes;
        }

        // ── Thông báo ───────────────────────────────────────────────────────────

        /// <summary>Id người dùng đưa vào mà mô hình không có. Trả null khi không thiếu cái nào.</summary>
        public static string? MissingIdsNote(IReadOnlyList<long>? missing)
        {
            if (missing == null || missing.Count == 0)
            {
                return null;
            }

            return $"{missing.Count} Id không có trong mô hình: {string.Join(", ", missing.Take(MaxListedIds))}{(missing.Count > MaxListedIds ? ", …" : string.Empty)}.";
        }

        /// <summary>Lỗi khi tên category không có trong mô hình.</summary>
        public static string UnknownCategoriesError(IReadOnlyList<string> unknown)
            => "Category không có: " + string.Join(", ", unknown ?? Array.Empty<string>()) + ". Tra tên category có thật bằng query categories hoặc parameters_of.";

        /// <summary>Mô tả đầu vào cho precondition "không có gì để xuất".</summary>
        public static string InputDescription(bool byElementIds, bool includeGridIntersections)
            => byElementIds
                ? "phần tử theo elementIds"
                : "điểm định vị (phần tử theo bộ lọc" + (includeGridIntersections ? ", giao trục" : string.Empty) + ")";

        /// <summary>Chữ cái của cột theo cách máy gọi (P N E Z D C L I), để in lại thứ tự cột trong Summary.</summary>
        public static string ColumnLetters(IEnumerable<SetoutColumn> columns)
            => string.Concat((columns ?? Array.Empty<SetoutColumn>()).Select(LetterOf));

        /// <summary>Chữ cái một cột; "?" cho giá trị ngoài enum.</summary>
        public static string LetterOf(SetoutColumn column)
        {
            switch (column)
            {
                case SetoutColumn.Name: return "P";
                case SetoutColumn.North: return "N";
                case SetoutColumn.East: return "E";
                case SetoutColumn.Elevation: return "Z";
                case SetoutColumn.Description: return "D";
                case SetoutColumn.Code: return "C";
                case SetoutColumn.Level: return "L";
                case SetoutColumn.ElementId: return "I";
                default: return "?";
            }
        }

        /// <summary>Summary khi xuất thành công — mọi con số quan trọng ở đây vì báo cáo batch chỉ in Summary.</summary>
        public static string Summary(
            int pointCount, int elementPoints, int elementCount,
            bool includeGridIntersections, int intersections,
            string outputPath, bool useSurvey, bool metres, IEnumerable<SetoutColumn> columns)
        {
            return $"Xuất {pointCount} điểm định vị ({elementPoints} điểm của {elementCount} phần tử"
                   + (includeGridIntersections ? $", {intersections} giao trục" : string.Empty)
                   + $") → \"{outputPath}\" — hệ {(useSurvey ? "Survey" : "Internal")}, {(metres ? "m" : "mm")}, cột {ColumnLetters(columns)}.";
        }

        /// <summary>Các dòng Messages cuối: DXF, phần tử bị lọc, lấy tâm hộp bao, không hình học, trục cong.</summary>
        public static List<string> TrailingNotes(string? dxfPath, int filteredOut, int boxFallback, int noGeometry, int curvedGrids)
        {
            var notes = new List<string>();
            if (!StringGuard.IsBlank(dxfPath))
            {
                notes.Add($"DXF điểm: \"{dxfPath}\" (layer DHCB-<mã> và DHCB-<mã>-TEN).");
            }

            if (filteredOut > 0)
            {
                notes.Add($"{filteredOut} phần tử ngoài bộ lọc tầng/family/type.");
            }

            if (boxFallback > 0)
            {
                notes.Add($"{boxFallback} phần tử không có điểm/đường đặt (Location) nên lấy tâm hộp bao — kiểm lại trước khi cắm.");
            }

            if (noGeometry > 0)
            {
                notes.Add($"{noGeometry} phần tử không có hình học nào để lấy điểm, đã bỏ qua.");
            }

            if (curvedGrids > 0)
            {
                notes.Add($"{curvedGrids} trục cong bị bỏ qua khi tính giao trục (chỉ xét trục thẳng).");
            }

            return notes;
        }
    }
}
