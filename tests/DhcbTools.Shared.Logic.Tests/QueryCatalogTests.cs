using System.Text.RegularExpressions;
using Xunit;

namespace DhcbTools.Shared.Logic.Tests;

/// <summary>
/// Danh sách truy vấn phải khớp nhau ở <b>ba</b> chỗ: bảng dispatch của handler, phần tài liệu trong
/// <c>QueryRequest</c>, và câu báo lỗi "Hợp lệ: …" mà agent đọc khi gõ sai tên.
/// <para>
/// Vì sao: thêm một truy vấn mà quên cập nhật câu báo lỗi thì agent gõ sai sẽ nhận về một danh sách
/// thiếu — nó không có cách nào biết truy vấn đó tồn tại. Đây đúng là chuyện vừa xảy ra khi giai đoạn
/// 10.1 thêm <c>entity_geometry</c>/<c>attributes_of</c> vào phía AutoCAD.
/// </para>
/// </summary>
public class QueryCatalogTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Dhcb-Tools.sln")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName ?? throw new InvalidOperationException("Không tìm thấy gốc repo.");
    }

    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine(RepoRoot(), Path.Combine(parts)));

    /// <summary>Tên truy vấn trong bảng dispatch: <c>"ENTITY_GEOMETRY" =&gt;</c>.</summary>
    private static HashSet<string> Dispatched(string source) =>
        new(Regex.Matches(source, @"""([A-Z_]{4,})""\s*=>")
                .Select(m => m.Groups[1].Value.ToLowerInvariant()),
            StringComparer.Ordinal);

    /// <summary>
    /// Tên trong hằng <c>ValidQueries</c> — danh sách mà agent đọc khi gõ sai tên truy vấn.
    /// <para>
    /// Mẫu chỉ dùng <c>\s*</c> để nhảy qua chỗ xuống dòng giữa <c>=</c> và chuỗi. Bản trước nhúng
    /// <b>ký tự xuống dòng thật</b> vào chuỗi verbatim, nên mẫu đòi đúng byte CR/LF của file: xanh trên
    /// CI Linux (checkout LF) mà đỏ trên cây làm việc Windows (CRLF) — cùng một commit, hai kết quả.
    /// </para>
    /// </summary>
    private static HashSet<string> Advertised(string source)
    {
        var m = Regex.Match(source, @"ValidQueries\s*=\s*""([^""]+)""");
        Assert.True(m.Success, "Không tìm thấy hằng ValidQueries trong handler.");
        return new HashSet<string>(
            m.Groups[1].Value.Split(',').Select(x => x.Trim()).Where(x => x.Length > 0),
            StringComparer.Ordinal);
    }

    [Fact]
    public void AutoCad_BangDispatch_VaCauBaoLoi_KhopNhau()
    {
        var source = Read("src", "DhcbTools.Core.AutoCAD", "Query", "AcadQueryHandler.cs");
        var dispatched = Dispatched(source);
        var advertised = Advertised(source);

        Assert.Contains("entity_geometry", dispatched);
        Assert.Contains("attributes_of", dispatched);
        Assert.Contains("snapshot", dispatched);
        Assert.True(dispatched.SetEquals(advertised),
            "Lệch giữa bảng dispatch và câu \"Hợp lệ\": chỉ dispatch " +
            string.Join(", ", dispatched.Except(advertised)) + " · chỉ quảng cáo " +
            string.Join(", ", advertised.Except(dispatched)));
    }

    [Fact]
    public void AutoCad_MoiTruyVanDispatch_DeuCoTrongTaiLieuQueryRequest()
    {
        var dispatched = Dispatched(Read("src", "DhcbTools.Core.AutoCAD", "Query", "AcadQueryHandler.cs"));
        var doc = Read("src", "DhcbTools.Core.AutoCAD", "Query", "QueryRequest.cs");

        var thieu = dispatched.Where(q => !doc.Contains(q, StringComparison.Ordinal)).ToList();
        Assert.True(thieu.Count == 0, "Truy vấn AutoCAD thiếu trong tài liệu QueryRequest: " + string.Join(", ", thieu));
    }

    [Fact]
    public void AutoCad_TruyVanCanEditor_NamOVo_VaDuocGhiTrongTaiLieu()
    {
        var vo = Read("src", "DhcbTools.AutoCAD", "Bridge", "AcadUiQueryHandler.cs");
        var doc = Read("src", "DhcbTools.Core.AutoCAD", "Query", "QueryRequest.cs");

        foreach (var query in new[] { "selection", "show_entities", "active_layout", "snapshot" })
        {
            Assert.Contains(query.ToUpperInvariant(), vo);
            Assert.Contains(query, doc);
        }

        // Core không được biết tới Editor: đó là điều kiện để lệnh còn chạy được trong accoreconsole.
        var core = Read("src", "DhcbTools.Core.AutoCAD", "Query", "AcadQueryHandler.cs");
        Assert.DoesNotContain("EditorInput", core);
    }

    [Fact]
    public void Revit_MoiTruyVanDispatch_DeuCoTrongTaiLieuQueryRequest()
    {
        var dispatched = new HashSet<string>(
            Regex.Matches(Read("src", "DhcbTools.Core", "Query", "RevitQueryHandler.cs"), @"""([a-z_]{4,})""\s*=>")
                 .Select(m => m.Groups[1].Value),
            StringComparer.Ordinal);
        var doc = Read("src", "DhcbTools.Core", "Query", "QueryRequest.cs");

        var thieu = dispatched.Where(q => !doc.Contains(q, StringComparison.Ordinal)).ToList();
        Assert.True(thieu.Count == 0, "Truy vấn Revit thiếu trong tài liệu QueryRequest: " + string.Join(", ", thieu));
    }
}
