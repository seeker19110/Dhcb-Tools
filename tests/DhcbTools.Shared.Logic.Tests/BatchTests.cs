using System.Globalization;
using DhcbTools.Shared.Logic.Batch;
using Newtonsoft.Json.Linq;
using Xunit;

namespace DhcbTools.Shared.Logic.Tests;

public class JobTokensTests
{
    private static readonly DateTime RunTime = new(2026, 9, 1, 23, 5, 0);

    [Fact]
    public void ThayOutputFolder_FileName_NgayGio()
    {
        var ctx = new JobTokenContext("D:/out", "ARC", RunTime);
        Assert.Equal("D:/out/ARC-2026-09-01_23-05.html", JobTokens.Expand("{outputFolder}/{fileName}-{yyyy-MM-dd}_{HH-mm}.html", ctx));
    }

    [Fact]
    public void TokenTenKhongPhanBietHoaThuong_NhungMauNgayGioThi()
    {
        var ctx = new JobTokenContext("O", "F", RunTime);
        Assert.Equal("O/F", JobTokens.Expand("{OUTPUTFOLDER}/{FileName}", ctx));
        Assert.Equal("09", JobTokens.Expand("{MM}", ctx));  // tháng
        Assert.Equal("05", JobTokens.Expand("{mm}", ctx));  // phút
    }

    [Fact]
    public void TokenKhongNhanRa_GiuNguyenDeThayLoi()
    {
        var ctx = new JobTokenContext("O", "F", RunTime);
        Assert.Equal("{khongCo}/x", JobTokens.Expand("{khongCo}/x", ctx));
    }

    [Fact]
    public void TokenTuyChinh()
    {
        var ctx = new JobTokenContext("O", "F", RunTime);
        ctx.Extra["projectCode"] = "LMK";
        Assert.Equal("LMK-F", JobTokens.Expand("{projectCode}-{fileName}", ctx));
    }

    [Fact]
    public void TenFileDuocSanitize()
    {
        var ctx = new JobTokenContext("O", "A/B:C", RunTime);
        Assert.Equal("A_B_C", JobTokens.Expand("{fileName}", ctx));
    }

    [Fact]
    public void ChuoiRong_TraVeRong()
    {
        Assert.Equal(string.Empty, JobTokens.Expand(null, new JobTokenContext("O", "F", RunTime)));
    }
}

public class BatchJobTests
{
    private const string Sample = """
        {
          "name": "Đêm",
          "revitVersion": 2024,
          "saveMode": "SaveAs",
          "outputFolder": "D:/nightly/{yyyy-MM-dd}",
          "tokens": { "projectCode": "LMK" },
          "files": [ { "path": "P:/ARC.rvt" }, { "path": "P:/MEP.rvt", "detachFromCentral": true, "onlySteps": ["HealthReport"] } ],
          "steps": [
            { "command": "HealthReport", "config": { "outputPath": "{outputFolder}/{fileName}-health.html" } },
            { "command": "BatchExport", "config": { "outputFolder": "{outputFolder}/pdf", "formats": ["Pdf"], "tags": ["{projectCode}"] } }
          ]
        }
        """;

    [Fact]
    public void DocFileJob_VaThayTokenTrongConfig()
    {
        var job = BatchJob.Parse(Sample);
        var run = new DateTime(2026, 9, 1, 23, 0, 0);

        Assert.Equal("D:/nightly/2026-09-01", job.ResolveOutputFolder(run));
        var cfg = JObject.Parse(job.ExpandStepConfig(job.Steps[0], "D:/nightly/2026-09-01", "P:/ARC.rvt", run));
        Assert.Equal("D:/nightly/2026-09-01/ARC-health.html", (string?)cfg["outputPath"]);

        var cfg2 = JObject.Parse(job.ExpandStepConfig(job.Steps[1], "O", "P:/ARC.rvt", run));
        Assert.Equal("LMK", (string?)cfg2["tags"]![0]);
        Assert.Equal("Pdf", (string?)cfg2["formats"]![0]);
    }

    [Fact]
    public void OnlySteps_LocStepTheoFile()
    {
        var job = BatchJob.Parse(Sample);
        Assert.Equal(2, job.StepsFor(job.Files[0]).Count());
        Assert.Single(job.StepsFor(job.Files[1]));
    }

    [Fact]
    public void MacDinh_SaveAs_VaDetachFalse()
    {
        var job = BatchJob.Parse(Sample);
        Assert.Equal(SaveMode.SaveAs, job.SaveMode);
        Assert.False(job.Files[0].DetachFromCentral);
        Assert.True(job.Files[1].DetachFromCentral);
    }

    [Fact]
    public void ThieuFiles_HoacSteps_BaoLoiRo()
    {
        var ex = Assert.Throws<System.IO.InvalidDataException>(() => BatchJob.Parse("""{ "files": [], "steps": [] , "saveMode":"None"}"""));
        Assert.Contains("'files' rỗng", ex.Message);
        Assert.Contains("'steps' rỗng", ex.Message);
    }

    [Fact]
    public void SaveAsThieuOutputFolder_LaLoi()
    {
        var job = new BatchJob();
        job.Files.Add(new BatchJobFile { Path = "a.rvt" });
        job.Steps.Add(new BatchJobStep { Command = "HealthReport" });
        Assert.Contains(job.Validate(), e => e.Contains("outputFolder"));
    }

    [Fact]
    public void JsonHong_BaoLoiKhongNemJsonException()
    {
        Assert.Throws<System.IO.InvalidDataException>(() => BatchJob.Parse("{ not json"));
    }
}

public class RunLogTests
{
    [Fact]
    public void RoundTrip_MotDong()
    {
        var entry = new RunLogEntry { File = "ARC.rvt", Command = "HealthReport", Success = true, Affected = 3, Summary = "ok", ElapsedMs = 120 };
        entry.Messages.Add("dòng \"có\" nháy");
        var line = RunLog.Serialize(entry);

        Assert.DoesNotContain('\n', line);
        var back = RunLog.Deserialize(line)!;
        Assert.Equal("ARC.rvt", back.File);
        Assert.Equal(3, back.Affected);
        Assert.Equal("dòng \"có\" nháy", back.Messages[0]);
    }

    [Fact]
    public void DongHong_BiBoQua_KhongNem()
    {
        var path = Path.Combine(Path.GetTempPath(), "dhcb-" + Guid.NewGuid() + ".jsonl");
        try
        {
            RunLog.Append(path, new RunLogEntry { File = "a", Command = "x", Success = true });
            File.AppendAllText(path, "{ hỏng dở\n");
            RunLog.Append(path, new RunLogEntry { File = "b", Command = "y", Success = false });

            var all = RunLog.ReadAll(path);
            Assert.Equal(2, all.Count);
            Assert.Equal(1, RunLog.ExitCode(all));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void MaThoat_0KhiTatCaThanhCong_1KhiBoQua()
    {
        Assert.Equal(0, RunLog.ExitCode(new[] { new RunLogEntry { Success = true } }));
        Assert.Equal(1, RunLog.ExitCode(new[] { new RunLogEntry { Success = true, Skipped = true } }));
    }

    [Fact]
    public void FileKhongTonTai_TraRong()
    {
        Assert.Empty(RunLog.ReadAll(Path.Combine(Path.GetTempPath(), "khong-co-" + Guid.NewGuid() + ".jsonl")));
    }
}

public class BatchReportTests
{
    [Fact]
    public void BangFileXStep_EscapeHtml()
    {
        var entries = new List<RunLogEntry>
        {
            new() { File = "A<1>.rvt", Command = "HealthReport", Success = true, Summary = "ok & xong" },
            new() { File = "A<1>.rvt", Command = "BatchExport", Success = false, Summary = "lỗi", Errors = { "không mở được" } },
            new() { File = "B.rvt", Command = "HealthReport", Success = true, Skipped = true, Summary = "bỏ qua" },
        };

        var html = BatchReport.Render("Job <đêm>", entries, new DateTime(2026, 9, 1));

        Assert.Contains("A&lt;1&gt;.rvt", html);
        Assert.Contains("ok &amp; xong", html);
        Assert.Contains("class=\"fail\"", html);
        Assert.Contains("class=\"skip\"", html);
        Assert.Contains("Job &lt;đêm&gt;", html);
        Assert.DoesNotContain("<1>", html);
    }
}

public class AcadScriptGenTests
{
    [Fact]
    public void ScriptCoNetload_Run_SaveAs_Quit()
    {
        var scr = AcadScriptGen.Build(@"C:\dhcb\DhcbTools.AutoCAD.dll", new[] { @"C:\t\s1.json", @"C:\t\s2.json" }, @"D:\out\a.dwg", @"D:\out\run.jsonl", @"P:\a.dwg");
        var lines = scr.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.Contains(lines, l => l.StartsWith("NETLOAD", StringComparison.Ordinal));
        Assert.Equal(2, lines.Count(l => l.StartsWith("DHCB_RUN", StringComparison.Ordinal)));
        Assert.Contains(lines, l => l.StartsWith("SAVEAS", StringComparison.Ordinal));
        Assert.Equal("QUIT Y", lines[^1]);
    }

    /// <summary>
    /// Mỗi tham số của DHCB_RUN phải nằm trên MỘT DÒNG RIÊNG. Trong script AutoCAD, một dòng là một
    /// lần Enter — tức một câu trả lời cho một prompt — mà DHCB_RUN hỏi ba lần. Bản cũ viết cả ba trên
    /// một dòng nên toàn bộ phần còn lại bị nuốt vào prompt đầu tiên và accoreconsole báo
    /// "The filename, directory name, or volume label syntax is incorrect": batch AutoCAD chưa từng
    /// chạy trọn lần nào. Lộ ra khi chạy thật trên AutoCAD 2026 ngày 2026-09-03.
    /// </summary>
    [Fact]
    public void MoiThamSoCuaDhcbRun_MotDongRieng_VaKhongBocNhay()
    {
        var scr = AcadScriptGen.Build(
            @"C:\dhcb\DhcbTools.AutoCAD.Core.dll",
            new[] { @"C:\t\s1.json" },
            null,
            @"D:\out\run.jsonl",
            @"P:\ban ve\a.dwg");

        var lines = scr.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var i = Array.FindIndex(lines, l => l.StartsWith("DHCB_RUN", StringComparison.Ordinal));

        Assert.Equal("DHCB_RUN", lines[i]);              // lệnh đứng một mình
        Assert.Equal(@"C:\t\s1.json", lines[i + 1]);
        Assert.Equal(@"D:\out\run.jsonl", lines[i + 2]);
        Assert.Equal(@"P:\ban ve\a.dwg", lines[i + 3]);   // đường dẫn có dấu cách vẫn nguyên một dòng
        Assert.DoesNotContain('"', lines[i + 1]);
    }

    [Fact]
    public void KhongSaveAs_KhiSaveModeNone()
    {
        var scr = AcadScriptGen.Build("p.dll", new[] { "s.json" }, null, "log", "a.dwg");
        Assert.DoesNotContain("SAVEAS", scr);
    }

    [Fact]
    public void StepJson_LaJsonHopLe()
    {
        var json = JObject.Parse(AcadScriptGen.StepJson("LayerExport", """{"outputPath":"x.csv"}"""));
        Assert.Equal("LayerExport", (string?)json["command"]);
        Assert.Equal("x.csv", (string?)json["config"]!["outputPath"]);
    }
}
