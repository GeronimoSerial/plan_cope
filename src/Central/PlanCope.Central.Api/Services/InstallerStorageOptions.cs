namespace PlanCope.Central.Api.Services;

/// <summary>
/// Configuration for the private GitHub installer backend. Values come from the
/// environment / configuration, never from literals:
///   PLANCOPE_PRIVATE_INSTALLER_REPO — "owner/repo" of the private repo holding installer
///                                     releases (same convention as scripts/publish-private-installer.ps1).
///   INSTALLER_REPO_TOKEN            — read-scoped GitHub token for that repo.
/// Both must be present at startup for <see cref="GitHubReleaseInstallerStorage"/> to be
/// registered; otherwise <see cref="NotConfiguredInstallerStorage"/> is used.
/// </summary>
public sealed class InstallerStorageOptions
{
    public const string SectionName = "InstallerStorage";

    public string Repo { get; set; } = string.Empty;

    public string Token { get; set; } = string.Empty;
}