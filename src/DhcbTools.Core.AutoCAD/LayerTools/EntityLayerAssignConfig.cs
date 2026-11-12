namespace DhcbTools.Core.AutoCAD.LayerTools;

/// <summary>Cấu hình chuyển từng entity (theo handle) sang layer khác.</summary>
public sealed class EntityLayerAssignConfig
{
    /// <summary>CSV cột: Handle,Layer,Color (Color = ACI 1–255, chỉ dùng khi phải tạo layer đích; tuỳ chọn).</summary>
    public required string MapCsvPath { get; init; }

    /// <summary>Tạo layer đích nếu chưa có (mặc định có). Tắt thì dòng trỏ tới layer chưa có bị bỏ qua và báo.</summary>
    public bool CreateMissingLayers { get; init; } = true;

    /// <summary>Chỉ xem trước, không ghi vào drawing.</summary>
    public bool DryRun { get; init; } = true;
}
