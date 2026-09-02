using System.Collections.Generic;
using Newtonsoft.Json;

namespace DhcbTools.Shared.Hosting
{
    /// <summary>
    /// Kết quả trả về của một lệnh Core — dùng chung cho Revit lẫn AutoCAD, cho vỏ desktop (hiển thị),
    /// HTTP Bridge (JSON) và batch runner (log). Trước đây bị nhân đôi ở hai Core (lỗi #9).
    /// </summary>
    public sealed class CommandResult
    {
        public bool Success { get; set; }

        public string Summary { get; set; } = string.Empty;

        public List<string> Messages { get; } = new List<string>();

        public List<string> Errors { get; } = new List<string>();

        /// <summary>Số phần tử/object bị ảnh hưởng.</summary>
        public int AffectedCount { get; set; }

        /// <summary>Tên cũ bên Revit — giữ để code hiện có không phải đổi; cùng giá trị với <see cref="AffectedCount"/>.</summary>
        [JsonIgnore]
        public int AffectedElementCount
        {
            get => AffectedCount;
            set => AffectedCount = value;
        }

        public static CommandResult Ok(string summary, int affected = 0) => new CommandResult
        {
            Success = true,
            Summary = summary,
            AffectedCount = affected,
        };

        public static CommandResult Fail(string summary, IEnumerable<string>? errors = null)
        {
            var result = new CommandResult { Success = false, Summary = summary };
            if (errors != null)
            {
                result.Errors.AddRange(errors);
            }
            return result;
        }

        /// <summary>Thêm một dòng cảnh báo (không đổi trạng thái Success). Trả về chính nó để nối chuỗi.</summary>
        public CommandResult WithMessage(string message)
        {
            Messages.Add(message);
            return this;
        }

        /// <summary>Thêm nhiều dòng cảnh báo.</summary>
        public CommandResult WithMessages(IEnumerable<string> messages)
        {
            Messages.AddRange(messages);
            return this;
        }
    }
}
