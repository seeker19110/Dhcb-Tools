using DhcbTools.Shared.Logic;
using DhcbTools.Shared.Logic.Ai;
using Newtonsoft.Json.Linq;
using Xunit;

namespace DhcbTools.Shared.Logic.Tests;

/// <summary>
/// Tầng thuần của lệnh <c>DictionaryLearn</c>: soi tên tham số thật của dự án rồi đề xuất
/// <c>dictionary.json</c>. Đây là thứ quyết định kỹ sư có phải mở JSON trong <c>%APPDATA%</c> bằng tay
/// mỗi lần vấp <c>E-PARAM-MISSING</c> hay không (ma sát đo được trên dự án thật — progress.md §21),
/// nên hai ràng buộc dưới đây phải có test: <b>chỉ đề xuất tên có thật</b> và <b>không xoá thứ đã khai</b>.
/// </summary>
public class DictionarySuggesterTests
{
    private static ParameterCandidate P(string name, string storage = "String", int filled = 8, int total = 10, string category = "Pipes")
        => new ParameterCandidate(name, category, storage, filled, total);

    [Fact]
    public void MoHinhDaCoTenTuDienBiet_ThiKhongDeXuatGiThem()
    {
        var suggestions = DictionarySuggester.Suggest(
            new[] { "level" },
            ParameterDictionary.BuiltinOnly(),
            new[] { P("Level", "ElementId"), P("Comments") });

        var level = Assert.Single(suggestions);
        Assert.Equal(SuggestionStatus.DaCo, level.Status);
        Assert.False(level.IsProposal);
        Assert.False(level.NeedsReview);
    }

    [Fact]
    public void TenRiengCuaDuAn_DuocDeXuatChoDungKhoa()
    {
        var suggestions = DictionarySuggester.Suggest(
            new[] { "bottomElevation" },
            ParameterDictionary.BuiltinOnly(),
            new[] { P("Cao độ đáy ống", "Double"), P("Ghi chú"), P("Mark") });

        var s = Assert.Single(suggestions);
        Assert.Equal(SuggestionStatus.DeXuat, s.Status);
        Assert.Equal("Cao độ đáy ống", s.Name);
    }

    /// <summary>Không có gì giống thì phải nói "không thấy" — đề xuất bừa còn tệ hơn báo thiếu.</summary>
    [Fact]
    public void KhongCoUngVienNaoGiong_ThiBaoKhongThay_ChuKhongDeXuatBua()
    {
        var suggestions = DictionarySuggester.Suggest(
            new[] { "diameter" },
            ParameterDictionary.BuiltinOnly(),
            new[] { P("Ghi chú"), P("Người duyệt"), P("Mã hồ sơ") });

        var s = Assert.Single(suggestions);
        Assert.Equal(SuggestionStatus.KhongThay, s.Status);
        Assert.Null(s.Name);
    }

    /// <summary>
    /// Tham số tồn tại mà rỗng toàn dự án đọc ra cũng vô nghĩa — đúng lớp lỗi "không làm gì mà vẫn báo
    /// thành công" mà từ điển sinh ra để chặn. Tên có dữ liệu thật phải thắng tên rỗng.
    /// </summary>
    [Fact]
    public void ThamSoCoDuLieuThat_ThangThamSoRongToanDuAn()
    {
        var suggestions = DictionarySuggester.Suggest(
            new[] { "diameter" },
            ParameterDictionary.BuiltinOnly(),
            new[]
            {
                P("Đường kính danh nghĩa", "Double", filled: 0, total: 50),
                P("Đường kính ống", "Double", filled: 47, total: 50),
            });

        Assert.Equal("Đường kính ống", Assert.Single(suggestions).Name);
    }

    /// <summary>Khoá kích thước mà tên khớp lại là ô chữ thì gần như chắc chắn nhầm — hạ điểm.</summary>
    [Fact]
    public void KhoaSo_UngVienKieuChuoi_BiHaDiemSoVoiUngVienKieuSo()
    {
        var suggestions = DictionarySuggester.Suggest(
            new[] { "width" },
            ParameterDictionary.BuiltinOnly(),
            new[]
            {
                P("Chiều rộng ghi chú", "String"),
                P("Chiều rộng danh nghĩa", "Double"),
            });

        Assert.Equal("Chiều rộng danh nghĩa", Assert.Single(suggestions).Name);
    }

    [Fact]
    public void KhongCoUngVienNao_ThiNemLoi_ChuKhongTraBangRong()
    {
        Assert.Throws<ArgumentException>(() => DictionarySuggester.Suggest(
            new[] { "level" }, ParameterDictionary.BuiltinOnly(), new List<ParameterCandidate>()));
    }

    // ── Trộn vào file từ điển ────────────────────────────────────────────────

    [Fact]
    public void Tron_GiuNguyenTenKySuDaKhai_VaDatTenMoiLenDau()
    {
        var cu = """
        { "parameters": { "level": ["Tầng nhà"] }, "families": { "sleeve": "DHCB_Sleeve: Tròn" } }
        """;

        var moi = DictionarySuggester.Merge(cu, new[]
        {
            new DictionarySuggestion("level", "Cốt tầng", 0.9, "", SuggestionStatus.DeXuat),
        });

        var root = JObject.Parse(moi);
        Assert.Equal(new[] { "Cốt tầng", "Tầng nhà" }, root["parameters"]!["level"]!.Select(v => v.ToString()));
        Assert.Equal("DHCB_Sleeve: Tròn", root["families"]!["sleeve"]!.ToString());
    }

    [Fact]
    public void Tron_KhongNhanBanTenDaCoSan()
    {
        var moi = DictionarySuggester.Merge(
            """{ "parameters": { "level": ["Tầng nhà"] } }""",
            new[] { new DictionarySuggestion("level", "tầng   nhà", 0.9, "", SuggestionStatus.DeXuat) });

        Assert.Single(JObject.Parse(moi)["parameters"]!["level"]!);
    }

    [Fact]
    public void Tron_BoQuaDongKhongPhaiDeXuat()
    {
        var moi = DictionarySuggester.Merge(null, new[]
        {
            new DictionarySuggestion("level", "Level", 1.0, "", SuggestionStatus.DaCo),
            new DictionarySuggestion("diameter", null, 0.1, "", SuggestionStatus.KhongThay),
        });

        Assert.Empty((JObject)JObject.Parse(moi)["parameters"]!);
    }

    /// <summary>Ghi đè một file JSON hỏng là xoá mất khai báo của kỹ sư — phải dừng và báo.</summary>
    [Fact]
    public void Tron_FileCuHong_ThiNemLoi_ChuKhongGhiDe()
    {
        Assert.Throws<InvalidOperationException>(() => DictionarySuggester.Merge(
            "{ đây không phải JSON",
            new[] { new DictionarySuggestion("level", "Tầng", 0.9, "", SuggestionStatus.DeXuat) }));
    }

    /// <summary>Trộn xong phải nạp lại được, và tên mới phải được thử TRƯỚC tên dựng sẵn.</summary>
    [Fact]
    public void VongTron_DeXuat_TronVaoFile_RoiNapLai_ThiTenMoiDungDau()
    {
        var moi = DictionarySuggester.Merge(null, DictionarySuggester.Suggest(
            new[] { "bottomElevation" },
            ParameterDictionary.BuiltinOnly(),
            new[] { P("Cao độ đáy ống", "Double") }));

        var names = ParameterDictionary.Parse(moi).NamesFor("bottomElevation");

        Assert.Equal("Cao độ đáy ống", names[0]);
        Assert.Contains("DHCB_Bottom_Elevation", names);   // tên dựng sẵn vẫn còn làm phương án dự phòng
    }

    [Fact]
    public void Csv_CoDuCotVaDongChoMoiKhoa()
    {
        var csv = DictionarySuggester.ToCsv(new[]
        {
            new DictionarySuggestion("level", "Tầng", 0.9, "lý do", SuggestionStatus.DeXuat),
        });

        Assert.StartsWith("Key,Name,Status,Confidence,NeedsReview,Reason", csv);
        Assert.Contains("level,Tầng,DeXuat", csv);
    }

    /// <summary>Chú thích trong file mẫu không được biến thành một khoá logic.</summary>
    [Fact]
    public void KhoaBatDauBangGachDuoi_LaChuThich_KhongPhaiKhoa()
    {
        var dictionary = ParameterDictionary.Parse("""
        { "parameters": { "_comment": ["ghi chú"], "level": ["Tầng"] },
          "families": { "_comment": "ghi chú", "sleeve": "DHCB_Sleeve" } }
        """);

        Assert.DoesNotContain("_comment", dictionary.Keys);
        Assert.DoesNotContain("_comment", dictionary.Families.Keys);
        Assert.Contains("level", dictionary.Keys);
    }
}
