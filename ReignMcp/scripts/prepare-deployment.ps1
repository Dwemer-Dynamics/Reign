[CmdletBinding()]
param(
    [ValidateSet('Release')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$mcpRoot = [System.IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$workspaceRoot = [System.IO.Path]::GetFullPath((Split-Path -Parent $mcpRoot))
$solutionPath = Join-Path $mcpRoot 'ReignMcp.slnx'
$projectPath = Join-Path $mcpRoot 'src\Reign.Mcp.Server\Reign.Mcp.Server.csproj'
$routeContractPath = Join-Path $workspaceRoot 'ReignServer\Program.cs'
$initialRouteContractHash = (Get-FileHash -LiteralPath $routeContractPath -Algorithm SHA256).Hash.ToLowerInvariant()
$artifactsRoot = [System.IO.Path]::GetFullPath((Join-Path $mcpRoot 'artifacts'))
$deploymentRoot = [System.IO.Path]::GetFullPath((Join-Path $artifactsRoot 'deployment'))
$publishRoot = Join-Path $deploymentRoot 'reign-mcp'
$zipPath = Join-Path $deploymentRoot 'reign-mcp.zip'

if (-not $deploymentRoot.StartsWith(
        $artifactsRoot + [System.IO.Path]::DirectorySeparatorChar,
        [System.StringComparison]::OrdinalIgnoreCase)) {
    throw 'Deployment output escaped the ReignMcp artifacts directory.'
}

dotnet build $solutionPath -c $Configuration
if ($LASTEXITCODE -ne 0) {
    throw 'Reign MCP build failed.'
}

dotnet test $solutionPath -c $Configuration --no-build --no-restore
if ($LASTEXITCODE -ne 0) {
    throw 'Reign MCP tests failed.'
}

dotnet format $solutionPath --verify-no-changes --no-restore
if ($LASTEXITCODE -ne 0) {
    throw 'Reign MCP formatting verification failed.'
}

if (Test-Path -LiteralPath $deploymentRoot) {
    $resolvedDeploymentRoot = [System.IO.Path]::GetFullPath($deploymentRoot)
    if (-not $resolvedDeploymentRoot.StartsWith(
            $artifactsRoot + [System.IO.Path]::DirectorySeparatorChar,
            [System.StringComparison]::OrdinalIgnoreCase)) {
        throw 'Refusing to remove a deployment directory outside ReignMcp artifacts.'
    }

    Remove-Item -LiteralPath $resolvedDeploymentRoot -Recurse -Force
}

New-Item -ItemType Directory -Path $publishRoot -Force | Out-Null

dotnet publish $projectPath `
    -c $Configuration `
    --no-build `
    --no-restore `
    --self-contained false `
    -p:UseAppHost=false `
    -o $publishRoot
if ($LASTEXITCODE -ne 0) {
    throw 'Reign MCP publish failed.'
}

$previousTestServer = $env:REIGN_MCP_TEST_SERVER_DLL
try {
    $env:REIGN_MCP_TEST_SERVER_DLL = Join-Path $publishRoot 'Reign.Mcp.Server.dll'
    dotnet test $solutionPath `
        -c $Configuration `
        --no-build `
        --no-restore `
        --filter 'FullyQualifiedName~McpHostIntegrationTests'
    if ($LASTEXITCODE -ne 0) {
        throw 'Published Reign MCP STDIO smoke test failed.'
    }
} finally {
    if ($null -eq $previousTestServer) {
        Remove-Item Env:\REIGN_MCP_TEST_SERVER_DLL -ErrorAction SilentlyContinue
    } else {
        $env:REIGN_MCP_TEST_SERVER_DLL = $previousTestServer
    }
}

Copy-Item -LiteralPath (Join-Path $mcpRoot 'README.md') -Destination $publishRoot
Copy-Item -LiteralPath (Join-Path $mcpRoot 'docs') -Destination $publishRoot -Recurse
Copy-Item -LiteralPath (Join-Path $mcpRoot 'deployment\config.toml.template') -Destination $publishRoot

$manifestEntries = Get-ChildItem -LiteralPath $publishRoot -File -Recurse |
    Sort-Object FullName |
    ForEach-Object {
        if (-not $_.FullName.StartsWith(
                $publishRoot + [System.IO.Path]::DirectorySeparatorChar,
                [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Published file escaped the deployment directory: $($_.FullName)"
        }

        [ordered]@{
            path = $_.FullName.Substring($publishRoot.Length + 1).Replace('\', '/')
            bytes = $_.Length
            sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        }
    }

$contractFiles = @(
    (Join-Path $mcpRoot 'Directory.Build.props'),
    (Join-Path $mcpRoot 'ReignMcp.slnx'),
    (Join-Path $workspaceRoot 'reign-projects.json'),
    (Join-Path $workspaceRoot 'AGENTS.md'),
    (Join-Path $workspaceRoot '.codex\hooks.json'),
    (Join-Path $workspaceRoot '.codex\hooks\enforce-reign-validation.ps1'),
    (Join-Path $mcpRoot 'scripts\reign-validate.ps1'),
    $routeContractPath
)
$contractFiles += Get-ChildItem -LiteralPath (Join-Path $mcpRoot 'src') -File -Recurse |
    Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' } |
    Select-Object -ExpandProperty FullName

$contractEntries = $contractFiles |
    Sort-Object -Unique |
    ForEach-Object {
        $contractPath = [System.IO.Path]::GetFullPath($_)
        if (-not $contractPath.StartsWith(
                $workspaceRoot + [System.IO.Path]::DirectorySeparatorChar,
                [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Contract input escaped the Reign workspace: $contractPath"
        }

        if (-not (Test-Path -LiteralPath $contractPath -PathType Leaf)) {
            throw "Contract input is missing: $contractPath"
        }

        $contractItem = Get-Item -LiteralPath $contractPath
        $contractHash = (Get-FileHash -LiteralPath $contractPath -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($contractPath -eq $routeContractPath -and $contractHash -ne $initialRouteContractHash) {
            throw 'The Reign server route contract changed during preparation. Rerun after the other deployment finishes.'
        }

        [ordered]@{
            path = $contractPath.Substring($workspaceRoot.Length + 1).Replace('\', '/')
            bytes = $contractItem.Length
            sha256 = $contractHash
        }
    }

$manifest = [ordered]@{
    schema = 'reign-mcp-deployment-v1'
    server = 'reign'
    version = '0.2.0'
    framework = 'net10.0'
    transport = 'stdio'
    generatedUtc = [DateTimeOffset]::UtcNow.ToString('O')
    files = @($manifestEntries)
    contractInputs = @($contractEntries)
}

$manifest |
    ConvertTo-Json -Depth 6 |
    Set-Content -LiteralPath (Join-Path $publishRoot 'deployment-manifest.json') -Encoding utf8

Compress-Archive -Path (Join-Path $publishRoot '*') -DestinationPath $zipPath -CompressionLevel Optimal

& (Join-Path $PSScriptRoot 'test-deployment.ps1') -PackageRoot $publishRoot

Write-Host "Reign MCP deployment package is ready: $publishRoot"
Write-Host "Archive: $zipPath"
Write-Host 'No Reign process, installed Reign file, campaign data, or Codex configuration was changed.'
