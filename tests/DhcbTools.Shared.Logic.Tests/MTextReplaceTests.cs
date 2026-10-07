using System.Text.RegularExpressions;
using DhcbTools.Shared.Logic.Cad;
using Xunit;

namespace DhcbTools.Shared.Logic.Tests;

/// <summary>
/// <c>TextReplace</c> trên MText: chỉ thay chữ hiển thị, không bao giờ sửa vào mã định dạng (để lại từ audit 2026-09-23).
/// Mỗi ca "Ban cu" chạy cách cũ — thay thẳng trên <c>Contents</c> — để thấy đúng cái hỏng mà bản mới tránh.
/// </summary>
public class MTextReplaceTests
{
    private static Regex Rx(string pattern, RegexOptions options = RegexOptions.None) => new(pattern, options);

    [Fact]
    public void ChuoiThuong_ThayTrongChu()
    {
        var r = MTextReplace.ReplaceAll("Phòng P-101", "P-1", "Q-2", ignoreCase: false);

        Assert.Equal("Phòng Q-201", r.Contents);
        Assert.Equal(1, r.Replaced);
        Assert.Equal(0, r.Skipped);
    }

    /// <summary>"PL" chỉ có trong mã xuống dòng + chữ: bản cũ biến <c>\PLine</c> thành <c>\XXine</c>.</summary>
    [Fact]
    public void KhongThayVaoMaXuongDong()
    {
        const string contents = "Ghi chú\\PLine 2";
        Assert.Equal("Ghi chú\\XXine 2", Shared.Logic.TextReplace.ReplaceAll(contents, "PL", "XX", false)); // bản cũ

        var r = MTextReplace.ReplaceAll(contents, "PL", "XX", ignoreCase: false);

        Assert.Equal(contents, r.Contents);
        Assert.Equal(0, r.Replaced);
    }

    /// <summary>Regex <c>\d</c>: bản cũ đổi luôn chữ số trong mã chiều cao <c>\H2.5x;</c> — chữ đổi cỡ.</summary>
    [Fact]
    public void RegexSo_KhongDungMaChieuCao()
    {
        const string contents = "{\\H2.5x;Cao 3.5m}";
        Assert.Equal("{\\H#.#x;Cao #.#m}", Rx(@"\d").Replace(contents, "#")); // bản cũ

        var r = MTextReplace.Replace(contents, Rx(@"\d"), "#");

        Assert.Equal("{\\H2.5x;Cao #.#m}", r.Contents);
        Assert.Equal(2, r.Replaced);
    }

    [Fact]
    public void TenFontTrongMa_KhongBiThay_ChuCungTenThiThay()
    {
        var r = MTextReplace.ReplaceAll("{\\fArial|b1|i0;Arial}", "arial", "Times", ignoreCase: true);

        Assert.Equal("{\\fArial|b1|i0;Times}", r.Contents);
    }

    /// <summary>Chuỗi vắt qua mã định dạng (một chữ in đậm giữa từ): không thay, đếm để báo kỹ sư sửa tay.</summary>
    [Fact]
    public void KhopVatQuaMa_KhongThay_DemSkipped()
    {
        const string contents = "A{\\fArial|b1;B}C và ABC";

        var r = MTextReplace.ReplaceAll(contents, "ABC", "X", ignoreCase: false);

        Assert.Equal("A{\\fArial|b1;B}C và X", r.Contents);
        Assert.Equal(1, r.Replaced);
        Assert.Equal(1, r.Skipped);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IdentityAcrossFormatting_IsNotIncompleteWork(bool regex)
    {
        const string contents = "DH{\\C1;CB} và DHCB";
        var result = regex
            ? MTextReplace.Replace(contents, Rx("DHCB"), "$0")
            : MTextReplace.ReplaceAll(contents, "DHCB", "DHCB", false);
        Assert.Equal(contents, result.Contents);
        Assert.Equal(0, result.Replaced);
        Assert.Equal(0, result.Skipped);
    }

    [Fact]
    public void IdentityMatchKeepsFormattingWhileOtherMatchesChange()
    {
        const string contents = "A{\\fArial|b1;B}C và X";
        var result = MTextReplace.Replace(contents, Rx("ABC|X"), "ABC");
        Assert.Equal("A{\\fArial|b1;B}C và ABC", result.Contents);
        Assert.Equal(1, result.Replaced);
        Assert.Equal(0, result.Skipped);
    }

    [Fact]
    public void CaseInsensitiveSearchStillReportsBlockedCaseChange()
    {
        const string contents = "A{\\C1;B}C";
        var result = MTextReplace.ReplaceAll(contents, "abc", "abc", true);
        Assert.Equal(contents, result.Contents);
        Assert.Equal(0, result.Replaced);
        Assert.Equal(1, result.Skipped);
        Assert.Equal(0, MTextReplace.ReplaceAll(contents, "abc", "ABC", true).Skipped);
    }

    [Fact]
    public void IdentityOfEscapedTextAndEmptyInsertionPreservesRawContents()
    {
        const string contents = "30\\U+00B0C\\PA";
        var result = MTextReplace.Replace(contents, Rx("30°C|\\n"), "$0");
        Assert.Equal(contents, result.Contents);
        Assert.Equal(0, result.Replaced);
        Assert.Equal(0, result.Skipped);
        var insertion = MTextReplace.Replace(contents, Rx("^|$"), "");
        Assert.Equal(contents, insertion.Contents);
        Assert.Equal(0, insertion.Replaced);
        Assert.Equal(0, insertion.Skipped);
    }

    /// <summary>Khớp có chứa xuống dòng (do <c>\P</c> sinh ra) cũng không thay — không được xoá mã xuống dòng.</summary>
    [Fact]
    public void KhopChuaXuongDong_KhongThay()
    {
        var r = MTextReplace.Replace("A  \\PB  C", Rx(@"\s+"), " ");

        Assert.Equal("A  \\PB C", r.Contents);
        Assert.Equal(1, r.Replaced);
        Assert.Equal(1, r.Skipped);
    }

    [Fact]
    public void Regex_NhieuDong_NeoTheoDoan_VaThamChieuNhom()
    {
        var r = MTextReplace.Replace("A1\\PB2\\NC3", Rx(@"^(\w)(\d)$", RegexOptions.Multiline), "$2$1");

        Assert.Equal("1A\\P2B\\N3C", r.Contents);
        Assert.Equal(3, r.Replaced);
    }

    /// <summary>Khớp rỗng (^): chèn ngay trước chữ, sau mã định dạng — chữ chèn mang định dạng của chữ sau.</summary>
    [Fact]
    public void KhopRong_ChenSauMaTruocChu()
    {
        Assert.Equal("{\\fArial;* Hi}", MTextReplace.Replace("{\\fArial;Hi}", Rx("^"), "* ").Contents);
        Assert.Equal("{\\fArial;Hi!}", MTextReplace.Replace("{\\fArial;Hi}", Rx("$"), "!").Contents);
        Assert.Equal("{\\H2x;}x", MTextReplace.Replace("{\\H2x;}", Rx("^"), "x").Contents); // không có chữ: chèn cuối
    }

    /// <summary>Chuỗi thay là CHỮ: <c>\</c>, <c>{</c>, <c>}</c> được escape; xuống dòng thành <c>\P</c>.</summary>
    [Fact]
    public void ChuoiThay_DuocEscape_XuongDongThanhP()
    {
        Assert.Equal("C:\\\\Data", MTextReplace.ReplaceAll("C:\\\\Temp", "\\Temp", "\\Data", false).Contents);
        Assert.Equal("\\{A\\}", MTextReplace.ReplaceAll("x", "x", "{A}", false).Contents);
        Assert.Equal("A\\PB\\PC", MTextReplace.ReplaceAll("A|B|C", "|", "\n", false).Contents);
        Assert.Equal("A\\PB", MTextReplace.ReplaceAll("A|B", "|", "\r\n", false).Contents);
        Assert.Equal("A\\PB", MTextReplace.ReplaceAll("A|B", "|", "\r", false).Contents);
    }

    /// <summary>Ký tự escape là chữ thật: <c>\\</c>, <c>\{</c>, <c>\~</c>, <c>\U+XXXX</c> khớp và thay được.</summary>
    [Fact]
    public void KyTuEscape_LaChu()
    {
        Assert.Equal("45°C", MTextReplace.ReplaceAll("30\\U+00B0C", "30°", "45°", false).Contents);
        Assert.Equal("A B", MTextReplace.ReplaceAll("A\\~B", "\u00A0", " ", false).Contents);
        Assert.Equal("[x]", MTextReplace.ReplaceAll("\\{x\\}", "{x}", "[x]", false).Contents);
        Assert.Equal("a/b", MTextReplace.ReplaceAll("a\\\\b", "\\", "/", false).Contents);
    }

    [Fact]
    public void ChuHienThi_TachMaDungBang()
    {
        Assert.Equal(
            "Tầng 1\nTên\u00A0phòng {A}\\\u00B0",
            MTextReplace.DisplayText("\\A1;{\\C1;\\fArial|b1;Tầng\\H2x; 1}\\P\\LTên\\l\\~phòng \\{A\\}\\\\\\U+00B0"));
        Assert.Equal("x\uFFFCy", MTextReplace.DisplayText("x\\S1/2;y"));        // phân số xếp chồng: ký tự giữ chỗ
        Assert.Equal("x\uFFFCy", MTextReplace.DisplayText("x\\M+1A2B3y"));      // ký tự đa byte font shape
        Assert.Equal("ab", MTextReplace.DisplayText("a\\zb"));                   // mã lạ: không hiển thị
        Assert.Equal("a\\", MTextReplace.DisplayText("a\\"));                    // gạch chéo lẻ cuối chuỗi là chữ
        Assert.Equal("a", MTextReplace.DisplayText("a\\fArial"));                // mã thiếu ';' kéo tới hết chuỗi
        Assert.Equal("a\uFFFC", MTextReplace.DisplayText("a\\S1/2"));          // phân số thiếu ';' vẫn là phân số
        Assert.Equal("a+ZZZZ", MTextReplace.DisplayText("a\\U+ZZZZ"));         // \U+ sai hex: \U là mã lạ
        Assert.Equal("a+1", MTextReplace.DisplayText("a\\M+1"));               // \M+ thiếu ký tự: mã lạ
        Assert.Equal(string.Empty, MTextReplace.DisplayText(null));
    }

    [Fact]
    public void PhanSoXepChong_KhongKhopChuThuong()
    {
        var r = MTextReplace.ReplaceAll("x\\S1/2;y", "1/2", "½", false);

        Assert.Equal("x\\S1/2;y", r.Contents);
        Assert.Equal(0, r.Replaced);
        Assert.Equal(0, r.Skipped);
    }

    [Fact]
    public void DauVaoRong()
    {
        Assert.Equal(string.Empty, MTextReplace.ReplaceAll(null, "a", "b", false).Contents);
        Assert.Equal("abc", MTextReplace.ReplaceAll("abc", "", "b", false).Contents);
        Assert.Equal("abc", MTextReplace.ReplaceAll("abc", null, "b", false).Contents);
        Assert.Equal("ac", MTextReplace.ReplaceAll("abc", "b", null, false).Contents);
        Assert.Equal("ac", MTextReplace.Replace("abc", Rx("b"), null).Contents);
        Assert.Equal(string.Empty, MTextReplace.Replace(null, Rx("b"), "x").Contents);
        Assert.Throws<ArgumentNullException>(() => MTextReplace.Replace("abc", null!, "x"));
    }

    [Fact]
    public void KhongPhanBietHoaThuong()
    {
        Assert.Equal("X-1\\PX-2", MTextReplace.ReplaceAll("p-1\\PP-2", "p-", "X-", ignoreCase: true).Contents);
    }

    /// <summary>Regex quá trần thời gian: ném ra cho lệnh gọi (lệnh giữ nguyên đối tượng và báo).</summary>
    [Fact]
    public void RegexQuaGio_NemChoLenhGoi()
    {
        var slow = new Regex("(a+)+$", RegexOptions.None, TimeSpan.FromMilliseconds(1));
        Assert.Throws<RegexMatchTimeoutException>(
            () => MTextReplace.Replace(new string('a', 5000) + "!", slow, "x"));
    }
}
