using DhcbTools.Shared.Logic;
using DhcbTools.Shared.Logic.Cad;
using Xunit;

namespace DhcbTools.Shared.Logic.Tests;

/// <summary>
/// Phần đọc CSV của lệnh LayerImport (cột: Name,Color,Linetype,Lineweight,IsPlottable,Description).
/// Ô để trống = "giữ nguyên giá trị trong drawing" nên phải ra null, không được ra 0/false —
/// đó là cách duy nhất để lệnh biết ô nào thật sự cần ghi.
/// </summary>
public class LayerCsvRowTests
{
    [Fact]
    public void Parse_DongDayDu_DocDuMoiCot()
    {
        var row = LayerCsvRow.Parse("A-WALL,3,DASHED,LineWeight025,true,Tường kiến trúc", 2);

        Assert.False(row.IsEmpty);
        Assert.Equal("A-WALL", row.Name);
        Assert.Equal((short)3, row.ColorAci);
        Assert.Null(row.ColorRgb);
        Assert.Equal("DASHED", row.Linetype);
        Assert.Equal(25, row.LineWeight);
        Assert.True(row.Plottable);
        Assert.Equal("Tường kiến trúc", row.Description);
        Assert.Empty(row.Warnings);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(",3,DASHED")]
    public void Parse_KhongCoTenLayer_LaDongRong(string line)
    {
        Assert.True(LayerCsvRow.Parse(line, 2).IsEmpty);
    }

    [Fact]
    public void Parse_OTrong_GiuNguyenGiaTriCu()
    {
        var row = LayerCsvRow.Parse("A-WALL,,,,,", 2);

        Assert.Null(row.ColorAci);
        Assert.Null(row.ColorRgb);
        Assert.Null(row.Linetype);
        Assert.Null(row.LineWeight);
        Assert.Null(row.Plottable);
        Assert.Empty(row.Warnings);
    }

    [Fact]
    public void Parse_ThieuCot_KhongVangLoi()
    {
        var row = LayerCsvRow.Parse("A-WALL,7", 2);

        Assert.Equal((short)7, row.ColorAci);
        Assert.Null(row.Linetype);
        Assert.Null(row.Description);
        Assert.Empty(row.Warnings);
    }

    [Theory]
    [InlineData("0", 0)]
    [InlineData("256", 256)]
    public void Parse_MauAci_NhanCaHaiDauMut(string cell, short expected)
    {
        var row = LayerCsvRow.Parse("L," + cell, 2);

        Assert.Equal(expected, row.ColorAci);
        Assert.Null(row.ColorRgb);
    }

    [Fact]
    public void Parse_MauTrueColor_DocTheoColorValue()
    {
        // Ngoài dải ACI (0–256) → hiểu là ColorValue của lệnh xuất. Bản trước bỏ qua im lặng.
        var row = LayerCsvRow.Parse("L,16711680", 2); // 0xFF0000

        Assert.Null(row.ColorAci);
        Assert.Equal(0xFF0000, row.ColorRgb);
    }

    [Fact]
    public void Parse_MauKhongDocDuoc_CanhBaoVaGiuNguyen()
    {
        var row = LayerCsvRow.Parse("L,đỏ", 5);

        Assert.Null(row.ColorAci);
        Assert.Null(row.ColorRgb);
        Assert.Contains(row.Warnings, w => w.Contains("Dòng 5") && w.Contains("đỏ"));
    }

    [Theory]
    [InlineData("LineWeight025", 25)]
    [InlineData("lineweight000", 0)]
    [InlineData("211", 211)]
    [InlineData("ByLayer", -1)]
    [InlineData("ByBlock", -2)]
    [InlineData("ByLineWeightDefault", -3)]
    public void Parse_Lineweight_NhanCaTenEnumLanSoTran(string cell, int expected)
    {
        Assert.Equal(expected, LayerCsvRow.Parse("L,,," + cell, 2).LineWeight);
    }

    [Fact]
    public void Parse_LineweightKhongHopLe_CanhBaoVaGiuNguyen()
    {
        var row = LayerCsvRow.Parse("L,,,mỏng", 7);

        Assert.Null(row.LineWeight);
        Assert.Contains(row.Warnings, w => w.Contains("Dòng 7") && w.Contains("lineweight"));
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("False", false)]
    public void Parse_Plottable_DocDuocCaHoaThuong(string cell, bool expected)
    {
        Assert.Equal(expected, LayerCsvRow.Parse("L,,,," + cell, 2).Plottable);
    }

    [Fact]
    public void Parse_PlottableKhongHopLe_CanhBaoVaGiuNguyen()
    {
        var row = LayerCsvRow.Parse("L,,,,có", 9);

        Assert.Null(row.Plottable);
        Assert.Contains(row.Warnings, w => w.Contains("Dòng 9") && w.Contains("IsPlottable"));
    }

    [Fact]
    public void Parse_DescriptionRong_VanLaLenhXoaMoTa()
    {
        // Khác các cột kia: ô mô tả để trống nghĩa là xoá mô tả, không phải "giữ nguyên".
        Assert.Equal(string.Empty, LayerCsvRow.Parse("L,,,,,", 2).Description);
    }

    [Fact]
    public void Parse_MoTaCoDauPhay_TonTrongNhayKep()
    {
        var row = LayerCsvRow.Parse("L,1,Continuous,LineWeight013,true,\"Tường, cột\"", 2);

        Assert.Equal("Tường, cột", row.Description);
    }

    [Fact]
    public void Parse_KhoangTrangQuanhGiaTri_BiCatBo()
    {
        var row = LayerCsvRow.Parse(" A-WALL , 3 , DASHED , LineWeight025 , true ", 2);

        Assert.Equal("A-WALL", row.Name);
        Assert.Equal((short)3, row.ColorAci);
        Assert.Equal("DASHED", row.Linetype);
        Assert.Equal(25, row.LineWeight);
        Assert.True(row.Plottable);
        Assert.Empty(row.Warnings);
    }

    [Fact]
    public void Parse_DongXuatRaRoiDocLai_KhongMatGiaTri()
    {
        // Vòng khép kín với định dạng LayerExportCommand ghi ra.
        var line = CsvText.JoinLine(new[] { "M-DUCT", "5", "HIDDEN", "LineWeight050", "true", "Ống gió, tầng 3" });

        var row = LayerCsvRow.Parse(line, 2);

        Assert.Equal("M-DUCT", row.Name);
        Assert.Equal((short)5, row.ColorAci);
        Assert.Equal("HIDDEN", row.Linetype);
        Assert.Equal(50, row.LineWeight);
        Assert.True(row.Plottable);
        Assert.Equal("Ống gió, tầng 3", row.Description);
        Assert.Empty(row.Warnings);
    }
}
