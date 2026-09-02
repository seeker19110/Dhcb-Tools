using System.Globalization;
using System.Text;
using DhcbTools.Shared.Logic;
using Xunit;

namespace DhcbTools.Shared.Logic.Tests;

public class CsvTextTests
{
    [Theory]
    [InlineData("Doors", "Doors")]
    [InlineData("", "")]
    [InlineData("Cửa đi 1 cánh", "Cửa đi 1 cánh")]
    [InlineData("Phòng, hành lang", "\"Phòng, hành lang\"")]
    [InlineData("Cao 2\"", "\"Cao 2\"\"\"")]
    [InlineData("Dòng 1\nDòng 2", "\"Dòng 1\nDòng 2\"")]
    [InlineData("Dòng 1\r\nDòng 2", "\"Dòng 1\r\nDòng 2\"")]
    public void Escape_ChiBocKhiCan(string input, string expected)
    {
        Assert.Equal(expected, CsvText.Escape(input));
    }

    [Fact]
    public void Escape_Null_TraVeChuoiRong()
    {
        Assert.Equal(string.Empty, CsvText.Escape(null!));
    }

    [Fact]
    public void SplitLine_TachDungOBinhThuong()
    {
        Assert.Equal(new[] { "123", "Doors", "M_Single-Flush" }, CsvText.SplitLine("123,Doors,M_Single-Flush"));
    }

    [Fact]
    public void SplitLine_GiuDauPhayTrongONhay()
    {
        Assert.Equal(new[] { "1", "Phòng, hành lang", "x" }, CsvText.SplitLine("1,\"Phòng, hành lang\",x"));
    }

    [Fact]
    public void SplitLine_GopNhayKepDoi()
    {
        Assert.Equal(new[] { "Cao 2\"" }, CsvText.SplitLine("\"Cao 2\"\"\""));
    }

    [Fact]
    public void SplitLine_ORongOCuoiVanDuocGiu()
    {
        Assert.Equal(new[] { "1", "", "" }, CsvText.SplitLine("1,,"));
    }

    [Fact]
    public void SplitLine_DongRong_TraVeMotORong()
    {
        Assert.Equal(new[] { "" }, CsvText.SplitLine(string.Empty));
    }

    [Fact]
    public void SplitLine_Null_KhongNemLoi()
    {
        Assert.Equal(new[] { "" }, CsvText.SplitLine(null!));
    }

    [Theory]
    [InlineData("123,Doors,\"Phòng, hành lang\",\"Cao 2\"\"\"")]
    [InlineData("a,b,c")]
    [InlineData("\"đa\ndòng\",x")]
    public void EscapeVaSplit_RoundTrip(string line)
    {
        var cells = CsvText.SplitLine(line);
        var rebuilt = CsvText.JoinLine(cells);
        Assert.Equal(cells, CsvText.SplitLine(rebuilt));
    }

    [Fact]
    public void JoinLine_TuEscapeTungO()
    {
        Assert.Equal("1,\"a,b\",\"c\"\"d\"", CsvText.JoinLine(new[] { "1", "a,b", "c\"d" }));
    }

    [Fact]
    public void Utf8WithBom_CoBom()
    {
        // Lỗi #4 trong progress.md: thiếu BOM làm Excel trên Windows hiển thị sai tên tiếng Việt.
        var bytes = CsvText.Utf8WithBom.GetPreamble();
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes);
    }

    [Fact]
    public void Utf8WithBom_GhiVaDocLaiGiuNguyenTiengViet()
    {
        var path = Path.GetTempFileName();
        try
        {
            const string content = "Name\nCửa đi — phòng ngủ\n";
            File.WriteAllText(path, content, CsvText.Utf8WithBom);

            var raw = File.ReadAllBytes(path);
            Assert.Equal(0xEF, raw[0]);
            Assert.Equal(0xBB, raw[1]);
            Assert.Equal(0xBF, raw[2]);
            Assert.Equal(content, File.ReadAllText(path, Encoding.UTF8));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
