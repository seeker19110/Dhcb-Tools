using Autodesk.Revit.DB;
using DhcbTools.Core;
using DhcbTools.Core.ProjectInit;
using DhcbTools.Core.Query;
using DhcbTools.Core.MEPF;
using Xunit;

namespace DhcbTools.Shared.Logic.Tests;

public class RevitAudit20261009Tests
{
    [Theory]
    [InlineData(StorageType.Double, 3048)]
    [InlineData(StorageType.Double, 0)]
    [InlineData(StorageType.String, 3048)]
    public void CorrectElevationIsUnchangedAndNeverSentToSetter(StorageType type, double millimetres)
    {
        var element = new Element();
        var parameter = new Parameter { StorageType = type, AcceptWrite = false,
            CurrentDouble = millimetres / 304.8, CurrentString = "3048.0" };
        element.InstanceParameters.Add("bottom", parameter);
        Assert.False(ElevationParameterWriter.TrySet(element, "bottomElevation", "bottom", millimetres, out var unchanged));
        Assert.True(unchanged);
        Assert.Null(parameter.Written);
    }

    [Fact]
    public void NativePendingObjectsDestroyedByHostAreRemovedWithoutApiCalls()
    {
        var doc = new Document { CommitResult = TransactionStatus.Pending };
        var tx = new RevitTransaction(doc, "pending");
        tx.Start();
        Assert.Throws<RevitTransactionException>(() => tx.Commit());
        tx.Dispose();
        var native = Assert.Single(doc.Transactions);
        native.IsValidObject = false;
        RevitTransaction.EnsureReady();
        Assert.Equal(0, native.DisposeCalls);
    }

    [Fact]
    public void OuterRouteGroupDoesNotRollbackWhileChildFailureIsPending()
    {
        var doc = new Document { CommitResult = TransactionStatus.Pending };
        var group = new RevitTransactionGroup(doc, "route");
        group.Start();
        var tx = new RevitTransaction(doc, "lines");
        tx.Start();
        Assert.Throws<RevitTransactionException>(() => tx.Commit());
        tx.Dispose();
        Assert.Equal(TransactionStatus.Started, group.RollBack());
        group.Dispose();
        var nativeGroup = Assert.Single(doc.Groups);
        Assert.Equal(0, nativeGroup.DisposeCalls);
        RevitTransaction.ReleaseFinishedTransactions();
        Assert.Equal(0, nativeGroup.RollbackCalls);
        Assert.Single(doc.Transactions).Status = TransactionStatus.RolledBack;
        RevitTransaction.ReleaseFinishedTransactions();
        Assert.Equal(1, nativeGroup.RollbackCalls);
        Assert.Equal(1, nativeGroup.DisposeCalls);
    }

    [Theory]
    [InlineData(TransactionStatus.Committed, TransactionStatus.Committed, true)]
    [InlineData(TransactionStatus.RolledBack, TransactionStatus.RolledBack, false)]
    [InlineData(TransactionStatus.Committed, TransactionStatus.RolledBack, false)]
    public void GroupAssimilationRequiresReturnedAndFinalCommit(TransactionStatus returned, TransactionStatus final, bool complete)
    {
        var doc = new Document { CommitResult = returned, FinalStatus = final };
        using var group = new RevitTransactionGroup(doc, "route");
        group.Start();
        if (complete) Assert.Equal(TransactionStatus.Committed, group.Assimilate());
        else Assert.Throws<RevitTransactionException>(() => group.Assimilate());
    }

    [Fact]
    public void PendingGroupFinalizedOrDestroyedIsReleasedSafely()
    {
        foreach (var valid in new[] { true, false })
        {
            var doc = new Document { CommitResult = TransactionStatus.Pending };
            var group = new RevitTransactionGroup(doc, "route");
            group.Start();
            Assert.Throws<RevitTransactionException>(() => group.Assimilate());
            group.Dispose();
            var native = Assert.Single(doc.Groups);
            Assert.Throws<RevitTransactionException>(() => RevitTransaction.EnsureReady());
            native.IsValidObject = valid;
            native.Status = TransactionStatus.RolledBack;
            RevitTransaction.EnsureReady();
            Assert.Equal(valid ? 1 : 0, native.DisposeCalls);
        }
    }

    [Fact]
    public void ElevationNeverWritesATypeParameterSharedByDifferentInstances()
    {
        var element = new Element();
        var typeParameter = new Parameter { StorageType = StorageType.Double };
        element.TypeParameters.Add("bottom", typeParameter);
        Assert.False(ElevationParameterWriter.TrySet(element, "bottomElevation", "bottom", 3200));
        Assert.Null(typeParameter.Written);
    }

    [Theory]
    [InlineData(StorageType.Double, true, false, true)]
    [InlineData(StorageType.Double, false, false, false)]
    [InlineData(StorageType.String, true, false, true)]
    [InlineData(StorageType.String, false, false, false)]
    [InlineData(StorageType.Integer, true, false, false)]
    [InlineData(StorageType.Double, true, true, false)]
    public void ElevationCountRequiresWritableInstanceAndHostAcceptingValue(StorageType type, bool accepts, bool readOnly, bool expected)
    {
        var element = new Element();
        var parameter = new Parameter { StorageType = type, AcceptWrite = accepts, IsReadOnly = readOnly };
        element.InstanceParameters.Add("bottom", parameter);
        Assert.Equal(expected, ElevationParameterWriter.TrySet(element, "bottomElevation", "bottom", 3048));
        if (expected && type == StorageType.Double) Assert.Equal(10d, parameter.Written);
        if (expected && type == StorageType.String) Assert.Equal("3048.0", parameter.Written);
        if (!expected) Assert.Null(parameter.Written);
    }

    [Fact]
    public void LegendOnSeveralSheetsRetainsEveryPlacementWithoutDuplicateKeyFailure()
    {
        var index = ViewPlacementIndex.Build(new[]
        {
            new KeyValuePair<long, long>(100, 1), new KeyValuePair<long, long>(100, 2),
            new KeyValuePair<long, long>(100, 1), new KeyValuePair<long, long>(200, 3),
        });
        Assert.Equal(new long[] { 1, 2 }, index[100]);
        Assert.Equal(new long[] { 3 }, index[200]);
        Assert.Empty(ViewPlacementIndex.Build(Array.Empty<KeyValuePair<long, long>>()));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(10001)]
    [InlineData(int.MaxValue)]
    public void RevitQueryCannotBypassBoundedLimit(int limit)
        => Assert.Throws<ArgumentOutOfRangeException>(() => new QueryParams { Limit = limit }.EffectiveLimit);

    [Fact]
    public void RevitQueryKeepsExistingDefaultAndExplicitLimits()
    {
        Assert.Equal(QueryParams.DefaultLimit, new QueryParams().EffectiveLimit);
        Assert.Equal(10, new QueryParams { Limit = 10 }.EffectiveLimit);
    }

    [Fact]
    public void SuccessfulCommitIsModalAndDisposedOnce()
    {
        var doc = new Document();
        var tx = new RevitTransaction(doc, "changes");
        Assert.Equal(TransactionStatus.Started, tx.Start());
        Assert.Equal(TransactionStatus.Committed, tx.Commit());
        tx.Dispose();
        tx.Dispose();
        var native = Assert.Single(doc.Transactions);
        Assert.True(native.Options.ForcedModal);
        Assert.Equal(1, native.DisposeCalls);
        Assert.Equal(0, native.RollbackCalls);
    }

    [Theory]
    [InlineData(TransactionStatus.RolledBack, null)]
    [InlineData(TransactionStatus.Committed, TransactionStatus.RolledBack)]
    public void CommitFailureDoesNotReportSuccessOrRollbackAnEndedTransaction(TransactionStatus returned, TransactionStatus? final)
    {
        var doc = new Document { CommitResult = returned, FinalStatus = final };
        using var tx = new RevitTransaction(doc, "changes");
        tx.Start();
        var ex = Assert.Throws<RevitTransactionException>(() => tx.Commit());
        Assert.Contains("E-TRANSACTION-COMMIT", ex.Message);
        Assert.Equal(TransactionStatus.RolledBack, tx.RollBack());
        Assert.Equal(0, Assert.Single(doc.Transactions).RollbackCalls);
    }

    [Fact]
    public void PendingTransactionBlocksDispatchAndIsReleasedOnlyAfterHostFinalizes()
    {
        var doc = new Document { CommitResult = TransactionStatus.Pending };
        var tx = new RevitTransaction(doc, "pending");
        tx.Start();
        Assert.Throws<RevitTransactionException>(() => tx.Commit());
        Assert.Equal(TransactionStatus.Pending, tx.RollBack());
        tx.Dispose();
        var native = Assert.Single(doc.Transactions);
        Assert.Equal(0, native.DisposeCalls);
        Assert.Throws<RevitTransactionException>(() => RevitTransaction.EnsureReady());
        RevitTransaction.ReleaseFinishedTransactions();
        Assert.Equal(0, native.DisposeCalls);
        native.Status = TransactionStatus.RolledBack; // Revit failure UI/finalizer completes externally.
        RevitTransaction.ReleaseFinishedTransactions(); // Exact method called by the App.Idling hook.
        RevitTransaction.EnsureReady();
        Assert.Equal(1, native.DisposeCalls);
        Assert.Equal(0, native.RollbackCalls);
    }

    [Theory]
    [InlineData(TransactionStatus.Committed, TransactionStatus.Committed, true)]
    [InlineData(TransactionStatus.RolledBack, TransactionStatus.RolledBack, false)]
    [InlineData(TransactionStatus.Committed, TransactionStatus.RolledBack, false)]
    public void FittingSubtransactionOnlyCountsFinishedCommit(TransactionStatus returned, TransactionStatus final, bool success)
    {
        var tx = new SubTransaction { CommitResult = returned, FinalStatus = final };
        if (success) RevitTransaction.CommitSubTransaction(tx);
        else Assert.Throws<RevitTransactionException>(() => RevitTransaction.CommitSubTransaction(tx));
    }

    [Theory]
    [InlineData(FailurePolicy.SuppressWarnings, false, 0)]
    [InlineData(FailurePolicy.SuppressWarnings, true, 0)]
    [InlineData(FailurePolicy.Silent, false, 0)]
    [InlineData(FailurePolicy.Silent, true, 1)]
    public void UnattendedErrorsRollbackSilentlyInsteadOfWaitingForUser(FailurePolicy policy, bool resolution, int attempts)
    {
        CoreContext.SuppressedWarnings.Clear();
        var accessor = new FailuresAccessor();
        accessor.Failures.Add(new FailureMessageAccessor { Severity = FailureSeverity.Error, Resolutions = resolution, Attempts = attempts });
        Assert.Equal(FailureProcessingResult.ProceedWithRollBack, new SilentFailuresPreprocessor(policy).PreprocessFailures(accessor));
        Assert.True(accessor.Options.ClearAfterRollback);
        Assert.Equal(0, accessor.ResolvedFailures);
        Assert.Contains("rollback", Assert.Single(CoreContext.SuppressedWarnings));
    }

    [Fact]
    public void SilentResolutionIsAttemptedOnceAndWarningIsRetained()
    {
        CoreContext.SuppressedWarnings.Clear();
        var accessor = new FailuresAccessor();
        accessor.Failures.Add(new FailureMessageAccessor { Severity = FailureSeverity.Warning, Text = "warning" });
        accessor.Failures.Add(new FailureMessageAccessor { Severity = FailureSeverity.Error, Resolutions = true });
        Assert.Equal(FailureProcessingResult.ProceedWithCommit, new SilentFailuresPreprocessor(FailurePolicy.Silent).PreprocessFailures(accessor));
        Assert.Equal(1, accessor.DeletedWarnings);
        Assert.Equal(1, accessor.ResolvedFailures);
        Assert.Equal(2, CoreContext.SuppressedWarnings.Count);
    }

    [Fact]
    public void InteractiveFailuresStayWithEngineerAndUnreadableDescriptionsRemainLogged()
    {
        CoreContext.SuppressedWarnings.Clear();
        var accessor = new FailuresAccessor();
        var failure = new FailureMessageAccessor { Severity = FailureSeverity.Error, ThrowDescription = true, ThrowIds = true };
        accessor.Failures.Add(failure);
        Assert.Equal(FailureProcessingResult.Continue, new SilentFailuresPreprocessor(FailurePolicy.Interactive).PreprocessFailures(accessor));
        Assert.Empty(CoreContext.SuppressedWarnings);
        Assert.Equal(FailureProcessingResult.ProceedWithRollBack, new SilentFailuresPreprocessor(FailurePolicy.SuppressWarnings).PreprocessFailures(accessor));
        Assert.Contains(Guid.Empty.ToString(), Assert.Single(CoreContext.SuppressedWarnings));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void FamilyLoaderValidatesRealHostAcceptanceAndRollsBackWholeSetOnFailure(bool dryRun)
    {
        using var folder = new FamilyFolder();
        var doc = new Document { LoadResult = path => !path.EndsWith("b.rfa", StringComparison.OrdinalIgnoreCase) };
        var result = new FamilyLoaderCommand().Execute(doc, new FamilyLoaderConfig { FamilyFolder = folder.Path, DryRun = dryRun });
        Assert.False(result.IsComplete);
        Assert.Equal(0, result.AffectedCount);
        Assert.Empty(doc.Families);
        Assert.Equal(2, doc.LoadCalls);
        Assert.Equal(1, Assert.Single(doc.Transactions).RollbackCalls);
    }

    [Theory]
    [InlineData(true, 0)]
    [InlineData(false, 2)]
    public void FamilyPreviewAndWriteShareOneTransactionAndCount(bool dryRun, int retained)
    {
        using var folder = new FamilyFolder();
        var doc = new Document();
        var result = new FamilyLoaderCommand().Execute(doc, new FamilyLoaderConfig { FamilyFolder = folder.Path, DryRun = dryRun });
        Assert.True(result.IsComplete);
        Assert.Equal(2, result.AffectedCount);
        Assert.Equal(retained, doc.Families.Count);
        Assert.Equal(2, doc.LoadCalls);
        Assert.Single(doc.Transactions);
    }

    [Fact]
    public void FamilyCommitRollbackReportsFailureWithoutInflatedCount()
    {
        using var folder = new FamilyFolder();
        var doc = new Document { CommitResult = TransactionStatus.RolledBack };
        var result = new FamilyLoaderCommand().Execute(doc, new FamilyLoaderConfig { FamilyFolder = folder.Path, DryRun = false });
        Assert.False(result.IsComplete);
        Assert.Equal(0, result.AffectedCount);
        Assert.Empty(doc.Families);
        Assert.Equal(0, Assert.Single(doc.Transactions).RollbackCalls);
    }

    private sealed class FamilyFolder : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dhcb-family-audit-" + Guid.NewGuid().ToString("N"));
        public FamilyFolder()
        {
            Directory.CreateDirectory(Path);
            File.WriteAllText(System.IO.Path.Combine(Path, "a.rfa"), "API double fixture");
            File.WriteAllText(System.IO.Path.Combine(Path, "b.rfa"), "API double fixture");
        }
        public void Dispose() => Directory.Delete(Path, true);
    }
}
