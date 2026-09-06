using DhcbTools.Shared.Logic.Setout;
using Xunit;

namespace DhcbTools.Shared.Logic.Tests;

/// <summary>
/// Phần quyết định của <c>SetoutExport</c> ngoài SetoutPlanner/SetoutCsv — trước đây nằm trong Core,
/// chỉ chạy được khi mở Revit thật. Lệnh này đưa toạ độ ra hiện trường nên từng câu cảnh báo và từng
/// nhánh chọn chiều transform đều có assert ở đây.
/// </summary>
public class SetoutExportLogicTests
{
    // ── curvePoints ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Ends", true, false)]
    [InlineData("ends", true, false)]
    [InlineData("Mid", false, true)]
    [InlineData("Both", true, true)]
    [InlineData("  both  ", true, true)]
    [InlineData(null, true, false)]     // rỗng = Ends
    [InlineData("", true, false)]
    [InlineData("   ", true, false)]
    public void TryParseCurvePoints_HopLe(string? raw, bool ends, bool mid)
    {
        Assert.True(SetoutExportLogic.TryParseCurvePoints(raw, out var e, out var m, out var error));
        Assert.Equal(ends, e);
        Assert.Equal(mid, m);
        Assert.Equal(string.Empty, error);
    }

    [Fact]
    public void TryParseCurvePoints_Sai_BaoRoKhongDoan()
    {
        Assert.False(SetoutExportLogic.TryParseCurvePoints("Middle", out var e, out var m, out var error));
        Assert.False(e);
        Assert.False(m);
        Assert.Equal("curvePoints \"Middle\" không hợp lệ. Hợp lệ: Ends (hai đầu), Mid (điểm giữa), Both.", error);
    }

    // ── coordinateSystem ─────────────────────────────────────────────────────

    [Theory]
    [InlineData("Survey", true)]
    [InlineData("shared", true)]
    [InlineData("Internal", false)]
    [InlineData("INTERNAL", false)]
    [InlineData(null, true)]
    [InlineData("", true)]
    public void TryParseCoordinateSystem_HopLe(string? raw, bool useSurvey)
    {
        Assert.True(SetoutExportLogic.TryParseCoordinateSystem(raw, out var survey, out var error));
        Assert.Equal(useSurvey, survey);
        Assert.Equal(string.Empty, error);
    }

    [Fact]
    public void TryParseCoordinateSystem_Sai_BaoRo()
    {
        Assert.False(SetoutExportLogic.TryParseCoordinateSystem("Project", out var survey, out var error));
        Assert.False(survey);
        Assert.Contains("coordinateSystem \"Project\" không hợp lệ", error);
    }

    // ── CurveAnchorKinds ─────────────────────────────────────────────────────

    [Fact]
    public void CurveAnchorKinds_ThuTuDauGiuaCuoi()
    {
        // Thứ tự quyết định {n} trong tên điểm — đổi là đổi tên trên máy toàn đạc.
        Assert.Equal(new[] { ("đầu", 0.0), ("cuối", 1.0) }, SetoutExportLogic.CurveAnchorKinds(true, false));
        Assert.Equal(new[] { ("giữa", 0.5) }, SetoutExportLogic.CurveAnchorKinds(false, true));
        Assert.Equal(new[] { ("đầu", 0.0), ("giữa", 0.5), ("cuối", 1.0) }, SetoutExportLogic.CurveAnchorKinds(true, true));
        Assert.Empty(SetoutExportLogic.CurveAnchorKinds(false, false));
    }

    // ── ChooseMatchingTransform ──────────────────────────────────────────────

    private static (double, double, double) Shift((double X, double Y, double Z) p) => (p.X + 100, p.Y + 50, p.Z);

    private static (double, double, double) Unshift((double X, double Y, double Z) p) => (p.X - 100, p.Y - 50, p.Z);

    [Fact]
    public void ChooseMatchingTransform_ChonUngVienDauTienKhopMoiDiemDo()
    {
        var probes = new[] { (0.0, 0.0, 0.0), (1.0, 0.0, 0.0) };
        var expected = new[] { (100.0, 50.0, 0.0), (101.0, 50.0, 0.0) };   // = Shift

        // Unshift đứng trước nhưng sai; Shift đứng sau và đúng → 1.
        var index = SetoutExportLogic.ChooseMatchingTransform(
            new Func<(double X, double Y, double Z), (double X, double Y, double Z)>[] { Unshift, Shift },
            probes, expected, 1e-6);

        Assert.Equal(1, index);
    }

    [Fact]
    public void ChooseMatchingTransform_KhongUngVienNaoKhop_TraAmMot()
    {
        var probes = new[] { (0.0, 0.0, 0.0) };
        var expected = new[] { (7.0, 7.0, 7.0) };

        var index = SetoutExportLogic.ChooseMatchingTransform(
            new Func<(double X, double Y, double Z), (double X, double Y, double Z)>[] { Unshift, Shift },
            probes, expected, 1e-6);

        Assert.Equal(-1, index);
    }

    [Fact]
    public void ChooseMatchingTransform_KhopMotDiemNhungTruotDiemKia_KhongChon()
    {
        // Chỉ đúng ở gốc mà sai ở điểm (1, 0, 0) là dấu hiệu chiều/độ xoay sai — phải dò ≥ 2 điểm.
        var probes = new[] { (0.0, 0.0, 0.0), (1.0, 0.0, 0.0) };
        var expected = new[] { (100.0, 50.0, 0.0), (99.0, 50.0, 0.0) };   // điểm hai đi ngược

        var index = SetoutExportLogic.ChooseMatchingTransform(
            new Func<(double X, double Y, double Z), (double X, double Y, double Z)>[] { Shift },
            probes, expected, 1e-6);

        Assert.Equal(-1, index);
    }

    [Fact]
    public void ChooseMatchingTransform_DungSaiChoPhepSaiSoNho()
    {
        var probes = new[] { (0.0, 0.0, 0.0) };
        var expected = new[] { (100.0, 50.0, 0.0004) };   // lệch 0,4 (đơn vị bất kỳ), dung sai 0,5

        Assert.Equal(0, SetoutExportLogic.ChooseMatchingTransform(
            new Func<(double X, double Y, double Z), (double X, double Y, double Z)>[] { Shift }, probes, expected, 0.5));
        Assert.Equal(-1, SetoutExportLogic.ChooseMatchingTransform(
            new Func<(double X, double Y, double Z), (double X, double Y, double Z)>[] { Shift }, probes, expected, 0.0001));
    }

    [Fact]
    public void ChooseMatchingTransform_DauVaoSai_Nem()
    {
        var one = new[] { (0.0, 0.0, 0.0) };
        var two = new[] { (0.0, 0.0, 0.0), (1.0, 0.0, 0.0) };
        var shift = new Func<(double X, double Y, double Z), (double X, double Y, double Z)>[] { Shift };

        Assert.Throws<ArgumentNullException>(() => SetoutExportLogic.ChooseMatchingTransform(null!, one, one, 1));
        Assert.Throws<ArgumentException>(() => SetoutExportLogic.ChooseMatchingTransform(shift, one, two, 1));
        Assert.Throws<ArgumentException>(() => SetoutExportLogic.ChooseMatchingTransform(shift, null!, one, 1));
        Assert.Throws<ArgumentException>(() => SetoutExportLogic.ChooseMatchingTransform(shift, one, null!, 1));
    }

    // ── SiteNotes ────────────────────────────────────────────────────────────

    [Fact]
    public void SiteNotes_CoToaDoChung_MotDongThongTin()
    {
        var notes = SetoutExportLogic.SiteNotes("Site A", 512345.6, 1234567.4, 12.4, 15.12345678);

        Assert.Single(notes);
        Assert.Equal("Site \"Site A\": gốc nội bộ ở E=512346 N=1234567 Z=12 mm, True North xoay 15.1235°.", notes[0]);
    }

    [Fact]
    public void SiteNotes_TrungHeNoiBo_ThemCanhBao()
    {
        // Mô hình chưa khai toạ độ chung: file trông như toạ độ khảo sát nhưng là toạ độ Revit — phải nói.
        var notes = SetoutExportLogic.SiteNotes("Internal", 0.2, -0.3, 0.1, 0);

        Assert.Equal(2, notes.Count);
        Assert.Equal(SetoutExportLogic.SurveyEqualsInternalNote, notes[1]);
    }

    [Theory]
    [InlineData(0.6, 0, 0, 0)]       // lệch E > 0,5 mm
    [InlineData(0, 0, 0, 1e-5)]      // có xoay
    [InlineData(0, 0, -0.7, 0)]      // lệch Z
    public void SiteNotes_ChiCanMotThanhPhanKhac_KhongCanhBao(double e, double n, double z, double angle)
    {
        Assert.Single(SetoutExportLogic.SiteNotes("S", e, n, z, angle));
    }

    // ── Thông báo ────────────────────────────────────────────────────────────

    [Fact]
    public void MissingIdsNote_KhongThieu_TraNull()
    {
        Assert.Null(SetoutExportLogic.MissingIdsNote(null));
        Assert.Null(SetoutExportLogic.MissingIdsNote(new long[0]));
    }

    [Fact]
    public void MissingIdsNote_LietKeToiDa20()
    {
        Assert.Equal("2 Id không có trong mô hình: 5, 9.", SetoutExportLogic.MissingIdsNote(new long[] { 5, 9 }));

        var many = Enumerable.Range(1, 21).Select(i => (long)i).ToList();
        var note = SetoutExportLogic.MissingIdsNote(many);

        Assert.StartsWith("21 Id không có trong mô hình: 1, 2,", note);
        Assert.EndsWith("20, ….", note);
    }

    [Fact]
    public void UnknownCategoriesError_LietKeVaChiCachTra()
    {
        var error = SetoutExportLogic.UnknownCategoriesError(new[] { "Cot", "Dam" });

        Assert.Equal("Category không có: Cot, Dam. Tra tên category có thật bằng query categories hoặc parameters_of.", error);
        Assert.StartsWith("Category không có: .", SetoutExportLogic.UnknownCategoriesError(null!));
    }

    [Fact]
    public void InputDescription_TheoNguonPhanTu()
    {
        Assert.Equal("phần tử theo elementIds", SetoutExportLogic.InputDescription(true, true));
        Assert.Equal("điểm định vị (phần tử theo bộ lọc)", SetoutExportLogic.InputDescription(false, false));
        Assert.Equal("điểm định vị (phần tử theo bộ lọc, giao trục)", SetoutExportLogic.InputDescription(false, true));
    }

    [Fact]
    public void ColumnLetters_TheoThuTuMayGoi()
    {
        Assert.True(SetoutColumns.TryParse("PENZDCLI", out var columns, out _));

        Assert.Equal("PENZDCLI", SetoutExportLogic.ColumnLetters(columns));
        Assert.Equal(string.Empty, SetoutExportLogic.ColumnLetters(null!));
        Assert.Equal("?", SetoutExportLogic.LetterOf((SetoutColumn)999));
    }

    [Fact]
    public void Summary_CoGiaoTruc()
    {
        Assert.True(SetoutColumns.TryParse("PNEZD", out var columns, out _));

        var s = SetoutExportLogic.Summary(60, 48, 24, true, 12, @"C:\out\setout.csv", true, true, columns);

        Assert.Equal("Xuất 60 điểm định vị (48 điểm của 24 phần tử, 12 giao trục) → \"C:\\out\\setout.csv\" — hệ Survey, m, cột PNEZD.", s);
    }

    [Fact]
    public void Summary_KhongGiaoTruc_HeInternalMm()
    {
        Assert.True(SetoutColumns.TryParse("PENZDI", out var columns, out _));

        var s = SetoutExportLogic.Summary(546, 546, 546, false, 0, "x.csv", false, false, columns);

        Assert.Equal("Xuất 546 điểm định vị (546 điểm của 546 phần tử) → \"x.csv\" — hệ Internal, mm, cột PENZDI.", s);
    }

    [Fact]
    public void TrailingNotes_KhongCoGi_Rong()
    {
        Assert.Empty(SetoutExportLogic.TrailingNotes(null, 0, 0, 0, 0));
        Assert.Empty(SetoutExportLogic.TrailingNotes("  ", 0, 0, 0, 0));
    }

    [Fact]
    public void TrailingNotes_DuBonLoaiTheoThuTu()
    {
        var notes = SetoutExportLogic.TrailingNotes(@"C:\p.dxf", 3, 2, 1, 4);

        Assert.Equal(5, notes.Count);
        Assert.Equal("DXF điểm: \"C:\\p.dxf\" (layer DHCB-<mã> và DHCB-<mã>-TEN).", notes[0]);
        Assert.Equal("3 phần tử ngoài bộ lọc tầng/family/type.", notes[1]);
        Assert.StartsWith("2 phần tử không có điểm/đường đặt", notes[2]);
        Assert.Equal("1 phần tử không có hình học nào để lấy điểm, đã bỏ qua.", notes[3]);
        Assert.Equal("4 trục cong bị bỏ qua khi tính giao trục (chỉ xét trục thẳng).", notes[4]);
    }
}
