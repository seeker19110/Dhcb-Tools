using Autodesk.AutoCAD.ApplicationServices;
using DhcbTools.AutoCAD.Bridge;
using DhcbTools.Core.AutoCAD;
using DhcbTools.Shared.Hosting;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DhcbTools.AutoCAD.UI;

internal sealed class AutoCadCommandContext : ICadCommandContext
{
    private readonly Document _document;
    private readonly object _context;
    public AutoCadCommandContext(Document document)
    {
        _document = document;
        _context = DhcbHttpBridge.TrackDocument(document.Database);
    }
    public string DocumentName => _document.Name;
    public string DocumentId => BridgeDocumentContext.IdFor(_context);
    public long Revision => BridgeDocumentContext.RevisionFor(_context);
    public bool IsActive
    {
        get
        {
            var current = Autodesk.AutoCAD.ApplicationServices.Core.Application.DocumentManager.MdiActiveDocument;
            return current != null && ReferenceEquals(DhcbHttpBridge.TrackDocument(current.Database), _context);
        }
    }
    public CommandResult Execute(string command, JObject config)
    {
        using (_document.LockDocument()) return AcadCommandTable.Dispatch(_document.Database, command, config.ToString(Formatting.None));
    }
}
