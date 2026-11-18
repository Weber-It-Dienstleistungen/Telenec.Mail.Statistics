
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$')]
    [string]$Version
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Dieses Skript verpackt einen bereits in Visual Studio
# veroeffentlichten Linux-Build. Es baut nichts neu.

$RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path

$PublishDirectory = Join-Path `
    $RepositoryRoot `
    'Telenec.Mail.Statistics.Server\bin\Release\net10.0\publish\server-linux-x64'

$ServerDll = Join-Path `
    $PublishDirectory `
    'Telenec.Mail.Statistics.Server.dll'

$RuntimeConfig = Join-Path `
    $PublishDirectory `
    'Telenec.Mail.Statistics.Server.runtimeconfig.json'

$SigningKey = Join-Path `
    $env:APPDATA `
    'Telenec\ReleaseSigning\statistics-server-ed25519'

$ReleaseDirectory = Join-Path `
    $RepositoryRoot `
    "publish\statistics-server-releases\$Version"

$ArchiveName = "telenec-mail-statistics-server-linux-x64-$Version.zip"
$ArchivePath = Join-Path $ReleaseDirectory $ArchiveName

$ManifestPath = Join-Path $ReleaseDirectory 'release.json'
$SignaturePath = "$ManifestPath.sig"

$SignatureNamespace = 'telenec-statistics-release'

Write-Host ''
Write-Host 'Telenec Mail Statistics - Server-Release'
Write-Host "Version: $Version"
Write-Host ''

# Voraussetzungen kontrollieren.

if (-not (Test-Path -LiteralPath $ServerDll -PathType Leaf)) {
    throw 'Die veroeffentlichte Server-DLL wurde nicht gefunden. Bitte zuerst in Visual Studio veroeffentlichen.'
}

if (-not (Test-Path -LiteralPath $RuntimeConfig -PathType Leaf)) {
    throw 'Die Runtime-Konfiguration fehlt im Publish-Verzeichnis.'
}

if (-not (Test-Path -LiteralPath $SigningKey -PathType Leaf)) {
    throw "Der private Signierschluessel wurde nicht gefunden: $SigningKey"
}

if (-not (Test-Path -LiteralPath "$SigningKey.pub" -PathType Leaf)) {
    throw 'Der zugehoerige oeffentliche Signierschluessel fehlt.'
}

if (-not (Get-Command 'ssh-keygen' -ErrorAction SilentlyContinue)) {
    throw 'OpenSSH ssh-keygen wurde nicht gefunden.'
}

if (Test-Path -LiteralPath $ReleaseDirectory) {
    throw "Das Release-Verzeichnis existiert bereits: $ReleaseDirectory"
}

# Release-Verzeichnis erstellen.

New-Item `
    -ItemType Directory `
    -Path $ReleaseDirectory `
    -Force | Out-Null

# Linux-Publish als ZIP verpacken.

Write-Host 'Erstelle ZIP-Paket ...'

Compress-Archive `
    -Path (Join-Path $PublishDirectory '*') `
    -DestinationPath $ArchivePath `
    -CompressionLevel Optimal

if (-not (Test-Path -LiteralPath $ArchivePath -PathType Leaf)) {
    throw 'ZIP-Erstellung fehlgeschlagen.'
}

# SHA-256 und Dateigroesse ermitteln.

$ArchiveHash = (
    Get-FileHash `
        -LiteralPath $ArchivePath `
        -Algorithm SHA256
).Hash.ToLowerInvariant()

$ArchiveSize = (Get-Item -LiteralPath $ArchivePath).Length

Write-Host 'ZIP-Paket erstellt.'
Write-Host "SHA-256: $ArchiveHash"

# Signierbares Release-Manifest erstellen.

$Manifest = [ordered]@{
    schemaVersion      = 1
    product            = 'Telenec.Mail.Statistics.Server'
    version            = $Version
    runtimeIdentifier  = 'linux-x64'
    package            = $ArchiveName
    sha256             = $ArchiveHash
    sizeBytes          = $ArchiveSize
    createdAtUtc       = [DateTime]::UtcNow.ToString(
        'yyyy-MM-ddTHH:mm:ssZ',
        [System.Globalization.CultureInfo]::InvariantCulture
    )
}

$ManifestJson = ConvertTo-Json `
    -InputObject $Manifest `
    -Depth 5

$Utf8WithoutBom = New-Object System.Text.UTF8Encoding($false)

[System.IO.File]::WriteAllText(
    $ManifestPath,
    $ManifestJson + "`n",
    $Utf8WithoutBom
)

Write-Host 'Release-Manifest erstellt.'

# Manifest mit dem bestehenden Ed25519-Schluessel signieren.
# Der private Schluessel bleibt auf dem Windows-Rechner.

Write-Host ''
Write-Host 'Erstelle digitale Signatur ...'
Write-Host 'Falls erforderlich, jetzt das Passwort des Signierschluessels eingeben.'
Write-Host ''

& ssh-keygen `
    -Y sign `
    -f $SigningKey `
    -n $SignatureNamespace `
    $ManifestPath

if ($LASTEXITCODE -ne 0) {
    throw 'Die digitale Signierung ist fehlgeschlagen.'
}

if (-not (Test-Path -LiteralPath $SignaturePath -PathType Leaf)) {
    throw 'Die erwartete Signaturdatei wurde nicht erstellt.'
}

# Ergebnis kontrollieren.

$ReleaseFiles = @(
    $ArchivePath,
    $ManifestPath,
    $SignaturePath
)

foreach ($File in $ReleaseFiles) {
    if (-not (Test-Path -LiteralPath $File -PathType Leaf)) {
        throw "Release-Datei fehlt: $File"
    }
}

Write-Host ''
Write-Host '========================================'
Write-Host 'SERVER-RELEASE ERFOLGREICH VORBEREITET'
Write-Host '========================================'
Write-Host ''
Write-Host "Version: $Version"
Write-Host "Ordner:  $ReleaseDirectory"
Write-Host ''
Write-Host 'Dateien fuer den manuellen Plesk-Upload:'
Write-Host ''

foreach ($File in $ReleaseFiles) {
    Write-Host " - $(Split-Path $File -Leaf)"
}

Write-Host ''
Write-Host 'Es wurde nichts auf den Server hochgeladen.'
Write-Host 'Der Produktivserver wurde nicht veraendert.'
