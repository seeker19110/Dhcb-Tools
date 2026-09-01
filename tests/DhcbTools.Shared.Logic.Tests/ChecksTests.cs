using DhcbTools.Shared.Logic.Checks;
using Xunit;

namespace DhcbTools.Shared.Logic.Tests;

public class RuleCheckerTests
{
    [Fact]
    public void DocQuyTac_MangHoacObject()
    {
        var a = RuleChecker.ParseRules("""[{"category":"Doors","parameter":"Mark","required":true,"pattern":"^D-\\d{3}$"}]""");
        var b = RuleChecker.ParseRules("""{"rules":[{"category":"Doors","parameter":"Mark","required":true}]}""");
        Assert.Single(a);
        Assert.Single(b);
        Assert.Equal("^D-\\d{3}$", a[0].Pattern);
    }

    [Fact]
    public void BatBuoc_RongLaViPham_KhongBatBuocRongHopLe()
    {
        var req = new ParameterRule { Required = true };
        var opt = new ParameterRule { Required = false, Pattern = "^X$" };
        Assert.Equal("thiếu giá trị", RuleChecker.Check(req, ""));
        Assert.Null(RuleChecker.Check(opt, null));
    }

    [Fact]
    public void KhopMau()
    {
        var r = new ParameterRule { Pattern = @"^D-\d{3}$" };
        Assert.Null(RuleChecker.Check(r, "D-001"));
        Assert.Contains("không khớp mẫu", RuleChecker.Check(r, "D-1"));
    }

    [Fact]
    public void DanhSachChoPhep_KhongPhanBietHoaThuong()
    {
        var r = new ParameterRule { AllowedValues = { "Bê tông", "Thép" } };
        Assert.Null(RuleChecker.Check(r, "bê tông"));
        Assert.Contains("danh sách", RuleChecker.Check(r, "Gỗ"));
    }

    [Fact]
    public void GomTheoCategory_VaHtmlEscape()
    {
        var v = new List<RuleViolation>
        {
            new("Doors", "1", "Cửa <A>", "Mark", "", "thiếu giá trị", "error"),
            new("Doors", "2", "Cửa B", "Mark", "x", "không khớp", "error"),
            new("Walls", "3", "W", "Type", null, "thiếu", "warning"),
        };
        var counts = RuleChecker.CountByCategory(v);
        Assert.Equal(2, counts["Doors"]);
        Assert.Equal(1, counts["Walls"]);

        var html = RuleChecker.RenderHtml("Kiểm tra", v, 10);
        Assert.Contains("Cửa &lt;A&gt;", html);
        Assert.Contains("3 vi phạm", html);
    }
}

public class ClashAcceptanceTests
{
    [Fact]
    public void KhoaKhongPhuThuocThuTuId_VaLamTronViTri()
    {
        var a = ClashAcceptance.MakeKey(10, 20, 1234, 5678, 3000);
        var b = ClashAcceptance.MakeKey(20, 10, 1249, 5651, 3040);
        Assert.Equal(a, b);
    }

    [Fact]
    public void PhanTuDoiCho_KhoaDoi()
    {
        Assert.NotEqual(ClashAcceptance.MakeKey(1, 2, 0, 0, 0), ClashAcceptance.MakeKey(1, 2, 1000, 0, 0));
    }

    [Fact]
    public void LuuVaDocLai()
    {
        var path = Path.Combine(Path.GetTempPath(), "dhcb-clash-" + Guid.NewGuid() + ".json");
        try
        {
            ClashAcceptance.Save(path, new[] { new AcceptedClash { Key = "1-2@0,0,0", Note = "đúng thiết kế" } });
            var keys = ClashAcceptance.LoadKeys(path);
            Assert.Contains("1-2@0,0,0", keys);
            Assert.Empty(ClashAcceptance.LoadKeys(path + ".khong-co"));
            Assert.Empty(ClashAcceptance.LoadKeys(null));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
