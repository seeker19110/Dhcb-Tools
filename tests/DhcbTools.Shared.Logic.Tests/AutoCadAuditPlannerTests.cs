using DhcbTools.Shared.Logic.Cad;
using Xunit;

namespace DhcbTools.Shared.Logic.Tests;

public class AutoCadAuditPlannerTests
{
    [Theory]
    [InlineData(4, 123.5, 123.5)]
    [InlineData(0, 123.5, 123.5)]
    [InlineData(99, 123.5, 123.5)]
    [InlineData(6, 1.25, 1250)]
    [InlineData(5, -1.5, -15)]
    [InlineData(1, 2, 50.8)]
    [InlineData(2, 3, 914.4)]
    public void GridCsvConvertsDrawingCoordinatesToRevitMillimeters(int units, double value, double expected) =>
        Assert.Equal(expected, DrawingUnits.ToMillimeters(value, units), 9);

    [Fact]
    public void AttributeRowsKeepFirstOccurrenceOrder()
    {
        var plan = AttributeImportPlanner.Plan(new[] { (2, "1A", "MARK", "A"), (3, "1B", "MARK", "B"), (4, "1A", "OTHER", "C") });
        Assert.Equal(new[] { 2, 3, 4 }, plan.Rows.Select(r => r.Row));
        Assert.Empty(plan.Notes);
        Assert.Empty(plan.Conflicts);
        Assert.Empty(AttributeImportPlanner.Plan(Array.Empty<(int, string, string, string)>()).Rows);
    }

    [Fact]
    public void RepeatedRowsNormalizeHandleAndTagButKeepValueCase()
    {
        var plan = AttributeImportPlanner.Plan(new[] { (2, "1A", "MARK", "A"), (3, "0x1a", "mark", "A"), (4, "(1A)", "MARK", "A") });
        Assert.Single(plan.Rows);
        Assert.Equal(2, plan.Notes.Count);
        Assert.Empty(plan.Conflicts);
        var conflict = AttributeImportPlanner.Plan(new[] { (2, "1A", "MARK", "A"), (3, "1a", "mark", "a") });
        Assert.Empty(conflict.Rows);
        Assert.Single(conflict.Conflicts);
        Assert.Contains("2, 3", conflict.Conflicts[0]);
    }

    [Fact]
    public void ConflictingRowsNeverUseLastWriteWinsInEitherOrder()
    {
        var entries = new[] { (2, "1A", "MARK", "changed"), (3, "1A", "MARK", "original"), (4, "1B", "MARK", "independent") };
        foreach (var rows in new[] { entries, entries.Reverse().ToArray() })
        {
            var plan = AttributeImportPlanner.Plan(rows);
            Assert.Equal("1B", Assert.Single(plan.Rows).Handle);
            Assert.Single(plan.Conflicts);
        }
    }

    [Fact]
    public void InvalidHandleStillFlowsToHostDiagnosticsWithoutCollidingWithOtherTags()
    {
        var plan = AttributeImportPlanner.Plan(new[] { (2, "invalid", "MARK", "A"), (3, "INVALID", "mark", "A"), (4, "invalid", "OTHER", "A") });
        Assert.Equal(2, plan.Rows.Count);
        Assert.Single(plan.Notes);
        Assert.Throws<ArgumentNullException>(() => AttributeImportPlanner.Plan(null!));
    }
}
