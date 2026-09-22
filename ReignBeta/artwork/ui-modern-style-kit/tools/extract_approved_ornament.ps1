param(
    [Parameter(Mandatory = $true)][string]$SpecPath
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

function Get-Sha256 {
    param([Parameter(Mandatory = $true)][string]$Path)
    return (Get-FileHash -Algorithm SHA256 -LiteralPath $Path).Hash.ToLowerInvariant()
}

$workspaceRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..\..'))
$workspacePrefix = $workspaceRoot.TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
$specFullPath = [System.IO.Path]::GetFullPath((Join-Path $workspaceRoot $SpecPath))
if (-not $specFullPath.StartsWith($workspacePrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Ornament spec must be beneath the Reign workspace: $specFullPath"
}
if (-not (Test-Path -LiteralPath $specFullPath -PathType Leaf)) {
    throw "Ornament spec is missing: $specFullPath"
}

$spec = Get-Content -Raw -LiteralPath $specFullPath | ConvertFrom-Json
if ($spec.schema -ne 'reign-ui-approved-ornament-extraction-v1') {
    throw "Unsupported ornament extraction schema: $($spec.schema)"
}

$sourcePath = [System.IO.Path]::GetFullPath((Join-Path $workspaceRoot ([string]$spec.source.path)))
$outputPath = [System.IO.Path]::GetFullPath((Join-Path $workspaceRoot ([string]$spec.output.path)))
$evidencePath = [System.IO.Path]::GetFullPath((Join-Path $workspaceRoot ([string]$spec.evidencePath)))
foreach ($path in @($sourcePath, $outputPath, $evidencePath)) {
    if (-not $path.StartsWith($workspacePrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Ornament extraction path escapes the Reign workspace: $path"
    }
}
if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) {
    throw "Approved ornament source is missing: $sourcePath"
}

$cropX = [int]$spec.source.crop.x
$cropY = [int]$spec.source.crop.y
$cropWidth = [int]$spec.source.crop.width
$cropHeight = [int]$spec.source.crop.height
$canvasWidth = [int]$spec.output.width
$canvasHeight = [int]$spec.output.height
$destinationX = [int]$spec.output.destination.x
$destinationY = [int]$spec.output.destination.y
$minimumRed = [int]$spec.selector.minimumRed
$minimumRedMinusBlue = [int]$spec.selector.minimumRedMinusBlue
$minimumGreenMinusBlue = [int]$spec.selector.minimumGreenMinusBlue

if ($cropWidth -le 0 -or $cropHeight -le 0 -or $canvasWidth -le 0 -or $canvasHeight -le 0) {
    throw 'Crop and output dimensions must be positive.'
}
if ($destinationX -lt 0 -or $destinationY -lt 0 -or $destinationX + $cropWidth -gt $canvasWidth -or $destinationY + $cropHeight -gt $canvasHeight) {
    throw 'The approved crop does not fit inside the requested output canvas.'
}

$source = [System.Drawing.Bitmap]::new($sourcePath)
$output = [System.Drawing.Bitmap]::new($canvasWidth, $canvasHeight, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$retained = 0
$minX = [int]::MaxValue
$minY = [int]::MaxValue
$maxX = -1
$maxY = -1
try {
    if ($cropX -lt 0 -or $cropY -lt 0 -or $cropX + $cropWidth -gt $source.Width -or $cropY + $cropHeight -gt $source.Height) {
        throw 'The approved crop lies outside the source image.'
    }

    for ($y = 0; $y -lt $cropHeight; $y++) {
        for ($x = 0; $x -lt $cropWidth; $x++) {
            $pixel = $source.GetPixel($cropX + $x, $cropY + $y)
            $isOrnament = $pixel.R -ge $minimumRed `
                -and ($pixel.R - $pixel.B) -ge $minimumRedMinusBlue `
                -and ($pixel.G - $pixel.B) -ge $minimumGreenMinusBlue
            if (-not $isOrnament) { continue }

            $targetX = $destinationX + $x
            $targetY = $destinationY + $y
            $output.SetPixel($targetX, $targetY, [System.Drawing.Color]::FromArgb(255, $pixel.R, $pixel.G, $pixel.B))
            $retained++
            if ($targetX -lt $minX) { $minX = $targetX }
            if ($targetY -lt $minY) { $minY = $targetY }
            if ($targetX -gt $maxX) { $maxX = $targetX }
            if ($targetY -gt $maxY) { $maxY = $targetY }
        }
    }

    if ($retained -ne [int]$spec.acceptance.expectedRetainedPixels) {
        throw "Retained $retained ornament pixels; expected $($spec.acceptance.expectedRetainedPixels)."
    }
    $expected = $spec.acceptance.expectedRetainedBounds
    if ($minX -ne [int]$expected.left -or $minY -ne [int]$expected.top -or $maxX -ne [int]$expected.right -or $maxY -ne [int]$expected.bottom) {
        throw "Retained ornament bounds $minX,$minY-$maxX,$maxY do not match the approved $($expected.left),$($expected.top)-$($expected.right),$($expected.bottom)."
    }

    [System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($outputPath)) | Out-Null
    $temporaryOutput = $outputPath + '.tmp.png'
    $output.Save($temporaryOutput, [System.Drawing.Imaging.ImageFormat]::Png)
    Move-Item -LiteralPath $temporaryOutput -Destination $outputPath -Force
}
finally {
    $output.Dispose()
    $source.Dispose()
}

[System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($evidencePath)) | Out-Null
$evidence = [ordered]@{
    schema = 'reign-ui-approved-ornament-extraction-evidence-v1'
    generatedUtc = [DateTime]::UtcNow.ToString('o')
    id = [string]$spec.id
    specPath = $SpecPath.Replace('\', '/')
    specSha256 = Get-Sha256 -Path $specFullPath
    source = [ordered]@{
        path = [string]$spec.source.path
        sha256 = Get-Sha256 -Path $sourcePath
        crop = $spec.source.crop
    }
    selector = $spec.selector
    output = [ordered]@{
        path = [string]$spec.output.path
        sha256 = Get-Sha256 -Path $outputPath
        width = $canvasWidth
        height = $canvasHeight
        retainedPixels = $retained
        retainedBounds = [ordered]@{ left = $minX; top = $minY; right = $maxX; bottom = $maxY }
        transparentPixels = ($canvasWidth * $canvasHeight) - $retained
    }
    exactSourceRgbRetained = $true
}
[System.IO.File]::WriteAllText($evidencePath, ($evidence | ConvertTo-Json -Depth 8) + [Environment]::NewLine, [System.Text.UTF8Encoding]::new($false))

$evidence | ConvertTo-Json -Depth 8
