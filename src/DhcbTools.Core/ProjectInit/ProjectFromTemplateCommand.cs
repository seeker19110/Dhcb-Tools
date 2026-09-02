using Autodesk.Revit.DB;
using DhcbTools.Shared.Logic;

namespace DhcbTools.Core.ProjectInit;

/// <summary>Mục 2.1 — tạo file mới từ template chuẩn, bật worksharing, tạo workset, lưu central.</summary>
public sealed class ProjectFromTemplateConfig
{
    public required string TemplatePath { get; init; }

    /// <summary>Đường dẫn file ra; token {projectCode} {discipline} {revitVersion} {yyyy-MM-dd}.</summary>
    public required string OutputPath { get; init; }

    public string ProjectCode { get; init; } = "PRJ";

    public string Discipline { get; init; } = "ARC";

    public bool CreateCentral { get; init; } = true;

    public List<string> Worksets { get; init; } = new List<string> { "Shared Levels and Grids", "Kiến trúc", "Kết cấu", "MEP", "Liên kết CAD" };

    /// <summary>Đóng file mới sau khi lưu (batch) — false để giữ mở cho người dùng.</summary>
    public bool CloseAfterSave { get; init; } = true;

    public bool DryRun { get; init; } = true;
}

/// <summary>
/// Lệnh này không cần document đang mở (nhận Document chỉ để lấy Application); tạo file mới bằng
/// <c>Application.NewProjectDocument</c>. KHÔNG ghi đè file đã tồn tại.
/// </summary>
public sealed class ProjectFromTemplateCommand : ICoreCommand<ProjectFromTemplateConfig>
{
    public string CommandName => "ProjectFromTemplate";

    public CommandResult Execute(Document document, ProjectFromTemplateConfig config)
    {
        var app = document.Application;
        if (!File.Exists(config.TemplatePath))
        {
            return CommandResult.Fail($"Không tìm thấy template \"{config.TemplatePath}\".");
        }

        var outputPath = ResolveOutputPath(config, app.VersionNumber);
        if (File.Exists(outputPath))
        {
            return CommandResult.Fail($"File đã tồn tại, không ghi đè: \"{outputPath}\".");
        }

        var result = CommandResult.Ok(string.Empty);
        if (config.DryRun)
        {
            result.Summary = $"[Xem trước] Sẽ tạo \"{outputPath}\" từ template, worksharing={(config.CreateCentral ? "bật" : "tắt")}, {config.Worksets.Count} workset.";
            result.Messages.AddRange(config.Worksets.Select(w => "Workset: " + w));
            return result;
        }

        var dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        Document? newDoc = null;
        try
        {
            newDoc = app.NewProjectDocument(config.TemplatePath);
            var created = 0;

            if (config.CreateCentral)
            {
                if (!newDoc.IsWorkshared && newDoc.CanEnableWorksharing())
                {
                    newDoc.EnableWorksharing("Shared Levels and Grids", "Workset1");
                }

                var existing = new FilteredWorksetCollector(newDoc).OfKind(WorksetKind.UserWorkset).Select(w => w.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
                using (var tx = RevitCompat.StartTransaction(newDoc, "DHCB - Tạo workset"))
                {
                    foreach (var name in config.Worksets)
                    {
                        if (existing.Contains(name))
                        {
                            result.Messages.Add($"Workset \"{name}\" đã có — bỏ qua.");
                            continue;
                        }

                        if (!WorksetTable.IsWorksetNameUnique(newDoc, name))
                        {
                            result.Messages.Add($"Tên workset \"{name}\" không hợp lệ/không duy nhất — bỏ qua.");
                            continue;
                        }

                        Workset.Create(newDoc, name);
                        created++;
                    }
                    tx.Commit();
                }

                var saveAs = new SaveAsOptions { OverwriteExistingFile = false, MaximumBackups = 3 };
                saveAs.SetWorksharingOptions(new WorksharingSaveAsOptions { SaveAsCentral = true });
                newDoc.SaveAs(outputPath, saveAs);
                try { newDoc.SynchronizeWithCentral(new TransactWithCentralOptions(), new SynchronizeWithCentralOptions { Comment = "DHCB - khởi tạo" }); } catch { /* không bắt buộc */ }
            }
            else
            {
                newDoc.SaveAs(outputPath, new SaveAsOptions { OverwriteExistingFile = false });
            }

            result.Summary = $"Đã tạo \"{outputPath}\" ({created} workset mới).";
            result.AffectedCount = 1;
            return result;
        }
        catch (Exception ex)
        {
            return CommandResult.Fail("Lỗi tạo file: " + ex.Message, result.Messages);
        }
        finally
        {
            if (newDoc != null && config.CloseAfterSave)
            {
                try { newDoc.Close(false); } catch { /* đã đóng */ }
            }
        }
    }

    internal static string ResolveOutputPath(ProjectFromTemplateConfig config, string revitVersion)
    {
        var name = config.OutputPath
            .Replace("{projectCode}", FileNaming.Sanitize(config.ProjectCode))
            .Replace("{discipline}", FileNaming.Sanitize(config.Discipline))
            .Replace("{revitVersion}", revitVersion)
            .Replace("{yyyy-MM-dd}", DateTime.Now.ToString("yyyy-MM-dd"));
        return name.EndsWith(".rvt", StringComparison.OrdinalIgnoreCase) ? name : name + ".rvt";
    }
}
