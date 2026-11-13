using DhcbTools.Shared.Logic;
using DhcbTools.Shared.Logic.Cad;
using Xunit;

namespace DhcbTools.Shared.Logic.Tests;

/// <summary>Danh sách vị trí đặt block của <c>BlockInsert</c> — JSON của agent và CSV của kỹ sư.</summary>
public class BlockInsertPlannerTests
{
    private static BlockInsertPlan Csv(params string[] lines) =>
        BlockInsertPlanner.FromCsv(CsvText.ReadRecords(new StringReader(string.Join("\n", lines))));

    [Fact]
    public void Csv_CotLaLaTagAttribute()
    {
        var plan = Csv("X,Y,Rotation,Scale,Layer,TAG,MO_TA", "100,200,90,2,M-THIETBI,P-01,Bơm cấp", "0.5,1.5,,,,P-02,", "", "3,4,0,1,,,");

        Assert.Empty(plan.Errors);
        Assert.Equal(3, plan.Rows.Count);
        var first = plan.Rows[0];
        Assert.Equal(new[] { 100.0, 200 }, first.Position);
        Assert.Equal((90.0, 2.0, "M-THIETBI"), (first.Rotation!.Value, first.Scale!.Value, first.Layer));
        Assert.Equal("P-01", first.Attributes!["tag"]);
        Assert.Equal("Bơm cấp", first.Attributes["MO_TA"]);
        Assert.Null(plan.Rows[1].Rotation);
        Assert.Null(plan.Rows[1].Layer);
        Assert.False(plan.Rows[1].Attributes!.ContainsKey("MO_TA")); // ô rỗng = giữ mặc định của định nghĩa
        Assert.Null(plan.Rows[2].Attributes);
    }

    [Fact]
    public void Csv_CoZ_DiemBaChieu()
    {
        var plan = Csv("X,Y,Z", "1,2,3", "1,2,");

        Assert.Equal(new[] { 1.0, 2, 3 }, plan.Rows[0].Position);
        Assert.Equal(new[] { 1.0, 2 }, plan.Rows[1].Position);
    }

    [Fact]
    public void Csv_LoiTheoDong()
    {
        var plan = Csv("X,Y,Rotation,Scale", "a,1,,", "1,1,xoay,", "1,1,0,-2", "1,1,0,1");

        Assert.Single(plan.Rows);
        Assert.Contains(plan.Errors, e => e.StartsWith("Dòng 2", StringComparison.Ordinal) && e.Contains("X/Y/Z"));
        Assert.Contains(plan.Errors, e => e.StartsWith("Dòng 3", StringComparison.Ordinal) && e.Contains("Rotation"));
        Assert.Contains(plan.Errors, e => e.StartsWith("Dòng 4", StringComparison.Ordinal) && e.Contains("scale"));
    }

    [Theory]
    [InlineData(new[] { "TAG" }, "thiếu cột X")]
    [InlineData(new string[0], "rỗng")]
    [InlineData(new[] { "X,Y" }, "không có dòng")]
    public void Csv_ThieuCotHoacRong(string[] lines, string expected)
    {
        Assert.Contains(expected, Assert.Single(Csv(lines).Errors));
    }

    [Fact]
    public void Json_KiemTungViTri()
    {
        var plan = BlockInsertPlanner.Validate(new List<BlockInsertion>
        {
            new() { Position = new[] { 0.0, 0 } },
            new() { Position = new[] { 0.0 } },
            new() { Position = new[] { 0.0, 0 }, Scale = 0 },
            new() { Position = new[] { 0.0, 0 }, Rotation = double.NaN },
            new() { Position = new[] { 0.0, 0 }, Layer = " " },
            new() { Position = new[] { 0.0, 0 }, Attributes = new Dictionary<string, string> { [""] = "x" } },
            null!,
        });

        Assert.Single(plan.Rows);
        Assert.Equal(6, plan.Errors.Count);
        Assert.Contains(plan.Errors, e => e.StartsWith("#2", StringComparison.Ordinal) && e.Contains("position"));
        Assert.Contains(plan.Errors, e => e.StartsWith("#7", StringComparison.Ordinal) && e.Contains("null"));
    }

    [Fact]
    public void Json_RongVaQuaTran()
    {
        Assert.Contains("rỗng", Assert.Single(BlockInsertPlanner.Validate(null).Errors));
        var many = Enumerable.Range(0, BlockInsertPlanner.MaxInsertions + 1).Select(_ => new BlockInsertion { Position = new[] { 0.0, 0 } }).ToList();
        Assert.Contains("tách", Assert.Single(BlockInsertPlanner.Validate(many).Errors));
    }
}
