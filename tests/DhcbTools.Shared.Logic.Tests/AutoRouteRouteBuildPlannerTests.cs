using System;
using System.Collections.Generic;
using System.Linq;
using DhcbTools.Shared.Logic.Mep;
using Xunit;

namespace DhcbTools.Shared.Logic.Tests;

/// <summary>
/// Phần quyết định của <c>AutoRoute</c> và <c>RouteFromLines</c> sau khi tách xuống tầng thuần (§56). Chuỗi ghim
/// từng ký tự vì bộ <c>autoroute</c>/<c>write-mep</c> chốt bằng <c>summaryContains</c>.
/// </summary>
public class AutoRouteRouteBuildPlannerTests
{
    // ───────────────────────── AutoRoutePlanner ─────────────────────────

    [Fact]
    public void SearchBounds_BaoHaiDiem_NoiTheoHaiBien()
    {
        var b = AutoRoutePlanner.SearchBounds(new Point3(100, 900, 50), new Point3(-200, 300, 10), 1000, 100);
        Assert.Equal(-1200, b.MinX); Assert.Equal(1100, b.MaxX);
        Assert.Equal(-700, b.MinY); Assert.Equal(1900, b.MaxY);
        Assert.Equal(-90, b.MinZ); Assert.Equal(150, b.MaxZ);
    }

    [Fact]
    public void Corners_TamDinhKhacNhau_VaBoundsOf_XoayVanBaoDu()
    {
        var corners = AutoRoutePlanner.Corners(0, 0, 0, 2, 4, 6).ToList();
        Assert.Equal(8, corners.Count);
        Assert.Equal(8, corners.Select(c => (c.X, c.Y, c.Z)).Distinct().Count());

        // Xoay 90° quanh Z: (x, y) → (−y, x). Biến đổi min/max cho hộp sai; tám đỉnh cho hộp đúng.
        var rotated = corners.Select(c => new Point3(-c.Y, c.X, c.Z));
        var b = AutoRoutePlanner.BoundsOf(rotated);
        Assert.Equal(-4, b.MinX); Assert.Equal(0, b.MaxX);
        Assert.Equal(0, b.MinY); Assert.Equal(2, b.MaxY);
        Assert.Equal(6, b.MaxZ);
    }

    [Fact]
    public void BoundsOf_Rong_Nem()
    {
        Assert.Throws<ArgumentException>(() => AutoRoutePlanner.BoundsOf(Array.Empty<Point3>()));
    }

    [Theory]
    [InlineData(true, false, false, false, false)]   // cửa, không bật → không phải lỗ
    [InlineData(true, true, false, false, true)]     // cửa, bật → lỗ
    [InlineData(false, false, true, false, false)]   // tường nhúng → đặc
    [InlineData(false, false, false, true, false)]   // liên kết kết cấu → đặc
    [InlineData(false, false, false, false, true)]   // shaft/opening → lỗ
    public void IsPassableOpening_TheoBonCo(bool door, bool includeDoors, bool embeddedWall, bool structConn, bool expected)
    {
        Assert.Equal(expected, AutoRoutePlanner.IsPassableOpening(door, includeDoors, embeddedWall, structConn));
    }

    [Fact]
    public void HoleTouchesHost_NullHoacKhongChong_False()
    {
        var host = new Box3(0, 0, 0, 1000, 200, 3000);
        Assert.False(AutoRoutePlanner.HoleTouchesHost(null, host));
        Assert.False(AutoRoutePlanner.HoleTouchesHost(new Box3(5000, 0, 0, 5500, 200, 2000), host));
        Assert.True(AutoRoutePlanner.HoleTouchesHost(new Box3(400, 0, 500, 600, 200, 2500), host));
    }

    [Fact]
    public void ObstaclePieces_CoLo_ThanhNhieuManh_KhongLo_NguyenHop()
    {
        var host = new Box3(0, 0, 0, 1000, 200, 3000);
        Assert.Single(AutoRoutePlanner.ObstaclePieces(host, new List<Box3>()));
        var pieces = AutoRoutePlanner.ObstaclePieces(host, new List<Box3> { new Box3(400, 0, 500, 600, 200, 2500) });
        Assert.True(pieces.Count > 1);
        Assert.All(pieces, p => Assert.False(p.Contains(500, 100, 1500)));
    }

    [Fact]
    public void OpeningLine_DinhDangKichThuocTamVaVatChu()
    {
        var line = AutoRoutePlanner.OpeningLine("Generic Models", new Box3(0, 0, 0, 1118, 124, 2134), "Walls", 2454523);
        Assert.Equal("Generic Models 1118×124×2134 tại (559,62,1067) trên Walls 2454523", line);
        Assert.StartsWith("? ", AutoRoutePlanner.OpeningLine(null, new Box3(0, 0, 0, 1, 1, 1), null, 1));
        Assert.EndsWith("trên ? 1", AutoRoutePlanner.OpeningLine(null, new Box3(0, 0, 0, 1, 1, 1), null, 1));
    }

    [Fact]
    public void LinkSelected_RongLaMoiLink_CoLocThiKhongPhanBietHoa()
    {
        Assert.True(AutoRoutePlanner.LinkSelected("STR-L03", new List<string>()));
        Assert.True(AutoRoutePlanner.LinkSelected("Snowdon Towers Sample Structural", new List<string> { "structural" }));
        Assert.False(AutoRoutePlanner.LinkSelected("Site", new List<string> { "STR", "ARC" }));
    }

    [Fact]
    public void LinkLines_BaCau()
    {
        Assert.Equal("A: chưa nạp (unloaded) — bỏ qua", AutoRoutePlanner.LinkUnloadedLine("A"));
        Assert.Equal("A: không có category vật cản nào", AutoRoutePlanner.LinkNoCategoryLine("A"));
        Assert.Equal("A: 13 vật cản", AutoRoutePlanner.LinkCountLine("A", 13));
    }

    [Fact]
    public void SourceText_MucCVaMucD()
    {
        Assert.Equal("0 trong file + 13 từ model liên kết, mức C: không đục lỗ mở", AutoRoutePlanner.SourceText(0, 13, false, 5));
        Assert.Equal("2 trong file + 6 từ model liên kết, 1 lỗ mở đã đục", AutoRoutePlanner.SourceText(2, 6, true, 1));
    }

    [Fact]
    public void FailSummary_GiuReason_VaCoLuoi()
    {
        var path = new PathResult { Reason = "Hết ngân sách", GridCells = 20951, ExpandedNodes = 11935, MaxExpandedNodes = 400000 };
        var s = AutoRoutePlanner.FailSummary(path, 15, "nguồn", 100);
        Assert.Equal("Không tìm được tuyến (Hết ngân sách). Đã xét 15 chướng ngại (nguồn), lưới 20,951 ô bước 100 mm, mở rộng 11,935/400,000 node.", s);
    }

    [Fact]
    public void FoundMessage_VaSummaries_MangThuocDoChatLuong()
    {
        var path = new PathResult { ExpandedNodes = 28, Turns = 0 };
        path.Polyline.Add(new Point3(0, 0, 0));
        path.Polyline.Add(new Point3(1350, 0, 0));
        path.ComputeQuality();
        Assert.Equal("6 chướng ngại trong hộp tìm kiếm (src), 28 node, 0 lần rẽ, 1 đoạn, tổng 1.4 m.", AutoRoutePlanner.FoundMessage(path, 6, "src", 1));
        Assert.Equal("[Xem trước] Tuyến 1 đoạn, dài 1,4 m = 1,00× Manhattan, 0 rẽ (tối thiểu 0), né 6 vật cản (src).", AutoRoutePlanner.PreviewSummary(1, path, 6, "src"));
        Assert.Equal("Đã vẽ 1 model line (line style \"DHCB-Route\"), dài 1,4 m = 1,00× Manhattan, 0 rẽ (tối thiểu 0), né 6 vật cản (src).",
            AutoRoutePlanner.WrittenSummary(1, "DHCB-Route", path, 6, "src"));
    }

    [Fact]
    public void NoObstacleWarning_ChiKhi0_HaiCauTheoCoLink()
    {
        Assert.Null(AutoRoutePlanner.NoObstacleWarning(1, true));
        Assert.Contains("kể cả từ model liên kết", AutoRoutePlanner.NoObstacleWarning(0, true));
        Assert.Contains("includeLinkedModels đang tắt", AutoRoutePlanner.NoObstacleWarning(0, false));
    }

    [Fact]
    public void SegmentLine_LamTronMm()
    {
        Assert.Equal("(1,2,3) → (4,5,6)", AutoRoutePlanner.SegmentLine(new Point3(1.4, 2.4, 2.6), new Point3(4, 5, 6)));
    }

    [Fact]
    public void ObstacleDetails_Tran30DongLoMo_LinkKhongTran()
    {
        var links = new List<string> { "A: 1 vật cản" };
        var holes = Enumerable.Range(1, 35).Select(i => "lỗ " + i).ToList();
        var lines = AutoRoutePlanner.ObstacleDetails(links, holes).ToList();
        Assert.Equal(1 + 30 + 1, lines.Count);
        Assert.Equal("  Link — A: 1 vật cản", lines[0]);
        Assert.Equal("  Lỗ mở — lỗ 1", lines[1]);
        Assert.Equal("  … và 5 lỗ mở nữa.", lines.Last());
        Assert.Equal(3, AutoRoutePlanner.ObstacleDetails(new List<string>(), holes.Take(3).ToList()).Count());
    }

    // ───────────────────────── RouteBuildPlanner ─────────────────────────

    private static RouteGraph<string> LGraph()
    {
        // Hai đoạn vuông góc gặp nhau tại (10,0,0): một elbow, hai đầu hở.
        return RouteGraph<string>.Build(new[]
        {
            new RouteSegment<string>("a", new Point3(0, 0, 0), new Point3(10, 0, 0)),
            new RouteSegment<string>("b", new Point3(10, 0, 0), new Point3(10, 8, 0)),
        }, 0.01);
    }

    [Fact]
    public void Segment_EpCaoDoKhiCoOffset_GiuNguyenKhiKhong()
    {
        var s = RouteBuildPlanner.Segment("k", new Point3(0, 0, 5), new Point3(1, 0, 7), 12.5);
        Assert.Equal(12.5, s.Start.Z); Assert.Equal(12.5, s.End.Z); Assert.Equal("k", s.Key);
        var t = RouteBuildPlanner.Segment("k", new Point3(0, 0, 5), new Point3(1, 0, 7), null);
        Assert.Equal(5, t.Start.Z); Assert.Equal(7, t.End.Z);
    }

    [Fact]
    public void FittingPlan_VaCount_ChiDinhCanFitting()
    {
        var g = LGraph();
        var plan = RouteBuildPlanner.FittingPlan(g);
        Assert.Single(plan);
        Assert.Equal(FittingKind.Elbow, plan[0].Kind);
        var c = RouteBuildPlanner.Count(plan);
        Assert.Equal(1, c.Elbows); Assert.Equal(0, c.Tees); Assert.Equal(0, c.Crosses); Assert.Equal(1, c.Total);
    }

    [Fact]
    public void Count_DemDuBaLoai()
    {
        var node = new RouteNode(0, new Point3(0, 0, 0));
        var c = RouteBuildPlanner.Count(new[]
        {
            (node, FittingKind.Elbow), (node, FittingKind.Tee), (node, FittingKind.Tee), (node, FittingKind.Cross), (node, FittingKind.None),
        });
        Assert.Equal(1, c.Elbows); Assert.Equal(2, c.Tees); Assert.Equal(1, c.Crosses);
    }

    [Fact]
    public void PreviewSummary_VaEdgeLine_GiuChuoi()
    {
        var g = LGraph();
        var f = RouteBuildPlanner.Count(RouteBuildPlanner.FittingPlan(g));
        Assert.Equal("[Xem trước] Sẽ dựng 2 đoạn Duct (Mitered Elbows / Taps), 1 elbow, 0 tee, 0 cross; 0 đoạn bị bỏ.",
            RouteBuildPlanner.PreviewSummary(g.Edges.Count, "Duct", "Mitered Elbows / Taps", f, g.Rejected.Count));
        var line = RouteBuildPlanner.PreviewEdgeLine(g.Edges[0], g);
        Assert.StartsWith("Đoạn a: ", line);
        Assert.EndsWith(" (ft)", line);
    }

    [Theory]
    [InlineData(true, 0, true)]
    [InlineData(true, 1, false)]
    [InlineData(false, 0, false)]
    public void ShouldDeleteLines_ChiKhiBatVaKhongLoi(bool flag, int errors, bool expected)
    {
        Assert.Equal(expected, RouteBuildPlanner.ShouldDeleteLines(flag, errors));
    }

    [Fact]
    public void FinalSummary_VaIsSuccess()
    {
        Assert.Equal("Đã dựng 3/3 đoạn Duct, 2 fitting OK, 0 fitting lỗi, 0 mối nối tự động.", RouteBuildPlanner.FinalSummary(3, 3, "Duct", 2, 0, 0));
        Assert.True(RouteBuildPlanner.IsSuccess(1));
        Assert.False(RouteBuildPlanner.IsSuccess(0));
    }

    [Fact]
    public void ThongBaoLoi_BonCau()
    {
        var p = new Point3(1, 2, 3);
        Assert.Equal("Không có model/detail line nào dùng line style \"DHCB-Route\".", RouteBuildPlanner.NoLinesMessage("DHCB-Route"));
        Assert.Equal("Không tìm thấy type \"X\" cho Foo (hợp lệ: Duct, Pipe, CableTray, Conduit).", RouteBuildPlanner.NoTypeMessage("X", "Foo"));
        Assert.Contains("thiếu đoạn để dựng Elbow", RouteBuildPlanner.MissingCurvesMessage(p, FittingKind.Elbow));
        Assert.Contains("không tìm được connector", RouteBuildPlanner.NoConnectorMessage(p));
        Assert.Contains("5 nhánh — không có fitting", RouteBuildPlanner.NoFittingForDegreeMessage(p, 5));
        Assert.Equal($"Đỉnh {p}: Tee thất bại (vì) — connector để hở, kỹ sư nối tay. Đoạn: 1, 2",
            RouteBuildPlanner.FittingFailedMessage(p, FittingKind.Tee, "vì", new[] { "1", "2" }));
    }
}
