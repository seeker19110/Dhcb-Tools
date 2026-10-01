using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using DhcbTools.Shared.Hosting;
using DhcbTools.Shared.Logic.Ai;
using DhcbTools.Shared.Logic.Batch;
using DhcbTools.Shared.Logic.Ifc;
using Xunit;

namespace DhcbTools.Shared.Logic.Tests;

/// <summary>
/// Ca chốt chặn cho các lỗi lộ ra ở audit toàn diện 2026-10-01 (<c>docs/audit-toan-dien-2026-10-01.md</c>).
/// Mỗi ca ghi rõ đầu vào từng làm sai.
/// </summary>
[Collection(EnvironmentCollection.Name)]
public class Audit20261001Tests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "dhcb-audit-1001-" + Guid.NewGuid().ToString("N"));

    public Audit20261001Tests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* thư mục tạm */ }
    }

    // ── Bridge: body JSON luôn đọc bằng UTF-8 khi client không khai charset ─────

    /// <summary>
    /// Thiếu charset: bản cũ dùng <c>HttpListenerRequest.ContentEncoding</c> = <c>Encoding.Default</c>, tức code
    /// page ANSI trên .NET Framework (Revit/AutoCAD ≤ 2024) — "Mã hiệu" thành "MÃ£ hiá»‡u".
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("application/json")]
    [InlineData("application/json; charset=")]
    [InlineData("application/json; charset=\"UTF-8\"")]
    [InlineData("application/json; charset=khong-co-bang-ma-nay")]
    public void BodyEncoding_ThieuHoacSaiCharset_LaUtf8(string? contentType)
    {
        Assert.Equal("utf-8", HttpBridgeServer.BodyEncoding(contentType).WebName);
    }

    [Fact]
    public void BodyEncoding_CharsetKhaiRo_DungBangMaDo()
    {
        Assert.Equal("utf-16", HttpBridgeServer.BodyEncoding("application/json; charset=utf-16").WebName);
        Assert.Equal("utf-16", HttpBridgeServer.BodyEncoding("application/json;Charset='UTF-16'; foo=bar").WebName);
    }

    /// <summary>Đường HTTP thật: UTF-8 thô (không escape \u, không charset) tới tay lệnh nguyên vẹn.</summary>
    [Fact]
    public async Task Chat_Utf8ThoKhongCharset_TextToiNguyenVen()
    {
        const string token = "token-audit-1001-du-dai-32-ky-tu-tro-len";
        var tokenPath = Path.Combine(_dir, "token.txt");
        File.WriteAllText(tokenPath, token);
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();

        string? received = null;
        using var server = new HttpBridgeServer(port, "TestBridge", "1")
        {
            Chat = text => { received = text; return new { ok = true }; },
        };
        server.Start(tokenPath);
        using var client = new HttpClient(new HttpClientHandler { UseProxy = false });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var content = new ByteArrayContent(Encoding.UTF8.GetBytes("{\"text\":\"Đánh số cửa tầng 3 — Mã hiệu\"}"));
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        var response = await client.PostAsync("http://127.0.0.1:" + port + "/chat", content);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Đánh số cửa tầng 3 — Mã hiệu", received);
    }

    // ── IFC: ngoặc lồng sâu không được làm tràn stack ───────────────────────────

    private static string IfcWithNesting(int innerLists) =>
        "ISO-10303-21;\nHEADER;\nFILE_SCHEMA(('IFC4'));\nENDSEC;\nDATA;\n#1=IFCWALL('0aaaaaaaaaaaaaaaaaaaaa',"
        + new string('(', innerLists) + new string(')', innerLists) + ");\nENDSEC;\nEND-ISO-10303-21;\n";

    /// <summary>
    /// 100.000 dấu "(" lồng nhau: bản cũ đệ quy tới tràn stack — StackOverflowException không bắt được, BatchRunner
    /// chết với mã 0xC00000FD (Linux 134) thay vì báo "không đọc được file" (mã thoát 1/2).
    /// </summary>
    [Fact]
    public void IfcStepParser_LongQuaSau_BaoLoiDocChuKhongTranStack()
    {
        var ex = Assert.Throws<IfcParseException>(() => IfcStepParser.Parse(IfcWithNesting(100_000)));

        Assert.Contains(IfcStepParser.MaxNesting.ToString(), ex.Message);
        Assert.Contains("lồng sâu", ex.Message);
    }

    /// <summary>Đúng trần (danh sách tham số của thực thể là tầng 1) vẫn đọc được; quá một tầng thì không.</summary>
    [Fact]
    public void IfcStepParser_DungTranDoLong_VanDocDuoc()
    {
        var file = IfcStepParser.Parse(IfcWithNesting(IfcStepParser.MaxNesting - 1));
        Assert.Single(file.Data);

        Assert.Throws<IfcParseException>(() => IfcStepParser.Parse(IfcWithNesting(IfcStepParser.MaxNesting)));
    }

    /// <summary>Qua IfcChecker (đường --verify-ifc và gói bàn giao): thành một mục "không đọc được", không sập.</summary>
    [Fact]
    public void IfcChecker_FileLongSau_LaKetQuaKhongDocDuoc()
    {
        var result = IfcChecker.Check(IfcWithNesting(50_000), new IfcCheckSpec());

        Assert.Contains(result.Findings, f => f.Message.StartsWith("Không đọc được file", StringComparison.Ordinal));
    }

    // ── AI offline: request tới Ollama không qua proxy, không theo chuyển hướng ──

    /// <summary>
    /// .NET 8/10 trên Windows không tự bỏ qua 127.0.0.1 với proxy cấu hình tay (thiếu &lt;local&gt;): prompt chứa
    /// dữ liệu mô hình đi ra proxy công ty. Redirect 307/308 thì POST lại nguyên prompt tới đích mới.
    /// </summary>
    [Fact]
    public void Ollama_CreateRequest_KhongProxy_KhongTheoRedirect()
    {
        var request = OllamaClient.CreateRequest("http://127.0.0.1:11434/api/generate", 30);

        Assert.Null(request.Proxy);
        Assert.False(request.AllowAutoRedirect);
        Assert.Equal("POST", request.Method);
        Assert.Equal(30_000, request.Timeout);
    }

    // ── BatchRunner: mã thoát tính cả đường chạy, không chỉ log ─────────────────

    private static RunLogEntry Ok(string file) => new RunLogEntry { File = file, Command = "HealthReport", Success = true };

    /// <summary>
    /// Revit sập sau 3/10 file: log chỉ có 3 dòng xanh, runner trả 1. Bản cũ tính từ log → mã 0, Task Scheduler
    /// không cảnh báo một đêm mới làm được một phần ba.
    /// </summary>
    [Fact]
    public void RunLogExitCode_RunnerBaoLoi_LogToanXanh_VanLa1()
    {
        var entries = new List<RunLogEntry> { Ok("a.rvt"), Ok("b.rvt"), Ok("c.rvt") };

        Assert.Equal(1, RunLog.ExitCode(entries, launchCode: 1));
        Assert.Equal(2, RunLog.ExitCode(entries, launchCode: 2));
        Assert.Equal(0, RunLog.ExitCode(entries, launchCode: 0));
        Assert.Equal(1, RunLog.ExitCode(entries, launchCode: -1));
    }

    [Fact]
    public void RunLogExitCode_LogRongHoacCoLoi_La1()
    {
        Assert.Equal(1, RunLog.ExitCode(new List<RunLogEntry>(), launchCode: 0));
        Assert.Equal(1, RunLog.ExitCode(new List<RunLogEntry> { Ok("a.dwg"), new RunLogEntry { Command = "X", Success = false } }, launchCode: 0));
    }

    // ── AutoCAD batch: saveOnError qua file tạm ─────────────────────────────────

    [Fact]
    public void StagedSave_StagingPath_CanhFileDich_DauThoiGianBatBienVanHoa()
    {
        var target = Path.Combine(_dir, "ban-ve.dwg");

        var staging = StagedSave.StagingPath(target, new DateTime(2026, 10, 1, 23, 5, 9));

        Assert.Equal(Path.Combine(_dir, "ban-ve.dhcb-luu-20261001-230509.dwg"), staging);
        Assert.Throws<ArgumentException>(() => StagedSave.StagingPath(" ", DateTime.Now));
    }

    [Fact]
    public void StagedSave_Blocker_MoiLyDoKhongLuu()
    {
        var ok = new List<RunLogEntry> { Ok("a.dwg"), Ok("a.dwg") };

        Assert.Null(StagedSave.Blocker(false, 0, false, 2, ok));
        Assert.Equal("accoreconsole quá giờ.", StagedSave.Blocker(true, -1, false, 2, ok));
        Assert.Equal("accoreconsole thoát mã 3.", StagedSave.Blocker(false, 3, false, 2, ok));
        Assert.StartsWith("NETLOAD thất bại", StagedSave.Blocker(false, 0, true, 2, new List<RunLogEntry>()));
        Assert.Equal("chỉ 2/3 step ghi kết quả vào log.", StagedSave.Blocker(false, 0, false, 3, ok));

        var withError = new List<RunLogEntry> { Ok("a.dwg"), new RunLogEntry { Command = "TextReplace", Success = false, Summary = "regex sai" } };
        Assert.Equal("step TextReplace lỗi: regex sai", StagedSave.Blocker(false, 0, false, 2, withError));

        Assert.Throws<ArgumentNullException>(() => StagedSave.Blocker(false, 0, false, 0, null!));
    }

    [Fact]
    public void StagedSave_Promote_DichChuaCo_DoiTen()
    {
        var staging = Path.Combine(_dir, "a.dhcb-luu-1.dwg");
        var target = Path.Combine(_dir, "out", "a.dwg");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.WriteAllText(staging, "moi");

        StagedSave.Promote(staging, target, keepBackup: false);

        Assert.Equal("moi", File.ReadAllText(target));
        Assert.False(File.Exists(staging));
    }

    /// <summary>Ghi đè file gốc (saveMode Save): bản cũ phải còn ở .bak như SAVEAS của AutoCAD vẫn để lại.</summary>
    [Fact]
    public void StagedSave_Promote_GhiDeGoc_GiuBak_ThayBakCu()
    {
        var staging = Path.Combine(_dir, "a.dhcb-luu-1.dwg");
        var target = Path.Combine(_dir, "a.dwg");
        var backup = Path.Combine(_dir, "a.bak");
        File.WriteAllText(staging, "moi");
        File.WriteAllText(target, "cu");
        File.WriteAllText(backup, "rat-cu");

        StagedSave.Promote(staging, target, keepBackup: true);

        Assert.Equal("moi", File.ReadAllText(target));
        Assert.Equal("cu", File.ReadAllText(backup));
        Assert.False(File.Exists(staging));
    }

    [Fact]
    public void StagedSave_Promote_KhongGiuBak_ThayThang()
    {
        var staging = Path.Combine(_dir, "b.dhcb-luu-1.dwg");
        var target = Path.Combine(_dir, "b.dwg");
        File.WriteAllText(staging, "moi");
        File.WriteAllText(target, "cu");

        StagedSave.Promote(staging, target, keepBackup: false);

        Assert.Equal("moi", File.ReadAllText(target));
        Assert.False(File.Exists(Path.Combine(_dir, "b.bak")));
    }

    [Fact]
    public void StagedSave_Promote_ThieuFileTam_NemFileNotFound()
    {
        Assert.Throws<FileNotFoundException>(() =>
            StagedSave.Promote(Path.Combine(_dir, "khong-co.dwg"), Path.Combine(_dir, "c.dwg"), keepBackup: true));
    }

    // ── RemoveUnusedViews: không xoá view chính kéo theo view phụ thuộc đang trên sheet ──

    /// <summary>
    /// Mặt bằng chia vùng: "Tầng 1" (view chính) không nằm trên sheet, "Tầng 1 - Vùng A" (phụ thuộc) thì có.
    /// Bản cũ xoá "Tầng 1" và Revit xoá luôn "Vùng A" khỏi sheet.
    /// </summary>
    [Fact]
    public void ViewCleanup_ViewChinhCoViewPhuThuocTrenSheet_Giu()
    {
        var views = new[]
        {
            new ViewCleanupItem(1, "Tầng 1", isPlaced: false, dependentIds: new long[] { 2, 3 }),
            new ViewCleanupItem(2, "Tầng 1 - Vùng A", isPlaced: true),
            new ViewCleanupItem(3, "Tầng 1 - Vùng B", isPlaced: false),
            new ViewCleanupItem(4, "Nháp", isPlaced: false),
        };

        var plan = ViewCleanupPlanner.Plan(views, null);

        Assert.Equal(new long[] { 3, 4 }, plan.Delete.Select(v => v.Id));
        var kept = Assert.Single(plan.Kept);
        Assert.Equal(1, kept.Key.Id);
        Assert.Contains("\"Tầng 1 - Vùng A\" đang nằm trên sheet", kept.Value);
    }

    [Fact]
    public void ViewCleanup_MoiViewPhuThuocDeuThua_XoaCaViewChinh()
    {
        var views = new[]
        {
            new ViewCleanupItem(1, "Tầng 2", false, new long[] { 2 }),
            new ViewCleanupItem(2, "Tầng 2 - Vùng A", false),
        };

        var plan = ViewCleanupPlanner.Plan(views, new[] { "" });

        Assert.Equal(new long[] { 1, 2 }, plan.Delete.Select(v => v.Id));
        Assert.Empty(plan.Kept);
    }

    /// <summary>View phụ thuộc được giữ theo tên (keepViewNameContains) cũng giữ luôn view chính của nó.</summary>
    [Fact]
    public void ViewCleanup_ViewPhuThuocGiuTheoTen_GiuViewChinh()
    {
        var views = new[]
        {
            new ViewCleanupItem(1, "Tầng 3", false, new long[] { 2 }),
            new ViewCleanupItem(2, "Tầng 3 - GIỮ", false),
        };

        var plan = ViewCleanupPlanner.Plan(views, new[] { "giữ" });

        Assert.Empty(plan.Delete);
        Assert.Contains("\"Tầng 3 - GIỮ\" được giữ", Assert.Single(plan.Kept).Value);
    }

    /// <summary>View phụ thuộc không có trong danh sách (loại view không thuộc diện dọn): không biết thì không xoá.</summary>
    [Fact]
    public void ViewCleanup_ViewPhuThuocNgoaiDanhSach_Giu()
    {
        var plan = ViewCleanupPlanner.Plan(new[] { new ViewCleanupItem(1, "Tầng 4", false, new long[] { 99 }) }, null);

        Assert.Empty(plan.Delete);
        Assert.Contains("#99", Assert.Single(plan.Kept).Value);
    }

    /// <summary>Lý do do vỏ Revit đưa vào (view đang mở, xoá kéo theo khung nhìn trên sheet) được giữ nguyên văn.</summary>
    [Fact]
    public void ViewCleanup_LyDoTuVo_DuocGiuNguyen()
    {
        var views = new[]
        {
            new ViewCleanupItem(1, "{3D}", false, keepReason: "đang mở trong Revit"),
            new ViewCleanupItem(2, "Mặt cắt", true, keepReason: "không dùng tới vì đã trên sheet"),
            new ViewCleanupItem(3, "3D tạm", false, Array.Empty<long>()),
        };

        var plan = ViewCleanupPlanner.Plan(views, Array.Empty<string>());

        Assert.Equal(3, Assert.Single(plan.Delete).Id);
        var kept = Assert.Single(plan.Kept);
        Assert.Equal("{3D}", kept.Key.Name);
        Assert.Equal("đang mở trong Revit", kept.Value);
    }

    [Fact]
    public void ViewCleanup_ThieuDanhSach_NemArgumentNull()
    {
        Assert.Throws<ArgumentNullException>(() => ViewCleanupPlanner.Plan(null!, null));
        Assert.Equal(string.Empty, new ViewCleanupItem(5, null!, false).Name);
    }
}
