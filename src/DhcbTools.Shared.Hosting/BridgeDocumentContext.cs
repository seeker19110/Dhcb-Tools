using System;
using System.Runtime.CompilerServices;
using DhcbTools.Shared.Logic.Ai;
using Newtonsoft.Json.Linq;

namespace DhcbTools.Shared.Hosting
{
    /// <summary>Định danh theo đối tượng document trong phiên mở, không theo tên/đường dẫn file.</summary>
    public static class BridgeDocumentContext
    {
        private sealed class Identity { public string Id { get; } = Guid.NewGuid().ToString("N"); }
        private static readonly ConditionalWeakTable<object, Identity> Identities = new ConditionalWeakTable<object, Identity>();

        public static string IdFor(object document) => Identities.GetValue(document, _ => new Identity()).Id;

        public static string? Validate(string app, BridgeRequest request, string currentId)
        {
            if (!string.IsNullOrWhiteSpace(request.DocumentId))
                return string.Equals(request.DocumentId, currentId, StringComparison.Ordinal) ? null
                    : "E-DOCUMENT-CHANGED: Mô hình đang mở khác phiên đã chọn. Đọc lại document_context và xem trước lại.";

            var descriptor = CommandCatalog.Find(app, request.Command);
            var dryRun = request.Config?.GetValue("dryRun", StringComparison.OrdinalIgnoreCase);
            if (descriptor?.WritesModel == false || (dryRun?.Type == JTokenType.Boolean && dryRun.Value<bool>()))
                return null;
            return "E-DOCUMENT-REQUIRED: Lệnh ghi cần documentId từ truy vấn document_context.";
        }
    }
}
