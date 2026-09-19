param([string]$EventJson)

$ErrorActionPreference = 'Stop'

function ConvertTo-TailText {
    param(
        [AllowNull()]
        [object]$Value,
        [int]$Limit = 2400
    )

    $text = ([string]$Value).Trim()
    if ($text.Length -le $Limit) {
        return $text
    }

    return $text.Substring($text.Length - $Limit)
}

function Write-HookResult {
    param([hashtable]$Value)

    $Value | ConvertTo-Json -Compress -Depth 8
    exit 0
}

$raw = if ([string]::IsNullOrWhiteSpace($EventJson)) {
    [Console]::In.ReadToEnd()
}
else {
    $EventJson
}
try {
    $event = $raw | ConvertFrom-Json -Depth 100
}
catch {
    Write-HookResult @{ continue = $true }
}

$stateRoot = Join-Path $env:TEMP 'ClubTimerXbox-CodexHooks'
$key = (('{0}-{1}' -f $event.session_id, $event.turn_id) -replace '[^a-zA-Z0-9_-]', '_')
$marker = Join-Path $stateRoot ($key + '.android.changed')
if (-not (Test-Path -LiteralPath $marker -PathType Leaf)) {
    Write-HookResult @{ continue = $true }
}

function Write-HookFailure {
    param([string]$Reason)

    $message = 'ClubTimerXbox Android validation failed. ' + (ConvertTo-TailText $Reason 3400)
    if ([bool]$event.stop_hook_active) {
        Write-HookResult @{ continue = $true; systemMessage = $message }
    }

    Write-HookResult @{ decision = 'block'; reason = $message }
}

$androidRoot = Join-Path $env:USERPROFILE 'Documents\Codex\2026-06-10\GGGsel\android-owner-app'
$gradle = Join-Path $androidRoot 'gradlew.bat'
$apk = Join-Path $androidRoot 'app\build\outputs\apk\debug\app-debug.apk'
$adb = Join-Path $env:LOCALAPPDATA 'Android\Sdk\platform-tools\adb.exe'

if (-not (Test-Path -LiteralPath $gradle -PathType Leaf)) {
    Write-HookFailure "Gradle wrapper not found: $gradle"
}

Push-Location $androidRoot
try {
    $gradleOutput = (& $gradle testDebugUnitTest lintDebug assembleDebug --console=plain 2>&1 | Out-String)
    $gradleExit = $LASTEXITCODE
}
finally {
    Pop-Location
}

if ($gradleExit -ne 0) {
    Write-HookFailure ('Gradle checks failed: ' + (ConvertTo-TailText $gradleOutput))
}

if (-not (Test-Path -LiteralPath $apk -PathType Leaf)) {
    Write-HookFailure "Debug APK was not produced: $apk"
}

if (-not (Test-Path -LiteralPath $adb -PathType Leaf)) {
    Write-HookFailure "ADB not found: $adb"
}

$deviceOutput = @(& $adb devices -l 2>&1)
if ($LASTEXITCODE -ne 0) {
    Write-HookFailure ('ADB device check failed: ' + (ConvertTo-TailText ($deviceOutput -join [Environment]::NewLine)))
}

$authorizedDevices = @()
$unavailableDevices = @()
foreach ($line in $deviceOutput) {
    $lineText = [string]$line
    if ($lineText -match '^(?<serial>\S+)\s+device(?:\s|$)') {
        $authorizedDevices += $Matches.serial
    }
    elseif ($lineText -match '^(?<serial>\S+)\s+(?<state>unauthorized|offline)(?:\s|$)') {
        $unavailableDevices += ($Matches.serial + ' (' + $Matches.state + ')')
    }
}

if ($authorizedDevices.Count -eq 0) {
    [System.IO.File]::Delete($marker)
    $detail = if ($unavailableDevices.Count -gt 0) {
        ' Unavailable devices: ' + ($unavailableDevices -join ', ') + '.'
    }
    else {
        ''
    }
    Write-HookResult @{
        continue = $true
        systemMessage = 'Android checks passed and the debug APK is ready, but no authorized phone was connected; report that it was not installed.' + $detail
    }
}

if ($authorizedDevices.Count -gt 1) {
    Write-HookFailure ('More than one authorized phone is connected; no APK was installed. Devices: ' + ($authorizedDevices -join ', '))
}

$serial = $authorizedDevices[0]
$installOutput = (& $adb -s $serial install -r $apk 2>&1 | Out-String)
if ($LASTEXITCODE -ne 0 -or $installOutput -notmatch '(?m)^Success\s*$') {
    Write-HookFailure ('ADB install failed for ' + $serial + ': ' + (ConvertTo-TailText $installOutput))
}

$packageOutput = (& $adb -s $serial shell dumpsys package com.clubtimer.owner 2>&1 | Out-String)
if ($LASTEXITCODE -ne 0) {
    Write-HookFailure ('Installed package verification failed for ' + $serial + ': ' + (ConvertTo-TailText $packageOutput))
}

$versionNameMatch = [regex]::Match($packageOutput, '(?m)^\s*versionName=(?<value>\S+)\s*$')
$versionCodeMatch = [regex]::Match($packageOutput, '(?m)^\s*versionCode=(?<value>\d+)')
if (-not $versionNameMatch.Success -or -not $versionCodeMatch.Success) {
    Write-HookFailure ('Package com.clubtimer.owner is missing version information after installation on ' + $serial + '.')
}

[System.IO.File]::Delete($marker)
Write-HookResult @{
    continue = $true
    systemMessage = ('Android checks passed and the debug APK was installed in place on {0}; versionName={1}, versionCode={2}. App data was preserved.' -f $serial, $versionNameMatch.Groups['value'].Value, $versionCodeMatch.Groups['value'].Value)
}
