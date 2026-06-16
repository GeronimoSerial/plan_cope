namespace PlanCope.Shared.Domain.ValueObjects;

public readonly record struct ExamCode(string Value)
{
    public static bool IsValid(string value)
    {
        return !string.IsNullOrWhiteSpace(value) &&
               value.Length <= 64 &&
               value.All(static c => char.IsLetterOrDigit(c) || c is '-' or '_');
    }
}
