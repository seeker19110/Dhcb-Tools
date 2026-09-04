using System.Drawing;
using System.Drawing.Imaging;
using Autodesk.AutoCAD.DatabaseServices;

namespace DhcbTools.Core.AutoCAD.Query;

/// <summary>
/// Giai đoạn 10.1 (phía AutoCAD) — ảnh để agent <b>nhìn thấy</b> bản vẽ, đối xứng với <c>SnapshotQuery</c>
/// bên Revit. AutoCAD không có API xuất ảnh headless kiểu <c>Document.ExportImage</c>, nên chia hai tầng:
/// <list type="bullet">
///   <item><b>Core (file này)</b>: <see cref="Database.ThumbnailBitmap"/> — ảnh xem trước mà AutoCAD lưu
///   sẵn trong DWG lúc save. Chỉ cần <see cref="Database"/>, chạy được cả trong <c>accoreconsole</c>.
///   Nhược điểm nói thẳng trong kết quả: là ảnh <b>lúc save</b>, không phải trạng thái hiện tại.</item>
///   <item><b>Vỏ</b> (<c>AcadUiQueryHandler</c>): render sống khung nhìn hiện tại bằng GraphicsSystem;
///   hỏng thì rơi về tầng này.</item>
/// </list>
/// Cả hai tầng cùng đi qua <see cref="Package"/> để kết quả có cùng hình dạng, và cùng ghi file ra thư mục
/// tạm như bên Revit — agent đọc <c>base64</c> hoặc <c>path</c> đều được.
/// </summary>
public static class AcadSnapshot
{
    /// <summary>Giới hạn kích thước ảnh nhúng — nhồi vài chục MB base64 qua HTTP là tự bắn vào chân.</summary>
    private const int MaxInlineBytes = 6 * 1024 * 1024;

    /// <summary>
    /// Ảnh xem trước lưu trong DWG. Không phải bản vẽ nào cũng có (lưu bằng phần mềm khác, hoặc
    /// <c>THUMBSAVE</c> tắt) — khi đó nói rõ chứ không trả ảnh trắng.
    /// </summary>
    public static object Thumbnail(Database db)
    {
        Bitmap? bitmap;
        try
        {
            bitmap = db.ThumbnailBitmap;
        }
        catch (Exception ex)
        {
            return new { error = "Không đọc được ảnh xem trước trong DWG: " + ex.Message };
        }

        if (bitmap == null)
        {
            return new
            {
                error = "Bản vẽ không có ảnh xem trước (THUMBSAVE tắt, hoặc file lưu bằng phần mềm khác). " +
                        "Mở bản vẽ trong AutoCAD và gọi lại với source=\"live\" để render khung nhìn hiện tại.",
            };
        }

        using (bitmap)
        {
            return Package(bitmap, source: "thumbnail", note:
                "Ảnh xem trước lưu trong DWG lúc SAVE gần nhất — không phản ánh thay đổi chưa lưu. " +
                "Muốn nhìn trạng thái hiện tại, gọi từ AutoCAD đang mở với source=\"live\".");
        }
    }

    /// <summary>
    /// Ghi bitmap ra PNG trong thư mục tạm rồi trả cả đường dẫn lẫn base64 — cùng hình dạng với
    /// <c>snapshot</c> bên Revit để agent không phải học hai kiểu kết quả.
    /// </summary>
    public static object Package(Bitmap bitmap, string source, string? note)
    {
        var folder = Path.Combine(Path.GetTempPath(), "DHCB", "snapshots");
        // Guid chứ không phải dấu giờ: hai request cùng giây không được vớ nhầm ảnh của nhau.
        var file = Path.Combine(folder, "dwg-" + Guid.NewGuid().ToString("N") + ".png");

        byte[] bytes;
        try
        {
            Directory.CreateDirectory(folder);
            PruneOld(folder);

            using var stream = new MemoryStream();
            bitmap.Save(stream, ImageFormat.Png);
            bytes = stream.ToArray();
            File.WriteAllBytes(file, bytes);
        }
        catch (Exception ex)
        {
            return new { error = "Không ghi được ảnh PNG: " + ex.Message };
        }

        var tooBig = bytes.Length > MaxInlineBytes;
        return new
        {
            source,
            width = bitmap.Width,
            height = bitmap.Height,
            path = file,
            bytes = bytes.Length,
            mimeType = "image/png",
            base64 = tooBig ? null : Convert.ToBase64String(bytes),
            note = tooBig
                ? (note == null ? string.Empty : note + " ") + $"Ảnh {bytes.Length / 1024} KB vượt ngưỡng nhúng, đọc từ path."
                : note,
        };
    }

    /// <summary>Xoá ảnh cũ hơn một ngày — thư mục tạm không được phình mãi theo số lần agent "nhìn".</summary>
    private static void PruneOld(string folder)
    {
        try
        {
            var cutoff = DateTime.UtcNow.AddDays(-1);
            foreach (var old in Directory.EnumerateFiles(folder, "dwg-*.png").Where(f => File.GetLastWriteTimeUtc(f) < cutoff))
            {
                try { File.Delete(old); } catch (Exception) { /* file đang mở — lần sau */ }
            }
        }
        catch (Exception)
        {
            // Dọn không được thì vẫn chụp được; không phải lý do để hỏng request.
        }
    }
}
