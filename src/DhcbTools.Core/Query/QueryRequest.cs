namespace DhcbTools.Core.Query;

/// <summary>
/// DTO nhận từ POST /query.
/// "query" xác định loại truy vấn; "params" chứa filter tuỳ chọn theo từng query type.
/// </summary>
public sealed class QueryRequest
{
    /// <summary>
    /// Loại truy vấn. Các giá trị hợp lệ:
    ///   document_info  — thông tin chung về file/project
    ///   elements       — danh sách phần tử theo category + tham số tuỳ chọn
    ///   levels         — danh sách tầng + cao độ
    ///   views          — tất cả view (tên, type, scale, template, sheet)
    ///   sheets         — tất cả sheet (số, tên, revision, views)
    ///   rooms          — phòng + area / perimeter / level
    ///   families       — family đã load + types
    ///   warnings       — cảnh báo mô hình (Manage → Review Warnings)
    ///   links          — RVT link đang gắn
    ///   stats          — đếm element theo từng category
    ///
    /// Giai đoạn 10.1 — đủ để agent nhìn, chỉ và kiểm được kết quả:
    ///   element_geometry — hộp bao, đường tâm, connector, host/level của phần tử
    ///   schedule_rows    — bảng thống kê dạng hàng (không ghi file)
    ///   parameters_of    — tham số của một category: tên, kiểu, đơn vị, chỉ đọc
    ///   snapshot         — ảnh PNG của view (base64) để agent nhìn thấy kết quả
    ///   selection        — phần tử đang chọn; kèm elementIds thì ĐẶT lựa chọn (vỏ Revit)
    ///   show_elements    — zoom tới phần tử (vỏ Revit)
    ///   active_view      — kỹ sư đang nhìn view nào (vỏ Revit)
    /// </summary>
    public string Query { get; set; } = string.Empty;

    /// <summary>Params tuỳ theo loại query (xem từng handler).</summary>
    public QueryParams Params { get; set; } = new();
}

public sealed class QueryParams
{
    /// <summary>[elements] Danh sách category cần lọc (rỗng = mọi category).</summary>
    public List<string> Categories { get; set; } = new();

    /// <summary>[elements] Danh sách tên tham số cần đọc.</summary>
    public List<string> ParameterNames { get; set; } = new();

    /// <summary>[elements, rooms] Lọc theo tên level (rỗng = mọi level).</summary>
    public string? Level { get; set; }

    /// <summary>[views] Lọc theo ViewType string (Floor Plan, Section, 3D View, …).</summary>
    public string? ViewType { get; set; }

    /// <summary>[families] Lọc theo family name (substring, case-insensitive).</summary>
    public string? FamilyNameContains { get; set; }

    /// <summary>Giới hạn số record trả về (0 = không giới hạn).</summary>
    public int Limit { get; set; } = 0;

    // ── Giai đoạn 10.1 ────────────────────────────────────────────────────────

    /// <summary>[element_geometry, selection, show_elements] ElementId cần xem/chọn/zoom.</summary>
    public List<long> ElementIds { get; set; } = new();

    /// <summary>[schedule_rows] Tên schedule; rỗng = liệt kê tên các schedule đang có.</summary>
    public string? ScheduleName { get; set; }

    /// <summary>[snapshot] Tên view cần chụp; rỗng = view đang mở.</summary>
    public string? ViewName { get; set; }

    /// <summary>[snapshot] Chiều rộng ảnh tính bằng pixel.</summary>
    public int ImageWidth { get; set; } = 1200;

    /// <summary>[parameters_of] Chỉ trả tham số ghi được (bỏ tham số chỉ đọc).</summary>
    public bool WritableOnly { get; set; }
}
