namespace DhcbTools.Shared.Logic.Batch
{
    /// <summary>Track failures within one drawing; skipped steps do not clear the preceding failure.</summary>
    public sealed class BatchStepState
    {
        private bool _previousFailed;
        private bool _stopped;

        public string? SkipReason(bool skipIfPreviousFailed)
        {
            if (_stopped) return "Dừng vì stopOnError sau lỗi ở bước trước.";
            if (skipIfPreviousFailed && _previousFailed) return "Bỏ qua vì step trước lỗi.";
            return null;
        }

        public void Observe(bool complete, bool stopOnError)
        {
            _previousFailed = !complete;
            _stopped |= !complete && stopOnError;
        }
    }
}
