using System.IO;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB.Events;
using DhcbTools.Core;
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

        // Cảnh báo phát sinh NGOÀI transaction của lệnh DHCB — điển hình là lúc Revit mở model và tính
        // lại Space/hệ MEP — không có preprocessor nào của lệnh chạm tới được, nên Revit hiện hộp thoại
        // "0 failures, 0 errors, 67 warnings" và batch treo cho tới khi hết giờ. Đúng chuyện đã xảy ra
        // với model Snowdon Towers HVAC (67 cảnh báo "Space is not in a properly enclosed region"):
        // Revit ngồi im ở hộp thoại, không ca kiểm nào chạy, không log nào ghi.
        // Bắt ở mức Application là chỗ duy nhất phủ được cả những transaction không phải của mình.
        application.FailuresProcessing += OnFailuresProcessing;

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
            application.FailuresProcessing -= OnFailuresProcessing;

            // Lỗi vận hành nghiêm trọng đã sửa: TryDelete cũ chỉ bắt IOException — nếu pending-job.json
            // không xoá được (khoá bởi AV, sync OneDrive…), lần mở Revit tương tác kế tiếp sẽ ÂM THẦM
            // chạy lại batch job rồi tự đóng Revit, chiếm luôn phiên làm việc của kỹ sư. Đổi tên trước
            // (ít khả năng bị khoá hơn là xoá) rồi mới thử xoá, và không giới hạn loại exception.
            RetirePendingFile();
            TryWrite(DonePath, new JObject { ["exitCode"] = exitCode }.ToString());
        }

        return true;
    }

    /// <summary>
    /// Nuốt cảnh báo của MỌI transaction trong phiên batch (kể cả của Revit), ghi lại mô tả vào
    /// <see cref="CoreContext.SuppressedWarnings"/> để báo cáo vẫn thấy. Dùng
    /// <see cref="FailurePolicy.Silent"/> — batch không có người bấm nút, nên lỗi có sẵn cách giải
    /// quyết cũng phải tự nhận thay vì treo.
    /// </summary>
    private static void OnFailuresProcessing(object? sender, FailuresProcessingEventArgs e)
    {
        var accessor = e.GetFailuresAccessor();
        var result = new SilentFailuresPreprocessor(FailurePolicy.Silent).PreprocessFailures(accessor);
        e.SetProcessingResult(result);
    }

    /// <summary>Đổi tên <c>pending-job.json</c> thành <c>.done</c> rồi xoá — không để nó sống sót sang phiên sau.</summary>
    private static void RetirePendingFile()
    {
        var retired = PendingPath + "." + DateTime.Now.Ticks + ".done";
        try
        {
            File.Move(PendingPath, retired);
        }
        catch (Exception)
        {
            // Không đổi tên được (đã bị xoá, hoặc khoá cứng) — vẫn thử xoá thẳng file gốc bên dưới.
            retired = PendingPath;
        }

        try
        {
            File.Delete(retired);
        }
        catch (Exception)
        {
            // Xoá không được thì thôi — quan trọng nhất đã đổi tên xong nên hook lần sau không nhận
            // nhầm là còn job đang chờ (RunIfRequested chỉ nhìn đúng tên PendingPath).
        }
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
}
