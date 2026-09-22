param(
    [string]$ModuleRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path,
    [string]$DownloadsRoot = (Join-Path $env:USERPROFILE 'Downloads')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$outputRoot = Join-Path $ModuleRoot 'GUI\SpriteParts\ui_reignbeta_correspondence'
New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null

function Save-Crop {
    param(
        [Parameter(Mandatory)][string]$Source,
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][System.Drawing.Rectangle]$Crop,
        [switch]$RemoveCheckerboard
    )

    $sourceBitmap = [System.Drawing.Bitmap]::FromFile($Source)
    try {
        $bitmap = New-Object System.Drawing.Bitmap($Crop.Width, $Crop.Height, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        try {
            $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
            try {
                $graphics.Clear([System.Drawing.Color]::Transparent)
                $graphics.DrawImage(
                    $sourceBitmap,
                    (New-Object System.Drawing.Rectangle(0, 0, $Crop.Width, $Crop.Height)),
                    $Crop,
                    [System.Drawing.GraphicsUnit]::Pixel
                )
            }
            finally {
                $graphics.Dispose()
            }

            if ($RemoveCheckerboard) {
                for ($y = 0; $y -lt $bitmap.Height; $y++) {
                    for ($x = 0; $x -lt $bitmap.Width; $x++) {
                        $pixel = $bitmap.GetPixel($x, $y)
                        $maximum = [Math]::Max($pixel.R, [Math]::Max($pixel.G, $pixel.B))
                        $minimum = [Math]::Min($pixel.R, [Math]::Min($pixel.G, $pixel.B))
                        if ($minimum -ge 218 -and ($maximum - $minimum) -le 20) {
                            $bitmap.SetPixel($x, $y, [System.Drawing.Color]::Transparent)
                        }
                    }
                }
            }

            $path = Join-Path $outputRoot ($Name + '.png')
            $bitmap.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
            Write-Output ("{0}: {1}x{2}" -f $Name, $bitmap.Width, $bitmap.Height)
        }
        finally {
            $bitmap.Dispose()
        }
    }
    finally {
        $sourceBitmap.Dispose()
    }
}

$backgroundSource = Join-Path $DownloadsRoot 'ChatGPT Image Jul 15, 2026, 12_11_34 PM (1).png'
$contactSource = Join-Path $DownloadsRoot 'ChatGPT Image Jul 15, 2026, 12_11_34 PM (4).png'
$messageSource = Join-Path $DownloadsRoot 'ChatGPT Image Jul 15, 2026, 12_11_35 PM (6).png'
$scrollbarSource = Join-Path $DownloadsRoot 'ChatGPT Image Jul 15, 2026, 12_11_37 PM (9).png'
$buttonSource = Join-Path $DownloadsRoot 'ChatGPT Image Jul 15, 2026, 12_11_37 PM (10).png'

Save-Crop -Source $backgroundSource -Name 'reign_correspondence_background' -Crop ([System.Drawing.Rectangle]::new(0, 0, 1608, 978))
Save-Crop -Source $contactSource -Name 'reign_correspondence_contact_frame' -Crop ([System.Drawing.Rectangle]::new(84, 36, 1548, 412)) -RemoveCheckerboard
Save-Crop -Source $messageSource -Name 'reign_correspondence_player_letter' -Crop ([System.Drawing.Rectangle]::new(112, 42, 1886, 292)) -RemoveCheckerboard
Save-Crop -Source $messageSource -Name 'reign_correspondence_npc_letter' -Crop ([System.Drawing.Rectangle]::new(112, 370, 1886, 300)) -RemoveCheckerboard
Save-Crop -Source $scrollbarSource -Name 'reign_correspondence_scroll_track' -Crop ([System.Drawing.Rectangle]::new(214, 48, 108, 1932)) -RemoveCheckerboard
Save-Crop -Source $scrollbarSource -Name 'reign_correspondence_scroll_handle' -Crop ([System.Drawing.Rectangle]::new(378, 812, 102, 410)) -RemoveCheckerboard
Save-Crop -Source $buttonSource -Name 'reign_correspondence_button' -Crop ([System.Drawing.Rectangle]::new(718, 478, 626, 164)) -RemoveCheckerboard

