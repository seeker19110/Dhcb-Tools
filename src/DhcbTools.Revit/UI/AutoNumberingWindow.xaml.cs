using System.Windows;
using DhcbTools.Core.AutoNumbering;

namespace DhcbTools.Revit.UI;

/// <summary>Cửa sổ WPF nhập cấu hình cho lệnh đánh số tự động (vỏ desktop, không chứa logic nghiệp vụ).</summary>
public partial class AutoNumberingWindow : Window
{
    public AutoNumberingConfig? Config { get; private set; }

    public AutoNumberingWindow()
    {
        InitializeComponent();
    }

    private void OnRun(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(CategoryBox.Text))
        {
            System.Windows.MessageBox.Show(this, "Vui lòng nhập category.", "DHCB Tools", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var startNumber = int.TryParse(StartNumberBox.Text, out var s) ? s : 1;
        var padWidth = int.TryParse(PadWidthBox.Text, out var p) ? p : 0;

        Config = new AutoNumberingConfig
        {
            Category = CategoryBox.Text.Trim(),
            ParameterName = string.IsNullOrWhiteSpace(ParameterBox.Text) ? "Mark" : ParameterBox.Text.Trim(),
            Prefix = PrefixBox.Text ?? string.Empty,
            StartNumber = startNumber,
            PadWidth = padWidth,
            DryRun = DryRunCheck.IsChecked == true,
        };

        DialogResult = true;
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
