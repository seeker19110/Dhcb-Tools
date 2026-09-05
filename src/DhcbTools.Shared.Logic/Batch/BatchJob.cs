using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DhcbTools.Shared.Logic.Batch
{
    /// <summary>Chế độ lưu file sau khi chạy xong các step.</summary>
    public enum SaveMode
    {
        /// <summary>Đóng không lưu — an toàn tuyệt đối, dùng cho báo cáo/xuất bản.</summary>
        None,

        /// <summary>Lưu đè lên file gốc.</summary>
        Save,

        /// <summary>Lưu bản sao vào <see cref="BatchJob.OutputFolder"/>, không đụng bản gốc (mặc định).</summary>
        SaveAs,
    }

    public sealed class BatchJobFile
    {
        [JsonProperty("path")]
        public string Path { get; set; } = string.Empty;

        [JsonProperty("detachFromCentral")]
        public bool DetachFromCentral { get; set; }

        [JsonProperty("worksets")]
        public List<string> Worksets { get; set; } = new List<string>();

        /// <summary>Nếu có, chỉ chạy các step này cho file này (theo tên lệnh); rỗng = chạy tất cả.</summary>
        [JsonProperty("onlySteps")]
        public List<string> OnlySteps { get; set; } = new List<string>();
    }

    public sealed class BatchJobStep
    {
        [JsonProperty("command")]
        public string Command { get; set; } = string.Empty;

        [JsonProperty("config")]
        public JObject Config { get; set; } = new JObject();

        /// <summary>Bỏ qua step này nếu step trước lỗi (mặc định vẫn chạy tiếp).</summary>
        [JsonProperty("skipIfPreviousFailed")]
        public bool SkipIfPreviousFailed { get; set; }
    }

    /// <summary>File job của batch runner (mục 1.2). Đọc bằng <see cref="Load"/>; token được thay lúc chạy.</summary>
    /// <summary>
    /// Mục 11.3 — gói bàn giao dựng sau khi job chạy xong: <c>ban-giao.html</c> + <c>ban-giao.json</c> trong
    /// <c>outputFolder</c>, gom IFC/PDF/danh mục bản vẽ với băm SHA-256, kiểm chuỗi băm nhật ký, kiểm IFC
    /// (và IDS nếu khai) và ô xác nhận của chủ đầu tư theo Điều 11 NĐ 207/2026.
    /// </summary>
    public sealed class HandoverOptions
    {
        [JsonProperty("enabled")]
        public bool Enabled { get; set; } = true;

        [JsonProperty("projectName")]
        public string ProjectName { get; set; } = string.Empty;

        [JsonProperty("owner")]
        public string Owner { get; set; } = string.Empty;

        [JsonProperty("contractor")]
        public string Contractor { get; set; } = string.Empty;

        /// <summary>File IDS để kiểm mọi IFC trong thư mục đầu ra; rỗng = chỉ kiểm cấu trúc IFC.</summary>
        [JsonProperty("idsPath")]
        public string? IdsPath { get; set; }

        /// <summary>Bộ quy tắc IFC (JSON) cho <c>--verify-ifc</c>; rỗng = bộ mặc định.</summary>
        [JsonProperty("ifcSpecPath")]
        public string? IfcSpecPath { get; set; }
    }

    public sealed class BatchJob
    {
        [JsonProperty("name")]
        public string Name { get; set; } = "DHCB batch";

        /// <summary>"revit" (mặc định) hoặc "autocad".</summary>
        [JsonProperty("app")]
        public string App { get; set; } = "revit";

        [JsonProperty("revitVersion")]
        public int RevitVersion { get; set; } = 2024;

        [JsonProperty("stopOnError")]
        public bool StopOnError { get; set; }

        [JsonProperty("saveMode")]
        public SaveMode SaveMode { get; set; } = SaveMode.SaveAs;

        [JsonProperty("outputFolder")]
        public string OutputFolder { get; set; } = string.Empty;

        /// <summary>
        /// Vẫn lưu (Save/SaveAs) dù có step lỗi trên file đó. Mặc định false: một step lỗi giữa chừng có thể
        /// để model ở trạng thái nửa vời (transaction đã rollback nhưng step trước đã ghi), lưu đè lên file
        /// gốc lúc đó là mất đường lui. Bật khi cố ý muốn giữ phần đã làm được.
        /// </summary>
        [JsonProperty("saveOnError")]
        public bool SaveOnError { get; set; }

        /// <summary>Phiên bản DWG khi SAVEAS trong accoreconsole (2000/2004/2007/2010/2013/2018). Mặc định 2018.</summary>
        [JsonProperty("dwgVersion")]
        public string DwgVersion { get; set; } = "2018";

        [JsonProperty("files")]
        public List<BatchJobFile> Files { get; set; } = new List<BatchJobFile>();

        [JsonProperty("steps")]
        public List<BatchJobStep> Steps { get; set; } = new List<BatchJobStep>();

        [JsonProperty("tokens")]
        public Dictionary<string, string> Tokens { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Gói bàn giao sau khi chạy (null = không dựng).</summary>
        [JsonProperty("handover")]
        public HandoverOptions? Handover { get; set; }

        public static BatchJob Parse(string json)
        {
            BatchJob? job;
            try
            {
                job = JsonConvert.DeserializeObject<BatchJob>(json);
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException("File job không phải JSON hợp lệ: " + ex.Message, ex);
            }

            if (job == null)
            {
                throw new InvalidDataException("File job rỗng hoặc không phải JSON object.");
            }

            var errors = job.Validate();
            if (errors.Count > 0)
            {
                throw new InvalidDataException("File job không hợp lệ: " + string.Join("; ", errors));
            }

            return job;
        }

        public static BatchJob Load(string path) => Parse(File.ReadAllText(path));

        public string ToJson() => JsonConvert.SerializeObject(this, Formatting.Indented);

        /// <summary>Kiểm tra cấu hình trước khi chạy (mã thoát 2 nếu có lỗi).</summary>
        public List<string> Validate()
        {
            var errors = new List<string>();
            if (Files.Count == 0)
            {
                errors.Add("'files' rỗng");
            }

            if (Steps.Count == 0)
            {
                errors.Add("'steps' rỗng");
            }

            for (var i = 0; i < Files.Count; i++)
            {
                if (string.IsNullOrWhiteSpace(Files[i].Path))
                {
                    errors.Add("files[" + i + "] thiếu 'path'");
                }
            }

            for (var i = 0; i < Steps.Count; i++)
            {
                if (string.IsNullOrWhiteSpace(Steps[i].Command))
                {
                    errors.Add("steps[" + i + "] thiếu 'command'");
                }
            }

            if (SaveMode == SaveMode.SaveAs && string.IsNullOrWhiteSpace(OutputFolder))
            {
                errors.Add("saveMode=SaveAs cần 'outputFolder'");
            }

            if (Handover != null && Handover.Enabled && string.IsNullOrWhiteSpace(OutputFolder))
            {
                errors.Add("'handover' cần 'outputFolder' — gói bàn giao gom file từ đó");
            }

            if (!App.Equals("revit", StringComparison.OrdinalIgnoreCase) && !App.Equals("autocad", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add("'app' phải là revit hoặc autocad");
            }

            return errors;
        }

        private JobTokenContext MakeContext(string outputFolder, string filePath, DateTime runTime)
        {
            var ctx = new JobTokenContext(outputFolder, System.IO.Path.GetFileNameWithoutExtension(filePath), runTime);
            foreach (var kv in Tokens)
            {
                ctx.Extra[kv.Key] = kv.Value;
            }
            return ctx;
        }

        /// <summary>Thư mục đầu ra sau khi thay token ngày giờ.</summary>
        public string ResolveOutputFolder(DateTime runTime) => JobTokens.Expand(OutputFolder, MakeContext(string.Empty, string.Empty, runTime));

        /// <summary>Các step áp cho một file (lọc theo <see cref="BatchJobFile.OnlySteps"/>).</summary>
        public IEnumerable<BatchJobStep> StepsFor(BatchJobFile file)
        {
            foreach (var step in Steps)
            {
                if (file.OnlySteps.Count == 0)
                {
                    yield return step;
                    continue;
                }

                foreach (var only in file.OnlySteps)
                {
                    if (string.Equals(only, step.Command, StringComparison.OrdinalIgnoreCase))
                    {
                        yield return step;
                        break;
                    }
                }
            }
        }

        /// <summary>Config của step sau khi thay token cho một file cụ thể — chuỗi JSON để vỏ deserialize.</summary>
        public string ExpandStepConfig(BatchJobStep step, string outputFolder, string filePath, DateTime runTime)
        {
            var ctx = MakeContext(outputFolder, filePath, runTime);
            var clone = (JObject)step.Config.DeepClone();
            JobTokens.ExpandIn(clone, ctx);
            return clone.ToString(Formatting.None);
        }

    }
}
