[CmdletBinding()]
param(
    [string]$Configuration = 'Debug',
    [string]$BannerlordPath = 'D:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord'
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $projectRoot 'BannerlordEditorMcp.slnx'
$editorAssembly = Join-Path $BannerlordPath 'bin\Win64_Shipping_wEditor\TaleWorlds.Engine.dll'

if (-not (Test-Path -LiteralPath $editorAssembly -PathType Leaf)) {
    throw "Bannerlord Modding Kit assembly not found: $editorAssembly"
}

dotnet restore $solution -p:BannerlordPath="$BannerlordPath"
if ($LASTEXITCODE -ne 0) { throw 'dotnet restore failed.' }

dotnet build $solution --no-restore --configuration $Configuration -p:BannerlordPath="$BannerlordPath"
if ($LASTEXITCODE -ne 0) { throw 'dotnet build failed.' }

dotnet test (Join-Path $projectRoot 'tests\Bannerlord.EditorMcp.Tests\Bannerlord.EditorMcp.Tests.csproj') --no-build --configuration $Configuration
if ($LASTEXITCODE -ne 0) { throw 'dotnet test failed.' }
