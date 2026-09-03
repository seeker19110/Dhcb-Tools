using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
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
