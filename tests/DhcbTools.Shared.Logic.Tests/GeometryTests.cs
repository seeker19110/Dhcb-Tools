using DhcbTools.Shared.Logic.Geometry;
using Xunit;

namespace DhcbTools.Shared.Logic.Tests;

public class GridClusteringTests
{
    [Fact]
    public void GomDoanThangHang_ThanhMotTruc()
    {
        // Ba đoạn của cùng trục dọc X≈5000 (lệch 10 mm), một đoạn ngang Y=0.
        var segs = new[]
        {
            new Segment2D(5000, 0, 5000, 10000),
            new Segment2D(5010, 12000, 5010, 20000),
            new Segment2D(4995, -3000, 4995, -1000),
            new Segment2D(0, 0, 30000, 0),
        };

        var grids = GridClustering.Cluster(segs, positionTolerance: 50);

        var v = Assert.Single(grids, g => g.IsVertical);
        Assert.Equal(3, v.SegmentCount);
        Assert.InRange(v.Position, 4990, 5010);
        Assert.Equal(-3000, v.Start);
        Assert.Equal(20000, v.End);

        var h = Assert.Single(grids, g => !g.IsVertical);
        Assert.Equal(0, h.Position);
    }

    [Fact]
    public void HaiTrucCachXaHonDungSai_KhongGom()
    {
        var segs = new[] { new Segment2D(0, 0, 0, 5000), new Segment2D(100, 0, 100, 5000) };
        Assert.Equal(2, GridClustering.Cluster(segs, positionTolerance: 50).Count);
    }

    [Fact]
    public void DoanNgan_VaDoanXien_BiBoQua()
    {
        var segs = new[]
        {
            new Segment2D(0, 0, 0, 100),        // bubble/gạch ngắn
            new Segment2D(0, 0, 5000, 5000),    // xiên 45°
            new Segment2D(0, 0, 0, 8000),
        };
        Assert.Single(GridClustering.Cluster(segs));
    }

    [Fact]
    public void DoanVeNguocChieu_VanLaTrucDoc()
    {
        var segs = new[] { new Segment2D(0, 8000, 0, 0) };
        Assert.True(GridClustering.Cluster(segs)[0].IsVertical);
    }

    [Fact]
    public void DungSaiAm_NemLoi()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => GridClustering.Cluster(Array.Empty<Segment2D>(), positionTolerance: -1));
    }
}

public class GridNamingTests
{
    [Theory]
    [InlineData(0, "A")]
    [InlineData(7, "H")]
    [InlineData(8, "J")]   // bỏ I
    [InlineData(13, "P")]  // bỏ O
    [InlineData(24, "AA")]
    public void ChuBoIO(int index, string expected)
    {
        Assert.Equal(expected, GridNaming.Letter(index));
    }

    [Fact]
    public void KhongBoIO()
    {
        Assert.Equal("I", GridNaming.Letter(8, skipIO: false));
        Assert.Equal("AA", GridNaming.Letter(26, skipIO: false));
    }

    [Fact]
    public void DatTen_DocChu_NgangSo_TheoViTri()
    {
        var grids = new List<GridLine>
        {
            new(true, 6000, 0, 10, 1), new(true, 0, 0, 10, 1), new(true, 3000, 0, 10, 1),
            new(false, 4000, 0, 10, 1), new(false, 0, 0, 10, 1),
        };

        GridNaming.Apply(grids);

        Assert.Equal("A", grids.Single(g => g.IsVertical && g.Position == 0).Name);
        Assert.Equal("B", grids.Single(g => g.IsVertical && g.Position == 3000).Name);
        Assert.Equal("C", grids.Single(g => g.IsVertical && g.Position == 6000).Name);
        Assert.Equal("1", grids.Single(g => !g.IsVertical && g.Position == 0).Name);
        Assert.Equal("2", grids.Single(g => !g.IsVertical && g.Position == 4000).Name);
    }

    [Fact]
    public void DaoQuyTac_DocSo_NgangChu_PhaiSangTrai()
    {
        var grids = new List<GridLine> { new(true, 0, 0, 1, 1), new(true, 100, 0, 1, 1), new(false, 0, 0, 1, 1) };
        GridNaming.Apply(grids, new GridNamingRule { VerticalUsesLetters = false, VerticalLeftToRight = false, Prefix = "G" });

        Assert.Equal("G1", grids.Single(g => g.IsVertical && g.Position == 100).Name);
        Assert.Equal("G2", grids.Single(g => g.IsVertical && g.Position == 0).Name);
        Assert.Equal("GA", grids.Single(g => !g.IsVertical).Name);
    }

    [Fact]
    public void Csv_RoundTrip()
    {
        var grids = new List<GridLine> { new(true, 1234.5, -1000, 9000, 1) { Name = "A" }, new(false, 500, 0, 20000, 1) { Name = "1" } };
        var csv = GridNaming.ToCsv(grids);
        Assert.StartsWith("Name,X1,Y1,X2,Y2\n", csv);
        Assert.Contains("A,1234.5,-1000.0,1234.5,9000.0", csv);

        var errors = new List<string>();
        var back = GridNaming.FromCsv(csv, errors);
        Assert.Empty(errors);
        Assert.Equal(2, back.Count);
        Assert.True(back[0].IsVertical);
        Assert.Equal(1234.5, back[0].Position, 6);
        Assert.False(back[1].IsVertical);
    }

    [Fact]
    public void Csv_DongLoi_GhiVaoErrors()
    {
        var errors = new List<string>();
        var back = GridNaming.FromCsv("Name,X1,Y1,X2,Y2\nA,abc,0,0,1\nB,0,0,0,5000\n", errors);
        Assert.Single(back);
        Assert.Single(errors);
    }

    [Fact]
    public void Csv_DocDuocSoDauPhay()
    {
        var errors = new List<string>();
        var back = GridNaming.FromCsv("Name,X1,Y1,X2,Y2\nA,\"1234,5\",0,\"1234,5\",5000\n", errors);
        Assert.Empty(errors);
        Assert.Equal(1234.5, back[0].Position, 6);
    }
}
