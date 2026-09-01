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

    public static CommandResult Fail(string summary, IEnumerable<string>? errors = null) => new()
    {
        Success = false,
        Summary = summary,
        Errors = errors is null ? new List<string>() : new List<string>(errors),
    };
}
