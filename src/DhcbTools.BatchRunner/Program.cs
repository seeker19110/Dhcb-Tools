using System.Diagnostics;
using System.Text;
using DhcbTools.Shared.Logic.Ai;
using DhcbTools.Shared.Logic.Batch;
using DhcbTools.Shared.Logic.Handover;
using DhcbTools.Shared.Logic.Ids;
using DhcbTools.Shared.Logic.Ifc;
using Newtonsoft.Json.Linq;

namespace DhcbTools.BatchRunner;

/// <summary>
/// DhcbTools.BatchRunner.exe --job jobs/nightly.json [--dry-run] [--log-dir logs] [--max-minutes 480]
///                           [--revit-exe "C:\Program Files\Autodesk\Revit 2024\Revit.exe"]
///                           [--accoreconsole "C:\Program Files\Autodesk\AutoCAD 2024\accoreconsole.exe"] [--plugin-dll path]
///                           [--report-only] [--analyze]
///                           [--verify-log logs/2026-09-04/run-013000.jsonl]
///                           [--verify-ifc xuat/toa-a.ifc [--ifc-spec configs/ifc-check.json]]
/// Mã thoát: 0 mọi step OK · 1 có step lỗi/bỏ qua · 2 lỗi cấu hình.
/// Log: logs/{yyyy-MM-dd}/run-HHmmss.jsonl (mỗi lần chạy một file); --report-only lấy lần mới nhất.
/// </summary>
public static class Program
{
    public static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        var opts = Options.Parse(args);
        if (opts is null)
        {
            Console.Error.WriteLine(Options.Usage);
            return 2;
        }

        // Kiểm log là việc độc lập với chạy job: không cần --job, không mở Revit/AutoCAD, không ghi gì.
        if (!string.IsNullOrEmpty(opts.VerifyLog))
        {
            return VerifyLog(opts.VerifyLog!);
        }

        // Kiểm file IFC cũng là việc độc lập: chỉ đọc một file văn bản, không mở Revit/AutoCAD.
        if (!string.IsNullOrEmpty(opts.VerifyIfc))
        {
            return string.IsNullOrEmpty(opts.VerifyIds)
                ? VerifyIfc(opts.VerifyIfc!, opts.IfcSpec)
                : VerifyIds(opts.VerifyIfc!, opts.VerifyIds!, opts.IdsReport);
        }

        BatchJob job;
        try
        {
            job = BatchJob.Load(opts.JobPath);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Lỗi file job: " + ex.Message);
            return 2;
        }

        var runTime = DateTime.Now;
        var logDir = Path.Combine(opts.LogDir, runTime.ToString("yyyy-MM-dd"));
        Directory.CreateDirectory(logDir);
        var report = Path.Combine(logDir, "report.html");

        // Mỗi lần chạy một file log riêng (run-HHmmss.jsonl). Bản cũ append vào run.jsonl chung của ngày:
        // chạy lại lần hai cùng ngày thừa hưởng nguyên dòng lỗi của lần đầu — mã thoát 1 mãi dù đã sửa xong,
        // và report.html trộn hai lần chạy thành một bảng không ai đọc nổi.
        string? runLog;
        if (opts.ReportOnly)
        {
            runLog = LatestRunLog(logDir);
            if (runLog is null)
            {
                Console.Error.WriteLine("Không có run-*.jsonl (hay run.jsonl) nào trong " + logDir + " để dựng báo cáo.");
                return 2;
            }

            Console.WriteLine("Dựng báo cáo từ: " + runLog);
        }
        else
        {
            runLog = Path.Combine(logDir, "run-" + runTime.ToString("HHmmss") + ".jsonl");
            var launched = job.App.Equals("autocad", StringComparison.OrdinalIgnoreCase)
                ? RunAutoCad(job, opts, runLog, runTime)
                : RunRevit(job, opts, runLog);
            if (launched != 0 && !File.Exists(runLog))
            {
                return launched;
            }
        }

        var entries = RunLog.ReadAll(runLog);
        File.WriteAllText(report, BatchReport.Render(job.Name, entries, DateTime.Now), new UTF8Encoding(false));
        Console.WriteLine($"Báo cáo: {report}");

        if (opts.Analyze)
        {
            var groups = WarningAnalyzer.Analyze(entries);
            var summary = WarningAnalyzer.Summarize(groups, job.Name);
            var summaryPath = Path.Combine(logDir, "warnings-summary.md");
            File.WriteAllText(summaryPath, summary, new UTF8Encoding(false));
            Console.WriteLine(summary);
            Console.WriteLine($"Tóm tắt: {summaryPath}");
        }

        var code = entries.Count == 0 ? 1 : RunLog.ExitCode(entries);
        Console.WriteLine($"Kết thúc, mã thoát {code}: {entries.Count(e => e.Success && !e.Skipped)} OK, {entries.Count(e => !e.Success && !e.Skipped)} lỗi, {entries.Count(e => e.Skipped)} bỏ qua.");

        if (job.Handover != null && job.Handover.Enabled)
        {
            // Gói bàn giao dựng SAU khi có mã thoát của job và không đổi mã đó: một job lỗi vẫn có gói (ghi rõ
            // bước lỗi) để người đọc thấy đêm đó thiếu gì, còn "kiểm không đạt" là nội dung của gói, không phải
            // lý do để batch báo hỏng.
            var handoverPath = BuildHandover(job, job.Handover, entries, runLog, runTime);
            Console.WriteLine($"Gói bàn giao: {handoverPath}");
        }

        return code;
    }

    /// <summary>
    /// Mục 11.3: gom đầu ra của đêm thành <c>ban-giao.html</c> + <c>ban-giao.json</c> trong <c>outputFolder</c>.
    /// Mọi phần kiểm (chuỗi băm, IFC, IDS) dùng lại đúng mã của <c>--verify-log</c>/<c>--verify-ifc</c>/<c>--verify-ids</c>.
    /// </summary>
    internal static string BuildHandover(BatchJob job, HandoverOptions options, List<RunLogEntry> entries, string runLog, DateTime runTime)
    {
        var outputFolder = job.ResolveOutputFolder(runTime);
        Directory.CreateDirectory(outputFolder);
        var input = new HandoverInput
        {
            JobName = job.Name,
            ProjectName = options.ProjectName,
            Owner = options.Owner,
            Contractor = options.Contractor,
            GeneratedAt = DateTime.Now,
            AddinVersion = AddinVersion(),
            OutputFolder = outputFolder,
            RunLogPath = runLog,
        };
        input.Entries.AddRange(entries);

        HandoverPackage.CheckRunLog(input);
        HandoverPackage.Collect(input);

        // ToList: vòng lặp thêm báo cáo IDS vào input.Files — sửa danh sách đang duyệt là ném ngay (lộ ở lần chạy thật đầu, §43).
        foreach (var ifc in input.Files.Where(f => f.Kind == "IFC").ToList())
        {
            var ifcPath = Path.Combine(outputFolder, ifc.RelativePath);
            var text = File.ReadAllText(ifcPath, Encoding.UTF8);
            IfcCheckSpec spec;
            try
            {
                spec = string.IsNullOrEmpty(options.IfcSpecPath) ? IfcCheckSpec.Default() : IfcCheckSpec.FromJson(File.ReadAllText(options.IfcSpecPath!));
            }
            catch (Exception ex) when (ex is ArgumentException || ex is IOException)
            {
                input.Checks.Add(new HandoverCheck("Kiểm IFC " + ifc.RelativePath, false, "Bộ quy tắc IFC không đọc được: " + ex.Message));
                continue;
            }

            var ifcResult = IfcChecker.Check(text, spec);
            input.Checks.Add(new HandoverCheck("Kiểm IFC " + ifc.RelativePath, ifcResult.Ok, Tail(ifcResult.Render(), 3)));

            if (!string.IsNullOrEmpty(options.IdsPath))
            {
                if (!File.Exists(options.IdsPath))
                {
                    input.Checks.Add(new HandoverCheck("Kiểm IDS " + ifc.RelativePath, false, "Không có file IDS " + options.IdsPath));
                    continue;
                }

                var xml = File.ReadAllText(options.IdsPath!, Encoding.UTF8);
                try
                {
                    var specs = IdsSpec.Parse(xml);
                    var warnings = IdsSchemaLint.Check(xml);
                    var check = IdsEvaluator.Check(specs, IfcIdsModel.Parse(text).Elements());
                    var reportName = Path.GetFileNameWithoutExtension(ifc.RelativePath) + "-ids.html";
                    var reportPath = Path.Combine(outputFolder, reportName);
                    File.WriteAllText(reportPath, IdsReport.Html(Path.GetFileName(ifcPath), options.IdsPath!, IdsReport.IfcScopeNote, check, warnings), new UTF8Encoding(true));
                    input.Checks.Add(new HandoverCheck("Kiểm IDS " + ifc.RelativePath, check.FailureCount == 0, IdsReport.Summary(check, warnings) + " → " + reportName));
                    // Chạy lại (--report-only) thì Collect đã thấy báo cáo của lần trước — không liệt kê hai lần.
                    input.Files.RemoveAll(f => f.RelativePath.Equals(reportName, StringComparison.OrdinalIgnoreCase));
                    input.Files.Add(new HandoverFile(reportName, "HTML", new FileInfo(reportPath).Length, HandoverPackage.Sha256Of(reportPath)));
                }
                catch (IdsParseException ex)
                {
                    input.Checks.Add(new HandoverCheck("Kiểm IDS " + ifc.RelativePath, false, "File IDS không dùng được: " + ex.Message));
                }
            }
        }

        var failedSteps = entries.Count(e => !e.Success && !e.Skipped);
        input.Checks.Add(new HandoverCheck(
            "Các bước của job",
            failedSteps == 0 && entries.Count > 0,
            $"{entries.Count(e => e.Success && !e.Skipped)} thành công, {failedSteps} lỗi, {entries.Count(e => e.Skipped)} bỏ qua"));

        var html = Path.Combine(outputFolder, HandoverPackage.HtmlName);
        File.WriteAllText(html, HandoverPackage.Html(input), new UTF8Encoding(true));
        File.WriteAllText(Path.Combine(outputFolder, HandoverPackage.JsonName), HandoverPackage.ToJson(input), new UTF8Encoding(false));
        return html;
    }

    private static string AddinVersion()
    {
        var attr = typeof(Program).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault();
        return attr?.InformationalVersion ?? typeof(Program).Assembly.GetName().Version?.ToString() ?? "?";
    }

    /// <summary>
    /// <c>--verify-log</c>: kiểm chuỗi băm của một file log đã ghi (mục 11.5). Mã thoát 0 nguyên vẹn ·
    /// 1 chuỗi hỏng (in ra đúng dòng) · 2 không có file. Tách khỏi đường chạy job để kiểm lại được một
    /// log 30 ngày tuổi mà không cần job, không cần Revit, không ghi thêm gì vào file đang kiểm.
    /// </summary>
    internal static int VerifyLog(string path)
    {
        if (!File.Exists(path))
        {
            Console.Error.WriteLine("Không có file log: " + path);
            return 2;
        }

        var result = RunLog.VerifyFile(path);
        Console.WriteLine(path);
        Console.WriteLine(result.Message);
        return result.Ok ? 0 : 1;
    }

    /// <summary>
    /// <c>--verify-ifc</c>: đọc lại file IFC vừa xuất và đối chiếu với bộ quy tắc (mục 11.2). Mã thoát
    /// 0 đạt · 1 có lỗi · 2 không có file hay file quy tắc hỏng. Không làm thành lệnh Core vì kiểm một
    /// file IFC không cần <c>Document</c> nào — cùng lý do với <c>--verify-log</c> ở mục 11.5, và đổi
    /// lại được thứ chạy trên CI thay vì phải chờ một vòng test trong Revit (nguyên tắc 6).
    /// </summary>
    internal static int VerifyIfc(string ifcPath, string? specPath)
    {
        if (!File.Exists(ifcPath))
        {
            Console.Error.WriteLine("Không có file IFC: " + ifcPath);
            return 2;
        }

        IfcCheckSpec spec;
        if (string.IsNullOrEmpty(specPath))
        {
            spec = IfcCheckSpec.Default();
            Console.WriteLine("Không có --ifc-spec: dùng bộ quy tắc mặc định (lược đồ, IfcProject, mã định danh, tham chiếu).");
        }
        else if (!File.Exists(specPath))
        {
            Console.Error.WriteLine("Không có file quy tắc: " + specPath);
            return 2;
        }
        else
        {
            try
            {
                spec = IfcCheckSpec.FromJson(File.ReadAllText(specPath!));
            }
            catch (ArgumentException ex)
            {
                Console.Error.WriteLine("Lỗi file quy tắc: " + ex.Message);
                return 2;
            }
        }

        // File IFC do Revit xuất là UTF-8; đọc kèm BOM để dòng ISO-10303-21 không bị lệch ký tự đầu.
        var result = IfcChecker.Check(File.ReadAllText(ifcPath, Encoding.UTF8), spec);
        Console.WriteLine(ifcPath);
        Console.WriteLine(result.Render());
        return result.Ok ? 0 : 1;
    }

    /// <summary>
    /// <c>--verify-ifc … --verify-ids &lt;file.ids&gt;</c>: kiểm <b>chính file IFC</b> theo IDS (mục 11.4) — cùng đầu
    /// vào mà IfcTester/Solibri đọc, nên đối chiếu được từng dòng với họ; và chạy trên CI không cần Revit.
    /// Mã thoát: 0 không phần tử nào không đạt · 1 có phần tử không đạt · 2 không có file hay file IDS hỏng.
    /// Specification không có phần tử nào để kiểm KHÔNG làm mã thoát thành 1 — nhưng được in ra, vì "0 không đạt"
    /// ở đó nói về bộ lọc chứ không nói về mô hình.
    /// </summary>
    internal static int VerifyIds(string ifcPath, string idsPath, string? reportPath)
    {
        if (!File.Exists(ifcPath))
        {
            Console.Error.WriteLine("Không có file IFC: " + ifcPath);
            return 2;
        }

        if (!File.Exists(idsPath))
        {
            Console.Error.WriteLine("Không có file IDS: " + idsPath);
            return 2;
        }

        var xml = File.ReadAllText(idsPath, Encoding.UTF8);
        IReadOnlyList<IdsSpecification> specifications;
        try
        {
            specifications = IdsSpec.Parse(xml);
        }
        catch (IdsParseException ex)
        {
            Console.Error.WriteLine("File IDS không dùng được: " + ex.Message);
            return 2;
        }

        var schemaWarnings = IdsSchemaLint.Check(xml);
        var model = IfcIdsModel.Parse(File.ReadAllText(ifcPath, Encoding.UTF8));
        var elements = model.Elements();
        var check = IdsEvaluator.Check(specifications, elements);

        Console.WriteLine(ifcPath);
        Console.WriteLine($"Lược đồ {model.Model.Schema}, {model.Model.Count} thực thể, {elements.Count} phần tử IDS có thể nói tới.");
        foreach (var line in IdsReport.Messages(check, schemaWarnings))
        {
            Console.WriteLine(line);
        }

        if (!string.IsNullOrEmpty(reportPath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(reportPath!)) ?? ".");
            File.WriteAllText(
                reportPath!,
                IdsReport.Html(Path.GetFileName(ifcPath), idsPath, IdsReport.IfcScopeNote, check, schemaWarnings),
                new UTF8Encoding(true));
            var csv = Path.ChangeExtension(reportPath!, ".csv");
            File.WriteAllText(csv, IdsReport.Csv(check), new UTF8Encoding(true));
        }

        Console.WriteLine(IdsReport.Summary(check, schemaWarnings) + (string.IsNullOrEmpty(reportPath) ? "." : $" → \"{reportPath}\"."));
        return check.FailureCount > 0 ? 1 : 0;
    }

    /// <summary>
    /// File log của lần chạy mới nhất trong thư mục ngày: <c>run-HHmmss.jsonl</c> lớn nhất theo tên; nếu chỉ
    /// có <c>run.jsonl</c> kiểu cũ thì dùng nó. Null khi không có gì.
    /// </summary>
    internal static string? LatestRunLog(string logDir)
    {
        if (!Directory.Exists(logDir))
        {
            return null;
        }

        var latest = Directory.GetFiles(logDir, "run-*.jsonl")
            .OrderByDescending(Path.GetFileName, StringComparer.Ordinal)
            .FirstOrDefault();
        if (latest is not null)
        {
            return latest;
        }

        var legacy = Path.Combine(logDir, "run.jsonl");
        return File.Exists(legacy) ? legacy : null;
    }

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
        File.Delete(done);
        // batch-error.txt của lần trước mà còn nằm đó thì chẩn đoán bên dưới ("add-in ĐÃ chạy") nói sai.
        try { File.Delete(errorFile); } catch (Exception ex) { Console.Error.WriteLine("Không xoá được " + errorFile + " cũ: " + ex.Message); }
        File.WriteAllText(pending, new JObject
        {
            ["jobPath"] = Path.GetFullPath(opts.JobPath),
            ["runLogPath"] = Path.GetFullPath(runLog),
            ["maxMinutes"] = opts.MaxMinutes,
            ["dryRun"] = opts.DryRun,
        }.ToString());

        var journal = Path.Combine(dhcbDir, "dhcb-batch.txt");
        File.WriteAllText(journal, RevitJournal(), new UTF8Encoding(false));

        // Revit chạy bằng journal CHỈ nạp add-in có .addin nằm cùng thư mục với journal (Autodesk cố ý,
        // để chạy kiểm thử hồi quy không bị add-in lạ xen vào). Không có file này thì add-in bị bỏ qua
        // hoàn toàn: không lỗi, không hộp thoại, Revit chỉ ngồi im tới hết giờ.
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

        File.WriteAllText(Path.Combine(dhcbDir, "DhcbTools.Revit.addin"),
            RevitAddinManifest.Build(addinDll), new UTF8Encoding(false));
        Console.WriteLine("Add-in cho batch: " + addinDll);

        Console.WriteLine($"Mở Revit: {revitExe}");
        using var process = Process.Start(new ProcessStartInfo(revitExe, "\"" + journal + "\" /nosplash") { UseShellExecute = false });
        if (process is null)
        {
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
                try { process.Kill(true); } catch { /* ignore */ }
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

    // ── AutoCAD: accoreconsole cho từng DWG ─────────────────────────────────────────────────────────

    private static int RunAutoCad(BatchJob job, Options opts, string runLog, DateTime runTime)
    {
        var console = opts.AccoreConsole ?? Directory.GetDirectories(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Autodesk"), "AutoCAD *")
            .Select(d => Path.Combine(d, "accoreconsole.exe")).FirstOrDefault(File.Exists);
        if (console is null || !File.Exists(console))
        {
            Console.Error.WriteLine("Không tìm thấy accoreconsole.exe (dùng --accoreconsole).");
            return 2;
        }

        // Ưu tiên vỏ core-only (không AcMgd) — NETLOAD chắc chắn trong accoreconsole; vỏ đầy đủ là dự phòng.
        var plugin = opts.PluginDll
                     ?? new[] { "DhcbTools.AutoCAD.Core.dll", "DhcbTools.AutoCAD.dll" }.Select(n => Path.Combine(AppContext.BaseDirectory, n)).FirstOrDefault(File.Exists)
                     ?? Path.Combine(AppContext.BaseDirectory, "DhcbTools.AutoCAD.Core.dll");
        if (!File.Exists(plugin))
        {
            Console.Error.WriteLine("Không tìm thấy DhcbTools.AutoCAD.Core.dll / DhcbTools.AutoCAD.dll (dùng --plugin-dll).");
            return 2;
        }

        var outputFolder = job.ResolveOutputFolder(runTime);
        if (!string.IsNullOrEmpty(outputFolder)) Directory.CreateDirectory(outputFolder);
        // Thư mục step/script riêng cho từng lần chạy, cùng dấu giờ với run-HHmmss.jsonl.
        var work = Path.Combine(Path.GetDirectoryName(runLog)!, "acad-steps-" + Path.GetFileNameWithoutExtension(runLog).Replace("run-", string.Empty));
        Directory.CreateDirectory(work);

        var deadline = runTime.AddMinutes(opts.MaxMinutes);
        var anyFailed = false;
        var index = 0;
        foreach (var file in job.Files)
        {
            index++;
            if (DateTime.Now > deadline)
            {
                RunLog.Append(runLog, new RunLogEntry { File = file.Path, Command = "*", Skipped = true, Summary = "Hết --max-minutes." });
                continue;
            }

            if (!File.Exists(file.Path))
            {
                RunLog.Append(runLog, new RunLogEntry { File = file.Path, Command = "Open", Success = false, Summary = "Không tìm thấy file." });
                anyFailed = true;
                if (job.StopOnError) break;
                continue;
            }

            var stepPaths = new List<string>();
            var s = 0;
            foreach (var step in job.StepsFor(file).Where(st => !st.Command.Equals("PlotPdf", StringComparison.OrdinalIgnoreCase)))
            {
                var cfg = job.ExpandStepConfig(step, outputFolder, file.Path, runTime);
                if (opts.DryRun)
                {
                    var o = JObject.Parse(cfg);
                    o["dryRun"] = true;
                    cfg = o.ToString(Newtonsoft.Json.Formatting.None);
                }

                var stepPath = Path.Combine(work, $"{index:D3}-{s++:D2}-{step.Command}.json");
                File.WriteAllText(stepPath, AcadScriptGen.StepJson(step.Command, cfg), new UTF8Encoding(false));
                stepPaths.Add(stepPath);
            }

            // Save = SAVEAS về chính file nguồn (QSAVE không có trong core console); SaveAs = bản sao trong
            // outputFolder. Cả hai đều phải trả lời prompt "replace it?" khi file đích đã có — với Save thì luôn có.
            string? saveAs = opts.DryRun ? null
                : job.SaveMode == SaveMode.SaveAs ? Path.Combine(outputFolder, Path.GetFileName(file.Path))
                : job.SaveMode == SaveMode.Save ? Path.GetFullPath(file.Path)
                : null;
            var saveTargetExists = saveAs is not null && File.Exists(saveAs);

            // Step đặc biệt "PlotPdf" (mục 7.13): không phải lệnh Core — sinh -PLOT trong script accoreconsole.
            string? plotScript = null;
            foreach (var step in job.StepsFor(file).Where(st => st.Command.Equals("PlotPdf", StringComparison.OrdinalIgnoreCase)))
            {
                var cfg = JObject.Parse(job.ExpandStepConfig(step, outputFolder, file.Path, runTime));
                var pdf = (string?)cfg["outputPath"] ?? Path.Combine(outputFolder, Path.GetFileNameWithoutExtension(file.Path) + ".pdf");
                plotScript = (plotScript ?? string.Empty) + AcadScriptGen.PlotPdf(pdf,
                    (string?)cfg["layout"] ?? "Model", (string?)cfg["paperSize"] ?? "ISO A3 (420.00 x 297.00 MM)",
                    (string?)cfg["orientation"] ?? "Landscape", (string?)cfg["plotArea"] ?? "Extents", (string?)cfg["plotStyle"] ?? "monochrome.ctb");
                RunLog.Append(runLog, new RunLogEntry { File = file.Path, Command = "PlotPdf", Success = true, Summary = "Đã xếp lệnh -PLOT → " + pdf + " (kết quả thật xem file PDF)." });
            }

            var script = Path.Combine(work, $"{index:D3}.scr");
            File.WriteAllText(script, AcadScriptGen.Build(plugin, stepPaths, saveAs, Path.GetFullPath(runLog), file.Path, plotScript, job.DwgVersion, saveTargetExists), new UTF8Encoding(false));

            Console.WriteLine($"[{index}/{job.Files.Count}] {file.Path}");
            var psi = new ProcessStartInfo(console, AcadScriptGen.Arguments(file.Path, script))
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            var startedAt = DateTime.Now;
            using var p = Process.Start(psi);
            if (p is null)
            {
                RunLog.Append(runLog, new RunLogEntry { File = file.Path, Command = "Open", Success = false, Summary = "Không khởi động được accoreconsole." });
                anyFailed = true;
                continue;
            }

            // Đọc cả stdout lẫn stderr bất đồng bộ TRƯỚC khi chờ: ReadToEnd() một ống rồi mới WaitForExit
            // treo chết khi ống kia đầy (accoreconsole in khá nhiều ra stderr), và kill-khi-quá-giờ không
            // bao giờ tới lượt vì ReadToEnd chặn vô hạn.
            var stdoutTask = p.StandardOutput.ReadToEndAsync();
            var stderrTask = p.StandardError.ReadToEndAsync();
            var timedOut = !p.WaitForExit((int)Math.Max(60_000, (deadline - DateTime.Now).TotalMilliseconds));
            if (timedOut)
            {
                try { p.Kill(true); } catch { /* ignore */ }
                try { p.WaitForExit(10_000); } catch { /* ignore */ }
                RunLog.Append(runLog, new RunLogEntry { File = file.Path, Command = "*", Success = false, Summary = "accoreconsole quá giờ — đã kết thúc." });
                anyFailed = true;
            }

            string output, errors;
            try
            {
                Task.WaitAll(new Task[] { stdoutTask, stderrTask }, 10_000);
                output = stdoutTask.IsCompletedSuccessfully ? stdoutTask.Result : string.Empty;
                errors = stderrTask.IsCompletedSuccessfully ? stderrTask.Result : string.Empty;
            }
            catch (Exception)
            {
                output = string.Empty;
                errors = string.Empty;
            }

            File.WriteAllText(Path.Combine(work, $"{index:D3}.log"), output + (errors.Length > 0 ? "\n--- stderr ---\n" + errors : string.Empty), new UTF8Encoding(false));

            var exitCode = timedOut ? -1 : p.ExitCode;
            if (!timedOut && exitCode != 0)
            {
                var tail = Tail(errors.Length > 0 ? errors : output, 5);
                RunLog.Append(runLog, new RunLogEntry { File = file.Path, Command = "*", Success = false, Summary = $"accoreconsole thoát mã {exitCode}." + (tail.Length > 0 ? " " + tail : string.Empty) });
                anyFailed = true;
            }

            if (saveAs is not null)
            {
                // Không có kênh nào từ accoreconsole báo "đã lưu": kiểm tra file đích có mới hơn lúc bắt đầu.
                var saved = exitCode == 0 && File.Exists(saveAs) && File.GetLastWriteTime(saveAs) >= startedAt;
                RunLog.Append(runLog, new RunLogEntry
                {
                    File = file.Path,
                    Command = "Save:" + job.SaveMode,
                    Success = saved,
                    Summary = saved
                        ? (job.SaveMode == SaveMode.Save ? "Đã lưu (SAVEAS " + job.DwgVersion + " về chính file)." : "Đã lưu bản sao: " + saveAs)
                        : "Không thấy file được lưu: " + saveAs + " (xem " + Path.Combine(work, $"{index:D3}.log") + ").",
                });
                if (!saved) anyFailed = true;
            }

            if (anyFailed && job.StopOnError) break;
        }

        return anyFailed ? 1 : 0;
    }

    /// <summary>Vài dòng cuối không rỗng của output — đủ để đọc lý do trong report.</summary>
    private static string Tail(string text, int lines)
    {
        var all = text.Split('\n').Select(l => l.TrimEnd('\r').Trim()).Where(l => l.Length > 0).ToList();
        return string.Join(" | ", all.Skip(Math.Max(0, all.Count - lines)));
    }
}

internal sealed class Options
{
    public string JobPath { get; private set; } = string.Empty;
    public bool DryRun { get; private set; }
    public string LogDir { get; private set; } = "logs";
    public int MaxMinutes { get; private set; } = 480;
    public string? RevitExe { get; private set; }
    public string? AccoreConsole { get; private set; }
    public string? PluginDll { get; private set; }
    public bool ReportOnly { get; private set; }
    public bool Analyze { get; private set; }
    public bool AutoDetectVersion { get; private set; } = true;
    public string? VerifyLog { get; private set; }
    public string? VerifyIfc { get; private set; }
    public string? IfcSpec { get; private set; }
    public string? VerifyIds { get; private set; }
    public string? IdsReport { get; private set; }

    public const string Usage = """
        DhcbTools.BatchRunner --job <job.json> [--dry-run] [--log-dir logs] [--max-minutes 480]
                              [--revit-exe <Revit.exe>] [--accoreconsole <accoreconsole.exe>] [--plugin-dll <DhcbTools.AutoCAD.dll>]
                              [--report-only] [--analyze] [--no-autodetect]
        DhcbTools.BatchRunner --verify-log <run-HHmmss.jsonl>
        DhcbTools.BatchRunner --verify-ifc <file.ifc> [--ifc-spec <quy-tac.json>]
        DhcbTools.BatchRunner --verify-ifc <file.ifc> --verify-ids <yeu-cau.ids> [--ids-report <bao-cao.html>]
        (Revit: phiên bản tự nhận từ header .rvt; step "PlotPdf" trong job AutoCAD sinh -PLOT ra PDF)
        (--verify-log kiểm chuỗi băm của log đã ghi: 0 nguyên vẹn · 1 hỏng, in ra đúng dòng · 2 không có file)
        (--verify-ifc đọc lại file IFC vừa xuất: 0 đạt · 1 có lỗi · 2 không có file hay quy tắc hỏng)
        (--verify-ids kiểm chính file IFC theo IDS 1.0: 0 không phần tử nào không đạt · 1 có · 2 không có file / IDS hỏng)
        """;

    public static Options? Parse(string[] args)
    {
        var o = new Options();
        for (var i = 0; i < args.Length; i++)
        {
            string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException("Thiếu giá trị cho " + args[i]);
            try
            {
                switch (args[i])
                {
                    case "--job": o.JobPath = Next(); break;
                    case "--dry-run": o.DryRun = true; break;
                    case "--log-dir": o.LogDir = Next(); break;
                    case "--max-minutes": o.MaxMinutes = int.Parse(Next()); break;
                    case "--revit-exe": o.RevitExe = Next(); break;
                    case "--accoreconsole": o.AccoreConsole = Next(); break;
                    case "--plugin-dll": o.PluginDll = Next(); break;
                    case "--report-only": o.ReportOnly = true; break;
                    case "--analyze": o.Analyze = true; break;
                    case "--no-autodetect": o.AutoDetectVersion = false; break;
                    case "--verify-log": o.VerifyLog = Next(); break;
                    case "--verify-ifc": o.VerifyIfc = Next(); break;
                    case "--ifc-spec": o.IfcSpec = Next(); break;
                    case "--verify-ids": o.VerifyIds = Next(); break;
                    case "--ids-report": o.IdsReport = Next(); break;
                    case "-h": case "--help": return null;
                    default:
                        Console.Error.WriteLine("Tham số không biết: " + args[i]);
                        return null;
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.Message);
                return null;
            }
        }

        // --verify-log và --verify-ifc đứng một mình được: chúng không chạy job nào cả.
        return string.IsNullOrEmpty(o.JobPath) && string.IsNullOrEmpty(o.VerifyLog) && string.IsNullOrEmpty(o.VerifyIfc) ? null : o;
    }
}
