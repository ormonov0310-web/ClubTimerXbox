param([string]$EventJson)

$ErrorActionPreference = 'Stop'

$raw = if ([string]::IsNullOrWhiteSpace($EventJson)) {
    [Console]::In.ReadToEnd()
}
else {
    $EventJson
}
try {
    $event = $raw | ConvertFrom-Json -Depth 100
    $serializedEvent = $event | ConvertTo-Json -Depth 100 -Compress
}
catch {
    exit 0
}

$normalized = (($serializedEvent -replace '\\', '/') -replace '/+', '/').ToLowerInvariant()
$isAndroidProject = $normalized.Contains('android-owner-app')
$isRelevantChange =
    $normalized.Contains('app/src/') -or
    $normalized.Contains('app/build.gradle') -or
    $normalized.Contains('build.gradle') -or
    $normalized.Contains('settings.gradle') -or
    $normalized.Contains('gradle.properties') -or
    $normalized.Contains('gradle/libs.versions.toml') -or
    $normalized.Contains('gradlew')

if (-not ($isAndroidProject -and $isRelevantChange)) {
    exit 0
}

$stateRoot = Join-Path $env:TEMP 'ClubTimerXbox-CodexHooks'
New-Item -ItemType Directory -Force -Path $stateRoot | Out-Null
$key = (('{0}-{1}' -f $event.session_id, $event.turn_id) -replace '[^a-zA-Z0-9_-]', '_')
$marker = Join-Path $stateRoot ($key + '.android.changed')
Set-Content -LiteralPath $marker -Value ([DateTimeOffset]::Now.ToString('O')) -Encoding utf8
