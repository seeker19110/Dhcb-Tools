using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using DhcbTools.Shared.Logic.Families;

namespace DhcbTools.Core.Families;

/// <summary>Cấu hình <see cref="FamilyUpgradeCommand"/>: nâng cấp hàng loạt .rfa sang định dạng của Revit đang chạy.</summary>
public sealed class FamilyUpgradeConfig
{
    /// <summary>Thư mục chứa .rfa cần nâng cấp. Không bị đụng tới — lệnh chỉ đọc.</summary>
    public required string SourceFolder { get; init; }

    /// <summary>Thư mục ghi bản đã nâng cấp; phải nằm ngoài <see cref="SourceFolder"/>.</summary>
    public required string OutputFolder { get; init; }

    /// <summary>Tìm cả thư mục con (mặc định bật), giữ nguyên cây thư mục khi ghi ra.</summary>
    public bool Recursive { get; init; } = true;

    /// <summary>Ghi đè file đã có trong thư mục đích.</summary>
    public bool Overwrite { get; init; } = false;

    /// <summary>Chỉ xử lý tối đa ngần này file (0 = tất cả) — chặn lỡ tay quét cả ổ đĩa.</summary>
    public int MaxFiles { get; init; } = 0;

    /// <summary>Xem trước: chỉ liệt kê sẽ nâng cấp file nào, không mở Revit document, không ghi gì.</summary>
    public bool DryRun { get; init; } = true;
}

/// <summary>
/// Nâng cấp hàng loạt file .rfa sang định dạng của phiên bản Revit ĐANG CHẠY (§63): mở từng family rồi lưu sang
/// thư mục khác. Chạy add-in này trên Revit 2024 ra bộ family 2024, chạy trên Revit 2026 ra bộ 2026 — đó là cách
/// duy nhất có một thư viện family dùng được cho nhiều phiên bản, vì .rfa nâng cấp MỘT CHIỀU và Revit cũ không mở
/// nổi file lưu từ bản mới hơn.
///
/// Bản gốc không bao giờ bị ghi đè: <see cref="FamilyUpgradeConfig.OutputFolder"/> bắt buộc nằm ngoài thư mục nguồn.
/// </summary>
public sealed class FamilyUpgradeCommand : ICoreCommand<FamilyUpgradeConfig>
{
    public string CommandName => "FamilyUpgrade";

    public CommandResult Execute(Document document, FamilyUpgradeConfig config)
    {
        var invalid = FamilyUpgradePlanner.ValidateFolders(config.SourceFolder, config.OutputFolder);
        if (invalid != null)
        {
            return CommandResult.Fail(invalid);
        }

        if (!Directory.Exists(config.SourceFolder))
        {
            return CommandResult.Fail($"Không có thư mục nguồn: {config.SourceFolder}.");
        }

        var version = document.Application.VersionNumber;
        var found = Directory.EnumerateFiles(config.SourceFolder, "*.rfa",
            config.Recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly);
        var plan = FamilyUpgradePlanner.Plan(config.SourceFolder, config.OutputFolder, found, config.MaxFiles);
        if (plan.Count == 0)
        {
            return CommandResult.Fail(FamilyUpgradePlanner.NoFilesMessage(config.SourceFolder, config.Recursive));
        }

        var result = CommandResult.Ok(string.Empty);
        if (config.DryRun)
        {
            foreach (var item in plan)
            {
                result.Messages.Add(FamilyUpgradePlanner.PreviewLine(item));
            }

            result.Summary = FamilyUpgradePlanner.PreviewSummary(plan.Count, version);
            result.AffectedCount = plan.Count;
            return result;
        }

        var upgraded = 0;
        var skipped = 0;
        var failed = 0;
        foreach (var item in plan)
        {
            if (File.Exists(item.Target) && !config.Overwrite)
            {
                result.Messages.Add(FamilyUpgradePlanner.SkipExistingMessage(item));
                skipped++;
                continue;
            }

            Document? family = null;
            try
            {
                family = document.Application.OpenDocumentFile(item.Source);
            }
            catch (Exception ex)
            {
                result.Errors.Add(FamilyUpgradePlanner.OpenFailedMessage(item, version, ex.Message));
                failed++;
                continue;
            }

            try
            {
                if (!family.IsFamilyDocument)
                {
                    result.Errors.Add($"{item.Name}: không phải file family — bỏ qua.");
                    failed++;
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(item.Target)!);
                family.SaveAs(item.Target, new SaveAsOptions { OverwriteExistingFile = true });
                upgraded++;
                result.Messages.Add($"{item.Name}: đã nâng cấp sang Revit {version} → {item.Target}");
            }
            catch (Exception ex)
            {
                result.Errors.Add($"{item.Name}: không lưu được ({ex.Message}).");
                failed++;
            }
            finally
            {
                // Đóng không lưu: bản gốc đã mở ở đây, nếu để Revit lưu là hỏng file nguồn của phiên bản cũ.
                try { family.Close(false); }
                catch (Exception ex) { result.Messages.Add($"{item.Name}: đóng document không sạch ({ex.Message})."); }
            }
        }

        result.Summary = FamilyUpgradePlanner.DoneSummary(upgraded, skipped, failed, config.OutputFolder, version);
        result.AffectedCount = upgraded;
        result.Success = upgraded > 0 || (failed == 0 && skipped > 0);
        return result;
    }
}
