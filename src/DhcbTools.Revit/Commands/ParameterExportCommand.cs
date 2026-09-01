using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using DhcbTools.Core.ParameterSync;
using Microsoft.Win32;

namespace DhcbTools.Revit.Commands;

/// <summary>
/// Vỏ desktop cho <see cref="ParameterExportCommand"/> (Core): hỏi đường dẫn lưu file bằng SaveFileDialog,
/// gọi Core xử lý, hiển thị kết quả bằng TaskDialog. Không chứa logic nghiệp vụ.
/// </summary>
[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public sealed class ParameterExportCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        var document = commandData.Application.ActiveUIDocument.Document;

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "DHCB Tools - Chọn nơi lưu file CSV",
            Filter = "CSV (*.csv)|*.csv",
            FileName = $"{document.Title}_parameters.csv",
        };
        if (dialog.ShowDialog() != true)
        {
            return Result.Cancelled;
        }

        // Cấu hình mặc định cho lệnh nền tảng: category/tham số phổ biến nhất.
        // TODO: thay bằng cửa sổ WPF cho phép kỹ sư chọn category + tham số (giai đoạn kế tiếp).
        var config = new ParameterExportConfig
        {
            Categories = new List<string> { "Doors", "Windows", "Rooms", "Walls" },
            ParameterNames = new List<string> { "Mark", "Comments", "Level" },
            OutputPath = dialog.FileName,
        };

        var command = new Core.ParameterSync.ParameterExportCommand();
        var result = command.Execute(document, config);

        Feedback.Show("Xuất tham số ra CSV", result);
        return result.Success ? Result.Succeeded : Result.Failed;
    }
}
