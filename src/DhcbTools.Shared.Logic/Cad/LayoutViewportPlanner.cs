using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace DhcbTools.Shared.Logic.Cad
{
    /// <summary>Một khung bản vẽ (layout) cần tạo bằng lệnh <c>LayoutViewport</c>: chép từ layout mẫu rồi canh viewport.</summary>
    public sealed class LayoutSheet
    {
        /// <summary>Tên layout mới (cho phép khoảng trắng, vd "VP-01 MB B1").</summary>
        public string? Name { get; set; }

        /// <summary>Layout có sẵn để chép (thiết lập in, khung tên, viewport).</summary>
        public string? Template { get; set; }

        /// <summary>Điểm Model Space (WCS) đặt vào giữa viewport, [x, y] hoặc [x, y, z] (z bỏ qua).</summary>
        public double[]? ViewCenter { get; set; }

        /// <summary>Tỉ lệ 1:N — CustomScale của viewport = 1/N.</summary>
        public double? Scale { get; set; }

        /// <summary>Handle viewport của LAYOUT MẪU cần điều khiển; bỏ trống = viewport nổi lớn nhất (trừ viewport tổng của tờ giấy).</summary>
        public string? ViewportHandle { get; set; }

        /// <summary>Chữ thay vào MText tiêu đề (giữ mã định dạng).</summary>
        public string? Title { get; set; }

        /// <summary>Chuỗi con nhận ra MText tiêu đề trong layout mẫu (không phân biệt hoa thường).</summary>
        public string? TitleMatch { get; set; }

        /// <summary>Thêm một MText dưới tiêu đề (vd "TỶ LỆ 1:75"), cùng style/layer/căn lề/bề rộng, cao ×0,8.</summary>
        public string? ScaleText { get; set; }

        /// <summary>Khoá hiển thị viewport (mặc định true).</summary>
        public bool? Lock { get; set; }
    }

    /// <summary>Kết quả kiểm danh sách khung: lỗi (nếu có) và tên layout sẽ bị thay.</summary>
    public sealed class LayoutViewportPlan
    {
        public List<string> Errors { get; } = new List<string>();

        /// <summary>Tên layout đã có sẽ bị xoá rồi tạo lại (chỉ khi <c>replaceExisting</c>).</summary>
        public List<string> Replaced { get; } = new List<string>();

        public bool IsValid => Errors.Count == 0;
    }

    /// <summary>Viewport của layout mẫu đưa vào chọn: thứ tự trong layout, handle, kích thước giấy, có phải viewport tổng không.</summary>
    public sealed class ViewportCandidate
    {
        public int Index { get; set; }

        public long Handle { get; set; }

        public double Width { get; set; }

        public double Height { get; set; }

        /// <summary>Viewport tổng của tờ giấy (Number = 1) — không bao giờ là viewport cần canh.</summary>
        public bool IsOverall { get; set; }
    }

    /// <summary>MText tách làm ba phần: mã định dạng mở đầu, phần chữ, phần đóng (ngoặc/mã sau chữ cuối).</summary>
    public sealed class MTextParts
    {
        public string Prefix { get; set; } = string.Empty;

        public string Plain { get; set; } = string.Empty;

        public string Suffix { get; set; } = string.Empty;
    }

    /// <summary>
    /// Logic thuần của lệnh <c>LayoutViewport</c> (tạo khung bản vẽ từ layout mẫu): kiểm tên/tỉ lệ/tâm/mẫu trước khi
    /// đụng bản vẽ, chọn viewport cần canh, thay chữ tiêu đề MText mà giữ mã định dạng, vị trí dòng tỉ lệ.
    /// <para>
    /// Trước lệnh này Bridge/MCP chỉ sửa được Model Space; ra khung bản vẽ phải làm tay hoặc qua COM — mà
    /// <c>Layouts.Add</c> + <c>CopyObjects</c> qua COM không tạo viewport tổng nên <c>MSpace</c> sau đó hỏng.
    /// </para>
    /// </summary>
    public static class LayoutViewportPlanner
    {
        public const int MaxSheets = 200;

        /// <summary>Tỉ lệ dòng "TỶ LỆ" so với chiều cao chữ tiêu đề.</summary>
        public const double ScaleTextHeightFactor = 0.8;

        private static readonly char[] Forbidden = { '<', '>', '/', '\\', '"', ':', ';', '?', '*', '|', ',', '=', '`' };

        /// <param name="sheets">Danh sách khung cần tạo.</param>
        /// <param name="existingLayouts">Tên layout giấy đang có trong bản vẽ (không kể Model).</param>
        /// <param name="replaceExisting">Layout trùng tên đã có thì xoá rồi tạo lại thay vì báo lỗi.</param>
        public static LayoutViewportPlan Plan(IReadOnlyList<LayoutSheet>? sheets, IEnumerable<string> existingLayouts, bool replaceExisting)
        {
            var plan = new LayoutViewportPlan();
            if (sheets == null || sheets.Count == 0)
            {
                plan.Errors.Add("Không có khung nào (sheets rỗng).");
                return plan;
            }

            if (sheets.Count > MaxSheets)
            {
                plan.Errors.Add($"Quá {MaxSheets} khung một lần ({sheets.Count}) — tách lô.");
                return plan;
            }

            var existing = new HashSet<string>(existingLayouts, StringComparer.OrdinalIgnoreCase);
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var targets = new HashSet<string>(sheets.Select(s => s?.Name?.Trim() ?? string.Empty), StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < sheets.Count; i++)
            {
                var s = sheets[i];
                var label = $"#{i + 1}";
                if (s == null)
                {
                    plan.Errors.Add($"{label}: khung rỗng.");
                    continue;
                }

                var name = s.Name?.Trim() ?? string.Empty;
                if (name.Length > 0)
                {
                    label += $" \"{name}\"";
                }

                if (!IsValidLayoutName(name))
                {
                    plan.Errors.Add($"{label}: \"name\" rỗng, quá 255 ký tự, trùng \"Model\" hoặc có ký tự cấm (< > / \\ \" : ; ? * | , = `).");
                }
                else if (!names.Add(name))
                {
                    plan.Errors.Add($"{label}: tên layout trùng trong danh sách.");
                }
                else if (existing.Contains(name))
                {
                    if (replaceExisting)
                    {
                        plan.Replaced.Add(name);
                    }
                    else
                    {
                        plan.Errors.Add($"{label}: layout đã có — đặt tên khác hoặc \"replaceExisting\": true để xoá rồi tạo lại.");
                    }
                }

                var template = s.Template?.Trim() ?? string.Empty;
                if (template.Length == 0)
                {
                    plan.Errors.Add($"{label}: thiếu \"template\" (layout mẫu để chép).");
                }
                else if (string.Equals(template, "Model", StringComparison.OrdinalIgnoreCase))
                {
                    plan.Errors.Add($"{label}: \"template\" phải là layout giấy, không phải Model.");
                }
                else if (!existing.Contains(template))
                {
                    plan.Errors.Add($"{label}: không có layout mẫu \"{template}\". Layout có: {string.Join(", ", existing.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))}.");
                }
                else if (replaceExisting && targets.Contains(template))
                {
                    plan.Errors.Add($"{label}: layout mẫu \"{template}\" cũng là layout sẽ bị thay — chọn mẫu khác.");
                }

                if (!EntityEditPlanner.IsPoint(s.ViewCenter))
                {
                    plan.Errors.Add($"{label}: \"viewCenter\" phải dạng [x, y] (toạ độ Model Space).");
                }

                if (!(s.Scale is double scale) || double.IsNaN(scale) || double.IsInfinity(scale) || scale <= 0)
                {
                    plan.Errors.Add($"{label}: \"scale\" phải là số > 0 (1:N, vd 75).");
                }

                if (s.ViewportHandle != null && !HandleText.TryParse(s.ViewportHandle, out _))
                {
                    plan.Errors.Add($"{label}: \"viewportHandle\" \"{s.ViewportHandle}\" không phải handle hex.");
                }

                var needsTitle = s.Title != null || s.ScaleText != null;
                if (needsTitle && string.IsNullOrWhiteSpace(s.TitleMatch))
                {
                    plan.Errors.Add($"{label}: \"title\"/\"scaleText\" cần \"titleMatch\" (chuỗi con của MText tiêu đề trong layout mẫu).");
                }
                else if (!needsTitle && s.TitleMatch != null)
                {
                    plan.Errors.Add($"{label}: \"titleMatch\" chỉ dùng kèm \"title\" hoặc \"scaleText\".");
                }

                if (s.Title != null && s.Title.Trim().Length == 0)
                {
                    plan.Errors.Add($"{label}: \"title\" rỗng — bỏ trường để giữ tiêu đề mẫu.");
                }

                if (s.ScaleText != null && s.ScaleText.Trim().Length == 0)
                {
                    plan.Errors.Add($"{label}: \"scaleText\" rỗng — bỏ trường nếu không cần dòng tỉ lệ.");
                }
            }

            return plan;
        }

        /// <summary>Tên layout hợp lệ: 1–255 ký tự, không ký tự cấm, không phải "Model".</summary>
        public static bool IsValidLayoutName(string? name)
        {
            var n = name?.Trim() ?? string.Empty;
            return n.Length > 0 && n.Length <= 255 && n.IndexOfAny(Forbidden) < 0
                   && !string.Equals(n, "Model", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>CustomScale của viewport cho tỉ lệ 1:N (đơn vị giấy = đơn vị bản vẽ, vd cùng mm).</summary>
        public static double CustomScale(double scale) => 1.0 / scale;

        /// <summary>
        /// Chọn viewport cần canh: theo handle nếu có (phải là viewport nổi của layout mẫu), không thì viewport nổi có
        /// diện tích lớn nhất. Null kèm <paramref name="error"/> khi không chọn được.
        /// </summary>
        public static ViewportCandidate? ChooseViewport(IReadOnlyList<ViewportCandidate> candidates, string? requestedHandle, string template, out string? error)
        {
            error = null;
            var floating = candidates.Where(c => !c.IsOverall).ToList();
            if (requestedHandle != null)
            {
                HandleText.TryParse(requestedHandle, out var wanted);
                var match = candidates.FirstOrDefault(c => c.Handle == wanted);
                if (match == null || match.IsOverall)
                {
                    error = match == null
                        ? $"Layout mẫu \"{template}\" không có viewport handle {HandleText.ToText(wanted)}. Viewport nổi có: {Describe(floating)}."
                        : $"Viewport {HandleText.ToText(wanted)} là viewport tổng của tờ giấy — chọn viewport nổi: {Describe(floating)}.";
                }

                return error == null ? match : null;
            }

            if (floating.Count == 0)
            {
                error = $"Layout mẫu \"{template}\" không có viewport nổi nào để canh.";
                return null;
            }

            return floating.OrderByDescending(c => c.Width * c.Height).ThenBy(c => c.Index).First();
        }

        private static string Describe(IEnumerable<ViewportCandidate> candidates)
        {
            var list = candidates.Select(c => $"{HandleText.ToText(c.Handle)} ({Number(c.Width)}×{Number(c.Height)})").ToList();
            return list.Count == 0 ? "(không có)" : string.Join(", ", list);
        }

        /// <summary>Số gọn cho báo cáo.</summary>
        public static string Number(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

        /// <summary>
        /// Tách nội dung MText thô (<c>Contents</c>) thành mã mở đầu, phần chữ, phần đóng — ví dụ
        /// <c>\pxqc;{\W0.9;CHI TIẾT}</c> → <c>\pxqc;{\W0.9;</c> | <c>CHI TIẾT</c> | <c>}</c>. Mã có tham số
        /// (<c>\A \C \c \F \f \H \Q \T \W \p</c>…<c>;</c>), bật/tắt (<c>\L \l \O \o \K \k</c>) và ngoặc là định dạng;
        /// ký tự thoát (<c>\\ \{ \}</c>), <c>\P</c>, <c>\~</c>, <c>\S…;</c> là chữ.
        /// </summary>
        public static MTextParts Split(string? contents)
        {
            var text = contents ?? string.Empty;
            var first = -1;
            var lastEnd = -1;
            var i = 0;
            while (i < text.Length)
            {
                var c = text[i];
                int length;
                bool plain;
                if (c == '{' || c == '}')
                {
                    length = 1;
                    plain = false;
                }
                else if (c == '\\' && i + 1 < text.Length)
                {
                    var code = text[i + 1];
                    if ("ACcFfHQTWp".IndexOf(code) >= 0)
                    {
                        var end = text.IndexOf(';', i + 2);
                        length = (end < 0 ? text.Length : end + 1) - i;
                        plain = false;
                    }
                    else if (code == 'S')
                    {
                        var end = text.IndexOf(';', i + 2);
                        length = (end < 0 ? text.Length : end + 1) - i;
                        plain = true;
                    }
                    else
                    {
                        length = 2;
                        plain = "LlOoKk".IndexOf(code) < 0;
                    }
                }
                else
                {
                    length = 1;
                    plain = true;
                }

                if (plain)
                {
                    if (first < 0)
                    {
                        first = i;
                    }

                    lastEnd = i + length;
                }

                i += length;
            }

            if (first < 0)
            {
                return new MTextParts { Prefix = text };
            }

            return new MTextParts
            {
                Prefix = text.Substring(0, first),
                Plain = text.Substring(first, lastEnd - first),
                Suffix = text.Substring(lastEnd),
            };
        }

        /// <summary>Thoát ký tự đặc biệt của MText: <c>\ { }</c>; xuống dòng thành <c>\P</c>.</summary>
        public static string Escape(string text)
        {
            var sb = new StringBuilder(text.Length + 8);
            foreach (var c in text.Replace("\r\n", "\n"))
            {
                switch (c)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '{': sb.Append("\\{"); break;
                    case '}': sb.Append("\\}"); break;
                    case '\n': sb.Append("\\P"); break;
                    default: sb.Append(c); break;
                }
            }

            return sb.ToString();
        }

        /// <summary>Thay phần chữ của MText bằng <paramref name="newText"/> (đã thoát), giữ nguyên mã định dạng hai đầu.</summary>
        public static string ReplacePlain(string? contents, string newText)
        {
            var parts = Split(contents);
            return parts.Prefix + Escape(newText) + parts.Suffix;
        }

        private static readonly Regex HeightCode = new Regex(@"\\H[^;]*;", RegexOptions.CultureInvariant);

        /// <summary>
        /// Nội dung dòng tỉ lệ: cùng mã định dạng với tiêu đề (căn lề, độ rộng chữ, font) nhưng bỏ mã chiều cao <c>\H…;</c>
        /// để chiều cao ×0,8 của đối tượng có hiệu lực.
        /// </summary>
        public static string ScaleTextContents(string? titleContents, string scaleText)
        {
            var parts = Split(titleContents);
            return HeightCode.Replace(parts.Prefix, string.Empty) + Escape(scaleText) + HeightCode.Replace(parts.Suffix, string.Empty);
        }

        /// <summary>
        /// Khoảng dời xuống (đơn vị giấy) từ điểm chèn tiêu đề tới điểm chèn dòng tỉ lệ cùng kiểu căn lề, để đỉnh dòng tỉ lệ
        /// nằm dưới đáy tiêu đề một khe bằng nửa chiều cao chữ mới. <paramref name="attachmentRow"/>: 0 = Top, 1 = Middle,
        /// 2 = Bottom (hàng của AttachmentPoint). Tiêu đề một dòng cao 3, căn Top → 3 + 1,2 = 4,2.
        /// </summary>
        public static double ScaleTextDrop(int attachmentRow, double titleActualHeight, double newHeight)
        {
            var below = attachmentRow == 0 ? 1.0 : attachmentRow == 1 ? 0.5 : 0.0; // phần tiêu đề nằm dưới điểm chèn
            var above = attachmentRow == 0 ? 0.0 : attachmentRow == 1 ? 0.5 : 1.0; // phần dòng mới nằm trên điểm chèn
            return (titleActualHeight * below) + (0.5 * newHeight) + (newHeight * above);
        }
    }
}
