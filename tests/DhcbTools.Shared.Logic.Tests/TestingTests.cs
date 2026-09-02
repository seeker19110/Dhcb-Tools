using DhcbTools.Shared.Logic.Testing;
using Xunit;

namespace DhcbTools.Shared.Logic.Tests;

/// <summary>
/// Tầng thuần của bộ kiểm thử chạy trong Revit (giai đoạn 8.3): đọc bộ ca kiểm, đánh giá kỳ vọng,
/// dựng báo cáo. Chính tầng này quyết định một ca là đạt hay trượt, nên nó phải có test của riêng nó —
/// nếu không thì "bộ test xanh" lại là một con số không kiểm chứng được nữa.
/// </summary>
public class TestingTests
{
    private static TestObservation Ok(int affected = 1, string summary = "Đã chạy xong.") => new()
    {
        Success = true,
        Summary = summary,
        AffectedCount = affected,
        ElapsedMs = 10,
    };

    [Fact]
    public void KyVongMacDinh_ChiDoiThanhCong()
    {
        var expectation = new TestExpectation();

        Assert.Empty(expectation.Evaluate(Ok()));

        var failed = Ok();
        failed.Success = false;
        failed.Summary = "Không tìm thấy family.";
        var failures = expectation.Evaluate(failed);
        Assert.Single(failures);
        Assert.Contains("Không tìm thấy family", failures[0]);
    }

    [Fact]
    public void Exception_LamTruotNgayVaBoQuaKiemTraKhac()
    {
        var expectation = new TestExpectation { MinAffected = 100, MaxMs = 1 };
        var observed = new TestObservation { Exception = "System.NullReferenceException: ...", ElapsedMs = 5000 };

        var failures = expectation.Evaluate(observed);

        Assert.Single(failures);
        Assert.Contains("ném exception", failures[0]);
    }

    [Fact]
    public void SoPhanTuAnhHuong_KiemCaHaiChieu()
    {
        Assert.Contains("≥ 5", string.Join(";", new TestExpectation { MinAffected = 5 }.Evaluate(Ok(affected: 2))));
        Assert.Contains("≤ 3", string.Join(";", new TestExpectation { MaxAffected = 3 }.Evaluate(Ok(affected: 9))));
        Assert.Empty(new TestExpectation { MinAffected = 1, MaxAffected = 10 }.Evaluate(Ok(affected: 5)));
    }

    [Fact]
    public void ChuoiTrongSummaryVaMessages_KhongPhanBietHoaThuong()
    {
        var observed = Ok(summary: "[Xem trước] Sẽ đặt 12 sleeve.");
        observed.Messages.Add("→ Tường 123 tại (100, 200, 300) mm");

        var expectation = new TestExpectation
        {
            SummaryContains = { "xem trước", "SLEEVE" },
            MessagesContain = { "tường 123" },
        };

        Assert.Empty(expectation.Evaluate(observed));
        Assert.Single(new TestExpectation { SummaryContains = { "không có chuỗi này" } }.Evaluate(observed));
    }

    /// <summary>
    /// Đây là kỳ vọng bắt được đúng loại lỗi mà giai đoạn 8.1 vừa sửa: lệnh báo thành công nhưng
    /// thực chất không làm gì vì thiếu tham số/family (no-op im lặng).
    /// </summary>
    [Fact]
    public void NeverContains_BatDuocThongBaoNoOp()
    {
        var observed = Ok();
        observed.Messages.Add("Bỏ qua dòng 4, cột \"Mark\": phần tử 123 không có tham số này.");

        var failures = new TestExpectation { NeverContains = { "không có tham số" } }.Evaluate(observed);

        Assert.Single(failures);
        Assert.Contains("Bỏ qua dòng 4", failures[0]);
    }

    [Fact]
    public void NguongThoiGian_BatHoiQuyHieuNang()
    {
        var slow = Ok();
        slow.ElapsedMs = 45_000;

        var failures = new TestExpectation { MaxMs = 30_000 }.Evaluate(slow);

        Assert.Single(failures);
        Assert.Contains("45000 ms", failures[0]);
        Assert.Contains("30000 ms", failures[0]);
    }

    [Fact]
    public void FileKetQua_KiemQuaHamTiemVao_KhongChamDia()
    {
        var expectation = new TestExpectation { FilesExist = { "C:/out/health.html", "C:/out/thieu.csv" } };

        var failures = expectation.Evaluate(Ok(), path => path.EndsWith("health.html", StringComparison.Ordinal));

        Assert.Single(failures);
        Assert.Contains("thieu.csv", failures[0]);
    }

    [Fact]
    public void NoErrors_BaoLoiDauTien()
    {
        var observed = Ok();
        observed.Errors.Add("Sheet A-101: số đã tồn tại");

        var failures = new TestExpectation { NoErrors = true }.Evaluate(observed);

        Assert.Single(failures);
        Assert.Contains("A-101", failures[0]);
    }

    [Fact]
    public void DocBoTest_TuJson()
    {
        var suite = TestSuite.Parse("""
        {
          "name": "Bộ mẫu",
          "model": "C:/models/Snowdon.rvt",
          "cases": [
            { "command": "HealthReport", "config": { "outputPath": "C:/out/h.html" },
              "expect": { "success": true, "minAffected": 0, "maxMs": 30000 } },
            { "name": "Đánh số cửa", "command": "AutoNumbering", "allowWrite": true, "skip": true, "skipReason": "cần model riêng" }
          ]
        }
        """);

        Assert.Equal("Bộ mẫu", suite.Name);
        Assert.Equal(2, suite.Cases.Count);
        Assert.Equal("HealthReport", suite.Cases[0].DisplayName);   // không có name → lấy tên lệnh
        Assert.Equal("Đánh số cửa", suite.Cases[1].DisplayName);
        Assert.True(suite.Cases[1].Skip);
        Assert.False(suite.Cases[0].AllowWrite);                     // mặc định không cho ghi
        Assert.Equal(30000, suite.Cases[0].Expect.MaxMs);
    }

    [Fact]
    public void BoTestThieuTenLenh_BaoLoiRoRang()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            TestSuite.Parse("""{ "cases": [ { "config": {} } ] }"""));

        Assert.Contains("thứ 1", ex.Message);
        Assert.Contains("command", ex.Message);
    }

    [Fact]
    public void BaoCao_DemDungVaXepCaTruotLenTruoc()
    {
        var outcomes = new List<TestOutcome>
        {
            new() { Name = "A", Command = "HealthReport", Summary = "xong" },
            new() { Name = "B", Command = "SleeveAuto", Failures = { "chạy 45000 ms, vượt ngưỡng 30000 ms" } },
            new() { Name = "C", Command = "AutoRoute", Skipped = true, SkipReason = "chưa có model" },
        };

        Assert.Equal(1, TestReport.PassedCount(outcomes));
        Assert.Equal(1, TestReport.FailedCount(outcomes));
        Assert.Equal(1, TestReport.SkippedCount(outcomes));
        Assert.Equal("1 đạt / 1 trượt / 1 bỏ qua trên 3 ca.", TestReport.Summarise(outcomes));

        var markdown = TestReport.ToMarkdown("Bộ mẫu", "C:/models/Snowdon.rvt", outcomes);
        Assert.Contains("## Trượt", markdown);
        Assert.True(markdown.IndexOf("## Trượt", StringComparison.Ordinal) < markdown.IndexOf("## Toàn bộ", StringComparison.Ordinal));
        Assert.Contains("vượt ngưỡng", markdown);
    }

    [Fact]
    public void Trx_DungSchemaVaDemDung()
    {
        var outcomes = new List<TestOutcome>
        {
            new() { Name = "A", Command = "HealthReport", ElapsedMs = 1200 },
            new() { Name = "B", Command = "SleeveAuto", Failures = { "trượt vì <lý do> & \"trích dẫn\"" } },
        };

        var trx = TestReport.ToTrx("Bộ mẫu", outcomes);

        Assert.Contains("http://microsoft.com/schemas/VisualStudio/TeamTest/2010", trx);
        Assert.Contains("total=\"2\"", trx);
        Assert.Contains("passed=\"1\"", trx);
        Assert.Contains("failed=\"1\"", trx);
        Assert.Contains("outcome=\"Failed\"", trx);
        // Ký tự đặc biệt trong lý do trượt phải được escape, nếu không TRX hỏng và CI không đọc được.
        Assert.Contains("&lt;lý do&gt;", trx);
        Assert.DoesNotContain("<lý do>", trx);
    }

    [Fact]
    public void Trx_KhongCoCaTruot_LaCompleted()
    {
        var trx = TestReport.ToTrx("Bộ mẫu", new List<TestOutcome>
        {
            new() { Name = "A", Command = "HealthReport" },
        });

        Assert.Contains("outcome=\"Completed\"", trx);
        Assert.Contains("outcome=\"Passed\"", trx);
    }
}
