using System;

namespace DhcbTools.Shared.Logic.Cad
{
    /// <summary>Phân trang trên dòng dữ liệu đã lọc; chỉ đọc thêm một record để biết còn trang sau.</summary>
    public sealed class QueryPage
    {
        public const int DefaultLimit = 2000;
        public const int MaxLimit = 10000;
        private long _matched;
        public QueryPage(int limit = 0, int offset = 0)
        {
            if (limit < 0 || limit > MaxLimit) throw new ArgumentOutOfRangeException(nameof(limit));
            if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset));
            Limit = limit == 0 ? DefaultLimit : limit;
            Offset = offset;
        }
        public int Limit { get; }
        public int Offset { get; }
        public int Count { get; private set; }
        public bool HasMore { get; private set; }
        public long? NextOffset => HasMore ? (long)Offset + Count : (long?)null;
        public bool IncludeMatch()
        {
            if (_matched++ < Offset) return false;
            if (Count >= Limit) { HasMore = true; return false; }
            Count++;
            return true;
        }
    }
}
