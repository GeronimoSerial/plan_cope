using System.Text.RegularExpressions;

namespace PlanCope.Local.Host.Services;

public static class UpdateFailureLogger
{
    public static void Log(string logsDirectory, string? feedUrl, string? accessToken, string operation, Exception exception)
    {
        var httpStatus = exception is HttpRequestException { StatusCode: { } statusCode }
            ? $" HTTP {(int)statusCode} ({statusCode})."
            : string.Empty;
        var safeMessage = exception.Message;
        if (!string.IsNullOrEmpty(accessToken))
        {
            safeMessage = safeMessage.Replace(accessToken, "[redacted]", StringComparison.Ordinal);
        }
        safeMessage = Regex.Replace(safeMessage, @"https?://[^\s""']+", match => RedactUrlQuery(match.Value));

        var safeUrl = string.IsNullOrWhiteSpace(feedUrl) ? "not configured" : RedactUrlQuery(feedUrl);
        var line = $"{DateTimeOffset.UtcNow:O} Update {operation} failed: {exception.GetType().Name}:{httpStatus} {safeMessage} URL={safeUrl}{Environment.NewLine}";
        try
        {
            Directory.CreateDirectory(logsDirectory);
            File.AppendAllText(Path.Combine(logsDirectory, "local-host.log"), line);
        }
        catch
        {
            // Update diagnostics must not interrupt the host's normal error handling.
        }
    }

    private static string RedactUrlQuery(string value)
    {
        var trailing = value.Length - value.TrimEnd('.', ',', ')', ']', ';').Length;
        var candidate = trailing == 0 ? value : value[..^trailing];
        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri)) return value;
        var builder = new UriBuilder(uri) { Query = string.Empty, Fragment = string.Empty };
        return builder.Uri + (trailing == 0 ? string.Empty : value[^trailing..]);
    }
}
