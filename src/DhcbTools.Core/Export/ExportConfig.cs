using System.Collections.Generic;

namespace DhcbTools.Core.Export;

public enum ExportFormat { Pdf, Dwg, Ifc, Nwc }

/// <summary>Cấu hình xuất file hàng loạt (PDF / DWG / IFC / NWC).</summary>
public sealed class ExportConfig
{
    /// <summary>Thư mục đầu ra.</summary>
    public required string OutputFolder { get; init; }

    /// <summary>Danh sách định dạng cần xuất.</summary>
    public required List<ExportFormat> Formats { get; init; }

    /// <summary>
    /// Mẫu tên file. Hỗ trợ token: {SheetNumber}, {SheetName}, {ProjectNumber}.
    /// </summary>
    public string FileNamePattern { get; init; } = "{SheetNumber}-{SheetName}";

    /// <summary>Lọc theo số bản vẽ. Để trống = xuất tất cả.</summary>
    public List<string> SheetNumbers { get; init; } = new List<string>();

    /// <summary>Xem trước (mặc định bật, như mọi lệnh khác): liệt kê sheet sẽ xuất, không ghi file.</summary>
    public bool DryRun { get; init; } = true;

    // DWG specific
    public string DwgVersion { get; init; } = "AcadRelease2018";

    // IFC specific
    public string IfcVersion { get; init; } = "IFC2x3";
}
