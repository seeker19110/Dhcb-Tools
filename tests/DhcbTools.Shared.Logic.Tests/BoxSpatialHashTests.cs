using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DhcbTools.Shared.Logic.Batch;
using DhcbTools.Shared.Logic.Geometry;
using DhcbTools.Shared.Logic.Mep;
using Xunit;

namespace DhcbTools.Shared.Logic.Tests;

/// <summary>Băm không gian cho pha thô (ClashDetection, SleeveAuto, ModelLinesFromCad) — đối chiếu với vòng lặp n×m.</summary>
public class BoxSpatialHashTests
{
    private static Box3 Box(double x, double y, double z, double size) => new Box3(x, y, z, x + size, y + size, z + size);

    [Fact]
    public void Query_KhopVoiDuyetTuyenTinh_TrenDuLieuNgauNhien()
    {
        var rng = new Random(11);
        var boxes = new List<Box3>();
        for (var i = 0; i < 600; i++)
        {
            boxes.Add(Box(rng.NextDouble() * 5000, rng.NextDouble() * 5000, rng.NextDouble() * 1000, 10 + rng.NextDouble() * 400));
        }

        var index = BoxSpatialHash<int>.Build(Enumerable.Range(0, boxes.Count), i => boxes[i]);
        Assert.Equal(600, index.Count);

        for (var q = 0; q < 200; q++)
        {
            var query = Box(rng.NextDouble() * 5000, rng.NextDouble() * 5000, rng.NextDouble() * 1000, rng.NextDouble() * 600);
            var tol = rng.NextDouble() * 20;
            var expected = Enumerable.Range(0, boxes.Count).Where(i => BoxSpatialHash<int>.Intersects(boxes[i], query, tol)).ToList();
            var actual = index.Query(query, tol);
            Assert.Equal(expected, actual);
        }
    }

    [Fact]
    public void Query_MoiPhanTuTraMotLan_DuPhuNhieuO()
    {
        var index = new BoxSpatialHash<string>(10);
        index.Insert(new Box3(0, 0, 0, 95, 95, 95), "lớn");
        index.Insert(new Box3(200, 200, 200, 201, 201, 201), "xa");

        var hits = index.Query(new Box3(40, 40, 40, 60, 60, 60));
        Assert.Equal(new[] { "lớn" }, hits);
        Assert.Empty(index.Query(new Box3(100, 100, 100, 150, 150, 150)));
        // Nới tolerance thì chạm hộp "xa" (cách 50 theo mỗi trục).
        Assert.Equal(new[] { "lớn", "xa" }, index.Query(new Box3(95, 95, 95, 150, 150, 150), 50));
    }

    [Fact]
    public void QueryPoint_VaInsertPoint_DiemSuyBien()
    {
        var index = new BoxSpatialHash<int>(0.5);
        index.InsertPoint(1, 1, 1, 1);
        index.InsertPoint(1.2, 1, 1, 2);
        index.InsertPoint(5, 5, 5, 3);

        Assert.Equal(new[] { 1, 2 }, index.QueryPoint(1.1, 1, 1, 0.15));
        Assert.Equal(new[] { 1 }, index.QueryPoint(1, 1, 1, 0.05));
        Assert.Empty(index.QueryPoint(3, 3, 3, 0.5));
    }

    [Fact]
    public void SuggestCellSize_TrungViCanhLonNhat()
    {
        var boxes = new[] { Box(0, 0, 0, 1), Box(0, 0, 0, 3), Box(0, 0, 0, 100) };
        Assert.Equal(3, BoxSpatialHash<int>.SuggestCellSize(boxes));
        Assert.Equal(7, BoxSpatialHash<int>.SuggestCellSize(Array.Empty<Box3>(), 7));
        // Hộp suy biến (cạnh 0) không tính.
        Assert.Equal(2, BoxSpatialHash<int>.SuggestCellSize(new[] { new Box3(1, 1, 1, 1, 1, 1), Box(0, 0, 0, 2) }));
    }

    [Fact]
    public void Build_BoQuaPhanTuKhongCoHop()
    {
        var items = new[] { "a", "b", "c" };
        var index = BoxSpatialHash<string>.Build(items, s => s == "b" ? null : Box(0, 0, 0, 1));
        Assert.Equal(2, index.Count);
        Assert.Equal(new[] { "a", "c" }, index.Query(Box(0, 0, 0, 1)));
    }

    [Fact]
    public void DoiSoSai_Nem()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new BoxSpatialHash<int>(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BoxSpatialHash<int>(double.NaN));
        var index = new BoxSpatialHash<int>(1);
        Assert.Equal(0, index.Count);
        Assert.Equal(1, index.CellSize);
        Assert.Throws<ArgumentNullException>(() => index.Insert(null!, 1));
        Assert.Throws<ArgumentNullException>(() => index.Query(null!));
    }

    /// <summary>Hộp vô hạn/NaN dùng đường dự phòng chính xác, không mở hàng triệu ô lưới.</summary>
    [Fact]
    public void HopVoHan_KhongTreo()
    {
        var index = new BoxSpatialHash<int>(1e6);
        index.Insert(new Box3(double.NegativeInfinity, 0, 0, double.PositiveInfinity, 1, 1), 1);
        index.Insert(new Box3(double.NaN, 0, 0, double.NaN, 1, 1), 2);
        Assert.Contains(1, index.Query(new Box3(-1e9, 0, 0, 1e9, 1, 1)));
        Assert.Equal(2, index.Count);
    }

    [Fact]
    public void HopLonBaChieu_VaTruyVanLon_KhopDuyetTuyenTinh()
    {
        var boxes = new[]
        {
            Box(20, 20, 20, 2),
            new Box3(-1e12, -1e12, -1e12, 1e12, 1e12, 1e12),
            Box(-10, -10, -10, 1),
            new Box3(double.NegativeInfinity, double.NegativeInfinity, double.NegativeInfinity,
                     double.PositiveInfinity, double.PositiveInfinity, double.PositiveInfinity),
            Box(1e15, 1e15, 1e15, 1),
            new Box3(double.NaN, 0, 0, double.NaN, 1, 1),
            Box(20, 20, 20, 2),
        };
        var index = BoxSpatialHash<int>.Build(Enumerable.Range(0, boxes.Length), i => boxes[i], 1);
        var queries = new[]
        {
            Box(20, 20, 20, 0),
            new Box3(-1e14, -1e14, -1e14, 1e14, 1e14, 1e14),
            boxes[3],
            new Box3(-double.MaxValue, -double.MaxValue, -double.MaxValue,
                     double.MaxValue, double.MaxValue, double.MaxValue),
            boxes[4],
            boxes[5],
        };
        foreach (var query in queries)
        {
            foreach (var tolerance in new[] { 0.0, 2.0, 1e15, -100.0, double.PositiveInfinity, double.NaN })
            {
                var expected = Enumerable.Range(0, boxes.Length)
                    .Where(i => BoxSpatialHash<int>.Intersects(boxes[i], query, tolerance));
                Assert.Equal(expected, index.Query(query, tolerance));
            }
        }

        Assert.Equal(new[] { 0, 1, 3, 6 }, index.Query(queries[0]));
        Assert.Equal(boxes.Length, index.Count);
    }

    [Fact]
    public void HopVuotNganSachTrongPhamViKey_VanTimDuocVaGiuThuTuChen()
    {
        // These bounds fit the packed key but span over 1e15 cells. Clamping coordinates alone
        // cannot make either the insert or the query bounded.
        var index = new BoxSpatialHash<string>(1);
        index.InsertPoint(0, 0, 0, "điểm đầu");
        index.Insert(new Box3(-100000, -100000, -100000, 100000, 100000, 100000), "hộp lớn");
        index.InsertPoint(0, 0, 0, "điểm cuối");
        Assert.Equal(new[] { "điểm đầu", "hộp lớn", "điểm cuối" }, index.QueryPoint(0, 0, 0, 0));
        Assert.Equal(new[] { "điểm đầu", "hộp lớn", "điểm cuối" },
            index.Query(new Box3(-200000, -200000, -200000, 200000, 200000, 200000)));
        Assert.Empty(index.QueryPoint(500000, 500000, 500000, 0));
    }

    [Fact]
    public void NganSachO_BienNhoVaTichBaTruc_GiuChinhXac()
    {
        var boxes = new[]
        {
            new Box3(0, 0, 0, 4095, 0, 0), // Exactly 4096 cells: keep normal grid indexing.
            new Box3(0, 0, 0, 4096, 0, 0), // One beyond the budget: overflow list.
            new Box3(0, 0, 0, 15, 15, 16), // Each axis small, but the 3D product exceeds the budget.
        };
        var index = BoxSpatialHash<int>.Build(Enumerable.Range(0, boxes.Length), i => boxes[i], 1);
        var queries = new[] { Box(4095, 0, 0, 0), Box(4096, 0, 0, 0), Box(14, 14, 15, 0), boxes[2] };
        foreach (var query in queries)
        {
            Assert.Equal(Enumerable.Range(0, boxes.Length).Where(i => BoxSpatialHash<int>.Intersects(boxes[i], query, 0)),
                         index.Query(query));
        }
    }

    [Fact]
    public void TiLeOTranSo_VaToaDoNgoaiKey_KhongMatKetQua()
    {
        var boxes = new[] { Box(-double.MaxValue, 0, 0, 0), Box(double.MaxValue, 0, 0, 0), Box(0, 0, 0, 0) };
        var index = BoxSpatialHash<int>.Build(Enumerable.Range(0, boxes.Length), i => boxes[i], double.Epsilon);
        foreach (var query in boxes)
        {
            Assert.Equal(Enumerable.Range(0, boxes.Length).Where(i => BoxSpatialHash<int>.Intersects(boxes[i], query, 0)),
                         index.Query(query));
        }

        Assert.Equal(new[] { 0, 1, 2 }, index.Query(new Box3(-double.MaxValue, 0, 0, double.MaxValue, 0, 0)));
    }

    // ── RunLog: cắt Messages/Errors khi ghi ─────────────────────────────

    [Fact]
    public void RunLog_Append_CatMessagesQuaDai_GhiChuSoDongBoDi()
    {
        var path = Path.Combine(Path.GetTempPath(), "dhcb-runlog-cap-" + Guid.NewGuid().ToString("N") + ".jsonl");
        try
        {
            var entry = new RunLogEntry { File = "a.rvt", Command = "SleeveAuto", Success = true };
            for (var i = 0; i < RunLog.MaxPersistedLines + 250; i++)
            {
                entry.Messages.Add("vị trí " + i);
            }

            entry.Errors.Add("một lỗi");
            RunLog.Append(path, entry);

            Assert.Equal(RunLog.MaxPersistedLines + 1, entry.Messages.Count);
            Assert.Contains("và 250 dòng nữa", entry.Messages[RunLog.MaxPersistedLines]);
            Assert.Single(entry.Errors);

            var back = RunLog.ReadAll(path);
            Assert.Equal(RunLog.MaxPersistedLines + 1, Assert.Single(back).Messages.Count);
            Assert.Equal("Intact", ChainStatusOf(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string ChainStatusOf(string path) => RunLog.VerifyFile(path).Status.ToString();
}
