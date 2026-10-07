using System;
using System.Collections.Generic;
using System.Linq;

namespace DhcbTools.Shared.Logic.Ids
{
    /// <summary>
    /// Một phần tử nhìn dưới con mắt IDS. Vỏ Revit dựng lớp cài đặt riêng; test dựng lớp giả — nhờ vậy
    /// toàn bộ luật kiểm chạy được trên CI Linux, không cần Revit.
    /// </summary>
    public interface IIdsElement
    {
        /// <summary>Nhãn hiện trong báo cáo (ElementId + tên) — chỉ để người đọc tìm lại phần tử.</summary>
        string Label { get; }

        /// <summary>Lớp IFC của phần tử, ví dụ <c>IfcWall</c>.</summary>
        string IfcEntity { get; }

        /// <summary>PredefinedType (rỗng khi không có).</summary>
        string PredefinedType { get; }

        /// <summary>Thuộc tính IFC: <c>Name</c>, <c>Description</c>, <c>Tag</c>…</summary>
        string? Attribute(string name);

        /// <summary>
        /// Property theo property set. <paramref name="propertySet"/> rỗng = tìm trong mọi bộ.
        /// Trả về <c>null</c> khi không có property đó.
        /// </summary>
        string? Property(string? propertySet, string name);

        /// <summary>Mã phân loại theo hệ; hệ rỗng = mọi hệ.</summary>
        IEnumerable<string> Classifications(string? system);

        /// <summary>Tên vật liệu.</summary>
        IEnumerable<string> Materials { get; }

        /// <summary>
        /// Tổ tiên "thuộc về" (tên nhóm/hệ/tầng…), mỗi mục gắn quan hệ IFC đã dùng để tới đó.
        /// <c>Relation</c> <c>null</c> nghĩa là tới được bằng cấu trúc quan hệ IFC hợp lệ bất kỳ,
        /// đi qua trộn lẫn nhiều loại quan hệ (đúng khi facet <c>partOf</c> không khai <c>relation</c>).
        /// Một quan hệ cụ thể chỉ xuất hiện khi tới được bằng <b>đúng một loại</b> quan hệ đó xuyên suốt —
        /// theo <c>partof-facet.md</c> của buildingSMART: "if specified only the given type must be
        /// evaluated (recursively)".
        /// </summary>
        IEnumerable<(string? Relation, string Entity)> PartOf { get; }
    }

    /// <summary>
    /// Chi tiết mà chỉ đường file IFC có. Evaluator dùng khi phần tử cài interface này; đường Revit không cài —
    /// khi đó facet partOf có khai predefinedType không kiểm được và TRƯỢT (không đạt oan).
    /// </summary>
    public interface IIdsElementDetails
    {
        /// <summary>PredefinedType gốc là <c>USERDEFINED</c> — IDS chấp nhận cả chữ "USERDEFINED" lẫn giá trị tự khai.</summary>
        bool PredefinedTypeIsUserDefined { get; }

        /// <summary>Như <see cref="IIdsElement.PartOf"/>, kèm PredefinedType của từng tổ tiên.</summary>
        IEnumerable<(string? Relation, string Entity, string PredefinedType)> PartOfWithPredefinedType { get; }
    }

    /// <summary>Một phần tử không đạt, kèm câu nói rõ thiếu gì.</summary>
    public sealed class IdsFailure
    {
        internal IdsFailure(string specification, string element, string reason)
        {
            Specification = specification;
            Element = element;
            Reason = reason;
        }

        /// <summary>Tên specification bị vi phạm.</summary>
        public string Specification { get; }

        /// <summary>Nhãn phần tử.</summary>
        public string Element { get; }

        /// <summary>Thiếu gì, và cần gì.</summary>
        public string Reason { get; }
    }

    /// <summary>Kết quả của một specification.</summary>
    public sealed class IdsSpecificationResult
    {
        internal IdsSpecificationResult(string name, string description, int applicable, int passed, IReadOnlyList<IdsFailure> failures, string? skipReason = null, bool required = false)
        {
            Name = name;
            Description = description;
            Applicable = applicable;
            Passed = passed;
            Failures = failures;
            SkipReason = skipReason;
            Required = required;
        }

        /// <summary>Specification bắt buộc (<c>minOccurs ≥ 1</c>, mặc định của IDS): phải có ít nhất một phần tử lọt applicability.</summary>
        public bool Required { get; }

        /// <summary>
        /// Bắt buộc mà không phần tử nào lọt applicability — IDS 1.0 tính là <b>không đạt</b> ("required
        /// specifications need at least one applicable entity"). Trước đây chỉ in cảnh báo và mã thoát vẫn 0.
        /// Không muốn thế thì khai <c>minOccurs="0"</c> trên <c>&lt;applicability&gt;</c>.
        /// </summary>
        public bool MissingRequired => Required && NoApplicableElements;

        /// <summary>Specification không đạt: có phần tử trượt, hoặc bắt buộc mà không có phần tử nào.</summary>
        public bool IsFailed => Failed > 0 || MissingRequired;

        /// <summary>Khác null khi specification KHÔNG được chạy (ví dụ <c>ifcVersion</c> không khớp lược đồ file) — báo cáo phải nói rõ, không hiện thành "0 phần tử".</summary>
        public string? SkipReason { get; }

        /// <summary>Specification bị bỏ qua, không phải "không có phần tử".</summary>
        public bool Skipped => SkipReason != null;

        /// <summary>Tên specification.</summary>
        public string Name { get; }

        /// <summary>Mô tả trong file IDS.</summary>
        public string Description { get; }

        /// <summary>Số phần tử lọt qua applicability — tức số phần tử specification này nói tới.</summary>
        public int Applicable { get; }

        /// <summary>Số phần tử đạt.</summary>
        public int Passed { get; }

        /// <summary>
        /// Số phần tử không đạt — luôn là <c>Applicable − Passed</c>, kể cả khi <see cref="Failures"/> đã bị cắt ở
        /// <see cref="IdsEvaluator.MaxFailuresPerSpecification"/>. Trước đây báo cáo đếm theo danh sách đã cắt:
        /// 785 tường sai FireRating hiện thành "200 không đạt" (lộ khi đối chiếu IfcTester, §41).
        /// </summary>
        public int Failed => Applicable - Passed;

        /// <summary>Phần tử không đạt (tối đa <see cref="IdsEvaluator.MaxFailuresPerSpecification"/> phần tử).</summary>
        public IReadOnlyList<IdsFailure> Failures { get; }

        /// <summary>Danh sách <see cref="Failures"/> ngắn hơn số không đạt thật.</summary>
        public bool FailuresTruncated => Failures.Count < Failed;

        /// <summary>
        /// Không phần tử nào lọt applicability. Đây <b>không phải</b> "đạt": nó nói rằng mô hình không có
        /// loại phần tử mà yêu cầu nhắm tới — có thể do lọc sai, có thể do mô hình thiếu hẳn nhóm đó.
        /// </summary>
        public bool NoApplicableElements => Applicable == 0 && !Skipped;
    }

    /// <summary>Kết quả kiểm cả file IDS.</summary>
    public sealed class IdsCheckResult
    {
        internal IdsCheckResult(IReadOnlyList<IdsSpecificationResult> specifications, int elementCount)
        {
            Specifications = specifications;
            ElementCount = elementCount;
        }

        /// <summary>Kết quả từng specification, đúng thứ tự trong file.</summary>
        public IReadOnlyList<IdsSpecificationResult> Specifications { get; }

        /// <summary>Số phần tử đã soi.</summary>
        public int ElementCount { get; }

        /// <summary>Tổng số phần tử không đạt (đếm thật, không phải theo danh sách đã cắt).</summary>
        public int FailureCount => Specifications.Sum(s => s.Failed);

        /// <summary>Số specification không có phần tử nào để kiểm.</summary>
        public int EmptySpecificationCount => Specifications.Count(s => s.NoApplicableElements);

        /// <summary>Số specification không đạt (có phần tử trượt, hoặc bắt buộc mà rỗng).</summary>
        public int FailedSpecificationCount => Specifications.Count(s => s.IsFailed);

        /// <summary>Cả file IDS đạt — thứ mã thoát và gói bàn giao dựa vào, không chỉ <see cref="FailureCount"/>.</summary>
        public bool AllPassed => FailedSpecificationCount == 0;
    }

    /// <summary>
    /// Đánh giá phần tử theo bộ specification IDS. Thuần tuyệt đối: không Revit, không file, không giờ hệ
    /// thống — nên mọi luật ở đây có test trên CI.
    /// </summary>
    public static class IdsEvaluator
    {
        /// <summary>Số phần tử không đạt liệt kê chi tiết cho mỗi specification.</summary>
        public const int MaxFailuresPerSpecification = 200;

        /// <summary>Kiểm danh sách phần tử theo bộ specification.</summary>
        public static IdsCheckResult Check(IEnumerable<IdsSpecification> specifications, IEnumerable<IIdsElement> elements) =>
            Check(specifications, elements, null);

        /// <summary>
        /// Kiểm danh sách phần tử theo bộ specification. <paramref name="modelSchema"/> (<c>IFC4</c>, <c>IFC2X3</c>…)
        /// khác rỗng thì specification khai <c>ifcVersion</c> không chứa lược đồ đó được BỎ QUA và ghi lý do —
        /// IDS 1.0 cho phép một file chứa quy tắc cho nhiều lược đồ, chạy nhầm là báo trượt thứ tác giả không đòi.
        /// </summary>
        public static IdsCheckResult Check(IEnumerable<IdsSpecification> specifications, IEnumerable<IIdsElement> elements, string? modelSchema)
        {
            var specs = specifications?.ToList() ?? new List<IdsSpecification>();
            var items = elements?.ToList() ?? new List<IIdsElement>();
            var results = new List<IdsSpecificationResult>();

            foreach (var spec in specs)
            {
                if (!spec.AppliesTo(modelSchema))
                {
                    results.Add(new IdsSpecificationResult(spec.Name, spec.Description, 0, 0, Array.Empty<IdsFailure>(),
                        "bỏ qua: ifcVersion=\"" + string.Join(" ", spec.IfcVersions) + "\" không gồm lược đồ của file (" + modelSchema + ")"));
                    continue;
                }

                var applicable = items.Where(e => spec.Applicability.All(f => Satisfies(e, f))).ToList();
                var failures = new List<IdsFailure>();
                var passed = 0;

                foreach (var element in applicable)
                {
                    var reasons = new List<string>();
                    if (spec.IsProhibited)
                    {
                        // minOccurs="0" maxOccurs="0": phần tử lọt applicability là vi phạm — không có gì để kiểm thêm.
                        reasons.Add("specification cấm (maxOccurs=0): không được có phần tử nào thuộc loại này");
                    }

                    foreach (var requirement in spec.IsProhibited ? Enumerable.Empty<IdsFacet>() : spec.Requirements)
                    {
                        var holds = Satisfies(element, requirement);
                        if (requirement.IsProhibited)
                        {
                            if (holds)
                            {
                                reasons.Add("không được có " + requirement.Describe());
                            }
                        }
                        else if (!holds && !requirement.IsOptional)
                        {
                            reasons.Add("thiếu/sai: cần " + requirement.Describe());
                        }
                        else if (!holds && IsPresent(element, requirement))
                        {
                            // IDS 1.0: optional = KHÔNG bắt buộc có, nhưng ĐÃ có thì phải đúng giá trị.
                            // Trước đây optional được tha vô điều kiện — FireRating = "banana" vẫn đạt.
                            reasons.Add("có nhưng sai giá trị: " + requirement.Describe());
                        }
                    }

                    if (reasons.Count == 0)
                    {
                        passed++;
                    }
                    else if (failures.Count < MaxFailuresPerSpecification)
                    {
                        failures.Add(new IdsFailure(spec.Name, element.Label, string.Join("; ", reasons)));
                    }
                }

                results.Add(new IdsSpecificationResult(spec.Name, spec.Description, applicable.Count, passed, failures, required: spec.MinOccurs >= 1));
            }

            return new IdsCheckResult(results, items.Count);
        }

        /// <summary>Đối tượng của facet có mặt trên phần tử không (bất kể giá trị) — dùng cho cardinality optional.</summary>
        private static bool IsPresent(IIdsElement element, IdsFacet facet)
        {
            switch (facet.Kind)
            {
                case IdsFacetKind.Attribute:
                    if (element is IIdsTypedElement typedAttributes)
                    {
                        return typedAttributes.Attributes(facet.Name).Any(a => !a.IsNull);
                    }

                    return NamesOf(facet.Name).Any(name => !string.IsNullOrWhiteSpace(element.Attribute(name)));

                case IdsFacetKind.Property:
                    if (element is IIdsTypedElement typedProperties)
                    {
                        return typedProperties.Properties(facet.Container, facet.Name).Any(set => set.Value.Count > 0);
                    }

                    return PropertySets(facet).Any(set => NamesOf(facet.Name).Any(name => !string.IsNullOrWhiteSpace(element.Property(set, name))));

                case IdsFacetKind.Classification:
                    if (element is IIdsTypedElement typedClassifications)
                    {
                        return typedClassifications.ClassificationReferences().Count > 0;
                    }

                    var system = facet.Container != null && !facet.Container.IsAny ? facet.Container.Simple : null;
                    return element.Classifications(system).Any(code => !string.IsNullOrWhiteSpace(code));

                case IdsFacetKind.Material:
                    return element.Materials.Any(m => !string.IsNullOrWhiteSpace(m));

                case IdsFacetKind.PartOf:
                    return element.PartOf.Any();

                default:
                    return false;
            }
        }

        private static bool Satisfies(IIdsElement element, IdsFacet facet)
        {
            switch (facet.Kind)
            {
                case IdsFacetKind.Entity:
                    // IDS 1.0 viết tên lớp bằng CHỮ HOA (IFCWALL) và so phân biệt hoa thường; đường Revit trả
                    // "IfcWall" nên nâng phía mô hình lên chữ hoa. IDS viết "IfcWall" là file sai chuẩn — không khớp.
                    return (facet.Name.Accepts(element.IfcEntity.ToUpperInvariant())
                            || (element is IIdsTypedElement mapped && mapped.EntityAlias != null && facet.Name.Accepts(mapped.EntityAlias)))
                           && (facet.Container == null || facet.Container.IsAny || facet.Container.Accepts(element.PredefinedType)
                               || (element is IIdsElementDetails details && details.PredefinedTypeIsUserDefined && facet.Container.Accepts("USERDEFINED")));

                case IdsFacetKind.Attribute:
                    // Tên thuộc tính trong IDS là một RÀNG BUỘC, không nhất thiết là một tên cụ thể
                    // ("mọi thuộc tính khớp mẫu…"). Ở đây chỉ hỗ trợ tên cố định — dạng hay dùng thật —
                    // và tên khai bằng danh sách/mẫu thì soi từng cái một. Đường IFC (giá trị có kiểu) hiểu cả
                    // tên khai bằng mẫu: mọi thuộc tính khớp và có giá trị đều phải đạt.
                    if (element is IIdsTypedElement typedAttributes)
                    {
                        var values = typedAttributes.Attributes(facet.Name).Where(a => a.Value != null).ToList();
                        return values.Count > 0 && values.All(a => facet.Value.AcceptsTyped(a.Value!));
                    }

                    return NamesOf(facet.Name).Any(name => facet.Value.Accepts(element.Attribute(name)));

                case IdsFacetKind.Property:
                    if (element is IIdsTypedElement typedProperties)
                    {
                        return PropertyHolds(typedProperties, facet);
                    }

                    return PropertySets(facet).Any(set => NamesOf(facet.Name).Any(name => facet.Value.Accepts(element.Property(set, name))));

                case IdsFacetKind.Classification:
                    if (element is IIdsTypedElement typedClassifications)
                    {
                        // Hệ khai bằng pattern/danh sách được hiểu đúng (bản chuỗi chỉ biết simpleValue, pattern thành "mọi hệ").
                        return typedClassifications.ClassificationReferences().Any(r =>
                            (facet.Container == null || facet.Container.IsAny || facet.Container.Accepts(r.Key))
                            && (facet.Value.IsAny || facet.Value.Accepts(r.Value)));
                    }

                    var system = facet.Container != null && !facet.Container.IsAny ? facet.Container.Simple : null;
                    return element.Classifications(system).Any(code => facet.Value.Accepts(code));

                case IdsFacetKind.Material:
                    return element.Materials.Any(material => facet.Value.Accepts(material));

                default:
                    if (facet.Container != null && !facet.Container.IsAny)
                    {
                        // predefinedType của tổ tiên chỉ đường IFC biết; phần tử không có thông tin đó thì trượt.
                        return element is IIdsElementDetails withTypes
                               && withTypes.PartOfWithPredefinedType.Any(parent =>
                                   (facet.Relation == null || string.Equals(parent.Relation, facet.Relation, StringComparison.OrdinalIgnoreCase))
                                   && facet.Value.Accepts(parent.Entity) && facet.Container.Accepts(parent.PredefinedType));
                    }

                    // facet.Relation null: chấp nhận entry nào cũng được (relation bất kỳ, kể cả entry
                    // "trộn" tự Relation null của IfcIdsElement). Có khai relation: chỉ entry ĐÚNG quan hệ
                    // đó — không rơi về entry "trộn", vì entry trộn không chứng minh được đúng MỘT loại
                    // quan hệ đã dùng xuyên suốt như buildingSMART đòi.
                    return facet.Relation == null
                        ? element.PartOf.Any(parent => facet.Value.Accepts(parent.Entity))
                        : element.PartOf.Any(parent => string.Equals(parent.Relation, facet.Relation, StringComparison.OrdinalIgnoreCase)
                                                        && facet.Value.Accepts(parent.Entity));
            }
        }

        /// <summary>
        /// Facet property trên giá trị có kiểu, theo IDS 1.0 (và IfcTester): phải có ít nhất một pset khớp; MỌI pset khớp
        /// phải có property khớp tên và có giá trị; MỌI property khớp phải đúng <c>dataType</c> (nếu khai) và có ít nhất
        /// một giá trị thoả ràng buộc (property danh sách/liệt kê/khoảng/bảng: một giá trị khớp là đủ).
        /// </summary>
        private static bool PropertyHolds(IIdsTypedElement element, IdsFacet facet)
        {
            var sets = element.Properties(facet.Container, facet.Name);
            if (facet.Container == null || facet.Container.IsAny)
            {
                // Không khai propertySet (lệch IDS 1.0, lint cảnh báo): property nằm ở pset nào cũng được — không
                // đòi MỌI pset của phần tử đều có nó.
                sets = sets.Where(set => set.Value.Count > 0).ToList();
            }

            return sets.Count > 0 && sets.All(set => set.Value.Count > 0 && set.Value.All(property =>
            {
                // Property bảng có cột mỗi kiểu một khác: khai dataType thì chỉ xét giá trị đúng kiểu đó; không còn giá trị nào là trượt.
                var candidates = property.Values
                    .Where(v => string.IsNullOrEmpty(facet.DataType) || v.Key == null || string.Equals(v.Key, facet.DataType, StringComparison.OrdinalIgnoreCase))
                    .Select(v => v.Value).ToList();
                return property.Supported && candidates.Count > 0 && (facet.Value.IsAny || candidates.Any(facet.Value.AcceptsTyped));
            }));
        }

        /// <summary>
        /// Property set cần thử: không khai/không ràng buộc → <c>null</c> (mọi pset, khớp tên property trần);
        /// <c>simpleValue</c> hoặc <c>enumeration</c> → từng pset một; khai bằng pattern → không suy ngược
        /// được, trả rỗng và facet trượt — trước đây rơi về "mọi pset", lỏng hơn điều IDS đòi.
        /// </summary>
        private static IEnumerable<string?> PropertySets(IdsFacet facet)
        {
            if (facet.Container == null || facet.Container.IsAny)
            {
                yield return null;
                yield break;
            }

            foreach (var set in NamesOf(facet.Container))
            {
                yield return set;
            }
        }

        /// <summary>
        /// Những tên cần thử cho một facet. Khai bằng <c>simpleValue</c> thì đúng một tên; khai bằng
        /// danh sách thì thử cả danh sách. Khai bằng mẫu (pattern) thì không suy ngược ra tên được —
        /// trả về rỗng, và facet đó trượt thay vì âm thầm coi như đạt.
        /// </summary>
        private static IEnumerable<string> NamesOf(IdsValue name)
        {
            if (!string.IsNullOrEmpty(name.Simple))
            {
                yield return name.Simple!;
                yield break;
            }

            foreach (var value in name.Enumeration)
            {
                yield return value;
            }
        }
    }
}
