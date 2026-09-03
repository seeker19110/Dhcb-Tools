using Xunit;

namespace DhcbTools.Shared.Logic.Tests;

/// <summary>
/// Chốt chặn cho lỗi "batch treo ở hộp thoại cảnh báo" (2026-09-03, bộ ghi thật trên model HVAC).
/// <para>
/// Revit tính lại Space lúc <b>mở model</b> và bật hộp thoại "0 failures, 0 errors, 67 warnings".
/// Cảnh báo đó nằm ngoài mọi transaction của lệnh DHCB nên preprocessor gắn theo từng transaction
/// không chạm tới được; batch đứng chờ người bấm nút cho tới khi hết giờ, không chạy nổi một ca nào.
/// </para>
/// <para>
/// Không mở được Revit trên CI nên test đọc thẳng mã nguồn: phiên batch phải đăng ký
/// <c>Application.FailuresProcessing</c> và phải gỡ ra khi xong. Thô, nhưng bắt được đúng cái hồi quy
/// đắt nhất — nếu ai đó xoá dòng đăng ký thì batch đêm lại treo im lặng.
/// </para>
/// </summary>
public class BatchFailureHandlingTests
{
    private static string HookSource()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Dhcb-Tools.sln")))
        {
            dir = dir.Parent;
        }
        var root = dir?.FullName ?? throw new InvalidOperationException("Không tìm thấy gốc repo.");
        return File.ReadAllText(Path.Combine(root, "src", "DhcbTools.Revit", "Batch", "BatchStartupHook.cs"));
    }

    [Fact]
    public void PhienBatch_DangKyXuLyCanhBaoOMucApplication()
    {
        Assert.Contains("application.FailuresProcessing += OnFailuresProcessing", HookSource());
    }

    [Fact]
    public void PhienBatch_GoXuLyCanhBaoKhiXong()
    {
        Assert.Contains("application.FailuresProcessing -= OnFailuresProcessing", HookSource());
    }

    [Fact]
    public void XuLyCanhBao_DungChinhSachSilent_DeKhongTreoOLoiCoCachGiaiQuyet()
    {
        Assert.Contains("SilentFailuresPreprocessor(FailurePolicy.Silent)", HookSource());
    }
}
