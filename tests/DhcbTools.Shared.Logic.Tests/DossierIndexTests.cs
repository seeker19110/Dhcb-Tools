using System;
using System.Collections.Generic;
using System.Linq;
using DhcbTools.Shared.Logic.AsBuilt;
using Xunit;

namespace DhcbTools.Shared.Logic.Tests;

/// <summary>
/// Mục 11.6 — đối chiếu danh mục hồ sơ với file thật. Hai ràng buộc phải có test: <b>thiếu mục bắt buộc
/// thì phải nói ra</b> (đây là thứ quyết định hồ sơ nộp được hay không), và <b>file thừa không bị giấu</b>
/// (người ký cần biết thư mục còn những gì).
/// </summary>
public class DossierIndexTests
{
    private static DossierSpec Spec(params DossierItem[] items) => new DossierSpec
    {
        Title = "Danh mục thử",
        LegalBasis = "Điều 28 NĐ 207/2026",
        Project = "Toà A",
        Groups = new List<DossierGroup>
        {
            new DossierGroup { Code = "I", Name = "Chuẩn bị đầu tư", Items = items.ToList() },
        },
    };

    private static DossierItem Item(string code, string name, bool required, params string[] patterns) =>
        new DossierItem { Code = code, Name = name, Required = required, Patterns = patterns.ToList() };

    [Fact]
    public void CoFileThiDat_ThieuMucBatBuocThiBaoThieu()
    {
        var spec = Spec(
            Item("I.1", "Hợp đồng", true, "*hop-dong*"),
            Item("I.2", "Giấy phép", true, "*giay-phep*"),
            Item("I.3", "Ảnh hiện trạng", false, "*anh*"));

        var result = DossierIndex.Check(spec, new[] { "phap-ly/hop-dong-01.pdf", "phap-ly/hop-dong-02.pdf" });

        Assert.Equal(3, result.Items.Count);
        Assert.Equal(1, result.Found);
        Assert.Equal(1, result.MissingRequired);
        Assert.False(result.Ok);

        var hopDong = result.Items[0];
        Assert.True(hopDong.Found);
        Assert.Equal(2, hopDong.Files.Count);
        Assert.False(hopDong.MissingRequired);

        Assert.True(result.Items[1].MissingRequired);
        // Không bắt buộc mà thiếu thì KHÔNG tính là thiếu — nếu không, mọi hồ sơ đều đỏ vì mục tuỳ chọn.
        Assert.False(result.Items[2].Found);
        Assert.False(result.Items[2].MissingRequired);
    }

    [Fact]
    public void DuHetMucBatBuoc_ThiOk()
    {
        var result = DossierIndex.Check(Spec(Item("I.1", "Hợp đồng", true, "*hop-dong*")), new[] { "hop-dong.pdf" });

        Assert.True(result.Ok);
        Assert.Equal(0, result.MissingRequired);
        Assert.Contains("0 mục bắt buộc còn THIẾU", DossierIndex.Summary(result));
    }

    /// <summary>File có trong thư mục mà không mục nào nhận: phải liệt kê, không được im lặng.</summary>
    [Fact]
    public void FileThua_DuocLietKeRieng()
    {
        var result = DossierIndex.Check(
            Spec(Item("I.1", "Hợp đồng", true, "*hop-dong*")),
            new[] { "hop-dong.pdf", "nhap/ban-nhap.docx", "thumbs.db" });

        Assert.Equal(new[] { "nhap/ban-nhap.docx", "thumbs.db" }, result.Unmatched);
        Assert.Contains("2 file chưa xếp vào mục nào", DossierIndex.Summary(result));
    }

    [Fact]
    public void KhongCoFileThua_ThiSummaryKhongNhacToi()
    {
        var result = DossierIndex.Check(Spec(Item("I.1", "Hợp đồng", true, "*hop-dong*")), new[] { "hop-dong.pdf" });

        Assert.Empty(result.Unmatched);
        Assert.DoesNotContain("chưa xếp", DossierIndex.Summary(result));
    }

    /// <summary>Một file thoả hai mục thì cả hai đều nhận nó — không "ai lấy trước người đó được".</summary>
    [Fact]
    public void MotFileThoaHaiMuc_CaHaiDeuNhan()
    {
        var spec = Spec(
            Item("I.1", "Mọi PDF", true, "*.pdf"),
            Item("I.2", "Hợp đồng", true, "*hop-dong*"));

        var result = DossierIndex.Check(spec, new[] { "hop-dong.pdf" });

        Assert.All(result.Items, i => Assert.True(i.Found));
        Assert.Empty(result.Unmatched);
    }

    /// <summary>
    /// Tên file thật đặt cả có dấu lẫn không dấu, cả gạch ngang lẫn khoảng trắng. Một mẫu phải bắt được
    /// cả hai, nếu không người khai phải viết hai mẫu cho mỗi mục và chắc chắn sẽ quên một.
    /// </summary>
    [Theory]
    [InlineData("bien-ban-nghiem-thu.pdf", "*nghiem-thu*", true)]
    [InlineData("Biên bản nghiệm-thu.pdf", "*nghiem-thu*", true)]
    [InlineData("BIEN-BAN-NGHIEM-THU.PDF", "*nghiem-thu*", true)]
    [InlineData("ho-so/nghiem-thu/bb01.pdf", "nghiem-thu/*", true)]
    [InlineData("ho-so/nghiem-thu/bb01.pdf", "*.pdf", true)]
    [InlineData("bb01.dwg", "*.pdf", false)]
    [InlineData("bb1.pdf", "bb?.pdf", true)]
    [InlineData("bb12.pdf", "bb?.pdf", false)]
    [InlineData("Báo cáo khảo sát địa chất.pdf", "*khao-sat*", true)]
    [InlineData("bien ban nghiem thu.pdf", "*nghiem-thu*", true)]
    [InlineData("bien_ban_nghiem_thu.pdf", "*nghiem-thu*", true)]
    [InlineData("ho so/nghiem thu/bb01.pdf", "nghiem-thu/*", true)]
    [InlineData("bat-ky.pdf", "", false)]
    public void KhopMau_BoDauVaKhongPhanBietHoaThuong(string path, string pattern, bool expected)
    {
        Assert.Equal(expected, DossierIndex.Matches(path, pattern));
    }

    [Fact]
    public void DuongDanNguocVaTrungLap_DuocChuanHoa()
    {
        var result = DossierIndex.Check(
            Spec(Item("I.1", "Hợp đồng", true, "*hop-dong*")),
            new[] { "/phap-ly\\hop-dong.pdf", "phap-ly/hop-dong.pdf", "  ", null! });

        Assert.Equal(new[] { "phap-ly/hop-dong.pdf" }, Assert.Single(result.Items).Files);
    }

    [Fact]
    public void MucKhongKhaiMauNao_LuonBaoThieu()
    {
        // Cố ý: mục chưa khai patterns thì KHÔNG file nào nhận nó — thà báo thiếu còn hơn nhận bừa
        // file đầu tiên gặp được rồi in ra một dấu "đã có" mà không ai kiểm lại.
        var result = DossierIndex.Check(Spec(Item("I.1", "Chưa khai mẫu", true)), new[] { "hop-dong.pdf" });

        Assert.True(Assert.Single(result.Items).MissingRequired);
        Assert.Single(result.Unmatched);
    }

    [Fact]
    public void KhongCoFileNao_ThiMoiMucBatBuocDeuThieu()
    {
        var result = DossierIndex.Check(Spec(Item("I.1", "Hợp đồng", true, "*hop-dong*")), null!);

        Assert.Equal(1, result.MissingRequired);
        Assert.Empty(result.Unmatched);
    }

    [Fact]
    public void SpecNull_ThiNem()
    {
        Assert.Throws<ArgumentNullException>(() => DossierIndex.Check(null!, new[] { "a.pdf" }));
    }

    [Fact]
    public void DocJson_VaTuChoiFileHong()
    {
        var spec = DossierSpec.FromJson(
            "{\"title\":\"T\",\"legalBasis\":\"L\",\"project\":\"P\",\"groups\":[{\"code\":\"I\",\"name\":\"N\","
            + "\"items\":[{\"code\":\"I.1\",\"name\":\"Hợp đồng\",\"patterns\":[\"*hop-dong*\"],\"note\":\"G\"}]}]}");

        Assert.Equal("T", spec.Title);
        var item = Assert.Single(Assert.Single(spec.Groups).Items);
        Assert.Equal("Hợp đồng", item.Name);
        // required mặc định TRUE: khai thiếu trường này thì mục vẫn được coi là bắt buộc, không lọt êm.
        Assert.True(item.Required);
        Assert.Equal("G", item.Note);

        Assert.Contains("không phải JSON hợp lệ", Assert.Throws<ArgumentException>(() => DossierSpec.FromJson("{")).Message);
        Assert.Contains("rỗng hoặc không phải JSON object", Assert.Throws<ArgumentException>(() => DossierSpec.FromJson("null")).Message);
        // Danh mục rỗng thì mọi thư mục đều "đủ hồ sơ" — từ chối thay vì in tờ giấy trắng.
        Assert.Contains("không có mục nào", Assert.Throws<ArgumentException>(() => DossierSpec.FromJson("{\"groups\":[]}")).Message);
    }

    [Fact]
    public void Csv_MotDongMoiMuc_DuCot()
    {
        var spec = Spec(
            Item("I.1", "Hợp đồng", true, "*hop-dong*"),
            Item("I.2", "Giấy phép", true, "*giay-phep*"),
            Item("I.3", "Ảnh", false, "*anh*"));
        var csv = DossierIndex.Csv(DossierIndex.Check(spec, new[] { "hop-dong.pdf" }));

        var rows = csv.Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(4, rows.Length);
        Assert.StartsWith("Nhóm,Mục,Tên tài liệu", rows[0]);
        Assert.Contains("có hồ sơ", rows[1]);
        Assert.Contains("THIẾU", rows[2]);
        Assert.Contains("chưa có", rows[3]);
    }

    [Fact]
    public void Html_NoiDuTrangThai_FileThua_VaOKy()
    {
        var spec = Spec(
            Item("I.1", "Hợp đồng", true, "*hop-dong*"),
            Item("I.2", "Giấy phép <b>", true, "*giay-phep*"),
            new DossierItem
            {
                Code = "I.3", Name = "Ảnh", Required = false,
                Patterns = new List<string> { "*anh*" }, Note = "chụp trước khi lấp",
            });

        var html = DossierIndex.Html(spec, DossierIndex.Check(spec, new[] { "hop-dong.pdf", "thua.tmp" }),
            "D:/ho-so", new DateTime(2026, 9, 6, 2, 3, 4));

        Assert.Contains("Danh mục thử", html);
        Assert.Contains("Toà A", html);
        Assert.Contains("D:/ho-so", html);
        Assert.Contains("2026-09-06 02:03:04", html);
        Assert.Contains("I. Chuẩn bị đầu tư", html);
        Assert.Contains("có hồ sơ", html);
        Assert.Contains("THIẾU", html);
        Assert.Contains("chưa có (không bắt buộc)", html);
        Assert.Contains("<small>chụp trước khi lấp</small>", html);
        Assert.Contains("Giấy phép &lt;b&gt;", html);
        Assert.Contains("thua.tmp", html);
        Assert.Contains("Chủ đầu tư xác nhận", html);
        Assert.Contains("Điều 28", html);

        // Không có file thừa thì không in mục đó ra.
        var clean = DossierIndex.Html(spec, DossierIndex.Check(spec, new[] { "hop-dong.pdf", "giay-phep.pdf", "anh.jpg" }),
            "D:/ho-so", DateTime.Now);
        Assert.DoesNotContain("chưa xếp vào mục nào", clean);
        // Chữ THIẾU vẫn còn ở dòng tổng kết ("0 mục bắt buộc còn THIẾU"); cái không được có là Ô ĐỎ trong bảng.
        Assert.DoesNotContain("thieu\">THIẾU", clean);
        Assert.Contains("0 mục bắt buộc còn THIẾU", clean);
    }
}
