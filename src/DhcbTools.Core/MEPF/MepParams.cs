using Autodesk.Revit.DB;

namespace DhcbTools.Core.MEPF;

/// <summary>
/// Đọc tên hệ / loại hệ của phần tử MEP qua <see cref="BuiltInParameter"/> thay vì chuỗi
/// "System Name"/"System Type" — tên hiển thị đổi theo ngôn ngữ giao diện Revit, BuiltInParameter thì không.
/// </summary>
internal static class MepParams
{
    /// <summary>Tên hệ (RBS_SYSTEM_NAME_PARAM), rỗng nếu không có.</summary>
    public static string SystemName(Element element)
    {
        var p = element.get_Parameter(BuiltInParameter.RBS_SYSTEM_NAME_PARAM);
        return p != null && p.HasValue ? p.AsString() ?? string.Empty : string.Empty;
    }

    /// <summary>Loại hệ (System Type của ống hoặc ống gió) dưới dạng chuỗi hiển thị, rỗng nếu không có.</summary>
    public static string SystemType(Element element)
    {
        var p = element.get_Parameter(BuiltInParameter.RBS_PIPING_SYSTEM_TYPE_PARAM)
                ?? element.get_Parameter(BuiltInParameter.RBS_DUCT_SYSTEM_TYPE_PARAM);
        if (p == null || !p.HasValue)
        {
            return string.Empty;
        }

        return p.StorageType == StorageType.String ? p.AsString() ?? string.Empty : p.AsValueString() ?? string.Empty;
    }

    /// <summary>Tên hệ, hoặc loại hệ nếu chưa có tên.</summary>
    public static string SystemNameOrType(Element element)
    {
        var name = SystemName(element);
        return name.Length > 0 ? name : SystemType(element);
    }
}
