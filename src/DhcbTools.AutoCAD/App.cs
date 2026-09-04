using Autodesk.AutoCAD.Runtime;
using DhcbTools.AutoCAD.Bridge;
using DhcbTools.Shared.Hosting;
#if !DHCB_NO_WPF
using DhcbTools.AutoCAD.Ribbon;
#endif
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
///
/// Kiểm chứng thật trên AutoCAD 2026 cho thấy AutoCAD còn mở một Drawing1 tạm lúc khởi động rồi
/// đóng để về tab Start, nên dòng in ở Idle có thể rơi vào document tạm đó. Vì vậy trạng thái Bridge
/// được giữ lại trong <see cref="StatusLines"/> và xem lại được bất cứ lúc nào bằng lệnh
/// <c>DHCB_BRIDGE</c> (<see cref="Commands.BridgeCommands"/>).
/// </summary>
public sealed class App : IExtensionApplication
{
    private static readonly List<string> s_status = new();

    private DhcbHttpBridge? _bridge;
    private readonly List<string> _pending = new();

    /// <summary>Trạng thái Bridge của instance này (PID, cổng hoặc lý do không mở được) — nguồn cho lệnh DHCB_BRIDGE.</summary>
    public static IReadOnlyList<string> StatusLines => s_status;

    public void Initialize()
    {
        var pid = System.Diagnostics.Process.GetCurrentProcess().Id;
        DhcbLog.Prune("AutoCAD");
        DhcbLog.Write("AutoCAD", $"Plugin nạp — phiên bản "
            + $"{DhcbVersion.Of(System.Reflection.Assembly.GetExecutingAssembly())}, PID {pid}.");
        Say("[DHCB Tools] Đã tải DHCB AutoCAD Tools (PID " + pid + "). Gõ DHCB_BRIDGE để xem trạng thái Bridge.");

#if !DHCB_NO_WPF
        try
        {
            RibbonBuilder.EnsureBuilt();
        }
        catch (System.Exception ex)
        {
            DhcbLog.Error("AutoCAD", "dựng Ribbon", ex);
        }
#endif

        try
        {
            _bridge = new DhcbHttpBridge();
            _bridge.Start();
            Status($"[DHCB Tools] HTTP Bridge (PID {pid}) đang lắng nghe tại http://127.0.0.1:{DhcbHttpBridge.Port}/ (token: {_bridge.TokenPath})");
        }
        catch (BridgePortInUseException ex)
        {
            _bridge?.Dispose();
            _bridge = null;
            Status($"[DHCB Tools] CẢNH BÁO (PID {pid}) — " + ex.Message);
        }
        catch (System.Exception ex)
        {
            _bridge?.Dispose();
            _bridge = null;
            DhcbLog.Error("AutoCAD", "khởi động HTTP Bridge", ex);
            Status($"[DHCB Tools] Lỗi khởi động Bridge (PID {pid}): {ex.Message}");
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

    /// <summary>Vừa in lúc khởi động, vừa giữ lại cho DHCB_BRIDGE.</summary>
    private void Status(string line)
    {
        s_status.Add(line);
        DhcbLog.Write("AutoCAD", line);
        Say(line);
    }

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
