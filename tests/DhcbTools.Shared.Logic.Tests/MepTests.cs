using DhcbTools.Shared.Logic.Mep;
using Xunit;

namespace DhcbTools.Shared.Logic.Tests;

public class RouteGraphTests
{
    private static RouteSegment<string> Seg3(string k, double x1, double y1, double x2, double y2)
        => new(k, new Point3(x1, y1, 0), new Point3(x2, y2, 0));

    [Fact]
    public void TuyenChuU_CoNhanhT_PhanLoaiDungFitting()
    {
        // U: (0,0)→(0,10)→(10,10)→(10,0), nhánh T từ (5,10) lên (5,15)
        var segs = new[]
        {
            Seg3("a", 0, 0, 0, 10),
            Seg3("b", 0, 10, 5, 10),
            Seg3("c", 5, 10, 10, 10),
            Seg3("d", 10, 10, 10, 0),
            Seg3("t", 5, 10, 5, 15),
        };

        var g = RouteGraph<string>.Build(segs, tolerance: 0.01);

        Assert.Equal(5, g.Edges.Count);
        Assert.Empty(g.Rejected);
        Assert.Equal(1, g.ComponentCount);

        var kinds = g.Nodes.Select(n => g.FittingAt(n.Id)).ToList();
        Assert.Equal(2, kinds.Count(k => k == FittingKind.Elbow));
        Assert.Equal(1, kinds.Count(k => k == FittingKind.Tee));
        Assert.Equal(3, kinds.Count(k => k == FittingKind.None)); // 3 đầu hở
    }

    [Fact]
    public void DauMutLechDuoiDungSai_VanNoi()
    {
        var segs = new[] { Seg3("a", 0, 0, 0, 10), Seg3("b", 0.0005, 10, 10, 10) };
        var g = RouteGraph<string>.Build(segs, tolerance: 0.001);
        Assert.Equal(3, g.Nodes.Count);
        Assert.Equal(FittingKind.Elbow, g.FittingAt(g.Nodes.Single(n => n.Degree == 2).Id));
    }

    [Fact]
    public void HaiDoanThangHang_KhongCanElbow()
    {
        var segs = new[] { Seg3("a", 0, 0, 0, 10), Seg3("b", 0, 10, 0, 20) };
        var g = RouteGraph<string>.Build(segs, 0.001);
        Assert.Equal(FittingKind.None, g.FittingAt(g.Nodes.Single(n => n.Degree == 2).Id));
        Assert.InRange(g.AngleAt(g.Nodes.Single(n => n.Degree == 2).Id), 0, 0.01);
    }

    [Fact]
    public void ChuTrinh_BiBaoVaLoaiMotCanh()
    {
        var segs = new[] { Seg3("a", 0, 0, 10, 0), Seg3("b", 10, 0, 10, 10), Seg3("c", 10, 10, 0, 10), Seg3("d", 0, 10, 0, 0) };
        var g = RouteGraph<string>.Build(segs, 0.001);

        Assert.Equal(3, g.Edges.Count);
        Assert.Single(g.Rejected);
        Assert.Contains(g.Warnings, w => w.Contains("chu trình"));
    }

    [Fact]
    public void DoanSuyBien_BiBoQua()
    {
        var g = RouteGraph<string>.Build(new[] { Seg3("z", 1, 1, 1, 1), Seg3("a", 0, 0, 5, 0) }, 0.001);
        Assert.Single(g.Edges);
        Assert.Single(g.Rejected);
    }

    [Fact]
    public void BonNhanh_LaCross_NamNhanh_KhongHoTro()
    {
        var cross = new[] { Seg3("a", -1, 0, 0, 0), Seg3("b", 1, 0, 0, 0), Seg3("c", 0, -1, 0, 0), Seg3("d", 0, 1, 0, 0) };
        var g = RouteGraph<string>.Build(cross, 0.001);
        Assert.Equal(FittingKind.Cross, g.FittingAt(g.Nodes.Single(n => n.Degree == 4).Id));

        var five = cross.Concat(new[] { new RouteSegment<string>("e", new Point3(0, 0, 1), new Point3(0, 0, 0)) });
        var g5 = RouteGraph<string>.Build(five, 0.001);
        Assert.Equal(FittingKind.Unsupported, g5.FittingAt(g5.Nodes.Single(n => n.Degree == 5).Id));
        Assert.Contains(g5.Warnings, w => w.Contains("5 nhánh"));
    }

    [Fact]
    public void ThuTuDung_BatDauTuDauHo_DiLienTuc()
    {
        var segs = new[] { Seg3("b", 0, 10, 10, 10), Seg3("a", 0, 0, 0, 10), Seg3("c", 10, 10, 10, 0) };
        var g = RouteGraph<string>.Build(segs, 0.001);
        var order = g.EdgesInBuildOrder().Select(e => e.Key).ToList();

        Assert.Equal(3, order.Count);
        // Mỗi cạnh sau phải chạm một đỉnh đã đi qua.
        var visited = new HashSet<int>();
        var first = g.Edges.First(e => e.Key == order[0]);
        visited.Add(first.StartNode);
        visited.Add(first.EndNode);
        foreach (var k in order.Skip(1))
        {
            var e = g.Edges.First(x => x.Key == k);
            Assert.True(visited.Contains(e.StartNode) || visited.Contains(e.EndNode));
            visited.Add(e.StartNode);
            visited.Add(e.EndNode);
        }
    }

    [Fact]
    public void HaiTuyenRoi_LaHaiThanhPhan()
    {
        var g = RouteGraph<string>.Build(new[] { Seg3("a", 0, 0, 1, 0), Seg3("b", 5, 5, 6, 5) }, 0.001);
        Assert.Equal(2, g.ComponentCount);
    }
}

public class DevicePatternTests
{
    private static readonly List<Point2> Room = new() { new(0, 0), new(12000, 0), new(12000, 9000), new(0, 9000) };

    [Fact]
    public void LuoiTrongPhongChuNhat_CachTuongDuMargin()
    {
        var plan = DevicePattern.GridInPolygon(Room, new GridPatternOptions { SpacingX = 3000, SpacingY = 3000, Margin = 1500, CoverageRadius = 0 });

        Assert.Equal(4 * 3, plan.Points.Count); // (12000-3000)/3000+1 = 4; (9000-3000)/3000+1 = 3
        Assert.All(plan.Points, p => Assert.True(DevicePattern.DistanceToBoundary(Room, p) >= 1500 - 1e-6));
        Assert.All(plan.Points, p => Assert.True(DevicePattern.Contains(Room, p)));
    }

    [Fact]
    public void LuoiCanGiua_DoiXung()
    {
        var plan = DevicePattern.GridInPolygon(Room, new GridPatternOptions { SpacingX = 4000, SpacingY = 4000, Margin = 1000, CoverageRadius = 0 });
        var minX = plan.Points.Min(p => p.X);
        var maxX = plan.Points.Max(p => p.X);
        Assert.Equal(minX, 12000 - maxX, 6);
    }

    [Fact]
    public void DiemTrongLo_BiLoai()
    {
        var hole = new List<Point2> { new(4000, 3000), new(8000, 3000), new(8000, 6000), new(4000, 6000) };
        var without = DevicePattern.GridInPolygon(Room, new GridPatternOptions { CoverageRadius = 0 });
        var with = DevicePattern.GridInPolygon(Room, new GridPatternOptions { CoverageRadius = 0 }, new[] { hole });
        Assert.True(with.Points.Count < without.Points.Count);
        Assert.DoesNotContain(with.Points, p => DevicePattern.Contains(hole, p));
    }

    [Fact]
    public void KiemTraPhu_ChenThemKhiThieu()
    {
        // Lưới thưa 6 m nhưng bán kính phủ 2.3 m → phải chèn thêm.
        var plan = DevicePattern.GridInPolygon(Room, new GridPatternOptions { SpacingX = 6000, SpacingY = 6000, Margin = 1500, CoverageRadius = 2300, CoverageCheckStep = 500 });
        Assert.NotEmpty(plan.AddedForCoverage);
        Assert.Empty(plan.Uncovered);
    }

    [Fact]
    public void PhongDuPhu_KhongChenThem()
    {
        var plan = DevicePattern.GridInPolygon(Room, new GridPatternOptions { SpacingX = 3000, SpacingY = 3000, Margin = 1500, CoverageRadius = 2300 });
        Assert.Empty(plan.AddedForCoverage);
    }

    [Fact]
    public void PhongHep_DatMotThietBiOTam()
    {
        var narrow = new List<Point2> { new(0, 0), new(2000, 0), new(2000, 2000), new(0, 2000) };
        var plan = DevicePattern.GridInPolygon(narrow, new GridPatternOptions { Margin = 1500, CoverageRadius = 0 });
        var p = Assert.Single(plan.Points);
        Assert.Equal(1000, p.X, 6);
        Assert.Equal(1000, p.Y, 6);
    }

    [Fact]
    public void PhongChuL_DiemChiNamTrongPhong()
    {
        var l = new List<Point2> { new(0, 0), new(10000, 0), new(10000, 4000), new(4000, 4000), new(4000, 10000), new(0, 10000) };
        var plan = DevicePattern.GridInPolygon(l, new GridPatternOptions { SpacingX = 2000, SpacingY = 2000, Margin = 800, CoverageRadius = 0 });
        Assert.NotEmpty(plan.Points);
        Assert.All(plan.Points, p => Assert.True(DevicePattern.Contains(l, p)));
        Assert.DoesNotContain(plan.Points, p => p.X > 4000 && p.Y > 4000);
    }

    [Fact]
    public void DienTich_TrongTam()
    {
        Assert.Equal(12000.0 * 9000.0, DevicePattern.Area(Room), 6);
        var c = DevicePattern.Centroid(Room);
        Assert.Equal(6000, c.X, 6);
        Assert.Equal(4500, c.Y, 6);
    }

    [Fact]
    public void ThamSoSai_NemLoi()
    {
        Assert.Throws<ArgumentException>(() => DevicePattern.GridInPolygon(new List<Point2> { new(0, 0), new(1, 1) }, new GridPatternOptions()));
        Assert.Throws<ArgumentOutOfRangeException>(() => DevicePattern.GridInPolygon(Room, new GridPatternOptions { SpacingX = 0 }));
    }
}

public class SizingTests
{
    [Fact]
    public void Duct_MaSatGiamKhiDuongKinhTang()
    {
        var q = 0.5; // m³/s
        Assert.True(DuctSizing.FrictionPaPerM(q, 0.3) > DuctSizing.FrictionPaPerM(q, 0.4));
    }

    [Fact]
    public void Duct_500Lps_1PaPerM_ChonKhoang400Den450()
    {
        // ASHRAE ductulator: 500 L/s ở ~1 Pa/m ≈ Ø355 mm (v ≈ 5 m/s); bảng chuẩn gần nhất là 350.
        var s = DuctSizing.SuggestRound(500, maxPaPerM: 1.0);
        Assert.InRange(s.SuggestedMm, 350, 400);
        Assert.InRange(s.VelocityMs, 4.0, 5.5);
    }

    [Fact]
    public void Duct_VanTocToiDa_KhongChe()
    {
        var s = DuctSizing.SuggestRound(2000, maxPaPerM: 100, maxVelocityMs: 6.0);
        Assert.True(s.VelocityMs <= 6.0);
    }

    [Fact]
    public void Duct_ChuNhat_DeKhongNhoHonTron()
    {
        var round = DuctSizing.SuggestRound(800);
        var rect = DuctSizing.SuggestRectangularWidth(800, fixedHeightMm: 300);
        Assert.True(rect.SuggestedMm > 0);
        Assert.True(DuctSizing.EquivalentDiameterMm(rect.SuggestedMm, 300) >= round.SuggestedMm);
        Assert.True(rect.SuggestedMm / 300 <= 4.0);
    }

    [Fact]
    public void Duct_DuongKinhTuongDuong_Huebscher()
    {
        // 400×200 → De ≈ 305 mm (bảng ASHRAE: 305).
        Assert.InRange(DuctSizing.EquivalentDiameterMm(400, 200), 300, 310);
    }

    [Fact]
    public void Duct_LuuLuongAm_NemLoi()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => DuctSizing.SuggestRound(0));
    }

    [Fact]
    public void Pipe_2Lps_VMax2_ChonDN40()
    {
        // 2 L/s trong DN32 (Ø35.1) → 2.07 m/s > 2; DN40 (Ø40.9) → 1.52 m/s.
        var s = PipeSizing.SuggestDn(2.0, maxVelocityMs: 2.0);
        Assert.Equal(40, s.SuggestedMm);
        Assert.InRange(s.VelocityMs, 1.4, 1.6);
    }

    [Fact]
    public void Pipe_DuoiVMin_CoCanhBao()
    {
        var s = PipeSizing.SuggestDn(0.05, maxVelocityMs: 2.0, minVelocityMs: 0.5);
        Assert.Contains("v_min", s.Reason);
    }

    [Fact]
    public void Pipe_VuotBang_BaoTachTuyen()
    {
        var s = PipeSizing.SuggestDn(500, maxVelocityMs: 1.0);
        Assert.Equal(300, s.SuggestedMm);
        Assert.Contains("tách tuyến", s.Reason);
    }
}

public class SystemNamingTests
{
    [Theory]
    [InlineData("#0070C0", 0, 112, 192)]
    [InlineData("0070C0", 0, 112, 192)]
    [InlineData("#FFF", 255, 255, 255)]
    public void HexHopLe(string hex, int r, int g, int b)
    {
        Assert.True(SystemNaming.TryParseHex(hex, out var c));
        Assert.Equal((r, g, b), (c.R, c.G, c.B));
    }

    [Theory]
    [InlineData("")]
    [InlineData("#12")]
    [InlineData("xanh")]
    [InlineData("#GGGGGG")]
    public void HexSai_TraFalse(string hex)
    {
        Assert.False(SystemNaming.TryParseHex(hex, out _));
    }

    [Fact]
    public void TenHe_BoPhanRong_DemSo()
    {
        Assert.Equal("MEC-SA-Z1-003", SystemNaming.Build("MEC", "SA", "Z1", 3, padWidth: 3));
        Assert.Equal("SA-07", SystemNaming.Build(null, "SA", "", 7));
    }

    [Fact]
    public void VietTat_UuTienNguoiDung_RoiMacDinh_RoiChuCaiDau()
    {
        Assert.Equal("SA", SystemNaming.Abbreviate("Supply Air"));
        Assert.Equal("CAP", SystemNaming.Abbreviate("Supply Air", new Dictionary<string, string> { ["Supply Air"] = "CAP" }));
        Assert.Equal("KGN", SystemNaming.Abbreviate("Khí Gas Nén"));
        Assert.Equal("SYS", SystemNaming.Abbreviate(""));
    }
}

public class FlowNumberingTests
{
    private static Tuple<string, string> E(string a, string b) => Tuple.Create(a, b);

    [Fact]
    public void TrucChinh_DanhSoLienTuc()
    {
        var labels = FlowNumbering.Assign(new[] { E("S", "a"), E("a", "b"), E("b", "c") }, "S");
        Assert.Equal(new[] { "1", "2", "3" }, labels.Select(l => l.Label));
        Assert.Equal(new[] { "a", "b", "c" }, labels.Select(l => l.Key));
    }

    [Fact]
    public void Nhanh_DanhSoPhanCap()
    {
        // S-a-b-c, nhánh tại a: a-x-y
        var labels = FlowNumbering.Assign(new[] { E("S", "a"), E("a", "b"), E("b", "c"), E("a", "x"), E("x", "y") }, "S", tieBreaker: StringComparer.Ordinal);
        var map = labels.ToDictionary(l => l.Key, l => l.Label);

        Assert.Equal("1", map["a"]);
        Assert.Equal("2", map["b"]);
        Assert.Equal("3", map["c"]);
        Assert.Equal("1.1", map["x"]);
        Assert.Equal("1.2", map["y"]);
    }

    [Fact]
    public void NhanhCuaNhanh_BaTang()
    {
        var labels = FlowNumbering.Assign(new[] { E("S", "a"), E("a", "b"), E("a", "x"), E("x", "y"), E("x", "p") }, "S", tieBreaker: StringComparer.Ordinal);
        var map = labels.ToDictionary(l => l.Key, l => l.Label);
        Assert.Equal("1.1", map["x"]);
        Assert.Equal("1.2", map["p"]);   // p < y theo Ordinal → tiếp trục nhánh
        Assert.Equal("1.2.1", map["y"]); // y thành nhánh con
    }

    [Fact]
    public void TienTo_DemSo_ThuTuDoSau()
    {
        var labels = FlowNumbering.Assign(new[] { E("S", "a"), E("a", "b") }, "S", prefix: "D-", padWidth: 2);
        Assert.Equal("D-01", labels[0].Label);
        Assert.Equal(1, labels[0].Depth);
        Assert.Equal(2, labels[1].Depth);
    }

    [Fact]
    public void ChuTrinh_KhongLapVoHan_MoiPhanTuMotNhan()
    {
        var labels = FlowNumbering.Assign(new[] { E("S", "a"), E("a", "b"), E("b", "S") }, "S");
        Assert.Equal(2, labels.Count);
        Assert.Equal(labels.Count, labels.Select(l => l.Key).Distinct().Count());
    }

    [Fact]
    public void NguonKhongTrongDoThi_NemLoi()
    {
        Assert.Throws<ArgumentException>(() => FlowNumbering.Assign(new[] { E("a", "b") }, "S"));
    }

    [Fact]
    public void Bfs_VaDfs_CungTapNhan_KhacThuTu()
    {
        var edges = new[] { E("S", "a"), E("a", "b"), E("a", "x"), E("x", "y") };
        var dfs = FlowNumbering.Assign(edges, "S", depthFirst: true, tieBreaker: StringComparer.Ordinal);
        var bfs = FlowNumbering.Assign(edges, "S", depthFirst: false, tieBreaker: StringComparer.Ordinal);
        Assert.Equal(dfs.ToDictionary(l => l.Key, l => l.Label), bfs.ToDictionary(l => l.Key, l => l.Label));
    }
}

public class PathFinder3DTests
{
    private static readonly Box3 Bounds = new(0, 0, 0, 10000, 10000, 3000);

    [Fact]
    public void KhongChuongNgai_DiThangMotLan()
    {
        var r = PathFinder3D.FindPath(new Point3(0, 0, 0), new Point3(5000, 0, 0), Array.Empty<Box3>(), Bounds, new PathFinderOptions { StepMm = 500, ClearanceMm = 0 });
        Assert.True(r.Found, r.Reason);
        Assert.Equal(0, r.Turns);
        Assert.Equal(2, r.Polyline.Count);
        Assert.Equal(0, r.Polyline[0].X, 6);
        Assert.Equal(5000, r.Polyline[^1].X, 6);
    }

    [Fact]
    public void CoTuong_DiVongItReNhatCoThe()
    {
        // Tường chắn ngang giữa, chừa lối ở y > 6000.
        var wall = new Box3(4000, 0, 0, 5000, 6000, 3000);
        var r = PathFinder3D.FindPath(new Point3(0, 0, 0), new Point3(9000, 0, 0), new[] { wall }, Bounds,
            new PathFinderOptions { StepMm = 500, ClearanceMm = 0, AllowVertical = false, TurnPenalty = 5 });

        Assert.True(r.Found, r.Reason);
        Assert.Equal(2, r.Turns); // hướng xuất phát không tính phạt: lên → sang → xuống = 2 chỗ rẽ
        Assert.All(r.Polyline, p => Assert.False(wall.Contains(p.X, p.Y, p.Z)));
        Assert.Equal(0, r.Polyline[0].X, 6);
        Assert.Equal(9000, r.Polyline[^1].X, 6);
    }

    [Fact]
    public void KhoangHo_DuocTonTrong()
    {
        var wall = new Box3(4000, 0, 0, 5000, 6000, 3000);
        var r = PathFinder3D.FindPath(new Point3(0, 0, 0), new Point3(9000, 0, 0), new[] { wall }, Bounds,
            new PathFinderOptions { StepMm = 500, ClearanceMm = 500, AllowVertical = false });
        Assert.True(r.Found);
        // Đỉnh polyline cao nhất phải ≥ 6000 + 500 (đi trên tường + khoảng hở).
        Assert.True(r.Polyline.Max(p => p.Y) >= 6500);
    }

    [Fact]
    public void BiChanHoanToan_BaoKhongCoDuong()
    {
        var wall = new Box3(4000, -1, -1, 5000, 10001, 3001);
        var r = PathFinder3D.FindPath(new Point3(0, 0, 0), new Point3(9000, 0, 0), new[] { wall }, Bounds, new PathFinderOptions { StepMm = 500, ClearanceMm = 0 });
        Assert.False(r.Found);
        Assert.Contains("Không có đường", r.Reason);
    }

    [Fact]
    public void DiemTrongChuongNgai_BaoRo()
    {
        var r = PathFinder3D.FindPath(new Point3(4500, 100, 0), new Point3(9000, 0, 0), new[] { new Box3(4000, 0, 0, 5000, 6000, 3000) }, Bounds, new PathFinderOptions { StepMm = 500 });
        Assert.False(r.Found);
        Assert.Contains("chướng ngại", r.Reason);
    }

    [Fact]
    public void GioiHanOMoRong_DungSachSe()
    {
        var r = PathFinder3D.FindPath(new Point3(0, 0, 0), new Point3(9000, 9000, 0), Array.Empty<Box3>(), Bounds, new PathFinderOptions { StepMm = 100, MaxExpandedNodes = 10 });
        Assert.False(r.Found);
        Assert.Contains("giới hạn", r.Reason);
    }

    [Fact]
    public void ChoPhepDiDoc_VuotQuaChuongNgaiThap()
    {
        var low = new Box3(4000, -1, 0, 5000, 10001, 1000);
        var r = PathFinder3D.FindPath(new Point3(0, 0, 0), new Point3(9000, 0, 0), new[] { low }, Bounds, new PathFinderOptions { StepMm = 500, ClearanceMm = 0, AllowVertical = true });
        Assert.True(r.Found, r.Reason);
        Assert.Contains(r.Polyline, p => p.Z > 1000);
    }
}
