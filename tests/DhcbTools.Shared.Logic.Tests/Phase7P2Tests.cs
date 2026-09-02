using DhcbTools.Shared.Logic.Mep;
using Xunit;

namespace DhcbTools.Shared.Logic.Tests;

public class SlopeMathTests
{
    [Theory]
    [InlineData(50, 2.0)]
    [InlineData(75, 2.0)]
    [InlineData(100, 1.0)]
    [InlineData(150, 1.0)]
    [InlineData(200, 0.5)]
    public void DocToiThieuTheoDuongKinh(double dn, double expected)
    {
        Assert.Equal(expected, SlopeMath.MinSlopePercent(dn));
    }

    [Fact]
    public void DoHa_VaDocThucTe()
    {
        Assert.Equal(60, SlopeMath.DropMm(6000, 1.0), 9);
        Assert.Equal(1.0, SlopeMath.SlopePercent(6000, 60), 9);
        Assert.Equal(0, SlopeMath.SlopePercent(0, 10));
    }

    [Fact]
    public void KiemTraDoc_DatKhongDatVaNguoc()
    {
        Assert.Null(SlopeMath.CheckSlope(6000, 60, 1.0));
        Assert.Null(SlopeMath.CheckSlope(6000, 58, 1.0)); // trong dung sai 0,05 %
        Assert.Contains("<", SlopeMath.CheckSlope(6000, 30, 1.0));
        Assert.Contains("ngược", SlopeMath.CheckSlope(6000, -30, 1.0));
    }

    [Fact]
    public void Kick45_Va90()
    {
        var k45 = SlopeMath.Kick(300, 45);
        Assert.Equal(300 * Math.Sqrt(2), k45.DiagonalMm, 6);
        Assert.Equal(300, k45.AlongAxisMm, 6);

        var k90 = SlopeMath.Kick(300, 90);
        Assert.Equal(300, k90.DiagonalMm, 6);
        Assert.Equal(0, k90.AlongAxisMm, 6);
    }

    [Fact]
    public void ChieuDaiToiThieuChoKick_TangTheoDuongKinh()
    {
        Assert.True(SlopeMath.MinPipeLengthForKick(300, 100) < SlopeMath.MinPipeLengthForKick(300, 200));
        Assert.Equal(300 + 2 * 1.5 * 100 + 200, SlopeMath.MinPipeLengthForKick(300, 100), 6);
    }

    [Fact]
    public void CaoDoDocTuyen()
    {
        var z = SlopeMath.ElevationsAlong(3000, new[] { 1000.0, 2000.0 }, 2.0);
        Assert.Equal(new[] { 3000.0, 2980.0, 2940.0 }, z);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ThamSoSai_NemLoi(double v)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SlopeMath.MinSlopePercent(v));
        Assert.Throws<ArgumentOutOfRangeException>(() => SlopeMath.Kick(v));
    }
}

public class BomAggregatorTests
{
    [Fact]
    public void GomTheoSpoolHeTypeSize_TongChieuDai()
    {
        var items = new[]
        {
            new BomItem("CW-1", "Pipes", "Steel", "DN50", 3000, "1", "SP-01"),
            new BomItem("CW-1", "Pipes", "Steel", "DN50", 4500, "2", "SP-01"),
            new BomItem("CW-1", "Pipe Fittings", "Elbow", "DN50", null, "3", "SP-01"),
            new BomItem("CW-1", "Pipes", "Steel", "DN50", 1000, "4", "SP-02"),
        };
        var rows = BomAggregator.Aggregate(items);

        Assert.Equal(3, rows.Count);
        var pipes01 = rows.Single(r => r.Spool == "SP-01" && r.Category == "Pipes");
        Assert.Equal(2, pipes01.Count);
        Assert.Equal(7500, pipes01.TotalLengthMm);
        Assert.Equal(2, pipes01.StockPieces(6000, 5)); // 7875 / 6000 → 2 cây
    }

    [Fact]
    public void Csv_CoHang_VaSoCay()
    {
        var rows = BomAggregator.Aggregate(new[] { new BomItem("S", "Pipes", "T", "DN100", 12500) });
        var csv = BomAggregator.ToCsv(rows, 6000, 0);
        Assert.StartsWith("Spool,System,Category,Type,Size,Count,TotalLengthM,StockPieces", csv);
        Assert.Contains(",1,12.50,3", csv);
    }

    [Fact]
    public void TongTheoHe()
    {
        var rows = BomAggregator.Aggregate(new[] { new BomItem("A", "Pipes", "T", "DN50", 1000), new BomItem("A", "Pipe Fittings", "E", "DN50", null), new BomItem("B", "Pipes", "T", "DN50", 500) });
        var t = BomAggregator.TotalsBySystem(rows);
        Assert.Equal((2, 1000.0), t["A"]);
        Assert.Equal((1, 500.0), t["B"]);
    }
}

public class PolylineSimplifierTests
{
    [Fact]
    public void BoDiemThangHang_VaTrung()
    {
        var pts = new[] { new Point3(0, 0, 0), new Point3(100, 0, 0), new Point3(200, 0, 0), new Point3(200, 0, 0), new Point3(200, 100, 0), new Point3(200, 200, 0), new Point3(200, 200, 100) };
        var s = PolylineSimplifier.Simplify(pts);
        Assert.Equal(4, s.Count);
        Assert.Equal(new Point3(200, 0, 0).X, s[1].X);
        Assert.Equal(3, PolylineSimplifier.ToSegments(pts).Count);
    }

    [Fact]
    public void KhongGopDoanQuayDau()
    {
        var pts = new[] { new Point3(0, 0, 0), new Point3(100, 0, 0), new Point3(50, 0, 0) };
        Assert.Equal(3, PolylineSimplifier.Simplify(pts).Count);
    }

    [Fact]
    public void ChieuDai_VaRong()
    {
        Assert.Equal(300, PolylineSimplifier.Length(new[] { new Point3(0, 0, 0), new Point3(100, 0, 0), new Point3(100, 200, 0) }), 9);
        Assert.Empty(PolylineSimplifier.Simplify(Array.Empty<Point3>()));
    }
}
