namespace DhcbTools.Core.AutoCAD.LayerTools;

/// <summary>Cấu hình gợi ý map layer CAD hiện có → Revit type (heuristic offline, tuỳ chọn Ollama).</summary>
public sealed class CadLayerMapConfig
{
    /// <summary>File .txt danh sách Revit type, mỗi dòng một tên.</summary>
    public required string RevitTypesPath { get; init; }

    /// <summary>Đường dẫn file CSV mapping đầu ra.</summary>
    public required string OutputPath { get; init; }

    /// <summary>Dùng model local (Ollama) để tinh chỉnh nếu có sẵn; nếu không cấu hình được thì rơi về heuristic.</summary>
    public bool UseOllama { get; init; }
}
