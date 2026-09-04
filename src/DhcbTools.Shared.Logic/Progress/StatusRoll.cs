using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace DhcbTools.Shared.Logic.Progress
{
    /// <summary>Một cấu kiện đã đọc trạng thái, chuẩn bị gộp.</summary>
    public sealed class StatusItem
    {
        public StatusItem(string group, ConstructionStage stage, double lengthMm = 0, DateTime? date = null, long elementId = 0)
        {
            Group = group ?? string.Empty;
            Stage = stage;
            LengthMm = lengthMm;
            Date = date;
            ElementId = elementId;
        }

        /// <summary>Nhóm gộp: tên tầng, tên hệ, hay category — người gọi quyết định.</summary>
        public string Group { get; }

        public ConstructionStage Stage { get; }

        /// <summary>Chiều dài (mm) với ống/duct/tray; 0 với thiết bị đếm theo cái.</summary>
        public double LengthMm { get; }

        /// <summary>Ngày ghi nhận trạng thái; null = không có ngày.</summary>
        public DateTime? Date { get; }

        public long ElementId { get; }
    }

    /// <summary>Kết quả gộp cho một nhóm.</summary>
    public sealed class StatusRollRow
    {
        public StatusRollRow(string group)
        {
            Group = group ?? string.Empty;
        }

        public string Group { get; }

        public Dictionary<ConstructionStage, int> CountByStage { get; } = new Dictionary<ConstructionStage, int>();

        public Dictionary<ConstructionStage, double> LengthMmByStage { get; } = new Dictionary<ConstructionStage, double>();

        public int Total { get; set; }

        public double TotalLengthMm { get; set; }

        public int CountOf(ConstructionStage stage) => CountByStage.TryGetValue(stage, out var v) ? v : 0;

        public double LengthMmOf(ConstructionStage stage) => LengthMmByStage.TryGetValue(stage, out var v) ? v : 0;

        /// <summary>Số cấu kiện <b>chưa ai ghi nhận</b> — khác hẳn "đã ghi nhận là chưa lắp".</summary>
        public int NoDataCount => CountOf(ConstructionStage.ChuaCoDuLieu);

        /// <summary>Đạt mức <paramref name="stage"/> trở lên.</summary>
        public int CountAtLeast(ConstructionStage stage) =>
            CountByStage.Where(p => p.Key >= stage).Sum(p => p.Value);

        public double LengthMmAtLeast(ConstructionStage stage) =>
            LengthMmByStage.Where(p => p.Key >= stage).Sum(p => p.Value);

        /// <summary>% theo <b>số lượng</b>, mẫu số là toàn bộ cấu kiện trong phạm vi (kể cả chưa có dữ liệu).</summary>
        public double PercentAtLeast(ConstructionStage stage) => Percent(CountAtLeast(stage), Total);

        /// <summary>% theo <b>chiều dài</b>; 0 khi nhóm không có cấu kiện nào có chiều dài.</summary>
        public double PercentByLengthAtLeast(ConstructionStage stage) => Percent(LengthMmAtLeast(stage), TotalLengthMm);

        /// <summary>Nhóm này có đo được theo chiều dài không (có ống/duct) — nếu không, cột % chiều dài là vô nghĩa.</summary>
        public bool HasLength => TotalLengthMm > 0;

        internal static double Percent(double part, double total) => total <= 0 ? 0 : part * 100.0 / total;
    }

    /// <summary>
    /// Gộp trạng thái thi công theo nhóm (tầng / hệ / category) và theo tuần — phần thay cho việc
    /// nhập Excel rời rồi vẽ tay biểu đồ tiến độ (đề xuất B1).
    /// <para>
    /// Hai điều được giữ nghiêm ở đây, vì cả hai đều là chỗ báo cáo tiến độ hay nói dối:
    /// ① <b>mẫu số là toàn bộ cấu kiện trong phạm vi</b>, kể cả cái chưa ai ghi nhận — chưa nhập thì
    /// chưa lắp, không được bỏ ra khỏi mẫu số cho phần trăm đẹp lên; ② <b>không có trọng số cho
    /// "đang lắp"</b>: một cấu kiện đang lắp không phải "nửa cái ống", nên nó chỉ được đếm ở cột của
    /// chính nó chứ không cộng nửa vào phần trăm hoàn thành.
    /// </para>
    /// </summary>
    public static class StatusRoll
    {
        /// <summary>Gộp theo <see cref="StatusItem.Group"/>, sắp xếp tên nhóm theo thứ tự tự nhiên.</summary>
        public static List<StatusRollRow> By(IEnumerable<StatusItem> items)
        {
            var rows = new Dictionary<string, StatusRollRow>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in items ?? Enumerable.Empty<StatusItem>())
            {
                if (!rows.TryGetValue(item.Group, out var row))
                {
                    rows[item.Group] = row = new StatusRollRow(item.Group);
                }

                row.Total++;
                row.TotalLengthMm += item.LengthMm;
                row.CountByStage.TryGetValue(item.Stage, out var count);
                row.CountByStage[item.Stage] = count + 1;
                if (item.LengthMm > 0)
                {
                    row.LengthMmByStage.TryGetValue(item.Stage, out var length);
                    row.LengthMmByStage[item.Stage] = length + item.LengthMm;
                }
            }

            return rows.Values.OrderBy(r => r.Group, NaturalComparer.Instance).ToList();
        }

        /// <summary>Một dòng tổng cho toàn bộ phạm vi, tên nhóm do người gọi đặt.</summary>
        public static StatusRollRow Total(IEnumerable<StatusItem> items, string groupName = "Tổng")
        {
            var all = items?.ToList() ?? new List<StatusItem>();
            var row = new StatusRollRow(groupName);
            foreach (var item in all)
            {
                row.Total++;
                row.TotalLengthMm += item.LengthMm;
                row.CountByStage.TryGetValue(item.Stage, out var count);
                row.CountByStage[item.Stage] = count + 1;
                if (item.LengthMm > 0)
                {
                    row.LengthMmByStage.TryGetValue(item.Stage, out var length);
                    row.LengthMmByStage[item.Stage] = length + item.LengthMm;
                }
            }

            return row;
        }
    }

    /// <summary>Một tuần trong chuỗi tiến độ.</summary>
    public sealed class ProgressWeek
    {
        public ProgressWeek(DateTime weekStart)
        {
            WeekStart = weekStart;
        }

        /// <summary>Thứ Hai của tuần (00:00). Dùng ngày đầu tuần thay vì "tuần số mấy" — số tuần ISO là chỗ mỗi phần mềm đếm một kiểu.</summary>
        public DateTime WeekStart { get; }

        /// <summary>Số cấu kiện đạt mức trong đúng tuần này.</summary>
        public int Added { get; set; }

        /// <summary>Luỹ kế tới hết tuần này.</summary>
        public int Cumulative { get; set; }

        /// <summary>% luỹ kế trên tổng phạm vi.</summary>
        public double CumulativePercent { get; set; }

        public string Label => WeekStart.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
    }

    /// <summary>Chuỗi tiến độ theo tuần và phần không xếp được vào tuần nào.</summary>
    public sealed class ProgressSeries
    {
        public List<ProgressWeek> Weeks { get; } = new List<ProgressWeek>();

        /// <summary>Cấu kiện đã đạt mức nhưng <b>không có ngày</b> — không vẽ được lên trục thời gian, phải nói ra.</summary>
        public int ReachedWithoutDate { get; set; }

        public int Total { get; set; }
    }

    /// <summary>Chuỗi tiến độ theo tuần (tuần bắt đầu thứ Hai).</summary>
    public static class WeeklyProgress
    {
        /// <summary>Thứ Hai của tuần chứa <paramref name="date"/>, 00:00.</summary>
        public static DateTime WeekStartOf(DateTime date)
        {
            var day = date.Date;
            var offset = ((int)day.DayOfWeek + 6) % 7;   // Chủ nhật = 6, thứ Hai = 0
            return day.AddDays(-offset);
        }

        /// <summary>
        /// Luỹ kế số cấu kiện đạt <paramref name="stage"/> trở lên theo từng tuần. Tuần không có gì mới
        /// vẫn xuất hiện (đường luỹ kế phải liền), nên biểu đồ không "nhảy cóc" qua những tuần đứng yên.
        /// </summary>
        public static ProgressSeries Series(IEnumerable<StatusItem> items, ConstructionStage stage = ConstructionStage.DaLap)
        {
            var all = items?.ToList() ?? new List<StatusItem>();
            var series = new ProgressSeries { Total = all.Count };

            var reached = all.Where(i => i.Stage >= stage).ToList();
            series.ReachedWithoutDate = reached.Count(i => i.Date == null);

            var dated = reached.Where(i => i.Date != null).ToList();
            if (dated.Count == 0)
            {
                return series;
            }

            var byWeek = dated.GroupBy(i => WeekStartOf(i.Date!.Value))
                .ToDictionary(g => g.Key, g => g.Count());

            var first = byWeek.Keys.Min();
            var last = byWeek.Keys.Max();
            var cumulative = 0;
            for (var week = first; week <= last; week = week.AddDays(7))
            {
                byWeek.TryGetValue(week, out var added);
                cumulative += added;
                series.Weeks.Add(new ProgressWeek(week)
                {
                    Added = added,
                    Cumulative = cumulative,
                    CumulativePercent = StatusRollRow.Percent(cumulative, all.Count),
                });
            }

            return series;
        }
    }
}
