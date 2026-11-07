using DhcbTools.Shared.Logic.Batch;
using Xunit;

namespace DhcbTools.BatchRunner.Tests;

public sealed class CliOptionValidationTests
{
    private static string ValidLog(Cli cli)
    {
        var path = cli.Path_("run.jsonl");
        RunLog.Append(path, new RunLogEntry { File = "a.rvt", Command = "HealthReport", Success = true, Summary = "OK" });
        return path;
    }

    [Theory]
    [InlineData("--job")]
    [InlineData("--verify-ifc")]
    [InlineData("--dossier")]
    public void SuccessfulLogVerificationCannotHideAnotherRequestedOperation(string operation)
    {
        using var cli = new Cli();
        var result = cli.Run("--verify-log", ValidLog(cli), operation, cli.Path_("missing"));
        Assert.Equal(2, result.Code);
        Assert.Contains("Chỉ chọn một chế độ", result.Output);
    }

    [Theory]
    [InlineData("--verify-ids")]
    [InlineData("--ids-report")]
    [InlineData("--ifc-spec")]
    [InlineData("--dossier-spec")]
    [InlineData("--dossier-report")]
    public void VerificationCannotSilentlyIgnoreAnOptionForAnotherMode(string option)
    {
        using var cli = new Cli();
        var result = cli.Run("--verify-log", ValidLog(cli), option, cli.Path_("missing"));
        Assert.Equal(2, result.Code);
        Assert.Contains(option, result.Output);
    }

    [Fact]
    public void IdsVerificationCannotSilentlyIgnoreIfcRules()
    {
        using var cli = new Cli();
        var result = cli.Run("--verify-ifc", cli.Write("model.ifc", Fixtures.OneWallIfc),
            "--verify-ids", cli.Write("rules.ids", Fixtures.ValidIds),
            "--ifc-spec", cli.Path_("missing-rules.json"));
        Assert.Equal(2, result.Code);
        Assert.Contains("--ifc-spec", result.Output);
    }

    [Fact]
    public void ReportOnlyCannotSilentlyOverrideDryRun()
    {
        using var cli = new Cli();
        var result = cli.Run("--job", cli.Path_("missing-job.json"), "--dry-run", "--report-only");
        Assert.Equal(2, result.Code);
        Assert.Contains("--dry-run", result.Output);
        Assert.Contains("--report-only", result.Output);
    }
}
