using DhcbTools.Shared.Logic;
using Xunit;

namespace DhcbTools.Shared.Logic.Tests;

/// <summary>
/// Bao lỗi #5 trong docs/progress.md: đánh số theo hàng không có dung sai — hai cửa cùng hàng lệch
/// 1 mm rơi vào hai "hàng" khác nhau nên kết quả thực tế là sắp thuần theo Y.
/// </summary>
public class NumberingPlannerTests
{
    private const double Mm = 1.0 / 304.8;

    private static NumberingItem<string> Item(string key, double xMm, double yMm)
        => new(key, xMm * Mm, yMm * Mm);

    private static string[] Keys(IEnumerable<NumberingItem<string>> items)
        => items.Select(i => i.Key).ToArray();

    [Fact]
    public void Order_CungHangLechMotMilimet_VanSapTheoXTrongHang()
    {
        // Ba cửa cùng một hàng, cao độ Y lệch nhau 1 mm; đặt lệch thứ tự X để thấy rõ tác dụng.
        var items = new[]
        {
            Item("phai", 5000, 10_001),
            Item("trai", 1000, 10_000),
            Item("giua", 3000, 9_999),
        };

        var ordered = NumberingPlanner.Order(items, ScanDirection.LeftToRightThenTopToBottom);

        Assert.Equal(new[] { "trai", "giua", "phai" }, Keys(ordered));
    }

    [Fact]
    public void Order_HaiHangCachXaHonDungSai_HangTrenTruoc()
    {
        var items = new[]
        {
            Item("duoi-trai", 1000, 0),
            Item("tren-phai", 5000, 5000),
            Item("tren-trai", 1000, 5000),
            Item("duoi-phai", 5000, 0),
        };

        var ordered = NumberingPlanner.Order(items, ScanDirection.LeftToRightThenTopToBottom);

        Assert.Equal(new[] { "tren-trai", "tren-phai", "duoi-trai", "duoi-phai" }, Keys(ordered));
    }

    [Fact]
    public void Order_QuetTheoCot_CotTraiTruocTrongCotTrenXuong()
    {
        var items = new[]
        {
            Item("phai-tren", 5000, 5000),
            Item("trai-duoi", 1000, 0),
            Item("trai-tren", 1000, 5000),
            Item("phai-duoi", 5000, 0),
        };

        var ordered = NumberingPlanner.Order(items, ScanDirection.TopToBottomThenLeftToRight);

        Assert.Equal(new[] { "trai-tren", "trai-duoi", "phai-tren", "phai-duoi" }, Keys(ordered));
    }

    [Fact]
    public void Order_DungSaiTuyChinh_GomHangTheoDungSaiDaChon()
    {
        // Lệch 500 mm: với dung sai mặc định 300 mm là hai hàng, với dung sai 800 mm là một hàng.
        var items = new[]
        {
            Item("b", 1000, 0),
            Item("a", 5000, 500),
        };

        Assert.Equal(new[] { "a", "b" }, Keys(NumberingPlanner.Order(items, ScanDirection.LeftToRightThenTopToBottom)));
        Assert.Equal(new[] { "b", "a" }, Keys(NumberingPlanner.Order(items, ScanDirection.LeftToRightThenTopToBottom, 800 * Mm)));
    }

    [Fact]
    public void Order_TrungHoanToanToaDo_GiuNguyenThuTuDauVao()
    {
        var items = new[]
        {
            Item("mot", 1000, 1000),
            Item("hai", 1000, 1000),
            Item("ba", 1000, 1000),
        };

        Assert.Equal(new[] { "mot", "hai", "ba" }, Keys(NumberingPlanner.Order(items, ScanDirection.LeftToRightThenTopToBottom)));
    }

    [Fact]
    public void Order_DanhSachRong_TraVeRong()
    {
        Assert.Empty(NumberingPlanner.Order(Array.Empty<NumberingItem<string>>(), ScanDirection.LeftToRightThenTopToBottom));
    }

    [Fact]
    public void Order_DungSaiAm_NemLoi()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => NumberingPlanner.Order(new[] { Item("a", 0, 0) }, ScanDirection.LeftToRightThenTopToBottom, -1));
    }

    [Fact]
    public void Order_ChuoiDaiLechDanDuoiDungSai_KhongTroiDai()
    {
        // 10 phần tử, mỗi cái cao hơn cái trước 250 mm (< dung sai 300 mm). Nếu so với phần tử liền trước
        // thì cả 10 gộp thành một hàng dù đầu và cuối cách nhau 2250 mm.
        var items = Enumerable.Range(0, 10)
            .Select(i => Item("e" + i, 0, i * 250))
            .ToArray();

        var ordered = NumberingPlanner.Order(items, ScanDirection.LeftToRightThenTopToBottom);

        // Dải gom theo ĐẦU dải nên chia thành 5 hàng (mỗi hàng 2 phần tử), hàng trên (Y lớn) trước.
        Assert.Equal(new[] { "e8", "e9" }, Keys(ordered).Take(2).ToArray());
        Assert.Equal(new[] { "e0", "e1" }, Keys(ordered).Skip(8).ToArray());
    }

    [Theory]
    [InlineData("D-", 1, 3, "D-001")]
    [InlineData("", 7, 0, "7")]
    [InlineData("P-", 42, 2, "P-42")]
    [InlineData("P-", 5, 4, "P-0005")]
    [InlineData("X", -7, 3, "X-007")]
    public void FormatLabel_DemSoVaTienTo(string prefix, int number, int pad, string expected)
    {
        Assert.Equal(expected, NumberingPlanner.FormatLabel(prefix, number, pad));
    }

    [Fact]
    public void FormatLabel_PrefixNull_CoiNhuRong()
    {
        Assert.Equal("001", NumberingPlanner.FormatLabel(null!, 1, 3));
    }

    [Fact]
    public void Assign_TangTheoBuocNhay()
    {
        var items = new[] { Item("a", 0, 0), Item("b", 0, 0), Item("c", 0, 0) };

        var assignments = NumberingPlanner.Assign(items, "D-", 10, 5, 3);

        Assert.Equal(new[] { "D-010", "D-015", "D-020" }, assignments.Select(a => a.Value).ToArray());
        Assert.Equal(new[] { "a", "b", "c" }, assignments.Select(a => a.Key).ToArray());
        Assert.Equal(new[] { 10, 15, 20 }, assignments.Select(a => a.Number).ToArray());
    }

    [Fact]
    public void Assign_BuocNhayAm_VanChay()
    {
        var items = new[] { Item("a", 0, 0), Item("b", 0, 0) };

        var assignments = NumberingPlanner.Assign(items, string.Empty, 5, -1, 0);

        Assert.Equal(new[] { "5", "4" }, assignments.Select(a => a.Value).ToArray());
    }

    [Fact]
    public void Assign_DanhSachRong_TraVeRong()
    {
        Assert.Empty(NumberingPlanner.Assign(Array.Empty<NumberingItem<string>>(), "D-", 1, 1, 3));
    }
}
