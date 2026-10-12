<#
.SYNOPSIS
    Writes a markdown summary of an E2E run from the results file dotnet test leaves behind.

.DESCRIPTION
    Reads a .trx file and writes the result, the counts, how long the run took and the names
    of the failed tests. The E2E workflows put the summary on the run and in a comment on the
    pull request.

    When the run did not pass and -Author is given, the summary opens by mentioning that
    person, so GitHub notifies them.

    A run that died before it wrote a results file is reported as such, not as a pass.

    When run by GitHub Actions, also sets the step output "result" to passed, failed or none.

.EXAMPLE
    ./scripts/e2e-summary.ps1 -ResultsFile TestResults/e2e.trx -OutFile summary.md
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ResultsFile,
    [Parameter(Mandatory)][string]$OutFile,
    # What to call the run in the heading.
    [string]$Name = 'E2E tests',
    # The GitHub login of whoever opened the pull request. Mentioned when the run did not pass.
    [string]$Author,
    # What was tested and where, for the line under the counts. All optional.
    [string]$Commit,
    [string]$AppUrl,
    [string]$RunUrl,
    # The most failed tests to name. The rest are counted.
    [int]$MaxFailedNames = 50
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Format-Duration([TimeSpan]$span) {
    if ($span.TotalMinutes -ge 1) { return '{0} min {1} s' -f [int][Math]::Floor($span.TotalMinutes), $span.Seconds }
    return '{0} s' -f [int][Math]::Round($span.TotalSeconds)
}

$lines = [System.Collections.Generic.List[string]]::new()
$result = 'none'

if (Test-Path -LiteralPath $ResultsFile) {
    [xml]$trx = Get-Content -LiteralPath $ResultsFile -Raw
    $counters = $trx.TestRun.ResultSummary.Counters
    $passed = [int]$counters.passed
    $failed = [int]$counters.failed + [int]$counters.error + [int]$counters.timeout + [int]$counters.aborted
    $skipped = [int]$counters.total - [int]$counters.executed
    $took = [DateTimeOffset]$trx.TestRun.Times.finish - [DateTimeOffset]$trx.TestRun.Times.start

    # A run that found no tests has not shown anything works.
    $result = if ($failed -gt 0 -or $passed -eq 0) { 'failed' } else { 'passed' }

    $lines.Add("### $Name $result")
    $lines.Add('')
    $lines.Add("**$passed passed, $failed failed, $skipped skipped** in $(Format-Duration $took)")

    $failedNames = @($trx.TestRun.Results.UnitTestResult |
        Where-Object { $_.outcome -notin 'Passed', 'NotExecuted' } |
        ForEach-Object { $_.testName -replace '^DfE\.CheckPerformanceData\.E2ETests\.', '' } |
        Sort-Object -Unique)
}
else {
    $lines.Add("### $Name produced no results")
    $lines.Add('')
    $lines.Add('The run stopped before it wrote a results file: it was cancelled, or it failed before or during the tests. This is not a pass.')
    $failedNames = @()
}

# A login ending in [bot] is an app, such as the one that raises dependency updates.
if ($result -ne 'passed' -and $Author -and $Author -notmatch '\[bot\]$') {
    $lines.Insert(2, '')
    $lines.Insert(2, "@$Author this is your pull request: please look at what failed and fix it, or say here why it is not the change.")
}

$about = @()
if ($Commit) { $about += 'Commit `{0}`' -f $Commit.Substring(0, [Math]::Min(7, $Commit.Length)) }
if ($AppUrl) { $about += "against $AppUrl" }
if ($RunUrl) { $about += "([run]($RunUrl))" }
if ($about) {
    $lines.Add('')
    $lines.Add($about -join ' ')
}

if ($failedNames.Count -gt 0) {
    $lines.Add('')
    $lines.Add("<details><summary>Failed tests ($($failedNames.Count))</summary>")
    $lines.Add('')
    foreach ($name in $failedNames | Select-Object -First $MaxFailedNames) {
        # A theory's name carries its arguments, which can be long and can hold a backtick.
        $short = if ($name.Length -gt 200) { $name.Substring(0, 200) + '…' } else { $name }
        $lines.Add('- `{0}`' -f $short.Replace('`', "'"))
    }
    if ($failedNames.Count -gt $MaxFailedNames) {
        $lines.Add("- and $($failedNames.Count - $MaxFailedNames) more")
    }
    $lines.Add('')
    $lines.Add('</details>')
}

Set-Content -LiteralPath $OutFile -Value $lines -Encoding utf8

if ($env:GITHUB_OUTPUT) { Add-Content -LiteralPath $env:GITHUB_OUTPUT -Value "result=$result" }
