namespace DhcbTools.Core.AutoCAD.Reporting;

/// <summary>Cấu hình trích trục (Grid) từ một layer ra CSV cho lệnh GridFromCsv bên Revit.</summary>
public sealed class GridExtractConfig
{
    /// <summary>Layer chứa các đường trục (Line).</summary>
    public string GridLayer { get; init; } = "AXIS";

    /// <summary>Đường dẫn CSV đầu ra; tọa độ đổi sang mm theo INSUNITS để nhập GridFromCsv.</summary>
    public required string OutputPath { get; init; }
}
