using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace DhcbTools.Shared.Logic.Ai
{
    /// <summary>Một tên tham số CÓ THẬT đọc được từ mô hình, kèm mức độ được điền.</summary>
    public sealed class ParameterCandidate
    {
        public ParameterCandidate(string name, string category, string storageType, int filledCount, int totalCount)
        {
            Name = name;
            Category = category;
            StorageType = storageType;
            FilledCount = filledCount;
            TotalCount = totalCount;
        }

        public string Name { get; }

        /// <summary>Category nơi bắt gặp tên này (chỉ để giải thích cho kỹ sư).</summary>
        public string Category { get; }

        /// <summary>"Double", "Integer", "String", "ElementId" — như <c>Parameter.StorageType</c>.</summary>
        public string StorageType { get; }

        /// <summary>Số phần tử thật sự có giá trị. 0 = tham số tồn tại nhưng rỗng toàn dự án.</summary>
        public int FilledCount { get; }

        public int TotalCount { get; }

        /// <summary>0–1. Tham số rỗng toàn dự án gần như chắc chắn không phải thứ lệnh cần đọc.</summary>
        public double FillRatio => TotalCount <= 0 ? 0 : (double)FilledCount / TotalCount;
    }

    /// <summary>Trạng thái của một khoá logic sau khi soi mô hình.</summary>
    public enum SuggestionStatus
    {
        /// <summary>Mô hình đã có đúng một tên nằm sẵn trong từ điển — không cần khai gì thêm.</summary>
        DaCo,

        /// <summary>Không có tên nào khớp, nhưng tìm được ứng viên đủ giống để kỹ sư duyệt.</summary>
        DeXuat,

        /// <summary>Không có ứng viên nào đủ giống — lệnh dùng khoá này sẽ báo E-PARAM-MISSING.</summary>
        KhongThay,
    }

    /// <summary>Một dòng đề xuất cho <c>dictionary.json</c>.</summary>
    public sealed class DictionarySuggestion
    {
        public DictionarySuggestion(string key, string? name, double confidence, string reason, SuggestionStatus status)
        {
            Key = key;
            Name = name;
            Confidence = confidence;
            Reason = reason;
            Status = status;
        }

        /// <summary>Khoá logic: <c>level</c>, <c>diameter</c>, <c>bottomElevation</c>…</summary>
        public string Key { get; }

        /// <summary>Tên tham số CÓ THẬT trong mô hình, hoặc null khi <see cref="Status"/> là <see cref="SuggestionStatus.KhongThay"/>.</summary>
        public string? Name { get; }

        public double Confidence { get; }

        public string Reason { get; }

        public SuggestionStatus Status { get; }

        /// <summary>Chỉ dòng này mới được ghi vào từ điển; <c>DaCo</c> không cần, <c>KhongThay</c> không có gì để ghi.</summary>
        public bool IsProposal => Status == SuggestionStatus.DeXuat && Name != null;

        /// <summary>Dưới ngưỡng thì kỹ sư phải nhìn tận mắt trước khi nhận.</summary>
        public bool NeedsReview => Status != SuggestionStatus.DaCo && Confidence < DictionarySuggester.ReviewThreshold;
    }

    /// <summary>
    /// Soi tên tham số CÓ THẬT trong mô hình rồi đề xuất nội dung <c>%APPDATA%\DHCB\dictionary.json</c>
    /// — hoàn toàn offline, hoàn toàn thuần (không tham chiếu Revit) nên chạy test được trên CI.
    /// <para>
    /// Vấn đề đang chữa: giai đoạn 9.2 đã bỏ được tên tham số cứng trong mã, nhưng đổi lại kỹ sư phải
    /// tự sửa JSON trong <c>%APPDATA%</c> mỗi lần vấp <c>E-PARAM-MISSING</c> — đúng thứ ma sát đã đo được
    /// trên dự án thật (progress.md §21: <c>ElevationTag</c>/<c>HangerAuto</c> đòi tên riêng của dự án).
    /// Sửa JSON tay chính là thứ giai đoạn 9.1 vừa xoá bỏ ở phần config, không có lý do gì giữ lại ở đây.
    /// </para>
    /// <para>
    /// Nguyên tắc giữ nguyên như <see cref="LayerMappingSuggester"/>: <b>chỉ đề xuất tên có thật</b> trong
    /// danh sách đọc từ mô hình, kèm độ tin cậy và lý do; không tự ghi đè thứ kỹ sư đã khai.
    /// </para>
    /// </summary>
    public static class DictionarySuggester
    {
        /// <summary>Dưới mức này thì đánh dấu để kỹ sư xem, không nhận tự động.</summary>
        public const double ReviewThreshold = 0.7;

        /// <summary>Dưới mức này thì coi như không tìm được gì — thà báo thiếu còn hơn đề xuất bừa.</summary>
        public const double MinConfidence = 0.45;

        private static readonly Regex TokenSplit = new Regex(@"[\s\-_\.\|/:,\(\)\[\]]+", RegexOptions.Compiled);

        /// <summary>Khoá mà giá trị phải là số — tên khớp nhưng kiểu chuỗi thì gần như chắc là nhầm.</summary>
        private static readonly HashSet<string> NumericKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "diameter", "width", "height", "bottomElevation", "topElevation", "centreElevation",
        };

        /// <summary>Tách tên thành token thường, bỏ dấu tiếng Việt.</summary>
        internal static List<string> Tokenize(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return new List<string>();
            }

            return TokenSplit.Split(LayerMappingSuggester.RemoveDiacritics(text).ToLowerInvariant())
                .Where(t => t.Length > 0)
                .ToList();
        }

        private static string Normalize(string text) =>
            string.Join(" ", Tokenize(text));

        /// <summary>Điểm giống nhau 0–1 giữa một tên tham số thật và một tên đồng nghĩa của khoá.</summary>
        internal static double NameScore(string candidate, string synonym)
        {
            var a = Tokenize(candidate);
            var b = Tokenize(synonym);
            if (a.Count == 0 || b.Count == 0)
            {
                return 0;
            }

            if (string.Equals(Normalize(candidate), Normalize(synonym), StringComparison.OrdinalIgnoreCase))
            {
                return 1.0;
            }

            var common = a.Intersect(b, StringComparer.OrdinalIgnoreCase).Count();
            if (common == 0)
            {
                return 0;
            }

            // Chứa trọn tên đồng nghĩa ("Cao độ đáy ống" ⊃ "Cao độ đáy") giá trị hơn nhiều so với
            // chỉ trùng một token chung ("Chiều cao trần" vs "Chiều cao" — cũng chứa trọn, nên
            // Jaccard bên dưới kéo điểm xuống theo số token thừa).
            var jaccard = (double)common / a.Union(b, StringComparer.OrdinalIgnoreCase).Count();
            var covered = (double)common / b.Count;

            return Math.Min(0.95, 0.35 * jaccard + 0.55 * covered);
        }

        /// <summary>
        /// Với mỗi khoá logic: tìm trong mô hình xem đã có tên nào từ điển đang biết chưa (<see cref="SuggestionStatus.DaCo"/>),
        /// nếu chưa thì đề xuất tên thật giống nhất.
        /// </summary>
        public static List<DictionarySuggestion> Suggest(
            IEnumerable<string> keys,
            ParameterDictionary dictionary,
            IReadOnlyList<ParameterCandidate> candidates,
            double minConfidence = MinConfidence)
        {
            if (dictionary == null)
            {
                throw new ArgumentNullException(nameof(dictionary));
            }

            if (candidates == null || candidates.Count == 0)
            {
                throw new ArgumentException("Mô hình không đọc được tên tham số nào.", nameof(candidates));
            }

            var result = new List<DictionarySuggestion>();

            foreach (var key in keys.Where(k => !string.IsNullOrWhiteSpace(k)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var known = dictionary.NamesFor(key);

                // 1. Mô hình đã có sẵn một tên từ điển đang biết → không cần khai thêm gì.
                var exact = candidates.FirstOrDefault(c =>
                    known.Any(n => string.Equals(Normalize(n), Normalize(c.Name), StringComparison.OrdinalIgnoreCase)));
                if (exact != null)
                {
                    result.Add(new DictionarySuggestion(
                        key, exact.Name, 1.0,
                        $"mô hình đã có \"{exact.Name}\" ({exact.Category}, {exact.FilledCount}/{exact.TotalCount} phần tử có giá trị)",
                        SuggestionStatus.DaCo));
                    continue;
                }

                // 2. Chưa có → chấm điểm mọi tên thật với mọi tên đồng nghĩa của khoá.
                ParameterCandidate? best = null;
                var bestScore = 0.0;
                var bestVia = string.Empty;

                foreach (var candidate in candidates)
                {
                    foreach (var synonym in known)
                    {
                        var score = NameScore(candidate.Name, synonym);
                        if (score <= 0)
                        {
                            continue;
                        }

                        score = Adjust(score, key, candidate);
                        if (score > bestScore)
                        {
                            bestScore = score;
                            best = candidate;
                            bestVia = synonym;
                        }
                    }
                }

                if (best == null || bestScore < minConfidence)
                {
                    var gan = best == null ? string.Empty : $" (gần nhất: \"{best.Name}\" {NumericText.Format(bestScore, 2)})";
                    result.Add(new DictionarySuggestion(
                        key, null, bestScore,
                        "không có tham số nào trong mô hình đủ giống" + gan,
                        SuggestionStatus.KhongThay));
                    continue;
                }

                result.Add(new DictionarySuggestion(
                    key, best.Name, Math.Round(bestScore, 2),
                    $"giống \"{bestVia}\"; {best.Category}, kiểu {best.StorageType}, {best.FilledCount}/{best.TotalCount} phần tử có giá trị",
                    SuggestionStatus.DeXuat));
            }

            return result;
        }

        /// <summary>Hiệu chỉnh điểm theo dữ liệu thật: tham số rỗng toàn dự án và sai kiểu bị hạ điểm.</summary>
        private static double Adjust(double score, string key, ParameterCandidate candidate)
        {
            if (candidate.FillRatio <= 0)
            {
                // Tồn tại mà rỗng toàn dự án thì đọc ra cũng vô nghĩa — đúng lớp lỗi "không làm gì mà
                // vẫn báo thành công" mà từ điển sinh ra để chặn. Hạ mạnh, không loại hẳn: có khoá
                // (bottomElevation) là tham số DHCB chờ chính tool ghi vào.
                score *= 0.55;
            }
            else
            {
                score += 0.05 * Math.Min(1.0, candidate.FillRatio);
            }

            if (NumericKeys.Contains(key)
                && !string.Equals(candidate.StorageType, "Double", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(candidate.StorageType, "Integer", StringComparison.OrdinalIgnoreCase))
            {
                score *= 0.5;
            }

            return Math.Max(0, Math.Min(1, score));
        }

        /// <summary>CSV để duyệt trong Excel: <c>Key,Name,Status,Confidence,NeedsReview,Reason</c>.</summary>
        public static string ToCsv(IEnumerable<DictionarySuggestion> suggestions)
        {
            var sb = new StringBuilder("Key,Name,Status,Confidence,NeedsReview,Reason\n");
            foreach (var s in suggestions)
            {
                sb.Append(CsvText.JoinLine(new[]
                {
                    s.Key,
                    s.Name ?? string.Empty,
                    s.Status.ToString(),
                    NumericText.Format(s.Confidence, 2),
                    s.NeedsReview ? "true" : "false",
                    s.Reason,
                })).Append('\n');
            }

            return sb.ToString();
        }

        /// <summary>
        /// Trộn các đề xuất vào nội dung <c>dictionary.json</c> hiện có và trả về JSON mới.
        /// <para>
        /// Ràng buộc: <b>không bao giờ xoá thứ kỹ sư đã khai</b>. Tên mới được chèn lên ĐẦU danh sách của
        /// khoá (đúng thứ tự ưu tiên mà <see cref="ParameterDictionary.NamesFor"/> dùng), tên đã có giữ
        /// nguyên phía sau. Đề xuất trùng tên đã khai thì bỏ qua, không nhân bản.
        /// </para>
        /// </summary>
        /// <param name="existingJson">Nội dung file cũ; rỗng/null thì tạo file mới.</param>
        /// <param name="accepted">Chỉ những dòng <see cref="DictionarySuggestion.IsProposal"/>.</param>
        /// <returns>JSON đã trộn, thụt lề sẵn để mở ra đọc được.</returns>
        public static string Merge(string? existingJson, IEnumerable<DictionarySuggestion> accepted)
        {
            JObject root;
            try
            {
                root = string.IsNullOrWhiteSpace(existingJson) ? new JObject() : JObject.Parse(existingJson!);
            }
            catch (Exception ex)
            {
                // Ghi đè một file JSON hỏng là xoá mất công khai báo của kỹ sư — thà dừng và báo.
                throw new InvalidOperationException("File từ điển hiện có không phải JSON hợp lệ: " + ex.Message, ex);
            }

            if (!(root["parameters"] is JObject parameters))
            {
                parameters = new JObject();
                root["parameters"] = parameters;
            }

            foreach (var suggestion in accepted.Where(s => s.IsProposal))
            {
                var names = parameters[suggestion.Key] is JArray array
                    ? array.Select(v => v.ToString()).ToList()
                    : parameters[suggestion.Key] is JValue single && !string.IsNullOrWhiteSpace(single.ToString())
                        ? new List<string> { single.ToString() }
                        : new List<string>();

                if (names.Any(n => string.Equals(Normalize(n), Normalize(suggestion.Name!), StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                names.Insert(0, suggestion.Name!);
                parameters[suggestion.Key] = new JArray(names);
            }

            return root.ToString(Newtonsoft.Json.Formatting.Indented);
        }
    }
}
