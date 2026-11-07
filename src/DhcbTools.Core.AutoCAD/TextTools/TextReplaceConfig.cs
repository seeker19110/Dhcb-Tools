namespace DhcbTools.Core.AutoCAD.TextTools;

/// <summary>Phạm vi tìm văn bản trong bản vẽ.</summary>
public enum TextReplaceScope { All, ModelSpace, PaperSpace, BlockDefinitions }

/// <summary>Cấu hình tìm/thay văn bản trong DBText, MText và attribute, mặc định toàn bộ drawing.</summary>
public sealed class TextReplaceConfig
{
    /// <summary>Chuỗi cần tìm (chuỗi thường hoặc regex nếu UseRegex=true).</summary>
    public required string Find { get; init; }

    /// <summary>Chuỗi thay thế.</summary>
    public string Replace { get; init; } = string.Empty;

    /// <summary>Coi Find là biểu thức chính quy (System.Text.RegularExpressions), có trần 2 giây cho mỗi phép khớp.</summary>
    public bool UseRegex { get; init; }

    /// <summary>Không phân biệt hoa/thường khi tìm.</summary>
    public bool IgnoreCase { get; init; }

    /// <summary>Regex: ^ và $ khớp từng dòng thay vì cả chuỗi (chỉ có nghĩa khi UseRegex = true).</summary>
    public bool Multiline { get; init; }

    /// <summary>Phạm vi tìm: All giữ hành vi cũ; có thể chọn riêng ModelSpace, PaperSpace hoặc BlockDefinitions.</summary>
    public TextReplaceScope Scope { get; init; } = TextReplaceScope.All;

    /// <summary>Chỉ tìm trên layer chứa chuỗi này (không phân biệt hoa/thường); attribute ở layer 0 dùng layer của block chèn.</summary>
    public string? LayerContains { get; init; }

    /// <summary>Khi khai, chỉ xử lý attribute có tag này (khớp chính xác, không phân biệt hoa/thường), không xử lý text thường.</summary>
    public string? AttributeTag { get; init; }

    public bool IncludeDbText { get; init; } = true;
    public bool IncludeMText { get; init; } = true;
    public bool IncludeAttributes { get; init; } = true;

    /// <summary>Số chi tiết tối đa trong preview (0–10000); tổng số thay đổi vẫn đếm đầy đủ.</summary>
    public int MaxPreviewItems { get; init; } = 200;

    /// <summary>Ngân sách lập kế hoạch 1–600 giây. Quá hạn thì huỷ toàn bộ, chưa ghi đối tượng nào.</summary>
    public int TimeBudgetSeconds { get; init; } = 30;

    /// <summary>Chỉ xem trước, không ghi vào drawing.</summary>
    public bool DryRun { get; init; } = true;
}
