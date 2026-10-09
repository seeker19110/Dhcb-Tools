using DhcbTools.Shared.Logic.Batch;
using Newtonsoft.Json.Linq;
using Xunit;

namespace DhcbTools.Shared.Logic.Tests;

public class BatchAudit20261009Tests
{
    private static JObject Job() => JObject.Parse("""
        {"app":"autocad","saveMode":"None","files":[{"path":"a.dwg"}],
         "steps":[{"command":"LayerExport","config":{}}]}
        """);

    [Theory]
    [InlineData("files")]
    [InlineData("steps")]
    [InlineData("tokens")]
    [InlineData("app")]
    public void NullCollectionsAndAppAreConfigurationErrors(string field)
    {
        var job = Job();
        job[field] = null;
        Assert.Contains(field, Assert.Throws<InvalidDataException>(() => BatchJob.Parse(job.ToString())).Message);
    }

    [Theory]
    [InlineData("files", null)]
    [InlineData("steps", null)]
    [InlineData("files", "worksets")]
    [InlineData("files", "onlySteps")]
    [InlineData("steps", "config")]
    public void NullNestedValuesAreRejectedBeforeOpeningHost(string list, string? field)
    {
        var job = Job();
        if (field is null) job[list]![0] = null;
        else job[list]![0]![field] = null;
        Assert.Contains(field ?? list, Assert.Throws<InvalidDataException>(() => BatchJob.Parse(job.ToString())).Message);
    }

    [Theory]
    [InlineData("Typo")]
    [InlineData("")]
    [InlineData(null)]
    public void UnknownOrEmptyOnlyStepsCannotSilentlySkipWork(string? command)
    {
        var job = Job();
        job["files"]![0]!["onlySteps"] = new JArray(command is null ? JValue.CreateNull() : new JValue(command));
        Assert.Contains("onlySteps", Assert.Throws<InvalidDataException>(() => BatchJob.Parse(job.ToString())).Message);
    }

    [Fact]
    public void OnlyStepsStillMatchesCaseInsensitively()
    {
        var json = Job();
        json["files"]![0]!["onlySteps"] = new JArray("layerexport");
        var job = BatchJob.Parse(json.ToString());
        Assert.Single(job.StepsFor(job.Files[0]));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(99)]
    public void UndefinedSaveModeIsRejected(int mode)
    {
        var job = Job();
        job["saveMode"] = mode;
        Assert.Contains("saveMode", Assert.Throws<InvalidDataException>(() => BatchJob.Parse(job.ToString())).Message);
    }

    [Fact]
    public void SaveAsCannotOverwriteAnotherDrawingsCopy()
    {
        var job = Job();
        job["saveMode"] = "SaveAs";
        job["outputFolder"] = "out";
        job["files"] = new JArray(new JObject { ["path"] = "P:/A/plan.dwg" }, new JObject { ["path"] = "P:\\B\\PLAN.dwg" });
        Assert.Contains("tên file trùng", Assert.Throws<InvalidDataException>(() => BatchJob.Parse(job.ToString())).Message);
        job["saveMode"] = "None";
        Assert.Equal(2, BatchJob.Parse(job.ToString()).Files.Count);
    }

    [Fact]
    public void TwoRunsAtTheSameTimeUseDifferentStagingFiles()
    {
        var target = Path.Combine(Path.GetTempPath(), "fixture.dwg");
        var time = new DateTime(2026, 10, 9, 23, 0, 0);
        var first = StagedSave.StagingPath(target, time);
        var second = StagedSave.StagingPath(target, time);
        Assert.NotEqual(first, second);
        Assert.Equal(Path.GetDirectoryName(target), Path.GetDirectoryName(first));
        Assert.Equal(".dwg", Path.GetExtension(first));
    }

    [Fact]
    public void SimultaneousRunsHaveSeparateLogsAndWorkspaces()
    {
        var dir = Path.Combine(Path.GetTempPath(), "dhcb-run-audit-" + Guid.NewGuid().ToString("N"));
        try
        {
            var time = new DateTime(2026, 10, 9, 23, 0, 0);
            var logs = Enumerable.Range(0, 10).AsParallel().Select(_ => RunArtifacts.CreateLog(dir, time)).ToArray();
            Assert.Equal(10, logs.Distinct().Count());
            RunLog.Append(logs[0], new RunLogEntry { Success = true, Command = "A" });
            Assert.Single(RunLog.ReadAll(logs[0]));
            Assert.All(logs.Skip(1), log => Assert.Empty(File.ReadAllText(log)));
            Assert.True(RunLog.VerifyFile(logs[0]).Ok);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void GuardSkipsDependentStepsAndCanRecoverWithAnIndependentStep()
    {
        var state = new BatchStepState();
        Assert.Null(state.SkipReason(true));
        state.Observe(complete: false, stopOnError: false);
        Assert.NotNull(state.SkipReason(true));
        Assert.NotNull(state.SkipReason(true));
        Assert.Null(state.SkipReason(false));
        state.Observe(complete: true, stopOnError: false);
        Assert.Null(state.SkipReason(true));
        state.Observe(complete: false, stopOnError: true);
        Assert.Contains("stopOnError", state.SkipReason(false));
    }

    [Fact]
    public void ScriptRequiresGuardAwarePluginAndPreservesPolicy()
    {
        var json = JObject.Parse(AcadScriptGen.StepJson("LayerExport", "{}", "batch", true, true));
        Assert.Equal("batch", (string?)json["batchId"]);
        Assert.True((bool)json["stopOnError"]!);
        Assert.True((bool)json["skipIfPreviousFailed"]!);
        var script = AcadScriptGen.Build("plugin.dll", new[] { "step.json" }, null, "run.jsonl", "a.dwg", guardedBatch: true);
        Assert.Contains("DHCB_BATCH\n", script);
        Assert.DoesNotContain("DHCB_RUN\n", script);
    }

    [Fact]
    public void NativePlotsCannotSilentlyMoveAfterLaterCoreSteps()
    {
        var job = Job();
        ((JArray)job["steps"]!).Insert(0, new JObject { ["command"] = "PlotPdf" });
        Assert.Contains("PlotPdf", Assert.Throws<InvalidDataException>(() => BatchJob.Parse(job.ToString())).Message);
        job["files"]![0]!["onlySteps"] = new JArray("LayerExport");
        var parsed = BatchJob.Parse(job.ToString());
        Assert.Single(parsed.StepsFor(parsed.Files[0]));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("QA Model 1 100")]
    public void PdfPathsAndNamesWithSpacesAreSingleScriptAnswers(string? setup)
    {
        var script = AcadScriptGen.PlotPdf("C:/QA folder/drawing report.pdf", layout: "Sheet A3", pageSetupName: setup,
            plotStyle: "company style.ctb");
        Assert.Contains("\n\"C:/QA folder/drawing report.pdf\"\n", script);
        Assert.Contains("\n\"Sheet A3\"\n", script);
        Assert.Contains("\nDWG To PDF.pc3\n", script);
        if (setup is null) Assert.Contains("\ncompany style.ctb\n", script);
        else Assert.Contains("\nQA Model 1 100\n", script);
    }
}
