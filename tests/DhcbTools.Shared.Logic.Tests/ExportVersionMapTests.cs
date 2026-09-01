using DhcbTools.Shared.Logic;
using Xunit;

namespace DhcbTools.Shared.Logic.Tests;

public class ExportVersionMapTests
{
    [Theory]
    [InlineData("AcadRelease2018", "R2018")]
    [InlineData("2013", "R2013")]
    [InlineData("R2007", "R2007")]
    [InlineData("AutoCAD 2010", "R2010")]
    public void TryParseAcadVersion_NhanDienNamPhatHanh(string input, string expected)
    {
        Assert.True(ExportVersionMap.TryParseAcadVersion(input, out var name));
        Assert.Equal(expected, name);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("R2099")]
    public void TryParseAcadVersion_KhongNhanRa_BaoFalseVaDungMacDinh(string? input)
    {
        Assert.False(ExportVersionMap.TryParseAcadVersion(input!, out var name));
        Assert.Equal(ExportVersionMap.DefaultAcadVersion, name);
    }

    [Theory]
    [InlineData("IFC2x3", "IFC2x3")]
    [InlineData("2x3", "IFC2x3")]
    [InlineData("IFC2x3 Coordination View 2.0", "IFC2x3")]
    [InlineData("IFC4", "IFC4")]
    [InlineData("IFC4 Design Transfer View", "IFC4DTV")]
    [InlineData("IFC4 Reference View", "IFC4RV")]
    public void TryParseIfcVersion_NhanDienPhienBan(string input, string expected)
    {
        Assert.True(ExportVersionMap.TryParseIfcVersion(input, out var name));
        Assert.Equal(expected, name);
    }

    [Fact]
    public void TryParseIfcVersion_Ifc2x3CoSo4TrongTen_KhongBiDocNhamThanhIfc4()
    {
        // Bản cũ chỉ tìm ký tự '4' nên chuỗi này bị đọc nhầm thành IFC4.
        Assert.True(ExportVersionMap.TryParseIfcVersion("IFC2x3 CV 2.0 + 4D", out var name));
        Assert.Equal("IFC2x3", name);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("STEP")]
    public void TryParseIfcVersion_KhongNhanRa_BaoFalseVaDungMacDinh(string? input)
    {
        Assert.False(ExportVersionMap.TryParseIfcVersion(input!, out var name));
        Assert.Equal(ExportVersionMap.DefaultIfcVersion, name);
    }
}
