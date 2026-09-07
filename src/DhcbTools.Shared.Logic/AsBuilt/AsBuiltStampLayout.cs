using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace DhcbTools.Shared.Logic.AsBuilt
{
    /// <summary>
    /// Cấu hình nội dung điền vào dấu bản vẽ hoàn công (mục 11.6, đề xuất C2). Khung mẫu (nhãn cột, dòng
    /// tiêu đề, "BẢN VẼ HOÀN CÔNG") lấy nguyên văn từ <b>Phụ lục IIb, Nghị định 207/2026/NĐ-CP</b> (trang 3–4,
    /// văn bản gốc trên Công báo Chính phủ) nên đặt cứng trong mã — đây là khuôn dấu pháp luật quy định, không
    /// đổi theo dự án, khác với danh mục hồ sơ ở <see cref="DossierIndex"/> vốn phải do doanh nghiệp khai.
    /// Phần <b>doanh nghiệp chịu trách nhiệm</b> chỉ là các giá trị điền vào: tên nhà thầu, ngày tháng năm,
    /// tên người ký từng vai trò.
    /// </summary>
    public sealed class AsBuiltStampConfig
    {
        /// <summary>1 = hợp đồng thường (không thầu chính/phụ); 2 = hợp đồng thầu chính/phụ, EPC, chìa khoá trao tay.</summary>
        [JsonProperty("mau")]
        public int Mau { get; set; } = 1;

        /// <summary>Tên nhà thầu thi công xây dựng — dòng tiêu đề đầu, in hoa trên khuôn dấu. Bắt buộc.</summary>
        [JsonProperty("tenNhaThau")]
        public string TenNhaThau { get; set; } = string.Empty;

        /// <summary>Ngày/tháng/năm lập bản vẽ hoàn công. Rỗng thì giữ nguyên chấm chờ điền tay như bản mẫu gốc.</summary>
        [JsonProperty("ngay")]
        public string? Ngay { get; set; }

        [JsonProperty("thang")]
        public string? Thang { get; set; }

        [JsonProperty("nam")]
        public string? Nam { get; set; }

        /// <summary>Người lập (mọi Mẫu). Bắt buộc.</summary>
        [JsonProperty("nguoiLap")]
        public string NguoiLap { get; set; } = string.Empty;

        /// <summary>Chỉ huy trưởng công trình hoặc giám đốc dự án — CHỈ dùng ở Mẫu 1. Bắt buộc khi <see cref="Mau"/> = 1.</summary>
        [JsonProperty("chiHuyTruongHoacGiamDocDuAn")]
        public string? ChiHuyTruongHoacGiamDocDuAn { get; set; }

        /// <summary>Chỉ huy trưởng/giám đốc dự án của nhà thầu PHỤ — CHỈ dùng ở Mẫu 2. Tuỳ chọn (không phải hợp đồng nào cũng có thầu phụ).</summary>
        [JsonProperty("chiHuyTruongNhaThauPhu")]
        public string? ChiHuyTruongNhaThauPhu { get; set; }

        /// <summary>Chỉ huy trưởng/giám đốc dự án của nhà thầu CHÍNH — CHỈ dùng ở Mẫu 2. Bắt buộc khi <see cref="Mau"/> = 2.</summary>
        [JsonProperty("chiHuyTruongNhaThauChinh")]
        public string? ChiHuyTruongNhaThauChinh { get; set; }

        /// <summary>Tư vấn giám sát trưởng (mọi Mẫu). Bắt buộc.</summary>
        [JsonProperty("tuVanGiamSatTruong")]
        public string TuVanGiamSatTruong { get; set; } = string.Empty;

        /// <summary>Bề rộng khuôn dấu (mm). Ghi chú gốc: "Kích thước dấu tùy thuộc kích cỡ chữ" — không có cỡ bắt buộc.</summary>
        [JsonProperty("widthMm")]
        public double WidthMm { get; set; } = 160;

        /// <summary>Cỡ chữ nội dung (mm, chiều cao chữ hoa) — các dòng cao lên theo tỉ lệ với giá trị này.</summary>
        [JsonProperty("textHeightMm")]
        public double TextHeightMm { get; set; } = 3.0;
    }

    /// <summary>Kiểu chữ của một ô văn bản trên khuôn dấu — Core dịch sang <c>FormattedText</c> của Revit.</summary>
    public enum StampTextStyle
    {
        /// <summary>Đậm, dùng cho hai dòng tiêu đề và nhãn cột.</summary>
        Bold,

        /// <summary>Nghiêng, dùng cho chú thích dưới nhãn cột ("Ghi rõ họ tên…").</summary>
        Italic,

        /// <summary>Thường, dùng cho giá trị điền từ config.</summary>
        Plain,
    }

    public enum StampAlign
    {
        Center,
        Left,
    }

    /// <summary>Một đoạn văn bản trên khuôn dấu, toạ độ mm gốc góc trên-trái của khuôn (X phải, Y xuống).</summary>
    public sealed class StampTextItem
    {
        public double XMm { get; set; }
        public double YMm { get; set; }
        public double WidthMm { get; set; }
        public string Text { get; set; } = string.Empty;
        public StampTextStyle Style { get; set; }
        public StampAlign Align { get; set; } = StampAlign.Center;
    }

    /// <summary>Một đoạn thẳng (viền hoặc lưới ô) trên khuôn dấu, cùng hệ toạ độ mm với <see cref="StampTextItem"/>.</summary>
    public sealed class StampLine
    {
        public double X1Mm { get; set; }
        public double Y1Mm { get; set; }
        public double X2Mm { get; set; }
        public double Y2Mm { get; set; }
    }

    /// <summary>Bản vẽ đầy đủ của một khuôn dấu: kích thước tổng và danh sách nét/chữ để Core dựng lên sheet.</summary>
    public sealed class AsBuiltStampLayout
    {
        public double WidthMm { get; set; }
        public double HeightMm { get; set; }
        public IReadOnlyList<StampLine> Lines { get; set; } = Array.Empty<StampLine>();
        public IReadOnlyList<StampTextItem> Texts { get; set; } = Array.Empty<StampTextItem>();
    }

    /// <summary>
    /// Dựng hình học + văn bản của khuôn dấu bản vẽ hoàn công theo đúng bố cục Phụ lục IIb: ba dòng tiêu đề
    /// (tên nhà thầu / "BẢN VẼ HOÀN CÔNG" / ngày tháng năm) rồi một hàng 3 cột (Mẫu 1) hoặc 4 cột (Mẫu 2),
    /// mỗi cột có nhãn (đậm) + chú thích (nghiêng) + giá trị điền (thường). Thuần tuý — không đụng Revit API,
    /// nên có test trên CI; <c>AsBuiltStampCommand</c> (Core) chỉ dịch mm → ft và gọi <c>NewDetailCurve</c>/
    /// <c>TextNote.Create</c>.
    /// </summary>
    public static class AsBuiltStampBuilder
    {
        private const double PaddingMm = 2.5;
        private const string DotsFallback = ".....";

        /// <summary>
        /// Tên trường (kiểu JSON) đang thiếu/sai với đúng <see cref="AsBuiltStampConfig.Mau"/> đã khai. Rỗng = hợp lệ.
        /// Không dùng <c>required</c> của C# vì "bắt buộc hay không" phụ thuộc giá trị <see cref="AsBuiltStampConfig.Mau"/>.
        /// </summary>
        public static List<string> Validate(AsBuiltStampConfig config)
        {
            var missing = new List<string>();
            if (config.Mau != 1 && config.Mau != 2)
            {
                missing.Add("mau (phải là 1 hoặc 2, đang là " + config.Mau.ToString(System.Globalization.CultureInfo.InvariantCulture) + ")");
                return missing;
            }

            if (string.IsNullOrWhiteSpace(config.TenNhaThau)) missing.Add("tenNhaThau");
            if (string.IsNullOrWhiteSpace(config.NguoiLap)) missing.Add("nguoiLap");
            if (string.IsNullOrWhiteSpace(config.TuVanGiamSatTruong)) missing.Add("tuVanGiamSatTruong");

            if (config.Mau == 1)
            {
                if (string.IsNullOrWhiteSpace(config.ChiHuyTruongHoacGiamDocDuAn)) missing.Add("chiHuyTruongHoacGiamDocDuAn");
            }
            else
            {
                // chiHuyTruongNhaThauPhu KHÔNG bắt buộc: hợp đồng thầu chính/EPC/chìa khoá trao tay có thể
                // không có thầu phụ. Bắt buộc thứ đó là bịa dữ liệu cho hợp đồng không có thầu phụ.
                if (string.IsNullOrWhiteSpace(config.ChiHuyTruongNhaThauChinh)) missing.Add("chiHuyTruongNhaThauChinh");
            }

            return missing;
        }

        /// <summary>Dựng layout. Gọi sau khi <see cref="Validate"/> trả rỗng — không tự kiểm lại để tránh hai nguồn sự thật.</summary>
        public static AsBuiltStampLayout Build(AsBuiltStampConfig config)
        {
            var width = config.WidthMm;
            var lineH = config.TextHeightMm * 2.2;
            var row1H = lineH * 1.3;
            var row2H = lineH * 1.1;
            var row3H = lineH * 1.1;
            var row4H = lineH * 4.5;
            var height = row1H + row2H + row3H + row4H;

            var lines = new List<StampLine>();
            var texts = new List<StampTextItem>();

            // Viền ngoài + các nét ngang phân dòng.
            var ys = new[] { 0.0, row1H, row1H + row2H, row1H + row2H + row3H, height };
            foreach (var y in ys)
            {
                lines.Add(new StampLine { X1Mm = 0, Y1Mm = y, X2Mm = width, Y2Mm = y });
            }
            lines.Add(new StampLine { X1Mm = 0, Y1Mm = 0, X2Mm = 0, Y2Mm = height });
            lines.Add(new StampLine { X1Mm = width, Y1Mm = 0, X2Mm = width, Y2Mm = height });

            texts.Add(Row(config.TenNhaThau, 0, row1H, width, StampTextStyle.Bold));
            texts.Add(Row("BẢN VẼ HOÀN CÔNG", row1H, row2H, width, StampTextStyle.Bold));

            var ngay = string.IsNullOrWhiteSpace(config.Ngay) ? DotsFallback : config.Ngay!;
            var thang = string.IsNullOrWhiteSpace(config.Thang) ? DotsFallback : config.Thang!;
            var nam = string.IsNullOrWhiteSpace(config.Nam) ? DotsFallback : config.Nam!;
            texts.Add(Row($"Ngày {ngay} tháng {thang} năm {nam}", row1H + row2H, row3H, width, StampTextStyle.Plain));

            var columns = config.Mau == 1
                ? new[]
                {
                    ("Người lập", "(Ghi rõ họ tên, chức vụ, chữ ký)", config.NguoiLap),
                    ("Chỉ huy trưởng công trình hoặc giám đốc dự án", "(Ghi rõ họ tên, chữ ký)", config.ChiHuyTruongHoacGiamDocDuAn ?? string.Empty),
                    ("Tư vấn giám sát trưởng", "(Ghi rõ họ tên, chức vụ, chữ ký)", config.TuVanGiamSatTruong),
                }
                : new[]
                {
                    ("Người lập", "(Ghi rõ họ tên, chức vụ, chữ ký)", config.NguoiLap),
                    ("Chỉ huy trưởng hoặc giám đốc dự án của nhà thầu phụ", "(Ghi rõ họ tên, chữ ký)", config.ChiHuyTruongNhaThauPhu ?? string.Empty),
                    ("Chỉ huy trưởng hoặc giám đốc dự án của nhà thầu chính", "(Ghi rõ họ tên, chữ ký)", config.ChiHuyTruongNhaThauChinh ?? string.Empty),
                    ("Tư vấn giám sát trưởng", "(Ghi rõ họ tên, chức vụ, chữ ký)", config.TuVanGiamSatTruong),
                };

            var colWidth = width / columns.Length;
            var row4Top = row1H + row2H + row3H;
            for (var i = 0; i < columns.Length; i++)
            {
                var (label, caption, value) = columns[i];
                var colX = i * colWidth;
                if (i > 0)
                {
                    lines.Add(new StampLine { X1Mm = colX, Y1Mm = row4Top, X2Mm = colX, Y2Mm = height });
                }

                var innerW = colWidth - 2 * PaddingMm;
                var slot = row4H / 4;
                texts.Add(new StampTextItem { XMm = colX + PaddingMm, YMm = row4Top + slot * 0.2, WidthMm = innerW, Text = label, Style = StampTextStyle.Bold, Align = StampAlign.Center });
                texts.Add(new StampTextItem { XMm = colX + PaddingMm, YMm = row4Top + slot * 2.0, WidthMm = innerW, Text = caption, Style = StampTextStyle.Italic, Align = StampAlign.Center });
                texts.Add(new StampTextItem
                {
                    XMm = colX + PaddingMm,
                    YMm = row4Top + slot * 3.1,
                    WidthMm = innerW,
                    Text = string.IsNullOrWhiteSpace(value) ? DotsFallback : value,
                    Style = StampTextStyle.Plain,
                    Align = StampAlign.Center,
                });
            }

            return new AsBuiltStampLayout { WidthMm = width, HeightMm = height, Lines = lines, Texts = texts };

            StampTextItem Row(string text, double top, double h, double w, StampTextStyle style) =>
                new StampTextItem { XMm = PaddingMm, YMm = top + h * 0.15, WidthMm = w - 2 * PaddingMm, Text = text, Style = style, Align = StampAlign.Center };
        }

        /// <summary>
        /// Đổi toạ độ cục bộ của khuôn dấu (gốc góc trên-trái, Y xuống — hệ của <see cref="StampLine"/>/
        /// <see cref="StampTextItem"/>) sang toạ độ tương đối với điểm neo góc DƯỚI-trái (Y lên — hệ trục
        /// của Revit trên sheet). Tách riêng để có test: nhầm chiều Y một lần là cả khuôn dấu vẽ lộn ngược
        /// trên sheet, và lỗi đó chỉ thấy được khi mở Revit lên nhìn — quá muộn để bắt bằng mắt mỗi lần sửa.
        /// </summary>
        public static (double UMm, double VMm) ToAnchorRelative(double xMm, double yMm, double heightMm) =>
            (xMm, heightMm - yMm);
    }
}
