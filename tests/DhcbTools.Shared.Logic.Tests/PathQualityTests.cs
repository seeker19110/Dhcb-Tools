using System.Collections.Generic;
using DhcbTools.Shared.Logic.Mep;
using Xunit;

namespace DhcbTools.Shared.Logic.Tests;

/// <summary>
/// Thước đo chất lượng tuyến (§54): trước đó <c>AutoRoute</c> chỉ báo "N đoạn, M lần rẽ", nên "chất lượng tuyến
/// chưa chứng minh" nằm ở mục *Còn mở* suốt từ §18. Nay mỗi tuyến mang tỉ số dài/Manhattan và số rẽ so với
/// tối thiểu — kiểm được cả trong test thuần lẫn trong Summary của lượt chạy thật.
/// </summary>
public class PathQualityTests
{
    private static readonly Box3 Bounds = new(0, 0, 0, 6000, 6000, 3000);
    private static readonly PathFinderOptions Opt = new() { StepMm = 250, ClearanceMm = 0 };

    [Fact]
    public void KhongVatCan_TuyenThang_TiSo1_KhongRe()
    {
        var r = PathFinder3D.FindPath(new Point3(250, 250, 250), new Point3(5000, 250, 250), new List<Box3>(), Bounds, Opt);
        Assert.True(r.Found, r.Reason);
        Assert.Equal(1.0, r.DetourRatio, 6);
        Assert.Equal(0, r.MinTurns);
        Assert.Equal(0, r.Turns);
        Assert.Equal(4750, r.LengthMm, 6);
        Assert.Contains("1,00× Manhattan", r.QualityText());
        Assert.Contains("0 rẽ (tối thiểu 0)", r.QualityText());
    }

    [Fact]
    public void KhongVatCan_HaiTruc_TiSo1_ReDungToiThieu()
    {
        var r = PathFinder3D.FindPath(new Point3(250, 250, 250), new Point3(4000, 3000, 250), new List<Box3>(), Bounds, Opt);
        Assert.True(r.Found, r.Reason);
        Assert.Equal(1.0, r.DetourRatio, 6);
        Assert.Equal(1, r.MinTurns);
        Assert.Equal(r.MinTurns, r.Turns);   // không có gì phải né thì A* với phạt rẽ phải ra đúng 1 lần rẽ
        Assert.Equal(6500, r.ManhattanMm, 6);
    }

    [Fact]
    public void CoVatCanChanNgang_TiSoLonHon1_ReNhieuHonToiThieu_VaLaTuyenNganNhat()
    {
        // Bức chắn cắt ngang trục x tại x∈[2000,2500], phủ toàn bộ y ∈ [0, 4000] và z ∈ [0, 3000]:
        // tuyến phải vòng lên y > 4000 → thêm ít nhất 2 × (4000 − 250) mm... nhưng lưới 250 nên né qua ô y=4250.
        var wall = new Box3(2000, -1, -1, 2500, 4000, 3001);
        var r = PathFinder3D.FindPath(new Point3(250, 250, 250), new Point3(5000, 250, 250), new List<Box3> { wall }, Bounds, Opt);
        Assert.True(r.Found, r.Reason);
        Assert.True(r.DetourRatio > 1.0, r.QualityText());
        Assert.Equal(0, r.MinTurns);
        Assert.True(r.Turns >= 2, r.QualityText());
        // Đường vòng ngắn nhất: lên tới ô đầu tiên không bị chặn (y = 4250), sang, rồi xuống — 4750 + 2 × 4000.
        Assert.Equal(4750 + 2 * 4000, r.LengthMm, 6);
        Assert.Contains("× Manhattan", r.QualityText());
    }

    [Fact]
    public void ComputeQuality_PolylineRong_KhongChia0()
    {
        var r = new PathResult();
        r.ComputeQuality();
        Assert.Equal(1.0, r.DetourRatio);
        Assert.Equal(0, r.LengthMm);
        Assert.Contains("0,0 m", r.QualityText());
    }
}
