using Autodesk.Revit.DB;
using DhcbTools.Shared.Logic;
using DhcbTools.Shared.Logic.Ai;
using DhcbTools.Shared.Logic.Usage;

namespace DhcbTools.Core.Health;

/// <summary>Cấu hình cho <see cref="UsageReportCommand"/>.</summary>
public sealed class UsageReportConfig
{
    /// <summary>Thư mục log; rỗng = <c>%APPDATA%\DHCB\logs</c>.</summary>
    public string? LogFolder { get; init; }

    /// <summary>Báo cáo Markdown; rỗng = không ghi (kết quả vẫn hiện trong Messages).</summary>
    public string? OutputPath { get; init; }

    /// <summary>CSV để gộp số liệu nhiều máy trong Excel; rỗng = không ghi.</summary>
    public string? CsvPath { get; init; }

    /// <summary>Chỉ tính <c>days</c> ngày gần nhất (0 = mọi file log đang có, tối đa 30 ngày theo DhcbLog).</summary>
    public int Days { get; init; } = 0;

    /// <summary>Lọc theo app: "Revit", "AutoCAD"; rỗng = cả hai.</summary>
    public string? App { get; init; }
}

/// <summary>
/// Đọc lại log của chính máy này thành số liệu <b>lệnh nào dùng hằng tuần, lệnh nào bấm rồi bỏ, lệnh nào
/// lỗi nhiều nhất</b> — đúng ba con số mà mục 9.4 định thu bằng bảng tick
/// (<c>docs/mau-phan-hoi-9-4.md</c>) và dùng để quyết định giai đoạn 10/11 đi sâu vào đâu.
/// <para>
/// Bảng tick phụ thuộc trí nhớ người điền và việc họ chịu điền; log thì <c>DhcbLog</c> đã giữ sẵn 30 ngày
/// mà chưa có gì đọc lại. Lệnh này không thay bảng tick (cột "vì sao bỏ" chỉ người trả lời được) — nó
/// làm phần đo được, để câu hỏi cho người chỉ còn phần người mới trả lời nổi.
/// </para>
/// <para>
/// Công cụ nội bộ như <c>RunTests</c>: không lên Ribbon, không chào ra <c>/tools</c>. Không cần
/// <see cref="Document"/> — nhận vào chỉ để vừa chữ ký <see cref="ICoreCommand{T}"/>.
/// </para>
/// </summary>
public sealed class UsageReportCommand : ICoreCommand<UsageReportConfig>
{
    public string CommandName => "UsageReport";

    public CommandResult Execute(Document document, UsageReportConfig config)
    {
        var folder = string.IsNullOrWhiteSpace(config.LogFolder) ? DhcbLog.DefaultDirectory : config.LogFolder!;
        if (!Directory.Exists(folder))
        {
            return CommandResult.Fail(
                $"E-PATH-MISSING: không có thư mục log \"{folder}\". Add-in chỉ ghi log từ bản 0.9 trở đi — "
                + "chạy vài lệnh rồi quay lại.");
        }

        var cutoff = config.Days > 0 ? DateTime.Now.Date.AddDays(-config.Days + 1) : DateTime.MinValue;
        var entries = new List<UsageEntry>();
        var files = 0;

        foreach (var file in Directory.EnumerateFiles(folder, "*.log"))
        {
            List<UsageEntry> parsed;
            try
            {
                parsed = UsageLog.Parse(Path.GetFileName(file), File.ReadLines(file));
            }
            catch (Exception ex)
            {
                // Một file log hỏng/đang bị khoá không được làm mất số liệu của những file còn lại.
                return CommandResult.Fail($"Không đọc được \"{file}\": {ex.Message}");
            }

            if (parsed.Count == 0)
            {
                continue;
            }

            files++;
            entries.AddRange(parsed.Where(e =>
                e.When >= cutoff
                && (string.IsNullOrWhiteSpace(config.App)
                    || string.Equals(e.App, config.App, StringComparison.OrdinalIgnoreCase))));
        }

        if (entries.Count == 0)
        {
            // Không có số liệu KHÁC HẲN "mọi lệnh đều không ai dùng" — nói rõ để không ai đọc nhầm
            // một báo cáo trống thành một kết luận.
            return CommandResult.Fail(
                $"Chưa có lần chạy lệnh nào trong log ({files} file ở \"{folder}\"). "
                + "Đây là \"chưa có số liệu\", không phải \"không ai dùng lệnh nào\".");
        }

        var stats = UsageLog.Aggregate(entries);
        var trongCatalog = new List<string>();
        if (string.IsNullOrWhiteSpace(config.App) || string.Equals(config.App, "Revit", StringComparison.OrdinalIgnoreCase))
        {
            trongCatalog.AddRange(CommandCatalog.For(CommandCatalog.Revit).Select(c => c.Name));
        }

        if (string.IsNullOrWhiteSpace(config.App) || string.Equals(config.App, "AutoCAD", StringComparison.OrdinalIgnoreCase))
        {
            trongCatalog.AddRange(CommandCatalog.For(CommandCatalog.AutoCad).Select(c => c.Name));
        }

        var chuaDung = UsageLog.ChuaDungLanNao(trongCatalog.Distinct(StringComparer.OrdinalIgnoreCase), stats);
        var soNgay = entries.Select(e => e.When.Date).Distinct().Count();

        if (!string.IsNullOrWhiteSpace(config.OutputPath))
        {
            RevitCompat.EnsureParentDirectory(config.OutputPath!);
            File.WriteAllText(config.OutputPath!, UsageLog.ToMarkdown(stats, chuaDung, soNgay), System.Text.Encoding.UTF8);
        }

        if (!string.IsNullOrWhiteSpace(config.CsvPath))
        {
            RevitCompat.EnsureParentDirectory(config.CsvPath!);
            File.WriteAllText(config.CsvPath!, UsageLog.ToCsv(stats), CsvText.Utf8WithBom);
        }

        var bamRoiBo = stats.Count(s => s.BamRoiBo);
        var result = CommandResult.Ok(
            $"{entries.Count} lần chạy trên {soNgay} ngày, {stats.Count} lệnh có người dùng "
            + $"({bamRoiBo} lệnh chỉ xem trước rồi bỏ, {chuaDung.Count} lệnh chưa bấm lần nào)."
            + (string.IsNullOrWhiteSpace(config.OutputPath) ? string.Empty : $" Báo cáo: \"{config.OutputPath}\"."),
            stats.Count);

        result.Messages.AddRange(stats.Take(20).Select(s =>
            $"{s.Command} ({s.App}): {s.Days} ngày, {s.Runs} lần ({s.RealRuns} chạy thật, {s.Failures} lỗi), trung vị {s.MedianMs} ms"));

        if (chuaDung.Count > 0)
        {
            result.Messages.Add("Chưa bấm lần nào: " + string.Join(", ", chuaDung));
        }

        return result;
    }
}
