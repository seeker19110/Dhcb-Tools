using DhcbTools.Shared.Logic.Mep;
using Xunit;

namespace DhcbTools.Shared.Logic.Tests;

public class MepAuditBoundaryTests
{
    private static readonly Point2[] Room = { new(0, 0), new(1600, 0), new(1600, 1600), new(0, 1600) };

    [Fact]
    public void CoverageBudgetReportsRemainingUncoveredSamples()
    {
        var plan = DevicePattern.GridInPolygon(Room, new GridPatternOptions {
            SpacingX = 2000, SpacingY = 2000, Margin = 0, CoverageRadius = 1, CoverageCheckStep = 50 });
        Assert.Equal(500, plan.AddedForCoverage.Count);
        Assert.Equal(524, plan.Uncovered.Count);
        Assert.Contains(plan.Messages, m => m.Contains("500"));
        Assert.All(plan.Uncovered, point => Assert.True(plan.Points.All(d => d.DistanceTo(point) > 1)));
    }

    [Fact]
    public void ExtremelyLargeObstacleCannotDisappearDuringRasterization()
    {
        var obstacle = new Box3(-1e20, -1e20, -1e20, 1e20, 1e20, 1e20);
        var result = PathFinder3D.FindPath(new Point3(0, 0, 0), new Point3(1000, 0, 0), new[] { obstacle },
            new Box3(0, 0, 0, 1000, 1000, 0), new PathFinderOptions { StepMm = 100, ClearanceMm = 0 });
        Assert.False(result.Found);
        Assert.Contains("chướng ngại", result.Reason);
    }

    [Fact]
    public void OversizedGridRejectsBeforeCastingAndAllocating()
    {
        var result = PathFinder3D.FindPath(new Point3(0, 0, 0), new Point3(1, 0, 0), Array.Empty<Box3>(),
            new Box3(0, 0, 0, 1e20, 1e20, 1e20), new PathFinderOptions { StepMm = 1 });
        Assert.False(result.Found);
        Assert.Contains("quá lớn", result.Reason);
        Assert.True(result.GridCells > 16000000);
    }

    [Fact]
    public void ManualCellBudgetCannotBypassArrayIndexLimit()
    {
        var result = PathFinder3D.FindPath(new Point3(0, 0, 0), new Point3(1, 0, 0), Array.Empty<Box3>(),
            new Box3(0, 0, 0, 49999, 49999, 0), new PathFinderOptions { StepMm = 1, MaxCells = long.MaxValue });
        Assert.False(result.Found);
        Assert.Contains("quá lớn", result.Reason);
        Assert.Equal(2500000000, result.GridCells);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void PathFinderRejectsNonFiniteInputs(double value)
    {
        var bounds = new Box3(0, 0, 0, 1000, 1000, 0);
        var start = new Point3(0, 0, 0); var goal = new Point3(1000, 0, 0);
        Assert.Throws<ArgumentOutOfRangeException>(() => PathFinder3D.FindPath(start, goal, Array.Empty<Box3>(), bounds, new PathFinderOptions { StepMm = value }));
        Assert.Throws<ArgumentOutOfRangeException>(() => PathFinder3D.FindPath(start, goal, Array.Empty<Box3>(), bounds, new PathFinderOptions { ClearanceMm = value }));
        Assert.Throws<ArgumentOutOfRangeException>(() => PathFinder3D.FindPath(start, goal, Array.Empty<Box3>(), bounds, new PathFinderOptions { TurnPenalty = value }));
        Assert.Throws<ArgumentOutOfRangeException>(() => PathFinder3D.FindPath(start, goal, Array.Empty<Box3>(), bounds, new PathFinderOptions { NearObstaclePenalty = value }));
        Assert.Throws<ArgumentOutOfRangeException>(() => PathFinder3D.FindPath(new Point3(value, 0, 0), goal, Array.Empty<Box3>(), bounds));
        Assert.Throws<ArgumentOutOfRangeException>(() => PathFinder3D.FindPath(start, new Point3(0, value, 0), Array.Empty<Box3>(), bounds));
        Assert.Throws<ArgumentOutOfRangeException>(() => PathFinder3D.FindPath(start, goal, Array.Empty<Box3>(), new Box3(0, 0, 0, value, 1, 1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => PathFinder3D.FindPath(start, goal, new[] { new Box3(0, 0, 0, value, 1, 1) }, bounds));
    }

    [Fact]
    public void PathFinderRejectsNegativeCostsAndInvalidBudgets()
    {
        var bounds = new Box3(0, 0, 0, 1000, 1000, 0);
        var start = new Point3(0, 0, 0); var goal = new Point3(1000, 0, 0);
        foreach (var options in new[] { new PathFinderOptions { ClearanceMm = -1 }, new PathFinderOptions { TurnPenalty = -1 },
            new PathFinderOptions { NearObstaclePenalty = -1 }, new PathFinderOptions { MaxCells = 0 }, new PathFinderOptions { MaxExpandedNodes = 0 } })
            Assert.Throws<ArgumentOutOfRangeException>(() => PathFinder3D.FindPath(start, goal, Array.Empty<Box3>(), bounds, options));
        Assert.Throws<ArgumentNullException>(() => PathFinder3D.FindPath(start, goal, Array.Empty<Box3>(), null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => PathFinder3D.FindPath(start, goal, new Box3[] { null! }, bounds));
        Assert.Equal(PathFinderOptions.AutoBudgetMax, new PathFinderOptions().EffectiveMaxExpandedNodes(long.MaxValue));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void DeviceGridRejectsNonFiniteGeometryAndOptions(double value)
    {
        foreach (var options in new[] { new GridPatternOptions { SpacingX = value }, new GridPatternOptions { SpacingY = value },
            new GridPatternOptions { Margin = value }, new GridPatternOptions { CoverageRadius = value }, new GridPatternOptions { CoverageCheckStep = value } })
            Assert.Throws<ArgumentOutOfRangeException>(() => DevicePattern.GridInPolygon(Room, options));
        Assert.Throws<ArgumentException>(() => DevicePattern.GridInPolygon(new[] { new Point2(value, 0), new Point2(0, 1), new Point2(1, 0) }, new GridPatternOptions()));
    }

    [Fact]
    public void DeviceGridRejectsMissingOptionsAndNegativeMargin()
    {
        Assert.Throws<ArgumentNullException>(() => DevicePattern.GridInPolygon(Room, null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => DevicePattern.GridInPolygon(Room, new GridPatternOptions { Margin = -1 }));
    }
}
