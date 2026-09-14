[CmdletBinding(SupportsShouldProcess)]
param(
    [string]$Configuration = 'Debug',
    [string]$BannerlordPath = 'D:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord'
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$sourceModule = Join-Path $projectRoot 'module\BannerlordEditorBridge'
$sourceDll = Join-Path $sourceModule 'bin\Win64_Shipping_wEditor\Bannerlord.EditorBridge.dll'
$destinationModule = Join-Path $BannerlordPath 'Modules\BannerlordEditorBridge'

$running = Get-Process -Name 'Bannerlord','Bannerlord.Native' -ErrorAction SilentlyContinue
if ($running) {
    throw 'Close the Bannerlord editor/game before deploying the bridge.'
}

if (-not (Test-Path -LiteralPath $sourceDll -PathType Leaf)) {
    throw "Bridge build output not found: $sourceDll. Run scripts/build.ps1 first."
}

$resolvedGameRoot = [IO.Path]::GetFullPath($BannerlordPath).TrimEnd('\')
$resolvedDestination = [IO.Path]::GetFullPath($destinationModule).TrimEnd('\')
if (-not $resolvedDestination.StartsWith($resolvedGameRoot + '\Modules\', [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Resolved deployment target is outside the Bannerlord Modules directory.'
}

if ($PSCmdlet.ShouldProcess($resolvedDestination, 'Deploy development-only Bannerlord editor bridge module')) {
    New-Item -ItemType Directory -Path $resolvedDestination -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $sourceModule 'SubModule.xml') -Destination (Join-Path $resolvedDestination 'SubModule.xml') -Force
    $destinationBin = Join-Path $resolvedDestination 'bin\Win64_Shipping_wEditor'
    New-Item -ItemType Directory -Path $destinationBin -Force | Out-Null
    Copy-Item -LiteralPath $sourceDll -Destination (Join-Path $destinationBin 'Bannerlord.EditorBridge.dll') -Force
    Copy-Item -LiteralPath (Join-Path (Split-Path -Parent $sourceDll) 'Bannerlord.EditorMcp.Protocol.dll') -Destination (Join-Path $destinationBin 'Bannerlord.EditorMcp.Protocol.dll') -Force
}
