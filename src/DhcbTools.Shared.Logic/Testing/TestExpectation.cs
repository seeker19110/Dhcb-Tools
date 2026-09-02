using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace DhcbTools.Shared.Logic.Testing
{
    /// <summary>
    /// Cái mà runner quan sát được sau khi chạy một lệnh Core. Là bản sao thuần của <c>CommandResult</c>
    /// (nằm ở Shared.Hosting) để tầng đánh giá này vẫn không phụ thuộc Revit/AutoCAD và test được trên CI.
    /// </summary>
    public sealed class TestObservation
    {
        public bool Success { get; set; }

        public string Summary { get; set; } = string.Empty;

        public List<string> Messages { get; set; } = new List<string>();

        public List<string> Errors { get; set; } = new List<string>();

        public int AffectedCount { get; set; }

        public long ElapsedMs { get; set; }

        /// <summary>Exception ném ra khỏi lệnh (nếu có) — lỗi nặng hơn là <c>Success=false</c>.</summary>
        public string? Exception { get; set; }
    }

    /// <summary>
    /// Kỳ vọng của một ca kiểm. Mọi trường đều tuỳ chọn; trường nào bỏ trống thì không kiểm.
    /// Mặc định <see cref="Success"/> = true vì phần lớn ca chỉ cần "chạy được, không ném".
    /// </summary>
    public sealed class TestExpectation
    {
        [JsonProperty("success")]
        public bool? Success { get; set; } = true;

        /// <summary>Số phần tử bị ảnh hưởng tối thiểu.</summary>
        [JsonProperty("minAffected")]
        public int? MinAffected { get; set; }

        /// <summary>Số phần tử bị ảnh hưởng tối đa.</summary>
        [JsonProperty("maxAffected")]
        public int? MaxAffected { get; set; }

        /// <summary>Summary phải chứa (không phân biệt hoa thường).</summary>
        [JsonProperty("summaryContains")]
        public List<string> SummaryContains { get; set; } = new List<string>();

        /// <summary>Ít nhất một dòng Messages phải chứa.</summary>
        [JsonProperty("messagesContain")]
        public List<string> MessagesContain { get; set; } = new List<string>();

        /// <summary>Không dòng nào của Messages/Errors được chứa các chuỗi này.</summary>
        [JsonProperty("neverContains")]
        public List<string> NeverContains { get; set; } = new List<string>();

        /// <summary>Danh sách Errors phải rỗng.</summary>
        [JsonProperty("noErrors")]
        public bool NoErrors { get; set; }

        /// <summary>
        /// Ngưỡng thời gian (ms). Đây là lưới bắt hồi quy hiệu năng — ví dụ SleeveAuto từng dựng
        /// collector toàn model trong vòng lặp và vượt timeout 30 s của Bridge.
        /// </summary>
        [JsonProperty("maxMs")]
        public long? MaxMs { get; set; }

        /// <summary>File phải tồn tại sau khi chạy (đường dẫn đã thay token).</summary>
        [JsonProperty("filesExist")]
        public List<string> FilesExist { get; set; } = new List<string>();

        /// <summary>
        /// Đánh giá quan sát so với kỳ vọng. Trả về danh sách lý do trượt; rỗng = đạt.
        /// <paramref name="fileExists"/> được tiêm vào để tầng này không chạm hệ thống file (test được).
        /// </summary>
        public IReadOnlyList<string> Evaluate(TestObservation observed, Func<string, bool>? fileExists = null)
        {
            if (observed is null)
            {
                throw new ArgumentNullException(nameof(observed));
            }

            var failures = new List<string>();

            if (observed.Exception != null)
            {
                failures.Add("lệnh ném exception: " + observed.Exception);
                return failures; // Đã ném thì mọi kiểm tra khác vô nghĩa.
            }

            if (Success.HasValue && observed.Success != Success.Value)
            {
                failures.Add($"mong Success={Success.Value} nhưng nhận {observed.Success}"
                             + (observed.Success ? string.Empty : " — " + observed.Summary));
            }

            if (MinAffected.HasValue && observed.AffectedCount < MinAffected.Value)
            {
                failures.Add($"mong ảnh hưởng ≥ {MinAffected.Value} nhưng chỉ {observed.AffectedCount}");
            }

            if (MaxAffected.HasValue && observed.AffectedCount > MaxAffected.Value)
            {
                failures.Add($"mong ảnh hưởng ≤ {MaxAffected.Value} nhưng tới {observed.AffectedCount}");
            }

            foreach (var needle in SummaryContains.Where(n => !string.IsNullOrEmpty(n)))
            {
                if (observed.Summary.IndexOf(needle, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    failures.Add($"Summary không chứa \"{needle}\" (thực tế: {observed.Summary})");
                }
            }

            foreach (var needle in MessagesContain.Where(n => !string.IsNullOrEmpty(n)))
            {
                if (!observed.Messages.Any(m => m.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    failures.Add($"không dòng Messages nào chứa \"{needle}\"");
                }
            }

            foreach (var needle in NeverContains.Where(n => !string.IsNullOrEmpty(n)))
            {
                var hit = observed.Messages.Concat(observed.Errors)
                    .FirstOrDefault(m => m.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0);
                if (hit != null)
                {
                    failures.Add($"không được xuất hiện \"{needle}\" nhưng có: {hit}");
                }
            }

            if (NoErrors && observed.Errors.Count > 0)
            {
                failures.Add($"mong không có Errors nhưng có {observed.Errors.Count}: {observed.Errors[0]}");
            }

            if (MaxMs.HasValue && observed.ElapsedMs > MaxMs.Value)
            {
                failures.Add($"chạy {observed.ElapsedMs} ms, vượt ngưỡng {MaxMs.Value} ms");
            }

            if (FilesExist.Count > 0)
            {
                var probe = fileExists ?? (_ => false);
                foreach (var file in FilesExist.Where(f => !string.IsNullOrWhiteSpace(f)))
                {
                    if (!probe(file))
                    {
                        failures.Add($"không thấy file kết quả \"{file}\"");
                    }
                }
            }

            return failures;
        }
    }
}
