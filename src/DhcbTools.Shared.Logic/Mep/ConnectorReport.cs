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
        public const string Header = "Key,ElementId,Category,Level,Domain,Shape,X_mm,Y_mm,Z_mm";

        /// <summary>
        /// Khoá ổn định của một connector hở: id phần tử + toạ độ làm tròn lưới 100 mm (cùng cách
        /// <c>ClashAcceptance.MakeKey</c>) — để ghi vào file "đã chấp nhận" (cùng định dạng clash-accepted.json) và
        /// lần chạy sau bỏ qua, như <c>ClashDetection</c> (§62). Phần tử dịch dưới 50 mm vẫn cùng khoá.
        /// </summary>
        public static string MakeKey(long elementId, double xMm, double yMm, double zMm)
        {
            return elementId.ToString(CultureInfo.InvariantCulture) + "@" + Snap(xMm) + "," + Snap(yMm) + "," + Snap(zMm);
        }

        private static string Snap(double v) => System.Math.Round(v / 100.0).ToString(CultureInfo.InvariantCulture);

        public static string Key(OpenConnectorRow r) => MakeKey(r.ElementId, r.XMm, r.YMm, r.ZMm);

        public static string Csv(IReadOnlyList<OpenConnectorRow> rows)
        {
            var sb = new StringBuilder();
            sb.Append(Header).Append('\n');
            foreach (var r in rows)
            {
                sb.Append(Key(r)).Append(',')
                  .Append(r.ElementId.ToString(CultureInfo.InvariantCulture)).Append(',')
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

        /// <summary>Phần tử không đọc được connector: nói ra để con số hở được hiểu là cận dưới. Rỗng khi 0.</summary>
        public static string SkippedNote(int skipped)
            => skipped <= 0 ? string.Empty : $" {skipped} phần tử không đọc được connector (bỏ qua) — con số hở là cận dưới.";

        public static string Summary(int connectors, int elements, string? csvPath, int accepted = 0)
        {
            var s = accepted > 0
                ? $"Tìm thấy {connectors} connector hở trên {elements} phần tử ({accepted} đã chấp nhận, bỏ qua)."
                : $"Tìm thấy {connectors} connector hở trên {elements} phần tử.";
            return csvPath == null ? s : s + $" CSV: \"{csvPath}\".";
        }
    }
}
