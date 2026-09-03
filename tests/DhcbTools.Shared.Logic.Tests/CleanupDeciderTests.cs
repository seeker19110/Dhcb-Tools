using DhcbTools.Shared.Logic;
using Xunit;

namespace DhcbTools.Shared.Logic.Tests;

public class CleanupDeciderTests
{
    [Fact]
    public void LayerRongKhongDung_DuocXoa()
    {
        Assert.True(CleanupDecider.ShouldErase("A-TEMP", isUsed: false, isCurrent: false, isSystem: false));
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    public void DangDung_HienHanh_HeThong_KhongBaoGioXoa(bool used, bool current, bool system)
    {
        Assert.False(CleanupDecider.ShouldErase("X", used, current, system));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("Defpoints")]
    [InlineData("DEFPOINTS")]
    public void LayerHeThong(string name)
    {
        Assert.True(CleanupDecider.IsSystemLayer(name));
    }

    [Theory]
    [InlineData("Continuous")]
    [InlineData("ByLayer")]
    [InlineData("BYBLOCK")]
    public void LinetypeHeThong(string name)
    {
        Assert.True(CleanupDecider.IsSystemLinetype(name));
    }

    [Fact]
    public void LayerCuaXref_KhongXoa()
    {
        Assert.False(CleanupDecider.ShouldErase("SITE|A-WALL", false, false, false));
    }

    [Fact]
    public void MauGiuLai_KhongPhanBietHoaThuong()
    {
        Assert.False(CleanupDecider.ShouldErase("DHCB-Tam", false, false, false, new[] { "dhcb" }));
        Assert.True(CleanupDecider.ShouldErase("A-Tam", false, false, false, new[] { "dhcb" }));
    }

    [Fact]
    public void TenRong_KhongXoa()
    {
        Assert.False(CleanupDecider.ShouldErase(null, false, false, false));
        Assert.False(CleanupDecider.ShouldErase(string.Empty, false, false, false));
    }

    // ── Purge sâu: text style / dim style / regapp (mục 7.12) ────────────────

    [Theory]
    [InlineData("Standard")]
    [InlineData("STANDARD")]
    public void TextStyleVaDimStyleHeThong_KhongXoa(string name)
    {
        Assert.True(CleanupDecider.IsSystemTextStyle(name));
        Assert.True(CleanupDecider.IsSystemDimStyle(name));
        Assert.False(CleanupDecider.ShouldErase(name, false, false, CleanupDecider.IsSystemTextStyle(name)));
    }

    [Fact]
    public void TextStyleThuong_XoaDuocKhiKhongDung()
    {
        Assert.False(CleanupDecider.IsSystemTextStyle("ARIAL-3MM"));
        Assert.True(CleanupDecider.ShouldErase("ARIAL-3MM", false, false, false));
    }

    [Theory]
    [InlineData("ACAD")]
    [InlineData("acad")]
    [InlineData("ACAD_MLEADERVER")]
    [InlineData("AcDbBlockRepETag")]
    [InlineData("AcadAnnotativeDecomposition")]   // thấy thật trên bản vẽ mẫu AutoCAD 2026
    [InlineData("AcadAnnoPO")]
    public void RegAppHeThong_KhongXoa(string name)
    {
        Assert.True(CleanupDecider.IsSystemRegApp(name));
    }

    [Theory]
    [InlineData("DHCB_TOOLS")]
    [InlineData("HHMM_XDATA")]
    [InlineData("AVE_FINISH")]        // rác của tính năng cũ, thấy thật trên bản vẽ mẫu
    [InlineData("CONTENTTABDATA")]
    public void RegAppCuaAddInLa_XoaDuocKhiKhongConXData(string name)
    {
        Assert.False(CleanupDecider.IsSystemRegApp(name));
        Assert.True(CleanupDecider.ShouldErase(name, isUsed: false, isCurrent: false, isSystem: false));
        // Còn entity nào mang XData của nó thì tuyệt đối không xoá — mất dữ liệu của add-in khác.
        Assert.False(CleanupDecider.ShouldErase(name, isUsed: true, isCurrent: false, isSystem: false));
    }
}
