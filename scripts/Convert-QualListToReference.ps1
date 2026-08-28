<#
.SYNOPSIS
Merges the supplier QualList.xlsx (AO / QAN / grades) and SyllabusCodes.xlsx (QUID -> syllabus
codes, 16-19 rows only) into Web/Data/QualificationReference/qualification-reference.json
(AB#297848). Re-run whenever either supplier export is refreshed.

.EXAMPLE
pwsh scripts/Convert-QualListToReference.ps1 `
    -QualList 'C:\Repos\Contracts\DfE\QualList.xlsx' `
    -SyllabusCodes 'C:\Repos\Contracts\DfE\SyllabusCodes.xlsx'
#>
param(
    [Parameter(Mandatory)] [string] $QualList,
    [Parameter(Mandatory)] [string] $SyllabusCodes,
    [string] $OutFile = "$PSScriptRoot/../src/DfE.CheckPerformanceData.Web/Data/QualificationReference/qualification-reference.json"
)
$ErrorActionPreference = 'Stop'

# An xlsx is a zip: shared strings + one sheet. No Excel/COM dependency, so this runs in CI too.
Add-Type -AssemblyName System.IO.Compression.FileSystem
function Read-Sheet([string] $Path) {
    $tmp = Join-Path ([IO.Path]::GetTempPath()) ("xlsx-" + [Guid]::NewGuid())
    [IO.Compression.ZipFile]::ExtractToDirectory($Path, $tmp)
    try {
        [xml]$ss = Get-Content (Join-Path $tmp 'xl/sharedStrings.xml') -Raw
        $strings = @($ss.sst.si | ForEach-Object { $_.InnerText })
        [xml]$sheet = Get-Content (Join-Path $tmp 'xl/worksheets/sheet1.xml') -Raw
        foreach ($row in $sheet.worksheet.sheetData.row) {
            $vals = @{}
            foreach ($c in $row.c) {
                $col = $c.r -replace '\d', ''
                $vals[$col] = if ($c.t -eq 's') { $strings[[int]$c.v] } else { $c.v }
            }
            ,$vals
        }
    }
    finally { Remove-Item -Recurse -Force $tmp }
}

# SyllabusCodes.xlsx: A=ao_code B=qualification_number C=quid D=syllabus_code E=syllabus_title
# F=KS4 G=1619. quid is the un-slashed QAN — the join key to QualList. Only 1619 rows apply to
# this service's 16-19 enquiry.
$syllabusByQuid = @{}
foreach ($r in (Read-Sheet $SyllabusCodes | Select-Object -Skip 1)) {
    if (('' + $r['G']).Trim() -ne '1') { continue }
    $quid = ('' + $r['C']).Trim()
    $code = ('' + $r['D']).Trim()
    $title = ('' + $r['E']).Trim()
    if (-not $quid -or -not $code) { continue }
    if (-not $syllabusByQuid.ContainsKey($quid)) { $syllabusByQuid[$quid] = [System.Collections.Generic.List[object]]::new() }
    $syllabusByQuid[$quid].Add([ordered]@{ code = $code; title = $title })
}
foreach ($k in @($syllabusByQuid.Keys)) {
    $syllabusByQuid[$k] = @($syllabusByQuid[$k] | Sort-Object { $_.code })
}

# QualList.xlsx: A=Qualification Number B=Title C=AO D=Grade E=Included in KS4 (ignored — no
# ticket filters the QAN list by it). One row per QAN+grade; grade order is the scale's own.
$quals = [ordered]@{}
foreach ($r in (Read-Sheet $QualList | Select-Object -Skip 1)) {
    $qan = ('' + $r['A']).Trim()
    if (-not $qan) { continue }
    if (-not $quals.Contains($qan)) {
        $quals[$qan] = [ordered]@{
            qan = $qan
            qualificationTitle = ('' + $r['B']).Trim()
            awardingOrganisation = ('' + $r['C']).Trim()
            grades = [System.Collections.Generic.List[string]]::new()
            # @(...) forces the if/else result into an array even when empty — an unwrapped @()
            # else-branch collapses to $null under ConvertTo-Json, turning "no syllabus codes"
            # into a missing field instead of [].
            syllabusCodes = @(if ($syllabusByQuid.ContainsKey($qan)) { $syllabusByQuid[$qan] } else { @() })
        }
    }
    $grade = ('' + $r['D']).Trim()
    if ($grade -and -not $quals[$qan].grades.Contains($grade)) { $quals[$qan].grades.Add($grade) }
}

New-Item -ItemType Directory -Force (Split-Path $OutFile) | Out-Null
$quals | ConvertTo-Json -Depth 6 | Set-Content $OutFile -Encoding utf8NoBOM
$covered = @($quals.Values | Where-Object { $_.syllabusCodes.Count -gt 0 }).Count
Write-Host "Wrote $($quals.Count) qualifications ($covered with syllabus codes) to $OutFile"
