using PlanCope.Shared.Domain.ValueObjects;
using Xunit;

namespace PlanCope.Shared.Tests;

public sealed class CueCodeTests
{
    [Theory]
    [InlineData("180055400", "180055400")]
    [InlineData("1800554-00", "180055400")]
    [InlineData(" 1800554 00 ", "180055400")]
    public void Normalize_accepts_common_input_and_returns_one_canonical_format(string input, string expected)
    {
        Assert.Equal(expected, CueCode.Normalize(input));
    }

    [Theory]
    [InlineData("")]
    [InlineData("1800554")]
    [InlineData("1800554000")]
    [InlineData("CUE180055400")]
    public void Normalize_rejects_values_without_exactly_nine_digits(string input)
    {
        Assert.False(CueCode.TryNormalize(input, out _));
        Assert.Throws<ArgumentException>(() => CueCode.Normalize(input));
    }

    [Fact]
    public void Ge_api_format_separates_the_two_digit_annex()
    {
        Assert.Equal("1800554-00", CueCode.ToGeApiFormat("180055400"));
    }
}
