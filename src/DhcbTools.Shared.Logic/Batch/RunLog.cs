using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
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
            if (string.IsNullOrWhiteSpace(line))
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

        public static void Append(string path, RunLogEntry entry)
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.AppendAllText(path, Serialize(entry) + "\n", new UTF8Encoding(false));
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
