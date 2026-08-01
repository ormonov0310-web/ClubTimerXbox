$ErrorActionPreference = "Stop"

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path `
    $repositoryRoot `
    "ClubTimerXbox.RegressionTests\ClubTimerXbox.RegressionTests.csproj"

dotnet run `
    --project $projectPath `
    -c Debug `
    -p:SkipCopyUpdater=true

if ($LASTEXITCODE -ne 0) {
    throw "Business accounting tests failed with exit code $LASTEXITCODE."
}
