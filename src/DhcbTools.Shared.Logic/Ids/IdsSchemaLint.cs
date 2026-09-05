using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml;
using System.Xml.Linq;

namespace DhcbTools.Shared.Logic.Ids
{
    /// <summary>
    /// Soát file IDS theo <b>cấu trúc mà XSD IDS 1.0 đòi hỏi</b>, nhưng chỉ <b>cảnh báo</b> — không chặn.
    /// <para>
    /// Vì sao cần: <see cref="IdsSpec"/> cố ý bỏ qua namespace và thứ tự thẻ để kỹ sư không bị chặn bởi
    /// một file "gần đúng". Cái giá lộ ra ở §39: fixture khai <c>&lt;restriction&gt;</c> không thuộc
    /// <c>xs:</c>, DHCB kiểm bình thường, còn IfcTester từ chối mở. Nghĩa là cùng một file, hai phần mềm
    /// ra hai kết luận — đúng điều IDS sinh ra để tránh. Bộ soát này nói cho kỹ sư biết <i>trước khi nộp</i>
    /// rằng file sẽ bị bên kia từ chối, và nói rõ ở dòng nào.
    /// </para>
    /// <para>
    /// Vì sao không kiểm bằng <c>XmlSchemaSet</c>: <c>ids.xsd</c> import chính <c>XMLSchema.xsd</c> của W3C
    /// (để dùng <c>xs:restriction</c> làm phần tử), mà .NET không biên dịch được schema đó — thử 2026-09-05,
    /// lỗi "facet is prohibited for anySimpleType" hàng loạt. Nên các quy tắc dưới đây được rút <b>bằng tay</b>
    /// từ <c>ids.xsd</c> 1.0.0 (buildingSMART): namespace, thứ tự facet, thuộc tính bắt buộc, giá trị
    /// <c>ifcVersion</c>/<c>cardinality</c> cho phép. Không đầy đủ như XSD, nhưng phủ mọi lỗi đã gặp thật.
    /// </para>
    /// </summary>
    public static class IdsSchemaLint
    {
        /// <summary>Namespace chính thức của IDS 1.0.</summary>
        public const string IdsNamespace = "http://standards.buildingsmart.org/IDS";

        /// <summary>Namespace XML Schema — <c>xs:restriction</c> và các ràng buộc bên trong phải thuộc đây.</summary>
        public const string XsNamespace = "http://www.w3.org/2001/XMLSchema";

        /// <summary>Số cảnh báo tối đa — file sai namespace từ gốc thì mọi thẻ đều sai, liệt kê hết là vô nghĩa.</summary>
        public const int MaxWarnings = 20;

        private static readonly string[] IfcVersions = { "IFC2X3", "IFC4", "IFC4X3_ADD2" };
        private static readonly string[] FacetOrder = { "entity", "partOf", "classification", "attribute", "property", "material" };
        private static readonly string[] SimpleCardinality = { "required", "prohibited" };
        private static readonly string[] ConditionalCardinality = { "required", "prohibited", "optional" };
        private static readonly string[] Restrictions =
        {
            "enumeration", "pattern", "minInclusive", "maxInclusive", "minExclusive", "maxExclusive",
            "length", "minLength", "maxLength", "totalDigits", "fractionDigits", "whiteSpace",
        };

        /// <summary>
        /// Trả về danh sách cảnh báo (rỗng = không thấy gì lệch). Không ném: file không phải XML thì trả về
        /// một dòng nói thế — việc từ chối file là của <see cref="IdsSpec.Parse"/>.
        /// </summary>
        public static IReadOnlyList<string> Check(string xml)
        {
            var warnings = new List<string>();
            XDocument document;
            try
            {
                document = XDocument.Parse(xml ?? string.Empty, LoadOptions.SetLineInfo);
            }
            catch (Exception ex)
            {
                warnings.Add("không đọc được XML: " + ex.Message);
                return warnings;
            }

            var root = document.Root!;
            var sink = new Sink(warnings);

            if (root.Name.NamespaceName != IdsNamespace)
            {
                sink.Add(root, "thẻ gốc <" + Local(root) + "> phải khai xmlns=\"" + IdsNamespace + "\""
                               + (root.Name.NamespaceName.Length == 0
                                   ? " (đang không có namespace)"
                                   : " (đang là \"" + root.Name.NamespaceName + "\")"));
            }

            if (!Local(root).Equals("ids", StringComparison.OrdinalIgnoreCase))
            {
                sink.Add(root, "thẻ gốc phải là <ids>");
                return sink.Warnings;
            }

            var info = Child(root, "info");
            if (info == null)
            {
                sink.Add(root, "thiếu <info> (bắt buộc, đứng trước <specifications>)");
            }
            else if (Child(info, "title") == null)
            {
                sink.Add(info, "<info> thiếu <title> (bắt buộc)");
            }

            var specifications = Child(root, "specifications");
            if (specifications == null)
            {
                sink.Add(root, "thiếu <specifications> bọc ngoài các <specification>");
            }

            foreach (var spec in root.Descendants().Where(e => Local(e).Equals("specification", StringComparison.OrdinalIgnoreCase)))
            {
                if (sink.Full)
                {
                    break;
                }

                if (specifications != null && spec.Parent != specifications)
                {
                    sink.Add(spec, "<specification> phải nằm trực tiếp trong <specifications>");
                }

                NamespaceIsIds(sink, spec);
                if (spec.Attribute("name") == null)
                {
                    sink.Add(spec, "<specification> thiếu thuộc tính name (bắt buộc)");
                }

                var ifcVersion = (string?)spec.Attribute("ifcVersion");
                if (ifcVersion == null)
                {
                    sink.Add(spec, "<specification> thiếu thuộc tính ifcVersion (bắt buộc; một trong " + string.Join(", ", IfcVersions) + ")");
                }
                else if (!ifcVersion.Split(' ').Where(v => v.Length > 0).All(v => IfcVersions.Contains(v)))
                {
                    sink.Add(spec, "ifcVersion=\"" + ifcVersion + "\" không thuộc {" + string.Join(", ", IfcVersions) + "}");
                }

                var applicability = Child(spec, "applicability");
                if (applicability == null)
                {
                    sink.Add(spec, "<specification> thiếu <applicability> (bắt buộc, kể cả khi rỗng)");
                }
                else
                {
                    NamespaceIsIds(sink, applicability);
                    FacetBlock(sink, applicability, isApplicability: true);
                }

                var requirements = Child(spec, "requirements");
                if (requirements != null)
                {
                    NamespaceIsIds(sink, requirements);
                    if (applicability != null && !requirements.ElementsBeforeSelf().Contains(applicability))
                    {
                        sink.Add(requirements, "<requirements> phải đứng sau <applicability>");
                    }

                    FacetBlock(sink, requirements, isApplicability: false);
                }
            }

            return sink.Warnings;
        }

        private static void FacetBlock(Sink sink, XElement block, bool isApplicability)
        {
            var lastRank = -1;
            var entityCount = 0;
            foreach (var facet in block.Elements())
            {
                if (sink.Full)
                {
                    return;
                }

                NamespaceIsIds(sink, facet);
                var name = Local(facet);
                var rank = Array.FindIndex(FacetOrder, f => f.Equals(name, StringComparison.OrdinalIgnoreCase));
                if (rank < 0)
                {
                    sink.Add(facet, "facet <" + name + "> không có trong IDS 1.0 (" + string.Join(", ", FacetOrder) + ")");
                    continue;
                }

                // XSD dùng xs:sequence: thứ tự entity → partOf → classification → attribute → property → material
                // là bắt buộc trong <applicability>. Bên <requirements> sequence lặp (maxOccurs="unbounded")
                // nên thứ tự tự do.
                if (isApplicability && rank < lastRank)
                {
                    sink.Add(facet, "<" + name + "> đứng sai thứ tự trong <applicability> — XSD đòi " + string.Join(" → ", FacetOrder));
                }

                lastRank = Math.Max(lastRank, rank);
                if (isApplicability && name.Equals("entity", StringComparison.OrdinalIgnoreCase) && ++entityCount > 1)
                {
                    sink.Add(facet, "<applicability> chỉ được có một <entity>");
                }

                var cardinality = (string?)facet.Attribute("cardinality");
                if (cardinality != null)
                {
                    if (isApplicability)
                    {
                        sink.Add(facet, "facet trong <applicability> không có thuộc tính cardinality (chỉ facet trong <requirements> mới có)");
                    }
                    else if (name.Equals("entity", StringComparison.OrdinalIgnoreCase))
                    {
                        sink.Add(facet, "<entity> trong <requirements> không có thuộc tính cardinality");
                    }
                    else
                    {
                        var allowed = name.Equals("partOf", StringComparison.OrdinalIgnoreCase) ? SimpleCardinality : ConditionalCardinality;
                        if (!allowed.Contains(cardinality))
                        {
                            sink.Add(facet, "cardinality=\"" + cardinality + "\" không thuộc {" + string.Join(", ", allowed) + "}");
                        }
                    }
                }

                switch (name.ToLowerInvariant())
                {
                    case "entity":
                        Required(sink, facet, "name");
                        Value(sink, Child(facet, "name"));
                        Value(sink, Child(facet, "predefinedType"));
                        break;
                    case "partof":
                        Required(sink, facet, "entity");
                        var entity = Child(facet, "entity");
                        if (entity != null)
                        {
                            Required(sink, entity, "name");
                            Value(sink, Child(entity, "name"));
                        }

                        break;
                    case "classification":
                        Required(sink, facet, "system");
                        Value(sink, Child(facet, "value"));
                        Value(sink, Child(facet, "system"));
                        break;
                    case "attribute":
                        Required(sink, facet, "name");
                        Value(sink, Child(facet, "name"));
                        Value(sink, Child(facet, "value"));
                        break;
                    case "property":
                        Required(sink, facet, "propertySet");
                        Required(sink, facet, "baseName");
                        Value(sink, Child(facet, "propertySet"));
                        Value(sink, Child(facet, "baseName"));
                        Value(sink, Child(facet, "value"));
                        var dataType = (string?)facet.Attribute("dataType");
                        if (dataType != null && dataType != dataType.ToUpperInvariant())
                        {
                            sink.Add(facet, "dataType=\"" + dataType + "\" phải viết HOA (ví dụ IFCLABEL)");
                        }

                        break;
                    case "material":
                        Value(sink, Child(facet, "value"));
                        break;
                }
            }
        }

        private static void Required(Sink sink, XElement facet, string child)
        {
            if (Child(facet, child) == null)
            {
                sink.Add(facet, "<" + Local(facet) + "> thiếu <" + child + "> (bắt buộc)");
            }
        }

        /// <summary>Một <c>idsValue</c>: đúng một trong <c>simpleValue</c> hoặc <c>xs:restriction</c>.</summary>
        private static void Value(Sink sink, XElement? value)
        {
            if (value == null)
            {
                return;
            }

            NamespaceIsIds(sink, value);
            var simple = Child(value, "simpleValue");
            var restriction = Child(value, "restriction");
            if (simple == null && restriction == null)
            {
                sink.Add(value, "<" + Local(value) + "> phải chứa <simpleValue> hoặc <xs:restriction>"
                                + (value.Value.Trim().Length > 0 ? " (chữ viết trần không hợp chuẩn)" : string.Empty));
                return;
            }

            if (simple != null)
            {
                NamespaceIsIds(sink, simple);
            }

            if (restriction == null)
            {
                return;
            }

            if (restriction.Name.NamespaceName != XsNamespace)
            {
                sink.Add(restriction, "<restriction> phải thuộc namespace XML Schema: viết <xs:restriction> với xmlns:xs=\"" + XsNamespace + "\"");
            }

            if (restriction.Attribute("base") == null)
            {
                sink.Add(restriction, "<xs:restriction> thiếu thuộc tính base (ví dụ base=\"xs:string\")");
            }

            foreach (var constraint in restriction.Elements())
            {
                var local = Local(constraint);
                if (!Restrictions.Any(r => r.Equals(local, StringComparison.OrdinalIgnoreCase)))
                {
                    sink.Add(constraint, "<" + local + "> không phải ràng buộc XSD");
                    continue;
                }

                if (constraint.Name.NamespaceName != XsNamespace)
                {
                    sink.Add(constraint, "<" + local + "> phải viết <xs:" + local + ">");
                }

                if (constraint.Attribute("value") == null)
                {
                    sink.Add(constraint, "<xs:" + local + "> thiếu thuộc tính value");
                }
            }
        }

        private static void NamespaceIsIds(Sink sink, XElement element)
        {
            if (element.Name.NamespaceName != IdsNamespace)
            {
                sink.Add(element, "<" + Local(element) + "> phải thuộc namespace IDS (" + IdsNamespace + ")");
            }
        }

        private static string Local(XElement element) => element.Name.LocalName;

        private static XElement? Child(XElement parent, string name) =>
            parent.Elements().FirstOrDefault(e => Local(e).Equals(name, StringComparison.OrdinalIgnoreCase));

        private sealed class Sink
        {
            private readonly HashSet<string> _seen = new HashSet<string>(StringComparer.Ordinal);

            public Sink(List<string> warnings)
            {
                Warnings = warnings;
            }

            public List<string> Warnings { get; }

            public bool Full => Warnings.Count >= MaxWarnings;

            public void Add(XElement at, string text)
            {
                if (Full)
                {
                    return;
                }

                // Cùng một lỗi lặp ở mọi thẻ (namespace sai từ gốc) chỉ cần nói một lần cho mỗi tên thẻ.
                if (!_seen.Add(Local(at) + "|" + text))
                {
                    return;
                }

                var info = (IXmlLineInfo)at;
                var line = info.HasLineInfo() ? info.LineNumber : 0;
                Warnings.Add((line > 0 ? "dòng " + line.ToString(CultureInfo.InvariantCulture) + ": " : string.Empty) + text);
            }
        }
    }
}
