<#
.SYNOPSIS
Merges a supplier qualification list (AO / QAN / grades) and the supplier syllabus codes
(QUID -> syllabus codes) into a qualification reference file under
Web/Data/QualificationReference. Run it again when a supplier file changes.

-KeyStage 1619 (the default) writes qualification-reference.json (AB#297848). It reads the first
sheet of the qualification list and the syllabus rows where the 1619 column is 1.

-KeyStage KS4 writes qualification-reference-ks4.json (#900). It reads the syllabus rows where
the KS4 column is 1. Name the KS4 sheet with -QualListSheet.

.EXAMPLE
pwsh scripts/Convert-QualListToReference.ps1 `
    -QualList 'C:\Repos\Contracts\DfE\Tasks\QualList.xlsx' `
    -SyllabusCodes 'C:\Repos\Contracts\DfE\Tasks\SyllabusCodes.xlsx'

.EXAMPLE
pwsh scripts/Convert-QualListToReference.ps1 -KeyStage KS4 -QualListSheet KS4 `
    -QualList 'C:\Repos\Contracts\DfE\Tasks\Dynamic form QAN list 2026 v1.xlsx' `
    -SyllabusCodes 'C:\Repos\Contracts\DfE\Tasks\CYPMD Syllabus Codes 2026 Final.xlsx'
#>
param(
    [Parameter(Mandatory)] [string] $QualList,
    [Parameter(Mandatory)] [string] $SyllabusCodes,
    [ValidateSet('1619', 'KS4')] [string] $KeyStage = '1619',
    [string] $QualListSheet,
    [string] $OutFile
)
$ErrorActionPreference = 'Stop'

$outFileNames = @{ '1619' = 'qualification-reference.json'; 'KS4' = 'qualification-reference-ks4.json' }
if (-not $OutFile) {
    $OutFile = "$PSScriptRoot/../src/DfE.CheckPerformanceData.Web/Data/QualificationReference/$($outFileNames[$KeyStage])"
}

# An xlsx is a zip: shared strings + one part per sheet. No Excel/COM dependency, so this runs in CI too.
Add-Type -AssemblyName System.IO.Compression.FileSystem

# The part of the named sheet, or of the first sheet when no name is given. A sheet's part comes
# from the workbook relationships, not from its position.
function Resolve-SheetPart([string] $Root, [string] $SheetName) {
    [xml]$workbook = Get-Content (Join-Path $Root 'xl/workbook.xml') -Raw
    $sheets = @($workbook.workbook.sheets.sheet)
    $sheet = if ($SheetName) { $sheets | Where-Object { $_.name -eq $SheetName } | Select-Object -First 1 } else { $sheets[0] }
    if (-not $sheet) {
        throw "Sheet '$SheetName' is not in the workbook. Its sheets: $(($sheets | ForEach-Object { $_.name }) -join ', ')"
    }
    [xml]$rels = Get-Content (Join-Path $Root 'xl/_rels/workbook.xml.rels') -Raw
    $relId = $sheet.GetAttribute('id', 'http://schemas.openxmlformats.org/officeDocument/2006/relationships')
    $target = (@($rels.Relationships.Relationship) | Where-Object { $_.Id -eq $relId } | Select-Object -First 1).Target
    'xl/' + ($target -replace '^/?(xl/)?', '')
}

function Read-Sheet([string] $Path, [string] $SheetName) {
    $tmp = Join-Path ([IO.Path]::GetTempPath()) ("xlsx-" + [Guid]::NewGuid())
    [IO.Compression.ZipFile]::ExtractToDirectory($Path, $tmp)
    try {
        [xml]$ss = Get-Content (Join-Path $tmp 'xl/sharedStrings.xml') -Raw
        $strings = @($ss.sst.si | ForEach-Object { $_.InnerText })
        [xml]$sheet = Get-Content (Join-Path $tmp (Resolve-SheetPart $tmp $SheetName)) -Raw
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

# The column letter of a header, so a supplier file with its columns in another order still reads.
function Find-Column([hashtable] $Header, [string] $Name) {
    $column = $Header.Keys | Where-Object { ('' + $Header[$_]).Trim() -eq $Name } | Select-Object -First 1
    if (-not $column) { throw "No column has the header '$Name'." }
    $column
}

# Syllabus codes: awarding_organisation_code, qualification_number, quid, syllabus_code,
# syllabus_title, KS4, 1619. quid is the un-slashed QAN — the join key to the qualification list.
# Only the rows flagged for the key stage apply. A code that a QUID repeats is kept once.
$syllabusRows = @(Read-Sheet $SyllabusCodes)
$flagColumn = Find-Column $syllabusRows[0] $KeyStage
$syllabusByQuid = @{}
foreach ($r in ($syllabusRows | Select-Object -Skip 1)) {
    if (('' + $r[$flagColumn]).Trim() -ne '1') { continue }
    $quid = ('' + $r['C']).Trim()
    $code = ('' + $r['D']).Trim()
    $title = ('' + $r['E']).Trim()
    if (-not $quid -or -not $code) { continue }
    if (-not $syllabusByQuid.ContainsKey($quid)) { $syllabusByQuid[$quid] = [System.Collections.Generic.List[object]]::new() }
    if (@($syllabusByQuid[$quid] | Where-Object { $_.code -ceq $code }).Count -gt 0) { continue }
    $syllabusByQuid[$quid].Add([ordered]@{ code = $code; title = $title })
}
foreach ($k in @($syllabusByQuid.Keys)) {
    $syllabusByQuid[$k] = @($syllabusByQuid[$k] | Sort-Object { $_.code })
}

# Qualification list: A=Qualification Number B=Title C=AO D=Grade E=an "Included in ..." flag
# (ignored: the sheet is the list). One row per QAN+grade; grade order is the scale's own.
$qualRows = @(Read-Sheet $QualList $QualListSheet)
if ($KeyStage -eq 'KS4' -and ('' + $qualRows[0]['E']).Trim() -eq 'Included in KS4') {
    throw "This sheet is the 16-19 list (it has the column 'Included in KS4'). Name the KS4 sheet with -QualListSheet."
}
$quals = [ordered]@{}
foreach ($r in ($qualRows | Select-Object -Skip 1)) {
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
