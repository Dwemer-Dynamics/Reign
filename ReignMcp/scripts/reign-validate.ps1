[CmdletBinding()]
param(
    [ValidateSet('changed', 'core', 'product', 'tooling', 'all')]
    [string]$Profile = 'changed',
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [switch]$Restore,
    [string[]]$ChangedPath = @(),
    [switch]$NoFailFast,
    [switch]$PlanOnly,
    [switch]$VectorRuntime,
    [switch]$ReleasePackage,
    [string]$ValidationRunId,
    [string]$BuildSpecification,
    [string]$PythonExecutable,
    [string]$ModelDirectory,
    [string]$RequestingTaskId = $env:CODEX_THREAD_ID
)

$ErrorActionPreference = 'Stop'
if (@($VectorRuntime,$ReleasePackage,$PlanOnly | Where-Object { $_ }).Count -gt 1) { throw 'Choose one validation plan, vector-runtime build, or release-package build.' }
$mcpRoot = [System.IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$workspaceRoot = [System.IO.Path]::GetFullPath((Split-Path -Parent $mcpRoot))
$project = Join-Path $mcpRoot 'src\Reign.Mcp.Server\Reign.Mcp.Server.csproj'
$server = Join-Path $mcpRoot 'src\Reign.Mcp.Server\bin\Release\net10.0\Reign.Mcp.Server.dll'

# Bootstrap only the validator itself. Every product build and test after this
# point is selected and executed by the same engine exposed through MCP.
$bootstrapArguments = @('build', $project, '-c', 'Release')
if (-not $Restore) {
    $bootstrapArguments += '--no-restore'
}
dotnet @bootstrapArguments
if ($LASTEXITCODE -ne 0) {
    throw 'Could not bootstrap the Reign validation engine.'
}

$previousWorkspace = $env:REIGN_WORKSPACE_ROOT
$previousBuild = $env:REIGN_MCP_ALLOW_BUILD
$previousRestore = $env:REIGN_MCP_ALLOW_RESTORE
$previousOfflineVerification = $env:REIGN_MCP_ALLOW_OFFLINE_VERIFICATION
try {
    $env:REIGN_WORKSPACE_ROOT = $workspaceRoot
    $env:REIGN_MCP_ALLOW_BUILD = 'true'
    $env:REIGN_MCP_ALLOW_RESTORE = if ($Restore) { 'true' } else { 'false' }
    $env:REIGN_MCP_ALLOW_OFFLINE_VERIFICATION = 'true'
    $arguments = @(
        $server,
        $(if ($ReleasePackage) { '--release-package-cli' } elseif ($VectorRuntime) { '--release-runtime-cli' } elseif ($PlanOnly) { '--plan-cli' } else { '--validate-cli' }),
        '--profile', $Profile,
        '--configuration', $Configuration
    )
    if ($VectorRuntime) { $arguments += @('--python', $PythonExecutable, '--model-directory', $ModelDirectory) }
    if ($ReleasePackage) { $arguments += @('--python', $PythonExecutable, '--validation-run', $ValidationRunId, '--specification', $BuildSpecification) }
    if (-not [string]::IsNullOrWhiteSpace($RequestingTaskId)) {
        $arguments += @('--task-id', $RequestingTaskId)
    }
    if ($ChangedPath.Count -gt 0) {
        $arguments += @('--changed-paths', ($ChangedPath -join ';'))
    }
    if ($Restore) {
        $arguments += '--restore'
    }
    if ($NoFailFast) {
        $arguments += '--no-fail-fast'
    }

    & dotnet @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Reign validation failed for profile '$Profile'."
    }
} finally {
    if ($null -eq $previousWorkspace) {
        Remove-Item Env:\REIGN_WORKSPACE_ROOT -ErrorAction SilentlyContinue
    } else {
        $env:REIGN_WORKSPACE_ROOT = $previousWorkspace
    }
    if ($null -eq $previousBuild) {
        Remove-Item Env:\REIGN_MCP_ALLOW_BUILD -ErrorAction SilentlyContinue
    } else {
        $env:REIGN_MCP_ALLOW_BUILD = $previousBuild
    }
    if ($null -eq $previousRestore) {
        Remove-Item Env:\REIGN_MCP_ALLOW_RESTORE -ErrorAction SilentlyContinue
    } else {
        $env:REIGN_MCP_ALLOW_RESTORE = $previousRestore
    }
    if ($null -eq $previousOfflineVerification) {
        Remove-Item Env:\REIGN_MCP_ALLOW_OFFLINE_VERIFICATION -ErrorAction SilentlyContinue
    } else {
        $env:REIGN_MCP_ALLOW_OFFLINE_VERIFICATION = $previousOfflineVerification
    }
}
