<#
.SYNOPSIS
    Scans a directory for VB6 FRM/FRX pairs and dumps a structured sample of every FRX item.

.PARAMETER Directory
    Root directory to scan (recurses into sub-folders).

.PARAMETER SampleBytes
    Maximum bytes to read from each item for display (default 64).
#>
param(
    [Parameter(Mandatory)][string] $Directory,
    [int] $SampleBytes = 64
)

$results = [System.Collections.Generic.List[PSCustomObject]]::new()

foreach ($frm in (Get-ChildItem -Path $Directory -Recurse -Filter '*.frm' | Sort-Object FullName)) {
    $frxPath = [IO.Path]::ChangeExtension($frm.FullName, '.frx')
    if (-not (Test-Path $frxPath)) { continue }

    $frxData = [IO.File]::ReadAllBytes($frxPath)
    $frxSize = $frxData.Length
    $content = [IO.File]::ReadAllText($frm.FullName)

    # Match:  SomeProperty  =  "whatever.frx":HEXOFFSET
    $refs = [System.Collections.Generic.List[PSCustomObject]]::new()
    foreach ($m in [regex]::Matches($content, '(?m)^\s*([\w\.]+)\s*=\s*"[^"]+\.frx":([0-9A-Fa-f]+)')) {
        $refs.Add([PSCustomObject]@{
            Property = $m.Groups[1].Value
            Offset   = [Convert]::ToInt32($m.Groups[2].Value, 16)
        })
    }
    if ($refs.Count -eq 0) { continue }

    # Sort by offset; compute each item's byte length from the gap to the next item
    $sorted = $refs | Sort-Object Offset

    for ($i = 0; $i -lt $sorted.Count; $i++) {
        $start  = [int]$sorted[$i].Offset
        $end    = if ($i + 1 -lt $sorted.Count) { [int]$sorted[$i + 1].Offset } else { $frxSize }
        $length = $end - $start

        $take   = [Math]::Min($SampleBytes, [Math]::Max(0, $length))
        $sample = if ($take -gt 0) { $frxData[$start .. ($start + $take - 1)] } else { [byte[]]@() }
        $hex    = ($sample | ForEach-Object { '{0:X2}' -f $_ }) -join ' '

        $results.Add([PSCustomObject]@{
            File       = $frm.Name
            Property   = $sorted[$i].Property
            Offset     = '0x{0:X4}' -f $start
            ByteLength = $length
            DataSample = $hex
        })
    }
}

# ── Output ───────────────────────────────────────────────────────────────────

foreach ($group in ($results | Group-Object File)) {
    Write-Host ''
    Write-Host ('━' * 80) -ForegroundColor DarkGray
    Write-Host "  $($group.Name)" -ForegroundColor Cyan
    Write-Host ('━' * 80) -ForegroundColor DarkGray

    foreach ($item in $group.Group) {
        Write-Host (
            '  {0,-40}  offset={1}  len={2,7}' -f $item.Property, $item.Offset, $item.ByteLength
        ) -ForegroundColor Yellow

        # Print hex sample in rows of 16 bytes, indented
        $bytes = if ($item.DataSample) { $item.DataSample -split ' ' } else { @() }
        for ($row = 0; $row -lt $bytes.Count; $row += 16) {
            $chunk     = $bytes[$row .. ([Math]::Min($row + 15, $bytes.Count - 1))]
            $hexPart   = ($chunk -join ' ').PadRight(47)
            $asciiPart = -join ($chunk | ForEach-Object {
                $b = [Convert]::ToByte($_, 16)
                if ($b -ge 0x20 -and $b -le 0x7E) { [char]$b } else { '.' }
            })
            Write-Host ('    {0:X4}  {1}  |{2}|' -f $row, $hexPart, $asciiPart)
        }
    }
}

Write-Host ''
Write-Host "Total items: $($results.Count)"
