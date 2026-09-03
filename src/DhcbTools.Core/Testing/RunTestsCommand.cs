using System.Diagnostics;
using Autodesk.Revit.DB;
using DhcbTools.Shared.Logic.Batch;
using DhcbTools.Shared.Logic.Testing;
using Newtonsoft.Json.Linq;

namespace DhcbTools.Core.Testing;

/// <summary>Cấu hình cho <see cref="RunTestsCommand"/>.</summary>
public sealed class RunTestsConfig
{
    /// <summary>File JSON mô tả bộ ca kiểm (<see cref="TestSuite"/>).</summary>
    public required string SuitePath { get; init; }

    /// <summary>Nơi ghi báo cáo. Không đặt thì ghi cạnh file bộ test.</summary>
    public string? OutputFolder { get; init; }

    /// <summary>Chỉ chạy các ca có tên lệnh trong danh sách này (rỗng = chạy tất cả).</summary>
    public List<string> OnlyCommands { get; init; } = new();

    /// <summary>
    /// Cho phép ca có <c>allowWrite</c> ghi thật vào model. Mặc định false: mọi ca đều bị ép
    /// <c>dryRun = true</c>, nên chạy bao nhiêu lần trên model mẫu cũng không làm bẩn nó.
    /// </summary>
    public bool AllowWrites { get; init; }
}

/// <summary>
/// Giai đoạn 8.3 — chạy bộ kiểm thử <b>bên trong</b> Revit.
/// <para>
/// Lý do tồn tại: toàn bộ <c>DhcbTools.Core</c> (mọi dòng chạm Revit API) không có test nào, trong khi
/// 347 test xUnit chỉ phủ <c>Shared.Logic</c> thuần. Revit không chạy headless được, nhưng batch runner
/// đã mở được Revit không người ngồi máy — nên bộ test đi đúng đường đó: một lệnh Core gọi từng lệnh
/// khác qua <see cref="RevitCommandTable"/> trên model mẫu rồi đối chiếu kỳ vọng.
/// </para>
/// <para>
/// Không lên Ribbon: kích bằng <c>BatchRunner --job jobs/tests.json</c> hoặc qua Bridge.
/// </para>
/// </summary>
public sealed class RunTestsCommand : ICoreCommand<RunTestsConfig>
{
    public string CommandName => "RunTests";

    public CommandResult Execute(Document document, RunTestsConfig config)
    {
        if (!File.Exists(config.SuitePath))
        {
            return CommandResult.Fail($"Không tìm thấy bộ test: \"{config.SuitePath}\".");
        }

        TestSuite suite;
        try
        {
            suite = TestSuite.Load(config.SuitePath);
        }
        catch (Exception ex)
        {
            return CommandResult.Fail("Bộ test không hợp lệ: " + ex.Message);
        }

        var only = new HashSet<string>(config.OnlyCommands, StringComparer.OrdinalIgnoreCase);
        var outcomes = new List<TestOutcome>();

        // Cùng bộ token với file job của batch runner ({outputFolder}, {fileName}, {yyyy-MM-dd}...),
        // để bộ test viết đường dẫn giống hệt cách người ta viết job thật.
        var outputFolder = ResolveOutputFolder(config);
        var tokens = new JobTokenContext(outputFolder, Path.GetFileNameWithoutExtension(document.PathName ?? string.Empty), DateTime.Now);

        // {suiteFolder} — thư mục chứa chính file bộ test. Nhiều lệnh cần file đầu vào (CSV tham số,
        // CSV sizing, quy tắc JSON…); không có token này thì bộ test phải viết đường dẫn tuyệt đối của
        // máy đang chạy, tức là chỉ chạy được trên đúng một máy.
        tokens.Extra["suiteFolder"] = Path.GetDirectoryName(Path.GetFullPath(config.SuitePath)) ?? ".";

        foreach (var testCase in suite.Cases)
        {
            if (only.Count > 0 && !only.Contains(testCase.Command))
            {
                continue;
            }

            var outcome = new TestOutcome
            {
                Name = testCase.DisplayName,
                Command = testCase.Command,
            };

            if (testCase.Skip)
            {
                outcome.Skipped = true;
                outcome.SkipReason = string.IsNullOrWhiteSpace(testCase.SkipReason) ? "đánh dấu skip" : testCase.SkipReason;
                outcomes.Add(outcome);
                continue;
            }

            var observation = Run(document, testCase, config.AllowWrites, tokens);
            outcome.ElapsedMs = observation.ElapsedMs;
            outcome.Summary = observation.Exception ?? observation.Summary;
            outcome.Failures.AddRange(testCase.Expect.Evaluate(
                observation,
                path => File.Exists(JobTokens.Expand(path, tokens))));
            outcomes.Add(outcome);
        }

        return WriteReports(suite, config, outcomes);
    }

    private static TestObservation Run(Document document, TestCase testCase, bool allowWrites, JobTokenContext tokens)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            // Ép dryRun trừ khi ca này khai báo allowWrite VÀ người chạy bật AllowWrites — hai lớp khoá
            // để một lần chạy test không bao giờ vô tình sửa model mẫu.
            // Thay token theo TỪNG giá trị, không phải trên chuỗi JSON đã serialize: đường dẫn Windows
            // ("C:\Users\...") có "\U" — escape không hợp lệ trong JSON — nên cách cũ làm vỡ cả config.
            // Cả khối này nằm TRONG try: một ca có config hỏng phải trượt một mình, không được giết
            // cả lượt chạy (trước đây nó ném ra ngoài Execute và 27 ca còn lại không chạy lần nào).
            var config = (JObject)testCase.Config.DeepClone();
            JobTokens.ExpandIn(config, tokens);
            var write = testCase.AllowWrite && allowWrites;
            config["dryRun"] = !write;

            // Test chạy không người ngồi máy: cảnh báo Revit phải được nuốt có ghi lại, không hiện hộp thoại.
            using var _ = CoreContext.Use(FailurePolicy.SuppressWarnings);
            CoreContext.SuppressedWarnings.Clear();

            var result = RevitCommandTable.Dispatch(document, testCase.Command, config.ToString());
            stopwatch.Stop();

            var observation = new TestObservation
            {
                Success = result.Success,
                Summary = result.Summary,
                AffectedCount = result.AffectedCount,
                ElapsedMs = stopwatch.ElapsedMilliseconds,
            };
            observation.Messages.AddRange(result.Messages);
            observation.Messages.AddRange(CoreContext.SuppressedWarnings.Select(w => "[Cảnh báo Revit] " + w));
            observation.Errors.AddRange(result.Errors);
            return observation;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return new TestObservation
            {
                Success = false,
                ElapsedMs = stopwatch.ElapsedMilliseconds,
                Exception = ex.ToString(),
            };
        }
    }

    /// <summary>Nơi ghi báo cáo và cũng là giá trị của token <c>{outputFolder}</c> trong bộ test.</summary>
    private static string ResolveOutputFolder(RunTestsConfig config) =>
        string.IsNullOrWhiteSpace(config.OutputFolder)
            ? Path.GetDirectoryName(Path.GetFullPath(config.SuitePath)) ?? "."
            : config.OutputFolder!;

    private static CommandResult WriteReports(TestSuite suite, RunTestsConfig config, List<TestOutcome> outcomes)
    {
        var folder = ResolveOutputFolder(config);

        var summary = TestReport.Summarise(outcomes);
        var result = TestReport.FailedCount(outcomes) == 0
            ? CommandResult.Ok(summary, TestReport.PassedCount(outcomes))
            : CommandResult.Fail(summary);

        try
        {
            Directory.CreateDirectory(folder);
            var trx = Path.Combine(folder, "in-revit-tests.trx");
            var markdown = Path.Combine(folder, "in-revit-tests.md");
            File.WriteAllText(trx, TestReport.ToTrx(suite.Name, outcomes));
            File.WriteAllText(markdown, TestReport.ToMarkdown(suite.Name, suite.Model, outcomes));
            result.Messages.Add($"Báo cáo: {trx}");
            result.Messages.Add($"Báo cáo: {markdown}");
        }
        catch (Exception ex)
        {
            result.Messages.Add("Không ghi được báo cáo: " + ex.Message);
        }

        foreach (var failed in outcomes.Where(o => !o.Passed && !o.Skipped))
        {
            result.Errors.Add($"{failed.Name} ({failed.Command}): {string.Join("; ", failed.Failures)}");
        }

        foreach (var outcome in outcomes)
        {
            var verdict = outcome.Skipped ? "BỎ QUA" : outcome.Passed ? "ĐẠT" : "TRƯỢT";
            result.Messages.Add($"[{verdict}] {outcome.Name} ({outcome.Command}) — {outcome.ElapsedMs} ms");
        }

        return result;
    }
}
