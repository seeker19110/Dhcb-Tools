using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace DhcbTools.Shared.Logic.Cad
{
    /// <summary>Một dòng map layer (mục 7.8, học từ LAYTRANS): layer nguồn (tên hoặc wildcard) → layer chuẩn + thuộc tính.</summary>
    public sealed class LayerMapEntry
    {
        public LayerMapEntry(string source, string target)
        {
            Source = source;
            Target = target;
        }

        /// <summary>Tên layer nguồn; hỗ trợ wildcard AutoCAD <c>*</c>, <c>?</c> và <c>~</c> (phủ định) đơn giản.</summary>
        public string Source { get; }

        public string Target { get; }

        public string? Color { get; set; }

        public string? Linetype { get; set; }

        public string? Lineweight { get; set; }

        public bool? Plottable { get; set; }

        private Regex? _regex;

        public bool Matches(string layerName)
        {
            if (Source.IndexOfAny(new[] { '*', '?', '~' }) < 0)
            {
                return string.Equals(Source, layerName, StringComparison.OrdinalIgnoreCase);
            }

            var negate = Source.StartsWith("~", StringComparison.Ordinal);
            var pattern = negate ? Source.Substring(1) : Source;
            _regex ??= new Regex("^" + Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            var hit = _regex.IsMatch(layerName);
            return negate ? !hit : hit;
        }
    }

    /// <summary>Bảng map đọc từ CSV <c>Source,Target,Color,Linetype,Lineweight,Plottable</c> (chỉ hai cột đầu bắt buộc).</summary>
    public sealed class LayerMapTable
    {
        public List<LayerMapEntry> Entries { get; } = new List<LayerMapEntry>();

        public static LayerMapTable ParseCsv(string csv, List<string> errors)
        {
            var table = new LayerMapTable();
            // Đọc theo bản ghi RFC 4180 (không tách theo '\n') để ô có nháy chứa xuống dòng không vỡ.
            var records = new List<string[]>(CsvText.ReadRecords(new System.IO.StringReader(csv ?? string.Empty)));
            var start = 0;
            if (records.Count > 0)
            {
                var head = records[0];
                if (head.Length > 0 && head[0].Trim().Equals("Source", StringComparison.OrdinalIgnoreCase))
                {
                    start = 1;
                }
            }

            for (var i = start; i < records.Count; i++)
            {
                var c = records[i];
                if (c.Length == 0 || (c.Length == 1 && string.IsNullOrWhiteSpace(c[0])))
                {
                    continue;
                }

                if (c.Length < 2 || string.IsNullOrWhiteSpace(c[0]) || string.IsNullOrWhiteSpace(c[1]))
                {
                    errors.Add("Dòng " + (i + 1) + ": cần Source,Target — bỏ qua.");
                    continue;
                }

                var e = new LayerMapEntry(c[0].Trim(), c[1].Trim())
                {
                    Color = c.Length > 2 && c[2].Trim().Length > 0 ? c[2].Trim() : null,
                    Linetype = c.Length > 3 && c[3].Trim().Length > 0 ? c[3].Trim() : null,
                    Lineweight = c.Length > 4 && c[4].Trim().Length > 0 ? c[4].Trim() : null,
                };
                if (c.Length > 5 && c[5].Trim().Length > 0)
                {
                    e.Plottable = c[5].Trim().Equals("true", StringComparison.OrdinalIgnoreCase) || c[5].Trim() == "1";
                }

                table.Entries.Add(e);
            }

            return table;
        }

        /// <summary>Dòng đầu tiên khớp (thứ tự trong file quyết định — dòng cụ thể nên đứng trước wildcard).</summary>
        public LayerMapEntry? Resolve(string layerName)
        {
            foreach (var e in Entries)
            {
                if (e.Matches(layerName))
                {
                    return e;
                }
            }

            return null;
        }

        /// <summary>Kế hoạch: layer nguồn → đích cho toàn bộ layer hiện có; layer không có trong bảng ghi vào <paramref name="unmapped"/>.</summary>
        public Dictionary<string, LayerMapEntry> Plan(IEnumerable<string> existingLayers, List<string> unmapped)
        {
            var plan = new Dictionary<string, LayerMapEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (var layer in existingLayers)
            {
                var e = Resolve(layer);
                if (e == null)
                {
                    unmapped.Add(layer);
                    continue;
                }

                if (string.Equals(e.Target, layer, StringComparison.OrdinalIgnoreCase))
                {
                    continue; // đã đúng chuẩn
                }

                plan[layer] = e;
            }

            return plan;
        }
    }
}
