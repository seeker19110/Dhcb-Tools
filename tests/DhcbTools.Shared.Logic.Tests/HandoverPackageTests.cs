using System;
using System.IO;
using System.Linq;
using System.Text;
using DhcbTools.Shared.Logic.Batch;
using DhcbTools.Shared.Logic.Handover;
using Newtonsoft.Json.Linq;
using Xunit;

namespace DhcbTools.Shared.Logic.Tests;

/// <summary>
/// Mục 11.3: gói bàn giao là tờ giấy chủ đầu tư ký (Điều 11 NĐ 207/2026), nên băm từng file, danh mục
/// bản vẽ và kết quả kiểm chuỗi băm phải đúng — sai một chỗ là tờ giấy không nối được với file điện tử.
/// </summary>
public class HandoverPackageTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "dhcb-handover-" + Guid.NewGuid().ToString("N"));

    public HandoverPackageTests()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "pdf"));
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    private static readonly SheetIndexRow[] Rows =
    {
        new SheetIndexRow("A-101", "Mặt bằng tầng 1", "1 — Phát hành thi công", "2026-09-01", "01/09/2026", "NV", "TK", 3),
        new SheetIndexRow("A-102", "Mặt bằng, \"tầng 2\"\ncó xuống dòng", string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, 0),
    };

    [Fact]
    public void DanhMucBanVe_CsvVongTron_GiuNhayVaXuongDong()
    {
        var csv = SheetIndexRow.ToCsv(Rows);
        Assert.StartsWith(CsvText.JoinLine(SheetIndexRow.CsvHeader), csv);

        var back = SheetIndexRow.FromCsv("\uFEFF" + csv);
        Assert.Equal(2, back.Count);
        Assert.Equal("A-101", back[0].Number);
        Assert.Equal("1 — Phát hành thi công", back[0].Revision);
        Assert.Equal(3, back[0].ViewCount);
        Assert.Equal(Rows[1].Name, back[1].Name);
        Assert.Equal(0, back[1].ViewCount);

        // CSV khác (tiêu đề khác) không bị nhận nhầm là danh mục.
        Assert.Empty(SheetIndexRow.FromCsv("Key,Name\nlevel,Level\n"));
        Assert.Empty(SheetIndexRow.FromCsv(string.Empty));
        // Dòng thiếu cột bị bỏ, không ném.
        Assert.Single(SheetIndexRow.FromCsv(CsvText.JoinLine(SheetIndexRow.CsvHeader) + "\r\nA-1,x\r\n" + CsvText.JoinLine(new[] { "A-2", "y", "", "", "", "", "", "2" }) + "\r\n"));
    }

    [Fact]
    public void DanhMucBanVe_Html_CoDuDong()
    {
        var html = SheetIndexRow.ToHtml("Model A", Rows);
        Assert.Contains("Model A", html);
        Assert.Contains("2 sheet", html);
        Assert.Contains("A-101", html);
        Assert.Contains("&quot;tầng 2&quot;", html);
    }

    [Fact]
    public void Collect_BamMoiFileSanPham_BoRvtVaChinhGoi_DocDanhMuc()
    {
        File.WriteAllText(Path.Combine(_dir, "toa-a.ifc"), "ISO-10303-21;");
        File.WriteAllBytes(Path.Combine(_dir, "pdf", "A-101.pdf"), new byte[] { 1, 2, 3 });
        File.WriteAllText(Path.Combine(_dir, "toa-a.rvt"), "khong bam");
        File.WriteAllText(Path.Combine(_dir, "khac.csv"), "Key,Name\nlevel,Level\n");
        File.WriteAllText(Path.Combine(_dir, "danh-muc.csv"), SheetIndexRow.ToCsv(Rows), CsvText.Utf8WithBom);
        File.WriteAllText(Path.Combine(_dir, HandoverPackage.HtmlName), "cu");
        File.WriteAllText(Path.Combine(_dir, HandoverPackage.JsonName), "{}");

        var input = new HandoverInput { OutputFolder = _dir };
        HandoverPackage.Collect(input);

        var paths = input.Files.Select(f => f.RelativePath).ToList();
        Assert.Contains("toa-a.ifc", paths);
        Assert.Contains("pdf/A-101.pdf", paths);
        Assert.Contains("danh-muc.csv", paths);
        Assert.DoesNotContain("toa-a.rvt", paths);
        Assert.DoesNotContain(HandoverPackage.HtmlName, paths);
        Assert.DoesNotContain(HandoverPackage.JsonName, paths);

        var pdf = input.Files.Single(f => f.RelativePath == "pdf/A-101.pdf");
        Assert.Equal("PDF", pdf.Kind);
        Assert.Equal(3, pdf.SizeBytes);
        Assert.Equal("039058c6f2c0cb492c533b0a4d14ef77cc0f78abccced5287d84a1a2011cfb81", pdf.Sha256);
        Assert.Equal(2, input.Sheets.Count);

        // Thư mục không tồn tại: không ném, không file.
        var none = new HandoverInput { OutputFolder = Path.Combine(_dir, "khong-co") };
        HandoverPackage.Collect(none);
        Assert.Empty(none.Files);
    }

    [Fact]
    public void CheckRunLog_LogNguyenVen_LogBiSua_VaKhongCoLog()
    {
        var log = Path.Combine(_dir, "run-000001.jsonl");
        RunLog.Append(log, new RunLogEntry { File = "a.rvt", Command = "HealthReport", Success = true, Summary = "ok" });
        RunLog.Append(log, new RunLogEntry { File = "a.rvt", Command = "BatchExport", Success = false, Summary = "loi" });

        var ok = new HandoverInput { RunLogPath = log };
        HandoverPackage.CheckRunLog(ok);
        Assert.True(Assert.Single(ok.Checks).Ok);
        Assert.Contains("run-000001.jsonl", ok.Checks[0].Detail);

        var lines = File.ReadAllLines(log);
        lines[0] = lines[0].Replace("\"ok\"", "\"da sua\"");
        File.WriteAllLines(log, lines);
        var tampered = new HandoverInput { RunLogPath = log };
        HandoverPackage.CheckRunLog(tampered);
        Assert.False(Assert.Single(tampered.Checks).Ok);

        var missing = new HandoverInput { RunLogPath = Path.Combine(_dir, "khong-co.jsonl") };
        HandoverPackage.CheckRunLog(missing);
        Assert.False(Assert.Single(missing.Checks).Ok);
        var empty = new HandoverInput();
        HandoverPackage.CheckRunLog(empty);
        Assert.False(Assert.Single(empty.Checks).Ok);
    }

    [Fact]
    public void Html_VaJson_CungNoiMotDieu_CoOXacNhan()
    {
        var input = new HandoverInput
        {
            JobName = "Đêm A",
            ProjectName = "TTTM Goldview",
            Owner = "Công ty CĐT",
            Contractor = "DHCB",
            GeneratedAt = new DateTime(2026, 9, 6, 1, 2, 3),
            AddinVersion = "1.1.0",
            OutputFolder = _dir,
            RunLogPath = "x.jsonl",
        };
        input.Entries.Add(new RunLogEntry { File = "C:/x/a.rvt", Command = "BatchExport", Success = true, Summary = "3 file" });
        input.Entries.Add(new RunLogEntry { File = "C:/x/a.rvt", Command = "SleeveAuto", Skipped = true });
        input.Entries.Add(new RunLogEntry { File = "C:/x/a.rvt", Command = "IdsValidate", Success = false, Summary = "loi <b>" });
        input.Files.Add(new HandoverFile("a.ifc", "IFC", 2 * 1024 * 1024, "abc"));
        input.Files.Add(new HandoverFile("b.pdf", "PDF", 2048, "def"));
        input.Files.Add(new HandoverFile("c.csv", "CSV", 12, "ghi"));
        input.Sheets.AddRange(Rows);
        input.Checks.Add(new HandoverCheck("Chuỗi băm", true, "8 dòng"));
        input.Checks.Add(new HandoverCheck("IFC", false, "thiếu IfcProject"));

        var html = HandoverPackage.Html(input);
        Assert.Contains("TTTM Goldview", html);
        Assert.Contains("DHCB Tools 1.1.0", html);
        Assert.Contains("2026-09-06 01:02:03", html);
        Assert.Contains("Điều 11 NĐ 207/2026", html);
        Assert.Contains("Chủ đầu tư xác nhận", html);
        Assert.Contains("Công ty CĐT", html);
        Assert.Contains("class=\"ok\">Đạt", html);
        Assert.Contains("class=\"fail\">Không đạt", html);
        Assert.Contains("Thành công", html);
        Assert.Contains("Bỏ qua", html);
        Assert.Contains("loi &lt;b&gt;", html);
        Assert.Contains("2.0 MB", html);
        Assert.Contains("2 KB", html);
        Assert.Contains("12 B", html);
        Assert.Contains("A-101", html);
        Assert.Contains("<code>abc</code>", html);

        var json = JObject.Parse(HandoverPackage.ToJson(input));
        Assert.Equal("TTTM Goldview", (string?)json["project"]);
        Assert.Equal(3, json["files"]!.Count());
        Assert.Equal(2, json["sheets"]!.Count());
        Assert.Equal(2, json["checks"]!.Count());
        Assert.Equal("a.rvt", (string?)json["steps"]![0]!["file"]);

        // Không danh mục, không file, không mục kiểm → nói rõ thay vì bảng rỗng.
        var bare = HandoverPackage.Html(new HandoverInput { GeneratedAt = DateTime.Now });
        Assert.Contains("Không có danh mục bản vẽ", bare);
        Assert.Contains("không có file sản phẩm nào", bare);
        Assert.Contains("Không có mục kiểm tra nào", bare);
    }

    [Fact]
    public void Sha256_KhopChuanBietTruoc()
    {
        var path = Path.Combine(_dir, "abc.txt");
        File.WriteAllText(path, "abc", new UTF8Encoding(false));
        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", HandoverPackage.Sha256Of(path));
    }

    [Fact]
    public void JobCoHandover_DocDuoc_VaDoiOutputFolder()
    {
        var job = BatchJob.Parse("{\"files\":[{\"path\":\"a.rvt\"}],\"steps\":[{\"command\":\"HealthReport\"}],\"saveMode\":\"None\",\"outputFolder\":\"D:/out\","
            + "\"handover\":{\"projectName\":\"P\",\"owner\":\"O\",\"contractor\":\"C\",\"idsPath\":\"y.ids\"}}");
        Assert.NotNull(job.Handover);
        Assert.True(job.Handover!.Enabled);
        Assert.Equal("P", job.Handover.ProjectName);
        Assert.Equal("y.ids", job.Handover.IdsPath);
        Assert.Null(job.Handover.IfcSpecPath);

        var ex = Assert.Throws<InvalidDataException>(() => BatchJob.Parse(
            "{\"files\":[{\"path\":\"a.rvt\"}],\"steps\":[{\"command\":\"HealthReport\"}],\"saveMode\":\"None\",\"handover\":{}}"));
        Assert.Contains("'handover' cần 'outputFolder'", ex.Message);

        var off = BatchJob.Parse("{\"files\":[{\"path\":\"a.rvt\"}],\"steps\":[{\"command\":\"HealthReport\"}],\"saveMode\":\"None\",\"handover\":{\"enabled\":false}}");
        Assert.False(off.Handover!.Enabled);
        Assert.Null(BatchJob.Parse("{\"files\":[{\"path\":\"a.rvt\"}],\"steps\":[{\"command\":\"HealthReport\"}],\"saveMode\":\"None\"}").Handover);
    }
}
