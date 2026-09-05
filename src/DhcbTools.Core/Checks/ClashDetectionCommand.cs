using System.Text;
using Autodesk.Revit.DB;
using DhcbTools.Shared.Logic;
using DhcbTools.Shared.Logic.Checks;

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

    /// <summary>Giới hạn số va chạm báo (0 = không giới hạn).</summary>
    public int MaxResults { get; init; } = 2000;

    /// <summary>
    /// File BCF 2.1 gửi tư vấn (tuỳ chọn) — mỗi va chạm một vấn đề, kèm góc nhìn đặt sẵn vào tâm va chạm.
    /// Thay việc chụp màn hình dán vào Word. Đuôi file nên là <c>.bcf</c>.
    /// </summary>
    public string? BcfPath { get; init; }
}

/// <summary>Lọc thô <see cref="MepLayout.BoundingBoxesIntersect"/> → <see cref="ElementIntersectsElementFilter"/> chính xác.</summary>
public sealed class ClashDetectionCommand : ICoreCommand<ClashDetectionConfig>
{
    public string CommandName => "ClashDetection";

    private sealed record Clash(Element A, Element B, XYZ Centre, string Key, string? LinkName, ElementId? LinkInstanceId);

    /// <summary>Phần tử nhóm B kèm hộp bao ĐÃ ĐƯA VỀ toạ độ file chủ (link thì khác toạ độ).</summary>
    private sealed record Candidate(Element Element, XYZ Min, XYZ Max, Transform? Transform, string? LinkName, ElementId? LinkInstanceId);

    public CommandResult Execute(Document document, ClashDetectionConfig config)
    {
        var idsA = ParameterSync.ParameterExportCommand.ResolveCategoryIds(document, config.CategoriesA, out var unknownA);
        var idsB = ParameterSync.ParameterExportCommand.ResolveCategoryIds(document, config.CategoriesB, out var unknownB);
        if (idsA.Count == 0 || idsB.Count == 0)
        {
            return CommandResult.Fail("Một trong hai nhóm category không có trong mô hình: " + string.Join(", ", unknownA.Concat(unknownB)));
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

                if (config.LinkNameContains.Count > 0 &&
                    !config.LinkNameContains.Any(f => linkInstance.Name.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0))
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
        var inputPre = Shared.Logic.Checks.Precondition.First(
            Shared.Logic.Checks.Precondition.NonEmptyInput(
                CommandName, $"phần tử nhóm A ({string.Join(", ", config.CategoriesA)})", elementsA.Count,
                "Kiểm lại categoriesA, hoặc mở đúng file có nhóm phần tử đó."),
            Shared.Logic.Checks.Precondition.NonEmptyInput(
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
                t.Min.X, t.Min.Y, t.Min.Z, t.Max.X, t.Max.Y, t.Max.Z, tol)).ToList();
            if (candidates.Count == 0) continue;

            var hits = PreciseHits(document, a, candidates, result);

            foreach (var b in hits)
            {
                var centre = new XYZ(
                    (Math.Max(boxA.Min.X, b.Min.X) + Math.Min(boxA.Max.X, b.Max.X)) / 2,
                    (Math.Max(boxA.Min.Y, b.Min.Y) + Math.Min(boxA.Max.Y, b.Max.Y)) / 2,
                    (Math.Max(boxA.Min.Z, b.Min.Z) + Math.Min(boxA.Max.Z, b.Max.Z)) / 2);
                var key = ClashAcceptance.MakeKey(RevitCompat.IdValue(a.Id), RevitCompat.IdValue(b.Element.Id), RevitCompat.FtToMm(centre.X), RevitCompat.FtToMm(centre.Y), RevitCompat.FtToMm(centre.Z));
                // ElementId của link là id TRONG document link, có thể trùng với id ở file chủ hoặc link khác
                // → khoá phải mang thêm id của link instance, nếu không hai va chạm khác nhau gộp làm một.
                if (b.LinkInstanceId != null)
                {
                    key += "#link" + RevitCompat.IdValue(b.LinkInstanceId);
                }
                if (!seen.Add(key)) continue;
                if (accepted.Contains(key))
                {
                    skippedAccepted++;
                    continue;
                }

                clashes.Add(new Clash(a, b.Element, centre, key, b.LinkName, b.LinkInstanceId));
                if (config.MaxResults > 0 && clashes.Count >= config.MaxResults)
                {
                    result.Messages.Add($"Đạt giới hạn {config.MaxResults} va chạm — dừng quét.");
                    goto Done;
                }
            }
        }

    Done:
        WriteHtml(document, config, clashes, skippedAccepted);
        WriteBcf(config, clashes, result);

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
                result.Messages.Add($"[Xem trước] Sẽ tạo/ghi đè 3D view \"{config.ViewName}\" và isolate {hostIds.Count} phần tử phía file chủ"
                                    + (fromLinks > 0 ? $" ({fromLinks} phần tử phía link không isolate được, xem danh sách)." : "."));
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
                        result.Messages.Add($"Đã tạo 3D view \"{view.Name}\" isolate {hostIds.Count} phần tử phía file chủ"
                                            + (fromLinks > 0 ? $"; {fromLinks} phần tử phía link không isolate được (khác document)." : "."));
                    }
                    tx.Commit();
                }
                catch (Exception ex)
                {
                    result.Messages.Add("Không tạo được 3D view: " + ex.Message);
                }
            }
        }

        foreach (var c in clashes.Take(500))
        {
            var bDesc = c.LinkName == null ? $"{c.B.Id}" : $"{c.B.Id} (link \"{c.LinkName}\")";
            result.Messages.Add($"Va chạm {c.A.Id} ({c.A.Category?.Name}) × {bDesc} ({c.B.Category?.Name}) tại ({RevitCompat.FtToMm(c.Centre.X):F0},{RevitCompat.FtToMm(c.Centre.Y):F0},{RevitCompat.FtToMm(c.Centre.Z):F0}) mm  key={c.Key}");
        }

        var inDocument = elementsB.Count(c => c.LinkName == null);
        var inLinks = elementsB.Count - inDocument;
        result.Messages.Add($"Nhóm B xét tới: {inDocument} phần tử trong file, {inLinks} từ model liên kết.");
        foreach (var line in linkSummary)
        {
            result.Messages.Add("  Link — " + line);
        }

        result.Summary = $"Tìm thấy {clashes.Count} va chạm ({skippedAccepted} đã chấp nhận, bỏ qua) → \"{config.OutputPath}\".";

        // "0 va chạm" là kết luận người ta TIN VÀ LÀM THEO, nên nó phải kèm cơ sở: xét bao nhiêu phần tử,
        // từ đâu. Bản trước chỉ có con số 0 trơ trọi, và trên file MEP link kết cấu thì con số đó luôn là 0.
        if (clashes.Count == 0)
        {
            result.Summary += elementsB.Count == 0
                ? (config.IncludeLinkedModels
                    ? " Không có phần tử nhóm B nào để xét, kể cả trong model liên kết — kiểm lại link đã nạp chưa."
                    : " Không có phần tử nhóm B nào trong file này và includeLinkedModels đang tắt — bật lên nếu nhóm B nằm ở model liên kết.")
                : $" Đã xét {elementsA.Count} × ({inDocument} trong file + {inLinks} từ model liên kết).";
        }
        else if (inLinks > 0)
        {
            var fromLinks = clashes.Count(c => c.LinkName != null);
            result.Summary += $" Trong đó {fromLinks} va chạm với model liên kết.";
        }
        result.AffectedCount = clashes.Count;
        return result;
    }


    /// <summary>
    /// Phần tử nhóm B kèm hộp bao ở toạ độ file chủ. Link xoay thì hộp bao dựng lại từ tám đỉnh —
    /// lấy hai điểm min/max qua phép biến đổi là sai khi có xoay.
    /// </summary>
    private static Candidate? Describe(Element element, Transform? transform, string? linkName, ElementId? linkInstanceId)
    {
        var box = element.get_BoundingBox(null);
        if (box == null) return null;

        if (transform == null)
        {
            return new Candidate(element, box.Min, box.Max, null, null, null);
        }

        double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;
        for (var i = 0; i < 8; i++)
        {
            var corner = transform.OfPoint(new XYZ(
                (i & 1) == 0 ? box.Min.X : box.Max.X,
                (i & 2) == 0 ? box.Min.Y : box.Max.Y,
                (i & 4) == 0 ? box.Min.Z : box.Max.Z));
            minX = Math.Min(minX, corner.X); maxX = Math.Max(maxX, corner.X);
            minY = Math.Min(minY, corner.Y); maxY = Math.Max(maxY, corner.Y);
            minZ = Math.Min(minZ, corner.Z); maxZ = Math.Max(maxZ, corner.Z);
        }

        return new Candidate(element, new XYZ(minX, minY, minZ), new XYZ(maxX, maxY, maxZ), transform, linkName, linkInstanceId);
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

    /// <summary>Mỗi va chạm một topic BCF: camera đặt vào tâm va chạm, hai phần tử là component đã chọn sẵn.</summary>
    private static void WriteBcf(ClashDetectionConfig config, List<Clash> clashes, CommandResult result)
    {
        if (string.IsNullOrWhiteSpace(config.BcfPath))
        {
            return;
        }

        var topics = new List<Shared.Logic.Bcf.BcfTopic>();
        foreach (var clash in clashes.Take(RevitBcf.MaxTopics))
        {
            var aName = clash.A.Category?.Name ?? string.Empty;
            var bName = clash.B.Category?.Name ?? string.Empty;
            var topic = new Shared.Logic.Bcf.BcfTopic($"Va chạm: {aName} × {bName}")
            {
                TopicType = "Clash",
                TopicStatus = "Open",
                Description =
                    $"Phần tử {RevitCompat.IdValue(clash.A.Id)} ({aName}) va chạm với {RevitCompat.IdValue(clash.B.Id)} ({bName})"
                    + (clash.LinkName == null ? string.Empty : $" trong model liên kết \"{clash.LinkName}\"")
                    + $". Toạ độ tâm va chạm: X={NumericText.Format(RevitCompat.FtToMm(clash.Centre.X), 0)} "
                    + $"Y={NumericText.Format(RevitCompat.FtToMm(clash.Centre.Y), 0)} "
                    + $"Z={NumericText.Format(RevitCompat.FtToMm(clash.Centre.Z), 0)} mm (toạ độ nội bộ mô hình).",
                Camera = RevitBcf.CameraAt(clash.Centre),
            };

            topic.Labels.Add(aName);
            if (!string.Equals(aName, bName, StringComparison.Ordinal))
            {
                topic.Labels.Add(bName);
            }

            foreach (var component in new[] { RevitBcf.ComponentOf(clash.A), RevitBcf.ComponentOf(clash.B) })
            {
                if (component != null)
                {
                    topic.Components.Add(component);
                }
            }

            topics.Add(topic);
        }

        RevitBcf.Write(config.BcfPath, topics, clashes.Count, result);
    }

    private static void WriteHtml(Document doc, ClashDetectionConfig config, List<Clash> clashes, int skipped)
    {
        var sb = new StringBuilder();
        sb.Append("<!DOCTYPE html><html lang=\"vi\"><head><meta charset=\"utf-8\"><title>DHCB - Va chạm</title>")
          .Append("<style>body{font-family:Segoe UI,Arial,sans-serif;margin:24px}table{border-collapse:collapse}td,th{border:1px solid #ccc;padding:4px 8px}th{background:#f3f3f3}code{font-size:11px}</style></head><body>")
          .Append("<h1>Va chạm nội bộ — ").Append(HtmlText.Escape(doc.Title)).Append("</h1>")
          .Append("<p>").Append(HtmlText.Escape(string.Join(", ", config.CategoriesA))).Append(" × ").Append(HtmlText.Escape(string.Join(", ", config.CategoriesB)))
          .Append(": <b>").Append(clashes.Count).Append("</b> va chạm; ").Append(skipped).Append(" đã chấp nhận (clash-accepted.json).</p>")
          .Append("<p>Để chấp nhận một va chạm: thêm <code>{\"key\":\"…\",\"note\":\"…\"}</code> vào file accepted với key ở cột cuối.</p>")
          .Append("<table><thead><tr><th>#</th><th>A</th><th>Category A</th><th>B</th><th>Category B</th><th>X</th><th>Y</th><th>Z (mm)</th><th>Key</th></tr></thead><tbody>");
        var i = 1;
        foreach (var c in clashes)
        {
            sb.Append("<tr><td>").Append(i++).Append("</td><td>").Append(RevitCompat.IdValue(c.A.Id)).Append("</td><td>").Append(HtmlText.Escape(c.A.Category?.Name))
              .Append("</td><td>").Append(RevitCompat.IdValue(c.B.Id)).Append("</td><td>").Append(HtmlText.Escape(c.B.Category?.Name))
              .Append("</td><td>").Append(RevitCompat.FtToMm(c.Centre.X).ToString("F0")).Append("</td><td>").Append(RevitCompat.FtToMm(c.Centre.Y).ToString("F0")).Append("</td><td>").Append(RevitCompat.FtToMm(c.Centre.Z).ToString("F0"))
              .Append("</td><td><code>").Append(HtmlText.Escape(c.Key)).Append("</code></td></tr>");
        }
        sb.Append("</tbody></table></body></html>");

        var dir = Path.GetDirectoryName(config.OutputPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(config.OutputPath, sb.ToString(), Encoding.UTF8);
    }
}
