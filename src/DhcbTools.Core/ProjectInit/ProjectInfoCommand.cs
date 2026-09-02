using Autodesk.Revit.DB;

namespace DhcbTools.Core.ProjectInit
{
    public sealed class ProjectInfoCommand : ICoreCommand<ProjectInfoConfig>
    {
        public string CommandName => "ProjectInfo";

        public CommandResult Execute(Document doc, ProjectInfoConfig config)
        {
            int count = 0;
            using (var tx = new Transaction(doc, "DHCB - Gan thong tin du an"))
            {
                RevitCompat.ApplyFailurePolicy(tx);
                tx.Start();
                try
                {
                    var pi = doc.ProjectInformation;
                    if (!string.IsNullOrEmpty(config.ProjectNumber))    { pi.Number           = config.ProjectNumber;    count++; }
                    if (!string.IsNullOrEmpty(config.ProjectName))      { pi.Name             = config.ProjectName;      count++; }
                    if (!string.IsNullOrEmpty(config.ProjectStatus))    { pi.Status           = config.ProjectStatus;    count++; }
                    if (!string.IsNullOrEmpty(config.ClientName))       { pi.ClientName       = config.ClientName;       count++; }
                    if (!string.IsNullOrEmpty(config.BuildingName))     { pi.BuildingName     = config.BuildingName;     count++; }
                    if (!string.IsNullOrEmpty(config.Address))          { pi.Address          = config.Address;          count++; }
                    if (!string.IsNullOrEmpty(config.OrganizationName)) { pi.OrganizationName = config.OrganizationName; count++; }

                    foreach (var kvp in config.ExtraParameters)
                    {
                        var param = pi.LookupParameter(kvp.Key);
                        if (param != null && !param.IsReadOnly) { param.Set(kvp.Value); count++; }
                    }
                    tx.Commit();
                }
                catch (System.Exception ex)
                {
                    tx.RollBack();
                    return CommandResult.Fail("Error setting project info: " + ex.Message);
                }
            }
            return CommandResult.Ok("Set " + count + " project info field(s).", count);
        }
    }
}
