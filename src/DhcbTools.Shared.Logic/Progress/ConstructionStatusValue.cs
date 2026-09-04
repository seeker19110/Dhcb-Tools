using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace DhcbTools.Shared.Logic.Progress
{
    /// <summary>
    /// Trạng thái thi công của một cấu kiện (đề xuất B1 — <c>ConstructionStatus</c>/<c>ProgressReport</c>).
    /// Thứ tự là <b>thứ hạng</b>: mỗi mức bao hàm mức trước, nên "đã lắp trở lên" tính được bằng so sánh.
    /// </summary>
    public enum ConstructionStage
    {
        /// <summary>Không có dữ liệu — <b>khác</b> "chưa lắp": chưa ai ghi nhận, không phải đã ghi nhận là chưa làm.</summary>
        ChuaCoDuLieu = 0,

        ChuaLap = 1,

        DangLap = 2,

        DaLap = 3,

        DaNghiemThu = 4,
    }

    /// <summary>
    /// Từ vựng trạng thái thi công: tên chuẩn tiếng Việt + tên đồng nghĩa Việt/Anh, viết có dấu hay
    /// không dấu đều nhận. Hiện trường gõ CSV bằng tay, nên nhận nhiều cách viết là bắt buộc — nhưng
    /// <b>chữ không nhận ra thì báo lỗi kèm danh sách hợp lệ</b>, không đoán và không im lặng bỏ qua.
    /// </summary>
    public static class ConstructionStatusValue
    {
        /// <summary>Tên chuẩn ghi vào mô hình và hiện trong báo cáo.</summary>
        public static string CanonicalOf(ConstructionStage stage)
        {
            switch (stage)
            {
                case ConstructionStage.ChuaLap: return "Chưa lắp";
                case ConstructionStage.DangLap: return "Đang lắp";
                case ConstructionStage.DaLap: return "Đã lắp";
                case ConstructionStage.DaNghiemThu: return "Đã nghiệm thu";
                default: return string.Empty;
            }
        }

        private static readonly Dictionary<ConstructionStage, string[]> Aliases =
            new Dictionary<ConstructionStage, string[]>
            {
                [ConstructionStage.ChuaLap] = new[] { "Chưa lắp", "Chưa lắp đặt", "Chưa thi công", "Not started", "Not installed", "Pending" },
                [ConstructionStage.DangLap] = new[] { "Đang lắp", "Đang lắp đặt", "Đang thi công", "In progress", "Installing", "WIP" },
                [ConstructionStage.DaLap] = new[] { "Đã lắp", "Đã lắp đặt", "Đã thi công", "Lắp xong", "Installed", "Complete", "Completed", "Done" },
                [ConstructionStage.DaNghiemThu] = new[] { "Đã nghiệm thu", "Nghiệm thu", "Đã bàn giao", "Accepted", "Approved", "Handover", "Signed off" },
            };

        /// <summary>Mọi cách viết được nhận, để in vào thông báo lỗi.</summary>
        public static IReadOnlyList<string> AllAliases =>
            Aliases.OrderBy(p => p.Key).SelectMany(p => p.Value).ToList();

        /// <summary>Tên chuẩn của các mức, theo thứ hạng — dùng cho cột báo cáo.</summary>
        public static IReadOnlyList<ConstructionStage> Stages => new[]
        {
            ConstructionStage.ChuaLap, ConstructionStage.DangLap, ConstructionStage.DaLap, ConstructionStage.DaNghiemThu,
        };

        private static readonly Dictionary<string, ConstructionStage> Lookup = BuildLookup();

        private static Dictionary<string, ConstructionStage> BuildLookup()
        {
            var map = new Dictionary<string, ConstructionStage>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in Aliases)
            {
                foreach (var alias in pair.Value)
                {
                    map[Normalize(alias)] = pair.Key;
                }
            }

            return map;
        }

        /// <summary>
        /// Đọc một ô trạng thái. Ô rỗng là <see cref="ConstructionStage.ChuaCoDuLieu"/> và trả
        /// <c>true</c> — "chưa nhập" là một câu trả lời hợp lệ; chữ lạ mới là lỗi.
        /// </summary>
        public static bool TryParse(string? text, out ConstructionStage stage)
        {
            stage = ConstructionStage.ChuaCoDuLieu;
            if (string.IsNullOrWhiteSpace(text))
            {
                return true;
            }

            return Lookup.TryGetValue(Normalize(text!), out stage);
        }

        /// <summary>Thông báo chuẩn khi ô trạng thái không đọc được — nêu đủ cách viết hợp lệ.</summary>
        public static string NotRecognised(string? text) =>
            $"trạng thái \"{(text ?? string.Empty).Trim()}\" không nhận ra. Hợp lệ (không phân biệt hoa thường, "
            + "có dấu hay không đều được): " + string.Join(", ", AllAliases) + ".";

        /// <summary>Bỏ dấu, gộp khoảng trắng, thường hoá — để "Đã lắp", "da lap", "ĐÃ  LẮP" là một.</summary>
        private static string Normalize(string text)
        {
            var stripped = text.Trim().Replace('đ', 'd').Replace('Đ', 'D').Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(stripped.Length);
            var lastWasSpace = false;
            foreach (var ch in stripped)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
                {
                    continue;
                }

                if (char.IsWhiteSpace(ch) || ch == '_' || ch == '-')
                {
                    lastWasSpace = sb.Length > 0;
                    continue;
                }

                if (lastWasSpace)
                {
                    sb.Append(' ');
                    lastWasSpace = false;
                }

                sb.Append(char.ToLowerInvariant(ch));
            }

            return sb.ToString();
        }
    }
}
