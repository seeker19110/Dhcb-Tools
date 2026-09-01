using Autodesk.AutoCAD.DatabaseServices;

namespace DhcbTools.Core.AutoCAD;

/// <summary>
/// Lệnh Core AutoCAD: <see cref="Shared.Hosting.ICoreCommand{TConfig, TDocument}"/> với TDocument = <see cref="Database"/>.
/// </summary>
public interface ICoreCommand<in TConfig> : Shared.Hosting.ICoreCommand<TConfig, Database>
{
}
