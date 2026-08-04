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

$requiredAlarmSounds = @(
    "calm-sound-of-the-appearance-in-the-system.mp3",
    "message-notification.mp3",
    "music-reminder-sound.mp3",
    "new_message-di-ding.mp3",
    "nice-melodic-sound.mp3",
    "short-calm-pleasant-notification-sound.mp3",
    "whatsapp-for-web.mp3",
    "whatsapp-short-ringtone.mp3"
)

$requiredUiSounds = @(
    "action.mp3",
    "click.mp3",
    "hover.mp3"
)

function Assert-PublishedSounds {
    param(
        [string]$SourceRoot,
        [string]$PublishRoot,
        [string]$RelativeDirectory,
        [string[]]$RequiredNames
    )

    foreach ($name in $RequiredNames) {
        $sourcePath = Join-Path $SourceRoot "ClubTimerXbox\Assets\$RelativeDirectory\$name"
        $publishedPath = Join-Path $PublishRoot "Assets\$RelativeDirectory\$name"

        if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) {
            throw "Required sound is missing from source: Assets/$RelativeDirectory/$name"
        }

        if (-not (Test-Path -LiteralPath $publishedPath -PathType Leaf)) {
            throw "Required sound is missing from publish output: Assets/$RelativeDirectory/$name"
        }

        $sourceHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $sourcePath).Hash
        $publishedHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $publishedPath).Hash
        if ($sourceHash -ne $publishedHash) {
            throw "Published sound does not match source: Assets/$RelativeDirectory/$name"
        }
    }
}

function Assert-ArchivedSounds {
    param(
        [string]$ZipPath,
        [string]$SourceRoot,
        [string]$RelativeDirectory,
        [string[]]$RequiredNames
    )

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [System.IO.Compression.ZipFile]::OpenRead($ZipPath)

    try {
        foreach ($name in $RequiredNames) {
            $entryName = "Assets/$RelativeDirectory/$name"
            $entry = $archive.Entries |
                Where-Object { $_.FullName.Replace('\', '/') -eq $entryName } |
                Select-Object -First 1

            if ($null -eq $entry) {
                throw "Required sound is missing from release ZIP: $entryName"
            }

            $sourcePath = Join-Path $SourceRoot "ClubTimerXbox\Assets\$RelativeDirectory\$name"
            if ($entry.Length -ne (Get-Item -LiteralPath $sourcePath).Length) {
                throw "Sound has an unexpected size in release ZIP: $entryName"
            }
        }
    }
    finally {
        $archive.Dispose()
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

Get-ChildItem -LiteralPath $updaterPublish -Filter "ClubTimerUpdater.*" -File |
    Copy-Item -Destination $mainPublish -Force

$windowsBasePath = Join-Path $mainPublish "WindowsBase.dll"
if (-not (Test-Path -LiteralPath $windowsBasePath -PathType Leaf)) {
    throw "WindowsBase.dll is missing. Release packages must be built with -SelfContained."
}

Assert-PublishedSounds `
    -SourceRoot $root `
    -PublishRoot $mainPublish `
    -RelativeDirectory "AlarmSounds" `
    -RequiredNames $requiredAlarmSounds

Assert-PublishedSounds `
    -SourceRoot $root `
    -PublishRoot $mainPublish `
    -RelativeDirectory "UiSounds" `
    -RequiredNames $requiredUiSounds

$zipPath = Join-Path $releaseRoot "ClubTimerXbox-$Version.zip"
if (Test-Path $zipPath) {
    Remove-Item $zipPath -Force
}

Compress-Archive -Path (Join-Path $mainPublish "*") -DestinationPath $zipPath -Force

Assert-ArchivedSounds `
    -ZipPath $zipPath `
    -SourceRoot $root `
    -RelativeDirectory "AlarmSounds" `
    -RequiredNames $requiredAlarmSounds

Assert-ArchivedSounds `
    -ZipPath $zipPath `
    -SourceRoot $root `
    -RelativeDirectory "UiSounds" `
    -RequiredNames $requiredUiSounds

$hash = (Get-FileHash -Algorithm SHA256 -Path $zipPath).Hash.ToLowerInvariant()
$manifestPath = Join-Path $releaseRoot "firebase-update-manifest-$Channel.json"

$manifest = [ordered]@{
    latestVersion = $Version
    version = $Version
    channel = $Channel
    url = $DownloadUrl
    downloadUrl = $DownloadUrl
    sha256 = $hash
    sizeBytes = (Get-Item -LiteralPath $zipPath).Length
    notes = $Notes
    publishedAt = (Get-Date).ToString("yyyy-MM-dd HH:mm:ss")
}

$manifestJson = $manifest | ConvertTo-Json -Depth 4
[System.IO.File]::WriteAllText(
    $manifestPath,
    $manifestJson,
    [System.Text.UTF8Encoding]::new($false)
)

Write-Host "Release package:"
Write-Host $zipPath
Write-Host ""
Write-Host "Firebase manifest:"
Write-Host $manifestPath
Write-Host ""
Write-Host "SHA256:"
Write-Host $hash

Remove-Item $workRoot -Recurse -Force
