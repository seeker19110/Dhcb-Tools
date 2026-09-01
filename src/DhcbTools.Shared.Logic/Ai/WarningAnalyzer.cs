using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using DhcbTools.Shared.Logic.Batch;

namespace DhcbTools.Shared.Logic.Ai
{
    /// <summary>Một nhóm cảnh báo cùng nguyên nhân.</summary>
    public sealed class WarningGroup
    {
        public WarningGroup(string cause, string action, int priority)
        {
            Cause = cause;
            Action = action;
            Priority = priority;
        }

        /// <summary>Nguyên nhân (tiếng Việt).</summary>
        public string Cause { get; }

        /// <summary>Đề xuất xử lý.</summary>
        public string Action { get; }

        /// <summary>1 = làm trước.</summary>
        public int Priority { get; }

        public List<string> Samples { get; } = new List<string>();

        public int Count { get; set; }

        public HashSet<string> Files { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Mục 5.3 — phân tích log <c>run.jsonl</c> (messages/errors của HealthReport, ConnectorChecker, Clash, RuleCheck…)
    /// offline bằng bảng mẫu: gom theo nguyên nhân, đề xuất thứ tự xử lý, xuất tóm tắt tiếng Việt. Chỉ đọc.
    /// Có thể đưa bản tóm tắt này cho model local (Ollama) viết lại tự nhiên hơn — không bắt buộc.
    /// </summary>
    public static class WarningAnalyzer
    {
        private sealed class Pattern
        {
            public Pattern(string regex, string cause, string action, int priority)
            {
                Regex = new Regex(regex, RegexOptions.IgnoreCase | RegexOptions.Compiled);
                Cause = cause;
                Action = action;
                Priority = priority;
            }

            public Regex Regex { get; }
            public string Cause { get; }
            public string Action { get; }
            public int Priority { get; }
        }

        private static readonly Pattern[] Patterns =
        {
            new Pattern(@"connector h[oở]|open connector|not connected|chưa kết nối", "Connector MEP hở", "Chạy ConnectorChecker với create3dView, nối lại từng chỗ; ưu tiên ống chính.", 1),
            new Pattern(@"va ch[aạ]m|clash|intersect", "Va chạm giữa các hệ", "Mở view Clash, xử lý theo nhóm Duct×Kết cấu trước, Pipe×Duct sau; chấp nhận vào clash-accepted.json nếu đúng thiết kế.", 1),
            new Pattern(@"thi[eế]u gi[aá] tr[iị]|missing|required|thi[eế]u tham s[oố]", "Tham số bắt buộc còn trống", "Xuất ParameterExport cho category đó, điền trong Excel, nhập lại bằng ParameterImport.", 2),
            new Pattern(@"kh[oô]ng kh[oớ]p m[aẫ]u|pattern|đ[aặ]t t[eê]n|naming", "Đặt tên sai quy tắc", "Dùng AutoNumbering/SystemName để đặt lại theo quy tắc; kiểm tra lại bằng RuleCheck.", 2),
            new Pattern(@"identical|tr[uù]ng|duplicate|overlap|ch[oồ]ng", "Phần tử trùng/chồng nhau", "Lọc theo warning 'identical instances', xoá bản thừa; thường do copy/paste nhiều lần.", 2),
            new Pattern(@"slightly off axis|l[eệ]ch tr[uụ]c|kh[oô]ng th[aẳ]ng", "Tường/đường hơi lệch trục", "Align lại theo trục; lệch nhỏ tích luỹ gây lỗi join và dim.", 3),
            new Pattern(@"room.*not.*enclosed|ph[oò]ng.*kh[oô]ng.*k[ií]n|not in a properly enclosed", "Phòng không kín", "Kiểm tra room separation line và tường không chạm nhau.", 3),
            new Pattern(@"view.*kh[oô]ng.*sheet|unplaced view|view th[uừ]a", "View thừa không đặt trên sheet", "Chạy RemoveUnusedViews (dryRun trước) — giảm dung lượng và thời gian mở file.", 4),
            new Pattern(@"in-place|inplace|family t[aạ]i ch[oỗ]", "Family in-place", "Thay bằng family loadable; in-place làm nặng file và không tái sử dụng được.", 4),
            new Pattern(@"file size|dung l[uư][oợ]ng|MB", "File quá lớn", "Purge unused, xoá view thừa, kiểm tra CAD import lồng nhau.", 4),
            new Pattern(@"kh[oô]ng th[eể] t[aạ]o|kh[oô]ng d[uự]ng đ[uư][oợ]c|fitting|elbow|tee|cannot create", "Fitting không dựng được", "Kiểm tra routing preference của type và góc/đoạn quá ngắn; nối tay chỗ báo.", 2),
            new Pattern(@"timeout|h[eế]t th[oờ]i gian", "Lệnh chạy quá lâu", "Tách job nhỏ hơn hoặc tăng --max-minutes; kiểm tra file nặng.", 3),
            new Pattern(@"kh[oô]ng m[oở] đ[uư][oợ]c|cannot open|could not open|corrupt", "File không mở được", "Kiểm tra đường dẫn/phiên bản Revit; mở tay với Audit.", 1),
        };

        public static List<WarningGroup> Analyze(IEnumerable<RunLogEntry> entries)
        {
            var groups = new Dictionary<string, WarningGroup>(StringComparer.Ordinal);
            var other = new WarningGroup("Khác (chưa phân loại)", "Đọc trực tiếp trong báo cáo chi tiết.", 5);

            foreach (var e in entries)
            {
                foreach (var line in e.Messages.Concat(e.Errors).Concat(e.Success ? Enumerable.Empty<string>() : new[] { e.Summary }))
                {
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        continue;
                    }

                    var matched = false;
                    foreach (var p in Patterns)
                    {
                        if (!p.Regex.IsMatch(line))
                        {
                            continue;
                        }

                        if (!groups.TryGetValue(p.Cause, out var g))
                        {
                            g = new WarningGroup(p.Cause, p.Action, p.Priority);
                            groups[p.Cause] = g;
                        }

                        Register(g, line, e.File);
                        matched = true;
                        break;
                    }

                    if (!matched)
                    {
                        Register(other, line, e.File);
                    }
                }
            }

            var list = groups.Values.OrderBy(g => g.Priority).ThenByDescending(g => g.Count).ToList();
            if (other.Count > 0)
            {
                list.Add(other);
            }
            return list;
        }

        private static void Register(WarningGroup g, string line, string file)
        {
            g.Count++;
            g.Files.Add(file);
            if (g.Samples.Count < 3)
            {
                g.Samples.Add(line.Length > 160 ? line.Substring(0, 160) + "…" : line);
            }
        }

        /// <summary>Tóm tắt tiếng Việt dạng Markdown — đọc được ngay, hoặc làm prompt cho model local viết lại.</summary>
        public static string Summarize(IReadOnlyList<WarningGroup> groups, string jobName)
        {
            var sb = new StringBuilder();
            sb.Append("# Tóm tắt cảnh báo — ").Append(jobName).Append('\n').Append('\n');
            if (groups.Count == 0)
            {
                sb.Append("Không có cảnh báo nào trong log.\n");
                return sb.ToString();
            }

            var total = groups.Sum(g => g.Count);
            sb.Append("Tổng ").Append(total).Append(" dòng cảnh báo, gom thành ").Append(groups.Count).Append(" nhóm. Thứ tự đề xuất xử lý:\n\n");
            var i = 1;
            foreach (var g in groups)
            {
                sb.Append(i++).Append(". **").Append(g.Cause).Append("** — ").Append(g.Count).Append(" lần, ").Append(g.Files.Count).Append(" file.\n");
                sb.Append("   Đề xuất: ").Append(g.Action).Append('\n');
                foreach (var s in g.Samples)
                {
                    sb.Append("   - ").Append(s).Append('\n');
                }
                sb.Append('\n');
            }

            return sb.ToString();
        }
    }
}
