using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;

namespace DhcbTools.Core.ProjectInit
{
    public sealed class FamilyLoaderCommand : ICoreCommand<FamilyLoaderConfig>
    {
        public string CommandName => "FamilyLoader";

        /// <summary>
        /// Trả lời sẵn hộp thoại "Family đã có trong dự án — ghi đè?": không có lớp này thì
        /// <c>LoadFamily</c> hiện TaskDialog và treo Bridge/batch chờ người bấm.
        /// </summary>
        private sealed class LoadOptions : IFamilyLoadOptions
        {
            private readonly bool _overwrite;

            public LoadOptions(bool overwrite) => _overwrite = overwrite;

            public bool OnFamilyFound(bool familyInUse, out bool overwriteParameterValues)
            {
                overwriteParameterValues = _overwrite;
                return true;
            }

            public bool OnSharedFamilyFound(Family sharedFamily, bool familyInUse, out FamilySource source, out bool overwriteParameterValues)
            {
                source = FamilySource.Family;
                overwriteParameterValues = _overwrite;
                return true;
            }
        }

        public CommandResult Execute(Document doc, FamilyLoaderConfig config)
        {
            if (!Directory.Exists(config.FamilyFolder))
                return CommandResult.Fail($"E-PATH-MISSING: không tìm thấy thư mục family \"{config.FamilyFolder}\".");

            string[] allRfa = Directory.GetFiles(config.FamilyFolder, "*.rfa", SearchOption.AllDirectories);
            IEnumerable<string> rfaFiles = allRfa;
            if (config.FamilyNames.Count > 0)
            {
                var nameSet = new HashSet<string>(config.FamilyNames, StringComparer.OrdinalIgnoreCase);
                rfaFiles = allRfa.Where(f => nameSet.Contains(Path.GetFileNameWithoutExtension(f)));
            }

            var existingFamilies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Family fam in new FilteredElementCollector(doc)
                         .OfClass(typeof(Family)).ToElements().Cast<Family>())
                existingFamilies.Add(fam.Name);

            var messages = new StringBuilder();
            int loaded = 0;

            foreach (string rfaPath in rfaFiles)
            {
                string famName = Path.GetFileNameWithoutExtension(rfaPath);
                if (!config.OverwriteExisting && existingFamilies.Contains(famName))
                { messages.AppendLine("[Bỏ qua, đã có] " + famName); continue; }

                if (config.DryRun)
                { messages.AppendLine("[Xem trước] Sẽ nạp: " + famName); loaded++; continue; }

                using (var tx = new Transaction(doc, "DHCB - Nạp family: " + famName))
                {
                    RevitCompat.ApplyFailurePolicy(tx);
                    tx.Start();
                    try
                    {
                        Family outFam;
                        bool ok = doc.LoadFamily(rfaPath, new LoadOptions(config.OverwriteExisting), out outFam);
                        if (ok || outFam != null) { loaded++; existingFamilies.Add(famName); messages.AppendLine("[OK] " + famName); }
                        else { messages.AppendLine("[Cảnh báo] Revit trả về false khi nạp: " + famName); }
                        tx.Commit();
                    }
                    catch (System.Exception ex) { tx.RollBack(); messages.AppendLine("[Lỗi] " + famName + ": " + ex.Message); }
                }
            }

            string prefix = config.DryRun ? "[Xem trước] " : string.Empty;
            return CommandResult.Ok(prefix + "Nạp " + loaded + " family." + Environment.NewLine + messages, loaded);
        }
    }
}