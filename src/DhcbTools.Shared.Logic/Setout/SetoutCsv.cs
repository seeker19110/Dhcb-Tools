using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace DhcbTools.Shared.Logic.Setout
{
    /// <summary>Định dạng file CSV cho máy toàn đạc: thứ tự cột, đơn vị, số lẻ, dòng tiêu đề.</summary>
    public sealed class SetoutCsvFormat
    {
        public List<SetoutColumn> Columns { get; set; } = DefaultColumns();

        /// <summary>true = mét (3 số lẻ mặc định), false = mm (0 số lẻ mặc định).</summary>
        public bool Metres { get; set; } = true;

        /// <summary>Số lẻ; null = theo đơn vị.</summary>
        public int? Decimals { get; set; }

        public bool IncludeHeader { get; set; } = true;

        public int EffectiveDecimals => Decimals ?? (Metres ? 3 : 0);

        public static List<SetoutColumn> DefaultColumns()
        {
            SetoutColumns.TryParse(SetoutColumns.Default, out var columns, out _);
            return columns;
        }

        /// <summary>Đọc tên đơn vị: <c>m</c>/<c>met</c>/<c>mét</c> hoặc <c>mm</c>; sai thì báo rõ.</summary>
        public static bool TryParseUnit(string? unit, out bool metres, out string error)
        {
            error = string.Empty;
            var text = (unit ?? string.Empty).Trim().ToLowerInvariant();
            switch (text)
            {
                case "":
                case "m":
                case "met":
                case "mét":
                case "meter":
                case "metre":
                    metres = true;
                    return true;
                case "mm":
                case "milimet":
                case "millimetre":
                case "millimeter":
                    metres = false;
                    return true;
                default:
                    metres = true;
                    error = "Đơn vị \"" + unit + "\" không hợp lệ. Hợp lệ: m hoặc mm.";
                    return false;
            }
        }
    }

    /// <summary>
    /// Ghi CSV toạ độ định vị. Số luôn dùng dấu chấm thập phân (máy toàn đạc không đọc dấu phẩy), dòng
    /// kết thúc CRLF, không có <c>-0.000</c>. Tên/mô tả đã được <see cref="SetoutPlanner"/> làm sạch nên
    /// dòng ra không bao giờ cần nháy kép — máy không hiểu RFC 4180 vẫn đọc được.
    /// </summary>
    public static class SetoutCsv
    {
        public static string Write(IReadOnlyList<SetoutPoint> points, SetoutCsvFormat format)
        {
            if (format == null)
            {
                throw new ArgumentNullException(nameof(format));
            }

            var sb = new StringBuilder();
            if (format.IncludeHeader)
            {
                var headers = new List<string>(format.Columns.Count);
                foreach (var column in format.Columns)
                {
                    headers.Add(SetoutColumns.HeaderOf(column));
                }

                sb.Append(CsvText.JoinLine(headers)).Append("\r\n");
            }

            var decimals = format.EffectiveDecimals;
            foreach (var point in points)
            {
                var cells = new List<string>(format.Columns.Count);
                foreach (var column in format.Columns)
                {
                    cells.Add(CellOf(point, column, format.Metres, decimals));
                }

                sb.Append(CsvText.JoinLine(cells)).Append("\r\n");
            }

            return sb.ToString();
        }

        public static string CellOf(SetoutPoint point, SetoutColumn column, bool metres, int decimals)
        {
            switch (column)
            {
                case SetoutColumn.Name: return point.Name;
                case SetoutColumn.North: return FormatCoordinate(point.NorthMm, metres, decimals);
                case SetoutColumn.East: return FormatCoordinate(point.EastMm, metres, decimals);
                case SetoutColumn.Elevation: return FormatCoordinate(point.ElevationMm, metres, decimals);
                case SetoutColumn.Description: return point.Description;
                case SetoutColumn.Code: return point.Code;
                case SetoutColumn.Level: return SetoutPlanner.Collapse(point.Level);
                case SetoutColumn.ElementId: return point.ElementId == 0 ? string.Empty : point.ElementId.ToString(CultureInfo.InvariantCulture);
                default: return string.Empty;
            }
        }

        /// <summary>mm → chuỗi theo đơn vị và số lẻ, dấu chấm thập phân, không có <c>-0</c>.</summary>
        public static string FormatCoordinate(double mm, bool metres, int decimals)
        {
            if (decimals < 0)
            {
                decimals = 0;
            }

            var value = Math.Round(metres ? mm / 1000.0 : mm, decimals, MidpointRounding.AwayFromZero);
            if (value == 0)
            {
                value = 0; // đổi -0 thành 0
            }

            return value.ToString("F" + decimals.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
        }
    }
}
