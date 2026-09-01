using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace DhcbTools.Shared.Logic.Batch
{
    /// <summary>Ngữ cảnh thay token khi chạy batch: thư mục đầu ra, tên file đang xử lý, thời điểm chạy.</summary>
    public sealed class JobTokenContext
    {
        public JobTokenContext(string outputFolder, string fileName, DateTime runTime)
        {
            OutputFolder = outputFolder ?? string.Empty;
            FileName = fileName ?? string.Empty;
            RunTime = runTime;
        }

        public string OutputFolder { get; }

        /// <summary>Tên file KHÔNG đuôi.</summary>
        public string FileName { get; }

        public DateTime RunTime { get; }

        /// <summary>Token bổ sung do người dùng khai báo trong job (ví dụ {projectCode}).</summary>
        public Dictionary<string, string> Extra { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Thay token trong chuỗi của file job (mục 1.2): <c>{outputFolder}</c>, <c>{fileName}</c>, <c>{yyyy-MM-dd}</c>,
    /// <c>{HH-mm}</c> và mọi mẫu ngày giờ .NET khác trong ngoặc nhọn, cộng token tuỳ chỉnh.
    /// Không phân biệt hoa thường với token tên; mẫu ngày giờ giữ nguyên hoa thường (M ≠ m).
    /// Token không nhận ra được giữ nguyên để người đọc log thấy chỗ sai.
    /// </summary>
    public static class JobTokens
    {
        private static readonly Regex TokenPattern = new Regex(@"\{([^{}]+)\}", RegexOptions.Compiled);

        private static readonly Regex DateTokenPattern = new Regex(@"^[yMdHhmsf\-_:. ]+$", RegexOptions.Compiled);

        public static string Expand(string? text, JobTokenContext context)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            return TokenPattern.Replace(text!, match =>
            {
                var key = match.Groups[1].Value;

                if (key.Equals("outputFolder", StringComparison.OrdinalIgnoreCase))
                {
                    return context.OutputFolder;
                }

                if (key.Equals("fileName", StringComparison.OrdinalIgnoreCase))
                {
                    return FileNaming.Sanitize(context.FileName);
                }

                if (context.Extra.TryGetValue(key, out var extra))
                {
                    return extra;
                }

                if (DateTokenPattern.IsMatch(key) && ContainsLetter(key))
                {
                    try
                    {
                        return context.RunTime.ToString(key, CultureInfo.InvariantCulture);
                    }
                    catch (FormatException)
                    {
                        return match.Value;
                    }
                }

                return match.Value;
            });
        }

        private static bool ContainsLetter(string key)
        {
            foreach (var c in key)
            {
                if (char.IsLetter(c))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
