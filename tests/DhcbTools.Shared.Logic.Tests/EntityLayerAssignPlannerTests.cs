using DhcbTools.Shared.Logic;
using DhcbTools.Shared.Logic.Cad;
using Xunit;

namespace DhcbTools.Shared.Logic.Tests;

/// <summary>
/// CSV <c>Handle,Layer,Color</c> của lệnh <c>EntityLayerAssign</c> — tách tag/DIM lẫn hệ về layer theo hệ.
/// Giống LayerImportPlanner: kết quả không phụ thuộc thứ tự dòng, không tự chọn khi mâu thuẫn.
/// </summary>
public class EntityLayerAssignPlannerTests
{
    private static EntityLayerAssignPlan Plan(params string[] lines) =>
        EntityLayerAssignPlanner.Plan(CsvText.ReadRecords(new StringReader("Handle,Layer,Color\n" + string.Join("\n", lines))));

    [Fact]
    public void DongHopLe_DocHandleHexVaMau()
    {
        var plan = Plan("2a81,KL-DP-TEXT,30", "0x5A35,KL-GAS-TEXT", "  3a905 , KL-DP-TEXT ,");

        Assert.Equal(new[] { "2A81", "5A35", "3A905" }, plan.Rows.Select(r => r.HandleText));
        Assert.Equal(new long[] { 0x2A81, 0x5A35, 0x3A905 }, plan.Rows.Select(r => r.Handle));
        Assert.Equal((short)30, plan.TargetColors["kl-dp-text"]);
        Assert.False(plan.TargetColors.ContainsKey("KL-GAS-TEXT"));
        Assert.Empty(plan.Notes);
        Assert.Empty(plan.Conflicts);
    }

    [Theory]
    [InlineData("ZZ,KL-DP-TEXT")]
    [InlineData("0,KL-DP-TEXT")]
    [InlineData("-5,KL-DP-TEXT")]
    [InlineData("12345678901234567,KL-DP-TEXT")]
    public void HandleKhongHopLe_BoQuaVaNoiRa(string line)
    {
        var plan = Plan(line);

        Assert.Empty(plan.Rows);
        Assert.Contains(plan.Notes, n => n.Contains("dòng 2") && n.Contains("không phải số hex"));
    }

    [Fact]
    public void ThieuLayer_BoQuaVaNoiRa()
    {
        var plan = Plan("2A81,");

        Assert.Empty(plan.Rows);
        Assert.Contains(plan.Notes, n => n.Contains("thiếu tên layer"));
    }

    [Fact]
    public void DongLapCungLayer_ApMotLan()
    {
        var plan = Plan("2A81,KL-DP-TEXT", "2a81,kl-dp-text");

        Assert.Equal(2, Assert.Single(plan.Rows).Row);
        Assert.Contains(plan.Notes, n => n.Contains("lặp lại dòng 2"));
        Assert.Empty(plan.Conflicts);
    }

    [Fact]
    public void CungHandleKhacLayer_XungDot_KhongApDongNao()
    {
        var plan = Plan("2A81,KL-DP-TEXT", "1FF,KL-GAS-TEXT", "2A81,KL-GAS-TEXT");

        Assert.Equal("1FF", Assert.Single(plan.Rows).HandleText);
        Assert.Contains("2A81", Assert.Single(plan.Conflicts));
    }

    [Fact]
    public void CungLayerKhacMau_XungDotMau_KhongTuChon()
    {
        var plan = Plan("2A81,KL-DP-TEXT,30", "2A82,KL-DP-TEXT,1");

        Assert.Equal(2, plan.Rows.Count);
        Assert.False(plan.TargetColors.ContainsKey("KL-DP-TEXT"));
        Assert.Single(plan.ColorConflicts);
    }

    [Fact]
    public void MauNgoaiDai_BoMauGiuDong()
    {
        var plan = Plan("2A81,KL-DP-TEXT,256");

        Assert.Single(plan.Rows);
        Assert.Null(plan.Rows[0].ColorAci);
        Assert.Contains(plan.Notes, n => n.Contains("ACI 1–255"));
    }
}
