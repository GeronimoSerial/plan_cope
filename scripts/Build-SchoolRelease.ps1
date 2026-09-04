[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $Cue,

    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d{4}$')]
    [string] $SchoolYear,

    [Parameter(Mandatory = $true)]
    [ValidatePattern('^https?://')]
    [string] $CentralUrl,

    [string] $AccessToken = $env:CENTRAL_ACCESS_TOKEN,

    [string] $DocumentHmacKey,

    [string] $Runtime = 'win-x64'
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$normalizedCue = -join ($Cue.ToCharArray() | Where-Object { $_ -ge '0' -and $_ -le '9' })
if ($normalizedCue.Length -ne 9) {
    throw 'El CUE debe contener exactamente 9 dígitos, incluidos los 2 del anexo.'
}

if ([string]::IsNullOrWhiteSpace($AccessToken)) {
    throw 'Falta AccessToken. Pasalo como parámetro o mediante CENTRAL_ACCESS_TOKEN.'
}

if ([string]::IsNullOrWhiteSpace($DocumentHmacKey)) {
    $keyBytes = [byte[]]::new(48)
    $random = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try {
        $random.GetBytes($keyBytes)
    } finally {
        $random.Dispose()
    }
    $DocumentHmacKey = [Convert]::ToBase64String($keyBytes)
}

if ([Text.Encoding]::UTF8.GetByteCount($DocumentHmacKey) -lt 32) {
    throw 'DocumentHmacKey debe contener al menos 32 bytes UTF-8.'
}

$centralBase = $CentralUrl.TrimEnd('/')
$headers = @{ Authorization = "Bearer $AccessToken" }
$requestBody = @{ cue = $normalizedCue; schoolYear = $SchoolYear } | ConvertTo-Json -Compress

Write-Host "Actualizando una vez el padrón GE de $normalizedCue/$SchoolYear..."
$refresh = Invoke-RestMethod `
    -Method Post `
    -Uri "$centralBase/api/rosters/refresh" `
    -Headers $headers `
    -ContentType 'application/json' `
    -Body $requestBody

if ($refresh.sectionCount -le 0 -or $refresh.studentCount -le 0 -or $refresh.status -ne 'Ready') {
    throw "Central no produjo un padrón utilizable: secciones=$($refresh.sectionCount), alumnos=$($refresh.studentCount), estado=$($refresh.status)."
}

$rosterDirectory = Join-Path $repoRoot 'artifacts/rosters'
$releaseDirectory = Join-Path $repoRoot "artifacts/local-release/$normalizedCue-$SchoolYear"
[IO.Directory]::CreateDirectory($rosterDirectory) | Out-Null
[IO.Directory]::CreateDirectory($releaseDirectory) | Out-Null
$rosterPath = Join-Path $rosterDirectory "$normalizedCue-$SchoolYear.roster.json"

Invoke-WebRequest `
    -Uri "$centralBase/api/sync/roster/$normalizedCue/$SchoolYear" `
    -Headers $headers `
    -OutFile $rosterPath

$package = Get-Content -Raw -LiteralPath $rosterPath | ConvertFrom-Json
if ($package.cue -ne $normalizedCue -or
    $package.schoolYear -ne $SchoolYear -or
    $package.status -ne 'Ready' -or
    $package.sectionCount -ne $package.sections.Count) {
    throw 'El paquete descargado no coincide con el CUE/ciclo solicitado o tiene cantidades inválidas.'
}

$studentCount = ($package.sections | ForEach-Object { @($_.students).Count } | Measure-Object -Sum).Sum
if ($studentCount -ne $package.studentCount -or $studentCount -le 0) {
    throw 'La cantidad de alumnos del paquete no coincide con sus secciones.'
}

Write-Host "Compilando el ejecutable con $($package.sectionCount) secciones y $studentCount alumnos embebidos..."
$hostProject = Join-Path $repoRoot 'src/Local/PlanCope.Local.Host/PlanCope.Local.Host.csproj'
& dotnet publish $hostProject `
    -c Release `
    -r $Runtime `
    --self-contained false `
    -o $releaseDirectory `
    "-p:RosterBundlePath=$rosterPath"
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish terminó con código $LASTEXITCODE."
}

$settingsPath = Join-Path $releaseDirectory 'appsettings.json'
$settings = if (Test-Path -LiteralPath $settingsPath) {
    Get-Content -Raw -LiteralPath $settingsPath | ConvertFrom-Json
} else {
    [pscustomobject]@{}
}
$settings | Add-Member -Force -MemberType NoteProperty -Name Nominalization -Value ([pscustomobject]@{
    DocumentHmacKey = $DocumentHmacKey
})
$settings | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $settingsPath -Encoding utf8

$executablePath = Join-Path $releaseDirectory 'PlanCope.Local.Host.exe'
if (-not (Test-Path -LiteralPath $executablePath)) {
    throw "No se generó el ejecutable esperado en $executablePath."
}

Write-Host ''
Write-Host 'Release generado correctamente:'
Write-Host "  Ejecutable: $executablePath"
Write-Host "  CUE:        $normalizedCue"
Write-Host "  Secciones:  $($package.sectionCount)"
Write-Host "  Alumnos:    $studentCount"
