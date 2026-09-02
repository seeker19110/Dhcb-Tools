using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace DhcbTools.Shared.Logic.Mep
{
    /// <summary>Một phần tử MEP đã tách khỏi Revit: hệ, category, family/type, kích thước, chiều dài (mm) nếu là đoạn thẳng.</summary>
    public sealed class BomItem
    {
        public BomItem(string system, string category, string typeName, string size, double? lengthMm, string? elementId = null, string? spool = null)
        {
            System = system ?? string.Empty;
            Category = category ?? string.Empty;
            TypeName = typeName ?? string.Empty;
            Size = size ?? string.Empty;
            LengthMm = lengthMm;
            ElementId = elementId;
            Spool = spool ?? string.Empty;
        }

        public string System { get; }

        public string Category { get; }

        public string TypeName { get; }

        public string Size { get; }

        public double? LengthMm { get; }

        public string? ElementId { get; }

        /// <summary>Mã spool/khu vực (tham số do người dùng chỉ định), rỗng = không chia spool.</summary>
        public string Spool { get; }
    }

    public sealed class BomRow
    {
        public BomRow(string spool, string system, string category, string typeName, string size, int count, double totalLengthMm)
        {
            Spool = spool;
            System = system;
            Category = category;
            TypeName = typeName;
            Size = size;
            Count = count;
            TotalLengthMm = totalLengthMm;
        }

        public string Spool { get; }

        public string System { get; }

        public string Category { get; }

        public string TypeName { get; }

        public string Size { get; }

        public int Count { get; }

        public double TotalLengthMm { get; }

        /// <summary>Số cây cần đặt hàng khi ống bán theo cây <paramref name="stockLengthMm"/> (làm tròn lên, + hao hụt %).</summary>
        public int StockPieces(double stockLengthMm, double wastePercent = 5)
        {
            if (stockLengthMm <= 0 || TotalLengthMm <= 0) return 0;
            return (int)Math.Ceiling(TotalLengthMm * (1 + wastePercent / 100.0) / stockLengthMm);
        }
    }

    /// <summary>
    /// Gom BOM theo hệ/spool (P2, học từ Victaulic Procurement Tool và Naviate spool BOM): nhóm theo
    /// (spool, hệ, category, type, size) → số lượng và tổng chiều dài. Thuần, test được.
    /// </summary>
    public static class BomAggregator
    {
        public static List<BomRow> Aggregate(IEnumerable<BomItem> items)
        {
            return items
                .GroupBy(i => (i.Spool, i.System, i.Category, i.TypeName, i.Size))
                .Select(g => new BomRow(g.Key.Spool, g.Key.System, g.Key.Category, g.Key.TypeName, g.Key.Size, g.Count(), g.Sum(i => i.LengthMm ?? 0)))
                .OrderBy(r => r.Spool, StringComparer.OrdinalIgnoreCase)
                .ThenBy(r => r.System, StringComparer.OrdinalIgnoreCase)
                .ThenBy(r => r.Category, StringComparer.OrdinalIgnoreCase)
                .ThenBy(r => r.TypeName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(r => r.Size, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static string ToCsv(IEnumerable<BomRow> rows, double stockLengthMm = 6000, double wastePercent = 5)
        {
            var sb = new StringBuilder();
            sb.Append(CsvText.JoinLine(new[] { "Spool", "System", "Category", "Type", "Size", "Count", "TotalLengthM", "StockPieces" })).Append('\n');
            foreach (var r in rows)
            {
                sb.Append(CsvText.JoinLine(new[]
                {
                    r.Spool, r.System, r.Category, r.TypeName, r.Size,
                    NumericText.Format(r.Count),
                    NumericText.Format(r.TotalLengthMm / 1000.0, 2),
                    r.TotalLengthMm > 0 ? NumericText.Format(r.StockPieces(stockLengthMm, wastePercent)) : string.Empty,
                })).Append('\n');
            }
            return sb.ToString();
        }

        /// <summary>Tổng theo hệ (để ghi Messages).</summary>
        public static Dictionary<string, (int Count, double LengthMm)> TotalsBySystem(IEnumerable<BomRow> rows)
        {
            var d = new Dictionary<string, (int, double)>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in rows)
            {
                var cur = d.TryGetValue(r.System, out var v) ? v : (0, 0.0);
                d[r.System] = (cur.Item1 + r.Count, cur.Item2 + r.TotalLengthMm);
            }
            return d;
        }
    }
}
