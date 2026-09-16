namespace PlanCope.Shared.Domain;

public static class CohortSuppression
{
    public const int MinimumCohort = 5;

    public static bool Applies(string rosterScope, int cohortCount) =>
        rosterScope != "school" && cohortCount < MinimumCohort;
}

public readonly record struct SuppressibleValue<T>
{
    public const string SuppressionLabel = "cohorte insuficiente";

    private readonly T _value;

    private SuppressibleValue(T value, bool isSuppressed)
    {
        _value = value;
        IsSuppressed = isSuppressed;
    }

    public bool IsSuppressed { get; }

    // Throws instead of returning default so a suppressed result can never render as
    // "0 students" (misreads as a real zero) or a blank (misreads as "no data").
    public T Value =>
        IsSuppressed
            ? throw new InvalidOperationException($"Value is unavailable while suppressed; render {SuppressionLabel} instead.")
            : _value;

    public static SuppressibleValue<T> For(string rosterScope, int cohortCount, T value) =>
        CohortSuppression.Applies(rosterScope, cohortCount)
            ? new SuppressibleValue<T>(default!, isSuppressed: true)
            : new SuppressibleValue<T>(value, isSuppressed: false);
}