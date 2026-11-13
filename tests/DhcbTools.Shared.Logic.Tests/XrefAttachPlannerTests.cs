using DhcbTools.Shared.Logic.Cad;
using Xunit;

namespace DhcbTools.Shared.Logic.Tests;

/// <summary>Kiểm yêu cầu của lệnh <c>XrefAttach</c> trước khi gắn — phiên 2026-11-13 phải gắn overlay qua COM.</summary>
public class XrefAttachPlannerTests
{
    private const string Floor = @"C:\DuAn\ThapA\1F.dwg";

    private static XrefAttachPlan Plan(XrefAttachRequest request, string? host = @"C:\DuAn\ThapA\BOQ.dwg") =>
        XrefAttachPlanner.Plan(request, path => path.EndsWith("1F.dwg", StringComparison.OrdinalIgnoreCase), host);

    [Fact]
    public void HopLe_TenMacDinhLaTenFile()
    {
        var plan = Plan(new XrefAttachRequest { Path = "\"" + Floor + "\"", Position = new[] { 0.0, 0, 0 }, Scale = 1, Rotation = 90, Layer = "X-REF" });

        Assert.True(plan.IsValid, string.Join("; ", plan.Errors));
        Assert.Equal("1F", plan.Name);
        Assert.Equal(Floor, plan.FullPath);
    }

    [Fact]
    public void TenTuDat_DuocGiu()
    {
        var plan = Plan(new XrefAttachRequest { Path = Floor, Name = " MB-1F " });

        Assert.True(plan.IsValid);
        Assert.Equal("MB-1F", plan.Name);
    }

    [Theory]
    [InlineData(null, null, "Thiếu \"path\"")]
    [InlineData(@"ThapA\1F.dwg", null, "đường dẫn đầy đủ")]
    [InlineData(@"C:\DuAn\ThapA\1F.dxf", null, "không phải file .dwg")]
    [InlineData(@"C:\DuAn\ThapA\2F.dwg", null, "Không tìm thấy")]
    [InlineData(Floor, "1F|A", "ký tự cấm")]
    public void DuongDanVaTenSai_BaoLoi(string? path, string? name, string expected)
    {
        var plan = Plan(new XrefAttachRequest { Path = path, Name = name });

        Assert.False(plan.IsValid);
        Assert.Contains(plan.Errors, e => e.Contains(expected, StringComparison.Ordinal));
    }

    [Fact]
    public void KhongGanChinhMinh()
    {
        var plan = Plan(new XrefAttachRequest { Path = Floor }, host: Floor.ToUpperInvariant());

        Assert.Contains(plan.Errors, e => e.Contains("chính nó", StringComparison.Ordinal));
    }

    [Fact]
    public void ThamSoHinhHocSai_BaoTungTruong()
    {
        var plan = Plan(new XrefAttachRequest { Path = Floor, Position = new[] { double.NaN, 0 }, Scale = 0, Rotation = double.PositiveInfinity, Layer = " " });

        Assert.Contains(plan.Errors, e => e.Contains("\"position\"", StringComparison.Ordinal));
        Assert.Contains(plan.Errors, e => e.Contains("\"scale\"", StringComparison.Ordinal));
        Assert.Contains(plan.Errors, e => e.Contains("\"rotation\"", StringComparison.Ordinal));
        Assert.Contains(plan.Errors, e => e.Contains("\"layer\"", StringComparison.Ordinal));
    }

    [Fact]
    public void TenDaCo_CungFileThiChenThem_KhacFileHoacBlockThuongThiTuChoi()
    {
        Assert.Null(XrefAttachPlanner.ExistingNameProblem("1F", true, Floor.ToLowerInvariant(), Floor));
        Assert.Null(XrefAttachPlanner.ExistingNameProblem("1F", true, @".\1F.dwg", Floor));
        Assert.Contains("trỏ tới", XrefAttachPlanner.ExistingNameProblem("1F", true, @"C:\Khac\1F_cu.dwg", Floor));
        Assert.Contains("block thường", XrefAttachPlanner.ExistingNameProblem("1F", false, null, Floor));
        Assert.False(XrefAttachPlanner.SamePath(null, Floor));
    }
}
