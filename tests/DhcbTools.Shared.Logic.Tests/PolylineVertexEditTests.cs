using DhcbTools.Shared.Logic.Cad;
using Xunit;

namespace DhcbTools.Shared.Logic.Tests;

/// <summary>
/// Cắt/thay đỉnh LWPOLYLINE tại chỗ cho <c>EntityEdit</c> op <c>trim</c>/<c>replacePoints</c> — thay cho việc xoá rồi vẽ
/// lại (mất handle, layer, xdata) mà phiên bóc khối lượng 2026-11-13 phải làm qua COM.
/// </summary>
public class PolylineVertexEditTests
{
    private const int Digits = 9;

    // Chữ L: (0,0) → (100,0) → (100,50), bề rộng 2.
    private static readonly List<PlineVertex> L = new()
    {
        new(0, 0, 0, 2, 2),
        new(100, 0, 0, 2, 2),
        new(100, 50, 0, 2, 2),
    };

    [Fact]
    public void CatDau_GiuPhanSauDiemFrom()
    {
        var cut = PolylineVertexEdit.Trim(L, false, new[] { 30.0, 0 }, null);

        Assert.True(cut.Ok, cut.Error);
        Assert.Equal(3, cut.Vertices.Count);
        Assert.Equal(30, cut.Vertices[0].X, Digits);
        Assert.Equal(0, cut.Vertices[0].Y, Digits);
        Assert.Equal(2, cut.Vertices[0].StartWidth, Digits);
        Assert.Equal(new PlineVertex(100, 0, 0, 2, 2), cut.Vertices[1]);
        Assert.Equal(100, cut.Vertices[2].X, Digits);
        Assert.Equal(50, cut.Vertices[2].Y, Digits);
    }

    [Fact]
    public void CatHaiDau_ChieuDiemLechLenDuong()
    {
        // from lệch 3 đơn vị khỏi đoạn đầu, to nằm giữa đoạn hai.
        var cut = PolylineVertexEdit.Trim(L, false, new[] { 20.0, 3 }, new[] { 100.0, 25 });

        Assert.True(cut.Ok, cut.Error);
        Assert.Equal(3, cut.Vertices.Count);
        Assert.Equal(20, cut.Vertices[0].X, Digits);
        Assert.Equal(0, cut.Vertices[0].Y, Digits);
        Assert.Equal(25, cut.Vertices[2].Y, Digits);
        Assert.Equal(3, cut.Start!.Value.Distance, Digits);
        Assert.Equal(0, cut.End!.Value.Distance, Digits);
    }

    [Fact]
    public void CatTrongMotDoan_BeRongNoiSuy()
    {
        var tapered = new List<PlineVertex> { new(0, 0, 0, 0, 10), new(100, 0) };

        var cut = PolylineVertexEdit.Trim(tapered, false, new[] { 25.0, 0 }, new[] { 75.0, 0 });

        Assert.True(cut.Ok, cut.Error);
        Assert.Equal(2, cut.Vertices.Count);
        Assert.Equal(2.5, cut.Vertices[0].StartWidth, Digits);
        Assert.Equal(7.5, cut.Vertices[0].EndWidth, Digits);
        Assert.Equal(75, cut.Vertices[1].X, Digits);
    }

    [Fact]
    public void FromDungDinh_KhongSinhDoanDaiKhong()
    {
        var cut = PolylineVertexEdit.Trim(L, false, new[] { 100.0, 0 }, null);

        Assert.True(cut.Ok, cut.Error);
        Assert.Equal(2, cut.Vertices.Count);
        Assert.Equal(100, cut.Vertices[0].X, Digits);
        Assert.Equal(0, cut.Vertices[0].Y, Digits);
        Assert.Equal(50, cut.Vertices[1].Y, Digits);
    }

    [Fact]
    public void ToDungDinh_DinhCuoiLaDinhDo()
    {
        var cut = PolylineVertexEdit.Trim(L, false, null, new[] { 100.0, 0 });

        Assert.True(cut.Ok, cut.Error);
        Assert.Equal(2, cut.Vertices.Count);
        Assert.Equal(new PlineVertex(0, 0, 0, 2, 2), cut.Vertices[0]);
        Assert.Equal(100, cut.Vertices[1].X, Digits);
        Assert.Equal(0, cut.Vertices[1].Y, Digits);
    }

    [Fact]
    public void DoanCung_ChiaDungBulge()
    {
        // Nửa đường tròn bán kính 50 từ (0,0) tới (100,0), ngược chiều kim đồng hồ (bulge 1): đi qua (50,-50).
        var arc = new List<PlineVertex> { new(0, 0, 1), new(100, 0) };

        var cut = PolylineVertexEdit.Trim(arc, false, new[] { 50.0, -60 }, null);

        Assert.True(cut.Ok, cut.Error);
        Assert.Equal(2, cut.Vertices.Count);
        Assert.Equal(50, cut.Vertices[0].X, Digits);
        Assert.Equal(-50, cut.Vertices[0].Y, Digits);
        Assert.Equal(0.5, cut.Start!.Value.Fraction, Digits);
        Assert.Equal(10, cut.Start.Value.Distance, Digits);
        // Một phần tư đường tròn: bulge = tan(90°/4).
        Assert.Equal(Math.Tan(Math.PI / 8), cut.Vertices[0].Bulge, Digits);
        Assert.Equal(100, cut.Vertices[1].X, Digits);
    }

    [Fact]
    public void DiemCungNgoaiTamQuet_VeDauMutGanHon()
    {
        var arc = new List<PlineVertex> { new(0, 0, 1), new(100, 0) };

        // (90, 60) nằm ở nửa trên — ngoài tầm quét của cung (cung đi qua nửa dưới), gần đầu (100, 0) hơn.
        var location = PolylineVertexEdit.Project(arc, new[] { 90.0, 60 });

        Assert.Equal(1, location.Fraction, Digits);
        Assert.Equal(100, location.X, Digits);
    }

    [Theory]
    [InlineData(new[] { 50.0, 0 }, new[] { 20.0, 0 }, "trước to")]
    [InlineData(new[] { 0.0, 0 }, new[] { 100.0, 50 }, "hai đầu")]
    [InlineData(null, new[] { 100.0, 50 }, "hai đầu")]
    public void CatSai_BaoLoiDocDuoc(double[]? from, double[]? to, string expected)
    {
        var cut = PolylineVertexEdit.Trim(L, false, from, to);

        Assert.False(cut.Ok);
        Assert.Contains(expected, cut.Error);
        Assert.Empty(cut.Vertices);
    }

    [Fact]
    public void PolylineKin_HoacThieuDinh_TuChoi()
    {
        Assert.Contains("khép kín", PolylineVertexEdit.Trim(L, true, new[] { 30.0, 0 }, null).Error);
        Assert.Contains("2 đỉnh", PolylineVertexEdit.Trim(new List<PlineVertex> { new(0, 0) }, false, new[] { 0.0, 0 }, null).Error);
        Assert.Contains("from và/hoặc to", PolylineVertexEdit.Trim(L, false, null, null).Error);
    }

    [Fact]
    public void ThayDinh_VaBeRongDeu()
    {
        var points = PolylineVertexEdit.FromPoints(new[] { new[] { 0.0, 0 }, new[] { 5.0, 5, 9 } }, 1.5);

        Assert.Equal(new PlineVertex(0, 0, 0, 1.5, 1.5), points[0]);
        Assert.Equal(new PlineVertex(5, 5, 0, 1.5, 1.5), points[1]);
        Assert.Equal(2, PolylineVertexEdit.UniformWidth(L, false));
        Assert.Null(PolylineVertexEdit.UniformWidth(new List<PlineVertex> { new(0, 0, 0, 0, 10), new(1, 0) }, false));
        // Đỉnh cuối của polyline hở không mang đoạn nào — bề rộng của nó không tính.
        Assert.Equal(2, PolylineVertexEdit.UniformWidth(new List<PlineVertex> { new(0, 0, 0, 2, 2), new(1, 0, 0, 7, 7) }, false));
    }

    [Fact]
    public void ToDungDinhGiua_KhongSinhDoanDaiKhong()
    {
        // to rơi đúng đỉnh giữa: hoà giữa đoạn 0 (f = 1) và đoạn 1 (f = 0) — phải chọn đoạn 0, không thêm đỉnh trùng.
        var location = PolylineVertexEdit.Project(L, new[] { 100.0, 0 });
        Assert.Equal(0, location.Segment);
        Assert.Equal(1, location.Fraction, Digits);

        var cut = PolylineVertexEdit.Trim(L, false, new[] { 50.0, 0 }, new[] { 100.0, 0 });

        Assert.True(cut.Ok, cut.Error);
        Assert.Equal(2, cut.Vertices.Count);
        Assert.Equal(100, cut.Vertices[1].X, Digits);
    }

    [Fact]
    public void ViTriChieu_CoToaDoVaThamSo()
    {
        var location = PolylineVertexEdit.Project(L, new[] { 103.0, 20 });

        Assert.Equal(1, location.Segment);
        Assert.Equal(100, location.X, Digits);
        Assert.Equal(20, location.Y, Digits);
        Assert.Equal(1.4, location.Parameter, Digits);
        Assert.Equal(3, location.Distance, Digits);
    }

    [Fact]
    public void Dinh_SoSanhBamVaChuoi()
    {
        var a = new PlineVertex(1, 2, 0.5, 3, 4);
        object same = new PlineVertex(1, 2, 0.5, 3, 4);

        Assert.True(a.Equals(same));
        Assert.False(a.Equals((object)new PlineVertex(1, 2)));
        Assert.False(a.Equals("không phải đỉnh"));
        Assert.Equal(a.GetHashCode(), same.GetHashCode());
        Assert.Contains("b=0.5", a.ToString());
        Assert.Contains("w=3/4", a.ToString());
    }

    [Fact]
    public void BeRongDeu_DuoiHaiDinh_LaKhong()
    {
        Assert.Equal(0, PolylineVertexEdit.UniformWidth(new List<PlineVertex> { new(0, 0, 0, 5, 5) }, false));
        Assert.Equal(0, PolylineVertexEdit.UniformWidth(new List<PlineVertex>(), true));
    }

    [Fact]
    public void SubBulge_ThangVaNuaCung()
    {
        Assert.Equal(0, PolylineVertexEdit.SubBulge(0, 0.3));
        Assert.Equal(1, PolylineVertexEdit.SubBulge(1, 1), Digits);
        Assert.Equal(-Math.Tan(Math.PI / 8), PolylineVertexEdit.SubBulge(-1, 0.5), Digits);
    }
}
