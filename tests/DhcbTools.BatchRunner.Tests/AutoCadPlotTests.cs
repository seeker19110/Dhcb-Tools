using DhcbTools.BatchRunner;
using Newtonsoft.Json.Linq;
using Xunit;

namespace DhcbTools.BatchRunner.Tests;

public sealed class AutoCadPlotTests
{
    [Fact]
    public void PreviewDoesNotCreateOutputDirectoryOrPlotScript()
    {
        using var cli = new Cli();
        var target = cli.Path_("missing", "drawing.pdf");
        var plot = new AutoCadPlot(new JObject(), target, dryRun: true);
        Assert.Null(plot.Script);
        Assert.Null(plot.Staging);
        Assert.False(Directory.Exists(Path.GetDirectoryName(target)));
        Assert.True(plot.Complete("drawing.dwg", null).IsComplete);
        Assert.False(File.Exists(target));
    }

    [Fact]
    public void ExistingPdfCannotHideMissingPlotResult()
    {
        using var cli = new Cli();
        var target = cli.Write("drawing.pdf", "%PDF-old");
        var plot = new AutoCadPlot(new JObject(), target, false);
        Assert.Contains(plot.Staging!, plot.Script);
        Assert.DoesNotContain(target, plot.Script);
        Assert.False(plot.Complete("drawing.dwg", null).Success);
        Assert.Equal("%PDF-old", File.ReadAllText(target));
    }

    [Theory]
    [InlineData("")]
    [InlineData("%PD")]
    [InlineData("ERROR: failed to plot")]
    public void InvalidPlotDoesNotReplaceExistingPdf(string content)
    {
        using var cli = new Cli();
        var target = cli.Write("drawing.pdf", "%PDF-old");
        var plot = new AutoCadPlot(new JObject(), target, false);
        File.WriteAllText(plot.Staging!, content);
        Assert.False(plot.Complete("drawing.dwg", null).Success);
        Assert.Equal("%PDF-old", File.ReadAllText(target));
        Assert.False(File.Exists(plot.Staging));
    }

    [Fact]
    public void FailedCadStepBlocksPublicationAndRemovesTemporaryPdf()
    {
        using var cli = new Cli();
        var target = cli.Write("drawing.pdf", "%PDF-old");
        var plot = new AutoCadPlot(new JObject(), target, false);
        File.WriteAllText(plot.Staging!, "%PDF-new");
        var entry = plot.Complete("drawing.dwg", "step AttributeImport lỗi");
        Assert.False(entry.Success);
        Assert.Contains("AttributeImport", entry.Summary);
        Assert.Equal("%PDF-old", File.ReadAllText(target));
        Assert.False(File.Exists(plot.Staging));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ValidNewPdfIsPublishedAndReported(bool existing)
    {
        using var cli = new Cli();
        var target = cli.Path_("output", "drawing.pdf");
        if (existing) cli.Write("output/drawing.pdf", "%PDF-old");
        var plot = new AutoCadPlot(new JObject { ["outputPath"] = target }, cli.Path_("unused.pdf"), false);
        Assert.Equal(Path.GetDirectoryName(target), Path.GetDirectoryName(plot.Staging));
        File.WriteAllText(plot.Staging!, "%PDF-new");
        var entry = plot.Complete("drawing.dwg", null);
        Assert.True(entry.IsComplete, entry.Summary);
        Assert.Equal("%PDF-new", File.ReadAllText(target));
        Assert.False(File.Exists(plot.Staging));
        Assert.Equal("drawing.dwg", entry.File);
        Assert.Equal("PlotPdf", entry.Command);
    }

    [Fact]
    public void NonPdfTargetIsRejectedBeforeCreatingDirectory()
    {
        using var cli = new Cli();
        var target = cli.Path_("missing", "drawing.dwg");
        Assert.Throws<ArgumentException>(() => new AutoCadPlot(new JObject(), target, false));
        Assert.False(Directory.Exists(Path.GetDirectoryName(target)));
    }

    [Fact]
    public void PublicationFailureIsReportedAndExistingDirectoryIsPreserved()
    {
        using var cli = new Cli();
        var target = cli.Path_("drawing.pdf");
        Directory.CreateDirectory(target);
        var existing = cli.Write("drawing.pdf/preserved.txt", "keep");
        var plot = new AutoCadPlot(new JObject(), target, false);
        File.WriteAllText(plot.Staging!, "%PDF-new");
        var entry = plot.Complete("drawing.dwg", null);
        Assert.False(entry.Success);
        Assert.Contains("Không xuất được PDF", entry.Summary);
        Assert.Equal("keep", File.ReadAllText(existing));
        Assert.False(File.Exists(plot.Staging));
    }

    [Fact]
    public void TemporaryWindowsReplaceFailureRetriesAtomicOperation()
    {
        using var cli = new Cli();
        var target = cli.Write("drawing.pdf", "%PDF-old");
        var plot = new AutoCadPlot(new JObject(), target, false);
        File.WriteAllText(plot.Staging!, "%PDF-new");
        var attempts = 0;
        var entry = plot.Complete("drawing.dwg", null, (staging, destination) =>
        {
            if (++attempts == 1) throw new IOException("Unable to remove the file to be replaced.", unchecked((int)0x80070497));
            DhcbTools.Shared.Logic.Batch.StagedSave.Promote(staging, destination, false);
        });
        Assert.True(entry.IsComplete, entry.Summary);
        Assert.InRange(attempts, 2, 4);
        Assert.Equal("%PDF-new", File.ReadAllText(target));
    }

    [Theory]
    [InlineData(1175, 4)]
    [InlineData(5, 1)]
    public void PersistentPublicationFailureIsBoundedAndPreservesDestination(int error, int expectedAttempts)
    {
        using var cli = new Cli();
        var target = cli.Write("drawing.pdf", "%PDF-old");
        var plot = new AutoCadPlot(new JObject(), target, false);
        File.WriteAllText(plot.Staging!, "%PDF-new");
        var attempts = 0;
        var entry = plot.Complete("drawing.dwg", null, (_, _) =>
        {
            attempts++;
            throw new IOException("Cannot publish", unchecked((int)(0x80070000u | (uint)error)));
        });
        Assert.False(entry.Success);
        Assert.Equal(expectedAttempts, attempts);
        Assert.Equal("%PDF-old", File.ReadAllText(target));
        Assert.False(File.Exists(plot.Staging));
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void InvalidScaleAndConflictingPageSetupFailBeforeCreatingDirectory(bool dryRun)
    {
        using var cli = new Cli();
        var target = cli.Path_("missing", "drawing.pdf");
        Assert.Throws<ArgumentException>(() => new AutoCadPlot(new JObject { ["plotScale"] = "1=0" }, target, dryRun));
        Assert.Throws<ArgumentException>(() => new AutoCadPlot(new JObject { ["pageSetupName"] = "A3", ["plotScale"] = "1=100" }, target, dryRun));
        Assert.False(Directory.Exists(Path.GetDirectoryName(target)));
    }

}
