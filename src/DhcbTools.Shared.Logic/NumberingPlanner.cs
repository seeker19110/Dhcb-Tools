using System;
using System.Collections.Generic;
using System.Globalization;

namespace DhcbTools.Shared.Logic
{
    /// <summary>Hướng quét khi đánh số theo vị trí hình học.</summary>
    public enum ScanDirection
    {
        /// <summary>Quét từng hàng ngang, trên xuống dưới; trong mỗi hàng đi trái sang phải.</summary>
        LeftToRightThenTopToBottom,

        /// <summary>Quét từng cột dọc, trái sang phải; trong mỗi cột đi trên xuống dưới.</summary>
        TopToBottomThenLeftToRight,
    }

    /// <summary>Một phần tử cần đánh số, đã tách khỏi Revit API: chỉ còn khoá và toạ độ.</summary>
    public sealed class NumberingItem<TKey>
    {
        public NumberingItem(TKey key, double x, double y)
        {
            Key = key;
            X = x;
            Y = y;
        }

        public TKey Key { get; }

        public double X { get; }

        public double Y { get; }
    }

    /// <summary>Kết quả đánh số cho một phần tử.</summary>
    public sealed class NumberingAssignment<TKey>
    {
        public NumberingAssignment(TKey key, int number, string value)
        {
            Key = key;
            Number = number;
            Value = value;
        }

        public TKey Key { get; }

        public int Number { get; }

        public string Value { get; }
    }

    /// <summary>
    /// Thuật toán đánh số theo vị trí. Tách khỏi <c>AutoNumberingCommand</c> để test được không cần Revit,
    /// đồng thời sửa lỗi #5 trong docs/progress.md: sắp xếp cũ dùng
    /// <c>OrderByDescending(Y).ThenBy(X)</c> nên hai cửa cùng một hàng lệch 1 mm rơi vào hai "hàng"
    /// khác nhau, làm <c>ThenBy(X)</c> gần như vô tác dụng. Ở đây toạ độ được gom theo dung sai trước.
    /// </summary>
    public static class NumberingPlanner
    {
        /// <summary>Dung sai gom hàng/cột mặc định: 300 mm, đổi ra feet (đơn vị nội bộ của Revit).</summary>
        public const double DefaultBandToleranceFt = 300.0 / 304.8;

        /// <summary>
        /// Sắp phần tử theo hướng quét, có gom dải (band) theo dung sai.
        /// Thứ tự trả về ổn định: hai phần tử trùng hoàn toàn toạ độ giữ nguyên thứ tự đầu vào.
        /// </summary>
        public static List<NumberingItem<TKey>> Order<TKey>(
            IEnumerable<NumberingItem<TKey>> items,
            ScanDirection direction,
            double bandToleranceFt = DefaultBandToleranceFt)
        {
            if (items == null)
            {
                throw new ArgumentNullException(nameof(items));
            }

            if (bandToleranceFt < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(bandToleranceFt), "Dung sai gom dải không được âm.");
            }

            var list = new List<NumberingItem<TKey>>(items);

            // Quét theo hàng: gom theo Y (hàng trên trước), trong hàng sắp theo X tăng dần.
            // Quét theo cột: gom theo X (cột trái trước), trong cột sắp theo Y giảm dần.
            var byRow = direction == ScanDirection.LeftToRightThenTopToBottom;

            var bandKeys = AssignBands(list, byRow ? (Func<NumberingItem<TKey>, double>)(i => i.Y) : i => i.X, bandToleranceFt);

            var indexed = new List<KeyValuePair<int, NumberingItem<TKey>>>(list.Count);
            for (var i = 0; i < list.Count; i++)
            {
                indexed.Add(new KeyValuePair<int, NumberingItem<TKey>>(i, list[i]));
            }

            indexed.Sort((a, b) =>
            {
                var bandA = bandKeys[a.Key];
                var bandB = bandKeys[b.Key];

                // Dải: hàng thì Y lớn (phía trên) trước; cột thì X nhỏ (bên trái) trước.
                var bandCompare = byRow ? bandB.CompareTo(bandA) : bandA.CompareTo(bandB);
                if (bandCompare != 0)
                {
                    return bandCompare;
                }

                // Trong dải: hàng thì X tăng dần; cột thì Y giảm dần.
                var inner = byRow
                    ? a.Value.X.CompareTo(b.Value.X)
                    : b.Value.Y.CompareTo(a.Value.Y);
                if (inner != 0)
                {
                    return inner;
                }

                return a.Key.CompareTo(b.Key);
            });

            var ordered = new List<NumberingItem<TKey>>(indexed.Count);
            foreach (var pair in indexed)
            {
                ordered.Add(pair.Value);
            }
            return ordered;
        }

        /// <summary>
        /// Gom giá trị thành các dải cách nhau quá dung sai. Trả về mảng chỉ số dải theo thứ tự đầu vào,
        /// dải được đại diện bằng giá trị trung bình để hai phần tử lệch nhỏ luôn cùng một dải.
        /// </summary>
        private static double[] AssignBands<TKey>(
            List<NumberingItem<TKey>> items,
            Func<NumberingItem<TKey>, double> selector,
            double toleranceFt)
        {
            var result = new double[items.Count];
            if (items.Count == 0)
            {
                return result;
            }

            var order = new List<int>(items.Count);
            for (var i = 0; i < items.Count; i++)
            {
                order.Add(i);
            }
            order.Sort((a, b) => selector(items[a]).CompareTo(selector(items[b])));

            var bandStart = selector(items[order[0]]);
            var bandSum = bandStart;
            var bandCount = 1;
            var members = new List<int> { order[0] };

            for (var i = 1; i < order.Count; i++)
            {
                var index = order[i];
                var value = selector(items[index]);

                // So với ĐẦU dải, không so với phần tử liền trước: tránh hiệu ứng "trôi dải"
                // khi có một chuỗi dài các phần tử cách nhau đúng dưới dung sai.
                if (value - bandStart <= toleranceFt)
                {
                    members.Add(index);
                    bandSum += value;
                    bandCount++;
                    continue;
                }

                Flush(result, members, bandSum / bandCount);

                bandStart = value;
                bandSum = value;
                bandCount = 1;
                members = new List<int> { index };
            }

            Flush(result, members, bandSum / bandCount);
            return result;
        }

        private static void Flush(double[] result, List<int> members, double bandValue)
        {
            foreach (var index in members)
            {
                result[index] = bandValue;
            }
        }

        /// <summary>
        /// Sinh nhãn số cho danh sách đã sắp: "{Prefix}{số}" với số bắt đầu từ <paramref name="startNumber"/>,
        /// nhảy <paramref name="step"/>, đệm 0 tới <paramref name="padWidth"/> chữ số.
        /// </summary>
        public static List<NumberingAssignment<TKey>> Assign<TKey>(
            IEnumerable<NumberingItem<TKey>> orderedItems,
            string prefix,
            int startNumber,
            int step,
            int padWidth)
        {
            if (orderedItems == null)
            {
                throw new ArgumentNullException(nameof(orderedItems));
            }

            if (step == 0)
            {
                // Bước 0 làm mọi phần tử cùng một số — chắc chắn là lỗi cấu hình, không phải ý định.
                throw new ArgumentOutOfRangeException(nameof(step), "Bước nhảy phải khác 0.");
            }

            var assignments = new List<NumberingAssignment<TKey>>();
            long number = startNumber;

            foreach (var item in orderedItems)
            {
                // Tính bằng long + checked: startNumber gần int.MaxValue không được lặng lẽ quay vòng về số âm.
                var current = checked((int)number);
                assignments.Add(new NumberingAssignment<TKey>(item.Key, current, FormatLabel(prefix, current, padWidth)));
                number = checked(number + step);
            }

            return assignments;
        }

        /// <summary>Sinh một nhãn số. Số âm giữ dấu trừ trước phần đệm 0 (ví dụ pad 3, -7 → "-007").</summary>
        public static string FormatLabel(string prefix, int number, int padWidth)
        {
            var negative = number < 0;
            var digits = Math.Abs((long)number).ToString(CultureInfo.InvariantCulture);

            if (padWidth > 0)
            {
                digits = digits.PadLeft(padWidth, '0');
            }

            return (prefix ?? string.Empty) + (negative ? "-" : string.Empty) + digits;
        }
    }
}
