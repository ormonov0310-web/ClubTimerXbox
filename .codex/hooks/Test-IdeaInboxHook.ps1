$ErrorActionPreference = 'Stop'
$scriptPath = Join-Path $PSScriptRoot 'idea-inbox.ps1'
$tempRoot = Join-Path ([IO.Path]::GetTempPath()) ('ClubTimer-Inbox-Test-' + [Guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $tempRoot
$sourcePath = Join-Path $tempRoot 'source.json'
$statePath = Join-Path $tempRoot 'review-state.json'
$pwsh = (Get-Process -Id $PID).Path
$script:passed = 0

function Write-Fixture($Path, $Value) {
    [IO.File]::WriteAllText($Path, ($Value | ConvertTo-Json -Depth 10), [Text.UTF8Encoding]::new($false))
}

function Test-Reminder([string]$Name, [string]$PromptText, [bool]$Expected,
    [string]$At = '2026-09-06T10:00:00+05:00', [string]$EventName = 'UserPromptSubmit') {
    $before = if (Test-Path -LiteralPath $statePath) { (Get-FileHash -LiteralPath $statePath).Hash } else { '' }
    $payload = @{ hook_event_name = $EventName; prompt = $PromptText; session_id = 'test'; turn_id = 'test' } | ConvertTo-Json -Compress
    $result = $payload | & $pwsh -NoProfile -NonInteractive -File $scriptPath -InboxDirectory $tempRoot -Now $At
    if ($LASTEXITCODE -ne 0) { throw "$Name returned nonzero exit" }
    $actual = -not [string]::IsNullOrWhiteSpace(($result -join ''))
    if ($actual -ne $Expected) { throw "$Name expected reminder=$Expected, actual=$actual; output=$result" }
    if ($actual) {
        $json = $result | ConvertFrom-Json
        if ($json.hookSpecificOutput.hookEventName -ne 'UserPromptSubmit') { throw "$Name wrong event" }
        if ($json.hookSpecificOutput.additionalContext -notmatch 'has not read the remote chat') { throw "$Name missing honesty boundary" }
        if ($null -ne $json.decision -or $json.continue -eq $false) { throw "$Name must not block work" }
    }
    $after = if (Test-Path -LiteralPath $statePath) { (Get-FileHash -LiteralPath $statePath).Hash } else { '' }
    if ($before -ne $after) { throw "$Name modified the checkpoint" }
    $script:passed++
    Write-Output "PASS: $Name"
}

try {
    Test-Reminder 'No private source configured' 'change the button' $false
    $source = @{ enabled = $true; threadId = '11111111-1111-1111-1111-111111111111'; timeZoneId = 'Asia/Almaty' }
    Write-Fixture $sourcePath $source
    Test-Reminder 'First request without state' 'change the button' $true
    $state = @{ threadId = $source.threadId; lastReadSucceeded = $true; lastSuccessfulReviewAt = '2026-09-06T08:00:00+05:00' }
    Write-Fixture $statePath $state
    Test-Reminder 'Ordinary same-day followup stays quiet' 'change the button' $false
    Test-Reminder 'Russian greeting refreshes notes' 'привет, начнем работу' $true
    Test-Reminder 'Work restart refreshes notes' 'продолжим работу' $true
    Test-Reminder 'Explicit notebook request' 'посмотри чат с заметками' $true
    Test-Reminder 'Russian arbitrary work request next day' 'нужно сделать вот что' $true '2026-09-07T10:00:00+05:00'
    $zone = [TimeZoneInfo]::FindSystemTimeZoneById($source.timeZoneId)
    $beforeMidnight = [DateTime]::new(2026, 9, 6, 23, 59, 0)
    $afterMidnight = $beforeMidnight.AddMinutes(2)
    $beforeUtc = [DateTimeOffset]::new($beforeMidnight, $zone.GetUtcOffset($beforeMidnight)).ToUniversalTime().ToString('o')
    $afterUtc = [DateTimeOffset]::new($afterMidnight, $zone.GetUtcOffset($afterMidnight)).ToUniversalTime().ToString('o')
    Test-Reminder 'Timezone: still same day' 'ordinary followup' $false $beforeUtc
    Test-Reminder 'Timezone: next local day' 'ordinary followup' $true $afterUtc
    Test-Reminder 'Other lifecycle event' 'hello' $false '2026-09-06T10:00:00+05:00' 'Stop'
    Test-Reminder 'Empty prompt' '' $false
    $state.lastReadSucceeded = $false
    Write-Fixture $statePath $state
    Test-Reminder 'Failed remote read must retry' 'ordinary followup' $true
    $state.lastReadSucceeded = $true
    $state.threadId = '22222222-2222-2222-2222-222222222222'
    Write-Fixture $statePath $state
    Test-Reminder 'Different notebook invalidates checkpoint' 'ordinary followup' $true
    [IO.File]::WriteAllText($statePath, '{broken')
    Test-Reminder 'Corrupt state fails open with reminder' 'ordinary followup' $true
    $source.enabled = $false
    Write-Fixture $sourcePath $source
    Test-Reminder 'Disabled notebook stays quiet' 'hello' $false
    $malformed = '{broken' | & $pwsh -NoProfile -NonInteractive -File $scriptPath -InboxDirectory $tempRoot
    if ($LASTEXITCODE -ne 0 -or ($malformed | ConvertFrom-Json).decision -eq 'block') { throw 'Malformed input blocks work' }
    $script:passed++
    Write-Output "PASS: Malformed input does not block work"
    Write-Output "RESULT: $script:passed tests passed"
} finally {
    $resolved = [IO.Path]::GetFullPath($tempRoot)
    $base = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
    if ($resolved.StartsWith($base, [StringComparison]::OrdinalIgnoreCase) -and
        [IO.Path]::GetFileName($resolved) -like 'ClubTimer-Inbox-Test-*') {
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
}
