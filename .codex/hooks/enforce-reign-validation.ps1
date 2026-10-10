$ErrorActionPreference = 'Stop'

$rawInput = [Console]::In.ReadToEnd()
if ([string]::IsNullOrWhiteSpace($rawInput)) {
    exit 0
}

try {
    $payload = $rawInput | ConvertFrom-Json
} catch {
    exit 0
}

$command = ''
if ($null -ne $payload.tool_input) {
    if ($null -ne $payload.tool_input.command) {
        $command = [string]$payload.tool_input.command
    } elseif ($null -ne $payload.tool_input.cmd) {
        $command = [string]$payload.tool_input.cmd
    }
}
if ([string]::IsNullOrWhiteSpace($command)) {
    exit 0
}

$usesCanonicalValidator =
    $command -match '(?i)ReignMcp[\\/]+scripts[\\/]+reign-validate\.ps1' -or
    $command -match '(?i)--validate-cli'
if ($usesCanonicalValidator) {
    exit 0
}

$isScopedMcpRepair =
    $command -match '(?i)REIGN_MCP_REPAIR\s*=\s*(?:1|true)' -and
    $command -match '(?i)ReignMcp' -and
    $command -notmatch '(?i)ReignBeta(?:Server)?|BannerlordEditorMcp|NativeCharacterImageGenerator'
if ($isScopedMcpRepair) {
    exit 0
}

$directDotnetOperation =
    $command -match '(?i)(?:^|[\s;&|])(?:&\s*)?(?:"[^"]*[\\/]dotnet(?:\.exe)?"|dotnet(?:\.exe)?)\s+(?:build|test|restore|publish|pack|clean|msbuild|run|format)\b'
$directMsBuild =
    $command -match '(?i)(?:^|[\s;&|])(?:&\s*)?(?:"[^"]*[\\/]msbuild(?:\.exe)?"|msbuild(?:\.exe)?)\b'
$directVstest =
    $command -match '(?i)(?:^|[\s;&|])(?:&\s*)?(?:"[^"]*[\\/]vstest\.console(?:\.exe)?"|vstest\.console(?:\.exe)?)\b'
$directVerification =
    $command -match '(?i)(?:ReignBetaServer|ReignVerification)(?:\.exe)?[^\r\n]*--run-verification'
# The paired deployment entry point only activates report-bound artifacts. Resolve
# each literal path separately so mentioning it cannot exempt another legacy
# script or a direct build elsewhere in the command.
$workspaceRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$canonicalDeploymentPaths = @(
    [IO.Path]::GetFullPath((Join-Path $workspaceRoot 'ReignServer\scripts\Deploy-LocalWsl.ps1')),
    [IO.Path]::GetFullPath((Join-Path $workspaceRoot '..\ReignServer\scripts\Deploy-LocalWsl.ps1'))
)
$commandDirectory = $workspaceRoot
if ($payload.tool_input.workdir) {
    $commandDirectory = [string]$payload.tool_input.workdir
} elseif ($payload.cwd) {
    $commandDirectory = [string]$payload.cwd
}
$deploymentLiterals = @()
try {
    $parseTokens = $null
    $parseErrors = $null
    $tree = [Management.Automation.Language.Parser]::ParseInput($command, [ref]$parseTokens, [ref]$parseErrors)
    if ($parseErrors.Count -eq 0) {
        $deploymentLiterals = @($tree.FindAll({ param($node)
            $node -is [Management.Automation.Language.StringConstantExpressionAst]
        }, $true) | Where-Object {
            try {
                $literalPath = if ([IO.Path]::IsPathRooted($_.Value)) { $_.Value } else { Join-Path $commandDirectory $_.Value }
                [IO.Path]::GetFullPath($literalPath) -in $canonicalDeploymentPaths
            } catch { $false }
        })
    }
} catch { $deploymentLiterals = @() }
$legacyBuildScript = $false
foreach ($match in [regex]::Matches($command, '(?i)(?:scripts[\\/])(?:build|prepare-deployment|test-deployment|deploy[^\\/]*)\.ps1')) {
    $canonicalLiteral = @($deploymentLiterals | Where-Object {
        $_.Extent.StartOffset -le $match.Index -and
        $_.Extent.EndOffset -ge ($match.Index + $match.Length)
    })
    if ($canonicalLiteral.Count -eq 0) { $legacyBuildScript = $true }
}

if (-not ($directDotnetOperation -or $directMsBuild -or $directVstest -or
        $directVerification -or $legacyBuildScript)) {
    exit 0
}

$reason = @'
Direct Reign build/test execution is blocked by repository policy.
Use mcp__reign.reign_get_validation_plan followed by mcp__reign.reign_validate.
For CI or when MCP transport is unavailable, use ReignMcp\scripts\reign-validate.ps1.
For a narrowly scoped MCP bootstrap repair only, set REIGN_MCP_REPAIR=1 and target ReignMcp exclusively.
'@.Trim()

[ordered]@{
    hookSpecificOutput = [ordered]@{
        hookEventName = 'PreToolUse'
        permissionDecision = 'deny'
        permissionDecisionReason = $reason
    }
} | ConvertTo-Json -Depth 4 -Compress
exit 0
