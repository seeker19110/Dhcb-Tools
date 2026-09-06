using DhcbTools.Shared.Logic.Mep;
using Xunit;

namespace DhcbTools.Shared.Logic.Tests;

/// <summary>Phần quyết định và Summary/Messages của <c>SlopePipes</c> và <c>PipeKick</c> — trước đây nằm trong Core (§51).</summary>
public class SlopeAndKickPlannerTests
{
    private static SlopePipeInput Pipe(long id, double z0, double z1, double lengthMm = 4000, double dn = 100)
        => new SlopePipeInput(id, 0, 0, z0, lengthMm, 0, z1, dn);

    // ── SlopePlanner ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Start", true)]
    [InlineData("start", true)]
    [InlineData("End", false)]
    [InlineData(null, false)]
    [InlineData("", false)]
    public void LowerStart_ChiNhanChuStart(string? lowerEnd, bool expected)
    {
        Assert.Equal(expected, SlopePlanner.LowerStart(lowerEnd));
    }

    [Fact]
    public void Plan_OngDungThuan_BoQuaImLang()
    {
        var plan = SlopePlanner.Plan(new SlopePipeInput(1, 0, 0, 0, 0, 0, 3000, 100), null, false, 10, out var skip);
        Assert.Null(plan);
        Assert.Null(skip);
    }

    [Fact]
    public void Plan_OngGanThangDung_BoQuaCoThongBao()
    {
        // 1000 ngang, 1000 lên = 45° > 10°.
        var plan = SlopePlanner.Plan(new SlopePipeInput(7, 0, 0, 0, 1000, 0, 1000, 100), null, false, 10, out var skip);
        Assert.Null(plan);
        Assert.Equal("7: bỏ qua (gần thẳng đứng).", skip);
    }

    [Fact]
    public void Plan_OngNgangDN100_YeuCau1PhanTram_VaChuaDat()
    {
        var plan = SlopePlanner.Plan(Pipe(3, 0, 0), null, false, 10, out _);
        Assert.NotNull(plan);
        Assert.Equal(1.0, plan!.RequiredPercent);
        Assert.Equal(4000, plan.HorizontalMm, 6);
        Assert.True(plan.NeedsFix);
        Assert.Equal("dốc 0.00 % < 1.00 % yêu cầu", plan.Issue);
        // Hạ đầu cuối 40 mm, đầu đầu giữ nguyên.
        Assert.Equal(0, plan.NewZ0Mm, 6);
        Assert.Equal(-40, plan.NewZ1Mm, 6);
    }

    [Fact]
    public void Plan_HaDauDau_ThiDauCuoiGiuNguyen()
    {
        var plan = SlopePlanner.Plan(Pipe(3, 100, 100), 2.0, true, 10, out _);
        Assert.NotNull(plan);
        Assert.Equal(2.0, plan!.RequiredPercent);
        Assert.Equal(100, plan.NewZ1Mm, 6);
        Assert.Equal(100 - 80, plan.NewZ0Mm, 6);
    }

    [Fact]
    public void Plan_OngDaDoc_KhongCanSua()
    {
        // 4000 ngang, hạ 40 mm về cuối = đúng 1 %.
        var plan = SlopePlanner.Plan(Pipe(5, 40, 0), null, false, 10, out _);
        Assert.NotNull(plan);
        Assert.False(plan!.NeedsFix);
        Assert.Null(plan.Issue);
    }

    [Fact]
    public void Plan_DocNguocSoVoiDauHa_BaoDocNguoc()
    {
        // Hạ đầu cuối nhưng ống lại dốc về đầu đầu.
        var plan = SlopePlanner.Plan(Pipe(5, 0, 40), null, false, 10, out _);
        Assert.NotNull(plan);
        Assert.StartsWith("dốc ngược", plan!.Issue);
    }

    [Fact]
    public void Plan_NullNem()
    {
        Assert.Throws<ArgumentNullException>(() => SlopePlanner.Plan(null!, null, false, 10, out _));
    }

    [Fact]
    public void PlanAll_GomKetQuaVaGhiOngBoQua()
    {
        var messages = new List<string>();
        var plans = SlopePlanner.PlanAll(new[]
        {
            Pipe(1, 0, 0),
            new SlopePipeInput(2, 0, 0, 0, 0, 0, 3000, 100),        // đứng thuần: im lặng
            new SlopePipeInput(3, 0, 0, 0, 1000, 0, 1000, 100),     // gần đứng: ghi Messages
            Pipe(4, 40, 0),
        }, null, false, 10, messages);

        Assert.Equal(new long[] { 1, 4 }, plans.Select(p => p.Id).ToArray());
        Assert.Equal(new[] { "3: bỏ qua (gần thẳng đứng)." }, messages);
        Assert.Equal(1, plans.Count(p => p.NeedsFix));
    }

    [Fact]
    public void PlanAll_NullNem()
    {
        Assert.Throws<ArgumentNullException>(() => SlopePlanner.PlanAll(null!, null, false, 10, new List<string>()));
        Assert.Throws<ArgumentNullException>(() => SlopePlanner.PlanAll(Array.Empty<SlopePipeInput>(), null, false, 10, null!));
    }

    [Fact]
    public void Slope_SummaryVaDong()
    {
        var plan = SlopePlanner.Plan(Pipe(9, 0, 0, dn: 110), 1.5, false, 10, out _)!;
        Assert.Equal("Kiểm 3 ống: 1 chưa đạt dốc.", SlopePlanner.CheckSummary(3, 1));
        Assert.Equal("9 DN110: dốc 0.00 % < 1.50 % yêu cầu", SlopePlanner.CheckLine(plan));
        Assert.Equal("[Xem trước] Sẽ đặt dốc cho 1/3 ống (hạ cuối).", SlopePlanner.PreviewSummary(1, 3, false));
        Assert.Equal("[Xem trước] Sẽ đặt dốc cho 1/3 ống (hạ đầu).", SlopePlanner.PreviewSummary(1, 3, true));
        Assert.EndsWith("→ đặt 1.5 %", SlopePlanner.PreviewLine(plan));
        Assert.Equal("Đã đặt dốc 2/3 ống.", SlopePlanner.WriteSummary(2, 3));
        Assert.Equal("9: lỗi X (ống đã nối fitting hai đầu có thể không dịch được — tách đoạn trước).", SlopePlanner.WriteError(9, "lỗi X"));
    }

    // ── KickPlanner ──────────────────────────────────────────────────────────

    [Fact]
    public void Validate_OngNgan_BaoChieuDaiToiThieu()
    {
        // DN100, kick 300 mm cút 45°: along 300 + 2×1,5×100 + 200 = 800 mm.
        var reason = KickPlanner.Validate(700, 100, 300, 45, 100);
        Assert.NotNull(reason);
        Assert.StartsWith("Ống dài 700 mm, cần ≥ 800 mm", reason);
        Assert.Contains("kick 300 mm với cút 45°", reason);
    }

    [Fact]
    public void Validate_DiemBatDauQuaXa_BaoKhongNamTrongOng()
    {
        // 2000 mm đủ dài, nhưng 1500 + 300 + 300 > 2000.
        Assert.Equal("distanceFromStartMm quá lớn: kick không nằm trong ống.", KickPlanner.Validate(2000, 100, 300, 45, 1500));
    }

    [Fact]
    public void Validate_DuDieuKien_TraNull()
    {
        Assert.Null(KickPlanner.Validate(2000, 100, 300, 45, 500));
    }

    [Theory]
    [InlineData("Up", 1, 0, 0, 0, 1)]
    [InlineData("up", 1, 0, 0, 0, 1)]
    [InlineData(null, 1, 0, 0, 0, 1)]
    [InlineData("Sideways", 1, 0, 0, 0, 1)]   // hướng lạ → Up như trước
    [InlineData("Down", 1, 0, 0, 0, -1)]
    [InlineData("Left", 1, 0, 0, 1, 0)]       // ống theo +X: trái = +Y
    [InlineData("Right", 1, 0, 0, -1, 0)]
    [InlineData("Left", 0, 2, -1, 0, 0)]      // ống theo +Y (chưa chuẩn hoá): trái = -X
    [InlineData("Left", 0, 0, 0, 1, 0)]       // ống thẳng đứng: lấy +Y
    [InlineData("Right", 0, 0, 0, -1, 0)]
    public void OffsetDirection_TheoHuongOng(string? direction, double dirX, double dirY, double ex, double ey, double ez)
    {
        var (x, y, z) = KickPlanner.OffsetDirection(direction, dirX, dirY);
        Assert.Equal(ex, x, 9);
        Assert.Equal(ey, y, 9);
        Assert.Equal(ez, z, 9);
    }

    [Theory]
    [InlineData(100, 300, 300)]
    [InlineData(100, 0, 100)]
    [InlineData(32, 0, 50)]
    public void MiddleLengthMm_Kick90LayMaxDVa50(double dn, double along, double expected)
    {
        Assert.Equal(expected, KickPlanner.MiddleLengthMm(dn, along));
    }

    [Fact]
    public void Kick_SummaryVaGhiChu()
    {
        Assert.Equal("Kick Up 300 mm, cút 45°: đoạn chéo 424 mm, bắt đầu cách đầu ống 500 mm.", KickPlanner.Note("Up", 300, 45, 424.26, 500));
        Assert.Equal("[Xem trước] Sẽ chia ống 12 thành 3 đoạn + 2 cút.", KickPlanner.PreviewSummary("12"));
        Assert.Equal("Đã kick ống: 3 đoạn (1, 2, 3), 2/2 cút dựng được.", KickPlanner.WriteSummary(1, 2, 3, 2));
    }
}
