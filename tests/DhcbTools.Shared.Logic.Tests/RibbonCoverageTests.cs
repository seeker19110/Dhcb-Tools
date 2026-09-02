using Xunit;
using System.Text.RegularExpressions;
using DhcbTools.Shared.Logic.Ai;

namespace DhcbTools.Shared.Logic.Tests;

/// <summary>
/// Giữ cho Ribbon không tụt lại sau bảng lệnh. Trước đây <c>RevitCommandTable</c> có 42 lệnh
/// nhưng Ribbon chỉ có 10 nút — 32 lệnh chỉ với tới được qua Bridge/batch, kỹ sư ngồi trong
/// Revit không bấm được. Test đọc thẳng mã nguồn vỏ nên chạy được trên CI Linux, không cần Revit.
/// </summary>
public class RibbonCoverageTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Dhcb-Tools.sln")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName ?? throw new InvalidOperationException("Không tìm thấy gốc repo.");
    }

    private static string ShellSource()
    {
        var commands = Path.Combine(RepoRoot(), "src", "DhcbTools.Revit", "Commands");
        var text = File.ReadAllText(Path.Combine(RepoRoot(), "src", "DhcbTools.Revit", "App.cs"));
        foreach (var file in Directory.EnumerateFiles(commands, "*.cs"))
        {
            text += File.ReadAllText(file);
        }
        return text;
    }

    /// <summary>
    /// Mỗi lệnh Revit phải xuất hiện trong vỏ: hoặc qua <c>CommandRunner.Run("Lệnh")</c>,
    /// hoặc qua một lớp lệnh chuyên biệt đã có nút riêng.
    /// </summary>
    [Fact]
    public void MoiLenhRevit_DeuCoDuongVaoTuVo()
    {
        var shell = ShellSource();

        // Các lệnh có cửa sổ/luồng riêng, không đi qua CommandRunner theo tên.
        var handledByDedicatedShell = new HashSet<string>(StringComparer.Ordinal)
        {
            "RemoveUnusedViews", "BatchExport", "HealthReport", "ProjectInfo",
            "SleeveAuto", "ElevationTag", "ConnectorChecker",
        };

        var missing = CommandCatalog.For(CommandCatalog.Revit)
            .Select(c => c.Name)
            .Where(name => !handledByDedicatedShell.Contains(name))
            .Where(name => !shell.Contains("\"" + name + "\"", StringComparison.Ordinal))
            .ToList();

        Assert.True(
            missing.Count == 0,
            "Lệnh chưa có đường vào từ Ribbon: " + string.Join(", ", missing));
    }

    /// <summary>Mỗi lớp *RibbonCommand phải được gắn vào một nút trong App.cs, nếu không là code chết.</summary>
    [Fact]
    public void MoiLopRibbonCommand_DeuDuocGanVaoNut()
    {
        var root = RepoRoot();
        var generated = File.ReadAllText(Path.Combine(root, "src", "DhcbTools.Revit", "Commands", "CoreRibbonCommands.cs"));
        var app = File.ReadAllText(Path.Combine(root, "src", "DhcbTools.Revit", "App.cs"));

        var classes = Regex.Matches(generated, @"class\s+(\w+RibbonCommand)\b")
            .Select(m => m.Groups[1].Value)
            .ToList();

        Assert.True(classes.Count > 0, "Không đọc được lớp RibbonCommand nào.");

        var unwired = classes.Where(c => !app.Contains("\"" + c + "\"", StringComparison.Ordinal)).ToList();
        Assert.True(unwired.Count == 0, "Lớp có mà không có nút: " + string.Join(", ", unwired));
    }

    /// <summary>Ribbon phải đủ 6 panel như README mô tả — chống việc tài liệu lại trôi khỏi mã nguồn.</summary>
    [Fact]
    public void RibbonCo_DuSauPanel()
    {
        var app = File.ReadAllText(Path.Combine(RepoRoot(), "src", "DhcbTools.Revit", "App.cs"));
        Assert.Equal(6, Regex.Matches(app, @"CreateRibbonPanel\(").Count);
    }

    /// <summary>Hai thứ README hứa mà vỏ từng thiếu: đăng ký updater và hook batch.</summary>
    [Fact]
    public void VoRevit_CoUpdaterVaHookBatch()
    {
        var app = File.ReadAllText(Path.Combine(RepoRoot(), "src", "DhcbTools.Revit", "App.cs"));
        Assert.Contains("ElevationUpdater", app, StringComparison.Ordinal);
        Assert.Contains("BatchStartupHook.RunIfRequested", app, StringComparison.Ordinal);

        var hook = Path.Combine(RepoRoot(), "src", "DhcbTools.Revit", "Batch", "BatchStartupHook.cs");
        Assert.True(File.Exists(hook), "Thiếu BatchStartupHook.cs — batch runner sẽ treo chờ batch-done.json.");

        var source = File.ReadAllText(hook);
        Assert.Contains("pending-job.json", source, StringComparison.Ordinal);
        Assert.Contains("batch-done.json", source, StringComparison.Ordinal);
    }
}
