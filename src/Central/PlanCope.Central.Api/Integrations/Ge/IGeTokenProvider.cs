namespace PlanCope.Central.Api.Integrations.Ge;

public interface IGeTokenProvider
{
    Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default);

    void Invalidate(string accessToken);
}
