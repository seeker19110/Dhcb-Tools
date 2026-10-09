using System;
using System.Collections.Generic;
using DhcbTools.Shared.Logic.Ai;
using Newtonsoft.Json.Linq;

namespace DhcbTools.Shared.Hosting
{
    /// <summary>
    /// Đuôi file mà một lệnh gửi qua Bridge được phép đọc/ghi. Chỉ áp cho Bridge — Ribbon và batch đêm là
    /// người dùng tự điền, không qua đây.
    /// <para>
    /// Vì sao cần: agent AI điền config, và agent có thể bị prompt injection (text trong bản vẽ, thuyết minh,
    /// tên phần tử). <c>ParameterExport {"outputPath": "…\Start Menu\Programs\Startup\x.bat"}</c> ghi một file
    /// CSV mà nội dung lấy từ tên phần tử — tên chứa <c>&amp;lệnh&amp;</c> là chạy khi đăng nhập. Không chốt thư mục
    /// (batch đêm và kỹ sư vẫn xuất ra ổ mạng tuỳ ý) mà chốt đuôi: không lệnh nào của DHCB đọc hay ghi file
    /// chạy được.
    /// </para>
    /// </summary>
    public static class BridgePathPolicy
    {
        /// <summary>Mọi định dạng mà lệnh DHCB thật sự đọc/ghi. Không có đuôi cũng được — file không đuôi không chạy được.</summary>
        public static readonly ISet<string> AllowedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".csv", ".tsv", ".txt", ".md", ".json", ".jsonl", ".html", ".htm", ".xml", ".ids",
            ".ifc", ".ifczip", ".bcf", ".bcfzip", ".dwg", ".dxf", ".rvt", ".rfa", ".rte", ".nwc",
            ".pdf", ".png", ".jpg", ".jpeg", ".xlsx",
        };

        /// <summary>
        /// Trường đường dẫn file đầu tiên trong <paramref name="config"/> có giá trị không an toàn, dạng
        /// <c>tênTrường = "giá trị": lý do</c>; <c>null</c> nếu không có. Trường thư mục (<c>…Folder</c>) không xét:
        /// lệnh tự đặt tên file trong đó.
        /// </summary>
        public static string? FirstUnsafe(CommandDescriptor descriptor, JObject config)
        {
            foreach (var property in config.Properties())
            {
                if (!IsFilePathField(descriptor, property.Name))
                {
                    continue;
                }

                var values = property.Value is JArray array ? (IEnumerable<JToken>)array : new[] { property.Value };
                foreach (var value in values)
                {
                    // Giá trị không phải chuỗi: BridgeCommitGuard/lệnh tự từ chối (fail closed), không phải việc ở đây.
                    if (value.Type != JTokenType.String)
                    {
                        continue;
                    }

                    var path = value.Value<string>() ?? string.Empty;
                    var problem = Problem(path);
                    if (problem != null)
                    {
                        return property.Name + " = \"" + path + "\": " + problem;
                    }
                }
            }

            return null;
        }

        /// <summary>Lý do <paramref name="path"/> không được dùng, hoặc <c>null</c> nếu dùng được (kể cả chuỗi rỗng).</summary>
        public static string? Problem(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            var normalized = path.Replace('/', '\\');
            if (normalized.StartsWith(@"\\?\", StringComparison.Ordinal)
                || normalized.StartsWith(@"\\.\", StringComparison.Ordinal))
            {
                return "đường dẫn thiết bị / namespace mở rộng không được dùng qua Bridge.";
            }

            // ':' ngoài "C:" là alternate data stream ("a.exe:x.csv") hoặc đường dẫn thiết bị ("\\?\", "\\.\") —
            // không lệnh nào cần, và nó lách được phép so đuôi ở dưới.
            var colon = path.IndexOf(':');
            if (colon >= 0 && (colon != 1 || path.IndexOf(':', 2) >= 0))
            {
                return "chứa ':' ngoài ký tự ổ đĩa (alternate data stream / đường dẫn thiết bị).";
            }

            // Tự tách tên file thay vì Path.GetExtension: net48 ném với ký tự lạ, và Windows bỏ dấu chấm/khoảng
            // trắng cuối tên — "x.bat." hay "x.bat " thành "x.bat" trên đĩa.
            var name = path.Substring(Math.Max(path.LastIndexOf('\\'), path.LastIndexOf('/')) + 1).TrimEnd('.', ' ');
            var dot = name.LastIndexOf('.');
            if (dot < 0)
            {
                return null;
            }

            var extension = name.Substring(dot);
            return AllowedExtensions.Contains(extension)
                ? null
                : "đuôi \"" + extension + "\" không thuộc định dạng lệnh DHCB đọc/ghi (" + string.Join(" ", AllowedExtensions) + ").";
        }

        private static bool IsFilePathField(CommandDescriptor descriptor, string name)
        {
            foreach (var field in descriptor.Fields)
            {
                if (string.Equals(field.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return field.Kind == FieldKind.FilePath;
                }
            }

            // Trường chưa khai trong catalog vẫn tới được lệnh (JSON dư bị báo E-CONFIG-UNKNOWN ở lệnh, nhưng đừng dựa vào đó).
            return FieldKindGuess.Of(name) == FieldKind.FilePath;
        }
    }
}
