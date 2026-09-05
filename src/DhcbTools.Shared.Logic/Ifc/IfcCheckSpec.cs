using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace DhcbTools.Shared.Logic.Ifc
{
    /// <summary>Một quy tắc kiểm áp lên tất cả thực thể mang đúng một tên kiểu IFC.</summary>
    public sealed class IfcTypeRule
    {
        /// <summary>Tên kiểu IFC đầy đủ, ví dụ <c>IfcWallStandardCase</c>. Không suy ra lớp con.</summary>
        [JsonProperty("type")]
        public string Type { get; set; } = string.Empty;

        /// <summary>Số lượng tối thiểu; bỏ trống là không kiểm.</summary>
        [JsonProperty("minCount")]
        public int? MinCount { get; set; }

        /// <summary>Số lượng tối đa; bỏ trống là không kiểm.</summary>
        [JsonProperty("maxCount")]
        public int? MaxCount { get; set; }

        /// <summary>Số lượng phải đúng bằng — dùng khi đối chiếu với số phần tử đếm được trong mô hình.</summary>
        [JsonProperty("exactCount")]
        public int? ExactCount { get; set; }

        /// <summary>Bắt buộc có tên (tham số Name) không rỗng.</summary>
        [JsonProperty("requireName")]
        public bool RequireName { get; set; }

        /// <summary>
        /// Các thuộc tính bắt buộc, viết <c>Pset_WallCommon.IsExternal</c> để chỉ đúng bộ, hoặc chỉ
        /// <c>IsExternal</c> để chấp nhận bất kỳ bộ nào. Thuộc tính có mặt nhưng bỏ trống vẫn là thiếu:
        /// bên thẩm tra đọc file không phân biệt được "chưa điền" với "không có".
        /// </summary>
        [JsonProperty("requireProperties")]
        public List<string> RequireProperties { get; set; } = new List<string>();

        /// <summary>Bắt buộc có ít nhất một mã phân loại gán qua <c>IfcRelAssociatesClassification</c>.</summary>
        [JsonProperty("requireClassification")]
        public bool RequireClassification { get; set; }

        /// <summary>Số phần tử vi phạm được kể tên trong thông báo; phần còn lại chỉ đếm.</summary>
        [JsonProperty("listLimit")]
        public int ListLimit { get; set; } = 10;
    }

    /// <summary>
    /// Bộ quy tắc kiểm một file IFC trước khi nộp. Đây là quy tắc NỘI BỘ về đầu ra của bộ xuất —
    /// đếm đúng, không thiếu thuộc tính, không tham chiếu gãy. Yêu cầu của chủ đầu tư/thẩm tra thì
    /// khai bằng IDS (mục 11.1), không khai ở đây.
    /// </summary>
    public sealed class IfcCheckSpec
    {
        /// <summary>Tên lược đồ bắt buộc (<c>IFC4</c>, <c>IFC2X3</c>…); bỏ trống là không kiểm.</summary>
        [JsonProperty("schema")]
        public string? Schema { get; set; }

        /// <summary>Kiểm mã định danh toàn cục không rỗng và không trùng. Mặc định bật.</summary>
        [JsonProperty("requireUniqueGlobalId")]
        public bool RequireUniqueGlobalId { get; set; } = true;

        /// <summary>Kiểm mọi tham chiếu đều trỏ tới thực thể có thật. Mặc định bật.</summary>
        [JsonProperty("requireResolvedReferences")]
        public bool RequireResolvedReferences { get; set; } = true;

        /// <summary>Tổng số thực thể tối thiểu — chặn file xuất ra rỗng mà vẫn báo thành công.</summary>
        [JsonProperty("minEntities")]
        public int? MinEntities { get; set; }

        /// <summary>Quy tắc theo từng kiểu.</summary>
        [JsonProperty("rules")]
        public List<IfcTypeRule> Rules { get; set; } = new List<IfcTypeRule>();

        /// <summary>Đọc bộ quy tắc từ chuỗi JSON.</summary>
        /// <exception cref="ArgumentException">Khi JSON hỏng hoặc có quy tắc không khai <c>type</c>.</exception>
        public static IfcCheckSpec FromJson(string json)
        {
            IfcCheckSpec? spec;
            try
            {
                spec = JsonConvert.DeserializeObject<IfcCheckSpec>(json);
            }
            catch (JsonException ex)
            {
                throw new ArgumentException("File quy tắc không phải JSON hợp lệ: " + ex.Message, ex);
            }

            if (spec is null)
            {
                throw new ArgumentException("File quy tắc rỗng.");
            }

            spec.Rules ??= new List<IfcTypeRule>();
            for (var i = 0; i < spec.Rules.Count; i++)
            {
                if (string.IsNullOrWhiteSpace(spec.Rules[i].Type))
                {
                    throw new ArgumentException("Quy tắc thứ " + (i + 1) + " thiếu trường bắt buộc \"type\".");
                }

                spec.Rules[i].RequireProperties ??= new List<string>();
            }

            return spec;
        }

        /// <summary>
        /// Bộ quy tắc mặc định khi kỹ sư không đưa file nào: chỉ kiểm những thứ đúng-sai không phụ thuộc
        /// dự án — có lược đồ, có <c>IfcProject</c>, mã định danh không trùng, tham chiếu không gãy.
        /// Không đoán dự án cần bao nhiêu bức tường.
        /// </summary>
        public static IfcCheckSpec Default() => new IfcCheckSpec
        {
            MinEntities = 1,
            Rules =
            {
                new IfcTypeRule { Type = "IfcProject", MinCount = 1, MaxCount = 1 },
            },
        };
    }
}
