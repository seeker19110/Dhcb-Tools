using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DhcbTools.AutoCAD.Ribbon;

/// <summary>
/// Sinh icon phẳng (bo góc, chữ đầu) ngay lúc chạy thay vì đóng gói file ảnh: add-in vẫn chỉ một DLL.
/// Bảng màu theo nhóm chức năng để mắt nhận ra panel trước khi kịp đọc chữ — dùng chung tông với
/// Ribbon bên Revit (<c>DhcbTools.Revit.UI.RibbonIcons</c>) để hai add-in trông cùng một bộ.
///
/// Render ra <see cref="RenderTargetBitmap"/> ĐÚNG số pixel Ribbon cần, không trả DrawingImage:
/// biên của DrawingImage lấy theo nội dung đã vẽ, nên khi Ribbon co giãn vào ô 16px thì icon bị méo
/// và cắt mất chữ (đã gặp thật). Bitmap đúng cỡ thì vẽ sao hiện vậy.
/// </summary>
internal static class RibbonIcons
{
    public static readonly Color Layer = Color.FromRgb(0x2E, 0x7D, 0x32);    // xanh lá — layer
    public static readonly Color Block = Color.FromRgb(0xEF, 0x6C, 0x00);    // cam — block & attribute
    public static readonly Color Drawing = Color.FromRgb(0x15, 0x65, 0xC0);  // xanh dương — nội dung bản vẽ
    public static readonly Color Bridge = Color.FromRgb(0x6A, 0x1B, 0x9A);   // tím — nối sang Revit/agent

    /// <summary>Cỡ ô icon nhỏ của Ribbon AutoCAD (nút Standard).</summary>
    public const int Small = 16;

    /// <summary>Cỡ ô icon lớn (nút Large) — dựng sẵn để dùng nếu sau này cần nút lớn.</summary>
    public const int Large = 32;

    public static ImageSource Create(string glyph, Color color, int size = Small)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            // Chừa lề: icon vẽ kín ô trông nặng và to hơn hẳn icon gốc của Autodesk bên cạnh.
            var margin = size * 0.09;
            var box = size - (margin * 2);
            var rect = new Rect(margin, margin, box, box);

            var background = new LinearGradientBrush(
                Lighten(color, 0.18), Darken(color, 0.10),
                new Point(0, 0), new Point(1, 1));
            dc.DrawRoundedRectangle(background, null, rect, box * 0.24, box * 0.24);

            var text = new FormattedText(
                glyph,
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal),
                box * (glyph.Length > 1 ? 0.62 : 0.78),
                Brushes.White,
                pixelsPerDip: 1.0);

            // Căn giữa theo chính ô đã vẽ, không theo cả canvas — chữ hai ký tự mới không lệch.
            dc.DrawText(text, new Point(
                rect.X + ((box - text.Width) / 2),
                rect.Y + ((box - text.Height) / 2)));
        }

        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    private static Color Lighten(Color c, double amount) => Mix(c, Colors.White, amount);

    private static Color Darken(Color c, double amount) => Mix(c, Colors.Black, amount);

    private static Color Mix(Color a, Color b, double t) => Color.FromRgb(
        (byte)(a.R + (b.R - a.R) * t),
        (byte)(a.G + (b.G - a.G) * t),
        (byte)(a.B + (b.B - a.B) * t));
}
