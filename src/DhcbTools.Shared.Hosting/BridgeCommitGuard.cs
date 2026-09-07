using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using DhcbTools.Shared.Logic.Ai;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DhcbTools.Shared.Hosting
{
    /// <summary>
    /// Chạy trên luồng host, ngay trước dispatch. Token preview dùng một lần cũng là khóa chống lặp.
    /// Ghi claim và Flush xuống đĩa TRƯỚC dispatch; claim không có kết quả là trạng thái không xác định,
    /// tuyệt đối không thực thi lại. Kết quả đã xong có thể đọc lại sau khi Bridge khởi động lại.
    /// </summary>
    public sealed class BridgeCommitGuard
    {
        private sealed class Plan
        {
            public string Fingerprint = "";
            public string Snapshot = "";
            public DateTime ExpiresUtc;
        }

        private readonly object _gate = new object();
        private readonly Dictionary<string, Plan> _plans = new Dictionary<string, Plan>();
        private readonly string _directory;
        private readonly Func<DateTime> _utcNow;
        public TimeSpan Lifetime { get; set; } = TimeSpan.FromMinutes(10);
        public int MaxPlans { get; set; } = 256;

        public BridgeCommitGuard(string directory, Func<DateTime>? utcNow = null)
        {
            _directory = directory;
            _utcNow = utcNow ?? (() => DateTime.UtcNow);
        }

        public static string DefaultDirectory(string app) => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DHCB", "bridge-commits", app);

        public CommandResult Execute(string app, BridgeRequest request, string currentDocumentId,
            Func<long> revision, Func<BridgeRequest, CommandResult> dispatch)
        {
            lock (_gate)
            {
                try
                {
                    return ExecuteCore(app, request, currentDocumentId, revision, dispatch);
                }
                catch (Exception)
                {
                    // Có thể đã dispatch: không đoán "chưa chạy". Claim nếu có được giữ nguyên.
                    return CommandResult.Fail("E-COMMIT-UNKNOWN: Không xác minh được trạng thái lưu/ghi. Giữ token, kiểm tra mô hình; không tự gửi lại bằng token mới.");
                }
            }
        }

        private CommandResult ExecuteCore(string app, BridgeRequest request, string currentDocumentId,
            Func<long> revision, Func<BridgeRequest, CommandResult> dispatch)
        {
            var descriptor = CommandCatalog.Find(app, request.Command);
            if (descriptor == null || !descriptor.Implemented || descriptor.Internal)
                return CommandResult.Fail("E-PREVIEW-INVALID: Lệnh không thuộc danh mục Bridge công khai.");

            var config = (JObject?)request.Config?.DeepClone() ?? new JObject();
            if (config.Properties().GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1))
                return CommandResult.Fail("E-PREVIEW-INVALID: Config có khóa trùng không phân biệt hoa thường.");
            var dryValue = config.GetValue("dryRun", StringComparison.OrdinalIgnoreCase);
            if (dryValue != null && dryValue.Type != JTokenType.Boolean)
                return CommandResult.Fail("E-PREVIEW-INVALID: dryRun phải là boolean.");
            var dry = dryValue == null || dryValue.Value<bool>();
            var dryProperty = config.Properties().FirstOrDefault(p => p.Name.Equals("dryRun", StringComparison.OrdinalIgnoreCase));
            dryProperty?.Remove();
            var canonical = Canonical(config).ToString(Formatting.None);
            config["dryRun"] = dry;
            var normalized = new BridgeRequest { Command = descriptor.Name, Config = config };
            if (!descriptor.WritesModel)
                return dispatch(normalized);

            var documentId = request.DocumentId ?? (dry ? currentDocumentId : "");
            var fingerprint = Hash(new JArray(app.ToLowerInvariant(), descriptor.Name, documentId, canonical).ToString(Formatting.None));
            string? token = null;
            string? claimPath = null;
            if (!dry)
            {
                if (!Guid.TryParseExact(request.PreviewToken, "N", out var id) || string.IsNullOrWhiteSpace(documentId))
                    return CommandResult.Fail("E-PREVIEW-REQUIRED: Ghi cần previewToken và documentId từ kết quả preview đã duyệt.");
                token = id.ToString("N");
                claimPath = Path.Combine(_directory, token + ".claim");
                // Replay phải đi trước kiểm revision: chính lần ghi đầu có thể đã đổi model.
                if (File.Exists(claimPath))
                    return Replay(claimPath, fingerprint);
            }

            if (documentId != currentDocumentId)
                return CommandResult.Fail("E-DOCUMENT-CHANGED: Mô hình hiện hành khác model đã preview.");

            var now = _utcNow();
            foreach (var key in _plans.Where(p => p.Value.ExpiresUtc <= now).Select(p => p.Key).ToArray())
                _plans.Remove(key);
            if (!dry && (!_plans.TryGetValue(token!, out var found) || found.Fingerprint != fingerprint))
                return CommandResult.Fail("E-PREVIEW-INVALID: Token hết hạn, không tồn tại hoặc cấu hình khác preview. Xem trước lại.");

            var snapshot = Capture(config, revision());
            if (dry)
            {
                if (_plans.Count >= MaxPlans)
                    return CommandResult.Fail("E-PREVIEW-CAPACITY: Quá nhiều preview còn hiệu lực. Đợi hết hạn rồi thử lại.");
                var result = dispatch(normalized);
                if (!result.Success || result.PartialSuccess || result.Errors.Count > 0)
                    return result;
                if (snapshot != Capture(config, revision()))
                    return CommandResult.Fail("E-PREVIEW-CHANGED: Mô hình/file đã đổi trong lúc preview. Xem trước lại.");
                token = Guid.NewGuid().ToString("N");
                var expires = _utcNow().Add(Lifetime);
                _plans.Add(token, new Plan { Fingerprint = fingerprint, Snapshot = snapshot, ExpiresUtc = expires });
                result.PreviewToken = token;
                result.DocumentId = documentId;
                result.PreviewExpiresUtc = expires;
                return result;
            }

            if (_plans[token!].Snapshot != snapshot)
            {
                _plans.Remove(token!);
                return CommandResult.Fail("E-PREVIEW-CHANGED: Mô hình/file đầu vào đã đổi sau preview. Xem trước lại.");
            }
            Directory.CreateDirectory(_directory);
            // FileMode.CreateNew là rào giữa các instance, không chỉ lock trong tiến trình này.
            try { Persist(claimPath!, fingerprint); }
            catch (IOException) when (File.Exists(claimPath)) { return Replay(claimPath!, fingerprint); }
            var committed = dispatch(normalized);
            Persist(claimPath! + ".result", JsonConvert.SerializeObject(committed));
            _plans.Remove(token!);
            return committed;
        }

        private static CommandResult Replay(string claimPath, string fingerprint)
        {
            if (File.ReadAllText(claimPath) != fingerprint)
                return CommandResult.Fail("E-PREVIEW-INVALID: Token đã dùng với cấu hình/model khác.");
            if (!File.Exists(claimPath + ".result"))
                return CommandResult.Fail("E-COMMIT-UNKNOWN: Lệnh đã được nhận nhưng chưa có kết quả bền vững. Không thực thi lại; kiểm tra mô hình.");
            var result = JsonConvert.DeserializeObject<CommandResult>(File.ReadAllText(claimPath + ".result"));
            if (result == null) throw new InvalidDataException("Missing result");
            return result;
        }

        private static void Persist(string path, string text)
        {
            using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                var bytes = Encoding.UTF8.GetBytes(text);
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }
        }

        private static JToken Canonical(JToken token)
        {
            if (token is JObject obj)
                return new JObject(obj.Properties().OrderBy(p => p.Name, StringComparer.Ordinal)
                    .Select(p => new JProperty(p.Name, Canonical(p.Value))));
            if (token is JArray array) return new JArray(array.Select(Canonical));
            return token.DeepClone();
        }

        private static string Capture(JObject config, long revision)
        {
            var paths = new List<string>();
            foreach (var p in config.Properties())
            {
                // Cả trường chưa lên catalog cũng được xét. Đầu ra không phải đầu vào preview.
                if (p.Name.StartsWith("output", StringComparison.OrdinalIgnoreCase)
                    || new[] { "reportPath", "htmlPath", "csvPath", "dxfPath" }.Contains(p.Name, StringComparer.OrdinalIgnoreCase))
                    continue;
                var kind = FieldKindGuess.Of(p.Name);
                if (kind != FieldKind.FilePath && kind != FieldKind.FolderPath
                    && !p.Name.EndsWith("Path", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (p.Value.Type == JTokenType.Null) continue;
                if (p.Value.Type != JTokenType.String) throw new InvalidDataException("Path must be a string");
                var path = p.Value.Value<string>();
                if (string.IsNullOrWhiteSpace(path)) continue;
                paths.Add(path!);
                if (Directory.Exists(path))
                {
                    // Không đi theo symlink/junction. Thư mục đầu vào được chụp toàn bộ, kể cả file mới.
                    AddDirectory(path!, paths);
                }
            }
            return Hash(revision.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + PreviewSnapshot.Capture("", paths));
        }

        private static void AddDirectory(string directory, List<string> paths)
        {
            if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Linked input directory");
            foreach (var file in Directory.GetFiles(directory))
            {
                if (paths.Count >= 4096) throw new IOException("Input directory too large");
                paths.Add(file);
            }
            foreach (var child in Directory.GetDirectories(directory)) AddDirectory(child, paths);
        }

        private static string Hash(string text)
        {
            using (var sha = SHA256.Create())
                return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(text)));
        }
    }
}
