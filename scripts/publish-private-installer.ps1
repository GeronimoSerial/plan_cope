# Backend rationale (docs/plan-cicd-batches.md, section 9, open question #2 -- RESOLVED):
# the private installer is hosted in a PRIVATE GitHub repository using its own
# Releases mechanism. Central authenticates against that repo with a read-scoped
# token referenced by secret name only (e.g. INSTALLER_REPO_TOKEN) -- never a
# value, never hardcoded. That is why this script targets -PrivateRepo and never
# a public GitHub Release, and why it never embeds a token: it relies on the
# token that gh already picks up from its environment (GH_TOKEN / GITHUB_TOKEN).
<#
.SYNOPSIS
    Publishes the signed installer as an asset of a private GitHub Release.

.DESCRIPTION
    Uploads the signed Velopack installer to the Releases mechanism of a PRIVATE
    GitHub repository. If a release tagged with -Version already exists in that
    repo it is reused; otherwise it is created. The asset URL and the SHA-256
    checksum of the uploaded file are printed to stdout and, when running inside
    GitHub Actions ($env:GITHUB_OUTPUT present), emitted as the output variables
    `installer-reference` and `installer-sha256`.

    Authentication is delegated entirely to the `gh` CLI, which picks up the
    token from its standard environment (GH_TOKEN or GITHUB_TOKEN). This script
    NEVER reads, embeds, prints, or persists a token value. It fails fast if
    `gh auth status` fails, so a missing or invalid token is never silently
    retried against another mechanism.

    Only the signed installer passed via -InstallerPath is handled. This script
    never touches the census dataset used by packaging; it only uploads the
    installer asset.

.PARAMETER InstallerPath
    Required. Path to the signed installer to upload as the release asset.

.PARAMETER Version
    Required. Release version; used as the tag of the release in the private
    repository (e.g. 1.2.3).

.PARAMETER Channel
    Required. Release channel: 'stable' or 'beta'. Beta releases are created as
    GitHub prereleases; a re-used release keeps its original state.

.PARAMETER PrivateRepo
    Owner/repo of the private GitHub repository that hosts the installer
    Releases (e.g. 'acme/plan-cope-installers'). Taken from this parameter or,
    if omitted, from the PLANCOPE_PRIVATE_INSTALLER_REPO environment variable.
    Never hardcoded; if neither is present the script exits non-zero.

.EXAMPLE
    ./scripts/publish-private-installer.ps1 -InstallerPath '.\artifacts\PlanCope.setup.exe' -Version '1.2.3' -Channel 'stable' -PrivateRepo 'acme/plan-cope-installers'

.EXAMPLE
    $env:PLANCOPE_PRIVATE_INSTALLER_REPO = 'acme/plan-cope-installers'
    ./scripts/publish-private-installer.ps1 -InstallerPath '.\PlanCope.setup.exe' -Version '1.2.3-rc.1' -Channel 'beta'

.NOTES
    Requires the `gh` CLI (https://cli.github.com) and an authenticated token
    available to it via GH_TOKEN or GITHUB_TOKEN with permission to read and
    create releases in -PrivateRepo. The resulting asset URL is private: it
    requires authenticated access to fetch.
#>
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [string] $InstallerPath,

    [Parameter(Mandatory = $true, Position = 1)]
    [string] $Version,

    [Parameter(Mandatory = $true, Position = 2)]
    [ValidateSet('stable', 'beta')]
    [string] $Channel,

    [Parameter(Mandatory = $false)]
    [string] $PrivateRepo = $env:PLANCOPE_PRIVATE_INSTALLER_REPO
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($PrivateRepo)) {
    Write-Error "No private repository specified: -PrivateRepo is missing and the PLANCOPE_PRIVATE_INSTALLER_REPO environment variable is not set. Refusing to guess a repository name."
    exit 1
}

if ([string]::IsNullOrWhiteSpace($InstallerPath)) {
    Write-Error "-InstallerPath is required."
    exit 1
}

if (-not (Test-Path -LiteralPath $InstallerPath -PathType Leaf)) {
    Write-Error "Installer file not found: $InstallerPath"
    exit 1
}

if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
    Write-Error "The 'gh' CLI is not installed or not on PATH. Install GitHub CLI (https://cli.github.com) and retry."
    exit 1
}

& gh auth status
if ($LASTEXITCODE -ne 0) {
    Write-Error "gh is not authenticated. This script relies solely on the token that gh picks up from its environment (GH_TOKEN or GITHUB_TOKEN); it never reads or embeds a token value itself. Authenticate gh and retry."
    exit 1
}

$assetName = Split-Path -LiteralPath $InstallerPath -Leaf
$sha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $InstallerPath).Hash.ToLowerInvariant()

& gh release view $Version --repo $PrivateRepo --json tagName *> $null
$releaseExists = ($LASTEXITCODE -eq 0)
if ($releaseExists) {
    Write-Host "Release '$Version' already exists in '$PrivateRepo'; reusing it."
} else {
    Write-Host "Release '$Version' not found in '$PrivateRepo'; creating it."
    $createArgs = @('release', 'create', $Version, '--repo', $PrivateRepo,
        '--title', "PlanCope $Version ($Channel)",
        '--notes', "Private installer release for PlanCope $Version on the $Channel channel. The asset requires authenticated access to $PrivateRepo.")
    if ($Channel -eq 'beta') {
        $createArgs += '--prerelease'
    }
    & gh @createArgs
    if ($LASTEXITCODE -ne 0) {
        Write-Error "gh release create failed for '$Version' in '$PrivateRepo' (exit code $LASTEXITCODE)."
        exit 1
    }
}

& gh release upload $Version --repo $PrivateRepo --clobber $InstallerPath
if ($LASTEXITCODE -ne 0) {
    Write-Error "gh release upload failed for '$assetName' onto release '$Version' in '$PrivateRepo' (exit code $LASTEXITCODE)."
    exit 1
}

$releaseAssets = (& gh release view $Version --repo $PrivateRepo --json assets) | ConvertFrom-Json
$asset = $releaseAssets.assets | Where-Object { $_.name -eq $assetName } | Select-Object -First 1
if (-not $asset) {
    Write-Error "Uploaded asset '$assetName' was not found in release '$Version' of '$PrivateRepo'."
    exit 1
}
$assetUrl = $asset.browser_download_url

Write-Output "Private installer published to $assetUrl"
Write-Output "sha256sum: $sha256  $assetName"

if (-not [string]::IsNullOrWhiteSpace($env:GITHUB_OUTPUT)) {
    "installer-reference=$assetUrl" | Out-File -FilePath $env:GITHUB_OUTPUT -Append -Encoding ascii
    "installer-sha256=$sha256" | Out-File -FilePath $env:GITHUB_OUTPUT -Append -Encoding ascii
    Write-Host "GitHub Actions outputs set: installer-reference=$assetUrl, installer-sha256=$sha256"
}