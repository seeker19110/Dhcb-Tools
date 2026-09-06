using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using DhcbTools.Shared.Logic.Mep;

namespace DhcbTools.Core.MEPF;

/// <summary>Cấu hình <see cref="FamilyStarterCommand"/>: dựng family mẫu DHCB_Sleeve / DHCB_Hanger rồi nạp vào mô hình.</summary>
public sealed class FamilyStarterConfig
{
    /// <summary>Thư mục ghi file .rfa (tạo nếu chưa có).</summary>
    public required string OutputFolder { get; init; }

    /// <summary>Family cần dựng: Sleeve, Hanger (rỗng = cả hai).</summary>
    public List<string> Families { get; init; } = new List<string>();

    /// <summary>Nạp family vừa dựng vào mô hình đang mở (mặc định bật).</summary>
    public bool Load { get; init; } = true;

    /// <summary>Ghi đè .rfa đã có và tham số của family đã nạp.</summary>
    public bool Overwrite { get; init; } = false;

    /// <summary>Xem trước: chỉ báo sẽ dựng gì, không ghi file, không nạp.</summary>
    public bool DryRun { get; init; } = true;
}

/// <summary>
/// Dựng family mẫu tối giản cho <c>SleeveAuto</c>/<c>HangerAuto</c> từ template Generic Model kèm Revit, ghi .rfa và
/// nạp vào mô hình — để hai lệnh đó chạy được trên dự án không có family sleeve/hanger (vai MEP §57: "Bỏ vì thiếu
/// family"). Hình học là hình giữ chỗ (ống lồng DN100×300, đế treo 100×100×50); tham số instance <c>Nominal Width</c>,
/// <c>Nominal Height</c>, <c>Rod Length</c> có để lệnh ghi kích thước và schedule đọc được, nhưng KHÔNG điều khiển
/// hình học — doanh nghiệp thay bằng family chuẩn của mình khi có. Family work-plane-based nên đặt được lên mặt tường/sàn.
/// </summary>
public sealed class FamilyStarterCommand : ICoreCommand<FamilyStarterConfig>
{
    public string CommandName => "FamilyStarter";

    private static readonly string[] All = { "Sleeve", "Hanger" };

    public CommandResult Execute(Document document, FamilyStarterConfig config)
    {
        var plan = FamilyStarterPlanner.Plan(config.Families, All, out var unknown);
        if (unknown.Count > 0)
        {
            return CommandResult.Fail(FamilyStarterPlanner.UnknownMessage(unknown, All));
        }

        var template = FindTemplate(document.Application, out var searched);
        if (template == null)
        {
            return CommandResult.Fail(FamilyStarterPlanner.NoTemplateMessage(searched));
        }

        var result = CommandResult.Ok(string.Empty);
        result.Messages.Add("Template: " + template);
        if (config.DryRun)
        {
            foreach (var key in plan)
            {
                result.Messages.Add(FamilyStarterPlanner.PreviewLine(key, Path.Combine(config.OutputFolder, FamilyStarterPlanner.FamilyName(key) + ".rfa"), config.Load));
            }

            result.Summary = FamilyStarterPlanner.PreviewSummary(plan, config.Load);
            result.AffectedCount = plan.Count;
            return result;
        }

        Directory.CreateDirectory(config.OutputFolder);
        var built = new List<string>();
        var loaded = 0;
        foreach (var key in plan)
        {
            var name = FamilyStarterPlanner.FamilyName(key);
            var rfa = Path.Combine(config.OutputFolder, name + ".rfa");
            if (File.Exists(rfa) && !config.Overwrite)
            {
                result.Messages.Add($"{name}: đã có {rfa} — giữ nguyên (overwrite=false).");
            }
            else
            {
                try
                {
                    Build(document.Application, template, key, rfa);
                    result.Messages.Add($"{name}: đã dựng → {rfa}" + (key == "Sleeve"
                        ? (LastLabelError == null ? " (đường kính do Nominal Width điều khiển)" : $" (hình cố định — không gắn được nhãn tham số: {LastLabelError})")
                        : string.Empty));
                }
                catch (Exception ex)
                {
                    result.Errors.Add($"{name}: không dựng được ({ex.Message}).");
                    continue;
                }
            }

            built.Add(name);
            if (config.Load)
            {
                try
                {
                    using var tx = RevitCompat.StartTransaction(document, "DHCB - Nạp family mẫu " + name);
                    var ok = document.LoadFamily(rfa, new LoadOptions(config.Overwrite), out var family);
                    tx.Commit();
                    if (ok && family != null)
                    {
                        loaded++;
                        result.Messages.Add($"{name}: đã nạp vào mô hình ({family.Name}).");
                    }
                    else
                    {
                        result.Messages.Add($"{name}: mô hình đã có family này — không nạp lại (overwrite=false).");
                        loaded++;
                    }
                }
                catch (Exception ex)
                {
                    result.Errors.Add($"{name}: nạp thất bại ({ex.Message}).");
                }
            }
        }

        result.Summary = FamilyStarterPlanner.DoneSummary(built, config.OutputFolder, config.Load ? loaded : (int?)null);
        result.AffectedCount = built.Count;
        result.Success = built.Count > 0;
        return result;
    }

    /// <summary>
    /// "Metric Generic Model.rft" theo <c>Application.FamilyTemplatePath</c>; bản cài tiếng Anh để template trong thư mục
    /// con "English" nên tìm thêm một cấp. Trả null kèm danh sách đã tìm để lỗi nói được vì sao.
    /// </summary>
    internal static string? FindTemplate(Autodesk.Revit.ApplicationServices.Application app, out List<string> searched)
    {
        searched = new List<string>();
        var root = app.FamilyTemplatePath ?? string.Empty;
        foreach (var dir in new[] { root, Path.Combine(root, "English"), Path.Combine(root, "English-Imperial") })
        {
            var p = Path.Combine(dir, "Metric Generic Model.rft");
            searched.Add(p);
            if (File.Exists(p)) return p;
        }

        try
        {
            if (Directory.Exists(root))
            {
                var found = Directory.EnumerateFiles(root, "Metric Generic Model.rft", SearchOption.AllDirectories).FirstOrDefault();
                if (found != null) return found;
            }
        }
        catch (Exception)
        {
            // Thư mục template không đọc được → rơi xuống null.
        }

        return null;
    }

    private static void Build(Autodesk.Revit.ApplicationServices.Application app, string template, string key, string rfa)
    {
        var fam = app.NewFamilyDocument(template);
        try
        {
            using (var tx = new Transaction(fam, "DHCB - Dựng family mẫu"))
            {
                tx.Start();
                // Work-plane-based: đặt được lên mặt tường/sàn (SleeveAuto dùng NewFamilyInstance(face, …)).
                fam.OwnerFamily.get_Parameter(BuiltInParameter.FAMILY_WORK_PLANE_BASED)?.Set(1);
                var plane = Plane.CreateByNormalAndOrigin(XYZ.BasisZ, XYZ.Zero);
                var sketch = SketchPlane.Create(fam, plane);

                if (key == "Sleeve")
                {
                    // Ống lồng DN100: hình tròn bán kính 50 mm, dài 300 mm, tâm tại gốc (−150…+150 theo Z = pháp tuyến mặt).
                    var r = RevitCompat.MmToFt(50);
                    var profile = new CurveArrArray();
                    var loop = new CurveArray();
                    loop.Append(Arc.Create(plane, r, 0, Math.PI));
                    loop.Append(Arc.Create(plane, r, Math.PI, 2 * Math.PI));
                    profile.Append(loop);
                    var ext = fam.FamilyCreate.NewExtrusion(true, profile, sketch, RevitCompat.MmToFt(300));
                    ext.get_Parameter(BuiltInParameter.EXTRUSION_START_PARAM)?.Set(RevitCompat.MmToFt(-150));
                    ext.get_Parameter(BuiltInParameter.EXTRUSION_END_PARAM)?.Set(RevitCompat.MmToFt(150));
                    var width = AddLengthParameter(fam, "Nominal Width", 100);
                    AddLengthParameter(fam, "Nominal Height", 100);
                    // Tham số điều khiển hình học (§62): kích thước đường kính gắn nhãn Nominal Width lên cung của
                    // sketch. Không gắn được (API/template khác) thì family vẫn dùng được với hình cố định — ghi lại.
                    LabelDiameter(fam, ext, width);
                }
                else
                {
                    // Đế treo 100×100 dày 50 mm nằm dưới gốc (ống/duct ở gốc), có tham số Rod Length để schedule.
                    var h = RevitCompat.MmToFt(50);
                    var profile = new CurveArrArray();
                    var loop = new CurveArray();
                    var a = new XYZ(-h, -h, 0); var b = new XYZ(h, -h, 0); var c = new XYZ(h, h, 0); var d = new XYZ(-h, h, 0);
                    loop.Append(Line.CreateBound(a, b)); loop.Append(Line.CreateBound(b, c));
                    loop.Append(Line.CreateBound(c, d)); loop.Append(Line.CreateBound(d, a));
                    profile.Append(loop);
                    var ext = fam.FamilyCreate.NewExtrusion(true, profile, sketch, RevitCompat.MmToFt(50));
                    ext.get_Parameter(BuiltInParameter.EXTRUSION_START_PARAM)?.Set(RevitCompat.MmToFt(-50));
                    ext.get_Parameter(BuiltInParameter.EXTRUSION_END_PARAM)?.Set(0);
                    AddLengthParameter(fam, "Rod Length", 500);
                }

                tx.Commit();
            }

            fam.SaveAs(rfa, new SaveAsOptions { OverwriteExistingFile = true, MaximumBackups = 1 });
        }
        finally
        {
            fam.Close(false);
        }
    }

    private static FamilyParameter AddLengthParameter(Document fam, string name, double defaultMm)
    {
        var fm = fam.FamilyManager;
#if REVIT2023_OR_GREATER
        var p = fm.AddParameter(name, GroupTypeId.Geometry, SpecTypeId.Length, true);
#else
        var p = fm.AddParameter(name, BuiltInParameterGroup.PG_GEOMETRY, ParameterType.Length, true);
#endif
        if (fm.CurrentType != null)
        {
            fm.Set(p, RevitCompat.MmToFt(defaultMm));
        }

        return p;
    }

    /// <summary>Đường kính = Nominal Width: kích thước đường kính trên cung sketch của extrusion, gắn nhãn tham số.</summary>
    internal static string? LastLabelError;

    private static void LabelDiameter(Document fam, Extrusion ext, FamilyParameter width)
    {
        LastLabelError = null;
        try
        {
            var view = new FilteredElementCollector(fam).OfClass(typeof(ViewPlan)).Cast<ViewPlan>().FirstOrDefault(v => !v.IsTemplate);
            if (view == null) { LastLabelError = "family không có view mặt bằng"; return; }
            Reference? arcRef = null;
            foreach (CurveArray loop in ext.Sketch.Profile)
            {
                foreach (Curve c in loop)
                {
                    if (c is Arc && c.Reference != null) { arcRef = c.Reference; break; }
                }
                if (arcRef != null) break;
            }
            if (arcRef == null) { LastLabelError = "không lấy được tham chiếu cung sketch"; return; }
            var dim = fam.FamilyCreate.NewDiameterDimension(view, arcRef, new XYZ(RevitCompat.MmToFt(120), RevitCompat.MmToFt(120), 0));
            dim.FamilyLabel = width;
        }
        catch (Exception ex)
        {
            LastLabelError = ex.Message;
        }
    }

    private sealed class LoadOptions : IFamilyLoadOptions
    {
        private readonly bool _overwrite;

        public LoadOptions(bool overwrite) => _overwrite = overwrite;

        public bool OnFamilyFound(bool familyInUse, out bool overwriteParameterValues)
        {
            overwriteParameterValues = _overwrite;
            return _overwrite;
        }

        public bool OnSharedFamilyFound(Family sharedFamily, bool familyInUse, out FamilySource source, out bool overwriteParameterValues)
        {
            source = FamilySource.Family;
            overwriteParameterValues = _overwrite;
            return _overwrite;
        }
    }
}
