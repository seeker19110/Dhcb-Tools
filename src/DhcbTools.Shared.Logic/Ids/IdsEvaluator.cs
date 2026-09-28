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
        /// <summary>Như <see cref="IIdsElement.PartOf"/>, kèm PredefinedType của từng tổ tiên.</summary>
        IEnumerable<(string? Relation, string Entity, string PredefinedType)> PartOfWithPredefinedType { get; }
    }

    /// <summary>Một facet soi trên một phần tử: không có gì để soi, có mà sai, hay khớp.</summary>
    internal enum IdsMatch
    {
        /// <summary>Không có đối tượng của facet (thuộc tính <c>$</c>, không có pset/property/phân loại…).</summary>
        Absent,

        /// <summary>Có, nhưng không thoả (sai giá trị, sai dataType, rỗng…).</summary>
        Mismatch,

        /// <summary>Thoả.</summary>
        Match,
    }

    /// <summary>
    /// Phần tử tự soi facet trên dữ liệu CÓ KIỂU (đường file IFC). Đường Revit không cài — facet đi luật chuỗi chung.
    /// </summary>
    internal interface IIdsTypedElement
    {
        /// <summary>
        /// Phần tử có thuộc tập ứng viên của facet applicability ĐẦU TIÊN không — theo IfcTester: facet property chỉ
        /// nhìn IfcObjectDefinition (và vật liệu/profile từ IFC4), classification/material chỉ nhìn IfcObjectDefinition,
        /// entity/attribute/partOf nhìn mọi thực thể. Applicability rỗng (<c>null</c>): mọi IfcObjectDefinition — đúng
        /// tập "thực thể có GlobalId, trừ quan hệ và định nghĩa property" mà đường IFC dùng trước đây, không phải cả
        /// triệu điểm toạ độ của file.
        /// </summary>
        bool InScopeOf(IdsFacet? first);

        /// <summary>Kết quả soi, hoặc <c>null</c> để evaluator dùng luật chuỗi chung (material, partOf).</summary>
        IdsMatch? Match(IdsFacet facet);
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
        internal IdsSpecificationResult(string name, string description, int applicable, int passed, IReadOnlyList<IdsFailure> failures, string? versionNote = null, bool required = false)
        {
            Name = name;
            Description = description;
            Applicable = applicable;
            Passed = passed;
            Failures = failures;
            VersionNote = versionNote;
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

        /// <summary>
        /// Khác null khi <c>ifcVersion</c> của specification không gồm lược đồ của file. Specification VẪN được kiểm —
        /// đúng như IfcTester và bộ ca chính thức của buildingSMART (ca <c>ids/…</c> khai IFC2X3 chạy trên file IFC4
        /// vẫn phải ra "không đạt"); trước đây bỏ qua hẳn nên 3 ca đó ra "đạt". Báo cáo in ghi chú này để người đọc
        /// biết quy tắc có thể viết cho lược đồ khác.
        /// </summary>
        public string? VersionNote { get; }

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
        public bool NoApplicableElements => Applicable == 0;
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
        /// khác rỗng thì specification khai <c>ifcVersion</c> không chứa lược đồ đó được ghi chú
        /// (<see cref="IdsSpecificationResult.VersionNote"/>) nhưng VẪN kiểm, như IfcTester.
        /// </summary>
        public static IdsCheckResult Check(IEnumerable<IdsSpecification> specifications, IEnumerable<IIdsElement> elements, string? modelSchema)
        {
            var specs = specifications?.ToList() ?? new List<IdsSpecification>();
            var items = elements?.ToList() ?? new List<IIdsElement>();
            var results = new List<IdsSpecificationResult>();

            foreach (var spec in specs)
            {
                var note = spec.AppliesTo(modelSchema)
                    ? null
                    : "ifcVersion=\"" + string.Join(" ", spec.IfcVersions) + "\" không gồm lược đồ của file (" + modelSchema + ") — vẫn kiểm như IfcTester";
                var first = spec.Applicability.FirstOrDefault();
                var applicable = items.Where(e => (!(e is IIdsTypedElement typed) || typed.InScopeOf(first))
                                                  && spec.Applicability.All(f => Match(e, f) == IdsMatch.Match)).ToList();
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
                        var match = Match(element, requirement);
                        if (requirement.IsProhibited)
                        {
                            if (match == IdsMatch.Match)
                            {
                                reasons.Add("không được có " + requirement.Describe());
                            }
                        }
                        else if (!requirement.IsOptional)
                        {
                            if (match != IdsMatch.Match)
                            {
                                reasons.Add("thiếu/sai: cần " + requirement.Describe());
                            }
                        }
                        else if (match == IdsMatch.Mismatch)
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

                results.Add(new IdsSpecificationResult(spec.Name, spec.Description, applicable.Count, passed, failures, note, required: spec.MinOccurs >= 1));
            }

            return new IdsCheckResult(results, items.Count);
        }

        /// <summary>
        /// Soi một facet: phần tử có kiểu (đường IFC) tự soi; còn lại theo luật chuỗi — khớp, hoặc không khớp mà đối
        /// tượng CÓ mặt (sai), hoặc vắng.
        /// </summary>
        private static IdsMatch Match(IIdsElement element, IdsFacet facet)
        {
            var typed = (element as IIdsTypedElement)?.Match(facet);
            if (typed != null)
            {
                return typed.Value;
            }

            return Satisfies(element, facet) ? IdsMatch.Match : IsPresent(element, facet) ? IdsMatch.Mismatch : IdsMatch.Absent;
        }

        /// <summary>Đối tượng của facet có mặt trên phần tử không (bất kể giá trị) — dùng cho cardinality optional.</summary>
        private static bool IsPresent(IIdsElement element, IdsFacet facet)
        {
            switch (facet.Kind)
            {
                case IdsFacetKind.Attribute:
                    return NamesOf(facet.Name).Any(name => !string.IsNullOrWhiteSpace(element.Attribute(name)));

                case IdsFacetKind.Property:
                    return PropertySets(facet).Any(set => NamesOf(facet.Name).Any(name => !string.IsNullOrWhiteSpace(element.Property(set, name))));

                case IdsFacetKind.Classification:
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
                    return facet.Name.Accepts(element.IfcEntity.ToUpperInvariant())
                           && (facet.Container == null || facet.Container.IsAny || facet.Container.Accepts(element.PredefinedType));

                case IdsFacetKind.Attribute:
                    // Tên thuộc tính trong IDS là một RÀNG BUỘC, không nhất thiết là một tên cụ thể
                    // ("mọi thuộc tính khớp mẫu…"). Ở đây chỉ hỗ trợ tên cố định — dạng hay dùng thật —
                    // và tên khai bằng danh sách/mẫu thì soi từng cái một.
                    return NamesOf(facet.Name).Any(name => facet.Value.Accepts(element.Attribute(name)));

                case IdsFacetKind.Property:
                    return PropertySets(facet).Any(set => NamesOf(facet.Name).Any(name => facet.Value.Accepts(element.Property(set, name))));

                case IdsFacetKind.Classification:
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
