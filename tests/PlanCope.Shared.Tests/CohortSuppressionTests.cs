using PlanCope.Shared.Domain;
using Xunit;

namespace PlanCope.Shared.Tests;

public sealed class CohortSuppressionTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(1000)]
    public void School_scope_is_never_suppressed(int cohortCount)
    {
        Assert.False(CohortSuppression.Applies("school", cohortCount));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(4)]
    public void Province_scope_is_suppressed_below_minimum(int cohortCount)
    {
        Assert.True(CohortSuppression.Applies("province", cohortCount));
    }

    [Theory]
    [InlineData(5)]
    [InlineData(1000)]
    public void Province_scope_is_not_suppressed_at_or_above_minimum(int cohortCount)
    {
        Assert.False(CohortSuppression.Applies("province", cohortCount));
    }

    [Fact]
    public void Unknown_scope_is_treated_like_not_school()
    {
        Assert.True(CohortSuppression.Applies("district", 4));
        Assert.False(CohortSuppression.Applies("district", 5));
    }

    [Fact]
    public void Suppressed_result_exposes_the_fixed_label_and_no_value()
    {
        var result = SuppressibleValue<int>.For("province", 4, 42);

        Assert.True(result.IsSuppressed);
        Assert.Equal("cohorte insuficiente", SuppressibleValue<int>.SuppressionLabel);
        Assert.Throws<InvalidOperationException>(() => result.Value);
    }

    [Fact]
    public void Not_suppressed_result_exposes_the_real_value()
    {
        var result = SuppressibleValue<int>.For("province", 5, 42);

        Assert.False(result.IsSuppressed);
        Assert.Equal(42, result.Value);
    }

    [Fact]
    public void School_scope_exposes_the_real_value_even_at_zero()
    {
        var result = SuppressibleValue<int>.For("school", 0, 7);

        Assert.False(result.IsSuppressed);
        Assert.Equal(7, result.Value);
    }
}