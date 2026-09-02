using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DhcbTools.Shared.Logic.Testing
{
    /// <summary>
    /// Một ca kiểm thử chạy <b>bên trong</b> Revit: gọi một lệnh Core thật trên model mẫu rồi đối chiếu
    /// <c>CommandResult</c> với kỳ vọng khai báo sẵn.
    /// <para>
    /// Vì sao khai báo kỳ vọng thay vì so file vàng nguyên vẹn: <c>Summary</c> chứa số đếm phụ thuộc model,
    /// nên so từng ký tự sẽ đỏ mỗi lần đổi model mẫu. Kỳ vọng dạng "phải thành công", "ít nhất N phần tử",
    /// "có chứa chuỗi này" bắt đúng lỗi thật mà không giòn.
    /// </para>
    /// </summary>
    public sealed class TestCase
    {
        /// <summary>Tên ca kiểm — hiện trong báo cáo. Rỗng thì lấy theo <see cref="Command"/>.</summary>
        [JsonProperty("name")]
        public string Name { get; set; } = string.Empty;

        /// <summary>Tên lệnh Core, đúng như trong <c>CommandCatalog</c>.</summary>
        [JsonProperty("command")]
        public string Command { get; set; } = string.Empty;

        /// <summary>Config truyền cho lệnh.</summary>
        [JsonProperty("config")]
        public JObject Config { get; set; } = new JObject();

        /// <summary>
        /// Cho phép ca này ghi vào model. Mặc định false: runner ép <c>dryRun = true</c> để chạy được
        /// nhiều lần trên cùng model mẫu mà không làm bẩn nó.
        /// </summary>
        [JsonProperty("allowWrite")]
        public bool AllowWrite { get; set; }

        /// <summary>Bỏ qua ca này (kèm lý do trong <see cref="SkipReason"/>).</summary>
        [JsonProperty("skip")]
        public bool Skip { get; set; }

        [JsonProperty("skipReason")]
        public string SkipReason { get; set; } = string.Empty;

        [JsonProperty("expect")]
        public TestExpectation Expect { get; set; } = new TestExpectation();

        /// <summary>Tên hiển thị: <see cref="Name"/> nếu có, không thì tên lệnh.</summary>
        public string DisplayName => string.IsNullOrWhiteSpace(Name) ? Command : Name;
    }

    /// <summary>Bộ ca kiểm cho một model mẫu.</summary>
    public sealed class TestSuite
    {
        [JsonProperty("name")]
        public string Name { get; set; } = "DHCB in-Revit tests";

        /// <summary>Đường dẫn model mẫu (.rvt) — batch runner mở file này trước khi chạy.</summary>
        [JsonProperty("model")]
        public string Model { get; set; } = string.Empty;

        [JsonProperty("cases")]
        public List<TestCase> Cases { get; set; } = new List<TestCase>();

        public static TestSuite Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                throw new ArgumentException("Nội dung bộ test rỗng.", nameof(json));
            }

            var suite = JsonConvert.DeserializeObject<TestSuite>(json)
                        ?? throw new InvalidOperationException("Không đọc được bộ test.");

            for (var i = 0; i < suite.Cases.Count; i++)
            {
                if (string.IsNullOrWhiteSpace(suite.Cases[i].Command))
                {
                    throw new InvalidOperationException($"Ca kiểm thứ {i + 1} thiếu trường \"command\".");
                }
            }

            return suite;
        }

        public static TestSuite Load(string path) => Parse(File.ReadAllText(path));
    }
}
