using System.Drawing;
using System.Globalization;
using System.Text.RegularExpressions;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.PlottingServices;
using CoreApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace DhcbTools.Core.AutoCAD.Query;

/// <summary>
/// Ảnh <b>trạng thái hiện tại</b> của Model Space mà không cần GUI: plot ra PNG qua <c>PublishToWeb PNG.pc3</c> — bộ
/// render của chính AutoCAD, chạy được trong <c>accoreconsole</c>. Lấp chỗ hổng của <see cref="AcadSnapshot.Thumbnail"/>
/// (chỉ là ảnh lúc save) khi agent đọc ngầm bản sao DWG, và cho phép khoanh vùng: <c>window</c> [x1,y1,x2,y2] hoặc
/// <c>handles</c> (ôm extents các đối tượng, nới thêm lề) — "zoom vào chỗ vừa vẽ" để tự kiểm kết quả.
/// </summary>
public static class AcadPlotSnapshot
{
    private const string Device = "PublishToWeb PNG.pc3";

    /// <summary>Vùng cần chụp theo tham số; null = toàn bộ extents. Lỗi tham số trả qua <paramref name="error"/>.</summary>
    public static Extents3d? Region(Database db, AcadQueryParams p, out string? error, out string? note)
    {
        error = null;
        note = null;
        if (p.Window is { Count: > 0 } w)
        {
            if (w.Count != 4 || w.Any(v => double.IsNaN(v) || double.IsInfinity(v)) || w[0] == w[2] || w[1] == w[3])
            {
                error = "window phải là [x1, y1, x2, y2] với hai góc khác nhau cả x lẫn y.";
                return null;
            }

            return new Extents3d(new Point3d(Math.Min(w[0], w[2]), Math.Min(w[1], w[3]), 0), new Point3d(Math.Max(w[0], w[2]), Math.Max(w[1], w[3]), 0));
        }

        if (p.Handles is not { Count: > 0 })
        {
            return null;
        }

        Extents3d? box = null;
        var missing = new List<string>();
        using (var tr = db.TransactionManager.StartTransaction())
        {
            foreach (var text in p.Handles)
            {
                try
                {
                    if (!Shared.Logic.Cad.HandleText.TryParse(text, out var raw)) throw new FormatException();
                    var id = db.GetObjectId(false, new Handle(raw), 0);
                    if (id.IsNull || id.IsErased || tr.GetObject(id, OpenMode.ForRead) is not Entity entity) throw new FormatException();
                    var e = entity.GeometricExtents;
                    if (box is { } b)
                    {
                        b.AddExtents(e);
                        box = b;
                    }
                    else
                    {
                        box = e;
                    }
                }
                catch (Exception ex) when (ex is FormatException or Autodesk.AutoCAD.Runtime.Exception)
                {
                    missing.Add(text);
                }
            }

            tr.Abort();
        }

        if (box == null)
        {
            error = "Không handle nào trỏ tới đối tượng có hình (" + string.Join(", ", missing.Take(20)) + ").";
            return null;
        }

        // Nới lề theo cạnh lớn để một điểm/đoạn thẳng đứng vẫn ra khung có diện tích, và còn thấy chút xung quanh.
        var min = box.Value.MinPoint;
        var max = box.Value.MaxPoint;
        var pad = Math.Max(Math.Max(max.X - min.X, max.Y - min.Y) * Math.Max(0, p.Margin), 1e-6) + (max.X - min.X == 0 || max.Y - min.Y == 0 ? 1 : 0);
        if (missing.Count > 0)
        {
            note = $"Bỏ qua {missing.Count} handle không tìm thấy/không có hình: {string.Join(", ", missing.Take(20))}.";
        }

        return new Extents3d(new Point3d(min.X - pad, min.Y - pad, 0), new Point3d(max.X + pad, max.Y + pad, 0));
    }

    /// <summary>Plot Model Space (toàn bộ hoặc <paramref name="region"/>) ra PNG gần cỡ <paramref name="width"/> pixel.</summary>
    public static object Render(Database db, Extents3d? region, int width, string? note)
    {
        if (PlotFactory.ProcessPlotState != ProcessPlotState.NotPlotting)
        {
            return new { error = "AutoCAD đang bận plot việc khác — thử lại sau." };
        }

        var file = Path.Combine(Path.GetTempPath(), "DHCB", "snapshots", "plot-" + Guid.NewGuid().ToString("N") + ".png");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        object? background = null;
        try
        {
            background = CoreApp.GetSystemVariable("BACKGROUNDPLOT");
            CoreApp.SetSystemVariable("BACKGROUNDPLOT", (short)0);
        }
        catch (Exception)
        {
            // accoreconsole không có plot nền; không đặt được biến thì vẫn plot tiền cảnh.
        }

        string media;
        string? previousLayout = null;
        try
        {
            // PlotEngine chỉ plot được layout ĐANG HIỆN HÀNH (eLayoutNotCurrent) — bản vẽ lưu khi đang ở tab giấy thì
            // mở ra cũng ở tab giấy. Chuyển tạm sang Model rồi trả lại trong finally.
            var manager = LayoutManager.Current;
            string modelName;
            using (var look = db.TransactionManager.StartTransaction())
            {
                var ms = (BlockTableRecord)look.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
                modelName = ((Layout)look.GetObject(ms.LayoutId, OpenMode.ForRead)).LayoutName;
                look.Abort();
            }

            if (!string.Equals(manager.CurrentLayout, modelName, StringComparison.OrdinalIgnoreCase))
            {
                previousLayout = manager.CurrentLayout;
                manager.CurrentLayout = modelName;
            }

            using var tr = db.TransactionManager.StartTransaction();
            var modelSpace = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
            var layout = (Layout)tr.GetObject(modelSpace.LayoutId, OpenMode.ForRead);
            using var settings = new PlotSettings(layout.ModelType);
            settings.CopyFrom(layout);

            var validator = PlotSettingsValidator.Current;
            validator.SetPlotConfigurationName(settings, Device, null);
            validator.RefreshLists(settings);
            media = PickMedia(validator.GetCanonicalMediaNameList(settings).Cast<string>(), width, region);
            validator.SetPlotConfigurationName(settings, Device, media);
            if (region is { } r)
            {
                validator.SetPlotWindowArea(settings, new Extents2d(r.MinPoint.X, r.MinPoint.Y, r.MaxPoint.X, r.MaxPoint.Y));
                validator.SetPlotType(settings, Autodesk.AutoCAD.DatabaseServices.PlotType.Window);
            }
            else
            {
                validator.SetPlotType(settings, Autodesk.AutoCAD.DatabaseServices.PlotType.Extents);
            }

            validator.SetUseStandardScale(settings, true);
            validator.SetStdScaleType(settings, StdScaleType.ScaleToFit);
            validator.SetPlotCentered(settings, true);
            validator.SetPlotRotation(settings, PlotRotation.Degrees000);

            var info = new PlotInfo { Layout = modelSpace.LayoutId, OverrideSettings = settings };
            new PlotInfoValidator { MediaMatchingPolicy = MatchingPolicy.MatchEnabled }.Validate(info);

            using (var engine = PlotFactory.CreatePublishEngine())
            {
                engine.BeginPlot(null, null);
                engine.BeginDocument(info, "dhcb-snapshot", null, 1, true, file);
                engine.BeginPage(new PlotPageInfo(), info, true, null);
                engine.BeginGenerateGraphics(null);
                engine.EndGenerateGraphics(null);
                engine.EndPage(null);
                engine.EndDocument(null);
                engine.EndPlot(null);
            }

            tr.Abort();
        }
        catch (Exception ex) when (ex is Autodesk.AutoCAD.Runtime.Exception or InvalidOperationException or ArgumentException)
        {
            return new { error = $"Không plot được ảnh ({ex.GetType().Name}: {ex.Message}). Cần {Device} trong thư mục Plotters của AutoCAD." };
        }
        finally
        {
            if (previousLayout != null)
            {
                try { LayoutManager.Current.CurrentLayout = previousLayout; } catch (Exception) { /* giữ Model */ }
            }

            if (background != null)
            {
                try { CoreApp.SetSystemVariable("BACKGROUNDPLOT", background); } catch (Exception) { /* giữ nguyên */ }
            }
        }

        if (!File.Exists(file))
        {
            return new { error = "Plot xong nhưng không thấy file PNG — kiểm tra quyền ghi thư mục tạm." };
        }

        try
        {
            using var stream = new MemoryStream(File.ReadAllBytes(file));
            using var bitmap = new Bitmap(stream);
            var where = region is { } box
                ? string.Format(CultureInfo.InvariantCulture, "vùng ({0:0.###}, {1:0.###}) – ({2:0.###}, {3:0.###})", box.MinPoint.X, box.MinPoint.Y, box.MaxPoint.X, box.MaxPoint.Y)
                : "toàn bộ extents Model Space";
            return AcadSnapshot.Package(bitmap, source: "plot",
                note: $"Ảnh plot {where} bằng {Device} ({media}) — đúng trạng thái hiện tại của database, kể cả thay đổi chưa lưu." + (note == null ? string.Empty : " " + note));
        }
        finally
        {
            try { File.Delete(file); } catch (Exception) { /* Package đã giữ bản riêng */ }
        }
    }

    private static readonly Regex MediaSize = new(@"(\d+(?:\.\d+)?)_x_(\d+(?:\.\d+)?)_Pixels", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>Khổ giấy pixel của pc3 gần nhất với chiều rộng xin, ưu tiên khổ cùng hướng với vùng chụp.</summary>
    private static string PickMedia(IEnumerable<string> names, int width, Extents3d? region)
    {
        var landscape = region is not { } r || (r.MaxPoint.X - r.MinPoint.X) >= (r.MaxPoint.Y - r.MinPoint.Y);
        var sized = names
            .Select(n => (Name: n, Match: MediaSize.Match(n)))
            .Where(x => x.Match.Success)
            .Select(x => (x.Name, W: double.Parse(x.Match.Groups[1].Value, CultureInfo.InvariantCulture), H: double.Parse(x.Match.Groups[2].Value, CultureInfo.InvariantCulture)))
            .ToList();
        if (sized.Count == 0)
        {
            return names.First();
        }

        return sized
            .OrderBy(m => (m.W >= m.H) == landscape ? 0 : 1)
            .ThenBy(m => Math.Abs(Math.Max(m.W, m.H) - width))
            .First().Name;
    }
}
