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
$workspaceRoot = [System.IO.Path]::GetFullPath((Join-Path $toolRoot '..\..\..'))
$catalogPath = Join-Path $toolRoot 'calibration\ui-catalog.json'
$workflowPath = Join-Path $toolRoot 'calibration_workflow.py'
$capturePath = Join-Path $toolRoot 'capture-window.ps1'
$liveTest = [System.IO.Path]::GetFullPath($LiveTestPath)
$installed = [System.IO.Path]::GetFullPath($InstalledRoot)
$render = [System.IO.Path]::GetFullPath($RenderReport)
$output = [System.IO.Path]::GetFullPath($OutputRoot)
$bannerlordConfig = [System.IO.Path]::GetFullPath($BannerlordConfigPath)
$catalog = Get-Content -Raw -LiteralPath $catalogPath | ConvertFrom-Json

if (-not (Test-Path -LiteralPath $liveTest -PathType Leaf)) { throw "Live-test controller is missing: $liveTest" }
if (-not (Test-Path -LiteralPath $installed -PathType Container)) { throw "Installed ReignBeta root is missing: $installed" }
if (-not (Test-Path -LiteralPath $render -PathType Leaf)) { throw "Rendered-preview report is missing: $render" }
if (-not (Test-Path -LiteralPath $bannerlordConfig -PathType Leaf)) { throw "Bannerlord configuration is missing: $bannerlordConfig" }
if (-not (Get-Command -Name $PythonPath -ErrorAction SilentlyContinue)) { throw "Python executable is unavailable: $PythonPath" }

$uiScaleLine = Get-Content -LiteralPath $bannerlordConfig | Where-Object { $_ -match '^\s*UIScale\s*=' } | Select-Object -Last 1
if ([string]::IsNullOrWhiteSpace($uiScaleLine)) { throw "Bannerlord configuration does not contain UIScale: $bannerlordConfig" }
$configuredUiScale = 0.0
$configuredUiScaleText = ($uiScaleLine -split '=', 2)[1].Trim()
if (-not [double]::TryParse($configuredUiScaleText, [Globalization.NumberStyles]::Float, [Globalization.CultureInfo]::InvariantCulture, [ref]$configuredUiScale)) {
    throw "Bannerlord UIScale is not a valid invariant number: $configuredUiScaleText"
}
if ([Math]::Abs($configuredUiScale - $UiScale) -gt 0.0001) {
    throw "Bannerlord configured UIScale is $configuredUiScale, expected catalog case $UiScale."
}

$matrixMatch = @($catalog.targetMatrix | Where-Object {
    $_.resolution -eq $Resolution -and @($_.uiScales | Where-Object { [Math]::Abs([double]$_ - $UiScale) -lt 0.0001 }).Count -gt 0
})
if ($matrixMatch.Count -ne 1) { throw "$Resolution@$UiScale is not a required catalog matrix case." }

$runtimeTargets = @($catalog.interfaces | Where-Object { -not $_.supportUi } | ForEach-Object { [string]$_.id })
if ($Targets.Count -eq 0) { $Targets = $runtimeTargets }
$unknownTargets = @($Targets | Where-Object { $runtimeTargets -notcontains $_ })
if ($unknownTargets.Count -gt 0) { throw "Only standalone runtime targets may be captured by this batch: $($unknownTargets -join ', ')" }

$requiredStateParentTarget = 'individual-chat'
$requiredStateId = 'individual-chat-pregnancy-warning'
$stateParent = @($catalog.interfaces | Where-Object { [string]$_.id -eq $requiredStateParentTarget })
if ($stateParent.Count -ne 1) { throw "Catalog parent target '$requiredStateParentTarget' is not declared exactly once." }
$requiredState = @($stateParent[0].previewStates | Where-Object { [string]$_.id -eq $requiredStateId })
if ($requiredState.Count -ne 1) { throw "Catalog runtime state '$requiredStateId' is not declared exactly once beneath '$requiredStateParentTarget'." }
if ($requiredState[0].previewOnly -ne $true -or $requiredState[0].nativeInjection -ne $false) {
    throw "Catalog runtime state '$requiredStateId' must remain previewOnly=true and nativeInjection=false."
}
$requiredStateEvidence = $requiredState[0].nativeEvidence
if ($null -eq $requiredStateEvidence -or $requiredStateEvidence.required -ne $true) { throw "Catalog runtime state '$requiredStateId' does not require native evidence." }
if ([string]$requiredStateEvidence.matrix -ne 'inherit-parent') { throw "Catalog runtime state '$requiredStateId' must inherit its parent's native matrix." }
if ([string]$requiredStateEvidence.acceptanceRelativePath -ne "states/$requiredStateId/acceptance.json") { throw "Catalog runtime state '$requiredStateId' has an unexpected acceptance path." }
$requiredStateSetupAction = [string]$requiredStateEvidence.setupAction
if ([string]::IsNullOrWhiteSpace($requiredStateSetupAction)) { throw "Catalog runtime state '$requiredStateId' has no native-evidence setup action." }
$requiredStateStatusCommand = [string]$requiredStateEvidence.statusReceiptCommand
if ($requiredStateStatusCommand -ne 'ui_status') { throw "Catalog runtime state '$requiredStateId' requires unsupported status command '$requiredStateStatusCommand'." }
$requiredStateStatusAssertions = $requiredStateEvidence.statusAssertions
if ($null -eq $requiredStateStatusAssertions -or @($requiredStateStatusAssertions.PSObject.Properties).Count -eq 0) { throw "Catalog runtime state '$requiredStateId' declares no status assertions." }

function Invoke-LiveTest {
    param([Parameter(Mandatory = $true)][string[]]$Arguments)
    $raw = & $liveTest @Arguments 2>&1 | Out-String
    if ($LASTEXITCODE -ne 0) { throw "ReignLiveTest failed ($($Arguments -join ' ')): $raw" }
    try { return $raw | ConvertFrom-Json }
    catch { throw "ReignLiveTest did not return JSON ($($Arguments -join ' ')): $raw" }
}

function Resolve-SnapshotPath {
    param([Parameter(Mandatory = $true)]$SnapshotResult)
    $command = @($SnapshotResult.commands)[-1]
    $reported = [string]$command.result.snapshotPath
    if ([string]::IsNullOrWhiteSpace($reported)) { throw 'The native snapshot receipt did not include snapshotPath.' }
    $candidate = Join-Path (Join-Path $installed 'UiCalibration\snapshots') ([System.IO.Path]::GetFileName($reported))
    if (-not (Test-Path -LiteralPath $candidate -PathType Leaf)) { throw "Native snapshot file is missing: $candidate" }
    return $candidate
}

function Wait-TargetReady {
    param(
        [Parameter(Mandatory = $true)][string]$Target,
        [int]$TimeoutSeconds = 120
    )
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    do {
        $status = Invoke-LiveTest -Arguments @('ui-status', '--json')
        $command = @($status.commands)[-1]
        $state = $command.result
        if ($null -eq $state) { throw "UI status for $Target did not contain a result object." }
        if ($Target -ne 'royal-council') { return $state }
        if (-not [bool]$state.royalCouncilOpen) { throw 'Royal Council closed before its calibration fixture became ready.' }
        if (-not [bool]$state.royalCouncilBusy) {
            if (-not [bool]$state.royalCouncilCalibrationFixture) { throw 'Royal Council native capture did not use the provider-free calibration fixture.' }
            if ([int]$state.royalCouncilProviderCallCount -ne 0) { throw "Royal Council calibration made $($state.royalCouncilProviderCallCount) provider calls." }
            if ([string]::IsNullOrWhiteSpace([string]$state.royalCouncilTranscript)) { throw 'Royal Council calibration fixture produced no transcript.' }
            return $state
        }
        Start-Sleep -Milliseconds 250
    } while ([DateTime]::UtcNow -lt $deadline)
    throw "Timed out waiting $TimeoutSeconds seconds for $Target to become capture-ready."
}

function Invoke-AcceptanceRecorder {
    param(
        [Parameter(Mandatory = $true)][string]$Target,
        [Parameter(Mandatory = $true)][string]$Screenshot,
        [Parameter(Mandatory = $true)][string]$Snapshot
    )
    $acceptanceRoot = $output
    $arguments = @(
        $workflowPath, 'accept-native-case',
        '--target', $Target,
        '--resolution', $Resolution,
        '--ui-scale', [string]::Format([Globalization.CultureInfo]::InvariantCulture, '{0:0.##}', $UiScale),
        '--screenshot', $Screenshot,
        '--snapshot', $Snapshot,
        '--evidence-root', $acceptanceRoot,
        '--installed-root', $installed,
        '--render-report', $render,
        '--diagnostic-count', '0'
    )
    $raw = & $PythonPath @arguments 2>&1 | Out-String
    if ($LASTEXITCODE -ne 0) { throw "Native acceptance staging failed for $Target`: $raw" }
    return $raw | ConvertFrom-Json
}

function Invoke-StateAcceptanceRecorder {
    param(
        [Parameter(Mandatory = $true)][string]$Target,
        [Parameter(Mandatory = $true)][string]$StateId,
        [Parameter(Mandatory = $true)][string]$Screenshot,
        [Parameter(Mandatory = $true)][string]$Snapshot,
        [Parameter(Mandatory = $true)][string]$StateReceipt
    )
    $arguments = @(
        $workflowPath, 'accept-native-state-case',
        '--target', $Target,
        '--state', $StateId,
        '--resolution', $Resolution,
        '--ui-scale', [string]::Format([Globalization.CultureInfo]::InvariantCulture, '{0:0.##}', $UiScale),
        '--screenshot', $Screenshot,
        '--snapshot', $Snapshot,
        '--state-receipt', $StateReceipt,
        '--evidence-root', $output,
        '--installed-root', $installed,
        '--render-report', $render,
        '--diagnostic-count', '0'
    )
    $raw = & $PythonPath @arguments 2>&1 | Out-String
    if ($LASTEXITCODE -ne 0) { throw "Native state acceptance staging failed for $Target/$StateId`: $raw" }
    return $raw | ConvertFrom-Json
}

function Close-RunBestEffort {
    param([string]$RunId)
    if ([string]::IsNullOrWhiteSpace($RunId)) { return }
    try { Invoke-LiveTest -Arguments @('ui-close', '--json') | Out-Null } catch { }
    try { Invoke-LiveTest -Arguments @('close', '--run', $RunId, '--json') | Out-Null } catch { }
}

$width, $height = $Resolution.Split('x') | ForEach-Object { [int]$_ }
$caseKey = "$Resolution-ui$([int][Math]::Round($UiScale * 100))"
$batchRoot = Join-Path $output $caseKey
[System.IO.Directory]::CreateDirectory($batchRoot) | Out-Null
$batchPath = Join-Path $batchRoot 'capture-batch.json'
$startedGame = $false
$rows = [System.Collections.Generic.List[object]]::new()
$stateRows = [System.Collections.Generic.List[object]]::new()
$startedUtc = [DateTime]::UtcNow.ToString('o')
$bridgeArmMinutes = 240
$liveTestBridgeArm = [ordered]@{
    required = $true
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
        throw 'Bannerlord is already running. Stop it through the unified Reign lifecycle before starting a deterministic native capture batch.'
    }
    Invoke-LiveTest -Arguments @('game', 'start', '--save', $SaveName, '--wait', '300', '--json') | Out-Null
    $startedGame = $true
    $liveTestBridgeArm['attempted'] = $true
    $bridgeArmReceipt = Invoke-LiveTest -Arguments @('arm', '--minutes', [string]$bridgeArmMinutes, '--json')
    $liveTestBridgeArm['ok'] = [bool]$bridgeArmReceipt.ok
    $liveTestBridgeArm['armed'] = [bool]$bridgeArmReceipt.armed
    $liveTestBridgeArm['armId'] = [string]$bridgeArmReceipt.armId
    $liveTestBridgeArm['expiresUtc'] = [string]$bridgeArmReceipt.expiresUtc
    if ($bridgeArmReceipt.ok -ne $true -or $bridgeArmReceipt.armed -ne $true) {
        throw "The native capture batch could not establish a fresh live-test bridge arm after campaign start (ok=$($bridgeArmReceipt.ok), armed=$($bridgeArmReceipt.armed))."
    }

    foreach ($target in $Targets) {
        $runId = ''
        $liveReportPath = ''
        $runtimeUiScale = $null
        $readinessState = $null
        $uiReady = $false
        $supportOverlayAbsentForCapture = $false
        $requiredStateRecorded = $false
        $targetDirectory = Join-Path $batchRoot $target
        $screenshot = Join-Path $targetDirectory 'native.png'
        $captureMetadata = $screenshot + '.json'
        $snapshot = Join-Path $targetDirectory 'runtime-snapshot.json'
        [System.IO.Directory]::CreateDirectory($targetDirectory) | Out-Null
        try {
            $open = Invoke-LiveTest -Arguments @('ui-open', '--target', $target, '--timeout', '120', '--json')
            $runId = [string]$open.runId
            $liveReportPath = [string]$open.reportPath

            $readinessState = Wait-TargetReady -Target $target
            $uiReady = $true

            $snapshotReceipt = Invoke-LiveTest -Arguments @('ui-snapshot', '--target', $target, '--json')
            $installedSnapshot = Resolve-SnapshotPath -SnapshotResult $snapshotReceipt
            Copy-Item -LiteralPath $installedSnapshot -Destination $snapshot -Force
            $snapshotJson = Get-Content -Raw -LiteralPath $snapshot | ConvertFrom-Json
            if ([int][Math]::Round([double]$snapshotJson.physicalWidth) -ne $width -or [int][Math]::Round([double]$snapshotJson.physicalHeight) -ne $height) {
                throw "Runtime snapshot resolution is $($snapshotJson.physicalWidth)x$($snapshotJson.physicalHeight), expected $Resolution."
            }
            $runtimeUiScale = [double]$snapshotJson.uiScale
            if ([double]::IsNaN($runtimeUiScale) -or [double]::IsInfinity($runtimeUiScale) -or $runtimeUiScale -le 0) {
                throw "Runtime snapshot UIContext.CustomScale must be positive; received $($snapshotJson.uiScale)."
            }
            $snapshotProperties = @($snapshotJson.PSObject.Properties.Name)
            if ($snapshotProperties -contains 'supportUi') {
                throw "Runtime snapshot for $target contains previewer-only support UI evidence. Native snapshots must be target-only."
            }
            if (-not ($snapshotProperties -contains 'supportUiInjected') -or
                $snapshotJson.supportUiInjected -isnot [bool] -or
                $snapshotJson.supportUiInjected -ne $false) {
                throw "Runtime snapshot for $target does not explicitly prove supportUiInjected=false."
            }
            if ($snapshotProperties -contains 'visualSupportUiInjected' -and [bool]$snapshotJson.visualSupportUiInjected) {
                throw "Runtime snapshot for $target reports visual support UI injection."
            }
            $supportOverlayAbsentForCapture = $true

            $captureArguments = @{
                ProcessName = 'Bannerlord'
                OutputPath = $screenshot
                ExpectedWidth = $width
                ExpectedHeight = $height
                AllowOffscreenClientResize = $true
            }
            if (-not [string]::IsNullOrWhiteSpace($SteamScreenshotRoot)) { $captureArguments['SteamScreenshotRoot'] = $SteamScreenshotRoot }
            $capture = & $capturePath @captureArguments | Out-String | ConvertFrom-Json
            if ([int]$capture.width -ne $width -or [int]$capture.height -ne $height) {
                throw "Verified foreground capture is $($capture.width)x$($capture.height), expected $Resolution."
            }

            $acceptance = Invoke-AcceptanceRecorder -Target $target -Screenshot $screenshot -Snapshot $snapshot
            $rows.Add([ordered]@{
                targetId = $target
                status = 'staged-for-review'
                reviewed = $false
                screenshotPath = [System.IO.Path]::GetFullPath($screenshot)
                captureMetadataPath = [System.IO.Path]::GetFullPath($captureMetadata)
                snapshotPath = [System.IO.Path]::GetFullPath($snapshot)
                configuredUiScale = $configuredUiScale
                runtimeUiScale = $runtimeUiScale
                uiReady = $uiReady
                supportOverlayAbsentForCapture = $supportOverlayAbsentForCapture
                providerCallCount = if ($target -eq 'royal-council') { [int]$readinessState.royalCouncilProviderCallCount } else { $null }
                calibrationFixture = if ($target -eq 'royal-council') { [bool]$readinessState.royalCouncilCalibrationFixture } else { $null }
                liveRunId = $runId
                liveReportPath = $liveReportPath
                acceptanceManifestPath = [string]$acceptance.manifestPath
                error = ''
            })

            if ($target -eq $requiredStateParentTarget) {
                $requiredStateRecorded = $true
                $stateDirectory = Join-Path $targetDirectory (Join-Path 'states' $requiredStateId)
                $stateScreenshot = Join-Path $stateDirectory 'native.png'
                $stateCaptureMetadata = $stateScreenshot + '.json'
                $stateSnapshot = Join-Path $stateDirectory 'runtime-snapshot.json'
                $stateReceiptPath = Join-Path $stateDirectory 'state-receipt.json'
                $stateRuntimeUiScale = $null
                $stateUiReady = $false
                [System.IO.Directory]::CreateDirectory($stateDirectory) | Out-Null
                try {
                    $stateAction = Invoke-LiveTest -Arguments @(
                        'ui-action', '--target', $target, '--text', $requiredStateSetupAction, '--json')
                    $stateActionCommand = @($stateAction.commands)[-1]
                    $stateStatus = Invoke-LiveTest -Arguments @('ui-status', '--json')
                    $stateStatusCommand = @($stateStatus.commands)[-1]
                    $stateResult = $stateStatusCommand.result
                    if ($null -eq $stateResult) { throw "UI status for $requiredStateId did not contain a result object." }

                    $actionCompleted = [bool]$stateAction.ok -and [string]$stateActionCommand.status -eq 'completed' -and [string]::IsNullOrWhiteSpace([string]$stateActionCommand.error)
                    $stateAssertions = [ordered]@{}
                    foreach ($assertionProperty in @($requiredStateStatusAssertions.PSObject.Properties)) {
                        $actual = $stateResult
                        $exists = $true
                        foreach ($segment in ([string]$assertionProperty.Name -split '\.')) {
                            if ($null -eq $actual) { $exists = $false; break }
                            $property = $actual.PSObject.Properties[$segment]
                            if ($null -eq $property) { $exists = $false; break }
                            $actual = $property.Value
                        }
                        $expected = $assertionProperty.Value
                        $matches = if (-not $exists) {
                            $false
                        } elseif ($expected -is [bool]) {
                            $actual -is [bool] -and $actual -eq $expected
                        } elseif ($expected -is [string]) {
                            $actual -is [string] -and $actual -ceq $expected
                        } else {
                            $actual -eq $expected
                        }
                        $stateAssertions[[string]$assertionProperty.Name] = [bool]$matches
                    }
                    $failedStateAssertions = @($stateAssertions.GetEnumerator() | Where-Object { $_.Value -ne $true } | ForEach-Object { $_.Key })
                    $stateReceipt = [ordered]@{
                        schema = 'reign-ui-native-runtime-state-receipt-v1'
                        generatedUtc = [DateTime]::UtcNow.ToString('o')
                        parentTargetId = $target
                        stateId = $requiredStateId
                        setupAction = $requiredStateSetupAction
                        statusReceiptCommand = $requiredStateStatusCommand
                        actionCompleted = $actionCompleted
                        allPassed = $actionCompleted -and $failedStateAssertions.Count -eq 0
                        statusAssertions = $stateAssertions
                        actionReceipt = $stateAction
                        statusReceipt = $stateStatus
                    }
                    $stateReceipt | ConvertTo-Json -Depth 16 | Set-Content -LiteralPath $stateReceiptPath -Encoding UTF8
                    if (-not $actionCompleted -or $failedStateAssertions.Count -gt 0) {
                        $failedNames = @($failedStateAssertions)
                        if (-not $actionCompleted) { $failedNames = @('setup-action-completed') + $failedNames }
                        throw "Runtime state '$requiredStateId' failed assertions: $($failedNames -join ', ')."
                    }
                    $stateUiReady = $true

                    $stateSnapshotReceipt = Invoke-LiveTest -Arguments @('ui-snapshot', '--target', $target, '--json')
                    $installedStateSnapshot = Resolve-SnapshotPath -SnapshotResult $stateSnapshotReceipt
                    Copy-Item -LiteralPath $installedStateSnapshot -Destination $stateSnapshot -Force
                    $stateSnapshotJson = Get-Content -Raw -LiteralPath $stateSnapshot | ConvertFrom-Json
                    if ([int][Math]::Round([double]$stateSnapshotJson.physicalWidth) -ne $width -or [int][Math]::Round([double]$stateSnapshotJson.physicalHeight) -ne $height) {
                        throw "Runtime state snapshot resolution is $($stateSnapshotJson.physicalWidth)x$($stateSnapshotJson.physicalHeight), expected $Resolution."
                    }
                    $stateRuntimeUiScale = [double]$stateSnapshotJson.uiScale
                    if ([double]::IsNaN($stateRuntimeUiScale) -or [double]::IsInfinity($stateRuntimeUiScale) -or $stateRuntimeUiScale -le 0) {
                        throw "Runtime state snapshot UIContext.CustomScale must be positive; received $($stateSnapshotJson.uiScale)."
                    }
                    $stateSnapshotProperties = @($stateSnapshotJson.PSObject.Properties.Name)
                    if ($stateSnapshotProperties -contains 'supportUi') { throw "Runtime state snapshot for $requiredStateId contains previewer-only support UI evidence." }
                    if (-not ($stateSnapshotProperties -contains 'supportUiInjected') -or
                        $stateSnapshotJson.supportUiInjected -isnot [bool] -or
                        $stateSnapshotJson.supportUiInjected -ne $false) {
                        throw "Runtime state snapshot for $requiredStateId does not explicitly prove supportUiInjected=false."
                    }
                    if ($stateSnapshotProperties -contains 'visualSupportUiInjected' -and [bool]$stateSnapshotJson.visualSupportUiInjected) {
                        throw "Runtime state snapshot for $requiredStateId reports visual support UI injection."
                    }
                    foreach ($widgetId in @($requiredStateEvidence.requiredVisibleWidgetIds)) {
                        $visibleWidget = @($stateSnapshotJson.widgets | Where-Object { [string]$_.id -eq [string]$widgetId })
                        if ($visibleWidget.Count -ne 1) {
                            throw "Runtime state snapshot for $requiredStateId does not contain exactly one required widget '$widgetId'."
                        }
                        if ($visibleWidget[0].isVisible -isnot [bool] -or $visibleWidget[0].isVisible -ne $true) { throw "Runtime state snapshot for $requiredStateId reports required widget '$widgetId' hidden." }
                    }
                    foreach ($widgetId in @($requiredStateEvidence.requiredEnabledWidgetIds)) {
                        $requiredWidget = @($stateSnapshotJson.widgets | Where-Object { [string]$_.id -eq [string]$widgetId })
                        if ($requiredWidget.Count -ne 1) { throw "Runtime state snapshot for $requiredStateId does not contain exactly one required enabled widget '$widgetId'." }
                        if ($requiredWidget[0].isEnabled -isnot [bool] -or $requiredWidget[0].isEnabled -ne $true) {
                            throw "Runtime state snapshot for $requiredStateId reports required widget '$widgetId' disabled."
                        }
                    }

                    $stateCaptureArguments = @{
                        ProcessName = 'Bannerlord'
                        OutputPath = $stateScreenshot
                        ExpectedWidth = $width
                        ExpectedHeight = $height
                        AllowOffscreenClientResize = $true
                    }
                    if (-not [string]::IsNullOrWhiteSpace($SteamScreenshotRoot)) { $stateCaptureArguments['SteamScreenshotRoot'] = $SteamScreenshotRoot }
                    $stateCapture = & $capturePath @stateCaptureArguments | Out-String | ConvertFrom-Json
                    if ([int]$stateCapture.width -ne $width -or [int]$stateCapture.height -ne $height) {
                        throw "Verified foreground runtime-state capture is $($stateCapture.width)x$($stateCapture.height), expected $Resolution."
                    }

                    $stateAcceptance = Invoke-StateAcceptanceRecorder -Target $target -StateId $requiredStateId -Screenshot $stateScreenshot -Snapshot $stateSnapshot -StateReceipt $stateReceiptPath
                    $stateRows.Add([ordered]@{
                        parentTargetId = $target
                        targetId = $target
                        stateId = $requiredStateId
                        kind = 'runtime-state'
                        matrix = [string]$requiredStateEvidence.matrix
                        setupAction = $requiredStateSetupAction
                        status = 'staged-for-review'
                        reviewed = $false
                        screenshotPath = [System.IO.Path]::GetFullPath($stateScreenshot)
                        captureMetadataPath = [System.IO.Path]::GetFullPath($stateCaptureMetadata)
                        snapshotPath = [System.IO.Path]::GetFullPath($stateSnapshot)
                        stateReceiptPath = [System.IO.Path]::GetFullPath($stateReceiptPath)
                        configuredUiScale = $configuredUiScale
                        runtimeUiScale = $stateRuntimeUiScale
                        uiReady = $stateUiReady
                        liveRunId = $runId
                        liveReportPath = $liveReportPath
                        acceptanceManifestPath = [string]$stateAcceptance.manifestPath
                        error = ''
                    })
                }
                catch {
                    $stateRows.Add([ordered]@{
                        parentTargetId = $target
                        targetId = $target
                        stateId = $requiredStateId
                        kind = 'runtime-state'
                        matrix = [string]$requiredStateEvidence.matrix
                        setupAction = $requiredStateSetupAction
                        status = 'failed'
                        reviewed = $false
                        screenshotPath = if (Test-Path -LiteralPath $stateScreenshot -PathType Leaf) { [System.IO.Path]::GetFullPath($stateScreenshot) } else { '' }
                        captureMetadataPath = if (Test-Path -LiteralPath $stateCaptureMetadata -PathType Leaf) { [System.IO.Path]::GetFullPath($stateCaptureMetadata) } else { '' }
                        snapshotPath = if (Test-Path -LiteralPath $stateSnapshot -PathType Leaf) { [System.IO.Path]::GetFullPath($stateSnapshot) } else { '' }
                        stateReceiptPath = if (Test-Path -LiteralPath $stateReceiptPath -PathType Leaf) { [System.IO.Path]::GetFullPath($stateReceiptPath) } else { '' }
                        configuredUiScale = $configuredUiScale
                        runtimeUiScale = $stateRuntimeUiScale
                        uiReady = $stateUiReady
                        liveRunId = $runId
                        liveReportPath = $liveReportPath
                        acceptanceManifestPath = ''
                        error = $_.Exception.Message
                    })
                }
            }
        }
        catch {
            $rows.Add([ordered]@{
                targetId = $target
                status = 'failed'
                reviewed = $false
                screenshotPath = if (Test-Path -LiteralPath $screenshot -PathType Leaf) { [System.IO.Path]::GetFullPath($screenshot) } else { '' }
                captureMetadataPath = if (Test-Path -LiteralPath $captureMetadata -PathType Leaf) { [System.IO.Path]::GetFullPath($captureMetadata) } else { '' }
                snapshotPath = if (Test-Path -LiteralPath $snapshot -PathType Leaf) { [System.IO.Path]::GetFullPath($snapshot) } else { '' }
                configuredUiScale = $configuredUiScale
                runtimeUiScale = $runtimeUiScale
                uiReady = $uiReady
                supportOverlayAbsentForCapture = $supportOverlayAbsentForCapture
                providerCallCount = if ($target -eq 'royal-council' -and $null -ne $readinessState) { [int]$readinessState.royalCouncilProviderCallCount } else { $null }
                calibrationFixture = if ($target -eq 'royal-council' -and $null -ne $readinessState) { [bool]$readinessState.royalCouncilCalibrationFixture } else { $null }
                liveRunId = $runId
                liveReportPath = $liveReportPath
                acceptanceManifestPath = ''
                error = $_.Exception.Message
            })
            if ($target -eq $requiredStateParentTarget -and -not $requiredStateRecorded) {
                $stateRows.Add([ordered]@{
                    parentTargetId = $target
                    targetId = $target
                    stateId = $requiredStateId
                    kind = 'runtime-state'
                    matrix = [string]$requiredStateEvidence.matrix
                    setupAction = $requiredStateSetupAction
                    status = 'failed'
                    reviewed = $false
                    screenshotPath = ''
                    captureMetadataPath = ''
                    snapshotPath = ''
                    stateReceiptPath = ''
                    configuredUiScale = $configuredUiScale
                    runtimeUiScale = $null
                    uiReady = $false
                    liveRunId = $runId
                    liveReportPath = $liveReportPath
                    acceptanceManifestPath = ''
                    error = "Parent surface capture failed before runtime state setup: $($_.Exception.Message)"
                })
            }
        }
        finally {
            Close-RunBestEffort -RunId $runId
        }
    }
}
finally {
    if ($startedGame -and -not $KeepGameOpen) {
        try { Invoke-LiveTest -Arguments @('game', 'stop', '--wait', '180', '--json') | Out-Null } catch { }
    }
    $batch = [ordered]@{
        schema = 'reign-ui-native-capture-batch-v1'
        generatedUtc = [DateTime]::UtcNow.ToString('o')
        startedUtc = $startedUtc
        resolution = $Resolution
        uiScale = $UiScale
        configuredUiScale = $configuredUiScale
        bannerlordConfigPath = $bannerlordConfig
        saveName = $SaveName
        providerFree = $true
        writesCampaignState = $false
        savedCampaign = $false
        liveTestBridgeArm = $liveTestBridgeArm
        gameStoppedAfterCapture = $startedGame -and -not $KeepGameOpen
        requiresHumanReview = $true
        surfaceCount = @($rows).Count
        stateCaseCount = @($stateRows).Count
        cases = @($rows)
        stateCases = @($stateRows)
    }
    $batch | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $batchPath -Encoding UTF8
}

$surfaceStaged = @($rows | Where-Object { $_.status -eq 'staged-for-review' })
$stateStaged = @($stateRows | Where-Object { $_.status -eq 'staged-for-review' })
$staged = @($surfaceStaged) + @($stateStaged)
$contactSheetPath = ''
if ($staged.Count -gt 0) {
    $contactSheet = Join-Path $batchRoot 'contact-sheet.png'
    $contactRaw = & $PythonPath $workflowPath 'contact-sheet' '--batch-report' $batchPath '--output' $contactSheet 2>&1 | Out-String
    if ($LASTEXITCODE -ne 0) { throw "Contact-sheet generation failed: $contactRaw" }
    $contact = $contactRaw | ConvertFrom-Json
    $contactSheetPath = [string]$contact.outputPath
}
$surfaceFailed = @($rows | Where-Object { $_.status -ne 'staged-for-review' })
$stateFailed = @($stateRows | Where-Object { $_.status -ne 'staged-for-review' })
$failed = @($surfaceFailed) + @($stateFailed)
$result = [ordered]@{
    ok = $failed.Count -eq 0
    schema = 'reign-ui-native-capture-batch-result-v1'
    batchReportPath = [System.IO.Path]::GetFullPath($batchPath)
    contactSheetPath = $contactSheetPath
    stagedCount = $surfaceStaged.Count
    stateStagedCount = $stateStaged.Count
    failedCount = $failed.Count
    surfaceFailedCount = $surfaceFailed.Count
    stateFailedCount = $stateFailed.Count
    requiresHumanReview = $true
}
$result | ConvertTo-Json -Depth 4
if ($failed.Count -gt 0) { exit 1 }
