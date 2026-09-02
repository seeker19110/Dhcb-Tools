using DhcbTools.Shared.Logic;
using Xunit;

namespace DhcbTools.Shared.Logic.Tests;

public class FileNamingTests
{
    [Theory]
    [InlineData("A-101", "A-101")]
    [InlineData("Mặt bằng tầng 1", "Mặt bằng tầng 1")]
    [InlineData("A/101", "A_101")]
    [InlineData("A\\101", "A_101")]
    [InlineData("Tỷ lệ 1:100", "Tỷ lệ 1_100")]
    [InlineData("Ghi chú <quan trọng>", "Ghi chú _quan trọng_")]
    [InlineData("Sơ đồ?", "Sơ đồ_")]
    public void Sanitize_ThayKyTuKhongHopLe(string input, string expected)
    {
        Assert.Equal(expected, FileNaming.Sanitize(input));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("   ")]
    public void Sanitize_TenRong_TraVeFallback(string? input)
    {
        Assert.Equal(FileNaming.Fallback, FileNaming.Sanitize(input!));
    }

    [Fact]
    public void Sanitize_CatDauChamVaDauCachOCuoi()
    {
        // Windows tự bỏ dấu chấm/dấu cách cuối tên file — cắt sẵn để tên trên đĩa khớp tên đã log.
        Assert.Equal("A-101", FileNaming.Sanitize("A-101. "));
        Assert.Equal("A-101", FileNaming.Sanitize("  A-101  "));
    }

    [Fact]
    public void Sanitize_KyTuDieuKhien_ThayBangGachDuoi()
    {
        Assert.Equal("A_101", FileNaming.Sanitize("A\t101"));
    }

    [Fact]
    public void ApplyPattern_ThayDayDuBaToken()
    {
        var name = FileNaming.ApplyPattern("{ProjectNumber}-{SheetNumber}-{SheetName}", "A-101", "Mặt bằng", "DA2026");

        Assert.Equal("DA2026-A-101-Mặt bằng", name);
    }

    [Fact]
    public void ApplyPattern_SanitizeTungGiaTriTruocKhiGhep()
    {
        // "A/101" không được biến thành thư mục con.
        var name = FileNaming.ApplyPattern("{SheetNumber}-{SheetName}", "A/101", "Mặt bằng", "DA");

        Assert.Equal("A_101-Mặt bằng", name);
        Assert.DoesNotContain("/", name);
    }

    [Fact]
    public void ApplyPattern_MauRong_DungMauMacDinh()
    {
        Assert.Equal("A-101-Mặt bằng", FileNaming.ApplyPattern(string.Empty, "A-101", "Mặt bằng", "DA"));
    }

    [Fact]
    public void ApplyPattern_TokenKhongCoTrongMau_GiuNguyenPhanConLai()
    {
        Assert.Equal("BanVe-A-101", FileNaming.ApplyPattern("BanVe-{SheetNumber}", "A-101", "Mặt bằng", "DA"));
    }

    [Fact]
    public void ApplyPattern_GiaTriRong_KhongSinhTenRong()
    {
        var name = FileNaming.ApplyPattern("{SheetNumber}", string.Empty, string.Empty, string.Empty);

        Assert.Equal(FileNaming.Fallback, name);
    }

    [Fact]
    public void MakeUnique_TenTrungDuocThemHauTo()
    {
        var used = new HashSet<string>();

        Assert.Equal("A-101", FileNaming.MakeUnique("A-101", used));
        Assert.Equal("A-101 (2)", FileNaming.MakeUnique("A-101", used));
        Assert.Equal("A-101 (3)", FileNaming.MakeUnique("A-101", used));
    }

    [Fact]
    public void MakeUnique_KhongPhanBietHoaThuong()
    {
        // NTFS không phân biệt hoa thường: "a-101" và "A-101" là cùng một file.
        var used = new HashSet<string>();

        Assert.Equal("A-101", FileNaming.MakeUnique("A-101", used));
        Assert.Equal("a-101 (2)", FileNaming.MakeUnique("a-101", used));
    }

    [Fact]
    public void MakeUnique_TenKhacNhau_GiuNguyen()
    {
        var used = new HashSet<string>();

        Assert.Equal("A-101", FileNaming.MakeUnique("A-101", used));
        Assert.Equal("A-102", FileNaming.MakeUnique("A-102", used));
    }
}
