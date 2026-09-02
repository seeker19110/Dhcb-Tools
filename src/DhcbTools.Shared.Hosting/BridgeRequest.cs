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

        /// <summary>
        /// Bí danh của <see cref="Params"/>. Panel, MCP server và tài liệu đều gửi <c>"config"</c>,
        /// nên nếu chỉ nhận <c>"params"</c> thì mọi tham số truy vấn bị bỏ qua trong im lặng —
        /// đó chính là lý do <c>limit</c> không có tác dụng (xin 200 vẫn trả về đủ 2.273 bản ghi).
        /// </summary>
        [JsonProperty("config")]
        public JObject? Config { get; set; }

        public string ParamsJson => (Params ?? Config)?.ToString(Formatting.None) ?? "{}";
    }

    /// <summary>Body của <c>POST /chat</c>: câu lệnh tiếng Việt cần dịch sang lệnh Core (mục 5.4).</summary>
    public sealed class BridgeChat
    {
        [JsonProperty("text")]
        public string Text { get; set; } = string.Empty;
    }
}
