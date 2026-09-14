[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$TileDirectory,

    [Parameter(Mandatory)]
    [string]$OutputDirectory,

    [ValidateRange(0.0, 0.01)]
    [double]$Tolerance = 0.000001
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Assert-Equal {
    param(
        [Parameter(Mandatory)]$Actual,
        [Parameter(Mandatory)]$Expected,
        [Parameter(Mandatory)][string]$Label,
        [Parameter(Mandatory)][string]$TileName
    )

    if ($Actual -ne $Expected) {
        throw "$TileName has $Label '$Actual'; expected '$Expected'."
    }
}

$resolvedTileDirectory = [IO.Path]::GetFullPath($TileDirectory).TrimEnd('\')
$resolvedOutputDirectory = [IO.Path]::GetFullPath($OutputDirectory).TrimEnd('\')

if (-not (Test-Path -LiteralPath $resolvedTileDirectory -PathType Container)) {
    throw "Terrain tile directory does not exist: $resolvedTileDirectory"
}

$tileFiles = @(
    Get-ChildItem -LiteralPath $resolvedTileDirectory -Filter 'tile_r*_c*.json' -File |
        Sort-Object Name
)

if ($tileFiles.Count -eq 0) {
    throw "No terrain tile JSON files were found in $resolvedTileDirectory."
}

$firstTile = Get-Content -LiteralPath $tileFiles[0].FullName -Raw | ConvertFrom-Json
$totalColumns = [int]$firstTile.totalSampleColumns
$totalRows = [int]$firstTile.totalSampleRows

if ($totalColumns -lt 1 -or $totalColumns -gt 4097 -or $totalRows -lt 1 -or $totalRows -gt 4097) {
    throw "Logical terrain grid ${totalColumns}x${totalRows} is outside the supported 1..4097 range."
}

$sampleCapacity = $totalColumns * $totalRows
$heights = [double[]]::new($sampleCapacity)
$sampleX = [double[]]::new($sampleCapacity)
$sampleY = [double[]]::new($sampleCapacity)
$seen = [bool[]]::new($sampleCapacity)

$sceneName = [string]$firstTile.sceneName
$revision = [string]$firstTile.revision
$nodeDimensionX = [int]$firstTile.nodeDimension.x
$nodeDimensionY = [int]$firstTile.nodeDimension.y
$nodeSize = [double]$firstTile.nodeSize
$terrainWidth = [double]$firstTile.terrainSize.width
$terrainHeight = [double]$firstTile.terrainSize.height
$reportedMinHeight = [double]$firstTile.minHeight
$reportedMaxHeight = [double]$firstTile.maxHeight

$uniqueSamples = 0L
$duplicateSamples = 0L
$sourceSamples = 0L
$observedMinHeight = [double]::PositiveInfinity
$observedMaxHeight = [double]::NegativeInfinity
$tileSummaries = [Collections.Generic.List[object]]::new()

foreach ($tileFile in $tileFiles) {
    if ($tileFile.Name -notmatch '^tile_r(?<row>\d+)_c(?<column>\d+)\.json$') {
        throw "Unexpected terrain tile filename: $($tileFile.Name)"
    }

    $tile = Get-Content -LiteralPath $tileFile.FullName -Raw | ConvertFrom-Json
    Assert-Equal $tile.sceneName $sceneName 'sceneName' $tileFile.Name
    Assert-Equal $tile.revision $revision 'revision' $tileFile.Name
    Assert-Equal ([int]$tile.totalSampleColumns) $totalColumns 'totalSampleColumns' $tileFile.Name
    Assert-Equal ([int]$tile.totalSampleRows) $totalRows 'totalSampleRows' $tileFile.Name
    Assert-Equal ([int]$tile.nodeDimension.x) $nodeDimensionX 'nodeDimension.x' $tileFile.Name
    Assert-Equal ([int]$tile.nodeDimension.y) $nodeDimensionY 'nodeDimension.y' $tileFile.Name

    if ([Math]::Abs(([double]$tile.nodeSize) - $nodeSize) -gt $Tolerance) {
        throw "$($tileFile.Name) has a different nodeSize."
    }
    if ([Math]::Abs(([double]$tile.terrainSize.width) - $terrainWidth) -gt $Tolerance -or
        [Math]::Abs(([double]$tile.terrainSize.height) - $terrainHeight) -gt $Tolerance) {
        throw "$($tileFile.Name) has different terrain dimensions."
    }

    $tileColumns = [int]$tile.sampleColumns
    $tileRows = [int]$tile.sampleRows
    $columnOffset = [int]$tile.sampleColumnOffset
    $rowOffset = [int]$tile.sampleRowOffset
    $expectedTileSamples = $tileColumns * $tileRows
    $actualTileSamples = @($tile.samples).Count

    if ($tileColumns -lt 1 -or $tileRows -lt 1 -or $tileColumns -gt 257 -or $tileRows -gt 257) {
        throw "$($tileFile.Name) has unsupported tile dimensions ${tileColumns}x${tileRows}."
    }
    if ($columnOffset -lt 0 -or $rowOffset -lt 0 -or
        $columnOffset + $tileColumns -gt $totalColumns -or
        $rowOffset + $tileRows -gt $totalRows) {
        throw "$($tileFile.Name) lies outside the logical terrain grid."
    }
    if ($actualTileSamples -ne $expectedTileSamples) {
        throw "$($tileFile.Name) has $actualTileSamples samples; expected $expectedTileSamples."
    }

    foreach ($sample in $tile.samples) {
        $localColumn = [int]$sample.column
        $localRow = [int]$sample.row
        $globalColumn = [int]$sample.globalColumn
        $globalRow = [int]$sample.globalRow

        if ($localColumn -lt 0 -or $localColumn -ge $tileColumns -or
            $localRow -lt 0 -or $localRow -ge $tileRows) {
            throw "$($tileFile.Name) contains an out-of-range local sample ($localColumn,$localRow)."
        }
        if ($globalColumn -ne $columnOffset + $localColumn -or
            $globalRow -ne $rowOffset + $localRow) {
            throw "$($tileFile.Name) contains an inconsistent global sample ($globalColumn,$globalRow)."
        }

        $index = $globalRow * $totalColumns + $globalColumn
        $height = [double]$sample.height
        $x = [double]$sample.x
        $y = [double]$sample.y

        if ($seen[$index]) {
            $duplicateSamples++
            if ([Math]::Abs($heights[$index] - $height) -gt $Tolerance -or
                [Math]::Abs($sampleX[$index] - $x) -gt $Tolerance -or
                [Math]::Abs($sampleY[$index] - $y) -gt $Tolerance) {
                throw "$($tileFile.Name) disagrees with an overlapping tile at logical sample ($globalColumn,$globalRow)."
            }
        }
        else {
            $seen[$index] = $true
            $heights[$index] = $height
            $sampleX[$index] = $x
            $sampleY[$index] = $y
            $uniqueSamples++

            if ($height -lt $observedMinHeight) { $observedMinHeight = $height }
            if ($height -gt $observedMaxHeight) { $observedMaxHeight = $height }
        }

        $sourceSamples++
    }

    $tileSummaries.Add([ordered]@{
        file = $tileFile.Name
        rowIndex = [int]$Matches.row
        columnIndex = [int]$Matches.column
        columnOffset = $columnOffset
        rowOffset = $rowOffset
        columns = $tileColumns
        rows = $tileRows
        samples = $actualTileSamples
        sha256 = (Get-FileHash -LiteralPath $tileFile.FullName -Algorithm SHA256).Hash
    })
}

if ($uniqueSamples -ne $sampleCapacity) {
    $missing = $sampleCapacity - $uniqueSamples
    throw "Terrain capture is incomplete: $missing of $sampleCapacity logical samples are missing."
}

New-Item -ItemType Directory -Path $resolvedOutputDirectory -Force | Out-Null
$heightGridPath = Join-Path $resolvedOutputDirectory "heightmap-${totalColumns}x${totalRows}-y-up-f32le.bin"
$heightMaskPath = Join-Path $resolvedOutputDirectory "heightmap-${totalColumns}x${totalRows}-north-up.pgm"
$reportPath = Join-Path $resolvedOutputDirectory 'terrain-capture-audit.json'

$heightStream = [IO.File]::Create($heightGridPath)
try {
    $writer = [IO.BinaryWriter]::new($heightStream)
    try {
        foreach ($height in $heights) {
            $writer.Write([single]$height)
        }
    }
    finally {
        $writer.Dispose()
    }
}
finally {
    $heightStream.Dispose()
}

$heightRange = $observedMaxHeight - $observedMinHeight
if ($heightRange -le 0.0) { throw 'Terrain height range is zero; a normalized heightmask cannot be produced.' }

$pgmHeader = [Text.Encoding]::ASCII.GetBytes("P5`n$totalColumns $totalRows`n65535`n")
$pgmData = [byte[]]::new($sampleCapacity * 2)
$destination = 0
for ($imageRow = 0; $imageRow -lt $totalRows; $imageRow++) {
    $sourceRow = $totalRows - 1 - $imageRow
    $rowStart = $sourceRow * $totalColumns
    for ($column = 0; $column -lt $totalColumns; $column++) {
        $normalizedHeight = ($heights[$rowStart + $column] - $observedMinHeight) / $heightRange
        $value = [uint16][Math]::Round([Math]::Max(0.0, [Math]::Min(1.0, $normalizedHeight)) * 65535.0)
        $pgmData[$destination++] = [byte](($value -shr 8) -band 0xFF)
        $pgmData[$destination++] = [byte]($value -band 0xFF)
    }
}

$pgmStream = [IO.File]::Create($heightMaskPath)
try {
    $pgmStream.Write($pgmHeader, 0, $pgmHeader.Length)
    $pgmStream.Write($pgmData, 0, $pgmData.Length)
}
finally {
    $pgmStream.Dispose()
}

$report = [ordered]@{
    schema = 'bannerlord.editor_terrain_capture_audit.v1'
    ok = $true
    sceneName = $sceneName
    revision = $revision
    logicalGrid = [ordered]@{ columns = $totalColumns; rows = $totalRows }
    terrain = [ordered]@{
        nodeDimension = [ordered]@{ x = $nodeDimensionX; y = $nodeDimensionY }
        nodeSize = $nodeSize
        width = $terrainWidth
        height = $terrainHeight
        reportedMinHeight = $reportedMinHeight
        reportedMaxHeight = $reportedMaxHeight
        observedSampleMinHeight = $observedMinHeight
        observedSampleMaxHeight = $observedMaxHeight
    }
    coverage = [ordered]@{
        tileCount = $tileFiles.Count
        sourceSamples = $sourceSamples
        uniqueSamples = $uniqueSamples
        expectedUniqueSamples = $sampleCapacity
        duplicateOverlapSamples = $duplicateSamples
        missingSamples = 0
        overlapMismatches = 0
        tolerance = $Tolerance
    }
    orientation = [ordered]@{
        binary = 'row-major; globalRow 0 first; world Y increases with row'
        pgm = 'north-up authoring mask; source global rows vertically flipped'
    }
    outputs = [ordered]@{
        heightGridF32Le = $heightGridPath
        heightGridSha256 = (Get-FileHash -LiteralPath $heightGridPath -Algorithm SHA256).Hash
        heightMaskPgm = $heightMaskPath
        heightMaskSha256 = (Get-FileHash -LiteralPath $heightMaskPath -Algorithm SHA256).Hash
    }
    tiles = $tileSummaries
}

$reportJson = $report | ConvertTo-Json -Depth 8
[IO.File]::WriteAllText($reportPath, $reportJson + [Environment]::NewLine, [Text.UTF8Encoding]::new($false))

$reportJson
