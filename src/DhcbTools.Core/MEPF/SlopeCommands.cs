using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using DhcbTools.Shared.Logic.Mep;

namespace DhcbTools.Core.MEPF;

/// <summary>P2 — ống dốc (Naviate MEP): đặt dốc cho ống thoát nước theo % hoặc kiểm tra dốc đã có.</summary>
public sealed class SlopePipesConfig
{
    /// <summary>Độ dốc %; null = theo bảng tối thiểu theo đường kính (<see cref="SlopeMath.MinSlopePercent"/>).</summary>
    public double? SlopePercent { get; init; }

    /// <summary>Lọc theo System Name / System Type chứa chuỗi này (ví dụ "Sanitary", "Thoát").</summary>
    public string? SystemContains { get; init; }

    public string? LevelName { get; init; }

    /// <summary>Chỉ những ống có Id trong danh sách (rỗng = theo bộ lọc).</summary>
    public List<string> ElementIds { get; init; } = new List<string>();

    /// <summary>Đầu bị hạ thấp: "End" (mặc định, theo chiều vẽ) hoặc "Start".</summary>
    public string LowerEnd { get; init; } = "End";

    /// <summary>Chỉ kiểm tra và báo ống chưa đạt dốc, không sửa.</summary>
    public bool CheckOnly { get; init; }

    /// <summary>Bỏ qua ống gần thẳng đứng (góc với mặt phẳng ngang &gt; giá trị này, độ).</summary>
    public double MaxAngleFromHorizontalDeg { get; init; } = 10;

    public bool DryRun { get; init; } = true;
}

public sealed class SlopePipesCommand : ICoreCommand<SlopePipesConfig>
{
    public string CommandName => "SlopePipes";

    public CommandResult Execute(Document document, SlopePipesConfig config)
    {
        var pipes = new FilteredElementCollector(document).OfClass(typeof(Pipe)).Cast<Pipe>().ToList();
        if (config.ElementIds.Count > 0)
        {
            var wanted = new HashSet<long>(config.ElementIds.Select(s => long.TryParse(s, out var v) ? v : -1));
            pipes = pipes.Where(p => wanted.Contains(RevitCompat.IdValue(p.Id))).ToList();
        }
        if (!string.IsNullOrEmpty(config.SystemContains))
        {
            pipes = pipes.Where(p =>
                MepParams.SystemName(p).IndexOf(config.SystemContains!, StringComparison.OrdinalIgnoreCase) >= 0 ||
                MepParams.SystemType(p).IndexOf(config.SystemContains!, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
        }
        if (!string.IsNullOrEmpty(config.LevelName))
        {
            var level = RevitCompat.FindLevel(document, config.LevelName);
            if (level == null) return CommandResult.Fail($"Không có Level \"{config.LevelName}\".");
            pipes = pipes.Where(p => p.ReferenceLevel?.Id == level.Id).ToList();
        }
        if (pipes.Count == 0)
        {
            return CommandResult.Fail("Không có ống nào khớp bộ lọc.");
        }

        var result = CommandResult.Ok(string.Empty);
        var lowerStart = SlopePlanner.LowerStart(config.LowerEnd);
        var byId = pipes.ToDictionary(p => RevitCompat.IdValue(p.Id));
        var inputs = new List<SlopePipeInput>();
        foreach (var pipe in pipes)
        {
            if (pipe.Location is not LocationCurve lc || lc.Curve is not Line line) continue;
            var p0 = line.GetEndPoint(0);
            var p1 = line.GetEndPoint(1);
            inputs.Add(new SlopePipeInput(RevitCompat.IdValue(pipe.Id),
                RevitCompat.FtToMm(p0.X), RevitCompat.FtToMm(p0.Y), RevitCompat.FtToMm(p0.Z),
                RevitCompat.FtToMm(p1.X), RevitCompat.FtToMm(p1.Y), RevitCompat.FtToMm(p1.Z),
                RevitCompat.FtToMm(pipe.Diameter)));
        }

        // Toàn bộ quyết định (bỏ qua ống gần đứng, dốc yêu cầu, đạt/chưa, cao độ mới) ở tầng thuần — §51.
        var plan = SlopePlanner.PlanAll(inputs, config.SlopePercent, lowerStart, config.MaxAngleFromHorizontalDeg, result.Messages);
        var toFix = plan.Where(p => p.NeedsFix).ToList();
        if (config.CheckOnly)
        {
            result.Summary = SlopePlanner.CheckSummary(plan.Count, toFix.Count);
            result.Messages.AddRange(toFix.Select(SlopePlanner.CheckLine));
            result.AffectedCount = toFix.Count;
            return result;
        }

        if (config.DryRun)
        {
            result.Summary = SlopePlanner.PreviewSummary(toFix.Count, plan.Count, lowerStart);
            result.Messages.AddRange(toFix.Select(SlopePlanner.PreviewLine));
            result.AffectedCount = toFix.Count;
            return result;
        }

        var done = 0;
        using var tx = RevitCompat.StartTransaction(document, "DHCB - Đặt dốc ống");
        foreach (var item in toFix)
        {
            var pipe = byId[item.Id];
            try
            {
                var line = (Line)((LocationCurve)pipe.Location).Curve;
                var p0 = line.GetEndPoint(0);
                var p1 = line.GetEndPoint(1);
                var n0 = new XYZ(p0.X, p0.Y, RevitCompat.MmToFt(item.NewZ0Mm));
                var n1 = new XYZ(p1.X, p1.Y, RevitCompat.MmToFt(item.NewZ1Mm));
                ((LocationCurve)pipe.Location).Curve = Line.CreateBound(n0, n1);
                done++;
            }
            catch (Exception ex)
            {
                result.Errors.Add(SlopePlanner.WriteError(item.Id, ex.Message));
            }
        }

        tx.Commit();
        result.Summary = SlopePlanner.WriteSummary(done, toFix.Count);
        result.AffectedCount = done;
        return result;
    }
}

/// <summary>P2 — kick (jog) ống bằng hai cút 45°/90° (Naviate kick-90, Victaulic): dịch ngang một đoạn ống đang thẳng.</summary>
public sealed class PipeKickConfig
{
    /// <summary>Id ống cần kick.</summary>
    public required string ElementId { get; init; }

    /// <summary>Khoảng dịch (mm), luôn dương; hướng theo <see cref="OffsetDirection"/>.</summary>
    public double OffsetMm { get; init; } = 300;

    /// <summary>Hướng dịch: "Up" | "Down" | "Left" | "Right" (trái/phải so với chiều vẽ, trong mặt phẳng ngang).</summary>
    public string OffsetDirection { get; init; } = "Up";

    /// <summary>Góc cút: 45 (mặc định) hoặc 90.</summary>
    public double ElbowAngleDeg { get; init; } = 45;

    /// <summary>Khoảng cách từ đầu ống tới điểm bắt đầu kick (mm).</summary>
    public double DistanceFromStartMm { get; init; } = 500;

    public bool DryRun { get; init; } = true;
}

public sealed class PipeKickCommand : ICoreCommand<PipeKickConfig>
{
    public string CommandName => "PipeKick";

    public CommandResult Execute(Document document, PipeKickConfig config)
    {
        if (!RevitCompat.TryParseId(config.ElementId, out var id) || document.GetElement(id) is not Pipe pipe)
        {
            return CommandResult.Fail($"Không tìm thấy ống Id {config.ElementId}.");
        }
        if (pipe.Location is not LocationCurve lc || lc.Curve is not Line line)
        {
            return CommandResult.Fail("Ống không phải đoạn thẳng.");
        }

        var geom = SlopeMath.Kick(config.OffsetMm, config.ElbowAngleDeg);
        var diameterMm = RevitCompat.FtToMm(pipe.Diameter);
        var lengthMm = RevitCompat.FtToMm(line.Length);
        var invalid = KickPlanner.Validate(lengthMm, diameterMm, config.OffsetMm, config.ElbowAngleDeg, config.DistanceFromStartMm);
        if (invalid != null)
        {
            return CommandResult.Fail(invalid);
        }

        var result = CommandResult.Ok(string.Empty);
        var p0 = line.GetEndPoint(0);
        var p1 = line.GetEndPoint(1);
        var dir = (p1 - p0).Normalize();
        var (ox, oy, oz) = KickPlanner.OffsetDirection(config.OffsetDirection, dir.X, dir.Y);
        var offsetDir = new XYZ(ox, oy, oz);

        var a = p0 + dir * RevitCompat.MmToFt(config.DistanceFromStartMm);
        var b = a + dir * RevitCompat.MmToFt(geom.AlongAxisMm);
        var bOff = b + offsetDir * RevitCompat.MmToFt(config.OffsetMm);
        var endOff = p1 + offsetDir * RevitCompat.MmToFt(config.OffsetMm);

        result.Messages.Add(KickPlanner.Note(config.OffsetDirection, config.OffsetMm, config.ElbowAngleDeg, geom.DiagonalMm, config.DistanceFromStartMm));
        if (config.DryRun)
        {
            result.Summary = KickPlanner.PreviewSummary(config.ElementId);
            result.AffectedCount = 1;
            return result;
        }

        using var tx = RevitCompat.StartTransaction(document, "DHCB - Kick ống");
        try
        {
            // Tách tại A rồi tại B (B nằm trên đoạn sau A). Sau BreakCurve, nhận diện đoạn bằng cách xem
            // đoạn nào CHỨA một điểm dò (trung điểm của khoảng cần tìm) — không so đầu mút bằng
            // IsAlmostEqualTo (dung sai 1e-9 ft, BreakCurve làm tròn là trượt).
            var id2 = PlumbingUtils.BreakCurve(document, pipe.Id, a);
            var pipe2 = (Pipe)document.GetElement(id2);
            var afterA = Containing(pipe, pipe2, (a + p1) * 0.5);
            var beforeA = afterA == pipe2 ? pipe : pipe2;

            Pipe middle, tail;
            if (geom.AlongAxisMm > 1e-6)
            {
                var id3 = PlumbingUtils.BreakCurve(document, afterA.Id, b);
                var pipe3 = (Pipe)document.GetElement(id3);
                middle = Containing(afterA, pipe3, (a + b) * 0.5);
                tail = middle == pipe3 ? afterA : pipe3;
            }
            else
            {
                // Kick-90: không có đoạn chéo chiếm trục — chèn đoạn thẳng đứng ngắn bằng cách tách thêm một lần sát A.
                var bb = a + dir * RevitCompat.MmToFt(KickPlanner.MiddleLengthMm(diameterMm, geom.AlongAxisMm));
                var id3 = PlumbingUtils.BreakCurve(document, afterA.Id, bb);
                var pipe3 = (Pipe)document.GetElement(id3);
                middle = Containing(afterA, pipe3, (a + bb) * 0.5);
                tail = middle == pipe3 ? afterA : pipe3;
                bOff = a + offsetDir * RevitCompat.MmToFt(config.OffsetMm);
            }

            // Dịch đoạn cuối lên/xuống, đoạn giữa thành đoạn chéo.
            var tailCurve = (LocationCurve)tail.Location;
            var t0 = tailCurve.Curve.GetEndPoint(0);
            var t1 = tailCurve.Curve.GetEndPoint(1);
            var tailStartIsB = t0.DistanceTo(b) < t1.DistanceTo(b);
            tailCurve.Curve = tailStartIsB ? Line.CreateBound(bOff, endOff) : Line.CreateBound(endOff, bOff);
            ((LocationCurve)middle.Location).Curve = Line.CreateBound(a, bOff);

            // Cút tại A và tại B'.
            var made = 0;
            made += TryElbow(document, beforeA, middle, a, result) ? 1 : 0;
            made += TryElbow(document, middle, tail, bOff, result) ? 1 : 0;

            tx.Commit();
            result.Summary = KickPlanner.WriteSummary(RevitCompat.IdValue(beforeA.Id), RevitCompat.IdValue(middle.Id), RevitCompat.IdValue(tail.Id), made);
            result.AffectedCount = 3;
            if (made < 2) result.Errors.Add("Một số cút không dựng được — kiểm tra routing preference có cút góc " + config.ElbowAngleDeg + "°.");
            return result;
        }
        catch (Exception ex)
        {
            tx.RollBack();
            return CommandResult.Fail("Kick thất bại, đã hoàn tác: " + ex.Message);
        }
    }

    /// <summary>Trong hai đoạn, đoạn nào chứa <paramref name="probe"/> (khoảng cách tới tuyến nhỏ hơn; dung sai 1 mm).</summary>
    private static Pipe Containing(Pipe x, Pipe y, XYZ probe)
    {
        var dx = ((LocationCurve)x.Location).Curve.Distance(probe);
        var dy = ((LocationCurve)y.Location).Curve.Distance(probe);
        var tol = RevitCompat.MmToFt(1.0);
        if (dx <= tol && dy > tol) return x;
        if (dy <= tol && dx > tol) return y;
        return dx <= dy ? x : y;
    }

    private static bool TryElbow(Document doc, Pipe a, Pipe b, XYZ at, CommandResult result)
    {
        try
        {
            var ca = Nearest(a, at);
            var cb = Nearest(b, at);
            if (ca == null || cb == null) return false;
            doc.Create.NewElbowFitting(ca, cb);
            return true;
        }
        catch (Exception ex)
        {
            result.Messages.Add($"Cút tại ({RevitCompat.FtToMm(at.X):F0},{RevitCompat.FtToMm(at.Y):F0},{RevitCompat.FtToMm(at.Z):F0}) mm: {ex.Message}");
            return false;
        }
    }

    private static Connector? Nearest(Pipe pipe, XYZ at)
    {
        Connector? best = null;
        var bestD = double.MaxValue;
        foreach (Connector c in pipe.ConnectorManager.Connectors)
        {
            if (c.IsConnected) continue;
            var d = c.Origin.DistanceTo(at);
            if (d < bestD) { bestD = d; best = c; }
        }
        return best;
    }
}
