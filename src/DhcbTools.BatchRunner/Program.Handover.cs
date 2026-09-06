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
/// <summary>Gói bàn giao (mục 11.3): gom đầu ra của đêm thành <c>ban-giao.html</c> + <c>ban-giao.json</c>.</summary>
public static partial class Program
{
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
                    // IFC hỏng: IfcChecker ở trên đã báo "không đọc được" thành một mục không đạt; ở đây phải
                    // bắt riêng, nếu không cả runner sập và đêm đó KHÔNG có gói bàn giao nào (§44).
                    IfcIdsModel model;
                    try
                    {
                        model = IfcIdsModel.Parse(text);
                    }
                    catch (IfcParseException ex)
                    {
                        input.Checks.Add(new HandoverCheck("Kiểm IDS " + ifc.RelativePath, false, "Không đọc được file IFC: " + ex.Message));
                        continue;
                    }

                    var check = IdsEvaluator.Check(specs, model.Elements());
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
}
