using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace DhcbTools.Revit.UI;

/// <summary>
/// Sinh icon phẳng (flat, bo góc, chữ đầu) ngay lúc chạy thay vì đóng gói file ảnh: giữ add-in chỉ
/// một DLL, và icon luôn sắc nét ở mọi mức DPI vì là vector chứ không phải bitmap phóng to.
/// Bảng màu theo nhóm chức năng để mắt nhận ra panel trước khi kịp đọc chữ — dùng chung tông với
/// Ribbon bên AutoCAD (<c>DhcbTools.AutoCAD.Ribbon.RibbonIcons</c>) để hai add-in trông cùng một bộ.
/// </summary>
internal static class RibbonIcons
{
    public static readonly Color Core = Color.FromRgb(0x2E, 0x7D, 0x32);     // xanh lá — dữ liệu nền tảng
    public static readonly Color Export = Color.FromRgb(0x00, 0x83, 0x8F);   // xanh mòng két — xuất & báo cáo
    public static readonly Color Init = Color.FromRgb(0xEF, 0x6C, 0x00);     // cam — khởi tạo dự án
    public static readonly Color Mepf = Color.FromRgb(0xC6, 0x28, 0x28);     // đỏ — MEPF
    public static readonly Color Sheets = Color.FromRgb(0x6A, 0x1B, 0x9A);   // tím — hồ sơ & style
    public static readonly Color Checks = Color.FromRgb(0x15, 0x65, 0xC0);   // xanh dương — kiểm tra & AI

    /// <summary>Cỡ vẽ chuẩn duy nhất cho mọi icon — vector nên thu nhỏ vẫn sắc.</summary>
    private const int Size = 32;

    /// <param name="glyph">1–2 ký tự hiện giữa icon.</param>
    public static ImageSource Create(string glyph, Color color)
    {
        const int size = Size;

        var drawing = new DrawingGroup();
        using (var dc = drawing.Open())
        {
            // Chừa lề: icon vẽ kín khung trông nặng và to hơn hẳn icon gốc của Revit bên cạnh.
            var margin = size * 0.16;
            var box = size - (margin * 2);
            var rect = new Rect(margin, margin, box, box);
            var radius = box * 0.26;

            var background = new LinearGradientBrush(
                Lighten(color, 0.18), Darken(color, 0.10),
                new Point(0, 0), new Point(1, 1));
            dc.DrawRoundedRectangle(background, null, rect, radius, radius);

            var text = new FormattedText(
                glyph,
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal),
                box * (glyph.Length > 1 ? 0.46 : 0.60),
                Brushes.White,
                pixelsPerDip: 1.0);

            dc.DrawText(text, new Point((size - text.Width) / 2, (size - text.Height) / 2));
        }

        var image = new DrawingImage(drawing);
        image.Freeze();
        return image;
    }

    private static Color Lighten(Color c, double amount) => Mix(c, Colors.White, amount);

    private static Color Darken(Color c, double amount) => Mix(c, Colors.Black, amount);

    private static Color Mix(Color a, Color b, double t) => Color.FromRgb(
        (byte)(a.R + (b.R - a.R) * t),
        (byte)(a.G + (b.G - a.G) * t),
        (byte)(a.B + (b.B - a.B) * t));
}
