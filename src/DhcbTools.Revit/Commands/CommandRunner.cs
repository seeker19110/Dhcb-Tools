using Autodesk.Revit.UI;
using DhcbTools.Core;
using Newtonsoft.Json.Linq;

namespace DhcbTools.Revit.Commands;

/// <summary>
/// Chạy một lệnh Core theo tên, lấy cấu hình từ file JSON thay cho cửa sổ WPF.
/// Dùng cho bản build không WPF (<c>DHCB_SKIP_WPF</c>) và cho những lệnh chưa có cửa sổ riêng:
/// xem trước (<c>dryRun = true</c>) → hỏi → chạy thật, đúng nguyên tắc xuyên suốt trong roadmap.
/// </summary>
internal static class CommandRunner
{
    /// <summary>Thư mục config: <c>%APPDATA%\DHCB\configs\revit</c>.</summary>
    public static string ConfigDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "DHCB", "configs", "revit");

    public static string ConfigPath(string commandName) =>
        Path.Combine(ConfigDirectory, commandName + ".json");

    public static Result Run(ExternalCommandData commandData, string commandName)
    {
        var uiDocument = commandData.Application.ActiveUIDocument;
        if (uiDocument?.Document is null)
        {
            TaskDialog.Show(commandName, "Chưa mở mô hình nào.");
            return Result.Cancelled;
        }

        JObject config;
        try
        {
            config = LoadConfig(commandName);
        }
        catch (Exception ex)
        {
            TaskDialog.Show(commandName, $"Không đọc được cấu hình:{Environment.NewLine}{ex.Message}");
            return Result.Failed;
        }

        // Bước 1 — xem trước, không ghi vào mô hình.
        var preview = Dispatch(uiDocument.Document, commandName, config, dryRun: true);
        Feedback.Show($"{commandName} — xem trước", preview);
        if (!preview.Success)
        {
            return Result.Failed;
        }

        // Bước 2 — hỏi rồi mới ghi thật.
        var confirm = new TaskDialog(commandName)
        {
            MainInstruction = "Ghi thay đổi vào mô hình?",
            MainContent = preview.Summary,
            CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
            DefaultButton = TaskDialogResult.No,
        };
        if (confirm.Show() != TaskDialogResult.Yes)
        {
            return Result.Cancelled;
        }

        var result = Dispatch(uiDocument.Document, commandName, config, dryRun: false);
        Feedback.Show(commandName, result);
        return result.Success ? Result.Succeeded : Result.Failed;
    }

    /// <summary>Config rỗng nếu chưa có file — mọi lệnh Core đều có giá trị mặc định hợp lý.</summary>
    private static JObject LoadConfig(string commandName)
    {
        var path = ConfigPath(commandName);
        return File.Exists(path) ? JObject.Parse(File.ReadAllText(path)) : new JObject();
    }

    private static CommandResult Dispatch(Autodesk.Revit.DB.Document document, string commandName, JObject config, bool dryRun)
    {
        // Bản sao để lần chạy thật không thừa hưởng dryRun của lần xem trước.
        var effective = (JObject)config.DeepClone();
        effective["dryRun"] = dryRun;

        try
        {
            return RevitCommandTable.Dispatch(document, commandName, effective.ToString());
        }
        catch (Exception ex)
        {
            return CommandResult.Fail($"Lỗi khi chạy {commandName}: {ex.Message}");
        }
    }
}
