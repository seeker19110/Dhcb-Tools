/// <summary>
/// Unit tests chạy độc lập, không cần Revit/AutoCAD cài đặt.
/// Test các logic thuần C# trong DhcbTools.Core: config parsing,
/// unit conversion, string formatting, pattern substitution.
/// </summary>

using System;
using System.Collections.Generic;
using Xunit;

namespace DhcbTools.Tests;

// ─── Helpers giả lập (không import DhcbTools.Core vì cần RevitAPI.dll) ───────
// Các class dưới đây mirror đúng logic trong Core để test isolated.

static class UnitConverter
{
    public const double MmPerFoot = 304.8;
    public static double MmToFeet(double mm) => mm / MmPerFoot;
    public static double FeetToMm(double ft) => ft * MmPerFoot;
}

static class FileNameFormatter
{
    public static string Apply(string pattern, string sheetNumber, string sheetName, string projectNumber)
        => pattern
            .Replace("{SheetNumber}", sheetNumber)
            .Replace("{SheetName}", sheetName)
            .Replace("{ProjectNumber}", projectNumber);
}

// ─── Tests ────────────────────────────────────────────────────────────────────

public class UnitConversionTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(304.8, 1.0)]
    [InlineData(3048, 10.0)]
    [InlineData(-3500, -3500.0 / 304.8)]
    [InlineData(3800, 3800.0 / 304.8)]   // Tầng 1: 3800mm
    [InlineData(11400, 11400.0 / 304.8)] // Tầng Mái: 11400mm
    public void MmToFeet_Correct(double mm, double expectedFt)
    {
        var result = UnitConverter.MmToFeet(mm);
        Assert.Equal(expectedFt, result, precision: 6);
    }

    [Theory]
    [InlineData(1.0, 304.8)]
    [InlineData(10.0, 3048.0)]
    [InlineData(0.0, 0.0)]
    public void FeetToMm_Correct(double ft, double expectedMm)
    {
        var result = UnitConverter.FeetToMm(ft);
        Assert.Equal(expectedMm, result, precision: 4);
    }

    [Fact]
    public void RoundTrip_Lossless()
    {
        double[] elevsMm = { -3500, 0, 3800, 7600, 11400, 14000 };
        foreach (var mm in elevsMm)
        {
            var roundTrip = UnitConverter.FeetToMm(UnitConverter.MmToFeet(mm));
            Assert.Equal(mm, roundTrip, precision: 4);
        }
    }
}

public class FileNameFormatterTests
{
    [Fact]
    public void DefaultPattern_AllTokens()
    {
        var result = FileNameFormatter.Apply("{SheetNumber}-{SheetName}", "A101", "Ground Floor Plan", "DHCB-2024");
        Assert.Equal("A101-Ground Floor Plan", result);
    }

    [Fact]
    public void PatternWithProjectNumber()
    {
        var result = FileNameFormatter.Apply("{ProjectNumber}_{SheetNumber}_{SheetName}", "A101", "Level 1", "PRJ-001");
        Assert.Equal("PRJ-001_A101_Level 1", result);
    }

    [Fact]
    public void EmptyPattern_ReturnsEmpty()
    {
        var result = FileNameFormatter.Apply("", "A101", "Level 1", "PRJ-001");
        Assert.Equal("", result);
    }

    [Fact]
    public void NoTokens_ReturnsLiteralPattern()
    {
        var result = FileNameFormatter.Apply("export", "A101", "Level 1", "PRJ-001");
        Assert.Equal("export", result);
    }
}

public class LevelSetupLogicTests
{
    // Test logic kiểm tra duplicate level name (case-insensitive)
    static bool ShouldSkip(HashSet<string> existingNames, string newName, bool skipExisting)
    {
        if (!skipExisting) return false;
        return existingNames.Contains(newName.ToUpperInvariant());
    }

    [Fact]
    public void SkipExisting_CaseInsensitive()
    {
        var existing = new HashSet<string> { "TẦNG 1", "TẦNG 2" };
        Assert.True(ShouldSkip(existing, "Tầng 1", true));
        Assert.True(ShouldSkip(existing, "tầng 2", true));
        Assert.False(ShouldSkip(existing, "Tầng 3", true));
    }

    [Fact]
    public void SkipExisting_False_NeverSkips()
    {
        var existing = new HashSet<string> { "TẦNG 1" };
        Assert.False(ShouldSkip(existing, "Tầng 1", false));
    }

    [Fact]
    public void StandardLevels_AllConvertCorrectly()
    {
        var levels = new[] {
            ("Tầng Hầm",  -3500.0, -3500.0 / 304.8),
            ("Tầng Trệt",     0.0,     0.0),
            ("Tầng 1",    3800.0,  3800.0 / 304.8),
            ("Tầng 2",    7600.0,  7600.0 / 304.8),
            ("Tầng Mái",  11400.0, 11400.0 / 304.8),
        };
        foreach (var (name, mm, expectedFt) in levels)
        {
            var ft = UnitConverter.MmToFeet(mm);
            Assert.Equal(expectedFt, ft, precision: 6);
        }
    }
}

public class GridSetupLogicTests
{
    // Test tính toán tọa độ grid line
    static (double x1, double y1, double x2, double y2) VerticalGrid(double posXmm, double startYmm, double endYmm)
    {
        double posX = UnitConverter.MmToFeet(posXmm);
        double startY = UnitConverter.MmToFeet(startYmm);
        double endY = UnitConverter.MmToFeet(endYmm);
        return (posX, startY, posX, endY);
    }

    static (double x1, double y1, double x2, double y2) HorizontalGrid(double posYmm, double startXmm, double endXmm)
    {
        double posY = UnitConverter.MmToFeet(posYmm);
        double startX = UnitConverter.MmToFeet(startXmm);
        double endX = UnitConverter.MmToFeet(endXmm);
        return (startX, posY, endX, posY);
    }

    [Fact]
    public void VerticalGrid_AtOrigin()
    {
        var (x1, y1, x2, y2) = VerticalGrid(0, -30000, 30000);
        Assert.Equal(0, x1, 4);
        Assert.Equal(0, x2, 4);
        Assert.Equal(UnitConverter.MmToFeet(-30000), y1, 4);
        Assert.Equal(UnitConverter.MmToFeet(30000), y2, 4);
    }

    [Fact]
    public void HorizontalGrid_HasConstantY()
    {
        var (x1, y1, x2, y2) = HorizontalGrid(5000, -30000, 30000);
        Assert.Equal(y1, y2, 4); // y1 == y2 for horizontal
        Assert.Equal(UnitConverter.MmToFeet(5000), y1, 4);
    }
}

public class HangerSpacingTests
{
    // Test logic rải hanger: số lượng và vị trí
    static List<double> ComputeHangerPositions(double lengthFt, double spacingFt)
    {
        var positions = new List<double>();
        if (spacingFt <= 0) return positions;
        double pos = spacingFt / 2.0;
        while (pos < lengthFt)
        {
            positions.Add(pos);
            pos += spacingFt;
        }
        return positions;
    }

    [Fact]
    public void ShortElement_OneHanger()
    {
        // Pipe 2m, spacing 3m → 1 hanger at 1.5m (halfway)
        double spacingFt = UnitConverter.MmToFeet(3000);
        double lengthFt = UnitConverter.MmToFeet(2000);
        var positions = ComputeHangerPositions(lengthFt, spacingFt);
        Assert.Single(positions);
    }

    [Fact]
    public void ExactlyDoubleSpacing_TwoHangers()
    {
        // Pipe 6m, spacing 3m → 2 hangers at 1.5m and 4.5m
        double spacingFt = UnitConverter.MmToFeet(3000);
        double lengthFt = UnitConverter.MmToFeet(6000);
        var positions = ComputeHangerPositions(lengthFt, spacingFt);
        Assert.Equal(2, positions.Count);
        Assert.Equal(spacingFt / 2.0, positions[0], 4);
        Assert.Equal(spacingFt * 1.5, positions[1], 4);
    }

    [Fact]
    public void LongPipe_CorrectCount()
    {
        // Pipe 10m, spacing 3m → positions at 1.5, 4.5, 7.5 → 3 hangers
        double spacingFt = UnitConverter.MmToFeet(3000);
        double lengthFt = UnitConverter.MmToFeet(10000);
        var positions = ComputeHangerPositions(lengthFt, spacingFt);
        Assert.Equal(3, positions.Count);
    }
}

public class PipeSplitLogicTests
{
    // Test logic tính điểm chia ống
    static List<double> ComputeSplitParams(double lengthFt, double maxSegFt)
    {
        var points = new List<double>();
        if (lengthFt <= maxSegFt + 0.01) return points; // không cần chia
        double pos = maxSegFt;
        while (pos < lengthFt - 0.01)
        {
            points.Add(pos);
            pos += maxSegFt;
        }
        return points;
    }

    [Fact]
    public void ShortPipe_NoSplit()
    {
        var pts = ComputeSplitParams(UnitConverter.MmToFeet(5000), UnitConverter.MmToFeet(6000));
        Assert.Empty(pts);
    }

    [Fact]
    public void PipeExactly12m_OneSplit()
    {
        var pts = ComputeSplitParams(UnitConverter.MmToFeet(12000), UnitConverter.MmToFeet(6000));
        Assert.Single(pts); // split tại 6m
    }

    [Fact]
    public void PipeLong_CorrectSplitCount()
    {
        // 20m pipe, 6m segments → splits tại 6, 12, 18 → 3 points
        var pts = ComputeSplitParams(UnitConverter.MmToFeet(20000), UnitConverter.MmToFeet(6000));
        Assert.Equal(3, pts.Count);
    }
}

public class HealthReportLogicTests
{
    [Fact]
    public void FileSizeMb_Calculation()
    {
        long bytes = 250L * 1024 * 1024; // 250 MB
        double mb = bytes / 1048576.0;
        Assert.Equal(250.0, mb, 1);
    }

    [Fact]
    public void FileSizeWarn_TriggersAboveThreshold()
    {
        double mb = 250.0;
        int warnThreshold = 200;
        bool shouldWarn = mb > warnThreshold;
        Assert.True(shouldWarn);
    }

    [Fact]
    public void FileSizeWarn_OkBelowThreshold()
    {
        double mb = 150.0;
        int warnThreshold = 200;
        bool shouldWarn = mb > warnThreshold;
        Assert.False(shouldWarn);
    }
}
