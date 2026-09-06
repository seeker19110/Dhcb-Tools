using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace DhcbTools.Shared.Logic.Mep
{
    /// <summary>Một connector hở: phần tử, toạ độ mm, domain, hình dạng — hàng CSV của <c>ConnectorChecker</c>.</summary>
    public sealed class OpenConnectorRow
    {
        public OpenConnectorRow(long elementId, string category, double xMm, double yMm, double zMm, string domain, string shape, string level)
        {
            ElementId = elementId;
            Category = category;
            XMm = xMm;
            YMm = yMm;
            ZMm = zMm;
            Domain = domain;
            Shape = shape;
            Level = level;
        }

        public long ElementId { get; }

        public string Category { get; }

        public double XMm { get; }

        public double YMm { get; }

        public double ZMm { get; }

        public string Domain { get; }

        public string Shape { get; }

        public string Level { get; }
    }

    /// <summary>
    /// CSV cho <c>ConnectorChecker</c> (vòng đóng vai §57: kỹ sư MEP muốn giao 1.040 connector hở cho người khác sửa —
    /// Messages trong log không giao được, CSV thì mở Excel, lọc theo tầng/domain, chia việc).
    /// </summary>
    public static class ConnectorReport
    {
        public const string Header = "ElementId,Category,Level,Domain,Shape,X_mm,Y_mm,Z_mm";

        public static string Csv(IReadOnlyList<OpenConnectorRow> rows)
        {
            var sb = new StringBuilder();
            sb.Append(Header).Append('\n');
            foreach (var r in rows)
            {
                sb.Append(r.ElementId.ToString(CultureInfo.InvariantCulture)).Append(',')
                  .Append(CsvText.Escape(r.Category)).Append(',')
                  .Append(CsvText.Escape(r.Level)).Append(',')
                  .Append(CsvText.Escape(r.Domain)).Append(',')
                  .Append(CsvText.Escape(r.Shape)).Append(',')
                  .Append(r.XMm.ToString("F1", CultureInfo.InvariantCulture)).Append(',')
                  .Append(r.YMm.ToString("F1", CultureInfo.InvariantCulture)).Append(',')
                  .Append(r.ZMm.ToString("F1", CultureInfo.InvariantCulture)).Append('\n');
            }

            return sb.ToString();
        }

        public static string MessageLine(OpenConnectorRow r)
        {
            return string.Format(CultureInfo.InvariantCulture, "Element {0} at ({1:F1},{2:F1},{3:F1}) mm - {4}", r.ElementId, r.XMm, r.YMm, r.ZMm, r.Domain);
        }

        public static string Summary(int connectors, int elements, string? csvPath)
        {
            var s = $"Tìm thấy {connectors} connector hở trên {elements} phần tử.";
            return csvPath == null ? s : s + $" CSV: \"{csvPath}\".";
        }
    }
}
