using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Autodesk.Revit.DB;
// Grid có ở cả System.Windows.Controls lẫn Autodesk.Revit.DB — đặt bí danh cho khỏi nhập nhằng.
using WpfGrid = System.Windows.Controls.Grid;
using DhcbTools.Core;
using DhcbTools.Core.Query;
using DhcbTools.Shared.Logic.Ai;
using DhcbTools.Shared.Hosting;
using Newtonsoft.Json.Linq;

namespace DhcbTools.Revit.UI;

/// <summary>
/// Form động cho MỌI lệnh Core (giai đoạn 9.1).
/// <para>
/// Trước đây 32/42 nút Ribbon chỉ đọc file JSON ở <c>%APPDATA%\DHCB\configs\revit</c> — mà file đó
/// còn không tự sinh, nên bấm nút là chạy với toàn giá trị mặc định và không có cách nào biết lệnh
/// nhận những trường gì. Không kỹ sư nào sửa JSON trong %APPDATA% để dùng tool.
/// </para>
/// <para>
/// Cửa sổ này dựng ô nhập từ <see cref="CommandDescriptor.Fields"/>: kiểu trường quyết định ô nhập
/// (checkbox, ô số, nút chọn file, combo lấy từ mô hình đang mở). Luôn chạy <b>xem trước</b> trước,
/// nút <i>Chạy thật</i> chỉ mở khi xem trước thành công.
/// </para>
/// </summary>
public partial class CommandFormWindow : Window
{
    private readonly Document _document;
    private readonly CommandDescriptor _descriptor;
    private readonly string _configPath;
    private readonly List<IFieldEditor> _editors = new();

    public CommandFormWindow(Document document, CommandDescriptor descriptor, JObject config, string configPath)
    {
        InitializeComponent();

        _document = document;
        _descriptor = descriptor;
        _configPath = configPath;

        Title = "DHCB Tools — " + descriptor.Name;
        TitleText.Text = descriptor.Name;
        DescriptionText.Text = descriptor.Description;

        BuildFields(config);
    }

    /// <summary>Lệnh đã chạy thật (không phải xem trước) chưa — vỏ dùng để trả Result cho Revit.</summary>
    public bool Executed { get; private set; }

    private void BuildFields(JObject config)
    {
        if (_descriptor.Fields.Count == 0)
        {
            FieldsPanel.Children.Add(new TextBlock
            {
                Text = "Lệnh này không có tham số nào — bấm Xem trước rồi Chạy thật.",
                Foreground = System.Windows.Media.Brushes.Gray,
                TextWrapping = TextWrapping.Wrap,
            });
            return;
        }

        foreach (var field in _descriptor.Fields)
        {
            // dryRun do cửa sổ điều khiển (Xem trước / Chạy thật), không để người dùng tự đặt.
            if (string.Equals(field.Name, "dryRun", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var editor = CreateEditor(field, config[field.Name]);
            _editors.Add(editor);
            FieldsPanel.Children.Add(editor.Build());
        }
    }

    private IFieldEditor CreateEditor(FieldSpec field, JToken? value) => field.Kind switch
    {
        FieldKind.Bool => new BoolEditor(field, value),
        FieldKind.Number => new NumberEditor(field, value),
        FieldKind.FilePath => new PathEditor(field, value, folder: false),
        FieldKind.FolderPath => new PathEditor(field, value, folder: true),
        FieldKind.Category or FieldKind.Parameter or FieldKind.Level or FieldKind.View or FieldKind.FamilyType =>
            new ChoiceEditor(field, value, ModelChoices.For(_document, field.Kind)),
        _ => new TextEditor(field, value),
    };

    /// <summary>Gom giá trị các ô nhập thành config JSON.</summary>
    private JObject Collect()
    {
        var config = new JObject();
        foreach (var editor in _editors)
        {
            var value = editor.Value;
            if (value != null)
            {
                config[editor.Field.Name] = value;
            }
        }

        return config;
    }

    private void OnPreview(object sender, RoutedEventArgs e) => Execute(dryRun: true);

    private void OnRun(object sender, RoutedEventArgs e)
    {
        var confirm = MessageBox.Show(
            this,
            $"Ghi thay đổi vào mô hình bằng lệnh {_descriptor.Name}?",
            "DHCB Tools",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);

        if (confirm == MessageBoxResult.Yes)
        {
            Execute(dryRun: false);
        }
    }

    private void Execute(bool dryRun)
    {
        JObject config;
        try
        {
            config = Collect();
        }
        catch (FormatException ex)
        {
            ShowText("Giá trị nhập chưa hợp lệ: " + ex.Message);
            return;
        }

        SaveConfig(config);

        config["dryRun"] = dryRun;
        CommandResult result;
        var cursor = Cursor;
        try
        {
            Cursor = System.Windows.Input.Cursors.Wait;
            result = RevitCommandTable.Dispatch(_document, _descriptor.Name, config.ToString());
        }
        catch (Exception ex)
        {
            DhcbLog.Error("Revit", $"lệnh {_descriptor.Name} (dryRun={dryRun})", ex);
            ShowText($"Lỗi: {ex.Message}{Environment.NewLine}Chi tiết trong {DhcbLog.PathFor("Revit")}");
            RunButton.IsEnabled = false;
            return;
        }
        finally
        {
            Cursor = cursor;
        }

        ResultHeader.Text = dryRun ? "Kết quả xem trước" : "Kết quả chạy thật";
        ShowText(Format(result));

        // Chỉ mở nút chạy thật sau khi xem trước thành công — đúng nguyên tắc DryRun mặc định của roadmap.
        RunButton.IsEnabled = dryRun && result.Success;
        if (!dryRun)
        {
            Executed = result.Success;
        }
    }

    private static string Format(CommandResult result)
    {
        var lines = new List<string> { result.Summary };

        if (result.Errors.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add($"Lỗi ({result.Errors.Count}):");
            lines.AddRange(result.Errors.Take(50));
        }

        if (result.Messages.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add($"Chi tiết ({result.Messages.Count} dòng):");
            lines.AddRange(result.Messages.Take(200));
            if (result.Messages.Count > 200)
            {
                lines.Add($"… còn {result.Messages.Count - 200} dòng nữa.");
            }
        }

        return string.Join(Environment.NewLine, lines);
    }

    private void ShowText(string text) => ResultBox.Text = text;

    private void SaveConfig(JObject config)
    {
        try
        {
            var folder = Path.GetDirectoryName(_configPath);
            if (!string.IsNullOrEmpty(folder))
            {
                Directory.CreateDirectory(folder!);
            }

            File.WriteAllText(_configPath, config.ToString());
        }
        catch (Exception ex)
        {
            // Không ghi được config thì vẫn chạy được lệnh — chỉ là lần sau phải nhập lại.
            DhcbLog.Write("Revit", $"Không lưu được config {_configPath}: {ex.Message}");
        }
    }
}

/// <summary>Một ô nhập trên form động.</summary>
internal interface IFieldEditor
{
    FieldSpec Field { get; }

    /// <summary>Dựng khối giao diện (nhãn + ô nhập).</summary>
    UIElement Build();

    /// <summary>Giá trị JSON hiện tại; null = không ghi trường này vào config.</summary>
    JToken? Value { get; }
}

internal abstract class FieldEditorBase : IFieldEditor
{
    protected FieldEditorBase(FieldSpec field)
    {
        Field = field;
    }

    public FieldSpec Field { get; }

    public abstract UIElement Build();

    public abstract JToken? Value { get; }

    /// <summary>Nhãn "tênTrường — mô tả", đủ để kỹ sư biết trường này là gì mà không phải mở tài liệu.</summary>
    protected TextBlock Label() => new()
    {
        Text = Field.Name + " — " + Field.Description + (Field.IsList ? "  (nhiều giá trị ngăn bằng dấu ; hoặc xuống dòng)" : string.Empty),
        Margin = new Thickness(0, 0, 0, 4),
        TextWrapping = TextWrapping.Wrap,
    };

    /// <summary>Ngăn cách danh sách bằng ";" hoặc xuống dòng — dấu phẩy có trong tên category ("Pipe Fittings, Bends") và tên type.</summary>
    protected static IEnumerable<string> SplitList(string text) =>
        text.Split(new[] { ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(p => p.Trim()).Where(p => p.Length > 0);

    protected static StackPanel Row(params UIElement[] children)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        foreach (var child in children)
        {
            panel.Children.Add(child);
        }

        return panel;
    }
}

internal sealed class TextEditor : FieldEditorBase
{
    private readonly TextBox _box = new();

    public TextEditor(FieldSpec field, JToken? value) : base(field)
    {
        // Ghép bằng ";" CHỈ cho danh sách chuỗi. Trường nhận JSON thô (levels, grids, colors) phải giữ
        // nguyên hình dáng JSON, nếu không thì hai object thành "{…}; {…}" và đọc lại là hỏng.
        _box.Text = FormValueText.Display(value, Field.IsList);
        if (Field.IsList || Field.Kind == FieldKind.Json)
        {
            _box.AcceptsReturn = true;
            _box.TextWrapping = TextWrapping.Wrap;
        }
    }

    public override UIElement Build() => Row(Label(), _box);

    public override JToken? Value
    {
        get
        {
            var text = _box.Text.Trim();
            if (text.Length == 0)
            {
                return null;
            }

            if (Field.IsList)
            {
                return new JArray(SplitList(text));
            }

            // Chỉ trường khai FieldKind.Json mới đọc bằng bộ đọc JSON. Đoán theo ký tự đầu thì mẫu đặt
            // tên "{Discipline}-{Number}" của SheetRename cũng bị coi là JSON hỏng và chặn người dùng lại.
            if (Field.Kind == FieldKind.Json)
            {
                try
                {
                    return JToken.Parse(text);
                }
                catch (Newtonsoft.Json.JsonException)
                {
                    throw new FormatException($"\"{Field.Name}\" trông như JSON nhưng không đọc được.");
                }
            }

            return text;
        }
    }
}

internal sealed class NumberEditor : FieldEditorBase
{
    private readonly TextBox _box = new();

    public NumberEditor(FieldSpec field, JToken? value) : base(field)
    {
        _box.Text = value?.ToString() ?? string.Empty;
    }

    public override UIElement Build() => Row(Label(), _box);

    public override JToken? Value
    {
        get
        {
            var text = _box.Text.Trim();
            if (text.Length == 0)
            {
                return null;
            }

            // Đọc ở tầng thuần (có test): dấu phẩy thập phân của máy tiếng Việt, và số nguyên phải ra
            // JSON số nguyên chứ không phải "1.0" — property int từ chối 1.0.
            var number = FormValueText.Number(text);
            if (number != null)
            {
                return number;
            }

            throw new FormatException($"\"{Field.Name}\" phải là số, đang nhập \"{text}\".");
        }
    }
}

internal sealed class BoolEditor : FieldEditorBase
{
    private readonly CheckBox _box = new();

    public BoolEditor(FieldSpec field, JToken? value) : base(field)
    {
        _box.Content = Field.Name + " — " + Field.Description;
        // Giá trị đã lưu > mặc định của lớp Config thật (catalog) > false. Trước đây trường mặc định
        // true (includeLinkedModels, skipExisting…) hiện KHÔNG tick, và bỏ tick thì không ghi gì vào
        // JSON nên lệnh vẫn chạy với true — không có cách nào tắt từ form.
        _box.IsChecked = value?.Type == JTokenType.Boolean
            ? value.Value<bool>()
            : CommandCatalog.DefaultBool(Field.Name);
    }

    public override UIElement Build() => Row(_box);

    /// <summary>Luôn ghi rõ true/false — false phải tới được lệnh để tắt mặc định true.</summary>
    public override JToken? Value => JToken.FromObject(_box.IsChecked == true);
}

internal sealed class PathEditor : FieldEditorBase
{
    private readonly TextBox _box = new();
    private readonly bool _folder;

    public PathEditor(FieldSpec field, JToken? value, bool folder) : base(field)
    {
        _folder = folder;
        _box.Text = value?.ToString() ?? string.Empty;
    }

    public override UIElement Build()
    {
        var grid = new WpfGrid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        WpfGrid.SetColumn(_box, 0);
        grid.Children.Add(_box);

        var button = new Button
        {
            Content = _folder ? "Thư mục…" : "Chọn file…",
            Width = 90,
            Margin = new Thickness(8, 0, 0, 0),
        };
        button.Click += (_, _) => Browse();
        WpfGrid.SetColumn(button, 1);
        grid.Children.Add(button);

        return Row(Label(), grid);
    }

    private void Browse()
    {
        // Không dùng WinForms FolderBrowserDialog (kéo theo tham chiếu nặng): hộp thoại lưu file với
        // tên giả, người dùng chọn thư mục rồi ta lấy phần thư mục — cách quen thuộc trong add-in Revit.
        if (_folder)
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Chọn thư mục (mở thư mục cần chọn rồi bấm Lưu)",
                FileName = "chon-thu-muc-nay",
                Filter = "Thư mục|*.none",
                CheckPathExists = true,
            };

            if (dialog.ShowDialog() == true)
            {
                _box.Text = Path.GetDirectoryName(dialog.FileName) ?? _box.Text;
            }

            return;
        }

        var save = Field.Name.IndexOf("output", StringComparison.OrdinalIgnoreCase) >= 0;
        var filter = FilterFor(Field.Name);

        if (save)
        {
            var dialog = new Microsoft.Win32.SaveFileDialog { Filter = filter, FileName = _box.Text };
            if (dialog.ShowDialog() == true)
            {
                _box.Text = dialog.FileName;
            }
        }
        else
        {
            var dialog = new Microsoft.Win32.OpenFileDialog { Filter = filter, FileName = _box.Text };
            if (dialog.ShowDialog() == true)
            {
                _box.Text = dialog.FileName;
            }
        }
    }

    private static string FilterFor(string name)
    {
        if (name.IndexOf("csv", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "CSV (*.csv)|*.csv|Tất cả (*.*)|*.*";
        }

        if (name.IndexOf("family", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "Family Revit (*.rfa)|*.rfa|Tất cả (*.*)|*.*";
        }

        if (name.IndexOf("template", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("source", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "Revit (*.rvt;*.rte)|*.rvt;*.rte|Tất cả (*.*)|*.*";
        }

        return "Tất cả (*.*)|*.*";
    }

    public override JToken? Value
    {
        get
        {
            var text = _box.Text.Trim();
            return text.Length == 0 ? null : text;
        }
    }
}

/// <summary>
/// Combo lấy giá trị từ mô hình đang mở (category, tham số, level, view template, family type).
/// <c>IsEditable</c> để vẫn gõ tay được khi mô hình chưa có giá trị cần dùng.
/// </summary>
internal sealed class ChoiceEditor : FieldEditorBase
{
    private readonly ComboBox _box = new() { IsEditable = true };

    public ChoiceEditor(FieldSpec field, JToken? value, IReadOnlyList<string> choices) : base(field)
    {
        foreach (var choice in choices)
        {
            _box.Items.Add(choice);
        }

        _box.Text = FormValueText.Display(value, field.IsList);
    }

    public override UIElement Build()
    {
        var label = Label();
        if (_box.Items.Count == 0)
        {
            label.Text += "  (mô hình chưa có giá trị nào — gõ tay)";
        }
        else if (Field.IsList)
        {
            label.Text += "  (chọn hoặc gõ nhiều giá trị)";
        }

        return Row(label, _box);
    }

    public override JToken? Value
    {
        get
        {
            var text = _box.Text?.Trim() ?? string.Empty;
            if (text.Length == 0)
            {
                return null;
            }

            return Field.IsList
                ? new JArray(SplitList(text))
                : text;
        }
    }
}
