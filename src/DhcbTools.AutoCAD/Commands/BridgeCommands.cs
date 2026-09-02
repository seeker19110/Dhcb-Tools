using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Runtime;

namespace DhcbTools.AutoCAD.Commands;

/// <summary>
/// DHCB_BRIDGE — in trạng thái HTTP Bridge của instance AutoCAD hiện tại: đang giữ cổng 8766,
/// hay không mở được vì instance khác đã chiếm. Cần vì dòng in lúc khởi động có thể rơi vào
/// Drawing1 tạm mà AutoCAD đóng ngay khi về tab Start (xem <see cref="App"/>).
/// </summary>
public sealed class BridgeCommands
{
    [CommandMethod("DHCB_BRIDGE", CommandFlags.Modal)]
    public void ShowBridgeStatus()
    {
        var editor = Application.DocumentManager.MdiActiveDocument?.Editor;
        if (editor is null)
        {
            return;
        }

        if (App.StatusLines.Count == 0)
        {
            editor.WriteMessage("\n[DHCB Tools] Bridge chưa được khởi động (App.Initialize chưa chạy?).\n");
            return;
        }

        foreach (var line in App.StatusLines)
        {
            editor.WriteMessage("\n" + line + "\n");
        }
    }
}
