namespace DhcbTools.Core.ParameterSync;

/// <summary>Cấu hình cho lệnh xuất tham số ra CSV/Excel (Giai đoạn nền tảng, lệnh #1).</summary>
public sealed class ParameterExportConfig
{
    /// <summary>Tên category cần xuất, ví dụ "Doors", "Walls", "Duct Fittings".</summary>
    public required List<string> Categories { get; init; }

    /// <summary>Tên các tham số cần xuất (Instance hoặc Type).</summary>
    public required List<string> ParameterNames { get; init; }

    /// <summary>Đường dẫn file CSV đầu ra.</summary>
    public required string OutputPath { get; init; }

    /// <summary>Ghi thời gian chạy vào Messages (đo hiệu năng, mục 0.6).</summary>
    public bool Verbose { get; init; }
}

/// <summary>Cấu hình cho lệnh nhập tham số từ CSV/Excel (ghi ngược giá trị đã chỉnh vào mô hình).</summary>
public sealed class ParameterImportConfig
{
    /// <summary>Đường dẫn file CSV đầu vào (đúng định dạng do lệnh xuất tạo ra).</summary>
    public required string InputPath { get; init; }

    /// <summary>Chỉ ghi thử, không commit — dùng để kỹ sư xem trước thay đổi. Mặc định bật như mọi lệnh ghi.</summary>
    public bool DryRun { get; init; } = true;
}
