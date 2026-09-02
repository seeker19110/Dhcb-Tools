using Autodesk.AutoCAD.Runtime;
using DhcbTools.AutoCAD.Bridge;
using DhcbTools.Shared.Hosting;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

[assembly: ExtensionApplication(typeof(DhcbTools.AutoCAD.App))]

namespace DhcbTools.AutoCAD;

/// <summary>
/// Entry point của DHCB AutoCAD Plugin.
/// Initialize() khởi động HTTP Bridge (port 8766) để agent AI gửi lệnh trực tiếp.
///
/// Lúc Initialize() chạy (nạp qua bundle khi AutoCAD khởi động) thường CHƯA có document nào mở,
/// nên <c>MdiActiveDocument</c> là null và mọi <c>WriteMessage</c> đều rơi vào khoảng không. Vì thế
/// thông báo khởi động/lỗi được xếp hàng và in ở sự kiện <c>Idle</c> đầu tiên có Editor — đây là
/// lý do trước đây instance AutoCAD thứ hai không chiếm được cổng 8766 mà không ai thấy gì.
/// </summary>
public sealed class App : IExtensionApplication
{
    private DhcbHttpBridge? _bridge;
    private readonly List<string> _pending = new();

    public void Initialize()
    {
        Say("[DHCB Tools] Đã tải DHCB AutoCAD Tools. Gõ DHCB để xem lệnh.");

        try
        {
            _bridge = new DhcbHttpBridge();
            _bridge.Start();
            Say($"[DHCB Tools] HTTP Bridge đang lắng nghe tại http://127.0.0.1:{DhcbHttpBridge.Port}/ (token: {_bridge.TokenPath})");
        }
        catch (BridgePortInUseException ex)
        {
            _bridge?.Dispose();
            _bridge = null;
            Say("[DHCB Tools] CẢNH BÁO — " + ex.Message);
        }
        catch (System.Exception ex)
        {
            _bridge?.Dispose();
            _bridge = null;
            Say($"[DHCB Tools] Lỗi khởi động Bridge: {ex.Message}");
        }

        Flush();
        if (_pending.Count > 0)
        {
            AcApp.Idle += OnIdle;
        }
    }

    public void Terminate()
    {
        AcApp.Idle -= OnIdle;
        _bridge?.Stop();
        _bridge?.Dispose();
    }

    private void OnIdle(object? sender, EventArgs e)
    {
        Flush();
        if (_pending.Count == 0)
        {
            AcApp.Idle -= OnIdle;
        }
    }

    private void Say(string line) => _pending.Add(line);

    /// <summary>In các dòng đang chờ nếu đã có Editor; không có thì giữ lại cho lần Idle sau.</summary>
    private void Flush()
    {
        try
        {
            var editor = AcApp.DocumentManager.MdiActiveDocument?.Editor;
            if (editor is null)
            {
                return;
            }

            foreach (var line in _pending)
            {
                editor.WriteMessage("\n" + line + "\n");
            }

            _pending.Clear();
        }
        catch
        {
            // Editor chưa sẵn sàng — thử lại ở Idle sau.
        }
    }
}
