using System.Text.RegularExpressions;
using DhcbTools.Shared.Logic.Cad;
using Xunit;

namespace DhcbTools.Shared.Logic.Tests;

public sealed class MTextFieldProtectionTests
{
    [Theory]
    [InlineData("A%<\\AcVar Date>%B", "A\uFFFCB")]
    [InlineData("%<outer %<inner>% tail>% after", "\uFFFC after")]
    [InlineData("before %<unfinished", "before \uFFFC")]
    [InlineData("%<", "\uFFFC")]
    [InlineData("100% complete > or < test", "100% complete > or < test")]
    public void FieldExpressionsAreOpaqueIncludingNestedAndTruncatedFields(string contents, string expected)
    {
        Assert.Equal(expected, MTextReplace.DisplayText(contents));
    }

    [Fact]
    public void LiteralReplacementPreservesNestedFieldsAndStillChangesSurroundingText()
    {
        const string contents = "OLD %<OLD %<OLD>% OLD>% OLD";
        var result = MTextReplace.ReplaceAll(contents, "OLD", "NEW", false);
        Assert.Equal("NEW %<OLD %<OLD>% OLD>% NEW", result.Contents);
        Assert.Equal(2, result.Replaced);
        Assert.Equal(0, result.Skipped);
    }

    [Fact]
    public void RegexCannotEraseFieldExpressionsOrMatchAcrossThem()
    {
        const string contents = "A%<field>%B";
        var result = MTextReplace.Replace(contents, new Regex(".", RegexOptions.None, TimeSpan.FromSeconds(1)), "X");
        Assert.Equal("X%<field>%X", result.Contents);
        Assert.Equal(2, result.Replaced);
        Assert.Equal(1, result.Skipped);
        var spanning = MTextReplace.Replace(contents, new Regex("A.*B", RegexOptions.None, TimeSpan.FromSeconds(1)), "X");
        Assert.Equal(contents, spanning.Contents);
        Assert.Equal(1, spanning.Skipped);
    }
}
