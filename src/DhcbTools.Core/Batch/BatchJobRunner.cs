using System.Diagnostics;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using DhcbTools.Shared.Logic.Batch;

namespace DhcbTools.Core.Batch;

/// <summary>
/// Giai đoạn 1 — chạy một <see cref="BatchJob"/> bên trong Revit: mở → chạy step qua <see cref="RevitCommandTable"/> →
/// lưu theo saveMode → đóng, ghi <c>run.jsonl</c>. Không UI; được kích hoạt bởi vỏ (journal/pending-job) hoặc Bridge.
/// </summary>
public sealed class BatchJobRunner
{
    private readonly Application _app;

    public BatchJobRunner(Application app)
    {
        _app = app;
    }

    /// <summary>Ghi log từng step (vỏ có thể in ra journal/console).</summary>
    public Action<string>? Log { get; set; }

    /// <summary>Chạy toàn bộ job. Trả mã thoát theo mục 1.4 (0/1). Không ném — mọi lỗi vào log.</summary>
    public int Run(BatchJob job, string runLogPath, int maxMinutes = 480, bool forceDryRun = false)
    {
        var runTime = DateTime.Now;
        var outputFolder = job.ResolveOutputFolder(runTime);
        if (!string.IsNullOrEmpty(outputFolder))
        {
            try { Directory.CreateDirectory(outputFolder); } catch (Exception ex) { Log?.Invoke("Không tạo được outputFolder: " + ex.Message); }
        }

        var deadline = runTime.AddMinutes(Math.Max(1, maxMinutes));
        var entries = new List<RunLogEntry>();
        var stop = false;

        foreach (var file in job.Files)
        {
            if (DateTime.Now > deadline)
            {
                var skip = new RunLogEntry { File = file.Path, Command = "*", Skipped = true, Success = false, Summary = "Hết --max-minutes, chưa kịp chạy." };
                RunLog.Append(runLogPath, skip);
                entries.Add(skip);
                continue;
            }

            if (stop)
            {
                var skip = new RunLogEntry { File = file.Path, Command = "*", Skipped = true, Success = false, Summary = "Dừng vì stopOnError sau lỗi ở file trước." };
                RunLog.Append(runLogPath, skip);
                entries.Add(skip);
                continue;
            }

            Document? doc = null;
            var swOpen = Stopwatch.StartNew();
            // Bắt đầu file mới: xoá cảnh báo/hộp thoại còn sót của file trước. Từ đây tới hết file, KHÔNG
            // xoá nữa mà chỉ chuyển dần vào từng dòng log — hộp thoại lúc mở (nâng cấp phiên bản…) rơi vào
            // step đầu tiên thay vì bị xoá mất như bản cũ (Clear() ngay trước Dispatch).
            CoreContext.SuppressedWarnings.Clear();
            try
            {
                doc = Open(file);
                Log?.Invoke($"Mở {file.Path} ({swOpen.ElapsedMilliseconds} ms)");
            }
            catch (Exception ex)
            {
                var entry = new RunLogEntry { File = file.Path, Command = "Open", Success = false, Summary = "Không mở được file: " + ex.Message, ElapsedMs = swOpen.ElapsedMilliseconds };
                entry.Messages.AddRange(TakeSuppressedWarnings());
                RunLog.Append(runLogPath, entry);
                entries.Add(entry);
                if (job.StopOnError) stop = true;
                continue;
            }

            var previousFailed = false;
            var anyStepFailed = false;
            try
            {
                foreach (var step in job.StepsFor(file))
                {
                    if (step.SkipIfPreviousFailed && previousFailed)
                    {
                        var skip = new RunLogEntry { File = file.Path, Command = step.Command, Skipped = true, Success = false, Summary = "Bỏ qua vì step trước lỗi." };
                        RunLog.Append(runLogPath, skip);
                        entries.Add(skip);
                        continue;
                    }

                    var configJson = job.ExpandStepConfig(step, outputFolder, file.Path, runTime);
                    if (forceDryRun)
                    {
                        configJson = ForceDryRun(configJson);
                    }

                    var sw = Stopwatch.StartNew();
                    CommandResult result;
                    try
                    {
                        using var _ = CoreContext.Use(FailurePolicy.Silent);
                        result = RevitCommandTable.Dispatch(doc, step.Command, configJson);
                    }
                    catch (Exception ex)
                    {
                        result = CommandResult.Fail("Exception: " + ex.Message);
                    }
                    result.Messages.AddRange(TakeSuppressedWarnings());
                    sw.Stop();

                    var entry = new RunLogEntry
                    {
                        File = file.Path,
                        Command = step.Command,
                        Success = result.Success,
                        Affected = result.AffectedCount,
                        Summary = result.Summary,
                        Messages = result.Messages.Take(2000).ToList(),
                        Errors = result.Errors,
                        ElapsedMs = sw.ElapsedMilliseconds,
                    };
                    RunLog.Append(runLogPath, entry);
                    entries.Add(entry);
                    Log?.Invoke($"  {(result.Success ? "OK " : "ERR")} {step.Command}: {result.Summary}");
                    previousFailed = !result.Success;
                    anyStepFailed |= !result.Success;
                    if (!result.Success && job.StopOnError)
                    {
                        stop = true;
                        break;
                    }
                }

                Save(doc, job, file, outputFolder, runLogPath, entries, forceDryRun, anyStepFailed);
            }
            finally
            {
                try { doc.Close(false); } catch { /* đã đóng */ }
            }
        }

        return RunLog.ExitCode(entries);
    }

    private Document Open(BatchJobFile file)
    {
        var modelPath = ModelPathUtils.ConvertUserVisiblePathToModelPath(file.Path);
        var options = new OpenOptions { Audit = false };
        if (file.DetachFromCentral)
        {
            options.DetachFromCentralOption = DetachFromCentralOption.DetachAndPreserveWorksets;
        }

        if (file.Worksets.Count > 0)
        {
            var config = new WorksetConfiguration(WorksetConfigurationOption.CloseAllWorksets);
            var ids = WorksharingUtils.GetUserWorksetInfo(modelPath)
                .Where(w => file.Worksets.Any(n => string.Equals(n, w.Name, StringComparison.OrdinalIgnoreCase)))
                .Select(w => w.Id).ToList();
            config.Open(ids);
            options.SetOpenWorksetsConfiguration(config);
        }

        var doc = _app.OpenDocumentFile(modelPath, options);
        LoadUnloadedLinks(doc);
        return doc;
    }

    /// <summary>
    /// Nạp lại mọi link đang ở trạng thái "chưa nạp" — trạng thái link được LƯU theo file (không phải
    /// theo lần mở), nên một file dù đường dẫn link vẫn đúng vẫn có thể mở lên với link chưa nạp (ví dụ
    /// kỹ sư lưu lần cuối lúc đang tắt link để nhẹ máy, hoặc <c>SaveAs</c> sau <c>DetachFromCentral</c>
    /// không giữ lại trạng thái đã nạp — thấy trên dự án GOLDVIEW thật: bản sao nâng cấp báo
    /// <c>ClashDetection</c> "0 va chạm" trong khi bản gốc cùng file báo 479, chỉ vì cả ba link đều
    /// "chưa nạp" sau <c>SaveAs</c>). Lệnh nào đọc model liên kết (ClashDetection, SleeveAuto,
    /// DevicePlacement, AutoRoute) mà link chưa nạp thì âm thầm báo "sạch"/"0 vật cản" — sai lệch nguy
    /// hiểm hơn một exception, vì trông y hệt kết quả tốt. Lỗi nạp từng link (đường dẫn hỏng, file thiếu)
    /// không được làm chết cả việc mở file — bắt riêng từng link, ghi vào <see cref="Log"/> để thấy được.
    /// </summary>
    private void LoadUnloadedLinks(Document doc)
    {
        foreach (var linkType in new FilteredElementCollector(doc).OfClass(typeof(RevitLinkType)).Cast<RevitLinkType>())
        {
            if (linkType.GetLinkedFileStatus() == LinkedFileStatus.Loaded)
            {
                continue;
            }

            try
            {
                var result = linkType.Load();
                if (result.LoadResult == LinkLoadResultType.LinkNotFound)
                {
                    // Đường dẫn GHI TRONG link (network central, hoặc đường dẫn cũ trước khi SaveAs) không
                    // giải được nữa — thường xảy ra khi SaveAs sang thư mục khác (xem chú thích trên hàm).
                    // Thử lại đúng MỘT nước: file cùng tên nằm CẠNH chính file host đang mở, đúng cách bố
                    // trí phổ biến của hồ sơ Việt Nam (các file kỷ luật tách rời cùng một thư mục dự án).
                    var retried = TryLoadFromSiblingFolder(doc, linkType, result);
                    Log?.Invoke($"  Nạp lại link \"{linkType.Name}\": {result.LoadResult}"
                        + (retried != null ? $" → thử lại cạnh file host: {retried.LoadResult}" : " (không có file cùng tên cạnh file host)"));
                }
                else
                {
                    Log?.Invoke($"  Nạp lại link \"{linkType.Name}\": {result.LoadResult}");
                }
            }
            catch (Exception ex)
            {
                Log?.Invoke($"  Không nạp lại được link \"{linkType.Name}\": {ex.Message}");
            }
        }
    }

    private static LinkLoadResult? TryLoadFromSiblingFolder(Document doc, RevitLinkType linkType, LinkLoadResult original)
    {
        if (string.IsNullOrEmpty(doc.PathName))
        {
            return null;
        }

        var hostFolder = Path.GetDirectoryName(doc.PathName);
        if (string.IsNullOrEmpty(hostFolder))
        {
            return null;
        }

        string recordedFileName;
        try
        {
            var externalRef = linkType.GetExternalFileReference();
            var recordedPath = ModelPathUtils.ConvertModelPathToUserVisiblePath(externalRef.GetAbsolutePath());
            recordedFileName = Path.GetFileName(recordedPath);
        }
        catch (Exception)
        {
            return null;
        }

        if (string.IsNullOrEmpty(recordedFileName))
        {
            return null;
        }

        var candidate = Path.Combine(hostFolder, recordedFileName);
        if (!File.Exists(candidate))
        {
            return null;
        }

        return linkType.LoadFrom(ModelPathUtils.ConvertUserVisiblePathToModelPath(candidate), new WorksetConfiguration(WorksetConfigurationOption.OpenAllWorksets));
    }

    /// <summary>Lấy và xoá cảnh báo/hộp thoại Revit đã nuốt kể từ lần lấy trước (cùng luồng với Revit).</summary>
    private static List<string> TakeSuppressedWarnings()
    {
        var taken = CoreContext.SuppressedWarnings.Select(w => "[Cảnh báo Revit bỏ qua] " + w).ToList();
        CoreContext.SuppressedWarnings.Clear();
        return taken;
    }

    private void Save(Document doc, BatchJob job, BatchJobFile file, string outputFolder, string runLogPath, List<RunLogEntry> entries, bool forceDryRun, bool anyStepFailed)
    {
        if (forceDryRun || job.SaveMode == SaveMode.None)
        {
            return;
        }

        if (anyStepFailed && !job.SaveOnError)
        {
            // Có step lỗi thì không lưu: transaction lỗi đã rollback nhưng step trước có thể đã ghi — lưu đè
            // (Save) là mất đường lui, lưu bản sao (SaveAs) là để lại một file nửa vời trông như hoàn chỉnh.
            // Muốn giữ phần đã làm được thì đặt "saveOnError": true trong job.
            var skipped = new RunLogEntry
            {
                File = file.Path,
                Command = "Save:" + job.SaveMode,
                Skipped = true,
                Success = false,
                Summary = "Không lưu vì có step lỗi (đặt saveOnError=true nếu vẫn muốn lưu).",
            };
            skipped.Messages.AddRange(TakeSuppressedWarnings());
            RunLog.Append(runLogPath, skipped);
            entries.Add(skipped);
            Log?.Invoke("  SKIP Save: có step lỗi, không lưu " + file.Path);
            return;
        }

        var sw = Stopwatch.StartNew();
        var entry = new RunLogEntry { File = file.Path, Command = "Save:" + job.SaveMode };
        try
        {
            if (job.SaveMode == SaveMode.Save)
            {
                if (doc.IsWorkshared)
                {
                    doc.SynchronizeWithCentral(new TransactWithCentralOptions(), new SynchronizeWithCentralOptions { Comment = "DHCB batch " + job.Name, SaveLocalBefore = true, SaveLocalAfter = true });
                }
                else
                {
                    doc.Save();
                }
                entry.Summary = "Đã lưu.";
            }
            else
            {
                var target = Path.Combine(outputFolder, Path.GetFileName(file.Path));
                var opts = new SaveAsOptions { OverwriteExistingFile = true, MaximumBackups = 1 };
                if (doc.IsWorkshared)
                {
                    opts.SetWorksharingOptions(new WorksharingSaveAsOptions { SaveAsCentral = true });
                }
                doc.SaveAs(target, opts);
                entry.Summary = "Đã lưu bản sao: " + target;
            }
            entry.Success = true;
        }
        catch (Exception ex)
        {
            entry.Success = false;
            entry.Summary = "Lưu thất bại: " + ex.Message;
        }
        entry.ElapsedMs = sw.ElapsedMilliseconds;
        entry.Messages.AddRange(TakeSuppressedWarnings());
        RunLog.Append(runLogPath, entry);
        entries.Add(entry);
    }

    private static string ForceDryRun(string configJson)
    {
        try
        {
            var obj = Newtonsoft.Json.Linq.JObject.Parse(configJson);
            obj["dryRun"] = true;
            return obj.ToString(Newtonsoft.Json.Formatting.None);
        }
        catch
        {
            return configJson;
        }
    }
}
