using System.Threading;
using System.Threading.Tasks;

namespace DhcbTools.Shared.Hosting
{
    /// <summary>
    /// Một việc chờ luồng UI của Revit/AutoCAD xử lý. Sửa lỗi #7 (mục 0.5): khi client hết thời gian chờ,
    /// Bridge đặt <see cref="Abandoned"/> TRƯỚC khi trả 504; vòng lặp thực thi kiểm tra cờ này ngay trước
    /// khi mở transaction và bỏ qua việc đã bị bỏ — lệnh <c>dryRun:false</c> không còn chạy "mồ côi".
    /// </summary>
    public sealed class BridgeWorkItem<TRequest, TResult>
    {
        private int _abandoned;

        public BridgeWorkItem(TRequest request)
        {
            Request = request;
            Completion = new TaskCompletionSource<TResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public TRequest Request { get; }

        public TaskCompletionSource<TResult> Completion { get; }

        /// <summary>Client đã bỏ đi (timeout hoặc ngắt kết nối). Đọc bằng <see cref="TryClaim"/> phía thực thi.</summary>
        public bool Abandoned => Volatile.Read(ref _abandoned) == 1;

        public void MarkAbandoned() => Interlocked.Exchange(ref _abandoned, 1);

        /// <summary>
        /// Phía thực thi gọi ngay trước khi chạy: trả <c>true</c> nếu được phép chạy (client còn chờ),
        /// <c>false</c> nếu việc đã bị bỏ. Không có cửa sổ đua: sau khi TryClaim trả true, MarkAbandoned
        /// vẫn có thể đặt cờ nhưng lệnh đã mở transaction thì chạy nốt — huỷ giữa chừng nguy hiểm hơn.
        /// </summary>
        public bool TryClaim() => Volatile.Read(ref _abandoned) == 0;
    }
}
