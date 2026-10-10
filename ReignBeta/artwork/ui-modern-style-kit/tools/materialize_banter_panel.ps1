# Deterministic, complete vector plate. Frozen palette tokens only; the frame
# and dialog symbol belong to this one artwork, never separate runtime overlays.
param([string]$ModuleRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$banterAssetDirectory = Join-Path $ModuleRoot 'GUI\SpriteParts\ui_reignbeta_banter'
New-Item -ItemType Directory -Force -Path $banterAssetDirectory | Out-Null
foreach ($banterAssetHeight in @(220,42)) {
    $banterBitmap = New-Object System.Drawing.Bitmap(420,$banterAssetHeight)
    $banterGraphics = [System.Drawing.Graphics]::FromImage($banterBitmap)
    $banterGraphics.Clear([System.Drawing.ColorTranslator]::FromHtml('#121211'))
    $banterPen = New-Object System.Drawing.Pen([System.Drawing.ColorTranslator]::FromHtml('#7E6A4D'),1)
    $banterQuietPen = New-Object System.Drawing.Pen([System.Drawing.ColorTranslator]::FromHtml('#352C20'),1)
    $banterGraphics.DrawRectangle($banterPen,0,0,419,($banterAssetHeight-1))
    $banterGraphics.DrawRectangle($banterQuietPen,3,3,413,($banterAssetHeight-7))
    $banterGraphics.DrawLine($banterQuietPen,12,36,($banterAssetHeight -eq 42 ? 346 : 408),36)
    $banterIconY = $banterAssetHeight - 31
    $banterGraphics.DrawRectangle($banterPen,374,$banterIconY,23,14)
    $banterGraphics.DrawLine($banterPen,378,($banterIconY+14),378,($banterIconY+20))
    $banterGraphics.DrawLine($banterPen,378,($banterIconY+20),385,($banterIconY+14))
    $banterName = $banterAssetHeight -eq 220 ? 'reign_banter_panel.png' : 'reign_banter_collapsed.png'
    $banterBitmap.Save((Join-Path $banterAssetDirectory $banterName),[System.Drawing.Imaging.ImageFormat]::Png)
    $banterPen.Dispose(); $banterQuietPen.Dispose(); $banterGraphics.Dispose(); $banterBitmap.Dispose()
}
