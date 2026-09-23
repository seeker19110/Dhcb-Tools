using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using DhcbTools.Shared.Hosting;
using DhcbTools.Shared.Logic;
using DhcbTools.Shared.Logic.Batch;
using DhcbTools.Shared.Logic.Checks;
using DhcbTools.Shared.Logic.Evidence;
using DhcbTools.Shared.Logic.Handover;
using DhcbTools.Shared.Logic.Ids;
using DhcbTools.Shared.Logic.Progress;
using DhcbTools.Shared.Logic.Usage;
using Newtonsoft.Json.Linq;
using Xunit;

namespace DhcbTools.Shared.Logic.Tests;

/// <summary>
/// Ca chốt chặn cho các lỗi tầng thuần lộ ra ở audit 2026-09-23. Mỗi ca ghi rõ đầu vào từng làm sai.
/// </summary>
public class Audit20260923Tests
{
    // ── CsvText: CSV injection ─────────────────────────────────────────────

    [Fact]
    public void CsvText_GuardFormula_ChanCongThucExcel_GiuSoAm()
    {
        Assert.Equal("'=HYPERLINK(\"http://x\")", CsvText.GuardFormula("=HYPERLINK(\"http://x\")"));
        Assert.Equal("'+1+1", CsvText.GuardFormula("+1+1"));
        Assert.Equal("'@SUM(A1)", CsvText.GuardFormula("@SUM(A1)"));
        Assert.Equal("'\tcmd", CsvText.GuardFormula("\tcmd"));
        Assert.Equal("'-1+cmd", CsvText.GuardFormula("-1+cmd"));
        // Số âm là dữ liệu hợp lệ (toạ độ, cao độ) — không đụng.
        Assert.Equal("-12.5", CsvText.GuardFormula("-12.5"));
        Assert.Equal("-12,5", CsvText.GuardFormula("-12,5"));
        Assert.Equal("Cửa D1", CsvText.GuardFormula("Cửa D1"));
        Assert.Equal(string.Empty, CsvText.GuardFormula(null));
        Assert.Equal(string.Empty, CsvText.GuardFormula(""));
    }

    [Fact]
    public void CsvText_JoinLine_ChiBaoVeKhiBat()
    {
        var cells = new[] { "=1+1", "a,b" };
        Assert.Equal("=1+1,\"a,b\"", CsvText.JoinLine(cells));
        Assert.Equal("=1+1,\"a,b\"", CsvText.JoinLine(cells, false));
        Assert.Equal("'=1+1,\"a,b\"", CsvText.JoinLine(cells, true));
    }

    /// <summary>CSV báo cáo tiến độ mở bằng Excel: tên nhóm bắt đầu bằng "=" không được thành công thức.</summary>
    [Fact]
    public void ProgressCsv_WriteReport_ChanCongThuc()
    {
        var rows = new List<StatusRollRow> { new StatusRollRow("=HYPERLINK(\"x\")") { Total = 1 } };
        var csv = ProgressCsv.WriteReport(rows);
        Assert.Contains("'=HYPERLINK", csv);
    }

    // ── NumericText: NaN/Infinity ──────────────────────────────────────────

    [Theory]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("-Infinity")]
    [InlineData("∞")]
    public void NumericText_NaNVaVoCuc_KhongPhaiSo(string text)
    {
        Assert.False(NumericText.TryParseDouble(text, out _));
    }

    [Fact]
    public void NumericText_SoThuong_VanDoc()
    {
        Assert.True(NumericText.TryParseDouble("1.5", out var a));
        Assert.Equal(1.5, a);
        Assert.True(NumericText.TryParseDouble("-2,5", out var b));
        Assert.Equal(-2.5, b);
    }

    // ── UsageLog: số quá cỡ ───────────────────────────────────────────────

    [Fact]
    public void UsageLog_SoQuaCo_BoDong_KhongNem()
    {
        var ok = "09:14:22  " + UsageLog.Format("HealthReport", true, true, 1, 10);
        var lines = new[]
        {
            ok,
            ok.Replace("affected=1", "affected=99999999999"),
            ok.Replace("ms=10", "ms=99999999999999999999"),
        };

        var entries = UsageLog.Parse("Revit-2026-09-23.log", lines);

        Assert.Single(entries);
    }

    // ── ProgressCsv: trùng mã theo giá trị ────────────────────────────────

    [Fact]
    public void ProgressCsv_MaTrungKhacCachViet_LayDongSauCung()
    {
        var records = new[]
        {
            new[] { "ElementId", "Trạng thái" },
            new[] { "123", "Đã lắp" },
            new[] { "+123", "Đang lắp" },
            new[] { "0123", "Chưa lắp" },
        };

        var result = ProgressCsv.Read(records, ProgressCsvKey.ElementId);

        var row = Assert.Single(result.Rows);
        Assert.Equal(123, row.ElementId);
        Assert.Equal(2, result.Errors.Count);
        Assert.All(result.Errors, e => Assert.Contains("đã có ở dòng", e));
    }

    // ── JobTokens: token một ký tự ────────────────────────────────────────

    [Fact]
    public void JobTokens_TokenMotKyTu_LaSoNgayThang_KhongPhaiDinhDangChuan()
    {
        var ctx = new JobTokenContext("out", "ban-ve", new DateTime(2026, 9, 3, 7, 5, 9));
        Assert.Equal("3-ban-ve", JobTokens.Expand("{d}-{fileName}", ctx));
        Assert.Equal("9", JobTokens.Expand("{M}", ctx));
        Assert.Equal("2026-09-03", JobTokens.Expand("{yyyy-MM-dd}", ctx));
        Assert.DoesNotContain("/", JobTokens.Expand("{d}", ctx));
    }

    // ── FileNaming: tên thiết bị Windows ──────────────────────────────────

    [Theory]
    [InlineData("CON", "_CON")]
    [InlineData("nul", "_nul")]
    [InlineData("NUL.pdf", "_NUL.pdf")]
    [InlineData("COM1", "_COM1")]
    [InlineData("LPT9.dwg", "_LPT9.dwg")]
    [InlineData("CONSOLE", "CONSOLE")]
    [InlineData("A-101", "A-101")]
    public void FileNaming_TenThietBi_ThemGachDuoi(string input, string expected)
    {
        Assert.Equal(expected, FileNaming.Sanitize(input));
    }

    // ── IdsReport: scopeNote lạ phải escape ───────────────────────────────

    [Fact]
    public void IdsReport_ScopeNoteLa_Escape_HangSanCoDuocChenTho()
    {
        var check = IdsEvaluator.Check(Array.Empty<IdsSpecification>(), Array.Empty<IIdsElement>());
        var html = IdsReport.Html("m", "a.ids", "</p><script>alert(1)</script>", check, Array.Empty<string>());
        Assert.DoesNotContain("<script>", html);
        Assert.Contains("&lt;script&gt;", html);

        var trusted = IdsReport.Html("m", "a.ids", IdsReport.RevitScopeNote, check, Array.Empty<string>());
        Assert.Contains("<b>trên mô hình Revit</b>", trusted);
    }

    // ── IdsEvaluator: optional có nhưng sai ───────────────────────────────

    private const string IdsHeader = "<ids xmlns=\"http://standards.buildingsmart.org/IDS\"><specifications>";
    private const string IdsFooter = "</specifications></ids>";

    [Fact]
    public void IdsEvaluator_Optional_CoNhungSaiGiaTri_Truot_KhongCoThiDat()
    {
        var specs = IdsSpec.Parse(IdsHeader
            + "<specification name=\"FireRating\" ifcVersion=\"IFC4\"><applicability><entity><name><simpleValue>IFCWALL</simpleValue></name></entity></applicability>"
            + "<requirements><property cardinality=\"optional\"><propertySet><simpleValue>Pset_WallCommon</simpleValue></propertySet><baseName><simpleValue>FireRating</simpleValue></baseName>"
            + "<value><xs:restriction xmlns:xs=\"http://www.w3.org/2001/XMLSchema\" base=\"xs:string\"><xs:enumeration value=\"EI60\"/><xs:enumeration value=\"EI90\"/></xs:restriction></value></property></requirements></specification>"
            + IdsFooter);

        var khongCo = new FakeIdsElement { IfcEntity = "IfcWall", Label = "1" };
        var dung = new FakeIdsElement { IfcEntity = "IfcWall", Label = "2" };
        dung.Properties["Pset_WallCommon.FireRating"] = "EI60";
        var sai = new FakeIdsElement { IfcEntity = "IfcWall", Label = "3" };
        sai.Properties["Pset_WallCommon.FireRating"] = "banana";

        var result = IdsEvaluator.Check(specs, new IIdsElement[] { khongCo, dung, sai });

        var spec = Assert.Single(result.Specifications);
        Assert.Equal(2, spec.Passed);
        Assert.Equal(1, spec.Failed);
        var failure = Assert.Single(spec.Failures);
        Assert.Equal("3", failure.Element);
        Assert.Contains("có nhưng sai giá trị", failure.Reason);
    }

    [Fact]
    public void IdsEvaluator_Optional_MoiLoaiFacet_CoMaSaiDeuTruot()
    {
        var specs = IdsSpec.Parse(IdsHeader
            + "<specification name=\"all\" ifcVersion=\"IFC4\"><applicability><entity><name><simpleValue>IFCDOOR</simpleValue></name></entity></applicability><requirements>"
            + "<attribute cardinality=\"optional\"><name><simpleValue>Description</simpleValue></name><value><simpleValue>ok</simpleValue></value></attribute>"
            + "<classification cardinality=\"optional\"><value><simpleValue>Ss_25</simpleValue></value></classification>"
            + "<material cardinality=\"optional\"><value><simpleValue>Gỗ</simpleValue></value></material>"
            + "<partOf cardinality=\"optional\"><entity><name><simpleValue>IFCBUILDINGSTOREY</simpleValue></name></entity></partOf>"
            + "</requirements></specification>" + IdsFooter);

        var trong = new FakeIdsElement { Label = "trống" };
        var sai = new FakeIdsElement { Label = "sai" };
        sai.Attributes["Description"] = "khác";
        sai.ClassificationCodes.Add("Ss_99");
        sai.MaterialNames.Add("Thép");
        sai.Parents.Add(("", "IfcSite"));

        var result = IdsEvaluator.Check(specs, new IIdsElement[] { trong, sai });

        var spec = Assert.Single(result.Specifications);
        Assert.Equal(1, spec.Passed);
        var failure = Assert.Single(spec.Failures);
        Assert.Equal(4, failure.Reason.Split("có nhưng sai giá trị").Length - 1);
    }

    /// <summary>Facet entity không có khái niệm "có mặt": optional với entity thì bỏ qua như trước (không ném, không trượt).</summary>
    [Fact]
    public void IdsEvaluator_Optional_EntityFacet_BoQua()
    {
        var specs = IdsSpec.Parse(IdsHeader
            + "<specification name=\"e\" ifcVersion=\"IFC4\"><applicability><entity><name><simpleValue>IFCDOOR</simpleValue></name></entity></applicability>"
            + "<requirements><entity cardinality=\"optional\"><name><simpleValue>IFCWINDOW</simpleValue></name></entity></requirements></specification>" + IdsFooter);

        var result = IdsEvaluator.Check(specs, new IIdsElement[] { new FakeIdsElement() });

        Assert.Equal(1, Assert.Single(result.Specifications).Passed);
    }

    // ── HashChain: cắt đuôi ───────────────────────────────────────────────

    private static List<string> Chain(params string[] bodies)
    {
        var lines = new List<string>();
        var prev = HashChain.Genesis;
        foreach (var body in bodies)
        {
            var payload = "{\"m\":\"" + body + "\",\"prevHash\":\"" + prev + "\"}";
            var hash = HashChain.ComputeHash(payload);
            lines.Add(HashChain.Seal(payload, hash));
            prev = hash;
        }

        return lines;
    }

    private static string? PrevOf(string line)
    {
        const string key = "\"prevHash\":\"";
        var at = line.IndexOf(key, StringComparison.Ordinal);
        return at < 0 ? null : line.Substring(at + key.Length, HashChain.HashLength);
    }

    [Fact]
    public void HashChain_CatDuoi_KhongCoMoc_VanIntact_CoMoc_BaoTruncated()
    {
        var lines = Chain("a", "b", "c");
        Assert.True(HashChain.TrySplit(lines[2], out _, out var last));
        Assert.True(HashChain.TrySplit(lines[1], out _, out var secondHash));

        var cut = lines.Take(2).ToList();
        // Không có mốc: chuỗi tự nó không biết đã mất dòng — đây là giới hạn phải nói rõ.
        Assert.Equal(ChainStatus.Intact, HashChain.Verify(cut, PrevOf).Status);

        var truncated = HashChain.Verify(cut, PrevOf, last);
        Assert.Equal(ChainStatus.Truncated, truncated.Status);
        Assert.Equal(2, truncated.CheckedLines);
        Assert.Equal(2, truncated.ProblemLine);
        Assert.Contains("cắt đuôi", truncated.Message);

        Assert.Equal(ChainStatus.Intact, HashChain.Verify(lines, PrevOf, last.ToUpperInvariant()).Status);
        Assert.Equal(ChainStatus.Intact, HashChain.Verify(cut, PrevOf, secondHash).Status);
        Assert.Equal(ChainStatus.Intact, HashChain.Verify(lines, PrevOf, "  ").Status);

        var empty = HashChain.Verify(new List<string>(), PrevOf, last);
        Assert.Equal(ChainStatus.Truncated, empty.Status);
        Assert.Null(empty.ProblemLine);
    }

    // ── HandoverPackage: băm trong HTML phải escape ───────────────────────

    [Fact]
    public void HandoverPackage_Html_BamVaKindDuocEscape()
    {
        var input = new HandoverInput { ProjectName = "P", OutputFolder = Path.GetTempPath() };
        input.Files.Add(new HandoverFile("a.ifc", "<b>IFC</b>", 1, "</code><script>alert(1)</script>"));

        var html = HandoverPackage.Html(input);

        Assert.DoesNotContain("<script>", html);
        Assert.Contains("&lt;script&gt;", html);
        Assert.DoesNotContain("<b>IFC</b>", html);
    }

    // ── ClashReport: toạ độ theo culture bất biến ─────────────────────────

    [Fact]
    public void ClashReport_ToaDo_KhongTheoCultureMay()
    {
        var clashes = new[] { new ClashRecord(1, "Duct", "a", 2, "Pipe", -1234.6, 5.4, 0, "k", null) };
        var html = ClashReport.Html("m", new[] { "Ducts" }, new[] { "Pipes" }, clashes, 2);
        Assert.Contains("<td>-1235</td><td>5</td><td>0</td>", html);
    }

    // ── BridgeTokenStore ──────────────────────────────────────────────────

    [Fact]
    public void BridgeTokenStore_MinTokenLength_La32()
    {
        Assert.Equal(32, BridgeTokenStore.MinTokenLength);
    }
}

/// <summary>Bridge nền: vỏ ném SAU await đầu tiên (task faulted, chưa hoàn tất Completion) — job phải thành error, không treo.</summary>
public class HttpBridgeServerFaultedAfterAwaitTests : IDisposable
{
    private const string Token = "token-test-du-dai-32-ky-tu-tro-len-nhe";
    private readonly string _tokenPath = Path.Combine(Path.GetTempPath(), "dhcb-http-fault-" + Guid.NewGuid().ToString("N") + ".txt");
    private readonly HttpBridgeServer _server;
    private readonly HttpClient _client;

    public HttpBridgeServerFaultedAfterAwaitTests()
    {
        File.WriteAllText(_tokenPath, Token);
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        _server = new HttpBridgeServer(port, "TestBridge", "9.9") { Timeout = TimeSpan.FromSeconds(5) };
        _server.Start(_tokenPath);
        _client = new HttpClient { BaseAddress = new Uri("http://127.0.0.1:" + port) };
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token);
    }

    public void Dispose()
    {
        _client.Dispose();
        _server.Dispose();
        try { File.Delete(_tokenPath); } catch { /* dọn dẹp */ }
    }

    [Fact]
    public async Task ExecuteAsync_NemSauAwait_JobGhiError_KhongTreoRunning()
    {
        _server.ExecuteAsync = async item =>
        {
            item.TryClaim();
            await Task.Delay(20);
            throw new InvalidOperationException("hỏng sau await");
        };

        var response = await _client.PostAsync("/execute", new StringContent("{\"command\":\"KiemTra\",\"async\":true}", Encoding.UTF8, "application/json"));
        var body = JObject.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        JObject? progress = null;
        for (var i = 0; i < 50; i++)
        {
            await Task.Delay(50);
            progress = JObject.Parse(await _client.GetStringAsync("/progress/" + (string?)body["id"]));
            if ((string?)progress["status"] != "running")
            {
                break;
            }
        }

        Assert.Equal("error", (string?)progress!["status"]);
        Assert.Contains("hỏng sau await", (string?)progress["error"]);
    }
}
