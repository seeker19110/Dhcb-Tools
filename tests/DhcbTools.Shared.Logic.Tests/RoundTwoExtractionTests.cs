using System;
using System.Collections.Generic;
using System.IO;
using DhcbTools.Shared.Hosting;
using DhcbTools.Shared.Hosting.Testing;
using DhcbTools.Shared.Logic;
using DhcbTools.Shared.Logic.Cad;
using DhcbTools.Shared.Logic.Checks;
using DhcbTools.Shared.Logic.Testing;
using Newtonsoft.Json;
using Xunit;

namespace DhcbTools.Shared.Logic.Tests;

/// <summary>
/// Vòng audit 2 (2026-09-23): phần quyết định tách từ Core/Core.AutoCAD xuống tầng thuần — trước đây chỉ
/// chạy được trong Revit/accoreconsole, nay có test trên CI.
/// </summary>
public class RoundTwoExtractionTests
{
    // ── TestReportWriter ─────────────────────────────────────────────────

    [Fact]
    public void TestReportWriter_ResolveOutputFolder_UuTienThuMucChiDinh_KhongThiLayThuMucSuite()
    {
        Assert.Equal(@"D:\ra", TestReportWriter.ResolveOutputFolder(@"D:\suites\a.json", @"D:\ra"));
        var suite = Path.Combine(Path.GetTempPath(), "x", "suite.json");
        Assert.Equal(Path.GetDirectoryName(Path.GetFullPath(suite)), TestReportWriter.ResolveOutputFolder(suite, "  "));
        Assert.Throws<ArgumentException>(() => TestReportWriter.ResolveOutputFolder("", null));
    }

    [Fact]
    public void TestReportWriter_Write_GhiTrxVaMd_PhanQuyetTheoCaTruot()
    {
        var dir = Path.Combine(Path.GetTempPath(), "dhcb-trw-" + Guid.NewGuid().ToString("N"));
        try
        {
            var suite = new TestSuite { Name = "Bộ thử", Model = "m.rvt" };
            var outcomes = new List<TestOutcome>
            {
                new TestOutcome { Name = "đạt", Command = "A", ElapsedMs = 5 },
                new TestOutcome { Name = "bỏ", Command = "B", Skipped = true },
                new TestOutcome { Name = "trượt", Command = "C", Failures = { "sai 1", "sai 2" } },
            };

            var result = TestReportWriter.Write(suite, dir, "in-revit-tests", outcomes);

            Assert.False(result.Success);
            Assert.True(File.Exists(Path.Combine(dir, "in-revit-tests.trx")));
            Assert.True(File.Exists(Path.Combine(dir, "in-revit-tests.md")));
            Assert.Contains(result.Messages, m => m.StartsWith("Báo cáo: ") && m.EndsWith(".trx"));
            Assert.Contains("[ĐẠT] đạt (A) — 5 ms", result.Messages);
            Assert.Contains("[BỎ QUA] bỏ (B) — 0 ms", result.Messages);
            Assert.Contains("[TRƯỢT] trượt (C) — 0 ms", result.Messages);
            var error = Assert.Single(result.Errors);
            Assert.Equal("trượt (C): sai 1; sai 2", error);

            var ok = TestReportWriter.Write(suite, dir, "in-autocad-tests", new[] { outcomes[0] });
            Assert.True(ok.Success);
            Assert.Equal(1, ok.AffectedCount);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* dọn dẹp */ }
        }
    }

    [Fact]
    public void TestReportWriter_Write_KhongGhiDuoc_VanTraPhanQuyet()
    {
        var suite = new TestSuite();
        var file = Path.Combine(Path.GetTempPath(), "dhcb-trw-file-" + Guid.NewGuid().ToString("N"));
        File.WriteAllText(file, "chặn thư mục");
        try
        {
            var result = TestReportWriter.Write(suite, Path.Combine(file, "con"), "x", new List<TestOutcome>());
            Assert.True(result.Success);
            Assert.Contains(result.Messages, m => m.StartsWith("Không ghi được báo cáo: "));
        }
        finally
        {
            File.Delete(file);
        }

        Assert.Throws<ArgumentNullException>(() => TestReportWriter.Write(null!, ".", "x", new List<TestOutcome>()));
        Assert.Throws<ArgumentNullException>(() => TestReportWriter.Write(suite, ".", "x", null!));
        Assert.Throws<ArgumentException>(() => TestReportWriter.Write(suite, ".", " ", new List<TestOutcome>()));
    }

    // ── LayerRuleSet ─────────────────────────────────────────────────────

    [Fact]
    public void LayerRuleSet_Parse_NhanCaMangThuanVaObjectBoc()
    {
        var wrapped = LayerRuleSet.Parse("  {\"rules\":[{\"pattern\":\"^A-\",\"description\":\"kiến trúc\"}]}");
        Assert.Equal("^A-", Assert.Single(wrapped!).Pattern);

        var plain = LayerRuleSet.Parse("[{\"pattern\":\"^S-\"},{\"pattern\":\"^M-\"}]");
        Assert.Equal(2, plain!.Count);
        Assert.Equal(string.Empty, plain[0].Description);

        Assert.Null(LayerRuleSet.Parse("{\"khac\":1}"));
        Assert.Null(LayerRuleSet.Parse("null"));
        Assert.ThrowsAny<JsonException>(() => LayerRuleSet.Parse("[{\"pattern\":"));
        Assert.Throws<ArgumentNullException>(() => LayerRuleSet.Parse(null!));
    }

    [Fact]
    public void LayerRuleSet_Html_EscapeVaDanhDauLayerSai()
    {
        var rules = new List<LayerNamingRule> { new LayerNamingRule { Pattern = "^A-<b>", Description = "mô & tả" } };
        var html = LayerRuleSet.Html(new[] { "A-WALL", "<script>", "S-COL" }, new[] { "<script>" }, rules);

        Assert.Contains("Tổng số layer: 3 — Không đúng chuẩn: <b>1</b>", html);
        Assert.Contains("<code>^A-&lt;b&gt;</code> — mô &amp; tả", html);
        Assert.Contains("<tr class=\"invalid\"><td>&lt;script&gt;</td><td>Không đúng chuẩn</td></tr>", html);
        Assert.Contains("<tr class=\"valid\"><td>A-WALL</td><td>Hợp lệ</td></tr>", html);
        Assert.DoesNotContain("<script>", html);

        var empty = LayerRuleSet.Html(null!, null!, null!);
        Assert.Contains("Tổng số layer: 0", empty);
    }

    // ── CadImportOptions ─────────────────────────────────────────────────

    [Theory]
    [InlineData(null, "Default")]
    [InlineData("  ", "Default")]
    [InlineData("auto", "Default")]
    [InlineData("MM", "Millimeter")]
    [InlineData("milimet", "Millimeter")]
    [InlineData("cm", "Centimeter")]
    [InlineData("mét", "Meter")]
    [InlineData("in", "Inch")]
    [InlineData("feet", "Foot")]
    public void CadImportOptions_TryParseUnit_CacCachViet(string? text, string expected)
    {
        Assert.True(CadImportOptions.TryParseUnit(text, out var name, out var error));
        Assert.Equal(expected, name);
        Assert.Equal(string.Empty, error);
    }

    [Fact]
    public void CadImportOptions_DonViVaCachDatSai_BaoLoiTiengViet()
    {
        Assert.False(CadImportOptions.TryParseUnit("yard", out var unit, out var error));
        Assert.Equal("Default", unit);
        Assert.Contains("\"yard\" không hợp lệ", error);

        Assert.False(CadImportOptions.TryParsePlacement("trên", out var placement, out var error2));
        Assert.Equal("Origin", placement);
        Assert.Contains("\"trên\" không hợp lệ", error2);
    }

    [Theory]
    [InlineData("", "Origin")]
    [InlineData("gốc", "Origin")]
    [InlineData("goc", "Origin")]
    [InlineData("Shared", "Shared")]
    [InlineData("chung", "Shared")]
    [InlineData("center", "Centered")]
    [InlineData("giữa", "Centered")]
    public void CadImportOptions_TryParsePlacement_CacCachViet(string text, string expected)
    {
        Assert.True(CadImportOptions.TryParsePlacement(text, out var name, out _));
        Assert.Equal(expected, name);
    }

    // ── TextReplace ──────────────────────────────────────────────────────

    [Fact]
    public void TextReplace_ReplaceAll_KhongPhanBietHoaThuong_KhongChongLan()
    {
        Assert.Equal("x-x-X", TextReplace.ReplaceAll("ab-AB-X", "ab", "x", ignoreCase: true));
        Assert.Equal("x-AB-X", TextReplace.ReplaceAll("ab-AB-X", "ab", "x", ignoreCase: false));
        Assert.Equal("bb", TextReplace.ReplaceAll("aaaa", "aa", "b", ignoreCase: false));
        Assert.Equal("Cửa d1", TextReplace.ReplaceAll("Cửa D1", "d", "d", ignoreCase: true));
        Assert.Equal("tường", TextReplace.ReplaceAll("tường", "cửa", "x", ignoreCase: false));
        Assert.Equal("ab", TextReplace.ReplaceAll("aXb", "X", null, ignoreCase: false));
    }

    /// <summary>Find rỗng từng làm vòng lặp IndexOf("") treo vô hạn — nay trả nguyên chuỗi.</summary>
    [Fact]
    public void TextReplace_ReplaceAll_FindRong_TraNguyen()
    {
        Assert.Equal("abc", TextReplace.ReplaceAll("abc", "", "x", true));
        Assert.Equal("abc", TextReplace.ReplaceAll("abc", null, "x", true));
        Assert.Equal(string.Empty, TextReplace.ReplaceAll(null, "a", "x", true));
        Assert.Equal(string.Empty, TextReplace.ReplaceAll("", "a", "x", true));
    }

    // ── LineWeightText ───────────────────────────────────────────────────

    [Theory]
    [InlineData("25", 25)]
    [InlineData(" 211 ", 211)]
    [InlineData("0", 0)]
    [InlineData("LineWeight025", 25)]
    [InlineData("lineweight050", 50)]
    [InlineData("ByLayer", -1)]
    [InlineData("byblock", -2)]
    [InlineData("Default", -3)]
    [InlineData("ByLineWeightDefault", -3)]
    [InlineData("0.25", 25)]
    [InlineData("0,5", 50)]
    [InlineData("-1", -1)]
    public void LineWeightText_TryParse_CacCachViet(string text, int expected)
    {
        Assert.True(LineWeightText.TryParse(text, out var value));
        Assert.Equal(expected, value);
        Assert.True(LineWeightText.IsDefined(value));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("26")]
    [InlineData("LineWeight026")]
    [InlineData("0.26")]
    [InlineData("dày")]
    [InlineData("-4")]
    public void LineWeightText_TryParse_GiaTriKhongCoTrongEnum_TuChoi(string text)
    {
        Assert.False(LineWeightText.TryParse(text, out _));
    }
}
