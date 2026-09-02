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

        /// <summary>Tên model đã pull sẵn. Mặc định qwen3 — dòng ổn nhất cho tool-calling/JSON trong benchmark 2026 (gemma3 không hỗ trợ tool).</summary>
        [JsonProperty("model")]
        public string Model { get; set; } = "qwen3:8b";

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
        public string? Generate(string prompt, string? system = null, bool jsonMode = false) => Generate(prompt, system, jsonMode ? (JToken)"json" : null);

        /// <summary>
        /// Sinh với <c>format</c> = JSON Schema (structured outputs của Ollama): ép cú pháp hợp lệ ở tầng token — cách đáng tin
        /// hơn <c>format:"json"</c> với model 7–9B. Schema càng phẳng, càng ít trường bắt buộc càng tốt.
        /// </summary>
        public string? GenerateStructured(string prompt, JObject schema, string? system = null) => Generate(prompt, system, schema);

        private string? Generate(string prompt, string? system, JToken? format)
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

                if (format != null)
                {
                    body["format"] = format;
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

            var text = GenerateStructured(prompt.ToString(), MappingSchema);
            if (text == null)
            {
                return null;
            }

            return ParseMappingJson(text, revitTypes, rejected);
        }

        /// <summary>JSON Schema phẳng cho kết quả map layer.</summary>
        public static readonly JObject MappingSchema = JObject.Parse(@"{
          ""type"": ""object"",
          ""properties"": {
            ""mappings"": { ""type"": ""array"", ""items"": { ""type"": ""object"",
              ""properties"": { ""layer"": {""type"":""string""}, ""revitType"": {""type"":[""string"",""null""]}, ""confidence"": {""type"":""number""}, ""reason"": {""type"":""string""} },
              ""required"": [""layer"", ""revitType"", ""confidence""] } }
          },
          ""required"": [""mappings""]
        }");

        /// <summary>JSON Schema cho việc chọn lệnh trong danh sách ứng viên (mục 7.14).</summary>
        public static readonly JObject ChoiceSchema = JObject.Parse(@"{
          ""type"": ""object"",
          ""properties"": { ""command"": {""type"":[""string"",""null""]}, ""confidence"": {""type"":""number""}, ""reason"": {""type"":""string""} },
          ""required"": [""command"", ""confidence""]
        }");

        /// <summary>
        /// Nhờ model CHỌN một lệnh trong ≤ 8 ứng viên (đã lọc bằng heuristic) — giới hạn thực tế của model local với nhiều tool.
        /// Trả null nếu tắt/lỗi/model chọn ngoài danh sách. Không bao giờ sinh lệnh mới.
        /// </summary>
        public string? ChooseCommand(string userText, IReadOnlyList<CommandDescriptor> candidates, out double confidence, out string? reason)
        {
            confidence = 0;
            reason = null;
            if (!IsUsable || candidates.Count == 0)
            {
                return null;
            }

            var prompt = new StringBuilder();
            prompt.Append("Người dùng (kỹ sư BIM) nói: \"").Append(userText).Append("\"\n\n")
                  .Append("Chọn ĐÚNG MỘT lệnh phù hợp nhất trong danh sách, hoặc null nếu không lệnh nào khớp. Chỉ trả tên lệnh y nguyên.\n");
            foreach (var c in candidates.Take(8))
            {
                prompt.Append("- ").Append(c.Name).Append(": ").Append(c.Description).Append('\n');
            }

            var text = GenerateStructured(prompt.ToString(), ChoiceSchema);
            if (text == null)
            {
                return null;
            }

            try
            {
                var json = JObject.Parse(text);
                var name = json["command"]?.Type == JTokenType.Null ? null : json["command"]?.ToString();
                confidence = json["confidence"]?.Type == JTokenType.Float || json["confidence"]?.Type == JTokenType.Integer ? json["confidence"]!.Value<double>() : 0.5;
                reason = json["reason"]?.ToString();
                if (name == null)
                {
                    return null;
                }

                var match = candidates.FirstOrDefault(c => c.Matches(name));
                return match?.Name; // ngoài danh sách → null (whitelist)
            }
            catch (JsonException)
            {
                return null;
            }
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
