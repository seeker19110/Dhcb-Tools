using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace DhcbTools.Shared.Logic.Ids
{
    /// <summary>
    /// Giá trị mà một facet IDS chấp nhận: một chuỗi cố định (<c>simpleValue</c>), một danh sách
    /// (<c>xs:enumeration</c>), một biểu thức (<c>xs:pattern</c>), hoặc một khoảng số (<c>xs:minInclusive</c>…).
    /// Không ràng buộc gì = "có giá trị là được".
    /// </summary>
    public sealed class IdsValue
    {
        private Regex? _pattern;

        /// <summary>Chuỗi phải khớp đúng (không phân biệt hoa thường).</summary>
        public string? Simple { get; set; }

        /// <summary>Danh sách giá trị cho phép.</summary>
        public List<string> Enumeration { get; } = new List<string>();

        /// <summary>Biểu thức chính quy theo XSD — neo hai đầu khi so.</summary>
        public string? Pattern { get; set; }

        /// <summary>Chặn dưới, lấy cả biên.</summary>
        public double? MinInclusive { get; set; }

        /// <summary>Chặn trên, lấy cả biên.</summary>
        public double? MaxInclusive { get; set; }

        /// <summary>Chặn dưới, không lấy biên.</summary>
        public double? MinExclusive { get; set; }

        /// <summary>Chặn trên, không lấy biên.</summary>
        public double? MaxExclusive { get; set; }

        /// <summary>Không ràng buộc gì: chỉ cần thuộc tính/property tồn tại và khác rỗng.</summary>
        public bool IsAny =>
            string.IsNullOrEmpty(Simple) && Enumeration.Count == 0 && string.IsNullOrEmpty(Pattern)
            && MinInclusive == null && MaxInclusive == null && MinExclusive == null && MaxExclusive == null;

        /// <summary>Giá trị đọc được từ mô hình có thoả không.</summary>
        public bool Accepts(string? text)
        {
            var value = (text ?? string.Empty).Trim();
            if (value.Length == 0)
            {
                return false;
            }

            if (IsAny)
            {
                return true;
            }

            if (!string.IsNullOrEmpty(Simple))
            {
                return string.Equals(value, Simple, StringComparison.OrdinalIgnoreCase);
            }

            if (Enumeration.Count > 0)
            {
                return Enumeration.Any(e => string.Equals(value, e, StringComparison.OrdinalIgnoreCase));
            }

            if (!string.IsNullOrEmpty(Pattern))
            {
                // XSD pattern khớp TOÀN BỘ chuỗi, Regex .NET thì khớp một đoạn. Không neo hai đầu thì
                // "AB-01-rác" cũng đạt quy tắc "AB-\d\d" — quy tắc đặt tên mất hiệu lực mà vẫn xanh.
                _pattern ??= new Regex("^(?:" + Pattern + ")$", RegexOptions.CultureInvariant);
                return _pattern.IsMatch(value);
            }

            if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
            {
                return false;
            }

            return (MinInclusive == null || number >= MinInclusive)
                   && (MaxInclusive == null || number <= MaxInclusive)
                   && (MinExclusive == null || number > MinExclusive)
                   && (MaxExclusive == null || number < MaxExclusive);
        }

        /// <summary>Câu mô tả ràng buộc, để báo cáo nói được "cần gì" chứ không chỉ "không đạt".</summary>
        public string Describe()
        {
            if (!string.IsNullOrEmpty(Simple))
            {
                return "= \"" + Simple + "\"";
            }

            if (Enumeration.Count > 0)
            {
                return "thuộc {" + string.Join(", ", Enumeration) + "}";
            }

            if (!string.IsNullOrEmpty(Pattern))
            {
                return "khớp mẫu \"" + Pattern + "\"";
            }

            var bounds = new List<string>();
            if (MinInclusive != null) { bounds.Add("≥ " + Text(MinInclusive.Value)); }
            if (MinExclusive != null) { bounds.Add("> " + Text(MinExclusive.Value)); }
            if (MaxInclusive != null) { bounds.Add("≤ " + Text(MaxInclusive.Value)); }
            if (MaxExclusive != null) { bounds.Add("< " + Text(MaxExclusive.Value)); }
            return bounds.Count > 0 ? string.Join(" và ", bounds) : "có giá trị (khác rỗng)";
        }

        private static string Text(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
    }

    /// <summary>Loại facet IDS.</summary>
    public enum IdsFacetKind
    {
        /// <summary>Lớp IFC (IfcWall, IfcDuctSegment…), kèm predefinedType nếu khai.</summary>
        Entity,

        /// <summary>Thuộc tính của thực thể IFC (Name, Description, Tag…).</summary>
        Attribute,

        /// <summary>Property trong một property set.</summary>
        Property,

        /// <summary>Mã phân loại (Uniclass, mã hiệu công tác…).</summary>
        Classification,

        /// <summary>Vật liệu.</summary>
        Material,

        /// <summary>Thuộc về một thực thể khác (nhóm, hệ, tầng).</summary>
        PartOf,
    }

    /// <summary>Một facet: một điều kiện áp lên phần tử.</summary>
    public sealed class IdsFacet
    {
        /// <summary>Loại facet.</summary>
        public IdsFacetKind Kind { get; set; }

        /// <summary>Entity: tên lớp IFC. Attribute: tên thuộc tính. Property: tên property.</summary>
        public IdsValue Name { get; set; } = new IdsValue();

        /// <summary>Property: tên property set. Entity: predefinedType. Classification: hệ phân loại.</summary>
        public IdsValue? Container { get; set; }

        /// <summary>Giá trị phải thoả (không ràng buộc = chỉ cần tồn tại).</summary>
        public IdsValue Value { get; set; } = new IdsValue();

        /// <summary>
        /// <c>required</c> (mặc định) | <c>optional</c> | <c>prohibited</c>. Bên phần yêu cầu,
        /// <c>prohibited</c> nghĩa là <b>có mới là sai</b> — đọc sót nó là đọc ngược quy tắc.
        /// </summary>
        public string Cardinality { get; set; } = "required";

        /// <summary>Facet cấm: phần tử thoả facet này là vi phạm.</summary>
        public bool IsProhibited => string.Equals(Cardinality, "prohibited", StringComparison.OrdinalIgnoreCase);

        /// <summary>Facet tuỳ chọn: thiếu cũng không sao.</summary>
        public bool IsOptional => string.Equals(Cardinality, "optional", StringComparison.OrdinalIgnoreCase);

        /// <summary>Câu mô tả facet cho báo cáo.</summary>
        public string Describe()
        {
            switch (Kind)
            {
                case IdsFacetKind.Entity:
                    return "lớp IFC " + Name.Describe()
                           + (Container != null ? ", predefinedType " + Container.Describe() : string.Empty);
                case IdsFacetKind.Attribute:
                    return "thuộc tính " + Name.Describe() + " " + Value.Describe();
                case IdsFacetKind.Property:
                    return "property " + (Container != null ? Container.Describe() + "." : string.Empty)
                           + Name.Describe() + " " + Value.Describe();
                case IdsFacetKind.Classification:
                    return "phân loại " + (Container != null ? Container.Describe() + ": " : string.Empty) + Value.Describe();
                case IdsFacetKind.Material:
                    return "vật liệu " + Value.Describe();
                default:
                    return "thuộc về " + Value.Describe();
            }
        }
    }

    /// <summary>Một specification: "phần tử nào" (applicability) phải "thoả gì" (requirements).</summary>
    public sealed class IdsSpecification
    {
        /// <summary>Tên do người khai đặt — hiện nguyên văn trong báo cáo.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Mô tả kèm theo.</summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>Lọc phần tử áp dụng. Rỗng = mọi phần tử.</summary>
        public List<IdsFacet> Applicability { get; } = new List<IdsFacet>();

        /// <summary>Điều kiện phần tử áp dụng phải thoả.</summary>
        public List<IdsFacet> Requirements { get; } = new List<IdsFacet>();
    }

    /// <summary>
    /// Bộ đọc file <b>IDS 1.0</b> (buildingSMART Information Delivery Specification — chuẩn chính thức từ
    /// 01/6/2024).
    /// <para>
    /// Vì sao đọc IDS thay vì một định dạng JSON tự nghĩ: chủ đầu tư hoặc tư vấn thẩm tra khai yêu cầu
    /// <b>một lần</b>, rồi DHCB, IfcTester hay Solibri kiểm đều phải ra <b>cùng kết luận</b> — đó chính là
    /// điều IDS được lập ra để bảo đảm. Một định dạng riêng thì mỗi phần mềm hiểu một kiểu, và tranh cãi
    /// giữa các bên quay về đúng chỗ cũ.
    /// </para>
    /// <para>
    /// Đọc bằng XLinq và <b>bỏ qua namespace</b> khi so tên thẻ: file IDS ngoài đời khai namespace nhiều
    /// kiểu (có/không prefix, bản nháp cũ), mà từ chối vì namespace thì kỹ sư chỉ thấy "file hỏng".
    /// </para>
    /// </summary>
    public static class IdsSpec
    {
        /// <summary>Đọc nội dung XML. Ném <see cref="IdsParseException"/> khi file không dùng được.</summary>
        public static IReadOnlyList<IdsSpecification> Parse(string xml)
        {
            XDocument document;
            try
            {
                document = XDocument.Parse(xml ?? string.Empty);
            }
            catch (Exception ex)
            {
                throw new IdsParseException("File IDS không phải XML đọc được: " + ex.Message);
            }

            // XDocument.Parse đã ném khi file không có thẻ gốc, nên tới đây Root luôn khác null —
            // không dựng nhánh "root rỗng" mà không ai chạy tới được.
            var root = document.Root!;
            if (!Local(root).Equals("ids", StringComparison.OrdinalIgnoreCase))
            {
                throw new IdsParseException("File IDS phải có thẻ gốc <ids>, đang thấy <" + Local(root) + ">.");
            }

            var specifications = Descendants(root, "specification").ToList();
            if (specifications.Count == 0)
            {
                throw new IdsParseException("File IDS không có <specification> nào — không có gì để kiểm.");
            }

            var result = new List<IdsSpecification>();
            foreach (var element in specifications)
            {
                var spec = new IdsSpecification
                {
                    Name = (string?)element.Attribute("name") ?? string.Empty,
                    Description = (string?)element.Attribute("description") ?? string.Empty,
                };

                foreach (var facet in ReadFacets(Child(element, "applicability")))
                {
                    spec.Applicability.Add(facet);
                }

                foreach (var facet in ReadFacets(Child(element, "requirements")))
                {
                    spec.Requirements.Add(facet);
                }

                // Specification không có yêu cầu nào thì luôn đạt. Nhận nó là in ra một dòng "✓" cho một
                // điều kiện chưa ai viết — đúng loại no-op im lặng mà E-PRECOND sinh ra để chặn.
                if (spec.Requirements.Count == 0)
                {
                    throw new IdsParseException(
                        "Specification \"" + spec.Name + "\" không có <requirements> nào — nó sẽ luôn đạt, tức là không kiểm gì cả.");
                }

                result.Add(spec);
            }

            return result;
        }

        private static IEnumerable<IdsFacet> ReadFacets(XElement? parent)
        {
            if (parent == null)
            {
                yield break;
            }

            foreach (var element in parent.Elements())
            {
                switch (Local(element).ToLowerInvariant())
                {
                    case "entity":
                        yield return Facet(element, IdsFacetKind.Entity, "name", "predefinedType", null);
                        break;
                    case "attribute":
                        yield return Facet(element, IdsFacetKind.Attribute, "name", null, "value");
                        break;
                    case "property":
                        yield return Facet(element, IdsFacetKind.Property, "baseName", "propertySet", "value");
                        break;
                    case "classification":
                        yield return Facet(element, IdsFacetKind.Classification, "value", "system", "value");
                        break;
                    case "material":
                        yield return Facet(element, IdsFacetKind.Material, "value", null, "value");
                        break;
                    case "partof":
                        yield return Facet(element, IdsFacetKind.PartOf, "entity", null, "entity");
                        break;
                    default:
                        // Facet lạ (bản IDS mới hơn) — bỏ qua im lặng thì bộ kiểm báo "đạt" cho một điều
                        // kiện nó không hề kiểm, và người đọc báo cáo không có cách nào biết.
                        throw new IdsParseException("Facet \"" + Local(element) + "\" chưa hỗ trợ.");
                }
            }
        }

        private static IdsFacet Facet(XElement element, IdsFacetKind kind, string nameChild, string? containerChild, string? valueChild)
        {
            var facet = new IdsFacet
            {
                Kind = kind,
                Name = ReadValue(Child(element, nameChild)) ?? new IdsValue(),
                Cardinality = (string?)element.Attribute("cardinality") ?? "required",
            };

            if (containerChild != null)
            {
                facet.Container = ReadValue(Child(element, containerChild));
            }

            if (valueChild != null)
            {
                facet.Value = ReadValue(Child(element, valueChild)) ?? new IdsValue();
            }

            return facet;
        }

        private static IdsValue? ReadValue(XElement? element)
        {
            if (element == null)
            {
                return null;
            }

            var value = new IdsValue();
            var simple = Child(element, "simpleValue");
            if (simple != null)
            {
                value.Simple = simple.Value.Trim();
                return value;
            }

            var restriction = Child(element, "restriction");
            if (restriction == null)
            {
                var text = element.Value.Trim();
                if (text.Length > 0)
                {
                    value.Simple = text;
                }

                return value;
            }

            foreach (var facet in restriction.Elements())
            {
                var text = ((string?)facet.Attribute("value") ?? facet.Value).Trim();
                switch (Local(facet).ToLowerInvariant())
                {
                    case "enumeration":
                        value.Enumeration.Add(text);
                        break;
                    case "pattern":
                        value.Pattern = text;
                        break;
                    case "mininclusive":
                        value.MinInclusive = Number(text);
                        break;
                    case "maxinclusive":
                        value.MaxInclusive = Number(text);
                        break;
                    case "minexclusive":
                        value.MinExclusive = Number(text);
                        break;
                    case "maxexclusive":
                        value.MaxExclusive = Number(text);
                        break;
                    default:
                        // Cùng lý do với facet lạ: nhận file rồi lờ một ràng buộc đi là nói dối về kết quả.
                        throw new IdsParseException("Ràng buộc \"" + Local(facet) + "\" chưa hỗ trợ.");
                }
            }

            return value;
        }

        private static double? Number(string text) =>
            double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : (double?)null;

        private static string Local(XElement element) => element.Name.LocalName;

        private static XElement? Child(XElement parent, string name) =>
            parent.Elements().FirstOrDefault(e => Local(e).Equals(name, StringComparison.OrdinalIgnoreCase));

        private static IEnumerable<XElement> Descendants(XElement root, string name) =>
            root.Descendants().Where(e => Local(e).Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>File IDS không dùng được — thông báo nói rõ chỗ hỏng.</summary>
    public sealed class IdsParseException : Exception
    {
        /// <summary>Khởi tạo với câu nói rõ chỗ hỏng.</summary>
        public IdsParseException(string message) : base(message)
        {
        }
    }
}
