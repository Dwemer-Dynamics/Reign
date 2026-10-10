param([string]$ModuleRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$source = Join-Path $ModuleRoot 'artwork\ui-modern-style-kit\generated-sources\outfit-change\shell.png'
$destination = Join-Path $ModuleRoot 'GUI\SpriteParts\ui_reignbeta_individual\reign_outfit_change_shell.png'
$assemblies = @([System.Drawing.Bitmap].Assembly.Location, [System.Drawing.Point].Assembly.Location) | Select-Object -Unique
Add-Type -ReferencedAssemblies $assemblies -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Imaging;
public static class OutfitShellMaterializer {
 public static int Run(string source, string destination) {
  using (var input = new Bitmap(source)) using (var output = new Bitmap(input.Width, input.Height, PixelFormat.Format32bppArgb)) {
   if (input.Width != 1672 || input.Height != 941) throw new Exception("Unexpected imagegen canvas.");
   int cleared = 0;
   for (int y = 0; y < input.Height; y++) for (int x = 0; x < input.Width; x++) {
    var p = input.GetPixel(x,y);
    // Materialize the two generated window interiors only. Gold framing is never cut.
    bool window = y >= 138 && y <= 725 && ((x >= 269 && x <= 733) || (x >= 940 && x <= 1406));
    if (window && p.R > 100 && Math.Abs(p.R-p.G) < 16 && Math.Abs(p.G-p.B) < 16) {
     output.SetPixel(x,y,Color.FromArgb(0,0,0,0)); cleared++; continue;
    }
    output.SetPixel(x,y,p);
   }
   output.Save(destination, ImageFormat.Png);
   return cleared;
  }
 }
}
'@
$cleared = [OutfitShellMaterializer]::Run($source, $destination)
# Reuse the approved complete action plate without changing its aspect or artwork.
$chat = [System.Drawing.Bitmap]::new((Join-Path $ModuleRoot 'GUI\SpriteParts\ui_reignbeta_individual\reign_individual_chat_modern_shell.png'))
$action = $chat.Clone([System.Drawing.Rectangle]::new(624,182,422,62), [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
try { $action.Save((Join-Path $ModuleRoot 'GUI\SpriteParts\ui_reignbeta_individual\reign_outfit_action_button.png'), [System.Drawing.Imaging.ImageFormat]::Png) }
finally { $action.Dispose(); $chat.Dispose() }
Copy-Item -LiteralPath $destination -Destination (Join-Path $ModuleRoot 'artwork\ui-modern-style-kit\generated-sources\outfit-change\shell-alpha.png') -Force
# Compose the existing approved dialog on the runtime backdrop for a fixed-art reference.
$dialog = [System.Drawing.Bitmap]::new((Join-Path $ModuleRoot 'GUI\SpriteParts\ui_reignbeta_individual\reign_pregnancy_warning.png'))
$canvas = [System.Drawing.Bitmap]::new(1672,941,[System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$graphics = [System.Drawing.Graphics]::FromImage($canvas)
try {
    $graphics.Clear([System.Drawing.Color]::FromArgb(255,3,3,3))
    $graphics.DrawImage($dialog,[System.Drawing.Rectangle]::new(386,133,900,675))
    $canvas.Save((Join-Path $ModuleRoot 'artwork\ui-modern-style-kit\generated-sources\outfit-change\input-reference.png'),[System.Drawing.Imaging.ImageFormat]::Png)
}
finally { $graphics.Dispose(); $canvas.Dispose(); $dialog.Dispose() }
[pscustomobject]@{ Source=$source; Destination=$destination; TransparentPixels=$cleared; Sha256=(Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash.ToLowerInvariant() } | ConvertTo-Json
