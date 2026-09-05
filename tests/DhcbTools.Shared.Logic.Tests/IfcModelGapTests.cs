using DhcbTools.Shared.Logic.Ifc;
using Xunit;

namespace DhcbTools.Shared.Logic.Tests;

/// <summary>
/// Những gì bộ đọc IFC gặp trên file THẬT nhưng không có trong file mẫu đẹp: bảng khối lượng
/// (<c>IfcElementQuantity</c>), quan hệ trỏ vòng, tham chiếu gãy, và phần tử không có thuộc tính nào.
/// Mỗi ca ở đây là một cách bộ kiểm có thể vỡ hoặc báo sai — mà báo sai thì kỹ sư tắt nó đi.
/// </summary>
public class IfcModelGapTests
{
    private static IfcModel Parse(string body) =>
        IfcModel.Parse("ISO-10303-21;\nHEADER;\nFILE_SCHEMA(('IFC4'));\nENDSEC;\nDATA;\n"
                       + body + "\nENDSEC;\nEND-ISO-10303-21;");

    /// <summary>Phần tử không có Pset nào: trả bảng rỗng, không phải null và không ném.</summary>
    [Fact]
    public void PhanTuKhongCoThuocTinh_TraBangRong()
    {
        var model = Parse("#1=IFCWALL('0Aa$b1cD2eF3gH4iJ5kL6m',$,'Tuong',$,$,$,$,$,$);");

        Assert.Empty(model.PropertiesOf(1));
        Assert.Empty(model.PropertiesOf(999));
        Assert.Empty(model.ClassificationsOf(1));
    }

    [Fact]
    public void TryProperty_PhanTuKhongCoBang_TraFalse()
    {
        var model = Parse("#1=IFCWALL('0Aa$b1cD2eF3gH4iJ5kL6m',$,'Tuong',$,$,$,$,$,$);");

        Assert.False(model.TryProperty(1, "Pset_WallCommon.IsExternal", out _));
    }

    /// <summary>
    /// Khoá có dấu chấm là chỉ đích danh Pset: không tìm thấy thì trả false, KHÔNG được rơi xuống
    /// tìm theo tên trần ở Pset khác — đó là lúc bộ kiểm báo "đạt" cho một Pset không hề tồn tại.
    /// </summary>
    [Fact]
    public void TryProperty_KhoaChiDichDanhPset_KhongRoiXuongTimTenTran()
    {
        var model = Parse("""
            #1=IFCWALL('0Aa$b1cD2eF3gH4iJ5kL6m',$,'Tuong',$,$,$,$,$,$);
            #20=IFCPROPERTYSINGLEVALUE('IsExternal',$,IFCBOOLEAN(.T.),$);
            #21=IFCPROPERTYSET('0Ba$b1cD2eF3gH4iJ5kL6m',$,'Pset_WallCommon',$,(#20));
            #22=IFCRELDEFINESBYPROPERTIES('1Ba$b1cD2eF3gH4iJ5kL6m',$,$,$,(#1),#21);
            """);

        Assert.True(model.TryProperty(1, "IsExternal", out var traTheoTenTran));
        Assert.Equal("T", traTheoTenTran);
        Assert.False(model.TryProperty(1, "Pset_KhongCo.IsExternal", out _));
    }

    /// <summary>Tên trần không khớp thuộc tính nào ở bất kỳ Pset nào.</summary>
    [Fact]
    public void TryProperty_TenTranKhongCoODau_TraFalse()
    {
        var model = Parse("""
            #1=IFCWALL('0Aa$b1cD2eF3gH4iJ5kL6m',$,'Tuong',$,$,$,$,$,$);
            #20=IFCPROPERTYSINGLEVALUE('IsExternal',$,IFCBOOLEAN(.T.),$);
            #21=IFCPROPERTYSET('0Ba$b1cD2eF3gH4iJ5kL6m',$,'Pset_WallCommon',$,(#20));
            #22=IFCRELDEFINESBYPROPERTIES('1Ba$b1cD2eF3gH4iJ5kL6m',$,$,$,(#1),#21);
            """);

        Assert.False(model.TryProperty(1, "KhongCoThuocTinhNay", out _));
    }

    /// <summary>Bảng khối lượng: đọc như Pset, giá trị nằm ở tham số 3 chứ không phải 2.</summary>
    [Fact]
    public void BangKhoiLuong_DocDuocGiaTri()
    {
        var model = Parse("""
            #1=IFCWALL('0Aa$b1cD2eF3gH4iJ5kL6m',$,'Tuong',$,$,$,$,$,$);
            #40=IFCQUANTITYAREA('NetSideArea',$,$,12.5,$);
            #41=IFCELEMENTQUANTITY('0Ba$b1cD2eF3gH4iJ5kL6m',$,'Qto_WallBaseQuantities',$,$,(#40));
            #42=IFCRELDEFINESBYPROPERTIES('1Ba$b1cD2eF3gH4iJ5kL6m',$,$,$,(#1),#41);
            """);

        Assert.True(model.TryProperty(1, "Qto_WallBaseQuantities.NetSideArea", out var value));
        Assert.Equal("12.5", value);
    }

    /// <summary>Mục trong bảng khối lượng gãy tham chiếu hoặc không có tên: bỏ qua, không ném.</summary>
    [Fact]
    public void BangKhoiLuong_MucGayHoacKhongTen_BoQua()
    {
        var model = Parse("""
            #1=IFCWALL('0Aa$b1cD2eF3gH4iJ5kL6m',$,'Tuong',$,$,$,$,$,$);
            #40=IFCQUANTITYAREA($,$,$,12.5,$);
            #41=IFCELEMENTQUANTITY('0Ba$b1cD2eF3gH4iJ5kL6m',$,'Qto_WallBaseQuantities',$,$,(#40,#99));
            #42=IFCRELDEFINESBYPROPERTIES('1Ba$b1cD2eF3gH4iJ5kL6m',$,$,$,(#1),#41);
            """);

        Assert.Empty(model.PropertiesOf(1));
    }

    /// <summary>
    /// Pset trỏ vòng về chính nó (và hai quan hệ cùng trỏ vào nó): dựng bảng phải DỪNG.
    /// Không có chốt chặn vòng thì test này treo chứ không đỏ — đó là lý do nó đáng có.
    /// </summary>
    [Fact]
    public void QuanHeTroVong_KhongDeQuyVoHan()
    {
        var model = Parse("""
            #1=IFCWALL('0Aa$b1cD2eF3gH4iJ5kL6m',$,'Tuong',$,$,$,$,$,$);
            #21=IFCPROPERTYSET('0Ba$b1cD2eF3gH4iJ5kL6m',$,'Pset_Vong',$,(#21));
            #22=IFCRELDEFINESBYPROPERTIES('1Ba$b1cD2eF3gH4iJ5kL6m',$,$,$,(#1),#21);
            #23=IFCRELDEFINESBYPROPERTIES('2Ba$b1cD2eF3gH4iJ5kL6m',$,$,$,(#1),#21);
            """);

        // Đọc chính nó như một thuộc tính là vô nghĩa nhưng vô hại; điều quan trọng là hàm đã trả về.
        Assert.Single(model.PropertiesOf(1));
    }

    /// <summary>Quan hệ trỏ tới một Pset không tồn tại: bỏ qua thay vì ném.</summary>
    [Fact]
    public void PsetKhongTonTai_BoQua()
    {
        var model = Parse("""
            #1=IFCWALL('0Aa$b1cD2eF3gH4iJ5kL6m',$,'Tuong',$,$,$,$,$,$);
            #22=IFCRELDEFINESBYPROPERTIES('1Ba$b1cD2eF3gH4iJ5kL6m',$,$,$,(#1),#99);
            """);

        Assert.Empty(model.PropertiesOf(1));
    }

    /// <summary>Quan hệ trỏ tới phần tử bằng một tham chiếu đơn (không bọc trong danh sách).</summary>
    [Fact]
    public void ThamChieuDon_KhongBocDanhSach_VanDoc()
    {
        var model = Parse("""
            #1=IFCWALL('0Aa$b1cD2eF3gH4iJ5kL6m',$,'Tuong',$,$,$,$,$,$);
            #20=IFCPROPERTYSINGLEVALUE('IsExternal',$,IFCBOOLEAN(.T.),$);
            #21=IFCPROPERTYSET('0Ba$b1cD2eF3gH4iJ5kL6m',$,'Pset_WallCommon',$,#20);
            #22=IFCRELDEFINESBYPROPERTIES('1Ba$b1cD2eF3gH4iJ5kL6m',$,$,$,#1,#21);
            """);

        Assert.True(model.TryProperty(1, "Pset_WallCommon.IsExternal", out _));
    }

    /// <summary>Danh sách quan hệ chứa cả thứ không phải tham chiếu: bỏ qua phần tử lạ.</summary>
    [Fact]
    public void DanhSachLanThuKhongPhaiThamChieu_BoQuaPhanTuLa()
    {
        var model = Parse("""
            #1=IFCWALL('0Aa$b1cD2eF3gH4iJ5kL6m',$,'Tuong',$,$,$,$,$,$);
            #20=IFCPROPERTYSINGLEVALUE('IsExternal',$,IFCBOOLEAN(.T.),$);
            #21=IFCPROPERTYSET('0Ba$b1cD2eF3gH4iJ5kL6m',$,'Pset_WallCommon',$,($,#20,'la'));
            #22=IFCRELDEFINESBYPROPERTIES('1Ba$b1cD2eF3gH4iJ5kL6m',$,$,$,(#1),#21);
            """);

        Assert.True(model.TryProperty(1, "Pset_WallCommon.IsExternal", out _));
    }

    /// <summary>Tham số quan hệ không phải danh sách cũng không phải tham chiếu: không sinh gì.</summary>
    [Fact]
    public void ThamSoQuanHeKhongPhaiThamChieu_KhongSinhGi()
    {
        var model = Parse("""
            #1=IFCWALL('0Aa$b1cD2eF3gH4iJ5kL6m',$,'Tuong',$,$,$,$,$,$);
            #20=IFCPROPERTYSINGLEVALUE('IsExternal',$,IFCBOOLEAN(.T.),$);
            #21=IFCPROPERTYSET('0Ba$b1cD2eF3gH4iJ5kL6m',$,'Pset_WallCommon',$,(#20));
            #22=IFCRELDEFINESBYPROPERTIES('1Ba$b1cD2eF3gH4iJ5kL6m',$,$,$,'khong-phai-tham-chieu',#21);
            """);

        Assert.Empty(model.PropertiesOf(1));
    }

    /// <summary>Ký tự ngoài bảng base64 của IFC không phải mã định danh, dù đủ 22 ký tự.</summary>
    [Theory]
    [InlineData("0Aa$b1cD2eF3gH4iJ5kL6-")]
    [InlineData("0Aa b1cD2eF3gH4iJ5kL6m")]
    public void LooksLikeGlobalId_KyTuNgoaiBang_TraFalse(string value)
    {
        Assert.False(IfcModel.LooksLikeGlobalId(value));
    }
}
