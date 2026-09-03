using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using DhcbTools.Shared.Logic;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;

namespace DhcbTools.Core.MEPF;

/// <summary>
/// Tìm giao cắt MEP × Tường/Sàn và đặt sleeve/opening family tại điểm giao.
/// Dùng hai lớp lọc: BoundingBoxIntersectsFilter (nhanh) → ElementIntersectsSolidFilter (chính xác).
/// </summary>
public sealed class SleeveCommand : ICoreCommand<SleeveConfig>
{
    public string CommandName => "SleeveAuto";

    private static readonly double ToleranceFt = RevitCompat.MmToFt(100.0); // 100 mm

    public CommandResult Execute(Document document, SleeveConfig config)
    {
        // 1. Find sleeve FamilySymbol
        var symbol = RevitCompat.FindFamilySymbol(document, config.SleeveFamilyName);
        if (symbol == null)
        {
            return CommandResult.Fail(
                $"Không tìm thấy FamilySymbol \"{config.SleeveFamilyName}\" trong mô hình.");
        }

        // 2. Collect MEP elements
        var mepElements = CollectMepElements(document, config.MepCategories);
        if (mepElements.Count == 0)
        {
            return CommandResult.Fail("Không có phần tử MEP nào để kiểm tra giao cắt.");
        }

        // 3. Pre-collect existing sleeves to avoid duplicates
        var existingSleeveLocations = CollectExistingSleeveLocations(document, config.SleeveFamilyName);

        // 4. Collect planned placements
        var placements = new List<(XYZ Point, Face? Face, Wall? HostWall, Floor? HostFloor, double WidthFt, double HeightFt, Element MepElement, string? LinkName)>();

        // Phần tử không tra được kích thước: trước đây âm thầm dùng 6 inch mặc định nên sleeve ra sai cỡ
        // mà không ai biết. Nay gom lại để báo trong CommandResult.
        var unknownSize = new List<long>();

        // Lỗi hiệu năng đã sửa: trước đây FilteredElementCollector toàn model (Walls+Floors) được dựng lại
        // BÊN TRONG vòng lặp cho từng phần tử MEP — O(n·m) trên model lớn, vượt timeout Bridge 30 s.
        // Thu thập một lần ở đây, lọc bbox trong bộ nhớ cho từng phần tử MEP.
        var hostCandidatesAll = CollectHostCandidates(document)
            .Select(e => new HostCandidate(e, null, null))
            .ToList();

        // Tường/sàn của dự án Việt Nam gần như luôn nằm ở MODEL LIÊN KẾT: file MEP link file kiến trúc.
        // Bản trước chỉ quét tường/sàn trong chính file đang mở, nên trên đúng cấu hình phổ biến nhất
        // lệnh trả "Đã đặt 0 sleeve" và trông như thành công — kiểu lỗi im lặng tệ nhất.
        var linkSummary = new List<string>();
        if (config.IncludeLinkedModels)
        {
            foreach (var linkInstance in new FilteredElementCollector(document)
                         .OfClass(typeof(RevitLinkInstance))
                         .Cast<RevitLinkInstance>())
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

                var transform = linkInstance.GetTotalTransform();
                var hosts = CollectHostCandidates(linkDoc);
                foreach (var host in hosts)
                {
                    hostCandidatesAll.Add(new HostCandidate(host, transform, linkInstance.Name));
                }
                linkSummary.Add($"{linkInstance.Name}: {hosts.Count} tường/sàn");
            }
        }

        foreach (var mepElem in mepElements)
        {
            if (!(mepElem.Location is LocationCurve locCurve))
                continue;

            var curve = locCurve.Curve;
            var bb = mepElem.get_BoundingBox(null);
            if (bb == null)
                continue;

            // Get element's solid for precise intersection
            var solid = GetFirstSolid(mepElem);

            // Find candidate host elements using bounding box first — lọc trong danh sách đã thu thập một lần
            // ở ngoài vòng lặp (hostCandidatesAll), không dựng FilteredElementCollector mới cho mỗi phần tử MEP.
            var outline = new Outline(bb.Min - new XYZ(0.1, 0.1, 0.1), bb.Max + new XYZ(0.1, 0.1, 0.1));
            var bbFilter = new BoundingBoxIntersectsFilter(outline);

            // Host trong link nằm ở toạ độ của link — phải đưa hộp bao về toạ độ file chủ rồi mới so.
            var candidates = hostCandidatesAll.Where(c => PassesBox(c, outline)).ToList();

            // Lọc tinh bằng solid CHỈ áp dụng cho host cùng file: ElementIntersectsSolidFilter so trong
            // một document, đưa element của link vào là sai kết quả. Host từ link giữ nguyên mức lọc hộp bao.
            List<HostCandidate> hosts;
            if (solid != null)
            {
                try
                {
                    var solidFilter = new ElementIntersectsSolidFilter(solid);
                    hosts = candidates
                        .Where(c => c.Transform != null || solidFilter.PassesFilter(c.Host))
                        .ToList();
                }
                catch (System.Exception)
                {
                    hosts = candidates;
                }
            }
            else
            {
                hosts = candidates;
            }

            // Get MEP size
            if (!GetMepSize(mepElem, config, out double widthFt, out double heightFt))
            {
                unknownSize.Add(RevitCompat.IdValue(mepElem.Id));
            }

            foreach (var candidate in hosts)
            {
                var host = candidate.Host;

                // Filter by host type name if configured
                if (config.HostTypeNames.Count > 0)
                {
                    var typeName = GetElementTypeName(host.Document, host);
                    bool matched = false;
                    foreach (var tn in config.HostTypeNames)
                    {
                        if (typeName.IndexOf(tn, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            matched = true;
                            break;
                        }
                    }
                    if (!matched) continue;
                }

                var intersectionPt = FindIntersectionPoint(curve, candidate);
                if (intersectionPt == null) continue;

                // Check duplicate
                if (IsNearExistingSleeve(intersectionPt, existingSleeveLocations))
                    continue;

                // Check already in placements list
                bool alreadyPlanned = false;
                foreach (var p in placements)
                {
                    if (p.Point.DistanceTo(intersectionPt) < ToleranceFt)
                    {
                        alreadyPlanned = true;
                        break;
                    }
                }
                if (alreadyPlanned) continue;

                placements.Add((intersectionPt, null!, host as Wall, host as Floor, widthFt, heightFt, mepElem, candidate.LinkName));
            }
        }

        if (config.DryRun)
        {
            var previewSummary = $"[Xem trước] Sẽ đặt {placements.Count} sleeve tại giao cắt MEP × Tường/Sàn.";
            if (placements.Count == 0)
            {
                previewSummary += " " + WhyNothing(hostCandidatesAll, mepElements.Count, config);
            }

            var preview = CommandResult.Ok(previewSummary, placements.Count);
            AddUnknownSizeWarning(preview, unknownSize, config);
            AddHostSourceNote(preview, hostCandidatesAll, linkSummary, placements.Count, mepElements.Count, config);
            foreach (var p in placements)
            {
                var hostDesc = p.HostWall != null ? $"Tường {p.HostWall.Id}" : $"Sàn {p.HostFloor?.Id}";
                if (p.LinkName != null) hostDesc += $" (link \"{p.LinkName}\")";
                preview.Messages.Add(
                    $"  → {hostDesc} tại ({RevitCompat.FtToMm(p.Point.X):F0}, {RevitCompat.FtToMm(p.Point.Y):F0}, {RevitCompat.FtToMm(p.Point.Z):F0}) mm" +
                    $"  W={RevitCompat.FtToMm(p.WidthFt):F0}mm H={RevitCompat.FtToMm(p.HeightFt):F0}mm");
            }
            return preview;
        }

        // 5. Execute placements in a single transaction
        int placed = 0;
        var placedIds = new List<long>();   // giai đoạn 10.2: agent zoom/kiểm được đúng sleeve vừa đặt
        using var tx = new Transaction(document, "DHCB - Sleeve tự động");
        tx.Start();
        RevitCompat.ApplyFailurePolicy(tx);

        if (!symbol.IsActive)
            symbol.Activate();

        var placedOnLink = 0;
        foreach (var (point, _, hostWall, hostFloor, widthFt, heightFt, _, linkName) in placements)
        {
            try
            {
                FamilyInstance? inst = null;

                if (linkName != null)
                {
                    // KHÔNG host được vào phần tử của link (Revit không cho tạo family instance bám mặt
                    // của model liên kết). Đặt tự do tại điểm giao — sleeve vẫn đúng chỗ, nhưng không tự
                    // dịch theo khi tường bên link đổi. Nói rõ trong Messages thay vì im lặng.
                    inst = document.Create.NewFamilyInstance(point, symbol,
                        Autodesk.Revit.DB.Structure.StructuralType.NonStructural);
                    placedOnLink++;
                }
                else if (hostWall != null)
                {
                    // Place on wall face
                    var face = GetNearestFace(hostWall, point);
                    if (face != null)
                    {
                        inst = document.Create.NewFamilyInstance(face, point, XYZ.BasisX, symbol);
                    }
                    else
                    {
                        inst = document.Create.NewFamilyInstance(point, symbol,
                            Autodesk.Revit.DB.Structure.StructuralType.NonStructural);
                    }
                }
                else if (hostFloor != null)
                {
                    var face = GetNearestFace(hostFloor, point);
                    if (face != null)
                    {
                        inst = document.Create.NewFamilyInstance(face, point, XYZ.BasisX, symbol);
                    }
                    else
                    {
                        inst = document.Create.NewFamilyInstance(point, symbol,
                            Autodesk.Revit.DB.Structure.StructuralType.NonStructural);
                    }
                }

                if (inst != null)
                {
                    SetParameterDouble(inst, config.WidthParamName, RevitCompat.FtToMm(widthFt));
                    SetParameterDouble(inst, config.HeightParamName, RevitCompat.FtToMm(heightFt));
                    placed++;
                    placedIds.Add(RevitCompat.IdValue(inst.Id));
                }
            }
            catch (System.Exception ex)
            {
                // Log and continue — don't abort the whole batch for one failure
                _ = ex;
            }
        }

        tx.Commit();
        var summary = $"Đã đặt {placed} sleeve tại giao cắt MEP × Tường/Sàn.";
        if (placed == 0)
        {
            // Con số 0 trơ trọi khiến người dùng tưởng model không có giao cắt. Nói ngay trong Summary
            // vì báo cáo batch chỉ in Summary — Messages không lọt tới mắt người đọc báo cáo.
            summary += " " + WhyNothing(hostCandidatesAll, mepElements.Count, config);
        }
        if (placedOnLink > 0)
        {
            summary += $" Trong đó {placedOnLink} cái bám tường/sàn của model liên kết nên đặt tự do (không host được vào link).";
        }

        var result = CommandResult.Ok(summary, placed).WithChanged(placedIds);
        AddUnknownSizeWarning(result, unknownSize, config);
        AddHostSourceNote(result, hostCandidatesAll, linkSummary, placed, mepElements.Count, config);
        return result;
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Một câu ngắn giải thích vì sao không đặt được cái nào — ghép thẳng vào Summary. Báo cáo batch chỉ
    /// in Summary, nên lời giải thích nằm trong Messages là lời giải thích không ai đọc.
    /// </summary>
    private static string WhyNothing(List<HostCandidate> hosts, int mepCount, SleeveConfig config)
    {
        var inDocument = hosts.Count(h => h.Transform == null);
        var inLinks = hosts.Count - inDocument;

        if (hosts.Count == 0)
        {
            return config.IncludeLinkedModels
                ? "Không có tường/sàn nào để xét, kể cả trong model liên kết — kiểm lại link đã nạp chưa."
                : "Không có tường/sàn nào trong file này và includeLinkedModels đang tắt — bật lên nếu tường nằm ở model liên kết.";
        }

        return $"Đã xét {inDocument} tường/sàn trong file + {inLinks} từ model liên kết trên {mepCount} phần tử MEP " +
               "nhưng không có giao cắt nào (thường do lệch cao độ hoặc hostTypeNames lọc quá chặt).";
    }

    /// <summary>Một ứng viên host: tường/sàn trong chính file, hoặc trong một model liên kết.</summary>
    private sealed class HostCandidate
    {
        public HostCandidate(Element host, Transform? transform, string? linkName)
        {
            Host = host;
            Transform = transform;
            LinkName = linkName;

            // Hộp bao tính MỘT LẦN lúc dựng, ở toạ độ file chủ. Bản trước gọi get_BoundingBox cho từng
            // ứng viên trong vòng lặp của từng phần tử MEP — 1.053 MEP × hàng nghìn tường/sàn của link
            // là hàng triệu lần gọi API, đo được 49,8 s trên model mẫu (ngưỡng Bridge là 30 s).
            var bb = host.get_BoundingBox(null);
            if (bb == null)
            {
                HasBox = false;
                return;
            }

            HasBox = true;
            if (transform == null)
            {
                MinX = bb.Min.X; MinY = bb.Min.Y; MinZ = bb.Min.Z;
                MaxX = bb.Max.X; MaxY = bb.Max.Y; MaxZ = bb.Max.Z;
                return;
            }

            // Link xoay thì hộp bao phải dựng lại từ tám đỉnh, không chỉ hai điểm min/max.
            double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;
            for (var i = 0; i < 8; i++)
            {
                var corner = transform.OfPoint(new XYZ(
                    (i & 1) == 0 ? bb.Min.X : bb.Max.X,
                    (i & 2) == 0 ? bb.Min.Y : bb.Max.Y,
                    (i & 4) == 0 ? bb.Min.Z : bb.Max.Z));
                minX = Math.Min(minX, corner.X); maxX = Math.Max(maxX, corner.X);
                minY = Math.Min(minY, corner.Y); maxY = Math.Max(maxY, corner.Y);
                minZ = Math.Min(minZ, corner.Z); maxZ = Math.Max(maxZ, corner.Z);
            }
            MinX = minX; MinY = minY; MinZ = minZ;
            MaxX = maxX; MaxY = maxY; MaxZ = maxZ;
        }

        public bool HasBox { get; }

        public double MinX { get; }

        public double MinY { get; }

        public double MinZ { get; }

        public double MaxX { get; }

        public double MaxY { get; }

        public double MaxZ { get; }

        public Element Host { get; }

        /// <summary>Phép biến đổi từ toạ độ link sang toạ độ file chủ. <c>null</c> = host cùng file.</summary>
        public Transform? Transform { get; }

        public string? LinkName { get; }
    }

    private static List<Element> CollectHostCandidates(Document doc) =>
        new FilteredElementCollector(doc)
            .WhereElementIsNotElementType()
            .WherePasses(new LogicalOrFilter(
                new ElementCategoryFilter(BuiltInCategory.OST_Walls),
                new ElementCategoryFilter(BuiltInCategory.OST_Floors)))
            .ToList();

    /// <summary>
    /// Hộp bao của host (đã đưa về toạ độ file chủ nếu nằm trong link) có giao với vùng quan tâm không.
    /// Link xoay thì hộp bao phải dựng lại từ tám đỉnh, không chỉ hai điểm min/max.
    /// </summary>
    private static bool PassesBox(HostCandidate candidate, Outline outline) =>
        candidate.HasBox && MepLayout.BoundingBoxesIntersect(
            candidate.MinX, candidate.MinY, candidate.MinZ,
            candidate.MaxX, candidate.MaxY, candidate.MaxZ,
            outline.MinimumPoint.X, outline.MinimumPoint.Y, outline.MinimumPoint.Z,
            outline.MaximumPoint.X, outline.MaximumPoint.Y, outline.MaximumPoint.Z);

    /// <summary>
    /// Nói rõ host lấy từ đâu, và khi không đặt được cái nào thì VÌ SAO. "Đã đặt 0 sleeve" trơ trọi là
    /// thứ khiến người dùng tưởng model không có giao cắt, trong khi thật ra tường nằm ở link chưa nạp
    /// hoặc bị bộ lọc loại hết.
    /// </summary>
    private static void AddHostSourceNote(
        CommandResult result, List<HostCandidate> hosts, List<string> linkSummary,
        int placedCount, int mepCount, SleeveConfig config)
    {
        var inDocument = hosts.Count(h => h.Transform == null);
        var inLinks = hosts.Count - inDocument;
        result.Messages.Add($"Tường/sàn xét tới: {inDocument} trong file, {inLinks} từ model liên kết.");
        foreach (var line in linkSummary)
        {
            result.Messages.Add("  Link — " + line);
        }

        if (placedCount > 0)
        {
            return;
        }

        if (hosts.Count == 0)
        {
            result.Messages.Add(!config.IncludeLinkedModels
                ? "Không có tường/sàn nào để xét. File này không có tường/sàn, và includeLinkedModels đang tắt — bật lên nếu tường nằm ở model liên kết."
                : "Không có tường/sàn nào để xét, kể cả trong model liên kết. Kiểm lại link đã nạp chưa (Manage → Manage Links).");
        }
        else
        {
            result.Messages.Add($"Có {hosts.Count} tường/sàn và {mepCount} phần tử MEP nhưng không tìm ra giao cắt nào. " +
                                "Thường là do MEP và kết cấu lệch cao độ, hoặc hostTypeNames lọc quá chặt.");
        }
    }

    private static List<Element> CollectMepElements(Document doc, List<string> categoryFilter)
    {
        var allCategories = new[]
        {
            BuiltInCategory.OST_DuctCurves,
            BuiltInCategory.OST_PipeCurves,
            BuiltInCategory.OST_CableTray,
            BuiltInCategory.OST_Conduit,
        };

        var result = new List<Element>();
        foreach (var bic in allCategories)
        {
            if (categoryFilter.Count > 0)
            {
                var catName = bic.ToString()
                    .Replace("OST_", string.Empty)
                    .Replace("Curves", string.Empty)
                    .Replace("Curve", string.Empty);
                bool include = false;
                foreach (var f in categoryFilter)
                {
                    if (catName.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0 ||
                        f.IndexOf(catName, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        include = true;
                        break;
                    }
                }
                if (!include) continue;
            }

            var elems = new FilteredElementCollector(doc)
                .OfCategory(bic)
                .WhereElementIsNotElementType()
                .ToElements();
            result.AddRange(elems);
        }
        return result;
    }

    private static List<XYZ> CollectExistingSleeveLocations(Document doc, string familyName)
    {
        var locs = new List<XYZ>();
        var instances = new FilteredElementCollector(doc)
            .OfClass(typeof(FamilyInstance))
            .Cast<FamilyInstance>()
            .Where(fi => fi.Symbol != null &&
                         (fi.Symbol.Name.IndexOf(familyName, StringComparison.OrdinalIgnoreCase) >= 0 ||
                          fi.Symbol.FamilyName.IndexOf(familyName, StringComparison.OrdinalIgnoreCase) >= 0));
        foreach (var fi in instances)
        {
            if (fi.Location is LocationPoint lp)
                locs.Add(lp.Point);
        }
        return locs;
    }

    private static bool IsNearExistingSleeve(XYZ point, List<XYZ> existing)
    {
        foreach (var ex in existing)
        {
            if (point.DistanceTo(ex) < ToleranceFt)
                return true;
        }
        return false;
    }

    private static Solid? GetFirstSolid(Element elem)
    {
        try
        {
            var opts = new Options { ComputeReferences = false, DetailLevel = ViewDetailLevel.Coarse };
            var geom = elem.get_Geometry(opts);
            if (geom == null) return null!;
            foreach (GeometryObject obj in geom)
            {
                if (obj is Solid s && s.Volume > 1e-9) return s;
                if (obj is GeometryInstance gi)
                {
                    foreach (GeometryObject o2 in gi.GetInstanceGeometry())
                    {
                        if (o2 is Solid s2 && s2.Volume > 1e-9) return s2;
                    }
                }
            }
        }
        catch (System.Exception) { }
        return null;
    }

    private static XYZ FindIntersectionPoint(Curve mepCurve, HostCandidate candidate)
    {
        // Use midpoint strategy: project midpoint of MEP curve onto host bounding box centre Z
        var mid = mepCurve.Evaluate(0.5, true);
        var host = candidate.Host;
        if (!candidate.HasBox) return mid;

        // For walls: keep XY of intersection with wall, Z from MEP
        // Simple: return MEP midpoint adjusted to host centreplane
        var hostCentreZ = (candidate.MinZ + candidate.MaxZ) * 0.5;
        if (host is Wall)
        {
            // Return point at MEP curve location with host's Z-centre if curve is horizontal
            return new XYZ(mid.X, mid.Y, mid.Z);
        }
        else // Floor
        {
            return new XYZ(mid.X, mid.Y, hostCentreZ);
        }
    }

    /// <summary>
    /// Cảnh báo các phần tử không tra được kích thước — kèm tên tham số đã thử và chỗ khai báo thêm,
    /// để kỹ sư sửa được chứ không chỉ biết là "có gì đó sai".
    /// </summary>
    private static void AddUnknownSizeWarning(CommandResult result, List<long> unknownSize, SleeveConfig config)
    {
        if (unknownSize.Count == 0)
        {
            return;
        }

        result.Messages.Add(
            $"{unknownSize.Count} phần tử MEP không tra được kích thước, dùng tạm 152 mm — sleeve có thể sai cỡ. "
            + RevitCompat.LookupFailed("diameter")
            + " Phần tử: " + string.Join(", ", unknownSize.Take(20))
            + (unknownSize.Count > 20 ? ", …" : string.Empty));
    }

    /// <summary>
    /// Kích thước phần tử MEP để tính lỗ mở. Tra qua từ điển tên tham số (giai đoạn 9.2) thay vì
    /// tên tiếng Anh cứng — trên Revit tiếng Việt thì "Outer Diameter"/"Width" không tồn tại.
    /// </summary>
    /// <returns>false khi không tra được kích thước nào: người gọi phải báo, không được im lặng
    /// dùng giá trị mặc định rồi đặt sleeve sai cỡ.</returns>
    private static bool GetMepSize(Element elem, SleeveConfig config,
        out double widthFt, out double heightFt)
    {
        double clearFt = RevitCompat.MmToFt(config.ClearanceMm) * 2; // both sides
        widthFt = 0.5; // 6 inch — chỉ dùng khi đã báo cho người dùng biết là không tra được
        heightFt = 0.5;

        var outerDiam = RevitCompat.Lookup(elem, "diameter");
        if (outerDiam != null && outerDiam.StorageType == StorageType.Double && outerDiam.AsDouble() > 0)
        {
            widthFt = outerDiam.AsDouble() + clearFt;
            heightFt = widthFt;
            return true;
        }

        var widthParam = RevitCompat.Lookup(elem, "width", config.WidthParamName);
        var heightParam = RevitCompat.Lookup(elem, "height", config.HeightParamName);
        var found = false;

        if (widthParam != null && widthParam.StorageType == StorageType.Double && widthParam.AsDouble() > 0)
        {
            widthFt = widthParam.AsDouble() + clearFt;
            found = true;
        }

        if (heightParam != null && heightParam.StorageType == StorageType.Double && heightParam.AsDouble() > 0)
        {
            heightFt = heightParam.AsDouble() + clearFt;
            found = true;
        }

        return found;
    }

    private static Face? GetNearestFace(Element host, XYZ point)
    {
        try
        {
            var opts = new Options { ComputeReferences = true, DetailLevel = ViewDetailLevel.Fine };
            var geom = host.get_Geometry(opts);
            if (geom == null) return null!;

            Face? nearest = null;
            double minDist = double.MaxValue;

            foreach (GeometryObject obj in geom)
            {
                Solid? solid = obj as Solid;
                if (solid == null && obj is GeometryInstance gi)
                {
                    foreach (GeometryObject o2 in gi.GetInstanceGeometry())
                    {
                        if (o2 is Solid s2) { solid = s2!; break; }
                    }
                }
                if (solid == null || solid.Faces.Size == 0) continue;

                foreach (Face face in solid.Faces)
                {
                    var uv = face.Project(point);
                    if (uv == null) continue;
                    var dist = uv.Distance;
                    if (dist < minDist)
                    {
                        minDist = dist;
                        nearest = face;
                    }
                }
            }
            return nearest!;
        }
        catch (System.Exception)
        {
            return null!;
        }
    }

    private static string GetElementTypeName(Document doc, Element elem)
    {
        var typeId = elem.GetTypeId();
        if (typeId == null || typeId == ElementId.InvalidElementId) return string.Empty;
        var type = doc.GetElement(typeId);
        return type?.Name ?? string.Empty;
    }

    private static void SetParameterDouble(FamilyInstance inst, string paramName, double valueMm)
    {
        var param = inst.LookupParameter(paramName);
        if (param == null || param.IsReadOnly) return;
        if (param.StorageType == StorageType.Double)
            param.Set(RevitCompat.MmToFt(valueMm));
        else if (param.StorageType == StorageType.String)
            param.Set(NumericText.Format(valueMm, 1)); // Invariant, không phụ thuộc culture máy
    }
}
