using System;
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

    /// <summary>
    /// Tên file của một bản vẽ CAD đã import/link, ví dụ <c>tuyen-ong.dxf</c>.
    /// <para>
    /// Không dùng được <c>ImportInstance.Name</c>: với bản vẽ <b>link</b>, thuộc tính đó không mang tên
    /// file — tên file nằm ở **element kiểu** (<c>CADLinkType</c>) và ở **category** mà Revit sinh riêng
    /// cho từng bản vẽ. Vòng chạy thật 2026-09-05 sập đúng chỗ này: <c>ModelLinesFromCad</c> có tham số
    /// <c>dwgNameContains</c> nhưng lọc theo <c>Name</c> nên **không bao giờ khớp** một bản vẽ link, và
    /// lệnh báo E-PRECOND "không tìm thấy bản vẽ CAD" ngay khi bản vẽ nằm sờ sờ trong mô hình
    /// (<c>docs/bang-chung-test.md</c> §29). Thử theo thứ tự: kiểu → category → tên phần tử.
    /// </para>
    /// </summary>
    public static string CadFileName(Document doc, Element import)
    {
        string Safe(Func<string?> read)
        {
            try { return read() ?? string.Empty; }
            catch (Exception) { return string.Empty; }
        }

        var typeName = Safe(() => doc.GetElement(import.GetTypeId())?.Name);
        if (!string.IsNullOrWhiteSpace(typeName))
        {
            return typeName;
        }

        var categoryName = Safe(() => import.Category?.Name);
        if (!string.IsNullOrWhiteSpace(categoryName))
        {
            return categoryName;
        }

        return Safe(() => import.Name);
    }

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

    /// <summary>
    /// Tìm ElementType theo "Family: Type", đúng tên type, đúng tên family, rồi mới tới "chứa chuỗi".
    /// <para>
    /// Thứ tự là có chủ ý: trước đây khớp "chứa chuỗi" chạy TRƯỚC khớp "Family: Type", nên gõ
    /// "M_Sleeve: 100" có thể vớ phải "M_Sleeve: 1000". Khớp "chứa chuỗi" nay chỉ được nhận khi
    /// DUY NHẤT một ứng viên; nhiều ứng viên → ném <see cref="ConfigException"/> liệt kê để người
    /// dùng chọn đúng thay vì lệnh lặng lẽ dùng type đầu tiên.
    /// </para>
    /// </summary>
    public static T? FindType<T>(Document doc, string? name) where T : ElementType
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var types = new FilteredElementCollector(doc).OfClass(typeof(T)).Cast<T>().ToList();
        return PickType(types, name!.Trim(), t => t.Name, t => t.FamilyName);
    }

    /// <summary>Khớp tên chung cho <see cref="FindType{T}"/> và <see cref="FindFamilySymbol"/>.</summary>
    private static T? PickType<T>(List<T> types, string name, Func<T, string> typeName, Func<T, string> familyName) where T : Element
    {
        var cmp = StringComparison.OrdinalIgnoreCase;
        string Full(T t) => familyName(t) + ": " + typeName(t);

        // 1. "Family: Type" đầy đủ.
        var exact = types.FirstOrDefault(t => string.Equals(Full(t), name, cmp));
        if (exact != null) return exact;

        var colon = name.IndexOf(':');
        if (colon > 0)
        {
            var family = name.Substring(0, colon).Trim();
            var type = name.Substring(colon + 1).Trim();
            exact = types.FirstOrDefault(t => string.Equals(typeName(t), type, cmp) && string.Equals(familyName(t), family, cmp));
            if (exact != null) return exact;
        }

        // 2. Đúng tên type. 3. Đúng tên family (lấy type đầu — người dùng nói "family sleeve" chứ không nói type).
        exact = types.FirstOrDefault(t => string.Equals(typeName(t), name, cmp));
        if (exact != null) return exact;

        exact = types.FirstOrDefault(t => string.Equals(familyName(t), name, cmp));
        if (exact != null) return exact;

        // 4. Chứa chuỗi — chỉ khi duy nhất.
        var partial = types.Where(t => typeName(t).IndexOf(name, cmp) >= 0 || familyName(t).IndexOf(name, cmp) >= 0).ToList();
        if (partial.Count == 1) return partial[0];
        if (partial.Count == 0) return null;

        var candidates = partial.Select(Full).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).Take(20).ToList();
        throw new ConfigException(
            $"E-CONFIG-AMBIGUOUS: tên \"{name}\" khớp {partial.Count} type, không rõ chọn cái nào. Ghi đúng dạng \"Family: Type\", ví dụ: "
            + string.Join("; ", candidates) + (partial.Count > candidates.Count ? "; …" : "") + ".");
    }

    /// <summary>Tạo thư mục cha của một file đầu ra (không làm gì nếu đường dẫn không có thư mục).</summary>
    public static void EnsureParentDirectory(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
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
        return PickType(symbols, name!.Trim(), s => s.Name, s => s.FamilyName);
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

    /// <summary>
    /// Như <see cref="Lookup"/> nhưng CHỈ tra ở instance — dùng khi sắp GHI: ghi vào tham số type sẽ đổi
    /// mọi instance cùng type, không phải điều người dùng muốn khi đánh số/nhập CSV từng phần tử.
    /// </summary>
    public static Parameter? LookupInstance(Element element, string key, string? preferred = null)
    {
        if (element is null)
        {
            return null;
        }

        foreach (var name in Dictionary.NamesFor(key, preferred))
        {
            var parameter = element.LookupParameter(name);
            if (parameter != null)
            {
                return parameter;
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
