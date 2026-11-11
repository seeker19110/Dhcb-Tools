using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace DhcbTools.Shared.Logic.Cad
{
    /// <summary>Hộp bao 2D của Model Space một bản vẽ nguồn, đơn vị bản vẽ đích.</summary>
    public sealed class CombineExtents
    {
        public CombineExtents(double minX, double minY, double maxX, double maxY)
        {
            MinX = minX;
            MinY = minY;
            MaxX = maxX;
            MaxY = maxY;
        }

        public double MinX { get; }
        public double MinY { get; }
        public double MaxX { get; }
        public double MaxY { get; }
        public double Width => MaxX - MinX;

        /// <summary>Hộp bao sau khi phóng <paramref name="factor"/> lần quanh gốc toạ độ (quy đổi đơn vị).</summary>
        public CombineExtents Scaled(double factor) => new CombineExtents(MinX * factor, MinY * factor, MaxX * factor, MaxY * factor);
    }

    /// <summary>Vị trí chèn của một bản vẽ nguồn: block mang tên <see cref="BlockName"/> đặt tại (X, Y).</summary>
    public sealed class CombinePlacement
    {
        public CombinePlacement(string blockName, double x, double y)
        {
            BlockName = blockName;
            X = x;
            Y = y;
        }

        public string BlockName { get; }
        public double X { get; }
        public double Y { get; }
    }

    /// <summary>
    /// Phần thuần của lệnh gộp bản vẽ (DrawingCombine): đặt tên block/layout không trùng và hợp lệ với AutoCAD,
    /// và xếp các bản vẽ nguồn thành một hàng không chồng lên nhau. Tách khỏi Core.AutoCAD để test không cần AutoCAD.
    /// </summary>
    public static class DrawingCombinePlanner
    {
        /// <summary>Hai cách đặt: giữ nguyên toạ độ gốc, hoặc xếp thành hàng ngang từ trái sang phải.</summary>
        public static readonly string[] Arrangements = { "Row", "Original" };

        /// <summary>Giới hạn độ dài tên symbol của AutoCAD.</summary>
        public const int MaxSymbolLength = 255;

        /// <summary>Giới hạn tên layout (tab) của AutoCAD.</summary>
        public const int MaxLayoutLength = 255;

        // Ký tự AutoCAD cấm trong tên symbol (SymbolUtilityServices.ValidateSymbolName, chế độ không cho phép dấu |).
        private const string Forbidden = "<>/\\\":;?*|,=`";

        /// <summary>
        /// Hệ số quy đổi đơn vị nguồn → đích theo INSUNITS, như lệnh INSERT: bản vẽ mm chèn vào bản vẽ m thành ×0,001.
        /// Một trong hai bên không khai đơn vị (0) hoặc mã lạ thì giữ 1:1.
        /// </summary>
        public static double UnitScale(int sourceInsunits, int targetInsunits)
        {
            var source = DrawingUnits.MillimetersPerUnit(sourceInsunits);
            var target = DrawingUnits.MillimetersPerUnit(targetInsunits);
            return source is null || target is null ? 1.0 : source.Value / target.Value;
        }

        /// <summary>Chuẩn hoá arrangement (không phân biệt hoa thường); giá trị lạ trả null để lệnh báo lỗi.</summary>
        public static string? NormalizeArrangement(string? value)
        {
            var text = (value ?? string.Empty).Trim();
            if (text.Length == 0)
            {
                return "Row";
            }

            return Arrangements.FirstOrDefault(a => string.Equals(a, text, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Tên hợp lệ: thay ký tự cấm bằng "_", bỏ khoảng trắng đầu/cuối, cắt độ dài, rỗng thành "DWG".</summary>
        public static string SanitizeName(string? raw, int maxLength = MaxSymbolLength)
        {
            var builder = new StringBuilder();
            foreach (var ch in raw ?? string.Empty)
            {
                builder.Append(Forbidden.IndexOf(ch) >= 0 || char.IsControl(ch) ? '_' : ch);
            }

            var text = builder.ToString().Trim();
            if (text.Length > maxLength)
            {
                text = text.Substring(0, maxLength).TrimEnd();
            }

            return text.Length == 0 ? "DWG" : text;
        }

        /// <summary>
        /// Tên không trùng (không phân biệt hoa thường) với <paramref name="taken"/>: thêm hậu tố "-2", "-3"…
        /// và cắt phần gốc để tổng độ dài không vượt <paramref name="maxLength"/>. Tên trả về được thêm vào <paramref name="taken"/>.
        /// </summary>
        public static string UniqueName(string baseName, ISet<string> taken, int maxLength = MaxSymbolLength)
        {
            var name = SanitizeName(baseName, maxLength);
            for (var n = 2; taken.Contains(name); n++)
            {
                var suffix = "-" + n;
                var stem = SanitizeName(baseName, maxLength - suffix.Length);
                name = stem + suffix;
            }

            taken.Add(name);
            return name;
        }

        /// <summary>Tên block cho một file nguồn: tên file không đuôi.</summary>
        public static string BlockNameFor(string path) => Path.GetFileNameWithoutExtension(path);

        /// <summary>Tên layout gộp: "&lt;tên block&gt; - &lt;tên layout nguồn&gt;".</summary>
        public static string LayoutNameFor(string blockName, string sourceLayout) => blockName + " - " + sourceLayout;

        /// <summary>
        /// Danh sách file nguồn: gộp <paramref name="paths"/> với mọi *.dwg trong <paramref name="folder"/> (không đệ quy),
        /// bỏ trùng (không phân biệt hoa thường, theo đường dẫn đầy đủ), bỏ chính bản vẽ đích, giữ thứ tự: danh sách tay trước,
        /// rồi file trong thư mục theo tên. <paramref name="skipped"/> nhận lý do của từng mục bị bỏ.
        /// </summary>
        public static List<string> ResolveInputs(IEnumerable<string>? paths, string? folder, IEnumerable<string> folderFiles, string? targetPath, List<string> skipped)
        {
            var result = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var target = string.IsNullOrWhiteSpace(targetPath) ? null : Full(targetPath!);

            void Add(string raw)
            {
                var path = raw.Trim();
                if (path.Length == 0)
                {
                    return;
                }

                if (!path.EndsWith(".dwg", StringComparison.OrdinalIgnoreCase))
                {
                    skipped.Add($"Bỏ qua \"{path}\": không phải file .dwg.");
                    return;
                }

                // Một file có thể vừa ở danh sách tay vừa ở thư mục: chỉ lấy (hoặc chỉ báo bỏ qua) một lần.
                var full = Full(path);
                if (!seen.Add(full))
                {
                    return;
                }

                if (target != null && string.Equals(full, target, StringComparison.OrdinalIgnoreCase))
                {
                    skipped.Add($"Bỏ qua \"{path}\": là chính bản vẽ đang gộp vào.");
                    return;
                }

                result.Add(path);
            }

            foreach (var path in paths ?? Enumerable.Empty<string>())
            {
                Add(path ?? string.Empty);
            }

            if (!string.IsNullOrWhiteSpace(folder))
            {
                foreach (var file in folderFiles.OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase))
                {
                    Add(file);
                }
            }

            return result;
        }

        /// <summary>
        /// Vị trí chèn từng bản vẽ. <c>Original</c>: mọi block chèn tại (0,0) — toạ độ gốc giữ nguyên.
        /// <c>Row</c>: xếp từ trái sang phải, đáy hộp bao thẳng hàng tại <paramref name="baseY"/>, bản đầu bắt đầu ở
        /// <paramref name="startX"/>, giữa hai bản cách <paramref name="gap"/>. Bản vẽ rỗng (extents null) chèn tại
        /// điểm hiện tại và không chiếm chỗ.
        /// </summary>
        public static List<CombinePlacement> Place(
            IReadOnlyList<(string BlockName, CombineExtents? Extents)> drawings, string arrangement, double startX, double baseY, double gap)
        {
            var result = new List<CombinePlacement>();
            if (string.Equals(arrangement, "Original", StringComparison.OrdinalIgnoreCase))
            {
                result.AddRange(drawings.Select(d => new CombinePlacement(d.BlockName, 0, 0)));
                return result;
            }

            var cursor = startX;
            foreach (var (name, extents) in drawings)
            {
                if (extents is null)
                {
                    result.Add(new CombinePlacement(name, cursor, baseY));
                    continue;
                }

                result.Add(new CombinePlacement(name, cursor - extents.MinX, baseY - extents.MinY));
                cursor += extents.Width + gap;
            }

            return result;
        }

        private static string Full(string path)
        {
            try
            {
                return Path.GetFullPath(path);
            }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException)
            {
                return path;
            }
        }
    }
}
