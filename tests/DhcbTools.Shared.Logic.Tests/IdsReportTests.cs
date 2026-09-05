using System.Linq;
using DhcbTools.Shared.Logic.Ids;
using Xunit;

namespace DhcbTools.Shared.Logic.Tests;

/// <summary>Báo cáo IDS dùng chung cho đường Revit và đường IFC: summary, messages, CSV, HTML cùng nói một điều.</summary>
public class IdsReportTests
{
    private static IdsCheckResult Check()
    {
        var specs = IdsSpec.Parse(
            "<ids><specifications>"
            + "<specification name=\"Cửa có Tag\" description=\"theo BEP\"><applicability><entity><name><simpleValue>IfcDoor</simpleValue></name></entity></applicability>"
            + "<requirements><attribute><name><simpleValue>Tag</simpleValue></name></attribute></requirements></specification>"
            + "<specification name=\"Bể\"><applicability><entity><name><simpleValue>IfcTank</simpleValue></name></entity></applicability>"
            + "<requirements><attribute><name><simpleValue>Name</simpleValue></name></attribute></requirements></specification>"
            + "</specifications></ids>");
        var ok = new FakeIdsElement { Label = "1 — Doors \"D1\"" };
        ok.Attributes["Tag"] = "D-01";
        var bad = new FakeIdsElement { Label = "2 — Doors \"D2\"" };
        return IdsEvaluator.Check(specs, new IIdsElement[] { ok, bad });
    }

    private static readonly string[] Warnings = { "dòng 3: <restriction> phải thuộc namespace XML Schema" };

    [Fact]
    public void Summary_DemDatTruotRongVaCanhBao()
    {
        var text = IdsReport.Summary(Check(), Warnings);
        Assert.Contains("Kiểm 2 phần tử theo 2 specification: 1 phần tử không đạt ở 1 specification", text);
        Assert.Contains("1 specification không có phần tử nào để kiểm", text);
        Assert.Contains("file IDS lệch chuẩn ở 1 chỗ", text);
        Assert.DoesNotContain("lệch chuẩn", IdsReport.Summary(Check(), new string[0]));
    }

    [Fact]
    public void Messages_CanhBaoTruoc_RoiTungSpec_RoiPhanTuTruot()
    {
        var lines = IdsReport.Messages(Check(), Warnings).ToList();
        Assert.StartsWith("⚠ File IDS lệch chuẩn IDS 1.0 ở 1 chỗ", lines[0]);
        Assert.Equal("   • " + Warnings[0], lines[1]);
        Assert.Equal("Cửa có Tag: 1/2 đạt, 1 phần tử không đạt", lines[2]);
        Assert.StartsWith("Bể: KHÔNG phần tử nào lọt bộ lọc", lines[3]);
        Assert.StartsWith("Cửa có Tag — 2 — Doors \"D2\": thiếu/sai", lines[4]);
        Assert.Equal(5, lines.Count);
    }

    [Fact]
    public void Csv_MotDongMoiPhanTuTruot()
    {
        var csv = IdsReport.Csv(Check());
        var rows = csv.Split(new[] { "\r\n" }, System.StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, rows.Length);
        Assert.StartsWith("Specification,", rows[0]);
        Assert.Contains("Cửa có Tag", rows[1]);
        Assert.Contains("D2", rows[1]);
    }

    [Fact]
    public void Html_CoCanhBao_MoTa_RanhGioi_VaBangTruot()
    {
        var html = IdsReport.Html("Snowdon.ifc", "C:/yeu-cau.ids", IdsReport.IfcScopeNote, Check(), Warnings);
        Assert.Contains("File IDS lệch chuẩn IDS 1.0", html);
        Assert.Contains("&lt;restriction&gt;", html);
        Assert.Contains("<small>theo BEP</small>", html);
        Assert.Contains("trên chính file IFC", html);
        Assert.Contains("0 phần tử — không kiểm được gì", html);
        Assert.Contains("<h2>Cửa có Tag</h2>", html);
        Assert.DoesNotContain("<h2>Bể</h2>", html);
        Assert.DoesNotContain("Danh sách cắt", html);

        var revit = IdsReport.Html("Model", "x.ids", IdsReport.RevitScopeNote, Check(), new string[0]);
        Assert.DoesNotContain("lệch chuẩn", revit);
        Assert.Contains("sẽ đạt khi xuất", revit);
    }
}
