using DhcbTools.Shared.Logic;
using Xunit;

namespace DhcbTools.Shared.Logic.Tests;

public class RevitElementIdValueTests
{
    [Theory]
    [InlineData("2147483648")]
    [InlineData("4294967297")]
    [InlineData("-2147483649")]
    public void LegacyHostsRejectIdsThatWouldSelectAnotherElement(string text)
    {
        Assert.False(RevitElementIdValue.TryParse(text, false, out var value));
        Assert.Equal(0, value);
        Assert.Throws<OverflowException>(() => RevitElementIdValue.ToLegacyValue(long.Parse(text)));
    }

    [Theory]
    [InlineData("2147483648", 2147483648L)]
    [InlineData("4294967297", 4294967297L)]
    [InlineData("9223372036854775807", long.MaxValue)]
    [InlineData("-9223372036854775808", long.MinValue)]
    public void ModernHostsPreserveTheEntire64BitId(string text, long expected)
    {
        Assert.True(RevitElementIdValue.TryParse(text, true, out var value));
        Assert.Equal(expected, value);
    }

    [Theory]
    [InlineData(" 2147483647 ", int.MaxValue)]
    [InlineData("-2147483648", int.MinValue)]
    [InlineData("-1", -1)]
    [InlineData("42", 42)]
    public void LegacyHostsPreserveValidValuesIncludingBuiltInIds(string text, int expected)
    {
        Assert.True(RevitElementIdValue.TryParse(text, false, out var value));
        Assert.Equal(expected, RevitElementIdValue.ToLegacyValue(value));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("9223372036854775808")]
    [InlineData("1.0")]
    [InlineData("1,234")]
    public void InvalidInputIsRejectedOnAllHosts(string? text)
    {
        Assert.False(RevitElementIdValue.TryParse(text, false, out _));
        Assert.False(RevitElementIdValue.TryParse(text, true, out _));
    }
}
