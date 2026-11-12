// API double for the exact AutoCAD context registry linked into this assembly.
// Different managed wrappers can represent the same native database and event source.
namespace Autodesk.AutoCAD.DatabaseServices;

internal sealed class Database
{
    private sealed class NativeState
    {
        public ObjectEventHandler? Modified;
        public ObjectEventHandler? Appended;
        public ObjectEventHandler? Erased;
        public EventHandler? Destroyed;
    }
    private readonly NativeState _state;
    public IntPtr UnmanagedObject { get; }
    public Database(IntPtr pointer, Database? sameNative = null)
    {
        UnmanagedObject = pointer;
        _state = sameNative?._state ?? new NativeState();
    }
    public event ObjectEventHandler ObjectModified { add => _state.Modified += value; remove => _state.Modified -= value; }
    public event ObjectEventHandler ObjectAppended { add => _state.Appended += value; remove => _state.Appended -= value; }
    public event ObjectEventHandler ObjectErased { add => _state.Erased += value; remove => _state.Erased -= value; }
    public event EventHandler DatabaseToBeDestroyed { add => _state.Destroyed += value; remove => _state.Destroyed -= value; }
    public int ModifiedSubscriptions => _state.Modified?.GetInvocationList().Length ?? 0;
    public int AppendedSubscriptions => _state.Appended?.GetInvocationList().Length ?? 0;
    public int ErasedSubscriptions => _state.Erased?.GetInvocationList().Length ?? 0;
    public int DestroyedSubscriptions => _state.Destroyed?.GetInvocationList().Length ?? 0;
    public void Modify(string? rxClass = null) => _state.Modified?.Invoke(this, new ObjectEventArgs(rxClass));
    public void Append(string? rxClass = null) => _state.Appended?.Invoke(this, new ObjectEventArgs(rxClass));
    public void Erase(string? rxClass = null) => _state.Erased?.Invoke(this, new ObjectEventArgs(rxClass));
    public void Destroy() => _state.Destroyed?.Invoke(this, EventArgs.Empty);
}

internal delegate void ObjectEventHandler(object sender, ObjectEventArgs e);

internal sealed class ObjectEventArgs : EventArgs
{
    public ObjectEventArgs(string? rxClass) => DBObject = rxClass == null ? null : new DBObject(rxClass);
    public DBObject? DBObject { get; }
}

internal sealed class DBObject
{
    private readonly Autodesk.AutoCAD.Runtime.RXClass _class;
    public DBObject(string rxClass) => _class = new Autodesk.AutoCAD.Runtime.RXClass(rxClass);
    public Autodesk.AutoCAD.Runtime.RXClass GetRXClass() => _class;
}
