namespace PlanCope.Shared.Domain.ValueObjects;

public readonly record struct Grade(int Value)
{
    public static bool IsValid(int value)
    {
        return value is >= 1 and <= 12;
    }
}
