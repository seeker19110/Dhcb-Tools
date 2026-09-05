using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace DhcbTools.Shared.Logic.Usage
{
    /// <summary>Một lần chạy lệnh, đọc lại từ file log.</summary>
    public sealed class UsageEntry
    {
        public UsageEntry(DateTime when, string app, string command, bool success, bool dryRun, int affected, long ms)
        {
            When = when;
            App = app;
            Command = command;
            Success = success;
            DryRun = dryRun;
            Affected = affected;
            Ms = ms;
        }

        public DateTime When { get; }

        /// <summary>"Revit" hoặc "AutoCAD" — lấy từ tên file log.</summary>
        public string App { get; }

        public string Command { get; }

        public bool Success { get; }

        public bool DryRun { get; }

        public int Affected { get; }

        public long Ms { get; }
    }

    /// <summary>Số liệu gộp của một lệnh.</summary>
    public sealed class UsageStat
    {
        public UsageStat(string app, string command, IReadOnlyList<UsageEntry> entries)
        {
            App = app;
            Command = command;
            Runs = entries.Count;
            Days = entries.Select(e => e.When.Date).Distinct().Count();
            Failures = entries.Count(e => !e.Success);
            RealRuns = entries.Count(e => !e.DryRun);
            TotalAffected = entries.Sum(e => (long)e.Affected);
            MedianMs = Median(entries.Select(e => e.Ms).ToList());
            // Danh sách rỗng: trả số liệu 0 thay vì ném "Sequence contains no elements" — dựng UsageStat
            // cho một lệnh chưa có lần chạy nào là chuyện bình thường, không đáng phải bắt exception.
            First = entries.Count == 0 ? default : entries.Min(e => e.When);
            Last = entries.Count == 0 ? default : entries.Max(e => e.When);
        }

        public string App { get; }

        public string Command { get; }

        public int Runs { get; }

        /// <summary>Số NGÀY khác nhau có chạy — thước đo "dùng thật" tốt hơn số lần chạy.</summary>
        public int Days { get; }

        public int Failures { get; }

        /// <summary>Số lần chạy thật (không phải xem trước).</summary>
        public int RealRuns { get; }

        public long TotalAffected { get; }

        /// <summary>Trung vị, không phải trung bình: một lần chạy 40 phút không được kéo lệch cả cột.</summary>
        public long MedianMs { get; }

        public DateTime First { get; }

        public DateTime Last { get; }

        public double FailureRate => Runs == 0 ? 0 : (double)Failures / Runs;

        /// <summary>
        /// Bấm rồi bỏ: đã chạy nhưng <b>chưa bao giờ</b> chạy thật — kỹ sư mở ra xem trước rồi thôi.
        /// Đây chính là cột "bấm rồi bỏ" của mẫu phản hồi 9.4, nhưng đo được thay vì hỏi.
        /// </summary>
        public bool BamRoiBo => Runs > 0 && RealRuns == 0;

        private static long Median(List<long> values)
        {
            if (values.Count == 0)
            {
                return 0;
            }

            values.Sort();
            return values.Count % 2 == 1
                ? values[values.Count / 2]
                : (values[values.Count / 2 - 1] + values[values.Count / 2]) / 2;
        }
    }

    /// <summary>
    /// Đọc lại file log của add-in thành số liệu sử dụng — <b>lệnh nào dùng hằng tuần, lệnh nào bấm rồi bỏ,
    /// lệnh nào lỗi nhiều nhất</b>.
    /// <para>
    /// Vì sao cần: mục 9.4 của lộ trình định lấy đúng ba con số đó bằng một bảng tick
    /// (<c>docs/mau-phan-hoi-9-4.md</c>) rồi dựa vào chúng để quyết định giai đoạn 10/11. Bảng tick phụ
    /// thuộc trí nhớ người điền và việc họ chịu điền; log thì đã ghi sẵn 30 ngày mà chưa có gì đọc lại.
    /// </para>
    /// <para>
    /// Đây là số liệu <b>của chính máy đó</b>, không gửi đi đâu — cùng nguyên tắc offline của toàn bộ tool.
    /// </para>
    /// </summary>
    public static class UsageLog
    {
        /// <summary>Tiền tố của dòng log một lần chạy lệnh. Đổi chuỗi này là mất số liệu cũ.</summary>
        public const string Prefix = "LỆNH ";

        // 09:14:22.031  LỆNH ClashDetection | ok=true | dryRun=true | affected=479 | ms=3821
        private static readonly Regex Line = new Regex(
            @"^(?<time>\d{2}:\d{2}:\d{2})[\.\d]*\s+LỆNH\s+(?<cmd>[A-Za-z_][A-Za-z0-9_]*)\s*\|\s*ok=(?<ok>true|false)\s*\|\s*dryRun=(?<dry>true|false)\s*\|\s*affected=(?<aff>-?\d+)\s*\|\s*ms=(?<ms>\d+)",
            RegexOptions.Compiled);

        // Revit-2026-09-04.log
        private static readonly Regex FileName = new Regex(
            @"^(?<app>[A-Za-z]+)-(?<date>\d{4}-\d{2}-\d{2})\.log$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>Dựng dòng log cho một lần chạy. Một chỗ duy nhất sinh ra định dạng mà <see cref="Parse"/> đọc.</summary>
        public static string Format(string command, bool success, bool dryRun, int affected, long ms) =>
            Prefix + command
            + " | ok=" + (success ? "true" : "false")
            + " | dryRun=" + (dryRun ? "true" : "false")
            + " | affected=" + affected.ToString(CultureInfo.InvariantCulture)
            + " | ms=" + ms.ToString(CultureInfo.InvariantCulture);

        /// <summary>
        /// Đọc một file log. <paramref name="fileName"/> cho biết app và ngày; dòng nào không phải dòng
        /// chạy lệnh thì bỏ qua (file log còn chứa cả lỗi, khởi động Bridge…).
        /// </summary>
        public static List<UsageEntry> Parse(string fileName, IEnumerable<string> lines)
        {
            var entries = new List<UsageEntry>();
            var meta = FileName.Match(fileName ?? string.Empty);
            if (!meta.Success)
            {
                return entries;
            }

            var app = meta.Groups["app"].Value;
            if (!DateTime.TryParseExact(meta.Groups["date"].Value, "yyyy-MM-dd",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
            {
                return entries;
            }

            foreach (var raw in lines ?? Enumerable.Empty<string>())
            {
                var m = Line.Match(raw ?? string.Empty);
                if (!m.Success)
                {
                    continue;
                }

                var when = DateTime.TryParseExact(m.Groups["time"].Value, "HH:mm:ss",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var t)
                    ? day.Add(t.TimeOfDay)
                    : day;

                entries.Add(new UsageEntry(
                    when,
                    app,
                    m.Groups["cmd"].Value,
                    bool.Parse(m.Groups["ok"].Value),
                    bool.Parse(m.Groups["dry"].Value),
                    int.Parse(m.Groups["aff"].Value, CultureInfo.InvariantCulture),
                    long.Parse(m.Groups["ms"].Value, CultureInfo.InvariantCulture)));
            }

            return entries;
        }

        /// <summary>Gộp theo (app, lệnh), sắp theo số ngày dùng giảm dần — lệnh dùng thật nằm trên đầu.</summary>
        public static List<UsageStat> Aggregate(IEnumerable<UsageEntry> entries) =>
            (entries ?? Enumerable.Empty<UsageEntry>())
            .GroupBy(e => (e.App, e.Command))
            .Select(g => new UsageStat(g.Key.App, g.Key.Command, g.ToList()))
            .OrderByDescending(s => s.Days)
            .ThenByDescending(s => s.Runs)
            .ThenBy(s => s.Command, StringComparer.OrdinalIgnoreCase)
            .ToList();

        /// <summary>
        /// Lệnh có trong catalog mà log <b>không hề nhắc tới</b>: chưa ai bấm lần nào trong khoảng log
        /// còn giữ. Đây là cột "chưa dùng" của mẫu 9.4 — và là danh sách ứng viên để cân nhắc bỏ bớt.
        /// </summary>
        public static List<string> ChuaDungLanNao(IEnumerable<string> commandsInCatalog, IEnumerable<UsageStat> stats)
        {
            var daDung = new HashSet<string>(stats.Select(s => s.Command), StringComparer.OrdinalIgnoreCase);
            return commandsInCatalog
                .Where(c => !daDung.Contains(c))
                .OrderBy(c => c, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>Báo cáo Markdown — cùng ba cột của mẫu phản hồi 9.4, nhưng đo được.</summary>
        public static string ToMarkdown(IReadOnlyList<UsageStat> stats, IReadOnlyList<string> chuaDung, int soNgayLog)
        {
            var sb = new StringBuilder();
            sb.Append("# Số liệu sử dụng DHCB Tools\n\n");
            sb.Append($"Đọc từ log của chính máy này, {soNgayLog} ngày gần nhất. Không có dữ liệu nào rời máy.\n\n");

            if (stats.Count == 0)
            {
                sb.Append("Chưa có lần chạy lệnh nào trong log.\n");
                return sb.ToString();
            }

            sb.Append("| Lệnh | App | Ngày dùng | Lần chạy | Chạy thật | Lỗi | Trung vị (ms) | Lần cuối |\n");
            sb.Append("|---|---|---|---|---|---|---|---|\n");
            foreach (var s in stats)
            {
                sb.Append($"| `{s.Command}` | {s.App} | {s.Days} | {s.Runs} | {s.RealRuns} | {s.Failures} "
                    + $"| {s.MedianMs} | {s.Last:yyyy-MM-dd} |\n");
            }

            var bamRoiBo = stats.Where(s => s.BamRoiBo).ToList();
            if (bamRoiBo.Count > 0)
            {
                sb.Append("\n## Bấm rồi bỏ — đã xem trước nhưng chưa bao giờ chạy thật\n\n");
                foreach (var s in bamRoiBo)
                {
                    sb.Append($"- `{s.Command}` ({s.App}): {s.Runs} lần xem trước, 0 lần chạy thật.\n");
                }
            }

            var hayLoi = stats.Where(s => s.Failures > 0).OrderByDescending(s => s.FailureRate).Take(10).ToList();
            if (hayLoi.Count > 0)
            {
                sb.Append("\n## Lỗi nhiều nhất\n\n");
                foreach (var s in hayLoi)
                {
                    sb.Append($"- `{s.Command}` ({s.App}): {s.Failures}/{s.Runs} lần lỗi "
                        + $"({NumericText.Format(s.FailureRate * 100, 0)}%).\n");
                }
            }

            if (chuaDung.Count > 0)
            {
                sb.Append($"\n## Chưa bấm lần nào ({chuaDung.Count} lệnh)\n\n");
                sb.Append(string.Join(", ", chuaDung.Select(c => "`" + c + "`")) + "\n");
            }

            return sb.ToString();
        }

        /// <summary>CSV để gộp số liệu của nhiều máy lại trong Excel.</summary>
        public static string ToCsv(IEnumerable<UsageStat> stats)
        {
            var sb = new StringBuilder("App,Command,Days,Runs,RealRuns,Failures,TotalAffected,MedianMs,First,Last\n");
            foreach (var s in stats)
            {
                sb.Append(CsvText.JoinLine(new[]
                {
                    s.App, s.Command,
                    s.Days.ToString(CultureInfo.InvariantCulture),
                    s.Runs.ToString(CultureInfo.InvariantCulture),
                    s.RealRuns.ToString(CultureInfo.InvariantCulture),
                    s.Failures.ToString(CultureInfo.InvariantCulture),
                    s.TotalAffected.ToString(CultureInfo.InvariantCulture),
                    s.MedianMs.ToString(CultureInfo.InvariantCulture),
                    s.First.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                    s.Last.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                })).Append('\n');
            }

            return sb.ToString();
        }
    }
}
