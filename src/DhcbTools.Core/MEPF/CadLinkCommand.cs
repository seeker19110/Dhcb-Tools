using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using DhcbTools.Core.Checks;

namespace DhcbTools.Core.MEPF;

/// <summary>Cấu hình <c>CadLink</c>: đưa một file CAD vào mô hình để <c>ModelLinesFromCad</c> đọc được.</summary>
public sealed class CadLinkConfig
{
    /// <summary>Đường dẫn file CAD (<c>.dwg</c> hoặc <c>.dxf</c>). Bắt buộc.</summary>
    public string CadPath { get; init; } = string.Empty;

    /// <summary>Tầng đặt bản vẽ (rỗng = tầng thấp nhất). Bản vẽ vào view mặt bằng của tầng này.</summary>
    public string? LevelName { get; init; }

    /// <summary>
    /// Đơn vị của file CAD: <c>mm</c>, <c>cm</c>, <c>m</c>, <c>inch</c>, <c>ft</c>, hoặc <c>auto</c> để Revit
    /// tự đọc từ file. Sai đơn vị là bản vẽ vào mô hình lệch 1000 lần mà không lệnh nào báo lỗi.
    /// </summary>
    public string Unit { get; init; } = "auto";

    /// <summary>
    /// Cách đặt: <c>origin</c> (gốc CAD trùng gốc dự án — mặc định, giữ được toạ độ), <c>shared</c>
    /// (theo toạ độ chung), <c>centered</c>.
    /// </summary>
    public string Placement { get; init; } = "origin";

    /// <summary>Chỉ hiện trong view được đặt vào. Mặc định tắt để mọi view mặt bằng đều thấy.</summary>
    public bool ThisViewOnly { get; init; }

    /// <summary>Xem trước: kiểm file, tầng, view và báo sẽ làm gì, KHÔNG đưa gì vào mô hình.</summary>
    public bool DryRun { get; init; } = true;
}

/// <summary>
/// Link một file CAD vào mô hình — bước "Insert → Link CAD" mà kỹ sư đang làm tay cho từng tầng, từng
/// model.
/// <para>
/// Vì sao có lệnh này: <c>ModelLinesFromCad</c> (C4) đọc bản vẽ CAD <b>đã có trong mô hình</b>, nên cả
/// chuỗi DWG → model line → <c>RouteFromLines</c> vẫn đứt ở mắt đầu tiên khi chạy batch đêm — không ai
/// ngồi bấm Insert lúc 2 giờ sáng. Vòng chạy thật 2026-09-05 còn cho thấy hệ quả thứ hai: <b>không có
/// lệnh này thì đường thành công của C4 không có cách nào kiểm tự động</b>, vì bộ ca kiểm chỉ chạy được
/// lệnh trong <see cref="RevitCommandTable"/> và không model mẫu nào của Revit có sẵn CAD link
/// (xem <c>docs/bang-chung-test.md</c> §28 và §29).
/// </para>
/// <para>
/// Chạy lại <b>không sinh bản sao</b>: file đã có trong mô hình thì bỏ qua và nói rõ, theo đúng cách chốt
/// tính idempotent của §12.
/// </para>
/// </summary>
public sealed class CadLinkCommand : ICoreCommand<CadLinkConfig>
{
    public string CommandName => "CadLink";

    public CommandResult Execute(Document document, CadLinkConfig config)
    {
        var result = CommandResult.Ok(string.Empty);

        if (string.IsNullOrWhiteSpace(config.CadPath))
        {
            return CommandResult.Fail("E-CONFIG-MISSING: CadLink cần trường \"cadPath\" (đường dẫn file .dwg hoặc .dxf).");
        }

        if (!File.Exists(config.CadPath))
        {
            return CommandResult.Fail($"E-PATH-MISSING: không tìm thấy file CAD \"{config.CadPath}\".");
        }

        var extension = Path.GetExtension(config.CadPath);
        if (!string.Equals(extension, ".dwg", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(extension, ".dxf", StringComparison.OrdinalIgnoreCase))
        {
            return CommandResult.Fail($"Chỉ nhận file .dwg hoặc .dxf, file này là \"{extension}\".");
        }

        if (!TryParseUnit(config.Unit, out var unit, out var unitError))
        {
            return CommandResult.Fail(unitError);
        }

        if (!TryParsePlacement(config.Placement, out var placement, out var placementError))
        {
            return CommandResult.Fail(placementError);
        }

        var level = RevitCompat.FindLevel(document, config.LevelName)
                    ?? new FilteredElementCollector(document).OfClass(typeof(Level)).Cast<Level>()
                        .OrderBy(l => l.Elevation).FirstOrDefault();
        if (level == null)
        {
            return CommandResult.Fail("Mô hình không có Level nào để đặt bản vẽ CAD.");
        }

        var view = PlanViewOf(document, level);
        if (view == null)
        {
            return CommandResult.Fail(
                $"Tầng \"{level.Name}\" không có view mặt bằng nào để đặt bản vẽ — tạo một view mặt bằng cho tầng này rồi chạy lại.");
        }

        // Trùng theo TÊN FILE chứ không theo đường dẫn: cùng một bản vẽ chép sang thư mục khác vẫn là
        // cùng bản vẽ, link hai lần là hai bộ hình học chồng nhau mà ModelLinesFromCad đọc thành đường đôi.
        var fileName = Path.GetFileName(config.CadPath);
        var existing = new FilteredElementCollector(document).OfClass(typeof(ImportInstance)).Cast<ImportInstance>()
            .FirstOrDefault(i => RevitCompat.CadFileName(document, i)
                .IndexOf(Path.GetFileNameWithoutExtension(fileName), StringComparison.OrdinalIgnoreCase) >= 0);
        if (existing != null)
        {
            result.Summary = $"Đã có \"{fileName}\" trong mô hình (id {RevitCompat.IdValue(existing.Id)}) — bỏ qua, không link lần hai.";
            result.Messages.Add("Muốn nạp bản vẽ mới thì xoá link cũ trong Manage → Manage Links → CAD Formats rồi chạy lại.");
            result.AffectedCount = 0;
            return result;
        }

        if (config.DryRun)
        {
            result.Summary = $"[Xem trước] Sẽ link \"{fileName}\" vào view \"{view.Name}\" của tầng \"{level.Name}\" "
                             + $"(đơn vị {DescribeUnit(unit)}, đặt theo {config.Placement.ToLowerInvariant()}).";
            result.AffectedCount = 1;
            return result;
        }

        var options = new DWGImportOptions
        {
            Unit = unit,
            Placement = placement,
            ThisViewOnly = config.ThisViewOnly,
            OrientToView = false,
            ColorMode = ImportColorMode.BlackAndWhite,
        };

        // Link (không Import): bản vẽ đổi thì Reload là xong, và không đẻ ra hàng nghìn phần tử trong mô hình.
        using (var tx = new Transaction(document, "DHCB — Link bản vẽ CAD"))
        {
            tx.Start();
            if (!document.Link(config.CadPath, options, view, out var linkedId))
            {
                tx.RollBack();
                return CommandResult.Fail(
                    $"Revit từ chối link \"{fileName}\" — kiểm file có mở được trong AutoCAD không, và có phải bản DWG quá mới không.");
            }

            tx.Commit();
            result.ChangedIds.Add(RevitCompat.IdValue(linkedId));
            result.Summary = $"Đã link \"{fileName}\" vào view \"{view.Name}\" của tầng \"{level.Name}\" "
                             + $"(đơn vị {DescribeUnit(unit)}, đặt theo {config.Placement.ToLowerInvariant()}).";
            result.AffectedCount = 1;
        }

        return result;
    }

    /// <summary>View mặt bằng của tầng, bỏ view template và view đã đặt lên sheet của người khác.</summary>
    private static View? PlanViewOf(Document document, Level level) =>
        new FilteredElementCollector(document).OfClass(typeof(ViewPlan)).Cast<ViewPlan>()
            .Where(v => !v.IsTemplate && v.GenLevel != null && v.GenLevel.Id == level.Id)
            .OrderBy(v => v.ViewType == ViewType.FloorPlan ? 0 : 1)
            .ThenBy(v => v.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

    internal static bool TryParseUnit(string? text, out ImportUnit unit, out string error)
    {
        error = string.Empty;
        unit = ImportUnit.Default;
        var value = (text ?? string.Empty).Trim().ToLowerInvariant();
        switch (value)
        {
            case "":
            case "auto":
            case "default":
                unit = ImportUnit.Default;
                return true;
            case "mm":
            case "milimet":
            case "millimeter":
                unit = ImportUnit.Millimeter;
                return true;
            case "cm":
            case "centimet":
                unit = ImportUnit.Centimeter;
                return true;
            case "m":
            case "met":
            case "mét":
                unit = ImportUnit.Meter;
                return true;
            case "inch":
            case "in":
                unit = ImportUnit.Inch;
                return true;
            case "ft":
            case "foot":
            case "feet":
                unit = ImportUnit.Foot;
                return true;
            default:
                error = $"Đơn vị \"{text}\" không hợp lệ. Hợp lệ: auto (Revit tự đọc từ file), mm, cm, m, inch, ft.";
                return false;
        }
    }

    internal static bool TryParsePlacement(string? text, out ImportPlacement placement, out string error)
    {
        error = string.Empty;
        placement = ImportPlacement.Origin;
        var value = (text ?? string.Empty).Trim().ToLowerInvariant();
        switch (value)
        {
            case "":
            case "origin":
            case "goc":
            case "gốc":
                placement = ImportPlacement.Origin;
                return true;
            case "shared":
            case "chung":
                placement = ImportPlacement.Shared;
                return true;
            case "centered":
            case "center":
            case "giua":
            case "giữa":
                placement = ImportPlacement.Centered;
                return true;
            default:
                error = $"Cách đặt \"{text}\" không hợp lệ. Hợp lệ: origin (gốc dự án), shared (toạ độ chung), centered (giữa view).";
                return false;
        }
    }

    private static string DescribeUnit(ImportUnit unit) => unit == ImportUnit.Default ? "tự đọc từ file" : unit.ToString();
}
