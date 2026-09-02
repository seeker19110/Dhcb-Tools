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

        /// <summary>
        /// ElementId của những phần tử lệnh vừa tạo/sửa/xoá (giai đoạn 10.2).
        /// <para>
        /// Chỉ có số đếm thì agent biết "đã đổi 37 phần tử" mà không chỉ được ra phần tử nào, nên
        /// không tự kiểm được kết quả và cũng không zoom cho kỹ sư xem được. Có danh sách này thì
        /// khép được vòng: chạy lệnh → <c>/query show_elements</c> hoặc <c>element_geometry</c> trên
        /// đúng những id vừa đổi → <c>snapshot</c> để nhìn.
        /// </para>
        /// <para>Giới hạn <see cref="MaxChangedIds"/> phần tử để một lệnh sửa cả vạn phần tử không
        /// làm phình response; <see cref="AffectedCount"/> vẫn là con số đầy đủ.</para>
        /// </summary>
        public List<long> ChangedIds { get; } = new List<long>();

        /// <summary>Số ElementId tối đa đưa vào <see cref="ChangedIds"/>.</summary>
        public const int MaxChangedIds = 500;

        /// <summary>Ghi nhận một phần tử vừa thay đổi. Bỏ qua khi đã đủ <see cref="MaxChangedIds"/>.</summary>
        public CommandResult WithChanged(long elementId)
        {
            if (ChangedIds.Count < MaxChangedIds)
            {
                ChangedIds.Add(elementId);
            }

            return this;
        }

        /// <summary>Ghi nhận nhiều phần tử vừa thay đổi.</summary>
        public CommandResult WithChanged(IEnumerable<long> elementIds)
        {
            foreach (var id in elementIds)
            {
                if (ChangedIds.Count >= MaxChangedIds)
                {
                    break;
                }

                ChangedIds.Add(id);
            }

            return this;
        }

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
