using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace DhcbTools.Shared.Logic.Ai
{
    /// <summary>Một dòng đề xuất map layer CAD → Revit type (mục 5.1).</summary>
    public sealed class LayerMapping
    {
        public LayerMapping(string layer, string? revitType, double confidence, string reason)
        {
            Layer = layer;
            RevitType = revitType;
            Confidence = confidence;
            Reason = reason;
        }

        public string Layer { get; }

        /// <summary>Luôn là một type CÓ THẬT trong danh sách đã cho, hoặc null nếu không tìm được.</summary>
        public string? RevitType { get; }

        /// <summary>0–1. Dưới <see cref="LayerMappingSuggester.ReviewThreshold"/> → đánh dấu để kỹ sư xem.</summary>
        public double Confidence { get; }

        public string Reason { get; }

        public bool NeedsReview => RevitType == null || Confidence < LayerMappingSuggester.ReviewThreshold;
    }

    /// <summary>
    /// Gợi ý map layer CAD → Revit type HOÀN TOÀN OFFLINE bằng heuristic: tách token (A-WALL-200 → a, wall, 200),
    /// từ điển đồng nghĩa Việt/Anh/AIA layer standard (wall≈tường≈tuong≈W), khớp kích thước (200 ↔ "200"),
    /// điểm Jaccard + bonus. Có thể tinh chỉnh bằng model local (Ollama) qua <see cref="OllamaClient"/> — nhưng
    /// kết quả của model vẫn bị lọc qua <see cref="Validate"/> để chỉ nhận type có thật (ràng buộc mục 5.1).
    /// </summary>
    public static class LayerMappingSuggester
    {
        public const double ReviewThreshold = 0.7;

        private static readonly Dictionary<string, string[]> Synonyms = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["wall"] = new[] { "wall", "tuong", "tường", "w", "vach", "vách", "walls" },
            ["door"] = new[] { "door", "cua", "cửa", "d", "doors" },
            ["window"] = new[] { "window", "cuaso", "cửa sổ", "cs", "win", "windows", "glaz" },
            ["column"] = new[] { "column", "col", "cot", "cột", "c", "cols", "columns" },
            ["beam"] = new[] { "beam", "dam", "dầm", "b", "beams", "girder" },
            ["slab"] = new[] { "slab", "san", "sàn", "floor", "flor", "s", "floors" },
            ["stair"] = new[] { "stair", "thang", "cauthang", "cầu thang", "stairs", "strs" },
            ["roof"] = new[] { "roof", "mai", "mái" },
            ["ceiling"] = new[] { "ceiling", "clng", "tran", "trần" },
            ["duct"] = new[] { "duct", "onggio", "ống gió", "hvac", "sa", "ra", "ea", "ducts" },
            ["pipe"] = new[] { "pipe", "ong", "ống", "pipes", "cw", "hw", "san", "plumb", "plbg" },
            ["cabletray"] = new[] { "tray", "cabletray", "mangcap", "máng cáp", "ct", "trays" },
            ["conduit"] = new[] { "conduit", "ongluon", "ống luồn", "cndt" },
            ["sprinkler"] = new[] { "sprinkler", "spk", "fp", "pccc", "chuachay", "chữa cháy", "sprk" },
            ["light"] = new[] { "light", "den", "đèn", "ltg", "lighting", "lite" },
            ["furniture"] = new[] { "furniture", "furn", "noithat", "nội thất", "fixt" },
            ["grid"] = new[] { "grid", "axis", "truc", "trục", "grids", "cols-grid" },
            ["level"] = new[] { "level", "tang", "tầng", "elev" },
            ["text"] = new[] { "text", "anno", "annot", "dim", "note", "chu", "chữ", "kichthuoc", "kích thước" },
        };

        private static readonly Regex TokenSplit = new Regex(@"[\s\-_\.\|/:,]+", RegexOptions.Compiled);

        private static readonly Regex NumberToken = new Regex(@"^\d{2,4}$", RegexOptions.Compiled);

        /// <summary>Tách chuỗi thành token thường, bỏ dấu tiếng Việt, kèm token "canonical" từ từ điển đồng nghĩa.</summary>
        public static HashSet<string> Tokenize(string text)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(text))
            {
                return result;
            }

            foreach (var raw in TokenSplit.Split(text.ToLowerInvariant()))
            {
                if (raw.Length == 0)
                {
                    continue;
                }

                var t = RemoveDiacritics(raw);
                result.Add(t);

                // "Basic Wall" → cũng thêm cụm ghép "basicwall" để bắt kiểu viết liền.
                foreach (var kv in Synonyms)
                {
                    if (kv.Value.Any(s => RemoveDiacritics(s).Equals(t, StringComparison.OrdinalIgnoreCase)))
                    {
                        result.Add("#" + kv.Key);
                    }
                }
            }

            return result;
        }

        /// <summary>Điểm khớp 0–1 giữa một layer và một type.</summary>
        public static double Score(string layer, string revitType, out string reason)
        {
            var a = Tokenize(layer);
            var b = Tokenize(revitType);

            var canonA = a.Where(t => t.StartsWith("#", StringComparison.Ordinal)).ToList();
            var canonB = b.Where(t => t.StartsWith("#", StringComparison.Ordinal)).ToList();
            var canonHit = canonA.Intersect(canonB, StringComparer.OrdinalIgnoreCase).ToList();

            var numsA = a.Where(t => NumberToken.IsMatch(t)).ToList();
            var numsB = b.Where(t => NumberToken.IsMatch(t)).ToList();
            var numHit = numsA.Intersect(numsB).ToList();

            var plainA = a.Where(t => !t.StartsWith("#", StringComparison.Ordinal) && t.Length > 1).ToList();
            var plainB = b.Where(t => !t.StartsWith("#", StringComparison.Ordinal) && t.Length > 1).ToList();
            var union = plainA.Union(plainB, StringComparer.OrdinalIgnoreCase).Count();
            var jaccard = union == 0 ? 0 : (double)plainA.Intersect(plainB, StringComparer.OrdinalIgnoreCase).Count() / union;

            var score = 0.0;
            var parts = new List<string>();
            if (canonHit.Count > 0)
            {
                score += 0.55;
                parts.Add("cùng loại " + string.Join("/", canonHit.Select(c => c.TrimStart('#'))));
            }
            else if (canonA.Count > 0 && canonB.Count > 0)
            {
                // Hai bên đều có loại rõ ràng nhưng khác nhau → gần như chắc chắn sai.
                score -= 0.5;
                parts.Add("khác loại");
            }

            if (numHit.Count > 0)
            {
                score += 0.3;
                parts.Add("khớp kích thước " + string.Join("/", numHit));
            }
            else if (numsA.Count > 0 && numsB.Count > 0)
            {
                score -= 0.15;
                parts.Add("kích thước khác (" + string.Join("/", numsA) + " vs " + string.Join("/", numsB) + ")");
            }

            score += 0.25 * jaccard;
            if (jaccard > 0)
            {
                parts.Add("từ chung " + NumericText.Format(jaccard, 2));
            }

            score = Math.Max(0, Math.Min(1, score));
            reason = parts.Count == 0 ? "không có dấu hiệu chung" : string.Join(", ", parts);
            return score;
        }

        /// <summary>Gợi ý cho từng layer: type tốt nhất trong danh sách (chỉ type có thật), kèm độ tin cậy.</summary>
        public static List<LayerMapping> Suggest(IEnumerable<string> layers, IReadOnlyList<string> revitTypes, double minConfidence = 0.3)
        {
            if (revitTypes == null || revitTypes.Count == 0)
            {
                throw new ArgumentException("Danh sách Revit type rỗng.", nameof(revitTypes));
            }

            var result = new List<LayerMapping>();
            foreach (var layer in layers.Where(l => !string.IsNullOrWhiteSpace(l)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                string? bestType = null;
                var best = -1.0;
                var bestReason = string.Empty;
                foreach (var type in revitTypes)
                {
                    var s = Score(layer, type, out var reason);
                    if (s > best)
                    {
                        best = s;
                        bestType = type;
                        bestReason = reason;
                    }
                }

                if (best < minConfidence)
                {
                    result.Add(new LayerMapping(layer, null, best, "không có type nào đủ giống (tốt nhất: " + bestType + " — " + bestReason + ")"));
                }
                else
                {
                    result.Add(new LayerMapping(layer, bestType, Math.Round(best, 2), bestReason));
                }
            }

            return result;
        }

        /// <summary>
        /// Lọc kết quả từ model ngôn ngữ: chỉ giữ dòng có <c>revitType</c> nằm trong danh sách thật; dòng bịa ra
        /// bị loại và ghi vào <paramref name="rejected"/>. Đây là ràng buộc bắt buộc của mục 5.1.
        /// </summary>
        public static List<LayerMapping> Validate(IEnumerable<LayerMapping> proposed, IReadOnlyList<string> revitTypes, List<string> rejected)
        {
            var known = new HashSet<string>(revitTypes, StringComparer.OrdinalIgnoreCase);
            var ok = new List<LayerMapping>();
            foreach (var m in proposed)
            {
                if (m.RevitType != null && known.Contains(m.RevitType))
                {
                    var exact = revitTypes.First(t => string.Equals(t, m.RevitType, StringComparison.OrdinalIgnoreCase));
                    ok.Add(new LayerMapping(m.Layer, exact, Math.Max(0, Math.Min(1, m.Confidence)), m.Reason));
                }
                else
                {
                    rejected.Add(m.Layer + " → \"" + m.RevitType + "\" (type không tồn tại — loại)");
                }
            }
            return ok;
        }

        /// <summary>CSV để duyệt trong Excel: <c>Layer,RevitType,Confidence,NeedsReview,Reason</c>.</summary>
        public static string ToCsv(IEnumerable<LayerMapping> mappings)
        {
            var sb = new StringBuilder("Layer,RevitType,Confidence,NeedsReview,Reason\n");
            foreach (var m in mappings)
            {
                sb.Append(CsvText.JoinLine(new[]
                {
                    m.Layer, m.RevitType ?? string.Empty, NumericText.Format(m.Confidence, 2), m.NeedsReview ? "true" : "false", m.Reason,
                })).Append('\n');
            }
            return sb.ToString();
        }

        public static string RemoveDiacritics(string text)
        {
            var normalized = text.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(normalized.Length);
            foreach (var c in normalized)
            {
                var cat = CharUnicodeInfo.GetUnicodeCategory(c);
                if (cat != UnicodeCategory.NonSpacingMark)
                {
                    sb.Append(c == 'đ' ? 'd' : c == 'Đ' ? 'D' : c);
                }
            }
            return sb.ToString().Normalize(NormalizationForm.FormC);
        }
    }
}
