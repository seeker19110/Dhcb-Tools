using System;
using System.Collections.Generic;
using System.IO;

namespace DhcbTools.Shared.Logic.Ifc
{
    /// <summary>
    /// Bảng lược đồ IFC (IFC2X3, IFC4, IFC4X3_ADD2): mỗi lớp có lớp cha nào và thuộc tính forward nào, đúng thứ
    /// tự tham số STEP. Sinh bằng <c>tools/ifc-schema/sinh-luoc-do.py</c> (ifcopenshell), nhúng vào assembly.
    /// <para>
    /// Vì sao cần: IDS hỏi thuộc tính theo <b>tên</b> (<c>RefractionIndex</c>, <c>NumberOfRisers</c>…) còn file STEP
    /// chỉ có <b>vị trí</b>. Bảng tay cũ biết chừng chục lớp — 20 ca attribute của buildingSMART trượt vì hỏi đúng
    /// những lớp nằm ngoài bảng đó.
    /// </para>
    /// </summary>
    internal sealed class IfcSchema
    {
        private const string ResourceName = "DhcbTools.Shared.Logic.Ids.ifc-schemas.txt";

        private static readonly Lazy<Dictionary<string, IfcSchema>> All = new Lazy<Dictionary<string, IfcSchema>>(Load);

        private readonly Dictionary<string, string?> _parent = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string[]> _own = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<string>> _attributes = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        private IfcSchema(string name)
        {
            Name = name;
        }

        /// <summary><c>IFC2X3</c>, <c>IFC4</c> hoặc <c>IFC4X3_ADD2</c>.</summary>
        public string Name { get; }

        /// <summary>Lược đồ khớp <c>FILE_SCHEMA</c> của file: <c>IFC2X3*</c>, <c>IFC4X3*</c>, còn lại (kể cả không khai) dùng IFC4.</summary>
        public static IfcSchema For(string? fileSchema)
        {
            var name = (fileSchema ?? string.Empty).Trim().ToUpperInvariant();
            return All.Value[name.StartsWith("IFC2X3", StringComparison.Ordinal) ? "IFC2X3"
                : name.StartsWith("IFC4X3", StringComparison.Ordinal) ? "IFC4X3_ADD2"
                : "IFC4"];
        }

        /// <summary>Lớp có trong lược đồ này không.</summary>
        public bool Knows(string entity) => _parent.ContainsKey(entity);

        /// <summary>Mọi thuộc tính forward của lớp (kể cả kế thừa), đúng thứ tự tham số STEP; lớp lạ → rỗng.</summary>
        public IReadOnlyList<string> AttributesOf(string entity) =>
            _attributes.TryGetValue(entity, out var list) ? list : (IReadOnlyList<string>)Array.Empty<string>();

        /// <summary>Dựng sẵn danh sách đầy đủ của mọi lớp lúc nạp — sau đó bảng chỉ đọc, dùng chung giữa các luồng được.</summary>
        private List<string> Build(string entity)
        {
            if (_attributes.TryGetValue(entity, out var built))
            {
                return built;
            }

            var list = new List<string>();
            var parent = _parent[entity];
            if (parent != null)
            {
                list.AddRange(Build(parent));
            }

            list.AddRange(_own[entity]);
            _attributes[entity] = list;
            return list;
        }

        /// <summary>Vị trí tham số của thuộc tính forward <paramref name="attribute"/> (phân biệt hoa thường như IFC), hoặc -1.</summary>
        public int IndexOf(string entity, string attribute)
        {
            var attributes = AttributesOf(entity);
            for (var i = 0; i < attributes.Count; i++)
            {
                if (string.Equals(attributes[i], attribute, StringComparison.Ordinal))
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary><paramref name="entity"/> là <paramref name="ancestor"/> hoặc lớp con của nó.</summary>
        public bool IsA(string entity, string ancestor)
        {
            string? current = entity;
            while (current != null)
            {
                if (string.Equals(current, ancestor, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                _parent.TryGetValue(current, out current);
            }

            return false;
        }

        private static Dictionary<string, IfcSchema> Load()
        {
            using (var stream = typeof(IfcSchema).Assembly.GetManifestResourceStream(ResourceName)!)
            using (var reader = new StreamReader(stream))
            {
                return Parse(reader.ReadToEnd());
            }
        }

        internal static Dictionary<string, IfcSchema> Parse(string text)
        {
            var schemas = new Dictionary<string, IfcSchema>(StringComparer.OrdinalIgnoreCase);
            IfcSchema? current = null;
            foreach (var raw in text.Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line[0] == '#')
                {
                    continue;
                }

                if (line[0] == '@')
                {
                    current = new IfcSchema(line.Substring(1));
                    schemas[current.Name] = current;
                    continue;
                }

                var parts = line.Split(' ');
                var entity = parts[0];
                current!._parent[entity] = parts[1] == "-" ? null : parts[1];
                var own = new string[parts.Length - 2];
                Array.Copy(parts, 2, own, 0, own.Length);
                current._own[entity] = own;
            }

            foreach (var schema in schemas.Values)
            {
                foreach (var entity in schema._parent.Keys)
                {
                    schema.Build(entity);
                }
            }

            return schemas;
        }
    }
}
