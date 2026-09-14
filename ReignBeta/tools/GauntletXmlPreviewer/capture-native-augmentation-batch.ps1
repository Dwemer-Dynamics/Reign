param(
    [Parameter(Mandatory = $true)][string]$LiveTestPath,
    [Parameter(Mandatory = $true)][string]$SaveName,
    [Parameter(Mandatory = $true)][ValidatePattern('^\d+x\d+$')][string]$Resolution,
    [Parameter(Mandatory = $true)][double]$UiScale,
    [Parameter(Mandatory = $true)][string]$InstalledRoot,
    [Parameter(Mandatory = $true)][string]$RenderReport,
    [Parameter(Mandatory = $true)][string]$OutputRoot,
    [string]$PythonPath = 'python',
    [string]$BannerlordConfigPath = (Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'Mount and Blade II Bannerlord\Configs\BannerlordConfig.txt'),
    [string]$SteamScreenshotRoot = '',
    [string[]]$Targets = @(),
    [switch]$KeepGameOpen
)

$ErrorActionPreference = 'Stop'
$toolRoot = $PSScriptRoot
$catalogPath = Join-Path $toolRoot 'calibration\ui-catalog.json'
$capturePath = Join-Path $toolRoot 'capture-native-augmentation.ps1'
$workflowPath = Join-Path $toolRoot 'calibration_workflow.py'
$liveTest = [System.IO.Path]::GetFullPath($LiveTestPath)
$installed = [System.IO.Path]::GetFullPath($InstalledRoot)
$render = [System.IO.Path]::GetFullPath($RenderReport)
$output = [System.IO.Path]::GetFullPath($OutputRoot)
$bannerlordConfig = [System.IO.Path]::GetFullPath($BannerlordConfigPath)
$catalog = Get-Content -Raw -LiteralPath $catalogPath | ConvertFrom-Json

foreach ($requiredFile in @($liveTest, $render, $catalogPath, $capturePath, $workflowPath, $bannerlordConfig)) {
    if (-not (Test-Path -LiteralPath $requiredFile -PathType Leaf)) {
        throw "Required native-augmentation capture input is missing: $requiredFile"
    }
}
if (-not (Test-Path -LiteralPath $installed -PathType Container)) {
    throw "Installed ReignBeta root is missing: $installed"
}
if (-not (Get-Command -Name $PythonPath -ErrorAction SilentlyContinue)) {
    throw "Python executable is unavailable: $PythonPath"
}

$uiScaleLine = Get-Content -LiteralPath $bannerlordConfig |
    Where-Object { $_ -match '^\s*UIScale\s*=' } |
    Select-Object -Last 1
if ([string]::IsNullOrWhiteSpace($uiScaleLine)) {
    throw "Bannerlord configuration does not contain UIScale: $bannerlordConfig"
}
$configuredUiScale = 0.0
$configuredUiScaleText = ($uiScaleLine -split '=', 2)[1].Trim()
if (-not [double]::TryParse(
        $configuredUiScaleText,
        [Globalization.NumberStyles]::Float,
        [Globalization.CultureInfo]::InvariantCulture,
        [ref]$configuredUiScale)) {
    throw "Bannerlord UIScale is not a valid invariant number: $configuredUiScaleText"
}
if ([Math]::Abs($configuredUiScale - $UiScale) -gt 0.0001) {
    throw "Bannerlord configured UIScale is $configuredUiScale, expected catalog case $UiScale."
}

$matrixMatch = @($catalog.targetMatrix | Where-Object {
    $_.resolution -eq $Resolution -and
        @($_.uiScales | Where-Object {
            [Math]::Abs([double]$_ - $UiScale) -lt 0.0001
        }).Count -gt 0
})
if ($matrixMatch.Count -ne 1) {
    throw "$Resolution@$UiScale is not a required catalog matrix case."
}

$nativeTargets = @($catalog.nativeAugmentations.targets | ForEach-Object { [string]$_.id })
if ($Targets.Count -eq 0) { $Targets = $nativeTargets }
$unknownTargets = @($Targets | Where-Object { $nativeTargets -notcontains $_ })
if ($unknownTargets.Count -gt 0) {
    throw "Only cataloged native augmentations may be captured by this batch: $($unknownTargets -join ', ')"
}

function Invoke-LiveTest {
    param([Parameter(Mandatory = $true)][string[]]$Arguments)
    $raw = & $liveTest @Arguments 2>&1 | Out-String
    if ($LASTEXITCODE -ne 0) {
        throw "ReignLiveTest failed ($($Arguments -join ' ')): $raw"
    }
    try { return $raw | ConvertFrom-Json }
    catch {
        throw "ReignLiveTest did not return JSON ($($Arguments -join ' ')): $raw"
    }
}

function Wait-NativeTargetReady {
    param(
        [Parameter(Mandatory = $true)][string]$Target,
        [int]$TimeoutSeconds = 120
    )
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    do {
        $status = Invoke-LiveTest -Arguments @('ui-status', '--json')
        $command = @($status.commands)[-1]
        $state = $command.result
        if ($null -eq $state) {
            throw "UI status for $Target did not contain a result object."
        }
        if (-not [string]::Equals(
                [string]$state.nativeUiCalibrationTarget,
                $Target,
                [StringComparison]::OrdinalIgnoreCase)) {
            throw "Native UI status reported '$($state.nativeUiCalibrationTarget)' while waiting for '$Target'."
        }
        if ([bool]$state.nativeUiCalibrationOpen -and
            [bool]$state.nativeUiCalibrationReady) {
            if (-not [bool]$state.nativeUiCalibrationProviderFree) {
                throw "$Target did not report provider-free calibration state."
            }
            if ([bool]$state.nativeUiCalibrationSavedCampaign) {
                throw "$Target reported a saved campaign mutation."
            }
            return $state
        }
        Start-Sleep -Milliseconds 250
    } while ([DateTime]::UtcNow -lt $deadline)
    throw "Timed out waiting $TimeoutSeconds seconds for native UI target $Target."
}

function Close-RunBestEffort {
    param([string]$RunId)
    if ([string]::IsNullOrWhiteSpace($RunId)) { return }
    try { Invoke-LiveTest -Arguments @('ui-close', '--json') | Out-Null } catch { }
    try { Invoke-LiveTest -Arguments @('close', '--run', $RunId, '--json') | Out-Null } catch { }
}

function Stop-GameBestEffort {
    try {
        $status = Invoke-LiveTest -Arguments @('game', 'status', '--json')
        if ($status.running) {
            Invoke-LiveTest -Arguments @('game', 'stop', '--force', '--wait', '180', '--json') | Out-Null
        }
    }
    catch { }
}

function Invoke-NativeCapture {
    param([Parameter(Mandatory = $true)][string]$Target)
    $arguments = @{
        Target = $Target
        Resolution = $Resolution
        UiScale = $UiScale
        LiveTestPath = $liveTest
        InstalledRoot = $installed
        RenderReport = $render
        OutputRoot = $output
        PythonPath = $PythonPath
        AllowOffscreenClientResize = $true
    }
    if (-not [string]::IsNullOrWhiteSpace($SteamScreenshotRoot)) {
        $arguments['SteamScreenshotRoot'] = $SteamScreenshotRoot
    }
    $raw = & $capturePath @arguments 2>&1 | Out-String
    if ($LASTEXITCODE -ne 0) {
        throw "Native augmentation capture failed for $Target`: $raw"
    }
    try { return $raw | ConvertFrom-Json }
    catch { throw "Native augmentation capture did not return JSON for $Target`: $raw" }
}

$caseKey = "$Resolution-ui$([int][Math]::Round($UiScale * 100))"
$batchRoot = Join-Path $output $caseKey
[System.IO.Directory]::CreateDirectory($batchRoot) | Out-Null
$batchPath = Join-Path $batchRoot 'native-augmentation-capture-batch.json'
$startedUtc = [DateTime]::UtcNow.ToString('o')
$rows = [System.Collections.Generic.List[object]]::new()
$campaignGameStarted = $false
$initialTargetSelected = $Targets -contains 'native-initial-screen'
$campaignTargets = @($Targets | Where-Object { $_ -ne 'native-initial-screen' })
$bridgeArmMinutes = 240
$liveTestBridgeArm = [ordered]@{
    required = $campaignTargets.Count -gt 0
    acquisition = 'self-armed-after-campaign-start'
    requestedMinutes = $bridgeArmMinutes
    attempted = $false
    ok = $false
    armed = $false
    armId = ''
    expiresUtc = ''
}

try {
    $gameStatus = Invoke-LiveTest -Arguments @('game', 'status', '--json')
    if ($gameStatus.running) {
        throw 'Bannerlord is already running. Stop it through the unified Reign lifecycle before native augmentation capture.'
    }

    if ($initialTargetSelected) {
        $target = 'native-initial-screen'
        try {
            Invoke-LiveTest -Arguments @('game', 'start-menu', '--wait', '120', '--json') | Out-Null
            $capture = Invoke-NativeCapture -Target $target
            $rows.Add([ordered]@{
                targetId = $target
                status = 'staged-for-review'
                reviewed = $false
                screenshotPath = [string]$capture.screenshotPath
                captureMetadataPath = [System.IO.Path]::GetFullPath([string]$capture.screenshotPath + '.json')
                configuredUiScale = $configuredUiScale
                providerFree = $true
                savedCampaign = $false
                nativeUiReady = $true
                fixtureHeroId = ''
                liveRunId = ''
                liveReportPath = ''
                acceptanceManifestPath = [string]$capture.acceptanceManifestPath
                error = ''
            })
        }
        catch {
            $rows.Add([ordered]@{
                targetId = $target
                status = 'failed'
                reviewed = $false
                screenshotPath = ''
                captureMetadataPath = ''
                configuredUiScale = $configuredUiScale
                providerFree = $true
                savedCampaign = $false
                nativeUiReady = $false
                fixtureHeroId = ''
                liveRunId = ''
                liveReportPath = ''
                acceptanceManifestPath = ''
                error = $_.Exception.Message
            })
        }
        finally {
            Stop-GameBestEffort
        }
    }

    if ($campaignTargets.Count -gt 0) {
        Invoke-LiveTest -Arguments @('game', 'start', '--save', $SaveName, '--wait', '300', '--json') | Out-Null
        $campaignGameStarted = $true
        $liveTestBridgeArm['attempted'] = $true
        $bridgeArmReceipt = Invoke-LiveTest -Arguments @('arm', '--minutes', [string]$bridgeArmMinutes, '--json')
        $liveTestBridgeArm['ok'] = [bool]$bridgeArmReceipt.ok
        $liveTestBridgeArm['armed'] = [bool]$bridgeArmReceipt.armed
        $liveTestBridgeArm['armId'] = [string]$bridgeArmReceipt.armId
        $liveTestBridgeArm['expiresUtc'] = [string]$bridgeArmReceipt.expiresUtc
        if ($bridgeArmReceipt.ok -ne $true -or $bridgeArmReceipt.armed -ne $true) {
            throw "The native augmentation batch could not establish a fresh live-test bridge arm after campaign start (ok=$($bridgeArmReceipt.ok), armed=$($bridgeArmReceipt.armed))."
        }
        # The process-ready signal can precede Campaign.Current.MapState by a few frames.
        # Give the map one bounded grace period before opening native screens, then retry
        # only the explicit transient map-state failure below.
        Start-Sleep -Seconds 3
        foreach ($target in $campaignTargets) {
            $runId = ''
            $liveReportPath = ''
            $readinessState = $null
            try {
                $open = $null
                for ($openAttempt = 1; $openAttempt -le 6; $openAttempt++) {
                    $open = Invoke-LiveTest -Arguments @('ui-open', '--target', $target, '--timeout', '120', '--json')
                    $runId = [string]$open.runId
                    $liveReportPath = [string]$open.reportPath
                    $openCommand = @($open.commands)[-1]
                    if ($null -eq $openCommand -or
                        -not [string]::Equals([string]$openCommand.status, 'failed', [StringComparison]::OrdinalIgnoreCase)) {
                        break
                    }

                    $openError = [string]$openCommand.error
                    if ([string]::IsNullOrWhiteSpace($openError)) { $openError = [string]$openCommand.message }
                    $mapStateUnavailable = $openError -match 'campaign map state is unavailable'
                    if (-not $mapStateUnavailable -or $openAttempt -eq 6) {
                        throw "Native UI open failed for $target`: $openError"
                    }

                    Close-RunBestEffort -RunId $runId
                    $runId = ''
                    $liveReportPath = ''
                    Start-Sleep -Seconds 2
                }
                $readinessState = Wait-NativeTargetReady -Target $target
                $capture = Invoke-NativeCapture -Target $target
                $rows.Add([ordered]@{
                    targetId = $target
                    status = 'staged-for-review'
                    reviewed = $false
                    screenshotPath = [string]$capture.screenshotPath
                    captureMetadataPath = [System.IO.Path]::GetFullPath([string]$capture.screenshotPath + '.json')
                    configuredUiScale = $configuredUiScale
                    providerFree = [bool]$readinessState.nativeUiCalibrationProviderFree
                    savedCampaign = [bool]$readinessState.nativeUiCalibrationSavedCampaign
                    nativeUiReady = [bool]$readinessState.nativeUiCalibrationReady
                    fixtureHeroId = [string]$readinessState.nativeUiCalibrationFixtureHeroId
                    liveRunId = $runId
                    liveReportPath = $liveReportPath
                    acceptanceManifestPath = [string]$capture.acceptanceManifestPath
                    error = ''
                })
            }
            catch {
                $rows.Add([ordered]@{
                    targetId = $target
                    status = 'failed'
                    reviewed = $false
                    screenshotPath = ''
                    captureMetadataPath = ''
                    configuredUiScale = $configuredUiScale
                    providerFree = if ($null -ne $readinessState) { [bool]$readinessState.nativeUiCalibrationProviderFree } else { $true }
                    savedCampaign = if ($null -ne $readinessState) { [bool]$readinessState.nativeUiCalibrationSavedCampaign } else { $false }
                    nativeUiReady = $false
                    fixtureHeroId = if ($null -ne $readinessState) { [string]$readinessState.nativeUiCalibrationFixtureHeroId } else { '' }
                    liveRunId = $runId
                    liveReportPath = $liveReportPath
                    acceptanceManifestPath = ''
                    error = $_.Exception.Message
                })
            }
            finally {
                Close-RunBestEffort -RunId $runId
            }
        }
    }
}
finally {
    if (-not $KeepGameOpen) { Stop-GameBestEffort }
    $gameStopped = $true
    try { $gameStopped = -not [bool](Invoke-LiveTest -Arguments @('game', 'status', '--json')).running }
    catch { $gameStopped = $false }
    $batch = [ordered]@{
        schema = 'reign-ui-native-capture-batch-v1'
        surfaceType = 'native-augmentation'
        generatedUtc = [DateTime]::UtcNow.ToString('o')
        startedUtc = $startedUtc
        resolution = $Resolution
        uiScale = $UiScale
        configuredUiScale = $configuredUiScale
        bannerlordConfigPath = $bannerlordConfig
        saveName = $SaveName
        providerFree = $true
        writesCampaignState = $false
        inMemoryCampaignStateMayChange = $campaignTargets.Count -gt 0
        savedCampaign = $false
        liveTestBridgeArm = $liveTestBridgeArm
        gameStoppedAfterCapture = if ($KeepGameOpen) { $false } else { $gameStopped }
        requiresHumanReview = $true
        cases = @($rows)
    }
    $batch | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $batchPath -Encoding UTF8
}

$staged = @($rows | Where-Object { $_.status -eq 'staged-for-review' })
$contactSheetPath = ''
if ($staged.Count -gt 0) {
    $contactSheet = Join-Path $batchRoot 'native-augmentation-contact-sheet.png'
    $contactRaw = & $PythonPath $workflowPath 'contact-sheet' '--batch-report' $batchPath '--output' $contactSheet 2>&1 | Out-String
    if ($LASTEXITCODE -ne 0) { throw "Contact-sheet generation failed: $contactRaw" }
    $contact = $contactRaw | ConvertFrom-Json
    $contactSheetPath = [string]$contact.outputPath
}

$failed = @($rows | Where-Object { $_.status -ne 'staged-for-review' })
$result = [ordered]@{
    ok = $failed.Count -eq 0 -and ($KeepGameOpen -or $gameStopped)
    schema = 'reign-ui-native-capture-batch-result-v1'
    surfaceType = 'native-augmentation'
    batchReportPath = [System.IO.Path]::GetFullPath($batchPath)
    contactSheetPath = $contactSheetPath
    stagedCount = $staged.Count
    failedCount = $failed.Count
    gameStoppedAfterCapture = if ($KeepGameOpen) { $false } else { $gameStopped }
    requiresHumanReview = $true
}
$result | ConvertTo-Json -Depth 4
if (-not $result.ok) { exit 1 }
