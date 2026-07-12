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

function Assert-PublishedAlarmSounds {
    param(
        [string]$SourceRoot,
        [string]$PublishRoot,
        [string[]]$RequiredNames
    )

    foreach ($name in $RequiredNames) {
        $sourcePath = Join-Path $SourceRoot "ClubTimerXbox\Assets\AlarmSounds\$name"
        $publishedPath = Join-Path $PublishRoot "Assets\AlarmSounds\$name"

        if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) {
            throw "Required alarm sound is missing from source: $name"
        }

        if (-not (Test-Path -LiteralPath $publishedPath -PathType Leaf)) {
            throw "Required alarm sound is missing from publish output: $name"
        }

        $sourceHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $sourcePath).Hash
        $publishedHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $publishedPath).Hash
        if ($sourceHash -ne $publishedHash) {
            throw "Published alarm sound does not match source: $name"
        }
    }
}

function Assert-ArchivedAlarmSounds {
    param(
        [string]$ZipPath,
        [string]$SourceRoot,
        [string[]]$RequiredNames
    )

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [System.IO.Compression.ZipFile]::OpenRead($ZipPath)

    try {
        foreach ($name in $RequiredNames) {
            $entryName = "Assets/AlarmSounds/$name"
            $entry = $archive.Entries |
                Where-Object { $_.FullName.Replace('\', '/') -eq $entryName } |
                Select-Object -First 1

            if ($null -eq $entry) {
                throw "Required alarm sound is missing from release ZIP: $name"
            }

            $sourcePath = Join-Path $SourceRoot "ClubTimerXbox\Assets\AlarmSounds\$name"
            if ($entry.Length -ne (Get-Item -LiteralPath $sourcePath).Length) {
                throw "Alarm sound has an unexpected size in release ZIP: $name"
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

Assert-PublishedAlarmSounds `
    -SourceRoot $root `
    -PublishRoot $mainPublish `
    -RequiredNames $requiredAlarmSounds

$zipPath = Join-Path $releaseRoot "ClubTimerXbox-$Version.zip"
if (Test-Path $zipPath) {
    Remove-Item $zipPath -Force
}

Compress-Archive -Path (Join-Path $mainPublish "*") -DestinationPath $zipPath -Force

Assert-ArchivedAlarmSounds `
    -ZipPath $zipPath `
    -SourceRoot $root `
    -RequiredNames $requiredAlarmSounds

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
