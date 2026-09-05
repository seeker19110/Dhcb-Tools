using System;
using System.Collections.Generic;
using System.Linq;
using DhcbTools.Shared.Logic.Mep;
using Xunit;

namespace DhcbTools.Shared.Logic.Tests;

/// <summary>
/// Routing mức D: tường/sàn có lỗ mở phải cho tuyến đi lọt qua lỗ, và CHỈ qua lỗ. Kiểm ở tầng hộp
/// (phép trừ đúng về thể tích) rồi ở tầng tìm đường (A* thật sự chui qua lỗ và giữ khoảng hở với mép lỗ).
/// </summary>
public class BoxSubtractTests
{
    private static double Volume(Box3 b) => (b.MaxX - b.MinX) * (b.MaxY - b.MinY) * (b.MaxZ - b.MinZ);

    private static bool Inside(Box3 b, double x, double y, double z)
        => x > b.MinX && x < b.MaxX && y > b.MinY && y < b.MaxY && z > b.MinZ && z < b.MaxZ;

    [Fact]
    public void HopNull_NemLoiRoRang()
    {
        Assert.Throws<ArgumentNullException>(() => BoxSubtract.Minus(null!, Array.Empty<Box3>()));
    }

    [Fact]
    public void KhongCoLo_TraVeChinhHop()
    {
        var box = new Box3(0, 0, 0, 1000, 200, 3000);
        var pieces = BoxSubtract.Minus(box, Array.Empty<Box3>());
        Assert.Single(pieces);
        Assert.Same(box, pieces[0]);
    }

    [Fact]
    public void LoKhongGiao_BoQua()
    {
        var box = new Box3(0, 0, 0, 1000, 200, 3000);
        var pieces = BoxSubtract.Minus(box, new[] { new Box3(5000, 0, 0, 6000, 200, 3000) });
        Assert.Single(pieces);
    }

    [Fact]
    public void LoNuotTronHop_TraVeRong()
    {
        var box = new Box3(0, 0, 0, 1000, 200, 3000);
        var pieces = BoxSubtract.Minus(box, new[] { new Box3(-1, -1, -1, 1001, 201, 3001) });
        Assert.Empty(pieces);
    }

    /// <summary>Lỗ xuyên tường (đúng chiều dày) chỉ để lại bốn mảnh quanh lỗ, thể tích khớp chính xác.</summary>
    [Fact]
    public void LoXuyenTuong_BonManhQuanhLo_TheTichKhop()
    {
        var wall = new Box3(0, 0, 0, 5000, 200, 3000);
        var hole = new Box3(2000, -50, 2400, 2600, 250, 2900); // 600 × 500, nhô ra hai mặt tường
        var pieces = BoxSubtract.Minus(wall, new[] { hole });

        Assert.Equal(4, pieces.Count);
        var expected = Volume(wall) - 600.0 * 200 * 500;
        Assert.Equal(expected, pieces.Sum(Volume), 6);
        Assert.All(pieces, p => Assert.False(BoxSubtract.Overlaps(p, hole)));
    }

    /// <summary>
    /// Vét cạn: với hộp và lỗ ngẫu nhiên (kể cả nhiều lỗ chồng nhau), mọi điểm thử phải "trong hộp và ngoài
    /// mọi lỗ" ⇔ "trong đúng một mảnh". Mảnh không được chồng nhau — nếu chồng, raster hoá vẫn đúng nhưng
    /// tốn gấp đôi; nếu hụt, tuyến xuyên tường.
    /// </summary>
    [Fact]
    public void VetCan_ManhPhuDungPhanConLai_KhongChong()
    {
        var rand = new Random(20260905);
        for (var lan = 0; lan < 200; lan++)
        {
            var box = RandomBox(rand, 0, 1000);
            var holes = new List<Box3>();
            var n = rand.Next(0, 4);
            for (var i = 0; i < n; i++)
            {
                holes.Add(RandomBox(rand, -200, 1200));
            }

            var pieces = BoxSubtract.Minus(box, holes);

            for (var t = 0; t < 300; t++)
            {
                double x = rand.Next(-100, 1100) + 0.5, y = rand.Next(-100, 1100) + 0.5, z = rand.Next(-100, 1100) + 0.5;
                var shouldBeSolid = Inside(box, x, y, z) && !holes.Any(h => Inside(h, x, y, z));
                var count = pieces.Count(p => Inside(p, x, y, z));
                Assert.True(count == (shouldBeSolid ? 1 : 0),
                    $"lần {lan} điểm ({x},{y},{z}): mong {(shouldBeSolid ? 1 : 0)} mảnh, có {count}");
            }
        }
    }

    /// <summary>
    /// Tầng tìm đường: bức tường kín thì hai điểm hai bên KHÔNG nối thông; đục một lỗ 600 × 600 thì
    /// tuyến chui qua đúng lỗ (điểm giữa tuyến nằm trong lỗ), và với khoảng hở 100 thì lỗ 250 × 250
    /// (nhỏ hơn 2 × clearance + 1 ô) vẫn KHÔNG đi lọt.
    /// </summary>
    [Fact]
    public void AStar_ChuiQuaLoTuong_VaKhongLotLoQuaNho()
    {
        var bounds = new Box3(0, 0, 0, 6000, 3000, 3000);
        var wall = new Box3(2900, 0, 0, 3100, 3000, 3000);
        var start = new Point3(500, 1500, 1500);
        var goal = new Point3(5500, 1500, 1500);
        var opt = new PathFinderOptions { StepMm = 100, ClearanceMm = 100, TurnPenalty = 5 };

        var kin = PathFinder3D.FindPath(start, goal, new[] { wall }, bounds, opt);
        Assert.False(kin.Found);
        Assert.False(kin.GoalConnected);

        var lo = new Box3(2800, 1200, 2000, 3200, 1800, 2600);
        var pieces = BoxSubtract.Minus(wall, new[] { lo });
        var qua = PathFinder3D.FindPath(start, goal, pieces, bounds, opt);
        Assert.True(qua.Found, qua.Reason);

        // Đoạn tuyến cắt mặt phẳng x = 3000 phải nằm trong lỗ (đã trừ khoảng hở).
        var crossing = Segments(qua.Polyline).Where(s => s.a.X <= 3000 && s.b.X >= 3000).ToList();
        Assert.NotEmpty(crossing);
        foreach (var (a, _) in crossing)
        {
            Assert.InRange(a.Y, 1300, 1700);
            Assert.InRange(a.Z, 2100, 2500);
        }

        var loNho = new Box3(2800, 1400, 2200, 3200, 1650, 2450); // 250 × 250 < 2 × 100 + 100
        var khongLot = PathFinder3D.FindPath(start, goal, BoxSubtract.Minus(wall, new[] { loNho }), bounds, opt);
        Assert.False(khongLot.Found);
    }

    private static Box3 RandomBox(Random rand, int lo, int hi)
    {
        int a = rand.Next(lo, hi), b = rand.Next(lo, hi), c = rand.Next(lo, hi), d = rand.Next(lo, hi), e = rand.Next(lo, hi), f = rand.Next(lo, hi);
        return new Box3(a, c, e, b, d, f);
    }

    private static IEnumerable<(Point3 a, Point3 b)> Segments(IReadOnlyList<Point3> pts)
    {
        for (var i = 1; i < pts.Count; i++)
        {
            yield return (pts[i - 1], pts[i]);
        }
    }
}
