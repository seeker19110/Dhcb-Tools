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
        var symbol = FindFamilySymbol(document, config.SleeveFamilyName);
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
        var placements = new List<(XYZ Point, Face? Face, Wall? HostWall, Floor? HostFloor, double WidthFt, double HeightFt, Element MepElement)>();

        // Phần tử không tra được kích thước: trước đây âm thầm dùng 6 inch mặc định nên sleeve ra sai cỡ
        // mà không ai biết. Nay gom lại để báo trong CommandResult.
        var unknownSize = new List<long>();

        // Lỗi hiệu năng đã sửa: trước đây FilteredElementCollector toàn model (Walls+Floors) được dựng lại
        // BÊN TRONG vòng lặp cho từng phần tử MEP — O(n·m) trên model lớn, vượt timeout Bridge 30 s.
        // Thu thập một lần ở đây, lọc bbox trong bộ nhớ cho từng phần tử MEP.
        var hostCandidatesAll = new FilteredElementCollector(document)
            .WhereElementIsNotElementType()
            .WherePasses(new LogicalOrFilter(
                new ElementCategoryFilter(BuiltInCategory.OST_Walls),
                new ElementCategoryFilter(BuiltInCategory.OST_Floors)))
            .ToList();

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

            var candidates = hostCandidatesAll.Where(c => bbFilter.PassesFilter(c)).ToList();

            // Refine with solid intersection if available
            IEnumerable<Element> hosts;
            if (solid != null)
            {
                try
                {
                    var solidFilter = new ElementIntersectsSolidFilter(solid);
                    hosts = candidates.Where(c => solidFilter.PassesFilter(c));
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

            foreach (var host in hosts)
            {
                // Filter by host type name if configured
                if (config.HostTypeNames.Count > 0)
                {
                    var typeName = GetElementTypeName(document, host);
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

                var intersectionPt = FindIntersectionPoint(curve, host);
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

                placements.Add((intersectionPt, null!, host as Wall, host as Floor, widthFt, heightFt, mepElem));
            }
        }

        if (config.DryRun)
        {
            var preview = CommandResult.Ok(
                $"[Xem trước] Sẽ đặt {placements.Count} sleeve tại giao cắt MEP × Tường/Sàn.",
                placements.Count);
            AddUnknownSizeWarning(preview, unknownSize, config);
            foreach (var p in placements)
            {
                var hostDesc = p.HostWall != null ? $"Tường {p.HostWall.Id}" : $"Sàn {p.HostFloor?.Id}";
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

        foreach (var (point, _, hostWall, hostFloor, widthFt, heightFt, _) in placements)
        {
            try
            {
                FamilyInstance? inst = null;

                if (hostWall != null)
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
        var result = CommandResult.Ok($"Đã đặt {placed} sleeve tại giao cắt MEP × Tường/Sàn.", placed)
            .WithChanged(placedIds);
        AddUnknownSizeWarning(result, unknownSize, config);
        return result;
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static FamilySymbol? FindFamilySymbol(Document doc, string name)
    {
        return new FilteredElementCollector(doc)
            .OfClass(typeof(FamilySymbol))
            .Cast<FamilySymbol>()
            .FirstOrDefault(s =>
                string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(s.FamilyName + ": " + s.Name, name, StringComparison.OrdinalIgnoreCase));
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

    private static XYZ FindIntersectionPoint(Curve mepCurve, Element host)
    {
        // Use midpoint strategy: project midpoint of MEP curve onto host bounding box centre Z
        var mid = mepCurve.Evaluate(0.5, true);
        var hostBb = host.get_BoundingBox(null);
        if (hostBb == null) return mid;

        // For walls: keep XY of intersection with wall, Z from MEP
        // Simple: return MEP midpoint adjusted to host centreplane
        var hostCentre = (hostBb.Min + hostBb.Max) * 0.5;
        if (host is Wall)
        {
            // Return point at MEP curve location with host's Z-centre if curve is horizontal
            return new XYZ(mid.X, mid.Y, mid.Z);
        }
        else // Floor
        {
            return new XYZ(mid.X, mid.Y, hostCentre.Z);
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
