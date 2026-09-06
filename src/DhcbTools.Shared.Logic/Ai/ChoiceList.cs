namespace DhcbTools.Shared.Logic.Ai;

/// <summary>
/// Danh sách gợi ý cho một trường form, kèm câu trả lời cho câu hỏi mà một <c>IReadOnlyList</c> trần
/// không trả lời được: <b>rỗng vì mô hình không có gì, hay rỗng vì đọc hỏng?</b>
/// <para>
/// Trước §64, <c>ModelChoices</c> bắt mọi lỗi rồi trả danh sách rỗng, còn form ghi lên nhãn
/// "mô hình chưa có giá trị nào — gõ tay". Trên một mô hình có link lỗi hay family hỏng, đó là một
/// lời khẳng định sai về mô hình, và kỹ sư gõ tay tên category mà không biết mình đang gõ mò.
/// Nay lỗi đi kèm danh sách: tên nào đọc được vẫn giữ (cận dưới), phần hỏng thì nói ra.
/// </para>
/// </summary>
public sealed class ChoiceList
{
    public ChoiceList(IReadOnlyList<string> names, string? error)
    {
        Names = names;
        Error = error;
    }

    public static ChoiceList Empty { get; } = new ChoiceList(Array.Empty<string>(), null);

    /// <summary>Tên đọc được. Khi <see cref="Error"/> khác null đây là cận dưới, không phải toàn bộ mô hình.</summary>
    public IReadOnlyList<string> Names { get; }

    /// <summary>Null nếu đọc trọn vẹn; ngược lại là lý do dừng giữa chừng.</summary>
    public string? Error { get; }

    public bool IsComplete => Error == null;

    public int Count => Names.Count;

    /// <summary>Giữ lại những gì đã gom được trước khi lỗi ném ra, kèm thông điệp của ngoại lệ.</summary>
    public static ChoiceList Failed(IEnumerable<string> collected, Exception exception) =>
        new ChoiceList(collected.ToList(), exception.Message);

    /// <summary>Chú thích gắn lên nhãn trường trong form; null nghĩa là không có gì phải nói thêm.</summary>
    public string? Note() => Error == null
        ? null
        : Count == 0
            ? "không đọc được danh sách từ mô hình — gõ tay"
            : $"danh sách chưa đầy đủ ({Count} giá trị đọc được) — gõ tay nếu thiếu";
}
