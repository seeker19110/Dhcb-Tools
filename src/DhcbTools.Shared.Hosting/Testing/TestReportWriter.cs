using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DhcbTools.Shared.Logic.Testing;

namespace DhcbTools.Shared.Hosting.Testing
{
    /// <summary>
    /// Ghi báo cáo TRX + Markdown và dựng <see cref="CommandResult"/> cho lệnh <c>RunTests</c>. Trước đây
    /// hai bản chép y hệt nằm trong <c>Core/Testing</c> và <c>Core.AutoCAD/Testing</c> (chỉ khác tiền tố tên
    /// file) — ngoài tầm cổng phủ CI; nay một chỗ, có test.
    /// </summary>
    public static class TestReportWriter
    {
        /// <summary>Nơi ghi báo cáo và cũng là giá trị của token <c>{outputFolder}</c> trong bộ test: thư mục của file suite nếu không chỉ định.</summary>
        public static string ResolveOutputFolder(string suitePath, string? outputFolder)
        {
            if (!string.IsNullOrWhiteSpace(outputFolder))
            {
                return outputFolder!;
            }

            if (string.IsNullOrWhiteSpace(suitePath))
            {
                throw new ArgumentException("Cần đường dẫn file suite để suy ra thư mục báo cáo.", nameof(suitePath));
            }

            return Path.GetDirectoryName(Path.GetFullPath(suitePath)) ?? ".";
        }

        /// <summary>
        /// Ghi <c>{filePrefix}.trx</c> và <c>{filePrefix}.md</c> vào <paramref name="folder"/>, trả kết quả lệnh:
        /// Ok khi không ca nào trượt, Fail (kèm Errors từng ca trượt) khi có; Messages liệt kê đường dẫn báo cáo
        /// và phán quyết từng ca. Không ghi được báo cáo thì ghi Messages, không đổi phán quyết.
        /// </summary>
        public static CommandResult Write(TestSuite suite, string folder, string filePrefix, IReadOnlyList<TestOutcome> outcomes)
        {
            if (suite == null) throw new ArgumentNullException(nameof(suite));
            if (outcomes == null) throw new ArgumentNullException(nameof(outcomes));
            if (string.IsNullOrWhiteSpace(filePrefix)) throw new ArgumentException("Cần tiền tố tên file báo cáo.", nameof(filePrefix));

            var summary = TestReport.Summarise(outcomes);
            var result = TestReport.FailedCount(outcomes) == 0
                ? CommandResult.Ok(summary, TestReport.PassedCount(outcomes))
                : CommandResult.Fail(summary);

            try
            {
                Directory.CreateDirectory(folder);
                var trx = Path.Combine(folder, filePrefix + ".trx");
                var markdown = Path.Combine(folder, filePrefix + ".md");
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
}
