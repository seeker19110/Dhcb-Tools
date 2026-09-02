using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace DhcbTools.Shared.Logic.Testing
{
    /// <summary>Kết quả một ca kiểm sau khi chạy.</summary>
    public sealed class TestOutcome
    {
        public string Name { get; set; } = string.Empty;

        public string Command { get; set; } = string.Empty;

        public bool Skipped { get; set; }

        public string SkipReason { get; set; } = string.Empty;

        public long ElapsedMs { get; set; }

        /// <summary>Lý do trượt; rỗng = đạt.</summary>
        public List<string> Failures { get; set; } = new List<string>();

        /// <summary>Summary thật của lệnh — để đọc báo cáo mà không phải mở log.</summary>
        public string Summary { get; set; } = string.Empty;

        public bool Passed => !Skipped && Failures.Count == 0;
    }

    /// <summary>
    /// Tổng hợp và xuất kết quả bộ test chạy trong Revit. Có TRX để CI/Visual Studio đọc được như
    /// mọi bộ test khác, và Markdown để dán thẳng vào <c>docs/bang-chung-test.md</c>.
    /// </summary>
    public static class TestReport
    {
        public static int PassedCount(IEnumerable<TestOutcome> outcomes) => outcomes.Count(o => o.Passed);

        public static int FailedCount(IEnumerable<TestOutcome> outcomes) => outcomes.Count(o => !o.Passed && !o.Skipped);

        public static int SkippedCount(IEnumerable<TestOutcome> outcomes) => outcomes.Count(o => o.Skipped);

        public static string Summarise(IEnumerable<TestOutcome> outcomes)
        {
            var list = outcomes.ToList();
            return $"{PassedCount(list)} đạt / {FailedCount(list)} trượt / {SkippedCount(list)} bỏ qua"
                 + $" trên {list.Count} ca.";
        }

        /// <summary>Báo cáo Markdown, xếp ca trượt lên trước để đọc là thấy ngay việc cần làm.</summary>
        public static string ToMarkdown(string suiteName, string modelPath, IEnumerable<TestOutcome> outcomes)
        {
            var list = outcomes.ToList();
            var sb = new StringBuilder();
            sb.Append("# ").Append(suiteName).AppendLine();
            sb.AppendLine();
            sb.Append("**Model:** `").Append(modelPath).Append("`  ").AppendLine();
            sb.Append("**Chạy lúc:** ").Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)).Append("  ").AppendLine();
            sb.Append("**Kết quả:** ").Append(Summarise(list)).AppendLine();
            sb.AppendLine();

            var failed = list.Where(o => !o.Passed && !o.Skipped).ToList();
            if (failed.Count > 0)
            {
                sb.AppendLine("## Trượt").AppendLine();
                foreach (var o in failed)
                {
                    sb.Append("### ").Append(o.Name).Append(" (`").Append(o.Command).Append("`)").AppendLine();
                    foreach (var f in o.Failures)
                    {
                        sb.Append("- ").Append(f).AppendLine();
                    }
                    sb.AppendLine();
                }
            }

            sb.AppendLine("## Toàn bộ").AppendLine();
            sb.AppendLine("| Ca | Lệnh | Kết quả | ms | Summary |");
            sb.AppendLine("|---|---|---|---:|---|");
            foreach (var o in list)
            {
                var verdict = o.Skipped ? "⏭ bỏ qua" : o.Passed ? "✅ đạt" : "❌ trượt";
                sb.Append("| ").Append(o.Name)
                  .Append(" | `").Append(o.Command)
                  .Append("` | ").Append(verdict)
                  .Append(" | ").Append(o.ElapsedMs.ToString(CultureInfo.InvariantCulture))
                  .Append(" | ").Append(Escape(o.Skipped ? o.SkipReason : o.Summary))
                  .AppendLine(" |");
            }

            return sb.ToString();
        }

        /// <summary>
        /// TRX tối giản nhưng đúng schema Visual Studio, để `actions/upload-artifact` và các trình đọc
        /// test report hiểu được kết quả chạy trong Revit như một bộ test bình thường.
        /// </summary>
        public static string ToTrx(string suiteName, IEnumerable<TestOutcome> outcomes)
        {
            var list = outcomes.ToList();
            var now = DateTime.Now.ToString("O", CultureInfo.InvariantCulture);
            var sb = new StringBuilder();
            sb.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
            sb.Append("<TestRun id=\"").Append(Guid.NewGuid().ToString())
              .Append("\" name=\"").Append(Xml(suiteName))
              .AppendLine("\" xmlns=\"http://microsoft.com/schemas/VisualStudio/TeamTest/2010\">");
            sb.Append("  <Times creation=\"").Append(now).Append("\" start=\"").Append(now).Append("\" finish=\"").Append(now).AppendLine("\" />");

            sb.Append("  <ResultSummary outcome=\"").Append(FailedCount(list) == 0 ? "Completed" : "Failed").AppendLine("\">");
            sb.Append("    <Counters total=\"").Append(list.Count)
              .Append("\" executed=\"").Append(list.Count - SkippedCount(list))
              .Append("\" passed=\"").Append(PassedCount(list))
              .Append("\" failed=\"").Append(FailedCount(list))
              .AppendLine("\" />");
            sb.AppendLine("  </ResultSummary>");

            sb.AppendLine("  <Results>");
            foreach (var o in list)
            {
                var outcome = o.Skipped ? "NotExecuted" : o.Passed ? "Passed" : "Failed";
                sb.Append("    <UnitTestResult testId=\"").Append(Guid.NewGuid().ToString())
                  .Append("\" testName=\"").Append(Xml(o.Name + " (" + o.Command + ")"))
                  .Append("\" duration=\"").Append(TimeSpan.FromMilliseconds(o.ElapsedMs).ToString(@"hh\:mm\:ss\.fffffff"))
                  .Append("\" outcome=\"").Append(outcome).Append("\"");

                if (o.Failures.Count > 0 || o.Skipped)
                {
                    sb.AppendLine(">");
                    sb.AppendLine("      <Output>");
                    sb.Append("        <ErrorInfo><Message>")
                      .Append(Xml(o.Skipped ? o.SkipReason : string.Join("; ", o.Failures)))
                      .AppendLine("</Message></ErrorInfo>");
                    sb.AppendLine("      </Output>");
                    sb.AppendLine("    </UnitTestResult>");
                }
                else
                {
                    sb.AppendLine(" />");
                }
            }

            sb.AppendLine("  </Results>");
            sb.AppendLine("</TestRun>");
            return sb.ToString();
        }

        private static string Escape(string text) =>
            (text ?? string.Empty).Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");

        private static string Xml(string text) =>
            (text ?? string.Empty)
                .Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
                .Replace("\"", "&quot;").Replace("'", "&apos;");
    }
}
