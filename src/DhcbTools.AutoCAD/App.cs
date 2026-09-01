using Autodesk.AutoCAD.Runtime;

[assembly: ExtensionApplication(typeof(DhcbTools.AutoCAD.App))]

namespace DhcbTools.AutoCAD;

/// <summary>
/// Entry point của DHCB AutoCAD Plugin.
/// AutoCAD gọi Initialize() khi load DLL; Terminate() khi tắt.
/// Lệnh được đăng ký qua [CommandMethod] trên các class Commands (không cần đăng ký thủ công).
/// </summary>
public sealed class App : IExtensionApplication
{
    public void Initialize()
    {
        try
        {
            var editor = Autodesk.AutoCAD.ApplicationServices.Core.Application.DocumentManager
                .MdiActiveDocument?.Editor;

            editor?.WriteMessage("\n[DHCB Tools] Đã tải DHCB AutoCAD Tools. Gõ DHCB để xem lệnh.\n");
        }
        catch
        {
            // Không block AutoCAD nếu lỗi khởi tạo.
        }
    }

    public void Terminate() { }
}
