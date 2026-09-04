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

                placements.Add((intersectionPt, null!, host as Wall, host as Floor, widthFt, heightFt, mepElem, candidate.LinkName));
            }
        }

        if (config.DryRun)
        {
            var previewSummary = $"[Xem trước] Sẽ đặt {placements.Count} sleeve tại giao cắt MEP × Tường/Sàn.";
            if (placements.Count == 0)
            {
                previewSummary += " " + WhyNothing(hostCandidatesAll, mepElements.Count, skippedExisting, config);
            }

            var preview = CommandResult.Ok(previewSummary, placements.Count);
            AddUnknownSizeWarning(preview, unknownSize, config);
            AddMidpointFallbackNote(preview, midpointFallback);
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
                    AddDistinct(failureReasons, "NewFamilyInstance trả về null");
                }
            }
            catch (System.Exception ex)
            {
                // Không huỷ cả lô vì một cái lỗi — nhưng PHẢI ghi lý do, trước đây nuốt im lặng nên
                // "0 sleeve" bị đổ oan cho "không có giao cắt".
                failedPlacements++;
                AddDistinct(failureReasons, ex.Message);
            }
        }

        tx.Commit();
        var summary = $"Đã đặt {placed} sleeve tại giao cắt MEP × Tường/Sàn.";
        if (failedPlacements > 0)
        {
            summary += $" {failedPlacements}/{placements.Count} vị trí đặt lỗi (xem chi tiết trong thông báo).";
        }

        if (placed == 0 && failedPlacements == 0)
        {
            // Con số 0 trơ trọi khiến người dùng tưởng model không có giao cắt. Nói ngay trong Summary
            // vì báo cáo batch chỉ in Summary — Messages không lọt tới mắt người đọc báo cáo.
            summary += " " + WhyNothing(hostCandidatesAll, mepElements.Count, skippedExisting, config);
        }
        if (placedOnLink > 0)
        {
            summary += $" Trong đó {placedOnLink} cái bám tường/sàn của model liên kết nên đặt tự do (không host được vào link).";
        }

        if (skippedExisting > 0 && placed > 0)
        {
            summary += $" Bỏ qua, đã có sleeve: {skippedExisting} vị trí.";
        }

        var result = CommandResult.Ok(summary, placed).WithChanged(placedIds);
        if (failedPlacements > 0)
        {
            result.Messages.Add($"{failedPlacements} vị trí không đặt được sleeve. Lý do (tối đa {MaxFailureReasons} loại): "
                                + string.Join(" | ", failureReasons));
        }
        AddUnknownSizeWarning(result, unknownSize, config);
        AddMidpointFallbackNote(result, midpointFallback);
        AddHostSourceNote(result, hostCandidatesAll, linkSummary, placed, mepElements.Count, config);
        return result;
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Một câu ngắn giải thích vì sao không đặt được cái nào — ghép thẳng vào Summary. Báo cáo batch chỉ
    /// in Summary, nên lời giải thích nằm trong Messages là lời giải thích không ai đọc.
    /// </summary>
    private static string WhyNothing(List<HostCandidate> hosts, int mepCount, int skippedExisting, SleeveConfig config)
    {
        var inDocument = hosts.Count(h => h.Transform == null);
        var inLinks = hosts.Count - inDocument;

        // Chạy lại lần hai là trường hợp phổ biến nhất của "0 sleeve" — và nó KHÔNG phải "không có giao
        // cắt". Nói đúng chuyện đang xảy ra, vì đây cũng là bằng chứng lần trước đã ghi thật.
        if (skippedExisting > 0)
        {
            return $"Bỏ qua, đã có sleeve: {skippedExisting} vị trí.";
        }

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

    private const int MaxFailureReasons = 5;

    private static void AddDistinct(List<string> reasons, string message)
    {
        if (reasons.Count < MaxFailureReasons && !reasons.Contains(message))
        {
            reasons.Add(message);
        }
    }

    private static void AddMidpointFallbackNote(CommandResult result, int midpointFallback)
    {
        if (midpointFallback > 0)
        {
            result.Messages.Add($"{midpointFallback} giao cắt không tính được bằng solid lẫn hộp bao của host — "
                                + "dùng tạm trung điểm tuyến MEP, vị trí sleeve có thể lệch, kiểm lại.");
        }
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
    /// (2) cắt tuyến với hộp bao host (Liang–Barsky) khi host không có solid; (3) bất đắc dĩ mới
    /// dùng trung điểm tuyến MEP và báo qua <paramref name="usedMidpoint"/>.
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
        if (candidate.HasBox && mepCurve is Line)
        {
            var p0 = mepCurve.GetEndPoint(0);
            var p1 = mepCurve.GetEndPoint(1);
            if (ClipLineToBox(p0, p1,
                    candidate.MinX, candidate.MinY, candidate.MinZ,
                    candidate.MaxX, candidate.MaxY, candidate.MaxZ,
                    out var t0, out var t1))
            {
                var tm = (t0 + t1) * 0.5;
                return p0 + (p1 - p0) * tm;
            }
        }

        // 3. Bất đắc dĩ: trung điểm tuyến.
        usedMidpoint = true;
        var mid = mepCurve.Evaluate(0.5, true);
        if (candidate.HasBox && candidate.Host is Floor)
        {
            return new XYZ(mid.X, mid.Y, (candidate.MinZ + candidate.MaxZ) * 0.5);
        }

        return mid;
    }

    /// <summary>Liang–Barsky: khoảng tham số [t0, t1] ⊂ [0, 1] của đoạn p0→p1 nằm trong hộp; false nếu không cắt.</summary>
    private static bool ClipLineToBox(XYZ p0, XYZ p1,
        double minX, double minY, double minZ, double maxX, double maxY, double maxZ,
        out double t0, out double t1)
    {
        t0 = 0.0;
        t1 = 1.0;
        var d = p1 - p0;
        var starts = new[] { p0.X, p0.Y, p0.Z };
        var deltas = new[] { d.X, d.Y, d.Z };
        var mins = new[] { minX, minY, minZ };
        var maxs = new[] { maxX, maxY, maxZ };

        for (var axis = 0; axis < 3; axis++)
        {
            if (Math.Abs(deltas[axis]) < 1e-12)
            {
                if (starts[axis] < mins[axis] || starts[axis] > maxs[axis]) return false;
                continue;
            }

            var tA = (mins[axis] - starts[axis]) / deltas[axis];
            var tB = (maxs[axis] - starts[axis]) / deltas[axis];
            if (tA > tB) { var tmp = tA; tA = tB; tB = tmp; }
            t0 = Math.Max(t0, tA);
            t1 = Math.Min(t1, tB);
            if (t0 > t1) return false;
        }

        return true;
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
