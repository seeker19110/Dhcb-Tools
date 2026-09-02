using DhcbTools.Shared.Logic;
using Xunit;

namespace DhcbTools.Shared.Logic.Tests;

/// <summary>
/// Từ điển tên tham số (giai đoạn 9.2). Đây là thứ quyết định một lệnh MEPF tìm thấy tham số hay
/// im lặng không làm gì trên dự án dùng Revit tiếng Việt — nên thứ tự ưu tiên phải đúng và có test.
/// </summary>
public class ParameterDictionaryTests
{
    [Fact]
    public void TenDungSan_CoCaTiengAnhLanTiengViet()
    {
        var names = ParameterDictionary.BuiltinOnly().NamesFor("level");

        Assert.Contains("Level", names);
        Assert.Contains("Tầng", names);
    }

    [Fact]
    public void TenNguoiDungChiDinh_DungDauTien()
    {
        var names = ParameterDictionary.BuiltinOnly().NamesFor("level", "Cao độ riêng");

        Assert.Equal("Cao độ riêng", names[0]);
        Assert.Contains("Level", names);   // vẫn giữ tên dựng sẵn làm phương án dự phòng
    }

    [Fact]
    public void TenTrongFile_DungTruocTenDungSan_NhungKhongThayThe()
    {
        var dictionary = ParameterDictionary.Parse("""
        { "parameters": { "level": ["Tầng nhà", "Cốt"] } }
        """);

        var names = dictionary.NamesFor("level");

        Assert.Equal("Tầng nhà", names[0]);
        Assert.Equal("Cốt", names[1]);
        Assert.Contains("Level", names);   // dự án dùng thư viện chuẩn vẫn chạy được
    }

    [Fact]
    public void TenNguoiDung_ThangCaTenTrongFile()
    {
        var dictionary = ParameterDictionary.Parse("""
        { "parameters": { "level": ["Tầng nhà"] } }
        """);

        Assert.Equal("Ưu tiên nhất", dictionary.NamesFor("level", "Ưu tiên nhất")[0]);
    }

    [Fact]
    public void MotChuoiThayVìMang_VanDocDuoc()
    {
        var dictionary = ParameterDictionary.Parse("""
        { "parameters": { "mark": "Số hiệu bản vẽ" } }
        """);

        Assert.Equal("Số hiệu bản vẽ", dictionary.NamesFor("mark")[0]);
    }

    [Fact]
    public void KhongTrungLapKhiFileKhaiTrungTenDungSan()
    {
        var dictionary = ParameterDictionary.Parse("""
        { "parameters": { "mark": ["Mark", "Ký hiệu"] } }
        """);

        var names = dictionary.NamesFor("mark");

        Assert.Equal(names.Count, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void KhoaLa_TraVeChinhNo()
    {
        Assert.Equal(new[] { "khoa_khong_co" }, ParameterDictionary.BuiltinOnly().NamesFor("khoa_khong_co"));
    }

    [Fact]
    public void FamilyMacDinh_DocDuocTuFile()
    {
        var dictionary = ParameterDictionary.Parse("""
        { "families": { "sleeve": "DHCB_Sleeve: Tròn", "hanger": "  " } }
        """);

        Assert.Equal("DHCB_Sleeve: Tròn", dictionary.Families["sleeve"]);
        Assert.False(dictionary.Families.ContainsKey("hanger"));   // giá trị rỗng bị bỏ qua
    }

    /// <summary>
    /// Thông báo lỗi phải nêu đủ ba thứ: mã lỗi tra được, những tên đã thử, và sửa ở đâu —
    /// nếu không kỹ sư chỉ biết "có gì đó sai" mà không biết làm gì tiếp.
    /// </summary>
    [Fact]
    public void ThongBaoLoi_DuThongTinDeSua()
    {
        var message = ParameterDictionary.BuiltinOnly().NotFoundMessage("diameter", "DN ngoài");

        Assert.Contains("E-PARAM-MISSING", message);
        Assert.Contains("DN ngoài", message);
        Assert.Contains("Outer Diameter", message);
        Assert.Contains("dictionary.json", message);
    }

    [Fact]
    public void JsonRong_KhongNem()
    {
        Assert.NotEmpty(ParameterDictionary.Parse("").NamesFor("level"));
        Assert.NotEmpty(ParameterDictionary.Parse("{}").NamesFor("level"));
    }

    [Fact]
    public void FileKhongCo_QuayVeTenDungSan()
    {
        var dictionary = ParameterDictionary.Load(Path.Combine(Path.GetTempPath(), "dhcb-khong-ton-tai.json"));

        Assert.Contains("Level", dictionary.NamesFor("level"));
    }

    [Fact]
    public void FileHong_QuayVeTenDungSan_ThayViNem()
    {
        var path = Path.Combine(Path.GetTempPath(), "dhcb-dictionary-hong.json");
        File.WriteAllText(path, "{ khong phai json");
        try
        {
            // File hỏng không được phép chặn lệnh chạy; lệnh sẽ tự báo nếu tra không ra tham số.
            Assert.Contains("Level", ParameterDictionary.Load(path).NamesFor("level"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>File mẫu trong repo phải đọc được — nếu không thì hướng dẫn chép file là sai.</summary>
    [Fact]
    public void FileMauTrongRepo_DocDuoc()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Dhcb-Tools.sln")))
        {
            dir = dir.Parent;
        }

        var sample = Path.Combine(dir!.FullName, "configs", "dictionary.sample.json");
        Assert.True(File.Exists(sample), "Thiếu configs/dictionary.sample.json");

        var dictionary = ParameterDictionary.Parse(File.ReadAllText(sample));

        Assert.Equal("Tầng", dictionary.NamesFor("level")[0]);
        Assert.Contains("sleeve", dictionary.Families.Keys);
    }
}
