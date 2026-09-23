using System.Diagnostics;
using Autodesk.AutoCAD.DatabaseServices;
using DhcbTools.Shared.Logic.Batch;
using DhcbTools.Shared.Hosting.Testing;
using DhcbTools.Shared.Logic.Testing;
using Newtonsoft.Json.Linq;

namespace DhcbTools.Core.AutoCAD.Testing;

/// <summary>Cấu hình cho <see cref="RunTestsCommand"/> — đối xứng với bản Revit.</summary>
public sealed class RunTestsConfig
{
    /// <summary>File JSON mô tả bộ ca kiểm (<see cref="TestSuite"/>).</summary>
    public required string SuitePath { get; init; }

    /// <summary>Nơi ghi báo cáo. Không đặt thì ghi cạnh file bộ test.</summary>
    public string? OutputFolder { get; init; }

    /// <summary>Chỉ chạy các ca có tên lệnh trong danh sách này (rỗng = chạy tất cả).</summary>
    public List<string> OnlyCommands { get; init; } = new();

    /// <summary>
    /// Cho phép ca có <c>allowWrite</c> ghi thật vào bản vẽ. Mặc định false: mọi ca đều bị ép
    /// <c>dryRun = true</c>, nên chạy bao nhiêu lần trên bản vẽ mẫu cũng không làm bẩn nó.
    /// </summary>
    public bool AllowWrites { get; init; }
}

/// <summary>
/// Chạy bộ kiểm thử <b>bên trong AutoCAD</b> (qua <c>accoreconsole</c>) — bản đối xứng của
/// <c>DhcbTools.Core.Testing.RunTestsCommand</c> bên Revit.
/// <para>
/// Lý do tồn tại: sau khi Revit đạt 42/42 lệnh có ca kiểm chạy thật (giai đoạn 8.4), phía AutoCAD vẫn là
/// 15 lệnh chỉ mới biên dịch xanh. Cơ chế đã có sẵn — batch runner mở được `accoreconsole` không người
/// ngồi máy — nên bộ test đi đúng đường đó: một lệnh Core gọi từng lệnh khác qua
/// <see cref="AcadCommandTable"/> trên bản vẽ mẫu rồi đối chiếu kỳ vọng khai báo sẵn.
/// </para>
/// <para>
/// Tầng đánh giá (<c>TestSuite</c>, <c>TestExpectation</c>, <c>TestReport</c>) dùng chung với Revit, nằm
/// ở <c>Shared.Logic/Testing</c> và có test riêng trên CI.
/// </para>
/// </summary>
public sealed class RunTestsCommand : ICoreCommand<RunTestsConfig>
{
    public string CommandName => "RunTests";

    public CommandResult Execute(Database database, RunTestsConfig config)
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

        var outputFolder = TestReportWriter.ResolveOutputFolder(config.SuitePath, config.OutputFolder);
        var tokens = new JobTokenContext(
            outputFolder,
            Path.GetFileNameWithoutExtension(database.Filename ?? string.Empty),
            DateTime.Now);

        // {suiteFolder} — thư mục chứa chính file bộ test, để ca kiểm trỏ tới fixtures đi theo repo
        // thay vì đường dẫn tuyệt đối của một máy.
        tokens.Extra["suiteFolder"] = Path.GetDirectoryName(Path.GetFullPath(config.SuitePath)) ?? ".";

        // {sourceFile} — đường dẫn đầy đủ của chính bản vẽ đang mở. Cần cho lệnh nhận một DWG khác làm
        // đối số (DrawingCompare): so bản vẽ với CHÍNH NÓ phải ra "không khác biệt", một phép tự kiểm
        // không cần commit file DWG nào vào repo.
        tokens.Extra["sourceFile"] = database.Filename ?? string.Empty;

        // Không tắt thì chính bộ ca kiểm bơm số liệu "lệnh nào dùng thật" lên (xem bản Revit).
        AcadCommandTable.LogUsage = false;
        try
        {
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

            var observation = Run(database, testCase, config.AllowWrites, tokens);
            outcome.ElapsedMs = observation.ElapsedMs;
            outcome.Summary = observation.Exception ?? observation.Summary;
            outcome.Failures.AddRange(testCase.Expect.Evaluate(
                observation,
                path => File.Exists(JobTokens.Expand(path, tokens))));
            outcomes.Add(outcome);
        }

        }
        finally
        {
            AcadCommandTable.LogUsage = true;
        }

        return TestReportWriter.Write(suite, outputFolder, "in-autocad-tests", outcomes);
    }

    private static TestObservation Run(Database database, TestCase testCase, bool allowWrites, JobTokenContext tokens)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            // Ép dryRun trừ khi ca này khai báo allowWrite VÀ người chạy bật AllowWrites — hai lớp khoá
            // để một lần chạy test không bao giờ vô tình sửa bản vẽ mẫu.
            // Thay token theo từng giá trị của cây JSON, không phải trên chuỗi đã serialize: đường dẫn
            // Windows có "\U" — escape không hợp lệ trong JSON (lỗi đã gặp ở bản Revit ngày 2026-09-03).
            var config = (JObject)testCase.Config.DeepClone();
            JobTokens.ExpandIn(config, tokens);
            var write = testCase.AllowWrite && allowWrites;
            config["dryRun"] = !write;

            var result = AcadCommandTable.Dispatch(database, testCase.Command, config.ToString());
            stopwatch.Stop();

            var observation = new TestObservation
            {
                Success = result.Success,
                Summary = result.Summary,
                AffectedCount = result.AffectedCount,
                ElapsedMs = stopwatch.ElapsedMilliseconds,
            };
            observation.Messages.AddRange(result.Messages);
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
}
