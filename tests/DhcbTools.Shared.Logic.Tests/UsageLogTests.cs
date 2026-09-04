using DhcbTools.Shared.Logic.Usage;
using Xunit;

namespace DhcbTools.Shared.Logic.Tests;

/// <summary>
/// Số liệu sử dụng đọc từ log (<c>UsageReport</c>). Mục 9.4 định lấy ba con số — *dùng hằng tuần / bấm
/// rồi bỏ / chưa dùng* — bằng một bảng tick rồi dựa vào chúng quyết định giai đoạn 10/11. Bảng tick phụ
/// thuộc trí nhớ người điền; log thì đã ghi sẵn 30 ngày mà chưa có gì đọc lại.
/// <para>
/// Ràng buộc quan trọng nhất được test ở đây: <b>vòng tròn Format → Parse phải khép</b>. Định dạng dòng
/// log là hợp đồng giữa hai thời điểm cách nhau 30 ngày; đổi một bên là mất sạch số liệu cũ mà không ai
/// nhận ra, vì log hỏng không làm gì đỏ cả.
/// </para>
/// </summary>
public class UsageLogTests
{
    private const string File1 = "Revit-2026-09-01.log";

    private static string Dong(string time, string body) => time + "  " + body;

    [Fact]
    public void VongTron_FormatRoiParse_GiuNguyenMoiTruong()
    {
        var line = Dong("09:14:22.031", UsageLog.Format("ClashDetection", success: true, dryRun: true, affected: 479, ms: 3821));

        var e = Assert.Single(UsageLog.Parse(File1, new[] { line }));

        Assert.Equal("ClashDetection", e.Command);
        Assert.Equal("Revit", e.App);
        Assert.True(e.Success);
        Assert.True(e.DryRun);
        Assert.Equal(479, e.Affected);
        Assert.Equal(3821, e.Ms);
        Assert.Equal(new DateTime(2026, 9, 1, 9, 14, 22), e.When);
    }

    /// <summary>File log còn chứa lỗi, khởi động Bridge, dọn log… — chỉ dòng chạy lệnh mới được tính.</summary>
    [Fact]
    public void DongKhongPhaiChayLenh_BiBoQua()
    {
        var lines = new[]
        {
            Dong("08:00:01.000", "Add-in khởi động — phiên bản 1.0.0, Revit 2024.3"),
            Dong("08:00:02.000", "LỖI lệnh SleeveAuto (dryRun=False): System.NullReferenceException…"),
            Dong("08:01:00.000", UsageLog.Format("SleeveAuto", true, false, 345, 1200)),
        };

        Assert.Equal("SleeveAuto", Assert.Single(UsageLog.Parse(File1, lines)).Command);
    }

    [Fact]
    public void TenFileKhongDungDinhDang_ThiKhongDoanBua()
    {
        var line = Dong("09:00:00.000", UsageLog.Format("HealthReport", true, true, 1, 10));

        Assert.Empty(UsageLog.Parse("ghi-chu.txt", new[] { line }));
        Assert.Empty(UsageLog.Parse("Revit.log", new[] { line }));
    }

    // ── Gộp ──────────────────────────────────────────────────────────────────

    private static List<UsageEntry> Ba()
    {
        var mot = UsageLog.Parse("Revit-2026-09-01.log", new[]
        {
            Dong("09:00:00.000", UsageLog.Format("HealthReport", true, true, 1, 100)),
            Dong("10:00:00.000", UsageLog.Format("HealthReport", true, true, 1, 300)),
            Dong("11:00:00.000", UsageLog.Format("SleeveAuto", true, true, 0, 900)),
        });
        var hai = UsageLog.Parse("Revit-2026-09-02.log", new[]
        {
            Dong("09:00:00.000", UsageLog.Format("HealthReport", false, true, 0, 200)),
        });
        return mot.Concat(hai).ToList();
    }

    [Fact]
    public void Gop_DemNgayDungChuKhongChiDemLanChay()
    {
        var health = UsageLog.Aggregate(Ba()).First(s => s.Command == "HealthReport");

        Assert.Equal(3, health.Runs);
        Assert.Equal(2, health.Days);        // hai ngày khác nhau — thước đo "dùng thật"
        Assert.Equal(1, health.Failures);
        Assert.Equal(new DateTime(2026, 9, 2, 9, 0, 0), health.Last);
    }

    /// <summary>Một lần chạy 40 phút không được kéo lệch cả cột thời gian.</summary>
    [Fact]
    public void Gop_DungTrungVi_KhongPhaiTrungBinh()
    {
        var entries = UsageLog.Parse(File1, new[]
        {
            Dong("09:00:00.000", UsageLog.Format("X", true, true, 0, 100)),
            Dong("09:01:00.000", UsageLog.Format("X", true, true, 0, 200)),
            Dong("09:02:00.000", UsageLog.Format("X", true, true, 0, 2_400_000)),
        });

        Assert.Equal(200, Assert.Single(UsageLog.Aggregate(entries)).MedianMs);
    }

    /// <summary>Cột "bấm rồi bỏ" của mẫu 9.4 — đo được thay vì hỏi.</summary>
    [Fact]
    public void BamRoiBo_LaChayXemTruocMaChuaBaoGioChayThat()
    {
        var stats = UsageLog.Aggregate(Ba());

        Assert.True(stats.First(s => s.Command == "SleeveAuto").BamRoiBo);

        var chayThat = UsageLog.Parse(File1, new[]
        {
            Dong("09:00:00.000", UsageLog.Format("AutoNumbering", true, true, 5, 10)),
            Dong("09:05:00.000", UsageLog.Format("AutoNumbering", true, false, 5, 40)),
        });
        Assert.False(Assert.Single(UsageLog.Aggregate(chayThat)).BamRoiBo);
    }

    [Fact]
    public void ChuaDungLanNao_LaHieuCuaCatalogVaLog()
    {
        var chuaDung = UsageLog.ChuaDungLanNao(
            new[] { "HealthReport", "SleeveAuto", "StylePurge", "ViewportCopy" },
            UsageLog.Aggregate(Ba()));

        Assert.Equal(new[] { "StylePurge", "ViewportCopy" }, chuaDung);
    }

    [Fact]
    public void Gop_LenhDungNhieuNgayNhatDungDau()
    {
        Assert.Equal("HealthReport", UsageLog.Aggregate(Ba())[0].Command);
    }

    // ── Báo cáo ──────────────────────────────────────────────────────────────

    [Fact]
    public void Markdown_CoDuBaMucCuaMau94()
    {
        var stats = UsageLog.Aggregate(Ba());

        var md = UsageLog.ToMarkdown(stats, new[] { "StylePurge" }, 2);

        Assert.Contains("| `HealthReport` |", md);
        Assert.Contains("Bấm rồi bỏ", md);
        Assert.Contains("Lỗi nhiều nhất", md);
        Assert.Contains("`StylePurge`", md);
    }

    /// <summary>Không có số liệu khác hẳn "không ai dùng lệnh nào" — báo cáo phải nói đúng cái nào.</summary>
    [Fact]
    public void Markdown_KhongCoSoLieu_ThiNoiRoLaChuaCo()
    {
        var md = UsageLog.ToMarkdown(new List<UsageStat>(), new string[0], 0);

        Assert.Contains("Chưa có lần chạy lệnh nào", md);
        Assert.DoesNotContain("| Lệnh |", md);
    }

    [Fact]
    public void Csv_CoDuCot()
    {
        var csv = UsageLog.ToCsv(UsageLog.Aggregate(Ba()));

        Assert.StartsWith("App,Command,Days,Runs,RealRuns,Failures,TotalAffected,MedianMs,First,Last", csv);
        Assert.Contains("Revit,HealthReport,2,3", csv);
    }
}
