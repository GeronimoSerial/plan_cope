namespace PlanCope.Shared.Domain.ValueObjects;

public readonly record struct CueCode(string Value)
{
    public static bool IsValid(string value)
    {
        return value.Length is >= 9 and <= 10 && value.All(char.IsDigit);
    }
}
