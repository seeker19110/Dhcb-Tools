using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.GraphicsSystem;
using DhcbTools.Core.AutoCAD.Query;
using DhcbTools.Shared.Logic.Cad;

namespace DhcbTools.AutoCAD.Bridge;

/// <summary>
/// Giai đoạn 10.1 (phía AutoCAD) — truy vấn cần <see cref="Editor"/>: đang chọn gì, đang ở layout nào,
/// và <b>chỉ cho kỹ sư xem</b> entity nào.
/// <para>
/// Nằm ở vỏ chứ không ở Core vì Core cố ý chỉ đụng <see cref="Database"/> (chạy được cả trong
/// <c>accoreconsole</c>, nơi không có Editor). Query nào không thuộc nhóm này thì chuyển thẳng xuống
/// <see cref="AcadQueryHandler"/>.
/// </para>
/// <para>
/// Đây là mảnh đối xứng với <c>UiQueryHandler</c> bên Revit, và sinh ra vì cùng một lý do: agent đọc
/// được số đếm nhưng không biết kỹ sư đang chọn gì, đang mở layout nào, và không có cách nào khoanh
/// vùng để người ngồi máy nhìn thấy thứ nó vừa đụng tới.
/// </para>
/// </summary>
internal static class AcadUiQueryHandler
{
    public static object Handle(Document document, QueryRequest request)
    {
        return request.Query.ToUpperInvariant() switch
        {
            "SELECTION" => Selection(document, request.Params),
            "SHOW_ENTITIES" => ShowEntities(document, request.Params),
            "ACTIVE_LAYOUT" => ActiveLayout(document),
            "SNAPSHOT" => Snapshot(document, request.Params),
            _ => AcadQueryHandler.Handle(document.Database, request),
        };
    }

    /// <summary>
    /// Đọc lựa chọn hiện tại; truyền <c>handles</c> thì ĐẶT lựa chọn — cách agent nói "những cái này
    /// đây" cho kỹ sư ngồi máy, thay vì đọc ra một danh sách handle không ai dò nổi bằng mắt.
    /// </summary>
    private static object Selection(Document document, AcadQueryParams p)
    {
        var editor = document.Editor;

        if (p.Handles.Count > 0)
        {
            var wanted = new List<ObjectId>();
            var notFound = new List<string>();
            Resolve(document.Database, p.Handles, wanted, notFound);

            editor.SetImpliedSelection(wanted.ToArray());
            return new
            {
                selected = wanted.Count,
                handles = Describe(document.Database, wanted),
                notFound,
            };
        }

        var implied = editor.SelectImplied();
        if (implied.Status != PromptStatus.OK || implied.Value == null)
        {
            return new { selected = 0, handles = Array.Empty<object>(), note = "Kỹ sư chưa chọn gì." };
        }

        var ids = implied.Value.GetObjectIds();
        return new { selected = ids.Length, handles = Describe(document.Database, ids) };
    }

    /// <summary>
    /// Zoom tới đúng những entity được chỉ đích danh và chọn luôn chúng. Không có entity nào hợp lệ thì
    /// KHÔNG đụng vào khung nhìn — zoom về một hộp rỗng làm kỹ sư mất chỗ đang xem mà chẳng được gì.
    /// </summary>
    private static object ShowEntities(Document document, AcadQueryParams p)
    {
        if (p.Handles.Count == 0)
        {
            return new { error = "show_entities cần \"handles\" — danh sách handle (hex) của entity cần xem." };
        }

        var editor = document.Editor;
        var wanted = new List<ObjectId>();
        var notFound = new List<string>();
        Resolve(document.Database, p.Handles, wanted, notFound);

        if (wanted.Count == 0)
        {
            return new { shown = 0, notFound, note = "Không có entity nào hợp lệ — giữ nguyên khung nhìn." };
        }

        Extents3d? box = null;
        using (var tr = document.Database.TransactionManager.StartTransaction())
        {
            foreach (var id in wanted)
            {
                if (tr.GetObject(id, OpenMode.ForRead) is not Entity entity) continue;
                try
                {
                    var ext = entity.GeometricExtents;
                    if (box == null)
                    {
                        box = ext;
                    }
                    else
                    {
                        var merged = box.Value;
                        merged.AddExtents(ext);
                        box = merged;
                    }
                }
                catch (Autodesk.AutoCAD.Runtime.Exception)
                {
                    // Entity suy biến không có extents — bỏ qua, những cái khác vẫn khoanh được.
                }
            }
            tr.Abort();
        }

        editor.SetImpliedSelection(wanted.ToArray());

        if (box == null)
        {
            return new { shown = wanted.Count, zoomed = false, notFound, note = "Đã chọn, nhưng không entity nào có hộp bao để zoom." };
        }

        ZoomTo(editor, box.Value);
        return new
        {
            shown = wanted.Count,
            zoomed = true,
            extentsMin = new { x = box.Value.MinPoint.X, y = box.Value.MinPoint.Y, z = box.Value.MinPoint.Z },
            extentsMax = new { x = box.Value.MaxPoint.X, y = box.Value.MaxPoint.Y, z = box.Value.MaxPoint.Z },
            notFound,
        };
    }

    /// <summary>Kỹ sư đang ở tab nào — Model hay một layout in, và tỉ lệ khung nhìn hiện tại.</summary>
    private static object ActiveLayout(Document document)
    {
        var database = document.Database;
        using var tr = database.TransactionManager.StartTransaction();

        var layoutManager = LayoutManager.Current;
        var name = layoutManager.CurrentLayout;
        var isModel = database.TileMode;

        double? paperX = null;
        double? paperY = null;
        if (tr.GetObject(layoutManager.GetLayoutId(name), OpenMode.ForRead) is Layout layout)
        {
            paperX = layout.PlotPaperSize.X;
            paperY = layout.PlotPaperSize.Y;
        }

        var view = document.Editor.GetCurrentView();
        var result = new
        {
            layoutName = name,
            isModelSpace = isModel,
            paperSize = paperX.HasValue ? new { x = paperX.Value, y = paperY!.Value } : null,
            viewCentre = new { x = view.CenterPoint.X, y = view.CenterPoint.Y },
            viewHeight = view.Height,
            viewWidth = view.Width,
            currentLayer = LayerNameOf(database, tr),
        };

        tr.Abort();
        return result;
    }

    /// <summary>
    /// Ảnh để agent <b>nhìn thấy</b> bản vẽ — mảnh cuối của giai đoạn 10.1 phía AutoCAD. Không có API
    /// xuất ảnh headless như <c>Document.ExportImage</c> của Revit, nên đi ba mức, mức nào hỏng thì rơi
    /// xuống mức sau và <b>nói rõ trong kết quả</b> ảnh thuộc mức nào:
    /// <list type="number">
    ///   <item><c>live</c> — render lại model space bằng thiết bị off-screen của GraphicsSystem, đúng cỡ
    ///   <c>imageWidth</c> agent xin, ôm trọn extents. Không đụng khung nhìn kỹ sư đang xem.</item>
    ///   <item><c>screen</c> — chụp thẳng khung nhìn hiện tại (<see cref="Manager.GetCurrentAcGsView"/>):
    ///   đúng thứ kỹ sư đang thấy, nhưng cỡ ảnh là cỡ cửa sổ, không phải cỡ agent xin.</item>
    ///   <item><c>thumbnail</c> — ảnh xem trước lưu trong DWG lúc save (tầng Core, dùng chung với
    ///   accoreconsole).</item>
    /// </list>
    /// <c>source="thumbnail"</c> thì bỏ qua hai mức đầu — để kiểm được tầng Core ngay trong GUI.
    /// </summary>
    private static object Snapshot(Document document, AcadQueryParams p)
    {
        if (string.Equals(p.Source, "thumbnail", StringComparison.OrdinalIgnoreCase))
        {
            return AcadSnapshot.Thumbnail(document.Database);
        }

        var reasons = new List<string>();
        var width = Math.Max(200, Math.Min(p.ImageWidth, 4000));

        try
        {
            using var bitmap = RenderOffScreen(document, width);
            return AcadSnapshot.Package(bitmap, source: "live", note: null);
        }
        catch (System.Exception ex)
        {
            reasons.Add("off-screen: " + ex.Message);
        }

        try
        {
            using var bitmap = SnapshotCurrentView(document, width);
            return AcadSnapshot.Package(bitmap, source: "screen",
                note: "Không render off-screen được (" + reasons[0] + ") — đây là ảnh chụp khung nhìn đang mở, cỡ theo cửa sổ.");
        }
        catch (System.Exception ex)
        {
            reasons.Add("khung nhìn hiện tại: " + ex.Message);
        }

        var fallback = AcadSnapshot.Thumbnail(document.Database);
        // Thumbnail trả object ẩn danh; ghép lý do rơi xuống vào để agent biết vì sao không có ảnh sống.
        return new { fallbackFrom = "live", reasons, result = fallback };
    }

    /// <summary>
    /// Render model space vào thiết bị off-screen, khung hình 4:3 theo <paramref name="width"/>.
    /// Extents của DWG chưa từng zoom là số rác (±1e20) — khi đó lấy theo khung nhìn hiện tại.
    /// </summary>
    private static System.Drawing.Bitmap RenderOffScreen(Document document, int width)
    {
        var database = document.Database;
        var height = width * 3 / 4;
        var manager = document.GraphicsManager;

        // Từ AutoCAD 2015 mọi device/model off-screen phải gắn với một GraphicsKernel; "3D Drawing" là
        // kernel dựng hình chuẩn của AutoCAD (đúng chuỗi trong mẫu ADN). Xin rồi phải trả — kernel là
        // tài nguyên đếm tham chiếu của GS, rò một cái là mỗi lần agent "nhìn" tốn thêm một.
        var descriptor = new KernelDescriptor();
        descriptor.addRequirement(Autodesk.AutoCAD.UniqueString.Intern("3D Drawing"));
        var kernel = Manager.AcquireGraphicsKernel(descriptor);
        try
        {
            using var device = manager.CreateAutoCADOffScreenDevice(kernel);
            device.OnSize(new System.Drawing.Size(width, height));

            using var view = new View();
            using var model = manager.CreateAutoCADModel(kernel);
            using var tr = database.TransactionManager.StartTransaction();
            var modelSpace = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(database), OpenMode.ForRead);

            view.Add(modelSpace, model);
            device.Add(view);

            var (min, max) = UsableExtents(document);
            view.ZoomExtents(min, max);
            device.Update();

            var bitmap = view.GetSnapshot(new System.Drawing.Rectangle(0, 0, width, height));
            device.Erase(view);
            tr.Abort();
            return bitmap;
        }
        finally
        {
            Manager.ReleaseGraphicsKernel(kernel);
        }
    }

    /// <summary>Chụp khung nhìn số 0 (model) đúng như đang hiện trên màn hình.</summary>
    private static System.Drawing.Bitmap SnapshotCurrentView(Document document, int width)
    {
        using var view = document.GraphicsManager.GetCurrentAcGsView(0);
        var height = width * 3 / 4;
        return view.GetSnapshot(new System.Drawing.Rectangle(0, 0, width, height));
    }

    private static (Point3d min, Point3d max) UsableExtents(Document document)
    {
        var db = document.Database;
        var min = db.Extmin;
        var max = db.Extmax;
        var sane = Math.Abs(min.X) < 1e15 && Math.Abs(max.X) < 1e15 && max.X > min.X && max.Y > min.Y;
        if (sane)
        {
            return (min, max);
        }

        // Chưa có extents đáng tin: ôm theo khung nhìn kỹ sư đang mở.
        var v = document.Editor.GetCurrentView();
        var hw = v.Width / 2;
        var hh = v.Height / 2;
        return (new Point3d(v.CenterPoint.X - hw, v.CenterPoint.Y - hh, 0),
                new Point3d(v.CenterPoint.X + hw, v.CenterPoint.Y + hh, 0));
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Đổi danh sách handle thành ObjectId. Cái nào sai định dạng hoặc không có trong bản vẽ thì đưa
    /// vào <paramref name="notFound"/> — không bao giờ bỏ im lặng.
    /// </summary>
    private static void Resolve(Database database, IEnumerable<string> handles, List<ObjectId> found, List<string> notFound)
    {
        foreach (var text in handles)
        {
            if (!HandleText.TryParse(text, out var raw))
            {
                notFound.Add(text + " (không phải handle hex)");
                continue;
            }

            try
            {
                var id = database.GetObjectId(false, new Handle(raw), 0);
                if (id.IsNull)
                {
                    notFound.Add(text + " (không có trong bản vẽ)");
                    continue;
                }
                found.Add(id);
            }
            catch (Autodesk.AutoCAD.Runtime.Exception)
            {
                notFound.Add(text + " (không có trong bản vẽ)");
            }
        }
    }

    private static object[] Describe(Database database, IEnumerable<ObjectId> ids)
    {
        using var tr = database.TransactionManager.StartTransaction();
        var rows = ids
            .Select(id => tr.GetObject(id, OpenMode.ForRead) as Entity)
            .Where(e => e != null)
            .Select(e => (object)new
            {
                handle = e!.Handle.ToString(),
                type = e.GetType().Name,
                layer = e.Layer,
            })
            .ToArray();
        tr.Abort();
        return rows;
    }

    /// <summary>Đặt khung nhìn ôm trọn hộp bao, chừa 10% lề để entity không dính sát mép.</summary>
    private static void ZoomTo(Editor editor, Extents3d box)
    {
        var width = Math.Abs(box.MaxPoint.X - box.MinPoint.X);
        var height = Math.Abs(box.MaxPoint.Y - box.MinPoint.Y);

        // Một điểm/một đường thẳng đứng cho ra chiều 0 — không có khung nhìn nào rộng 0.
        if (width < 1e-6) width = 1;
        if (height < 1e-6) height = 1;

        var view = editor.GetCurrentView();
        view.CenterPoint = new Point2d(
            (box.MinPoint.X + box.MaxPoint.X) / 2,
            (box.MinPoint.Y + box.MaxPoint.Y) / 2);
        view.Width = width * 1.1;
        view.Height = height * 1.1;
        editor.SetCurrentView(view);
    }

    private static string? LayerNameOf(Database database, Transaction tr) =>
        tr.GetObject(database.Clayer, OpenMode.ForRead) is LayerTableRecord layer ? layer.Name : null;
}
