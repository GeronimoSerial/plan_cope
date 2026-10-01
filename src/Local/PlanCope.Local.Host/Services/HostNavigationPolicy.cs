namespace PlanCope.Local.Host.Services;

internal enum HostNavigationAction
{
    Allow,
    Cancel,
    OpenExternal
}

internal static class HostNavigationPolicy
{
    internal static HostNavigationAction Decide(Uri clientAppUri, string destination, bool isTopFrame)
    {
        if (!Uri.TryCreate(destination, UriKind.Absolute, out var destinationUri))
        {
            return HostNavigationAction.Cancel;
        }

        if (!isTopFrame || SameOrigin(clientAppUri, destinationUri))
        {
            return HostNavigationAction.Allow;
        }

        return destinationUri.Scheme is "http" or "https"
            ? HostNavigationAction.OpenExternal
            : HostNavigationAction.Cancel;
    }

    internal static bool IsClientAppOrigin(Uri clientAppUri, string destination)
        => Uri.TryCreate(destination, UriKind.Absolute, out var destinationUri) && SameOrigin(clientAppUri, destinationUri);

    private static bool SameOrigin(Uri left, Uri right)
        => left.Scheme.Equals(right.Scheme, StringComparison.OrdinalIgnoreCase)
            && left.Host.Equals(right.Host, StringComparison.OrdinalIgnoreCase)
            && left.Port == right.Port;
}
