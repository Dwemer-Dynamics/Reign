param(
    [string]$ProcessName = 'Bannerlord',
    [Parameter(Mandatory = $true)][string]$OutputPath,
    [ValidateRange(0, 16384)][int]$ExpectedWidth = 0,
    [ValidateRange(0, 16384)][int]$ExpectedHeight = 0,
    [switch]$AllowOffscreenClientResize,
    [string]$SteamScreenshotRoot = ''
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class ReignWindowCaptureNative {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr hWnd, ref System.Drawing.Point point);
    [DllImport("user32.dll", SetLastError=true)] public static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll", SetLastError=true)] public static extern bool AdjustWindowRectEx(ref RECT rect, int style, bool menu, int exStyle);
    [DllImport("user32.dll", SetLastError=true)] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll", SetLastError=true)] public static extern bool PrintWindow(IntPtr hWnd, IntPtr hdcBlt, uint flags);
    [DllImport("user32.dll", SetLastError=true)] public static extern bool SetProcessDpiAwarenessContext(IntPtr dpiContext);
    [DllImport("user32.dll", SetLastError=true)] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr dpiContext);
    [DllImport("user32.dll")] public static extern void keybd_event(byte virtualKey, byte scanCode, uint flags, UIntPtr extraInfo);
    [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern void SwitchToThisWindow(IntPtr hWnd, bool fAltTab);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
    [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool attach);
    [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
}
'@ -ReferencedAssemblies System.Drawing,System.Drawing.Primitives

if (($ExpectedWidth -gt 0) -xor ($ExpectedHeight -gt 0)) { throw 'ExpectedWidth and ExpectedHeight must be supplied together.' }
$perMonitorV2Context = [IntPtr](-4)
[ReignWindowCaptureNative]::SetProcessDpiAwarenessContext($perMonitorV2Context) | Out-Null
$previousThreadDpiContext = [ReignWindowCaptureNative]::SetThreadDpiAwarenessContext($perMonitorV2Context)

try {
$process = Get-Process -ErrorAction Stop |
    Where-Object { $_.MainWindowHandle -ne 0 -and ($_.ProcessName -like "*$ProcessName*" -or $_.MainWindowTitle -like "*$ProcessName*") } |
    Select-Object -First 1
if (-not $process) { throw "No visible window matched '$ProcessName'." }

function Request-Foreground {
    param([Parameter(Mandatory = $true)][IntPtr]$TargetHandle)
    $foregroundHandle = [ReignWindowCaptureNative]::GetForegroundWindow()
    $foregroundProcessId = [uint32]0
    $targetProcessId = [uint32]0
    $foregroundThread = if ($foregroundHandle.ToInt64() -ne 0) { [ReignWindowCaptureNative]::GetWindowThreadProcessId($foregroundHandle, [ref]$foregroundProcessId) } else { [uint32]0 }
    $targetThread = [ReignWindowCaptureNative]::GetWindowThreadProcessId($TargetHandle, [ref]$targetProcessId)
    $currentThread = [ReignWindowCaptureNative]::GetCurrentThreadId()
    $attachedForeground = $false
    $attachedTarget = $false
    try {
        if ($foregroundThread -ne 0 -and $foregroundThread -ne $currentThread) {
            $attachedForeground = [ReignWindowCaptureNative]::AttachThreadInput($currentThread, $foregroundThread, $true)
        }
        if ($targetThread -ne 0 -and $targetThread -ne $currentThread) {
            $attachedTarget = [ReignWindowCaptureNative]::AttachThreadInput($currentThread, $targetThread, $true)
        }
        [ReignWindowCaptureNative]::ShowWindowAsync($TargetHandle, 9) | Out-Null
        [ReignWindowCaptureNative]::BringWindowToTop($TargetHandle) | Out-Null
        [ReignWindowCaptureNative]::SetForegroundWindow($TargetHandle) | Out-Null
        [ReignWindowCaptureNative]::SwitchToThisWindow($TargetHandle, $true)
    }
    finally {
        if ($attachedTarget) { [ReignWindowCaptureNative]::AttachThreadInput($currentThread, $targetThread, $false) | Out-Null }
        if ($attachedForeground) { [ReignWindowCaptureNative]::AttachThreadInput($currentThread, $foregroundThread, $false) | Out-Null }
    }
}

function Resolve-SteamScreenshotRoot {
    if (-not [string]::IsNullOrWhiteSpace($SteamScreenshotRoot)) {
        $explicit = [System.IO.Path]::GetFullPath($SteamScreenshotRoot)
        if (-not (Test-Path -LiteralPath $explicit -PathType Container)) { throw "Steam screenshot root is missing: $explicit" }
        return $explicit
    }

    $steamPath = ''
    try { $steamPath = [string](Get-ItemProperty -LiteralPath 'HKCU:\Software\Valve\Steam' -Name SteamPath -ErrorAction Stop).SteamPath } catch { }
    if ([string]::IsNullOrWhiteSpace($steamPath)) { throw 'SteamScreenshotRoot is required because the Steam installation path was not found.' }
    $userDataRoot = Join-Path ([System.IO.Path]::GetFullPath($steamPath)) 'userdata'
    $candidates = @(Get-ChildItem -LiteralPath $userDataRoot -Directory -ErrorAction SilentlyContinue | ForEach-Object {
        $candidate = Join-Path $_.FullName '760\remote\261550\screenshots'
        if (Test-Path -LiteralPath $candidate -PathType Container) { Get-Item -LiteralPath $candidate }
    })
    if ($candidates.Count -ne 1) { throw "Expected one Steam Bannerlord screenshot root; found $($candidates.Count). Supply SteamScreenshotRoot explicitly." }
    return $candidates[0].FullName
}

# CopyFromScreen is intentionally used for the native DirectX client, but it
# records whatever is actually visible at those pixels. Retain the user's prior
# foreground window, acquire the exact target through attached input threads,
# and prove ownership before copying pixels.
$restoreForegroundHandle = [ReignWindowCaptureNative]::GetForegroundWindow()
if ($restoreForegroundHandle.ToInt64() -eq $process.MainWindowHandle.ToInt64()) { $restoreForegroundHandle = [IntPtr]::Zero }
$foregroundHandle = [ReignWindowCaptureNative]::GetForegroundWindow()
for ($attempt = 1; $attempt -le 20 -and $foregroundHandle.ToInt64() -ne $process.MainWindowHandle.ToInt64(); $attempt++) {
    Request-Foreground -TargetHandle $process.MainWindowHandle
    Start-Sleep -Milliseconds 250
    $foregroundHandle = [ReignWindowCaptureNative]::GetForegroundWindow()
}
if ($foregroundHandle.ToInt64() -ne $process.MainWindowHandle.ToInt64()) {
    $foregroundOwner = Get-Process -ErrorAction SilentlyContinue |
        Where-Object { $_.MainWindowHandle.ToInt64() -eq $foregroundHandle.ToInt64() } |
        Select-Object -First 1
    $foregroundDescription = if ($foregroundOwner) { "$($foregroundOwner.ProcessName): $($foregroundOwner.MainWindowTitle)" } else { "handle $($foregroundHandle.ToInt64())" }
    throw "Could not make '$($process.MainWindowTitle)' the foreground capture target; foreground is '$foregroundDescription'."
}

$rect = New-Object ReignWindowCaptureNative+RECT
if (-not [ReignWindowCaptureNative]::GetClientRect($process.MainWindowHandle, [ref]$rect)) { throw 'GetClientRect failed.' }
$width = $rect.Right - $rect.Left
$height = $rect.Bottom - $rect.Top
$observedClientWidth = $width
$observedClientHeight = $height
$virtualizedClientBounds = $false
$resizedForCapture = $false
if ($ExpectedWidth -gt 0 -and ($width -ne $ExpectedWidth -or $height -ne $ExpectedHeight)) {
    if (-not $AllowOffscreenClientResize) {
        throw "Bannerlord client is ${width}x${height}, expected ${ExpectedWidth}x${ExpectedHeight}; off-screen resize was not authorized."
    }

    $captureHandle = $process.MainWindowHandle
    $style = [ReignWindowCaptureNative]::GetWindowLong($captureHandle, -16)
    $exStyle = [ReignWindowCaptureNative]::GetWindowLong($captureHandle, -20)
    $outer = New-Object ReignWindowCaptureNative+RECT
    $outer.Left = 0
    $outer.Top = 0
    $outer.Right = $ExpectedWidth
    $outer.Bottom = $ExpectedHeight
    if (-not [ReignWindowCaptureNative]::AdjustWindowRectEx([ref]$outer, $style, $false, $exStyle)) {
        throw 'AdjustWindowRectEx failed while preparing an exact off-screen client capture.'
    }
    $outerWidth = $outer.Right - $outer.Left
    $outerHeight = $outer.Bottom - $outer.Top
    if (-not [ReignWindowCaptureNative]::SetWindowPos($captureHandle, [IntPtr]::Zero, 0, 0, $outerWidth, $outerHeight, 0x0024)) {
        throw 'SetWindowPos failed while preparing an exact off-screen client capture.'
    }

    $clientRectObserved = $false
    for ($attempt = 1; $attempt -le 40; $attempt++) {
        Start-Sleep -Milliseconds 100
        try {
            $candidateProcess = Get-Process -Id $process.Id -ErrorAction Stop
            $candidateProcess.Refresh()
        }
        catch {
            throw "Bannerlord process $($process.Id) exited during off-screen resize."
        }

        $candidateHandle = $candidateProcess.MainWindowHandle
        if ($candidateHandle.ToInt64() -eq 0) { continue }
        if ($candidateHandle.ToInt64() -ne $captureHandle.ToInt64()) {
            # Bannerlord may recreate its top-level window while changing the
            # DirectX render target. Reacquire only the same verified process,
            # then apply the requested client geometry to its replacement.
            $process = $candidateProcess
            $captureHandle = $candidateHandle
            $style = [ReignWindowCaptureNative]::GetWindowLong($captureHandle, -16)
            $exStyle = [ReignWindowCaptureNative]::GetWindowLong($captureHandle, -20)
            $outer = New-Object ReignWindowCaptureNative+RECT
            $outer.Left = 0
            $outer.Top = 0
            $outer.Right = $ExpectedWidth
            $outer.Bottom = $ExpectedHeight
            if (-not [ReignWindowCaptureNative]::AdjustWindowRectEx([ref]$outer, $style, $false, $exStyle)) {
                throw 'AdjustWindowRectEx failed after Bannerlord recreated its capture window.'
            }
            $outerWidth = $outer.Right - $outer.Left
            $outerHeight = $outer.Bottom - $outer.Top
            if (-not [ReignWindowCaptureNative]::SetWindowPos($captureHandle, [IntPtr]::Zero, 0, 0, $outerWidth, $outerHeight, 0x0024)) {
                throw 'SetWindowPos failed after Bannerlord recreated its capture window.'
            }
            Request-Foreground -TargetHandle $captureHandle
            continue
        }

        $process = $candidateProcess
        if (-not [ReignWindowCaptureNative]::GetClientRect($captureHandle, [ref]$rect)) { continue }
        $clientRectObserved = $true
        $width = $rect.Right - $rect.Left
        $height = $rect.Bottom - $rect.Top
        if ($width -eq $ExpectedWidth -and $height -eq $ExpectedHeight) { break }
    }
    if (-not $clientRectObserved) { throw 'GetClientRect did not recover after Bannerlord recreated its off-screen capture window.' }
    $observedClientWidth = $width
    $observedClientHeight = $height
    if ($width -ne $ExpectedWidth -or $height -ne $ExpectedHeight) {
        # Bannerlord can keep an engine render target larger than the desktop
        # while User32 exposes DPI-virtualized or monitor-clamped client bounds.
        # The caller has already verified the exact Gauntlet physical snapshot;
        # allocate that requested render-target size for PrintWindow and retain
        # the observed discrepancy for review instead of mislabeling it.
        $virtualizedClientBounds = $true
        $width = $ExpectedWidth
        $height = $ExpectedHeight
    }
    $resizedForCapture = $true
    Request-Foreground -TargetHandle $process.MainWindowHandle
    Start-Sleep -Milliseconds 500
}

$origin = New-Object System.Drawing.Point 0, 0
if (-not [ReignWindowCaptureNative]::ClientToScreen($process.MainWindowHandle, [ref]$origin)) { throw 'ClientToScreen failed.' }
$dpi = [ReignWindowCaptureNative]::GetDpiForWindow($process.MainWindowHandle)
if ($width -lt 1 -or $height -lt 1) { throw "Invalid client size ${width}x${height}." }

$resolved = [System.IO.Path]::GetFullPath($OutputPath)
[System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($resolved)) | Out-Null
$bitmap = New-Object System.Drawing.Bitmap $width, $height, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$virtualScreen = [System.Windows.Forms.SystemInformation]::VirtualScreen
$extendsOffscreen = $origin.X -lt $virtualScreen.Left -or $origin.Y -lt $virtualScreen.Top -or
    ($origin.X + $width) -gt $virtualScreen.Right -or ($origin.Y + $height) -gt $virtualScreen.Bottom
$usePrintWindow = $resizedForCapture -or $extendsOffscreen
$useSteamBackbuffer = $virtualizedClientBounds
$captureMode = if ($useSteamBackbuffer) { 'verified-foreground-steam-backbuffer' } elseif ($usePrintWindow) { 'verified-foreground-print-window-client-area' } else { 'verified-foreground-client-area' }
$printWindowFlags = if ($usePrintWindow) { 3 } else { 0 }
$steamOriginalPath = ''
$steamSourcePath = ''
$steamSourceSha256 = ''
$steamOriginalRemoved = $false
$steamThumbnailPath = ''
$steamThumbnailRemoved = $false
$steamTriggerAttempts = 0
try {
    if ($useSteamBackbuffer) {
        $steamRoot = Resolve-SteamScreenshotRoot
        $before = @{}
        Get-ChildItem -LiteralPath $steamRoot -File -ErrorAction SilentlyContinue | ForEach-Object { $before[$_.FullName] = $_.LastWriteTimeUtc }
        $triggeredUtc = [DateTime]::UtcNow
        $nextTriggerUtc = [DateTime]::MinValue
        $deadline = [DateTime]::UtcNow.AddSeconds(24)
        do {
            if ($steamTriggerAttempts -lt 3 -and [DateTime]::UtcNow -ge $nextTriggerUtc) {
                [ReignWindowCaptureNative]::keybd_event(0x7B, 0, 0, [UIntPtr]::Zero)
                Start-Sleep -Milliseconds 100
                [ReignWindowCaptureNative]::keybd_event(0x7B, 0, 2, [UIntPtr]::Zero)
                $steamTriggerAttempts++
                $nextTriggerUtc = [DateTime]::UtcNow.AddSeconds(6)
            }
            Start-Sleep -Milliseconds 250
            $steamCapture = Get-ChildItem -LiteralPath $steamRoot -File -ErrorAction SilentlyContinue |
                Where-Object { -not $before.ContainsKey($_.FullName) -and $_.LastWriteTimeUtc -ge $triggeredUtc.AddSeconds(-1) } |
                Sort-Object LastWriteTimeUtc -Descending |
                Select-Object -First 1
        } while (-not $steamCapture -and [DateTime]::UtcNow -lt $deadline)
        if (-not $steamCapture) { throw "Steam did not produce a new Bannerlord backbuffer screenshot after $steamTriggerAttempts bounded trigger attempts." }

        $steamOriginalPath = $steamCapture.FullName
        $steamSourceSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $steamOriginalPath).Hash.ToLowerInvariant()
        $stream = [System.IO.File]::Open($steamOriginalPath, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::ReadWrite)
        try {
            $steamImage = [System.Drawing.Image]::FromStream($stream)
            try {
                if ($steamImage.Width -ne $ExpectedWidth -or $steamImage.Height -ne $ExpectedHeight) {
                    throw "Steam backbuffer screenshot is $($steamImage.Width)x$($steamImage.Height), expected ${ExpectedWidth}x${ExpectedHeight}."
                }
                $graphics.DrawImageUnscaled($steamImage, 0, 0)
            }
            finally { $steamImage.Dispose() }
        }
        finally { $stream.Dispose() }
    }
    elseif ($usePrintWindow) {
        $deviceContext = $graphics.GetHdc()
        try {
            if (-not [ReignWindowCaptureNative]::PrintWindow($process.MainWindowHandle, $deviceContext, [uint32]$printWindowFlags)) {
                throw 'PrintWindow failed for the exact off-screen Bannerlord client.'
            }
        }
        finally {
            $graphics.ReleaseHdc($deviceContext)
        }
        $meaningfulSamples = 0
        $sampleStepX = [Math]::Max(1, [int][Math]::Floor($width / 120.0))
        $sampleStepY = [Math]::Max(1, [int][Math]::Floor($height / 80.0))
        for ($sampleY = 0; $sampleY -lt $height; $sampleY += $sampleStepY) {
            for ($sampleX = 0; $sampleX -lt $width; $sampleX += $sampleStepX) {
                $pixel = $bitmap.GetPixel($sampleX, $sampleY)
                if (($pixel.R + $pixel.G + $pixel.B) -gt 30) { $meaningfulSamples++ }
            }
        }
        if ($meaningfulSamples -lt 12) { throw 'PrintWindow returned an empty or effectively black Bannerlord client image.' }
    }
    else {
        $graphics.CopyFromScreen($origin, [System.Drawing.Point]::Empty, (New-Object System.Drawing.Size $width, $height))
    }
    $bitmap.Save($resolved, [System.Drawing.Imaging.ImageFormat]::Png)

    if ($useSteamBackbuffer) {
        $steamRootPrefix = $steamRoot.TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
        if (-not $steamOriginalPath.StartsWith($steamRootPrefix, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to remove a Steam screenshot outside the resolved Bannerlord screenshot root: $steamOriginalPath"
        }

        $steamSourcePath = [System.IO.Path]::ChangeExtension($resolved, '.steam-backbuffer.jpg')
        Copy-Item -LiteralPath $steamOriginalPath -Destination $steamSourcePath -Force
        $retainedSteamHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $steamSourcePath).Hash.ToLowerInvariant()
        if (-not [string]::Equals($retainedSteamHash, $steamSourceSha256, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Retained Steam backbuffer hash mismatch: $steamSourcePath"
        }

        $steamThumbnailPath = Join-Path (Join-Path $steamRoot 'thumbnails') ([System.IO.Path]::GetFileName($steamOriginalPath))
        Remove-Item -LiteralPath $steamOriginalPath -Force
        $steamOriginalRemoved = -not (Test-Path -LiteralPath $steamOriginalPath)
        if (-not $steamOriginalRemoved) { throw "Exact run-created Steam screenshot could not be removed: $steamOriginalPath" }
        if (Test-Path -LiteralPath $steamThumbnailPath -PathType Leaf) {
            Remove-Item -LiteralPath $steamThumbnailPath -Force
            $steamThumbnailRemoved = -not (Test-Path -LiteralPath $steamThumbnailPath)
            if (-not $steamThumbnailRemoved) { throw "Exact run-created Steam thumbnail could not be removed: $steamThumbnailPath" }
        }
    }
} finally {
    $graphics.Dispose()
    $bitmap.Dispose()
    if ($restoreForegroundHandle.ToInt64() -ne 0) {
        [ReignWindowCaptureNative]::ShowWindowAsync($restoreForegroundHandle, 9) | Out-Null
        [ReignWindowCaptureNative]::BringWindowToTop($restoreForegroundHandle) | Out-Null
        [ReignWindowCaptureNative]::SetForegroundWindow($restoreForegroundHandle) | Out-Null
        [ReignWindowCaptureNative]::SwitchToThisWindow($restoreForegroundHandle, $true)
    }
}

$metadata = [ordered]@{
    schema = 'reign-ui-window-capture-v1'
    processId = $process.Id
    processName = $process.ProcessName
    windowTitle = $process.MainWindowTitle
    x = $origin.X
    y = $origin.Y
    width = $width
    height = $height
    dpi = $dpi
    dpiScale = if ($dpi) { $dpi / 96.0 } else { 1.0 }
    perMonitorDpiAware = $true
    captureMode = $captureMode
    expectedWidth = $ExpectedWidth
    expectedHeight = $ExpectedHeight
    resizedForCapture = $resizedForCapture
    observedClientWidth = $observedClientWidth
    observedClientHeight = $observedClientHeight
    virtualizedClientBounds = $virtualizedClientBounds
    extendedOffscreen = $extendsOffscreen
    printWindowFlags = $printWindowFlags
    steamOriginalPath = $steamOriginalPath
    steamOriginalRemoved = $steamOriginalRemoved
    steamSourcePath = $steamSourcePath
    steamSourceSha256 = $steamSourceSha256
    steamThumbnailPath = $steamThumbnailPath
    steamThumbnailRemoved = $steamThumbnailRemoved
    steamTriggerAttempts = $steamTriggerAttempts
    capturedUtc = [DateTime]::UtcNow.ToString('o')
    path = $resolved
    sha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $resolved).Hash.ToLowerInvariant()
}
$metadataPath = $resolved + '.json'
$metadata['metadataPath'] = $metadataPath
$metadata | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath $metadataPath -Encoding UTF8
$metadata | ConvertTo-Json -Depth 3
}
finally {
    if ($previousThreadDpiContext.ToInt64() -ne 0) {
        [ReignWindowCaptureNative]::SetThreadDpiAwarenessContext($previousThreadDpiContext) | Out-Null
    }
}
