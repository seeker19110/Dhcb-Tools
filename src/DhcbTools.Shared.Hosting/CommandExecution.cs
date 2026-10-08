using System;
using System.Threading;
using Newtonsoft.Json;

namespace DhcbTools.Shared.Hosting
{
    /// <summary>Hủy hợp tác có điểm đóng trước commit; không gián đoạn cưỡng bức host.</summary>
    public sealed class CommandExecution
    {
        private static readonly AsyncLocal<CommandExecution?> CurrentSlot = new AsyncLocal<CommandExecution?>();
        private readonly object _gate = new object();
        private readonly CancellationTokenSource _cancellation = new CancellationTokenSource();
        private bool _enabled;
        private bool _sealed;
        private CommandProgress _progress = new CommandProgress("queued", 0, 0);
        public static CommandExecution? Current => CurrentSlot.Value;
        public CancellationToken CancellationToken => _cancellation.Token;
        public bool CanCancel { get { lock (_gate) { return _enabled && !_sealed; } } }
        public bool CancellationRequested => _cancellation.IsCancellationRequested;
        public CommandProgress Progress => Volatile.Read(ref _progress);
        public void EnableCancellation() { lock (_gate) { if (!_sealed) _enabled = true; } }
        public bool RequestCancellation()
        {
            lock (_gate)
            {
                if (!_enabled || _sealed) return false;
                _cancellation.Cancel();
                return true;
            }
        }
        public void Report(string stage, long completed = 0, long total = 0)
            => Volatile.Write(ref _progress, new CommandProgress(stage, completed, total));
        public void Seal()
        {
            lock (_gate)
            {
                _cancellation.Token.ThrowIfCancellationRequested();
                _sealed = true;
            }
        }
        public IDisposable Enter()
        {
            var previous = CurrentSlot.Value;
            CurrentSlot.Value = this;
            return new Scope(previous);
        }
        private sealed class Scope : IDisposable
        {
            private readonly CommandExecution? _previous;
            public Scope(CommandExecution? previous) { _previous = previous; }
            public void Dispose() { CurrentSlot.Value = _previous; }
        }
    }

    public sealed class CommandProgress
    {
        public CommandProgress(string stage, long completed, long total)
        { Stage = stage; Completed = completed; Total = total; }
        [JsonProperty("stage")]
        public string Stage { get; }
        [JsonProperty("completed")]
        public long Completed { get; }
        [JsonProperty("total")]
        public long Total { get; }
    }
}
