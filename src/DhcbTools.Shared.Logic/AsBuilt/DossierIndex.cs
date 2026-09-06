using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;

namespace DhcbTools.Shared.Logic.AsBuilt
{
    /// <summary>Một mục trong danh mục hồ sơ: một loại tài liệu phải nộp.</summary>
    public sealed class DossierItem
    {
        /// <summary>Số hiệu mục trong danh mục, ví dụ <c>I.1</c>. Hiện nguyên văn trong báo cáo.</summary>
        [JsonProperty("code")]
        public string Code { get; set; } = string.Empty;

        /// <summary>Tên tài liệu.</summary>
        [JsonProperty("name")]
        public string Name { get; set; } = string.Empty;

        /// <summary>Thiếu mục này thì hồ sơ chưa nộp được (mặc định) — quyết định mã thoát.</summary>
        [JsonProperty("required")]
        public bool Required { get; set; } = true;

        /// <summary>
        /// Mẫu tên file nhận cho mục này (<c>*</c> và <c>?</c>), so trên đường dẫn tương đối, không phân
        /// biệt hoa thường. Rỗng = không mẫu nào, mục luôn bị coi là thiếu — cố ý, xem <see cref="DossierIndex"/>.
        /// </summary>
        [JsonProperty("patterns")]
        public List<string> Patterns { get; set; } = new List<string>();

        /// <summary>Ghi chú cho người lập hồ sơ (ai ký, bản gốc hay bản sao…).</summary>
        [JsonProperty("note")]
        public string Note { get; set; } = string.Empty;
    }

    /// <summary>Một nhóm của danh mục (Phụ lục VII chia ba nhóm).</summary>
    public sealed class DossierGroup
    {
        [JsonProperty("code")]
        public string Code { get; set; } = string.Empty;

        [JsonProperty("name")]
        public string Name { get; set; } = string.Empty;

        [JsonProperty("items")]
        public List<DossierItem> Items { get; set; } = new List<DossierItem>();
    }

    /// <summary>
    /// Danh mục hồ sơ hoàn thành công trình, đọc từ file JSON của dự án.
    /// <para>
    /// Vì sao danh mục nằm ở <b>file cấu hình</b> chứ không viết cứng trong mã: nội dung Phụ lục VII là văn
    /// bản pháp luật — nó đổi theo nghị định, và mỗi dự án còn thêm/bớt theo hợp đồng. Viết cứng thì mã nguồn
    /// thành nơi phát biểu về luật, mà không ai sửa được khi luật đổi. DHCB cung cấp <b>mẫu</b>
    /// (<c>configs/ho-so-hoan-cong.sample.json</c>) với ba nhóm theo Điều 28 + Phụ lục VII NĐ 207/2026;
    /// <b>từng dòng mục phải do doanh nghiệp điền theo bản gốc</b> và chịu trách nhiệm — cùng ranh giới đã
    /// đặt cho family dấu hoàn công ở mục 11.6.
    /// </para>
    /// </summary>
    public sealed class DossierSpec
    {
        /// <summary>Tiêu đề in trên báo cáo.</summary>
        [JsonProperty("title")]
        public string Title { get; set; } = "Danh mục hồ sơ hoàn thành công trình";

        /// <summary>Căn cứ pháp lý, in nguyên văn — DHCB không diễn giải hộ.</summary>
        [JsonProperty("legalBasis")]
        public string LegalBasis { get; set; } = string.Empty;

        [JsonProperty("project")]
        public string Project { get; set; } = string.Empty;

        [JsonProperty("groups")]
        public List<DossierGroup> Groups { get; set; } = new List<DossierGroup>();

        /// <summary>Đọc file danh mục. Ném <see cref="ArgumentException"/> khi file không dùng được.</summary>
        public static DossierSpec FromJson(string json)
        {
            DossierSpec? spec;
            try
            {
                spec = JsonConvert.DeserializeObject<DossierSpec>(json ?? string.Empty);
            }
            catch (JsonException ex)
            {
                throw new ArgumentException("File danh mục không phải JSON hợp lệ: " + ex.Message, ex);
            }

            if (spec == null)
            {
                throw new ArgumentException("File danh mục rỗng hoặc không phải JSON object.");
            }

            // Danh mục không có mục nào thì mọi thư mục đều "đủ hồ sơ" — đúng loại no-op im lặng mà
            // E-PRECOND sinh ra để chặn, nên từ chối ngay thay vì in ra một tờ giấy trắng.
            if (spec.Groups.Sum(g => g.Items.Count) == 0)
            {
                throw new ArgumentException("File danh mục không có mục nào — không có gì để đối chiếu.");
            }

            return spec;
        }
    }

    /// <summary>Kết quả đối chiếu một mục với thư mục hồ sơ.</summary>
    public sealed class DossierItemResult
    {
        internal DossierItemResult(string groupCode, string groupName, DossierItem item, IReadOnlyList<string> files)
        {
            GroupCode = groupCode;
            GroupName = groupName;
            Item = item;
            Files = files;
        }

        public string GroupCode { get; }

        public string GroupName { get; }

        public DossierItem Item { get; }

        /// <summary>File tìm được cho mục này (đường dẫn tương đối), đã sắp xếp.</summary>
        public IReadOnlyList<string> Files { get; }

        /// <summary>Có file = mục đã có hồ sơ.</summary>
        public bool Found => Files.Count > 0;

        /// <summary>Bắt buộc mà không có file — thứ làm hồ sơ chưa nộp được.</summary>
        public bool MissingRequired => Item.Required && !Found;
    }

    /// <summary>Kết quả đối chiếu cả danh mục.</summary>
    public sealed class DossierResult
    {
        internal DossierResult(IReadOnlyList<DossierItemResult> items, IReadOnlyList<string> unmatched)
        {
            Items = items;
            Unmatched = unmatched;
        }

        public IReadOnlyList<DossierItemResult> Items { get; }

        /// <summary>
        /// File có trong thư mục nhưng không mục nào nhận. Không phải lỗi — nhưng phải NÓI RA: hồ sơ thật
        /// hay có file thừa (bản nháp, bản trùng), và người ký cần biết mình đang ký lên cái gì.
        /// </summary>
        public IReadOnlyList<string> Unmatched { get; }

        public int Found => Items.Count(i => i.Found);

        public int MissingRequired => Items.Count(i => i.MissingRequired);

        /// <summary>Không thiếu mục bắt buộc nào.</summary>
        public bool Ok => MissingRequired == 0;
    }

    /// <summary>
    /// Mục 11.6 — đối chiếu <b>danh mục hồ sơ</b> với <b>file thật trong thư mục</b> và báo thiếu mục nào.
    /// <para>
    /// Thuần tuyệt đối: nhận sẵn danh sách đường dẫn tương đối, không tự đọc đĩa. Nhờ vậy mọi luật khớp
    /// tên chạy được trên CI, còn phần quét thư mục nằm ở BatchRunner nơi có bộ test CLI riêng (§47).
    /// </para>
    /// </summary>
    public static class DossierIndex
    {
        /// <summary>Đối chiếu danh mục với danh sách file (đường dẫn tương đối, dùng <c>/</c>).</summary>
        public static DossierResult Check(DossierSpec spec, IEnumerable<string> relativeFiles)
        {
            if (spec == null)
            {
                throw new ArgumentNullException(nameof(spec));
            }

            var files = (relativeFiles ?? Enumerable.Empty<string>())
                .Where(f => !string.IsNullOrWhiteSpace(f))
                .Select(f => f.Replace('\\', '/').TrimStart('/'))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var results = new List<DossierItemResult>();
            var matched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var group in spec.Groups)
            {
                foreach (var item in group.Items)
                {
                    var hit = files.Where(f => item.Patterns.Any(p => Matches(f, p))).ToList();
                    foreach (var file in hit)
                    {
                        matched.Add(file);
                    }

                    results.Add(new DossierItemResult(group.Code, group.Name, item, hit));
                }
            }

            return new DossierResult(results, files.Where(f => !matched.Contains(f)).ToList());
        }

        /// <summary>
        /// Khớp mẫu kiểu shell (<c>*</c>, <c>?</c>), không phân biệt hoa thường và <b>bỏ dấu tiếng Việt</b>.
        /// Bỏ dấu vì hồ sơ thật đặt tên cả hai kiểu ("bien-ban-nghiem-thu.pdf" và "biên bản nghiệm thu.pdf")
        /// — bắt người khai hai mẫu cho một mục là cách chắc chắn để họ khai thiếu một cái.
        /// <para>
        /// So trên <b>mọi phần đuôi</b> của đường dẫn tính từ một dấu <c>/</c>, chứ không chỉ cả đường dẫn:
        /// mẫu <c>nghiem-thu/*</c> phải bắt được cả <c>nghiem-thu/bb01.pdf</c> lẫn
        /// <c>ho-so/2026/nghiem-thu/bb01.pdf</c>. Người khai danh mục nghĩ theo "thư mục nghiệm thu", không
        /// theo chỗ ai đó đặt thư mục đó nằm sâu mấy tầng.
        /// </para>
        /// </summary>
        internal static bool Matches(string relativePath, string pattern)
        {
            if (string.IsNullOrWhiteSpace(pattern))
            {
                return false;
            }

            var regex = new Regex(
                "^" + string.Join(".*", Regex.Split(Normalize(pattern), @"\*").Select(Regex.Escape)).Replace(@"\?", ".") + "$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

            var path = Normalize(relativePath);
            for (var i = 0; i <= path.Length; i++)
            {
                if ((i == 0 || path[i - 1] == '/') && regex.IsMatch(path.Substring(i)))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Bỏ dấu, đổi dấu gạch ngược thành <c>/</c>, và gom mọi dấu ngăn cách tên (khoảng trắng,
        /// gạch dưới, gạch ngang) về một dấu gạch ngang.
        /// <para>
        /// Gom dấu ngăn cách vì hồ sơ thật đặt tên đủ kiểu: "khao-sat", "khao_sat", và
        /// "Báo cáo khảo sát địa chất.pdf". Bản đầu chỉ bỏ dấu nên mẫu <c>*khao-sat*</c> trượt file có
        /// khoảng trắng — lộ ngay lượt chạy thử đầu tiên trên thư mục hồ sơ dựng tay (§48).
        /// </para>
        /// </summary>
        private static string Normalize(string text) =>
            SeparatorRuns.Replace(
                Ai.LayerMappingSuggester.RemoveDiacritics(text ?? string.Empty).Replace('\\', '/'),
                "-");

        private static readonly Regex SeparatorRuns =
            new Regex(@"[\s_-]+", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        /// <summary>Dòng tổng kết dùng cho console và cho summary lệnh.</summary>
        public static string Summary(DossierResult result) =>
            $"Danh mục {result.Items.Count} mục: {result.Found} mục có hồ sơ, "
            + $"{result.MissingRequired} mục bắt buộc còn THIẾU"
            + (result.Unmatched.Count > 0 ? $", {result.Unmatched.Count} file chưa xếp vào mục nào" : string.Empty)
            + ".";

        /// <summary>CSV để lọc trong Excel: một dòng mỗi mục.</summary>
        public static string Csv(DossierResult result)
        {
            var sb = new StringBuilder();
            sb.Append(CsvText.JoinLine(new[] { "Nhóm", "Mục", "Tên tài liệu", "Bắt buộc", "Trạng thái", "Số file", "File", "Ghi chú" }))
              .Append("\r\n");
            foreach (var item in result.Items)
            {
                sb.Append(CsvText.JoinLine(new[]
                {
                    item.GroupCode,
                    item.Item.Code,
                    item.Item.Name,
                    item.Item.Required ? "có" : "không",
                    item.Found ? "có hồ sơ" : (item.Item.Required ? "THIẾU" : "chưa có"),
                    item.Files.Count.ToString(CultureInfo.InvariantCulture),
                    string.Join(" | ", item.Files),
                    item.Item.Note,
                })).Append("\r\n");
            }

            return sb.ToString();
        }

        /// <summary>Báo cáo HTML in được, kèm ô ký của chủ đầu tư (Điều 28: chủ đầu tư tổ chức lập hồ sơ).</summary>
        public static string Html(DossierSpec spec, DossierResult result, string folder, DateTime generatedAt)
        {
            var sb = new StringBuilder();
            sb.Append("<!DOCTYPE html><html lang=\"vi\"><head><meta charset=\"utf-8\"><title>")
              .Append(HtmlText.Escape(spec.Title)).Append("</title><style>")
              .Append("body{font-family:Segoe UI,Arial,sans-serif;margin:24px;color:#222;max-width:1000px}")
              .Append("table{border-collapse:collapse;width:100%;margin:8px 0 16px}th,td{border:1px solid #bbb;padding:5px 8px;text-align:left;vertical-align:top;font-size:13px}")
              .Append("th{background:#f2f2f2}.co{color:#0a7d28}.thieu{color:#b00020;font-weight:bold}.chua{color:#a06000}")
              .Append(".ky{display:flex;gap:24px;margin-top:32px}.ky div{flex:1;border:1px solid #888;padding:12px;min-height:140px}")
              .Append("@media print{body{margin:12mm}}")
              .Append("</style></head><body>");

            sb.Append("<h1>").Append(HtmlText.Escape(spec.Title)).Append("</h1><table>")
              .Append("<tr><th style=\"width:24%\">Công trình</th><td>").Append(HtmlText.Escape(spec.Project)).Append("</td></tr>")
              .Append("<tr><th>Căn cứ</th><td>").Append(HtmlText.Escape(spec.LegalBasis)).Append("</td></tr>")
              .Append("<tr><th>Thư mục hồ sơ</th><td>").Append(HtmlText.Escape(folder)).Append("</td></tr>")
              .Append("<tr><th>Thời điểm đối chiếu</th><td>")
              .Append(generatedAt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)).Append("</td></tr></table>");

            sb.Append("<p><b>").Append(HtmlText.Escape(Summary(result))).Append("</b></p>");
            sb.Append("<p>Danh mục lấy từ file cấu hình của dự án; DHCB chỉ <b>đối chiếu với file có thật</b> trong thư mục "
                      + "và đếm. Nội dung từng mục do đơn vị lập hồ sơ khai theo bản gốc và chịu trách nhiệm.</p>");

            foreach (var group in result.Items.GroupBy(i => new { i.GroupCode, i.GroupName }))
            {
                sb.Append("<h2>").Append(HtmlText.Escape(group.Key.GroupCode)).Append(". ")
                  .Append(HtmlText.Escape(group.Key.GroupName)).Append("</h2>")
                  .Append("<table><tr><th style=\"width:8%\">Mục</th><th>Tài liệu</th><th style=\"width:12%\">Trạng thái</th><th style=\"width:34%\">File</th></tr>");
                foreach (var item in group)
                {
                    sb.Append("<tr><td>").Append(HtmlText.Escape(item.Item.Code)).Append("</td><td>")
                      .Append(HtmlText.Escape(item.Item.Name));
                    if (item.Item.Note.Length > 0)
                    {
                        sb.Append("<br><small>").Append(HtmlText.Escape(item.Item.Note)).Append("</small>");
                    }

                    sb.Append("</td><td class=\"").Append(item.Found ? "co\">có hồ sơ" : item.Item.Required ? "thieu\">THIẾU" : "chua\">chưa có (không bắt buộc)")
                      .Append("</td><td>");
                    sb.Append(item.Files.Count == 0 ? "—" : string.Join("<br>", item.Files.Select(HtmlText.Escape)));
                    sb.Append("</td></tr>");
                }

                sb.Append("</table>");
            }

            if (result.Unmatched.Count > 0)
            {
                sb.Append("<h2>File chưa xếp vào mục nào (").Append(result.Unmatched.Count).Append(")</h2>")
                  .Append("<p>Không phải lỗi — nhưng người ký cần biết thư mục còn những gì: bản nháp, bản trùng, "
                          + "hay một mục danh mục chưa khai mẫu tên file.</p><ul>");
                foreach (var file in result.Unmatched)
                {
                    sb.Append("<li>").Append(HtmlText.Escape(file)).Append("</li>");
                }

                sb.Append("</ul>");
            }

            sb.Append("<h2>Xác nhận</h2><p>Theo Điều 28 NĐ 207/2026, <b>chủ đầu tư</b> tổ chức lập hồ sơ hoàn thành "
                      + "công trình và chịu trách nhiệm về tính chính xác, trung thực; mỗi nhà thầu chịu trách nhiệm "
                      + "phần mình lập.</p>")
              .Append("<div class=\"ky\"><div><b>Đơn vị lập danh mục</b><br><br>Họ tên: ______________________<br><br>"
                      + "Chức vụ: _____________________<br><br>Ngày: ____/____/________</div>")
              .Append("<div><b>Chủ đầu tư xác nhận</b><br><br>Họ tên: ______________________<br><br>"
                      + "Chức vụ: _____________________<br><br>Ngày: ____/____/________</div></div>");

            sb.Append("</body></html>");
            return sb.ToString();
        }
    }
}
