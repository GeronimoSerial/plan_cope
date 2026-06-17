namespace PlanCope.Central.Api.Auth;

public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    public string Issuer { get; init; } = "PlanCope.Central";

    public string Audience { get; init; } = "PlanCope.Clients";

    public string SigningKey { get; init; } = string.Empty;

    public int AccessTokenMinutes { get; init; } = 30;

    public int RefreshTokenDays { get; init; } = 7;
}
