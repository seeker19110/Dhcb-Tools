using DhcbTools.Shared.Logic.Ai;
using DhcbTools.Shared.Logic.Cad;
using DhcbTools.Shared.Logic.Checks;
using DhcbTools.Shared.Logic.Evidence;
using DhcbTools.Shared.Logic.Geometry;
using DhcbTools.Shared.Logic.Mep;
using Xunit;

namespace DhcbTools.Shared.Logic.Tests;

/// <summary>
/// Các nhánh còn lại của tầng logic: chuỗi băm bằng chứng, quy tắc ngưỡng, giao trục, gộp polyline
/// và bố trí thiết bị. Toàn nhánh "đầu vào hỏng" — đúng chỗ dễ bị bỏ quên nhất khi sửa code.
/// </summary>
public class LogicGapTests
{
    [Fact]
    public void HashChain_ComputeHash_PayloadNull_NemArgumentNull()
    {
        Assert.Throws<ArgumentNullException>(() => HashChain.ComputeHash(null!));
    }

    [Fact]
    public void HashChain_Seal_ThamSoNull_NemArgumentNull()
    {
        Assert.Throws<ArgumentNullException>(() => HashChain.Seal(null!, "abc"));
        Assert.Throws<ArgumentNullException>(() => HashChain.Seal("{}", null!));
    }

    [Fact]
    public void HashChain_TrySplit_DongNull_TraFalse()
    {
        Assert.False(HashChain.TrySplit(null, out _, out _));
    }

    [Fact]
    public void HashChain_Verify_ThamSoNull_NemArgumentNull()
    {
        Assert.Throws<ArgumentNullException>(() => HashChain.Verify(null!, _ => null));
        Assert.Throws<ArgumentNullException>(() => HashChain.Verify(Array.Empty<string>(), null!));
    }

    /// <summary>
    /// Dòng đúng băm nhưng không đọc lại được thành bản ghi (prevHashOf trả null): chuỗi coi như hỏng
    /// định dạng, chứ không được lặng lẽ bỏ qua rồi báo "toàn vẹn".
    /// </summary>
    [Fact]
    public void HashChain_KhongDocLaiDuocPrevHash_BaoMalformed()
    {
        var payload = "{\"a\":1}";
        var line = HashChain.Seal(payload, HashChain.ComputeHash(payload));

        var verification = HashChain.Verify(new[] { line }, _ => null);

        Assert.Equal(ChainStatus.Malformed, verification.Status);
        Assert.Equal(1, verification.ProblemLine);
        Assert.Contains("không đọc lại được", verification.Message);
    }

    [Fact]
    public void ThresholdRule_TrongNguong_TraNull()
    {
        Assert.Null(new ThresholdRule { Metric = "m", Min = 1, Max = 10 }.Check(5));
    }

    [Theory]
    [InlineData("{khong-phai-json")]
    [InlineData("\"chi-la-chuoi\"")]
    public void ThresholdRule_JsonKhongDungKieu_TraDanhSachRong(string json)
    {
        Assert.Empty(ThresholdRule.Parse(json));
    }

    [Fact]
    public void ThresholdRule_ParseMangTrucTiep_DocDuocQuyTac()
    {
        var rules = ThresholdRule.Parse("[{\"metric\":\"clash\",\"max\":0}]");

        Assert.Equal("clash", Assert.Single(rules).Metric);
    }

    [Fact]
    public void RuleChecker_GiuLaiMoTaChoBaoCao()
    {
        Assert.Equal("Mã hệ phải đúng khuôn", new ParameterRule { Description = "Mã hệ phải đúng khuôn" }.Description);
    }

    [Fact]
    public void GridIntersections_GridsNull_TraDanhSachRong()
    {
        Assert.Empty(GridIntersections.Find(null!));
    }

    /// <summary>Trục suy biến (dài 0) và hai trục song song đều không sinh giao điểm.</summary>
    [Fact]
    public void GridIntersections_TrucSuyBienVaSongSong_KhongSinhGiaoDiem()
    {
        var suyBien = new[]
        {
            new NamedSegment2D("A", new Segment2D(0, 0, 0, 0)),
            new NamedSegment2D("1", new Segment2D(-1000, 0, 1000, 0)),
        };
        var songSong = new[]
        {
            new NamedSegment2D("A", new Segment2D(0, 0, 0, 1000)),
            new NamedSegment2D("B", new Segment2D(500, 0, 500, 1000)),
        };

        Assert.Empty(GridIntersections.Find(suyBien));
        Assert.Empty(GridIntersections.Find(songSong));
    }

    /// <summary>Hai trục cắt nhau nhưng giao điểm nằm ngoài đoạn vẽ: không sinh điểm (không có trên bản vẽ).</summary>
    [Fact]
    public void GridIntersections_GiaoDiemNgoaiPhamViVe_KhongSinhDiem()
    {
        var grids = new[]
        {
            new NamedSegment2D("A", new Segment2D(0, 0, 0, 1000)),
            new NamedSegment2D("1", new Segment2D(5000, 500, 9000, 500)),
        };

        Assert.Empty(GridIntersections.Find(grids));
    }

    /// <summary>
    /// Nối polyline theo cả bốn hướng ghép (đầu/cuối × đầu/cuối) — các đoạn cố ý cho ngược chiều nhau
    /// để đi hết bốn nhánh, kết quả vẫn phải là một chuỗi liền.
    /// </summary>
    [Fact]
    public void CadCurveFilter_NoiDoanNguocChieu_VanRaMotChuoiLien()
    {
        var curves = new[]
        {
            new CadCurve("TRUC", new Point3(0, 0, 0), new Point3(1000, 0, 0)),
            new CadCurve("TRUC", new Point3(2000, 0, 0), new Point3(1000, 0, 0)),
            new CadCurve("TRUC", new Point3(0, 0, 0), new Point3(-1000, 0, 0)),
            new CadCurve("TRUC", new Point3(-2000, 0, 0), new Point3(-1000, 0, 0)),
        };

        var result = CadCurveFilter.Filter(curves);

        var line = Assert.Single(result.Curves);
        Assert.Equal(4000.0, line.Length, 6);
        Assert.Equal(3, result.MergedCollinear);
    }

    [Fact]
    public void DevicePattern_DistancePointToSegment_DoanSuyBien_DoTuDiemDau()
    {
        var p = new Point2(30, 40);
        var a = new Point2(0, 0);

        Assert.Equal(50.0, DevicePattern.DistancePointToSegment(p, a, a), 9);
    }

    /// <summary>Đa giác suy biến (diện tích 0): trọng tâm lấy trung bình toạ độ thay vì chia cho 0.</summary>
    [Fact]
    public void DevicePattern_TrongTamDaGiacSuyBien_LayTrungBinhToaDo()
    {
        var polygon = new[] { new Point2(0, 0), new Point2(1000, 0), new Point2(2000, 0) };

        var c = DevicePattern.Centroid(polygon);

        Assert.Equal(1000.0, c.X, 6);
        Assert.Equal(0.0, c.Y, 6);
    }

    [Fact]
    public void KickGeometry_GiuLaiDoLechVaGocCut()
    {
        var kick = new KickGeometry(150, 45, 212, 150);

        Assert.Equal(150.0, kick.OffsetMm);
        Assert.Equal(45.0, kick.ElbowAngleDeg);
    }

    [Fact]
    public void FieldSpec_KieuDanhSachVaChuoiMoTa()
    {
        var spec = new FieldSpec("layers", "Danh sách layer", FieldKind.TextList);

        Assert.True(spec.IsList);
        Assert.Equal("layers:TextList", spec.ToString());
    }
}
