using System.Text.RegularExpressions;
using DhcbTools.Shared.Logic.Ai;
using DhcbTools.Shared.Logic.Batch;
using Xunit;

namespace DhcbTools.Shared.Logic.Tests;

public class LayerMappingSuggesterTests
{
    private static readonly string[] Types =
    {
        "Basic Wall: DHCB-Tuong 200", "Basic Wall: DHCB-Tuong 100", "Door: Cua don 900", "Window: Cua so 1200",
        "Structural Column: Cot BTCT 400x400", "Floor: San BTCT 150", "Round Duct: Ong gio tron", "Pipe Types: Ong cap nuoc PPR",
    };

    [Fact]
    public void MapTuong200_DungTypeVaKichThuoc()
    {
        var m = LayerMappingSuggester.Suggest(new[] { "A-WALL-200" }, Types).Single();
        Assert.Equal("Basic Wall: DHCB-Tuong 200", m.RevitType);
        Assert.True(m.Confidence >= LayerMappingSuggester.ReviewThreshold);
        Assert.False(m.NeedsReview);
    }

    [Fact]
    public void TiengVietCoDau_VaKhongDau_DeuHieu()
    {
        Assert.Equal("Door: Cua don 900", LayerMappingSuggester.Suggest(new[] { "KT-CỬA" }, Types).Single().RevitType);
        Assert.Equal("Structural Column: Cot BTCT 400x400", LayerMappingSuggester.Suggest(new[] { "KC-COT" }, Types).Single().RevitType);
    }

    [Fact]
    public void KhacLoai_KhongMapBua()
    {
        var m = LayerMappingSuggester.Suggest(new[] { "E-LTG-CEIL" }, Types).Single();
        Assert.True(m.NeedsReview);
    }

    [Fact]
    public void KhongCoDauHieu_TraNullVaCanXem()
    {
        var m = LayerMappingSuggester.Suggest(new[] { "XYZ-123-ABC" }, Types, minConfidence: 0.3).Single();
        Assert.Null(m.RevitType);
        Assert.True(m.NeedsReview);
    }

    [Fact]
    public void Validate_LoaiTypeBiaRa()
    {
        var proposed = new[]
        {
            new LayerMapping("A-WALL", "Basic Wall: DHCB-Tuong 200", 0.9, "ok"),
            new LayerMapping("A-DOOR", "Door: Khong ton tai", 0.95, "bịa"),
            new LayerMapping("A-WIN", "window: cua so 1200", 0.8, "sai hoa thường"),
        };
        var rejected = new List<string>();
        var ok = LayerMappingSuggester.Validate(proposed, Types, rejected);

        Assert.Equal(2, ok.Count);
        Assert.Single(rejected);
        Assert.Equal("Window: Cua so 1200", ok[1].RevitType); // chuẩn hoá về tên thật
    }

    [Fact]
    public void Csv_CoCotDuyet()
    {
        var csv = LayerMappingSuggester.ToCsv(new[] { new LayerMapping("A-WALL-200", "Basic Wall: DHCB-Tuong 200", 0.93, "cùng loại wall, khớp kích thước 200") });
        Assert.StartsWith("Layer,RevitType,Confidence,NeedsReview,Reason", csv);
        Assert.Contains("0.93,false", csv);
    }

    [Fact]
    public void DanhSachTypeRong_NemLoi()
    {
        Assert.Throws<ArgumentException>(() => LayerMappingSuggester.Suggest(new[] { "A" }, Array.Empty<string>()));
    }

    [Fact]
    public void ParseJsonCuaModel_KeCaBocMarkdown_VaLocTypeBia()
    {
        var text = "Đây là kết quả:\n```json\n{\"mappings\":[{\"layer\":\"A-WALL\",\"revitType\":\"Basic Wall: DHCB-Tuong 200\",\"confidence\":0.9,\"reason\":\"tường\"},{\"layer\":\"A-X\",\"revitType\":\"Bịa\",\"confidence\":0.9}]}\n```";
        var rejected = new List<string>();
        var ok = OllamaClient.ParseMappingJson(text, Types, rejected)!;
        Assert.Single(ok);
        Assert.Single(rejected);
    }

    [Fact]
    public void ParseJsonHong_TraNull()
    {
        Assert.Null(OllamaClient.ParseMappingJson("không phải json", Types, new List<string>()));
    }
}

public class SpecTextExtractorTests
{
    private const string Spec = """
        THUYẾT MINH THIẾT KẾ
        Tên dự án: Toà nhà văn phòng Landmark
        Mã số dự án: LMK-2026-01
        Cao độ các tầng:
        Tầng hầm 1: -3.300
        Tầng 1: +0.000
        Tầng 2: +4.200 m
        Tầng 3 = 7800 mm
        Tầng kỹ thuật: +45.500
        Mái +49.100
        Hệ thống: HVAC, cấp nước, thoát nước, chữa cháy sprinkler, điện nhẹ.
        Tiêu chuẩn áp dụng: TCVN 5687:2010, QCVN 06:2022/BXD, ASHRAE 62.1, NFPA 13.
        """;

    [Fact]
    public void TrichCaoDoTang_DoiVeMm_SapTheoCaoDo()
    {
        var r = SpecTextExtractor.Extract(Spec);
        var byName = r.Levels.ToDictionary(l => l.Name, l => l.ElevationMm);

        Assert.Equal(-3300, byName["Tầng hầm 1"]);
        Assert.Equal(0, byName["Tầng 1"]);
        Assert.Equal(4200, byName["Tầng 2"]);
        Assert.Equal(7800, byName["Tầng 3"]);
        Assert.Equal(45500, byName["Tầng kỹ thuật"]);
        Assert.Equal(49100, byName["Mái"]);
        Assert.True(r.Levels.Select(l => l.ElevationMm).SequenceEqual(r.Levels.Select(l => l.ElevationMm).OrderBy(x => x)));
    }

    [Fact]
    public void GiuDongGoc_DeKySuDoiChieu()
    {
        var r = SpecTextExtractor.Extract(Spec);
        Assert.Contains("Tầng 2: +4.200 m", r.Levels.Single(l => l.Name == "Tầng 2").SourceLine);
    }

    [Fact]
    public void TrichHeThong_VaTieuChuan_VaThongTinDuAn()
    {
        var r = SpecTextExtractor.Extract(Spec);
        Assert.Contains("HVAC", r.Systems);
        Assert.Contains("chữa cháy", r.Systems);
        Assert.Contains("TCVN 5687:2010", r.Standards);
        Assert.Contains("NFPA 13", r.Standards);
        Assert.Equal("Toà nhà văn phòng Landmark", r.ProjectName);
        Assert.Equal("LMK-2026-01", r.ProjectNumber);
    }

    [Fact]
    public void ConfigJson_DungSchemaLevelSetup_DryRunTrue()
    {
        var json = Newtonsoft.Json.Linq.JObject.Parse(SpecTextExtractor.Extract(Spec).ToProjectInitJson());
        Assert.True((bool)json["levelSetup"]!["dryRun"]!);
        Assert.Equal(6, json["levelSetup"]!["levels"]!.Count());
        Assert.Equal("Tầng 1", (string?)json["levelSetup"]!["levels"]![1]!["name"]);
    }

    [Fact]
    public void KhongCoTang_CanhBaoKhongDoan()
    {
        var r = SpecTextExtractor.Extract("Toà nhà 5 tầng rất đẹp.");
        Assert.Empty(r.Levels);
        Assert.Contains(r.Warnings, w => w.Contains("Không tìm thấy"));
    }

    [Fact]
    public void ChieuCaoTangBatThuong_CanhBao()
    {
        var r = SpecTextExtractor.Extract("Tầng 1: +0.000\nTầng 2: +0.500");
        Assert.Contains(r.Warnings, w => w.Contains("500 mm"));
    }

    [Theory]
    [InlineData("tầng 3", "Tầng 3")]
    [InlineData("Tang 03", "Tầng 3")]
    [InlineData("Level 12", "Tầng 12")]
    [InlineData("L5", "Tầng 5")]
    [InlineData("B2", "Tầng hầm 2")]
    [InlineData("tầng hầm 1", "Tầng hầm 1")]
    [InlineData("MÁI", "Mái")]
    public void ChuanHoaTenTang(string raw, string expected)
    {
        Assert.Equal(expected, SpecTextExtractor.NormalizeLevelName(raw));
    }
}

public class WarningAnalyzerTests
{
    [Fact]
    public void GomTheoNguyenNhan_UuTienConnectorVaClash()
    {
        var entries = new List<RunLogEntry>
        {
            new() { File = "MEP.rvt", Command = "ConnectorChecker", Success = true, Messages = { "Element 123 at (1,2,3) mm - Piping connector hở", "Element 124 connector hở" } },
            new() { File = "MEP.rvt", Command = "HealthReport", Success = true, Messages = { "12 view thừa không đặt trên sheet", "Family in-place: Abc" } },
            new() { File = "ARC.rvt", Command = "RuleCheck", Success = true, Messages = { "Doors 55: Mark thiếu giá trị" } },
            new() { File = "ARC.rvt", Command = "BatchExport", Success = false, Summary = "Không mở được file" },
            new() { File = "X.rvt", Command = "Y", Success = true, Messages = { "một dòng lạ hoắc" } },
        };

        var groups = WarningAnalyzer.Analyze(entries);

        Assert.Equal(1, groups[0].Priority);
        Assert.Contains(groups, g => g.Cause == "Connector MEP hở" && g.Count == 2);
        Assert.Contains(groups, g => g.Cause == "File không mở được");
        Assert.Contains(groups, g => g.Cause == "Tham số bắt buộc còn trống");
        Assert.Equal("Khác (chưa phân loại)", groups[^1].Cause);
        Assert.True(groups.Select(g => g.Priority).SequenceEqual(groups.Select(g => g.Priority).OrderBy(p => p)));
    }

    [Fact]
    public void TomTat_TiengViet_CoThuTu()
    {
        var groups = WarningAnalyzer.Analyze(new[] { new RunLogEntry { File = "a", Messages = { "connector hở" } } });
        var md = WarningAnalyzer.Summarize(groups, "Đêm");
        Assert.Contains("1. **Connector MEP hở**", md);
        Assert.Contains("Đề xuất:", md);
    }

    [Fact]
    public void LogRong_KhongCanhBao()
    {
        Assert.Contains("Không có cảnh báo", WarningAnalyzer.Summarize(WarningAnalyzer.Analyze(Array.Empty<RunLogEntry>()), "x"));
    }
}

public class CommandIntentParserTests
{
    [Fact]
    public void DanhSoCua_TienTo_DemSo_DryRun()
    {
        var intent = CommandIntentParser.Parse("Đánh số cửa tầng 3 tiền tố D- 3 chữ số", CommandCatalog.Revit);

        Assert.Equal("AutoNumbering", intent.Command);
        Assert.Equal("Doors", (string?)intent.Config["category"]);
        Assert.Equal("D-", (string?)intent.Config["prefix"]);
        Assert.Equal(3, (int)intent.Config["padWidth"]!);
        Assert.True((bool)intent.Config["dryRun"]!);
    }

    [Fact]
    public void XuatPdf_HieuFormats_KhongDryRunViChiDoc()
    {
        var intent = CommandIntentParser.Parse("xuất pdf và dwg toàn bộ sheet ra D:/out", CommandCatalog.Revit);
        Assert.Equal("BatchExport", intent.Command);
        var formats = intent.Config["formats"]!.Select(f => (string?)f).ToList();
        Assert.Contains("Pdf", formats);
        Assert.Contains("Dwg", formats);
        Assert.Null(intent.Config["dryRun"]);
    }

    [Fact]
    public void Hanger_TrichKhoangCachMet()
    {
        var intent = CommandIntentParser.Parse("đặt hanger cách 2.5 m cho ống", CommandCatalog.Revit);
        Assert.Equal("HangerAuto", intent.Command);
        Assert.Equal(2500, (double)intent.Config["spacingMm"]!);
    }

    [Fact]
    public void Routing_HieuLoaiPhanTu()
    {
        var intent = CommandIntentParser.Parse("dựng tuyến ống gió theo line đã vẽ", CommandCatalog.Revit);
        Assert.Equal("RouteFromLines", intent.Command);
        Assert.Equal("Duct", (string?)intent.Config["elementType"]);
    }

    [Fact]
    public void AutoCad_DonBanVe()
    {
        var intent = CommandIntentParser.Parse("purge dọn bản vẽ này", CommandCatalog.AutoCad);
        Assert.Equal("DrawingCleanup", intent.Command);
        Assert.True((bool)intent.Config["dryRun"]!);
    }

    [Fact]
    public void KhongHieu_TraNullKemDanhSachLenh()
    {
        var intent = CommandIntentParser.Parse("hôm nay trời đẹp quá", CommandCatalog.Revit);
        Assert.Null(intent.Command);
        Assert.Contains("AutoNumbering", intent.Explanation);
        Assert.True(intent.ToPayload() is not null);
    }

    [Fact]
    public void ChiTraLenhTrongWhitelist()
    {
        foreach (var text in new[] { "đánh số", "xuất pdf", "sleeve", "kiểm tra va chạm", "map layer", "tạo sheet" })
        {
            var intent = CommandIntentParser.Parse(text, CommandCatalog.Revit);
            Assert.NotNull(intent.Command);
            Assert.Contains(intent.Command!, CommandCatalog.Names(CommandCatalog.Revit));
        }
    }
}

/// <summary>
/// §2.6 đặc tả kiểm thử: mọi CommandName trong Core phải có trong CommandCatalog, và mọi lệnh trong catalog phải
/// có case trong dispatch của Bridge. Đọc thẳng mã nguồn để không phụ thuộc Revit/AutoCAD.
/// </summary>
public class CommandCatalogTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Dhcb-Tools.sln")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName ?? throw new InvalidOperationException("Không tìm thấy gốc repo.");
    }

    private static HashSet<string> CommandNamesIn(string folder)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        var rx = new Regex("CommandName\\s*=>\\s*\"([^\"]+)\"");
        foreach (var file in Directory.EnumerateFiles(folder, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) || file.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar))
            {
                continue;
            }

            foreach (Match m in rx.Matches(File.ReadAllText(file)))
            {
                names.Add(m.Groups[1].Value);
            }
        }
        return names;
    }

    [Theory]
    [InlineData("src/DhcbTools.Core", CommandCatalog.Revit)]
    [InlineData("src/DhcbTools.Core.AutoCAD", CommandCatalog.AutoCad)]
    public void MoiLenhCore_DeuCoTrongCatalog_VaNguocLai(string coreFolder, string app)
    {
        var root = RepoRoot();
        var inCode = CommandNamesIn(Path.Combine(root, coreFolder));
        // AllFor chứ không phải Names: Names() lọc bỏ lệnh nội bộ (RunTests) khỏi /tools và MCP,
        // nhưng lệnh nội bộ vẫn phải khai báo trong catalog để không trôi khỏi bảng dispatch.
        var inCatalog = new HashSet<string>(CommandCatalog.AllFor(app).Select(c => c.Name), StringComparer.Ordinal);

        Assert.True(inCode.Count > 0, "Không đọc được CommandName nào từ " + coreFolder);
        Assert.True(inCode.SetEquals(inCatalog),
            "Lệch giữa Core và CommandCatalog (" + app + ").\n  Chỉ có trong code: " + string.Join(", ", inCode.Except(inCatalog))
            + "\n  Chỉ có trong catalog: " + string.Join(", ", inCatalog.Except(inCode)));
    }

    [Theory]
    [InlineData("src/DhcbTools.Core/RevitCommandTable.cs", CommandCatalog.Revit)]
    [InlineData("src/DhcbTools.Core.AutoCAD/AcadCommandTable.cs", CommandCatalog.AutoCad)]
    public void MoiLenhTrongCatalog_DeuCoCaseDispatch(string tableFile, string app)
    {
        var text = File.ReadAllText(Path.Combine(RepoRoot(), tableFile)).ToUpperInvariant();
        var missing = CommandCatalog.For(app)
            .Where(c => !text.Contains("\"" + c.Name.ToUpperInvariant() + "\""))
            .Select(c => c.Name)
            .ToList();
        Assert.True(missing.Count == 0, "Thiếu case dispatch cho: " + string.Join(", ", missing));
    }

    [Fact]
    public void BiDanh_TraVeDungLenh()
    {
        Assert.Equal("RemoveUnusedViews", CommandCatalog.Find(CommandCatalog.Revit, "cleanup")!.Name);
        Assert.Equal("DrawingCleanup", CommandCatalog.Find(CommandCatalog.AutoCad, "cleanup")!.Name);
        Assert.Null(CommandCatalog.Find(CommandCatalog.Revit, "khong-co"));
    }

    [Fact]
    public void Describe_CoInputSchemaChoMcp()
    {
        var json = Newtonsoft.Json.Linq.JObject.FromObject(CommandCatalog.Describe(CommandCatalog.Revit));
        var tool = json["tools"]!.First(t => (string?)t["name"] == "AutoNumbering");
        Assert.NotNull(tool["inputSchema"]!["properties"]!["category"]);
        Assert.True((bool)tool["writesModel"]!);
    }
}
