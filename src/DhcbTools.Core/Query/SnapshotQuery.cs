using Autodesk.Revit.DB;

namespace DhcbTools.Core.Query;

/// <summary>
/// Giai đoạn 10.1 — ảnh chụp một view, trả về base64 để agent <b>nhìn thấy</b> mô hình.
/// <para>
/// Đây là mảnh còn thiếu để khép vòng: không có ảnh thì agent chỉ đọc được số đếm, không tự kiểm
/// được việc mình vừa làm trông có đúng không, và kỹ sư cũng phải tự đi mở Revit ra xem.
/// </para>
/// <para>
/// Dùng <c>Document.ExportImage</c> nên không cần RevitAPIUI — Core giữ nguyên nguyên tắc không
/// tham chiếu UI. Ảnh ghi ra file tạm rồi đọc lại vì API chỉ xuất ra đĩa.
/// </para>
/// </summary>
internal static class SnapshotQuery
{
    /// <summary>Giới hạn kích thước ảnh trả về; ảnh to hơn thì chỉ trả đường dẫn.</summary>
    private const int MaxInlineBytes = 6 * 1024 * 1024;

    public static object Snapshot(Document doc, QueryParams p)
    {
        View? view;
        try
        {
            view = string.IsNullOrWhiteSpace(p.ViewName)
                ? doc.ActiveView
                : new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>()
                    .FirstOrDefault(v => !v.IsTemplate && string.Equals(v.Name, p.ViewName, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex)
        {
            return new { error = "Không lấy được view: " + ex.Message };
        }

        if (view == null)
        {
            return new
            {
                error = string.IsNullOrWhiteSpace(p.ViewName)
                    ? "Không có view nào đang mở."
                    : $"Không tìm thấy view \"{p.ViewName}\".",
            };
        }

        if (!view.CanBePrinted)
        {
            return new { error = $"View \"{view.Name}\" không xuất ảnh được (schedule, view template…)." };
        }

        var folder = Path.Combine(Path.GetTempPath(), "DHCB", "snapshots");
        var stem = Path.Combine(folder, "view-" + DateTime.Now.ToString("HHmmss-fff"));

        try
        {
            Directory.CreateDirectory(folder);

            var options = new ImageExportOptions
            {
                FilePath = stem,
                ExportRange = ExportRange.SetOfViews,
                HLRandWFViewsFileType = ImageFileType.PNG,
                ShadowViewsFileType = ImageFileType.PNG,
                ImageResolution = ImageResolution.DPI_150,
                ZoomType = ZoomFitType.FitToPage,
                PixelSize = Math.Max(200, Math.Min(p.ImageWidth, 4000)),
                FitDirection = FitDirectionType.Horizontal,
            };
            options.SetViewsAndSheets(new List<ElementId> { view.Id });

            doc.ExportImage(options);
        }
        catch (Exception ex)
        {
            return new { error = $"Không xuất được ảnh view \"{view.Name}\": {ex.Message}" };
        }

        // Revit thêm hậu tố tên view vào tên file nên phải đi tìm file vừa tạo.
        var file = Directory.EnumerateFiles(folder, Path.GetFileName(stem) + "*.png")
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();

        if (file == null)
        {
            return new { error = "Đã gọi ExportImage nhưng không thấy file PNG nào được tạo." };
        }

        try
        {
            var bytes = File.ReadAllBytes(file);
            var tooBig = bytes.Length > MaxInlineBytes;

            return new
            {
                viewName = view.Name,
                viewType = view.ViewType.ToString(),
                viewId = RevitCompat.IdValue(view.Id),
                path = file,
                bytes = bytes.Length,
                mimeType = "image/png",
                // Ảnh quá lớn thì chỉ đưa đường dẫn — nhồi 20 MB base64 qua HTTP là tự bắn vào chân.
                base64 = tooBig ? null : Convert.ToBase64String(bytes),
                note = tooBig ? $"Ảnh {bytes.Length / 1024} KB vượt ngưỡng nhúng, đọc từ path." : null,
            };
        }
        catch (Exception ex)
        {
            return new { error = "Không đọc lại được ảnh vừa xuất: " + ex.Message };
        }
    }
}
