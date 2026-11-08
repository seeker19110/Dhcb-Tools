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
            database.ObjectModified += (_, _) => BridgeDocumentContext.Touch(context);
            database.ObjectAppended += (_, _) => BridgeDocumentContext.Touch(context);
            database.ObjectErased += (_, _) => BridgeDocumentContext.Touch(context);
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
}
