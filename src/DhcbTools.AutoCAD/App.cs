using Autodesk.AutoCAD.Runtime;
using DhcbTools.AutoCAD.Bridge;

[assembly: ExtensionApplication(typeof(DhcbTools.AutoCAD.App))]

namespace DhcbTools.AutoCAD;

/// <summary>
/// Entry point của DHCB AutoCAD Plugin.
/// Initialize() khởi động HTTP Bridge (port 8766) để agent AI gửi lệnh trực tiếp.
/// </summary>
public sealed class App : IExtensionApplication
{
    private DhcbHttpBridge? _bridge;

    public void Initialize()
    {
        try
        {
            var editor = Autodesk.AutoCAD.ApplicationServices.Core.Application.DocumentManager
                .MdiActiveDocument?.Editor;

            editor?.WriteMessage("\n[DHCB Tools] Đã tải DHCB AutoCAD Tools. Gõ DHCB để xem lệnh.\n");
            editor?.WriteMessage($"[DHCB Tools] HTTP Bridge đang lắng nghe tại http://localhost:{DhcbHttpBridge.Port}/execute\n");

            _bridge = new DhcbHttpBridge();
            _bridge.Start();
        }
        catch (System.Exception ex)
        {
            try
            {
                Autodesk.AutoCAD.ApplicationServices.Core.Application.DocumentManager
                    .MdiActiveDocument?.Editor
                    .WriteMessage($"\n[DHCB Tools] Lỗi khởi động Bridge: {ex.Message}\n");
            }
            catch { }
        }
    }

    public void Terminate()
    {
        _bridge?.Stop();
        _bridge?.Dispose();
    }
}
