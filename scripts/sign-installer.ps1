<#
.SYNOPSIS
    Signs one or more executables/installers with an Authenticode certificate.

.DESCRIPTION
    Reads the code-signing certificate (PFX, base64-encoded) and its password
    from the environment variables WINDOWS_SIGNING_PFX and
    WINDOWS_SIGNING_PASSWORD, decodes the PFX into a temporary file, signs
    the given paths with signtool.exe (SHA-256 + RFC 3161 timestamp), and
    deletes the temporary PFX in a finally block.

    The certificate and password are NEVER hardcoded and NEVER written to any
    output. If either environment variable is missing or empty, the script
    fails fast with a non-zero exit code before touching any file.

.PARAMETER Path
    One or more paths to the files to sign, e.g.
    'C:\path\to\App.exe','C:\path\to\Setup.exe'.

.EXAMPLE
    ./scripts/sign-installer.ps1 -Path 'C:\path\to\App.exe','C:\path\to\Setup.exe'

.NOTES
    Requires signtool.exe from the Windows SDK. Runs on windows-latest CI or a
    Windows dev machine. Configure the environment variables:
      WINDOWS_SIGNING_PFX       - base64-encoded PFX certificate
      WINDOWS_SIGNING_PASSWORD  - PFX password
#>
param(
    [Parameter(Mandatory = $true)]
    [string[]] $Path
)

$ErrorActionPreference = "Stop"

$pfxBase64 = $env:WINDOWS_SIGNING_PFX
$pfxPassword = $env:WINDOWS_SIGNING_PASSWORD

if ([string]::IsNullOrWhiteSpace($pfxBase64)) {
    Write-Error "WINDOWS_SIGNING_PFX environment variable is not set. Refusing to sign without a certificate."
    exit 1
}

if ([string]::IsNullOrWhiteSpace($pfxPassword)) {
    Write-Error "WINDOWS_SIGNING_PASSWORD environment variable is not set. Refusing to sign without a password."
    exit 1
}

$signtoolCandidates = @(
    "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\signtool.exe"
    "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\arm64\signtool.exe"
    "${env:ProgramFiles}\Windows Kits\10\bin\*\x64\signtool.exe"
    "${env:ProgramFiles}\Windows Kits\10\bin\*\x86\signtool.exe"
    "${env:ProgramFiles(x86)}\Windows Kits\*\bin\*\x64\signtool.exe"
)

$signtoolPath = $null
foreach ($pattern in $signtoolCandidates) {
    $found = Get-ChildItem -Path $pattern -ErrorAction SilentlyContinue
    if ($found) {
        $signtoolPath = $found | Sort-Object VersionInfo -Descending | Select-Object -First 1 -ExpandProperty FullName
        break
    }
}

if (-not $signtoolPath) {
    Write-Error "signtool.exe was not found under the Windows SDK. Install the Windows SDK or set up the SDK bin directory on PATH, then retry."
    exit 1
}

$tempPfxName = [System.IO.Path]::ChangeExtension([System.IO.Path]::GetRandomFileName(), ".pfx")
$tempPfx = Join-Path ([System.IO.Path]::GetTempPath()) $tempPfxName

try {
    [System.IO.File]::WriteAllBytes($tempPfx, [Convert]::FromBase64String($pfxBase64))

    foreach ($file in $Path) {
        & $signtoolPath sign /f $tempPfx /p $pfxPassword /fd sha256 /tr http://timestamp.digicert.com /td sha256 $file
        if ($LASTEXITCODE -ne 0) {
            throw "signtool sign failed for $file with exit code $LASTEXITCODE"
        }
    }
}
finally {
    if (Test-Path $tempPfx) {
        Remove-Item -Path $tempPfx -Force
    }
}