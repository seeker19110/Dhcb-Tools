using Autodesk.AutoCAD.DatabaseServices;

namespace DhcbTools.Core.AutoCAD;

/// <summary>
/// Cầu nối để lệnh Core báo "màn hình cần vẽ lại" mà không phải đụng tới <c>Editor</c>. Core cố ý chỉ
/// nhận <see cref="Database"/> để còn chạy được trong accoreconsole; vỏ GUI gán <see cref="RegenRequested"/>
/// lúc nạp plugin để gọi <c>Editor.Regen()</c> cho đúng document.
/// <para>
/// Lý do có lớp này (2026-11-13): <c>LayerImport</c> rã băng 30 layer qua Bridge, dữ liệu đã đổi
/// (<c>isFrozen: false</c>) nhưng kỹ sư không thấy gì trên màn hình — gán <c>IsFrozen</c>/<c>IsOff</c> qua API
/// không tự dựng lại đồ hoạ, phải gõ REGEN. Lệnh báo thành công mà bản vẽ trông như chưa đổi là một dạng
/// báo sai.
/// </para>
/// </summary>
public static class AcadDisplay
{
    /// <summary>Vỏ GUI gán; null trong accoreconsole hay test — khi đó yêu cầu bị bỏ qua.</summary>
    public static Action<Database>? RegenRequested { get; set; }

    /// <summary>
    /// Xin vẽ lại drawing. Không bao giờ ném: lệnh đã commit xong, lỗi vẽ lại chỉ được thành một dòng nhắc
    /// gõ REGEN chứ không được biến lần ghi thành công thành thất bại.
    /// </summary>
    /// <returns>true nếu vỏ đã vẽ lại; false nếu không có vỏ hoặc vẽ lại lỗi.</returns>
    public static bool RequestRegen(Database database)
    {
        var handler = RegenRequested;
        if (handler is null)
        {
            return false;
        }

        try
        {
            handler(database);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
