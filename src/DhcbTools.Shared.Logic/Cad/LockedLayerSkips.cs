using System;
using System.Collections.Generic;
using System.Linq;

namespace DhcbTools.Shared.Logic.Cad
{
    /// <summary>
    /// Đếm đối tượng mà lệnh ghi AutoCAD BỎ QUA vì nằm trên layer đang khoá, rồi nói ra thành một câu.
    /// <para>
    /// Audit 2026-10-01 vòng 2: mở để ghi một entity/attribute trên layer khoá thì AutoCAD ném <c>eOnLockedLayer</c>.
    /// TextReplace, AttributeImport, AutoNumbering/AttributeIncrement và LayerTranslate đều mở thẳng
    /// <c>ForWrite</c>/<c>UpgradeOpen</c> nên MỘT chữ trên layer khung tên bị khoá làm sập cả lệnh với thông báo khó
    /// hiểu. Giờ các lệnh tôn trọng khoá như lệnh FIND của AutoCAD: bỏ qua, và xem trước lẫn chạy thật cùng báo
    /// số đối tượng bỏ qua theo từng layer — nên con số xem trước khớp con số chạy thật.
    /// </para>
    /// </summary>
    public sealed class LockedLayerSkips
    {
        public const int MaxLayers = 5;

        private readonly Dictionary<string, int> _byLayer = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        public int Count { get; private set; }

        public void Add(string layer)
        {
            var name = layer ?? string.Empty;
            _byLayer[name] = _byLayer.TryGetValue(name, out var n) ? n + 1 : 1;
            Count++;
        }

        /// <summary>Câu báo cho kỹ sư; <c>null</c> khi không bỏ qua gì. <paramref name="what"/>: "đối tượng văn bản", "attribute"…</summary>
        public string? Message(string what)
        {
            if (Count == 0)
            {
                return null;
            }

            var top = _byLayer
                .OrderByDescending(p => p.Value)
                .ThenBy(p => p.Key, StringComparer.OrdinalIgnoreCase)
                .Take(MaxLayers)
                .Select(p => $"\"{p.Key}\" ×{p.Value}");
            var more = _byLayer.Count > MaxLayers ? $" … và {_byLayer.Count - MaxLayers} layer khác" : string.Empty;
            return $"Bỏ qua {Count} {what} nằm trên layer đang khoá ({string.Join(", ", top)}{more}) — "
                   + "mở khoá layer rồi chạy lại nếu muốn sửa cả chúng.";
        }
    }
}
