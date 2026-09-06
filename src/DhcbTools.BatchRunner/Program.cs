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
public static partial class Program
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

        // Đối chiếu danh mục hồ sơ cũng là việc độc lập: chỉ liệt kê file trong một thư mục.
        if (!string.IsNullOrEmpty(opts.Dossier))
        {
            return VerifyDossier(opts.Dossier!, opts.DossierSpec, opts.DossierReport);
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
}
