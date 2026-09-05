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
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                // File đang bị khoá/đọc dở, hoặc không có quyền đọc: coi như chưa cấu hình,
                // không làm hỏng lệnh đang chạy.
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
        /// <summary>Giới hạn thân phản hồi đọc vào (1 MB) — model local trả lời dài bất thường không được phép nuốt hết RAM.</summary>
        public const int MaxResponseBytes = 1024 * 1024;

        /// <summary>Độ dài tối đa của <c>reason</c> do model sinh — chuỗi dài hơn bị cắt để không tràn log/UI.</summary>
        public const int MaxReasonLength = 300;

        private readonly LocalAiSettings _settings;
        private readonly Func<string, string, int, string?> _transport;

        public OllamaClient(LocalAiSettings settings)
            : this(settings, null)
        {
        }

        /// <summary>
        /// <paramref name="transport"/>: hàm (url, body JSON, timeout giây) → thân phản hồi; null → <see cref="HttpTransport"/>.
        /// Tiêm được để test toàn bộ đường parse/whitelist mà không cần Ollama thật.
        /// </summary>
        public OllamaClient(LocalAiSettings settings, Func<string, string, int, string?>? transport)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _transport = transport ?? HttpTransport;
        }

        public bool IsUsable => _settings.Enabled && _settings.IsLoopback() && !string.IsNullOrWhiteSpace(_settings.Model);

        /// <summary>
        /// Lý do lần gọi gần nhất trả null (kết nối, IO, JSON hỏng…), để lệnh gọi nói được với kỹ sư
        /// "vì sao rơi về heuristic". Null nếu lần gọi gần nhất thành công.
        /// </summary>
        public string? LastError { get; private set; }

        /// <summary>Transport mặc định: <see cref="HttpWebRequest"/> POST, đọc tối đa <see cref="MaxResponseBytes"/>.</summary>
        public static string? HttpTransport(string url, string body, int timeoutSeconds)
        {
            var request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = "POST";
            request.ContentType = "application/json";
            request.Timeout = Math.Max(5, timeoutSeconds) * 1000;
            request.ReadWriteTimeout = request.Timeout;
            var bytes = Encoding.UTF8.GetBytes(body);
            request.ContentLength = bytes.Length;
            using (var stream = request.GetRequestStream())
            {
                stream.Write(bytes, 0, bytes.Length);
            }

            using (var response = (HttpWebResponse)request.GetResponse())
            using (var reader = new StreamReader(response.GetResponseStream() ?? Stream.Null, Encoding.UTF8))
            {
                return ReadCapped(reader, MaxResponseBytes);
            }
        }

        /// <summary>Đọc tối đa <paramref name="maxChars"/> ký tự; dài hơn thì ném IOException thay vì đọc tiếp.</summary>
        internal static string ReadCapped(TextReader reader, int maxChars)
        {
            var sb = new StringBuilder();
            var buffer = new char[8192];
            int read;
            while ((read = reader.Read(buffer, 0, buffer.Length)) > 0)
            {
                if (sb.Length + read > maxChars)
                {
                    throw new IOException("Phản hồi model vượt " + (maxChars / 1024) + " KB — bỏ qua.");
                }

                sb.Append(buffer, 0, read);
            }

            return sb.ToString();
        }

        /// <summary>Sinh văn bản. Trả null nếu tắt, không loopback, hoặc lỗi (xem <see cref="LastError"/>).</summary>
        public string? Generate(string prompt, string? system = null, bool jsonMode = false) => Generate(prompt, system, jsonMode ? (JToken)"json" : null);

        /// <summary>
        /// Sinh với <c>format</c> = JSON Schema (structured outputs của Ollama): ép cú pháp hợp lệ ở tầng token — cách đáng tin
        /// hơn <c>format:"json"</c> với model 7–9B. Schema càng phẳng, càng ít trường bắt buộc càng tốt.
        /// </summary>
        public string? GenerateStructured(string prompt, JObject schema, string? system = null) => Generate(prompt, system, schema);

        private string? Generate(string prompt, string? system, JToken? format)
        {
            LastError = null;
            if (!IsUsable)
            {
                LastError = "Model local đang tắt hoặc endpoint không phải loopback.";
                return null;
            }

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

            try
            {
                var raw = _transport(_settings.Endpoint.TrimEnd('/') + "/api/generate", body.ToString(Formatting.None), _settings.TimeoutSeconds);
                if (raw == null)
                {
                    LastError = "Không nhận được phản hồi từ model local.";
                    return null;
                }

                var json = JObject.Parse(raw);
                return json["response"]?.ToString();
            }
            catch (WebException ex)
            {
                LastError = "Không kết nối được model local (" + _settings.Endpoint + "): " + ex.Message;
                return null;
            }
            catch (IOException ex)
            {
                LastError = "Lỗi đọc phản hồi model local: " + ex.Message;
                return null;
            }
            catch (JsonException ex)
            {
                LastError = "Phản hồi model local không phải JSON: " + ex.Message;
                return null;
            }
        }

        /// <summary>Đọc <c>confidence</c> chịu được model trả số dạng chuỗi, số quá lớn hoặc kiểu lạ; mặc định 0.5.</summary>
        internal static double ReadConfidence(JToken? token)
        {
            if (token == null)
            {
                return 0.5;
            }

            try
            {
                switch (token.Type)
                {
                    case JTokenType.Float:
                    case JTokenType.Integer:
                        return Clamp(token.Value<double>());
                    case JTokenType.String:
                        var text = (token.ToString() ?? string.Empty).Trim().Replace(',', '.');
                        return double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? Clamp(v) : 0.5;
                    default:
                        return 0.5;
                }
            }
            catch (Exception ex) when (ex is OverflowException || ex is FormatException || ex is InvalidCastException)
            {
                // InvalidCastException là ca thật: số nguyên JSON quá lớn cho double (Newtonsoft giữ
                // dưới dạng BigInteger) làm Value<double>() ném chứ không phải OverflowException.
                return 0.5;
            }
        }

        private static double Clamp(double v) => double.IsNaN(v) ? 0.5 : Math.Max(0.0, Math.Min(1.0, v));

        /// <summary>Cắt <c>reason</c> của model về tối đa <see cref="MaxReasonLength"/> ký tự.</summary>
        internal static string? TrimReason(JToken? token)
        {
            if (token == null || token.Type == JTokenType.Null)
            {
                return null;
            }

            var text = token.Type == JTokenType.String ? token.ToString() : token.ToString(Formatting.None);
            return text.Length <= MaxReasonLength ? text : text.Substring(0, MaxReasonLength) + "…";
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
                var cmdToken = json["command"];
                var name = cmdToken == null || cmdToken.Type == JTokenType.Null
                    ? null
                    : cmdToken.Type == JTokenType.String ? cmdToken.ToString() : cmdToken.ToString(Formatting.None);
                confidence = ReadConfidence(json["confidence"]);
                reason = TrimReason(json["reason"]);
                if (string.IsNullOrWhiteSpace(name))
                {
                    return null;
                }

                var match = candidates.FirstOrDefault(c => c.Matches(name!));
                if (match == null)
                {
                    LastError = "Model chọn lệnh ngoài danh sách: \"" + TrimReason(cmdToken) + "\".";
                }

                return match?.Name; // ngoài danh sách → null (whitelist)
            }
            catch (JsonException ex)
            {
                LastError = "Model trả JSON không đọc được: " + ex.Message;
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
                    ReadConfidence(o["confidence"]),
                    TrimReason(o["reason"]) ?? "model local")).Where(m => m.Layer.Length > 0);

                return LayerMappingSuggester.Validate(proposed, revitTypes, rejected);
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }
}
