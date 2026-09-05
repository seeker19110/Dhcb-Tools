using DhcbTools.Shared.Logic.Batch;
using Newtonsoft.Json.Linq;
using Xunit;

namespace DhcbTools.BatchRunner.Tests;

/// <summary>
/// Gói bàn giao dựng từ CLI (<c>--report-only</c> để không cần Revit). §44 T5b: một file <c>.ifc</c> hỏng nằm
/// trong thư mục đầu ra từng làm SẬP cả runner — đêm đó không có <c>ban-giao.html</c> nào.
/// </summary>
public class HandoverCliTests
{
    /// <summary>Job chỉ có handover; các bước coi như đã chạy xong (log ghi sẵn), nên không cần Revit.</summary>
    private static string WriteJob(Cli cli, string outputFolder, string? idsPath)
    {
        var job = new JObject
        {
            ["name"] = "Đêm thử",
            ["app"] = "revit",
            ["saveMode"] = "None",
            ["outputFolder"] = outputFolder.Replace('\\', '/'),
            ["files"] = new JArray(new JObject { ["path"] = "a.rvt" }),
            ["steps"] = new JArray(new JObject { ["command"] = "HealthReport" }),
            ["handover"] = new JObject
            {
                ["projectName"] = "Dự án thử",
                ["owner"] = "Chủ đầu tư",
                ["contractor"] = "DHCB",
                ["idsPath"] = idsPath?.Replace('\\', '/'),
            },
        };
        return cli.Write("job.json", job.ToString());
    }

    private static string WriteRunLog(Cli cli, string logDir)
    {
        var day = Path.Combine(logDir, DateTime.Now.ToString("yyyy-MM-dd"));
        Directory.CreateDirectory(day);
        var log = Path.Combine(day, "run-000001.jsonl");
        RunLog.Append(log, new RunLogEntry { File = "a.rvt", Command = "HealthReport", Success = true, Summary = "ok" });
        return log;
    }

    private static List<JObject> Checks(string outputFolder) =>
        JObject.Parse(File.ReadAllText(Path.Combine(outputFolder, "ban-giao.json")))["checks"]!.ToObject<List<JObject>>()!;

    /// <summary>
    /// §44 T5b — .ifc hỏng trong thư mục đầu ra: gói VẪN phải dựng, và phải NÓI RA là không đọc được, thay
    /// vì sập (mất trắng gói) hoặc im lặng bỏ qua (gói nói dối là mọi thứ đạt).
    /// </summary>
    [Fact]
    public void IfcHongTrongThuMucDauRa_VanDungGoi_VaGhiKhongDat()
    {
        using var cli = new Cli();
        var outFolder = cli.Path_("out");
        Directory.CreateDirectory(outFolder);
        cli.Write("out/hong.ifc", Fixtures.JunkIfc);
        var job = WriteJob(cli, outFolder, cli.Write("yeu-cau.ids", Fixtures.ValidIds));
        WriteRunLog(cli, cli.Path_("logs"));

        var (code, _) = cli.Run("--job", job, "--log-dir", cli.Path_("logs"), "--report-only");

        // Gói không đổi mã thoát của job; job này mọi bước đều OK.
        Assert.Equal(0, code);
        Assert.True(File.Exists(Path.Combine(outFolder, "ban-giao.html")), "không dựng được ban-giao.html");

        var checks = Checks(outFolder);
        Assert.Contains(checks, c => c["Name"]!.ToString().StartsWith("Kiểm IFC") && !c["Ok"]!.ToObject<bool>());
        Assert.Contains(checks, c => c["Name"]!.ToString().StartsWith("Kiểm IDS")
                                     && !c["Ok"]!.ToObject<bool>()
                                     && c["Detail"]!.ToString().Contains("Không đọc được file IFC"));
    }

    /// <summary>IFC lành: gói ghi "đạt" cho cả hai mục và băm file sản phẩm.</summary>
    [Fact]
    public void IfcLanh_GoiGhiDat_VaBamFileSanPham()
    {
        using var cli = new Cli();
        var outFolder = cli.Path_("out");
        Directory.CreateDirectory(outFolder);
        cli.Write("out/toa-a.ifc", Fixtures.OneWallIfc);
        var job = WriteJob(cli, outFolder, cli.Write("yeu-cau.ids", Fixtures.ValidIds));
        WriteRunLog(cli, cli.Path_("logs"));

        var (code, _) = cli.Run("--job", job, "--log-dir", cli.Path_("logs"), "--report-only");
        Assert.Equal(0, code);

        var checks = Checks(outFolder);
        Assert.Contains(checks, c => c["Name"]!.ToString().StartsWith("Kiểm IDS") && c["Ok"]!.ToObject<bool>());
        Assert.Contains(checks, c => c["Name"]!.ToString().Contains("Chuỗi băm") && c["Ok"]!.ToObject<bool>());

        var files = JObject.Parse(File.ReadAllText(Path.Combine(outFolder, "ban-giao.json")))["files"]!
            .ToObject<List<JObject>>()!;
        var ifc = Assert.Single(files, f => f["RelativePath"]!.ToString() == "toa-a.ifc");
        Assert.Equal(64, ifc["Sha256"]!.ToString().Length);
    }

    /// <summary>IDS hỏng khai trong job: một mục Không đạt, gói vẫn dựng — không được ném ra ngoài.</summary>
    [Fact]
    public void IdsHongKhaiTrongJob_MotMucKhongDat_GoiVanDung()
    {
        using var cli = new Cli();
        var outFolder = cli.Path_("out");
        Directory.CreateDirectory(outFolder);
        cli.Write("out/toa-a.ifc", Fixtures.OneWallIfc);
        var job = WriteJob(cli, outFolder, cli.Write("hong.ids", "<ids"));
        WriteRunLog(cli, cli.Path_("logs"));

        var (code, _) = cli.Run("--job", job, "--log-dir", cli.Path_("logs"), "--report-only");

        Assert.Equal(0, code);
        Assert.Contains(Checks(outFolder),
            c => c["Name"]!.ToString().StartsWith("Kiểm IDS") && !c["Ok"]!.ToObject<bool>());
    }

    /// <summary>Nhật ký bị sửa một chữ: gói phải nói chuỗi băm hỏng (NĐ 207/2026, mục 11.5).</summary>
    [Fact]
    public void NhatKyBiSua_GoiBaoChuoiBamHong()
    {
        using var cli = new Cli();
        var outFolder = cli.Path_("out");
        Directory.CreateDirectory(outFolder);
        var job = WriteJob(cli, outFolder, null);
        var log = WriteRunLog(cli, cli.Path_("logs"));
        File.WriteAllLines(log, File.ReadAllLines(log).Select(line => line.Replace("\"ok\"", "\"da sua\"")));

        var (code, _) = cli.Run("--job", job, "--log-dir", cli.Path_("logs"), "--report-only");

        Assert.Equal(0, code);
        Assert.Contains(Checks(outFolder),
            c => c["Name"]!.ToString().Contains("Chuỗi băm") && !c["Ok"]!.ToObject<bool>());
    }

    [Fact]
    public void JobThieuFile_HoacKhongPhaiJson_HoacHandoverThieuOutputFolder_MaThoat2()
    {
        using var cli = new Cli();
        Assert.Equal(2, cli.Run("--job", cli.Path_("khong-co.json")).Code);
        Assert.Equal(2, cli.Run("--job", cli.Write("hong.json", "{ khong phai json")).Code);

        var noOutput = cli.Write("thieu-output.json", new JObject
        {
            ["saveMode"] = "None",
            ["files"] = new JArray(new JObject { ["path"] = "a.rvt" }),
            ["steps"] = new JArray(new JObject { ["command"] = "HealthReport" }),
            ["handover"] = new JObject { ["projectName"] = "P" },
        }.ToString());

        var (code, output) = cli.Run("--job", noOutput);
        Assert.Equal(2, code);
        Assert.Contains("'handover' cần 'outputFolder'", output);
    }

    /// <summary><c>--report-only</c> mà thư mục log chưa có lượt chạy nào: nói rõ, mã 2, không dựng gói rỗng.</summary>
    [Fact]
    public void ReportOnly_KhongCoNhatKy_MaThoat2()
    {
        using var cli = new Cli();
        var outFolder = cli.Path_("out");
        Directory.CreateDirectory(outFolder);
        var job = WriteJob(cli, outFolder, null);

        var (code, output) = cli.Run("--job", job, "--log-dir", cli.Path_("logs-rong"), "--report-only");

        Assert.Equal(2, code);
        Assert.Contains("để dựng báo cáo", output);
        Assert.False(File.Exists(Path.Combine(outFolder, "ban-giao.html")));
    }
}
