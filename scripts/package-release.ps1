param(
    [string]$Version = "1.0.0",
    [string]$Channel = "stable",
    [string]$Notes = "",
    [string]$DownloadUrl = "",
    [string]$OutputRoot = "",
    [switch]$SelfContained,
    [string]$RuntimeIdentifier = "win-x64"
)

$ErrorActionPreference = "Stop"

function Invoke-NativeCommand {
    param(
        [string]$FilePath,
        [string[]]$Arguments
    )

    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$FilePath failed with exit code $LASTEXITCODE."
    }
}

$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$releaseRoot = if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    Join-Path $root "release"
} else {
    $OutputRoot
}
$workRoot = Join-Path $releaseRoot "_work"
$mainPublish = Join-Path $workRoot "ClubTimerXbox"
$updaterPublish = Join-Path $workRoot "ClubTimerUpdater"
$assemblyVersion = if ($Version -match '^\d+\.\d+\.\d+$') {
    "$Version.0"
} elseif ($Version -match '^\d+\.\d+\.\d+\.\d+$') {
    $Version
} else {
    "1.0.0.0"
}

if (Test-Path $workRoot) {
    Remove-Item $workRoot -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $mainPublish | Out-Null
New-Item -ItemType Directory -Force -Path $updaterPublish | Out-Null
New-Item -ItemType Directory -Force -Path $releaseRoot | Out-Null

$publishProperties = @(
    "-p:Version=$Version",
    "-p:AssemblyVersion=$assemblyVersion",
    "-p:FileVersion=$assemblyVersion",
    "-p:InformationalVersion=$Version"
)

$runtimeArgs = if ($SelfContained) {
    @("-r", $RuntimeIdentifier, "--self-contained", "true")
} else {
    @("--self-contained", "false")
}

$mainPublishArgs = @(
    "publish",
    (Join-Path $root "ClubTimerXbox\ClubTimerXbox.csproj"),
    "-c",
    "Release",
    "-o",
    $mainPublish,
    "-p:SkipCopyUpdater=true"
) + $publishProperties + $runtimeArgs

$updaterPublishArgs = @(
    "publish",
    (Join-Path $root "ClubTimerUpdater\ClubTimerUpdater.csproj"),
    "-c",
    "Release",
    "-o",
    $updaterPublish
) + $publishProperties + $runtimeArgs

Invoke-NativeCommand "dotnet" $mainPublishArgs
Invoke-NativeCommand "dotnet" $updaterPublishArgs

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
