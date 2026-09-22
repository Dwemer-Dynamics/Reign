[CmdletBinding()]
param(
    [string]$PackageRoot = (Join-Path (Split-Path -Parent $PSScriptRoot) 'artifacts\deployment\reign-mcp')
)

$ErrorActionPreference = 'Stop'
$mcpRoot = [System.IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$workspaceRoot = [System.IO.Path]::GetFullPath((Split-Path -Parent $mcpRoot))
$packagePath = [System.IO.Path]::GetFullPath($PackageRoot)
$manifestPath = Join-Path $packagePath 'deployment-manifest.json'
$serverPath = Join-Path $packagePath 'Reign.Mcp.Server.dll'

if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
    throw "Deployment manifest is missing: $manifestPath"
}

if (-not (Test-Path -LiteralPath $serverPath -PathType Leaf)) {
    throw "Published MCP server is missing: $serverPath"
}

$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if ($manifest.schema -ne 'reign-mcp-deployment-v1' -or $manifest.transport -ne 'stdio') {
    throw 'Deployment manifest has an unsupported schema or transport.'
}

foreach ($entry in $manifest.files) {
    $candidate = [System.IO.Path]::GetFullPath((Join-Path $packagePath $entry.path))
    if (-not $candidate.StartsWith(
            $packagePath + [System.IO.Path]::DirectorySeparatorChar,
            [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Manifest path escaped the deployment package: $($entry.path)"
    }

    if (-not (Test-Path -LiteralPath $candidate -PathType Leaf)) {
        throw "Manifest file is missing: $($entry.path)"
    }

    $actualLength = (Get-Item -LiteralPath $candidate).Length
    if ($actualLength -ne $entry.bytes) {
        throw "Manifest length mismatch: $($entry.path)"
    }

    $actual = (Get-FileHash -LiteralPath $candidate -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actual -ne $entry.sha256) {
        throw "Manifest hash mismatch: $($entry.path)"
    }
}

$unexpected = Get-ChildItem -LiteralPath $packagePath -File -Recurse |
    Where-Object { $_.FullName -ne $manifestPath } |
    ForEach-Object {
        if (-not $_.FullName.StartsWith(
                $packagePath + [System.IO.Path]::DirectorySeparatorChar,
                [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Packaged file escaped the deployment directory: $($_.FullName)"
        }

        $_.FullName.Substring($packagePath.Length + 1).Replace('\', '/')
    } |
    Where-Object { $_ -notin @($manifest.files.path) }
if ($unexpected) {
    throw "Deployment contains files absent from the manifest: $($unexpected -join ', ')"
}

if (-not $manifest.contractInputs -or $manifest.contractInputs.Count -eq 0) {
    throw 'Deployment manifest does not contain workspace contract inputs.'
}

foreach ($entry in $manifest.contractInputs) {
    $candidate = [System.IO.Path]::GetFullPath((Join-Path $workspaceRoot $entry.path))
    if (-not $candidate.StartsWith(
            $workspaceRoot + [System.IO.Path]::DirectorySeparatorChar,
            [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Contract input escaped the Reign workspace: $($entry.path)"
    }

    if (-not (Test-Path -LiteralPath $candidate -PathType Leaf)) {
        throw "Contract input is missing: $($entry.path)"
    }

    $actualLength = (Get-Item -LiteralPath $candidate).Length
    if ($actualLength -ne $entry.bytes) {
        throw "Deployment is stale; contract length changed: $($entry.path)"
    }

    $actual = (Get-FileHash -LiteralPath $candidate -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actual -ne $entry.sha256) {
        throw "Deployment is stale; contract hash changed: $($entry.path)"
    }
}

$serverAssembly = [System.Reflection.AssemblyName]::GetAssemblyName($serverPath)
if ($serverAssembly.Name -ne 'Reign.Mcp.Server') {
    throw "Unexpected server assembly: $($serverAssembly.Name)"
}

Write-Host "Deployment preflight passed for $($manifest.files.Count) packaged files and $($manifest.contractInputs.Count) workspace contracts."
