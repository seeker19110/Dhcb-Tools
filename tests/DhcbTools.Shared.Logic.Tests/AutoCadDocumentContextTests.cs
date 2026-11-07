using Autodesk.AutoCAD.DatabaseServices;
using DhcbTools.AutoCAD.Bridge;
using DhcbTools.Shared.Hosting;
using Xunit;

namespace DhcbTools.Shared.Logic.Tests;

public sealed class AutoCadDocumentContextTests
{
    private static long _pointer = 0x10000;
    private static IntPtr NewPointer() => new(Interlocked.Increment(ref _pointer));

    [Fact]
    public void DifferentWrappersForOneNativeDatabaseKeepOneIdAndEventSubscription()
    {
        var first = new Database(NewPointer());
        var second = new Database(first.UnmanagedObject, first);
        try
        {
            var context = AcadDocumentContexts.For(first);
            for (var i = 0; i < 10; i++) Assert.Same(context, AcadDocumentContexts.For(new Database(first.UnmanagedObject, first)));
            Assert.Same(context, AcadDocumentContexts.For(second));
            Assert.Equal(BridgeDocumentContext.IdFor(context), BridgeDocumentContext.IdFor(AcadDocumentContexts.For(second)));
            Assert.Null(BridgeDocumentContext.ValidateQuery(new BridgeQuery { DocumentId = BridgeDocumentContext.IdFor(context) },
                BridgeDocumentContext.IdFor(AcadDocumentContexts.For(second))));
            Assert.Equal(1, first.ModifiedSubscriptions);
            Assert.Equal(1, first.AppendedSubscriptions);
            Assert.Equal(1, first.ErasedSubscriptions);
            Assert.Equal(1, first.DestroyedSubscriptions);
            Assert.Equal(0, BridgeDocumentContext.RevisionFor(context));
            second.Modify(); second.Append(); first.Erase();
            Assert.Equal(3, BridgeDocumentContext.RevisionFor(AcadDocumentContexts.For(second)));
        }
        finally { first.Destroy(); }
    }

    [Fact]
    public void DestroyedDatabaseCannotDonateIdentityOrRevisionToAReusedNativePointer()
    {
        var oldDatabase = new Database(NewPointer());
        var oldContext = AcadDocumentContexts.For(oldDatabase);
        oldDatabase.Modify();
        var oldId = BridgeDocumentContext.IdFor(oldContext);
        oldDatabase.Destroy();
        var newDatabase = new Database(oldDatabase.UnmanagedObject);
        try
        {
            var newContext = AcadDocumentContexts.For(newDatabase);
            Assert.NotSame(oldContext, newContext);
            Assert.NotEqual(oldId, BridgeDocumentContext.IdFor(newContext));
            Assert.Equal(0, BridgeDocumentContext.RevisionFor(newContext));
            // A delayed duplicate destroy notification must not evict the new native lifetime.
            oldDatabase.Destroy();
            Assert.Same(newContext, AcadDocumentContexts.For(newDatabase));
            newDatabase.Modify();
            Assert.Equal(1, BridgeDocumentContext.RevisionFor(newContext));
            Assert.Equal(1, BridgeDocumentContext.RevisionFor(oldContext));
            Assert.Contains("E-DOCUMENT-CHANGED", BridgeDocumentContext.ValidateQuery(new BridgeQuery { DocumentId = oldId },
                BridgeDocumentContext.IdFor(newContext)));
        }
        finally { newDatabase.Destroy(); }
    }

    [Fact]
    public void DifferentNativeDatabasesHaveIndependentRevisionAndIdentity()
    {
        var first = new Database(NewPointer());
        var second = new Database(NewPointer());
        try
        {
            var a = AcadDocumentContexts.For(first);
            var b = AcadDocumentContexts.For(second);
            Assert.NotEqual(BridgeDocumentContext.IdFor(a), BridgeDocumentContext.IdFor(b));
            first.Modify();
            Assert.Equal(1, BridgeDocumentContext.RevisionFor(a));
            Assert.Equal(0, BridgeDocumentContext.RevisionFor(b));
        }
        finally { first.Destroy(); second.Destroy(); }
    }

    [Fact]
    public void MissingOrDisposedDatabaseIsRejected()
    {
        Assert.Throws<ArgumentNullException>(() => AcadDocumentContexts.For(null!));
        Assert.Throws<InvalidOperationException>(() => AcadDocumentContexts.For(new Database(IntPtr.Zero)));
    }
}
