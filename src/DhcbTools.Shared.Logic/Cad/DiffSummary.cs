using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace DhcbTools.Shared.Logic.Cad
{
    /// <summary>Ảnh chụp một entity đủ để so sánh: handle, loại, layer, hộp bao/điểm đặc trưng (mm), văn bản.</summary>
    public sealed class EntitySnapshot
    {
        public EntitySnapshot(string handle, string type, string layer, double x, double y, string? text = null)
        {
            Handle = handle;
            Type = type;
            Layer = layer;
            X = x;
            Y = y;
            Text = text;
        }

        public string Handle { get; }

        public string Type { get; }

        public string Layer { get; }

        public double X { get; }

        public double Y { get; }

        public string? Text { get; }
    }

    public enum DiffKind
    {
        Added,
        Removed,
        LayerChanged,
        Moved,
        TextChanged,
    }

    public sealed class DiffEntry
    {
        public DiffEntry(DiffKind kind, string handle, string type, string detail)
        {
            Kind = kind;
            Handle = handle;
            Type = type;
            Detail = detail;
        }

        public DiffKind Kind { get; }

        public string Handle { get; }

        public string Type { get; }

        public string Detail { get; }
    }

    /// <summary>
    /// So hai bản vẽ theo handle (mục 7.9, offline thay Drawing Compare): handle là khoá ổn định của entity trong một
    /// DWG qua các lần lưu, nên "cùng handle" = cùng đối tượng. Thuần, test được.
    /// </summary>
    public static class DiffSummary
    {
        public static List<DiffEntry> Compare(IEnumerable<EntitySnapshot> before, IEnumerable<EntitySnapshot> after, double moveToleranceMm = 1.0)
        {
            var a = before.ToDictionary(e => e.Handle, StringComparer.OrdinalIgnoreCase);
            var b = after.ToDictionary(e => e.Handle, StringComparer.OrdinalIgnoreCase);
            var result = new List<DiffEntry>();

            foreach (var kv in b)
            {
                if (!a.TryGetValue(kv.Key, out var old))
                {
                    result.Add(new DiffEntry(DiffKind.Added, kv.Key, kv.Value.Type, "layer " + kv.Value.Layer));
                    continue;
                }

                var cur = kv.Value;
                if (!string.Equals(old.Layer, cur.Layer, StringComparison.OrdinalIgnoreCase))
                {
                    result.Add(new DiffEntry(DiffKind.LayerChanged, kv.Key, cur.Type, old.Layer + " → " + cur.Layer));
                }

                var d = Math.Sqrt((old.X - cur.X) * (old.X - cur.X) + (old.Y - cur.Y) * (old.Y - cur.Y));
                if (d > moveToleranceMm)
                {
                    result.Add(new DiffEntry(DiffKind.Moved, kv.Key, cur.Type, "dời " + NumericText.Format(d, 1) + " mm"));
                }

                if (!string.Equals(old.Text ?? string.Empty, cur.Text ?? string.Empty, StringComparison.Ordinal))
                {
                    result.Add(new DiffEntry(DiffKind.TextChanged, kv.Key, cur.Type, "\"" + old.Text + "\" → \"" + cur.Text + "\""));
                }
            }

            foreach (var kv in a)
            {
                if (!b.ContainsKey(kv.Key))
                {
                    result.Add(new DiffEntry(DiffKind.Removed, kv.Key, kv.Value.Type, "layer " + kv.Value.Layer));
                }
            }

            return result;
        }

        public static Dictionary<DiffKind, int> Count(IEnumerable<DiffEntry> entries)
        {
            var d = new Dictionary<DiffKind, int>();
            foreach (var e in entries)
            {
                d[e.Kind] = d.TryGetValue(e.Kind, out var n) ? n + 1 : 1;
            }
            return d;
        }

        public static string ToCsv(IEnumerable<DiffEntry> entries)
        {
            var sb = new StringBuilder("Kind,Handle,Type,Detail\n");
            foreach (var e in entries)
            {
                sb.Append(CsvText.JoinLine(new[] { e.Kind.ToString(), e.Handle, e.Type, e.Detail })).Append('\n');
            }
            return sb.ToString();
        }

        public static string ToHtml(string title, IReadOnlyList<DiffEntry> entries)
        {
            var counts = Count(entries);
            var sb = new StringBuilder();
            sb.Append("<!DOCTYPE html><html lang=\"vi\"><head><meta charset=\"utf-8\"><title>").Append(HtmlText.Escape(title)).Append("</title>")
              .Append("<style>body{font-family:Segoe UI,Arial,sans-serif;margin:24px}table{border-collapse:collapse}td,th{border:1px solid #ccc;padding:4px 8px}th{background:#f3f3f3}")
              .Append(".Added{color:#070}.Removed{color:#a00}.Moved{color:#06c}.LayerChanged{color:#a60}.TextChanged{color:#606}</style></head><body>")
              .Append("<h1>").Append(HtmlText.Escape(title)).Append("</h1><p>");
            foreach (var kv in counts)
            {
                sb.Append("<span class=\"").Append(kv.Key).Append("\">").Append(kv.Key).Append(": ").Append(kv.Value).Append("</span> &nbsp; ");
            }
            sb.Append("</p><table><thead><tr><th>Loại</th><th>Handle</th><th>Entity</th><th>Chi tiết</th></tr></thead><tbody>");
            foreach (var e in entries)
            {
                sb.Append("<tr class=\"").Append(e.Kind).Append("\"><td>").Append(e.Kind).Append("</td><td>").Append(HtmlText.Escape(e.Handle))
                  .Append("</td><td>").Append(HtmlText.Escape(e.Type)).Append("</td><td>").Append(HtmlText.Escape(e.Detail)).Append("</td></tr>");
            }
            sb.Append("</tbody></table></body></html>");
            return sb.ToString();
        }
    }
}
