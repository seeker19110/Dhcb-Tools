using Autodesk.AutoCAD.DatabaseServices;
using DhcbTools.Shared.Hosting;

namespace DhcbTools.AutoCAD.Bridge;

/// <summary>One session per native AcDbDatabase, even when AutoCAD returns different managed wrappers.</summary>
internal static class AcadDocumentContexts
{
    private sealed class Context
    {
        // Retain the wrapper that owns the event subscriptions for the database's lifetime.
        public Database Database { get; }
        public Context(Database database) => Database = database;
    }

    private static readonly object Gate = new();
    private static readonly Dictionary<IntPtr, Context> Contexts = new();

    /// <summary>
    /// Lớp đối tượng AutoCAD GUI tự ghi mỗi khi kết thúc một command (kể cả command context của Bridge):
    /// mạng liên kết tham số <c>AcDbAssocNetwork</c> được đánh giá lại và đánh dấu "modified" 3–4 lần, kể cả
    /// khi command chỉ đọc. Đo trên AutoCAD 2026 (DHCB_REVISION_TRACE=1): revision tăng 4 mỗi request nên
    /// mọi preview qua Bridge bị E-PREVIEW-CHANGED ở lần ghi kế tiếp. Đây là sổ sách nội bộ của framework;
    /// nếu việc đánh giá lại thật sự đổi hình học thì chính các entity bị đổi vẫn phát sự kiện và được đếm.
    /// </summary>
    internal static bool IsBookkeepingOnly(string? rxClassName) =>
        string.Equals(rxClassName, "AcDbAssocNetwork", StringComparison.Ordinal);

    /// <summary>Đặt biến môi trường DHCB_REVISION_TRACE=1 để ghi lớp đối tượng của từng sự kiện vào DhcbLog.</summary>
    private static readonly bool Trace = Environment.GetEnvironmentVariable("DHCB_REVISION_TRACE") == "1";
    private static int _traced;

    public static object For(Database database)
    {
        if (database == null) throw new ArgumentNullException(nameof(database));
        var pointer = database.UnmanagedObject;
        if (pointer == IntPtr.Zero) throw new InvalidOperationException("Bản vẽ đã đóng hoặc database không còn hợp lệ.");
        lock (Gate)
        {
            if (Contexts.TryGetValue(pointer, out var existing)) return existing;
            var context = new Context(database);
            Contexts.Add(pointer, context);
            database.ObjectModified += (_, e) => OnChange(context, "modified", ClassOf(e.DBObject), ignorable: true);
            database.ObjectAppended += (_, e) => OnChange(context, "appended", ClassOf(e.DBObject), ignorable: false);
            database.ObjectErased += (_, e) => OnChange(context, "erased", ClassOf(e.DBObject), ignorable: false);
            database.DatabaseToBeDestroyed += (_, _) =>
            {
                lock (Gate)
                {
                    // A delayed notification from a former database must not remove a reused pointer.
                    if (Contexts.TryGetValue(pointer, out var current) && ReferenceEquals(current, context))
                        Contexts.Remove(pointer);
                }
            };
            return context;
        }
    }

    private static void OnChange(Context context, string kind, string? rxClass, bool ignorable)
    {
        var ignored = ignorable && IsBookkeepingOnly(rxClass);
        if (!ignored) BridgeDocumentContext.Touch(context);
        if (Trace && Interlocked.Increment(ref _traced) <= 2000)
            DhcbLog.Write("AutoCAD", $"revision {kind} {rxClass ?? "?"}{(ignored ? " (bỏ qua)" : "")} → {BridgeDocumentContext.RevisionFor(context)}");
    }

    private static string? ClassOf(DBObject? obj)
    {
        // Sự kiện không được làm hỏng thao tác của người dùng: lỗi đọc lớp thì coi là thay đổi thật.
        try { return obj?.GetRXClass()?.Name; }
        catch (Exception) { return null; }
    }
}
