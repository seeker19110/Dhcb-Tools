using Autodesk.Revit.DB;
using DhcbTools.Shared.Logic;
using DhcbTools.Shared.Logic.Mep;

namespace DhcbTools.Core.MEPF;

/// <summary>Cao độ thuộc từng instance; ghi tham số type sẽ đổi mọi tuyến dùng chung type.</summary>
public static class ElevationParameterWriter
{
    public static bool TrySet(Element element, string key, string? preferredName, double valueMm)
        => TrySet(element, key, preferredName, valueMm, out _);

    public static bool TrySet(Element element, string key, string? preferredName, double valueMm, out bool unchanged)
    {
        unchanged = false;
        var parameter = RevitCompat.LookupInstance(element, key, preferredName);
        if (parameter == null || parameter.IsReadOnly) return false;
        if (parameter.StorageType == StorageType.Double)
        {
            var value = MepLayout.MmToFeet(valueMm);
            unchanged = Math.Abs(parameter.AsDouble() - value) < 1e-9;
            return !unchanged && parameter.Set(value);
        }
        if (parameter.StorageType == StorageType.String)
        {
            var value = NumericText.Format(valueMm, 1);
            unchanged = string.Equals(parameter.AsString(), value, StringComparison.Ordinal);
            return !unchanged && parameter.Set(value);
        }
        return false;
    }
}
