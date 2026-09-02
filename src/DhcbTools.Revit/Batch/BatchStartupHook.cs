using System.IO;
using Autodesk.Revit.ApplicationServices;
using DhcbTools.Core.Batch;
using DhcbTools.Shared.Logic.Batch;
using Newtonsoft.Json.Linq;

namespace DhcbTools.Revit.Batch;

/// <summary>
/// Đầu kia của cái bắt tay với <c>DhcbTools.BatchRunner</c>. Runner ghi
/// <c>%APPDATA%\DHCB\pending-job.json</c> rồi mở Revit bằng journal; add-in đọc file đó lúc
/// khởi động, chạy job, ghi <c>batch-done.json</c> (kèm <c>exitCode</c>) và đóng Revit.
/// Không có file pending thì đây là phiên làm việc bình thường — hook đứng yên.
/// </summary>
internal static class BatchStartupHook
{
    private static string DhcbDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DHCB");

    private static string PendingPath => Path.Combine(DhcbDirectory, "pending-job.json");
    private static string DonePath => Path.Combine(DhcbDirectory, "batch-done.json");
    private static string ErrorPath => Path.Combine(DhcbDirectory, "batch-error.txt");

    /// <summary>Trả về true nếu đã chạy batch (khi đó Revit đang tự đóng).</summary>
    public static bool RunIfRequested(Application application)
    {
        if (!File.Exists(PendingPath))
        {
            return false;
        }

        var exitCode = 1;
        try
        {
            var request = JObject.Parse(File.ReadAllText(PendingPath));
            var jobPath = (string?)request["jobPath"];
            var runLogPath = (string?)request["runLogPath"];
            if (string.IsNullOrWhiteSpace(jobPath) || string.IsNullOrWhiteSpace(runLogPath))
            {
                throw new InvalidOperationException("pending-job.json thiếu jobPath hoặc runLogPath.");
            }

            var runner = new BatchJobRunner(application)
            {
                Log = line => File.AppendAllText(Path.ChangeExtension(runLogPath, ".addin.log"), line + Environment.NewLine),
            };

            exitCode = runner.Run(
                BatchJob.Load(jobPath!),
                runLogPath!,
                (int?)request["maxMinutes"] ?? 480,
                (bool?)request["dryRun"] ?? false);
        }
        catch (Exception ex)
        {
            // Runner chỉ đọc được batch-done.json và batch-error.txt — đừng để lỗi nào chết lặng.
            exitCode = 2;
            TryWrite(ErrorPath, ex.ToString());
        }
        finally
        {
            TryDelete(PendingPath);
            TryWrite(DonePath, new JObject { ["exitCode"] = exitCode }.ToString());
        }

        return true;
    }

    private static void TryWrite(string path, string content)
    {
        try
        {
            Directory.CreateDirectory(DhcbDirectory);
            File.WriteAllText(path, content);
        }
        catch (IOException)
        {
            // Hết cách báo ra ngoài; runner sẽ coi như add-in không hoàn thành.
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // Không xoá được thì lần sau hook chạy lại — vẫn tốt hơn là ném ra lúc khởi động.
        }
    }
}
