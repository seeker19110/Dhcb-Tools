using DhcbTools.Shared.Hosting;
using Newtonsoft.Json.Linq;

namespace DhcbTools.AutoCAD.UI;

/// <summary>UI contract: all database work stays on the AutoCAD command thread.</summary>
public interface ICadCommandContext
{
    string DocumentName { get; }
    string DocumentId { get; }
    long Revision { get; }
    bool IsActive { get; }
    CommandResult Execute(string command, JObject config);
}
