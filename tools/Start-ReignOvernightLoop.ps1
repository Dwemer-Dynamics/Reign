param(
    [int]$MaxBatches = 1000000,
    [switch]$Resume,
    [switch]$NoGameRestart,
    [switch]$NoServerRestart,
    [switch]$NoAutoClickContinue,
    [int]$BatchTimeoutMinutes = 45
)

$ErrorActionPreference = "Stop"

$ModuleDir = "D:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord\Modules\ReignBeta"
$ServerExe = Join-Path $ModuleDir "server\app\ReignBetaServer.exe"
$ServerWorkDir = Join-Path $ModuleDir "server\app"
$LauncherShortcut = "C:\Users\speed\Desktop\Bannerlord.BLSE.Launcher - Shortcut.lnk"
$LogDir = Join-Path $ModuleDir "logs"
$CommandPath = Join-Path $LogDir "live-dialogue-overnight-command.json"
$StatusPath = Join-Path $LogDir "live-dialogue-overnight-status.json"
$SummaryScript = "C:\Users\speed\Documents\Bannerlord Events\tools\Summarize-LiveDialogueBeta.ps1"

function Ensure-LogDir {
    if (-not (Test-Path -LiteralPath $LogDir)) {
        New-Item -ItemType Directory -Path $LogDir | Out-Null
    }
}

function Stop-ReignServer {
    $procs = Get-CimInstance Win32_Process |
        Where-Object { ($_.ExecutablePath -eq $ServerExe) -or ($_.CommandLine -like "*ReignBetaServer.exe*") }
    foreach ($proc in $procs) {
        Write-Host "Stopping ReignBetaServer pid=$($proc.ProcessId)"
        Stop-Process -Id $proc.ProcessId -Force -ErrorAction SilentlyContinue
    }
}

function Start-ReignServer {
    if (-not (Test-Path -LiteralPath $ServerExe)) {
        throw "Server exe not found: $ServerExe"
    }

    Stop-ReignServer
    Write-Host "Starting visible ReignBeta server..."
    Start-Process -FilePath $ServerExe -WorkingDirectory $ServerWorkDir | Out-Null
    Start-Sleep -Seconds 4
}

function Stop-Bannerlord {
    $gameProcs = Get-CimInstance Win32_Process |
        Where-Object {
            ($_.Name -match "Bannerlord|TaleWorlds|BLSE") -and
            ($_.CommandLine -like "*Mount & Blade II Bannerlord*" -or $_.CommandLine -like "*Bannerlord*")
        }
    foreach ($proc in $gameProcs) {
        if ($proc.CommandLine -like "*ReignBetaServer.exe*") { continue }
        Write-Host "Stopping game/launcher pid=$($proc.ProcessId) name=$($proc.Name)"
        Stop-Process -Id $proc.ProcessId -Force -ErrorAction SilentlyContinue
    }
}

Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class ReignWin32 {
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left; public int Top; public int Right; public int Bottom; }
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int X, int Y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extraInfo);
    public const uint LEFTDOWN = 0x0002;
    public const uint LEFTUP = 0x0004;
}
"@

function Click-WindowFraction {
    param(
        [Parameter(Mandatory=$true)] [System.Diagnostics.Process]$Process,
        [double]$XFraction,
        [double]$YFraction
    )

    if ($Process.MainWindowHandle -eq [IntPtr]::Zero) { return $false }
    [ReignWin32]::SetForegroundWindow($Process.MainWindowHandle) | Out-Null
    Start-Sleep -Milliseconds 500
    $rect = New-Object ReignWin32+RECT
    if (-not [ReignWin32]::GetWindowRect($Process.MainWindowHandle, [ref]$rect)) { return $false }
    $x = [int]($rect.Left + (($rect.Right - $rect.Left) * $XFraction))
    $y = [int]($rect.Top + (($rect.Bottom - $rect.Top) * $YFraction))
    [ReignWin32]::SetCursorPos($x, $y) | Out-Null
    [ReignWin32]::mouse_event([ReignWin32]::LEFTDOWN, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 80
    [ReignWin32]::mouse_event([ReignWin32]::LEFTUP, 0, 0, 0, [UIntPtr]::Zero)
    return $true
}

function Start-BannerlordContinue {
    if (-not (Test-Path -LiteralPath $LauncherShortcut)) {
        throw "BLSE launcher shortcut not found: $LauncherShortcut"
    }

    Stop-Bannerlord
    Write-Host "Starting BLSE launcher..."
    Start-Process -FilePath $LauncherShortcut | Out-Null
    if ($NoAutoClickContinue) {
        Write-Host "Auto-click disabled. Click Continue manually."
        return
    }

    Start-Sleep -Seconds 10
    $launcher = Get-Process | Where-Object { $_.MainWindowTitle -like "*Mount*Blade*Bannerlord*" -or $_.ProcessName -like "*Launcher*" } | Select-Object -First 1
    if ($launcher) {
        Write-Host "Clicking Continue on launcher window..."
        Click-WindowFraction -Process $launcher -XFraction 0.52 -YFraction 0.85 | Out-Null
    } else {
        Write-Warning "Could not find launcher window to click Continue."
    }

    Start-Sleep -Seconds 25
    $game = Get-Process | Where-Object { $_.MainWindowTitle -like "*Mount*Blade*Bannerlord*" -and $_.ProcessName -notlike "*Launcher*" } | Select-Object -First 1
    if ($game) {
        Write-Host "Sending Esc to skip intro/loading overlays if focused."
        [ReignWin32]::SetForegroundWindow($game.MainWindowHandle) | Out-Null
        Add-Type -AssemblyName System.Windows.Forms
        [System.Windows.Forms.SendKeys]::SendWait("{ESC}")
    }
}

function Write-OvernightCommand {
    param([string]$Command)
    Ensure-LogDir
    $payload = [ordered]@{
        commandId = [guid]::NewGuid().ToString("N")
        command = $Command
        maxBatches = $MaxBatches
        issuedUtc = [DateTime]::UtcNow.ToString("o")
    }
    $payload | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $CommandPath -Encoding UTF8
    Write-Host "Wrote overnight command: $Command -> $CommandPath"
}

function Read-Status {
    if (-not (Test-Path -LiteralPath $StatusPath)) { return $null }
    try {
        return Get-Content -LiteralPath $StatusPath -Raw | ConvertFrom-Json
    } catch {
        return $null
    }
}

Ensure-LogDir

if (-not $Resume -and (Test-Path -LiteralPath $StatusPath)) {
    Remove-Item -LiteralPath $StatusPath -Force
    Write-Host "Cleared stale overnight status for fresh start: $StatusPath"
}

if (-not $NoServerRestart) { Start-ReignServer }
if (-not $NoGameRestart) { Start-BannerlordContinue }

Write-OvernightCommand -Command ($(if ($Resume) { "resume" } else { "start" }))

$deadline = (Get-Date).AddMinutes($BatchTimeoutMinutes)
Write-Host "Monitoring overnight status: $StatusPath"
while ($true) {
    Start-Sleep -Seconds 10
    $status = Read-Status
    if ($null -eq $status) {
        if ((Get-Date) -gt $deadline) {
            Write-Error "Timed out waiting for overnight status file."
            exit 3
        }
        Write-Host "Waiting for status file..."
        continue
    }

    Write-Host ("[{0}] {1}" -f $status.state, $status.message)

    if ($status.state -eq "paused_for_repair") {
        Write-Host "Batch failed and paused for repair. Stopping game/server so code can be rebuilt safely."
        if (-not $NoGameRestart) { Stop-Bannerlord }
        if (-not $NoServerRestart) { Stop-ReignServer }
        if (Test-Path -LiteralPath $SummaryScript) {
            powershell -ExecutionPolicy Bypass -File $SummaryScript
        }
        exit 2
    }

    if ($status.state -eq "complete") {
        Write-Host "Overnight gauntlet complete."
        if (Test-Path -LiteralPath $SummaryScript) {
            powershell -ExecutionPolicy Bypass -File $SummaryScript
        }
        exit 0
    }

    if ($status.state -eq "command_error") {
        Write-Error $status.message
        exit 4
    }

    if ((Get-Date) -gt $deadline -and ($status.state -eq "running_batch" -or $status.state -eq "waiting_for_campaign")) {
        Write-Error "Batch/status timed out in state '$($status.state)'."
        if (-not $NoGameRestart) { Stop-Bannerlord }
        if (-not $NoServerRestart) { Stop-ReignServer }
        exit 5
    }

    if ($status.state -eq "waiting_next_batch" -or $status.state -eq "running") {
        $deadline = (Get-Date).AddMinutes($BatchTimeoutMinutes)
    }
}
