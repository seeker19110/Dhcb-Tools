using System.Globalization;
using Autodesk.Revit.DB;
using DhcbTools.Shared.Logic;

namespace DhcbTools.Core;

/// <summary>
/// Khác biệt API giữa các phiên bản Revit gom về một chỗ: <c>ElementId.IntegerValue</c> (≤2023) và
/// <c>ElementId.Value</c> (2024+, kiểu long). Hằng <c>REVIT2024_OR_GREATER</c> đặt trong Directory.Build.props.
/// </summary>
public static class RevitCompat
{
    /// <summary>Giá trị số của ElementId, không phụ thuộc phiên bản.</summary>
    public static long IdValue(ElementId id)
    {
#if REVIT2024_OR_GREATER
        return id.Value;
#else
        return id.IntegerValue;
#endif
    }

    /// <summary>Dựng ElementId từ số (đọc từ CSV/JSON).</summary>
    public static ElementId MakeId(long value)
    {
#if REVIT2024_OR_GREATER
        return new ElementId(value);
#else
        return new ElementId((int)value);
#endif
    }

    /// <summary>Đọc ElementId từ ô văn bản; false nếu không phải số.</summary>
    public static bool TryParseId(string? text, out ElementId id)
    {
        id = ElementId.InvalidElementId;
        if (string.IsNullOrWhiteSpace(text) || !long.TryParse(text!.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v))
        {
            return false;
        }

        id = MakeId(v);
        return true;
    }

    public static double MmToFt(double mm) => MepLayout.MmToFeet(mm);

    public static double FtToMm(double ft) => MepLayout.FeetToMillimetres(ft);

    /// <summary>Đổi foot² (Room.Area) sang m² — điểm duy nhất, tránh hai hệ số làm tròn khác nhau.</summary>
    public static double SqFtToSqm(double squareFeet) => MepLayout.SquareFeetToSquareMetres(squareFeet);

    /// <summary>Tìm Level theo tên (không phân biệt hoa thường).</summary>
    public static Level? FindLevel(Document doc, string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        return new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>()
            .FirstOrDefault(l => string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Tìm view template theo tên.</summary>
    public static View? FindViewTemplate(Document doc, string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        return new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>()
            .FirstOrDefault(v => v.IsTemplate && string.Equals(v.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Tìm ElementType theo "Family: Type" hoặc chỉ "Type" trong một class.</summary>
    public static T? FindType<T>(Document doc, string? name) where T : ElementType
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var types = new FilteredElementCollector(doc).OfClass(typeof(T)).Cast<T>().ToList();
        var exact = types.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));
        if (exact != null)
        {
            return exact;
        }

        var colon = name!.IndexOf(':');
        if (colon > 0)
        {
            var family = name.Substring(0, colon).Trim();
            var type = name.Substring(colon + 1).Trim();
            return types.FirstOrDefault(t =>
                string.Equals(t.Name, type, StringComparison.OrdinalIgnoreCase)
                && string.Equals(t.FamilyName, family, StringComparison.OrdinalIgnoreCase));
        }

        return types.FirstOrDefault(t => t.Name.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0);
    }

    /// <summary>
    /// Tên category tuyến MEP → <see cref="BuiltInCategory"/>. Nhận cả số ít lẫn số nhiều, không phân
    /// biệt hoa thường: người dùng và agent gõ tên category của Revit ("Pipes", "Ducts"), trong khi
    /// vài lệnh trước đây chỉ nhận số ít và phân biệt hoa thường.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, BuiltInCategory> MepCurveCategories =
        new Dictionary<string, BuiltInCategory>(StringComparer.OrdinalIgnoreCase)
        {
            { "Duct", BuiltInCategory.OST_DuctCurves },
            { "Ducts", BuiltInCategory.OST_DuctCurves },
            { "Pipe", BuiltInCategory.OST_PipeCurves },
            { "Pipes", BuiltInCategory.OST_PipeCurves },
            { "CableTray", BuiltInCategory.OST_CableTray },
            { "CableTrays", BuiltInCategory.OST_CableTray },
            { "Conduit", BuiltInCategory.OST_Conduit },
            { "Conduits", BuiltInCategory.OST_Conduit },
        };

    /// <summary>
    /// Đổi danh sách tên thành category, và trả về tên KHÔNG nhận ra qua <paramref name="unknown"/>.
    /// <para>
    /// Trả tên sai ra ngoài là điểm mấu chốt: <c>HangerCommand</c> trước đây gặp tên lạ thì âm thầm
    /// rơi về "toàn bộ category mặc định" (chạy sai phạm vi mà vẫn báo thành công), còn
    /// <c>PipeSplitterCommand</c> âm thầm bỏ qua rồi kết luận "không có phần tử nào" trên model có
    /// 1.794 ống. Cả hai lộ ra ở vòng kiểm thử cấp thoát nước 2026-09-03.
    /// </para>
    /// </summary>
    public static List<BuiltInCategory> ResolveMepCategories(IEnumerable<string>? names, out List<string> unknown)
    {
        unknown = new List<string>();
        var result = new List<BuiltInCategory>();
        if (names == null)
        {
            return result;
        }

        foreach (var name in names)
        {
            if (MepCurveCategories.TryGetValue(name ?? string.Empty, out var category))
            {
                if (!result.Contains(category))
                {
                    result.Add(category);
                }
            }
            else
            {
                unknown.Add(name ?? string.Empty);
            }
        }

        return result;
    }

    /// <summary>Thông báo chuẩn khi tên category không hợp lệ.</summary>
    public static string UnknownMepCategories(IEnumerable<string> unknown) =>
        "Không nhận ra category: " + string.Join(", ", unknown)
        + ". Hợp lệ: " + string.Join(", ", MepCurveCategories.Keys) + ".";

    /// <summary>
    /// Tra <see cref="FamilySymbol"/> theo tên type, tên family, hoặc dạng đầy đủ "Family: Type".
    /// <para>
    /// Một điểm tra duy nhất là có chủ ý: trước đây <c>SleeveCommand</c> và <c>HangerCommand</c> mỗi
    /// lớp có một bản sao gần giống nhau và **không khớp nhau** — bản của Sleeve chỉ nhận tên type
    /// hoặc "Family: Type", nên truyền tên family (đúng như tên trường <c>sleeveFamilyName</c> và ví dụ
    /// trong tài liệu, <c>M_Generic Model</c>) thì không bao giờ tra ra. Vòng kiểm thử MEP đầu tiên
    /// trong Revit (2026-09-03) cho thấy cùng một tên: Hanger tra được, Sleeve thì không.
    /// </para>
    /// </summary>
    public static FamilySymbol? FindFamilySymbol(Document doc, string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var symbols = new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>().ToList();

        return symbols.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase))
               ?? symbols.FirstOrDefault(s => string.Equals(s.FamilyName + ": " + s.Name, name, StringComparison.OrdinalIgnoreCase))
               // Tên family: lấy type đầu tiên của family đó — người dùng nói "family sleeve", không
               // phải "type sleeve", nên đây là cách hiểu đúng thay vì trả về null.
               ?? symbols.FirstOrDefault(s => string.Equals(s.FamilyName, name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Luật "chứa chuỗi" cho ParameterFilter. Revit 2023 bỏ tham số <c>caseSensitive</c> và
    /// đánh dấu overload cũ là obsolete; cả hai nhánh đều không phân biệt hoa thường,
    /// nên hành vi giữ nguyên qua mọi phiên bản.
    /// </summary>
    public static FilterRule CreateContainsRule(ElementId parameterId, string value)
    {
#if REVIT2023_OR_GREATER
        return ParameterFilterRuleFactory.CreateContainsRule(parameterId, value);
#else
        return ParameterFilterRuleFactory.CreateContainsRule(parameterId, value, false);
#endif
    }

    /// <summary>Mở transaction theo chính sách cảnh báo của vỏ — khuôn chung cho mọi lệnh Core.</summary>
    public static Transaction StartTransaction(Document doc, string name)
    {
        var tx = new Transaction(doc, name);
        tx.Start();
        ApplyFailurePolicy(tx);
        return tx;
    }

    /// <summary>
    /// Gắn <see cref="SilentFailuresPreprocessor"/> theo <see cref="CoreContext.FailurePolicy"/>. Ribbon (Interactive)
    /// không gắn gì để Revit hiện hộp thoại cho kỹ sư; Bridge/batch mới tự xử lý. Gọi được trước hoặc sau <c>Start()</c>.
    /// </summary>
    public static void ApplyFailurePolicy(Transaction tx)
    {
        var policy = CoreContext.FailurePolicy;
        if (policy == FailurePolicy.Interactive)
        {
            return;
        }

        tx.SetFailureHandlingOptions(tx.GetFailureHandlingOptions().SetFailuresPreprocessor(new SilentFailuresPreprocessor(policy)));
    }

    private static ParameterDictionary? _dictionary;

    /// <summary>
    /// Từ điển tên tham số/family của dự án (giai đoạn 9.2), nạp một lần từ
    /// <c>%APPDATA%\DHCB\dictionary.json</c>. Đặt lại được để test hoặc để nạp lại sau khi sửa file.
    /// </summary>
    public static ParameterDictionary Dictionary
    {
        get => _dictionary ??= ParameterDictionary.Load();
        set => _dictionary = value;
    }

    /// <summary>
    /// Tra tham số theo <b>khoá logic</b> ("level", "diameter", "width"…) thay vì tên tiếng Anh cứng.
    /// Thử lần lượt: tên người dùng đặt trong config → tên đồng nghĩa trong từ điển; tìm ở instance
    /// trước rồi tới type.
    /// <para>
    /// Đây là điểm tra tham số DUY NHẤT của Core. Trước đây mỗi lệnh gọi thẳng
    /// <c>LookupParameter("Level")</c>, nên trên Revit giao diện tiếng Việt hoặc thư viện family riêng,
    /// lệnh không tìm thấy gì và <b>im lặng không làm gì mà vẫn báo thành công</b>.
    /// </para>
    /// </summary>
    /// <returns>Parameter tìm được, hoặc null — người gọi PHẢI báo lỗi bằng <see cref="LookupFailed"/>.</returns>
    public static Parameter? Lookup(Element element, string key, string? preferred = null)
    {
        if (element is null)
        {
            return null;
        }

        var names = Dictionary.NamesFor(key, preferred);

        foreach (var name in names)
        {
            var parameter = element.LookupParameter(name);
            if (parameter != null)
            {
                return parameter;
            }
        }

        // Tham số có thể nằm ở type (ví dụ kích thước danh nghĩa của family sleeve).
        Element? type = null;
        try
        {
            type = element.Document?.GetElement(element.GetTypeId());
        }
        catch (Exception)
        {
        }

        if (type != null)
        {
            foreach (var name in names)
            {
                var parameter = type.LookupParameter(name);
                if (parameter != null)
                {
                    return parameter;
                }
            }
        }

        return null;
    }

    /// <summary>Thông báo lỗi chuẩn khi <see cref="Lookup"/> trả null — nêu rõ đã thử tên nào và sửa ở đâu.</summary>
    public static string LookupFailed(string key, string? preferred = null) =>
        Dictionary.NotFoundMessage(key, preferred);

    /// <summary>Tên family mặc định theo khoá do từ điển khai báo; null nếu công ty chưa khai.</summary>
    public static string? FamilyFor(string key) =>
        Dictionary.Families.TryGetValue(key, out var name) ? name : null;

    /// <summary>Ghi tham số chuỗi nếu tồn tại và ghi được; trả lý do khi không ghi được.</summary>
    public static string? TrySetString(Element element, string parameterName, string value)
    {
        var p = element.LookupParameter(parameterName);
        if (p == null)
        {
            return $"không có tham số \"{parameterName}\"";
        }

        if (p.IsReadOnly)
        {
            return $"tham số \"{parameterName}\" chỉ đọc";
        }

        if (p.StorageType != StorageType.String)
        {
            return $"tham số \"{parameterName}\" không phải kiểu Text";
        }

        p.Set(value);
        return null;
    }

    /// <summary>Đọc giá trị tham số dưới dạng chuỗi (instance rồi type), rỗng nếu không có.</summary>
    public static string ReadString(Element element, string parameterName)
    {
        var p = element.LookupParameter(parameterName);
        if (p == null)
        {
            var type = element.Document.GetElement(element.GetTypeId());
            p = type?.LookupParameter(parameterName);
        }

        if (p == null || !p.HasValue)
        {
            return string.Empty;
        }

        return p.StorageType switch
        {
            StorageType.String => p.AsString() ?? string.Empty,
            StorageType.Integer => NumericText.Format(p.AsInteger()),
            StorageType.Double => NumericText.Format(p.AsDouble()),
            _ => p.AsValueString() ?? string.Empty,
        };
    }
}
