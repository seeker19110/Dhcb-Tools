using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DhcbTools.Shared.Hosting
{
    /// <summary>Body của <c>POST /execute</c>: tên lệnh + config JSON tuỳ lệnh.</summary>
    public sealed class BridgeRequest
    {
        [JsonProperty("command")]
        public string Command { get; set; } = string.Empty;

        [JsonProperty("config")]
        public JObject? Config { get; set; }

        /// <summary>Config dưới dạng chuỗi JSON ("{}" nếu thiếu) — để vỏ deserialize sang kiểu config cụ thể.</summary>
        public string ConfigJson => Config?.ToString(Formatting.None) ?? "{}";
    }

    /// <summary>Body của <c>POST /query</c>: tên truy vấn + params thô (vỏ tự deserialize).</summary>
    public sealed class BridgeQuery
    {
        [JsonProperty("query")]
        public string Query { get; set; } = string.Empty;

        [JsonProperty("params")]
        public JObject? Params { get; set; }

        public string ParamsJson => Params?.ToString(Formatting.None) ?? "{}";
    }

    /// <summary>Body của <c>POST /chat</c>: câu lệnh tiếng Việt cần dịch sang lệnh Core (mục 5.4).</summary>
    public sealed class BridgeChat
    {
        [JsonProperty("text")]
        public string Text { get; set; } = string.Empty;
    }
}
