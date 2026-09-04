using System.IO;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB.Events;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
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

    /// <summary>Ghi một dòng vào <c>.addin.log</c> của lần chạy (đặt trong <see cref="RunIfRequested"/>).</summary>
    private static Action<string>? _log;

    /// <summary>
    /// Hộp thoại kiểu cũ (không phải TaskDialog) được phép trả lời OK — chỉ những id đã gặp và biết chắc OK
    /// là "đọc rồi, đi tiếp", không phải "đồng ý ghi/xoá". Mọi id khác bị Cancel: đóng màn hình chờ mà không
    /// xác nhận bất cứ điều gì. Thêm id vào đây sau khi đã thấy nó trong log <c>[Hộp thoại]</c>.
    /// </summary>
    private static readonly HashSet<string> BenignOkDialogIds = new(StringComparer.OrdinalIgnoreCase)
    {
        // Chưa có id nào được xác nhận an toàn — chờ log thật. Ví dụ ứng viên: hộp thoại thông tin
        // "model was last saved in an earlier version" (chỉ báo, không hỏi gì).
    };

    /// <summary>TaskDialog đã gặp và biết chắc Ok/Close chỉ đóng thông báo, không đổi dữ liệu.</summary>
    private static readonly HashSet<string> BenignOkTaskDialogIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "TaskDialog_Views_Related_To_Analytical_Changed", // nâng cấp file cũ có phần tử analytical (GOLDVIEW 2019 → 2024)
        "TaskDialog_Missing_Third_Party_Updater",         // thiếu updater của add-in khác — không liên quan batch
    };

    private const int IdOk = 1;
    private const int IdCancel = 2;

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

        // FailuresProcessing chỉ bắt cảnh báo/lỗi trong transaction — nó KHÔNG bắt TaskDialog mà Revit
        // tự bật thẳng lúc mở file, ví dụ hộp thoại nâng cấp phiên bản "Some annotations, schedules...
        // related to analytical elements might be modified or lost during the upgrade process." Đây là
        // dạng lỗi chặn thứ hai cùng họ với FailuresProcessing (xem chú thích trên) nhưng khác cơ chế:
        // batch treo im re, CPU về 0, không log nào ghi, tới khi hết --max-minutes mới chết — lộ ra khi
        // chạy trên một file .rvt R19 thật (2019) được Revit 2024 nâng cấp lúc mở, có phần tử kết cấu
        // dạng analytical. UIApplication.DialogBoxShowing bắt được cả TaskDialog lẫn dialog kiểu cũ;
        // dựng UIApplication từ chính Application vì hook chỉ nhận được Application (không phải
        // UIControlledApplication) từ ApplicationInitialized.
        var uiApplication = new UIApplication(application);
        uiApplication.DialogBoxShowing += OnDialogBoxShowing;

        try
        {
            var request = JObject.Parse(File.ReadAllText(PendingPath));
            var jobPath = (string?)request["jobPath"];
            var runLogPath = (string?)request["runLogPath"];
            if (string.IsNullOrWhiteSpace(jobPath) || string.IsNullOrWhiteSpace(runLogPath))
            {
                throw new InvalidOperationException("pending-job.json thiếu jobPath hoặc runLogPath.");
            }

            var addinLog = Path.ChangeExtension(runLogPath, ".addin.log");
            _log = line => File.AppendAllText(addinLog, line + Environment.NewLine);
            var runner = new BatchJobRunner(application) { Log = _log };

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
            uiApplication.DialogBoxShowing -= OnDialogBoxShowing;
            _log = null;

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

    /// <summary>
    /// Đóng thay mọi TaskDialog/hộp thoại Revit tự bật ngoài transaction — batch không có ai bấm nút.
    /// Ghi lại vào <see cref="CoreContext.SuppressedWarnings"/> (cùng chỗ với cảnh báo bị nuốt ở
    /// <see cref="OnFailuresProcessing"/>) nên vẫn hiện trong dòng log của step kế tiếp, và ghi cả vào
    /// <c>.addin.log</c> để có id mà đưa vào danh sách trắng.
    /// <para>
    /// Nguyên tắc: KHÔNG bấm OK bừa. Bản cũ trả IDOK cho mọi hộp thoại kiểu cũ — với "Do you want to
    /// save changes?" hay "Overwrite?" thì OK chính là đồng ý. Chỉ id trong danh sách trắng mới được OK;
    /// còn lại Cancel (hộp thoại cũ) / Close (TaskDialog) — thoát màn hình chờ mà không xác nhận gì.
    /// </para>
    /// </summary>
    private static void OnDialogBoxShowing(object? sender, DialogBoxShowingEventArgs e)
    {
        try
        {
            if (e is TaskDialogShowingEventArgs taskDialog)
            {
                var id = taskDialog.DialogId ?? string.Empty;
                var ok = BenignOkTaskDialogIds.Contains(id);
                var text = $"[Hộp thoại tự đóng] TaskDialog \"{id}\" → {(ok ? "OK" : "Close")}: {taskDialog.Message}";
                CoreContext.SuppressedWarnings.Add(text);
                SafeLog(text);
                e.OverrideResult((int)(ok ? TaskDialogResult.Ok : TaskDialogResult.Close));
                return;
            }

            var dialogId = e.DialogId ?? string.Empty;
            var allowOk = BenignOkDialogIds.Contains(dialogId);
            var line = $"[Hộp thoại tự đóng] {dialogId} → {(allowOk ? "OK" : "Cancel")}";
            CoreContext.SuppressedWarnings.Add(line);
            SafeLog(line);
            e.OverrideResult(allowOk ? IdOk : IdCancel);
        }
        catch (Exception ex)
        {
            // Không để lỗi trong handler làm Revit chết — cùng lắm là hộp thoại vẫn hiện và batch hết giờ.
            SafeLog("[Hộp thoại] lỗi khi xử lý: " + ex.Message);
        }
    }

    private static void SafeLog(string line)
    {
        try { _log?.Invoke(line); } catch { /* log phụ trợ */ }
        DhcbTools.Shared.Hosting.DhcbLog.Write("Revit", line);
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
        catch (Exception)
        {
            // IOException, UnauthorizedAccessException (thư mục bị chặn quyền/AV)… — hết cách báo ra ngoài;
            // runner sẽ coi như add-in không hoàn thành. Tuyệt đối không ném khỏi ApplicationInitialized.
        }
    }
}
