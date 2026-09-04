using System.Windows.Input;
using Autodesk.Windows;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace DhcbTools.AutoCAD.Ribbon;

/// <summary>Gửi macro của nút Ribbon vào command line — cách chuẩn để nút Ribbon gọi lệnh AutoCAD.</summary>
internal sealed class AcadCommandHandler : ICommand
{
#pragma warning disable CS0067 // ICommand yêu cầu sự kiện này dù Ribbon không dùng tới.
    public event EventHandler? CanExecuteChanged;
#pragma warning restore CS0067

    // Luôn true: Ribbon đánh giá CanExecute lúc dựng nút — thời điểm add-in nạp qua bundle thì AutoCAD
    // CHƯA mở bản vẽ nào, nên nếu trả về false ở đây thì nút bị vô hiệu vĩnh viễn (không có
    // CanExecuteChanged nào đánh thức lại). Việc thiếu document được Execute() xử lý.
    public bool CanExecute(object? parameter) => true;

    public void Execute(object? parameter)
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        if (doc is null)
        {
            return;
        }

        // Ribbon của Autodesk truyền chính RibbonButton vào đây, KHÔNG phải chuỗi CommandParameter —
        // nhận nhầm kiểu thì nút bấm không ra gì mà cũng không báo lỗi.
        var macro = parameter switch
        {
            RibbonButton button => button.CommandParameter as string,
            string text => text,
            _ => null,
        };

        if (string.IsNullOrEmpty(macro))
        {
            return;
        }

        doc.SendStringToExecute(macro, true, false, true);
    }
}
