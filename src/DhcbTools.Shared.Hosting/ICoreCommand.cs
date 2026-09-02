namespace DhcbTools.Shared.Hosting
{
    /// <summary>
    /// Hợp đồng chung cho mọi lệnh Core: nhận document (Revit <c>Document</c> hoặc AutoCAD <c>Database</c>)
    /// + config, tự mở transaction, trả <see cref="CommandResult"/>. Không TaskDialog, không Editor,
    /// không WPF — để cùng một lệnh chạy được từ Ribbon, HTTP Bridge và batch runner.
    /// </summary>
    public interface ICoreCommand<in TConfig, in TDocument>
    {
        /// <summary>Tên duy nhất của lệnh — khoá tra cứu của Bridge, batch runner và lớp AI.</summary>
        string CommandName { get; }

        CommandResult Execute(TDocument document, TConfig config);
    }
}
