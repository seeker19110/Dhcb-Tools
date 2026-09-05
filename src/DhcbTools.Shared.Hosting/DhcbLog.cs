using System;
using System.IO;
using System.Text;

namespace DhcbTools.Shared.Hosting
{
    /// <summary>
    /// Log file dùng chung cho vỏ Revit/AutoCAD. Trước đây cả hai Bridge đặt <c>Log = _ => { }</c> và không có
    /// chỗ nào khác ghi log, nên một lần sập trong phiên tương tác chỉ để lại TaskDialog mà kỹ sư bấm cho mất —
    /// không có gì để gửi kèm khi báo lỗi.
    /// <para>
    /// File: <c>%APPDATA%\DHCB\logs\&lt;app&gt;-&lt;yyyy-MM-dd&gt;.log</c>. Không bao giờ ném ra ngoài: log hỏng
    /// không được phép làm hỏng lệnh đang chạy.
    /// </para>
    /// </summary>
    public static class DhcbLog
    {
        private static readonly object Gate = new object();

        /// <summary>Số ngày giữ file log; file cũ hơn bị xoá khi khởi động.</summary>
        public const int RetentionDays = 30;

        public static string DefaultDirectory =>
            Path.Combine(BridgeTokenStore.DefaultDirectory, "logs");

        /// <summary>Đường dẫn file log của hôm nay cho một ứng dụng ("Revit", "AutoCAD", "BatchRunner").</summary>
        public static string PathFor(string app) =>
            Path.Combine(DefaultDirectory, app + "-" + DateTime.Now.ToString("yyyy-MM-dd") + ".log");

        /// <summary>Ghi một dòng kèm dấu thời gian. Nuốt mọi lỗi IO.</summary>
        public static void Write(string app, string message)
        {
            try
            {
                Directory.CreateDirectory(DefaultDirectory);
                var line = DateTime.Now.ToString("HH:mm:ss.fff") + "  " + message + Environment.NewLine;
                lock (Gate)
                {
                    File.AppendAllText(PathFor(app), line, Encoding.UTF8);
                }
            }
            catch (Exception)
            {
                // Không có chỗ nào báo tiếp; im lặng là đúng ở đây vì log chỉ là phụ trợ.
            }
        }

        /// <summary>Ghi một exception kèm ngữ cảnh (stack trace đầy đủ để dán vào issue).</summary>
        public static void Error(string app, string context, Exception exception) =>
            Write(app, "LỖI " + context + ": " + exception);

        /// <summary>Xoá file log cũ hơn <see cref="RetentionDays"/> ngày. Gọi một lần lúc khởi động.</summary>
        /// <param name="deleteFile">
        /// Bước xoá, tiêm được để test nhánh "file đang bị khoá" mà không cần dựng một file thật không xoá
        /// được (chỉ Windows mới khoá file đang mở); null = <see cref="File.Delete"/>.
        /// </param>
        public static void Prune(string app, Action<string>? deleteFile = null)
        {
            try
            {
                if (!Directory.Exists(DefaultDirectory))
                {
                    return;
                }

                var cutoff = DateTime.Now.AddDays(-RetentionDays);
                foreach (var file in Directory.GetFiles(DefaultDirectory, app + "-*.log"))
                {
                    try
                    {
                        if (File.GetLastWriteTime(file) < cutoff)
                        {
                            (deleteFile ?? File.Delete)(file);
                        }
                    }
                    catch (Exception)
                    {
                        // File đang bị khoá — bỏ qua, lần khởi động sau thử lại.
                    }
                }
            }
            catch (Exception)
            {
                // Dọn log không thành công không được phép chặn khởi động add-in.
            }
        }
    }
}
