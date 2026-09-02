using Autodesk.Revit.DB;

namespace DhcbTools.Core;

/// <summary>
/// Lệnh Core Revit: <see cref="Shared.Hosting.ICoreCommand{TConfig, TDocument}"/> với TDocument = <see cref="Document"/>.
/// Giữ tên ngắn để mọi lệnh hiện có không phải đổi chữ ký.
/// </summary>
public interface ICoreCommand<in TConfig> : Shared.Hosting.ICoreCommand<TConfig, Document>
{
}
