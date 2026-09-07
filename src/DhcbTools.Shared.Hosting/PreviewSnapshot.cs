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
                using (var stream = File.OpenRead(path))
                using (var sha = SHA256.Create())
                    files[path] = Convert.ToBase64String(sha.ComputeHash(stream));
            }
            return new JObject { ["config"] = config, ["files"] = files }.ToString(Formatting.None);
        }
    }
}
