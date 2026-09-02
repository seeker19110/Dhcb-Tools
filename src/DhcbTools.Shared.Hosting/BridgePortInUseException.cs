using System;

namespace DhcbTools.Shared.Hosting
{
    /// <summary>
    /// Cổng của Bridge đã có tiến trình khác lắng nghe — thường là instance Revit/AutoCAD thứ hai
    /// đang chạy cùng add-in. Trước đây <c>HttpListener.Start()</c> ném <c>HttpListenerException</c>
    /// chung chung ("The process cannot access the file...") và vỏ AutoCAD nuốt luôn vì lúc
    /// Initialize() chưa có Editor để in — instance thứ hai chạy mà không có Bridge, không ai biết.
    /// </summary>
    public sealed class BridgePortInUseException : InvalidOperationException
    {
        public BridgePortInUseException(string appName, int port, Exception inner)
            : base("Cổng " + port + " đang bị tiến trình khác chiếm — có thể một " + appName
                   + " khác đã nạp DHCB Tools và đang giữ Bridge. Instance này KHÔNG nhận lệnh qua Bridge; "
                   + "agent sẽ nói chuyện với instance kia. Đóng instance thừa rồi nạp lại để đổi.", inner)
        {
            Port = port;
        }

        public int Port { get; }
    }
}
