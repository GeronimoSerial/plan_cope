namespace PlanCope.Central.Api.Integrations.Ge;

public sealed class GeApiOptions
{
    public const string SectionName = "GeApi";

    public string BaseUrl { get; init; } = "http://geapi.mec.gob.ar";

    public string Username { get; init; } = string.Empty;

    public string Password { get; init; } = string.Empty;

    public int PageSize { get; init; } = 100;

    public int TimeoutSeconds { get; init; } = 30;

    public int MaxPages { get; init; } = 1000;

    public int MaxStudents { get; init; } = 100_000;

    public int TokenSafetyMarginSeconds { get; init; } = 300;

    public int PersonBatchSize { get; init; } = 5;

    public int PersonBatchDelaySeconds { get; init; } = 2;
}
