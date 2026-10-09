using Autodesk.Revit.DB;

namespace DhcbTools.Core;

/// <summary>Giữ transaction group đến khi transaction con Pending được Revit xử lý xong.</summary>
public sealed class RevitTransactionGroup : IDisposable
{
    private readonly TransactionGroup _group;
    private readonly string _name;
    private bool _disposed;

    public RevitTransactionGroup(Document document, string name)
    {
        RevitTransaction.EnsureReady();
        _name = name;
        _group = new TransactionGroup(document, name);
    }

    public TransactionStatus Start() => _group.Start();

    public TransactionStatus Assimilate()
    {
        RevitTransaction.EnsureReady();
        var returned = _group.Assimilate();
        var final = _group.IsValidObject ? _group.GetStatus() : TransactionStatus.Uninitialized;
        if (returned != TransactionStatus.Committed || final != TransactionStatus.Committed)
            throw new RevitTransactionException(_name, returned, final);
        return returned;
    }

    public TransactionStatus RollBack()
    {
        if (!_group.IsValidObject) return TransactionStatus.Uninitialized;
        var status = _group.GetStatus();
        return status == TransactionStatus.Started && !RevitTransaction.HasPendingTransactions
            ? _group.RollBack() : status;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (!_group.IsValidObject) return;
        if (RevitTransaction.HasPendingTransactions || _group.GetStatus() == TransactionStatus.Pending)
            RevitTransaction.DeferGroup(_group);
        else _group.Dispose();
    }
}
