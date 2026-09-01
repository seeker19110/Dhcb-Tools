using Autodesk.AutoCAD.DatabaseServices;

namespace DhcbTools.Core.AutoCAD;

/// <summary>
/// Hợp đồng chung cho mọi lệnh Core AutoCAD: nhận Database + config, tự mở Transaction,
/// trả về CommandResult. Không có Editor, không có WPF — để cùng một lệnh chạy được từ
/// Ribbon AutoCAD lẫn từ batch runner mà không cần viết lại.
/// </summary>
public interface ICoreCommand<in TConfig>
{
    string CommandName { get; }

    CommandResult Execute(Database database, TConfig config);
}
