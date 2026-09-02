using System;

namespace DhcbTools.Shared.Logic
{
    /// <summary>
    /// Ánh xạ chuỗi phiên bản trong config sang tên hằng của enum Revit API
    /// (<c>ACADVersion</c>, <c>IFCVersion</c>). Trả về chuỗi thay vì enum để thư viện này
    /// không phải tham chiếu RevitAPI; vỏ Revit chỉ việc Enum.Parse tên trả về.
    /// </summary>
    public static class ExportVersionMap
    {
        /// <summary>Phiên bản DWG mặc định khi config để trống hoặc không nhận ra.</summary>
        public const string DefaultAcadVersion = "R2018";

        /// <summary>Phiên bản IFC mặc định khi config để trống hoặc không nhận ra.</summary>
        public const string DefaultIfcVersion = "IFC2x3";

        /// <summary>
        /// "AcadRelease2018", "2018", "R2018" → "R2018". Không nhận ra thì trả false kèm giá trị mặc định,
        /// để lệnh gọi ghi được cảnh báo thay vì im lặng đổi phiên bản (bản cũ im lặng).
        /// </summary>
        public static bool TryParseAcadVersion(string? version, out string enumName)
        {
            enumName = DefaultAcadVersion;
            if (StringGuard.IsBlank(version))
            {
                return false;
            }

            string[] known = { "2000", "2004", "2007", "2010", "2013", "2018" };
            foreach (var year in known)
            {
                if (version.IndexOf(year, StringComparison.Ordinal) >= 0)
                {
                    enumName = "R" + year;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// "IFC2x3", "2x3", "IFC4", "IFC4 Design Transfer View" → tên hằng IFCVersion tương ứng.
        /// Kiểm tra IFC4 TRƯỚC IFC2x3 nhưng phải loại "2x3" ra, vì chuỗi "IFC2x3" cũng chứa ký tự '4'
        /// trong một số biến thể ("IFC2x3 Coordination View 2.0" thì không, nhưng "IFC4" thì có) —
        /// bản cũ chỉ tìm ký tự '4' nên "IFC2x3 CV 2.0 + 4D" bị đọc nhầm thành IFC4.
        /// </summary>
        public static bool TryParseIfcVersion(string? version, out string enumName)
        {
            enumName = DefaultIfcVersion;
            if (StringGuard.IsBlank(version))
            {
                return false;
            }

            var normalized = version.Replace(" ", string.Empty).ToUpperInvariant();

            if (normalized.StartsWith("IFC2X3", StringComparison.Ordinal) || normalized.StartsWith("2X3", StringComparison.Ordinal))
            {
                enumName = "IFC2x3";
                return true;
            }

            if (normalized.StartsWith("IFC4", StringComparison.Ordinal) || normalized.StartsWith("4", StringComparison.Ordinal))
            {
                enumName = normalized.IndexOf("DESIGNTRANSFER", StringComparison.Ordinal) >= 0
                    ? "IFC4DTV"
                    : normalized.IndexOf("REFERENCE", StringComparison.Ordinal) >= 0
                        ? "IFC4RV"
                        : "IFC4";
                return true;
            }

            return false;
        }
    }
}
