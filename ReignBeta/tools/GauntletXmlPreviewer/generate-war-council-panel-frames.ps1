param(
    [string]$ModuleRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$drawingAssemblies = @([System.Drawing.Bitmap].Assembly.Location)
$drawingAssemblies += [System.Drawing.Bitmap].Assembly.GetReferencedAssemblies() | ForEach-Object {
    [Reflection.Assembly]::Load($_).Location
}
$drawingAssemblies = $drawingAssemblies | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Unique
Add-Type -ReferencedAssemblies $drawingAssemblies -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

public static class ReignWarCouncilPanelFrameComposer
{
    private const int OuterLeft = 34;
    private const int OuterTop = 46;
    private const int OuterRight = 1502;
    private const int OuterBottom = 970;
    private const int InnerLeft = 126;
    private const int InnerTop = 139;
    private const int InnerRight = 1422;
    private const int InnerBottom = 891;

    public static Bitmap LoadWithTrueTransparency(string path)
    {
        using (Bitmap source = new Bitmap(path))
        {
            Bitmap result = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
            using (Graphics graphics = Graphics.FromImage(result))
            {
                graphics.CompositingMode = CompositingMode.SourceCopy;
                graphics.DrawImageUnscaled(source, 0, 0);
            }

            Rectangle bounds = new Rectangle(0, 0, result.Width, result.Height);
            BitmapData data = result.LockBits(bounds, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
            try
            {
                int bytes = Math.Abs(data.Stride) * data.Height;
                byte[] pixels = new byte[bytes];
                Marshal.Copy(data.Scan0, pixels, 0, bytes);
                for (int y = 0; y < data.Height; y++)
                {
                    int row = y * data.Stride;
                    for (int x = 0; x < data.Width; x++)
                    {
                        int index = row + x * 4;
                        int blue = pixels[index];
                        int green = pixels[index + 1];
                        int red = pixels[index + 2];
                        int maximum = Math.Max(red, Math.Max(green, blue));
                        int minimum = Math.Min(red, Math.Min(green, blue));
                        bool generatedCheckerboard = minimum >= 215 && maximum - minimum <= 14;
                        pixels[index + 3] = generatedCheckerboard ? (byte)0 : (byte)255;
                    }
                }
                Marshal.Copy(pixels, 0, data.Scan0, bytes);
            }
            finally
            {
                result.UnlockBits(data);
            }
            return result;
        }
    }

    public static Bitmap CreateFrame(Bitmap source, int width, int height, int border)
    {
        Bitmap result = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using (Graphics graphics = Graphics.FromImage(result))
        {
            Configure(graphics);
            DrawFrame(graphics, source, new Rectangle(0, 0, width, height), border);
        }
        return result;
    }

    public static Bitmap CreateOverlay(Bitmap source)
    {
        Bitmap result = new Bitmap(1920, 1080, PixelFormat.Format32bppArgb);
        using (Graphics graphics = Graphics.FromImage(result))
        {
            Configure(graphics);
            DrawFrame(graphics, source, new Rectangle(1012, 90, 385, 252), 10);
            DrawFrame(graphics, source, new Rectangle(1012, 354, 385, 298), 10);
            DrawFrame(graphics, source, new Rectangle(1012, 664, 385, 92), 9);
            DrawFrame(graphics, source, new Rectangle(1012, 768, 385, 246), 10);
            DrawFrame(graphics, source, new Rectangle(1412, 78, 492, 988), 12);
            DrawFrame(graphics, source, new Rectangle(1424, 832, 468, 222), 10);
        }
        return result;
    }

    public static void DrawFrame(Graphics graphics, Bitmap source, Rectangle target, int border)
    {
        int destinationBorder = Math.Max(4, Math.Min(border, Math.Min(target.Width, target.Height) / 4));
        Rectangle topLeft = new Rectangle(OuterLeft, OuterTop, InnerLeft - OuterLeft, InnerTop - OuterTop);
        Rectangle top = new Rectangle(InnerLeft, OuterTop, InnerRight - InnerLeft, InnerTop - OuterTop);
        Rectangle topRight = new Rectangle(InnerRight, OuterTop, OuterRight - InnerRight, InnerTop - OuterTop);
        Rectangle left = new Rectangle(OuterLeft, InnerTop, InnerLeft - OuterLeft, InnerBottom - InnerTop);
        Rectangle right = new Rectangle(InnerRight, InnerTop, OuterRight - InnerRight, InnerBottom - InnerTop);
        Rectangle bottomLeft = new Rectangle(OuterLeft, InnerBottom, InnerLeft - OuterLeft, OuterBottom - InnerBottom);
        Rectangle bottom = new Rectangle(InnerLeft, InnerBottom, InnerRight - InnerLeft, OuterBottom - InnerBottom);
        Rectangle bottomRight = new Rectangle(InnerRight, InnerBottom, OuterRight - InnerRight, OuterBottom - InnerBottom);

        Draw(graphics, source, new Rectangle(target.Left, target.Top, destinationBorder, destinationBorder), topLeft);
        Draw(graphics, source, new Rectangle(target.Left + destinationBorder, target.Top,
            target.Width - destinationBorder * 2, destinationBorder), top);
        Draw(graphics, source, new Rectangle(target.Right - destinationBorder, target.Top,
            destinationBorder, destinationBorder), topRight);
        Draw(graphics, source, new Rectangle(target.Left, target.Top + destinationBorder,
            destinationBorder, target.Height - destinationBorder * 2), left);
        Draw(graphics, source, new Rectangle(target.Right - destinationBorder, target.Top + destinationBorder,
            destinationBorder, target.Height - destinationBorder * 2), right);
        Draw(graphics, source, new Rectangle(target.Left, target.Bottom - destinationBorder,
            destinationBorder, destinationBorder), bottomLeft);
        Draw(graphics, source, new Rectangle(target.Left + destinationBorder, target.Bottom - destinationBorder,
            target.Width - destinationBorder * 2, destinationBorder), bottom);
        Draw(graphics, source, new Rectangle(target.Right - destinationBorder, target.Bottom - destinationBorder,
            destinationBorder, destinationBorder), bottomRight);
    }

    private static void Configure(Graphics graphics)
    {
        graphics.Clear(Color.Transparent);
        graphics.CompositingMode = CompositingMode.SourceOver;
        graphics.CompositingQuality = CompositingQuality.HighQuality;
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.SmoothingMode = SmoothingMode.HighQuality;
    }

    private static void Draw(Graphics graphics, Bitmap source, Rectangle destination, Rectangle sourceRectangle)
    {
        if (destination.Width <= 0 || destination.Height <= 0) return;
        graphics.DrawImage(source, destination, sourceRectangle, GraphicsUnit.Pixel);
    }
}
'@

$sourcePath = Join-Path $ModuleRoot 'artwork\war-council-map-sources\reign-war-council-panel-frame-v1-imagegen.png'
$runtimeRoot = Join-Path $ModuleRoot 'GUI\SpriteParts\ui_reignbeta_war_council'
$framePath = Join-Path $runtimeRoot 'reign_war_council_panel_frame.png'
$tallFramePath = Join-Path $runtimeRoot 'reign_war_council_panel_frame_tall.png'
$overlayPath = Join-Path $runtimeRoot 'reign_war_council_panel_frame_overlay.png'

if (-not (Test-Path -LiteralPath $sourcePath)) {
    throw "Image-generated War Council panel-frame source was not found: $sourcePath"
}
New-Item -ItemType Directory -Path $runtimeRoot -Force | Out-Null

$source = [ReignWarCouncilPanelFrameComposer]::LoadWithTrueTransparency($sourcePath)
try {
    $frame = [ReignWarCouncilPanelFrameComposer]::CreateFrame($source, 760, 360, 24)
    try { $frame.Save($framePath, [System.Drawing.Imaging.ImageFormat]::Png) }
    finally { $frame.Dispose() }

    $tallFrame = [ReignWarCouncilPanelFrameComposer]::CreateFrame($source, 458, 650, 22)
    try { $tallFrame.Save($tallFramePath, [System.Drawing.Imaging.ImageFormat]::Png) }
    finally { $tallFrame.Dispose() }

    $overlay = [ReignWarCouncilPanelFrameComposer]::CreateOverlay($source)
    try { $overlay.Save($overlayPath, [System.Drawing.Imaging.ImageFormat]::Png) }
    finally { $overlay.Dispose() }
}
finally {
    $source.Dispose()
}

foreach ($path in @($framePath, $tallFramePath, $overlayPath)) {
    $bitmap = [System.Drawing.Bitmap]::FromFile($path)
    try {
        if ($bitmap.PixelFormat -ne [System.Drawing.Imaging.PixelFormat]::Format32bppArgb) {
            throw "Generated frame does not preserve 32-bit alpha: $path"
        }
        $center = $bitmap.GetPixel([int]($bitmap.Width / 2), [int]($bitmap.Height / 2))
        if ($center.A -ne 0) { throw "Generated frame center is not transparent: $path" }
    }
    finally { $bitmap.Dispose() }
    Write-Output ((Resolve-Path -LiteralPath $path).Path + '  ' + (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash)
}
