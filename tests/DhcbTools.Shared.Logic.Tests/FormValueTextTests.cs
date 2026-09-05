using DhcbTools.Shared.Logic.Ai;
using Newtonsoft.Json.Linq;
using Xunit;

namespace DhcbTools.Shared.Logic.Tests;

/// <summary>
/// Đổ giá trị config vào ô nhập của form động rồi đọc ngược lại. Sai ở đây thì lệnh KHÔNG CHẠY ĐƯỢC TỪ
/// RIBBON, trong khi mọi bộ ca kiểm (gửi thẳng JSON) vẫn xanh — nên nó phải có test riêng.
/// </summary>
public class FormValueTextTests
{
    [Fact]
    public void DanhSachChuoi_GhepBangDauChamPhay()
    {
        var value = new JArray("Mark", "Comments");
        Assert.Equal("Mark; Comments", FormValueText.Display(value, isList: true));
    }

    [Fact]
    public void MangObject_GiuNguyenJson_ChuKhongGhepBangDauChamPhay()
    {
        // Đúng lỗi đã gặp: ghép hai object bằng "; " ra "{…}; {…}" — không còn là JSON, và form đọc lại
        // thì báo "trông như JSON nhưng không đọc được".
        var value = JArray.Parse("[{\"name\":\"A\",\"positionMm\":0},{\"name\":\"B\",\"positionMm\":1}]");

        var text = FormValueText.Display(value, isList: false);

        Assert.DoesNotContain("}; {", text);
        Assert.Equal(value.ToString(), JArray.Parse(text).ToString());
    }

    [Fact]
    public void Object_GiuNguyenJson()
    {
        var value = JObject.Parse("{\"Cấp nước\":\"#0070C0\"}");
        var text = FormValueText.Display(value, isList: false);
        Assert.Equal("#0070C0", JObject.Parse(text)["Cấp nước"]!.ToString());
    }

    [Fact]
    public void MangChuoi_MaTruongKHONGphaiDanhSach_ThiVanLaJson()
    {
        // Config cũ có thể còn mảng ở một trường nay là giá trị đơn; hiện "A; B" thì người dùng bấm chạy
        // là gửi đúng chuỗi "A; B" vào một property string — sai âm thầm.
        var text = FormValueText.Display(new JArray("A", "B"), isList: false);
        Assert.True(FormValueText.LooksLikeJson(text));
    }

    [Theory]
    [InlineData("Doors", "Doors")]
    [InlineData("", "")]
    public void GiaTriDon_HienNguyenVan(string raw, string expected) =>
        Assert.Equal(expected, FormValueText.Display(JToken.FromObject(raw), isList: false));

    [Fact]
    public void Rong_HienChuoiRong()
    {
        Assert.Equal(string.Empty, FormValueText.Display(null, isList: false));
        Assert.Equal(string.Empty, FormValueText.Display(JValue.CreateNull(), isList: true));
    }

    [Theory]
    [InlineData("{\"a\":1}", true)]
    [InlineData("  [1,2]", true)]
    [InlineData("Doors", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void NhanDangJsonTho(string? text, bool expected) =>
        Assert.Equal(expected, FormValueText.LooksLikeJson(text));

    [Fact]
    public void So_HienNguyenVan() =>
        Assert.Equal("30", FormValueText.Display(JToken.FromObject(30), isList: false));
}

/// <summary>Ô số của form động: số nguyên phải ra JSON số nguyên, nếu không property int từ chối.</summary>
public class FormValueNumberTests
{
    [Theory]
    [InlineData("1", 1L)]
    [InlineData(" 30 ", 30L)]
    [InlineData("-2", -2L)]
    [InlineData("3.0", 3L)]
    public void SoNguyen_RaJsonSoNguyen(string text, long expected)
    {
        var value = FormValueText.Number(text);
        Assert.Equal(JTokenType.Integer, value!.Type);
        Assert.Equal(expected, value.Value<long>());
    }

    [Theory]
    [InlineData("1.5", 1.5)]
    [InlineData("1,5", 1.5)]   // máy tiếng Việt gõ dấu phẩy thập phân
    public void SoThuc_GiuNguyenPhanThapPhan(string text, double expected)
    {
        var value = FormValueText.Number(text);
        Assert.Equal(JTokenType.Float, value!.Type);
        Assert.Equal(expected, value.Value<double>());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void ORong_TraNull(string? text) => Assert.Null(FormValueText.Number(text));

    [Fact]
    public void KhongPhaiSo_TraNull_DeFormBaoLoi() => Assert.Null(FormValueText.Number("ba mươi"));

    [Fact]
    public void SoQuaLon_KhongEpVeLong() =>
        Assert.Equal(JTokenType.Float, FormValueText.Number("1e30")!.Type);
}
