using System.Text;
using Autodesk.Revit.DB;
using DhcbTools.Shared.Logic;
using DhcbTools.Shared.Logic.Bcf;
using DhcbTools.Shared.Logic.Checks;
using DhcbTools.Shared.Logic.Mep;

namespace DhcbTools.Core.Checks;

/// <summary>Mục 4.3 — clash nội bộ giữa hai nhóm category.</summary>
public sealed class ClashDetectionConfig
{
    public required List<string> CategoriesA { get; init; }

    public required List<string> CategoriesB { get; init; }

    /// <summary>
    /// Xét cả phần tử ở model LIÊN KẾT cho nhóm B (mặc định bật). Hồ sơ Việt Nam tách file MEP với
    /// kiến trúc/kết cấu rồi link vào nhau, nên tắt cái này là "MEP × Kết cấu" không tìm thấy gì mà
    /// vẫn báo thành công — một báo cáo va chạm nói "không có va chạm" là thứ người ta tin và làm theo.
    /// </summary>
    public bool IncludeLinkedModels { get; init; } = true;

    /// <summary>Chỉ xét link có tên chứa một trong các chuỗi này (rỗng = mọi link đã nạp).</summary>
    public List<string> LinkNameContains { get; init; } = new List<string>();

    public required string OutputPath { get; init; }

    /// <summary>File clash-accepted.json: cặp đã chấp nhận không báo lại.</summary>
    public string? AcceptedPath { get; init; }

    /// <summary>Dung sai lọc thô bounding box (mm).</summary>
    public double BoundingBoxToleranceMm { get; init; } = 0;

    /// <summary>
    /// Tạo/ghi đè 3D view "ViewName" và isolate các phần tử va chạm — đây là thao tác GHI duy nhất của
    /// lệnh, nên mặc định tắt và chỉ chạy khi <see cref="DryRun"/> = false.
    /// </summary>
    public bool Create3dView { get; init; } = false;

    public string ViewName { get; init; } = "DHCB - Clashes";

    /// <summary>Xem trước: quét và ghi báo cáo như thường, nhưng không tạo 3D view trong mô hình.</summary>
    public bool DryRun { get; init; } = true;

    /// <summary>
    /// Ngoài báo cáo HTML, ghi thêm file <b>BCF 2.1</b> (đề xuất B3) để mở thẳng trong Navisworks /
    /// Solibri / BIMcollab — nơi tổ điều phối thật sự duyệt va chạm. Rỗng = không ghi.
    /// </summary>
    public string? BcfPath { get; init; }

    /// <summary>Tên dự án ghi vào <c>project.bcfp</c>; rỗng = lấy tên file Revit.</summary>
    public string? BcfProjectName { get; init; }

    /// <summary>Giới hạn số va chạm báo (0 = không giới hạn).</summary>
    public int MaxResults { get; init; } = 2000;
}

/// <summary>
/// Lọc thô <see cref="MepLayout.BoundingBoxesIntersect"/> → <see cref="ElementIntersectsElementFilter"/> chính xác.
/// Khoá, Summary, HTML, BCF và mọi câu giải thích nằm ở <see cref="ClashReport"/> (có test trên CI);
/// file này chỉ còn quét hình học Revit và tạo 3D view.
/// </summary>
public sealed class ClashDetectionCommand : ICoreCommand<ClashDetectionConfig>
{
    public string CommandName => "ClashDetection";

    private sealed record Clash(Element A, Element B, ClashRecord Record, ElementId? LinkInstanceId);

    /// <summary>Phần tử nhóm B kèm hộp bao ĐÃ ĐƯA VỀ toạ độ file chủ (link thì khác toạ độ).</summary>
    private sealed record Candidate(Element Element, Box3 Box, Transform? Transform, string? LinkName, ElementId? LinkInstanceId);

    public CommandResult Execute(Document document, ClashDetectionConfig config)
    {
        var idsA = ParameterSync.ParameterExportCommand.ResolveCategoryIds(document, config.CategoriesA, out var unknownA);
        var idsB = ParameterSync.ParameterExportCommand.ResolveCategoryIds(document, config.CategoriesB, out var unknownB);
        if (idsA.Count == 0 || idsB.Count == 0)
        {
            return CommandResult.Fail(ClashReport.UnknownCategoriesError(unknownA.Concat(unknownB)));
        }

        var result = CommandResult.Ok(string.Empty);

        // Bug #14: bản sao model làm mất trạng thái nạp link → lệnh này báo 0 va chạm thay vì 479, và
        // im lặng vì "0 va chạm" trông y hệt kết quả sạch. Chỗ vá cũ chỉ nằm ở BatchJobRunner.Open();
        // đường Ribbon/Bridge không đi qua đó, nên tiền đề phải nằm ngay trong lệnh.
        if (config.IncludeLinkedModels
            && RevitPrecondition.Blocks(RevitPrecondition.LinkedModels(document, CommandName), result))
        {
            return result;
        }

        var elementsA = new FilteredElementCollector(document).WhereElementIsNotElementType().WherePasses(new ElementMulticategoryFilter(idsA.ToList())).ToElements();
        var elementsB = new FilteredElementCollector(document).WhereElementIsNotElementType().WherePasses(new ElementMulticategoryFilter(idsB.ToList())).ToElements()
            .Select(e => Describe(e, null, null, null)).Where(c => c != null).Select(c => c!).ToList();

        // Nhóm B ở model liên kết. Không có nhánh này thì "Ducts × Structural Framing" trên file MEP
        // luôn ra 0 va chạm — dầm nằm bên link kết cấu (đo được trên Snowdon HVAC, 2026-09-03).
        var linkSummary = new List<string>();
        if (config.IncludeLinkedModels)
        {
            foreach (var linkInstance in new FilteredElementCollector(document).OfClass(typeof(RevitLinkInstance)).Cast<RevitLinkInstance>())
            {
                var linkDoc = linkInstance.GetLinkDocument();
                if (linkDoc == null)
                {
                    linkSummary.Add($"{linkInstance.Name}: chưa nạp (unloaded) — bỏ qua");
                    continue;
                }

                if (!ClashReport.LinkNameMatches(linkInstance.Name, config.LinkNameContains))
                {
                    continue;
                }

                // Category phải tra TRONG chính link: id category là của từng document.
                var idsLink = ParameterSync.ParameterExportCommand.ResolveCategoryIds(linkDoc, config.CategoriesB, out _);
                if (idsLink.Count == 0)
                {
                    linkSummary.Add($"{linkInstance.Name}: không có category nào của nhóm B");
                    continue;
                }

                var transform = linkInstance.GetTotalTransform();
                var added = 0;
                foreach (var e in new FilteredElementCollector(linkDoc).WhereElementIsNotElementType()
                             .WherePasses(new ElementMulticategoryFilter(idsLink.ToList())).ToElements())
                {
                    var candidate = Describe(e, transform, linkInstance.Name, linkInstance.Id);
                    if (candidate == null) continue;
                    elementsB.Add(candidate);
                    added++;
                }
                linkSummary.Add($"{linkInstance.Name}: {added} phần tử nhóm B");
            }
        }

        // "0 va chạm" khi một trong hai nhóm rỗng là câu nói về TẬP ĐẦU VÀO, không phải về mô hình.
        var inputPre = Precondition.First(
            Precondition.NonEmptyInput(
                CommandName, $"phần tử nhóm A ({string.Join(", ", config.CategoriesA)})", elementsA.Count,
                "Kiểm lại categoriesA, hoặc mở đúng file có nhóm phần tử đó."),
            Precondition.NonEmptyInput(
                CommandName, $"phần tử nhóm B ({string.Join(", ", config.CategoriesB)})", elementsB.Count,
                config.IncludeLinkedModels
                    ? "Kiểm lại categoriesB; nếu nhóm B nằm ở file liên kết thì kiểm cả bộ lọc linkNameContains."
                    : "Kiểm lại categoriesB; nhóm B thường nằm ở file liên kết — thử bật includeLinkedModels."));
        if (RevitPrecondition.Blocks(inputPre, result))
        {
            result.Messages.AddRange(linkSummary.Select(l => "Link: " + l));
            return result;
        }

        var accepted = ClashAcceptance.LoadKeys(config.AcceptedPath);
        var tol = RevitCompat.MmToFt(config.BoundingBoxToleranceMm);
        var clashes = new List<Clash>();
        var skippedAccepted = 0;
        var seen = new HashSet<string>();

        foreach (var a in elementsA)
        {
            var boxA = a.get_BoundingBox(null);
            if (boxA == null) continue;

            var candidates = elementsB.Where(t => (t.LinkName != null || t.Element.Id != a.Id) && MepLayout.BoundingBoxesIntersect(
                boxA.Min.X, boxA.Min.Y, boxA.Min.Z, boxA.Max.X, boxA.Max.Y, boxA.Max.Z,
                t.Box.MinX, t.Box.MinY, t.Box.MinZ, t.Box.MaxX, t.Box.MaxY, t.Box.MaxZ, tol)).ToList();
            if (candidates.Count == 0) continue;

            var hits = PreciseHits(document, a, candidates, result);

            foreach (var b in hits)
            {
                var centre = ClashReport.IntersectionCentre(
                    boxA.Min.X, boxA.Min.Y, boxA.Min.Z, boxA.Max.X, boxA.Max.Y, boxA.Max.Z,
                    b.Box.MinX, b.Box.MinY, b.Box.MinZ, b.Box.MaxX, b.Box.MaxY, b.Box.MaxZ);
                var xMm = RevitCompat.FtToMm(centre.X);
                var yMm = RevitCompat.FtToMm(centre.Y);
                var zMm = RevitCompat.FtToMm(centre.Z);
                var key = ClashReport.MakeKey(
                    RevitCompat.IdValue(a.Id), RevitCompat.IdValue(b.Element.Id), xMm, yMm, zMm,
                    b.LinkInstanceId == null ? (long?)null : RevitCompat.IdValue(b.LinkInstanceId));
                if (!seen.Add(key)) continue;
                if (accepted.Contains(key))
                {
                    skippedAccepted++;
                    continue;
                }

                var record = new ClashRecord(
                    RevitCompat.IdValue(a.Id), a.Category?.Name, a.Name,
                    RevitCompat.IdValue(b.Element.Id), b.Element.Category?.Name,
                    xMm, yMm, zMm, key, b.LinkName);
                clashes.Add(new Clash(a, b.Element, record, b.LinkInstanceId));
                if (config.MaxResults > 0 && clashes.Count >= config.MaxResults)
                {
                    result.Messages.Add(ClashReport.MaxResultsNote(config.MaxResults));
                    goto Done;
                }
            }
        }

    Done:
        var records = clashes.Select(c => c.Record).ToList();
        RevitCompat.EnsureParentDirectory(config.OutputPath);
        File.WriteAllText(config.OutputPath, ClashReport.Html(document.Title, config.CategoriesA, config.CategoriesB, records, skippedAccepted), Encoding.UTF8);
        WriteBcf(document, config, records, result);

        if (config.Create3dView && clashes.Count > 0)
        {
            // IsolateElementsTemporary chỉ nhận id của CHÍNH document này: id phần tử trong link ném
            // ArgumentException. Với cặp link chỉ isolate phần tử phía file chủ, phần tử link nêu trong Messages.
            var hostIds = clashes.Select(c => c.A.Id)
                .Concat(clashes.Where(c => c.LinkInstanceId == null).Select(c => c.B.Id))
                .Distinct().ToList();
            var fromLinks = clashes.Count(c => c.LinkInstanceId != null);

            if (config.DryRun)
            {
                result.Messages.Add(ClashReport.ViewNote(true, config.ViewName, hostIds.Count, fromLinks));
            }
            else
            {
                try
                {
                    using var tx = RevitCompat.StartTransaction(document, "DHCB - View va chạm");
                    var vft = new FilteredElementCollector(document).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>().FirstOrDefault(v => v.ViewFamily == ViewFamily.ThreeDimensional);
                    var view = new FilteredElementCollector(document).OfClass(typeof(View3D)).Cast<View3D>().FirstOrDefault(v => !v.IsTemplate && v.Name == config.ViewName)
                               ?? (vft != null ? View3D.CreateIsometric(document, vft.Id) : null);
                    if (view != null)
                    {
                        try { view.Name = config.ViewName; } catch { /* trùng tên */ }
                        view.IsolateElementsTemporary(hostIds);
                        result.Messages.Add(ClashReport.ViewNote(false, view.Name, hostIds.Count, fromLinks));
                    }
                    tx.Commit();
                }
                catch (Exception ex)
                {
                    result.Messages.Add("Không tạo được 3D view: " + ex.Message);
                }
            }
        }

        var inDocument = elementsB.Count(c => c.LinkName == null);
        var inLinks = elementsB.Count - inDocument;
        result.Messages.AddRange(ClashReport.Notes(records, inDocument, inLinks, linkSummary));
        result.Summary = ClashReport.Summary(records, skippedAccepted, config.OutputPath, elementsA.Count, inDocument, inLinks, config.IncludeLinkedModels);
        result.AffectedCount = clashes.Count;
        return result;
    }

    /// <summary>
    /// Phần tử nhóm B kèm hộp bao ở toạ độ file chủ. Link xoay thì hộp bao dựng lại từ tám đỉnh
    /// (<see cref="SleevePlanner.TransformedBox"/>) — lấy hai điểm min/max qua phép biến đổi là sai khi có xoay.
    /// </summary>
    private static Candidate? Describe(Element element, Transform? transform, string? linkName, ElementId? linkInstanceId)
    {
        var box = element.get_BoundingBox(null);
        if (box == null) return null;

        var local = new Box3(box.Min.X, box.Min.Y, box.Min.Z, box.Max.X, box.Max.Y, box.Max.Z);
        if (transform == null)
        {
            return new Candidate(element, local, null, null, null);
        }

        var moved = SleevePlanner.TransformedBox(local, (x, y, z) =>
        {
            var p = transform.OfPoint(new XYZ(x, y, z));
            return (p.X, p.Y, p.Z);
        });
        return new Candidate(element, moved, transform, linkName, linkInstanceId);
    }

    /// <summary>
    /// Lọc tinh (solid × solid) sau bước hộp bao.
    /// <para>
    /// Ứng viên cùng file dùng <see cref="ElementIntersectsElementFilter"/> như cũ. Ứng viên ở link thì
    /// KHÔNG dùng được filter đó (nó so trong một document), nên đưa solid của A về toạ độ link rồi lọc
    /// bằng <see cref="ElementIntersectsSolidFilter"/> ngay trong document của link — vẫn là phép so
    /// solid thật, không rơi về mức hộp bao.
    /// </para>
    /// </summary>
    private static List<Candidate> PreciseHits(Document document, Element a, List<Candidate> candidates, CommandResult result)
    {
        var hits = new List<Candidate>();

        var sameDoc = candidates.Where(c => c.LinkName == null).ToList();
        if (sameDoc.Count > 0)
        {
            try
            {
                var ids = new FilteredElementCollector(document, sameDoc.Select(c => c.Element.Id).ToList())
                    .WherePasses(new ElementIntersectsElementFilter(a))
                    .ToElementIds();
                hits.AddRange(sameDoc.Where(c => ids.Contains(c.Element.Id)));
            }
            catch (Exception ex)
            {
                result.Messages.Add($"{a.Id}: không kiểm được solid ({ex.Message}) — dùng kết quả bounding box.");
                hits.AddRange(sameDoc);
            }
        }

        foreach (var group in candidates.Where(c => c.LinkName != null).GroupBy(c => c.LinkName))
        {
            var list = group.ToList();
            var transform = list[0].Transform!;
            var solid = FirstSolid(a);
            if (solid == null)
            {
                // Không lấy được solid của A (phần tử suy biến, geometry rỗng) — giữ kết quả hộp bao và
                // nói rõ, thay vì âm thầm bỏ qua cả nhóm link.
                result.Messages.Add($"{a.Id}: không lấy được solid — va chạm với link \"{group.Key}\" chỉ ở mức hộp bao.");
                hits.AddRange(list);
                continue;
            }

            try
            {
                var inLinkCoords = SolidUtils.CreateTransformed(solid, transform.Inverse);
                var linkDoc = list[0].Element.Document;
                var ids = new FilteredElementCollector(linkDoc, list.Select(c => c.Element.Id).ToList())
                    .WherePasses(new ElementIntersectsSolidFilter(inLinkCoords))
                    .ToElementIds();
                hits.AddRange(list.Where(c => ids.Contains(c.Element.Id)));
            }
            catch (Exception ex)
            {
                result.Messages.Add($"{a.Id}: không kiểm được solid với link \"{group.Key}\" ({ex.Message}) — dùng kết quả bounding box.");
                hits.AddRange(list);
            }
        }

        return hits;
    }

    private static Solid? FirstSolid(Element element)
    {
        try
        {
            var geometry = element.get_Geometry(new Options { ComputeReferences = false, DetailLevel = ViewDetailLevel.Coarse });
            if (geometry == null) return null;

            foreach (var obj in geometry)
            {
                if (obj is Solid s && s.Volume > 1e-9) return s;
                if (obj is GeometryInstance gi)
                {
                    foreach (var inner in gi.GetInstanceGeometry())
                    {
                        if (inner is Solid s2 && s2.Volume > 1e-9) return s2;
                    }
                }
            }
        }
        catch (Exception)
        {
            // Không đọc được geometry thì coi như không có solid — người gọi tự xử lý.
        }

        return null;
    }

    /// <summary>Xuất BCF 2.1 — nội dung vấn đề dựng ở <see cref="ClashReport.BcfIssues"/>; ở đây chỉ ghi file.</summary>
    private static void WriteBcf(Document doc, ClashDetectionConfig config, List<ClashRecord> clashes, CommandResult result)
    {
        if (string.IsNullOrWhiteSpace(config.BcfPath)) return;

        try
        {
            var issues = ClashReport.BcfIssues(doc.Title, clashes);
            BcfWriter.WriteFile(config.BcfPath!, issues, new BcfProject { Name = ClashReport.BcfProjectName(config.BcfProjectName, doc.Title) });
            result.Messages.Add($"Đã ghi BCF {BcfWriter.Version}: {issues.Count} vấn đề → \"{config.BcfPath}\".");
        }
        catch (Exception ex)
        {
            // Báo cáo HTML đã ghi xong; hỏng phần BCF không được biến cả lượt quét thành thất bại,
            // nhưng cũng không được im — người dùng đang chờ một file để mở bên Navisworks.
            result.Messages.Add("Không ghi được file BCF: " + ex.Message);
        }
    }
}
