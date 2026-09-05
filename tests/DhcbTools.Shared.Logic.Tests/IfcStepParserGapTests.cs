using DhcbTools.Shared.Logic.Ifc;
using Xunit;

namespace DhcbTools.Shared.Logic.Tests;

/// <summary>
/// Các nhánh cú pháp STEP mà file IFC "đẹp" không đi qua: dạng giá trị hiếm (<c>*</c>, chuỗi nhị phân,
/// số mũ), và MỌI cách một file có thể hỏng. Bộ đọc phải báo lỗi kèm SỐ DÒNG — file IFC thật hàng chục
/// nghìn dòng, một thông báo không có số dòng thì kỹ sư không có chỗ bắt đầu tìm.
/// </summary>
public class IfcStepParserGapTests
{
    private static IfcStepFile Parse(string body) =>
        IfcStepParser.Parse("ISO-10303-21;\nHEADER;\nENDSEC;\nDATA;\n" + body + "\nENDSEC;\nEND-ISO-10303-21;");

    [Fact]
    public void TextNull_NemArgumentNull()
    {
        Assert.Throws<ArgumentNullException>(() => IfcStepParser.Parse(null!));
    }

    /// <summary>DATA có thể mang tham số — <c>DATA('ten');</c> vẫn phải đọc được.</summary>
    [Fact]
    public void DataCoThamSo_VanDocDuoc()
    {
        var file = IfcStepParser.Parse(
            "ISO-10303-21;\nDATA('phan-1');\n#1=IFCWALL('a',$,$,$,$,$,$,$,$);\nENDSEC;\nEND-ISO-10303-21;");

        Assert.Equal(1, Assert.Single(file.Data).Id);
    }

    /// <summary>Mục không số hiệu nằm sau DATA thì thuộc về DATA, không phải HEADER.</summary>
    [Fact]
    public void MucKhongSoHieuSauData_XepVaoData()
    {
        var file = Parse("FILE_COMMENT('ghi chu');");

        Assert.Equal("FILE_COMMENT", Assert.Single(file.Data).Type);
        Assert.Empty(file.Header);
    }

    /// <summary>Chú thích kiểu C bị bỏ qua; chú thích không đóng thì báo lỗi kèm dòng mở.</summary>
    [Fact]
    public void ChuThich_BoQuaKhiDongDu_BaoLoiKhiKhongDong()
    {
        var file = Parse("/* ghi chu */ #1=IFCWALL('a',$,$,$,$,$,$,$,$);");
        Assert.Single(file.Data);

        var ex = Assert.Throws<IfcParseException>(() => Parse("/* khong dong"));
        Assert.Contains("Chú thích không được đóng", ex.Message);
    }

    [Fact]
    public void ThieuPhanData_NoiRoKhongPhaiFileIfc()
    {
        var ex = Assert.Throws<IfcParseException>(
            () => IfcStepParser.Parse("ISO-10303-21;\nHEADER;\nENDSEC;\nEND-ISO-10303-21;"));

        Assert.Contains("không có phần DATA", ex.Message);
    }

    /// <summary>Từ khoá chỉ khớp khi đứng trọn vẹn: DATABASE không được nuốt thành DATA.</summary>
    [Fact]
    public void TuKhoaKhongKhopMotNua()
    {
        var ex = Assert.Throws<IfcParseException>(
            () => IfcStepParser.Parse("ISO-10303-21;\nDATABASE;\nEND-ISO-10303-21;"));

        Assert.Contains("không có phần DATA", ex.Message);
    }

    [Fact]
    public void SauSoHieuPhaiLaDauBang()
    {
        var ex = Assert.Throws<IfcParseException>(() => Parse("#1 IFCWALL('a');"));

        Assert.Contains("phải là dấu bằng", ex.Message);
    }

    [Fact]
    public void ThieuDauChamPhay_BaoDongNao()
    {
        var ex = Assert.Throws<IfcParseException>(() => Parse("#1=IFCWALL('a')"));

        Assert.Contains("Thiếu dấu chấm phẩy", ex.Message);
    }

    [Fact]
    public void SoHieuKhongPhaiSo()
    {
        var ex = Assert.Throws<IfcParseException>(() => Parse("#abc=IFCWALL('a');"));

        Assert.Contains("chờ một số nguyên", ex.Message);
    }

    [Fact]
    public void ThieuTenKieu()
    {
        var ex = Assert.Throws<IfcParseException>(() => Parse("#1=('a');"));

        Assert.Contains("chờ tên kiểu", ex.Message);
    }

    [Fact]
    public void DanhSachThamSoRong()
    {
        var entity = Assert.Single(Parse("#1=IFCWALL();").Data);

        Assert.Empty(entity.Attributes);
    }

    [Fact]
    public void DanhSachThamSoKhongDong()
    {
        var ex = Assert.Throws<IfcParseException>(
            () => IfcStepParser.Parse("ISO-10303-21;\nDATA;\n#1=IFCWALL('a'"));

        Assert.Contains("không được đóng", ex.Message);
    }

    [Fact]
    public void SauThamSoPhaiLaPhayHoacNgoacDong()
    {
        var ex = Assert.Throws<IfcParseException>(() => Parse("#1=IFCWALL('a' 'b');"));

        Assert.Contains("chờ dấu phẩy hoặc ngoặc đóng", ex.Message);
    }

    [Fact]
    public void FileKetThucGiuaChung()
    {
        var ex = Assert.Throws<IfcParseException>(() => IfcStepParser.Parse("ISO-10303-21;\nDATA;\n#1=IFCWALL("));

        Assert.Contains("kết thúc giữa chừng", ex.Message);
    }

    [Fact]
    public void KyTuKhongHopLe()
    {
        var ex = Assert.Throws<IfcParseException>(() => Parse("#1=IFCWALL(@);"));

        Assert.Contains("ký tự không hợp lệ", ex.Message);
    }

    /// <summary>Dấu <c>*</c> = giá trị dẫn xuất (kế thừa từ kiểu cha), khác hẳn <c>$</c> = thiếu.</summary>
    [Fact]
    public void GiaTriDanXuat_KhacGiaTriThieu()
    {
        var entity = Assert.Single(Parse("#1=IFCWALL(*,$);").Data);

        Assert.Equal(IfcValueKind.Derived, entity.At(0).Kind);
        Assert.Equal(IfcValueKind.Null, entity.At(1).Kind);
        Assert.Null(entity.At(0).AsText());
    }

    [Fact]
    public void ChuoiNhiPhan_GiuNguyenVan()
    {
        var entity = Assert.Single(Parse("#1=IFCWALL(\"0F3A\");").Data);

        Assert.Equal("0F3A", entity.At(0).Raw);
    }

    [Fact]
    public void ChuoiNhiPhanKhongDong()
    {
        var ex = Assert.Throws<IfcParseException>(() => IfcStepParser.Parse("ISO-10303-21;\nDATA;\n#1=IFCWALL(\"0F3A"));

        Assert.Contains("Chuỗi nhị phân không được đóng", ex.Message);
    }

    [Fact]
    public void GiaTriLietKeKhongDong()
    {
        var ex = Assert.Throws<IfcParseException>(() => IfcStepParser.Parse("ISO-10303-21;\nDATA;\n#1=IFCWALL(.TRUE"));

        Assert.Contains("Giá trị liệt kê không được đóng", ex.Message);
    }

    [Fact]
    public void ChuoiKhongDong()
    {
        var ex = Assert.Throws<IfcParseException>(() => IfcStepParser.Parse("ISO-10303-21;\nDATA;\n#1=IFCWALL('chua dong"));

        Assert.Contains("Chuỗi không được đóng", ex.Message);
    }

    /// <summary>Số viết dạng mũ (Revit xuất toạ độ kiểu này) phải giữ nguyên văn để so sánh.</summary>
    [Theory]
    [InlineData("1.5E-3", "1.5E-3")]
    [InlineData("-2.5e+6", "-2.5e+6")]
    [InlineData("+7", "+7")]
    public void SoDangMuVaCoDau(string raw, string expected)
    {
        var entity = Assert.Single(Parse("#1=IFCWALL(" + raw + ");").Data);

        Assert.Equal(expected, entity.At(0).AsText());
    }

    /// <summary>Tên không kèm ngoặc là một giá trị liệt kê viết trần (không phải giá trị bọc kiểu).</summary>
    [Fact]
    public void TenKhongKemNgoac_LaLietKe()
    {
        var entity = Assert.Single(Parse("#1=IFCWALL(UNKNOWN);").Data);

        Assert.Equal(IfcValueKind.Enumeration, entity.At(0).Kind);
        Assert.Equal("UNKNOWN", entity.At(0).AsText());
    }

    /// <summary>Giá trị bọc kiểu nhiều hơn một tham số không có nghĩa so sánh — trả null.</summary>
    [Fact]
    public void GiaTriBocKieuNhieuThamSo_KhongCoChuoiSoSanh()
    {
        var entity = Assert.Single(Parse("#1=IFCWALL(IFCPOINT(1.,2.));").Data);

        Assert.Equal(IfcValueKind.Typed, entity.At(0).Kind);
        Assert.Null(entity.At(0).AsText());
    }

    [Fact]
    public void ThamSoThieu_TraGiaTriRongDungChung()
    {
        var entity = Assert.Single(Parse("#1=IFCWALL('a');").Data);

        Assert.Same(IfcValue.Empty, entity.At(9));
        Assert.Same(IfcValue.Empty, entity.At(-1));
    }

    [Fact]
    public void SoDongDuocGhiLaiDeBaoLoiChiDungCho()
    {
        var file = Parse("\n\n#7=IFCWALL('a');");

        Assert.Equal(7, Assert.Single(file.Data).Line);
    }

    /// <summary>Dãy thoát Unicode: có đóng X0, không đóng X0, một byte, trang mã trên, và gạch chéo đôi.</summary>
    [Theory]
    [InlineData(@"T\X2\01B0\X0\ng", "T\u01B0ng")]        // \X2\ đóng đủ
    [InlineData(@"a\X2\0041", "aA")]                        // \X2\ không đóng X0
    [InlineData(@"a\X4\00000041\X0\b", "aAb")]              // \X4\ đóng đủ
    [InlineData(@"a\X4\00000041", "aA")]                    // \X4\ không đóng X0
    [InlineData(@"a\X\41b", "aAb")]                         // một byte
    [InlineData(@"a\S\Ab", "aÁb")]                     // trang mã trên: 'A' + 128
    [InlineData(@"a\\b", @"a\b")]                           // gạch chéo đôi
    [InlineData(@"a\Qb", @"a\Qb")]                          // dãy không nhận ra: giữ nguyên
    [InlineData("khong co gi", "khong co gi")]              // không có gạch chéo: trả thẳng
    public void GiaiMaDayThoat(string raw, string expected)
    {
        Assert.Equal(expected, IfcStepParser.DecodeEscapes(raw));
    }

    /// <summary>Hai dấu nháy liền trong chuỗi là một dấu nháy, không phải kết thúc chuỗi.</summary>
    [Fact]
    public void HaiDauNhayLien_LaMotDauNhay()
    {
        var entity = Assert.Single(Parse("#1=IFCWALL('Tuong D''Angelo');").Data);

        Assert.Equal("Tuong D'Angelo", entity.At(0).AsText());
    }
}
