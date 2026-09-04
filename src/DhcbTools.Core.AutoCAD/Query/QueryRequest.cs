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
    ///
    /// Giai đoạn 10.1 — đủ để agent nhìn, chỉ và kiểm được kết quả:
    ///   entity_geometry — hộp bao + chi tiết theo loại của entity chỉ đích danh (theo handle)
    ///   attributes_of   — thuộc tính của một block: tag, prompt, ghi được không, kèm giá trị mẫu
    ///   selection       — entity đang chọn; kèm handles thì ĐẶT lựa chọn (vỏ AutoCAD)
    ///   show_entities   — zoom tới entity (vỏ AutoCAD)
    ///   active_layout   — kỹ sư đang ở layout nào (vỏ AutoCAD)
    ///   snapshot        — ảnh PNG (base64 + path) để agent nhìn thấy bản vẽ: vỏ AutoCAD render sống
    ///                     khung nhìn hiện tại; Core/accoreconsole trả ảnh xem trước lưu trong DWG
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

    /// <summary>[entity_geometry, selection, show_entities] Handle (hex) của entity cần xem/chọn/zoom.</summary>
    public List<string> Handles { get; set; } = new();

    /// <summary>Giới hạn số record trả về (0 = không giới hạn).</summary>
    public int Limit { get; set; } = 0;

    /// <summary>[snapshot] Chiều rộng ảnh (pixel) khi render sống; kẹp 200–4000. Ảnh xem trước thì cỡ có sẵn.</summary>
    public int ImageWidth { get; set; } = 1200;

    /// <summary>
    /// [snapshot] "live" (mặc định) = render khung nhìn hiện tại, cần AutoCAD đang mở; "thumbnail" = ảnh xem
    /// trước lưu trong DWG lúc save, chạy được cả trong accoreconsole. Kết quả luôn nói rõ nó là loại nào.
    /// </summary>
    public string Source { get; set; } = "live";
}
