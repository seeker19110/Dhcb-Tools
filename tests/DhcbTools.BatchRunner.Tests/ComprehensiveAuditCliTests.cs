using DhcbTools.Shared.Logic.Batch;
using DhcbTools.Shared.Logic.Handover;
using Newtonsoft.Json.Linq;
using Xunit;

namespace DhcbTools.BatchRunner.Tests;

public sealed class ComprehensiveAuditCliTests
{
    private static string Job(Cli cli, bool ids = false) => cli.Write("job.json", new JObject
    {
        ["name"] = "audit", ["app"] = "revit", ["saveMode"] = "None", ["outputFolder"] = cli.Path_("out"),
        ["files"] = new JArray(new JObject { ["path"] = "a.rvt" }),
        ["steps"] = new JArray(new JObject { ["command"] = "HealthReport" }),
        ["handover"] = ids ? new JObject { ["idsPath"] = cli.Write("rules.ids", Fixtures.ValidIds) } : null,
    }.ToString());

    private static string Log(Cli cli)
    {
        var path = cli.Path_("logs", DateTime.Now.ToString("yyyy-MM-dd"), "run-120000.jsonl");
        RunLog.Append(path, new RunLogEntry { File = "a.rvt", Command = "HealthReport", Success = true, Summary = "OK" });
        return path;
    }

    [Theory]
    [InlineData("{broken")]
    [InlineData("{\"success\":true,\"errors\":null}")]
    public void ReportOnlyCannotHideTruncatedOrInvalidLogRows(string tail)
    {
        using var cli = new Cli();
        var log = Log(cli);
        File.AppendAllText(log, tail + "\n");
        var result = cli.Run("--job", Job(cli), "--log-dir", cli.Path_("logs"), "--report-only");
        Assert.Equal(1, result.Code);
        Assert.Contains("Nhật ký", File.ReadAllText(Path.ChangeExtension(log, ".html")));
    }

    [Fact]
    public void ReportOnlyRejectsAnEmptyRun()
    {
        using var cli = new Cli();
        var log = Log(cli);
        File.WriteAllText(log, "");
        var result = cli.Run("--job", Job(cli), "--log-dir", cli.Path_("logs"), "--report-only");
        Assert.Equal(1, result.Code);
    }

    [Fact]
    public void SameBasenameIfcsKeepSeparateIdsReportsAndHashes()
    {
        using var cli = new Cli();
        cli.Write("out/first/model.ifc", Fixtures.OneWallIfc);
        cli.Write("out/second/model.ifc", Fixtures.OneWallIfc);
        Log(cli);
        var result = cli.Run("--job", Job(cli, ids: true), "--log-dir", cli.Path_("logs"), "--report-only");
        Assert.Equal(0, result.Code);
        foreach (var relative in new[] { "first/model-ids.html", "second/model-ids.html" })
            Assert.True(File.Exists(cli.Path_("out", relative)), relative);
        var files = JObject.Parse(File.ReadAllText(cli.Path_("out", "ban-giao.json")))["files"]!.ToObject<List<JObject>>()!;
        var reports = files.Where(f => f["RelativePath"]!.ToString().EndsWith("model-ids.html")).ToList();
        Assert.Equal(2, reports.Count);
        foreach (var report in reports)
            Assert.Equal(HandoverPackage.Sha256Of(cli.Path_("out", report["RelativePath"]!.ToString())), report["Sha256"]!.ToString());
    }
}
