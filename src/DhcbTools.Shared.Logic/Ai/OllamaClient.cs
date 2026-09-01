using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DhcbTools.Shared.Logic.Ai
{
    /// <summary>Cấu hình model local (đọc từ <c>%APPDATA%\DHCB\ai.json</c>; không có file → tắt).</summary>
    public sealed class LocalAiSettings
    {
        /// <summary>Bật/tắt dùng model local. Mặc định tắt: mọi tính năng AI đều có đường heuristic không cần model.</summary>
        [JsonProperty("enabled")]
        public bool Enabled { get; set; }

        /// <summary>Endpoint Ollama (hoặc bất kỳ server tương thích API /api/generate) — luôn localhost, không ra internet.</summary>
        [JsonProperty("endpoint")]
        public string Endpoint { get; set; } = "http://127.0.0.1:11434";

        /// <summary>Tên model đã pull sẵn, ví dụ "qwen2.5:7b", "llama3.1:8b".</summary>
        [JsonProperty("model")]
        public string Model { get; set; } = "qwen2.5:7b";

        [JsonProperty("timeoutSeconds")]
        public int TimeoutSeconds { get; set; } = 120;

        public static string DefaultPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DHCB", "ai.json");

        public static LocalAiSettings Load(string? path = null)
        {
            var file = path ?? DefaultPath;
            if (!File.Exists(file))
            {
                return new LocalAiSettings();
            }

            try
            {
                return JsonConvert.DeserializeObject<LocalAiSettings>(File.ReadAllText(file)) ?? new LocalAiSettings();
            }
            catch (JsonException)
            {
                return new LocalAiSettings();
            }
        }

        /// <summary>Chỉ chấp nhận endpoint loopback — đảm bảo "offline" đúng nghĩa: dữ liệu mô hình không rời máy.</summary>
        public bool IsLoopback()
        {
            if (!Uri.TryCreate(Endpoint, UriKind.Absolute, out var uri))
            {
                return false;
            }

            return uri.IsLoopback;
        }
    }

    /// <summary>
    /// Client tối giản cho Ollama (<c>POST /api/generate</c>, không stream) — dùng <see cref="HttpWebRequest"/> để chạy được
    /// trên netstandard2.0 trong Revit net48 lẫn net8. Mọi hàm đều "best effort": lỗi kết nối → trả null, người gọi
    /// dùng đường heuristic. Không bao giờ gửi ra ngoài loopback.
    /// </summary>
    public sealed class OllamaClient
    {
        private readonly LocalAiSettings _settings;

        public OllamaClient(LocalAiSettings settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        public bool IsUsable => _settings.Enabled && _settings.IsLoopback() && !string.IsNullOrWhiteSpace(_settings.Model);

        /// <summary>Sinh văn bản. Trả null nếu tắt, không loopback, hoặc lỗi.</summary>
        public string? Generate(string prompt, string? system = null, bool jsonMode = false)
        {
            if (!IsUsable)
            {
                return null;
            }

            try
            {
                var body = new JObject
                {
                    ["model"] = _settings.Model,
                    ["prompt"] = prompt,
                    ["stream"] = false,
                    ["options"] = new JObject { ["temperature"] = 0.1 },
                };
                if (!string.IsNullOrEmpty(system))
                {
                    body["system"] = system;
                }

                if (jsonMode)
                {
                    body["format"] = "json";
                }

                var request = (HttpWebRequest)WebRequest.Create(_settings.Endpoint.TrimEnd('/') + "/api/generate");
                request.Method = "POST";
                request.ContentType = "application/json";
                request.Timeout = Math.Max(5, _settings.TimeoutSeconds) * 1000;
                request.ReadWriteTimeout = request.Timeout;
                var bytes = Encoding.UTF8.GetBytes(body.ToString(Formatting.None));
                request.ContentLength = bytes.Length;
                using (var stream = request.GetRequestStream())
                {
                    stream.Write(bytes, 0, bytes.Length);
                }

                using (var response = (HttpWebResponse)request.GetResponse())
                using (var reader = new StreamReader(response.GetResponseStream() ?? Stream.Null, Encoding.UTF8))
                {
                    var json = JObject.Parse(reader.ReadToEnd());
                    return json["response"]?.ToString();
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Nhờ model chọn type cho từng layer TRONG danh sách cho sẵn. Kết quả luôn đi qua
        /// <see cref="LayerMappingSuggester.Validate"/> — dòng bịa type bị loại.
        /// </summary>
        public List<LayerMapping>? SuggestLayerMappings(IReadOnlyList<string> layers, IReadOnlyList<string> revitTypes, List<string> rejected)
        {
            var prompt = new StringBuilder();
            prompt.Append("Bạn là kỹ sư BIM. Với mỗi layer AutoCAD dưới đây, chọn ĐÚNG MỘT Revit type trong danh sách cho sẵn ")
                  .Append("(chép nguyên văn, không bịa tên mới), kèm confidence 0-1 và lý do ngắn tiếng Việt. ")
                  .Append("Trả về JSON: {\"mappings\":[{\"layer\":\"...\",\"revitType\":\"...\",\"confidence\":0.9,\"reason\":\"...\"}]}. ")
                  .Append("Không chắc thì để revitType là null.\n\nREVIT TYPES:\n");
            foreach (var t in revitTypes)
            {
                prompt.Append("- ").Append(t).Append('\n');
            }

            prompt.Append("\nLAYERS:\n");
            foreach (var l in layers)
            {
                prompt.Append("- ").Append(l).Append('\n');
            }

            var text = Generate(prompt.ToString(), null, jsonMode: true);
            if (text == null)
            {
                return null;
            }

            return ParseMappingJson(text, revitTypes, rejected);
        }

        /// <summary>Đọc JSON mappings của model (kể cả khi bọc trong ```json). Thuần, test được.</summary>
        public static List<LayerMapping>? ParseMappingJson(string text, IReadOnlyList<string> revitTypes, List<string> rejected)
        {
            try
            {
                var start = text.IndexOf('{');
                var end = text.LastIndexOf('}');
                if (start < 0 || end <= start)
                {
                    return null;
                }

                var json = JObject.Parse(text.Substring(start, end - start + 1));
                var arr = json["mappings"] as JArray;
                if (arr == null)
                {
                    return null;
                }

                var proposed = arr.OfType<JObject>().Select(o => new LayerMapping(
                    o["layer"]?.ToString() ?? string.Empty,
                    o["revitType"]?.Type == JTokenType.Null ? null : o["revitType"]?.ToString(),
                    o["confidence"]?.Type == JTokenType.Float || o["confidence"]?.Type == JTokenType.Integer ? o["confidence"]!.Value<double>() : 0.5,
                    o["reason"]?.ToString() ?? "model local")).Where(m => m.Layer.Length > 0);

                return LayerMappingSuggester.Validate(proposed, revitTypes, rejected);
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }
}
