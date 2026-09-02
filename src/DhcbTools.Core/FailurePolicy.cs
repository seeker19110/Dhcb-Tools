namespace DhcbTools.Core;

/// <summary>
/// Cách xử lý cảnh báo/lỗi Revit trong transaction của lệnh Core — do <b>vỏ</b> quyết định, Core không tự chọn.
/// </summary>
public enum FailurePolicy
{
    /// <summary>Ribbon: không can thiệp, Revit hiện hộp thoại cảnh báo cho kỹ sư quyết định.</summary>
    Interactive,

    /// <summary>
    /// Bridge/MCP: không có ai ngồi máy để bấm hộp thoại, nên bỏ qua cảnh báo (Warning) nhưng ghi lại
    /// nội dung vào <see cref="CoreContext.SuppressedWarnings"/> để trả về trong <c>CommandResult</c>;
    /// lỗi (Error) vẫn làm transaction rollback như bình thường.
    /// </summary>
    SuppressWarnings,

    /// <summary>Batch đêm: bỏ cảnh báo và tự chấp nhận cách giải quyết mặc định của lỗi có resolution.</summary>
    Silent,
}

/// <summary>
/// Ngữ cảnh chạy của lệnh Core, đặt bởi vỏ (Ribbon/Bridge/batch) trước khi gọi <c>RevitCommandTable.Dispatch</c>.
/// Revit API chỉ chạy trên một luồng nên dùng <c>[ThreadStatic]</c> là đủ.
/// </summary>
public static class CoreContext
{
    [ThreadStatic] private static FailurePolicy? _policy;
    [ThreadStatic] private static List<string>? _suppressed;

    /// <summary>Mặc định <see cref="FailurePolicy.Interactive"/> — vỏ nào không đặt thì kỹ sư vẫn thấy cảnh báo.</summary>
    public static FailurePolicy FailurePolicy
    {
        get => _policy ?? FailurePolicy.Interactive;
        set => _policy = value;
    }

    /// <summary>Cảnh báo đã bị bỏ qua trong lệnh hiện tại (mô tả từ Revit), để đưa vào <c>CommandResult.Messages</c>.</summary>
    public static List<string> SuppressedWarnings => _suppressed ??= new List<string>();

    /// <summary>Đặt chính sách trong một phạm vi, tự trả lại giá trị cũ khi Dispose.</summary>
    public static IDisposable Use(FailurePolicy policy)
    {
        var previous = _policy;
        _policy = policy;
        return new Restore(previous);
    }

    private sealed class Restore : IDisposable
    {
        private readonly FailurePolicy? _previous;
        public Restore(FailurePolicy? previous) => _previous = previous;
        public void Dispose() => _policy = _previous;
    }
}
