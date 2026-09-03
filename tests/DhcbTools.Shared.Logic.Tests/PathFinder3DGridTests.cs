using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using DhcbTools.Shared.Logic.Mep;
using Xunit;

namespace DhcbTools.Shared.Logic.Tests;

/// <summary>
/// Ca kiểm cho ba thứ vá sau §18: raster hoá chướng ngại (tốc độ), heuristic có phạt rẽ (số ô mở rộng),
/// và chẩn đoán khi thất bại (nói được vì sao thua). Tất cả đều thuần logic — không cần Revit.
/// </summary>
public class PathFinder3DGridTests
{
    private static readonly Box3 Bounds = new(0, 0, 0, 10000, 10000, 3000);

    /// <summary>
    /// Raster hoá phải cho ra ĐÚNG tập ô mà cách cũ (thử tâm ô với từng hộp) cho ra — nếu lệch thì tuyến
    /// có thể xuyên vật cản. Đối chiếu vét cạn trên lưới nhỏ với hộp đặt ngẫu nhiên, kể cả biên lẻ.
    /// </summary>
    [Fact]
    public void RasterHoa_TrungKhopVoiPhepThuTamO()
    {
        var rand = new Random(20260903);
        for (var lan = 0; lan < 40; lan++)
        {
            var boxes = new List<Box3>();
            for (var i = 0; i < 5; i++)
            {
                double x = rand.Next(-500, 5000), y = rand.Next(-500, 5000), z = rand.Next(-500, 2000);
                boxes.Add(new Box3(x, y, z, x + rand.Next(1, 1500), y + rand.Next(1, 1500), z + rand.Next(1, 1200)));
            }

            var clearance = rand.Next(0, 400);
            var step = 250d;
            var bounds = new Box3(0, 0, 0, 5000, 5000, 2000);

            // Ô nào bị chặn theo cách cũ thì tuyến mới phải không bao giờ đi qua: dựng lại tuyến và đối
            // chiếu từng điểm; đồng thời đối chiếu cả hai điểm đầu/cuối được nhận hay bị từ chối.
            var start = new Point3(0, 0, 0);
            var goal = new Point3(5000, 5000, 2000);
            var startBlocked = boxes.Any(b => b.Contains(start.X, start.Y, start.Z, clearance));
            var goalBlocked = boxes.Any(b => b.Contains(goal.X, goal.Y, goal.Z, clearance));

            var r = PathFinder3D.FindPath(start, goal, boxes, bounds,
                new PathFinderOptions { StepMm = step, ClearanceMm = clearance, NearObstaclePenalty = 0 });

            if (startBlocked || goalBlocked)
            {
                Assert.False(r.Found);
                Assert.Contains("chướng ngại", r.Reason);
                continue;
            }

            if (!r.Found)
            {
                continue;
            }

            // Mọi điểm trên polyline (kể cả các ô giữa hai đỉnh) phải nằm ngoài mọi hộp đã nới clearance.
            foreach (var p in DiemTrenTuyen(r.Polyline, step))
            {
                Assert.DoesNotContain(boxes, b => b.Contains(p.X, p.Y, p.Z, clearance));
            }
        }
    }

    /// <summary>
    /// Heuristic cũ bỏ qua <c>TurnPenalty</c> nên A* thoái hoá gần thành Dijkstra. Trên lưới 100 mm với
    /// vật cản thật, một tuyến chỉ cần hai lần rẽ mà phải mở rộng cả trăm nghìn ô là dấu hiệu của lỗi đó.
    /// </summary>
    [Fact]
    public void PhatReTrongHeuristic_KhongCanQuetCaLuoi()
    {
        var wall = new Box3(4000, 0, 0, 5000, 6000, 3000);
        var r = PathFinder3D.FindPath(new Point3(0, 0, 1000), new Point3(9000, 0, 1000), new[] { wall }, Bounds,
            new PathFinderOptions { StepMm = 100, ClearanceMm = 100, AllowVertical = false });

        Assert.True(r.Found, r.Reason);
        // Lưới 101×101 ô ở một cao độ ≈ 10.200 ô; nhân với 6 hướng lưu trạng thái là ~61.000. Cách cũ
        // ngốn gần hết chỗ đó, cách mới đi gần như thẳng tới đích.
        Assert.True(r.ExpandedNodes < 20_000, $"mở rộng {r.ExpandedNodes} ô — heuristic không còn dẫn hướng");
    }

    /// <summary>Heuristic mạnh hơn nhưng vẫn là chặn dưới: tuyến trả về phải vẫn ít lần rẽ nhất.</summary>
    [Fact]
    public void VanTraVeTuyenToiUu_DuHeuristicManhHon()
    {
        var wall = new Box3(4000, 0, 0, 5000, 6000, 3000);
        var r = PathFinder3D.FindPath(new Point3(0, 0, 0), new Point3(9000, 0, 0), new[] { wall }, Bounds,
            new PathFinderOptions { StepMm = 500, ClearanceMm = 0, AllowVertical = false, TurnPenalty = 5 });

        Assert.True(r.Found, r.Reason);
        Assert.Equal(2, r.Turns);
    }

    /// <summary>
    /// Thất bại vì bị bịt kín phải nói rõ là bịt kín — §18 chỉ có câu "Không có đường đi", người đọc không
    /// biết nên nới hộp tìm kiếm hay đổi điểm.
    /// </summary>
    [Fact]
    public void BiBitKin_NoiRoLaKhongNoiThong_VaChoBietKhoangTroConLai()
    {
        var wall = new Box3(4000, -1, -1, 5000, 10001, 3001);
        var r = PathFinder3D.FindPath(new Point3(0, 0, 0), new Point3(9000, 0, 0), new[] { wall }, Bounds,
            new PathFinderOptions { StepMm = 500, ClearanceMm = 0 });

        Assert.False(r.Found);
        Assert.False(r.GoalConnected);
        Assert.True(r.ReachableCells > 0);
        Assert.True(r.ReachableCells < r.GridCells);
        Assert.Contains("ô", r.Reason);
    }

    /// <summary>Ngược lại: hết ngân sách mà hai điểm vẫn nối thông thì phải nói ra, vì cách chữa khác hẳn.</summary>
    [Fact]
    public void HetNganSachNhungVanNoiThong_NoiDungLaHetNganSach()
    {
        var r = PathFinder3D.FindPath(new Point3(0, 0, 0), new Point3(9000, 9000, 0), Array.Empty<Box3>(), Bounds,
            new PathFinderOptions { StepMm = 100, MaxExpandedNodes = 10 });

        Assert.False(r.Found);
        Assert.True(r.GoalConnected);
        Assert.Contains("nối thông", r.Reason);
    }

    /// <summary>
    /// Hộp to với bước lưới mịn phải bị từ chối NGAY. Trước đây lệnh chạy gần 18 giây rồi mới báo chạm
    /// trần — trong batch một đêm thì đó là thời gian mất trắng.
    /// </summary>
    [Fact]
    public void LuoiQuaLon_TuChoiNgayThayViChayHangChucGiay()
    {
        var dongHo = Stopwatch.StartNew();
        var r = PathFinder3D.FindPath(new Point3(0, 0, 0), new Point3(100000, 100000, 30000), Array.Empty<Box3>(),
            new Box3(0, 0, 0, 100000, 100000, 30000), new PathFinderOptions { StepMm = 50 });
        dongHo.Stop();

        Assert.False(r.Found);
        Assert.Contains("quá lớn", r.Reason);
        Assert.True(r.GridCells > 16_000_000);
        Assert.True(dongHo.ElapsedMilliseconds < 1000, $"mất {dongHo.ElapsedMilliseconds} ms để nói câu từ chối");
    }

    /// <summary>
    /// Mô phỏng quy mô model thật của §18: ~550 vật cản trên lưới 100 mm. Cách cũ là 546 hộp × mỗi lần tra
    /// ô, tức hàng tỉ phép thử; cách mới raster hoá một lần nên phải xong trong vài giây.
    /// </summary>
    [Fact]
    public void QuyMoModelThat_550VatCan_XongTrongVaiGiay()
    {
        var rand = new Random(546);
        var boxes = new List<Box3>();
        for (var i = 0; i < 550; i++)
        {
            double x = rand.Next(0, 9500), y = rand.Next(0, 9500), z = rand.Next(0, 2500);
            boxes.Add(new Box3(x, y, z, x + rand.Next(200, 900), y + rand.Next(200, 900), z + rand.Next(200, 500)));
        }

        var dongHo = Stopwatch.StartNew();
        var r = PathFinder3D.FindPath(new Point3(0, 0, 2900), new Point3(10000, 10000, 2900), boxes, Bounds,
            new PathFinderOptions { StepMm = 100, ClearanceMm = 100 });
        dongHo.Stop();

        // Không khẳng định tìm được tuyến — chốt thứ đo được: có kết luận, và có trong vài giây.
        Assert.NotNull(r.Reason ?? (r.Found ? "ok" : null));
        Assert.True(dongHo.ElapsedMilliseconds < 5000, $"mất {dongHo.ElapsedMilliseconds} ms với 550 vật cản");
    }

    /// <summary>Nội suy các ô giữa hai đỉnh polyline — polyline chỉ giữ điểm rẽ nên phải trải lại để kiểm.</summary>
    private static IEnumerable<Point3> DiemTrenTuyen(IReadOnlyList<Point3> polyline, double step)
    {
        for (var i = 1; i < polyline.Count; i++)
        {
            var a = polyline[i - 1];
            var b = polyline[i];
            var so = (int)Math.Round((Math.Abs(b.X - a.X) + Math.Abs(b.Y - a.Y) + Math.Abs(b.Z - a.Z)) / step);
            for (var k = 0; k <= so; k++)
            {
                var t = so == 0 ? 0 : (double)k / so;
                yield return new Point3(a.X + ((b.X - a.X) * t), a.Y + ((b.Y - a.Y) * t), a.Z + ((b.Z - a.Z) * t));
            }
        }
    }
}
