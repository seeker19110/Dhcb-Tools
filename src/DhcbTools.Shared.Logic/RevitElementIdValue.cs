using System.Globalization;

namespace DhcbTools.Shared.Logic;

/// <summary>Validate IDs across the Revit 2024 transition from 32-bit to 64-bit identifiers.</summary>
public static class RevitElementIdValue
{
    /// <summary>Reject values that would wrap to a different element on Revit 2022/2023.</summary>
    public static int ToLegacyValue(long value) => checked((int)value);

    public static bool TryParse(string? text, bool supports64Bit, out long value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text) ||
            !long.TryParse(text!.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ||
            (!supports64Bit && (parsed < int.MinValue || parsed > int.MaxValue)))
        {
            return false;
        }

        value = parsed;
        return true;
    }
}
