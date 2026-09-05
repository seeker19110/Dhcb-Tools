using System.IO;
using Autodesk.Revit.UI;
using DhcbTools.Core;
using DhcbTools.Shared.Hosting;
using Newtonsoft.Json.Linq;

namespace DhcbTools.Revit.Commands;

/// <summary>
/// Chạy một lệnh Core theo tên. Bản có WPF mở <see cref="UI.CommandFormWindow"/> — form dựng ô nhập
/// từ <c>CommandCatalog</c> nên mọi lệnh đều có giao diện thật (giai đoạn 9.1). Bản không WPF
/// (<c>DHCB_SKIP_WPF</c>, dùng cho build kiểm tra trên CI) rơi về đường cũ: đọc config JSON ở
/// <c>%APPDATA%\DHCB\configs\revit</c> → xem trước → hỏi → chạy thật.
/// <para>Cả hai đường đều giữ nguyên tắc xuyên suốt: <c>DryRun</c> chạy trước, kỹ sư xác nhận mới ghi.</para>
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

#if !DHCB_SKIP_WPF
        var descriptor = DhcbTools.Shared.Logic.Ai.CommandCatalog.Find(
            DhcbTools.Shared.Logic.Ai.CommandCatalog.Revit, commandName);

        if (descriptor != null)
        {
            var window = new UI.CommandFormWindow(uiDocument.Document, descriptor, config, ConfigPath(commandName));

            // Gắn cửa sổ vào Revit để nó luôn nổi lên trên và Revit không nhận thao tác khi form đang mở.
            //
            // Lấy handle từ CHÍNH API Revit (UIApplication.MainWindowHandle), không lấy
            // Process.MainWindowHandle: tiến trình Revit có nhiều cửa sổ cấp cao nhất (một cái tên "Revit"
            // ẩn, một "Hidden Window"…), và Process.MainWindowHandle trả về cái Windows tìm thấy trước —
            // không nhất thiết là khung chính đang hiện. Chủ sai thì z-order không được bảo đảm: bấm lệnh
            // trên Ribbon, form chạy xong là **rơi xuống dưới cửa sổ chính**, kỹ sư thấy Revit đứng im
            // (vì form vẫn modal) mà không thấy hộp thoại đâu. Bắt được khi bấm tay Ribbon ngày
            // 2026-09-05 — xem docs/bang-chung-test.md §33.
            new System.Windows.Interop.WindowInteropHelper(window).Owner =
                commandData.Application.MainWindowHandle;

            window.ShowDialog();
            return window.Executed ? Result.Succeeded : Result.Cancelled;
        }
#endif

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
            // Hộp thoại chỉ hiện Message rồi kỹ sư bấm cho mất; stack trace đầy đủ vào log để còn gửi kèm khi báo lỗi.
            DhcbLog.Error("Revit", $"lệnh {commandName} (dryRun={dryRun})", ex);
            return CommandResult.Fail($"Lỗi khi chạy {commandName}: {ex.Message}"
                                    + $"{Environment.NewLine}Chi tiết trong {DhcbLog.PathFor("Revit")}");
        }
    }
}
