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
                RevitCompat.ReadString(p, "System Name").IndexOf(config.SystemContains!, StringComparison.OrdinalIgnoreCase) >= 0 ||
                RevitCompat.ReadString(p, "System Type").IndexOf(config.SystemContains!, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
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
        var plan = new List<(Pipe Pipe, XYZ P0, XYZ P1, double RequiredPercent, string? Issue)>();
        var maxAngle = config.MaxAngleFromHorizontalDeg * Math.PI / 180.0;

        foreach (var pipe in pipes)
        {
            if (pipe.Location is not LocationCurve lc || lc.Curve is not Line line) continue;
            var p0 = line.GetEndPoint(0);
            var p1 = line.GetEndPoint(1);
            var horizontal = Math.Sqrt(Math.Pow(p1.X - p0.X, 2) + Math.Pow(p1.Y - p0.Y, 2));
            if (horizontal < 1e-6) continue;
            var angle = Math.Atan2(Math.Abs(p1.Z - p0.Z), horizontal);
            if (angle > maxAngle)
            {
                result.Messages.Add($"{RevitCompat.IdValue(pipe.Id)}: bỏ qua (gần thẳng đứng).");
                continue;
            }

            var diameterMm = RevitCompat.FtToMm(pipe.Diameter);
            var required = config.SlopePercent ?? SlopeMath.MinSlopePercent(diameterMm);
            var lowerEnd = config.LowerEnd.Equals("Start", StringComparison.OrdinalIgnoreCase);
            var dropMm = lowerEnd ? RevitCompat.FtToMm(p0.Z - p1.Z) * -1 : RevitCompat.FtToMm(p0.Z - p1.Z);
            var issue = SlopeMath.CheckSlope(RevitCompat.FtToMm(horizontal), dropMm, required);
            plan.Add((pipe, p0, p1, required, issue));
        }

        var toFix = plan.Where(p => p.Issue != null).ToList();
        if (config.CheckOnly)
        {
            result.Summary = $"Kiểm {plan.Count} ống: {toFix.Count} chưa đạt dốc.";
            result.Messages.AddRange(toFix.Select(p => $"{RevitCompat.IdValue(p.Pipe.Id)} DN{RevitCompat.FtToMm(p.Pipe.Diameter):F0}: {p.Issue}"));
            result.AffectedCount = toFix.Count;
            return result;
        }

        if (config.DryRun)
        {
            result.Summary = $"[Xem trước] Sẽ đặt dốc cho {toFix.Count}/{plan.Count} ống (hạ đầu {(config.LowerEnd.Equals("Start", StringComparison.OrdinalIgnoreCase) ? "đầu" : "cuối")}).";
            result.Messages.AddRange(toFix.Select(p => $"{RevitCompat.IdValue(p.Pipe.Id)}: {p.Issue} → đặt {p.RequiredPercent:0.##} %"));
            result.AffectedCount = toFix.Count;
            return result;
        }

        var done = 0;
        using var tx = RevitCompat.StartTransaction(document, "DHCB - Đặt dốc ống");
        foreach (var (pipe, p0, p1, required, _) in toFix)
        {
            try
            {
                var horizontalFt = Math.Sqrt(Math.Pow(p1.X - p0.X, 2) + Math.Pow(p1.Y - p0.Y, 2));
                var dropFt = RevitCompat.MmToFt(SlopeMath.DropMm(RevitCompat.FtToMm(horizontalFt), required));
                XYZ n0, n1;
                if (config.LowerEnd.Equals("Start", StringComparison.OrdinalIgnoreCase))
                {
                    n1 = p1;
                    n0 = new XYZ(p0.X, p0.Y, p1.Z - dropFt);
                }
                else
                {
                    n0 = p0;
                    n1 = new XYZ(p1.X, p1.Y, p0.Z - dropFt);
                }
                ((LocationCurve)pipe.Location).Curve = Line.CreateBound(n0, n1);
                done++;
            }
            catch (Exception ex)
            {
                result.Errors.Add($"{RevitCompat.IdValue(pipe.Id)}: {ex.Message} (ống đã nối fitting hai đầu có thể không dịch được — tách đoạn trước).");
            }
        }

        tx.Commit();
        result.Summary = $"Đã đặt dốc {done}/{toFix.Count} ống.";
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
        var minLen = SlopeMath.MinPipeLengthForKick(config.OffsetMm, diameterMm, config.ElbowAngleDeg);
        var result = CommandResult.Ok(string.Empty);
        if (lengthMm < minLen)
        {
            return CommandResult.Fail($"Ống dài {lengthMm:F0} mm, cần ≥ {minLen:F0} mm để đặt kick {config.OffsetMm} mm với cút {config.ElbowAngleDeg}°.");
        }
        if (config.DistanceFromStartMm + geom.AlongAxisMm + 3 * diameterMm > lengthMm)
        {
            return CommandResult.Fail($"distanceFromStartMm quá lớn: kick không nằm trong ống.");
        }

        var p0 = line.GetEndPoint(0);
        var p1 = line.GetEndPoint(1);
        var dir = (p1 - p0).Normalize();
        var horizontal = new XYZ(dir.X, dir.Y, 0);
        XYZ offsetDir = config.OffsetDirection.ToUpperInvariant() switch
        {
            "UP" => XYZ.BasisZ,
            "DOWN" => -XYZ.BasisZ,
            "LEFT" => horizontal.GetLength() > 1e-9 ? XYZ.BasisZ.CrossProduct(horizontal).Normalize() : XYZ.BasisY,
            "RIGHT" => horizontal.GetLength() > 1e-9 ? horizontal.CrossProduct(XYZ.BasisZ).Normalize() : -XYZ.BasisY,
            _ => XYZ.BasisZ,
        };

        var a = p0 + dir * RevitCompat.MmToFt(config.DistanceFromStartMm);
        var b = a + dir * RevitCompat.MmToFt(geom.AlongAxisMm);
        var bOff = b + offsetDir * RevitCompat.MmToFt(config.OffsetMm);
        var endOff = p1 + offsetDir * RevitCompat.MmToFt(config.OffsetMm);

        result.Messages.Add($"Kick {config.OffsetDirection} {config.OffsetMm} mm, cút {config.ElbowAngleDeg}°: đoạn chéo {geom.DiagonalMm:F0} mm, bắt đầu cách đầu ống {config.DistanceFromStartMm} mm.");
        if (config.DryRun)
        {
            result.Summary = $"[Xem trước] Sẽ chia ống {config.ElementId} thành 3 đoạn + 2 cút.";
            result.AffectedCount = 1;
            return result;
        }

        using var tx = RevitCompat.StartTransaction(document, "DHCB - Kick ống");
        try
        {
            // Tách tại A rồi tại B (B nằm trên đoạn sau A).
            var id2 = PlumbingUtils.BreakCurve(document, pipe.Id, a);
            var pipe2 = (Pipe)document.GetElement(id2);
            var afterA = ((LocationCurve)pipe2.Location).Curve.GetEndPoint(0).IsAlmostEqualTo(a) ? pipe2 : pipe;
            var beforeA = afterA == pipe2 ? pipe : pipe2;

            Pipe middle, tail;
            if (geom.AlongAxisMm > 1e-6)
            {
                var id3 = PlumbingUtils.BreakCurve(document, afterA.Id, b);
                var pipe3 = (Pipe)document.GetElement(id3);
                var startsAtA = ((LocationCurve)afterA.Location).Curve.GetEndPoint(0).IsAlmostEqualTo(a) || ((LocationCurve)afterA.Location).Curve.GetEndPoint(1).IsAlmostEqualTo(a);
                middle = startsAtA ? afterA : pipe3;
                tail = startsAtA ? pipe3 : afterA;
            }
            else
            {
                // Kick-90: không có đoạn chéo chiếm trục — chèn đoạn thẳng đứng ngắn bằng cách tách thêm một lần sát A.
                var bb = a + dir * RevitCompat.MmToFt(Math.Max(diameterMm, 50));
                var id3 = PlumbingUtils.BreakCurve(document, afterA.Id, bb);
                var pipe3 = (Pipe)document.GetElement(id3);
                var startsAtA = ((LocationCurve)afterA.Location).Curve.GetEndPoint(0).IsAlmostEqualTo(a) || ((LocationCurve)afterA.Location).Curve.GetEndPoint(1).IsAlmostEqualTo(a);
                middle = startsAtA ? afterA : pipe3;
                tail = startsAtA ? pipe3 : afterA;
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
            result.Summary = $"Đã kick ống: 3 đoạn ({RevitCompat.IdValue(beforeA.Id)}, {RevitCompat.IdValue(middle.Id)}, {RevitCompat.IdValue(tail.Id)}), {made}/2 cút dựng được.";
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
