using System.Globalization;
using DhcbTools.Shared.Logic;
using Xunit;

namespace DhcbTools.Shared.Logic.Tests;

/// <summary>
/// Bao lỗi #1 trong docs/progress.md: round-trip số thực hỏng trên máy locale Việt Nam.
/// Mỗi test đổi CurrentCulture sang vi-VN (dấu phẩy thập phân) rồi trả lại.
/// </summary>
public class NumericTextTests : IDisposable
{
    private readonly CultureInfo _original = CultureInfo.CurrentCulture;

    private static void UseVietnamese() => CultureInfo.CurrentCulture = new CultureInfo("vi-VN");

    public void Dispose() => CultureInfo.CurrentCulture = _original;

    [Fact]
    public void Format_LuonDungDauChamDuTrenMayTiengViet()
    {
        UseVietnamese();
        Assert.Equal("1234.5", NumericText.Format(1234.5));
    }

    [Fact]
    public void FormatVoiSoLeCoDinh_LuonDungDauCham()
    {
        UseVietnamese();
        Assert.Equal("3200.0", NumericText.Format(3200.04, 1));
    }

    [Fact]
    public void FormatInt_KhongCoDauPhanNhomHangNghin()
    {
        UseVietnamese();
        Assert.Equal("1234567", NumericText.Format(1234567));
    }

    [Fact]
    public void RoundTrip_TrenMayTiengViet_KhongMatGiaTri()
    {
        UseVietnamese();
        const double value = 2749.3218;

        var text = NumericText.Format(value);
        Assert.True(NumericText.TryParseDouble(text, out var parsed));
        Assert.Equal(value, parsed, 9);
    }

    [Theory]
    [InlineData("1234.5", 1234.5)]
    [InlineData("1234,5", 1234.5)]
    [InlineData("  42 ", 42)]
    [InlineData("-0.75", -0.75)]
    [InlineData("-0,75", -0.75)]
    [InlineData("1E3", 1000)]
    public void TryParseDouble_ChapNhanCaHaiDauThapPhan(string text, double expected)
    {
        UseVietnamese();
        Assert.True(NumericText.TryParseDouble(text, out var value));
        Assert.Equal(expected, value, 9);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    [InlineData("1,234.5")]
    [InlineData("1,2,3")]
    public void TryParseDouble_TuChoiChuoiKhongHopLe(string text)
    {
        UseVietnamese();
        Assert.False(NumericText.TryParseDouble(text, out _));
    }

    [Fact]
    public void TryParseDouble_Null_TraVeFalse()
    {
        Assert.False(NumericText.TryParseDouble(null!, out _));
    }

    [Theory]
    [InlineData("42", 42)]
    [InlineData(" -7 ", -7)]
    public void TryParseInt_DocDungKhongPhuThuocCulture(string text, int expected)
    {
        UseVietnamese();
        Assert.True(NumericText.TryParseInt(text, out var value));
        Assert.Equal(expected, value);
    }

    [Theory]
    [InlineData("1.5")]
    [InlineData("x")]
    [InlineData("")]
    public void TryParseInt_TuChoiChuoiKhongPhaiSoNguyen(string text)
    {
        Assert.False(NumericText.TryParseInt(text, out _));
    }
}
