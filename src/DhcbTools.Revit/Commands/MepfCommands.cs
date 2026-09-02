using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace DhcbTools.Revit.Commands;

// Ba lệnh MEPF này từng có vỏ riêng viết tay với config gắn cứng trong mã — SleeveAuto còn để
// SleeveFamilyName = "M_Generic Model" kèm chú thích "placeholder, cấu hình qua HTTP Bridge", tức là
// nút Ribbon của tính năng MEPF chủ lực hoặc chạy hỏng, hoặc đặt nhầm family trên mọi dự án không nạp
// thư viện mét bản Mỹ. Từ giai đoạn 9.1 chúng đi qua form động như 39 lệnh còn lại: ô nhập dựng từ
// CommandCatalog, family/tham số chọn từ mô hình đang mở, xem trước rồi mới ghi.

[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public sealed class SleeveAutoCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements) =>
        CommandRunner.Run(commandData, "SleeveAuto");
}

[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public sealed class ElevationTagAutoCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements) =>
        CommandRunner.Run(commandData, "ElevationTag");
}

[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public sealed class ConnectorCheckerAutoCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements) =>
        CommandRunner.Run(commandData, "ConnectorChecker");
}
