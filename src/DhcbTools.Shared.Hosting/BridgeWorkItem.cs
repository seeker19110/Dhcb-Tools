using System;
using System.Threading;
using System.Threading.Tasks;

namespace DhcbTools.Shared.Hosting
{
    /// <summary>
    /// Một việc chờ luồng UI của Revit/AutoCAD xử lý. Sửa lỗi #7 (mục 0.5): khi client hết thời gian chờ,
    /// Bridge đặt <see cref="Abandoned"/> TRƯỚC khi trả 504; vòng lặp thực thi kiểm tra cờ này ngay trước
    /// khi mở transaction và bỏ qua việc đã bị bỏ — lệnh <c>dryRun:false</c> không còn chạy "mồ côi".
    /// <para>
    /// Ba trạng thái, chuyển một chiều: <c>Pending</c> → <c>Claimed</c> (phía thực thi đã nhận, sẽ chạy nốt)
    /// hoặc <c>Pending</c> → <c>Abandoned</c> (client bỏ đi trước khi ai nhận). Nhờ vậy Bridge biết chắc khi
    /// timeout: <see cref="MarkAbandoned"/> trả <c>true</c> nghĩa là lệnh KHÔNG chạy; trả <c>false</c> nghĩa
    /// là lệnh có thể đã/đang chạy và client không được gửi lại.
    /// </para>
    /// </summary>
    public sealed class BridgeWorkItem<TRequest, TResult>
    {
        private const int Pending = 0;
        private const int AbandonedState = 1;
        private const int ClaimedState = 2;

        private int _state;

        public BridgeWorkItem(TRequest request)
        {
            Request = request;
            Completion = new TaskCompletionSource<TResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public TRequest Request { get; }

        public TaskCompletionSource<TResult> Completion { get; }

        /// <summary>Client đã bỏ đi (timeout hoặc ngắt kết nối) trước khi phía thực thi nhận việc.</summary>
        public bool Abandoned => Volatile.Read(ref _state) == AbandonedState;

        /// <summary>Phía thực thi đã nhận việc (<see cref="TryClaim"/> trả <c>true</c>) — lệnh đã/đang chạy.</summary>
        public bool Claimed => Volatile.Read(ref _state) == ClaimedState;

        /// <summary>Gọi đúng một lần khi việc được nhận (dùng để đổi trạng thái job nền sang "đang chạy").</summary>
        public Action? OnClaimed { get; set; }

        /// <summary>
        /// Đánh dấu bỏ. Trả <c>true</c> nếu việc còn chưa ai nhận (chắc chắn không chạy); <c>false</c> nếu
        /// phía thực thi đã nhận rồi — khi đó lệnh vẫn chạy nốt và kết quả phải giữ lại cho client hỏi sau.
        /// </summary>
        public bool MarkAbandoned() => Interlocked.CompareExchange(ref _state, AbandonedState, Pending) == Pending;

        /// <summary>
        /// Phía thực thi gọi ngay trước khi chạy: trả <c>true</c> nếu được phép chạy (client còn chờ),
        /// <c>false</c> nếu việc đã bị bỏ. Không có cửa sổ đua: sau khi TryClaim trả true, MarkAbandoned
        /// không đặt được cờ nữa — lệnh đã mở transaction thì chạy nốt, huỷ giữa chừng nguy hiểm hơn.
        /// </summary>
        public bool TryClaim()
        {
            var previous = Interlocked.CompareExchange(ref _state, ClaimedState, Pending);
            if (previous == Pending)
            {
                try { OnClaimed?.Invoke(); } catch { /* chỉ là thông báo trạng thái */ }
                return true;
            }

            return previous == ClaimedState;
        }
    }
}
