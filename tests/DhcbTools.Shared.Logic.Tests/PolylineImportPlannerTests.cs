using DhcbTools.Shared.Logic;
using DhcbTools.Shared.Logic.Cad;
using Xunit;

namespace DhcbTools.Shared.Logic.Tests;

/// <summary>CSV <c>Id,Layer,Width,Color,Closed,Vertices</c> của lệnh <c>PolylineImport</c> (vẽ lại tim ống bóc khối lượng).</summary>
public class PolylineImportPlannerTests
{
    private static PolylineImportPlan Plan(params string[] lines) =>
        PolylineImportPlanner.Plan(CsvText.ReadRecords(new StringReader("Id,Layer,Width,Color,Closed,Vertices\n" + string.Join("\n", lines))));

    [Fact]
    public void DongDayDu_DocDinhBeRongMauVaChieuDai()
    {
        var plan = Plan("T1,D21,30,4,,0 0;3000 0;3000 4000", "R,KHU,0,,true,0 0;10 0;10 10;0 10");

        Assert.Equal(2, plan.Rows.Count);
        var t1 = plan.Rows[0];
        Assert.Equal(new[] { 2, 3 }, plan.Rows.Select(r => r.Row));
        Assert.Equal(("T1", "D21", 30.0, (short?)4, false), (t1.Id, t1.Layer, t1.Width, t1.ColorAci, t1.Closed));
        Assert.Equal(7000, t1.Length, 6);
        Assert.True(plan.Rows[1].Closed);
        Assert.Equal(40, plan.Rows[1].Length, 6);
        Assert.Equal((short)4, plan.TargetColors["d21"]);
        Assert.Empty(plan.Notes);
    }

    [Fact]
    public void DinhLienNhauTrung_GopLai()
    {
        var plan = Plan("a,D27,,,,1.5 2;1.5 2;4.5 6");

        Assert.Equal(2, Assert.Single(plan.Rows).Points.Count);
        Assert.Equal(5, plan.Rows[0].Length, 6);
        Assert.Equal(0, plan.Rows[0].Width);
    }

    [Theory]
    [InlineData("a,,30,,,0 0;1 1", "thiếu tên layer")]
    [InlineData("a,D21,-1,,,0 0;1 1", "bề rộng")]
    [InlineData("a,D21,x,,,0 0;1 1", "bề rộng")]
    [InlineData("a,D21,30,,,0 0;1", "không đọc được")]
    [InlineData("a,D21,30,,,0 0;a b", "không đọc được")]
    [InlineData("a,D21,30,,,0 0;NaN 1", "không đọc được")]
    [InlineData("a,D21,30,,,", "không đọc được")]
    [InlineData("a,D21,30,,,5 5", "ít nhất 2 đỉnh")]
    [InlineData("a,D21,30,,,5 5;5 5", "ít nhất 2 đỉnh")]
    public void DongHong_BoQuaVaNoiRa(string line, string expected)
    {
        var plan = Plan(line);

        Assert.Empty(plan.Rows);
        Assert.Contains(plan.Notes, n => n.Contains("dòng 2") && n.Contains(expected));
    }

    [Fact]
    public void DongTrong_BoQuaImLang()
    {
        var plan = Plan(",,,,,", "a,D21,,,,0 0;1 0");

        Assert.Single(plan.Rows);
        Assert.Empty(plan.Notes);
    }

    [Fact]
    public void MauNgoaiDai_BoMau_CungLayerKhacMau_XungDot()
    {
        var plan = Plan("a,D21,,999,,0 0;1 0", "b,D34,,1,,0 0;1 0", "c,D34,,2,,0 0;1 0");

        Assert.Equal(3, plan.Rows.Count);
        Assert.Null(plan.Rows[0].ColorAci);
        Assert.Contains(plan.Notes, n => n.Contains("ACI 1–255"));
        Assert.Single(plan.ColorConflicts);
        Assert.False(plan.TargetColors.ContainsKey("D34"));
    }

    [Fact]
    public void DongThieuCot_BaoThieuDinh()
    {
        var plan = Plan("a,D21");

        Assert.Empty(plan.Rows);
        Assert.Contains(plan.Notes, n => n.Contains("không đọc được"));
    }

    [Fact]
    public void Null_NemArgumentNull()
    {
        Assert.Throws<ArgumentNullException>(() => PolylineImportPlanner.Plan(null!));
    }
}
