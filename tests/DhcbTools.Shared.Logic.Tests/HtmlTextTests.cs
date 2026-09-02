using DhcbTools.Shared.Logic;
using Xunit;

namespace DhcbTools.Shared.Logic.Tests;

public class HtmlTextTests
{
    [Theory]
    [InlineData("View 1", "View 1")]
    [InlineData("A & B", "A &amp; B")]
    [InlineData("<script>", "&lt;script&gt;")]
    [InlineData("\"nháy\"", "&quot;nháy&quot;")]
    [InlineData("d'Artagnan", "d&#39;Artagnan")]
    public void Escape_ThoatKyTuHtml(string input, string expected)
    {
        Assert.Equal(expected, HtmlText.Escape(input));
    }

    [Fact]
    public void Escape_ThoatDauVaTruoc_KhongEscapeKep()
    {
        Assert.Equal("&amp;lt;", HtmlText.Escape("&lt;"));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void Escape_ChuoiRong_TraVeRong(string? input)
    {
        Assert.Equal(string.Empty, HtmlText.Escape(input!));
    }

    [Fact]
    public void Escape_GiuNguyenTiengViet()
    {
        Assert.Equal("Mặt bằng tầng 1", HtmlText.Escape("Mặt bằng tầng 1"));
    }
}
