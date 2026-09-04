using System;
using System.Collections.Generic;
using System.Linq;
using DhcbTools.Shared.Logic;
using DhcbTools.Shared.Logic.Progress;
using Xunit;

namespace DhcbTools.Shared.Logic.Tests;

/// <summary>
/// Tầng thuần của <c>ConstructionStatus</c>/<c>ProgressReport</c> (đề xuất B1 — trạng thái thi công và
/// báo cáo tiến độ). Báo cáo tiến độ là thứ đi lên bàn chủ đầu tư, nên bốn điều được chốt ở đây đều là
/// những chỗ một báo cáo tiến độ hay nói dối:
/// <list type="number">
/// <item>Mẫu số là <b>toàn bộ cấu kiện trong phạm vi</b> — cái chưa ai ghi nhận không được lặng lẽ rơi
/// khỏi mẫu số cho phần trăm đẹp lên.</item>
/// <item>"Đang lắp" <b>không có trọng số</b>: nó không phải nửa cái ống.</item>
/// <item>Phần đã lắp mà <b>không có ngày</b> phải được đếm riêng, vì nó không vẽ được lên trục thời gian.</item>
/// <item>Dòng CSV hiện trường không đọc được phải <b>báo đúng số dòng</b>, không bỏ qua im lặng.</item>
/// </list>
/// </summary>
public class ProgressTests
{
    private static DateTime D(string ddMMyyyy) => DateTime.ParseExact(ddMMyyyy, "dd/MM/yyyy", System.Globalization.CultureInfo.InvariantCulture);

    // ── Từ vựng trạng thái ───────────────────────────────────────────────────

    [Theory]
    [InlineData("Đã lắp", ConstructionStage.DaLap)]
    [InlineData("da lap", ConstructionStage.DaLap)]
    [InlineData("ĐÃ  LẮP", ConstructionStage.DaLap)]
    [InlineData("Đã lắp đặt", ConstructionStage.DaLap)]
    [InlineData("Installed", ConstructionStage.DaLap)]
    [InlineData("da-nghiem-thu", ConstructionStage.DaNghiemThu)]
    [InlineData("Approved", ConstructionStage.DaNghiemThu)]
    [InlineData("Đang thi công", ConstructionStage.DangLap)]
    [InlineData("wip", ConstructionStage.DangLap)]
    [InlineData("Chưa lắp", ConstructionStage.ChuaLap)]
    [InlineData("Not started", ConstructionStage.ChuaLap)]
    public void TuVung_NhanNhieuCachViet(string text, ConstructionStage expected)
    {
        Assert.True(ConstructionStatusValue.TryParse(text, out var stage));
        Assert.Equal(expected, stage);
    }

    [Fact]
    public void TuVung_ORong_LaChuaCoDuLieu_KhongPhaiLoi()
    {
        Assert.True(ConstructionStatusValue.TryParse("", out var stage));
        Assert.Equal(ConstructionStage.ChuaCoDuLieu, stage);
        Assert.True(ConstructionStatusValue.TryParse("   ", out _));
        Assert.True(ConstructionStatusValue.TryParse(null, out _));
    }

    [Fact]
    public void TuVung_ChuLa_LaLoi_VaBaoDuCachVietHopLe()
    {
        Assert.False(ConstructionStatusValue.TryParse("xong roi nhe", out _));

        var message = ConstructionStatusValue.NotRecognised("xong roi nhe");
        Assert.Contains("xong roi nhe", message);
        Assert.Contains("Đã lắp", message);
        Assert.Contains("Đã nghiệm thu", message);
    }

    [Fact]
    public void TuVung_ThuHangTangDan_NenDaLapTroLenSoSanhDuoc()
    {
        Assert.True(ConstructionStage.DaNghiemThu > ConstructionStage.DaLap);
        Assert.True(ConstructionStage.DaLap > ConstructionStage.DangLap);
        Assert.True(ConstructionStage.DangLap > ConstructionStage.ChuaLap);
        Assert.True(ConstructionStage.ChuaLap > ConstructionStage.ChuaCoDuLieu);
    }

    // ── Gộp theo nhóm ────────────────────────────────────────────────────────

    private static StatusItem Item(string group, ConstructionStage stage, double lengthMm = 0, string? date = null, long id = 0) =>
        new StatusItem(group, stage, lengthMm, date == null ? (DateTime?)null : D(date), id);

    [Fact]
    public void Gop_MauSoLaToanBoPhamVi_KeCaChuaCoDuLieu()
    {
        var items = new List<StatusItem>
        {
            Item("Level 1", ConstructionStage.DaLap),
            Item("Level 1", ConstructionStage.DaLap),
            Item("Level 1", ConstructionStage.ChuaCoDuLieu),
            Item("Level 1", ConstructionStage.ChuaCoDuLieu),
        };

        var row = StatusRoll.By(items).Single();

        Assert.Equal(4, row.Total);
        Assert.Equal(2, row.NoDataCount);
        // 2/4 chứ KHÔNG phải 2/2: chưa nhập thì chưa lắp, không được bỏ khỏi mẫu số.
        Assert.Equal(50, row.PercentAtLeast(ConstructionStage.DaLap));
    }

    [Fact]
    public void Gop_DangLapKhongCoTrongSo()
    {
        var items = new List<StatusItem>
        {
            Item("Hệ A", ConstructionStage.DangLap),
            Item("Hệ A", ConstructionStage.DangLap),
            Item("Hệ A", ConstructionStage.DaLap),
            Item("Hệ A", ConstructionStage.ChuaLap),
        };

        var row = StatusRoll.By(items).Single();

        Assert.Equal(25, row.PercentAtLeast(ConstructionStage.DaLap));   // không phải 50 vì "cộng nửa"
        Assert.Equal(2, row.CountOf(ConstructionStage.DangLap));
    }

    [Fact]
    public void Gop_DaNghiemThuTinhCaVaoDaLapTroLen()
    {
        var items = new List<StatusItem>
        {
            Item("L1", ConstructionStage.DaNghiemThu),
            Item("L1", ConstructionStage.DaLap),
            Item("L1", ConstructionStage.ChuaLap),
            Item("L1", ConstructionStage.ChuaLap),
        };

        var row = StatusRoll.By(items).Single();

        Assert.Equal(50, row.PercentAtLeast(ConstructionStage.DaLap));
        Assert.Equal(25, row.PercentAtLeast(ConstructionStage.DaNghiemThu));
    }

    [Fact]
    public void Gop_PhanTramTheoChieuDaiKhacPhanTramTheoSoLuong()
    {
        // Ba ống: một ống dài 90 m đã lắp, hai ống 5 m chưa lắp.
        var items = new List<StatusItem>
        {
            Item("Hệ nước", ConstructionStage.DaLap, 90000),
            Item("Hệ nước", ConstructionStage.ChuaLap, 5000),
            Item("Hệ nước", ConstructionStage.ChuaLap, 5000),
        };

        var row = StatusRoll.By(items).Single();

        Assert.Equal(33.3, Math.Round(row.PercentAtLeast(ConstructionStage.DaLap), 1));
        Assert.Equal(90, row.PercentByLengthAtLeast(ConstructionStage.DaLap));
        Assert.True(row.HasLength);
    }

    [Fact]
    public void Gop_NhomKhongCoChieuDai_ThiCotChieuDaiVoNghia_VaNoiRa()
    {
        var row = StatusRoll.By(new[] { Item("Thiết bị", ConstructionStage.DaLap) }).Single();

        Assert.False(row.HasLength);
        Assert.Equal(0, row.PercentByLengthAtLeast(ConstructionStage.DaLap));   // không chia cho 0
    }

    [Fact]
    public void Gop_SapXepNhomTheoThuTuTuNhien()
    {
        var items = new[]
        {
            Item("Level 10", ConstructionStage.DaLap),
            Item("Level 2", ConstructionStage.DaLap),
            Item("Level 1", ConstructionStage.DaLap),
        };

        Assert.Equal(new[] { "Level 1", "Level 2", "Level 10" }, StatusRoll.By(items).Select(r => r.Group));
    }

    [Fact]
    public void Gop_DongTong_CongDungBangTongCacNhom()
    {
        var items = new[]
        {
            Item("L1", ConstructionStage.DaLap, 1000),
            Item("L2", ConstructionStage.ChuaLap, 3000),
            Item("L2", ConstructionStage.DaNghiemThu, 1000),
        };

        var total = StatusRoll.Total(items);

        Assert.Equal(3, total.Total);
        Assert.Equal(5000, total.TotalLengthMm);
        Assert.Equal(2, total.CountAtLeast(ConstructionStage.DaLap));
        Assert.Equal(40, total.PercentByLengthAtLeast(ConstructionStage.DaLap));
    }

    [Fact]
    public void Gop_DanhSachRong_KhongNem()
    {
        Assert.Empty(StatusRoll.By(new List<StatusItem>()));
        var total = StatusRoll.Total(new List<StatusItem>());
        Assert.Equal(0, total.Total);
        Assert.Equal(0, total.PercentAtLeast(ConstructionStage.DaLap));
    }

    // ── Chuỗi theo tuần ──────────────────────────────────────────────────────

    [Theory]
    [InlineData("03/09/2026", "31/08/2026")]   // thứ Năm → thứ Hai cùng tuần
    [InlineData("31/08/2026", "31/08/2026")]   // chính thứ Hai
    [InlineData("06/09/2026", "31/08/2026")]   // Chủ nhật thuộc tuần bắt đầu thứ Hai trước đó
    public void Tuan_BatDauThuHai(string date, string expectedStart) =>
        Assert.Equal(D(expectedStart), WeeklyProgress.WeekStartOf(D(date)));

    [Fact]
    public void Tuan_LuyKe_VaTuanDungYenVanCoTrongChuoi()
    {
        var items = new List<StatusItem>
        {
            Item("L1", ConstructionStage.DaLap, date: "01/09/2026"),   // tuần 31/08
            Item("L1", ConstructionStage.DaLap, date: "03/09/2026"),   // tuần 31/08
            // tuần 07/09 không có gì
            Item("L1", ConstructionStage.DaNghiemThu, date: "15/09/2026"),  // tuần 14/09
            Item("L1", ConstructionStage.ChuaLap, date: "16/09/2026"),      // chưa đạt mức, không tính
        };

        var series = WeeklyProgress.Series(items);

        Assert.Equal(new[] { "31/08/2026", "07/09/2026", "14/09/2026" }, series.Weeks.Select(w => w.Label));
        Assert.Equal(new[] { 2, 0, 1 }, series.Weeks.Select(w => w.Added));
        Assert.Equal(new[] { 2, 2, 3 }, series.Weeks.Select(w => w.Cumulative));
        Assert.Equal(75, series.Weeks.Last().CumulativePercent);   // 3 trên tổng 4 cấu kiện
    }

    [Fact]
    public void Tuan_DaLapMaKhongCoNgay_DemRieng_KhongAmThamBoQua()
    {
        var items = new List<StatusItem>
        {
            Item("L1", ConstructionStage.DaLap, date: "01/09/2026"),
            Item("L1", ConstructionStage.DaLap),
            Item("L1", ConstructionStage.DaLap),
        };

        var series = WeeklyProgress.Series(items);

        Assert.Equal(2, series.ReachedWithoutDate);
        Assert.Single(series.Weeks);
        Assert.Equal(1, series.Weeks[0].Cumulative);
    }

    [Fact]
    public void Tuan_KhongCoNgayNao_ChuoiRong_NhungVanNoiSoLuong()
    {
        var series = WeeklyProgress.Series(new[] { Item("L1", ConstructionStage.DaLap) });
        Assert.Empty(series.Weeks);
        Assert.Equal(1, series.ReachedWithoutDate);
        Assert.Equal(1, series.Total);
    }

    // ── CSV hiện trường ──────────────────────────────────────────────────────

    private static List<string[]> Csv(params string[] lines) =>
        lines.Select(l => CsvText.SplitLine(l).ToArray()).ToList();

    [Fact]
    public void DocCsv_TieuDeTiengVietCoDau()
    {
        var result = ProgressCsv.Read(Csv(
            "ElementId,Trạng thái,Ngày,Người xác nhận,Ghi chú",
            "1234,Đã lắp,03/09/2026,Nguyễn Văn A,lắp trước trần",
            "1235,Đã nghiệm thu,2026-09-04,Trần B,"));

        Assert.True(result.Ok);
        Assert.Empty(result.Errors);
        Assert.Equal(2, result.Rows.Count);
        Assert.Equal(1234, result.Rows[0].ElementId);
        Assert.Equal("Đã lắp", result.Rows[0].StatusText);
        Assert.Equal(D("03/09/2026"), result.Rows[0].Date);
        Assert.Equal("Nguyễn Văn A", result.Rows[0].Person);
        Assert.Equal("lắp trước trần", result.Rows[0].Note);
        Assert.Equal(D("04/09/2026"), result.Rows[1].Date);
    }

    [Fact]
    public void DocCsv_TieuDeKhongDauVaTiengAnh_CungNhanDuoc()
    {
        var result = ProgressCsv.Read(Csv(
            "Id,Status,Date,By",
            "7,Installed,2026-09-03,Sang"));

        Assert.True(result.Ok);
        Assert.Single(result.Rows);
        Assert.Equal(ConstructionStage.DaLap, result.Rows[0].Stage);
    }

    [Fact]
    public void DocCsv_ChiCanHaiCotBatBuoc()
    {
        var result = ProgressCsv.Read(Csv("ElementId,TrangThai", "7,Đã lắp"));

        Assert.True(result.Ok);
        Assert.Single(result.Rows);
        Assert.Null(result.Rows[0].Date);
    }

    [Fact]
    public void DocCsv_ThieuCotBatBuoc_LaLoiChanCaFile_VaNoiDangThayGiCot()
    {
        var result = ProgressCsv.Read(Csv("Ten,Ngay", "cửa 1,03/09/2026"));

        Assert.False(result.Ok);
        Assert.Contains("thiếu cột bắt buộc", result.FatalError);
        Assert.Contains("\"Ten\"", result.FatalError);
        Assert.Empty(result.Rows);
    }

    [Fact]
    public void DocCsv_FileRong_LaLoiChan()
    {
        Assert.False(ProgressCsv.Read(Csv("ElementId,TrangThai")).Ok);
        Assert.False(ProgressCsv.Read(new List<string[]>()).Ok);
    }

    [Fact]
    public void DocCsv_DongHong_BaoDungSoDong_VaKhongNuotDongTot()
    {
        var result = ProgressCsv.Read(Csv(
            "ElementId,TrangThai,Ngay",
            "1,Đã lắp,03/09/2026",
            "hai,Đã lắp,03/09/2026",
            "3,xong roi nhe,03/09/2026",
            "4,Đã lắp,32/13/2026",
            "5,,03/09/2026",
            "6,Đã lắp,"));

        Assert.True(result.Ok);
        Assert.Equal(new long[] { 1, 6 }, result.Rows.Select(r => r.ElementId));
        Assert.Equal(4, result.Errors.Count);
        Assert.Contains(result.Errors, e => e.StartsWith("Dòng 3:") && e.Contains("không phải số"));
        Assert.Contains(result.Errors, e => e.StartsWith("Dòng 4:") && e.Contains("không nhận ra"));
        Assert.Contains(result.Errors, e => e.StartsWith("Dòng 5:") && e.Contains("không đọc được"));
        Assert.Contains(result.Errors, e => e.StartsWith("Dòng 6:") && e.Contains("để trống"));
    }

    [Fact]
    public void DocCsv_MaTrungNhau_LayDongSauCung_VaNoiRa()
    {
        var result = ProgressCsv.Read(Csv(
            "ElementId,TrangThai",
            "9,Chưa lắp",
            "9,Đã lắp"));

        Assert.Single(result.Rows);
        Assert.Equal(ConstructionStage.DaLap, result.Rows[0].Stage);
        Assert.Contains(result.Errors, e => e.Contains("đã có ở dòng 2"));
    }

    [Fact]
    public void DocCsv_DongTrong_KhongPhaiLoi()
    {
        var result = ProgressCsv.Read(Csv("ElementId,TrangThai", "", "1,Đã lắp", ""));

        Assert.Single(result.Rows);
        Assert.Empty(result.Errors);
    }

    [Theory]
    [InlineData("03/09/2026", 3, 9)]
    [InlineData("3/9/2026", 3, 9)]
    [InlineData("03-09-2026", 3, 9)]
    [InlineData("2026-09-03", 3, 9)]
    public void DocNgay_NgayDungTruocThang(string text, int day, int month)
    {
        Assert.True(ProgressCsv.TryParseDate(text, out var date));
        Assert.Equal(day, date.Day);
        Assert.Equal(month, date.Month);
    }

    // ── CSV báo cáo ──────────────────────────────────────────────────────────

    [Fact]
    public void CsvBaoCao_DuCotVaPhanTram()
    {
        var rows = StatusRoll.By(new[]
        {
            Item("Level 1", ConstructionStage.DaLap, 3000),
            Item("Level 1", ConstructionStage.ChuaLap, 1000),
            Item("Level 2", ConstructionStage.ChuaCoDuLieu),
        });

        var csv = ProgressCsv.WriteReport(rows, "Tầng");
        var lines = csv.Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal("Tầng,Tổng,Chưa lắp,Đang lắp,Đã lắp,Đã nghiệm thu,Chưa có dữ liệu,% đã lắp trở lên,% đã nghiệm thu,Tổng chiều dài (m),% đã lắp theo chiều dài", lines[0]);
        Assert.Equal("Level 1,2,1,0,1,0,0,50.0,0.0,4.0,75.0", lines[1]);
        // Nhóm không có chiều dài để trống hai cột cuối thay vì ghi 0 — 0 % và "không đo được" là hai chuyện khác nhau.
        Assert.Equal("Level 2,1,0,0,0,0,1,0.0,0.0,,", lines[2]);
    }
}
