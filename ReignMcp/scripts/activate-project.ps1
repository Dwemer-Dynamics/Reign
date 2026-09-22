[CmdletBinding(SupportsShouldProcess)]
param(
    [string]$PackageRoot = (Join-Path (Split-Path -Parent $PSScriptRoot) 'artifacts\deployment\reign-mcp')
)

$ErrorActionPreference = 'Stop'
$mcpRoot = [System.IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$workspaceRoot = [System.IO.Path]::GetFullPath((Split-Path -Parent $mcpRoot))
$packagePath = [System.IO.Path]::GetFullPath($PackageRoot)
$serverPath = Join-Path $packagePath 'Reign.Mcp.Server.dll'
$templatePath = Join-Path $mcpRoot 'deployment\config.toml.template'
$codexDirectory = Join-Path $workspaceRoot '.codex'
$configPath = Join-Path $codexDirectory 'config.toml'

& (Join-Path $PSScriptRoot 'test-deployment.ps1') -PackageRoot $packagePath

if (Test-Path -LiteralPath $configPath) {
    throw "Refusing to merge or overwrite the existing project configuration: $configPath"
}

if (-not (Test-Path -LiteralPath $templatePath -PathType Leaf)) {
    throw "Configuration template is missing: $templatePath"
}

function ConvertTo-TomlBasicStringValue([string]$Value) {
    return $Value.Replace('\', '\\').Replace('"', '\"')
}

$config = Get-Content -LiteralPath $templatePath -Raw
$config = $config.Replace(
    '__REIGN_MCP_DLL__',
    (ConvertTo-TomlBasicStringValue $serverPath))
$config = $config.Replace(
    '__REIGN_WORKSPACE_ROOT__',
    (ConvertTo-TomlBasicStringValue $workspaceRoot))

if ($config.Contains('__REIGN_')) {
    throw 'Configuration template still contains unresolved placeholders.'
}

if ($PSCmdlet.ShouldProcess($configPath, 'Create project-scoped Reign MCP registration')) {
    New-Item -ItemType Directory -Path $codexDirectory -Force | Out-Null
    $config | Set-Content -LiteralPath $configPath -Encoding utf8
    Write-Host "Project-scoped Reign MCP registration is ready at $configPath"
    Write-Host 'Restart Codex, inspect /mcp, and perform the read-only parity checks before enabling additional gates.'
} else {
    Write-Host "Activation preview completed for $configPath"
}
