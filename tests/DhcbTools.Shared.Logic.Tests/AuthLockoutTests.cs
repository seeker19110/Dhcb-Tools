using DhcbTools.Shared.Hosting;
using Xunit;

namespace DhcbTools.Shared.Logic.Tests;

/// <summary>
/// Khoá dò token (mục 0.1) với đồng hồ giả: 5 lần sai trong 60 s → khoá 5 phút; hết 5 phút tự mở;
/// một lần đúng xoá lịch sử sai; lần sai quá cửa sổ 60 s không còn được đếm.
/// </summary>
public class AuthLockoutTests
{
    private static readonly DateTime T0 = new DateTime(2026, 9, 4, 8, 0, 0, DateTimeKind.Utc);

    private sealed class FakeClock
    {
        public DateTime Now = T0;
        public DateTime Read() => Now;
        public void Advance(TimeSpan by) => Now += by;
    }

    [Fact]
    public void NamLanSaiTrong60Giay_ThiKhoa()
    {
        var clock = new FakeClock();
        var lockout = new AuthLockout(clock.Read);

        for (var i = 0; i < 4; i++)
        {
            Assert.False(lockout.RecordFailure());
            Assert.False(lockout.IsLocked);
            clock.Advance(TimeSpan.FromSeconds(10));
        }

        Assert.True(lockout.RecordFailure());   // lần thứ 5, ở giây 40
        Assert.True(lockout.IsLocked);
    }

    [Fact]
    public void KhoaHet5Phut_TuMo()
    {
        var clock = new FakeClock();
        var lockout = new AuthLockout(clock.Read);
        for (var i = 0; i < 5; i++) lockout.RecordFailure();
        Assert.True(lockout.IsLocked);

        clock.Advance(TimeSpan.FromMinutes(5) - TimeSpan.FromSeconds(1));
        Assert.True(lockout.IsLocked);

        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.False(lockout.IsLocked);

        // Sau khi mở, lịch sử sai cũ đã bị xoá: cần đủ 5 lần mới nữa mới khoá lại.
        for (var i = 0; i < 4; i++) Assert.False(lockout.RecordFailure());
        Assert.False(lockout.IsLocked);
    }

    [Fact]
    public void DungToken_XoaLichSuSai()
    {
        var clock = new FakeClock();
        var lockout = new AuthLockout(clock.Read);
        for (var i = 0; i < 4; i++) lockout.RecordFailure();

        lockout.RecordSuccess();

        for (var i = 0; i < 4; i++) Assert.False(lockout.RecordFailure());
        Assert.False(lockout.IsLocked);
        Assert.True(lockout.RecordFailure());   // lần thứ 5 kể từ RecordSuccess
    }

    [Fact]
    public void LanSaiQuaCuaSo60Giay_KhongDuocDem()
    {
        var clock = new FakeClock();
        var lockout = new AuthLockout(clock.Read);

        for (var i = 0; i < 4; i++) lockout.RecordFailure();   // 4 lần ở giây 0
        clock.Advance(TimeSpan.FromSeconds(61));

        Assert.False(lockout.RecordFailure());  // 4 lần cũ đã rơi khỏi cửa sổ → mới có 1
        Assert.False(lockout.IsLocked);
    }

    [Fact]
    public void ThamSoTuyChinh_DuocTonTrong()
    {
        var clock = new FakeClock();
        var lockout = new AuthLockout(clock.Read, maxFailures: 2, window: TimeSpan.FromSeconds(5), lockDuration: TimeSpan.FromSeconds(30));

        Assert.False(lockout.RecordFailure());
        Assert.True(lockout.RecordFailure());
        Assert.True(lockout.IsLocked);
        clock.Advance(TimeSpan.FromSeconds(30));
        Assert.False(lockout.IsLocked);
    }

    /// <summary>
    /// Sai Content-Type không phải dò token: server trả 415 và KHÔNG gọi RecordFailure. Test bảo vệ hàm
    /// phân loại Content-Type mà server dùng để quyết định điều đó.
    /// </summary>
    [Theory]
    [InlineData("application/json", true)]
    [InlineData("application/json; charset=utf-8", true)]
    [InlineData("  Application/JSON", true)]
    [InlineData("text/plain", false)]
    [InlineData("application/x-www-form-urlencoded", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void ContentTypeJson_PhanLoaiDung(string? contentType, bool expected)
    {
        Assert.Equal(expected, HttpBridgeServer.IsJsonContentType(contentType));
    }
}
