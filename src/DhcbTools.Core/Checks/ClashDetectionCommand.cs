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

    public required string OutputPath { get; init; }

    /// <summary>File clash-accepted.json: cặp đã chấp nhận không báo lại.</summary>
    public string? AcceptedPath { get; init; }

    /// <summary>Dung sai lọc thô bounding box (mm).</summary>
    public double BoundingBoxToleranceMm { get; init; } = 0;

    public bool Create3dView { get; init; } = true;

    public string ViewName { get; init; } = "DHCB - Clashes";

    /// <summary>Giới hạn số va chạm báo (0 = không giới hạn).</summary>
    public int MaxResults { get; init; } = 2000;
}

/// <summary>Lọc thô <see cref="MepLayout.BoundingBoxesIntersect"/> → <see cref="ElementIntersectsElementFilter"/> chính xác.</summary>
public sealed class ClashDetectionCommand : ICoreCommand<ClashDetectionConfig>
{
    public string CommandName => "ClashDetection";

    private sealed record Clash(Element A, Element B, XYZ Centre, string Key);

    public CommandResult Execute(Document document, ClashDetectionConfig config)
    {
        var idsA = ParameterSync.ParameterExportCommand.ResolveCategoryIds(document, config.CategoriesA, out var unknownA);
        var idsB = ParameterSync.ParameterExportCommand.ResolveCategoryIds(document, config.CategoriesB, out var unknownB);
        if (idsA.Count == 0 || idsB.Count == 0)
        {
            return CommandResult.Fail("Một trong hai nhóm category không có trong mô hình: " + string.Join(", ", unknownA.Concat(unknownB)));
        }

        var result = CommandResult.Ok(string.Empty);
        var elementsA = new FilteredElementCollector(document).WhereElementIsNotElementType().WherePasses(new ElementMulticategoryFilter(idsA.ToList())).ToElements();
        var elementsB = new FilteredElementCollector(document).WhereElementIsNotElementType().WherePasses(new ElementMulticategoryFilter(idsB.ToList())).ToElements()
            .Select(e => (Element: e, Box: e.get_BoundingBox(null))).Where(t => t.Box != null).ToList();

        var accepted = ClashAcceptance.LoadKeys(config.AcceptedPath);
        var tol = RevitCompat.MmToFt(config.BoundingBoxToleranceMm);
        var clashes = new List<Clash>();
        var skippedAccepted = 0;
        var seen = new HashSet<string>();

        foreach (var a in elementsA)
        {
            var boxA = a.get_BoundingBox(null);
            if (boxA == null) continue;

            var candidates = elementsB.Where(t => t.Element.Id != a.Id && MepLayout.BoundingBoxesIntersect(
                boxA.Min.X, boxA.Min.Y, boxA.Min.Z, boxA.Max.X, boxA.Max.Y, boxA.Max.Z,
                t.Box!.Min.X, t.Box.Min.Y, t.Box.Min.Z, t.Box.Max.X, t.Box.Max.Y, t.Box.Max.Z, tol)).ToList();
            if (candidates.Count == 0) continue;

            ICollection<ElementId> hits;
            try
            {
                hits = new FilteredElementCollector(document, candidates.Select(c => c.Element.Id).ToList())
                    .WherePasses(new ElementIntersectsElementFilter(a))
                    .ToElementIds();
            }
            catch (Exception ex)
            {
                result.Messages.Add($"{a.Id}: không kiểm được solid ({ex.Message}) — dùng kết quả bounding box.");
                hits = candidates.Select(c => c.Element.Id).ToList();
            }

            foreach (var hitId in hits)
            {
                var b = candidates.First(c => c.Element.Id == hitId);
                var centre = new XYZ(
                    (Math.Max(boxA.Min.X, b.Box!.Min.X) + Math.Min(boxA.Max.X, b.Box.Max.X)) / 2,
                    (Math.Max(boxA.Min.Y, b.Box.Min.Y) + Math.Min(boxA.Max.Y, b.Box.Max.Y)) / 2,
                    (Math.Max(boxA.Min.Z, b.Box.Min.Z) + Math.Min(boxA.Max.Z, b.Box.Max.Z)) / 2);
                var key = ClashAcceptance.MakeKey(RevitCompat.IdValue(a.Id), RevitCompat.IdValue(hitId), RevitCompat.FtToMm(centre.X), RevitCompat.FtToMm(centre.Y), RevitCompat.FtToMm(centre.Z));
                if (!seen.Add(key)) continue;
                if (accepted.Contains(key))
                {
                    skippedAccepted++;
                    continue;
                }

                clashes.Add(new Clash(a, b.Element, centre, key));
                if (config.MaxResults > 0 && clashes.Count >= config.MaxResults)
                {
                    result.Messages.Add($"Đạt giới hạn {config.MaxResults} va chạm — dừng quét.");
                    goto Done;
                }
            }
        }

    Done:
        WriteHtml(document, config, clashes, skippedAccepted);

        if (config.Create3dView && clashes.Count > 0)
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
                    view.IsolateElementsTemporary(clashes.SelectMany(c => new[] { c.A.Id, c.B.Id }).Distinct().ToList());
                }
                tx.Commit();
            }
            catch (Exception ex)
            {
                result.Messages.Add("Không tạo được 3D view: " + ex.Message);
            }
        }

        foreach (var c in clashes.Take(500))
        {
            result.Messages.Add($"Va chạm {c.A.Id} ({c.A.Category?.Name}) × {c.B.Id} ({c.B.Category?.Name}) tại ({RevitCompat.FtToMm(c.Centre.X):F0},{RevitCompat.FtToMm(c.Centre.Y):F0},{RevitCompat.FtToMm(c.Centre.Z):F0}) mm  key={c.Key}");
        }

        result.Summary = $"Tìm thấy {clashes.Count} va chạm ({skippedAccepted} đã chấp nhận, bỏ qua) → \"{config.OutputPath}\".";
        result.AffectedCount = clashes.Count;
        return result;
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
