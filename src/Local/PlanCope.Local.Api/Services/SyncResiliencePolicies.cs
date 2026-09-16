using Polly;
using Polly.Extensions.Http;
using System.Net.Http;

namespace PlanCope.Local.Api.Services;

public static class SyncResiliencePolicies
{
    private sealed class RetryAndCircuitBreakerHandler : DelegatingHandler
    {
        private readonly IAsyncPolicy<HttpResponseMessage> _policies;

        public RetryAndCircuitBreakerHandler()
        {
            var retry = HttpPolicyExtensions.HandleTransientHttpError()
                .WaitAndRetryAsync(3, attempt =>
                    TimeSpan.FromSeconds(Math.Pow(2, attempt)) +
                    TimeSpan.FromMilliseconds(Random.Shared.Next(0, 1000)));
            var circuitBreaker = HttpPolicyExtensions.HandleTransientHttpError()
                .CircuitBreakerAsync(5, TimeSpan.FromSeconds(30));
            _policies = Policy.WrapAsync(retry, circuitBreaker);
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return await _policies.ExecuteAsync(
                (ct) => base.SendAsync(request, ct), cancellationToken);
        }
    }

    public static IHttpClientBuilder AddSyncResilience(this IHttpClientBuilder builder) =>
        builder.AddHttpMessageHandler(() => new RetryAndCircuitBreakerHandler());
}