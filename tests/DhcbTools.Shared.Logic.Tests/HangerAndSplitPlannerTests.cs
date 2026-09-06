using DhcbTools.Shared.Logic.Mep;
using Xunit;

namespace DhcbTools.Shared.Logic.Tests;

/// <summary>Phần quyết định và Summary/Messages của <c>HangerAuto</c> và <c>PipeSplitter</c> — trước đây trong Core.</summary>
public class HangerAndSplitPlannerTests
{
    // ── HangerPlanner ────────────────────────────────────────────────────────

    [Theory]
    [InlineData(1, 0, true)]        // dọc X thuần nhưng |dx| > 0.01 → vẫn xoay (góc 0) — đúng hành vi cũ
    [InlineData(0, 1, true)]
    [InlineData(0.005, 0.005, false)]
    [InlineData(0, 0, false)]       // tuyến thẳng đứng: không xoay theo mặt bằng
    public void NeedsRotation_TheoNguongTrucX(double dx, double dy, bool expected)
    {
        Assert.Equal(expected, HangerPlanner.NeedsRotation(dx, dy));
    }

    [Fact]
    public void RotationAngle_LaAtan2()
    {
        Assert.Equal(Math.PI / 2, HangerPlanner.RotationAngle(0, 1), 9);
        Assert.Equal(0, HangerPlanner.RotationAngle(1, 0), 9);
        Assert.Equal(Math.PI, Math.Abs(HangerPlanner.RotationAngle(-1, 0)), 9);
    }

    [Fact]
    public void Hanger_SummaryVaPreview()
    {
        Assert.Equal(string.Empty, HangerPlanner.SkipNote(0));
        Assert.Equal(" Bỏ qua, đã có hanger: 3 vị trí.", HangerPlanner.SkipNote(3));
        Assert.Equal("[Xem trước] Sẽ đặt 10 hanger trên 4 phần tử MEP. Bỏ qua, đã có hanger: 2 vị trí.", HangerPlanner.PreviewSummary(10, 4, 2));
        Assert.Equal("  → (1000, 2001, 3000) mm  dir=(0.71,0.71,0.00)", HangerPlanner.PreviewLine(1000.4, 2000.6, 3000, 0.7071, 0.7071, 0));
        Assert.Equal("Đã đặt 8 hanger trên 4 phần tử MEP.", HangerPlanner.WriteSummary(8, 4, 0, 0, 8));
        Assert.Equal("Đã đặt 8 hanger trên 4 phần tử MEP. Bỏ qua, đã có hanger: 1 vị trí. 2/10 vị trí đặt lỗi.", HangerPlanner.WriteSummary(8, 4, 1, 2, 10));
    }

    [Fact]
    public void Hanger_LyDoLoi_CoTranVaKhongTrung()
    {
        var reasons = new List<string>();
        HangerPlanner.AddDistinctReason(reasons, "a");
        HangerPlanner.AddDistinctReason(reasons, "a");
        for (var i = 0; i < 10; i++) HangerPlanner.AddDistinctReason(reasons, "r" + i);

        Assert.Equal(HangerPlanner.MaxFailureReasons, reasons.Count);
        Assert.Throws<ArgumentNullException>(() => HangerPlanner.AddDistinctReason(null!, "x"));
        Assert.Null(HangerPlanner.FailureReasonsLine(0, reasons));
        Assert.Equal("2 vị trí không đặt được hanger. Lý do (tối đa 5 loại): a | b", HangerPlanner.FailureReasonsLine(2, new[] { "a", "b" }));
        Assert.EndsWith("loại): ", HangerPlanner.FailureReasonsLine(1, null));
    }

    // ── PipeSplitPlanner ─────────────────────────────────────────────────────

    [Theory]
    [InlineData("Pipe", true, true)]
    [InlineData("PipeCurves", true, true)]
    [InlineData("duct", true, false)]
    [InlineData("CableTray", false, false)]
    [InlineData("Conduit", false, false)]
    [InlineData(null, false, false)]
    public void Split_CategoryCatDuocVaLaPipe(string? category, bool splittable, bool isPipe)
    {
        Assert.Equal(splittable, PipeSplitPlanner.IsSplittable(category));
        Assert.Equal(isPipe, PipeSplitPlanner.IsPipe(category));
    }

    [Fact]
    public void Split_SummaryVaDong()
    {
        Assert.Equal("(1000,2001,3000)mm", PipeSplitPlanner.FormatPointMm(1000.4, 2000.6, 3000));
        Assert.Equal("[Xem trước] Sẽ cắt 3 phần tử, tạo 7 điểm cắt.", PipeSplitPlanner.PreviewSummary(3, 7, 0));
        Assert.Equal("[Xem trước] Sẽ cắt 3 phần tử, tạo 7 điểm cắt. 2 CableTray/Conduit quá dài chỉ liệt kê (Revit không có API cắt), không tính vào tổng.", PipeSplitPlanner.PreviewSummary(3, 7, 2));
        Assert.Equal("  Pipe 5: 2 điểm cắt tại (1,2,3)mm, (4,5,6)mm", PipeSplitPlanner.PreviewLine("Pipe", 5, true, new[] { (1.0, 2.0, 3.0), (4.0, 5.0, 6.0) }));
        Assert.Equal("  CableTray 6 [chỉ báo cáo]: 0 điểm cắt tại ", PipeSplitPlanner.PreviewLine("CableTray", 6, false, null!));
        Assert.Equal("Đã cắt 7 điểm trên 3 phần tử MEP.", PipeSplitPlanner.WriteSummary(7, 3, 0, 0));
        Assert.Equal("Đã cắt 7 điểm trên 3 phần tử MEP, 1 điểm cắt lỗi; 2 CableTray/Conduit quá dài chỉ báo cáo (không có API cắt).", PipeSplitPlanner.WriteSummary(7, 3, 1, 2));
        Assert.Equal("  Conduit 9 [chỉ báo cáo]: cần 4 điểm cắt, cắt tay.", PipeSplitPlanner.ReportOnlyLine("Conduit", 9, 4));
        Assert.Equal("Pipe 5: BreakCurve không tạo được đoạn mới tại (1,2,3)mm.", PipeSplitPlanner.BreakReturnedNothing("Pipe", 5, 1, 2, 3));
        Assert.Equal("Duct 5: không cắt được tại (1,2,3)mm — lý do", PipeSplitPlanner.BreakFailed("Duct", 5, 1, 2, 3, "lý do"));
    }
}
