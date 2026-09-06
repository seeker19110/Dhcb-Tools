using DhcbTools.Shared.Logic.Bcf;
using DhcbTools.Shared.Logic.Checks;
using Xunit;

namespace DhcbTools.Shared.Logic.Tests;

/// <summary>
/// Khoá, Summary, HTML, BCF và mọi câu giải thích của <c>ClashDetection</c> — trước đây trong Core, chỉ
/// kiểm được khi mở Revit. "0 va chạm" là kết luận người ta tin và làm theo nên từng câu có assert.
/// </summary>
public class ClashReportTests
{
    private static ClashRecord Rec(long a = 1, long b = 2, string? link = null, string key = "k1")
        => new(a, "Ducts", "Duct 300x200", b, "Structural Framing", 1000.4, 2000.6, 3000, key, link);

    [Fact]
    public void ClashRecord_KiemNullVaMoTaB()
    {
        Assert.Throws<ArgumentNullException>(() => new ClashRecord(1, null, null, 2, null, 0, 0, 0, null!, null));
        var r = new ClashRecord(1, null, null, 2, null, 0, 0, 0, "k", null);
        Assert.Equal(string.Empty, r.CategoryA);
        Assert.Equal("2", r.DescribeB());
        Assert.False(r.FromLink);
        Assert.Equal("7 (link \"STR\")", Rec(b: 7, link: "STR").DescribeB());
        Assert.True(Rec(link: "STR").FromLink);
    }

    [Fact]
    public void IntersectionCentre_TamPhanGiao()
    {
        var c = ClashReport.IntersectionCentre(0, 0, 0, 10, 10, 10, 6, -5, 8, 20, 5, 30);

        Assert.Equal((8.0, 2.5, 9.0), c);
    }

    [Fact]
    public void MakeKey_LinkThemHauTo_KhongLinkGiuNguyenKhoaChapNhan()
    {
        var plain = ClashReport.MakeKey(1, 2, 10, 20, 30, null);

        Assert.Equal(ClashAcceptance.MakeKey(1, 2, 10, 20, 30), plain);
        Assert.Equal(plain + "#link99", ClashReport.MakeKey(1, 2, 10, 20, 30, 99));
    }

    [Fact]
    public void LinkNameMatches_CungLuatVoiSleeve()
    {
        Assert.True(ClashReport.LinkNameMatches("STR-L01", null));
        Assert.True(ClashReport.LinkNameMatches("STR-L01", new[] { "str" }));
        Assert.False(ClashReport.LinkNameMatches("ARC", new[] { "str" }));
    }

    [Fact]
    public void ThongBaoDon()
    {
        Assert.Equal("Một trong hai nhóm category không có trong mô hình: A, B", ClashReport.UnknownCategoriesError(new[] { "A", "B" }));
        Assert.EndsWith(": ", ClashReport.UnknownCategoriesError(null!));
        Assert.Equal("Đạt giới hạn 5 va chạm — dừng quét.", ClashReport.MaxResultsNote(5));
        Assert.Equal("Va chạm 1 (Ducts) × 2 (Structural Framing) tại (1000,2001,3000) mm  key=k1", ClashReport.ClashLine(Rec()));
    }

    [Fact]
    public void Notes_ToiDa500DongVaChamRoiNguonNhomB()
    {
        var many = Enumerable.Range(0, 600).Select(i => Rec(key: "k" + i)).ToList();

        var notes = ClashReport.Notes(many, 12, 340, new[] { "STR: 340 phần tử nhóm B" });

        Assert.Equal(502, notes.Count);
        Assert.Equal("Nhóm B xét tới: 12 phần tử trong file, 340 từ model liên kết.", notes[500]);
        Assert.Equal("  Link — STR: 340 phần tử nhóm B", notes[501]);
        Assert.Single(ClashReport.Notes(null!, 0, 0, null));
    }

    [Fact]
    public void Summary_KhongVaCham_PhaiKemCoSo()
    {
        Assert.Equal(
            "Tìm thấy 0 va chạm (3 đã chấp nhận, bỏ qua) → \"x.html\". Đã xét 50 × (10 trong file + 20 từ model liên kết).",
            ClashReport.Summary(new List<ClashRecord>(), 3, "x.html", 50, 10, 20, true));
        Assert.Contains("kiểm lại link đã nạp chưa", ClashReport.Summary(null!, 0, "x", 5, 0, 0, true));
        Assert.Contains("includeLinkedModels đang tắt", ClashReport.Summary(null!, 0, "x", 5, 0, 0, false));
    }

    [Fact]
    public void Summary_CoVaCham_DemPhanVoiLink()
    {
        var clashes = new[] { Rec(), Rec(link: "STR", key: "k2"), Rec(link: "STR", key: "k3") };

        Assert.Equal("Tìm thấy 3 va chạm (0 đã chấp nhận, bỏ qua) → \"x\". Trong đó 2 va chạm với model liên kết.",
            ClashReport.Summary(clashes, 0, "x", 5, 1, 1, true));
        Assert.Equal("Tìm thấy 1 va chạm (0 đã chấp nhận, bỏ qua) → \"x\".",
            ClashReport.Summary(new[] { Rec() }, 0, "x", 5, 1, 0, true));
    }

    [Fact]
    public void ViewNote_XemTruocVaDaTao_CoKhongLink()
    {
        Assert.Equal("[Xem trước] Sẽ tạo/ghi đè 3D view \"V\" và isolate 4 phần tử phía file chủ.", ClashReport.ViewNote(true, "V", 4, 0));
        Assert.Equal("[Xem trước] Sẽ tạo/ghi đè 3D view \"V\" và isolate 4 phần tử phía file chủ (2 phần tử phía link không isolate được, xem danh sách).", ClashReport.ViewNote(true, "V", 4, 2));
        Assert.Equal("Đã tạo 3D view \"V\" isolate 4 phần tử phía file chủ.", ClashReport.ViewNote(false, "V", 4, 0));
        Assert.Equal("Đã tạo 3D view \"V\" isolate 4 phần tử phía file chủ; 2 phần tử phía link không isolate được (khác document).", ClashReport.ViewNote(false, "V", 4, 2));
    }

    [Fact]
    public void Html_TieuDeEscape_BangDungCotVaKey()
    {
        var html = ClashReport.Html("A<B", new[] { "Ducts" }, new[] { "Walls", "Floors" }, new[] { Rec(key: "k<1>") }, 2);

        Assert.Contains("<h1>Va chạm nội bộ — A&lt;B</h1>", html);
        Assert.Contains("<p>Ducts × Walls, Floors: <b>1</b> va chạm; 2 đã chấp nhận (clash-accepted.json).</p>", html);
        Assert.Contains("<tr><td>1</td><td>1</td><td>Ducts</td><td>2</td><td>Structural Framing</td><td>1000</td><td>2001</td><td>3000</td><td><code>k&lt;1&gt;</code></td></tr>", html);
        Assert.Contains("<tbody></tbody>", ClashReport.Html(null, null!, null!, null!, 0));
    }

    [Fact]
    public void BcfIssues_MetVaGuidTuKey_NhanTheoNguon()
    {
        var issues = ClashReport.BcfIssues("Dự án", new[] { Rec(), Rec(b: 9, link: "STR", key: "k2") });

        Assert.Equal(2, issues.Count);
        var first = issues[0];
        Assert.Equal(BcfWriter.GuidFromKey("k1"), first.Guid);
        Assert.Equal("Ducts × Structural Framing — Duct 300x200", first.Title);
        Assert.Equal("Va chạm 1 (Ducts) × 2 (Structural Framing) tại (1000, 2001, 3000) mm. key=k1", first.Description);
        Assert.Equal(1.0004, first.Target!.X, 9);
        Assert.Equal(3.0, first.Target.Z, 9);
        Assert.Equal("Trong file", first.Labels[0]);
        Assert.Equal(new[] { "Dự án", "Dự án" }, first.Components.Select(c => c.OriginatingSystem));

        var second = issues[1];
        Assert.Equal("Với model liên kết", second.Labels[0]);
        Assert.Equal("STR", second.Components[1].OriginatingSystem);
        Assert.Contains("9 (link \"STR\")", second.Description);
        Assert.Empty(ClashReport.BcfIssues(null, null!));
    }

    [Fact]
    public void BcfProjectName_RongThiLayTenFile()
    {
        Assert.Equal("Cấu hình", ClashReport.BcfProjectName("Cấu hình", "file"));
        Assert.Equal("file", ClashReport.BcfProjectName("  ", "file"));
        Assert.Equal(string.Empty, ClashReport.BcfProjectName(null, null));
    }
}
