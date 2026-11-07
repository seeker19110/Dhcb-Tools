// API double for the exact AutoCAD context registry linked into this assembly.
// Different managed wrappers can represent the same native database and event source.
namespace Autodesk.AutoCAD.DatabaseServices;

internal sealed class Database
{
    private sealed class NativeState
    {
        public EventHandler? Modified;
        public EventHandler? Appended;
        public EventHandler? Erased;
        public EventHandler? Destroyed;
    }
    private readonly NativeState _state;
    public IntPtr UnmanagedObject { get; }
    public Database(IntPtr pointer, Database? sameNative = null)
    {
        UnmanagedObject = pointer;
        _state = sameNative?._state ?? new NativeState();
    }
    public event EventHandler ObjectModified { add => _state.Modified += value; remove => _state.Modified -= value; }
    public event EventHandler ObjectAppended { add => _state.Appended += value; remove => _state.Appended -= value; }
    public event EventHandler ObjectErased { add => _state.Erased += value; remove => _state.Erased -= value; }
    public event EventHandler DatabaseToBeDestroyed { add => _state.Destroyed += value; remove => _state.Destroyed -= value; }
    public int ModifiedSubscriptions => _state.Modified?.GetInvocationList().Length ?? 0;
    public int AppendedSubscriptions => _state.Appended?.GetInvocationList().Length ?? 0;
    public int ErasedSubscriptions => _state.Erased?.GetInvocationList().Length ?? 0;
    public int DestroyedSubscriptions => _state.Destroyed?.GetInvocationList().Length ?? 0;
    public void Modify() => _state.Modified?.Invoke(this, EventArgs.Empty);
    public void Append() => _state.Appended?.Invoke(this, EventArgs.Empty);
    public void Erase() => _state.Erased?.Invoke(this, EventArgs.Empty);
    public void Destroy() => _state.Destroyed?.Invoke(this, EventArgs.Empty);
}
