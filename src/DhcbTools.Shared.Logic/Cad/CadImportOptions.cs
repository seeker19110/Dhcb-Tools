using System;

namespace DhcbTools.Shared.Logic.Cad
{
    /// <summary>
    /// Đọc hai tuỳ chọn chữ của lệnh <c>CadLink</c> (đơn vị, cách đặt) thành TÊN giá trị enum của Revit
    /// (<c>ImportUnit</c>, <c>ImportPlacement</c>). Core chỉ còn <c>Enum.Parse</c> theo tên — mọi cách viết được
    /// chấp nhận (tiếng Việt có dấu/không dấu, viết tắt) và thông báo lỗi nằm ở đây, có test.
    /// </summary>
    public static class CadImportOptions
    {
        /// <summary>Tên đơn vị hợp lệ theo <c>Autodesk.Revit.DB.ImportUnit</c>.</summary>
        public static bool TryParseUnit(string? text, out string unitName, out string error)
        {
            error = string.Empty;
            switch ((text ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "":
                case "auto":
                case "default":
                    unitName = "Default";
                    return true;
                case "mm":
                case "milimet":
                case "millimeter":
                    unitName = "Millimeter";
                    return true;
                case "cm":
                case "centimet":
                    unitName = "Centimeter";
                    return true;
                case "m":
                case "met":
                case "mét":
                    unitName = "Meter";
                    return true;
                case "inch":
                case "in":
                    unitName = "Inch";
                    return true;
                case "ft":
                case "foot":
                case "feet":
                    unitName = "Foot";
                    return true;
                default:
                    unitName = "Default";
                    error = $"Đơn vị \"{text}\" không hợp lệ. Hợp lệ: auto (Revit tự đọc từ file), mm, cm, m, inch, ft.";
                    return false;
            }
        }

        /// <summary>Tên cách đặt hợp lệ theo <c>Autodesk.Revit.DB.ImportPlacement</c>.</summary>
        public static bool TryParsePlacement(string? text, out string placementName, out string error)
        {
            error = string.Empty;
            switch ((text ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "":
                case "origin":
                case "goc":
                case "gốc":
                    placementName = "Origin";
                    return true;
                case "shared":
                case "chung":
                    placementName = "Shared";
                    return true;
                case "centered":
                case "center":
                case "giua":
                case "giữa":
                    placementName = "Centered";
                    return true;
                default:
                    placementName = "Origin";
                    error = $"Cách đặt \"{text}\" không hợp lệ. Hợp lệ: origin (gốc dự án), shared (toạ độ chung), centered (giữa view).";
                    return false;
            }
        }
    }
}
