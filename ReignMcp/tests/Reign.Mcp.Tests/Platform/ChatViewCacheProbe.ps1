param([Parameter(Mandatory)][string]$Source, [Parameter(Mandatory)][string]$Scratch)
$ErrorActionPreference = 'Stop'
Add-Type -TypeDefinition ([IO.File]::ReadAllText($Source)) -WarningAction SilentlyContinue
$writer = [AppDomain]::CurrentDomain.GetAssemblies() | ForEach-Object { $_.GetType('ReignBeta.Integration.ReignChatViewCacheFile', $false) } | Where-Object { $_ } | Select-Object -First 1
$flags = [Reflection.BindingFlags]'Static,NonPublic'
$write = $writer.GetMethod('WriteAtomic', $flags)
$temporaryPath = $writer.GetMethod('TemporaryPath', $flags)
if (-not $write -or -not $temporaryPath) { throw 'Actual client cache writer was not loaded.' }
# Strongly typed bridge methods unwrap PowerShell values before reflection.
$parallelSource = @'
using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
public static class ViewCacheParallelProbe {
    public static string Temporary(Type writer, string path) {
        return (string)writer.GetMethod("TemporaryPath", BindingFlags.Static | BindingFlags.NonPublic)
            .Invoke(null, new object[] { path });
    }
    public static void Write(Type writer, string path, byte[] bytes) {
        writer.GetMethod("WriteAtomic", BindingFlags.Static | BindingFlags.NonPublic)
            .Invoke(null, new object[] { path, bytes });
    }
    public static void Run(Type writer, string path) {
        Parallel.For(0, 8, i => Write(writer, path, new byte[] { (byte)i, (byte)i, (byte)i }));
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length != 3 || bytes[0] != bytes[1] || bytes[1] != bytes[2]) throw new Exception("A partial concurrent write escaped.");
    }
}
'@
Add-Type -TypeDefinition $parallelSource
$paddingLength = 176 - $Scratch.Length - 1
if ($paddingLength -lt 1 -or $paddingLength -gt 240) { throw 'Invalid bounded legacy path fixture.' }
$folder = Join-Path $Scratch ('d' * $paddingLength)
$image = Join-Path $folder (('b' * 64) + '.png')
$inline = Join-Path $folder (('b' * 64) + '_inline.png')
$caption = [IO.Path]::ChangeExtension($image, '.txt')
$oldTemporary = $image + '.' + ('c' * 32) + '.tmp'
$newTemporary = [ViewCacheParallelProbe]::Temporary($writer, [string]$image)
if ($image.Length -ne 245 -or $inline.Length -ne 252 -or $oldTemporary.Length -ne 282) { throw 'The failing Steam-path geometry was not reproduced.' }
if ($newTemporary.Length -ge 260 -or [IO.Path]::GetDirectoryName($newTemporary) -ne $folder) { throw 'Temporary image must remain a short sibling for atomic replacement.' }
if ([IO.Directory]::Exists($folder)) { throw 'The missing-directory case must start empty.' }
foreach ($path in @($image, $inline, $caption)) {
    [ViewCacheParallelProbe]::Write($writer, [string]$path, [byte[]](1,2,3))
    if ([Convert]::ToBase64String([IO.File]::ReadAllBytes($path)) -ne 'AQID') { throw 'Cache creation failed.' }
    [ViewCacheParallelProbe]::Write($writer, [string]$path, [byte[]](4,5,6))
    if ([Convert]::ToBase64String([IO.File]::ReadAllBytes($path)) -ne 'BAUG') { throw 'Atomic cache replacement failed.' }
}
# The production writer's lock is exercised by the in-process thread-pool probe.
[ViewCacheParallelProbe]::Run($writer, $image)
if ([IO.Directory]::GetFiles($folder, '*.tmp').Length -ne 0) { throw 'Temporary cache files leaked.' }
@{ok=$true; runtime=[Runtime.InteropServices.RuntimeInformation]::FrameworkDescription; imageLength=$image.Length; inlineLength=$inline.Length; oldTemporaryLength=$oldTemporary.Length; newTemporaryLength=$newTemporary.Length; operations=14} | ConvertTo-Json -Compress
