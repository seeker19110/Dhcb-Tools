using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DhcbTools.Shared.Logic.Ifc
{
    /// <summary>Một thuộc tính trực tiếp (explicit) của một lớp IFC, đúng vị trí trong danh sách tham số STEP.</summary>
    public sealed class IfcAttributeInfo
    {
        internal IfcAttributeInfo(string name, int index, bool derived)
        {
            Name = name;
            Index = index;
            Derived = derived;
        }

        /// <summary>Tên thuộc tính theo lược đồ (<c>RefractionIndex</c>).</summary>
        public string Name { get; }

        /// <summary>Vị trí trong danh sách tham số STEP (từ 0).</summary>
        public int Index { get; }

        /// <summary>Lớp này khai lại thuộc tính thành dẫn xuất — STEP ghi <c>*</c>, không có giá trị để kiểm.</summary>
        public bool Derived { get; }
    }

    /// <summary>
    /// Bảng thuộc tính của mọi lớp trong IFC2X3, IFC4, IFC4X3_ADD2 — sinh bằng <c>scripts/gen-ifc-schema.py</c> từ
    /// IfcOpenShell, nhúng trong DLL. Trước đây đường IFC chỉ biết Name/Description/Tag… và bảng tay của vài lớp, nên
    /// facet <c>attribute</c> hỏi <c>IfcTask.IsMilestone</c> hay <c>IfcStairFlight.NumberOfRisers</c> luôn trượt.
    /// </summary>
    public static class IfcSchemaTable
    {
        private static readonly Lazy<Dictionary<string, Dictionary<string, IReadOnlyList<IfcAttributeInfo>>>> Tables =
            new Lazy<Dictionary<string, Dictionary<string, IReadOnlyList<IfcAttributeInfo>>>>(Load);

        /// <summary>
        /// Lược đồ trong bảng ứng với tên lược đồ khai trong file: <c>IFC4X3…</c> → <c>IFC4X3_ADD2</c>,
        /// <c>IFC2X3…</c> → <c>IFC2X3</c>, còn lại → <c>IFC4</c>.
        /// </summary>
        public static string Normalize(string? schema)
        {
            var text = (schema ?? string.Empty).Trim().ToUpperInvariant();
            if (text.StartsWith("IFC4X3", StringComparison.Ordinal))
            {
                return "IFC4X3_ADD2";
            }

            return text.StartsWith("IFC2X3", StringComparison.Ordinal) ? "IFC2X3" : "IFC4";
        }

        /// <summary>Mọi thuộc tính trực tiếp của lớp (kể cả thừa kế, theo đúng thứ tự STEP); <c>null</c> khi lược đồ không có lớp đó.</summary>
        public static IReadOnlyList<IfcAttributeInfo>? Attributes(string? schema, string entity)
        {
            return Tables.Value[Normalize(schema)].TryGetValue((entity ?? string.Empty).ToUpperInvariant(), out var list) ? list : null;
        }

        /// <summary>Thuộc tính theo tên (phân biệt hoa thường như IDS); <c>null</c> khi lớp không có thuộc tính trực tiếp đó.</summary>
        public static IfcAttributeInfo? Attribute(string? schema, string entity, string name)
        {
            return Attributes(schema, entity)?.FirstOrDefault(a => string.Equals(a.Name, name, StringComparison.Ordinal));
        }

        private static Dictionary<string, Dictionary<string, IReadOnlyList<IfcAttributeInfo>>> Load()
        {
            using (var stream = typeof(IfcSchemaTable).Assembly.GetManifestResourceStream("DhcbTools.Shared.Logic.ifc-schema.txt")!)
            using (var reader = new StreamReader(stream))
            {
                return Parse(reader.ReadToEnd());
            }
        }

        internal static Dictionary<string, Dictionary<string, IReadOnlyList<IfcAttributeInfo>>> Parse(string text)
        {
            var result = new Dictionary<string, Dictionary<string, IReadOnlyList<IfcAttributeInfo>>>(StringComparer.Ordinal);
            var raw = new Dictionary<string, (string? Parent, string[] Own, string[] Derived)>(StringComparer.Ordinal);
            string? schema = null;

            void Flush()
            {
                if (schema == null)
                {
                    return;
                }

                var table = new Dictionary<string, IReadOnlyList<IfcAttributeInfo>>(StringComparer.Ordinal);
                foreach (var name in raw.Keys)
                {
                    table[name] = Flatten(raw, name);
                }

                result[schema] = table;
                raw.Clear();
            }

            foreach (var line in text.Split('\n').Select(l => l.TrimEnd('\r')))
            {
                if (line.Length == 0 || line[0] == '#')
                {
                    continue;
                }

                if (line[0] == '@')
                {
                    Flush();
                    schema = line.Substring(1);
                    continue;
                }

                var parts = line.Split('|');
                var head = parts[0].Split('<');
                raw[head[0]] = (head.Length > 1 ? head[1] : null,
                    parts[1].Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries),
                    parts.Length > 2 ? parts[2].Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(d => d.TrimStart('~')).ToArray() : Array.Empty<string>());
            }

            Flush();
            return result;
        }

        private static IReadOnlyList<IfcAttributeInfo> Flatten(Dictionary<string, (string? Parent, string[] Own, string[] Derived)> raw, string name)
        {
            var chain = new List<string>();
            for (string? current = name; current != null && raw.ContainsKey(current) && chain.Count < 64; current = raw[current].Parent)
            {
                chain.Insert(0, current);
            }

            var derived = new HashSet<string>(chain.SelectMany(c => raw[c].Derived), StringComparer.Ordinal);
            var list = new List<IfcAttributeInfo>();
            foreach (var entry in chain.SelectMany(c => raw[c].Own))
            {
                // "Tên:K" — mã kiểu K để người đọc bảng; giá trị có kiểu đọc theo chính cách ghi STEP (IfcIdsElement.Typed).
                var attribute = entry.Substring(0, entry.IndexOf(':'));
                list.Add(new IfcAttributeInfo(attribute, list.Count, derived.Contains(attribute)));
            }

            return list;
        }
    }
}
