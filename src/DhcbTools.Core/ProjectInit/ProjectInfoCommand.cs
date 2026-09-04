using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace DhcbTools.Core.ProjectInit
{
    public sealed class ProjectInfoCommand : ICoreCommand<ProjectInfoConfig>
    {
        public string CommandName => "ProjectInfo";

        public CommandResult Execute(Document doc, ProjectInfoConfig config)
        {
            var planned = new List<string>();
            var skipped = new List<string>();

            using (var tx = new Transaction(doc, "DHCB - Gán thông tin dự án"))
            {
                RevitCompat.ApplyFailurePolicy(tx);
                tx.Start();
                try
                {
                    var pi = doc.ProjectInformation;
                    Set(pi.Number, config.ProjectNumber, "Project Number", v => pi.Number = v, planned);
                    Set(pi.Name, config.ProjectName, "Project Name", v => pi.Name = v, planned);
                    Set(pi.Status, config.ProjectStatus, "Project Status", v => pi.Status = v, planned);
                    Set(pi.ClientName, config.ClientName, "Client Name", v => pi.ClientName = v, planned);
                    Set(pi.BuildingName, config.BuildingName, "Building Name", v => pi.BuildingName = v, planned);
                    Set(pi.Address, config.Address, "Address", v => pi.Address = v, planned);
                    Set(pi.OrganizationName, config.OrganizationName, "Organization Name", v => pi.OrganizationName = v, planned);

                    foreach (var kvp in config.ExtraParameters)
                    {
                        var param = RevitCompat.LookupInstance(pi, kvp.Key, kvp.Key);
                        if (param == null)
                        {
                            // Trước đây bỏ qua im lặng: kỹ sư khai một tham số sai tên vẫn nhận "thành công".
                            skipped.Add($"E-PARAM-MISSING: Project Information không có tham số \"{kvp.Key}\".");
                            continue;
                        }

                        if (param.IsReadOnly)
                        {
                            skipped.Add($"E-PARAM-READONLY: tham số \"{kvp.Key}\" chỉ đọc.");
                            continue;
                        }

                        param.Set(kvp.Value);
                        planned.Add($"{kvp.Key} = \"{kvp.Value}\"");
                    }

                    if (config.DryRun)
                    {
                        tx.RollBack();
                    }
                    else
                    {
                        tx.Commit();
                    }
                }
                catch (System.Exception ex)
                {
                    tx.RollBack();
                    return CommandResult.Fail("Lỗi khi ghi thông tin dự án: " + ex.Message);
                }
            }

            if (planned.Count == 0 && skipped.Count > 0)
            {
                var failure = CommandResult.Fail("Không ghi được trường thông tin dự án nào.");
                failure.Errors.AddRange(skipped);
                return failure;
            }

            var summary = config.DryRun
                ? $"[Xem trước] Sẽ ghi {planned.Count} trường thông tin dự án."
                : $"Đã ghi {planned.Count} trường thông tin dự án.";

            var result = CommandResult.Ok(summary, planned.Count);
            result.Messages.AddRange(planned);
            result.Errors.AddRange(skipped);
            return result;
        }

        private static void Set(string current, string? value, string label, System.Action<string> apply, List<string> planned)
        {
            if (string.IsNullOrEmpty(value) || value == current)
            {
                return;
            }

            apply(value!);
            planned.Add($"{label} = \"{value}\"");
        }
    }
}
