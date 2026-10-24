param([Parameter(Mandatory=$true)][string]$EvidencePath)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$drawingAssemblies = @([System.Drawing.Bitmap].Assembly.Location, [System.Drawing.Point].Assembly.Location) | Select-Object -Unique
Add-Type -ReferencedAssemblies $drawingAssemblies -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
public static class ReignNewsReadingArt {
    public static int Compose(string beforePath, string fillPath, string outputPath) {
        using (var before = new Bitmap(beforePath))
        using (var fill = new Bitmap(fillPath)) {
            if (before.Width != 1672 || before.Height != 941 || fill.Size != before.Size)
                throw new InvalidOperationException("The approved shell and generated fill must be exactly 1672x941.");
            var bounds = new Rectangle(0, 0, 1672, 941);
            using (var output = before.Clone(bounds, PixelFormat.Format32bppArgb))
            using (var pixels = fill.Clone(bounds, PixelFormat.Format32bppArgb)) {
                var od = output.LockBits(bounds, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
                var fd = pixels.LockBits(bounds, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                try {
                    var row = new byte[782 * 4];
                    for (int y = 232; y < 763; y++) {
                        Marshal.Copy(IntPtr.Add(fd.Scan0, y * fd.Stride + 444 * 4), row, 0, row.Length);
                        Marshal.Copy(row, 0, IntPtr.Add(od.Scan0, y * od.Stride + 444 * 4), row.Length);
                    }
                } finally { output.UnlockBits(od); pixels.UnlockBits(fd); }
                int unchanged = 0;
                for (int y = 0; y < 941; y++) for (int x = 0; x < 1672; x++) {
                    if (x >= 444 && x < 1226 && y >= 232 && y < 763) continue;
                    if (before.GetPixel(x,y) != output.GetPixel(x,y)) throw new InvalidOperationException("Fixed pixel changed.");
                    unchanged++;
                }
                output.Save(outputPath, ImageFormat.Png);
                return unchanged;
            }
        }
    }
}
'@
$styleRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$moduleRoot = (Resolve-Path (Join-Path $styleRoot '..\..')).Path
$source = Join-Path $PSScriptRoot 'source.png'
if ((Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash.ToLowerInvariant() -ne '2381599406bcf8db9c0c6288745afaa3918eff0e766bcc3036cc6aa35d760d95') { throw 'Unexpected generated reading fill.' }
$previous = Join-Path $PSScriptRoot 'previous-runtime-shell.png'
$priorReference = Join-Path $styleRoot 'approved-references\diplomacy-announcement.png'
if ((Get-FileHash -LiteralPath $previous -Algorithm SHA256).Hash.ToLowerInvariant() -ne 'b3dc408d3a2f97a8e06b1f387387b5af7be68b1a62e05807424c98848b00e70e') { throw 'Unexpected prior runtime shell.' }
if ((Get-FileHash -LiteralPath $priorReference -Algorithm SHA256).Hash.ToLowerInvariant() -ne '6ee8dd4623033e226b50815b8fbd95b269673b908942edc7061728398674dd12') { throw 'Unexpected prior approved reference.' }
$runtime = Join-Path $moduleRoot 'GUI\SpriteParts\ui_reignbeta_diplomacy\reign_diplomacy_modern_shell.png'
$reference = Join-Path $styleRoot 'approved-references\diplomacy-announcement-readable.png'
$runtimeFixedPixels = [ReignNewsReadingArt]::Compose($previous, $source, $runtime)
$referenceFixedPixels = [ReignNewsReadingArt]::Compose($priorReference, $source, $reference)
$evidence = [ordered]@{
    schema='reign-news-reading-art-v1'; generatedWith='OpenAI built-in image generation'; updatedUtc=[DateTime]::UtcNow.ToString('o')
    editedInterior=@{x=444;y=232;width=782;height=531}; fixedPixelsIdentical=$true
    runtimeFixedPixelCount=$runtimeFixedPixels; referenceFixedPixelCount=$referenceFixedPixels
    sourceSha256=(Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash.ToLowerInvariant()
    runtimeSha256=(Get-FileHash -LiteralPath $runtime -Algorithm SHA256).Hash.ToLowerInvariant()
    referenceSha256=(Get-FileHash -LiteralPath $reference -Algorithm SHA256).Hash.ToLowerInvariant()
    nativeAcceptance=$false
}
$evidence | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $EvidencePath -Encoding utf8
$evidence | ConvertTo-Json -Depth 6
