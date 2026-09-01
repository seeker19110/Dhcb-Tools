namespace DhcbTools.Core;

/// <summary>
/// Kết quả trả về của một lệnh Core. Dùng chung cho vỏ desktop (hiển thị) và vỏ batch (ghi log/report),
/// để không lệnh nào phải biết mình đang chạy trong ngữ cảnh nào.
/// </summary>
public sealed class CommandResult
{
    public bool Success { get; init; }
    public string Summary { get; init; } = string.Empty;
    public List<string> Messages { get; } = new();
    public List<string> Errors { get; } = new();
    public int AffectedElementCount { get; init; }

    public static CommandResult Ok(string summary, int affected = 0) => new()
    {
        Success = true,
        Summary = summary,
        AffectedElementCount = affected,
    };

    /// <summary>
    /// Tạo kết quả mới với summary/số lượng cuối cùng nhưng **giữ nguyên** Messages và Errors đã gom
    /// trong lúc chạy. Trước đây các lệnh tạo object mới ở dòng return nên nuốt hết cảnh báo (lỗi #2).
    /// </summary>
    public CommandResult With(string summary, int affected)
    {
        var result = new CommandResult
        {
            Success = Success,
            Summary = summary,
            AffectedElementCount = affected,
        };
        result.Messages.AddRange(Messages);
        result.Errors.AddRange(Errors);
        return result;
    }

    public static CommandResult Fail(string summary, IEnumerable<string>? errors = null)
    {
        var result = new CommandResult
        {
            Success = false,
            Summary = summary,
        };
        if (errors is not null)
        {
            result.Errors.AddRange(errors);
        }
        return result;
    }
}
