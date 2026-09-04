namespace DhcbTools.Core.AutoCAD.TextTools;

/// <summary>Cấu hình tìm/thay văn bản trong DBText, MText, AttributeReference của toàn bộ drawing.</summary>
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

    /// <summary>Chỉ xem trước, không ghi vào drawing.</summary>
    public bool DryRun { get; init; } = true;
}
