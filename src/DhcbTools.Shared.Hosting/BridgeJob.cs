using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace DhcbTools.Shared.Hosting
{
    /// <summary>Trạng thái một lệnh chạy nền qua Bridge.</summary>
    public enum BridgeJobStatus
    {
        /// <summary>Đang xếp hàng hoặc đang chạy trên luồng UI.</summary>
        Running,

        /// <summary>Chạy xong (kể cả khi lệnh trả về Success=false — đó là kết quả, không phải lỗi hạ tầng).</summary>
        Done,

        /// <summary>Ném exception ngoài lệnh.</summary>
        Error,
    }

    /// <summary>
    /// Một lệnh chạy nền (giai đoạn 10.5). Client gửi <c>POST /execute</c> kèm <c>"async": true</c>,
    /// nhận ngay <c>202</c> với <c>id</c>, rồi hỏi <c>GET /progress/&lt;id&gt;</c> cho tới khi xong.
    /// <para>
    /// Vì sao cần: Revit chỉ có một luồng, và <c>SleeveAuto</c>/<c>AutoRoute</c>/<c>HangerAuto</c> trên
    /// model thật chạy hàng chục giây (vòng ghi thật đo được 26,6 s cho 1120 hanger). Giữ một kết nối
    /// HTTP suốt thời gian đó là mong manh: client, proxy hay chính người dùng ngắt giữa chừng là mất
    /// kết quả của một việc đã chạy xong. Có id để hỏi lại thì kết quả không mất theo kết nối.
    /// </para>
    /// </summary>
    public sealed class BridgeJob
    {
        private int _state;
        private object? _result;
        private string? _error;

        public BridgeJob(string id, string command, DateTime startedUtc)
        {
            Id = id;
            Command = command;
            StartedUtc = startedUtc;
        }

        public string Id { get; }

        public string Command { get; }

        public DateTime StartedUtc { get; }

        public DateTime? FinishedUtc { get; private set; }

        public BridgeJobStatus Status => (BridgeJobStatus)Volatile.Read(ref _state);

        /// <summary>Kết quả lệnh khi <see cref="Status"/> là <see cref="BridgeJobStatus.Done"/>.</summary>
        public object? Result => Volatile.Read(ref _result);

        /// <summary>Mô tả lỗi khi <see cref="Status"/> là <see cref="BridgeJobStatus.Error"/>.</summary>
        public string? Error => Volatile.Read(ref _error);

        /// <summary>Thời gian đã chạy (hoặc đã chạy hết) tính bằng ms.</summary>
        public long ElapsedMs(DateTime utcNow) =>
            (long)((FinishedUtc ?? utcNow) - StartedUtc).TotalMilliseconds;

        public void Complete(object result, DateTime utcNow)
        {
            Volatile.Write(ref _result, result);
            FinishedUtc = utcNow;
            Volatile.Write(ref _state, (int)BridgeJobStatus.Done);
        }

        public void Fail(string error, DateTime utcNow)
        {
            Volatile.Write(ref _error, error);
            FinishedUtc = utcNow;
            Volatile.Write(ref _state, (int)BridgeJobStatus.Error);
        }
    }

    /// <summary>
    /// Sổ lệnh chạy nền, có giới hạn để không phình mãi trong một phiên Revit mở cả ngày:
    /// lệnh đã xong quá <see cref="MaxAge"/> thì bỏ, và không bao giờ giữ quá <see cref="MaxCount"/> mục
    /// (bỏ mục xong lâu nhất trước). Lệnh **đang chạy** không bao giờ bị bỏ — mất nó là client không còn
    /// cách nào biết kết quả.
    /// </summary>
    public sealed class BridgeJobStore
    {
        private readonly object _gate = new object();
        private readonly Dictionary<string, BridgeJob> _jobs = new Dictionary<string, BridgeJob>(StringComparer.Ordinal);

        /// <summary>Giữ kết quả bao lâu sau khi lệnh xong. Đủ để client chậm quay lại hỏi.</summary>
        public TimeSpan MaxAge { get; set; } = TimeSpan.FromMinutes(30);

        /// <summary>Số mục tối đa giữ lại.</summary>
        public int MaxCount { get; set; } = 50;

        public int Count
        {
            get { lock (_gate) { return _jobs.Count; } }
        }

        public BridgeJob Add(string command, DateTime utcNow, string? id = null)
        {
            var job = new BridgeJob(id ?? Guid.NewGuid().ToString("N").Substring(0, 12), command, utcNow);
            lock (_gate)
            {
                _jobs[job.Id] = job;
            }
            Prune(utcNow);
            return job;
        }

        public BridgeJob? Find(string id)
        {
            lock (_gate)
            {
                return _jobs.TryGetValue(id, out var job) ? job : null;
            }
        }

        /// <summary>Bỏ mục đã xong quá hạn hoặc vượt số lượng. Trả về số mục đã bỏ.</summary>
        public int Prune(DateTime utcNow)
        {
            lock (_gate)
            {
                var finished = _jobs.Values
                    .Where(j => j.Status != BridgeJobStatus.Running && j.FinishedUtc.HasValue)
                    .OrderBy(j => j.FinishedUtc!.Value)
                    .ToList();

                var drop = finished.Where(j => utcNow - j.FinishedUtc!.Value > MaxAge).ToList();

                var keepTarget = _jobs.Count - drop.Count;
                foreach (var job in finished.Except(drop))
                {
                    if (keepTarget <= MaxCount)
                    {
                        break;
                    }
                    drop.Add(job);
                    keepTarget--;
                }

                foreach (var job in drop)
                {
                    _jobs.Remove(job.Id);
                }

                return drop.Count;
            }
        }
    }
}
