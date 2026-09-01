using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using DhcbTools.Core;
using DhcbTools.Shared.Logic.Ai;
using Newtonsoft.Json.Linq;

namespace DhcbTools.Revit.Commands;

/// <summary>
/// Config JSON theo tên lệnh ở <c>%APPDATA%\DHCB\configs\revit\&lt;CommandName&gt;.json</c>. Lần đầu bấm nút chưa có file
/// → tạo file mẫu từ <see cref="CommandCatalog"/> và mở cho kỹ sư điền. Cùng một config dùng được cho Ribbon, Bridge và batch.
/// </summary>
internal static class ConfigStore
{
    public static string Directory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DHCB", "configs", "revit");

    public static string PathFor(string commandName) => Path.Combine(Directory, commandName + ".json");

    public static JObject? Load(string commandName)
    {
        var path = PathFor(commandName);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return JObject.Parse(File.ReadAllText(path));
        }
        catch (Newtonsoft.Json.JsonException ex)
        {
            throw new InvalidOperationException($"Config {path} không phải JSON hợp lệ: {ex.Message}");
        }
    }

    public static string WriteTemplate(CommandDescriptor descriptor, JObject? defaults = null)
    {
        System.IO.Directory.CreateDirectory(Directory);
        var path = PathFor(descriptor.Name);
        if (File.Exists(path))
        {
            return path;
        }

        var obj = defaults ?? new JObject();
        obj["_description"] = descriptor.Description + " — sửa các giá trị rồi bấm lại nút. dryRun=true chỉ xem trước.";
        foreach (var f in descriptor.ConfigFields)
        {
            if (obj[f.Key] != null) continue;
            obj[f.Key] = f.Key.Equals("dryRun", StringComparison.OrdinalIgnoreCase) ? true : (JToken)("<" + f.Value + ">");
        }

        File.WriteAllText(path, obj.ToString(Newtonsoft.Json.Formatting.Indented));
        return path;
    }
}

/// <summary>
/// Khuôn chung cho mọi nút Ribbon: đọc config → chạy xem trước (dryRun) → hỏi xác nhận → chạy thật.
/// Lệnh chỉ đọc (WritesModel=false) chạy một lần. Đây là "đọc config JSON, hiện kết quả DryRun trước, hỏi xác nhận"
/// mà mục 0.3 yêu cầu, áp cho toàn bộ lệnh thay vì viết riêng từng nút.
/// </summary>
internal static class CommandRunner
{
    public static Result Run(ExternalCommandData commandData, string commandName, JObject? defaults = null)
    {
        var doc = commandData.Application.ActiveUIDocument?.Document;
        if (doc is null)
        {
            TaskDialog.Show("DHCB Tools", "Không có document nào đang mở.");
            return Result.Cancelled;
        }

        var descriptor = CommandCatalog.Find(CommandCatalog.Revit, commandName);
        if (descriptor is null)
        {
            TaskDialog.Show("DHCB Tools", $"Lệnh \"{commandName}\" không có trong danh mục.");
            return Result.Failed;
        }

        JObject? config;
        try
        {
            config = ConfigStore.Load(descriptor.Name);
        }
        catch (InvalidOperationException ex)
        {
            TaskDialog.Show("DHCB Tools", ex.Message);
            return Result.Failed;
        }

        if (config is null || config.Properties().Any(p => p.Value.Type == JTokenType.String && ((string?)p.Value ?? string.Empty).StartsWith("<", StringComparison.Ordinal)))
        {
            var path = ConfigStore.WriteTemplate(descriptor, defaults);
            var td = new TaskDialog("DHCB - " + descriptor.Name)
            {
                MainInstruction = config is null ? "Đã tạo file config mẫu" : "Config còn giá trị mẫu chưa điền",
                MainContent = $"{descriptor.Description}\n\nSửa file rồi bấm lại nút:\n{path}",
                CommonButtons = TaskDialogCommonButtons.Close,
            };
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Mở file config");
            if (td.Show() == TaskDialogResult.CommandLink1)
            {
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true }); } catch { /* ignore */ }
            }
            return Result.Cancelled;
        }

        config.Remove("_description");

        if (!descriptor.WritesModel)
        {
            var once = RevitCommandTable.Dispatch(doc, descriptor.Name, config.ToString(Newtonsoft.Json.Formatting.None));
            Feedback.Show(descriptor.Description, once);
            return once.Success ? Result.Succeeded : Result.Failed;
        }

        // Bước 1: xem trước.
        var preview = (JObject)config.DeepClone();
        preview["dryRun"] = true;
        var previewResult = RevitCommandTable.Dispatch(doc, descriptor.Name, preview.ToString(Newtonsoft.Json.Formatting.None));
        if (!previewResult.Success)
        {
            Feedback.Show(descriptor.Description + " — xem trước", previewResult);
            return Result.Failed;
        }

        var confirm = new TaskDialog("DHCB - " + descriptor.Name)
        {
            MainInstruction = previewResult.Summary,
            MainContent = "Đây là kết quả XEM TRƯỚC, mô hình chưa đổi. Chạy thật với cấu hình này?",
            ExpandedContent = string.Join(Environment.NewLine, previewResult.Messages.Take(200)),
            CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
            DefaultButton = TaskDialogResult.No,
        };
        if (confirm.Show() != TaskDialogResult.Yes)
        {
            return Result.Cancelled;
        }

        // Bước 2: chạy thật.
        config["dryRun"] = false;
        var result = RevitCommandTable.Dispatch(doc, descriptor.Name, config.ToString(Newtonsoft.Json.Formatting.None));
        Feedback.Show(descriptor.Description, result);
        return result.Success ? Result.Succeeded : Result.Failed;
    }
}
