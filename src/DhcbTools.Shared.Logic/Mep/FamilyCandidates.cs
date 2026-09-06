using System;
using System.Collections.Generic;
using System.Linq;

namespace DhcbTools.Shared.Logic.Mep
{
    /// <summary>Một FamilySymbol của mô hình, đủ để gợi ý: tên family, tên type, category, số instance.</summary>
    public sealed class FamilySymbolInfo
    {
        public FamilySymbolInfo(string family, string type, string category, int instances)
        {
            Family = family;
            Type = type;
            Category = category;
            Instances = instances;
        }

        public string Family { get; }

        public string Type { get; }

        public string Category { get; }

        public int Instances { get; }
    }

    /// <summary>
    /// Khi <c>SleeveAuto</c>/<c>HangerAuto</c> không tìm thấy family kỹ sư khai, lỗi phải nói mô hình CÓ family gì —
    /// vòng đóng vai §57: người dùng phải chạy <c>FamilyAudit</c> riêng để tra tên, hai bước cho một việc. Xếp ứng viên:
    /// tên chứa từ khoá của tên đã khai trước, rồi theo số instance giảm dần; category ưu tiên khi được chỉ.
    /// </summary>
    public static class FamilyCandidates
    {
        public const int MaxListed = 8;

        public static string NotFoundMessage(string wanted, IReadOnlyList<FamilySymbolInfo> symbols, IReadOnlyList<string>? preferCategories = null)
        {
            var head = $"Không tìm thấy FamilySymbol \"{wanted}\" trong mô hình.";
            if (symbols.Count == 0)
            {
                return head + " Mô hình không có family nạp được nào (FamilySymbol).";
            }

            var ranked = Rank(wanted, symbols, preferCategories ?? Array.Empty<string>()).Take(MaxListed)
                .Select(s => $"{s.Family}: {s.Type} ({s.Category}, {s.Instances} instance)");
            var more = symbols.Count > MaxListed ? $" … và {symbols.Count - MaxListed} type nữa (FamilyAudit liệt kê đủ)" : string.Empty;
            return head + $" Family có trong mô hình ({symbols.Count} type), gần nhất trước: {string.Join("; ", ranked)}{more}. Khai tên family hoặc \"Family: Type\".";
        }

        /// <summary>Xếp hạng: trùng từ khoá của tên khai (điểm cao nhất), category ưu tiên, rồi số instance.</summary>
        public static IEnumerable<FamilySymbolInfo> Rank(string wanted, IReadOnlyList<FamilySymbolInfo> symbols, IReadOnlyList<string> preferCategories)
        {
            var tokens = Tokens(wanted);
            return symbols
                .Select(s => (Symbol: s, Score: Score(s, tokens, preferCategories)))
                .OrderByDescending(t => t.Score)
                .ThenByDescending(t => t.Symbol.Instances)
                .ThenBy(t => t.Symbol.Family, StringComparer.OrdinalIgnoreCase)
                .ThenBy(t => t.Symbol.Type, StringComparer.OrdinalIgnoreCase)
                .Select(t => t.Symbol);
        }

        private static int Score(FamilySymbolInfo s, IReadOnlyList<string> tokens, IReadOnlyList<string> preferCategories)
        {
            var name = (s.Family + " " + s.Type).ToLowerInvariant();
            var hits = tokens.Count(t => name.Contains(t));
            var cat = preferCategories.Any(c => string.Equals(c, s.Category, StringComparison.OrdinalIgnoreCase)) ? 1 : 0;
            // Fitting (cút, tê) nhiều instance nhất mô hình nhưng không bao giờ là sleeve/hanger — đẩy xuống cuối
            // (Snowdon HVAC: "Round Elbow" 738 instance chiếm đầu danh sách, §58).
            var fitting = s.Category.IndexOf("Fitting", StringComparison.OrdinalIgnoreCase) >= 0 ? -5 : 0;
            // Họ chú thích (khung tên, tag, nhãn, ký hiệu) là FamilySymbol nhưng không đặt được vào mô hình.
            var annotation = IsAnnotationCategory(s.Category) ? -5 : 0;
            return hits * 10 + cat + fitting + annotation;
        }

        private static readonly string[] AnnotationHints = { "Annotation", "Title Block", "Tag", "Label", "Callout", "Symbol", "Legend" };

        public static bool IsAnnotationCategory(string category)
        {
            return AnnotationHints.Any(h => category.IndexOf(h, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static List<string> Tokens(string wanted)
        {
            return (wanted ?? string.Empty).ToLowerInvariant()
                .Split(new[] { ' ', '_', '-', ':', '(', ')', '.', ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(t => t.Length >= 3)
                .Distinct()
                .ToList();
        }
    }
}
