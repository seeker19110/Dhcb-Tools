using DhcbTools.Shared.Hosting;
using Xunit;

namespace DhcbTools.Shared.Logic.Tests;

/// <summary>
/// <c>ChangedIds</c> (giai đoạn 10.2) — danh sách phần tử lệnh vừa đổi, để agent zoom/kiểm lại đúng
/// những phần tử đó thay vì chỉ biết một con số đếm.
/// </summary>
public class CommandResultTests
{
    [Fact]
    public void MacDinh_KhongCoPhanTuNaoDuocGhiNhan()
    {
        Assert.Empty(CommandResult.Ok("xong").ChangedIds);
    }

    [Fact]
    public void GhiNhanTungPhanTu_GiuNguyenThuTu()
    {
        var result = CommandResult.Ok("xong").WithChanged(12).WithChanged(7).WithChanged(99);

        Assert.Equal(new long[] { 12, 7, 99 }, result.ChangedIds);
    }

    [Fact]
    public void GhiNhanNhieuPhanTu_CongDon()
    {
        var result = CommandResult.Ok("xong")
            .WithChanged(new long[] { 1, 2 })
            .WithChanged(new long[] { 3 });

        Assert.Equal(new long[] { 1, 2, 3 }, result.ChangedIds);
    }

    /// <summary>
    /// Một lệnh sửa cả vạn phần tử không được làm phình response HTTP; số đếm đầy đủ vẫn ở AffectedCount.
    /// </summary>
    [Fact]
    public void VuotNguong_CatBotNhungKhongNem()
    {
        var result = CommandResult.Ok("xong", affected: 10_000)
            .WithChanged(Enumerable.Range(1, 10_000).Select(i => (long)i));

        Assert.Equal(CommandResult.MaxChangedIds, result.ChangedIds.Count);
        Assert.Equal(10_000, result.AffectedCount);
        Assert.Equal(1, result.ChangedIds[0]);
    }

    [Fact]
    public void VuotNguong_ThemTungCaiCungDung()
    {
        var result = CommandResult.Ok("xong");
        for (var i = 0; i < CommandResult.MaxChangedIds + 50; i++)
        {
            result.WithChanged(i);
        }

        Assert.Equal(CommandResult.MaxChangedIds, result.ChangedIds.Count);
    }

    /// <summary>ChangedIds phải ra JSON để agent đọc được qua Bridge.</summary>
    [Fact]
    public void SerializeSangJson_CoChangedIds()
    {
        var json = Newtonsoft.Json.JsonConvert.SerializeObject(
            CommandResult.Ok("xong", 2).WithChanged(new long[] { 5, 6 }));

        Assert.Contains("\"ChangedIds\":[5,6]", json.Replace(" ", string.Empty));
    }
}

/// <summary>
/// Chọn thời gian chờ cho từng request (giai đoạn 10.5). 30 s cố định đủ cho lệnh đọc nhưng không đủ
/// cho SleeveAuto/AutoRoute trên model thật; ngược lại không có trần thì một client giữ hàng đợi
/// Revit vô hạn — Revit chỉ có một luồng nên lệnh nào cũng phải nhường chỗ.
/// </summary>
public class BridgeTimeoutTests
{
    private static readonly TimeSpan Fallback = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan Max = TimeSpan.FromMinutes(10);

    private static TimeSpan Resolve(int? requested) =>
        DhcbTools.Shared.Hosting.HttpBridgeServer.ResolveTimeout(requested, Fallback, Max);

    [Fact]
    public void KhongXinGiCa_DungMacDinh() => Assert.Equal(Fallback, Resolve(null));

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void SoKhongHopLe_DungMacDinh(int requested) => Assert.Equal(Fallback, Resolve(requested));

    [Fact]
    public void XinLauHon_DuocChap() => Assert.Equal(TimeSpan.FromSeconds(120), Resolve(120));

    [Fact]
    public void XinNganHon_CungDuocChap() => Assert.Equal(TimeSpan.FromSeconds(5), Resolve(5));

    [Fact]
    public void XinQuaTran_BiCatVeTran() => Assert.Equal(Max, Resolve(99_999));

    [Fact]
    public void XinDungBangTran_GiuNguyen() => Assert.Equal(Max, Resolve((int)Max.TotalSeconds));
}
