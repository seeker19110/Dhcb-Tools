using DhcbTools.Shared.Logic.Cad;
using Xunit;

namespace DhcbTools.Shared.Logic.Tests;

/// <summary>
/// Giai đoạn 10.1 (phía AutoCAD) — đọc handle từ chuỗi agent gửi xuống. Handle là định danh BỀN của
/// entity trong DWG; nhận sai một dạng viết là truy vấn trả rỗng mà không báo gì.
/// </summary>
public class HandleTextTests
{
    [Theory]
    [InlineData("1A3", 419)]
    [InlineData("1a3", 419)]
    [InlineData("0x1A3", 419)]
    [InlineData("0X1a3", 419)]
    [InlineData("  1A3  ", 419)]
    [InlineData("(1A3)", 419)]
    [InlineData("<1A3>", 419)]
    [InlineData("A", 10)]
    public void DocDuocCacDangVietThuongGap(string text, long expected)
    {
        Assert.True(HandleText.TryParse(text, out var value));
        Assert.Equal(expected, value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("0x")]
    [InlineData("XYZ")]
    [InlineData("12.5")]
    public void ChuoiKhongPhaiHandle_TraVeFalse(string? text)
    {
        Assert.False(HandleText.TryParse(text, out var value));
        Assert.Equal(0, value);
    }

    [Fact]
    public void ToText_VietHoaKhongTienTo_GiongCachAutoCadHienThi()
    {
        Assert.Equal("1A3", HandleText.ToText(419));
        Assert.Equal("A", HandleText.ToText(10));
    }

    [Fact]
    public void ToText_RoiTryParse_QuayVeChinhNo()
    {
        for (long i = 1; i < 5000; i += 137)
        {
            Assert.True(HandleText.TryParse(HandleText.ToText(i), out var back));
            Assert.Equal(i, back);
        }
    }
}
