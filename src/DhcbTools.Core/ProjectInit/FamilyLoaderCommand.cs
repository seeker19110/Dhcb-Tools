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

            string[] allRfa = Directory.GetFiles(config.FamilyFolder, "*.rfa", SearchOption.AllDirectories)
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToArray();
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

            var messages = new List<string>();
            var loaded = 0;
            // Cùng vòng quyết định cho preview và chạy thật; nạp thử rồi rollback kiểm được RFA hỏng
            // hoặc host từ chối nạp. Một transaction cho cả lệnh để lỗi không để lại nửa bộ family.
            using var tx = RevitCompat.StartTransaction(doc, "DHCB - Nạp family hàng loạt");
            try
            {
                foreach (string rfaPath in rfaFiles)
                {
                    var famName = Path.GetFileNameWithoutExtension(rfaPath);
                    if (!config.OverwriteExisting && existingFamilies.Contains(famName))
                    {
                        messages.Add("[Bỏ qua, đã có] " + famName);
                        continue;
                    }

                    var ok = doc.LoadFamily(rfaPath, new LoadOptions(config.OverwriteExisting), out var family);
                    if (!ok && family == null)
                        throw new InvalidOperationException("Revit từ chối nạp family: " + rfaPath);
                    loaded++;
                    existingFamilies.Add(family?.Name ?? famName);
                    messages.Add((config.DryRun ? "[Xem trước] Sẽ nạp: " : "[OK] ") + famName);
                }

                if (config.DryRun) tx.RollBack();
                else tx.Commit();
            }
            catch (Exception ex)
            {
                var status = tx.RollBack();
                var state = status == TransactionStatus.RolledBack
                    ? "đã hoàn tác toàn bộ"
                    : "trạng thái transaction " + status + ", cần kiểm tra mô hình";
                return CommandResult.Fail("Không nạp trọn vẹn bộ family; " + state + ": " + ex.Message)
                    .WithMessages(messages);
            }

            string prefix = config.DryRun ? "[Xem trước] " : string.Empty;
            return CommandResult.Ok(prefix + "Nạp " + loaded + " family.", loaded).WithMessages(messages);
        }
    }
}
