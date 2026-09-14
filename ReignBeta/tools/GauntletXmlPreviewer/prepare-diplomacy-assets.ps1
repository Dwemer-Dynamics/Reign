param(
    [string]$ModuleRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path,
    [string]$DownloadsRoot = (Join-Path $env:USERPROFILE 'Downloads')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

public static class ReignDiplomacyAssetProcessor
{
    private static bool IsMagenta(Color pixel)
    {
        return Math.Min(pixel.R, pixel.B) - pixel.G > 55;
    }

    private static int MatteAlpha(Color pixel)
    {
        int magentaSignal = Math.Min(pixel.R, pixel.B) - pixel.G;
        if (magentaSignal <= 55) return 255;
        if (magentaSignal >= 120) return 0;
        return Math.Max(0, Math.Min(255, (int)Math.Round((120 - magentaSignal) * (255.0 / 65.0))));
    }

    public static void SaveOpaque(string sourcePath, string outputPath, int width, int height)
    {
        using (Bitmap source = new Bitmap(sourcePath))
        using (Bitmap output = new Bitmap(width, height, PixelFormat.Format32bppArgb))
        using (Graphics graphics = Graphics.FromImage(output))
        {
            Configure(graphics);
            graphics.DrawImage(source, new Rectangle(0, 0, width, height), new Rectangle(0, 0, source.Width, source.Height), GraphicsUnit.Pixel);
            output.Save(outputPath, ImageFormat.Png);
        }
    }

    public static void SaveKeyed(string sourcePath, string outputPath, int width, int height)
    {
        using (Bitmap source = new Bitmap(sourcePath))
        {
            int minX = source.Width;
            int minY = source.Height;
            int maxX = -1;
            int maxY = -1;

            for (int y = 0; y < source.Height; y++)
            {
                for (int x = 0; x < source.Width; x++)
                {
                    if (MatteAlpha(source.GetPixel(x, y)) <= 10) continue;
                    minX = Math.Min(minX, x);
                    minY = Math.Min(minY, y);
                    maxX = Math.Max(maxX, x);
                    maxY = Math.Max(maxY, y);
                }
            }

            if (maxX < minX || maxY < minY) throw new InvalidOperationException("No non-magenta artwork found in " + sourcePath);
            Rectangle crop = Rectangle.FromLTRB(minX, minY, maxX + 1, maxY + 1);

            using (Bitmap keyed = new Bitmap(crop.Width, crop.Height, PixelFormat.Format32bppArgb))
            {
                for (int y = 0; y < crop.Height; y++)
                {
                    for (int x = 0; x < crop.Width; x++)
                    {
                        Color pixel = source.GetPixel(crop.X + x, crop.Y + y);
                        int alpha = MatteAlpha(pixel);
                        if (alpha == 0)
                        {
                            keyed.SetPixel(x, y, Color.Transparent);
                            continue;
                        }

                        int red = pixel.R;
                        int green = pixel.G;
                        int blue = pixel.B;
                        if (alpha < 255 && IsMagenta(pixel))
                        {
                            blue = Math.Min(blue, green + 18);
                            if (Math.Abs(pixel.R - pixel.B) < 38) red = Math.Min(red, green + 22);
                        }
                        keyed.SetPixel(x, y, Color.FromArgb(alpha, red, green, blue));
                    }
                }

                using (Bitmap output = new Bitmap(width, height, PixelFormat.Format32bppArgb))
                using (Graphics graphics = Graphics.FromImage(output))
                {
                    Configure(graphics);
                    graphics.Clear(Color.Transparent);
                    graphics.DrawImage(keyed, new Rectangle(0, 0, width, height), new Rectangle(0, 0, keyed.Width, keyed.Height), GraphicsUnit.Pixel);
                    output.Save(outputPath, ImageFormat.Png);
                }
            }
        }
    }

    private static void Configure(Graphics graphics)
    {
        graphics.CompositingMode = CompositingMode.SourceCopy;
        graphics.CompositingQuality = CompositingQuality.HighQuality;
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.SmoothingMode = SmoothingMode.HighQuality;
    }
}
'@

$outputRoot = Join-Path $ModuleRoot 'GUI\SpriteParts\ui_reignbeta_diplomacy'
New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null

function Resolve-Artwork([string]$fileName) {
    $path = Join-Path $DownloadsRoot $fileName
    if (-not (Test-Path -LiteralPath $path)) { throw "Diplomacy artwork was not found: $path" }
    return $path
}

function Save-Opaque([string]$source, [string]$name, [int]$width, [int]$height) {
    $output = Join-Path $outputRoot ($name + '.png')
    [ReignDiplomacyAssetProcessor]::SaveOpaque($source, $output, $width, $height)
    Write-Output ("{0}: {1}x{2}" -f $name, $width, $height)
}

function Save-Keyed([string]$source, [string]$name, [int]$width, [int]$height) {
    $output = Join-Path $outputRoot ($name + '.png')
    [ReignDiplomacyAssetProcessor]::SaveKeyed($source, $output, $width, $height)
    Write-Output ("{0}: {1}x{2}" -f $name, $width, $height)
}

$assembled = Resolve-Artwork 'ChatGPT Image Jul 15, 2026, 02_17_31 PM (10).png'
$header = Resolve-Artwork 'ChatGPT Image Jul 15, 2026, 02_17_29 PM (1).png'
$leftPanel = Resolve-Artwork 'ChatGPT Image Jul 15, 2026, 02_17_29 PM (3).png'
$rightPanel = Resolve-Artwork 'ChatGPT Image Jul 15, 2026, 02_17_29 PM (2).png'
$centerPanel = Resolve-Artwork 'ChatGPT Image Jul 15, 2026, 02_17_30 PM (6).png'
$portraitFrame = Resolve-Artwork 'ChatGPT Image Jul 15, 2026, 02_17_30 PM (4).png'
$bannerFrame = Resolve-Artwork 'ChatGPT Image Jul 15, 2026, 02_17_30 PM (5).png'
$divider = Resolve-Artwork 'ChatGPT Image Jul 15, 2026, 02_17_31 PM (7).png'
$button = Resolve-Artwork 'ChatGPT Image Jul 15, 2026, 02_17_31 PM (8).png'
$buttonHover = Resolve-Artwork 'ChatGPT Image Jul 15, 2026, 02_17_31 PM (9).png'

# The supplied assembled composition is exactly 1602:982, the same aspect as
# Reign's 1240x760 logical panel. The remaining pieces are retained as reusable
# alpha sprites even when the assembled background already carries their frame.
Save-Opaque $assembled 'reign_diplomacy_background' 1240 760
Save-Keyed $header 'reign_diplomacy_header' 900 240
Save-Keyed $leftPanel 'reign_diplomacy_actor_panel' 248 520
Save-Keyed $rightPanel 'reign_diplomacy_target_panel' 248 520
Save-Keyed $centerPanel 'reign_diplomacy_center_panel' 632 520
Save-Keyed $portraitFrame 'reign_diplomacy_portrait_frame' 204 228
Save-Keyed $bannerFrame 'reign_diplomacy_banner_frame' 96 96
Save-Keyed $divider 'reign_diplomacy_divider' 544 24
Save-Keyed $button 'reign_diplomacy_acknowledge_button' 260 50
Save-Keyed $buttonHover 'reign_diplomacy_acknowledge_button_hover' 260 50
