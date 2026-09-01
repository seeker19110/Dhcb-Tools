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

    /// <summary>Mở transaction với SilentFailuresPreprocessor — khuôn chung cho mọi lệnh Core.</summary>
    public static Transaction StartTransaction(Document doc, string name)
    {
        var tx = new Transaction(doc, name);
        tx.Start();
        tx.SetFailureHandlingOptions(tx.GetFailureHandlingOptions().SetFailuresPreprocessor(new SilentFailuresPreprocessor()));
        return tx;
    }

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
