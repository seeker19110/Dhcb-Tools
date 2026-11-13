using DhcbTools.Shared.Logic.Cad;
using Xunit;

namespace DhcbTools.Shared.Logic.Tests;

/// <summary>
/// Layer phụ thuộc xref <c>XREF|LAYER</c>: LayerImport cho đổi trạng thái layer xref đã có (phiên 2026-11-13 phải đóng
/// băng qua COM) nhưng vẫn không bao giờ tạo layer có '|'.
/// </summary>
public class XrefLayerNameTests
{
    [Theory]
    [InlineData("1F|M-HVAC-DUCT-1", "1F", "M-HVAC-DUCT-1")]
    [InlineData(" KT-T3 | A-WALL ", "KT-T3", "A-WALL")]
    [InlineData("X|0", "X", "0")]
    public void TachDungTenXref(string name, string xref, string layer)
    {
        Assert.True(XrefLayerName.TrySplit(name, out var x, out var l));
        Assert.Equal(xref, x);
        Assert.Equal(layer, l);
        Assert.True(XrefLayerName.IsDependentName(name));
    }

    [Theory]
    [InlineData("M-HVAC-DUCT-1")]
    [InlineData("|LAYER")]
    [InlineData("XREF|")]
    [InlineData("A|B|C")]
    [InlineData(" | ")]
    [InlineData("")]
    [InlineData(null)]
    public void KhongPhaiTenXref(string? name)
    {
        Assert.False(XrefLayerName.TrySplit(name, out _, out _));
        Assert.False(XrefLayerName.IsDependentName(name));
    }

    [Fact]
    public void ChiSuaLayerXrefDaCo_KhongTaoMoi()
    {
        Assert.Null(XrefLayerName.ImportProblem("1F|M-DUCT", exists: true, isDependent: true));

        var missing = XrefLayerName.ImportProblem("1F|M-DUCT", exists: false, isDependent: false);
        Assert.NotNull(missing);
        Assert.Contains("không tạo layer có '|'", missing);

        var notDependent = XrefLayerName.ImportProblem("1F|M-DUCT", exists: true, isDependent: false);
        Assert.Contains("không phải layer phụ thuộc xref", notDependent);
    }

    [Fact]
    public void CanhBaoVisretain_NeuSoLayer()
    {
        var text = XrefLayerName.VisretainWarning(3);
        Assert.Contains("VISRETAIN", text);
        Assert.Contains("3 layer xref", text);
    }
}
