using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using DhcbTools.Shared.Logic;
using DhcbTools.Shared.Logic.Mep;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;

namespace DhcbTools.Core.MEPF;

/// <summary>
/// Tìm giao cắt MEP × Tường/Sàn và đặt sleeve/opening family tại điểm giao.
/// Dùng hai lớp lọc: BoundingBoxIntersectsFilter (nhanh) → ElementIntersectsSolidFilter (chính xác).
/// <para>
/// Phần quyết định (cắt tuyến với hộp, chọn cỡ, lọc, gom lý do, viết Summary) nằm ở
/// <see cref="SleevePlanner"/> trong Shared.Logic để có test trên CI; file này chỉ còn phần
/// dịch qua lại với API Revit.
/// </para>
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
            // Lỗi phải nói mô hình CÓ family gì — vai MEP §57 phải chạy FamilyAudit riêng để tra tên (§58).
            return CommandResult.Fail(FamilyCandidates.NotFoundMessage(config.SleeveFamilyName,
                RevitCompat.FamilySymbolCandidates(document), new[] { "Generic Models", "Pipe Accessories", "Duct Accessories" }));
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

        // Đếm vị trí bỏ qua vì đã có sleeve. Thiếu con số này thì lần chạy thứ hai báo "0 sleeve" kèm lý
        // do SAI ("không có giao cắt nào"), trong khi thật ra giao cắt vẫn còn nguyên và đã có sleeve rồi.
        var skippedExisting = 0;

        // Số giao cắt không tính được bằng solid lẫn hộp bao — phải rơi về trung điểm tuyến MEP (kém chính
        // xác). Báo trong Messages thay vì im lặng.
        var midpointFallback = 0;

        // Lỗi hiệu năng đã sửa: trước đây FilteredElementCollector toàn model (Walls+Floors) được dựng lại
        // BÊN TRONG vòng lặp cho từng phần tử MEP — O(n·m) trên model lớn, vượt timeout Bridge 30 s.
        // Thu thập một lần ở đây, lọc bbox trong bộ nhớ cho từng phần tử MEP.
        var hostCandidatesAll = CollectHostCandidates(document)
            .Select(e => new HostCandidate(e, null, null))
            .ToList();

        // Tường/sàn của dự án Việt Nam gần như luôn nằm ở MODEL LIÊN KẾT: file MEP link file kiến trúc.
        // Bản trước chỉ quét tường/sàn trong chính file đang mở, nên trên đúng cấu hình phổ biến nhất
        // lệnh trả "Đã đặt 0 sleeve" và trông như thành công — kiểu lỗi im lặng tệ nhất.
        // Link chưa nạp mà vẫn chạy tiếp thì con số trả ra nói về trạng thái link, không nói về mô hình
        // — đúng lớp lỗi của bug #14. Dừng ở đây, trước mọi transaction.
        var linkPre = Checks.RevitPrecondition.LinkedModels(document, CommandName);
        if (config.IncludeLinkedModels && linkPre.Blocks)
        {
            return CommandResult.Fail(linkPre.Message);
        }

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

                if (!SleevePlanner.LinkNameMatches(linkInstance.Name, config.LinkNameContains))
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

        var hostsInDocument = hostCandidatesAll.Count(h => h.Transform == null);
        var hostsInLinks = hostCandidatesAll.Count - hostsInDocument;

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
            var size = GetMepSize(mepElem, config);
            if (!size.Resolved)
            {
                unknownSize.Add(RevitCompat.IdValue(mepElem.Id));
            }

            foreach (var candidate in hosts)
            {
                var host = candidate.Host;

                // Filter by host type name if configured
                if (!SleevePlanner.HostTypeMatches(GetElementTypeName(host.Document, host), config.HostTypeNames))
                {
                    continue;
                }

                var intersectionPt = FindIntersectionPoint(curve, candidate, out var usedMidpoint);
                if (intersectionPt == null) continue;
                if (usedMidpoint) midpointFallback++;

                // Check duplicate
                if (IsNearExistingSleeve(intersectionPt, existingSleeveLocations))
                {
                    skippedExisting++;
                    continue;
                }

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

                placements.Add((intersectionPt, null!, host as Wall, host as Floor, size.WidthFt, size.HeightFt, mepElem, candidate.LinkName));
            }
        }

        var whyNothing = SleevePlanner.WhyNothing(hostsInDocument, hostsInLinks, mepElements.Count, skippedExisting, config.IncludeLinkedModels);

        if (config.DryRun)
        {
            var preview = CommandResult.Ok(SleevePlanner.PreviewSummary(placements.Count, whyNothing), placements.Count);
            AddNotes(preview, unknownSize, midpointFallback, hostsInDocument, hostsInLinks, linkSummary, placements.Count, mepElements.Count, config);
            foreach (var p in placements)
            {
                var hostId = p.HostWall != null ? p.HostWall.Id : p.HostFloor?.Id;
                preview.Messages.Add(SleevePlanner.PreviewLine(
                    p.HostWall != null, hostId == null ? 0 : RevitCompat.IdValue(hostId), p.LinkName,
                    RevitCompat.FtToMm(p.Point.X), RevitCompat.FtToMm(p.Point.Y), RevitCompat.FtToMm(p.Point.Z),
                    RevitCompat.FtToMm(p.WidthFt), RevitCompat.FtToMm(p.HeightFt)));
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
        var failedPlacements = 0;
        var failureReasons = new List<string>();
        foreach (var (point, _, hostWall, hostFloor, widthFt, heightFt, mepElement, linkName) in placements)
        {
            try
            {
                FamilyInstance? inst = null;
                var mepDir = MepDirection(mepElement);

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
                    // Tường: ưu tiên mặt có pháp tuyến song song hướng tuyến MEP (mặt bên), tránh mặt đỉnh tường.
                    var face = GetNearestFace(hostWall, point, mepDir);
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
                    // Sàn: ưu tiên mặt có pháp tuyến thẳng đứng (mặt trên/dưới), tránh mặt cạnh sàn.
                    var face = GetNearestFace(hostFloor, point, XYZ.BasisZ);
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
                    SetParameterDouble(inst, "width", config.WidthParamName, RevitCompat.FtToMm(widthFt));
                    SetParameterDouble(inst, "height", config.HeightParamName, RevitCompat.FtToMm(heightFt));
                    placed++;
                    placedIds.Add(RevitCompat.IdValue(inst.Id));
                }
                else
                {
                    failedPlacements++;
                    SleevePlanner.AddDistinctReason(failureReasons, "NewFamilyInstance trả về null");
                }
            }
            catch (System.Exception ex)
            {
                // Không huỷ cả lô vì một cái lỗi — nhưng PHẢI ghi lý do, trước đây nuốt im lặng nên
                // "0 sleeve" bị đổ oan cho "không có giao cắt".
                failedPlacements++;
                SleevePlanner.AddDistinctReason(failureReasons, ex.Message);
            }
        }

        tx.Commit();

        var result = CommandResult.Ok(
            SleevePlanner.WriteSummary(placed, failedPlacements, placements.Count, placedOnLink, skippedExisting, whyNothing),
            placed).WithChanged(placedIds);
        var failureLine = SleevePlanner.FailureReasonsLine(failedPlacements, failureReasons);
        if (failureLine != null)
        {
            result.Messages.Add(failureLine);
        }
        AddNotes(result, unknownSize, midpointFallback, hostsInDocument, hostsInLinks, linkSummary, placed, mepElements.Count, config);
        return result;
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    /// <summary>Ba nhóm ghi chú dùng chung cho xem trước và ghi thật, theo đúng thứ tự cũ.</summary>
    private static void AddNotes(
        CommandResult result, List<long> unknownSize, int midpointFallback,
        int hostsInDocument, int hostsInLinks, List<string> linkSummary,
        int placedCount, int mepCount, SleeveConfig config)
    {
        var unknown = SleevePlanner.UnknownSizeWarning(unknownSize, RevitCompat.LookupFailed("diameter"));
        if (unknown != null)
        {
            result.Messages.Add(unknown);
        }

        var midpoint = SleevePlanner.MidpointFallbackNote(midpointFallback);
        if (midpoint != null)
        {
            result.Messages.Add(midpoint);
        }

        result.Messages.AddRange(SleevePlanner.HostSourceNotes(
            hostsInDocument, hostsInLinks, linkSummary, placedCount, mepCount, config.IncludeLinkedModels));
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
                Box = null;
                return;
            }

            var local = new Box3(bb.Min.X, bb.Min.Y, bb.Min.Z, bb.Max.X, bb.Max.Y, bb.Max.Z);
            Box = transform == null
                ? local
                : SleevePlanner.TransformedBox(local, (x, y, z) =>
                {
                    var p = transform.OfPoint(new XYZ(x, y, z));
                    return (p.X, p.Y, p.Z);
                });
        }

        /// <summary>Hộp bao ở toạ độ file chủ; null khi host không có hộp bao.</summary>
        public Box3? Box { get; }

        public bool HasBox => Box != null;

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
    /// </summary>
    private static bool PassesBox(HostCandidate candidate, Outline outline) =>
        candidate.Box != null && MepLayout.BoundingBoxesIntersect(
            candidate.Box.MinX, candidate.Box.MinY, candidate.Box.MinZ,
            candidate.Box.MaxX, candidate.Box.MaxY, candidate.Box.MaxZ,
            outline.MinimumPoint.X, outline.MinimumPoint.Y, outline.MinimumPoint.Z,
            outline.MaximumPoint.X, outline.MaximumPoint.Y, outline.MaximumPoint.Z);

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
            if (!SleevePlanner.CategoryIncluded(SleevePlanner.ShortCategoryName(bic.ToString()), categoryFilter))
            {
                continue;
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

    /// <summary>Hướng tuyến MEP (đơn vị), null nếu không có LocationCurve.</summary>
    private static XYZ? MepDirection(Element mepElement)
    {
        if (!(mepElement.Location is LocationCurve lc)) return null;
        var d = lc.Curve.GetEndPoint(1) - lc.Curve.GetEndPoint(0);
        return d.GetLength() < 1e-9 ? null : d.Normalize();
    }

    /// <summary>
    /// Điểm giao thật giữa tuyến MEP và host (toạ độ file chủ). Thứ tự: (1) solid của host ×
    /// tuyến — <see cref="Solid.IntersectWithCurve"/>, lấy trung điểm đoạn nằm trong host;
    /// (2) cắt tuyến với hộp bao host (Liang–Barsky, <see cref="SleevePlanner.ClipLineToBox"/>) khi host
    /// không có solid; (3) bất đắc dĩ mới dùng trung điểm tuyến MEP và báo qua <paramref name="usedMidpoint"/>.
    /// Bản trước luôn trả trung điểm tuyến, nên ống dài xuyên nhiều tường thì mọi sleeve dồn về một chỗ.
    /// </summary>
    private static XYZ? FindIntersectionPoint(Curve mepCurve, HostCandidate candidate, out bool usedMidpoint)
    {
        usedMidpoint = false;

        // Host trong link: đưa tuyến về toạ độ của link trước khi giao với solid của host.
        var transform = candidate.Transform;
        Curve localCurve = mepCurve;
        if (transform != null)
        {
            try { localCurve = mepCurve.CreateTransformed(transform.Inverse); }
            catch (System.Exception) { localCurve = mepCurve; }
        }

        // 1. Solid × curve
        var hostSolid = GetFirstSolid(candidate.Host);
        if (hostSolid != null)
        {
            try
            {
                var options = new SolidCurveIntersectionOptions { ResultType = SolidCurveIntersectionMode.CurveSegmentsInside };
                var intersection = hostSolid.IntersectWithCurve(localCurve, options);
                if (intersection != null && intersection.SegmentCount > 0)
                {
                    Curve? longest = null;
                    for (var i = 0; i < intersection.SegmentCount; i++)
                    {
                        var seg = intersection.GetCurveSegment(i);
                        if (seg != null && (longest == null || seg.Length > longest.Length)) longest = seg;
                    }

                    if (longest != null)
                    {
                        var localMid = longest.Evaluate(0.5, true);
                        return transform != null ? transform.OfPoint(localMid) : localMid;
                    }
                }
            }
            catch (System.Exception)
            {
                // Rơi xuống bước hộp bao.
            }
        }

        // 2. Cắt tuyến với hộp bao host — hộp bao của HostCandidate đã ở toạ độ file chủ, nên dùng
        // tuyến gốc (file chủ). Chỉ áp dụng cho tuyến thẳng.
        if (candidate.Box != null && mepCurve is Line)
        {
            var p0 = mepCurve.GetEndPoint(0);
            var p1 = mepCurve.GetEndPoint(1);
            if (SleevePlanner.ClipLineToBox(p0.X, p0.Y, p0.Z, p1.X, p1.Y, p1.Z, candidate.Box, out var t0, out var t1))
            {
                var tm = (t0 + t1) * 0.5;
                return p0 + (p1 - p0) * tm;
            }
        }

        // 3. Bất đắc dĩ: trung điểm tuyến.
        usedMidpoint = true;
        var mid = mepCurve.Evaluate(0.5, true);
        if (candidate.Box != null && candidate.Host is Floor)
        {
            return new XYZ(mid.X, mid.Y, (candidate.Box.MinZ + candidate.Box.MaxZ) * 0.5);
        }

        return mid;
    }

    /// <summary>Giá trị double dương của tham số, hoặc null khi không có/không phải double/≤ 0.</summary>
    private static double? PositiveDouble(Parameter? param) =>
        param != null && param.StorageType == StorageType.Double && param.AsDouble() > 0
            ? param.AsDouble()
            : (double?)null;

    /// <summary>
    /// Kích thước phần tử MEP để tính lỗ mở. Tra qua từ điển tên tham số (giai đoạn 9.2) thay vì
    /// tên tiếng Anh cứng — trên Revit tiếng Việt thì "Outer Diameter"/"Width" không tồn tại.
    /// Quyết định cỡ nằm ở <see cref="SleevePlanner.SizeFrom"/>; ở đây chỉ đọc ba tham số.
    /// </summary>
    private static SleevePlanner.SleeveSize GetMepSize(Element elem, SleeveConfig config) =>
        SleevePlanner.SizeFrom(
            PositiveDouble(RevitCompat.Lookup(elem, "diameter")),
            PositiveDouble(RevitCompat.Lookup(elem, "width", config.WidthParamName)),
            PositiveDouble(RevitCompat.Lookup(elem, "height", config.HeightParamName)),
            RevitCompat.MmToFt(config.ClearanceMm));

    /// <summary>
    /// Mặt host gần điểm nhất, ƯU TIÊN mặt phẳng có pháp tuyến gần song song <paramref name="preferredNormal"/>
    /// (tường: hướng tuyến MEP → mặt bên; sàn: thẳng đứng → mặt trên/dưới). Không ưu tiên thì điểm giao
    /// nằm giữa bề dày tường thường gần mặt đỉnh tường hơn, sleeve bị đặt lên nóc tường.
    /// </summary>
    private static Face? GetNearestFace(Element host, XYZ point, XYZ? preferredNormal)
    {
        try
        {
            var opts = new Options { ComputeReferences = true, DetailLevel = ViewDetailLevel.Fine };
            var geom = host.get_Geometry(opts);
            if (geom == null) return null!;

            Face? nearest = null;
            double minDist = double.MaxValue;
            Face? nearestPreferred = null;
            double minDistPreferred = double.MaxValue;
            const double parallelDot = 0.7;

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

                    if (preferredNormal != null && face is PlanarFace pf
                        && Math.Abs(pf.FaceNormal.DotProduct(preferredNormal)) >= parallelDot
                        && dist < minDistPreferred)
                    {
                        minDistPreferred = dist;
                        nearestPreferred = face;
                    }
                }
            }
            return (nearestPreferred ?? nearest)!;
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

    /// <summary>
    /// Ghi kích thước lên sleeve: tra qua từ điển tên tham số (tên trong config đứng đầu, rồi tên đồng
    /// nghĩa) thay vì LookupParameter với một chuỗi cứng. Chỉ ghi tham số INSTANCE — Lookup có thể trả
    /// tham số ở type, ghi vào đó là đổi cả loạt sleeve khác.
    /// </summary>
    private static void SetParameterDouble(FamilyInstance inst, string key, string preferredName, double valueMm)
    {
        var param = RevitCompat.Lookup(inst, key, preferredName);
        if (param == null || param.IsReadOnly) return;
        if (param.Element == null || param.Element.Id != inst.Id) return;
        if (param.StorageType == StorageType.Double)
            param.Set(RevitCompat.MmToFt(valueMm));
        else if (param.StorageType == StorageType.String)
            param.Set(NumericText.Format(valueMm, 1)); // Invariant, không phụ thuộc culture máy
    }
}
