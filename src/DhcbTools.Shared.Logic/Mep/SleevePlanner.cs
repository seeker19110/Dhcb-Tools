using System;
using System.Collections.Generic;
using System.Linq;

namespace DhcbTools.Shared.Logic.Mep
{
    /// <summary>
    /// Phần QUYẾT ĐỊNH của lệnh <c>SleeveAuto</c>, tách khỏi Revit để nằm trong cổng phủ 100%.
    /// <para>
    /// Vì sao: <c>Core/MEPF/SleeveCommand.cs</c> 855 dòng nhưng chỉ ~55 dòng chạm API Revit; phần còn
    /// lại (cắt tuyến với hộp, chọn cỡ sleeve, lọc category/host, gom lý do lỗi, viết Summary) là
    /// logic thuần bị giam trong Core — không test được trên CI, chỉ kiểm được khi mở Revit thật.
    /// Ba sự cố gần nhất (§38, §44, §48) đều rơi vào đúng nửa mã nguồn không phủ ấy. Lớp này nhận
    /// số và chuỗi, trả số và chuỗi; Core chỉ còn dịch <c>Element</c>/<c>XYZ</c> qua lại.
    /// </para>
    /// </summary>
    public static class SleevePlanner
    {
        /// <summary>Cỡ dùng tạm khi không tra được kích thước MEP: 6 inch = 0,5 ft. Chỉ dùng SAU KHI đã báo người dùng.</summary>
        public const double FallbackSizeFt = 0.5;

        /// <summary>Số loại lý do lỗi tối đa ghi vào Messages — quá số này thì lặp lại cùng câu, không thêm thông tin.</summary>
        public const int MaxFailureReasons = 5;

        /// <summary>Số id phần tử tối đa liệt kê trong cảnh báo không tra được kích thước.</summary>
        public const int MaxListedIds = 20;

        // ── Hình học ───────────────────────────────────────────────────────────

        /// <summary>
        /// Liang–Barsky: khoảng tham số [t0, t1] ⊂ [0, 1] của đoạn p0→p1 nằm trong hộp; false nếu không cắt.
        /// Dùng khi host không có solid để giao — bản trước luôn lấy trung điểm tuyến nên ống dài xuyên
        /// nhiều tường thì mọi sleeve dồn về một chỗ.
        /// </summary>
        public static bool ClipLineToBox(
            double x0, double y0, double z0, double x1, double y1, double z1,
            Box3 box, out double t0, out double t1)
        {
            if (box == null)
            {
                throw new ArgumentNullException(nameof(box));
            }

            t0 = 0.0;
            t1 = 1.0;
            var starts = new[] { x0, y0, z0 };
            var deltas = new[] { x1 - x0, y1 - y0, z1 - z0 };
            var mins = new[] { box.MinX, box.MinY, box.MinZ };
            var maxs = new[] { box.MaxX, box.MaxY, box.MaxZ };

            for (var axis = 0; axis < 3; axis++)
            {
                if (Math.Abs(deltas[axis]) < 1e-12)
                {
                    if (starts[axis] < mins[axis] || starts[axis] > maxs[axis])
                    {
                        return false;
                    }

                    continue;
                }

                var tA = (mins[axis] - starts[axis]) / deltas[axis];
                var tB = (maxs[axis] - starts[axis]) / deltas[axis];
                if (tA > tB)
                {
                    var tmp = tA;
                    tA = tB;
                    tB = tmp;
                }

                t0 = Math.Max(t0, tA);
                t1 = Math.Min(t1, tB);
                if (t0 > t1)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Hộp bao sau khi qua một phép biến đổi (link xoay/dời): dựng lại từ TÁM đỉnh rồi lấy min/max,
        /// không chỉ biến đổi hai điểm min/max — link xoay 90° mà chỉ đổi hai điểm thì hộp lệch hẳn.
        /// <paramref name="transform"/> nhận (x, y, z) trả (x', y', z'); null = hộp giữ nguyên.
        /// </summary>
        public static Box3 TransformedBox(
            Box3 box, Func<double, double, double, (double X, double Y, double Z)>? transform)
        {
            if (box == null)
            {
                throw new ArgumentNullException(nameof(box));
            }

            if (transform == null)
            {
                return box;
            }

            double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;
            for (var i = 0; i < 8; i++)
            {
                var corner = transform(
                    (i & 1) == 0 ? box.MinX : box.MaxX,
                    (i & 2) == 0 ? box.MinY : box.MaxY,
                    (i & 4) == 0 ? box.MinZ : box.MaxZ);
                minX = Math.Min(minX, corner.X);
                maxX = Math.Max(maxX, corner.X);
                minY = Math.Min(minY, corner.Y);
                maxY = Math.Max(maxY, corner.Y);
                minZ = Math.Min(minZ, corner.Z);
                maxZ = Math.Max(maxZ, corner.Z);
            }

            return new Box3(minX, minY, minZ, maxX, maxY, maxZ);
        }

        // ── Lọc ────────────────────────────────────────────────────────────────

        /// <summary>
        /// Tên category ngắn từ tên BuiltInCategory: <c>OST_DuctCurves</c> → <c>Duct</c>,
        /// <c>OST_CableTray</c> → <c>CableTray</c>. Đây là tên người dùng gõ trong <c>mepCategories</c>.
        /// </summary>
        public static string ShortCategoryName(string? builtInCategoryName)
        {
            if (StringGuard.IsBlank(builtInCategoryName))
            {
                return string.Empty;
            }

            return builtInCategoryName!
                .Replace("OST_", string.Empty)
                .Replace("Curves", string.Empty)
                .Replace("Curve", string.Empty);
        }

        /// <summary>
        /// Category có được lấy không. Bộ lọc rỗng = lấy tất cả. So khớp hai chiều, không phân biệt hoa
        /// thường: người dùng gõ "duct" hay "DuctCurves" đều trúng <c>Duct</c>.
        /// </summary>
        public static bool CategoryIncluded(string shortCategoryName, IReadOnlyList<string>? filter)
        {
            if (filter == null || filter.Count == 0)
            {
                return true;
            }

            foreach (var f in filter)
            {
                // Mục rỗng trong bộ lọc = ký tự đại diện, khớp mọi category (giữ đúng hành vi cũ:
                // IndexOf("") luôn ≥ 0). Người dùng để trống một ô là muốn "không lọc", không phải "lọc rỗng".
                if (StringGuard.IsBlank(f))
                {
                    return true;
                }

                if (shortCategoryName.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0
                    || f.IndexOf(shortCategoryName, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Tên type của host có khớp bộ lọc <c>hostTypeNames</c> không (rỗng = mọi host; chứa chuỗi, không phân biệt hoa thường).</summary>
        public static bool HostTypeMatches(string? typeName, IReadOnlyList<string>? filter)
        {
            if (filter == null || filter.Count == 0)
            {
                return true;
            }

            if (typeName == null)
            {
                return false;
            }

            // Mục rỗng = ký tự đại diện (xem CategoryIncluded).
            return filter.Any(f => StringGuard.IsBlank(f) || typeName.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        /// <summary>Tên link có khớp bộ lọc <c>linkNameContains</c> không (rỗng = mọi link).</summary>
        public static bool LinkNameMatches(string? linkName, IReadOnlyList<string>? filter)
            => HostTypeMatches(linkName, filter);

        // ── Kích thước ─────────────────────────────────────────────────────────

        /// <summary>Kích thước lỗ mở tính được, và có tra được từ mô hình hay đang dùng cỡ tạm.</summary>
        public readonly struct SleeveSize
        {
            public SleeveSize(double widthFt, double heightFt, bool resolved)
            {
                WidthFt = widthFt;
                HeightFt = heightFt;
                Resolved = resolved;
            }

            public double WidthFt { get; }

            public double HeightFt { get; }

            /// <summary>false = không tra được kích thước nào; người gọi PHẢI báo, không được im lặng dùng cỡ tạm.</summary>
            public bool Resolved { get; }
        }

        /// <summary>
        /// Cỡ sleeve từ kích thước MEP (feet, null hoặc ≤ 0 = không có) cộng khoảng hở hai bên.
        /// Ống tròn: đường kính ngoài quyết định cả hai chiều. Ống chữ nhật/máng: rộng × cao, thiếu
        /// một chiều thì chiều đó dùng cỡ tạm nhưng vẫn coi là tra được (có ít nhất một số thật).
        /// </summary>
        public static SleeveSize SizeFrom(double? outerDiameterFt, double? widthFt, double? heightFt, double clearanceEachSideFt)
        {
            var clear = Math.Max(0, clearanceEachSideFt) * 2;

            if (outerDiameterFt.HasValue && outerDiameterFt.Value > 0)
            {
                var d = outerDiameterFt.Value + clear;
                return new SleeveSize(d, d, true);
            }

            var w = FallbackSizeFt;
            var h = FallbackSizeFt;
            var found = false;
            if (widthFt.HasValue && widthFt.Value > 0)
            {
                w = widthFt.Value + clear;
                found = true;
            }

            if (heightFt.HasValue && heightFt.Value > 0)
            {
                h = heightFt.Value + clear;
                found = true;
            }

            return new SleeveSize(w, h, found);
        }

        // ── Gom lý do & thông báo ──────────────────────────────────────────────

        /// <summary>Thêm lý do lỗi nếu chưa có và chưa vượt <see cref="MaxFailureReasons"/>.</summary>
        public static void AddDistinctReason(List<string> reasons, string message)
        {
            if (reasons == null)
            {
                throw new ArgumentNullException(nameof(reasons));
            }

            if (reasons.Count < MaxFailureReasons && !reasons.Contains(message))
            {
                reasons.Add(message);
            }
        }

        /// <summary>
        /// Một câu ngắn giải thích vì sao không đặt được cái nào — ghép thẳng vào Summary. Báo cáo batch
        /// chỉ in Summary, nên lời giải thích nằm trong Messages là lời giải thích không ai đọc.
        /// </summary>
        public static string WhyNothing(int hostsInDocument, int hostsInLinks, int mepCount, int skippedExisting, bool includeLinkedModels)
        {
            // Chạy lại lần hai là trường hợp phổ biến nhất của "0 sleeve" — và nó KHÔNG phải "không có
            // giao cắt". Nói đúng chuyện đang xảy ra, vì đây cũng là bằng chứng lần trước đã ghi thật.
            if (skippedExisting > 0)
            {
                return $"Bỏ qua, đã có sleeve: {skippedExisting} vị trí.";
            }

            if (hostsInDocument + hostsInLinks == 0)
            {
                return includeLinkedModels
                    ? "Không có tường/sàn nào để xét, kể cả trong model liên kết — kiểm lại link đã nạp chưa."
                    : "Không có tường/sàn nào trong file này và includeLinkedModels đang tắt — bật lên nếu tường nằm ở model liên kết.";
            }

            return $"Đã xét {hostsInDocument} tường/sàn trong file + {hostsInLinks} từ model liên kết trên {mepCount} phần tử MEP " +
                   "nhưng không có giao cắt nào (thường do lệch cao độ hoặc hostTypeNames lọc quá chặt).";
        }

        /// <summary>
        /// Các dòng Messages nói rõ host lấy từ đâu, và khi không đặt được cái nào thì VÌ SAO.
        /// "Đã đặt 0 sleeve" trơ trọi là thứ khiến người dùng tưởng model không có giao cắt.
        /// </summary>
        public static List<string> HostSourceNotes(
            int hostsInDocument, int hostsInLinks, IReadOnlyList<string>? linkSummary,
            int placedCount, int mepCount, bool includeLinkedModels)
        {
            var notes = new List<string>
            {
                $"Tường/sàn xét tới: {hostsInDocument} trong file, {hostsInLinks} từ model liên kết.",
            };
            if (linkSummary != null)
            {
                foreach (var line in linkSummary)
                {
                    notes.Add("  Link — " + line);
                }
            }

            if (placedCount > 0)
            {
                return notes;
            }

            if (hostsInDocument + hostsInLinks == 0)
            {
                notes.Add(!includeLinkedModels
                    ? "Không có tường/sàn nào để xét. File này không có tường/sàn, và includeLinkedModels đang tắt — bật lên nếu tường nằm ở model liên kết."
                    : "Không có tường/sàn nào để xét, kể cả trong model liên kết. Kiểm lại link đã nạp chưa (Manage → Manage Links).");
            }
            else
            {
                notes.Add($"Có {hostsInDocument + hostsInLinks} tường/sàn và {mepCount} phần tử MEP nhưng không tìm ra giao cắt nào. " +
                          "Thường là do MEP và kết cấu lệch cao độ, hoặc hostTypeNames lọc quá chặt.");
            }

            return notes;
        }

        /// <summary>
        /// Cảnh báo các phần tử không tra được kích thước — kèm gợi ý tên tham số (<paramref name="lookupHint"/>,
        /// từ <c>RevitCompat.LookupFailed</c>) để kỹ sư sửa được chứ không chỉ biết là "có gì đó sai".
        /// Trả null khi không có gì để cảnh báo.
        /// </summary>
        public static string? UnknownSizeWarning(IReadOnlyList<long>? unknownSizeIds, string lookupHint)
        {
            if (unknownSizeIds == null || unknownSizeIds.Count == 0)
            {
                return null;
            }

            return $"{unknownSizeIds.Count} phần tử MEP không tra được kích thước, dùng tạm 152 mm — sleeve có thể sai cỡ. "
                   + lookupHint
                   + " Phần tử: " + string.Join(", ", unknownSizeIds.Take(MaxListedIds))
                   + (unknownSizeIds.Count > MaxListedIds ? ", …" : string.Empty);
        }

        /// <summary>Ghi chú khi phải rơi về trung điểm tuyến MEP (kém chính xác). Trả null khi không có ca nào.</summary>
        public static string? MidpointFallbackNote(int midpointFallback)
        {
            if (midpointFallback <= 0)
            {
                return null;
            }

            return $"{midpointFallback} giao cắt không tính được bằng solid lẫn hộp bao của host — "
                   + "dùng tạm trung điểm tuyến MEP, vị trí sleeve có thể lệch, kiểm lại.";
        }

        /// <summary>
        /// Số phần tử MEP không đọc được solid nên chỉ lọc host bằng hộp bao. Kết quả vẫn ra sleeve,
        /// nhưng rộng hơn thực tế — im lặng thì không ai biết con số mình đang đọc lỏng đến đâu (§64).
        /// </summary>
        public static string? NoSolidNote(int noSolidCount)
        {
            if (noSolidCount <= 0)
            {
                return null;
            }

            return $"{noSolidCount} phần tử MEP không đọc được solid — host chỉ được lọc ở mức hộp bao, "
                   + "danh sách giao cắt có thể rộng hơn thực tế, kiểm lại.";
        }

        /// <summary>Summary cho bản xem trước.</summary>
        public static string PreviewSummary(int plannedCount, string whyNothing)
        {
            var summary = $"[Xem trước] Sẽ đặt {plannedCount} sleeve tại giao cắt MEP × Tường/Sàn.";
            return plannedCount == 0 ? summary + " " + whyNothing : summary;
        }

        /// <summary>
        /// Summary cho đường ghi thật. Mọi con số quan trọng đều nằm ở đây vì báo cáo batch chỉ in Summary.
        /// </summary>
        public static string WriteSummary(
            int placed, int failed, int planned, int placedOnLink, int skippedExisting, string whyNothing)
        {
            var summary = $"Đã đặt {placed} sleeve tại giao cắt MEP × Tường/Sàn.";
            if (failed > 0)
            {
                summary += $" {failed}/{planned} vị trí đặt lỗi (xem chi tiết trong thông báo).";
            }

            if (placed == 0 && failed == 0)
            {
                summary += " " + whyNothing;
            }

            if (placedOnLink > 0)
            {
                summary += $" Trong đó {placedOnLink} cái bám tường/sàn của model liên kết nên đặt tự do (không host được vào link).";
            }

            if (skippedExisting > 0 && placed > 0)
            {
                summary += $" Bỏ qua, đã có sleeve: {skippedExisting} vị trí.";
            }

            return summary;
        }

        /// <summary>Dòng Messages liệt kê lý do đặt lỗi. Trả null khi không có lỗi.</summary>
        public static string? FailureReasonsLine(int failed, IReadOnlyList<string>? reasons)
        {
            if (failed <= 0)
            {
                return null;
            }

            return $"{failed} vị trí không đặt được sleeve. Lý do (tối đa {MaxFailureReasons} loại): "
                   + string.Join(" | ", reasons ?? Array.Empty<string>());
        }

        /// <summary>Mô tả một vị trí trong bản xem trước: host, toạ độ mm, cỡ mm.</summary>
        public static string PreviewLine(
            bool isWall, long hostId, string? linkName,
            double xMm, double yMm, double zMm, double widthMm, double heightMm)
        {
            var hostDesc = (isWall ? "Tường " : "Sàn ") + hostId;
            if (linkName != null)
            {
                hostDesc += $" (link \"{linkName}\")";
            }

            return $"  → {hostDesc} tại ({xMm:F0}, {yMm:F0}, {zMm:F0}) mm  W={widthMm:F0}mm H={heightMm:F0}mm";
        }
    }
}
