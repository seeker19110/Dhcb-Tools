using DhcbTools.Shared.Logic.Batch;
using DhcbTools.Shared.Logic.Geometry;
using DhcbTools.Shared.Logic.Mep;
using DhcbTools.Shared.Logic.Usage;
using Xunit;

namespace DhcbTools.Shared.Logic.Tests;

/// <summary>Nhánh còn lại: đầu vào rỗng/null của các hàm đọc log, và các trường hợp hình học biên.</summary>
public class LogicGap2Tests
{
    [Fact]
    public void RunLog_Deserialize_DongRong_TraNull()
    {
        Assert.Null(RunLog.Deserialize(null));
        Assert.Null(RunLog.Deserialize("   "));
    }

    [Fact]
    public void RvtFileInfo_BufferRong_TraNull()
    {
        Assert.Null(RvtFileInfo.DetectVersion((byte[])null!));
        Assert.Null(RvtFileInfo.DetectVersion(Array.Empty<byte>()));
    }

    /// <summary>Token toàn dấu, không chữ cái: không phải mẫu ngày giờ, giữ nguyên.</summary>
    [Fact]
    public void JobTokens_TokenKhongCoChuCai_GiuNguyen()
    {
        var context = new JobTokenContext("out", "ban-ve", new DateTime(2026, 9, 5));

        Assert.Equal("{--}", JobTokens.Expand("{--}", context));
    }

    /// <summary>Tên trục có chữ không phải số: xếp vào nhóm trục chữ, tên giao điểm là "chữ-số".</summary>
    [Fact]
    public void GridIntersections_TenTrucCoChu_XepVaoNhomTrucChu()
    {
        var grids = new[]
        {
            new NamedSegment2D("A1", new Segment2D(0, 0, 0, 1000)),
            new NamedSegment2D("2", new Segment2D(-1000, 500, 1000, 500)),
        };

        Assert.Equal("A1-2", Assert.Single(GridIntersections.Find(grids)).Name);
    }

    /// <summary>Trục không có tên: không phải trục số, xếp vào nhóm trục chữ.</summary>
    [Fact]
    public void GridIntersections_TrucKhongCoTen_XepVaoNhomTrucChu()
    {
        var grids = new[]
        {
            new NamedSegment2D(string.Empty, new Segment2D(0, 0, 0, 1000)),
            new NamedSegment2D("2", new Segment2D(-1000, 500, 1000, 500)),
        };

        Assert.Equal("-2", Assert.Single(GridIntersections.Find(grids)).Name);
    }

    /// <summary>
    /// Điểm cần phủ rơi đúng vào trọng tâm phòng: không có hướng nào để lùi khỏi biên, giữ nguyên vị trí
    /// thay vì chia cho vector 0 rồi ra NaN.
    /// </summary>
    [Fact]
    public void DevicePattern_DiemCanPhuTrungTrongTam_GiuNguyenViTri()
    {
        var boundary = new[]
        {
            new Point2(750, 0), new Point2(1750, 0), new Point2(1750, 750), new Point2(2500, 750),
            new Point2(2500, 1750), new Point2(1750, 1750), new Point2(1750, 2500), new Point2(750, 2500),
            new Point2(750, 1750), new Point2(0, 1750), new Point2(0, 750), new Point2(750, 750),
        };
        var options = new GridPatternOptions
        {
            Margin = 750,
            SpacingX = 2000,
            SpacingY = 2000,
            CoverageRadius = 100,
            CoverageCheckStep = 2500,
        };

        var plan = DevicePattern.GridInPolygon(boundary, options);

        var added = Assert.Single(plan.AddedForCoverage);
        Assert.Equal(1250.0, added.X, 6);
        Assert.Equal(1250.0, added.Y, 6);
        Assert.Empty(plan.Uncovered);
    }

    /// <summary>Lệnh chưa có lần chạy nào: số liệu bằng 0, không ném "Sequence contains no elements".</summary>
    [Fact]
    public void UsageStat_KhongCoLanChayNao_SoLieuBang0()
    {
        var stat = new UsageStat("Revit", "ClashDetection", Array.Empty<UsageEntry>());

        Assert.Equal(0, stat.Runs);
        Assert.Equal(0, stat.MedianMs);
        Assert.Equal(default, stat.First);
    }

    /// <summary>Tên file đúng khuôn nhưng ngày không có thật (2026-13-45): bỏ qua, không ném.</summary>
    [Fact]
    public void UsageLog_NgayKhongCoThat_TraDanhSachRong()
    {
        Assert.Empty(UsageLog.Parse("Revit-2026-13-45.log", new[] { UsageLog.Format("ClashDetection", true, false, 1, 10) }));
    }

    /// <summary>Điểm lưới rơi đúng lên biên phòng vẫn tính là nằm trong phòng.</summary>
    [Fact]
    public void DevicePattern_DiemNamTrenBien_TinhLaTrongPhong()
    {
        var boundary = new[]
        {
            new Point2(0, 0), new Point2(1000, 0), new Point2(1000, 1000), new Point2(0, 1000),
        };
        var options = new GridPatternOptions
        {
            Margin = 0,
            SpacingX = 1000,
            SpacingY = 1000,
            CoverageRadius = 0,
        };

        var plan = DevicePattern.GridInPolygon(boundary, options);

        Assert.Equal(4, plan.Points.Count);
    }

    /// <summary>
    /// Phòng hình chữ thập, bán kính phủ nhỏ hơn bước lưới rất nhiều: thuật toán chèn thêm thiết bị tới
    /// khi không chèn được nữa, rồi báo rõ còn bao nhiêu điểm chưa phủ thay vì lặng lẽ trả một kế hoạch thiếu.
    /// </summary>
    [Fact]
    public void DevicePattern_KhongPhuHet_BaoSoDiemConLai()
    {
        var boundary = new[]
        {
            new Point2(750, 0), new Point2(1750, 0), new Point2(1750, 750), new Point2(2500, 750),
            new Point2(2500, 1750), new Point2(1750, 1750), new Point2(1750, 2500), new Point2(750, 2500),
            new Point2(750, 1750), new Point2(0, 1750), new Point2(0, 750), new Point2(750, 750),
        };
        var options = new GridPatternOptions
        {
            Margin = 600,
            SpacingX = 1000,
            SpacingY = 1000,
            CoverageRadius = 100,
            CoverageCheckStep = 500,
        };

        var plan = DevicePattern.GridInPolygon(boundary, options);

        Assert.NotEmpty(plan.Uncovered);
        Assert.Contains(plan.Messages, m => m.Contains("Không đặt thêm được thiết bị"));
    }

    /// <summary>
    /// Cạnh được thêm tay vào <c>Edges</c> (List công khai) sau khi Build: tra ngược theo Id vẫn phải
    /// tìm được, không nổ KeyNotFound.
    /// </summary>
    [Fact]
    public void RouteGraph_CanhThemTaySauBuild_VanTraNguocDuocTheoId()
    {
        var graph = RouteGraph<string>.Build(new[]
        {
            new RouteSegment<string>("S1", new Point3(0, 0, 0), new Point3(1000, 0, 0)),
        }, tolerance: 1);

        // Nút 1 (đầu 1000) đang bậc 1; nối tay thêm một cạnh tới một nút mới để nó thành bậc 2.
        graph.Nodes.Add(new RouteNode(2, new Point3(1000, 1000, 0)));
        graph.Edges.Add(new RouteEdge<string>(1, "S2", 1, 2));
        graph.Nodes[1].EdgeIds.Add(1);
        graph.Nodes[2].EdgeIds.Add(1);

        Assert.Equal(90.0, graph.AngleAt(1), 6);
    }

    /// <summary>Báo cáo HTML: dòng Messages của một step phải nằm trong ô kết quả, đã escape.</summary>
    [Fact]
    public void BatchReport_CoMessages_HienTrongOKetQua()
    {
        var entries = new List<RunLogEntry>
        {
            new RunLogEntry
            {
                File = "a.rvt",
                Command = "KiemTra",
                Success = true,
                Summary = "xong",
                Messages = { "sửa <b>3</b> phần tử" },
            },
        };

        var html = BatchReport.Render("Lô kiểm tra", entries, new DateTime(2026, 9, 5));

        Assert.Contains("<pre>sửa &lt;b&gt;3&lt;/b&gt; phần tử</pre>", html);
    }
}
