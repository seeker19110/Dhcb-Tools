using Autodesk.Revit.DB;

namespace DhcbTools.Core;

/// <summary>
/// Transaction có kiểm tra kết quả: Commit() của Revit có thể rollback mà không ném lỗi.
/// Không giải phóng transaction Pending khi Revit còn đang xử lý failure; giữ nó đến khi
/// lần gọi API tiếp theo xác nhận đã kết thúc. Revit API chỉ chạy trên luồng của host.
/// </summary>
public sealed class RevitTransaction : IDisposable
{
    private static readonly List<Transaction> PendingTransactions = new();
    private static readonly List<TransactionGroup> PendingGroups = new();
    private readonly Transaction _transaction;
    private readonly string _name;
    private bool _disposed;

    public RevitTransaction(Document document, string name)
    {
        EnsureReady();
        _name = name;
        _transaction = new Transaction(document, name);
    }

    public TransactionStatus Start() => _transaction.Start();
    public FailureHandlingOptions GetFailureHandlingOptions() => _transaction.GetFailureHandlingOptions();
    public void SetFailureHandlingOptions(FailureHandlingOptions options) => _transaction.SetFailureHandlingOptions(options);

    public TransactionStatus Commit()
    {
        // Luồng gọi chỉ được tiếp tục khi xử lý failure đã hoàn tất; giữ UI cảnh báo của Ribbon.
        var options = _transaction.GetFailureHandlingOptions().SetForcedModalHandling(true);
        var returned = _transaction.Commit(options);
        var final = _transaction.IsValidObject ? _transaction.GetStatus() : TransactionStatus.Uninitialized;
        if (returned != TransactionStatus.Committed || final != TransactionStatus.Committed)
            throw new RevitTransactionException(_name, returned, final);
        return returned;
    }

    public TransactionStatus RollBack()
    {
        if (!_transaction.IsValidObject) return TransactionStatus.Uninitialized;
        var status = _transaction.GetStatus();
        // Catch sau Commit rollback không được ném lỗi thứ hai và che mất lỗi gốc.
        return status == TransactionStatus.Started ? _transaction.RollBack() : status;
    }

    public static void CommitSubTransaction(SubTransaction transaction)
    {
        var returned = transaction.Commit();
        var final = transaction.GetStatus();
        if (returned != TransactionStatus.Committed || final != TransactionStatus.Committed)
            throw new RevitTransactionException("SubTransaction", returned, final);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (!_transaction.IsValidObject) return;
        if (_transaction.GetStatus() == TransactionStatus.Pending)
        {
            // Dispose của native Transaction có thể rollback; Pending cấm mọi thao tác transaction.
            PendingTransactions.Add(_transaction);
            return;
        }
        _transaction.Dispose();
    }

    /// <summary>Được gọi từ Revit Idling: chỉ giải phóng sau khi failure processing đã kết thúc.</summary>
    public static void ReleaseFinishedTransactions()
    {
        for (var i = PendingTransactions.Count - 1; i >= 0; i--)
        {
            var transaction = PendingTransactions[i];
            if (!transaction.IsValidObject)
            {
                PendingTransactions.RemoveAt(i);
                continue;
            }
            if (transaction.GetStatus() == TransactionStatus.Pending) continue;
            transaction.Dispose();
            PendingTransactions.RemoveAt(i);
        }
        // TransactionGroup.Dispose có thể rollback, phải chờ transaction con hoàn tất trước.
        if (PendingTransactions.Count > 0) return;
        for (var i = PendingGroups.Count - 1; i >= 0; i--)
        {
            var group = PendingGroups[i];
            if (!group.IsValidObject) { PendingGroups.RemoveAt(i); continue; }
            if (group.GetStatus() == TransactionStatus.Pending) continue;
            group.Dispose();
            PendingGroups.RemoveAt(i);
        }
    }

    /// <summary>Không chạy tiếp lệnh hay ghi file khi một transaction chưa xử lý failure xong.</summary>
    public static void EnsureReady()
    {
        ReleaseFinishedTransactions();
        if (PendingTransactions.Count > 0)
            throw new RevitTransactionException(PendingTransactions[0].GetName(), TransactionStatus.Pending, TransactionStatus.Pending);
        if (PendingGroups.Count > 0)
            throw new RevitTransactionException(PendingGroups[0].GetName(), TransactionStatus.Pending, TransactionStatus.Pending);
    }

    internal static bool HasPendingTransactions => PendingTransactions.Count > 0;
    internal static void DeferGroup(TransactionGroup group) => PendingGroups.Add(group);
}

/// <summary>Không báo thành công hoặc số phần tử đã ghi khi Revit không commit transaction.</summary>
public sealed class RevitTransactionException : InvalidOperationException
{
    public RevitTransactionException(string name, TransactionStatus returned, TransactionStatus final)
        : base($"E-TRANSACTION-COMMIT: Revit không commit \"{name}\" (kết quả {returned}, trạng thái {final}).")
    {
    }
}
