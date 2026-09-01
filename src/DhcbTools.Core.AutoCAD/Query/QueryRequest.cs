namespace DhcbTools.Core.AutoCAD.Query;

/// <summary>
/// DTO nhận từ POST /query (AutoCAD).
/// </summary>
public sealed class QueryRequest
{
    /// <summary>
    /// Loại truy vấn. Các giá trị hợp lệ:
    ///   drawing_info  — thông tin chung về file DWG
    ///   layers        — danh sách layer + thuộc tính
    ///   blocks        — block definitions + insert count
    ///   inserts       — BlockReference instances trong Model Space
    ///   entities      — entities theo layer / type
    ///   text          — nội dung DBText và MText
    ///   xrefs         — external reference đang attach
    ///   layouts       — danh sách layout (Model + Paper spaces)
    ///   stats         — đếm entity theo type và layer
    /// </summary>
    public string Query { get; set; } = string.Empty;

    public AcadQueryParams Params { get; set; } = new();
}

public sealed class AcadQueryParams
{
    /// <summary>[entities, inserts] Lọc theo tên layer (substring, case-insensitive).</summary>
    public string? LayerContains { get; set; }

    /// <summary>[entities] Lọc theo loại entity (Line, Arc, Text, …).</summary>
    public string? EntityType { get; set; }

    /// <summary>[inserts] Lọc theo tên block (exact, case-insensitive).</summary>
    public string? BlockName { get; set; }

    /// <summary>[blocks] Chỉ lấy block có tên chứa chuỗi này.</summary>
    public string? BlockNameContains { get; set; }

    /// <summary>[text] Lọc theo layer.</summary>
    public string? TextLayer { get; set; }

    /// <summary>Giới hạn số record trả về (0 = không giới hạn).</summary>
    public int Limit { get; set; } = 0;
}
