using System.Collections.Generic;
using DhcbTools.Shared.Logic.Mep;
using Xunit;

namespace DhcbTools.Shared.Logic.Tests;

/// <summary>§60: phần thuần của FamilyStarter — chọn family, câu chữ.</summary>
public class FamilyStarterPlannerTests
{
    private static readonly string[] All = { "Sleeve", "Hanger" };

    [Fact]
    public void Plan_RongLaTatCa_TenTheoThuTuChuan_NhanCaTenDayDu()
    {
        Assert.Equal(new[] { "Sleeve", "Hanger" }, FamilyStarterPlanner.Plan(new List<string>(), All, out var u1));
        Assert.Empty(u1);
        Assert.Equal(new[] { "Sleeve", "Hanger" }, FamilyStarterPlanner.Plan(new List<string> { "hanger", "DHCB_Sleeve", "Hanger" }, All, out var u2));
        Assert.Empty(u2);
        Assert.Equal(new[] { "Hanger" }, FamilyStarterPlanner.Plan(new List<string> { "Hanger", "Ong" }, All, out var u3));
        Assert.Equal(new[] { "Ong" }, u3);
        Assert.Equal("Không biết family \"Ong\". Hợp lệ: Sleeve, Hanger.", FamilyStarterPlanner.UnknownMessage(u3, All));
    }

    [Fact]
    public void CauChu()
    {
        Assert.Equal("DHCB_Sleeve", FamilyStarterPlanner.FamilyName("Sleeve"));
        Assert.Contains("đã tìm: a; b.", FamilyStarterPlanner.NoTemplateMessage(new[] { "a", "b" }));
        Assert.Equal("Sẽ dựng DHCB_Sleeve → x.rfa rồi nạp vào mô hình.", FamilyStarterPlanner.PreviewLine("Sleeve", "x.rfa", true));
        Assert.EndsWith("(không nạp).", FamilyStarterPlanner.PreviewLine("Sleeve", "x.rfa", false));
        Assert.Equal("[Xem trước] Sẽ dựng 2 family mẫu (DHCB_Sleeve, DHCB_Hanger) và nạp vào mô hình.", FamilyStarterPlanner.PreviewSummary(All, true));
        Assert.EndsWith("DHCB_Hanger).", FamilyStarterPlanner.PreviewSummary(All, false));
        Assert.Equal("Đã dựng 2 family mẫu (DHCB_Sleeve, DHCB_Hanger) → D:/f; đã nạp 2/2 vào mô hình.", FamilyStarterPlanner.DoneSummary(new[] { "DHCB_Sleeve", "DHCB_Hanger" }, "D:/f", 2));
        Assert.Equal("Đã dựng 1 family mẫu (DHCB_Sleeve) → D:/f.", FamilyStarterPlanner.DoneSummary(new[] { "DHCB_Sleeve" }, "D:/f", null));
        Assert.StartsWith("Không dựng được", FamilyStarterPlanner.DoneSummary(new string[0], "D:/f", 0));
    }
}
