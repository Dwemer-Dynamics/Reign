param(
    [string]$ModuleRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path,
    [string[]]$Categories = @()
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$drawingAssemblies = @(
    [System.Drawing.Bitmap].Assembly.Location,
    [System.Drawing.Point].Assembly.Location
) | Select-Object -Unique
Add-Type -ReferencedAssemblies $drawingAssemblies -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

public static class ReignRuntimeAtlasBitmap
{
    public static void CopyExact(Bitmap source, Bitmap destination, int destinationX, int destinationY)
    {
        Rectangle sourceRect = new Rectangle(0, 0, source.Width, source.Height);
        Rectangle destinationRect = new Rectangle(destinationX, destinationY, source.Width, source.Height);
        BitmapData sourceData = source.LockBits(sourceRect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        BitmapData destinationData = destination.LockBits(destinationRect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);

        try
        {
            int rowBytes = source.Width * 4;
            byte[] row = new byte[rowBytes];
            for (int y = 0; y < source.Height; y++)
            {
                Marshal.Copy(IntPtr.Add(sourceData.Scan0, y * sourceData.Stride), row, 0, rowBytes);
                Marshal.Copy(row, 0, IntPtr.Add(destinationData.Scan0, y * destinationData.Stride), rowBytes);
            }
        }
        finally
        {
            source.UnlockBits(sourceData);
            destination.UnlockBits(destinationData);
        }
    }

    public static bool RegionEquals(Bitmap source, Bitmap atlas, int atlasX, int atlasY)
    {
        Rectangle sourceRect = new Rectangle(0, 0, source.Width, source.Height);
        Rectangle atlasRect = new Rectangle(atlasX, atlasY, source.Width, source.Height);
        BitmapData sourceData = source.LockBits(sourceRect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        BitmapData atlasData = atlas.LockBits(atlasRect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);

        try
        {
            int rowBytes = source.Width * 4;
            byte[] sourceRow = new byte[rowBytes];
            byte[] atlasRow = new byte[rowBytes];
            for (int y = 0; y < source.Height; y++)
            {
                Marshal.Copy(IntPtr.Add(sourceData.Scan0, y * sourceData.Stride), sourceRow, 0, rowBytes);
                Marshal.Copy(IntPtr.Add(atlasData.Scan0, y * atlasData.Stride), atlasRow, 0, rowBytes);
                for (int i = 0; i < rowBytes; i++)
                {
                    if (sourceRow[i] != atlasRow[i]) return false;
                }
            }

            return true;
        }
        finally
        {
            source.UnlockBits(sourceData);
            atlas.UnlockBits(atlasData);
        }
    }
}
'@

$spriteDataPath = Join-Path $ModuleRoot 'GUI\ReignBetaSpriteData.xml'
$spritePartsRoot = Join-Path $ModuleRoot 'GUI\SpriteParts'
$outputRoot = Join-Path $ModuleRoot 'GUI\RuntimeSpriteSheets'

if (-not (Test-Path -LiteralPath $spriteDataPath)) {
    throw "Sprite data was not found: $spriteDataPath"
}

[xml]$spriteData = Get-Content -LiteralPath $spriteDataPath -Raw
$categoryNodes = @($spriteData.SpriteData.SpriteCategories.SpriteCategory | Where-Object {
    [string]$_.Name -like 'ui_reignbeta_*' -and (-not $Categories.Count -or [string]$_.Name -in $Categories)
})
$partNodes = @($spriteData.SpriteData.SpriteParts.SpritePart)

if (-not $categoryNodes.Count) {
    throw "No ui_reignbeta_* sprite categories were found in $spriteDataPath"
}

New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null

$manifestCategories = [ordered]@{}
if ($Categories.Count) {
    $previousManifest = Join-Path $outputRoot 'manifest.json'
    if (-not (Test-Path -LiteralPath $previousManifest)) { throw 'A full manifest must exist before a category-only rebuild.' }
    $previous = Get-Content -LiteralPath $previousManifest -Raw | ConvertFrom-Json
    foreach ($property in $previous.categories.PSObject.Properties) { $manifestCategories[$property.Name] = $property.Value }
    foreach ($requested in $Categories) {
        if (-not ($categoryNodes | Where-Object { [string]$_.Name -eq $requested })) { throw "Unknown sprite category: $requested" }
    }
}
foreach ($categoryNode in $categoryNodes) {
    $categoryName = [string]$categoryNode.Name
    $categoryOutput = Join-Path $outputRoot $categoryName
    New-Item -ItemType Directory -Path $categoryOutput -Force | Out-Null

    $sheets = @()
    $parts = [ordered]@{}
    $sheetSizeNodes = @($categoryNode.SpriteSheetSize)
    for ($sheetId = 1; $sheetId -le [int]$categoryNode.SpriteSheetCount; $sheetId++) {
        $sizeNode = $sheetSizeNodes | Where-Object { [int]$_.ID -eq $sheetId } | Select-Object -First 1
        if (-not $sizeNode) {
            throw "Category $categoryName does not define a size for sheet $sheetId."
        }

        $sheetWidth = [int]$sizeNode.Width
        $sheetHeight = [int]$sizeNode.Height
        $bitmap = New-Object System.Drawing.Bitmap($sheetWidth, $sheetHeight, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        try {
            $sheetParts = @($partNodes | Where-Object {
                [string]$_.CategoryName -eq $categoryName -and [int]$_.SheetID -eq $sheetId
            })
            foreach ($partNode in $sheetParts) {
                $partName = [string]$partNode.Name
                $partPath = Join-Path (Join-Path $spritePartsRoot $categoryName) ($partName + '.png')
                if (-not (Test-Path -LiteralPath $partPath)) {
                    throw "Sprite part $partName is declared but its PNG is missing: $partPath"
                }

                $image = [System.Drawing.Bitmap]::FromFile($partPath)
                try {
                    $expectedWidth = [int]$partNode.Width
                    $expectedHeight = [int]$partNode.Height
                    if ($image.Width -ne $expectedWidth -or $image.Height -ne $expectedHeight) {
                        throw "Sprite part $partName is $($image.Width)x$($image.Height); SpriteData declares ${expectedWidth}x${expectedHeight}."
                    }

                    $x = [int]$partNode.SheetX
                    $y = [int]$partNode.SheetY
                    if ($x -lt 0 -or $y -lt 0 -or ($x + $expectedWidth) -gt $sheetWidth -or ($y + $expectedHeight) -gt $sheetHeight) {
                        throw "Sprite part $partName lies outside $categoryName sheet $sheetId."
                    }

                    # GDI+'s DrawImage/DrawImageUnscaled path can discard opaque pixels on the
                    # source image's right and bottom edges. Copy the ARGB rows verbatim so a
                    # packed sprite is pixel-identical to its source PNG.
                    [ReignRuntimeAtlasBitmap]::CopyExact($image, $bitmap, $x, $y)
                }
                finally {
                    $image.Dispose()
                }

                $relativePartPath = ('GUI/SpriteParts/{0}/{1}.png' -f $categoryName, $partName)
                $parts[$partName] = [ordered]@{
                    sheetId = $sheetId
                    x = [int]$partNode.SheetX
                    y = [int]$partNode.SheetY
                    width = [int]$partNode.Width
                    height = [int]$partNode.Height
                    sourcePath = $relativePartPath
                    sourceSha256 = (Get-FileHash -LiteralPath $partPath -Algorithm SHA256).Hash.ToLowerInvariant()
                }
            }

            $sheetFileName = "${categoryName}_${sheetId}.png"
            $sheetPath = Join-Path $categoryOutput $sheetFileName
            $bitmap.Save($sheetPath, [System.Drawing.Imaging.ImageFormat]::Png)
        }
        finally {
            $bitmap.Dispose()
        }

        # Re-open the encoded atlas and verify every region against its source. A build must
        # fail instead of publishing an atlas with silently clipped right or bottom edges.
        $writtenAtlas = [System.Drawing.Bitmap]::FromFile($sheetPath)
        try {
            foreach ($partNode in $sheetParts) {
                $partName = [string]$partNode.Name
                $partPath = Join-Path (Join-Path $spritePartsRoot $categoryName) ($partName + '.png')
                $sourceBitmap = [System.Drawing.Bitmap]::FromFile($partPath)
                try {
                    if (-not [ReignRuntimeAtlasBitmap]::RegionEquals(
                        $sourceBitmap,
                        $writtenAtlas,
                        [int]$partNode.SheetX,
                        [int]$partNode.SheetY
                    )) {
                        throw "Packed sprite $partName is not pixel-identical to its source PNG."
                    }
                }
                finally {
                    $sourceBitmap.Dispose()
                }
            }
        }
        finally {
            $writtenAtlas.Dispose()
        }

        $sheets += [ordered]@{
            id = $sheetId
            width = $sheetWidth
            height = $sheetHeight
            path = ('GUI/RuntimeSpriteSheets/{0}/{1}' -f $categoryName, $sheetFileName)
            sha256 = (Get-FileHash -LiteralPath $sheetPath -Algorithm SHA256).Hash.ToLowerInvariant()
        }
    }

    $manifestCategories[$categoryName] = [ordered]@{
        sheets = $sheets
        parts = $parts
    }
}

$manifest = [ordered]@{
    version = 1
    generatedUtc = [DateTime]::UtcNow.ToString('o')
    spriteDataPath = 'GUI/ReignBetaSpriteData.xml'
    spriteDataSha256 = (Get-FileHash -LiteralPath $spriteDataPath -Algorithm SHA256).Hash.ToLowerInvariant()
    categories = $manifestCategories
}

$manifestPath = Join-Path $outputRoot 'manifest.json'
$json = $manifest | ConvertTo-Json -Depth 12
[System.IO.File]::WriteAllText($manifestPath, $json, (New-Object System.Text.UTF8Encoding($false)))

Write-Output "Built $($categoryNodes.Count) runtime sprite categories."
Write-Output "Manifest: $manifestPath"
