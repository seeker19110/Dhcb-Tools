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

    /// <summary>Hộp vô hạn/NaN không được biến vòng lặp ô thành vô tận — kẹp ở ±2^20 ô.</summary>
    [Fact]
    public void HopVoHan_KhongTreo()
    {
        var index = new BoxSpatialHash<int>(1e6);
        index.Insert(new Box3(double.NegativeInfinity, 0, 0, double.PositiveInfinity, 1, 1), 1);
        index.Insert(new Box3(double.NaN, 0, 0, double.NaN, 1, 1), 2);
        Assert.Contains(1, index.Query(new Box3(-1e9, 0, 0, 1e9, 1, 1)));
        Assert.Equal(2, index.Count);
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
