using System;
using System.Collections.Generic;

namespace DhcbTools.Shared.Hosting
{
    /// <summary>
    /// Khoá tạm khi dò token (mục 0.1): ≥ <see cref="MaxFailures"/> lần sai trong <see cref="Window"/>
    /// thì từ chối mọi request trong <see cref="LockDuration"/>. Thuần, có thể test bằng đồng hồ giả.
    /// </summary>
    public sealed class AuthLockout
    {
        private readonly object _gate = new object();
        private readonly Queue<DateTime> _failures = new Queue<DateTime>();
        private readonly Func<DateTime> _clock;
        private DateTime? _lockedUntil;

        public AuthLockout(Func<DateTime>? clock = null, int maxFailures = 5, TimeSpan? window = null, TimeSpan? lockDuration = null)
        {
            _clock = clock ?? (() => DateTime.UtcNow);
            MaxFailures = maxFailures;
            Window = window ?? TimeSpan.FromSeconds(60);
            LockDuration = lockDuration ?? TimeSpan.FromMinutes(5);
        }

        public int MaxFailures { get; }

        public TimeSpan Window { get; }

        public TimeSpan LockDuration { get; }

        /// <summary>Đang bị khoá? Hết hạn khoá thì tự mở.</summary>
        public bool IsLocked
        {
            get
            {
                lock (_gate)
                {
                    if (_lockedUntil.HasValue && _clock() < _lockedUntil.Value)
                    {
                        return true;
                    }

                    _lockedUntil = null;
                    return false;
                }
            }
        }

        /// <summary>Ghi nhận một lần sai token. Trả <c>true</c> nếu lần này làm kích hoạt khoá.</summary>
        public bool RecordFailure()
        {
            lock (_gate)
            {
                var now = _clock();
                _failures.Enqueue(now);
                while (_failures.Count > 0 && now - _failures.Peek() > Window)
                {
                    _failures.Dequeue();
                }

                if (_failures.Count >= MaxFailures)
                {
                    _lockedUntil = now + LockDuration;
                    _failures.Clear();
                    return true;
                }

                return false;
            }
        }

        /// <summary>Đúng token → xoá lịch sử sai gần đây.</summary>
        public void RecordSuccess()
        {
            lock (_gate)
            {
                _failures.Clear();
            }
        }
    }
}
