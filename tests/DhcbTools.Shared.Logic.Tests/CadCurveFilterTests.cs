using System.Linq;
using DhcbTools.Shared.Logic.Cad;
using DhcbTools.Shared.Logic.Mep;
using Xunit;

namespace DhcbTools.Shared.Logic.Tests;

/// <summary>
/// Tầng thuần của <c>ModelLinesFromCad</c> (đề xuất C4 — mắt xích còn thiếu: không ai dựng model line từ
/// DWG cho <c>RouteFromLines</c> ăn). Điều phải giữ: (1) **đường vẽ chồng hai lần không thành hai model
/// line** — rất phổ biến khi copy giữa các bản vẽ, và hai ống chồng nhau thì nhìn mặt bằng không thấy;
/// (2) đoạn rác trim/extend bị loại; (3) nối đoạn thẳng hàng nhưng **không nối xuyên ngã ba** — nối qua
/// đó là xoá mất một nhánh tuyến; (4) mọi đường bị bỏ đều đếm được, nói rõ vì sao.
/// </summary>
public class CadCurveFilterTests
{
    private static Point3 P(double x, double y, double z = 0) => new Point3(x, y, z);

    private static CadCurve Line(double x1, double y1, double x2, double y2, string layer = "M-PIPE", double z = 0)
        => new CadCurve(layer, P(x1, y1, z), P(x2, y2, z));

    private static CadCurve Arc(double x1, double y1, double mx, double my, double x2, double y2, string layer = "M-PIPE")
        => new CadCurve(layer, P(x1, y1), P(x2, y2), CadCurveKind.Arc, P(mx, my));

    // ── Layer ────────────────────────────────────────────────────────────────

    [Fact]
    public void Layer_KhongKhaiThiLayGiuNguyen()
    {
        var result = CadCurveFilter.Filter(new[] { Line(0, 0, 1000, 0, "bất kỳ") });
        Assert.Single(result.Curves);
        Assert.Equal(0, result.SkippedByLayer);
    }

    [Fact]
    public void Layer_LayTheoWildcardVaLoaiSauCungThang()
    {
        var options = new CadCurveFilterOptions();
        options.IncludeLayers.Add("M-*");
        options.ExcludeLayers.Add("M-*-TEXT");

        var result = CadCurveFilter.Filter(
            new[]
            {
                Line(0, 0, 1000, 0, "M-PIPE"),
                Line(0, 500, 1000, 500, "M-DUCT-TEXT"),
                Line(0, 1000, 1000, 1000, "A-WALL"),
            },
            options);

        Assert.Single(result.Curves);
        Assert.Equal("M-PIPE", result.Curves[0].Layer);
        Assert.Equal(2, result.SkippedByLayer);
    }

    [Fact]
    public void Layer_ThongKeTheoTungLayer()
    {
        var result = CadCurveFilter.Filter(new[]
        {
            Line(0, 0, 1000, 0, "M-PIPE"),
            Line(0, 2000, 1000, 2000, "M-PIPE"),
            Line(0, 4000, 1000, 4000, "M-DUCT"),
        });

        Assert.Equal(2, result.ByLayer["M-PIPE"]);
        Assert.Equal(1, result.ByLayer["M-DUCT"]);
    }

    // ── Rác và trùng ─────────────────────────────────────────────────────────

    [Fact]
    public void BoDoanNganHonNguong()
    {
        var result = CadCurveFilter.Filter(new[] { Line(0, 0, 10, 0), Line(0, 1000, 1000, 1000) },
            new CadCurveFilterOptions { MinLengthMm = 50 });

        Assert.Single(result.Curves);
        Assert.Equal(1, result.SkippedShort);
    }

    [Fact]
    public void DuongVeChongHaiLanChiRaMotModelLine()
    {
        var result = CadCurveFilter.Filter(new[] { Line(0, 0, 5000, 0), Line(0, 0, 5000, 0) });
        Assert.Single(result.Curves);
        Assert.Equal(1, result.SkippedDuplicate);
    }

    [Fact]
    public void TrungKeCaKhiVeNguocChieu()
    {
        var result = CadCurveFilter.Filter(new[] { Line(0, 0, 5000, 0), Line(5000, 0, 0, 0) });
        Assert.Single(result.Curves);
        Assert.Equal(1, result.SkippedDuplicate);
    }

    [Fact]
    public void TrungTheoDungSaiHan_KhongPhaiTrungTuyetDoi()
    {
        // CAD hay lệch vài phần mười mm giữa hai lần copy; đúng bằng nhau tới từng số lẻ là chuyện hiếm.
        var result = CadCurveFilter.Filter(new[] { Line(0, 0, 5000, 0), Line(0.4, 0, 5000.3, 0) },
            new CadCurveFilterOptions { WeldToleranceMm = 1.0 });
        Assert.Single(result.Curves);
    }

    [Fact]
    public void KhacLayerThiKhongCoiLaTrung()
    {
        var result = CadCurveFilter.Filter(new[] { Line(0, 0, 5000, 0, "M-PIPE"), Line(0, 0, 5000, 0, "M-DUCT") });
        Assert.Equal(2, result.Curves.Count);
        Assert.Equal(0, result.SkippedDuplicate);
    }

    // ── Cung ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Cung_GiuNguyenKhiBat_BoVaDemKhiTat()
    {
        var arcs = new[] { Arc(0, 0, 1000, 500, 2000, 0) };
        Assert.Single(CadCurveFilter.Filter(arcs).Curves);

        var off = CadCurveFilter.Filter(arcs, new CadCurveFilterOptions { IncludeArcs = false });
        Assert.Empty(off.Curves);
        Assert.Equal(1, off.SkippedArc);
    }

    [Fact]
    public void Cung_CongNguocNhauKhongPhaiLaTrung()
    {
        var result = CadCurveFilter.Filter(new[]
        {
            Arc(0, 0, 1000, 500, 2000, 0),
            Arc(0, 0, 1000, -500, 2000, 0),
        });
        Assert.Equal(2, result.Curves.Count);
    }

    [Fact]
    public void Cung_KhongBiGopThangHang()
    {
        var result = CadCurveFilter.Filter(new[] { Arc(0, 0, 1000, 500, 2000, 0), Line(2000, 0, 5000, 0) });
        Assert.Equal(2, result.Curves.Count);
        Assert.Equal(0, result.MergedCollinear);
    }

    // ── Nối thẳng hàng ───────────────────────────────────────────────────────

    [Fact]
    public void NoiBaDoanThangHangThanhMot()
    {
        var result = CadCurveFilter.Filter(new[]
        {
            Line(0, 0, 1000, 0),
            Line(1000, 0, 2000, 0),
            Line(2000, 0, 3000, 0),
        });

        Assert.Single(result.Curves);
        Assert.Equal(3000, result.Curves[0].Length, 6);
        Assert.Equal(2, result.MergedCollinear);
    }

    [Fact]
    public void KhongNoiXuyenNgaBa()
    {
        // Tuyến chính 0→3000 có nhánh rẽ tại 1500: nối xuyên qua đó là xoá mất nhánh.
        var result = CadCurveFilter.Filter(new[]
        {
            Line(0, 0, 1500, 0),
            Line(1500, 0, 3000, 0),
            Line(1500, 0, 1500, 2000),
        });

        Assert.Equal(3, result.Curves.Count);
        Assert.Contains(result.Curves, c => c.Start.DistanceTo(P(1500, 0)) < 1e-6 && c.End.DistanceTo(P(1500, 2000)) < 1e-6);
    }

    [Fact]
    public void KhongNoiHaiDoanGapNhauNhungGapKhuc()
    {
        var result = CadCurveFilter.Filter(new[] { Line(0, 0, 1000, 0), Line(1000, 0, 1000, 1000) });
        Assert.Equal(2, result.Curves.Count);
        Assert.Equal(0, result.MergedCollinear);
    }

    [Fact]
    public void TatNoiThiGiuNguyenTungDoan()
    {
        var result = CadCurveFilter.Filter(
            new[] { Line(0, 0, 1000, 0), Line(1000, 0, 2000, 0) },
            new CadCurveFilterOptions { MergeCollinear = false });
        Assert.Equal(2, result.Curves.Count);
    }

    [Fact]
    public void NoiKhongTronLayerKhacNhau()
    {
        var result = CadCurveFilter.Filter(new[] { Line(0, 0, 1000, 0, "M-PIPE"), Line(1000, 0, 2000, 0, "M-DUCT") });
        Assert.Equal(2, result.Curves.Count);
    }

    // ── Cao độ ───────────────────────────────────────────────────────────────

    [Fact]
    public void EpVeMotCaoDoKhiKhai()
    {
        var result = CadCurveFilter.Filter(
            new[] { new CadCurve("M-PIPE", P(0, 0, 137), P(3000, 0, -42)) },
            new CadCurveFilterOptions { FlattenToZMm = 2800 });

        Assert.Equal(2800, result.Curves[0].Start.Z, 6);
        Assert.Equal(2800, result.Curves[0].End.Z, 6);
    }

    [Fact]
    public void KhongEpThiGiuZCuaBanVe()
    {
        var result = CadCurveFilter.Filter(new[] { new CadCurve("M-PIPE", P(0, 0, 137), P(3000, 0, 137)) });
        Assert.Equal(137, result.Curves[0].Start.Z, 6);
    }

    [Fact]
    public void EpCaoDoRoiMoiXetTrung()
    {
        // Hai đường cùng vị trí mặt bằng nhưng khác Z: sau khi ép về một cao độ thì là một.
        var result = CadCurveFilter.Filter(
            new[] { new CadCurve("M-PIPE", P(0, 0, 0), P(3000, 0, 0)), new CadCurve("M-PIPE", P(0, 0, 500), P(3000, 0, 500)) },
            new CadCurveFilterOptions { FlattenToZMm = 0 });

        Assert.Single(result.Curves);
        Assert.Equal(1, result.SkippedDuplicate);
    }

    // ── Báo cáo ──────────────────────────────────────────────────────────────

    [Fact]
    public void TomTatNoiDuLyDoBiBo()
    {
        var options = new CadCurveFilterOptions { MinLengthMm = 50 };
        options.IncludeLayers.Add("M-*");
        var result = CadCurveFilter.Filter(
            new[] { Line(0, 0, 5000, 0), Line(0, 0, 5000, 0), Line(0, 1000, 5, 1000), Line(0, 2000, 5000, 2000, "A-WALL") },
            options);

        var summary = result.Summary();
        Assert.Contains("1 đường giữ lại", summary);
        Assert.Contains("1 sai layer", summary);
        Assert.Contains("1 quá ngắn", summary);
        Assert.Contains("1 trùng", summary);
    }

    [Fact]
    public void KhongCoDuongNaoThiKhongNo()
    {
        var result = CadCurveFilter.Filter(System.Array.Empty<CadCurve>());
        Assert.Empty(result.Curves);
        Assert.Equal("0 đường giữ lại", result.Summary());
    }
}

/// <summary>
/// So trùng với model line **đã có trong mô hình** — chạy `ModelLinesFromCad` lần hai không được đẻ ra
/// bản sao nằm đè lên bản cũ (§12: chốt bằng tính idempotent thay vì dọn lại).
/// </summary>
public class CadCurveSameShapeTests
{
    private static Point3 P(double x, double y, double z = 0) => new Point3(x, y, z);

    [Fact]
    public void SameShape_BoQuaLayer()
    {
        // Model line mang tên line style Revit ("DHCB-Route"), đường CAD mang tên layer DWG ("M-PIPE").
        var fromModel = new CadCurve("DHCB-Route", P(0, 0), P(5000, 0));
        var fromCad = new CadCurve("M-PIPE", P(0, 0), P(5000, 0));

        Assert.True(CadCurveFilter.SameShape(fromModel, fromCad, 1.0));
        Assert.False(CadCurveFilter.SameCurve(fromModel, fromCad, 1.0));
    }

    [Fact]
    public void SameShape_VanPhanBietHinhKhacNhau()
    {
        var a = new CadCurve("x", P(0, 0), P(5000, 0));
        Assert.False(CadCurveFilter.SameShape(a, new CadCurve("x", P(0, 0), P(5000, 1000)), 1.0));
        Assert.False(CadCurveFilter.SameShape(a, new CadCurve("x", P(0, 0), P(5000, 0), CadCurveKind.Arc, P(2500, 200)), 1.0));
    }
}

/// <summary>
/// So tên bản vẽ khi quyết định "đã link rồi thì bỏ qua". Lớp lỗi ở đây không ném ngoại lệ nào: lệnh báo
/// thành công, bản vẽ không vào mô hình, và lệnh sau đó nói "không tìm thấy bản vẽ CAD nào".
/// </summary>
public class CadFileMatchTests
{
    [Theory]
    [InlineData("tuyen-ong.dxf", "C:/fixtures/tuyen-ong.dxf")]
    [InlineData("TUYEN-ONG.DXF", "C:/khac/tuyen-ong.dxf")]   // khác thư mục, khác hoa thường = vẫn cùng bản vẽ
    public void CungBanVe(string existing, string candidate) =>
        Assert.True(CadFileMatch.SameDrawing(existing, candidate));

    // Tên mất đuôi mở rộng (bản vẽ IMPORT, chỉ còn tên element kiểu): nhận, nhưng phải khai rõ ràng.
    [Fact]
    public void TenMatDuoiMoRong_ChiNhanKhiBatCoCho() 
    {
        Assert.False(CadFileMatch.SameDrawing("tuyen-ong", "C:/fixtures/tuyen-ong.dxf"));
        Assert.True(CadFileMatch.SameDrawing("tuyen-ong", "C:/fixtures/tuyen-ong.dxf", allowMissingExtension: true));
        // Có đuôi rồi thì cờ đó không được nới lỏng gì thêm — đây chính là chỗ .dwg bị coi là .dxf.
        Assert.False(CadFileMatch.SameDrawing("tuyen-ong.dxf", "C:/fixtures/tuyen-ong.dwg", allowMissingExtension: true));
    }

    [Theory]
    [InlineData("tuyen-ong.dxf", "C:/fixtures/tuyen-ong.dwg")]      // ĐÚNG LỖI ĐÃ GẶP: .dwg bị coi là đã có
    [InlineData("tuyen-ong.dxf", "C:/fixtures/tuyen-ong-giua.dxf")] // chiều ngược lại của "chứa chuỗi con"
    [InlineData("tuyen-ong-giua.dxf", "C:/fixtures/tuyen-ong.dxf")]
    [InlineData("", "C:/fixtures/tuyen-ong.dxf")]
    [InlineData("tuyen-ong.dxf", "")]
    [InlineData(null, null)]
    public void KhacBanVe(string? existing, string? candidate) =>
        Assert.False(CadFileMatch.SameDrawing(existing, candidate));
}
