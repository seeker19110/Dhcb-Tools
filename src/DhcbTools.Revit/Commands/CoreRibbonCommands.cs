using Autodesk.Revit.DB;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;

namespace DhcbTools.Revit.Commands;

// Vỏ Ribbon cho các lệnh Core chưa có cửa sổ riêng. Mỗi lớp chỉ uỷ quyền cho
// CommandRunner: đọc config JSON ở %APPDATA%\DHCB\configs\revit\<Lệnh>.json →
// chạy xem trước (dryRun) → hỏi xác nhận → chạy thật. Revit đòi mỗi nút một lớp
// IExternalCommand riêng, nên danh sách dài nhưng mỗi mục chỉ có một dòng thân hàm.
// Tên lệnh phải khớp RevitCommandTable.Dispatch — RibbonCoverageTests giữ điều đó.

/// <summary>Tạo Level và view plan tương ứng từ danh sách trong config.</summary>
[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public sealed class LevelSetupRibbonCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        => CommandRunner.Run(commandData, "LevelSetup");
}

/// <summary>Tạo lưới trục từ toạ độ khai báo trong config.</summary>
[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public sealed class GridSetupRibbonCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        => CommandRunner.Run(commandData, "GridSetup");
}

/// <summary>Ghi thông tin dự án (tên, mã, chủ đầu tư…) vào Project Information.</summary>
[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public sealed class ProjectInfoRibbonCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        => CommandRunner.Run(commandData, "ProjectInfo");
}

/// <summary>Nạp hàng loạt family từ danh sách đường dẫn.</summary>
[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public sealed class FamilyLoaderRibbonCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        => CommandRunner.Run(commandData, "FamilyLoader");
}

/// <summary>Tạo file dự án mới từ template chuẩn của công ty.</summary>
[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public sealed class ProjectFromTemplateRibbonCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        => CommandRunner.Run(commandData, "ProjectFromTemplate");
}

/// <summary>Copy tiêu chuẩn (type, filter, view template) từ file nguồn.</summary>
[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public sealed class TransferStandardsRibbonCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        => CommandRunner.Run(commandData, "TransferStandards");
}

/// <summary>Dựng trục từ CSV xuất bởi Excel hoặc lệnh GridExtract bên AutoCAD.</summary>
[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public sealed class GridFromCsvRibbonCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        => CommandRunner.Run(commandData, "GridFromCsv");
}

/// <summary>Tạo sheet hàng loạt theo danh sách số hiệu và tên.</summary>
[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public sealed class SheetBatchCreateRibbonCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        => CommandRunner.Run(commandData, "SheetBatchCreate");
}

/// <summary>Xuất toàn bộ warning của mô hình ra file để phân tích.</summary>
[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public sealed class WarningsExportRibbonCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        => CommandRunner.Run(commandData, "WarningsExport");
}

/// <summary>Xuất các schedule ra CSV/Excel.</summary>
[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public sealed class ScheduleExportRibbonCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        => CommandRunner.Run(commandData, "ScheduleExport");
}

/// <summary>Rải hanger dọc ống/ducting theo bước khai báo.</summary>
[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public sealed class HangerAutoRibbonCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        => CommandRunner.Run(commandData, "HangerAuto");
}

/// <summary>Cắt ống dài thành đoạn theo chiều dài thương mại.</summary>
[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public sealed class PipeSplitterRibbonCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        => CommandRunner.Run(commandData, "PipeSplitter");
}

/// <summary>Dựng ống/duct từ model line vẽ tay (routing mức A).</summary>
[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public sealed class RouteFromLinesRibbonCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        => CommandRunner.Run(commandData, "RouteFromLines");
}

/// <summary>Rải thiết bị theo phòng và mẫu bố trí (routing mức B).</summary>
[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public sealed class DevicePlacementRibbonCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        => CommandRunner.Run(commandData, "DevicePlacement");
}

/// <summary>Tính tiết diện ống/duct theo lưu lượng, xuất CSV để duyệt.</summary>
[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public sealed class SizingProposalRibbonCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        => CommandRunner.Run(commandData, "SizingProposal");
}

/// <summary>Đọc CSV đã duyệt và áp tiết diện vào mô hình.</summary>
[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public sealed class ApplySizingRibbonCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        => CommandRunner.Run(commandData, "ApplySizing");
}

/// <summary>Tô màu và tạo filter theo hệ thống MEP.</summary>
[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public sealed class SystemColorRibbonCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        => CommandRunner.Run(commandData, "SystemColor");
}

/// <summary>Chuẩn hoá System Name theo quy tắc đặt tên.</summary>
[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public sealed class SystemNameRibbonCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        => CommandRunner.Run(commandData, "SystemName");
}

/// <summary>Đánh số thiết bị theo thứ tự dòng chảy trong hệ.</summary>
[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public sealed class FlowNumberingRibbonCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        => CommandRunner.Run(commandData, "FlowNumbering");
}

/// <summary>Gán độ dốc cho tuyến ống thoát theo tiêu chuẩn.</summary>
[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public sealed class SlopePipesRibbonCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        => CommandRunner.Run(commandData, "SlopePipes");
}

/// <summary>Chèn đoạn kick (lệch trục) để né chướng ngại.</summary>
[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public sealed class PipeKickRibbonCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        => CommandRunner.Run(commandData, "PipeKick");
}

/// <summary>Bóc khối lượng theo hệ, xuất bảng spool/BOM.</summary>
[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public sealed class SystemBomRibbonCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        => CommandRunner.Run(commandData, "SystemBom");
}

/// <summary>Tìm đường A* 3D giữa hai điểm, né chướng ngại (routing mức C).</summary>
[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public sealed class AutoRouteRibbonCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        => CommandRunner.Run(commandData, "AutoRoute");
}

/// <summary>Đổi tên/số hiệu sheet hàng loạt theo mẫu.</summary>
[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public sealed class SheetRenameRibbonCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        => CommandRunner.Run(commandData, "SheetRename");
}

/// <summary>Gán revision cho nhóm sheet được chọn.</summary>
[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public sealed class RevisionOnSheetsRibbonCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        => CommandRunner.Run(commandData, "RevisionOnSheets");
}

/// <summary>Xoá text style, dimension style, line pattern không dùng.</summary>
[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public sealed class StylePurgeRibbonCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        => CommandRunner.Run(commandData, "StylePurge");
}

/// <summary>Tô màu phần tử theo giá trị một tham số (kiểu Colour Splasher).</summary>
[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public sealed class ColorByParameterRibbonCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        => CommandRunner.Run(commandData, "ColorByParameter");
}

/// <summary>Rà soát family: trùng lặp, nặng, in-place, không dùng.</summary>
[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public sealed class FamilyAuditRibbonCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        => CommandRunner.Run(commandData, "FamilyAudit");
}

/// <summary>Copy bố trí viewport từ sheet mẫu sang các sheet khác.</summary>
[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public sealed class ViewportCopyRibbonCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        => CommandRunner.Run(commandData, "ViewportCopy");
}

/// <summary>Đối chiếu tham số với bộ quy tắc trong parameter-rules.json.</summary>
[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public sealed class ParameterRuleCheckRibbonCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        => CommandRunner.Run(commandData, "ParameterRuleCheck");
}

/// <summary>Dò va chạm nội bộ, xuất HTML + 3D view, đọc clash-accepted.json.</summary>
[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public sealed class ClashDetectionRibbonCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        => CommandRunner.Run(commandData, "ClashDetection");
}

/// <summary>Đề xuất map layer bản vẽ CAD sang type Revit (AI offline).</summary>
[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public sealed class CadLayerMapRibbonCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        => CommandRunner.Run(commandData, "CadLayerMap");
}

/// <summary>Trích tham số kỹ thuật từ thuyết minh thành file config (AI offline).</summary>
[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public sealed class SpecToConfigRibbonCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        => CommandRunner.Run(commandData, "SpecToConfig");
}

/// <summary>Soi tên tham số thật của dự án, đề xuất/ghi dictionary.json.</summary>
[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public sealed class DictionaryLearnRibbonCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        => CommandRunner.Run(commandData, "DictionaryLearn");
}

/// <summary>Xuất toạ độ định vị (tim cột, tâm thiết bị, giao trục) ra CSV cho máy toàn đạc + DXF điểm.</summary>
[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public sealed class SetoutExportRibbonCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        => CommandRunner.Run(commandData, "SetoutExport");
}
