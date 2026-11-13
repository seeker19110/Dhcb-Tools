using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Data;
using DhcbTools.Core.AutoCAD;
using DhcbTools.Core.AutoCAD.Attributes;
using DhcbTools.Core.AutoCAD.AutoNumbering;
using DhcbTools.Core.AutoCAD.DrawingCleanup;
using DhcbTools.Core.AutoCAD.LayerSync;
using DhcbTools.Core.AutoCAD.LayerTools;
using DhcbTools.Core.AutoCAD.Reporting;
using DhcbTools.Core.AutoCAD.TextTools;
using DhcbTools.Shared.Hosting;
using DhcbTools.Shared.Logic.Ai;
using Microsoft.Win32;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace DhcbTools.AutoCAD.UI;

/// <summary>Windows command workspace. Config types supply defaults and editor types; the catalog supplies descriptions.</summary>
public sealed class CadCommandWindow : Window
{
    private static readonly IReadOnlyDictionary<string, Type> ConfigTypes = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase)
    {
        ["LayerExport"] = typeof(LayerExportConfig), ["LayerImport"] = typeof(LayerImportConfig),
        ["DrawingCleanup"] = typeof(CleanupConfig), ["AutoNumbering"] = typeof(AutoNumberingConfig),
        ["AttributeExport"] = typeof(AttributeExportConfig), ["AttributeImport"] = typeof(AttributeImportConfig),
        ["TextReplace"] = typeof(TextReplaceConfig), ["LayerStandardCheck"] = typeof(LayerStandardCheckConfig),
        ["GridExtract"] = typeof(GridExtractConfig), ["XrefAudit"] = typeof(XrefAuditConfig),
        ["LayerTranslate"] = typeof(LayerTranslateConfig), ["EntityLayerAssign"] = typeof(EntityLayerAssignConfig), ["PolylineImport"] = typeof(PolylineImportConfig), ["EntityEdit"] = typeof(EntityEditConfig), ["BlockInsert"] = typeof(BlockInsertConfig), ["DrawingCompare"] = typeof(DrawingCompareConfig),
        ["DrawingCombine"] = typeof(DrawingCombineConfig),
        ["BlockQuantity"] = typeof(BlockQuantityConfig), ["AttributeIncrement"] = typeof(AttributeIncrementConfig),
        ["CadLayerMap"] = typeof(CadLayerMapConfig),
    };
    private static readonly JsonSerializer Serializer = JsonSerializer.Create(new JsonSerializerSettings
    {
        ContractResolver = new CamelCasePropertyNamesContractResolver(),
        Converters = { new StringEnumConverter() },
    });
    private readonly ICadCommandContext _context;
    private readonly string _configDirectory;
    private readonly ListBox _commands = new() { DisplayMemberPath = "Description", BorderThickness = new Thickness(0) };
    private readonly StackPanel _fields = new() { Margin = new Thickness(0, 4, 12, 8) };
    private readonly TextBlock _title = new() { FontSize = 21, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _note = new() { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DimGray, Margin = new Thickness(0, 8, 0, 12) };
    private readonly TextBox _result = new() { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(12), Background = new SolidColorBrush(Color.FromRgb(245, 247, 250)) };
    private readonly Button _preview = ActionButton("Xem trước");
    private readonly Button _run = ActionButton("Ghi vào bản vẽ");
    private readonly Dictionary<string, (PropertyInfo Property, Control Editor)> _editors = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<CommandDescriptor> _all = CommandCatalog.AllFor(CommandCatalog.AutoCad).Where(c => c.Implemented && !c.Internal).ToList();
    private CommandDescriptor? _descriptor;
    private string? _previewFingerprint;

    public CadCommandWindow(ICadCommandContext context, string? command = null, string? configDirectory = null)
    {
        _context = context;
        _configDirectory = configDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DHCB", "configs", "autocad");
        Title = "DHCB Tools — AutoCAD";
        Width = 1120; Height = 780; MinWidth = 850; MinHeight = 620;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brushes.White;
        FontFamily = new FontFamily("Segoe UI"); FontSize = 14;
        var layout = new Grid { Margin = new Thickness(20), Background = Brushes.White };
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(285) });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
        layout.ColumnDefinitions.Add(new ColumnDefinition());
        Content = layout;
        var sidebar = new DockPanel(); Grid.SetColumn(sidebar, 0); layout.Children.Add(sidebar);
        var brand = new TextBlock { Text = "DHCB  /  AUTOCAD", FontSize = 19, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 14) };
        DockPanel.SetDock(brand, Dock.Top); sidebar.Children.Add(brand);
        var search = new TextBox { ToolTip = "Tìm tên lệnh hoặc từ khóa tiếng Việt / Anh", Padding = new Thickness(8), Margin = new Thickness(0, 0, 0, 12) };
        System.Windows.Automation.AutomationProperties.SetName(search, "Tìm công cụ");
        DockPanel.SetDock(search, Dock.Top); sidebar.Children.Add(search);
        var template = new DataTemplate(typeof(CommandDescriptor));
        var itemText = new FrameworkElementFactory(typeof(TextBlock));
        itemText.SetBinding(TextBlock.TextProperty, new Binding("Description"));
        itemText.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
        itemText.SetValue(TextBlock.MarginProperty, new Thickness(6));
        template.VisualTree = itemText;
        _commands.DisplayMemberPath = ""; _commands.ItemTemplate = template;
        _commands.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        ScrollViewer.SetHorizontalScrollBarVisibility(_commands, ScrollBarVisibility.Disabled);
        sidebar.Children.Add(_commands);
        search.TextChanged += (_, _) => Guard(() => Filter(search.Text));
        _commands.SelectionChanged += (_, _) => Guard(() => SelectCommand(_commands.SelectedItem as CommandDescriptor));
        var work = new Grid(); Grid.SetColumn(work, 2); layout.Children.Add(work);
        work.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        work.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        work.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        work.RowDefinitions.Add(new RowDefinition { Height = new GridLength(180) });
        var heading = new StackPanel(); heading.Children.Add(_title); heading.Children.Add(_note); work.Children.Add(heading);
        var scroll = new ScrollViewer { Content = _fields, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetRow(scroll, 1); work.Children.Add(scroll);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 10) };
        Grid.SetRow(actions, 2); work.Children.Add(actions);
        actions.Children.Add(_preview); actions.Children.Add(_run);
        var reset = ActionButton("Mặc định"); actions.Children.Add(reset);
        var close = ActionButton("Đóng"); actions.Children.Add(close);
        _preview.Click += (_, _) => Guard(() => Execute(preview: true));
        _run.Click += (_, _) => Guard(() =>
        {
            if (MessageBox.Show(this, "Ghi các thay đổi vừa xem trước vào bản vẽ?", "DHCB Tools", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes)
                Execute(preview: false);
        });
        reset.Click += (_, _) => Guard(() => BuildFields(Defaults()));
        close.Click += (_, _) => Close();
        Grid.SetRow(_result, 3); work.Children.Add(_result);
        foreach (var descriptor in _all) _commands.Items.Add(descriptor);
        _commands.SelectedItem = _all.FirstOrDefault(c => c.Matches(command ?? "")) ?? _all.FirstOrDefault();
    }

    private static Button ActionButton(string text) => new() { Content = text, Padding = new Thickness(13, 7, 13, 7), Margin = new Thickness(0, 0, 8, 0), MinHeight = 36 };

    private void Filter(string text)
    {
        var selected = _descriptor;
        _commands.Items.Clear();
        foreach (var item in _all.Where(c => string.IsNullOrWhiteSpace(text) || LayerMappingSuggester.RemoveDiacritics(string.Join(" ", new[] { c.Name, c.Description }.Concat(c.Keywords).Concat(c.Aliases))).IndexOf(LayerMappingSuggester.RemoveDiacritics(text.Trim()), StringComparison.OrdinalIgnoreCase) >= 0))
            _commands.Items.Add(item);
        _commands.SelectedItem = selected != null && _commands.Items.Contains(selected) ? selected : _commands.Items.Cast<object>().FirstOrDefault();
    }

    private void SelectCommand(CommandDescriptor? descriptor)
    {
        InvalidatePreview();
        _descriptor = descriptor;
        _fields.Children.Clear(); _editors.Clear(); _result.Clear();
        _preview.IsEnabled = descriptor != null;
        if (descriptor == null) { _title.Text = "Không tìm thấy công cụ"; _note.Text = "Thử từ khóa khác."; return; }
        _title.Text = descriptor.Description;
        _note.Text = Path.GetFileName(_context.DocumentName) + "  •  " + (descriptor.WritesModel
            ? "Xem trước, kiểm tra kết quả rồi xác nhận ghi. Sửa cấu hình sẽ yêu cầu xem trước lại."
            : "Đọc bản vẽ và xuất báo cáo theo cấu hình bên dưới.");
        _preview.Content = descriptor.WritesModel ? "Xem trước" : "Thực hiện";
        _run.Visibility = descriptor.WritesModel ? Visibility.Visible : Visibility.Collapsed;
        var config = Defaults();
        var path = ConfigPath();
        if (File.Exists(path))
        {
            try
            {
                var loaded = (JObject)config.DeepClone();
                loaded.Merge(JObject.Parse(File.ReadAllText(path)), new JsonMergeSettings { MergeArrayHandling = MergeArrayHandling.Replace });
                loaded.ToObject(ConfigTypes[descriptor.Name], Serializer);
                config = loaded;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is JsonException)
            { _result.Text = "Không đọc được cấu hình đã lưu; đang dùng mặc định. " + ex.Message; }
        }
        BuildFields(config);
    }

    private JObject Defaults() => _descriptor == null ? new JObject() : JObject.FromObject(Activator.CreateInstance(ConfigTypes[_descriptor.Name])!, Serializer);
    private string ConfigPath() => Path.Combine(_configDirectory, _descriptor!.Name + ".json");

    private void BuildFields(JObject config)
    {
        InvalidatePreview(); _fields.Children.Clear(); _editors.Clear();
        if (_descriptor == null) return;
        foreach (var property in ConfigTypes[_descriptor.Name].GetProperties().Where(p => p.CanRead && p.CanWrite && p.Name != "DryRun"))
        {
            var name = char.ToLowerInvariant(property.Name[0]) + property.Name.Substring(1);
            var spec = _descriptor.Fields.FirstOrDefault(f => f.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            var caption = Caption(property.Name);
            var label = caption != property.Name ? caption : spec?.Description ?? caption;
            var value = config.GetValue(name, StringComparison.OrdinalIgnoreCase);
            var row = new StackPanel { Margin = new Thickness(0, 0, 0, 11) };
            _fields.Children.Add(row);
            var type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
            Control editor;
            if (type == typeof(bool))
            {
                var check = new CheckBox { Content = label, IsChecked = value?.Type == JTokenType.Boolean && value.Value<bool>(), Padding = new Thickness(0, 4, 0, 4) };
                check.Checked += (_, _) => InvalidatePreview(); check.Unchecked += (_, _) => InvalidatePreview();
                row.Children.Add(check); editor = check;
            }
            else
            {
                row.Children.Add(new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 5) });
                if (type.IsEnum)
                {
                    var selected = value?.Type == JTokenType.Integer ? Enum.ToObject(type, value.Value<int>()).ToString() : value?.ToString();
                    var combo = new ComboBox { ItemsSource = Enum.GetNames(type), SelectedItem = selected, Padding = new Thickness(7, 4, 7, 4) };
                    combo.SelectionChanged += (_, _) => InvalidatePreview(); row.Children.Add(combo); editor = combo;
                }
                else
                {
                    var text = new TextBox { Text = FormValueText.Display(value, type == typeof(List<string>)), Padding = new Thickness(8, 5, 8, 5), MinWidth = 180 };
                    text.TextChanged += (_, _) => InvalidatePreview(); editor = text;
                    if (name.EndsWith("Path", StringComparison.OrdinalIgnoreCase))
                    {
                        var paths = new DockPanel(); var browse = ActionButton("Chọn…");
                        DockPanel.SetDock(browse, Dock.Right); paths.Children.Add(browse); paths.Children.Add(text); row.Children.Add(paths);
                        browse.Click += (_, _) => Guard(() => Browse(text, name, label));
                    }
                    else row.Children.Add(text);
                }
            }
            editor.ToolTip = name;
            System.Windows.Automation.AutomationProperties.SetName(editor, name);
            _editors.Add(name, (property, editor));
        }
    }

    private JObject Collect()
    {
        var config = new JObject();
        foreach (var item in _editors)
        {
            var type = Nullable.GetUnderlyingType(item.Value.Property.PropertyType) ?? item.Value.Property.PropertyType;
            var editor = item.Value.Editor;
            if (editor is CheckBox check) config[item.Key] = check.IsChecked == true;
            else if (editor is ComboBox combo) config[item.Key] = combo.SelectedItem?.ToString() ?? "";
            else
            {
                var text = ((TextBox)editor).Text;
                if (type == typeof(string)) config[item.Key] = text.Length == 0 && Defaults()[item.Key]?.Type == JTokenType.Null ? JValue.CreateNull() : new JValue(text);
                else if (type == typeof(List<string>)) config[item.Key] = new JArray(text.Split(new[] { ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).Where(s => s.Length > 0));
                else if (type.IsPrimitive || type == typeof(decimal))
                {
                    var value = FormValueText.Number(text);
                    var number = value?.Value<double>() ?? double.NaN;
                    if (double.IsNaN(number) || double.IsInfinity(number))
                        throw new FormatException(Caption(item.Value.Property.Name) + ": cần nhập số hữu hạn.");
                    if ((type == typeof(int) || type == typeof(long)) && number != Math.Truncate(number))
                        throw new FormatException(Caption(item.Value.Property.Name) + ": cần nhập số nguyên.");
                    config[item.Key] = value;
                }
                else config[item.Key] = JToken.Parse(text);
            }
        }
        var typed = config.ToObject(ConfigTypes[_descriptor!.Name], Serializer)!;
        RequiredConfig.ThrowIfIncomplete(typed, _descriptor.Name);
        return config;
    }

    private string Fingerprint(JObject config) => _context.DocumentId + "\n" + config.ToString(Formatting.None) + "\n"
        + BridgeCommitGuard.CaptureInputSnapshot(config, _context.Revision);

    private void InvalidatePreview() { _previewFingerprint = null; _run.IsEnabled = false; }

    private void Execute(bool preview)
    {
        if (_descriptor == null) return;
        if (!_context.IsActive)
            throw new InvalidOperationException("Bản vẽ hiện hành đã đổi. Đóng cửa sổ và mở lại công cụ.");
        var config = Collect();
        var fingerprint = Fingerprint(config);
        if (!preview && (_previewFingerprint == null || _previewFingerprint != fingerprint))
        {
            InvalidatePreview(); _result.Text = "Cấu hình, bản vẽ hoặc file đầu vào đã thay đổi. Hãy xem trước lại."; return;
        }
        InvalidatePreview();
        config["dryRun"] = preview;
        var timer = System.Diagnostics.Stopwatch.StartNew();
        CommandResult result;
        var previous = Cursor;
        try
        {
            Cursor = System.Windows.Input.Cursors.Wait;
            result = _context.Execute(_descriptor.Name, config);
        }
        finally { Cursor = previous; }
        timer.Stop(); config.Remove("dryRun");
        _result.Text = string.Join(Environment.NewLine, new[]
        {
            result.IsComplete ? "Hoàn tất" : "Chưa hoàn tất — kiểm tra lỗi bên dưới",
            result.Summary, $"Đối tượng: {result.AffectedCount:N0}  •  Thời gian: {timer.ElapsedMilliseconds:N0} ms", "",
        }.Concat(result.Errors.Take(100).Select(s => "Lỗi: " + s)).Concat(result.Messages.Take(200)));
        if (result.Errors.Count > 100 || result.Messages.Count > 200) _result.AppendText("\nChi tiết vượt giới hạn hiển thị; kiểm tra báo cáo do lệnh xuất ra.");
        if (preview && _descriptor.WritesModel && result.IsComplete && fingerprint == Fingerprint(config))
        { _previewFingerprint = fingerprint; _run.IsEnabled = true; }
        if (result.IsComplete)
        {
            try { Directory.CreateDirectory(_configDirectory); File.WriteAllText(ConfigPath(), config.ToString(Formatting.Indented)); }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { _result.AppendText("\nKhông lưu được cấu hình: " + ex.Message); }
        }
    }

    private void Browse(TextBox target, string name, string label)
    {
        var output = name.StartsWith("output", StringComparison.OrdinalIgnoreCase);
        var filter = label.IndexOf("CSV", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("csv", StringComparison.OrdinalIgnoreCase) >= 0 ? "CSV (*.csv)|*.csv|Mọi file (*.*)|*.*"
            : name == "otherPath" ? "Bản vẽ (*.dwg)|*.dwg|Mọi file (*.*)|*.*"
            : name == "rulesPath" ? "Quy tắc (*.json)|*.json|Mọi file (*.*)|*.*"
            : name == "revitTypesPath" ? "Danh sách (*.txt)|*.txt|Mọi file (*.*)|*.*" : "Mọi file (*.*)|*.*";
        if (output)
        {
            var dialog = new SaveFileDialog { Title = label, FileName = target.Text, Filter = filter, AddExtension = true, OverwritePrompt = true };
            if (dialog.ShowDialog(this) == true) target.Text = dialog.FileName;
        }
        else
        {
            var dialog = new OpenFileDialog { Title = label, FileName = target.Text, Filter = filter, CheckFileExists = true };
            if (dialog.ShowDialog(this) == true) target.Text = dialog.FileName;
        }
    }

    private void Guard(Action action)
    {
        try { action(); }
        catch (Exception ex)
        {
            InvalidatePreview(); DhcbLog.Error("AutoCAD", "cửa sổ công cụ", ex);
            _result.Text = "Không thực hiện được: " + ex.Message;
        }
    }

    private static string Caption(string name) => name switch
    {
        "StartNumber" => "Số bắt đầu", "Step" => "Bước nhảy", "PadWidth" => "Số chữ số tối thiểu",
        "Direction" => "Hướng đánh số", "RowToleranceMm" => "Dung sai gom hàng / cột (đơn vị bản vẽ)",
        "IgnoreCase" => "Không phân biệt hoa / thường", "Multiline" => "Regex khớp từng dòng",
        "RemoveEmptyLayers" => "Xóa layer rỗng", "PurgeUnusedBlocks" => "Dọn block không dùng",
        "PurgeUnusedLinetypes" => "Dọn kiểu nét không dùng", "KeepLayerNameContains" => "Giữ layer có tên chứa (ngăn bằng dấu chấm phẩy)",
        "IncludePaperSpace" => "Bao gồm block trong layout", "Encoding" => "Mã hóa",
        "Scope" => "Phạm vi", "LayerContains" => "Lọc layer có tên chứa", "AttributeTag" => "Tag attribute",
        "IncludeDbText" => "Tìm trong Text", "IncludeMText" => "Tìm trong MText", "IncludeAttributes" => "Tìm trong attribute",
        "MaxPreviewItems" => "Số chi tiết tối đa trong xem trước", "TimeBudgetSeconds" => "Thời gian xử lý tối đa (giây)", _ => name,
    };
}
