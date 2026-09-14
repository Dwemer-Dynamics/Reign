[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

dotnet build (Join-Path $root 'ReignMcp.slnx') -c $Configuration
dotnet test (Join-Path $root 'ReignMcp.slnx') -c $Configuration --no-build

