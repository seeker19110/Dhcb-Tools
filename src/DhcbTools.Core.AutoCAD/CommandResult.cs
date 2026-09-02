namespace DhcbTools.Core.AutoCAD;

/// <summary>
/// Kết quả trả về của một lệnh Core AutoCAD. Giữ cùng hình dạng với Revit để dễ tích hợp chung
/// vào một batch runner hoặc UI layer về sau.
/// </summary>
public sealed class CommandResult
{
    public bool Success { get; init; }
    public string Summary { get; init; } = string.Empty;
    public List<string> Messages { get; } = new();
    public List<string> Errors { get; } = new();
    public int AffectedCount { get; init; }

    public static CommandResult Ok(string summary, int affected = 0) => new()
    {
        Success = true,
        Summary = summary,
        AffectedCount = affected,
    };

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
