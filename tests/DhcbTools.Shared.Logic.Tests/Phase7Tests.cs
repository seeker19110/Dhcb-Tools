using DhcbTools.Shared.Logic.Ai;
using DhcbTools.Shared.Logic.Batch;
using DhcbTools.Shared.Logic.Cad;
using DhcbTools.Shared.Logic.Checks;
using DhcbTools.Shared.Logic.Mep;
using Xunit;

namespace DhcbTools.Shared.Logic.Tests;

public class NamePatternTests
{
    private static Dictionary<string, string> V(params (string K, string Val)[] kv) => kv.ToDictionary(x => x.K, x => x.Val);

    [Fact]
    public void Token_BoDem_DinhDang()
    {
        var p = new NamePattern("A-{Level}-{n:00}") { CounterStart = 5 };
        Assert.Equal("A-L3-05", p.Apply(0, V(("Level", "L3"))));
        Assert.Equal("A-L3-06", p.Apply(1, V(("Level", "L3"))));
    }

    [Fact]
    public void DinhDangVanBan_Upper_Left()
    {
        var p = new NamePattern("{Name:upper}-{Name:left:3}");
        Assert.Equal("MAT BANG-Mat", p.Apply(0, V(("Name", "Mat bang"))));
    }

    [Fact]
    public void TimThay_Regex_VaKhongRegex()
    {
        var rx = new NamePattern("{Name}") { Find = @"^A-(\d+)$", Replace = "AR-$1" };
        Assert.Equal("AR-101", rx.Apply(0, V(("Name", "A-101"))));

        var plain = new NamePattern("{Name}") { Find = "(old)", Replace = "$new", FindIsRegex = false };
        Assert.Equal("x $new y", plain.Apply(0, V(("Name", "x (old) y"))));
    }

    [Fact]
    public void TokenLa_GiuNguyen_TienToHauTo()
    {
        var p = new NamePattern("{Khong}") { Prefix = "[", Suffix = "]" };
        Assert.Equal("[{Khong}]", p.Apply(0, null));
    }

    [Fact]
    public void ChongTrung_TrongLoVaVoiTenDaCo()
    {
        var p = new NamePattern("S-{Level}");
        var items = new IDictionary<string, string>?[] { V(("Level", "L1")), V(("Level", "L1")), V(("Level", "L2")) };
        var names = p.ApplyAll(items, new HashSet<string> { "S-L2" }, out var notes);

        Assert.Equal(new[] { "S-L1", "S-L1 (2)", "S-L2 (2)" }, names);
        Assert.Equal(2, notes.Count);
    }

    [Fact]
    public void LietKeToken()
    {
        Assert.Equal(new[] { "Level", "n" }, NamePattern.TokensIn("A-{Level}-{n:00}").ToArray());
    }
}

public class PaletteGeneratorTests
{
    [Fact]
    public void MauKeNhau_KhacXa()
    {
        for (var i = 0; i < 10; i++)
        {
            Assert.True(PaletteGenerator.Distance(PaletteGenerator.ByIndex(i), PaletteGenerator.ByIndex(i + 1)) > 60);
        }
    }

    [Fact]
    public void CungGiaTri_CungMau_KhongPhuThuocThuTu()
    {
        Assert.Equal(PaletteGenerator.ForValue("Bê tông").ToString(), PaletteGenerator.ForValue("Bê tông").ToString());
        Assert.NotEqual(PaletteGenerator.ForValue("Bê tông").ToString(), PaletteGenerator.ForValue("Thép").ToString());
    }

    [Fact]
    public void Assign_UuTienMauCoDinh_VaKhongTrungKhoa()
    {
        var map = PaletteGenerator.Assign(new[] { "A", "B", "A", null, "C" }, new Dictionary<string, string> { ["B"] = "#FF0000" });
        Assert.Equal(4, map.Count);
        Assert.Equal("#FF0000", map["B"].ToString());
        Assert.True(map.ContainsKey(string.Empty));
    }

    [Fact]
    public void Hsl_DoiDung()
    {
        Assert.Equal("#FF0000", PaletteGenerator.HslToRgb(0, 1, 0.5).ToString());
        Assert.Equal("#00FF00", PaletteGenerator.HslToRgb(120, 1, 0.5).ToString());
        Assert.Equal("#0000FF", PaletteGenerator.HslToRgb(240, 1, 0.5).ToString());
    }
}

public class ThresholdRuleTests
{
    [Fact]
    public void DocTuCungFileVoiParameterRule()
    {
        var json = """{"rules":[{"category":"Doors","parameter":"Mark","required":true},{"metric":"warnings","max":200},{"metric":"fileSizeMb","max":300,"severity":"warning"}]}""";
        var thresholds = ThresholdRule.Parse(json);
        var rules = RuleChecker.ParseRules(json);
        Assert.Equal(2, thresholds.Count);
        Assert.Equal(3, rules.Count); // ParameterRule vẫn đọc mọi phần tử — Core lọc theo có category
    }

    [Fact]
    public void ViPhamMax_Min_VaThieuSoDo()
    {
        var rules = new[] { new ThresholdRule { Metric = "warnings", Max = 10 }, new ThresholdRule { Metric = "elements", Min = 100 }, new ThresholdRule { Metric = "khongCo", Max = 1 } };
        var notes = new List<string>();
        var v = ThresholdRule.Evaluate(rules, new Dictionary<string, double> { ["warnings"] = 25, ["elements"] = 50 }, notes);
        Assert.Equal(2, v.Count);
        Assert.Contains("25.0 > ngưỡng tối đa 10.0", v[0].Reason);
        Assert.Single(notes);
    }
}

public class LayerMapTableTests
{
    private const string Csv = """
        Source,Target,Color,Linetype,Lineweight,Plottable
        WALL,A-WALL,7,Continuous,,true
        A-WALL,A-WALL,7,,,
        TUONG*,A-WALL,,,,
        ~A-*,Z-UNMAPPED,,,,
        """;

    [Fact]
    public void KhopChinhXac_Wildcard_PhuDinh()
    {
        var errors = new List<string>();
        var t = LayerMapTable.ParseCsv(Csv, errors);
        Assert.Empty(errors);
        Assert.Equal("A-WALL", t.Resolve("wall")!.Target);
        Assert.Equal("A-WALL", t.Resolve("TUONG-200")!.Target);
        Assert.Equal("Z-UNMAPPED", t.Resolve("BAT-KY")!.Target); // ~A-* = mọi layer KHÔNG bắt đầu bằng A-
        Assert.Equal("A-WALL", t.Resolve("A-WALL")!.Target);
        Assert.True(t.Resolve("WALL")!.Plottable);
    }

    [Fact]
    public void Plan_BoLayerDaDungChuan_VaLietKeUnmapped()
    {
        var t = LayerMapTable.ParseCsv("Source,Target\nWALL,A-WALL\nA-WALL,A-WALL\n", new List<string>());
        var unmapped = new List<string>();
        var plan = t.Plan(new[] { "WALL", "A-WALL", "DOOR" }, unmapped);
        Assert.Single(plan);
        Assert.Equal("A-WALL", plan["WALL"].Target);
        Assert.Equal(new[] { "DOOR" }, unmapped);
    }

    [Fact]
    public void DongThieu_GhiLoi()
    {
        var errors = new List<string>();
        var t = LayerMapTable.ParseCsv("A\n,B\nX,Y\n", errors);
        Assert.Single(t.Entries);
        Assert.Equal(2, errors.Count);
    }
}

public class DiffSummaryTests
{
    [Fact]
    public void ThemXoaDoiLayerDoiViTriDoiText()
    {
        var before = new[]
        {
            new EntitySnapshot("A1", "Line", "WALL", 0, 0),
            new EntitySnapshot("A2", "Text", "TEXT", 10, 10, "Tầng 1"),
            new EntitySnapshot("A3", "Circle", "COL", 5, 5),
        };
        var after = new[]
        {
            new EntitySnapshot("A1", "Line", "A-WALL", 0, 0),
            new EntitySnapshot("A2", "Text", "TEXT", 10, 15, "Tầng 2"),
            new EntitySnapshot("A4", "Arc", "DOOR", 1, 1),
        };

        var diff = DiffSummary.Compare(before, after);
        var counts = DiffSummary.Count(diff);

        Assert.Equal(1, counts[DiffKind.LayerChanged]);
        Assert.Equal(1, counts[DiffKind.Moved]);
        Assert.Equal(1, counts[DiffKind.TextChanged]);
        Assert.Equal(1, counts[DiffKind.Added]);
        Assert.Equal(1, counts[DiffKind.Removed]);
        Assert.Contains(diff, d => d.Kind == DiffKind.Moved && d.Detail.Contains("5.0 mm"));
    }

    [Fact]
    public void DoiDuoiDungSai_KhongBaoDoi()
    {
        var diff = DiffSummary.Compare(new[] { new EntitySnapshot("A", "Line", "L", 0, 0) }, new[] { new EntitySnapshot("A", "Line", "L", 0.5, 0) });
        Assert.Empty(diff);
    }

    [Fact]
    public void Csv_Html()
    {
        var diff = DiffSummary.Compare(Array.Empty<EntitySnapshot>(), new[] { new EntitySnapshot("H1", "Text", "L<1>", 0, 0, "x") });
        Assert.Contains("Added,H1,Text,layer L<1>", DiffSummary.ToCsv(diff));
        Assert.Contains("L&lt;1&gt;", DiffSummary.ToHtml("t", diff));
    }
}

public class RvtFileInfoTests
{
    [Fact]
    public void DocFormatUtf16()
    {
        var bytes = new byte[512].Concat(System.Text.Encoding.Unicode.GetBytes("Worksharing: Not enabled\r\nFormat: 2024\r\nBuild: 20230308")).Concat(new byte[64]).ToArray();
        Assert.Equal(2024, RvtFileInfo.DetectVersion(bytes));
    }

    [Fact]
    public void DocChuoiBuildCu()
    {
        var bytes = System.Text.Encoding.Unicode.GetBytes("Revit Build: Autodesk Revit 2018 (Build: 20170223_1515(x64))");
        Assert.Equal(2018, RvtFileInfo.DetectVersion(bytes));
    }

    [Fact]
    public void KhongNhanRa_TraNull()
    {
        Assert.Null(RvtFileInfo.DetectVersion(new byte[] { 1, 2, 3 }));
        Assert.Null(RvtFileInfo.DetectVersion(Path.Combine(Path.GetTempPath(), "khong-co-" + Guid.NewGuid() + ".rvt")));
    }
}

public class AcadPlotTests
{
    [Fact]
    public void PlotPdf_Model_VaLayout()
    {
        var model = AcadScriptGen.PlotPdf(@"D:\out\a.pdf");
        Assert.StartsWith("-PLOT\nY\nModel\nDWG To PDF.pc3\n", model);
        Assert.Contains(@"D:\out\a.pdf", model);
        Assert.EndsWith("N\nY\n", model);

        var layout = AcadScriptGen.PlotPdf(@"D:\out\b.pdf", layout: "A3-01", plotArea: "Layout");
        Assert.Contains("\nA3-01\n", layout);
        Assert.Contains("\nLayout\n", layout);
        Assert.True(layout.Split('\n').Length > model.Split('\n').Length);
    }

    [Fact]
    public void Build_ChenPlotTruocSaveAs()
    {
        var scr = AcadScriptGen.Build("p.dll", new[] { "s.json" }, @"D:\o\a.dwg", "log", "a.dwg", AcadScriptGen.PlotPdf(@"D:\o\a.pdf"));
        Assert.True(scr.IndexOf("-PLOT", StringComparison.Ordinal) < scr.IndexOf("SAVEAS", StringComparison.Ordinal));
        Assert.True(scr.IndexOf("DHCB_RUN", StringComparison.Ordinal) < scr.IndexOf("-PLOT", StringComparison.Ordinal));
    }
}

public class IntentCandidatesTests
{
    [Fact]
    public void ToiDa8UngVien_LenhKhopDungDau()
    {
        var c = CommandIntentParser.Candidates("đổi tên sheet theo tầng", CommandCatalog.Revit);
        Assert.True(c.Count <= 8);
        Assert.Equal("SheetRename", c[0].Name);
        Assert.Equal(c.Count, c.Select(x => x.Name).Distinct().Count());
    }

    [Fact]
    public void KhongKhop_VanCoUngVien()
    {
        Assert.NotEmpty(CommandIntentParser.Candidates("xyz", CommandCatalog.AutoCad, 5));
    }

    [Fact]
    public void LenhMoi_NhanDangDuoc()
    {
        Assert.Equal("ColorByParameter", CommandIntentParser.Parse("tô màu theo tham số Fire Rating", CommandCatalog.Revit).Command);
        Assert.Equal("StylePurge", CommandIntentParser.Parse("xoá view template thừa", CommandCatalog.Revit).Command);
        // LayerTranslate đã có mã nguồn (bỏ .Pending() trong CommandCatalog) nên giờ được nhận dạng bình thường.
        Assert.Equal("LayerTranslate", CommandIntentParser.Parse("laytrans đổi layer theo chuẩn", CommandCatalog.AutoCad).Command);
    }

    /// <summary>
    /// Lệnh mới chỉ có đặc tả (<c>Pending</c>) không được lớp AI đề xuất — nếu không agent sẽ gọi
    /// một lệnh không có trong Core. Toàn bộ 11 lệnh AutoCAD từng Pending nay đã có mã nguồn nên
    /// danh sách Pending của AutoCAD hiện rỗng; test giữ lại để bắt hồi quy nếu có lệnh mới bị bỏ dở.
    /// </summary>
    [Fact]
    public void LenhChuaTrienKhai_KhongDuocDeXuat()
    {
        Assert.Empty(CommandCatalog.PendingNames(CommandCatalog.AutoCad));
    }

    [Fact]
    public void SchemaChonLenh_LaJsonHopLe()
    {
        Assert.Equal("object", (string?)OllamaClient.ChoiceSchema["type"]);
        Assert.NotNull(OllamaClient.MappingSchema["properties"]!["mappings"]);
    }
}
