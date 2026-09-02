using DhcbTools.Shared.Logic.Ai;
using Xunit;

namespace DhcbTools.Shared.Logic.Tests;

/// <summary>
/// Suy đoán kiểu trường từ tên khoá (giai đoạn 9.1). Đoán sai một luật là form động dựng sai ô nhập
/// cho cả một nhóm lệnh — ví dụ hiện textbox thay vì checkbox, hoặc mất nút chọn file.
/// </summary>
public class FieldKindTests
{
    [Theory]
    [InlineData("outputPath", FieldKind.FilePath)]
    [InlineData("inputPath", FieldKind.FilePath)]
    [InlineData("rulesPath", FieldKind.FilePath)]
    [InlineData("familyPaths", FieldKind.FilePath)]
    [InlineData("outputFolder", FieldKind.FolderPath)]
    public void DuongDan_TheoHauTo(string name, FieldKind expected) =>
        Assert.Equal(expected, FieldKindGuess.Of(name));

    [Theory]
    [InlineData("spacingMm", FieldKind.Number)]
    [InlineData("clearanceMm", FieldKind.Number)]
    [InlineData("elbowAngleDeg", FieldKind.Number)]
    [InlineData("maxVelocityMs", FieldKind.Number)]
    [InlineData("slopePercent", FieldKind.Number)]
    [InlineData("startNumber", FieldKind.Number)]
    [InlineData("elementId", FieldKind.Number)]
    public void So_TheoHauToDonVi(string name, FieldKind expected) =>
        Assert.Equal(expected, FieldKindGuess.Of(name));

    [Theory]
    [InlineData("dryRun")]
    [InlineData("useOllama")]
    [InlineData("createMissing")]
    [InlineData("purgeRegApps")]
    [InlineData("pinAfterCopy")]
    [InlineData("deleteEmptySource")]
    [InlineData("checkOnly")]
    [InlineData("buildRoute")]
    [InlineData("create3dView")]   // "create" + '3' nên luật tiền tố không bắt được, phải khai riêng
    [InlineData("remove")]
    [InlineData("reset")]
    public void Bool_NhanDungCoBatTat(string name) =>
        Assert.Equal(FieldKind.Bool, FieldKindGuess.Of(name));

    /// <summary>
    /// Bẫy dễ sập nhất của luật tiền tố: "category" bắt đầu bằng "cat" nhưng không phải bool,
    /// còn "keepNameContains" bắt đầu bằng "keep" + chữ hoa nhưng là chuỗi lọc.
    /// </summary>
    [Theory]
    [InlineData("category", FieldKind.Category)]
    [InlineData("categories", FieldKind.Category)]
    [InlineData("categoriesA", FieldKind.Category)]
    [InlineData("obstacleCategories", FieldKind.Category)]
    [InlineData("keepNameContains", FieldKind.Text)]
    [InlineData("checkOnly", FieldKind.Bool)]
    public void TienTo_KhongDuocBatNham(string name, FieldKind expected) =>
        Assert.Equal(expected, FieldKindGuess.Of(name));

    [Theory]
    [InlineData("parameterName", FieldKind.Parameter)]
    [InlineData("parameterNames", FieldKind.Parameter)]
    [InlineData("spoolParameter", FieldKind.Parameter)]
    [InlineData("levelName", FieldKind.Level)]
    [InlineData("viewTemplateName", FieldKind.View)]
    [InlineData("sleeveFamilyName", FieldKind.FamilyType)]
    [InlineData("hangerFamilyName", FieldKind.FamilyType)]
    [InlineData("typeName", FieldKind.FamilyType)]
    public void TruongLayTuMoHinh_NhanDung(string name, FieldKind expected) =>
        Assert.Equal(expected, FieldKindGuess.Of(name));

    /// <summary>Trường nhận object/mảng-object JSON phải là Text để form hiện ô JSON thô, không phải combo.</summary>
    [Theory]
    [InlineData("levels")]
    [InlineData("grids")]
    [InlineData("colors")]
    [InlineData("roomFilter")]
    public void TruongJsonTho_LaText(string name) =>
        Assert.Equal(FieldKind.Text, FieldKindGuess.Of(name));

    [Theory]
    [InlineData("formats")]
    [InlineData("kinds")]
    [InlineData("worksets")]
    [InlineData("sheetNumbers")]
    public void DanhSachChuoi_NhanDung(string name) =>
        Assert.Equal(FieldKind.TextList, FieldKindGuess.Of(name));

    [Theory]
    [InlineData("namePattern")]
    [InlineData("numberPattern")]
    [InlineData("find")]
    [InlineData("replace")]
    [InlineData("prefix")]
    [InlineData("target")]
    [InlineData("viewName")]
    [InlineData("lowerEnd")]
    public void MacDinh_LaText(string name) =>
        Assert.Equal(FieldKind.Text, FieldKindGuess.Of(name));

    [Fact]
    public void TenRong_KhongNem()
    {
        Assert.Equal(FieldKind.Text, FieldKindGuess.Of(""));
        Assert.Equal(FieldKind.Text, FieldKindGuess.Of("   "));
        Assert.Equal(FieldKind.Text, FieldKindGuess.Of(null!));
    }

    /// <summary>
    /// Chốt chặn cho toàn catalog: mọi lệnh phải có Fields khớp ConfigFields, và không trường nào
    /// rơi vào combo lấy-từ-mô-hình mà thực chất là chuỗi tự do (đã kiểm tay từng cái một lần).
    /// </summary>
    [Fact]
    public void MoiLenh_CoFieldsKhopConfigFields()
    {
        foreach (var app in new[] { CommandCatalog.Revit, CommandCatalog.AutoCad })
        {
            foreach (var cmd in CommandCatalog.AllFor(app))
            {
                Assert.Equal(cmd.ConfigFields.Count, cmd.Fields.Count);
                foreach (var field in cmd.Fields)
                {
                    Assert.True(cmd.ConfigFields.ContainsKey(field.Name),
                        $"{cmd.Name}: trường {field.Name} có trong Fields mà không có trong ConfigFields");
                }
            }
        }
    }

    /// <summary>Khai báo kiểu thẳng phải thắng suy đoán theo tên.</summary>
    [Fact]
    public void KieuKhaiBaoThang_ThangSuyDoan()
    {
        var descriptor = new CommandDescriptor("Thử", CommandCatalog.Revit, "mô tả", false)
            .Field("outputPath", "vốn là FilePath", FieldKind.Text);

        Assert.Equal(FieldKind.Text, descriptor.Fields.Single().Kind);
    }

    /// <summary>Khai báo lại cùng tên thì thay thế, không nhân đôi.</summary>
    [Fact]
    public void KhaiBaoTrungTen_ThayTheChuKhongNhanDoi()
    {
        var descriptor = new CommandDescriptor("Thử", CommandCatalog.Revit, "mô tả", false)
            .Field("x", "lần đầu")
            .Field("x", "lần sau", FieldKind.Bool);

        Assert.Single(descriptor.Fields);
        Assert.Equal(FieldKind.Bool, descriptor.Fields[0].Kind);
        Assert.Equal("lần sau", descriptor.ConfigFields["x"]);
    }

    /// <summary>MCP schema phải nói đúng kiểu JSON, nếu không model local hay điền số vào ô chuỗi.</summary>
    [Fact]
    public void McpSchema_CoKieuJsonDung()
    {
        var json = Newtonsoft.Json.Linq.JObject.FromObject(CommandCatalog.Describe(CommandCatalog.Revit));
        var properties = json["tools"]!.First(t => (string?)t["name"] == "HangerAuto")!["inputSchema"]!["properties"]!;

        Assert.Equal("number", (string?)properties["spacingMm"]!["type"]);
        Assert.Equal("string", (string?)properties["hangerFamilyName"]!["type"]);
    }
}
