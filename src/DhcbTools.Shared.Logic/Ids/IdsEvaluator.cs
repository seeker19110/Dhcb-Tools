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

        /// <summary>Tên nhóm/hệ/tầng mà phần tử thuộc về.</summary>
        IEnumerable<string> PartOf { get; }
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
        internal IdsSpecificationResult(string name, string description, int applicable, int passed, IReadOnlyList<IdsFailure> failures)
        {
            Name = name;
            Description = description;
            Applicable = applicable;
            Passed = passed;
            Failures = failures;
        }

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
        public static IdsCheckResult Check(IEnumerable<IdsSpecification> specifications, IEnumerable<IIdsElement> elements)
        {
            var specs = specifications?.ToList() ?? new List<IdsSpecification>();
            var items = elements?.ToList() ?? new List<IIdsElement>();
            var results = new List<IdsSpecificationResult>();

            foreach (var spec in specs)
            {
                var applicable = items.Where(e => spec.Applicability.All(f => Satisfies(e, f))).ToList();
                var failures = new List<IdsFailure>();
                var passed = 0;

                foreach (var element in applicable)
                {
                    var reasons = new List<string>();
                    foreach (var requirement in spec.Requirements)
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

                results.Add(new IdsSpecificationResult(spec.Name, spec.Description, applicable.Count, passed, failures));
            }

            return new IdsCheckResult(results, items.Count);
        }

        private static bool Satisfies(IIdsElement element, IdsFacet facet)
        {
            switch (facet.Kind)
            {
                case IdsFacetKind.Entity:
                    return facet.Name.Accepts(element.IfcEntity)
                           && (facet.Container == null || facet.Container.IsAny || facet.Container.Accepts(element.PredefinedType));

                case IdsFacetKind.Attribute:
                    // Tên thuộc tính trong IDS là một RÀNG BUỘC, không nhất thiết là một tên cụ thể
                    // ("mọi thuộc tính khớp mẫu…"). Ở đây chỉ hỗ trợ tên cố định — dạng hay dùng thật —
                    // và tên khai bằng danh sách/mẫu thì soi từng cái một.
                    return NamesOf(facet.Name).Any(name => facet.Value.Accepts(element.Attribute(name)));

                case IdsFacetKind.Property:
                    var set = facet.Container != null && !facet.Container.IsAny ? facet.Container.Simple : null;
                    return NamesOf(facet.Name).Any(name => facet.Value.Accepts(element.Property(set, name)));

                case IdsFacetKind.Classification:
                    var system = facet.Container != null && !facet.Container.IsAny ? facet.Container.Simple : null;
                    return element.Classifications(system).Any(code => facet.Value.Accepts(code));

                case IdsFacetKind.Material:
                    return element.Materials.Any(material => facet.Value.Accepts(material));

                default:
                    return element.PartOf.Any(parent => facet.Value.Accepts(parent));
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
