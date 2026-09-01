using System.Windows;
using System.Windows.Controls;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using DhcbTools.Core;
using DhcbTools.Shared.Logic.Ai;

namespace DhcbTools.Revit.UI;

/// <summary>
/// Mục 5.4 — ra lệnh bằng tiếng Việt, offline: câu → <see cref="CommandIntentParser"/> → hiện lệnh + config đề xuất →
/// "Xem trước" chạy dryRun → "Chạy thật" chỉ bật sau khi xem trước thành công. Không có model nào được sinh code.
/// Cửa sổ dựng bằng code (không XAML) để đơn giản.
/// </summary>
public sealed class AiChatWindow : Window
{
    private readonly Document _doc;
    private readonly TextBox _input = new() { Height = 60, TextWrapping = TextWrapping.Wrap, AcceptsReturn = true, Margin = new Thickness(0, 4, 0, 8) };
    private readonly TextBox _config = new() { Height = 140, TextWrapping = TextWrapping.Wrap, AcceptsReturn = true, FontFamily = new System.Windows.Media.FontFamily("Consolas"), Margin = new Thickness(0, 4, 0, 8) };
    private readonly TextBlock _explain = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
    private readonly TextBox _output = new() { Height = 180, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, FontFamily = new System.Windows.Media.FontFamily("Consolas") };
    private readonly Button _preview = new() { Content = "Xem trước (dryRun)", Margin = new Thickness(0, 0, 8, 0), IsEnabled = false, Padding = new Thickness(12, 4, 12, 4) };
    private readonly Button _run = new() { Content = "Chạy THẬT", IsEnabled = false, Padding = new Thickness(12, 4, 12, 4) };
    private string? _command;

    public AiChatWindow(Document doc)
    {
        _doc = doc;
        Title = "DHCB - Ra lệnh bằng tiếng Việt (offline)";
        Width = 720;
        Height = 620;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        var panel = new StackPanel { Margin = new Thickness(12) };
        panel.Children.Add(new TextBlock { Text = "Bạn muốn làm gì? Ví dụ: \"đánh số cửa tầng 3 tiền tố D- 3 chữ số\", \"xuất pdf toàn bộ sheet ra D:/out\"", TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(_input);
        var parse = new Button { Content = "Đề xuất lệnh", Padding = new Thickness(12, 4, 12, 4), HorizontalAlignment = HorizontalAlignment.Left };
        parse.Click += (_, _) => Parse();
        panel.Children.Add(parse);
        panel.Children.Add(_explain);
        panel.Children.Add(new TextBlock { Text = "Config đề xuất (sửa được trước khi chạy):" });
        panel.Children.Add(_config);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        _preview.Click += (_, _) => Execute(dryRun: true);
        _run.Click += (_, _) => Execute(dryRun: false);
        buttons.Children.Add(_preview);
        buttons.Children.Add(_run);
        panel.Children.Add(buttons);
        panel.Children.Add(new TextBlock { Text = "Kết quả:" });
        panel.Children.Add(_output);
        Content = new ScrollViewer { Content = panel };
    }

    private void Parse()
    {
        var intent = CommandIntentParser.Parse(_input.Text, CommandCatalog.Revit);
        _command = intent.Command;
        _explain.Text = intent.Explanation + (intent.Command is null ? string.Empty : $"  (độ tin cậy {intent.Confidence:F2})")
                        + (intent.Alternatives.Count > 0 ? "\nLệnh khác có thể: " + string.Join(", ", intent.Alternatives) : string.Empty);
        _config.Text = intent.Config.ToString(Newtonsoft.Json.Formatting.Indented);
        _preview.IsEnabled = intent.Command is not null;
        _run.IsEnabled = false;
        _output.Clear();
    }

    private void Execute(bool dryRun)
    {
        if (_command is null) return;
        try
        {
            var cfg = Newtonsoft.Json.Linq.JObject.Parse(_config.Text);
            var descriptor = CommandCatalog.Find(CommandCatalog.Revit, _command)!;
            if (descriptor.WritesModel) cfg["dryRun"] = dryRun;
            var result = RevitCommandTable.Dispatch(_doc, _command, cfg.ToString(Newtonsoft.Json.Formatting.None));
            _output.Text = (result.Success ? "✓ " : "✗ ") + result.Summary + Environment.NewLine
                           + string.Join(Environment.NewLine, result.Messages.Take(300).Select(m => "  • " + m))
                           + (result.Errors.Count > 0 ? Environment.NewLine + string.Join(Environment.NewLine, result.Errors.Select(e => "  ! " + e)) : string.Empty);
            _run.IsEnabled = dryRun && result.Success && descriptor.WritesModel;
        }
        catch (Exception ex)
        {
            _output.Text = "✗ " + ex.Message;
        }
    }
}

[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public sealed class AiChatCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        var doc = commandData.Application.ActiveUIDocument?.Document;
        if (doc is null)
        {
            TaskDialog.Show("DHCB Tools", "Không có document nào đang mở.");
            return Result.Cancelled;
        }

        new AiChatWindow(doc).ShowDialog();
        return Result.Succeeded;
    }
}
