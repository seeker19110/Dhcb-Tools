using System.Net;
using System.Text;
using DhcbTools.Shared.Logic.Ai;
using DhcbTools.Shared.Logic.Cad;
using DhcbTools.Shared.Logic.Geometry;
using DhcbTools.Shared.Logic.Mep;
using Xunit;

namespace DhcbTools.Shared.Logic.Tests;

/// <summary>CsvText.ReadRecords — RFC 4180 đầy đủ: ô có nháy chứa xuống dòng và nháy kép đôi.</summary>
public class CsvReadRecordsTests
{
    [Fact]
    public void ReadRecords_ONhieuDong_VaNhayKepDoi_DiVongEscapeRoiDocLai()
    {
        var description = "Dòng 1\r\nDòng 2, có phẩy và \"nháy\"";
        var csv = CsvText.JoinLine(new[] { "Name", "Description" }) + "\r\n"
                  + CsvText.JoinLine(new[] { "A-WALL", description }) + "\r\n"
                  + CsvText.JoinLine(new[] { "A-DOOR", "đơn giản" }) + "\r\n";

        var records = CsvText.ReadRecords(new StringReader(csv)).ToList();

        Assert.Equal(3, records.Count);
        Assert.Equal(new[] { "A-WALL", description }, records[1]);
        Assert.Equal(new[] { "A-DOOR", "đơn giản" }, records[2]);
    }

    [Fact]
    public void ReadRecords_File_UTF8CoBOM_GiuTiengViet()
    {
        var path = Path.Combine(Path.GetTempPath(), "dhcb-csv-" + Guid.NewGuid().ToString("N") + ".csv");
        try
        {
            File.WriteAllText(path, "Tên,Ghi chú\n\"Phòng\nkhách\",x\n", CsvText.Utf8WithBom);
            var records = CsvText.ReadRecords(path).ToList();
            Assert.Equal(2, records.Count);
            Assert.Equal("Tên", records[0][0]);
            Assert.Equal("Phòng\nkhách", records[1][0]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ReadRecords_DongTrongGiua_LaMotORong_DongTrongCuoi_KhongSinhBanGhi()
    {
        var records = CsvText.ReadRecords(new StringReader("a,b\n\nc,d\n")).ToList();
        Assert.Equal(3, records.Count);
        Assert.Equal(new[] { "" }, records[1]);
        Assert.Equal(new[] { "c", "d" }, records[2]);
    }

    [Fact]
    public void ReadRecords_ChiCR_VaKhongXuongDongCuoi_VanDocDu()
    {
        var records = CsvText.ReadRecords(new StringReader("a,b\rc,d")).ToList();
        Assert.Equal(2, records.Count);
        Assert.Equal(new[] { "c", "d" }, records[1]);
    }

    [Fact]
    public void ReadRecords_KhopVoiSplitLine_TrenDongDon()
    {
        const string line = "1,\"Phòng, hành lang\",\"Cao 2\"\"\",x";
        Assert.Equal(CsvText.SplitLine(line), CsvText.ReadRecords(new StringReader(line)).Single());
    }

    [Fact]
    public void LayerMapTable_ONhieuDong_KhongVoBanGhi()
    {
        var csv = "Source,Target,Color\n\"A\nB\",C,1\nD,E\n";
        var errors = new List<string>();
        var table = LayerMapTable.ParseCsv(csv, errors);
        Assert.Empty(errors);
        Assert.Equal(2, table.Entries.Count);
        Assert.Equal("A\nB", table.Entries[0].Source);
    }
}

/// <summary>OllamaClient qua transport giả — không cần Ollama thật.</summary>
public class OllamaClientTransportTests
{
    private static LocalAiSettings On() => new() { Enabled = true, Endpoint = "http://127.0.0.1:11434", Model = "qwen3:8b" };

    private static IReadOnlyList<CommandDescriptor> Candidates() => CommandCatalog.For(CommandCatalog.AutoCad).Take(3).ToList();

    private static string Wrap(string modelJson) => "{\"response\":" + Newtonsoft.Json.JsonConvert.ToString(modelJson) + "}";

    [Fact]
    public void ChooseCommand_ChonTrongDanhSach_ConfidenceLaChuoi_VanDoc()
    {
        var name = Candidates()[1].Name;
        var client = new OllamaClient(On(), (_, _, _) => Wrap("{\"command\":\"" + name + "\",\"confidence\":\"0.8\",\"reason\":\"ok\"}"));

        var chosen = client.ChooseCommand("dọn bản vẽ", Candidates(), out var confidence, out var reason);

        Assert.Equal(name, chosen);
        Assert.Equal(0.8, confidence, 6);
        Assert.Equal("ok", reason);
        Assert.Null(client.LastError);
    }

    [Fact]
    public void ChooseCommand_NgoaiWhitelist_TraNullVaNoiLyDo()
    {
        var client = new OllamaClient(On(), (_, _, _) => Wrap("{\"command\":\"FormatDisk\",\"confidence\":0.99}"));

        Assert.Null(client.ChooseCommand("x", Candidates(), out _, out _));
        Assert.Contains("FormatDisk", client.LastError);
    }

    [Fact]
    public void ChooseCommand_JsonSaiKieu_KhongNem()
    {
        var client = new OllamaClient(On(), (_, _, _) => Wrap("{\"command\":5,\"confidence\":{\"a\":1},\"reason\":[1,2]}"));

        Assert.Null(client.ChooseCommand("x", Candidates(), out var confidence, out _));
        Assert.Equal(0.5, confidence);

        var huge = new OllamaClient(On(), (_, _, _) => Wrap("{\"command\":null,\"confidence\":1e999}"));
        Assert.Null(huge.ChooseCommand("x", Candidates(), out confidence, out _));
        Assert.True(confidence >= 0 && confidence <= 1);
    }

    [Fact]
    public void ChooseCommand_LoiHttp_TraNullVaLastErrorCoEndpoint()
    {
        var client = new OllamaClient(On(), (_, _, _) => throw new WebException("Connection refused"));

        Assert.Null(client.ChooseCommand("x", Candidates(), out _, out _));
        Assert.Contains("127.0.0.1", client.LastError);
        Assert.Contains("Connection refused", client.LastError);
    }

    [Fact]
    public void ChooseCommand_PhanHoiKhongPhaiJson_TraNull()
    {
        var client = new OllamaClient(On(), (_, _, _) => "<html>502</html>");
        Assert.Null(client.ChooseCommand("x", Candidates(), out _, out _));
        Assert.Contains("JSON", client.LastError);
    }

    [Fact]
    public void ChooseCommand_ReasonDai_BiCatVe300()
    {
        var name = Candidates()[0].Name;
        var longReason = new string('a', 1000);
        var client = new OllamaClient(On(), (_, _, _) => Wrap("{\"command\":\"" + name + "\",\"confidence\":0.9,\"reason\":\"" + longReason + "\"}"));

        Assert.Equal(name, client.ChooseCommand("x", Candidates(), out _, out var reason));
        Assert.True(reason!.Length <= OllamaClient.MaxReasonLength + 1);
    }

    [Fact]
    public void Transport_GuiDungUrlVaBody()
    {
        string? url = null;
        string? body = null;
        var client = new OllamaClient(On(), (u, b, _) => { url = u; body = b; return "{\"response\":\"hi\"}"; });

        Assert.Equal("hi", client.Generate("xin chào"));
        Assert.Equal("http://127.0.0.1:11434/api/generate", url);
        Assert.Contains("\"model\":\"qwen3:8b\"", body);
        Assert.Contains("xin chào", body);
    }

    [Fact]
    public void Tat_KhongGoiTransport()
    {
        var called = false;
        var client = new OllamaClient(new LocalAiSettings { Enabled = false }, (_, _, _) => { called = true; return "{}"; });
        Assert.Null(client.Generate("x"));
        Assert.False(called);
        Assert.NotNull(client.LastError);
    }

    [Fact]
    public void ReadCapped_QuaGioiHan_NemIOException()
    {
        Assert.Equal("abc", OllamaClient.ReadCapped(new StringReader("abc"), 10));
        Assert.Throws<IOException>(() => OllamaClient.ReadCapped(new StringReader(new string('x', 100)), 10));
    }

    [Fact]
    public void Load_FileBiKhoa_TraMacDinhKhongNem()
    {
        var path = Path.Combine(Path.GetTempPath(), "dhcb-ai-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllText(path, "{\"enabled\":true}");
            using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                var settings = LocalAiSettings.Load(path);
                Assert.False(settings.Enabled);
            }
        }
        finally
        {
            File.Delete(path);
        }
    }
}

/// <summary>Đọc số kiểu Việt/Âu trong CommandIntentParser.</summary>
public class CommandIntentNumberTests
{
    [Theory]
    [InlineData("2.000", 2000)]
    [InlineData("2,000", 2000)]
    [InlineData("2,5", 2.5)]
    [InlineData("2.5", 2.5)]
    [InlineData("1.234.567", 1234567)]
    [InlineData("1.000,5", 1000.5)]
    [InlineData("1,234.5", 1234.5)]
    [InlineData("12.34", 12.34)]
    [InlineData("300", 300)]
    public void TryParseNumber_PhanBietNghinVaThapPhan(string text, double expected)
    {
        Assert.True(CommandIntentParser.TryParseNumber(text, out var v));
        Assert.Equal(expected, v, 9);
    }

    [Theory]
    [InlineData("cách 2.000 mm", 2000)]
    [InlineData("cách 2,5 m", 2500)]
    [InlineData("cách 2 mét", 2000)]
    [InlineData("cách 250 cm", 2500)]
    [InlineData("cách 1.5m", 1500)]
    public void ExtractLengthsMm_DonVi(string text, double expected)
    {
        Assert.Equal(new[] { expected }, CommandIntentParser.ExtractLengthsMm(text));
    }

    [Fact]
    public void ExtractLengthsMm_BoSoSauTang_LevelVaSo()
    {
        Assert.Equal(new[] { 1500.0 }, CommandIntentParser.ExtractLengthsMm("đặt hanger tầng 2 cách 1500"));
        Assert.Equal(new[] { 900.0 }, CommandIntentParser.ExtractLengthsMm("level 3, khoảng cách 900 mm"));
        Assert.Empty(CommandIntentParser.ExtractLengthsMm("đánh số 5 cửa"));
    }

    [Fact]
    public void ExtractLengthsMm_DonViKhongPhaiTienToCuaTu()
    {
        // "2 max" không phải "2 m".
        Assert.Equal(new[] { 2.0 }, CommandIntentParser.ExtractLengthsMm("tối đa 2 max"));
    }

    [Fact]
    public void Parse_HangerTang2_KhoangCachLaSoThuHai()
    {
        var intent = CommandIntentParser.Parse("đặt hanger tầng 2 cách 2.000 mm cho ống", CommandCatalog.Revit);
        Assert.Equal("HangerAuto", intent.Command);
        Assert.Equal(2000, (double)intent.Config["spacingMm"]!);
    }

    [Fact]
    public void Parse_OnDinhGiuaCacLanGoi()
    {
        var a = CommandIntentParser.Parse("đánh số", CommandCatalog.Revit);
        for (var i = 0; i < 5; i++)
        {
            var b = CommandIntentParser.Parse("đánh số", CommandCatalog.Revit);
            Assert.Equal(a.Command, b.Command);
            Assert.Equal(a.Alternatives, b.Alternatives);
        }
    }
}

public class NumberingPlannerAssignTests
{
    [Fact]
    public void Assign_Buoc0_NemLoi()
    {
        var items = new[] { new NumberingItem<int>(1, 0, 0) };
        Assert.Throws<ArgumentOutOfRangeException>(() => NumberingPlanner.Assign(items, "P", 1, 0, 0));
    }

    [Fact]
    public void Assign_TranInt_NemOverflowThayViQuayVong()
    {
        var items = new[] { new NumberingItem<int>(1, 0, 0), new NumberingItem<int>(2, 1, 0) };
        Assert.Throws<OverflowException>(() => NumberingPlanner.Assign(items, "", int.MaxValue, 1, 0));
    }

    [Fact]
    public void Assign_BuocAm_VanChay()
    {
        var items = new[] { new NumberingItem<int>(1, 0, 0), new NumberingItem<int>(2, 1, 0) };
        var plan = NumberingPlanner.Assign(items, "", 5, -2, 0);
        Assert.Equal(new[] { "5", "3" }, plan.Select(p => p.Value));
    }
}

public class DuctSizingFixTests
{
    [Fact]
    public void FrictionPaPerM_LuuLuongAm_NemLoi()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => DuctSizing.FrictionPaPerM(-0.1, 0.3));
        Assert.Equal(0, DuctSizing.FrictionPaPerM(0, 0.3));
    }

    [Fact]
    public void ChuNhat_KiemVanTocThat_KhongChiDe()
    {
        // 2000 L/s, cao 250 mm, vmax 6 m/s: cạnh cần ≥ 2000/6/0.25 = 1333 mm → tối thiểu 1400 nếu tỉ số cạnh cho phép,
        // nhưng 1400/250 = 5.6 > 4 nên phải báo không có cạnh chuẩn.
        var s = DuctSizing.SuggestRectangularWidth(2000, 250, 1.0, 6.0);
        Assert.Equal(0, s.SuggestedMm);

        // Cao 400 mm: cạnh 900 có De ≥ tròn nhưng v = 5.56 ≤ 6 → mọi đề xuất phải có v ≤ vmax.
        var ok = DuctSizing.SuggestRectangularWidth(2000, 400, 1.0, 6.0);
        Assert.True(ok.SuggestedMm > 0);
        Assert.True(ok.VelocityMs <= 6.0, "v = " + ok.VelocityMs);
    }

    [Fact]
    public void ChuNhat_KhongNoiLyDoTranBangTron()
    {
        // Lưu lượng vượt bảng tròn 1600: chữ nhật vẫn đề xuất được (hoặc báo riêng) — không mang câu "cần tách nhánh".
        var s = DuctSizing.SuggestRectangularWidth(30000, 2000, 1.0, 8.0);
        Assert.DoesNotContain("Vượt bảng chuẩn", s.Reason);
        Assert.DoesNotContain("dùng chữ nhật", s.Reason);
    }
}

public class GeometryValidationTests
{
    [Fact]
    public void PathFinder_ObstaclesNull_NemArgumentNull()
    {
        var bounds = new Box3(0, 0, 0, 1000, 1000, 1000);
        Assert.Throws<ArgumentNullException>(() => PathFinder3D.FindPath(new Point3(0, 0, 0), new Point3(500, 0, 0), null!, bounds));
    }

    [Fact]
    public void GridClustering_DungSaiGocAm_NemLoi()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => GridClustering.Cluster(Array.Empty<Segment2D>(), 50, -1));
    }

    [Fact]
    public void GridClustering_DoanDai0_BiBoQuaKeCaMinLength0()
    {
        var grids = GridClustering.Cluster(new[] { new Segment2D(5, 5, 5, 5), new Segment2D(0, 0, 1000, 0) }, 50, 2, minLength: 0);
        Assert.Single(grids);
        Assert.False(grids[0].IsVertical);
    }
}

public class RouteGraphPerformanceTests
{
    [Fact]
    public void Build_NhieuDoan_VanNoiDungVaNhanh()
    {
        // 3000 đoạn nối đuôi nhau — trước đây FindOrAddNode O(N²) và BFS O(E²).
        var segs = new List<RouteSegment<int>>();
        for (var i = 0; i < 3000; i++)
        {
            segs.Add(new RouteSegment<int>(i, new Point3(i * 10.0, 0, 0), new Point3((i + 1) * 10.0 + 0.0005, 0, 0)));
        }

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var g = RouteGraph<int>.Build(segs, 0.01);
        var order = g.EdgesInBuildOrder();
        sw.Stop();

        Assert.Equal(3000, g.Edges.Count);
        Assert.Equal(3001, g.Nodes.Count);
        Assert.Equal(1, g.ComponentCount);
        Assert.Equal(3000, order.Count);
        Assert.True(sw.ElapsedMilliseconds < 5000, "mất " + sw.ElapsedMilliseconds + " ms");
    }

    [Fact]
    public void Build_DiemAmVaGanBienO_VanGop()
    {
        var segs = new[]
        {
            new RouteSegment<string>("a", new Point3(-1.0, -1.0, 0), new Point3(-0.001, -0.001, 0)),
            new RouteSegment<string>("b", new Point3(0.001, 0.001, 0), new Point3(5, 5, 0)),
        };
        var g = RouteGraph<string>.Build(segs, 0.01);
        Assert.Equal(3, g.Nodes.Count);
        Assert.Equal(2, g.Edges.Count);
    }
}

public class StringGuardTests
{
    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("   ", true)]
    [InlineData("\t\n", true)]
    [InlineData("a", false)]
    [InlineData(" a ", false)]
    public void IsBlank(string? value, bool expected)
    {
        Assert.Equal(expected, StringGuard.IsBlank(value));
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("   ", false)]
    [InlineData("a", false)]
    public void IsEmpty(string? value, bool expected)
    {
        Assert.Equal(expected, StringGuard.IsEmpty(value));
    }
}
