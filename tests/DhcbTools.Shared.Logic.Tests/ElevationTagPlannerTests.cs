using DhcbTools.Shared.Logic.Mep;
using Xunit;

namespace DhcbTools.Shared.Logic.Tests;

/// <summary>Lọc category và Summary/Messages của <c>ElevationTag</c> — trước đây trong Core.</summary>
public class ElevationTagPlannerTests
{
    [Theory]
    [InlineData("OST_DuctCurves", "duct", true)]
    [InlineData("OST_PipeCurves", "Pipe", true)]
    [InlineData("OST_CableTray", "cabletray", true)]
    [InlineData("OST_Conduit", "Duct", false)]
    public void CategoryIncluded_CungLuatVoiSleeve(string bic, string filter, bool expected)
    {
        Assert.Equal(expected, ElevationTagPlanner.CategoryIncluded(bic, new[] { filter }));
        Assert.True(ElevationTagPlanner.CategoryIncluded(bic, null));
    }

    [Fact]
    public void SummaryVaDong()
    {
        Assert.Equal("[Xem trước] Sẽ gán cao độ cho 5 phần tử MEP.", ElevationTagPlanner.PreviewSummary(5));
        Assert.Equal("  12: đáy=3200.0mm, đỉnh=3500.5mm, tim=3350.3mm", ElevationTagPlanner.PreviewLine(12, 3200, 3500.5, 3350.26));
        Assert.Equal("Đã gán cao độ cho 4/5 phần tử MEP.", ElevationTagPlanner.WriteSummary(4, 5));
        Assert.Equal("Không gán được cao độ cho phần tử nào trong 5 phần tử.", ElevationTagPlanner.NothingWrittenSummary(5));
        Assert.Equal("Không gán được DHCB_Day cho 7: lý do", ElevationTagPlanner.SetFailedLine("DHCB_Day", 7, "lý do"));
    }

    [Fact]
    public void NothingWritten_ChiKhiCoKeHoachMaKhongGhiDuoc()
    {
        // 0/N là LỖI (dự án không có tham số cao độ), không phải thành công — bản đầu từng báo như thường.
        Assert.True(ElevationTagPlanner.NothingWritten(0, 5));
        Assert.False(ElevationTagPlanner.NothingWritten(0, 0));
        Assert.False(ElevationTagPlanner.NothingWritten(1, 5));
    }
}
