using DhcbTools.Shared.Logic.Cad;
using Xunit;

namespace DhcbTools.Shared.Logic.Tests;

/// <summary>
/// Phần thuần của DrawingCombine (gộp nhiều DWG): tên block/layout hợp lệ và không trùng, danh sách file nguồn,
/// quy đổi đơn vị như INSERT và xếp hàng không chồng nhau. Đường ghi thật chạy trong autocad-write.json.
/// </summary>
public class DrawingCombinePlannerTests
{
    [Theory]
    [InlineData(null, "Row")]
    [InlineData("", "Row")]
    [InlineData("  row ", "Row")]
    [InlineData("ORIGINAL", "Original")]
    [InlineData("Grid", null)]
    public void NormalizeArrangement(string? input, string? expected)
    {
        Assert.Equal(expected, DrawingCombinePlanner.NormalizeArrangement(input));
    }

    [Theory]
    [InlineData("A-01 Mặt bằng", "A-01 Mặt bằng")]
    [InlineData("a<b>c/d\\e\"f:g;h?i*j|k,l=m`n", "a_b_c_d_e_f_g_h_i_j_k_l_m_n")]
    [InlineData("  có\tkhoảng trắng  ", "có_khoảng trắng")]
    [InlineData("   ", "DWG")]
    [InlineData(null, "DWG")]
    public void SanitizeName_ThayKyTuCam(string? raw, string expected)
    {
        Assert.Equal(expected, DrawingCombinePlanner.SanitizeName(raw));
    }

    [Fact]
    public void SanitizeName_CatDoDaiVaBoKhoangTrangCuoi()
    {
        Assert.Equal("abc", DrawingCombinePlanner.SanitizeName("abc def", 4));
        Assert.Equal(DrawingCombinePlanner.MaxSymbolLength, DrawingCombinePlanner.SanitizeName(new string('x', 400)).Length);
    }

    [Fact]
    public void UniqueName_ThemHauToKhongPhanBietHoaThuong()
    {
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "SHEET1" };
        Assert.Equal("Sheet1-2", DrawingCombinePlanner.UniqueName("Sheet1", taken));
        Assert.Equal("Sheet1-3", DrawingCombinePlanner.UniqueName("Sheet1", taken));
        Assert.Equal("Sheet2", DrawingCombinePlanner.UniqueName("Sheet2", taken));
        Assert.Contains("sheet1-3", taken);
    }

    [Fact]
    public void UniqueName_HauToVanNamTrongGioiHanDoDai()
    {
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "abcdef" };
        var name = DrawingCombinePlanner.UniqueName("abcdefghij", taken, 6);
        Assert.Equal("abcd-2", name);
    }

    [Fact]
    public void TenBlockVaLayout()
    {
        Assert.Equal("A-01", DrawingCombinePlanner.BlockNameFor(@"D:\du an\A-01.dwg"));
        Assert.Equal("A-01 - Layout1", DrawingCombinePlanner.LayoutNameFor("A-01", "Layout1"));
    }

    [Theory]
    [InlineData(4, 1, 1 / 25.4)]   // mm → inch
    [InlineData(1, 4, 25.4)]       // inch → mm
    [InlineData(6, 4, 1000.0)]     // m → mm
    [InlineData(4, 4, 1.0)]
    [InlineData(0, 4, 1.0)]        // nguồn không khai: 1:1
    [InlineData(4, 0, 1.0)]        // đích không khai: 1:1
    public void UnitScale_NhuInsert(int source, int target, double expected)
    {
        Assert.Equal(expected, DrawingCombinePlanner.UnitScale(source, target), 9);
    }

    [Fact]
    public void ResolveInputs_GopDanhSachTayVaThuMuc_BoTrungVaBanVeDich()
    {
        var skipped = new List<string>();
        var inputs = DrawingCombinePlanner.ResolveInputs(
            new[] { @"C:\in\b\X.dwg", "", @"C:\in\a.DWG", @"C:\in\ghi-chu.txt", @"c:\IN\A.dwg" },
            @"C:\in",
            new[] { @"C:\in\target.dwg", @"C:\in\Z.dwg", @"C:\in\a.dwg", @"C:\in\M.dwg" },
            @"C:\in\target.dwg",
            skipped);

        Assert.Equal(new[] { @"C:\in\b\X.dwg", @"C:\in\a.DWG", @"C:\in\M.dwg", @"C:\in\Z.dwg" }, inputs);
        Assert.Equal(2, skipped.Count);
        Assert.Contains(skipped, s => s.Contains("ghi-chu.txt") && s.Contains("không phải file .dwg"));
        Assert.Contains(skipped, s => s.Contains("target.dwg") && s.Contains("chính bản vẽ"));
    }

    [Fact]
    public void ResolveInputs_BanVeDichOHaiNguon_ChiBaoMotLan()
    {
        var skipped = new List<string>();
        var inputs = DrawingCombinePlanner.ResolveInputs(new[] { @"C:\in\t.dwg" }, @"C:\in", new[] { @"C:\in\t.dwg" }, @"C:\in\t.dwg", skipped);
        Assert.Empty(inputs);
        Assert.Single(skipped);
    }

    [Fact]
    public void ResolveInputs_KhongThuMucKhongDich_BoQuaDanhSachThuMuc()
    {
        var skipped = new List<string>();
        var inputs = DrawingCombinePlanner.ResolveInputs(null, "  ", new[] { @"C:\in\a.dwg" }, null, skipped);
        Assert.Empty(inputs);
        Assert.Empty(skipped);
    }

    [Fact]
    public void ResolveInputs_DuongDanKhongHopLe_VanSoSanhTheoChuoi()
    {
        var skipped = new List<string>();
        var bad = "C:\\in\\a\0b.dwg";
        var inputs = DrawingCombinePlanner.ResolveInputs(new[] { bad, bad }, null, Array.Empty<string>(), bad, skipped);
        Assert.Empty(inputs);
        Assert.Single(skipped);
    }

    [Fact]
    public void Place_Original_GiuToaDoGoc()
    {
        var placed = DrawingCombinePlanner.Place(
            new List<(string, CombineExtents?)> { ("A", new CombineExtents(5, 5, 9, 9)), ("B", null) }, "original", 100, 50, 10);
        Assert.All(placed, p => Assert.Equal((0.0, 0.0), (p.X, p.Y)));
        Assert.Equal(new[] { "A", "B" }, placed.Select(p => p.BlockName));
    }

    [Fact]
    public void Place_Row_XepTraiSangPhai_DayThangHang_KhongChongNhau()
    {
        var placed = DrawingCombinePlanner.Place(
            new List<(string, CombineExtents?)>
            {
                ("A", new CombineExtents(-10, 20, 30, 60)),   // rộng 40
                ("Rong", null),                                // Model Space rỗng: không chiếm chỗ
                ("B", new CombineExtents(100, -5, 125, 5)),    // rộng 25
            },
            "Row", startX: 1000, baseY: 200, gap: 50);

        // A: góc dưới-trái (−10, 20) phải rơi đúng (1000, 200).
        Assert.Equal((1010.0, 180.0), (placed[0].X, placed[0].Y));
        // Bản rỗng đặt tại con trỏ hiện tại (sau A + gap) và không đẩy con trỏ.
        Assert.Equal((1090.0, 200.0), (placed[1].X, placed[1].Y));
        // B: góc dưới-trái (100, −5) rơi đúng (1000 + 40 + 50, 200).
        Assert.Equal((990.0, 205.0), (placed[2].X, placed[2].Y));
    }

    [Fact]
    public void CombineExtents_ScaledVaWidth()
    {
        var e = new CombineExtents(1, 2, 5, 10).Scaled(2);
        Assert.Equal((2.0, 4.0, 10.0, 20.0), (e.MinX, e.MinY, e.MaxX, e.MaxY));
        Assert.Equal(8.0, e.Width);
    }
}
