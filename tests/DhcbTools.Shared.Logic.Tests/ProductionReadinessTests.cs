using DhcbTools.Shared.Hosting;
using DhcbTools.Shared.Logic.Batch;
using DhcbTools.Shared.Logic.Cad;
using DhcbTools.Shared.Logic.Checks;
using DhcbTools.Shared.Logic.Mep;
using Xunit;

namespace DhcbTools.Shared.Logic.Tests;

public class ProductionReadinessTests
{
    [Fact]
    public void PageOnlyConsumesOffsetLimitAndOneLookahead()
    {
        var page = new QueryPage(3, 2);
        var visited = 0;
        var selected = new List<int>();
        foreach (var n in Enumerable.Range(0, 1000000))
        {
            visited++;
            if (page.IncludeMatch()) selected.Add(n);
            else if (page.HasMore) break;
        }
        Assert.Equal(new[] { 2, 3, 4 }, selected);
        Assert.Equal(6, visited);
        Assert.Equal(5, page.NextOffset);
        Assert.Equal(2, page.Offset);
        Assert.Equal(3, page.Limit);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(2, 0)]
    [InlineData(3, 5)]
    public void EndOfFilteredStreamDoesNotAdvertiseAnotherPage(int count, int offset)
    {
        var page = new QueryPage(2, offset);
        foreach (var _ in Enumerable.Range(0, count)) page.IncludeMatch();
        Assert.False(page.HasMore);
        Assert.Null(page.NextOffset);
    }

    [Fact]
    public void PageUsesBoundedDefaultAndRejectsInvalidRequests()
    {
        Assert.Equal(2000, new QueryPage().Limit);
        Assert.Throws<ArgumentOutOfRangeException>(() => new QueryPage(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new QueryPage(10001));
        Assert.Throws<ArgumentOutOfRangeException>(() => new QueryPage(1, -1));
    }

    [Theory]
    [InlineData("Fit", "Fit")]
    [InlineData(" fit ", "Fit")]
    [InlineData("1:100", "1=100")]
    [InlineData("1=50", "1=50")]
    [InlineData(".5", "0.5")]
    public void PlotScaleIsExplicitAndInvariant(string input, string expected)
        => Assert.Equal(expected, AcadScriptGen.NormalizePlotScale(input, true));

    [Theory]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("1=0")]
    [InlineData("1=2=3")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("-1")]
    [InlineData("1\nQUIT")]
    public void InvalidScaleCannotAddPrompts(string input)
        => Assert.Throws<ArgumentException>(() => AcadScriptGen.NormalizePlotScale(input, true));

    [Fact]
    public void LayoutDefaultsToOneToOneAndNamedSetupKeepsStoredSettings()
    {
        Assert.Equal("Fit", AcadScriptGen.NormalizePlotScale(null, true));
        Assert.Equal("1=1", AcadScriptGen.NormalizePlotScale(null, false));
        Assert.Contains("\n1=1\n", AcadScriptGen.PlotPdf("out.pdf", layout: "Sheet A3", plotArea: "Layout"));
        Assert.Contains("\n1=100\n", AcadScriptGen.PlotPdf("out.pdf", plotScale: "1:100"));
        Assert.Equal("-PLOT\nN\nSheet A3\nA3-project\nDWG To PDF.pc3\nout.pdf\nN\nY\n",
            AcadScriptGen.PlotPdf("out.pdf", layout: "Sheet A3", pageSetupName: "A3-project"));
        Assert.Contains("\nN\nModel\n", AcadScriptGen.PlotPdf("out.pdf", pageSetupName: "A3-project"));
        Assert.Throws<ArgumentException>(() => AcadScriptGen.PlotPdf("out.pdf", pageSetupName: ""));
        Assert.Throws<ArgumentException>(() => AcadScriptGen.PlotPdf("out.pdf", plotArea: "Window"));
        Assert.Throws<ArgumentException>(() => AcadScriptGen.PlotPdf("out.pdf", plotArea: "Layout"));
        Assert.Throws<ArgumentException>(() => AcadScriptGen.PlotPdf("out.pdf", layout: "Sheet A3", plotArea: "Limits"));
        Assert.Contains("\nLimits\n", AcadScriptGen.PlotPdf("out.pdf", plotArea: "Limits"));
        Assert.Throws<ArgumentException>(() => AcadScriptGen.PlotPdf("out.pdf", pageSetupName: "A\nQUIT"));
    }

    [Fact]
    public void CooperativeCancellationClosesAtomicallyBeforeCommitAndScopesRestore()
    {
        Assert.Null(CommandExecution.Current);
        var execution = new CommandExecution();
        Assert.False(execution.CanCancel);
        Assert.False(execution.RequestCancellation());
        using (execution.Enter())
        {
            Assert.Same(execution, CommandExecution.Current);
            using (new CommandExecution().Enter()) Assert.NotSame(execution, CommandExecution.Current);
            Assert.Same(execution, CommandExecution.Current);
            execution.EnableCancellation();
            Assert.True(execution.CanCancel);
            execution.Report("searching", 10, 20);
            Assert.Equal("searching", execution.Progress.Stage);
            Assert.Equal(10, execution.Progress.Completed);
            Assert.Equal(20, execution.Progress.Total);
            Assert.True(execution.RequestCancellation());
            Assert.True(execution.CancellationRequested);
            Assert.Throws<OperationCanceledException>(() => execution.CancellationToken.ThrowIfCancellationRequested());
            Assert.Throws<OperationCanceledException>(() => execution.Seal());
        }
        Assert.Null(CommandExecution.Current);
        var completed = new CommandExecution();
        completed.EnableCancellation();
        completed.Seal();
        completed.EnableCancellation();
        Assert.False(completed.CanCancel);
        Assert.False(completed.RequestCancellation());
        Assert.False(completed.CancellationRequested);
    }

    [Fact]
    public void PathSearchAndCandidateSearchObserveCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        var options = new PathFinderOptions { StepMm = 100, ClearanceMm = 0, CancellationToken = cancellation.Token,
            ReportExpandedNodes = _ => cancellation.Cancel() };
        var bounds = new Box3(0, 0, 0, 10000, 10000, 0);
        Assert.Throws<OperationCanceledException>(() => PathFinder3D.FindPath(new Point3(0, 0, 0), new Point3(10000, 10000, 0),
            Array.Empty<Box3>(), bounds, options));
        Assert.Throws<OperationCanceledException>(() => RouteOptionGenerator.GenerateCandidates(new Point3(0, 0, 0),
            new Point3(1000, 1000, 0), Array.Empty<Box3>(), options));
    }

    [Fact]
    public void ClassifiedReportAndBcfRetainTheEstimateAndDirection()
    {
        var classified = ClashClassifier.Classify(1, "<pipe>", 2, "Wall", 1000, 2000, 3000, 1000, 0);
        var record = new ClashRecord(1, "<pipe>", "P", 2, "Wall", 1000, 2000, 3000, "key", null, classified);
        Assert.Same(classified, record.Classification);
        var html = ClashReport.Html("Project", new[] { "Pipe" }, new[] { "Wall" }, new[] { record }, 0);
        Assert.Contains("ước lượng hộp bao", html);
        Assert.Contains("HardClash", html);
        Assert.DoesNotContain("<pipe>", html);
        var issue = Assert.Single(ClashReport.BcfIssues("Project", new[] { record }));
        Assert.Contains("HardClash", issue.Labels);
        Assert.Contains("ước lượng hộp bao", issue.Description);
        Assert.NotNull(issue.ViewDirection);
        Assert.True(issue.ViewDistance > 0);
    }
    [Fact]
    public void NativeConsoleOutputDecodesUtf16AndUtf8WithoutNulCharacters()
    {
        const string text = "AutoCAD: lỗi nạp assembly — kiểm file.";
        foreach (var encoding in new[] { System.Text.Encoding.Unicode, System.Text.Encoding.BigEndianUnicode, System.Text.Encoding.UTF8 })
        {
            Assert.Equal(text, AcadScriptGen.DecodeConsole(encoding.GetBytes(text)));
            Assert.Equal(text, AcadScriptGen.DecodeConsole(encoding.GetPreamble().Concat(encoding.GetBytes(text)).ToArray()));
        }
        Assert.Equal("", AcadScriptGen.DecodeConsole(Array.Empty<byte>()));
        Assert.Equal("OK", AcadScriptGen.DecodeConsole(System.Text.Encoding.UTF8.GetBytes("OK")));
    }
}
