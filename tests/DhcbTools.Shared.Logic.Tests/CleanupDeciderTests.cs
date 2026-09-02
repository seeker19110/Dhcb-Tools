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
}
