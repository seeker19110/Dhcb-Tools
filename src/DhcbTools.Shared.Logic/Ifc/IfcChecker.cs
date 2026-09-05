using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace DhcbTools.Shared.Logic.Ifc
{
    /// <summary>Mức nặng nhẹ của một phát hiện.</summary>
    public enum IfcSeverity
    {
        /// <summary>Chỉ để biết, không chặn nộp.</summary>
        ThongTin,

        /// <summary>Đáng xem lại nhưng không sai chuẩn.</summary>
        CanhBao,

        /// <summary>Sai — không nộp file này.</summary>
        Loi,
    }

    /// <summary>Một phát hiện khi kiểm file IFC.</summary>
    public sealed class IfcFinding
    {
        /// <summary>Khởi tạo một phát hiện.</summary>
        public IfcFinding(IfcSeverity severity, string message)
        {
            Severity = severity;
            Message = message;
        }

        /// <summary>Mức nặng nhẹ.</summary>
        public IfcSeverity Severity { get; }

        /// <summary>Thông báo tiếng Việt, đã kèm số hiệu thực thể khi có.</summary>
        public string Message { get; }
    }

    /// <summary>Kết quả kiểm một file IFC.</summary>
    public sealed class IfcCheckResult
    {
        internal IfcCheckResult(IReadOnlyList<IfcFinding> findings, int entityCount, string schema)
        {
            Findings = findings;
            EntityCount = entityCount;
            Schema = schema;
        }

        /// <summary>Mọi phát hiện, theo thứ tự kiểm.</summary>
        public IReadOnlyList<IfcFinding> Findings { get; }

        /// <summary>Số thực thể đọc được.</summary>
        public int EntityCount { get; }

        /// <summary>Lược đồ khai trong file.</summary>
        public string Schema { get; }

        /// <summary>Số phát hiện mức lỗi.</summary>
        public int ErrorCount => Findings.Count(f => f.Severity == IfcSeverity.Loi);

        /// <summary>Số phát hiện mức cảnh báo.</summary>
        public int WarningCount => Findings.Count(f => f.Severity == IfcSeverity.CanhBao);

        /// <summary>Không có lỗi nào.</summary>
        public bool Ok => ErrorCount == 0;

        /// <summary>Bản in nhiều dòng để đưa thẳng ra màn hình hoặc vào log.</summary>
        public string Render()
        {
            var sb = new StringBuilder();
            sb.AppendLine("Lược đồ: " + (string.IsNullOrEmpty(Schema) ? "(không khai)" : Schema)
                          + " · " + EntityCount.ToString(CultureInfo.InvariantCulture) + " thực thể");
            foreach (var f in Findings)
            {
                sb.AppendLine(Prefix(f.Severity) + " " + f.Message);
            }

            sb.Append(Ok
                ? "Đạt: không có lỗi" + (WarningCount > 0 ? ", " + WarningCount + " cảnh báo." : ".")
                : "Không đạt: " + ErrorCount + " lỗi" + (WarningCount > 0 ? ", " + WarningCount + " cảnh báo." : "."));
            return sb.ToString();
        }

        private static string Prefix(IfcSeverity s) => s switch
        {
            IfcSeverity.Loi => "[Lỗi]",
            IfcSeverity.CanhBao => "[Cảnh báo]",
            _ => "[Thông tin]",
        };
    }

    /// <summary>
    /// Đối chiếu một file IFC với bộ quy tắc: đúng lược đồ, đủ số lượng, đủ thuộc tính, không tham
    /// chiếu gãy, mã định danh không trùng.
    /// <para>
    /// Vì sao cần: NĐ 217/2026 bắt nộp dữ liệu BIM cho cơ quan chuyên môn, nên "xuất được" chưa đủ —
    /// phải "xuất rồi tự đọc lại thấy đúng". Bộ xuất IFC của Revit im lặng bỏ phần tử khi mapping
    /// thiếu; không đọc lại thì đến lúc bên nhận mở file mới biết.
    /// </para>
    /// </summary>
    public static class IfcChecker
    {
        /// <summary>Kiểm nội dung một file IFC theo bộ quy tắc.</summary>
        public static IfcCheckResult Check(string ifcText, IfcCheckSpec spec)
        {
            if (spec is null)
            {
                throw new ArgumentNullException(nameof(spec));
            }

            var findings = new List<IfcFinding>();
            IfcModel model;
            try
            {
                model = IfcModel.Parse(ifcText);
            }
            catch (IfcParseException ex)
            {
                findings.Add(new IfcFinding(IfcSeverity.Loi, "Không đọc được file: " + ex.Message));
                return new IfcCheckResult(findings, 0, string.Empty);
            }

            CheckSchema(model, spec, findings);
            CheckStructure(model, spec, findings);

            foreach (var rule in spec.Rules)
            {
                CheckRule(model, rule, findings);
            }

            return new IfcCheckResult(findings, model.Count, model.Schema);
        }

        private static void CheckSchema(IfcModel model, IfcCheckSpec spec, List<IfcFinding> findings)
        {
            if (string.IsNullOrEmpty(model.Schema))
            {
                findings.Add(new IfcFinding(IfcSeverity.CanhBao, "File không khai FILE_SCHEMA — bên nhận không biết đọc theo bản IFC nào."));
            }

            if (!string.IsNullOrWhiteSpace(spec.Schema)
                && !string.Equals(model.Schema, spec.Schema, StringComparison.OrdinalIgnoreCase))
            {
                findings.Add(new IfcFinding(
                    IfcSeverity.Loi,
                    "Lược đồ là " + (string.IsNullOrEmpty(model.Schema) ? "(không khai)" : model.Schema)
                    + ", quy tắc yêu cầu " + spec.Schema + "."));
            }
        }

        private static void CheckStructure(IfcModel model, IfcCheckSpec spec, List<IfcFinding> findings)
        {
            if (spec.MinEntities.HasValue && model.Count < spec.MinEntities.Value)
            {
                findings.Add(new IfcFinding(
                    IfcSeverity.Loi,
                    "File chỉ có " + model.Count + " thực thể, quy tắc yêu cầu tối thiểu " + spec.MinEntities.Value + "."));
            }

            if (model.DuplicateIds.Count > 0)
            {
                findings.Add(new IfcFinding(
                    IfcSeverity.Loi,
                    "Có " + model.DuplicateIds.Count + " số hiệu khai hai lần: " + Sample(model.DuplicateIds.Select(id => "#" + id), 10) + "."));
            }

            if (spec.RequireResolvedReferences)
            {
                var dangling = model.DanglingReferences();
                if (dangling.Count > 0)
                {
                    findings.Add(new IfcFinding(
                        IfcSeverity.Loi,
                        "Có " + dangling.Count + " tham chiếu trỏ tới thực thể không tồn tại: "
                        + Sample(dangling.Select(d => "#" + d.Key.Id + " → #" + d.Value), 10) + "."));
                }
            }

            if (!spec.RequireUniqueGlobalId)
            {
                return;
            }

            // Kiểu nào ĐÃ có ít nhất một mã định danh đúng dạng thì kiểu đó là lớp con của IfcRoot;
            // suy từ chính file thay vì mang theo bảng lược đồ. Nhờ vậy IFCPROPERTYSINGLEVALUE —
            // cũng mở đầu bằng một chuỗi — không bị coi là mang mã định danh.
            var rootTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entity in model.File.Data)
            {
                if (IfcModel.LooksLikeGlobalId(IfcModel.GlobalIdOf(entity)))
                {
                    rootTypes.Add(entity.Type);
                }
            }

            if (rootTypes.Count == 0)
            {
                if (model.Count > 0)
                {
                    findings.Add(new IfcFinding(
                        IfcSeverity.Loi,
                        "Không thực thể nào mang mã định danh đúng dạng (22 ký tự) — file không dùng được để cập nhật mô hình."));
                }

                return;
            }

            var seen = new Dictionary<string, int>(StringComparer.Ordinal);
            var duplicated = new List<string>();
            var malformed = new List<int>();
            foreach (var entity in model.File.Data)
            {
                if (!rootTypes.Contains(entity.Type))
                {
                    continue;
                }

                var gid = IfcModel.GlobalIdOf(entity);
                if (!IfcModel.LooksLikeGlobalId(gid))
                {
                    malformed.Add(entity.Id);
                    continue;
                }

                if (seen.TryGetValue(gid!, out var first))
                {
                    duplicated.Add(gid + " (#" + first + " và #" + entity.Id + ")");
                }
                else
                {
                    seen[gid!] = entity.Id;
                }
            }

            if (malformed.Count > 0)
            {
                findings.Add(new IfcFinding(
                    IfcSeverity.Loi,
                    "Có " + malformed.Count + " thực thể mang mã định danh rỗng hoặc không hợp lệ: "
                    + Sample(malformed.Select(id => "#" + id), 10) + "."));
            }

            if (duplicated.Count > 0)
            {
                findings.Add(new IfcFinding(
                    IfcSeverity.Loi,
                    "Có " + duplicated.Count + " mã định danh trùng nhau: " + Sample(duplicated, 5)
                    + ". Bên nhận cập nhật theo mã này, trùng mã là ghi đè nhầm phần tử."));
            }
        }

        private static void CheckRule(IfcModel model, IfcTypeRule rule, List<IfcFinding> findings)
        {
            var items = model.OfType(rule.Type);
            var label = rule.Type.ToUpperInvariant();
            var limit = rule.ListLimit > 0 ? rule.ListLimit : 10;

            if (rule.ExactCount.HasValue && items.Count != rule.ExactCount.Value)
            {
                findings.Add(new IfcFinding(
                    IfcSeverity.Loi,
                    label + ": có " + items.Count + ", quy tắc yêu cầu đúng " + rule.ExactCount.Value + "."));
            }

            if (rule.MinCount.HasValue && items.Count < rule.MinCount.Value)
            {
                findings.Add(new IfcFinding(
                    IfcSeverity.Loi,
                    label + ": có " + items.Count + ", quy tắc yêu cầu tối thiểu " + rule.MinCount.Value + "."));
            }

            if (rule.MaxCount.HasValue && items.Count > rule.MaxCount.Value)
            {
                findings.Add(new IfcFinding(
                    IfcSeverity.Loi,
                    label + ": có " + items.Count + ", quy tắc yêu cầu tối đa " + rule.MaxCount.Value + "."));
            }

            if (items.Count == 0)
            {
                return;
            }

            if (rule.RequireName)
            {
                var noName = items.Where(e => string.IsNullOrWhiteSpace(IfcModel.NameOf(e))).ToList();
                if (noName.Count > 0)
                {
                    findings.Add(new IfcFinding(
                        IfcSeverity.Loi,
                        label + ": " + noName.Count + "/" + items.Count + " phần tử không có tên: "
                        + Sample(noName.Select(Describe), limit) + "."));
                }
            }

            foreach (var key in rule.RequireProperties)
            {
                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                var missing = new List<IfcEntity>();
                var blank = new List<IfcEntity>();
                foreach (var e in items)
                {
                    if (!model.TryProperty(e.Id, key, out var value))
                    {
                        missing.Add(e);
                    }
                    else if (string.IsNullOrWhiteSpace(value))
                    {
                        blank.Add(e);
                    }
                }

                if (missing.Count > 0)
                {
                    findings.Add(new IfcFinding(
                        IfcSeverity.Loi,
                        label + ": " + missing.Count + "/" + items.Count + " phần tử thiếu thuộc tính " + key + ": "
                        + Sample(missing.Select(Describe), limit) + "."));
                }

                if (blank.Count > 0)
                {
                    findings.Add(new IfcFinding(
                        IfcSeverity.Loi,
                        label + ": " + blank.Count + "/" + items.Count + " phần tử có thuộc tính " + key
                        + " nhưng bỏ trống: " + Sample(blank.Select(Describe), limit) + "."));
                }
            }

            if (rule.RequireClassification)
            {
                var noClass = items.Where(e => model.ClassificationsOf(e.Id).Count == 0).ToList();
                if (noClass.Count > 0)
                {
                    findings.Add(new IfcFinding(
                        IfcSeverity.Loi,
                        label + ": " + noClass.Count + "/" + items.Count + " phần tử chưa gán mã phân loại: "
                        + Sample(noClass.Select(Describe), limit) + "."));
                }
            }
        }

        /// <summary>Nhãn của một phần tử trong thông báo: số hiệu kèm tên nếu có.</summary>
        private static string Describe(IfcEntity e)
        {
            var name = IfcModel.NameOf(e);
            return string.IsNullOrWhiteSpace(name) ? "#" + e.Id : "#" + e.Id + " " + name;
        }

        /// <summary>
        /// Kể tối đa <paramref name="limit"/> mục rồi nói còn bao nhiêu. Không in hết: một file IFC lỗi
        /// mapping có thể có hàng nghìn phần tử cùng một lỗi, in hết là không ai đọc được dòng nào.
        /// </summary>
        internal static string Sample(IEnumerable<string> items, int limit)
        {
            var list = items.ToList();
            if (list.Count <= limit)
            {
                return string.Join(", ", list);
            }

            return string.Join(", ", list.Take(limit)) + " … và " + (list.Count - limit) + " nữa";
        }
    }
}
