[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[A-Za-z0-9][A-Za-z0-9._-]{1,127}$')]
    [string]$ExistingPackId,

    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[A-Za-z0-9][A-Za-z0-9_-]{0,39}

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# ACHTUNG: ExistingPackId MUSS der PackId des bereits installierten
# 0.1.0-test.3-Clients sein. Sonst wird kein In-Place-Update moeglich.
# Auch ExistingChannel muss der bisherige Release-Kanal sein.
# Ein altes releases.test.json ist NICHT releases.win.json.
# Dieses Skript stellt nichts online und veraendert keine Installation.

$Version = '0.1.1'
$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$Project = Join-Path $RepoRoot 'Telenec.Mail.Statistics.Admin\Telenec.Mail.Statistics.Admin.csproj'
$PublishDir = Join-Path $RepoRoot 'publish\statistics-admin\win-x64'
$ReleaseDir = Join-Path $RepoRoot "publish\statistics-admin-releases\$Version"
$MainExe = 'Telenec.Mail.Statistics.Admin.exe'
$FeedName = "releases.$ExistingChannel.json"

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'dotnet wurde nicht gefunden.'
}
if (-not (Get-Command vpk -ErrorAction SilentlyContinue)) {
    throw 'Velopack CLI vpk wurde nicht gefunden.'
}
if (Test-Path -LiteralPath $ReleaseDir) {
    throw "Release-Verzeichnis existiert bereits, kein Ueberschreiben: $ReleaseDir"
}

[xml]$ProjectXml = Get-Content -LiteralPath $Project -Raw
$ProjectVersion = [string]$ProjectXml.Project.PropertyGroup.Version
if ($ProjectVersion -ne $Version) {
    throw "Clientprojekt hat Version $ProjectVersion, erwartet $Version."
}

Write-Host ''
Write-Host 'Telenec Mail Statistics - Admin-Client-Release'
Write-Host "Version: $Version"
Write-Host "Bestehende Velopack-Paketkennung: $ExistingPackId"
Write-Host "Bisheriger Velopack-Kanal: $ExistingChannel"
Write-Host ''
Write-Host 'Erstelle Windows x64 Release-Publish ...'

& dotnet publish $Project -c Release -r win-x64 --self-contained false -o $PublishDir
if ($LASTEXITCODE -ne 0) {
    throw 'dotnet publish fehlgeschlagen.'
}

if (-not (Test-Path -LiteralPath (Join-Path $PublishDir $MainExe) -PathType Leaf)) {
    throw "Hauptprogramm fehlt: $MainExe"
}

New-Item -ItemType Directory -Path $ReleaseDir -Force | Out-Null

Write-Host 'Erstelle Velopack-Paket und Setup ...'
& vpk pack --packId $ExistingPackId --packVersion $Version --packDir $PublishDir --mainExe $MainExe --runtime win-x64 --channel $ExistingChannel --outputDir $ReleaseDir --noPortable
if ($LASTEXITCODE -ne 0) {
    throw 'vpk pack fehlgeschlagen.'
}

$FeedFile = Join-Path $ReleaseDir $FeedName
if (-not (Test-Path -LiteralPath $FeedFile -PathType Leaf)) {
    throw "Velopack Feed fehlt: $FeedName"
}

$Feed = Get-Content -LiteralPath $FeedFile -Raw | ConvertFrom-Json
if (-not $Feed.Assets -or $Feed.Assets.Count -eq 0) {
    throw "Velopack Feed hat keine Releases."
}

$FullPackages = @(Get-ChildItem -LiteralPath $ReleaseDir -Filter '*-full.nupkg' -File)
$Setups = @(Get-ChildItem -LiteralPath $ReleaseDir -Filter '*-Setup.exe' -File)
if ($FullPackages.Count -lt 1 -or $Setups.Count -lt 1) {
    throw 'Setup oder Full-Updatepaket fehlt. Release nicht veroeffentlichen.'
}

$Ids = @($Feed.Assets | ForEach-Object { $_.PackageId } | Select-Object -Unique)
if ($Ids.Count -ne 1 -or [string]$Ids[0] -ne $ExistingPackId) {
    throw 'PackageId im Release-Feed stimmt nicht mit dem bestehenden Client ueberein.'
}

Write-Host ''
Write-Host '====================================='
Write-Host 'ADMIN-RELEASE VORBEREITET'
Write-Host '====================================='
Write-Host "Version: $Version"
Write-Host "PackId: $ExistingPackId"
Write-Host "Channel: $ExistingChannel"
Write-Host "Ordner: $ReleaseDir"
Write-Host ''
Get-ChildItem -LiteralPath $ReleaseDir -File |
    Select-Object Name, Length |
    Format-Table -AutoSize
Write-Host ''
Write-Host 'WICHTIG: Noch nichts veroeffentlicht!'
Write-Host 'Erst beide Updatequellen und Paket-Identitaet pruefen.'
)]
    [string]$ExistingChannel
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# ACHTUNG: ExistingPackId MUSS der PackId des bereits installierten
# 0.1.0-test.3-Clients sein. Sonst wird kein In-Place-Update moeglich.
# Die alte Release-Datei releases.win.json enthaelt diesen PackageId.
# Dieses Skript stellt nichts online und veraendert keine Installation.

$Version = '0.1.1'
$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$Project = Join-Path $RepoRoot 'Telenec.Mail.Statistics.Admin\Telenec.Mail.Statistics.Admin.csproj'
$PublishDir = Join-Path $RepoRoot 'publish\statistics-admin\win-x64'
$ReleaseDir = Join-Path $RepoRoot "publish\statistics-admin-releases\$Version"
$MainExe = 'Telenec.Mail.Statistics.Admin.exe'
$FeedName = 'releases.win.json'

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'dotnet wurde nicht gefunden.'
}
if (-not (Get-Command vpk -ErrorAction SilentlyContinue)) {
    throw 'Velopack CLI vpk wurde nicht gefunden.'
}
if (Test-Path -LiteralPath $ReleaseDir) {
    throw "Release-Verzeichnis existiert bereits, kein Ueberschreiben: $ReleaseDir"
}

[xml]$ProjectXml = Get-Content -LiteralPath $Project -Raw
$ProjectVersion = [string]$ProjectXml.Project.PropertyGroup.Version
if ($ProjectVersion -ne $Version) {
    throw "Clientprojekt hat Version $ProjectVersion, erwartet $Version."
}

Write-Host ''
Write-Host 'Telenec Mail Statistics - Admin-Client-Release'
Write-Host "Version: $Version"
Write-Host "Bestehende Velopack-Paketkennung: $ExistingPackId"
Write-Host ''
Write-Host 'Erstelle Windows x64 Release-Publish ...'

& dotnet publish $Project -c Release -r win-x64 --self-contained false -o $PublishDir
if ($LASTEXITCODE -ne 0) {
    throw 'dotnet publish fehlgeschlagen.'
}

if (-not (Test-Path -LiteralPath (Join-Path $PublishDir $MainExe) -PathType Leaf)) {
    throw "Hauptprogramm fehlt: $MainExe"
}

New-Item -ItemType Directory -Path $ReleaseDir -Force | Out-Null

Write-Host 'Erstelle Velopack-Paket und Setup ...'
& vpk pack --packId $ExistingPackId --packVersion $Version --packDir $PublishDir --mainExe $MainExe --runtime win-x64 --channel win --outputDir $ReleaseDir --noPortable
if ($LASTEXITCODE -ne 0) {
    throw 'vpk pack fehlgeschlagen.'
}

$FeedFile = Join-Path $ReleaseDir $FeedName
if (-not (Test-Path -LiteralPath $FeedFile -PathType Leaf)) {
    throw "Velopack Feed fehlt: $FeedName"
}

$Feed = Get-Content -LiteralPath $FeedFile -Raw | ConvertFrom-Json
if (-not $Feed.Assets -or $Feed.Assets.Count -eq 0) {
    throw "Velopack Feed hat keine Releases."
}

$FullPackages = @(Get-ChildItem -LiteralPath $ReleaseDir -Filter '*-full.nupkg' -File)
$Setups = @(Get-ChildItem -LiteralPath $ReleaseDir -Filter '*-Setup.exe' -File)
if ($FullPackages.Count -lt 1 -or $Setups.Count -lt 1) {
    throw 'Setup oder Full-Updatepaket fehlt. Release nicht veroeffentlichen.'
}

$Ids = @($Feed.Assets | ForEach-Object { $_.PackageId } | Select-Object -Unique)
if ($Ids.Count -ne 1 -or [string]$Ids[0] -ne $ExistingPackId) {
    throw 'PackageId im Release-Feed stimmt nicht mit dem bestehenden Client ueberein.'
}

Write-Host ''
Write-Host '====================================='
Write-Host 'ADMIN-RELEASE VORBEREITET'
Write-Host '====================================='
Write-Host "Version: $Version"
Write-Host "PackId: $ExistingPackId"
Write-Host "Ordner: $ReleaseDir"
Write-Host ''
Get-ChildItem -LiteralPath $ReleaseDir -File |
    Select-Object Name, Length |
    Format-Table -AutoSize
Write-Host ''
Write-Host 'WICHTIG: Noch nichts veroeffentlicht!'
Write-Host 'Erst beide Updatequellen und Paket-Identitaet pruefen.'
