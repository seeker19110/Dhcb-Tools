using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using DhcbTools.Core.Checks;
using DhcbTools.Shared.Logic.Checks;
using StampConfig = DhcbTools.Shared.Logic.AsBuilt.AsBuiltStampConfig;
using StampBuilder = DhcbTools.Shared.Logic.AsBuilt.AsBuiltStampBuilder;
using StampTextStyle = DhcbTools.Shared.Logic.AsBuilt.StampTextStyle;
using StampAlign = DhcbTools.Shared.Logic.AsBuilt.StampAlign;

namespace DhcbTools.Core.AsBuilt;

/// <summary>
/// Mục 11.6, đề xuất C2: gán khuôn dấu bản vẽ hoàn công (Phụ lục IIb, NĐ 207/2026/NĐ-CP) lên loạt sheet.
/// <para>
/// Vẽ trực tiếp lên sheet bằng <c>DetailCurve</c>/<c>TextNote</c> thay vì nạp một family — Revit API
/// <b>không có</b> cách tạo phần tử "Label" gắn tham số trong family annotation (đã kiểm bằng phản chiếu
/// toàn bộ <c>RevitAPI.dll</c> 2024: không có <c>NewLabel</c> ở đâu cả), nên hướng "family mẫu dấu" ban đầu
/// của roadmap không khả thi qua API công khai. Vẽ thẳng lên sheet cho đúng kết quả cuối (dấu có tên/ngày
/// thật trên bản in) mà không cần quản lý một file family riêng.
/// </para>
/// <para>Hình học/nội dung khuôn dấu ở tầng thuần <see cref="StampBuilder"/> — file này chỉ dịch mm → ft.</para>
/// </summary>
public sealed class AsBuiltStampConfig
{
    /// <summary>1 = hợp đồng thường; 2 = hợp đồng thầu chính/phụ, EPC, chìa khoá trao tay.</summary>
    public int Mau { get; init; } = 1;

    public string TenNhaThau { get; init; } = string.Empty;

    public string? Ngay { get; init; }

    public string? Thang { get; init; }

    public string? Nam { get; init; }

    public string NguoiLap { get; init; } = string.Empty;

    /// <summary>Chỉ dùng ở Mẫu 1.</summary>
    public string? ChiHuyTruongHoacGiamDocDuAn { get; init; }

    /// <summary>Chỉ dùng ở Mẫu 2 — tuỳ chọn (có thể không có thầu phụ).</summary>
    public string? ChiHuyTruongNhaThauPhu { get; init; }

    /// <summary>Chỉ dùng ở Mẫu 2.</summary>
    public string? ChiHuyTruongNhaThauChinh { get; init; }

    public string TuVanGiamSatTruong { get; init; } = string.Empty;

    public double WidthMm { get; init; } = 160;

    public double TextHeightMm { get; init; } = 3.0;

    /// <summary>Danh sách số sheet chính xác (rỗng = theo bộ lọc chứa).</summary>
    public List<string> SheetNumbers { get; init; } = new List<string>();

    public string? SheetNumberContains { get; init; }

    /// <summary>
    /// Neo góc dưới-trái của khuôn dấu, tính từ góc dưới-trái vùng in của sheet (mm). Rỗng = tự đặt cách
    /// mép <see cref="MarginMm"/> — vùng trống thường gặp nhất trên khổ giấy chuẩn, khung tên thường nằm
    /// bên phải/dưới nên góc dưới-trái ít khi bị đè. Dự án có khung tên khác thì khai đè hai trường này.
    /// </summary>
    public double? AnchorXMm { get; init; }

    public double? AnchorYMm { get; init; }

    /// <summary>Khoảng cách tới mép vùng in khi tự đặt neo (không khai <see cref="AnchorXMm"/>/<see cref="AnchorYMm"/>).</summary>
    public double MarginMm { get; init; } = 15;

    /// <summary>Sheet đã có dấu (đè đúng vùng chữ nhật khuôn dấu) thì ghi đè thay vì bỏ qua.</summary>
    public bool Overwrite { get; init; }

    public bool DryRun { get; init; } = true;
}

public sealed class AsBuiltStampCommand : ICoreCommand<AsBuiltStampConfig>
{
    public string CommandName => "AsBuiltStamp";

    public CommandResult Execute(Document document, AsBuiltStampConfig config)
    {
        var stampConfig = new StampConfig
        {
            Mau = config.Mau,
            TenNhaThau = config.TenNhaThau,
            Ngay = config.Ngay,
            Thang = config.Thang,
            Nam = config.Nam,
            NguoiLap = config.NguoiLap,
            ChiHuyTruongHoacGiamDocDuAn = config.ChiHuyTruongHoacGiamDocDuAn,
            ChiHuyTruongNhaThauPhu = config.ChiHuyTruongNhaThauPhu,
            ChiHuyTruongNhaThauChinh = config.ChiHuyTruongNhaThauChinh,
            TuVanGiamSatTruong = config.TuVanGiamSatTruong,
            WidthMm = config.WidthMm,
            TextHeightMm = config.TextHeightMm,
        };

        var missing = StampBuilder.Validate(stampConfig);
        if (missing.Count > 0)
        {
            return CommandResult.Fail(
                $"E-CONFIG-MISSING: AsBuiltStamp (Mẫu {config.Mau}) thiếu trường bắt buộc: "
                + string.Join(", ", missing.Select(m => "\"" + m + "\"")) + ".");
        }

        var sheets = new FilteredElementCollector(document).OfClass(typeof(ViewSheet)).Cast<ViewSheet>()
            .Where(s => config.SheetNumbers.Count > 0
                ? config.SheetNumbers.Any(n => string.Equals(n, s.SheetNumber, StringComparison.OrdinalIgnoreCase))
                : string.IsNullOrEmpty(config.SheetNumberContains) || s.SheetNumber.IndexOf(config.SheetNumberContains!, StringComparison.OrdinalIgnoreCase) >= 0)
            .OrderBy(s => s.SheetNumber, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var precondition = Precondition.NonEmptyInput(
            CommandName, "sheet", sheets.Count,
            "Không có sheet nào khớp bộ lọc — kiểm lại sheetNumbers/sheetNumberContains.");
        var blocked = CommandResult.Ok(string.Empty);
        if (RevitPrecondition.Blocks(precondition, blocked))
        {
            return blocked;
        }

        var layout = StampBuilder.Build(stampConfig);

        var result = CommandResult.Ok(string.Empty);
        var anchors = Anchors(sheets, config);
        var existingBySheet = FindExisting(
            document,
            sheets,
            anchors,
            layout,
            includeAllStampElements: config.Overwrite && !config.DryRun);
        var toStamp = new List<(ViewSheet Sheet, XYZ Anchor, List<Element> Existing)>();
        foreach (var sheet in sheets)
        {
            var anchor = anchors[sheet.Id];
            var existing = existingBySheet.TryGetValue(sheet.Id, out var found)
                ? found
                : new List<Element>();
            if (existing.Count > 0 && !config.Overwrite)
            {
                result.Messages.Add($"{sheet.SheetNumber}: đã có dấu hoàn công trong vùng này — giữ nguyên (overwrite=false).");
                continue;
            }

            toStamp.Add((sheet, anchor, existing));
        }

        if (config.DryRun)
        {
            foreach (var (sheet, _, _) in toStamp)
            {
                result.Messages.Add($"[Xem trước] {sheet.SheetNumber} \"{sheet.Name}\": sẽ vẽ dấu Mẫu {config.Mau} ({layout.WidthMm:0} × {layout.HeightMm:0} mm).");
            }

            result.Summary = toStamp.Count == 0
                ? "[Xem trước] Không có sheet nào cần vẽ dấu (đã có dấu ở mọi sheet khớp bộ lọc)."
                : $"[Xem trước] Sẽ vẽ dấu hoàn công Mẫu {config.Mau} lên {toStamp.Count}/{sheets.Count} sheet.";
            result.AffectedCount = toStamp.Count;
            return result;
        }

        var stamped = 0;
        using (var tx = RevitCompat.StartTransaction(document, "DHCB - Dấu bản vẽ hoàn công"))
        {
            foreach (var (sheet, anchor, existing) in toStamp)
            {
                try
                {
                    if (existing.Count > 0)
                    {
                        document.Delete(existing.Select(e => e.Id).ToList());
                    }

                    Draw(document, sheet, layout, anchor);
                    stamped++;
                    result.Messages.Add($"[Vẽ] {sheet.SheetNumber} \"{sheet.Name}\": đã vẽ dấu Mẫu {config.Mau}.");
                }
                catch (Exception ex)
                {
                    result.Errors.Add($"{sheet.SheetNumber}: không vẽ được dấu ({ex.Message}).");
                }
            }

            tx.Commit();
        }

        result.Summary = $"Đã vẽ dấu hoàn công Mẫu {config.Mau} lên {stamped}/{sheets.Count} sheet.";
        result.AffectedCount = stamped;
        result.Success = stamped > 0 || toStamp.Count == 0;
        return result;
    }

    private static Dictionary<ElementId, XYZ> Anchors(
        IReadOnlyCollection<ViewSheet> sheets,
        AsBuiltStampConfig config)
    {
        var margin = RevitCompat.MmToFt(config.MarginMm);
        // Revit định nghĩa paper space của sheet với [0,0] ở góc dưới-trái. ViewSheet.Outline.Min trả
        // chính gốc đó nhưng lần truy cập đầu có thể buộc Revit dựng vùng hiển thị; profile Snowdon đo
        // 190 giây/55 sheet. Dùng trực tiếp hệ paper space giữ nguyên vị trí mà không phụ thuộc hình học
        // hoặc điểm chèn tùy ý của family khung tên.
        // Source: https://help.autodesk.com/cloudhelp/2024/ENU/Revit-API/files/Revit_API_Developers_Guide/Basic_Interaction_with_Revit_Elements/Views/View_Types/Revit_API_Revit_API_Developers_Guide_Basic_Interaction_with_Revit_Elements_Views_View_Types_ViewSheet_html.html
        var u = config.AnchorXMm.HasValue ? RevitCompat.MmToFt(config.AnchorXMm.Value) : margin;
        var v = config.AnchorYMm.HasValue ? RevitCompat.MmToFt(config.AnchorYMm.Value) : margin;
        return sheets.ToDictionary(sheet => sheet.Id, _ => new XYZ(u, v, 0));
    }

    /// <summary>
    /// Nhận diện dấu đã vẽ trước đó bằng chính <b>vùng chữ nhật</b> của khuôn dấu tại đúng điểm neo hiện
    /// tại — không dùng tham số đánh dấu (Comments): thử rồi mới biết <c>ALL_MODEL_INSTANCE_COMMENTS</c>
    /// không tồn tại trên category Text Notes/Lines của model mẫu Snowdon, nên <c>Set</c> im lặng không
    /// làm gì — lần chạy sau không bao giờ thấy "đã có dấu", chạy lại đè chồng vô hạn (§73). Vùng hình học
    /// thì luôn có, không phụ thuộc binding tham số của từng dự án: khuôn dấu vẽ ra từ đúng một config
    /// luôn nằm khít trong đúng một chữ nhật, nên "phần tử Text Note/Detail Line nằm trong vùng đó" là tín
    /// hiệu đáng tin — dùng thẳng toạ độ đặt phần tử (<c>Coord</c>/<c>GeometryCurve</c>), đúng hệ U/V với
    /// lúc dựng nên không có bất ngờ nào về hệ trục. Đã chạy thật xác nhận đúng trên Revit 2024 (§73).
    /// </summary>
    private static Dictionary<ElementId, List<Element>> FindExisting(
        Document document,
        IReadOnlyCollection<ViewSheet> sheets,
        IReadOnlyDictionary<ElementId, XYZ> anchors,
        Shared.Logic.AsBuilt.AsBuiltStampLayout layout,
        bool includeAllStampElements)
    {
        var pad = RevitCompat.MmToFt(2);
        var width = RevitCompat.MmToFt(layout.WidthMm);
        var height = RevitCompat.MmToFt(layout.HeightMm);
        var sheetIds = new HashSet<ElementId>(sheets.Select(sheet => sheet.Id));

        bool PointInRegion(ElementId sheetId, XYZ point)
        {
            var anchor = anchors[sheetId];
            return point.X >= anchor.X - pad
                && point.X <= anchor.X + width + pad
                && point.Y >= anchor.Y - pad
                && point.Y <= anchor.Y + height + pad;
        }

        // Không dùng FilteredElementCollector(document, viewId): tài liệu Autodesk cảnh báo lần đầu
        // dựng collector theo view có thể tái dựng hình học hiển thị. Với hàng chục sheet, hai collector
        // mỗi sheet biến preview chỉ đọc thành công việc kéo dài nhiều phút. Quét TextNote toàn document
        // đúng một lần và dùng OwnerViewId (quick filter) để lập chỉ mục theo sheet.
        var notesBySheet = new FilteredElementCollector(document).OfClass(typeof(TextNote)).Cast<TextNote>()
            .Where(note => sheetIds.Contains(note.OwnerViewId))
            .GroupBy(note => note.OwnerViewId)
            .ToDictionary(group => group.Key, group => group.ToList());

        var foundBySheet = new Dictionary<ElementId, List<Element>>();
        foreach (var sheet in sheets)
        {
            if (!notesBySheet.TryGetValue(sheet.Id, out var notes))
            {
                continue;
            }

            // Giữ đúng quy tắc tương thích đã chạy thật từ bản đầu: bất kỳ TextNote nào trong vùng khuôn
            // đều chứng minh vùng đã có dấu. Không dựa vào note.Text vì Revit có thể trả nội dung đã qua
            // định dạng khác chuỗi đầu vào; bộ revit-write chốt việc chạy lần hai phải bỏ qua toàn bộ.
            var regionNotes = notes.Where(note => PointInRegion(sheet.Id, note.Coord)).Cast<Element>().ToList();
            if (regionNotes.Count == 0)
            {
                continue;
            }

            foundBySheet[sheet.Id] = regionNotes;
        }

        if (!includeAllStampElements || foundBySheet.Count == 0)
        {
            return foundBySheet;
        }

        var stampedSheetIds = new HashSet<ElementId>(foundBySheet.Keys);
        // DetailLine không phải kiểu collector "gốc" của Revit; lọc CurveElement rồi kiểm lại kiểu.
        foreach (var line in new FilteredElementCollector(document).OfClass(typeof(CurveElement)).OfType<DetailLine>())
        {
            var sheetId = line.OwnerViewId;
            if (!stampedSheetIds.Contains(sheetId))
            {
                continue;
            }

            var curve = line.GeometryCurve;
            if (PointInRegion(sheetId, curve.GetEndPoint(0)) && PointInRegion(sheetId, curve.GetEndPoint(1)))
            {
                foundBySheet[sheetId].Add(line);
            }
        }

        return foundBySheet;
    }

    private static void Draw(Document document, ViewSheet sheet, Shared.Logic.AsBuilt.AsBuiltStampLayout layout, XYZ anchor)
    {
        var typeId = document.GetDefaultElementTypeId(ElementTypeGroup.TextNoteType);

        foreach (var line in layout.Lines)
        {
            var (u1, v1) = StampBuilder.ToAnchorRelative(line.X1Mm, line.Y1Mm, layout.HeightMm);
            var (u2, v2) = StampBuilder.ToAnchorRelative(line.X2Mm, line.Y2Mm, layout.HeightMm);
            var p1 = new XYZ(anchor.X + RevitCompat.MmToFt(u1), anchor.Y + RevitCompat.MmToFt(v1), 0);
            var p2 = new XYZ(anchor.X + RevitCompat.MmToFt(u2), anchor.Y + RevitCompat.MmToFt(v2), 0);
            if (p1.DistanceTo(p2) < 1e-9)
            {
                continue;
            }

            document.Create.NewDetailCurve(sheet, Line.CreateBound(p1, p2));
        }

        foreach (var text in layout.Texts)
        {
            var (u, v) = StampBuilder.ToAnchorRelative(text.XMm, text.YMm, layout.HeightMm);
            var position = new XYZ(anchor.X + RevitCompat.MmToFt(u), anchor.Y + RevitCompat.MmToFt(v), 0);
            var note = TextNote.Create(document, sheet.Id, position, RevitCompat.MmToFt(text.WidthMm), text.Text, typeId);
            note.HorizontalAlignment = text.Align == StampAlign.Center ? HorizontalTextAlignment.Center : HorizontalTextAlignment.Left;
            ApplyStyle(note, text.Style);
        }
    }

    /// <summary>Đậm/nghiêng qua <c>FormattedText</c> (Revit 2022+). Không có thì để chữ thường — nội dung
    /// vẫn đúng, chỉ mất phần nhấn mạnh thị giác, không phải lỗi chặn.</summary>
    private static void ApplyStyle(TextNote note, StampTextStyle style)
    {
        if (style == StampTextStyle.Plain)
        {
            return;
        }

        try
        {
            var formatted = note.GetFormattedText();
            var range = formatted.AsTextRange();
            if (style == StampTextStyle.Bold)
            {
                formatted.SetBoldStatus(range, true);
            }
            else
            {
                formatted.SetItalicStatus(range, true);
            }

            note.SetFormattedText(formatted);
        }
        catch (Exception)
        {
            // Bản Revit/định dạng không hỗ trợ FormattedText ở đây — bỏ qua, giữ chữ thường.
        }
    }
}
