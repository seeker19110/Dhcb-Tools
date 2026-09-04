using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace DhcbTools.Shared.Hosting
{
    /// <summary>Trạng thái một lệnh chạy nền qua Bridge.</summary>
    public enum BridgeJobStatus
    {
        /// <summary>Đang xếp hàng hoặc đang chạy trên luồng UI (phân biệt bằng <see cref="BridgeJob.Started"/>).</summary>
        Running,

        /// <summary>Chạy xong (kể cả khi lệnh trả về Success=false — đó là kết quả, không phải lỗi hạ tầng).</summary>
        Done,

        /// <summary>Ném exception ngoài lệnh.</summary>
        Error,

        /// <summary>Hết hạn chờ mà chưa ai nhận việc — lệnh KHÔNG chạy và sẽ không bao giờ chạy.</summary>
        Abandoned,
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
    /// <para>
    /// Job xếp hàng cũng có hạn (<see cref="TimeoutUtc"/>): quá hạn mà luồng UI chưa nhận thì
    /// <see cref="Abandon"/> — nếu không, một Revit đang treo hộp thoại gom cả trăm lệnh rồi chạy dồn
    /// một lượt khi kỹ sư bấm OK, trong khi người gửi đã bỏ đi từ lâu. Job ĐÃ bắt đầu chạy thì không
    /// bao giờ bị huỷ — cắt giữa transaction nguy hiểm hơn để nó chạy nốt.
    /// </para>
    /// </summary>
    public sealed class BridgeJob
    {
        private int _state;
        private int _started;
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

        /// <summary>Lúc nhận lệnh (vào hàng đợi).</summary>
        public DateTime StartedUtc { get; }

        public DateTime? FinishedUtc { get; private set; }

        /// <summary>Hạn chót để luồng UI nhận việc; null = không hạn.</summary>
        public DateTime? TimeoutUtc { get; set; }

        public BridgeJobStatus Status => (BridgeJobStatus)Volatile.Read(ref _state);

        /// <summary>Luồng UI đã nhận việc (đang chạy thật). Job chưa Started là job đang xếp hàng.</summary>
        public bool Started => Volatile.Read(ref _started) == 1;

        /// <summary>
        /// Móc huỷ việc bên dưới (thường là <c>BridgeWorkItem.MarkAbandoned</c>): trả <c>false</c> nếu việc
        /// đã được nhận rồi — khi đó job không được chuyển sang Abandoned.
        /// </summary>
        public Func<bool>? TryAbandonWork { get; set; }

        /// <summary>Kết quả lệnh khi <see cref="Status"/> là <see cref="BridgeJobStatus.Done"/>.</summary>
        public object? Result => Volatile.Read(ref _result);

        /// <summary>Mô tả lỗi khi <see cref="Status"/> là <see cref="BridgeJobStatus.Error"/> hoặc <see cref="BridgeJobStatus.Abandoned"/>.</summary>
        public string? Error => Volatile.Read(ref _error);

        /// <summary>Thời gian đã chạy (hoặc đã chạy hết) tính bằng ms.</summary>
        public long ElapsedMs(DateTime utcNow) =>
            (long)((FinishedUtc ?? utcNow) - StartedUtc).TotalMilliseconds;

        public void MarkStarted() => Volatile.Write(ref _started, 1);

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

        /// <summary>
        /// Huỷ job chưa được nhận. Trả <c>true</c> nếu đã chuyển sang <see cref="BridgeJobStatus.Abandoned"/>;
        /// <c>false</c> nếu job đã bắt đầu chạy hoặc đã xong (không đụng vào).
        /// </summary>
        public bool Abandon(string reason, DateTime utcNow)
        {
            if (Status != BridgeJobStatus.Running || Started)
            {
                return false;
            }

            var hook = TryAbandonWork;
            if (hook != null && !hook())
            {
                MarkStarted();
                return false;
            }

            Volatile.Write(ref _error, reason);
            FinishedUtc = utcNow;
            Volatile.Write(ref _state, (int)BridgeJobStatus.Abandoned);
            return true;
        }

        /// <summary>Quá hạn nhận việc chưa?</summary>
        public bool IsQueuedPastDeadline(DateTime utcNow) =>
            Status == BridgeJobStatus.Running && !Started && TimeoutUtc.HasValue && utcNow >= TimeoutUtc.Value;
    }

    /// <summary>
    /// Sổ lệnh chạy nền, có giới hạn để không phình mãi trong một phiên Revit mở cả ngày:
    /// lệnh đã xong quá <see cref="MaxAge"/> thì bỏ, và không bao giờ giữ quá <see cref="MaxCount"/> mục
    /// (bỏ mục xong lâu nhất trước; mục Abandoned cũng tính là "xong"). Lệnh **đang chạy** không bao giờ
    /// bị bỏ — mất nó là client không còn cách nào biết kết quả. Hàng đợi (chưa chạy) cũng có trần
    /// <see cref="MaxQueued"/>: quá trần thì từ chối nhận thêm (429) thay vì để một client dồn lệnh vô hạn.
    /// </summary>
    public sealed class BridgeJobStore
    {
        private readonly object _gate = new object();
        private readonly Dictionary<string, BridgeJob> _jobs = new Dictionary<string, BridgeJob>(StringComparer.Ordinal);

        /// <summary>Giữ kết quả bao lâu sau khi lệnh xong. Đủ để client chậm quay lại hỏi.</summary>
        public TimeSpan MaxAge { get; set; } = TimeSpan.FromMinutes(30);

        /// <summary>Số mục tối đa giữ lại.</summary>
        public int MaxCount { get; set; } = 50;

        /// <summary>Số job được phép xếp hàng (chưa chạy) cùng lúc.</summary>
        public int MaxQueued { get; set; } = 20;

        public int Count
        {
            get { lock (_gate) { return _jobs.Count; } }
        }

        /// <summary>Số job đang xếp hàng (Running nhưng chưa Started).</summary>
        public int QueuedCount
        {
            get { lock (_gate) { return _jobs.Values.Count(j => j.Status == BridgeJobStatus.Running && !j.Started); } }
        }

        public BridgeJob Add(string command, DateTime utcNow, string? id = null, TimeSpan? timeout = null)
        {
            var job = new BridgeJob(id ?? Guid.NewGuid().ToString("N").Substring(0, 12), command, utcNow)
            {
                TimeoutUtc = timeout.HasValue ? utcNow + timeout.Value : (DateTime?)null,
            };
            lock (_gate)
            {
                _jobs[job.Id] = job;
            }
            Prune(utcNow);
            return job;
        }

        /// <summary>
        /// Như <see cref="Add"/> nhưng trả <c>null</c> khi hàng đợi đã đầy (<see cref="MaxQueued"/>).
        /// Job quá hạn nhận việc được huỷ trước khi đếm, để job "chết" không chiếm chỗ.
        /// </summary>
        public BridgeJob? TryAdd(string command, DateTime utcNow, TimeSpan timeout, string? id = null)
        {
            ExpireQueued(utcNow);
            lock (_gate)
            {
                if (_jobs.Values.Count(j => j.Status == BridgeJobStatus.Running && !j.Started) >= MaxQueued)
                {
                    return null;
                }
            }

            return Add(command, utcNow, id, timeout);
        }

        public BridgeJob? Find(string id)
        {
            lock (_gate)
            {
                return _jobs.TryGetValue(id, out var job) ? job : null;
            }
        }

        /// <summary>Huỷ mọi job xếp hàng quá hạn. Trả về số job đã huỷ.</summary>
        public int ExpireQueued(DateTime utcNow)
        {
            List<BridgeJob> expired;
            lock (_gate)
            {
                expired = _jobs.Values.Where(j => j.IsQueuedPastDeadline(utcNow)).ToList();
            }

            var count = 0;
            foreach (var job in expired)
            {
                if (job.Abandon("Hết hạn chờ: luồng UI không nhận lệnh trong thời gian cho phép — lệnh KHÔNG chạy.", utcNow))
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>Bỏ mục đã xong (Done/Error/Abandoned) quá hạn hoặc vượt số lượng. Trả về số mục đã bỏ.</summary>
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
