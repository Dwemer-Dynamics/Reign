param([string]$EvidenceRoot = '.codex-build/social-event-frame-cleanup-20260905')
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$kit = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$workspace = [IO.Path]::GetFullPath((Join-Path $kit '../../..'))
$sourcePath = Join-Path $kit 'generated-sources/screen-shells/social-event-pre-cleanup-shell.png'
$fillPath = Join-Path $kit 'generated-sources/screen-shells/social-event-clean-roster-source.png'
if ((Get-FileHash $sourcePath).Hash -ne '8BFDDC7169166E5389BB71F1B0BBCD1ED17D77230657B567F1CFEC15425DCCC2') { throw 'Original shell changed.' }
if ((Get-FileHash $fillPath).Hash -ne '476169C736114D51BC720DA3371FF75D560B9BEB74C68F164700976321CD65B9') { throw 'Generated cleanup source changed.' }
$source = [Drawing.Bitmap]::new($sourcePath)
$fill = [Drawing.Bitmap]::new($fillPath)
$shell = $source.Clone([Drawing.Rectangle]::new(0,0,$source.Width,$source.Height),[Drawing.Imaging.PixelFormat]::Format32bppArgb)
$card = [Drawing.Bitmap]::new(480,124)
try {
    if ($fill.Width -ne 1672 -or $fill.Height -ne 941) { throw 'Unexpected generated dimensions.' }
    # Integrate only the generated clean marble. The +24 source offset omits
    # the small obsolete diamond retained by the image editor at y=132.
    for ($y=120; $y -lt 670; $y++) {
        for ($x=30; $x -lt 578; $x++) {
            $weight = [Math]::Min(1.0,[Math]::Min([Math]::Min(($x-29)/4.0,(578-$x)/4.0),[Math]::Min(($y-119)/4.0,(670-$y)/12.0)))
            $newPixel = $fill.GetPixel($x,$y+24)
            $oldPixel = $source.GetPixel($x,$y)
            $shell.SetPixel($x,$y,[Drawing.Color]::FromArgb(255,
                [int][Math]::Round($newPixel.R*$weight+$oldPixel.R*(1-$weight)),
                [int][Math]::Round($newPixel.G*$weight+$oldPixel.G*(1-$weight)),
                [int][Math]::Round($newPixel.B*$weight+$oldPixel.B*(1-$weight))))
        }
    }
    # Reuse exact approved gold border pixels, without tint or interpolation.
    # Existing 34px nine-slice registration and 480x124 atlas footprint remain.
    for ($y=0; $y -lt 124; $y++) {
        $sy = if ($y -lt 34) { $y } elseif ($y -ge 90) { 485-(124-$y) } else { 34+[int][Math]::Floor(($y-34)*417.0/56) }
        for ($x=0; $x -lt 480; $x++) {
            $sx = if ($x -lt 34) { $x } elseif ($x -ge 446) { 530-(480-$x) } else { 34+[int][Math]::Floor(($x-34)*462.0/412) }
            # The original panel's heading diamond is not part of a card edge.
            if ($y -lt 8 -and $sx -ge 250 -and $sx -le 282) { $sx = 120 }
            if ($x -lt 8 -or $x -ge 472 -or $y -lt 8 -or $y -ge 116 -or (($x -lt 34 -or $x -ge 446) -and ($y -lt 34 -or $y -ge 90))) {
                $borderPixel = $source.GetPixel(38+$sx,136+$sy)
                # A frame-only sprite must not carry rectangular marble corner
                # patches over the accepted flat card fill. Keep original gold
                # pixels exactly; the neutral source backing stays transparent.
                if ($borderPixel.R -ge 25 -and $borderPixel.R -gt $borderPixel.G -and $borderPixel.G -gt $borderPixel.B) {
                    $card.SetPixel($x,$y,$borderPixel)
                }
            }
        }
    }
    $fixed = 0
    for ($y=0; $y -lt 941; $y++) {
        for ($x=0; $x -lt 1672; $x++) {
            if ($x -ge 30 -and $x -lt 578 -and $y -ge 120 -and $y -lt 670) { continue }
            if ($source.GetPixel($x,$y).ToArgb() -ne $shell.GetPixel($x,$y).ToArgb()) { throw 'Unexpected fixed artwork change.' }
            $fixed++
        }
    }
    $output = Join-Path $workspace 'ReignBeta/GUI/SpriteParts/ui_reignbeta_social_event'
    $shell.Save((Join-Path $output 'reign_social_event_modern_shell.png'),[Drawing.Imaging.ImageFormat]::Png)
    $card.Save((Join-Path $output 'reign_social_event_card_frame.png'),[Drawing.Imaging.ImageFormat]::Png)
    $evidence = Join-Path $workspace $EvidenceRoot
    New-Item -ItemType Directory -Force $evidence | Out-Null
    @{ ok=$true; fixedPixelCount=$fixed; exactFixedPixelRatio=1; portraitInputsChanged=$false; cleanupRegion=@(30,120,548,550); frameSource=@(38,136,530,485); frameColorPolicy='Exact original shell pixels; no tint'; generation='Built-in imagegen, remove retired attendee rectangle and divider only' } | ConvertTo-Json | Set-Content (Join-Path $evidence 'asset-contract.json')
} finally { $card.Dispose(); $shell.Dispose(); $fill.Dispose(); $source.Dispose() }
