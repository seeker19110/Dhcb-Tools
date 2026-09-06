using System;
using System.Collections.Generic;
using System.Linq;

namespace DhcbTools.Shared.Logic.Mep
{
    /// <summary>Phần quyết định thuần của <c>FamilyStarter</c>: chọn family cần dựng, tên file, câu chữ (§60).</summary>
    public static class FamilyStarterPlanner
    {
        public static string FamilyName(string key) => "DHCB_" + key;

        /// <summary>Danh sách cần dựng theo thứ tự chuẩn; rỗng = tất cả. Tên lạ trả về qua <paramref name="unknown"/>.</summary>
        public static List<string> Plan(IReadOnlyList<string> requested, IReadOnlyList<string> all, out List<string> unknown)
        {
            unknown = new List<string>();
            if (requested.Count == 0)
            {
                return all.ToList();
            }

            var wanted = new List<string>();
            foreach (var r in requested)
            {
                var hit = all.FirstOrDefault(a => string.Equals(a, r.Trim(), StringComparison.OrdinalIgnoreCase)
                                                  || string.Equals(FamilyName(a), r.Trim(), StringComparison.OrdinalIgnoreCase));
                if (hit == null) unknown.Add(r);
                else if (!wanted.Contains(hit)) wanted.Add(hit);
            }

            return all.Where(wanted.Contains).ToList();
        }

        public static string UnknownMessage(IReadOnlyList<string> unknown, IReadOnlyList<string> all)
            => $"Không biết family \"{string.Join("\", \"", unknown)}\". Hợp lệ: {string.Join(", ", all)}.";

        public static string NoTemplateMessage(IReadOnlyList<string> searched)
            => "Không tìm thấy template \"Metric Generic Model.rft\" của Revit — đã tìm: " + string.Join("; ", searched)
               + ". Cài Family Templates (Metric) kèm Revit, hoặc dựng family bằng trình soạn family.";

        public static string PreviewLine(string key, string rfaPath, bool load)
            => $"Sẽ dựng {FamilyName(key)} → {rfaPath}" + (load ? " rồi nạp vào mô hình." : " (không nạp).");

        public static string PreviewSummary(IReadOnlyList<string> plan, bool load)
            => $"[Xem trước] Sẽ dựng {plan.Count} family mẫu ({string.Join(", ", plan.Select(FamilyName))})" + (load ? " và nạp vào mô hình." : ".");

        public static string DoneSummary(IReadOnlyList<string> built, string folder, int? loaded)
        {
            if (built.Count == 0) return "Không dựng được family nào — xem Errors.";
            var s = $"Đã dựng {built.Count} family mẫu ({string.Join(", ", built)}) → {folder}";
            return loaded.HasValue ? s + $"; đã nạp {loaded.Value}/{built.Count} vào mô hình." : s + ".";
        }
    }
}
