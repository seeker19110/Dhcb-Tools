using Newtonsoft.Json.Linq;
using DhcbTools.Shared.Logic.Batch;
using Xunit;

namespace DhcbTools.BatchRunner.Tests;

public class BatchInputAuditTests
{
    private static string Job(Cli cli) => cli.Write("job.json", new JObject
    {
        ["app"] = "autocad", ["saveMode"] = "None",
        ["files"] = new JArray(new JObject { ["path"] = cli.Path_("missing.dwg") }),
        ["steps"] = new JArray(new JObject { ["command"] = "LayerExport" }),
    }.ToString());

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void NonPositiveTimeBudgetFailsBeforeCreatingLogs(int minutes)
    {
        using var cli = new Cli();
        var result = cli.Run("--job", Job(cli), "--max-minutes", minutes.ToString(),
            "--log-dir", cli.Path_("logs"), "--accoreconsole", cli.Path_("missing.exe"));
        Assert.Equal(2, result.Code);
        Assert.Contains("max-minutes", result.Output);
        Assert.False(Directory.Exists(cli.Path_("logs")));
    }

    [Fact]
    public void UnwritableLogDirectoryReturnsDiagnosticInsteadOfCrashing()
    {
        using var cli = new Cli();
        var file = cli.Write("log-blocker", "keep");
        var result = cli.Run("--job", Job(cli), "--log-dir", file, "--report-only");
        Assert.Equal(2, result.Code);
        Assert.Contains("I/O", result.Output);
        Assert.Equal("keep", File.ReadAllText(file));
    }

    [Fact]
    public void NewRunLogTakesPrecedenceOverLegacyNameAtSameSecond()
    {
        using var cli = new Cli();
        var dir = cli.Path_("logs", DateTime.Now.ToString("yyyy-MM-dd"));
        RunLog.Append(Path.Combine(dir, "run-120000.jsonl"), new RunLogEntry { Success = true, Command = "old" });
        var current = Path.Combine(dir, "run-120000-1234567-current.jsonl");
        RunLog.Append(current, new RunLogEntry { Success = false, Command = "current" });
        var result = cli.Run("--job", Job(cli), "--log-dir", cli.Path_("logs"), "--report-only");
        Assert.Equal(1, result.Code);
        Assert.Contains(current, result.Output);
        Assert.Contains("current", File.ReadAllText(Path.ChangeExtension(current, ".html")));
    }

    [Fact]
    public void MissingHostDoesNotLeaveAnEmptyLatestRunOrSuccessfulReport()
    {
        using var cli = new Cli();
        var result = cli.Run("--job", Job(cli), "--log-dir", cli.Path_("logs"),
            "--accoreconsole", cli.Path_("missing.exe"));
        Assert.Equal(2, result.Code);
        Assert.Empty(Directory.GetFiles(cli.Path_("logs"), "run-*.jsonl", SearchOption.AllDirectories));
        Assert.Empty(Directory.GetFiles(cli.Path_("logs"), "*.html", SearchOption.AllDirectories));
    }

    [Fact]
    public void OutputDirectoryFailureDoesNotLeaveAnEmptyLatestRun()
    {
        using var cli = new Cli();
        var console = cli.Write("custom-host/accoreconsole.exe", "invalid executable");
        var blocker = cli.Write("output-blocker", "keep");
        var job = JObject.Parse(File.ReadAllText(Job(cli)));
        job["outputFolder"] = blocker;
        var path = cli.Write("job.json", job.ToString());
        var result = cli.Run("--job", path, "--log-dir", cli.Path_("logs"), "--accoreconsole", console,
            "--plugin-dll", typeof(DhcbTools.BatchRunner.Program).Assembly.Location);
        Assert.Equal(2, result.Code);
        Assert.Contains("I/O", result.Output);
        Assert.Equal("keep", File.ReadAllText(blocker));
        Assert.Empty(Directory.GetFiles(cli.Path_("logs"), "run-*.jsonl", SearchOption.AllDirectories));
    }
}
