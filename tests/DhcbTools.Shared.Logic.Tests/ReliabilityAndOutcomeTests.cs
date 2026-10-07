using DhcbTools.Shared.Hosting;
using DhcbTools.Shared.Logic.Batch;
using Newtonsoft.Json;
using DhcbTools.Shared.Logic.Testing;
using Xunit;

namespace DhcbTools.Shared.Logic.Tests;

public class ReliabilityAndOutcomeTests
{
    [Fact]
    public void HostTestPhaiDoDuocKetQuaMotPhanVaSuKienGhiTam()
    {
        var expect = JsonConvert.DeserializeObject<TestExpectation>("{\"partialSuccess\":false,\"maxChangeEvents\":0}")!;
        Assert.Empty(expect.Evaluate(new TestObservation { Success = true, ChangeEvents = 0 }));
        var partial = expect.Evaluate(new TestObservation { Success = true, PartialSuccess = true, ChangeEvents = 1 });
        Assert.Equal(2, partial.Count);
        Assert.Contains(partial, e => e.Contains("PartialSuccess"));
        Assert.Contains(partial, e => e.Contains("sự kiện thay đổi"));
        Assert.Contains(expect.Evaluate(new TestObservation { Success = true }), e => e.Contains("host chưa đo"));
    }

    [Theory]
    [InlineData(true, false, false, true)]
    [InlineData(true, true, false, false)]
    [InlineData(true, false, true, false)]
    [InlineData(false, false, false, false)]
    public void KetQuaKhongTronVen_KhongDuocDuyetHoacLuu(bool success, bool partial, bool error, bool complete)
    {
        var result = new CommandResult { Success = success, PartialSuccess = partial };
        var entry = new RunLogEntry { Success = success, PartialSuccess = partial, Command = "Import", Summary = "test" };
        if (error) { result.Errors.Add("xung đột"); entry.Errors.Add("xung đột"); }
        Assert.Equal(complete, result.IsComplete);
        Assert.Equal(complete, entry.IsComplete);
        Assert.Equal(complete ? 0 : 1, RunLog.ExitCode(new[] { entry }));
        Assert.Equal(complete, StagedSave.Blocker(false, 0, false, 1, new[] { entry }) == null);
        Assert.DoesNotContain("IsComplete", JsonConvert.SerializeObject(result));
        Assert.DoesNotContain("IsComplete", RunLog.Serialize(entry));
        if (success && !complete)
            Assert.Contains("class=\"partial\"", BatchReport.Render("test", new[] { entry }, DateTime.Now));
    }

    [Fact]
    public void StepBoQua_KhongDuocLuuDuSuccessTrue()
    {
        var entry = new RunLogEntry { Success = true, Skipped = true };
        Assert.False(entry.IsComplete);
        Assert.NotNull(StagedSave.Blocker(false, 0, false, 1, new[] { entry }));
    }

    [Fact]
    public void WorkItem_ChiMotConsumerDuocNhanQuyenChay()
    {
        var item = new BridgeWorkItem<string, string>("ghi");
        var notifications = 0;
        item.OnClaimed = () => Interlocked.Increment(ref notifications);
        var claims = 0;
        Parallel.For(0, 128, _ => { if (item.TryClaim()) Interlocked.Increment(ref claims); });
        Assert.Equal(1, claims);
        Assert.Equal(1, notifications);
        Assert.True(item.Claimed);
        Assert.False(item.TryClaim());
    }

    [Fact]
    public void HangDoi_DongThoiKhongVuotTran()
    {
        for (var round = 0; round < 32; round++)
        {
            var store = new BridgeJobStore { MaxQueued = 2 };
            var accepted = 0;
            Parallel.For(0, 128, _ =>
            {
                if (store.TryAdd("ghi", DateTime.UtcNow, TimeSpan.FromMinutes(1)) != null)
                    Interlocked.Increment(ref accepted);
            });
            Assert.Equal(2, accepted);
            Assert.Equal(2, store.QueuedCount);
        }
    }

    [Fact]
    public void JobDaKetThuc_KhongBiCompletionMuonGhiDe()
    {
        var now = DateTime.UtcNow;
        var job = new BridgeJob("a", "ghi", now);
        job.Complete("kết quả đầu", now);
        job.Complete("kết quả muộn", now.AddSeconds(10));
        job.MarkStarted();
        Assert.Equal("kết quả đầu", job.Result);
        Assert.Equal(now, job.FinishedUtc);
        Assert.False(job.Started);

        var failed = new BridgeJob("b", "ghi", now);
        Assert.True(failed.Fail("lỗi đầu", now));
        failed.Complete("muộn", now.AddSeconds(10));
        Assert.False(failed.Fail("lỗi sau", now.AddSeconds(20)));
        Assert.Equal(BridgeJobStatus.Error, failed.Status);
        Assert.Equal("lỗi đầu", failed.Error);
        Assert.Null(failed.Result);

        var abandoned = new BridgeJob("c", "ghi", now);
        Assert.True(abandoned.Abandon("hủy", now));
        abandoned.Complete("muộn", now.AddSeconds(10));
        Assert.False(abandoned.Abandon("hủy sau", now));
        Assert.Equal(BridgeJobStatus.Abandoned, abandoned.Status);
        Assert.Null(abandoned.Result);
    }

    [Fact]
    public void TerminalDongThoi_KetQuaVaLoiKhongTronLan()
    {
        var job = new BridgeJob("a", "ghi", DateTime.UtcNow);
        Parallel.For(0, 128, i =>
        {
            if (i % 2 == 0) job.Complete("xong", DateTime.UtcNow);
            else job.Fail("lỗi", DateTime.UtcNow);
        });
        Assert.NotNull(job.FinishedUtc);
        Assert.True((job.Result != null) ^ (job.Error != null));
        Assert.Equal(job.Result != null ? BridgeJobStatus.Done : BridgeJobStatus.Error, job.Status);
    }

    [Fact]
    public void TrungId_KhongLamMatJobCu()
    {
        var store = new BridgeJobStore();
        var first = store.Add("ghi", DateTime.UtcNow, "same");
        Assert.Throws<ArgumentException>(() => store.Add("ghi", DateTime.UtcNow, "same"));
        Assert.Same(first, store.Find("same"));
    }

    [Fact]
    public void DuongRibbonCu_DiQuaDuyetChungVaBatchKhongBoQuaMotPhan()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "Dhcb-Tools.sln"))) root = root.Parent;
        Assert.NotNull(root);
        foreach (var command in new[] { "ParameterImport", "AutoNumbering", "RemoveUnusedViews" })
        {
            var source = File.ReadAllText(Path.Combine(root!.FullName, "src", "DhcbTools.Revit", "Commands", command + "Command.cs"));
            Assert.Contains($"CommandRunner.Run(commandData, \"{command}\")", source);
            Assert.DoesNotContain(".Execute(document", source);
        }
        var runner = File.ReadAllText(Path.Combine(root!.FullName, "src", "DhcbTools.Core", "Batch", "BatchJobRunner.cs"));
        Assert.Contains("anyStepFailed |= !result.IsComplete", runner);
        Assert.Contains("previousFailed = !result.IsComplete", runner);
        var acad = File.ReadAllText(Path.Combine(root.FullName, "src", "DhcbTools.AutoCAD.Core", "RunCommand.cs"));
        Assert.Contains("entry.PartialSuccess = result.PartialSuccess", acad);
    }
}
