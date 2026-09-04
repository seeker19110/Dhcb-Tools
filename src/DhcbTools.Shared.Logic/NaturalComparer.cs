using System;
using System.Collections.Generic;

namespace DhcbTools.Shared.Logic
{
    /// <summary>
    /// So sánh chuỗi "tự nhiên": <c>Level 2</c> đứng trước <c>Level 10</c>, không phân biệt hoa thường.
    /// Tên tầng/hệ/trục xuất hiện ở mọi báo cáo, nên thứ tự này phải giống nhau ở mọi chỗ — đó là lý do
    /// nó nằm ở đây chứ không nằm trong một lệnh cụ thể.
    /// </summary>
    public sealed class NaturalComparer : IComparer<string>
    {
        public static readonly NaturalComparer Instance = new NaturalComparer();

        public int Compare(string? x, string? y)
        {
            x = x ?? string.Empty;
            y = y ?? string.Empty;
            int i = 0, j = 0;
            while (i < x.Length && j < y.Length)
            {
                if (char.IsDigit(x[i]) && char.IsDigit(y[j]))
                {
                    var si = i;
                    var sj = j;
                    while (i < x.Length && char.IsDigit(x[i])) i++;
                    while (j < y.Length && char.IsDigit(y[j])) j++;
                    var nx = x.Substring(si, i - si).TrimStart('0');
                    var ny = y.Substring(sj, j - sj).TrimStart('0');
                    if (nx.Length != ny.Length)
                    {
                        return nx.Length.CompareTo(ny.Length);
                    }

                    var c = string.CompareOrdinal(nx, ny);
                    if (c != 0)
                    {
                        return c;
                    }
                }
                else
                {
                    var c = char.ToUpperInvariant(x[i]).CompareTo(char.ToUpperInvariant(y[j]));
                    if (c != 0)
                    {
                        return c;
                    }

                    i++;
                    j++;
                }
            }

            return (x.Length - i).CompareTo(y.Length - j);
        }
    }
}
