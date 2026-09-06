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
/// <summary>Các đường kiểm đứng một mình, không chạy job: <c>--verify-log</c>, <c>--verify-ifc</c>, <c>--verify-ids</c>, <c>--dossier</c>.</summary>
public static partial class Program
{
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
        IfcIdsModel model;
        try
        {
            model = IfcIdsModel.Parse(File.ReadAllText(ifcPath, Encoding.UTF8));
        }
        catch (IfcParseException ex)
        {
            // File rác/hỏng: --verify-ifc một mình báo gọn "Không đọc được file", còn đường này từng ném
            // ngoại lệ chưa bắt (mã thoát 127) — lộ khi kỹ sư test đưa file "hello" vào (§44).
            Console.Error.WriteLine("Không đọc được file IFC: " + ex.Message);
            return 2;
        }

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
    /// <c>--dossier &lt;thư mục&gt; --dossier-spec &lt;danh-muc.json&gt;</c>: đối chiếu danh mục hồ sơ hoàn thành
    /// công trình (mục 11.6) với file THẬT trong thư mục. Mã thoát 0 đủ mục bắt buộc · 1 còn thiếu ·
    /// 2 không có thư mục/danh mục hoặc danh mục hỏng.
    /// <para>
    /// Không làm thành lệnh Core vì việc này không cần <c>Document</c> nào — cùng lý do với
    /// <c>--verify-log</c> và <c>--verify-ifc</c>, và đổi lại được thứ chạy trên CI.
    /// </para>
    /// </summary>
    internal static int VerifyDossier(string folder, string? specPath, string? reportPath)
    {
        if (!Directory.Exists(folder))
        {
            Console.Error.WriteLine("Không có thư mục hồ sơ: " + folder);
            return 2;
        }

        if (string.IsNullOrEmpty(specPath) || !File.Exists(specPath))
        {
            Console.Error.WriteLine("Không có file danh mục: " + (specPath ?? "(chưa khai --dossier-spec)"));
            return 2;
        }

        DossierSpec spec;
        try
        {
            spec = DossierSpec.FromJson(File.ReadAllText(specPath!, Encoding.UTF8));
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine("File danh mục không dùng được: " + ex.Message);
            return 2;
        }

        var root = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var files = Directory.GetFiles(root, "*", SearchOption.AllDirectories)
            .Select(f => f.Substring(root.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            .ToList();

        var result = DossierIndex.Check(spec, files);

        Console.WriteLine(root);
        Console.WriteLine(DossierIndex.Summary(result));
        foreach (var item in result.Items.Where(i => i.MissingRequired))
        {
            Console.WriteLine($"THIẾU {item.Item.Code} — {item.Item.Name}"
                              + (item.Item.Patterns.Count > 0 ? " (mẫu: " + string.Join(", ", item.Item.Patterns) + ")" : " (chưa khai mẫu tên file)"));
        }

        foreach (var file in result.Unmatched.Take(20))
        {
            Console.WriteLine("Chưa xếp mục: " + file);
        }

        if (!string.IsNullOrEmpty(reportPath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(reportPath!)) ?? ".");
            File.WriteAllText(reportPath!, DossierIndex.Html(spec, result, root, DateTime.Now), new UTF8Encoding(true));
            File.WriteAllText(Path.ChangeExtension(reportPath!, ".csv"), DossierIndex.Csv(result), CsvText.Utf8WithBom);
            Console.WriteLine("Báo cáo: " + reportPath);
        }

        return result.Ok ? 0 : 1;
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
}
