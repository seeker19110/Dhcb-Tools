using System.Diagnostics;
using System.Text;
using DhcbTools.Shared.Logic;
using DhcbTools.Shared.Logic.Ai;
using DhcbTools.Shared.Logic.AsBuilt;
using DhcbTools.Shared.Logic.Batch;
using DhcbTools.Shared.Logic.Handover;
using DhcbTools.Shared.Logic.Ids;
using DhcbTools.Shared.Logic.Ifc;
using Newtonsoft.Json.Linq;

namespace DhcbTools.BatchRunner;
/// <summary>Đường Revit: pending-job + journal → Revit.exe → add-in chạy → batch-done.json.</summary>
public static partial class Program
{
    // ── Revit: pending-job + journal → Revit.exe → add-in chạy → batch-done.json ───────────────────────

    /// <summary>Mục 7.13 (RevitBatchProcessor): phiên bản Revit theo header file — nhiều phiên bản khác nhau → dùng cao nhất và cảnh báo.</summary>
    internal static int ResolveRevitVersion(BatchJob job, Action<string> log)
    {
        var detected = job.Files.Select(f => (f.Path, Version: RvtFileInfo.DetectVersion(f.Path))).Where(t => t.Version.HasValue).Select(t => (t.Path, t.Version!.Value)).ToList();
        if (detected.Count == 0)
        {
            return job.RevitVersion;
        }

        var max = detected.Max(d => d.Item2);
        foreach (var (path, v) in detected.Where(d => d.Item2 != max))
        {
            log($"Cảnh báo: {path} lưu bằng Revit {v}, sẽ mở bằng Revit {max} (nâng cấp trong phiên, không ghi ngược nếu saveMode=None/SaveAs).");
        }

        if (max != job.RevitVersion)
        {
            log($"Phiên bản Revit theo file: {max} (job ghi {job.RevitVersion}).");
        }

        return max;
    }

    private static int RunRevit(BatchJob job, Options opts, string runLog)
    {
        var version = opts.AutoDetectVersion ? ResolveRevitVersion(job, Console.WriteLine) : job.RevitVersion;
        var revitExe = opts.RevitExe ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Autodesk", "Revit " + version, "Revit.exe");
        if (!File.Exists(revitExe))
        {
            Console.Error.WriteLine("Không tìm thấy Revit.exe: " + revitExe + " (dùng --revit-exe).");
            return 2;
        }

        var dhcbDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DHCB");
        Directory.CreateDirectory(dhcbDir);
        var pending = Path.Combine(dhcbDir, "pending-job.json");
        var done = Path.Combine(dhcbDir, "batch-done.json");
        var errorFile = Path.Combine(dhcbDir, "batch-error.txt");

        // Revit chạy bằng journal CHỈ nạp add-in có .addin nằm cùng thư mục với journal (Autodesk cố ý,
        // để chạy kiểm thử hồi quy không bị add-in lạ xen vào). Không có file này thì add-in bị bỏ qua
        // hoàn toàn: không lỗi, không hộp thoại, Revit chỉ ngồi im tới hết giờ.
        // Kiểm TRƯỚC khi ghi pending-job.json: bản cũ ghi pending rồi mới kiểm, và nhánh "chưa cài add-in"
        // thoát luôn — file nằm lại, lần sau kỹ sư mở Revit (năm khác, có add-in) thì Revit âm thầm chạy job
        // của đêm hôm trước rồi tự đóng giữa phiên làm việc.
        var addinDll = FindInstalledAddin(version);
        if (addinDll is null)
        {
            Console.Error.WriteLine("Không tìm thấy DhcbTools.Revit.dll đã cài cho Revit " + version + ".");
            Console.Error.WriteLine("  Đã tìm trong:");
            foreach (var dir in AddinSearchDirs(version))
            {
                Console.Error.WriteLine("    " + dir);
            }
            Console.Error.WriteLine("  Cài add-in trước (installer hoặc scripts/run-in-revit-tests.ps1).");
            return 2;
        }

        File.Delete(done);
        // batch-error.txt của lần trước mà còn nằm đó thì chẩn đoán bên dưới ("add-in ĐÃ chạy") nói sai.
        try { File.Delete(errorFile); } catch (Exception ex) { Console.Error.WriteLine("Không xoá được " + errorFile + " cũ: " + ex.Message); }

        var journal = Path.Combine(dhcbDir, "dhcb-batch.txt");
        File.WriteAllText(journal, RevitJournal(), new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(dhcbDir, "DhcbTools.Revit.addin"),
            RevitAddinManifest.Build(addinDll), new UTF8Encoding(false));
        Console.WriteLine("Add-in cho batch: " + addinDll);

        // pending-job.json là thứ DUY NHẤT khiến add-in chạy job khi Revit khởi động — ghi ngay trước khi mở
        // Revit, và mọi nhánh không mở được Revit phải dọn nó (xem TryDeletePending).
        File.WriteAllText(pending, new JObject
        {
            ["jobPath"] = Path.GetFullPath(opts.JobPath),
            ["runLogPath"] = Path.GetFullPath(runLog),
            ["maxMinutes"] = opts.MaxMinutes,
            ["dryRun"] = opts.DryRun,
        }.ToString());

        Console.WriteLine($"Mở Revit: {revitExe}");
        Process? started;
        try
        {
            started = Process.Start(new ProcessStartInfo(revitExe, "\"" + journal + "\" /nosplash") { UseShellExecute = false });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception || ex is InvalidOperationException || ex is IOException)
        {
            // --revit-exe trỏ vào file không chạy được (sai kiến trúc, bị chặn quyền, không phải .exe).
            TryDeletePending(pending);
            Console.Error.WriteLine("Không khởi động được Revit (" + revitExe + "): " + ex.Message);
            return 2;
        }

        using var process = started;
        if (process is null)
        {
            TryDeletePending(pending);
            Console.Error.WriteLine("Không khởi động được Revit.");
            return 2;
        }

        var deadline = DateTime.Now.AddMinutes(opts.MaxMinutes + 15);
        while (!process.HasExited && !File.Exists(done) && DateTime.Now < deadline)
        {
            Thread.Sleep(5000);
        }

        if (!process.HasExited)
        {
            if (!process.WaitForExit(60_000))
            {
                Console.Error.WriteLine("Revit không thoát sau khi xong — kết thúc tiến trình.");
                try { process.Kill(true); } catch { /* tiến trình có thể vừa thoát */ }
                // Không chờ thì Revit.exe zombie giữ .slog của model, đêm sau không mở được file — như đường AutoCAD.
                try
                {
                    if (!process.WaitForExit(10_000))
                    {
                        Console.Error.WriteLine("Revit vẫn chưa thoát sau 10 giây kể từ Kill — kiểm tra tiến trình treo trước lượt chạy sau.");
                    }
                }
                catch { /* WaitForExit ném khi tiến trình đã biến mất */ }
            }
        }

        if (!File.Exists(done))
        {
            // Báo cho đúng chỗ. Bản trước luôn đổ tại "add-in chưa cài" nên lần chạy thật đầu tiên đi tìm
            // nhầm hướng mất nhiều thời gian, trong khi add-in đã cài đúng và thủ phạm là một hộp thoại
            // của Revit chặn ngay lúc khởi động (hết hạn license, cập nhật, đăng nhập…).
            var addinLog = Path.ChangeExtension(Path.GetFullPath(runLog), ".addin.log");
            var addinRan = File.Exists(addinLog) || File.Exists(errorFile);

            Console.Error.WriteLine("Add-in không báo hoàn thành (batch-done.json).");
            if (addinRan)
            {
                Console.Error.WriteLine("  Add-in ĐÃ chạy nhưng không kết thúc — xem " + errorFile + " và " + addinLog + ".");
            }
            else
            {
                Console.Error.WriteLine("  Add-in CHƯA từng chạy: không có " + addinLog + ".");
                Console.Error.WriteLine("  Thường gặp nhất là Revit dừng ở một hộp thoại lúc khởi động (license hết hạn,");
                Console.Error.WriteLine("  cập nhật, đăng nhập) — journal không tắt được loại hộp thoại này.");
                Console.Error.WriteLine("  Mở Revit " + version + " bằng tay một lần, xử lý hộp thoại đang chờ, rồi chạy lại.");
                Console.Error.WriteLine("  Kiểm chứng: mở journal Revit vừa ghi và tìm dòng 'ADialog::doModal start'.");
            }

            TryDeletePending(pending);
            // Ghi cả vào log của lượt: report.html chỉ đọc log, mà Revit sập sau 3/10 file thì log chỉ có dòng
            // xanh của 3 file đã chạy — bảng báo cáo trông như một đêm trọn vẹn.
            try
            {
                RunLog.Append(runLog, new RunLogEntry
                {
                    File = "*",
                    Command = "Revit",
                    Success = false,
                    Summary = "Revit không báo hoàn thành (thiếu batch-done.json) — các file chưa có dòng nào trong log là CHƯA chạy."
                              + (addinRan ? " Xem " + errorFile + " và " + addinLog + "." : " Add-in chưa từng chạy (hộp thoại lúc khởi động?)."),
                });
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Không ghi được dòng lỗi vào " + runLog + ": " + ex.Message);
            }

            return 1;
        }

        var exit = (int?)JObject.Parse(File.ReadAllText(done))["exitCode"] ?? 1;
        Console.WriteLine($"Revit báo mã thoát {exit}.");
        return exit;
    }

    /// <summary>
    /// Xoá <c>pending-job.json</c> bằng mọi giá. Để sót file này là lần mở Revit tương tác kế tiếp sẽ
    /// âm thầm chạy lại job rồi tự đóng Revit — đúng cái bẫy đã sửa ở phía add-in (giai đoạn 8.1), nhưng
    /// runner cũng phải tự dọn cho trường hợp add-in chưa từng chạy.
    /// </summary>
    private static void TryDeletePending(string pending)
    {
        if (!File.Exists(pending))
        {
            return;
        }

        try
        {
            File.Delete(pending);
        }
        catch (Exception)
        {
            try
            {
                // Xoá không được (đang bị khoá) thì đổi tên — hook chỉ tìm đúng tên pending-job.json.
                File.Move(pending, pending + ".stale-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("CẢNH BÁO: không dọn được " + pending + " (" + ex.Message + "). "
                    + "Xoá tay trước khi mở Revit, nếu không Revit sẽ tự chạy lại job này rồi tự đóng.");
            }
        }
    }

    /// <summary>Nơi Revit tìm add-in của người dùng và của toàn máy.</summary>
    private static IEnumerable<string> AddinSearchDirs(int version)
    {
        yield return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Autodesk", "Revit", "Addins", version.ToString());

        yield return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Autodesk", "Revit", "Addins", version.ToString());
    }

    /// <summary>Đường dẫn DhcbTools.Revit.dll đã cài, hoặc null nếu chưa cài.</summary>
    private static string? FindInstalledAddin(int version)
    {
        foreach (var dir in AddinSearchDirs(version))
        {
            var dll = Path.Combine(dir, "DhcbTools.Revit.dll");
            if (File.Exists(dll))
            {
                return dll;
            }
        }

        return null;
    }

    /// <summary>
    /// Journal khởi động Revit cho batch. Nội dung nằm ở <see cref="RevitJournalGen"/> trong Shared.Logic
    /// để có test — một dòng thừa trong journal làm hỏng cả vòng batch mà không lỗi biên dịch nào bắt được.
    /// </summary>
    internal static string RevitJournal() => RevitJournalGen.Build();
}
