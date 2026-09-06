using DhcbTools.Shared.Logic.Mep;
using Xunit;

namespace DhcbTools.Shared.Logic.Tests;

/// <summary>
/// Phần quyết định của <c>SleeveAuto</c> — trước đây nằm trong <c>Core/MEPF/SleeveCommand.cs</c>,
/// chỉ chạy được khi mở Revit thật nên chưa từng có test. Mỗi ca ở đây là một hành vi mà bản Core
/// cũ đã có (và đã lộ lỗi khi chạy thật, xem bang-chung-test.md §6/§14), nay ghim lại bằng assert.
/// </summary>
public class SleevePlannerTests
{
    // ── ClipLineToBox (Liang–Barsky) ─────────────────────────────────────────

    [Fact]
    public void ClipLineToBox_DoanXuyenQuaHop_TraKhoangThamSoBenTrong()
    {
        var box = new Box3(4, -1, -1, 6, 1, 1);   // tường dày 2 ft, tâm x = 5

        var hit = SleevePlanner.ClipLineToBox(0, 0, 0, 10, 0, 0, box, out var t0, out var t1);

        Assert.True(hit);
        Assert.Equal(0.4, t0, 9);
        Assert.Equal(0.6, t1, 9);
        // Trung điểm đoạn nằm trong hộp = tâm tường — đúng chỗ đặt sleeve.
        Assert.Equal(5.0, (t0 + t1) * 0.5 * 10, 9);
    }

    [Fact]
    public void ClipLineToBox_DoanKhongChamHop_TraFalse()
    {
        var box = new Box3(4, -1, -1, 6, 1, 1);

        Assert.False(SleevePlanner.ClipLineToBox(0, 5, 0, 10, 5, 0, box, out _, out _));   // đi ngang bên trên
        Assert.False(SleevePlanner.ClipLineToBox(0, 0, 0, 3, 0, 0, box, out _, out _));    // dừng trước tường
        Assert.False(SleevePlanner.ClipLineToBox(7, 0, 0, 10, 0, 0, box, out _, out _));   // bắt đầu sau tường
    }

    [Fact]
    public void ClipLineToBox_DoanSongSongMotTruc_VaNamNgoaiTrucDo_TraFalse()
    {
        // delta.z = 0 (song song mặt phẳng XY) và z = 5 nằm ngoài [-1, 1]: nhánh "song song và ở ngoài".
        var box = new Box3(4, -1, -1, 6, 1, 1);

        Assert.False(SleevePlanner.ClipLineToBox(0, 0, 5, 10, 0, 5, box, out _, out _));
    }

    [Fact]
    public void ClipLineToBox_DoanNguocChieu_VanCatDung()
    {
        // p0 ở x = 10 đi về x = 0: tA > tB phải hoán đổi, không thì t0 > t1 và báo trượt oan.
        var box = new Box3(4, -1, -1, 6, 1, 1);

        var hit = SleevePlanner.ClipLineToBox(10, 0, 0, 0, 0, 0, box, out var t0, out var t1);

        Assert.True(hit);
        Assert.Equal(0.4, t0, 9);
        Assert.Equal(0.6, t1, 9);
    }

    [Fact]
    public void ClipLineToBox_DoanNamTronTrongHop_TraCaKhoang()
    {
        var box = new Box3(-10, -10, -10, 10, 10, 10);

        var hit = SleevePlanner.ClipLineToBox(-1, 0, 0, 1, 0, 0, box, out var t0, out var t1);

        Assert.True(hit);
        Assert.Equal(0.0, t0);
        Assert.Equal(1.0, t1);
    }

    [Fact]
    public void ClipLineToBox_HopNull_Nem()
    {
        Assert.Throws<ArgumentNullException>(() =>
            SleevePlanner.ClipLineToBox(0, 0, 0, 1, 1, 1, null!, out _, out _));
    }

    // ── TransformedBox ───────────────────────────────────────────────────────

    [Fact]
    public void TransformedBox_KhongCoPhepBienDoi_TraChinhHopDo()
    {
        var box = new Box3(0, 0, 0, 1, 2, 3);

        Assert.Same(box, SleevePlanner.TransformedBox(box, null));
    }

    [Fact]
    public void TransformedBox_XoayLink90Do_HopDungLaiTuTamDinh()
    {
        // Hộp 10 × 2 dọc trục X. Xoay 90° quanh Z: x' = -y, y' = x → hộp phải thành 2 × 10 dọc trục Y.
        // Nếu chỉ biến đổi hai điểm min/max thì được (-0,0)…(-2,10): rộng 2 theo X nhưng lệch sang âm — sai.
        var box = new Box3(0, 0, 0, 10, 2, 1);

        var rotated = SleevePlanner.TransformedBox(box, (x, y, z) => (-y, x, z));

        Assert.Equal(-2, rotated.MinX, 9);
        Assert.Equal(0, rotated.MaxX, 9);
        Assert.Equal(0, rotated.MinY, 9);
        Assert.Equal(10, rotated.MaxY, 9);
        Assert.Equal(0, rotated.MinZ, 9);
        Assert.Equal(1, rotated.MaxZ, 9);
    }

    [Fact]
    public void TransformedBox_DoiLink_HopDoiTheo()
    {
        var box = new Box3(0, 0, 0, 1, 1, 1);

        var moved = SleevePlanner.TransformedBox(box, (x, y, z) => (x + 100, y - 50, z + 3));

        Assert.Equal(100, moved.MinX);
        Assert.Equal(101, moved.MaxX);
        Assert.Equal(-50, moved.MinY);
        Assert.Equal(-49, moved.MaxY);
        Assert.Equal(3, moved.MinZ);
        Assert.Equal(4, moved.MaxZ);
    }

    [Fact]
    public void TransformedBox_HopNull_Nem()
    {
        Assert.Throws<ArgumentNullException>(() => SleevePlanner.TransformedBox(null!, null));
    }

    // ── Lọc category / host / link ───────────────────────────────────────────

    [Theory]
    [InlineData("OST_DuctCurves", "Duct")]
    [InlineData("OST_PipeCurves", "Pipe")]
    [InlineData("OST_CableTray", "CableTray")]
    [InlineData("OST_Conduit", "Conduit")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void ShortCategoryName_BoTienToVaHauTo(string? bic, string expected)
    {
        Assert.Equal(expected, SleevePlanner.ShortCategoryName(bic));
    }

    [Fact]
    public void CategoryIncluded_BoLocRong_LayTatCa()
    {
        Assert.True(SleevePlanner.CategoryIncluded("Duct", null));
        Assert.True(SleevePlanner.CategoryIncluded("Duct", new string[0]));
    }

    [Theory]
    [InlineData("duct", true)]         // không phân biệt hoa thường
    [InlineData("DuctCurves", true)]   // người dùng gõ tên dài hơn tên ngắn — khớp chiều ngược
    [InlineData("Pipe", false)]
    [InlineData("Cable", false)]
    public void CategoryIncluded_SoKhopHaiChieu(string filter, bool expected)
    {
        Assert.Equal(expected, SleevePlanner.CategoryIncluded("Duct", new[] { filter }));
    }

    [Fact]
    public void CategoryIncluded_MucRongTrongBoLoc_LaKyTuDaiDien()
    {
        // Hành vi cũ: IndexOf("") ≥ 0 nên một mục rỗng kéo mọi category vào. Giữ nguyên để config
        // đang chạy ngoài hiện trường không đổi kết quả sau khi tách.
        Assert.True(SleevePlanner.CategoryIncluded("Duct", new[] { "Pipe", "" }));
        Assert.True(SleevePlanner.CategoryIncluded("Duct", new[] { "   " }));
    }

    [Fact]
    public void HostTypeMatches_BoLocRong_KhopMoiHost()
    {
        Assert.True(SleevePlanner.HostTypeMatches("Tường gạch 200", null));
        Assert.True(SleevePlanner.HostTypeMatches(null, new string[0]));
    }

    [Theory]
    [InlineData("Tường gạch 200", "gạch", true)]
    [InlineData("Tường gạch 200", "GẠCH", true)]
    [InlineData("Tường gạch 200", "bê tông", false)]
    [InlineData("", "gạch", false)]      // host không có type name → không khớp bộ lọc có nội dung
    [InlineData(null, "gạch", false)]
    [InlineData("Bất kỳ", "", true)]      // mục rỗng = ký tự đại diện
    public void HostTypeMatches_ChuaChuoiKhongPhanBietHoaThuong(string? typeName, string filter, bool expected)
    {
        Assert.Equal(expected, SleevePlanner.HostTypeMatches(typeName, new[] { filter }));
    }

    [Fact]
    public void LinkNameMatches_CungLuatVoiHostType()
    {
        Assert.True(SleevePlanner.LinkNameMatches("ARC-L01.rvt", new[] { "arc" }));
        Assert.False(SleevePlanner.LinkNameMatches("STR-L01.rvt", new[] { "arc" }));
        Assert.True(SleevePlanner.LinkNameMatches("STR-L01.rvt", null));
    }

    // ── SizeFrom ─────────────────────────────────────────────────────────────

    [Fact]
    public void SizeFrom_OngTron_DuongKinhQuyetDinhCaHaiChieu()
    {
        var size = SleevePlanner.SizeFrom(outerDiameterFt: 1.0, widthFt: 5.0, heightFt: 5.0, clearanceEachSideFt: 0.1);

        Assert.True(size.Resolved);
        Assert.Equal(1.2, size.WidthFt, 9);
        Assert.Equal(1.2, size.HeightFt, 9);
    }

    [Fact]
    public void SizeFrom_OngChuNhat_RongCaoRieng()
    {
        var size = SleevePlanner.SizeFrom(null, 2.0, 1.0, 0.25);

        Assert.True(size.Resolved);
        Assert.Equal(2.5, size.WidthFt, 9);
        Assert.Equal(1.5, size.HeightFt, 9);
    }

    [Fact]
    public void SizeFrom_ChiCoMotChieu_ChieuKiaDungCoTam_VanTinhLaTraDuoc()
    {
        var size = SleevePlanner.SizeFrom(null, 2.0, null, 0);

        Assert.True(size.Resolved);
        Assert.Equal(2.0, size.WidthFt);
        Assert.Equal(SleevePlanner.FallbackSizeFt, size.HeightFt);
    }

    [Fact]
    public void SizeFrom_KhongTraDuocGi_BaoResolvedFalse_DeNguoiGoiPhaiCanhBao()
    {
        // Đây là lỗi im lặng của bản đầu: dùng 6 inch mặc định mà không ai biết → sleeve sai cỡ.
        var size = SleevePlanner.SizeFrom(null, null, null, 0.1);

        Assert.False(size.Resolved);
        Assert.Equal(SleevePlanner.FallbackSizeFt, size.WidthFt);
        Assert.Equal(SleevePlanner.FallbackSizeFt, size.HeightFt);
    }

    [Fact]
    public void SizeFrom_GiaTriKhongDuong_CoiNhuKhongCo()
    {
        Assert.False(SleevePlanner.SizeFrom(0, -1, 0, 0).Resolved);
        // Đường kính 0 thì rơi xuống rộng/cao.
        Assert.True(SleevePlanner.SizeFrom(0, 1, null, 0).Resolved);
    }

    [Fact]
    public void SizeFrom_KhoangHoAm_CoiNhuKhong()
    {
        var size = SleevePlanner.SizeFrom(1.0, null, null, -5);

        Assert.Equal(1.0, size.WidthFt);
    }

    // ── AddDistinctReason ────────────────────────────────────────────────────

    [Fact]
    public void AddDistinctReason_KhongTrung_VaKhongVuotTran()
    {
        var reasons = new List<string>();

        SleevePlanner.AddDistinctReason(reasons, "a");
        SleevePlanner.AddDistinctReason(reasons, "a");
        for (var i = 0; i < 10; i++)
        {
            SleevePlanner.AddDistinctReason(reasons, "lý do " + i);
        }

        Assert.Equal(SleevePlanner.MaxFailureReasons, reasons.Count);
        Assert.Equal("a", reasons[0]);
        Assert.Single(reasons, "a");
    }

    [Fact]
    public void AddDistinctReason_DanhSachNull_Nem()
    {
        Assert.Throws<ArgumentNullException>(() => SleevePlanner.AddDistinctReason(null!, "x"));
    }

    // ── WhyNothing ───────────────────────────────────────────────────────────

    [Fact]
    public void WhyNothing_DaCoSleeve_NoiDungChuyenDangXayRa()
    {
        // Chạy lần hai: giao cắt còn nguyên, chỉ là đã có sleeve — không được đổ cho "không có giao cắt".
        var why = SleevePlanner.WhyNothing(10, 5, 100, 7, true);

        Assert.Equal("Bỏ qua, đã có sleeve: 7 vị trí.", why);
    }

    [Fact]
    public void WhyNothing_KhongCoHost_ChiRaLinkHayCoIncludeLinked()
    {
        Assert.Contains("kiểm lại link đã nạp chưa", SleevePlanner.WhyNothing(0, 0, 100, 0, true));
        Assert.Contains("includeLinkedModels đang tắt", SleevePlanner.WhyNothing(0, 0, 100, 0, false));
    }

    [Fact]
    public void WhyNothing_CoHostMaKhongGiao_NoiSoLuongVaGoiYNguyenNhan()
    {
        var why = SleevePlanner.WhyNothing(12, 340, 1053, 0, true);

        Assert.Contains("12 tường/sàn trong file + 340 từ model liên kết trên 1053 phần tử MEP", why);
        Assert.Contains("hostTypeNames", why);
    }

    // ── HostSourceNotes ──────────────────────────────────────────────────────

    [Fact]
    public void HostSourceNotes_DaDatDuoc_ChiCoDongNguonVaLink()
    {
        var notes = SleevePlanner.HostSourceNotes(12, 340, new[] { "ARC: 340 tường/sàn" }, placedCount: 5, mepCount: 100, includeLinkedModels: true);

        Assert.Equal(2, notes.Count);
        Assert.Equal("Tường/sàn xét tới: 12 trong file, 340 từ model liên kết.", notes[0]);
        Assert.Equal("  Link — ARC: 340 tường/sàn", notes[1]);
    }

    [Fact]
    public void HostSourceNotes_KhongDatDuocVaKhongCoHost_GiaiThichTheoIncludeLinked()
    {
        var off = SleevePlanner.HostSourceNotes(0, 0, null, 0, 100, false);
        var on = SleevePlanner.HostSourceNotes(0, 0, null, 0, 100, true);

        Assert.Contains("includeLinkedModels đang tắt", off[^1]);
        Assert.Contains("Manage Links", on[^1]);
    }

    [Fact]
    public void HostSourceNotes_KhongDatDuocMaCoHost_NoiSoLuong()
    {
        var notes = SleevePlanner.HostSourceNotes(3, 4, null, 0, 50, true);

        Assert.Contains("Có 7 tường/sàn và 50 phần tử MEP", notes[^1]);
    }

    // ── Cảnh báo & Summary ───────────────────────────────────────────────────

    [Fact]
    public void UnknownSizeWarning_KhongCo_TraNull()
    {
        Assert.Null(SleevePlanner.UnknownSizeWarning(null, "gợi ý"));
        Assert.Null(SleevePlanner.UnknownSizeWarning(new long[0], "gợi ý"));
    }

    [Fact]
    public void UnknownSizeWarning_LietKeToiDa20Id_RoiBaCham()
    {
        var ids = Enumerable.Range(1, 25).Select(i => (long)i).ToList();

        var warning = SleevePlanner.UnknownSizeWarning(ids, "GỢI Ý");

        Assert.NotNull(warning);
        Assert.StartsWith("25 phần tử MEP không tra được kích thước", warning);
        Assert.Contains("GỢI Ý", warning);
        Assert.Contains("20, …", warning);
        Assert.DoesNotContain("21", warning);
    }

    [Fact]
    public void UnknownSizeWarning_ItHon20_KhongCoBaCham()
    {
        var warning = SleevePlanner.UnknownSizeWarning(new long[] { 7, 9 }, "h");

        Assert.EndsWith("Phần tử: 7, 9", warning);
    }

    [Fact]
    public void MidpointFallbackNote_ChiKhiCoCa()
    {
        Assert.Null(SleevePlanner.MidpointFallbackNote(0));
        Assert.Null(SleevePlanner.MidpointFallbackNote(-1));
        Assert.StartsWith("3 giao cắt không tính được", SleevePlanner.MidpointFallbackNote(3));
    }

    [Fact]
    public void PreviewSummary_KhongCoGi_GhepLyDoVaoSummary()
    {
        Assert.Equal("[Xem trước] Sẽ đặt 4 sleeve tại giao cắt MEP × Tường/Sàn.", SleevePlanner.PreviewSummary(4, "LÝ DO"));
        Assert.Equal("[Xem trước] Sẽ đặt 0 sleeve tại giao cắt MEP × Tường/Sàn. LÝ DO", SleevePlanner.PreviewSummary(0, "LÝ DO"));
    }

    [Fact]
    public void WriteSummary_ThanhCongHet()
    {
        Assert.Equal("Đã đặt 5 sleeve tại giao cắt MEP × Tường/Sàn.", SleevePlanner.WriteSummary(5, 0, 5, 0, 0, "LÝ DO"));
    }

    [Fact]
    public void WriteSummary_MotPhanLoi_NoiTiLe()
    {
        var s = SleevePlanner.WriteSummary(3, 2, 5, 0, 0, "LÝ DO");

        Assert.Contains("Đã đặt 3 sleeve", s);
        Assert.Contains("2/5 vị trí đặt lỗi", s);
        Assert.DoesNotContain("LÝ DO", s);
    }

    [Fact]
    public void WriteSummary_KhongDatKhongLoi_GhepLyDo()
    {
        Assert.Contains("LÝ DO", SleevePlanner.WriteSummary(0, 0, 0, 0, 0, "LÝ DO"));
        // Có lỗi thì lý do "vì sao 0" không đúng nữa — lý do là đặt lỗi.
        Assert.DoesNotContain("LÝ DO", SleevePlanner.WriteSummary(0, 2, 2, 0, 0, "LÝ DO"));
    }

    [Fact]
    public void WriteSummary_DatTuDoTrenLink_VaBoQuaDaCo()
    {
        var s = SleevePlanner.WriteSummary(10, 0, 10, 4, 3, "LÝ DO");

        Assert.Contains("4 cái bám tường/sàn của model liên kết", s);
        Assert.Contains("Bỏ qua, đã có sleeve: 3 vị trí.", s);
    }

    [Fact]
    public void WriteSummary_BoQuaDaCoNhungKhongDatDuoc_KhongLapLaiHaiLan()
    {
        // WhyNothing đã nói "Bỏ qua, đã có sleeve" — phần đuôi chỉ thêm khi placed > 0, tránh in hai lần.
        var s = SleevePlanner.WriteSummary(0, 0, 0, 0, 3, "Bỏ qua, đã có sleeve: 3 vị trí.");

        Assert.Equal(1, CountOccurrences(s, "Bỏ qua, đã có sleeve"));
    }

    [Fact]
    public void FailureReasonsLine_ChiKhiCoLoi()
    {
        Assert.Null(SleevePlanner.FailureReasonsLine(0, new[] { "x" }));
        Assert.Equal(
            "2 vị trí không đặt được sleeve. Lý do (tối đa 5 loại): a | b",
            SleevePlanner.FailureReasonsLine(2, new[] { "a", "b" }));
        Assert.EndsWith("loại): ", SleevePlanner.FailureReasonsLine(1, null));
    }

    [Fact]
    public void PreviewLine_TuongTrongFile()
    {
        var line = SleevePlanner.PreviewLine(true, 1234, null, 1000.4, 2000.6, 3000, 250, 150);

        Assert.Equal("  → Tường 1234 tại (1000, 2001, 3000) mm  W=250mm H=150mm", line);
    }

    [Fact]
    public void PreviewLine_SanTrongLink()
    {
        var line = SleevePlanner.PreviewLine(false, 99, "ARC-L01", 0, 0, 0, 100, 100);

        Assert.StartsWith("  → Sàn 99 (link \"ARC-L01\") tại", line);
    }

    private static int CountOccurrences(string text, string needle)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }

        return count;
    }
}
