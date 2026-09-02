using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace DhcbTools.Shared.Logic.Batch
{
    /// <summary>
    /// Đọc phiên bản Revit đã lưu file .rvt/.rte/.rfa mà KHÔNG cần Revit (mục 7.13, học từ RevitBatchProcessor):
    /// file Revit là OLE compound file, stream <c>BasicFileInfo</c> chứa chuỗi UTF-16 kiểu
    /// <c>"Format: 2024"</c> (2019+) hoặc <c>"Autodesk Revit 2018 (Build: …)"</c> (cũ). Ta quét thô 2 MB đầu thay vì
    /// parse OLE đầy đủ — đủ tin cậy cho việc chọn Revit.exe; không nhận ra thì trả null để runner dùng <c>revitVersion</c> của job.
    /// </summary>
    public static class RvtFileInfo
    {
        private static readonly Regex FormatPattern = new Regex(@"Format:\s*(20\d\d)", RegexOptions.Compiled);

        private static readonly Regex BuildPattern = new Regex(@"Autodesk Revit(?: Architecture| MEP| Structure)?\s+(20\d\d)", RegexOptions.Compiled);

        /// <summary>Phiên bản (ví dụ 2024) hoặc null.</summary>
        public static int? DetectVersion(string path, int maxBytes = 2 * 1024 * 1024)
        {
            if (!File.Exists(path))
            {
                return null;
            }

            byte[] buffer;
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                var len = (int)Math.Min(fs.Length, maxBytes);
                buffer = new byte[len];
                var read = 0;
                while (read < len)
                {
                    var n = fs.Read(buffer, read, len - read);
                    if (n <= 0) break;
                    read += n;
                }
            }

            return DetectVersion(buffer);
        }

        /// <summary>Quét bộ đệm (UTF-16LE là chính, thử cả ASCII) — tách ra để test không cần file thật.</summary>
        public static int? DetectVersion(byte[] buffer)
        {
            if (buffer == null || buffer.Length == 0)
            {
                return null;
            }

            foreach (var text in new[] { Encoding.Unicode.GetString(buffer), Encoding.ASCII.GetString(buffer) })
            {
                var m = FormatPattern.Match(text);
                if (m.Success && int.TryParse(m.Groups[1].Value, out var v1))
                {
                    return v1;
                }

                m = BuildPattern.Match(text);
                if (m.Success && int.TryParse(m.Groups[1].Value, out var v2))
                {
                    return v2;
                }
            }

            return null;
        }
    }
}
