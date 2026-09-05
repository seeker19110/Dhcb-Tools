using DhcbTools.Shared.Logic.Geometry;
using DhcbTools.Shared.Logic.Mep;
using Xunit;

namespace DhcbTools.Shared.Logic.Tests;

/// <summary>
/// Nhánh phòng vệ và nhánh hiếm của tầng MEP: đối số vô lý phải bị chặn ngay tại cửa thay vì
/// đi tiếp thành NaN/Infinity trong kết quả tính toán mà kỹ sư không nhận ra.
/// </summary>
public class MepGapTests
{
    [Fact]
    public void FrictionPaPerM_DuongKinhKhongDuong_NemOutOfRange()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => DuctSizing.FrictionPaPerM(0.1, 0));
    }

    /// <summary>Re &lt; 2300 (chảy tầng): dùng 64/Re chứ không phải Swamee–Jain.</summary>
    [Fact]
    public void FrictionPaPerM_ChayTang_DungCongThucLaminar()
    {
        var pa = DuctSizing.FrictionPaPerM(1e-6, 1.0);

        Assert.True(pa > 0, "Tổn thất phải dương.");
        Assert.True(double.IsFinite(pa), "Tổn thất phải là số hữu hạn.");
    }

    [Fact]
    public void EquivalentDiameterMm_CanhKhongDuong_NemOutOfRange()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => DuctSizing.EquivalentDiameterMm(0, 300));
        Assert.Throws<ArgumentOutOfRangeException>(() => DuctSizing.EquivalentDiameterMm(300, -1));
    }

    [Fact]
    public void SuggestRectangularWidth_ChieuCaoKhongDuong_NemOutOfRange()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => DuctSizing.SuggestRectangularWidth(500, 0));
    }

    [Fact]
    public void PipeSizing_VelocityMs_DuongKinhKhongDuong_NemOutOfRange()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PipeSizing.VelocityMs(1.0, 0));
    }

    [Fact]
    public void PipeSizing_SuggestDn_ThamSoVoLy_NemOutOfRange()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PipeSizing.SuggestDn(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => PipeSizing.SuggestDn(1.0, maxVelocityMs: 0));
    }

    [Fact]
    public void RouteGraph_SegmentsNull_NemArgumentNull()
    {
        Assert.Throws<ArgumentNullException>(() => RouteGraph<string>.Build(null!, 10));
    }

    [Fact]
    public void RouteGraph_DungSaiAm_NemOutOfRange()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => RouteGraph<string>.Build(Array.Empty<RouteSegment<string>>(), -1));
    }

    /// <summary>
    /// Đoạn dài hơn dung sai (16 &gt; 10) nhưng cả hai đầu đều gộp về một nút đã có sẵn: phải bị loại
    /// kèm cảnh báo, chứ không thành cạnh tự nối vào chính nó.
    /// </summary>
    [Fact]
    public void RouteGraph_HaiDauGopVeMotNutDaCo_LoaiKemCanhBao()
    {
        var segments = new[]
        {
            new RouteSegment<string>("S1", new Point3(0, 0, 0), new Point3(1000, 0, 0)),
            new RouteSegment<string>("S2", new Point3(-8, 0, 0), new Point3(8, 0, 0)),
        };

        var graph = RouteGraph<string>.Build(segments, tolerance: 10);

        Assert.Single(graph.Edges);
        Assert.Single(graph.Rejected);
        Assert.Contains("ngắn hơn dung sai", Assert.Single(graph.Warnings));
    }

    /// <summary>Đỉnh không phải bậc 2 thì không có góc đổi hướng — NaN, không phải 0.</summary>
    [Fact]
    public void RouteGraph_AngleAt_DinhKhongPhaiBac2_TraNaN()
    {
        var graph = RouteGraph<string>.Build(new[]
        {
            new RouteSegment<string>("S1", new Point3(0, 0, 0), new Point3(1000, 0, 0)),
        }, tolerance: 1);

        Assert.True(double.IsNaN(graph.AngleAt(0)));
    }

    /// <summary>Cạnh được thêm tay ngoài Build vẫn tra ngược được (Edges là List public).</summary>
    [Fact]
    public void RouteGraph_AngleAt_CanhThemTayNgoaiBuild_VanTraDuocGoc()
    {
        var graph = RouteGraph<string>.Build(new[]
        {
            new RouteSegment<string>("S1", new Point3(0, 0, 0), new Point3(1000, 0, 0)),
            new RouteSegment<string>("S2", new Point3(1000, 0, 0), new Point3(1000, 1000, 0)),
        }, tolerance: 1);

        Assert.Equal(90.0, graph.AngleAt(1), 6);
    }

    [Fact]
    public void PathFinder3D_ObstaclesNull_NemArgumentNull()
    {
        Assert.Throws<ArgumentNullException>(() => PathFinder3D.FindPath(
            new Point3(0, 0, 0), new Point3(100, 0, 0), null!, new Box3(0, 0, 0, 1000, 1000, 1000)));
    }

    [Fact]
    public void PathFinder3D_BuocLuoiKhongDuong_NemOutOfRange()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PathFinder3D.FindPath(
            new Point3(0, 0, 0), new Point3(100, 0, 0), Array.Empty<Box3>(),
            new Box3(0, 0, 0, 1000, 1000, 1000), new PathFinderOptions { StepMm = 0 }));
    }

    /// <summary>Điểm đích nằm ngoài hộp tìm kiếm: nói rõ lý do thay vì trả "không tìm thấy tuyến".</summary>
    [Fact]
    public void PathFinder3D_DiemNgoaiHop_NoiRoLyDo()
    {
        var result = PathFinder3D.FindPath(
            new Point3(0, 0, 0), new Point3(50_000, 0, 0), Array.Empty<Box3>(),
            new Box3(0, 0, 0, 1000, 1000, 1000), new PathFinderOptions { StepMm = 100 });

        Assert.False(result.Found);
        Assert.Contains("ngoài hộp tìm kiếm", result.Reason);
    }

    [Fact]
    public void BomItem_GiuLaiElementId()
    {
        var item = new BomItem("HVAC", "Ducts", "Rect", "300x200", 1000, elementId: "12345");

        Assert.Equal("12345", item.ElementId);
    }

    /// <summary>Đoạn ngang đi ngược chiều: atan2 ra sát 180°, phải quy về 0.</summary>
    [Fact]
    public void Segment2D_GocSat180Do_QuyVe0()
    {
        Assert.Equal(0.0, new Segment2D(0, 0, -1000, 1e-12).AngleDeg, 9);
    }

    /// <summary>Hai chuỗi khác nhau ở ký tự chữ: so sánh không phân biệt hoa thường quyết định.</summary>
    [Fact]
    public void NaturalComparer_KhacNhauOKyTuChu()
    {
        Assert.True(NaturalComparer.Instance.Compare("AB1", "AC1") < 0);
        Assert.True(NaturalComparer.Instance.Compare("AC1", "AB1") > 0);
    }
}
