using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace DhcbTools.Shared.Logic.Handover
{
    /// <summary>
    /// Báo cáo kết quả kiểm tra tính đầy đủ và hợp lệ của gói hồ sơ bàn giao theo Nghị định 207/2026.
    /// </summary>
    public sealed class HandoverValidationResult
    {
        public HandoverValidationResult(
            bool isPassed,
            int totalFiles,
            int verifiedHashes,
            IReadOnlyList<string> missingMandatoryKinds,
            IReadOnlyList<string> hashMismatches,
            IReadOnlyList<string> auditNotes,
            IReadOnlyList<string>? missingFiles = null,
            IReadOnlyList<string>? unboundSheets = null)
        {
            IsPassed = isPassed;
            TotalFiles = totalFiles;
            VerifiedHashes = verifiedHashes;
            MissingMandatoryKinds = missingMandatoryKinds ?? Array.Empty<string>();
            HashMismatches = hashMismatches ?? Array.Empty<string>();
            AuditNotes = auditNotes ?? Array.Empty<string>();
            MissingFiles = missingFiles ?? Array.Empty<string>();
            UnboundSheets = unboundSheets ?? Array.Empty<string>();
        }

        public bool IsPassed { get; }
        public int TotalFiles { get; }

        /// <summary>Số file có mặt trên đĩa VÀ băm khớp manifest. File thiếu không bao giờ được đếm vào đây.</summary>
        public int VerifiedHashes { get; }

        public IReadOnlyList<string> MissingMandatoryKinds { get; }
        public IReadOnlyList<string> HashMismatches { get; }
        public IReadOnlyList<string> AuditNotes { get; }

        /// <summary>File có trong manifest nhưng không có trên đĩa (hoặc đường dẫn thoát ra ngoài thư mục gói).</summary>
        public IReadOnlyList<string> MissingFiles { get; }

        /// <summary>Bản vẽ trong danh mục hoàn công mà gói không có file PDF/DWG nào mang số hiệu đó.</summary>
        public IReadOnlyList<string> UnboundSheets { get; }

        public string Summary
        {
            get
            {
                if (IsPassed)
                {
                    return $"ĐẠT CHUẨN NĐ 207: Đã xác minh {VerifiedHashes}/{TotalFiles} file, đầy đủ hồ sơ pháp lý.";
                }

                var parts = new List<string>();
                if (MissingMandatoryKinds.Count > 0)
                {
                    parts.Add($"thiếu loại file [{string.Join(", ", MissingMandatoryKinds)}]");
                }

                if (MissingFiles.Count > 0)
                {
                    parts.Add($"{MissingFiles.Count} file trong manifest không có trên đĩa");
                }

                if (HashMismatches.Count > 0)
                {
                    parts.Add($"{HashMismatches.Count} file sai lệch chuỗi băm HashChain");
                }

                if (UnboundSheets.Count > 0)
                {
                    parts.Add($"{UnboundSheets.Count} bản vẽ trong danh mục chưa có file");
                }

                return "KHÔNG ĐẠT: " + string.Join("; ", parts) + $" (đã xác minh {VerifiedHashes}/{TotalFiles} file).";
            }
        }
    }

    /// <summary>
    /// Bộ kiểm tra hợp lệ hồ sơ bàn giao BIM theo NĐ 207/2026: đủ loại file bắt buộc, mọi file trong manifest
    /// có trên đĩa và băm khớp, và mỗi bản vẽ trong danh mục hoàn công có file đi kèm.
    /// Trước đây file thiếu trên đĩa được đếm là "đã xác minh" — xoá một file khỏi gói mà báo cáo vẫn ĐẠT.
    /// </summary>
    public static class HandoverPackageValidator
    {
        /// <summary>
        /// Danh mục loại file bắt buộc phải có trong gói bàn giao theo Nghị định 207.
        /// </summary>
        public static readonly IReadOnlyList<string> MandatoryFileKinds = new[] { "IFC", "PDF", "CSV", "JSON" };

        /// <summary>Loại file được coi là "bản vẽ" khi đối chiếu danh mục.</summary>
        private static readonly HashSet<string> DrawingKinds = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "PDF", "DWG", "DXF" };

        /// <summary>
        /// Thực hiện kiểm tra tính hợp lệ và xác minh chuỗi băm của gói bàn giao.
        /// </summary>
        /// <param name="baseFolder">Thư mục gói. Rỗng hoặc không tồn tại → không xác minh được file nào, gói KHÔNG đạt.</param>
        /// <param name="files">Manifest.</param>
        /// <param name="sheetIndex">Danh mục bản vẽ hoàn công; null/rỗng thì bỏ qua bước đối chiếu.</param>
        /// <param name="hashOf">
        /// Hàm băm SHA-256 hex của một đường dẫn — tiêm được để test; null = <see cref="HandoverPackage.Sha256Of"/>.
        /// </param>
        public static HandoverValidationResult ValidatePackage(
            string? baseFolder,
            IReadOnlyList<HandoverFile>? files,
            IReadOnlyList<SheetIndexRow>? sheetIndex,
            Func<string, string>? hashOf = null)
        {
            if (files == null || files.Count == 0)
            {
                return new HandoverValidationResult(
                    isPassed: false,
                    totalFiles: 0,
                    verifiedHashes: 0,
                    missingMandatoryKinds: MandatoryFileKinds,
                    hashMismatches: Array.Empty<string>(),
                    auditNotes: new[] { "Gói bàn giao không chứa bất kỳ file nào." });
            }

            hashOf ??= HandoverPackage.Sha256Of;

            var presentKinds = new HashSet<string>(files.Select(f => (f.Kind ?? string.Empty).ToUpperInvariant()));
            var missingKinds = MandatoryFileKinds.Where(k => !presentKinds.Contains(k)).ToList();

            var hashMismatches = new List<string>();
            var missingFiles = new List<string>();
            var notes = new List<string>();
            var verifiedCount = 0;

            var root = ResolveRoot(baseFolder);
            if (root == null)
            {
                notes.Add("Không có thư mục gói để đối chiếu — không xác minh được file nào trên đĩa.");
            }

            foreach (var file in files)
            {
                var relative = file.RelativePath ?? string.Empty;
                var fullPath = root == null ? null : ResolveInside(root, relative);
                if (fullPath == null)
                {
                    missingFiles.Add(root == null ? relative : relative + " (đường dẫn nằm ngoài thư mục gói)");
                    continue;
                }

                if (!File.Exists(fullPath))
                {
                    missingFiles.Add(relative);
                    continue;
                }

                var actualHash = hashOf(fullPath);
                if (!string.Equals(actualHash, file.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    hashMismatches.Add($"{relative} (kỳ vọng {Short(file.Sha256)}, thực tế {Short(actualHash)})");
                }
                else
                {
                    verifiedCount++;
                }
            }

            var unboundSheets = new List<string>();
            if (sheetIndex != null && sheetIndex.Count > 0)
            {
                var drawingNames = files
                    .Where(f => DrawingKinds.Contains(f.Kind ?? string.Empty))
                    .Select(f => Path.GetFileNameWithoutExtension(f.RelativePath ?? string.Empty))
                    .ToList();

                foreach (var sheet in sheetIndex)
                {
                    var number = (sheet.Number ?? string.Empty).Trim();
                    if (number.Length == 0)
                    {
                        continue;
                    }

                    var sanitized = FileNaming.Sanitize(number);
                    var bound = drawingNames.Any(n =>
                        n.IndexOf(number, StringComparison.OrdinalIgnoreCase) >= 0
                        || n.IndexOf(sanitized, StringComparison.OrdinalIgnoreCase) >= 0);
                    if (!bound)
                    {
                        unboundSheets.Add(number);
                    }
                }

                notes.Add($"Đã đối chiếu danh mục {sheetIndex.Count.ToString(CultureInfo.InvariantCulture)} bản vẽ hoàn công với file trong gói: "
                          + (unboundSheets.Count == 0
                              ? "tất cả đều có file."
                              : $"{unboundSheets.Count.ToString(CultureInfo.InvariantCulture)} bản vẽ chưa có file PDF/DWG mang số hiệu."));
            }

            var isPassed = missingKinds.Count == 0
                           && hashMismatches.Count == 0
                           && missingFiles.Count == 0
                           && unboundSheets.Count == 0
                           && verifiedCount == files.Count;

            return new HandoverValidationResult(
                isPassed: isPassed,
                totalFiles: files.Count,
                verifiedHashes: verifiedCount,
                missingMandatoryKinds: missingKinds,
                hashMismatches: hashMismatches,
                auditNotes: notes,
                missingFiles: missingFiles,
                unboundSheets: unboundSheets);
        }

        private static string? ResolveRoot(string? baseFolder)
        {
            if (string.IsNullOrWhiteSpace(baseFolder) || !Directory.Exists(baseFolder))
            {
                return null;
            }

            var full = Path.GetFullPath(baseFolder!);
            return full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        }

        /// <summary>Ghép đường dẫn tương đối vào gói; trả null nếu tuyệt đối hoặc thoát ra ngoài (<c>..</c>).</summary>
        internal static string? ResolveInside(string root, string relative)
        {
            if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative))
            {
                return null;
            }

            var full = Path.GetFullPath(Path.Combine(root, relative));
            return full.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? full : null;
        }

        private static string Short(string? hash)
        {
            if (string.IsNullOrEmpty(hash))
            {
                return "(trống)";
            }

            return hash!.Length > 8 ? hash.Substring(0, 8) : hash;
        }
    }
}
