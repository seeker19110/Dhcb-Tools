using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using DhcbTools.Shared.Logic.Batch;
using DhcbTools.Shared.Logic.Evidence;
using Newtonsoft.Json;

namespace DhcbTools.Shared.Logic.Handover
{
    /// <summary>Một file trong gói bàn giao: đường dẫn, cỡ, băm SHA-256 — để "trích xuất ra giấy" có thứ đối chiếu.</summary>
    public sealed class HandoverFile
    {
        public HandoverFile(string relativePath, string kind, long sizeBytes, string sha256)
        {
            RelativePath = relativePath;
            Kind = kind;
            SizeBytes = sizeBytes;
            Sha256 = sha256;
        }

        /// <summary>Đường dẫn tương đối so với thư mục đầu ra của job.</summary>
        public string RelativePath { get; }

        /// <summary><c>IFC</c>, <c>PDF</c>, <c>DWG</c>, <c>NWC</c>, <c>CSV</c>, <c>HTML</c>, <c>JSON</c>…</summary>
        public string Kind { get; }

        public long SizeBytes { get; }

        /// <summary>SHA-256 dạng hex thường của toàn bộ byte file.</summary>
        public string Sha256 { get; }
    }

    /// <summary>Một dòng danh mục bản vẽ (đọc từ CSV do <c>SheetIndex</c> ghi).</summary>
    public sealed class SheetIndexRow
    {
        public SheetIndexRow(string number, string name, string revision, string revisionDate, string issueDate, string drawnBy, string checkedBy, int viewCount)
        {
            Number = number;
            Name = name;
            Revision = revision;
            RevisionDate = revisionDate;
            IssueDate = issueDate;
            DrawnBy = drawnBy;
            CheckedBy = checkedBy;
            ViewCount = viewCount;
        }

        public string Number { get; }

        public string Name { get; }

        public string Revision { get; }

        public string RevisionDate { get; }

        public string IssueDate { get; }

        public string DrawnBy { get; }

        public string CheckedBy { get; }

        public int ViewCount { get; }

        /// <summary>Tiêu đề cột CSV — hợp đồng giữa lệnh <c>SheetIndex</c> (Revit) và gói bàn giao (thuần).</summary>
        public static readonly string[] CsvHeader =
        {
            "Số bản vẽ", "Tên bản vẽ", "Revision", "Ngày revision", "Ngày phát hành", "Người vẽ", "Người kiểm", "Số view",
        };

        /// <summary>Ghi CSV (UTF-8 có BOM do bên gọi quyết).</summary>
        public static string ToCsv(IEnumerable<SheetIndexRow> rows)
        {
            var sb = new StringBuilder();
            sb.Append(CsvText.JoinLine(CsvHeader)).Append("\r\n");
            foreach (var r in rows)
            {
                sb.Append(CsvText.JoinLine(new[]
                {
                    r.Number, r.Name, r.Revision, r.RevisionDate, r.IssueDate, r.DrawnBy, r.CheckedBy,
                    r.ViewCount.ToString(CultureInfo.InvariantCulture),
                })).Append("\r\n");
            }

            return sb.ToString();
        }

        /// <summary>Bảng HTML in được của danh mục (lệnh <c>SheetIndex</c> ghi khi có <c>htmlPath</c>).</summary>
        public static string ToHtml(string modelTitle, IReadOnlyList<SheetIndexRow> rows)
        {
            var sb = new StringBuilder();
            sb.Append("<!DOCTYPE html><html lang=\"vi\"><head><meta charset=\"utf-8\"><title>Danh mục bản vẽ — ")
              .Append(HtmlText.Escape(modelTitle)).Append("</title><style>body{font-family:Segoe UI,Arial,sans-serif;margin:24px;color:#222}")
              .Append("table{border-collapse:collapse;width:100%}th,td{border:1px solid #bbb;padding:5px 8px;text-align:left;font-size:13px}th{background:#f2f2f2}</style></head><body>")
              .Append("<h1>Danh mục bản vẽ</h1><p><b>Mô hình:</b> ").Append(HtmlText.Escape(modelTitle)).Append(" · ")
              .Append(rows.Count.ToString(CultureInfo.InvariantCulture)).Append(" sheet</p><table><tr><th>#</th>");
            foreach (var h in CsvHeader)
            {
                sb.Append("<th>").Append(HtmlText.Escape(h)).Append("</th>");
            }

            sb.Append("</tr>");
            var i = 0;
            foreach (var r in rows)
            {
                sb.Append("<tr><td>").Append(++i).Append("</td><td>").Append(HtmlText.Escape(r.Number)).Append("</td><td>").Append(HtmlText.Escape(r.Name))
                  .Append("</td><td>").Append(HtmlText.Escape(r.Revision)).Append("</td><td>").Append(HtmlText.Escape(r.RevisionDate))
                  .Append("</td><td>").Append(HtmlText.Escape(r.IssueDate)).Append("</td><td>").Append(HtmlText.Escape(r.DrawnBy))
                  .Append("</td><td>").Append(HtmlText.Escape(r.CheckedBy)).Append("</td><td>").Append(r.ViewCount).Append("</td></tr>");
            }

            sb.Append("</table></body></html>");
            return sb.ToString();
        }

        /// <summary>Đọc lại CSV; trả về rỗng nếu tiêu đề không phải của <c>SheetIndex</c> (file CSV khác trong thư mục).</summary>
        public static List<SheetIndexRow> FromCsv(string text)
        {
            var rows = new List<SheetIndexRow>();
            var lines = CsvText.ReadRecords(new StringReader((text ?? string.Empty).TrimStart('\uFEFF'))).ToList();
            if (lines.Count == 0 || lines[0].Length < CsvHeader.Length || !lines[0].Take(CsvHeader.Length).SequenceEqual(CsvHeader, StringComparer.Ordinal))
            {
                return rows;
            }

            foreach (var cells in lines.Skip(1))
            {
                if (cells.Length < CsvHeader.Length)
                {
                    continue;
                }

                int.TryParse(cells[7], NumberStyles.Integer, CultureInfo.InvariantCulture, out var views);
                rows.Add(new SheetIndexRow(cells[0], cells[1], cells[2], cells[3], cells[4], cells[5], cells[6], views));
            }

            return rows;
        }
    }

    /// <summary>Một mục kiểm tra kèm kết luận, hiện trong bảng "Kiểm tra tự động" của gói.</summary>
    public sealed class HandoverCheck
    {
        public HandoverCheck(string name, bool ok, string detail)
        {
            Name = name;
            Ok = ok;
            Detail = detail;
        }

        public string Name { get; }

        public bool Ok { get; }

        public string Detail { get; }
    }

    /// <summary>Đầu vào để dựng gói bàn giao — thuần dữ liệu, không đọc đĩa; <see cref="HandoverPackage.Collect"/> đọc đĩa hộ.</summary>
    public sealed class HandoverInput
    {
        public string JobName { get; set; } = string.Empty;

        public string ProjectName { get; set; } = string.Empty;

        public string Owner { get; set; } = string.Empty;

        public string Contractor { get; set; } = string.Empty;

        public DateTime GeneratedAt { get; set; }

        public string AddinVersion { get; set; } = string.Empty;

        public string OutputFolder { get; set; } = string.Empty;

        public string RunLogPath { get; set; } = string.Empty;

        public List<RunLogEntry> Entries { get; } = new List<RunLogEntry>();

        public List<HandoverFile> Files { get; } = new List<HandoverFile>();

        public List<SheetIndexRow> Sheets { get; } = new List<SheetIndexRow>();

        public List<HandoverCheck> Checks { get; } = new List<HandoverCheck>();
    }

    /// <summary>
    /// Mục 11.3 — <b>gói bàn giao</b>: một trang HTML in được, gom mọi đầu ra của một đêm batch (IFC, PDF,
    /// danh mục bản vẽ, báo cáo kiểm) kèm dấu thời gian, phiên bản add-in, băm SHA-256 từng file, kết quả
    /// kiểm tự động (chuỗi băm nhật ký, IFC, IDS) và <b>ô xác nhận của chủ đầu tư</b>.
    /// <para>
    /// Vì sao có ô xác nhận: Điều 11 NĐ 207/2026 — hồ sơ điện tử khi cơ quan có thẩm quyền yêu cầu phải
    /// <i>trích xuất, in ra giấy và được chủ đầu tư xác nhận</i>. Băm từng file là thứ nối tờ giấy đã ký với
    /// đúng file đã bàn giao: đổi một byte là băm đổi. Trang này in ra là hồ sơ; <c>ban-giao.json</c> cạnh nó
    /// là bản máy đọc được để kiểm lại sau 30 ngày.
    /// </para>
    /// </summary>
    public static class HandoverPackage
    {
        /// <summary>Đuôi file được coi là "sản phẩm bàn giao" (không phải log hay báo cáo phụ).</summary>
        private static readonly Dictionary<string, string> Kinds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [".ifc"] = "IFC", [".pdf"] = "PDF", [".dwg"] = "DWG", [".nwc"] = "NWC", [".csv"] = "CSV", [".html"] = "HTML", [".json"] = "JSON", [".xlsx"] = "XLSX",
        };

        /// <summary>Tên file gói trong thư mục đầu ra.</summary>
        public const string HtmlName = "ban-giao.html";

        /// <summary>Bản máy đọc được của cùng nội dung.</summary>
        public const string JsonName = "ban-giao.json";

        /// <summary>SHA-256 hex thường của một file.</summary>
        public static string Sha256Of(string path)
        {
            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(path))
            {
                var hash = sha.ComputeHash(stream);
                var sb = new StringBuilder(hash.Length * 2);
                foreach (var b in hash)
                {
                    sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
                }

                return sb.ToString();
            }
        }

        /// <summary>
        /// Quét thư mục đầu ra: băm mọi file sản phẩm (trừ chính gói và log), đọc danh mục bản vẽ từ CSV có
        /// đúng tiêu đề <see cref="SheetIndexRow.CsvHeader"/>. Không đọc <c>.rvt</c>: bản sao mô hình không
        /// phải sản phẩm bàn giao, và băm 700 MB mỗi đêm là phí vô ích.
        /// </summary>
        public static void Collect(HandoverInput input)
        {
            if (!Directory.Exists(input.OutputFolder))
            {
                return;
            }

            var root = Path.GetFullPath(input.OutputFolder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            foreach (var path in Directory.GetFiles(root, "*", SearchOption.AllDirectories).OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            {
                var name = Path.GetFileName(path);
                if (name.Equals(HtmlName, StringComparison.OrdinalIgnoreCase) || name.Equals(JsonName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var ext = Path.GetExtension(path);
                if (!Kinds.TryGetValue(ext, out var kind))
                {
                    continue;
                }

                var relative = path.Substring(root.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Replace('\\', '/');
                input.Files.Add(new HandoverFile(relative, kind, new FileInfo(path).Length, Sha256Of(path)));

                if (kind == "CSV" && input.Sheets.Count == 0)
                {
                    var rows = SheetIndexRow.FromCsv(File.ReadAllText(path, Encoding.UTF8));
                    if (rows.Count > 0)
                    {
                        input.Sheets.AddRange(rows);
                    }
                }
            }
        }

        /// <summary>Kiểm chuỗi băm của nhật ký chạy và thêm vào <see cref="HandoverInput.Checks"/>.</summary>
        public static void CheckRunLog(HandoverInput input)
        {
            if (string.IsNullOrEmpty(input.RunLogPath) || !File.Exists(input.RunLogPath))
            {
                input.Checks.Add(new HandoverCheck("Nhật ký chạy", false, "Không có file nhật ký " + input.RunLogPath));
                return;
            }

            var verification = RunLog.VerifyFile(input.RunLogPath);
            input.Checks.Add(new HandoverCheck(
                "Chuỗi băm nhật ký (NĐ 207/2026, 11.5)",
                verification.Status == ChainStatus.Intact,
                verification.Message + " — " + Path.GetFileName(input.RunLogPath)));
        }

        /// <summary>JSON máy đọc: cùng dữ liệu với trang HTML.</summary>
        public static string ToJson(HandoverInput input) => JsonConvert.SerializeObject(new
        {
            job = input.JobName,
            project = input.ProjectName,
            owner = input.Owner,
            contractor = input.Contractor,
            generatedAt = input.GeneratedAt.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture),
            addinVersion = input.AddinVersion,
            outputFolder = input.OutputFolder,
            runLog = input.RunLogPath,
            checks = input.Checks.Select(c => new { c.Name, c.Ok, c.Detail }),
            files = input.Files.Select(f => new { f.RelativePath, f.Kind, f.SizeBytes, f.Sha256 }),
            sheets = input.Sheets.Select(s => new { s.Number, s.Name, s.Revision, s.RevisionDate, s.IssueDate, s.DrawnBy, s.CheckedBy, s.ViewCount }),
            steps = input.Entries.Select(e => new { file = Path.GetFileName(e.File), e.Command, e.Success, e.Skipped, e.Summary, e.Hash }),
        }, Formatting.Indented);

        /// <summary>Trang HTML in được (A4, có ô ký).</summary>
        public static string Html(HandoverInput input)
        {
            var sb = new StringBuilder();
            sb.Append("<!DOCTYPE html><html lang=\"vi\"><head><meta charset=\"utf-8\"><title>Gói bàn giao — ")
              .Append(HtmlText.Escape(input.ProjectName.Length > 0 ? input.ProjectName : input.JobName)).Append("</title><style>")
              .Append("body{font-family:Segoe UI,Arial,sans-serif;margin:24px;color:#222;max-width:1000px}")
              .Append("table{border-collapse:collapse;width:100%;margin:8px 0 16px}th,td{border:1px solid #bbb;padding:5px 8px;text-align:left;vertical-align:top;font-size:13px}")
              .Append("th{background:#f2f2f2}.ok{color:#0a7d28}.fail{color:#b00020}code{font-size:11px;word-break:break-all}")
              .Append(".ky{display:flex;gap:24px;margin-top:32px}.ky div{flex:1;border:1px solid #888;padding:12px;min-height:150px}")
              .Append("@media print{body{margin:12mm}.ky div{min-height:120px}}")
              .Append("</style></head><body>");

            sb.Append("<h1>Gói bàn giao hồ sơ điện tử</h1><table><tr><th style=\"width:28%\">Dự án</th><td>")
              .Append(HtmlText.Escape(input.ProjectName)).Append("</td></tr><tr><th>Job</th><td>").Append(HtmlText.Escape(input.JobName))
              .Append("</td></tr><tr><th>Chủ đầu tư</th><td>").Append(HtmlText.Escape(input.Owner))
              .Append("</td></tr><tr><th>Nhà thầu / đơn vị lập</th><td>").Append(HtmlText.Escape(input.Contractor))
              .Append("</td></tr><tr><th>Thời điểm lập</th><td>").Append(input.GeneratedAt.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture))
              .Append("</td></tr><tr><th>Phần mềm</th><td>DHCB Tools ").Append(HtmlText.Escape(input.AddinVersion))
              .Append("</td></tr><tr><th>Thư mục đầu ra</th><td>").Append(HtmlText.Escape(input.OutputFolder))
              .Append("</td></tr></table>");

            sb.Append("<h2>1. Kiểm tra tự động</h2><table><tr><th>Mục</th><th>Kết quả</th><th>Chi tiết</th></tr>");
            foreach (var c in input.Checks)
            {
                sb.Append("<tr><td>").Append(HtmlText.Escape(c.Name)).Append("</td><td class=\"").Append(c.Ok ? "ok\">Đạt" : "fail\">Không đạt")
                  .Append("</td><td>").Append(HtmlText.Escape(c.Detail)).Append("</td></tr>");
            }

            if (input.Checks.Count == 0)
            {
                sb.Append("<tr><td colspan=\"3\">Không có mục kiểm tra nào.</td></tr>");
            }

            sb.Append("</table>");

            sb.Append("<h2>2. Các bước đã chạy</h2><table><tr><th>File</th><th>Lệnh</th><th>Kết quả</th><th>Tóm tắt</th></tr>");
            foreach (var e in input.Entries)
            {
                sb.Append("<tr><td>").Append(HtmlText.Escape(Path.GetFileName(e.File))).Append("</td><td>").Append(HtmlText.Escape(e.Command))
                  .Append("</td><td class=\"").Append(e.Skipped ? "\">Bỏ qua" : e.Success ? "ok\">Thành công" : "fail\">Lỗi")
                  .Append("</td><td>").Append(HtmlText.Escape(e.Summary)).Append("</td></tr>");
            }

            sb.Append("</table>");

            sb.Append("<h2>3. Danh mục bản vẽ</h2>");
            if (input.Sheets.Count == 0)
            {
                sb.Append("<p><i>Không có danh mục bản vẽ (job không chạy <code>SheetIndex</code>, hoặc mô hình không có sheet).</i></p>");
            }
            else
            {
                sb.Append("<table><tr><th>#</th>");
                foreach (var h in SheetIndexRow.CsvHeader)
                {
                    sb.Append("<th>").Append(HtmlText.Escape(h)).Append("</th>");
                }

                sb.Append("</tr>");
                var i = 0;
                foreach (var s in input.Sheets)
                {
                    sb.Append("<tr><td>").Append(++i).Append("</td><td>").Append(HtmlText.Escape(s.Number)).Append("</td><td>").Append(HtmlText.Escape(s.Name))
                      .Append("</td><td>").Append(HtmlText.Escape(s.Revision)).Append("</td><td>").Append(HtmlText.Escape(s.RevisionDate))
                      .Append("</td><td>").Append(HtmlText.Escape(s.IssueDate)).Append("</td><td>").Append(HtmlText.Escape(s.DrawnBy))
                      .Append("</td><td>").Append(HtmlText.Escape(s.CheckedBy)).Append("</td><td>").Append(s.ViewCount).Append("</td></tr>");
                }

                sb.Append("</table>");
            }

            sb.Append("<h2>4. File bàn giao và băm SHA-256</h2>");
            if (input.Files.Count == 0)
            {
                sb.Append("<p><i>Thư mục đầu ra không có file sản phẩm nào.</i></p>");
            }
            else
            {
                sb.Append("<table><tr><th>#</th><th>File</th><th>Loại</th><th>Cỡ</th><th>SHA-256</th></tr>");
                var i = 0;
                foreach (var f in input.Files)
                {
                    sb.Append("<tr><td>").Append(++i).Append("</td><td>").Append(HtmlText.Escape(f.RelativePath)).Append("</td><td>").Append(f.Kind)
                      .Append("</td><td>").Append(SizeText(f.SizeBytes)).Append("</td><td><code>").Append(f.Sha256).Append("</code></td></tr>");
                }

                sb.Append("</table>");
            }

            sb.Append("<h2>5. Xác nhận (Điều 11 NĐ 207/2026)</h2><p>Hồ sơ điện tử trên đây được trích xuất và in ra giấy từ gói bàn giao lập lúc ")
              .Append(input.GeneratedAt.ToString("HH:mm:ss dd/MM/yyyy", CultureInfo.InvariantCulture))
              .Append(". Băm SHA-256 ở mục 4 dùng để đối chiếu bản in với file điện tử.</p>")
              .Append("<div class=\"ky\"><div><b>Đơn vị lập</b><br>").Append(HtmlText.Escape(input.Contractor))
              .Append("<br><br>Họ tên: ______________________<br><br>Chức vụ: _____________________<br><br>Ngày: ____/____/________</div>")
              .Append("<div><b>Chủ đầu tư xác nhận</b><br>").Append(HtmlText.Escape(input.Owner))
              .Append("<br><br>Họ tên: ______________________<br><br>Chức vụ: _____________________<br><br>Ngày: ____/____/________</div></div>");

            sb.Append("</body></html>");
            return sb.ToString();
        }

        private static string SizeText(long bytes)
        {
            if (bytes >= 1024 * 1024)
            {
                return (bytes / (1024.0 * 1024.0)).ToString("0.0", CultureInfo.InvariantCulture) + " MB";
            }

            if (bytes >= 1024)
            {
                return (bytes / 1024.0).ToString("0", CultureInfo.InvariantCulture) + " KB";
            }

            return bytes.ToString(CultureInfo.InvariantCulture) + " B";
        }
    }
}
