using DhcbTools.Shared.Logic.Progress;
using Xunit;

namespace DhcbTools.Shared.Logic.Tests;

/// <summary>
/// Phần quyết định, Summary, Messages và HTML của <c>ProgressReport</c> — trước đây trong Core, chỉ kiểm
/// được khi mở Revit. Báo cáo này lên bàn ban chỉ huy công trường nên từng câu có assert.
/// </summary>
public class ProgressReportLogicTests
{
    private static readonly DateTime Now = new(2026, 9, 6, 8, 30, 0);

    private static List<StatusItem> SampleItems() => new()
    {
        new StatusItem("L1", ConstructionStage.DaNghiemThu, 1000, new DateTime(2026, 8, 3), 1),
        new StatusItem("L1", ConstructionStage.DaLap, 2000, new DateTime(2026, 8, 12), 2),
        new StatusItem("L1", ConstructionStage.DangLap, 500, null, 3),
        new StatusItem("L2", ConstructionStage.ChuaLap, 1500, null, 4),
        new StatusItem("L2", ConstructionStage.ChuaCoDuLieu, 0, null, 5),
        new StatusItem("L2", ConstructionStage.DaLap, 0, null, 6),   // đã lắp nhưng không có ngày
    };

    // ── groupBy ──────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Level", ProgressGroupBy.Level)]
    [InlineData("level", ProgressGroupBy.Level)]
    [InlineData("System", ProgressGroupBy.System)]
    [InlineData("CATEGORY", ProgressGroupBy.Category)]
    [InlineData(null, ProgressGroupBy.Level)]
    [InlineData("", ProgressGroupBy.Level)]
    [InlineData("  ", ProgressGroupBy.Level)]
    public void TryParseGroupBy_HopLe(string? raw, ProgressGroupBy expected)
    {
        Assert.True(ProgressReportLogic.TryParseGroupBy(raw, out var groupBy, out var error));
        Assert.Equal(expected, groupBy);
        Assert.Equal(string.Empty, error);
    }

    [Fact]
    public void TryParseGroupBy_Sai_BaoRoDungCauDaChayThat()
    {
        // Câu này đã có ca chạy trong Revit ("Báo cáo tiến độ — báo lỗi rõ khi groupBy sai"): ghim để không đổi.
        Assert.False(ProgressReportLogic.TryParseGroupBy("Tang", out _, out var error));
        Assert.Equal("groupBy \"Tang\" không hợp lệ. Hợp lệ: Level (tầng), System (hệ), Category.", error);
    }

    [Fact]
    public void GroupHeader_TheoCachGop()
    {
        Assert.Equal("Tầng", ProgressReportLogic.GroupHeader(ProgressGroupBy.Level));
        Assert.Equal("Hệ", ProgressReportLogic.GroupHeader(ProgressGroupBy.System));
        Assert.Equal("Category", ProgressReportLogic.GroupHeader(ProgressGroupBy.Category));
        Assert.Equal("Tầng", ProgressReportLogic.GroupHeader((ProgressGroupBy)99));
    }

    [Fact]
    public void GroupOf_ThieuThongTin_VaoNhomKhongThayViRoiKhoiBaoCao()
    {
        Assert.Equal("L1", ProgressReportLogic.GroupOf(ProgressGroupBy.Level, "L1", "HVAC", "Ducts"));
        Assert.Equal("(không tầng)", ProgressReportLogic.GroupOf(ProgressGroupBy.Level, "", "HVAC", "Ducts"));
        Assert.Equal("HVAC", ProgressReportLogic.GroupOf(ProgressGroupBy.System, "L1", "HVAC", "Ducts"));
        Assert.Equal("(không hệ)", ProgressReportLogic.GroupOf(ProgressGroupBy.System, "L1", null, "Ducts"));
        Assert.Equal("Ducts", ProgressReportLogic.GroupOf(ProgressGroupBy.Category, "L1", "HVAC", "Ducts"));
        Assert.Equal("(không category)", ProgressReportLogic.GroupOf(ProgressGroupBy.Category, "L1", "HVAC", " "));
    }

    // ── Thông báo ────────────────────────────────────────────────────────────

    [Fact]
    public void UnknownCategoriesError_VaUnreadableEntry()
    {
        Assert.Equal("Category không có: A, B.", ProgressReportLogic.UnknownCategoriesError(new[] { "A", "B" }));
        Assert.Equal("Category không có: .", ProgressReportLogic.UnknownCategoriesError(null!));
        Assert.Equal("123: \"xong rồi\"", ProgressReportLogic.UnreadableEntry(123, "xong rồi"));
        Assert.Equal("7: \"\"", ProgressReportLogic.UnreadableEntry(7, null));
    }

    [Fact]
    public void NoStatusParameterMessage_NoiRo0PhanTramLaVeThamSoKhongPhaiCongTruong()
    {
        var m = ProgressReportLogic.NoStatusParameterMessage("E-PARAM-MISSING: …", 142);

        Assert.StartsWith("E-PARAM-MISSING: …", m);
        Assert.Contains("Không phần tử nào trong 142 phần tử của phạm vi có tham số này", m);
        Assert.Contains("nói về tham số chứ không nói về công trường", m);
        Assert.Contains("DictionaryLearn", m);
    }

    // ── Summary / Notes ──────────────────────────────────────────────────────

    [Fact]
    public void Summary_CoChieuDai()
    {
        var items = SampleItems();
        var total = StatusRoll.Total(items);

        var s = ProgressReportLogic.Summary(total, 2, ProgressGroupBy.Level, @"C:\out\tien-do.html");

        // 3/6 đã lắp trở lên; theo chiều dài: (1000 + 2000 + 0) / 5000 = 60 %.
        Assert.Equal("Tiến độ 50.0% đã lắp trở lên (3/6 cấu kiện, 60.0% theo chiều dài), 2 nhóm theo tầng → \"C:\\out\\tien-do.html\".", s);
    }

    [Fact]
    public void Summary_KhongChieuDai_KhongCoPhanTheoChieuDai()
    {
        var items = new List<StatusItem> { new("L1", ConstructionStage.DaLap), new("L1", ConstructionStage.ChuaLap) };

        var s = ProgressReportLogic.Summary(StatusRoll.Total(items), 1, ProgressGroupBy.System, "x.html");

        Assert.Equal("Tiến độ 50.0% đã lắp trở lên (1/2 cấu kiện), 1 nhóm theo hệ → \"x.html\".", s);
    }

    [Fact]
    public void Summary_TotalNull_Nem()
    {
        Assert.Throws<ArgumentNullException>(() => ProgressReportLogic.Summary(null!, 0, ProgressGroupBy.Level, "x"));
    }

    [Fact]
    public void Notes_DuMoiLoaiTheoThuTu()
    {
        var items = SampleItems();
        var total = StatusRoll.Total(items);
        var series = WeeklyProgress.Series(items);

        var notes = ProgressReportLogic.Notes(total, series, new[] { "5: \"?\"" }, 3, @"C:\out\tien-do.csv");

        Assert.Equal(6, notes.Count);
        Assert.Equal("Đã nghiệm thu: 1 · đã lắp: 2 · đang lắp: 1 · chưa lắp: 1.", notes[0]);
        Assert.Equal("1/6 cấu kiện chưa ai ghi nhận trạng thái — vẫn nằm trong mẫu số của phần trăm (chưa nhập thì chưa lắp).", notes[1]);
        Assert.Equal("1 cấu kiện đã lắp nhưng không có ngày nên không lên được biểu đồ tuần.", notes[2]);
        Assert.Equal("Giá trị trạng thái không đọc được ở 1 phần tử (đếm như chưa có dữ liệu): 5: \"?\"", notes[3]);
        Assert.Equal("3 phần tử ngoài bộ lọc tầng/hệ.", notes[4]);
        Assert.Equal("CSV: \"C:\\out\\tien-do.csv\".", notes[5]);
    }

    [Fact]
    public void Notes_KhongCoGiDacBiet_ChiMotDong()
    {
        var items = new List<StatusItem> { new("L1", ConstructionStage.DaLap, 0, new DateTime(2026, 8, 3)) };

        var notes = ProgressReportLogic.Notes(StatusRoll.Total(items), WeeklyProgress.Series(items), null!, 0, null);

        Assert.Single(notes);
    }

    [Fact]
    public void Notes_DauVaoNull_Nem()
    {
        var series = new ProgressSeries();
        Assert.Throws<ArgumentNullException>(() => ProgressReportLogic.Notes(null!, series, null!, 0, null));
        Assert.Throws<ArgumentNullException>(() => ProgressReportLogic.Notes(new StatusRollRow("t"), null!, null!, 0, null));
    }

    // ── HTML ─────────────────────────────────────────────────────────────────

    private static string SampleHtml(IReadOnlyList<string>? unreadable = null, string? statusParameter = null)
    {
        var items = SampleItems();
        return ProgressReportLogic.Html(
            "Dự án <A&B>", statusParameter, ProgressGroupBy.Level,
            StatusRoll.By(items), StatusRoll.Total(items), WeeklyProgress.Series(items),
            unreadable ?? Array.Empty<string>(), Now);
    }

    [Fact]
    public void Html_TieuDeEscape_VaDongLapLucTheoGioTiem()
    {
        var html = SampleHtml();

        Assert.Contains("<title>Tiến độ thi công — Dự án &lt;A&amp;B&gt;</title>", html);
        Assert.Contains("<p>Lập lúc 06/09/2026 08:30 · gộp theo tầng · 6 cấu kiện trong phạm vi</p>", html);
    }

    [Fact]
    public void Html_PhanTramTongVaTheoChieuDai()
    {
        var html = SampleHtml();

        Assert.Contains("<strong>50.0% đã lắp trở lên</strong> (3/6 cấu kiện)", html);
        Assert.Contains("<strong>60.0% theo chiều dài</strong> (3.0/5.0 m)", html);
        Assert.Contains("16.7% đã nghiệm thu</p>", html);
    }

    [Fact]
    public void Html_ChuaCoDuLieu_CoKhungLuuYVeMauSo()
    {
        var html = SampleHtml();

        Assert.Contains("1/6 cấu kiện <strong>chưa ai ghi nhận trạng thái</strong>", html);
    }

    [Fact]
    public void Html_BangTheoNhom_CoDongTongVaCotChieuDaiGachNgangKhiKhongCo()
    {
        var html = SampleHtml();

        Assert.Contains("<th>Tầng</th><th>Tổng</th><th>Chưa lắp</th><th>Đang lắp</th><th>Đã lắp</th><th>Đã nghiệm thu</th>", html);
        Assert.Contains("<tr><td>L1</td><td>3</td>", html);
        Assert.Contains("<tr><td>L2</td><td>3</td>", html);
        Assert.Contains("<tr><td>Tổng</td><td>6</td>", html);
        // L2 chỉ có 1500 mm ở "chưa lắp" → có chiều dài → 0.0; nhóm không có chiều dài nào mới ra "—".
        var noLength = new List<StatusItem> { new("L9", ConstructionStage.DaLap) };
        var htmlNoLength = ProgressReportLogic.Html("t", null, ProgressGroupBy.Level,
            StatusRoll.By(noLength), StatusRoll.Total(noLength), WeeklyProgress.Series(noLength), Array.Empty<string>(), Now);
        Assert.Contains("<td>—</td>", htmlNoLength);
        Assert.DoesNotContain("theo chiều dài</strong>", htmlNoLength);
    }

    [Fact]
    public void Html_LuyKeTuan_LienTuanDungYen()
    {
        var html = SampleHtml();

        // Hai mốc 03/08 (thứ Hai) và 12/08 (thứ Tư → tuần 10/08): tuần 03/08 và 10/08 đều có mặt.
        Assert.Contains("<tr><td>03/08/2026</td><td>1</td><td>1</td><td>16.7</td>", html);
        Assert.Contains("<tr><td>10/08/2026</td><td>1</td><td>2</td><td>33.3</td>", html);
        Assert.Contains("1 cấu kiện đã lắp nhưng <strong>không có ngày</strong>", html);
    }

    [Fact]
    public void Html_KhongCoNgayNao_NoiChuaDungDuocChuoiTuan()
    {
        var items = new List<StatusItem> { new("L1", ConstructionStage.ChuaLap) };

        var html = ProgressReportLogic.Html("t", null, ProgressGroupBy.Category,
            StatusRoll.By(items), StatusRoll.Total(items), WeeklyProgress.Series(items), Array.Empty<string>(), Now);

        Assert.Contains("chưa dựng được chuỗi theo tuần", html);
        Assert.DoesNotContain("không có ngày", html);
        Assert.DoesNotContain("chưa ai ghi nhận", html);
        Assert.Contains("<h2>Theo category</h2>", html);
    }

    [Fact]
    public void Html_GiaTriKhongDocDuoc_EscapeVaLietKe()
    {
        var html = SampleHtml(new[] { "5: \"<x>\"" });

        Assert.Contains("Giá trị trạng thái không đọc được ở 1 phần tử, đếm như chưa có dữ liệu: 5: &quot;&lt;x&gt;&quot;", html);
    }

    [Fact]
    public void Html_ChanTrang_TenThamSoHoacTheoTuDien()
    {
        Assert.Contains("tham số trạng thái: (theo từ điển constructionStatus)</p>", SampleHtml());
        Assert.Contains("tham số trạng thái: DHCB_TrangThai</p>", SampleHtml(statusParameter: "DHCB_TrangThai"));
        Assert.Contains("<title>Tiến độ thi công — </title>", ProgressReportLogic.Html(null, null, ProgressGroupBy.Level,
            new List<StatusRollRow>(), new StatusRollRow("Tổng"), new ProgressSeries(), null!, Now));
    }

    [Fact]
    public void Html_DauVaoNull_Nem()
    {
        var row = new StatusRollRow("t");
        var series = new ProgressSeries();
        Assert.Throws<ArgumentNullException>(() => ProgressReportLogic.Html("t", null, ProgressGroupBy.Level, null!, row, series, null!, Now));
        Assert.Throws<ArgumentNullException>(() => ProgressReportLogic.Html("t", null, ProgressGroupBy.Level, new List<StatusRollRow>(), null!, series, null!, Now));
        Assert.Throws<ArgumentNullException>(() => ProgressReportLogic.Html("t", null, ProgressGroupBy.Level, new List<StatusRollRow>(), row, null!, null!, Now));
    }
}
