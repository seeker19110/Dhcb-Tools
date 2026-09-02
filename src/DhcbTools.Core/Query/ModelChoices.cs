using Autodesk.Revit.DB;
using DhcbTools.Shared.Logic.Ai;

namespace DhcbTools.Core.Query;

/// <summary>
/// Danh sách gợi ý lấy từ mô hình đang mở, để form động (giai đoạn 9.1) cho kỹ sư <b>chọn</b> thay vì
/// gõ tay tên category/tham số/level/view/family — gõ sai một chữ là lệnh chạy xong mà không làm gì.
/// <para>Ở Core chứ không ở vỏ: chỉ đọc <c>Document</c>, không dính WPF, nên Bridge cũng dùng lại được.</para>
/// </summary>
public static class ModelChoices
{
    /// <summary>Gợi ý cho một trường config theo kiểu của nó. Rỗng = trường tự do, form hiện ô nhập thường.</summary>
    public static IReadOnlyList<string> For(Document document, FieldKind kind) => kind switch
    {
        FieldKind.Category => Categories(document),
        FieldKind.Parameter => Parameters(document),
        FieldKind.Level => Levels(document),
        FieldKind.View => ViewTemplates(document),
        FieldKind.FamilyType => FamilyTypes(document),
        _ => Array.Empty<string>(),
    };

    /// <summary>Category có phần tử thật trong mô hình — không liệt kê cả trăm category rỗng.</summary>
    public static IReadOnlyList<string> Categories(Document document)
    {
        var names = new SortedSet<string>(StringComparer.CurrentCultureIgnoreCase);
        try
        {
            foreach (var element in new FilteredElementCollector(document).WhereElementIsNotElementType())
            {
                var name = element.Category?.Name;
                if (!string.IsNullOrWhiteSpace(name))
                {
                    names.Add(name!);
                }
            }
        }
        catch (Exception)
        {
            // Mô hình lỗi thì thà trả danh sách rỗng (form về ô nhập tay) còn hơn chặn cả cửa sổ.
        }

        return names.ToList();
    }

    /// <summary>Tên tham số instance và type gặp trong mô hình.</summary>
    public static IReadOnlyList<string> Parameters(Document document)
    {
        var names = new SortedSet<string>(StringComparer.CurrentCultureIgnoreCase);
        try
        {
            // Lấy mẫu chứ không quét cả mô hình: đủ để phủ tham số hay dùng mà không treo cửa sổ.
            var sample = new FilteredElementCollector(document)
                .WhereElementIsNotElementType()
                .Take(400);

            foreach (var element in sample)
            {
                foreach (Parameter p in element.Parameters)
                {
                    var name = p.Definition?.Name;
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        names.Add(name!);
                    }
                }
            }
        }
        catch (Exception)
        {
        }

        return names.ToList();
    }

    public static IReadOnlyList<string> Levels(Document document) =>
        Names(document, () => new FilteredElementCollector(document)
            .OfClass(typeof(Level)).Cast<Element>());

    public static IReadOnlyList<string> ViewTemplates(Document document) =>
        Names(document, () => new FilteredElementCollector(document)
            .OfClass(typeof(View)).Cast<View>().Where(v => v.IsTemplate).Cast<Element>());

    /// <summary>Tên type dạng "Family: Type" — đúng định dạng <c>RevitCompat.FindType</c> nhận.</summary>
    public static IReadOnlyList<string> FamilyTypes(Document document)
    {
        var names = new SortedSet<string>(StringComparer.CurrentCultureIgnoreCase);
        try
        {
            foreach (var type in new FilteredElementCollector(document).OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>())
            {
                names.Add(type.FamilyName + ": " + type.Name);
            }
        }
        catch (Exception)
        {
        }

        return names.ToList();
    }

    private static IReadOnlyList<string> Names(Document document, Func<IEnumerable<Element>> query)
    {
        var names = new SortedSet<string>(StringComparer.CurrentCultureIgnoreCase);
        try
        {
            foreach (var element in query())
            {
                if (!string.IsNullOrWhiteSpace(element.Name))
                {
                    names.Add(element.Name);
                }
            }
        }
        catch (Exception)
        {
        }

        return names.ToList();
    }
}
