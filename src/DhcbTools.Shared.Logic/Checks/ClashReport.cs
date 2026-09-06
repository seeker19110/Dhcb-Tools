using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using DhcbTools.Shared.Logic.Bcf;

namespace DhcbTools.Shared.Logic.Checks
{
    /// <summary>Một va chạm đã tìm thấy, đã dịch khỏi kiểu Revit: id, category, tâm (mm), khoá, link.</summary>
    public sealed class ClashRecord
    {
        public ClashRecord(long idA, string? categoryA, string? nameA, long idB, string? categoryB,
            double xMm, double yMm, double zMm, string key, string? linkName)
        {
            IdA = idA;
            CategoryA = categoryA ?? string.Empty;
            NameA = nameA ?? string.Empty;
            IdB = idB;
            CategoryB = categoryB ?? string.Empty;
            XMm = xMm;
            YMm = yMm;
            ZMm = zMm;
            Key = key ?? throw new ArgumentNullException(nameof(key));
            LinkName = linkName;
        }

        public long IdA { get; }

        public string CategoryA { get; }

        public string NameA { get; }

        public long IdB { get; }

        public string CategoryB { get; }

        public double XMm { get; }

        public double YMm { get; }

        public double ZMm { get; }

        /// <summary>Khoá dùng cho clash-accepted.json và GUID BCF.</summary>
        public string Key { get; }

        /// <summary>Tên link chứa phần tử B; null = cùng file.</summary>
        public string? LinkName { get; }

        public bool FromLink => LinkName != null;

        /// <summary>"1234" hoặc "1234 (link \"ARC\")".</summary>
        public string DescribeB() => LinkName == null ? IdB.ToString() : $"{IdB} (link \"{LinkName}\")";
    }

    /// <summary>
    /// Phần quyết định và phần dựng báo cáo của <c>ClashDetection</c>, tách khỏi Revit.
    /// <para>
    /// Vì sao: "0 va chạm" là kết luận người ta tin và làm theo — mọi câu giải thích vì sao ra 0, khoá
    /// chấp nhận, HTML và BCF trước đây nằm trong Core, chỉ kiểm được khi mở Revit. Ở đây nhận
    /// <see cref="ClashRecord"/> và số, trả chuỗi; Core chỉ còn quét hình học.
    /// </para>
    /// </summary>
    public static class ClashReport
    {
        /// <summary>Số va chạm tối đa liệt kê trong Messages.</summary>
        public const int MaxListed = 500;

        // ── Hình học & khoá ─────────────────────────────────────────────────────

        /// <summary>Tâm phần giao của hai hộp bao (cùng đơn vị với đầu vào). Không kiểm giao — người gọi đã lọc thô.</summary>
        public static (double X, double Y, double Z) IntersectionCentre(
            double aMinX, double aMinY, double aMinZ, double aMaxX, double aMaxY, double aMaxZ,
            double bMinX, double bMinY, double bMinZ, double bMaxX, double bMaxY, double bMaxZ)
        {
            return (
                (Math.Max(aMinX, bMinX) + Math.Min(aMaxX, bMaxX)) / 2,
                (Math.Max(aMinY, bMinY) + Math.Min(aMaxY, bMaxY)) / 2,
                (Math.Max(aMinZ, bMinZ) + Math.Min(aMaxZ, bMaxZ)) / 2);
        }

        /// <summary>
        /// Khoá va chạm. ElementId của link là id TRONG document link, có thể trùng với id ở file chủ hoặc
        /// link khác → khoá phải mang thêm id của link instance, nếu không hai va chạm khác nhau gộp làm một.
        /// </summary>
        public static string MakeKey(long idA, long idB, double xMm, double yMm, double zMm, long? linkInstanceId)
        {
            var key = ClashAcceptance.MakeKey(idA, idB, xMm, yMm, zMm);
            return linkInstanceId.HasValue ? key + "#link" + linkInstanceId.Value : key;
        }

        /// <summary>Tên link có khớp bộ lọc <c>linkNameContains</c> không (rỗng = mọi link).</summary>
        public static bool LinkNameMatches(string? linkName, IReadOnlyList<string>? filter)
            => Mep.SleevePlanner.LinkNameMatches(linkName, filter);

        // ── Thông báo ───────────────────────────────────────────────────────────

        public static string UnknownCategoriesError(IEnumerable<string> unknown)
            => "Một trong hai nhóm category không có trong mô hình: " + string.Join(", ", unknown ?? Array.Empty<string>());

        public static string MaxResultsNote(int maxResults) => $"Đạt giới hạn {maxResults} va chạm — dừng quét.";

        public static string ClashLine(ClashRecord c)
            => $"Va chạm {c.IdA} ({c.CategoryA}) × {c.DescribeB()} ({c.CategoryB}) tại ({c.XMm:F0},{c.YMm:F0},{c.ZMm:F0}) mm  key={c.Key}";

        /// <summary>Tối đa <see cref="MaxListed"/> dòng va chạm, rồi nguồn nhóm B và từng link.</summary>
        public static List<string> Notes(IReadOnlyList<ClashRecord> clashes, int inDocument, int inLinks, IReadOnlyList<string>? linkSummary)
        {
            var notes = (clashes ?? Array.Empty<ClashRecord>()).Take(MaxListed).Select(ClashLine).ToList();
            notes.Add($"Nhóm B xét tới: {inDocument} phần tử trong file, {inLinks} từ model liên kết.");
            if (linkSummary != null)
            {
                notes.AddRange(linkSummary.Select(l => "  Link — " + l));
            }

            return notes;
        }

        /// <summary>
        /// Summary. "0 va chạm" phải kèm cơ sở: xét bao nhiêu phần tử, từ đâu — bản trước chỉ có con số 0
        /// trơ trọi, và trên file MEP link kết cấu thì con số đó luôn là 0.
        /// </summary>
        public static string Summary(
            IReadOnlyList<ClashRecord> clashes, int skippedAccepted, string outputPath,
            int countA, int inDocument, int inLinks, bool includeLinkedModels)
        {
            clashes ??= Array.Empty<ClashRecord>();
            var summary = $"Tìm thấy {clashes.Count} va chạm ({skippedAccepted} đã chấp nhận, bỏ qua) → \"{outputPath}\".";
            if (clashes.Count == 0)
            {
                summary += inDocument + inLinks == 0
                    ? (includeLinkedModels
                        ? " Không có phần tử nhóm B nào để xét, kể cả trong model liên kết — kiểm lại link đã nạp chưa."
                        : " Không có phần tử nhóm B nào trong file này và includeLinkedModels đang tắt — bật lên nếu nhóm B nằm ở model liên kết.")
                    : $" Đã xét {countA} × ({inDocument} trong file + {inLinks} từ model liên kết).";
            }
            else if (inLinks > 0)
            {
                summary += $" Trong đó {clashes.Count(c => c.FromLink)} va chạm với model liên kết.";
            }

            return summary;
        }

        /// <summary>Dòng xem trước / đã tạo 3D view. Phần tử phía link không isolate được (khác document).</summary>
        public static string ViewNote(bool preview, string viewName, int hostIds, int fromLinks)
        {
            var tail = fromLinks > 0
                ? (preview ? $" ({fromLinks} phần tử phía link không isolate được, xem danh sách)." : $"; {fromLinks} phần tử phía link không isolate được (khác document).")
                : ".";
            return preview
                ? $"[Xem trước] Sẽ tạo/ghi đè 3D view \"{viewName}\" và isolate {hostIds} phần tử phía file chủ{tail}"
                : $"Đã tạo 3D view \"{viewName}\" isolate {hostIds} phần tử phía file chủ{tail}";
        }

        // ── HTML & BCF ──────────────────────────────────────────────────────────

        public static string Html(string? documentTitle, IEnumerable<string> categoriesA, IEnumerable<string> categoriesB,
            IReadOnlyList<ClashRecord> clashes, int skipped)
        {
            clashes ??= Array.Empty<ClashRecord>();
            var sb = new StringBuilder();
            sb.Append("<!DOCTYPE html><html lang=\"vi\"><head><meta charset=\"utf-8\"><title>DHCB - Va chạm</title>")
              .Append("<style>body{font-family:Segoe UI,Arial,sans-serif;margin:24px}table{border-collapse:collapse}td,th{border:1px solid #ccc;padding:4px 8px}th{background:#f3f3f3}code{font-size:11px}</style></head><body>")
              .Append("<h1>Va chạm nội bộ — ").Append(HtmlText.Escape(documentTitle ?? string.Empty)).Append("</h1>")
              .Append("<p>").Append(HtmlText.Escape(string.Join(", ", categoriesA ?? Array.Empty<string>()))).Append(" × ")
              .Append(HtmlText.Escape(string.Join(", ", categoriesB ?? Array.Empty<string>())))
              .Append(": <b>").Append(clashes.Count).Append("</b> va chạm; ").Append(skipped).Append(" đã chấp nhận (clash-accepted.json).</p>")
              .Append("<p>Để chấp nhận một va chạm: thêm <code>{\"key\":\"…\",\"note\":\"…\"}</code> vào file accepted với key ở cột cuối.</p>")
              .Append("<table><thead><tr><th>#</th><th>A</th><th>Category A</th><th>B</th><th>Category B</th><th>X</th><th>Y</th><th>Z (mm)</th><th>Key</th></tr></thead><tbody>");
            var i = 1;
            foreach (var c in clashes)
            {
                sb.Append("<tr><td>").Append(i++).Append("</td><td>").Append(c.IdA).Append("</td><td>").Append(HtmlText.Escape(c.CategoryA))
                  .Append("</td><td>").Append(c.IdB).Append("</td><td>").Append(HtmlText.Escape(c.CategoryB))
                  .Append("</td><td>").Append(c.XMm.ToString("F0")).Append("</td><td>").Append(c.YMm.ToString("F0")).Append("</td><td>").Append(c.ZMm.ToString("F0"))
                  .Append("</td><td><code>").Append(HtmlText.Escape(c.Key)).Append("</code></td></tr>");
            }

            sb.Append("</tbody></table></body></html>");
            return sb.ToString();
        }

        /// <summary>
        /// Vấn đề BCF 2.1. Toạ độ BCF là **mét** — quy đổi từ mm ở đúng một chỗ này. GUID topic sinh từ
        /// chính <c>key</c> nên xuất lại cùng va chạm vẫn ra đúng vấn đề cũ trong phần mềm điều phối.
        /// </summary>
        public static List<BcfIssue> BcfIssues(string? documentTitle, IReadOnlyList<ClashRecord> clashes)
        {
            var title = documentTitle ?? string.Empty;
            var issues = new List<BcfIssue>();
            foreach (var c in clashes ?? Array.Empty<ClashRecord>())
            {
                var issue = new BcfIssue(BcfWriter.GuidFromKey(c.Key), $"{c.CategoryA} × {c.CategoryB} — {c.NameA}")
                {
                    Description = $"Va chạm {c.IdA} ({c.CategoryA}) × {c.DescribeB()} ({c.CategoryB})"
                                  + $" tại ({c.XMm:F0}, {c.YMm:F0}, {c.ZMm:F0}) mm. key={c.Key}",
                    Target = new BcfPoint(c.XMm / 1000.0, c.YMm / 1000.0, c.ZMm / 1000.0),
                    Author = "DHCB Tools",
                };
                issue.Labels.Add(c.FromLink ? "Với model liên kết" : "Trong file");
                issue.Components.Add(new BcfComponent(c.IdA.ToString(), null, title));
                issue.Components.Add(new BcfComponent(c.IdB.ToString(), null, c.LinkName ?? title));
                issues.Add(issue);
            }

            return issues;
        }

        /// <summary>Tên dự án ghi vào project.bcfp: theo config, rỗng thì lấy tên file.</summary>
        public static string BcfProjectName(string? configured, string? documentTitle)
            => StringGuard.IsBlank(configured) ? (documentTitle ?? string.Empty) : configured!;
    }
}
