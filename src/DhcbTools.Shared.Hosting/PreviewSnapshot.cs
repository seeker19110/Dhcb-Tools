using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DhcbTools.Shared.Hosting
{
    /// <summary>Dấu vết cấu hình và nội dung file đã xem trước; không chỉ dựa vào mtime/kích thước.</summary>
    public static class PreviewSnapshot
    {
        public static string Capture(string config, IEnumerable<string> paths)
        {
            var files = new JObject();
            foreach (var path in paths.Where(p => !string.IsNullOrWhiteSpace(p))
                         .Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase)
                         .OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            {
                if (!File.Exists(path))
                {
                    files[path] = JValue.CreateNull();
                    continue;
                }
                files[path] = HashFile(path);
            }
            return new JObject { ["config"] = config, ["files"] = files }.ToString(Formatting.None);
        }

        /// <summary>
        /// File đầu vào thường đang mở trong AutoCAD/Revit (giữ quyền ghi). <c>File.OpenRead</c> đòi FileShare.Read
        /// nên đụng handle ghi đó và ném IOException — DrawingCombine với DWG đang mở từng chỉ trả E-COMMIT-UNKNOWN.
        /// Đọc chung ReadWrite|Delete; nếu ứng dụng khoá hẳn thì lùi về dấu vết kích thước + thời điểm ghi.
        /// </summary>
        private static string HashFile(string path)
        {
            try
            {
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                           FileShare.ReadWrite | FileShare.Delete))
                using (var sha = SHA256.Create())
                    return Convert.ToBase64String(sha.ComputeHash(stream));
            }
            catch (IOException)
            {
                var info = new FileInfo(path);
                return "locked:" + info.Length + ":" + info.LastWriteTimeUtc.Ticks;
            }
        }
    }
}
