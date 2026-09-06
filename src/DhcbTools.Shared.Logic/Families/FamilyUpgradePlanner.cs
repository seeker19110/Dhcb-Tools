using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DhcbTools.Shared.Logic.Families
{
    /// <summary>Một file .rfa cần nâng cấp: nguồn và đích (giữ nguyên cây thư mục con).</summary>
    public sealed class FamilyUpgradeItem
    {
        public FamilyUpgradeItem(string source, string target)
        {
            Source = source;
            Target = target;
        }

        public string Source { get; }

        public string Target { get; }

        public string Name => Path.GetFileName(Source);
    }

    /// <summary>
    /// Phần quyết định thuần của <c>FamilyUpgrade</c> (§63): ghép đường dẫn đích, chặn ghi đè lên thư mục nguồn,
    /// và câu chữ. Nâng cấp .rfa là MỘT CHIỀU — file đã lưu bằng Revit mới thì Revit cũ không mở lại được — nên
    /// mọi đường ở đây đều ghi sang thư mục khác, không bao giờ đụng bản gốc.
    /// </summary>
    public static class FamilyUpgradePlanner
    {
        /// <summary>Đích = OutputFolder + đường dẫn tương đối của nguồn so với SourceFolder (giữ cây thư mục con).</summary>
        public static string TargetPath(string sourceFolder, string outputFolder, string sourceFile)
        {
            var rel = Relative(sourceFolder, sourceFile);
            return Path.Combine(outputFolder, rel);
        }

        internal static string Relative(string root, string file)
        {
            var r = Normalize(root);
            var f = Normalize(file);
            if (r.Length > 0 && f.StartsWith(r + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                return f.Substring(r.Length + 1);
            }

            return Path.GetFileName(f);
        }

        private static string Normalize(string path)
            => Path.GetFullPath(path.Replace('/', Path.DirectorySeparatorChar)).TrimEnd(Path.DirectorySeparatorChar);

        /// <summary>
        /// Thư mục đích không được nằm trong (hoặc trùng) thư mục nguồn: nếu nằm trong thì lượt chạy sau sẽ quét
        /// đúng file mình vừa ghi ra và nâng cấp chồng lên nhau. Trả null khi hợp lệ, ngược lại trả câu lỗi.
        /// </summary>
        public static string? ValidateFolders(string sourceFolder, string outputFolder)
        {
            if (string.IsNullOrWhiteSpace(sourceFolder)) return "Thiếu sourceFolder — thư mục chứa .rfa cần nâng cấp.";
            if (string.IsNullOrWhiteSpace(outputFolder)) return "Thiếu outputFolder — nâng cấp .rfa là một chiều nên phải ghi sang thư mục khác.";

            var s = Normalize(sourceFolder);
            var o = Normalize(outputFolder);
            if (string.Equals(s, o, StringComparison.OrdinalIgnoreCase)
                || o.StartsWith(s + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                return $"outputFolder ({o}) nằm trong sourceFolder ({s}) — sẽ nâng cấp chồng lên file mình vừa ghi. Chọn thư mục khác.";
            }

            return null;
        }

        /// <summary>Danh sách việc theo thứ tự đường dẫn; <paramref name="maxFiles"/> &gt; 0 thì cắt bớt.</summary>
        public static List<FamilyUpgradeItem> Plan(string sourceFolder, string outputFolder, IEnumerable<string> sourceFiles, int maxFiles)
        {
            var items = sourceFiles
                .Where(f => f.EndsWith(".rfa", StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .Select(f => new FamilyUpgradeItem(f, TargetPath(sourceFolder, outputFolder, f)))
                .ToList();

            return maxFiles > 0 && items.Count > maxFiles ? items.Take(maxFiles).ToList() : items;
        }

        public static string NoFilesMessage(string sourceFolder, bool recursive)
            => $"Không thấy file .rfa nào trong {sourceFolder}" + (recursive ? " (đã tìm cả thư mục con)." : " (không tìm thư mục con — đặt recursive:true nếu cần).");

        public static string PreviewLine(FamilyUpgradeItem item)
            => $"Sẽ nâng cấp {item.Name} → {item.Target}";

        public static string PreviewSummary(int count, string revitVersion)
            => $"[Xem trước] Sẽ nâng cấp {count} family sang định dạng Revit {revitVersion}. Bản gốc giữ nguyên.";

        public static string SkipExistingMessage(FamilyUpgradeItem item)
            => $"{item.Name}: đã có {item.Target} — bỏ qua (overwrite=false).";

        /// <summary>Revit không mở được family lưu từ bản mới hơn; câu lỗi nói thẳng thay vì để nguyên chữ của API.</summary>
        public static string OpenFailedMessage(FamilyUpgradeItem item, string revitVersion, string reason)
            => $"{item.Name}: không mở được bằng Revit {revitVersion} ({reason}). Thường là file đã lưu từ bản Revit mới hơn — nâng cấp phải chạy từ bản thấp lên bản cao.";

        public static string DoneSummary(int upgraded, int skipped, int failed, string outputFolder, string revitVersion)
        {
            var s = $"Đã nâng cấp {upgraded} family sang Revit {revitVersion} → {outputFolder}";
            if (skipped > 0) s += $"; bỏ qua {skipped}";
            if (failed > 0) s += $"; lỗi {failed}";
            return s + ".";
        }
    }
}
