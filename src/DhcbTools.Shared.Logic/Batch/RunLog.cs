using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using DhcbTools.Shared.Logic;
using DhcbTools.Shared.Logic.Evidence;
using Newtonsoft.Json;

namespace DhcbTools.Shared.Logic.Batch
{
    /// <summary>Một dòng trong <c>run.jsonl</c> (mục 1.4): kết quả của một step trên một file.</summary>
    public sealed class RunLogEntry
    {
        [JsonProperty("time")]
        public DateTime Time { get; set; } = DateTime.Now;

        [JsonProperty("file")]
        public string File { get; set; } = string.Empty;

        [JsonProperty("command")]
        public string Command { get; set; } = string.Empty;

        [JsonProperty("success")]
        public bool Success { get; set; }

        [JsonProperty("affected")]
        public int Affected { get; set; }

        [JsonProperty("summary")]
        public string Summary { get; set; } = string.Empty;

        [JsonProperty("messages")]
        public List<string> Messages { get; set; } = new List<string>();

        [JsonProperty("errors")]
        public List<string> Errors { get; set; } = new List<string>();

        [JsonProperty("elapsedMs")]
        public long ElapsedMs { get; set; }

        /// <summary>Step bị bỏ qua (file không mở được, hết giờ…) — khác với chạy và lỗi.</summary>
        [JsonProperty("skipped")]
        public bool Skipped { get; set; }

        /// <summary>
        /// Băm của dòng ngay trước trong cùng file — mắt xích nối chuỗi (mục 11.5). Do
        /// <see cref="RunLog.Append"/> đặt; dòng đầu tiên mang <see cref="HashChain.Genesis"/>.
        /// </summary>
        [JsonProperty("prevHash", NullValueHandling = NullValueHandling.Ignore)]
        public string? PrevHash { get; set; }

        /// <summary>
        /// SHA-256 của chính dòng này, tính trên phần đứng trước trường <c>hash</c>. Do
        /// <see cref="RunLog.Append"/> đặt và ghi ra **cuối dòng**; không tự gán.
        /// </summary>
        [JsonProperty("hash", NullValueHandling = NullValueHandling.Ignore)]
        public string? Hash { get; set; }
    }

    /// <summary>Ghi/đọc log dòng-JSON. Mỗi dòng một object, UTF-8 không BOM, append được từ nhiều lần chạy.</summary>
    public static class RunLog
    {
        private static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            Formatting = Formatting.None,
            DateFormatHandling = DateFormatHandling.IsoDateFormat,
        };

        public static string Serialize(RunLogEntry entry) => JsonConvert.SerializeObject(entry, Settings);

        public static RunLogEntry? Deserialize(string? line)
        {
            if (StringGuard.IsBlank(line))
            {
                return null;
            }

            try
            {
                return JsonConvert.DeserializeObject<RunLogEntry>(line);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        /// <summary>
        /// Ghi thêm một dòng, đã gắn sẵn chuỗi băm (mục 11.5). Đây là **điểm ghi duy nhất** của cả batch
        /// Revit lẫn AutoCAD, nên gắn dấu vết ở đây là phủ hết mọi đường ghi mà không sửa chỗ gọi nào.
        /// </summary>
        public static void Append(string path, RunLogEntry entry)
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            entry.PrevHash = LastHash(path) ?? HashChain.Genesis;
            entry.Hash = null;
            var payload = Serialize(entry);
            entry.Hash = HashChain.ComputeHash(payload);

            File.AppendAllText(path, HashChain.Seal(payload, entry.Hash) + "\n", new UTF8Encoding(false));
        }

        /// <summary>
        /// Băm của dòng cuối cùng đang có trong file; null khi file chưa có hoặc dòng cuối chưa mang dấu vết
        /// (log cũ) — khi đó dòng mới bắt đầu lại từ <see cref="HashChain.Genesis"/> và
        /// <see cref="VerifyFile"/> sẽ báo <see cref="ChainStatus.NotSealed"/> ở đúng dòng cũ đó.
        /// <para>
        /// Đọc cả file mỗi lần ghi, không đọc đuôi: một lần chạy batch là vài chục dòng (9 file × 10 step
        /// ở đêm batch thật), nên cái giá không đáng kể, còn đọc đuôi theo byte thì phải tự xử lý ký tự
        /// UTF-8 bị cắt đôi — đổi một lỗi không có lấy một lỗi khó thấy.
        /// </para>
        /// </summary>
        internal static string? LastHash(string path)
        {
            if (!File.Exists(path))
            {
                return null;
            }

            string? last = null;
            foreach (var line in File.ReadAllLines(path))
            {
                if (!StringGuard.IsBlank(line))
                {
                    last = line;
                }
            }

            return HashChain.TrySplit(last, out _, out var hash) ? hash : null;
        }

        /// <summary>Kiểm chuỗi băm của một file log — trả lời "dòng nào bị sửa", không chỉ "có bị sửa không".</summary>
        public static ChainVerification VerifyFile(string path)
        {
            var lines = File.Exists(path) ? File.ReadAllLines(path) : new string[0];
            return HashChain.Verify(lines, line => Deserialize(line)?.PrevHash);
        }

        /// <summary>Đọc toàn bộ file, bỏ qua dòng hỏng (ghi dở khi crash) thay vì ném lỗi.</summary>
        public static List<RunLogEntry> ReadAll(string path)
        {
            var entries = new List<RunLogEntry>();
            if (!File.Exists(path))
            {
                return entries;
            }

            foreach (var line in File.ReadAllLines(path))
            {
                var entry = Deserialize(line);
                if (entry != null)
                {
                    entries.Add(entry);
                }
            }

            return entries;
        }

        /// <summary>Mã thoát theo mục 1.4: 0 mọi step thành công; 1 có step lỗi hoặc bị bỏ qua.</summary>
        public static int ExitCode(IEnumerable<RunLogEntry> entries)
        {
            foreach (var e in entries)
            {
                if (!e.Success || e.Skipped)
                {
                    return 1;
                }
            }
            return 0;
        }
    }
}
