using DhcbTools.Shared.Logic.Cad;
using Newtonsoft.Json;
using Xunit;

namespace DhcbTools.Shared.Logic.Tests;

/// <summary>Kiểm lô thao tác hình học của <c>EntityEdit</c> trước khi đụng vào bản vẽ.</summary>
public class EntityEditPlannerTests
{
    private static readonly JsonSerializerSettings Strict = new() { MissingMemberHandling = MissingMemberHandling.Error };

    private static EntityEditPlan Plan(string json) =>
        EntityEditPlanner.Plan(JsonConvert.DeserializeObject<List<EntityOperation>>(json, Strict)!);

    [Fact]
    public void LoHopLe_DuCacLoaiTaoVaSua()
    {
        var plan = Plan("""
            [
              {"op":"line","from":[0,0],"to":[1000,0],"layer":"M-ONG","id":"a"},
              {"op":"pline","points":[[0,0],[10,0],[10,10]],"closed":true,"width":2},
              {"op":"rectangle","from":[0,0],"to":[5,3]},
              {"op":"circle","center":[0,0],"radius":5,"color":1},
              {"op":"arc","center":[0,0],"radius":5,"startAngle":0,"endAngle":90},
              {"op":"ellipse","center":[0,0],"majorAxis":[10,0],"ratio":0.5},
              {"op":"text","position":[0,0],"text":"P-01","height":250,"rotation":90},
              {"op":"mtext","position":[0,0],"text":"Ghi chú","width":3000},
              {"op":"hatch","points":[[0,0],[1,0],[1,1]],"pattern":"ANSI31","patternScale":10},
              {"op":"point","position":[1,2,3]},
              {"op":"copy","handles":["@a"],"delta":[0,500],"count":3,"id":"b"},
              {"op":"array","handles":["2A1"],"rows":2,"columns":3,"rowSpacing":100,"columnSpacing":200},
              {"op":"move","handles":["@b","2A1"],"delta":[10,0]},
              {"op":"rotate","handles":["@a"],"basePoint":[0,0],"angle":45},
              {"op":"scale","handles":["@a"],"basePoint":[0,0],"factor":2},
              {"op":"mirror","handles":["@a"],"from":[0,0],"to":[0,1],"keepOriginal":false},
              {"op":"offset","handles":["@a"],"distance":-50,"id":"c"},
              {"op":"delete","handles":["@c"]}
            ]
            """);

        Assert.Empty(plan.Errors);
        Assert.True(plan.IsValid);
        Assert.Equal(18, plan.Steps.Count);
        Assert.Equal(EntityOpKind.Polyline, plan.Steps[1].Kind);
        Assert.Equal(EntityOpKind.Erase, plan.Steps[^1].Kind);
        Assert.True(plan.Steps[0].Creates);
        Assert.True(plan.Steps[10].Produces);
        Assert.False(plan.Steps[15].Produces); // mirror không giữ gốc = sửa tại chỗ
    }

    [Fact]
    public void LoiBaoTheoSoThuTu_VaKhongChayOpNao()
    {
        var plan = Plan("""
            [
              {"op":"line","from":[0,0],"to":[0,0]},
              {"op":"circle","center":[0,0],"radius":-1},
              {"op":"bogus"},
              {"op":"move","handles":["XYZ"],"delta":[1,1]},
              {"op":"text","position":[0,0]},
              {"op":"ok-line-below-is-valid-but-plan-is-atomic"},
              {"op":"line","from":[0,0],"to":[1,0]}
            ]
            """);

        Assert.False(plan.IsValid);
        Assert.Empty(plan.Steps);
        Assert.Contains(plan.Errors, e => e.StartsWith("#1 line", StringComparison.Ordinal) && e.Contains("dài 0"));
        Assert.Contains(plan.Errors, e => e.StartsWith("#2 circle", StringComparison.Ordinal) && e.Contains("radius"));
        Assert.Contains(plan.Errors, e => e.StartsWith("#3:", StringComparison.Ordinal) && e.Contains("bogus"));
        Assert.Contains(plan.Errors, e => e.StartsWith("#4 move", StringComparison.Ordinal) && e.Contains("XYZ"));
        Assert.Contains(plan.Errors, e => e.StartsWith("#5 text", StringComparison.Ordinal) && e.Contains("cần text"));
    }

    [Fact]
    public void NhanPhaiDuocDatTruoc_VaKhongTrung()
    {
        var plan = Plan("""
            [
              {"op":"move","handles":["@sau"],"delta":[1,0]},
              {"op":"line","from":[0,0],"to":[1,0],"id":"sau"},
              {"op":"line","from":[0,0],"to":[2,0],"id":"SAU"},
              {"op":"erase","handles":["1F"],"id":"x"}
            ]
            """);

        Assert.Contains(plan.Errors, e => e.StartsWith("#1 move", StringComparison.Ordinal) && e.Contains("@sau"));
        Assert.Contains(plan.Errors, e => e.StartsWith("#3 line", StringComparison.Ordinal) && e.Contains("trùng"));
        Assert.Contains(plan.Errors, e => e.StartsWith("#4 erase", StringComparison.Ordinal) && e.Contains("id"));
    }

    [Fact]
    public void OpSuaKhongNhanLayerMau_OpTaoKhongNhanHandles()
    {
        var plan = Plan("""
            [
              {"op":"move","handles":["1F"],"delta":[1,0],"layer":"X"},
              {"op":"circle","center":[0,0],"radius":1,"handles":["1F"],"color":300}
            ]
            """);

        Assert.Contains(plan.Errors, e => e.StartsWith("#1 move", StringComparison.Ordinal) && e.Contains("EntityLayerAssign"));
        Assert.Contains(plan.Errors, e => e.StartsWith("#2 circle", StringComparison.Ordinal) && e.Contains("handles"));
        Assert.Contains(plan.Errors, e => e.StartsWith("#2 circle", StringComparison.Ordinal) && e.Contains("ACI"));
    }

    [Theory]
    [InlineData("""{"op":"line","from":[0,0],"to":[1,0],"id":"@a"}""", "không bắt đầu bằng @")]
    [InlineData("""{"op":"point","position":[0,0],"layer":"  "}""", "layer rỗng")]
    [InlineData("""{"op":"rotate","basePoint":[0,0],"angle":5}""", "thiếu \"handles\"")]
    [InlineData("""{"op":"array","handles":["1F"],"rows":1,"columns":1}""", "rows×columns")]
    [InlineData("""{"op":"array","handles":["1F"],"rows":200,"columns":200,"rowSpacing":1,"columnSpacing":1}""", "vượt")]
    [InlineData("""{"op":"array","handles":["1F"],"rows":2}""", "rowSpacing")]
    [InlineData("""{"op":"copy","handles":["1F"],"delta":[1,0],"count":0}""", "count")]
    [InlineData("""{"op":"ellipse","center":[0,0],"majorAxis":[0,0],"ratio":2}""", "majorAxis")]
    [InlineData("""{"op":"hatch","points":[[0,0],[1,0]]}""", "≥ 3")]
    [InlineData("""{"op":"offset","handles":["1F"],"distance":0}""", "distance")]
    [InlineData("""{"op":"mirror","handles":["1F"],"from":[1,1],"to":[1,1]}""", "trùng")]
    [InlineData("""{"op":"scale","handles":["1F"],"basePoint":[0,0],"factor":0}""", "factor")]
    [InlineData("""{"op":"rectangle","from":[0,0],"to":[0,5]}""", "khác nhau")]
    [InlineData("""{"op":"text","position":[0,0],"text":"a","width":5}""", "mtext")]
    [InlineData("""{"op":"mtext","position":[0,0],"text":"a","style":" "}""", "style rỗng")]
    [InlineData("""{"op":"line","from":[0,"NaN"],"to":[1,0]}""", "from")]
    public void GiaTriSai_BaoDungTruong(string op, string expected)
    {
        var plan = Plan("[" + op + "]");

        Assert.False(plan.IsValid);
        Assert.Contains(plan.Errors, e => e.Contains(expected, StringComparison.Ordinal));
    }

    [Fact]
    public void RongVaQuaTran_BaoLoi()
    {
        Assert.Contains("rỗng", Assert.Single(EntityEditPlanner.Plan(null).Errors));
        Assert.Contains("rỗng", Assert.Single(EntityEditPlanner.Plan(new List<EntityOperation>()).Errors));
        var many = Enumerable.Range(0, EntityEditPlanner.MaxOperations + 1).Select(_ => new EntityOperation { Op = "point", Position = new[] { 0.0, 0 } }).ToList();
        Assert.Contains("tách", Assert.Single(EntityEditPlanner.Plan(many).Errors));
        Assert.Contains("null", Assert.Single(EntityEditPlanner.Plan(new List<EntityOperation> { null! }).Errors));
    }

    [Fact]
    public void TruongGoSai_NewtonsoftChanNgay()
    {
        var ex = Assert.Throws<JsonSerializationException>(() => Plan("""[{"op":"circle","centre":[0,0],"radius":1}]"""));
        Assert.Contains("centre", ex.Message);
    }

    [Fact]
    public void TienIch_DoiDoVaDinhDangDiem()
    {
        Assert.Equal(Math.PI / 2, EntityEditPlanner.Radians(90), 12);
        Assert.Equal("(1.5, -2, 3)", EntityEditPlanner.Format(new[] { 1.5, -2, 3 }));
        Assert.Equal(0, EntityEditPlanner.Z(new[] { 1.0, 2 }));
        Assert.False(EntityEditPlanner.IsPoint(new[] { 1.0 }));
        Assert.Contains("erase", EntityEditPlanner.ValidOps);
        Assert.Contains("explode", EntityEditPlanner.ValidOps);
        Assert.Contains("replacepoints", EntityEditPlanner.ValidOps);
    }

    /// <summary>
    /// Bốn op thêm sau phiên bóc khối lượng 2026-11-13 (khi đó phải vòng qua COM): phá block, đổi layer/màu/bề rộng
    /// đều, cắt đầu pline và thay đỉnh pline tại chỗ.
    /// </summary>
    [Fact]
    public void OpMoi_ExplodeSetTrimReplacePoints_HopLe()
    {
        var plan = Plan("""
            [
              {"op":"explode","handles":["2A1"],"layerMap":{"0":"M-ONG-GIO"},"layer":"M-PHU-KIEN","id":"manh"},
              {"op":"set","handles":["@manh"],"color":256},
              {"op":"set","handles":["3B"],"layer":"M-ONG","color":0,"width":0},
              {"op":"trim","handles":["3B"],"from":[100,0]},
              {"op":"trim","handles":["3C"],"from":[0,0],"to":[50,0]},
              {"op":"replacePoints","handles":["3D"],"points":[[0,0],[10,0],[10,10]],"closed":false,"width":2}
            ]
            """);

        Assert.Empty(plan.Errors);
        Assert.Equal(new[] { EntityOpKind.Explode, EntityOpKind.Set, EntityOpKind.Set, EntityOpKind.Trim, EntityOpKind.Trim, EntityOpKind.ReplacePoints },
            plan.Steps.Select(s => s.Kind));
        Assert.True(plan.Steps[0].Produces);
        Assert.False(plan.Steps[0].Creates);
        Assert.False(plan.Steps[1].Produces);
        Assert.False(plan.Steps[3].Produces);
        Assert.Equal("M-ONG-GIO", plan.Steps[0].Source.LayerMap!["0"]);
    }

    [Theory]
    [InlineData("""{"op":"set","handles":["1F"]}""", "ít nhất một")]
    [InlineData("""{"op":"set","handles":["1F"],"color":300}""", "0–256")]
    [InlineData("""{"op":"set","handles":["1F"],"width":-1}""", "width")]
    [InlineData("""{"op":"set","handles":["1F"],"layer":" "}""", "layer rỗng")]
    [InlineData("""{"op":"set","handles":["1F"],"color":1,"id":"x"}""", "\"id\"")]
    [InlineData("""{"op":"explode","handles":["1F"],"color":1}""", "\"set\"")]
    [InlineData("""{"op":"explode","handles":["1F"],"layerMap":{}}""", "layerMap rỗng")]
    [InlineData("""{"op":"explode","handles":["1F"],"layerMap":{"0":" "}}""", "tên layer rỗng")]
    [InlineData("""{"op":"explode"}""", "thiếu \"handles\"")]
    [InlineData("""{"op":"move","handles":["1F"],"delta":[1,0],"layerMap":{"0":"A"}}""", "chỉ dùng cho explode")]
    [InlineData("""{"op":"trim","handles":["1F"]}""", "from (đầu mới)")]
    [InlineData("""{"op":"trim","handles":["1F"],"from":[1]}""", "dạng [x, y]")]
    [InlineData("""{"op":"trim","handles":["1F"],"from":[1,1],"to":[1,1]}""", "from trùng to")]
    [InlineData("""{"op":"trim","handles":["1F"],"from":[1,1],"layer":"A"}""", "\"set\"")]
    [InlineData("""{"op":"replacePoints","handles":["1F","20"],"points":[[0,0],[1,0]]}""", "đúng một polyline")]
    [InlineData("""{"op":"replacepoints","handles":["1F"],"points":[[0,0]]}""", "points ≥ 2")]
    [InlineData("""{"op":"replacePoints","handles":["1F"],"points":[[0,0],[1,0]],"width":-2}""", "width")]
    public void OpMoi_GiaTriSai_BaoDungTruong(string op, string expected)
    {
        var plan = Plan("[" + op + "]");

        Assert.False(plan.IsValid);
        Assert.Contains(plan.Errors, e => e.Contains(expected, StringComparison.Ordinal));
    }
}
