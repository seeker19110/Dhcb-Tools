using DhcbTools.BatchRunner;
using Newtonsoft.Json.Linq;
using Xunit;

namespace DhcbTools.BatchRunner.Tests;

public sealed class AutoCadLaunchTests
{
    [Fact]
    public void InvalidExecutableReportsFailureWithoutThrowingOrPublishingPdf()
    {
        using var cli = new Cli();
        var console = cli.Write("custom-host/accoreconsole.exe", "invalid executable");
        var drawing = cli.Write("drawing.dwg", "disposable fixture");
        var pdf = cli.Write("drawing.pdf", "%PDF-old");
        var job = cli.Write("job.json", new JObject
        {
            ["name"] = "Invalid host", ["app"] = "autocad", ["saveMode"] = "None",
            ["files"] = new JArray(new JObject { ["path"] = drawing }),
            ["steps"] = new JArray(new JObject { ["command"] = "PlotPdf", ["config"] = new JObject { ["outputPath"] = pdf } }),
        }.ToString());
        var result = cli.Run("--job", job, "--log-dir", cli.Path_("logs"), "--accoreconsole", console,
            "--plugin-dll", typeof(Program).Assembly.Location);
        Assert.Equal(1, result.Code);
        Assert.Contains("1 lỗi", result.Output);
        Assert.Equal("%PDF-old", File.ReadAllText(pdf));
        Assert.Empty(Directory.GetFiles(cli.Root, ".dhcb-plot-*.pdf"));
        Assert.Contains("Không khởi động được accoreconsole", File.ReadAllText(Directory.GetFiles(cli.Path_("logs"), "run-*.jsonl", SearchOption.AllDirectories).Single()));
    }
}
