using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using DhcbTools.Core;
using DhcbTools.Core.Query;

namespace DhcbTools.Revit.Bridge;

/// <summary>
/// Giai đoạn 10.1 — truy vấn cần <see cref="UIDocument"/>: đang chọn gì, đang nhìn view nào, và
/// <b>chỉ cho kỹ sư xem</b> phần tử nào.
/// <para>
/// Nằm ở vỏ chứ không ở Core vì Core cố ý không tham chiếu RevitAPIUI (nguyên tắc "Core không biết UI").
/// Query nào không thuộc nhóm này thì chuyển thẳng xuống <see cref="RevitQueryHandler"/>.
/// </para>
/// <para>
/// Đây là mảnh làm agent hết "mù": trước đây agent đọc được số đếm nhưng không biết kỹ sư đang chọn
/// gì, đang mở view nào, và không có cách nào khoanh vùng phần tử để người ngồi máy nhìn thấy.
/// </para>
/// </summary>
internal static class UiQueryHandler
{
    public static object Handle(UIDocument uiDocument, QueryRequest request)
    {
        var document = uiDocument.Document;

        return request.Query.ToUpperInvariant() switch
        {
            "SELECTION" => Selection(uiDocument, request.Params),
            "SHOW_ELEMENTS" => ShowElements(uiDocument, request.Params),
            "ACTIVE_VIEW" => ActiveView(uiDocument),
            _ => RevitQueryHandler.Handle(document, request),
        };
    }

    /// <summary>
    /// Đọc lựa chọn hiện tại; truyền <c>elementIds</c> thì ĐẶT lựa chọn — cách agent nói
    /// "những phần tử này đây" cho kỹ sư ngồi máy.
    /// </summary>
    private static object Selection(UIDocument uiDocument, QueryParams p)
    {
        var document = uiDocument.Document;

        if (p.ElementIds.Count > 0)
        {
            var wanted = new List<ElementId>();
            var missing = new List<long>();

            foreach (var raw in p.ElementIds)
            {
                var id = RevitCompat.MakeId(raw);
                if (document.GetElement(id) != null)
                {
                    wanted.Add(id);
                }
                else
                {
                    missing.Add(raw);
                }
            }

            try
            {
                uiDocument.Selection.SetElementIds(wanted);
            }
            catch (Exception ex)
            {
                return new { error = "Không đặt được lựa chọn: " + ex.Message };
            }

            return new
            {
                selected = wanted.Count,
                missing,
                note = missing.Count > 0 ? "Một số ElementId không có trong mô hình." : null,
            };
        }

        var current = uiDocument.Selection.GetElementIds();
        var items = current
            .Select(id => document.GetElement(id))
            .Where(e => e != null)
            .Select(e => new
            {
                id = RevitCompat.IdValue(e!.Id),
                category = e.Category?.Name,
                name = SafeName(e),
            })
            .ToList();

        return new { count = items.Count, elements = items };
    }

    /// <summary>Zoom tới phần tử và chọn luôn — kỹ sư nhìn thấy ngay cái agent đang nói tới.</summary>
    private static object ShowElements(UIDocument uiDocument, QueryParams p)
    {
        if (p.ElementIds.Count == 0)
        {
            return new { error = "Cần truyền elementIds." };
        }

        var document = uiDocument.Document;
        var ids = p.ElementIds
            .Select(RevitCompat.MakeId)
            .Where(id => document.GetElement(id) != null)
            .ToList();

        if (ids.Count == 0)
        {
            return new { error = "Không ElementId nào có trong mô hình." };
        }

        try
        {
            uiDocument.Selection.SetElementIds(ids);
            uiDocument.ShowElements(ids);
        }
        catch (Exception ex)
        {
            return new { error = "Không zoom tới phần tử được: " + ex.Message };
        }

        return new { shown = ids.Count, viewName = SafeName(uiDocument.ActiveView) };
    }

    /// <summary>Kỹ sư đang nhìn cái gì — để agent nói chuyện đúng ngữ cảnh thay vì đoán.</summary>
    private static object ActiveView(UIDocument uiDocument)
    {
        var view = uiDocument.ActiveView;
        if (view == null)
        {
            return new { error = "Không có view nào đang mở." };
        }

        return new
        {
            id = RevitCompat.IdValue(view.Id),
            name = SafeName(view),
            viewType = view.ViewType.ToString(),
            scale = SafeGet(() => view.Scale),
            detailLevel = SafeGet(() => view.DetailLevel.ToString()),
            discipline = SafeGet(() => view.Discipline.ToString()),
            isTemplate = view.IsTemplate,
            canBePrinted = view.CanBePrinted,
            levelName = SafeGet(() => (uiDocument.Document.GetElement(view.GenLevel?.Id ?? ElementId.InvalidElementId) as Level)?.Name),
            selectionCount = SafeGet(() => uiDocument.Selection.GetElementIds().Count),
        };
    }

    private static string? SafeName(Element? element)
    {
        try
        {
            return element?.Name;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static T? SafeGet<T>(Func<T?> get)
    {
        try
        {
            return get();
        }
        catch (Exception)
        {
            return default;
        }
    }
}
