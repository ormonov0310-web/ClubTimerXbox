param(
    [string]$Version = "1.0.0",
    [string]$Channel = "stable",
    [string]$Notes = "",
    [string]$DownloadUrl = ""
)

$ErrorActionPreference = "Stop"

$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$releaseRoot = Join-Path $root "release"
$workRoot = Join-Path $releaseRoot "_work"
$mainPublish = Join-Path $workRoot "ClubTimerXbox"
$updaterPublish = Join-Path $workRoot "ClubTimerUpdater"

if (Test-Path $workRoot) {
    Remove-Item $workRoot -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $mainPublish | Out-Null
New-Item -ItemType Directory -Force -Path $updaterPublish | Out-Null
New-Item -ItemType Directory -Force -Path $releaseRoot | Out-Null

dotnet publish (Join-Path $root "ClubTimerXbox\ClubTimerXbox.csproj") `
    -c Release `
    -o $mainPublish `
    --self-contained false

dotnet publish (Join-Path $root "ClubTimerUpdater\ClubTimerUpdater.csproj") `
    -c Release `
    -o $updaterPublish `
    --self-contained false

Copy-Item (Join-Path $updaterPublish "ClubTimerUpdater.*") $mainPublish -Force

$zipPath = Join-Path $releaseRoot "ClubTimerXbox-$Version.zip"
if (Test-Path $zipPath) {
    Remove-Item $zipPath -Force
}

Compress-Archive -Path (Join-Path $mainPublish "*") -DestinationPath $zipPath -Force

$hash = (Get-FileHash -Algorithm SHA256 -Path $zipPath).Hash.ToLowerInvariant()
$manifestPath = Join-Path $releaseRoot "firebase-update-manifest-$Channel.json"

$manifest = [ordered]@{
    latestVersion = $Version
    version = $Version
    channel = $Channel
    url = $DownloadUrl
    downloadUrl = $DownloadUrl
    sha256 = $hash
    notes = $Notes
    publishedAt = (Get-Date).ToString("yyyy-MM-dd HH:mm:ss")
}

$manifest | ConvertTo-Json -Depth 4 | Set-Content -Path $manifestPath -Encoding UTF8

Write-Host "Release package:"
Write-Host $zipPath
Write-Host ""
Write-Host "Firebase manifest:"
Write-Host $manifestPath
Write-Host ""
Write-Host "SHA256:"
Write-Host $hash

Remove-Item $workRoot -Recurse -Force
