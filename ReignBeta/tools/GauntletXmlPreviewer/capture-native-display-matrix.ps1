param(
    [Parameter(Mandatory = $true)][string]$LiveTestPath,
    [Parameter(Mandatory = $true)][string]$SaveName,
    [Parameter(Mandatory = $true)][string]$InstalledRoot,
    [Parameter(Mandatory = $true)][string]$RenderReport,
    [Parameter(Mandatory = $true)][string]$OutputRoot,
    [Parameter(Mandatory = $true)][string]$Confirmation,
    [string]$PythonPath = 'python',
    [string]$BannerlordConfigPath = (Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'Mount and Blade II Bannerlord\Configs\BannerlordConfig.txt'),
    [string]$EngineConfigPath = (Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'Mount and Blade II Bannerlord\Configs\engine_config.txt'),
    [string]$SteamScreenshotRoot = '',
    [ValidateSet(0, 1, 2)][int]$CaptureDisplayMode = 0,
    [string[]]$CaseKeys = @(),
    [string[]]$Targets = @(),
    [switch]$NativeAugmentations,
    [switch]$FailFast
)

$ErrorActionPreference = 'Stop'
$requiredConfirmation = 'temporarily configure Bannerlord display matrix and restore it'
if (-not [string]::Equals($Confirmation, $requiredConfirmation, [StringComparison]::Ordinal)) {
    throw "Display-matrix capture requires exact confirmation: $requiredConfirmation"
}

$toolRoot = $PSScriptRoot
$workspaceRoot = [System.IO.Path]::GetFullPath((Join-Path $toolRoot '..\..\..'))
$catalogPath = Join-Path $toolRoot 'calibration\ui-catalog.json'
$captureBatchName = if ($NativeAugmentations) {
    'capture-native-augmentation-batch.ps1'
} else {
    'capture-native-matrix.ps1'
}
$captureBatchPath = Join-Path $toolRoot $captureBatchName
$liveTest = [System.IO.Path]::GetFullPath($LiveTestPath)
$installed = [System.IO.Path]::GetFullPath($InstalledRoot)
$render = [System.IO.Path]::GetFullPath($RenderReport)
$output = [System.IO.Path]::GetFullPath($OutputRoot)
$bannerlordConfig = [System.IO.Path]::GetFullPath($BannerlordConfigPath)
$engineConfig = [System.IO.Path]::GetFullPath($EngineConfigPath)
$workspacePrefix = $workspaceRoot.TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
if (-not $output.StartsWith($workspacePrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Display-matrix evidence must stay beneath the Reign workspace: $output"
}

foreach ($requiredFile in @($liveTest, $render, $catalogPath, $captureBatchPath, $bannerlordConfig, $engineConfig)) {
    if (-not (Test-Path -LiteralPath $requiredFile -PathType Leaf)) { throw "Required display-matrix input is missing: $requiredFile" }
}
if (-not (Test-Path -LiteralPath $installed -PathType Container)) { throw "Installed ReignBeta root is missing: $installed" }
if (-not (Get-Command -Name $PythonPath -ErrorAction SilentlyContinue)) { throw "Python executable is unavailable: $PythonPath" }

function Invoke-LiveTest {
    param([Parameter(Mandatory = $true)][string[]]$Arguments)
    $raw = & $liveTest @Arguments 2>&1 | Out-String
    if ($LASTEXITCODE -ne 0) { throw "ReignLiveTest failed ($($Arguments -join ' ')): $raw" }
    try { return $raw | ConvertFrom-Json }
    catch { throw "ReignLiveTest did not return JSON ($($Arguments -join ' ')): $raw" }
}

function Get-FileSha256 {
    param([Parameter(Mandatory = $true)][string]$Path)
    return (Get-FileHash -Algorithm SHA256 -LiteralPath $Path).Hash.ToLowerInvariant()
}

function Set-SingleConfigValue {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Key,
        [Parameter(Mandatory = $true)][string]$Value
    )
    $text = [System.IO.File]::ReadAllText($Path)
    $pattern = '(?m)^(\s*' + [Regex]::Escape($Key) + '\s*=\s*).*$'
    $matches = [Regex]::Matches($text, $pattern)
    if ($matches.Count -ne 1) { throw "Expected exactly one '$Key' setting in $Path; found $($matches.Count)." }
    $match = $matches[0]
    $replacement = $match.Groups[1].Value + $Value
    $updated = $text.Substring(0, $match.Index) + $replacement + $text.Substring($match.Index + $match.Length)
    [System.IO.File]::WriteAllText($Path, $updated, [System.Text.UTF8Encoding]::new($false))
}

function Write-MatrixReport {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)]$Rows,
        [Parameter(Mandatory = $true)][string]$BackupRoot,
        [Parameter(Mandatory = $true)][string]$StartedUtc,
        [Parameter(Mandatory = $true)][string]$OriginalBannerlordHash,
        [Parameter(Mandatory = $true)][string]$OriginalEngineHash,
        [bool]$ConfigRestored,
        [bool]$GameStopped,
        [string]$TerminalError = ''
    )
    $report = [ordered]@{
        schema = 'reign-ui-native-display-matrix-v1'
        surfaceType = if ($NativeAugmentations) { 'native-augmentation' } else { 'standalone-runtime' }
        generatedUtc = [DateTime]::UtcNow.ToString('o')
        startedUtc = $StartedUtc
        saveName = $SaveName
        providerFree = $true
        writesCampaignState = $false
        savedCampaign = $false
        requiresHumanReview = $true
        bannerlordConfigPath = $bannerlordConfig
        engineConfigPath = $engineConfig
        backupRoot = $BackupRoot
        originalBannerlordConfigSha256 = $OriginalBannerlordHash
        originalEngineConfigSha256 = $OriginalEngineHash
        captureDisplayMode = $CaptureDisplayMode
        configRestored = $ConfigRestored
        gameStoppedAfterMatrix = $GameStopped
        terminalError = $TerminalError
        cases = @($Rows)
    }
    $report | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $Path -Encoding UTF8
}

$catalog = Get-Content -Raw -LiteralPath $catalogPath | ConvertFrom-Json
$availableCases = [System.Collections.Generic.List[object]]::new()
foreach ($entry in @($catalog.targetMatrix)) {
    foreach ($scale in @($entry.uiScales)) {
        $scaleValue = [double]$scale
        $scaleText = [string]::Format([Globalization.CultureInfo]::InvariantCulture, '{0:0.00}', $scaleValue)
        $availableCases.Add([pscustomobject]@{
            key = "$($entry.resolution)@$scaleText"
            resolution = [string]$entry.resolution
            uiScale = $scaleValue
        })
    }
}
$selectedCases = if ($CaseKeys.Count -eq 0) {
    @($availableCases)
} else {
    @($availableCases | Where-Object { $CaseKeys -contains $_.key })
}
$unknownCases = @($CaseKeys | Where-Object { $availableCases.key -notcontains $_ })
if ($unknownCases.Count -gt 0) { throw "Unknown catalog display-matrix cases: $($unknownCases -join ', ')" }
if ($selectedCases.Count -eq 0) { throw 'No catalog display-matrix cases were selected.' }

$status = Invoke-LiveTest -Arguments @('game', 'status', '--json')
if ($status.running) { throw 'Bannerlord must be stopped before display configuration can be backed up and changed.' }

[System.IO.Directory]::CreateDirectory($output) | Out-Null
$startedUtc = [DateTime]::UtcNow.ToString('o')
$stamp = [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fff')
$backupRoot = Join-Path $output "display-matrix-config-backup-$stamp"
[System.IO.Directory]::CreateDirectory($backupRoot) | Out-Null
$originalBannerlordConfigBytes = [System.IO.File]::ReadAllBytes($bannerlordConfig)
$originalEngineConfigBytes = [System.IO.File]::ReadAllBytes($engineConfig)
$backupBannerlordPath = Join-Path $backupRoot 'BannerlordConfig.txt'
$backupEnginePath = Join-Path $backupRoot 'engine_config.txt'
[System.IO.File]::WriteAllBytes($backupBannerlordPath, $originalBannerlordConfigBytes)
[System.IO.File]::WriteAllBytes($backupEnginePath, $originalEngineConfigBytes)
$originalBannerlordHash = Get-FileSha256 -Path $backupBannerlordPath
$originalEngineHash = Get-FileSha256 -Path $backupEnginePath
$reportPath = Join-Path $output "display-matrix-$stamp.json"
$rows = [System.Collections.Generic.List[object]]::new()
$configRestored = $false
$gameStopped = $true
$terminalError = ''

try {
    foreach ($case in $selectedCases) {
        $width, $height = $case.resolution.Split('x') | ForEach-Object { [int]$_ }
        $scaleText = [string]::Format([Globalization.CultureInfo]::InvariantCulture, '{0:0.##}', $case.uiScale)
        $caseStartedUtc = [DateTime]::UtcNow.ToString('o')
        try {
            $beforeCaseStatus = Invoke-LiveTest -Arguments @('game', 'status', '--json')
            if ($beforeCaseStatus.running) { throw "Bannerlord was still running before case $($case.key)." }

            Set-SingleConfigValue -Path $bannerlordConfig -Key 'UIScale' -Value $scaleText
            Set-SingleConfigValue -Path $engineConfig -Key 'display_width' -Value ([string]$width)
            Set-SingleConfigValue -Path $engineConfig -Key 'display_height' -Value ([string]$height)
            Set-SingleConfigValue -Path $engineConfig -Key 'display_mode' -Value ([string]$CaptureDisplayMode)

            $arguments = @{
                LiveTestPath = $liveTest
                SaveName = $SaveName
                Resolution = $case.resolution
                UiScale = $case.uiScale
                InstalledRoot = $installed
                RenderReport = $render
                OutputRoot = $output
                PythonPath = $PythonPath
                BannerlordConfigPath = $bannerlordConfig
            }
            if ($Targets.Count -gt 0) { $arguments['Targets'] = $Targets }
            if (-not [string]::IsNullOrWhiteSpace($SteamScreenshotRoot)) { $arguments['SteamScreenshotRoot'] = $SteamScreenshotRoot }

            $raw = & $captureBatchPath @arguments 2>&1 | Out-String
            $batchExitCode = $LASTEXITCODE
            $result = $null
            try { $result = $raw | ConvertFrom-Json } catch { }
            if ($batchExitCode -ne 0 -or $null -eq $result -or -not [bool]$result.ok) {
                throw "Native capture batch failed for $($case.key): $raw"
            }
            $rows.Add([ordered]@{
                caseKey = $case.key
                resolution = $case.resolution
                uiScale = $case.uiScale
                status = 'staged-for-review'
                reviewed = $false
                startedUtc = $caseStartedUtc
                completedUtc = [DateTime]::UtcNow.ToString('o')
                batchReportPath = [string]$result.batchReportPath
                contactSheetPath = [string]$result.contactSheetPath
                error = ''
            })
        }
        catch {
            $rows.Add([ordered]@{
                caseKey = $case.key
                resolution = $case.resolution
                uiScale = $case.uiScale
                status = 'failed'
                reviewed = $false
                startedUtc = $caseStartedUtc
                completedUtc = [DateTime]::UtcNow.ToString('o')
                batchReportPath = ''
                contactSheetPath = ''
                error = $_.Exception.Message
            })
            if ($FailFast) { throw }
        }
        finally {
            try {
                $afterCaseStatus = Invoke-LiveTest -Arguments @('game', 'status', '--json')
                if ($afterCaseStatus.running) {
                    Invoke-LiveTest -Arguments @('game', 'stop', '--wait', '180', '--json') | Out-Null
                }
            }
            catch {
                $gameStopped = $false
                if ([string]::IsNullOrWhiteSpace($terminalError)) { $terminalError = $_.Exception.Message }
            }
            Write-MatrixReport -Path $reportPath -Rows $rows -BackupRoot $backupRoot -StartedUtc $startedUtc -OriginalBannerlordHash $originalBannerlordHash -OriginalEngineHash $originalEngineHash -ConfigRestored $false -GameStopped $gameStopped -TerminalError $terminalError
        }
    }
}
catch {
    $terminalError = $_.Exception.Message
}
finally {
    try {
        $finalStatus = Invoke-LiveTest -Arguments @('game', 'status', '--json')
        if ($finalStatus.running) {
            Invoke-LiveTest -Arguments @('game', 'stop', '--wait', '180', '--json') | Out-Null
        }
        $gameStopped = -not (Invoke-LiveTest -Arguments @('game', 'status', '--json')).running
    }
    catch {
        $gameStopped = $false
        if ([string]::IsNullOrWhiteSpace($terminalError)) { $terminalError = $_.Exception.Message }
    }

    [System.IO.File]::WriteAllBytes($bannerlordConfig, $originalBannerlordConfigBytes)
    [System.IO.File]::WriteAllBytes($engineConfig, $originalEngineConfigBytes)
    $configRestored = (Get-FileSha256 -Path $bannerlordConfig) -eq $originalBannerlordHash -and
        (Get-FileSha256 -Path $engineConfig) -eq $originalEngineHash
    if (-not $configRestored -and [string]::IsNullOrWhiteSpace($terminalError)) {
        $terminalError = 'Bannerlord display configuration did not restore to the byte-for-byte backup hashes.'
    }
    Write-MatrixReport -Path $reportPath -Rows $rows -BackupRoot $backupRoot -StartedUtc $startedUtc -OriginalBannerlordHash $originalBannerlordHash -OriginalEngineHash $originalEngineHash -ConfigRestored $configRestored -GameStopped $gameStopped -TerminalError $terminalError
}

$failedRows = @($rows | Where-Object { $_.status -ne 'staged-for-review' })
$resultObject = [ordered]@{
    ok = $failedRows.Count -eq 0 -and $configRestored -and $gameStopped -and [string]::IsNullOrWhiteSpace($terminalError)
    schema = 'reign-ui-native-display-matrix-result-v1'
    surfaceType = if ($NativeAugmentations) { 'native-augmentation' } else { 'standalone-runtime' }
    reportPath = [System.IO.Path]::GetFullPath($reportPath)
    backupRoot = [System.IO.Path]::GetFullPath($backupRoot)
    selectedCaseCount = $selectedCases.Count
    stagedCaseCount = @($rows | Where-Object { $_.status -eq 'staged-for-review' }).Count
    failedCaseCount = $failedRows.Count
    configRestored = $configRestored
    gameStoppedAfterMatrix = $gameStopped
    terminalError = $terminalError
}
$resultObject | ConvertTo-Json -Depth 5
if (-not $resultObject.ok) { exit 1 }
