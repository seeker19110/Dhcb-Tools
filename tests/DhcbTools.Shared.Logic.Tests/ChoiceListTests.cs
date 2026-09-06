using DhcbTools.Shared.Logic.Ai;
using Xunit;

namespace DhcbTools.Shared.Logic.Tests;

/// <summary>
/// §64 — điều duy nhất ChoiceList phải làm cho ra: phân biệt "mô hình không có gì" với "đọc hỏng".
/// Bản trước cả hai đều là một danh sách rỗng và form nói câu đầu cho cả hai trường hợp.
/// </summary>
public class ChoiceListTests
{
    [Fact]
    public void DocTronVen_KhongCoChuThich()
    {
        var list = new ChoiceList(new[] { "Tường", "Sàn" }, null);

        Assert.True(list.IsComplete);
        Assert.Equal(2, list.Count);
        Assert.Null(list.Note());
    }

    [Fact]
    public void MoHinhTrong_VanLaDocTronVen()
    {
        // Rỗng mà đọc trọn vẹn thì không được gắn nhãn lỗi — form giữ nguyên câu cũ.
        Assert.Null(ChoiceList.Empty.Note());
        Assert.True(ChoiceList.Empty.IsComplete);
    }

    [Fact]
    public void DocHongTuDau_NoiLaKhongDocDuoc()
    {
        var list = ChoiceList.Failed(Array.Empty<string>(), new InvalidOperationException("link lỗi"));

        Assert.False(list.IsComplete);
        Assert.Equal("link lỗi", list.Error);
        Assert.Equal("không đọc được danh sách từ mô hình — gõ tay", list.Note());
    }

    [Fact]
    public void DocHongGiuaChung_GiuLaiCanDuoiVaNoiChuaDayDu()
    {
        // Đúng lối §61: giữ những gì đã đọc được và gọi nó là cận dưới, thay vì vứt hết.
        var list = ChoiceList.Failed(new[] { "Tường" }, new InvalidOperationException("family hỏng"));

        Assert.Equal(new[] { "Tường" }, list.Names);
        Assert.Equal("danh sách chưa đầy đủ (1 giá trị đọc được) — gõ tay nếu thiếu", list.Note());
    }
}
