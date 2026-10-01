namespace PlanCope.Local.Host.Services;

/// <summary>Small, deterministic policies used by the update status reporting path.</summary>
public sealed class UpdateProgressThrottle
{
    private int? _lastProgress;
    private DateTimeOffset _lastReportedAt;

    public bool ShouldReport(int progress, DateTimeOffset now)
    {
        var value = Math.Clamp(progress, 0, 100);
        if (_lastProgress is null || value >= 100 || value - _lastProgress >= 2 || now - _lastReportedAt >= TimeSpan.FromMilliseconds(250))
        {
            _lastProgress = value;
            _lastReportedAt = now;
            return true;
        }

        return false;
    }
}

public static class SessionGateRetryPolicy
{
    public static TimeSpan GetDelay(int consecutiveFailures)
    {
        var exponent = Math.Clamp(consecutiveFailures - 1, 0, 5);
        return TimeSpan.FromSeconds(Math.Min(30, 2 << exponent));
    }

    public static string DescribeException(Exception exception)
        => "No se pudo verificar si hay una sesión activa. La actualización se aplicará cuando termine la sesión activa.";

    public static void LogFailure(Action<string> log, Exception exception, int consecutiveFailures)
        => log($"{DescribeException(exception)}. Reintento {consecutiveFailures} en {GetDelay(consecutiveFailures).TotalSeconds:0} segundos.");
}
