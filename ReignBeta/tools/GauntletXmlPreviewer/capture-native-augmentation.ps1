param(
    [Parameter(Mandatory = $true)][string]$Target,
    [Parameter(Mandatory = $true)][ValidatePattern('^\d+x\d+$')][string]$Resolution,
    [Parameter(Mandatory = $true)][double]$UiScale,
    [Parameter(Mandatory = $true)][string]$LiveTestPath,
    [Parameter(Mandatory = $true)][string]$InstalledRoot,
    [Parameter(Mandatory = $true)][string]$RenderReport,
    [Parameter(Mandatory = $true)][string]$OutputRoot,
    [string]$PythonPath = 'python',
    [string]$SteamScreenshotRoot = '',
    [switch]$AllowOffscreenClientResize
)

$ErrorActionPreference = 'Stop'
$toolRoot = $PSScriptRoot
$catalogPath = Join-Path $toolRoot 'calibration\ui-catalog.json'
$workflowPath = Join-Path $toolRoot 'calibration_workflow.py'
$capturePath = Join-Path $toolRoot 'capture-window.ps1'
$liveTest = [System.IO.Path]::GetFullPath($LiveTestPath)
$installed = [System.IO.Path]::GetFullPath($InstalledRoot)
$render = [System.IO.Path]::GetFullPath($RenderReport)
$output = [System.IO.Path]::GetFullPath($OutputRoot)
$catalog = Get-Content -Raw -LiteralPath $catalogPath | ConvertFrom-Json

if (-not (Test-Path -LiteralPath $liveTest -PathType Leaf)) { throw "Live-test controller is missing: $liveTest" }
if (-not (Test-Path -LiteralPath $installed -PathType Container)) { throw "Installed ReignBeta root is missing: $installed" }
if (-not (Test-Path -LiteralPath $render -PathType Leaf)) { throw "Rendered-preview report is missing: $render" }
if (-not (Get-Command -Name $PythonPath -ErrorAction SilentlyContinue)) { throw "Python executable is unavailable: $PythonPath" }

$surface = @($catalog.nativeAugmentations.targets | Where-Object { [string]$_.id -eq $Target })
if ($surface.Count -ne 1) { throw "Target '$Target' is not a cataloged native Bannerlord augmentation." }
$matrix = @($catalog.targetMatrix | Where-Object {
    $_.resolution -eq $Resolution -and @($_.uiScales | Where-Object { [Math]::Abs([double]$_ - $UiScale) -lt 0.0001 }).Count -gt 0
})
if ($matrix.Count -ne 1) { throw "$Resolution@$UiScale is not a required catalog matrix case." }

$statusRaw = & $liveTest 'game' 'status' '--json' 2>&1 | Out-String
if ($LASTEXITCODE -ne 0) { throw "Could not read Bannerlord lifecycle status: $statusRaw" }
$status = $statusRaw | ConvertFrom-Json
if (-not $status.running) { throw 'Bannerlord must already be running with the exact production screen open before capturing a native augmentation.' }

$width, $height = $Resolution.Split('x') | ForEach-Object { [int]$_ }
$caseKey = "$Resolution-ui$([int][Math]::Round($UiScale * 100))"
$caseRoot = Join-Path (Join-Path $output $Target) $caseKey
[System.IO.Directory]::CreateDirectory($caseRoot) | Out-Null
$screenshot = Join-Path $caseRoot 'native.png'
$captureArguments = @{
    ProcessName = 'Bannerlord'
    OutputPath = $screenshot
    ExpectedWidth = $width
    ExpectedHeight = $height
    AllowOffscreenClientResize = $AllowOffscreenClientResize
}
if (-not [string]::IsNullOrWhiteSpace($SteamScreenshotRoot)) {
    $captureArguments['SteamScreenshotRoot'] = $SteamScreenshotRoot
}
$capture = & $capturePath @captureArguments | Out-String | ConvertFrom-Json
if ([int]$capture.width -ne $width -or [int]$capture.height -ne $height) {
    throw "Verified foreground capture is $($capture.width)x$($capture.height), expected $Resolution."
}

$acceptanceRoot = $output
$uiScaleText = [string]::Format([Globalization.CultureInfo]::InvariantCulture, '{0:0.##}', $UiScale)
$raw = & $PythonPath $workflowPath 'accept-native-case' `
    '--target' $Target '--resolution' $Resolution '--ui-scale' $uiScaleText `
    '--screenshot' $screenshot '--capture-metadata' ($screenshot + '.json') `
    '--evidence-root' $acceptanceRoot '--installed-root' $installed `
    '--render-report' $render '--diagnostic-count' '0' 2>&1 | Out-String
if ($LASTEXITCODE -ne 0) { throw "Native augmentation acceptance staging failed: $raw" }
$acceptance = $raw | ConvertFrom-Json

$receipt = [ordered]@{
    schema = 'reign-ui-native-augmentation-capture-v1'
    generatedUtc = [DateTime]::UtcNow.ToString('o')
    targetId = $Target
    nativeTarget = [string]$surface[0].target
    resolution = $Resolution
    uiScale = $UiScale
    screenshotPath = [System.IO.Path]::GetFullPath($screenshot)
    captureMetadataPath = [System.IO.Path]::GetFullPath($screenshot + '.json')
    acceptanceManifestPath = [string]$acceptance.manifestPath
    reviewed = $false
    requiresHumanReview = $true
    providerFree = $true
    inMemoryCampaignStateMayChange = $true
    savedCampaign = $false
    campaignPersistence = $false
}
$receiptPath = Join-Path $caseRoot 'capture-receipt.json'
$receipt | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $receiptPath -Encoding UTF8
[ordered]@{
    ok = $true
    receiptPath = [System.IO.Path]::GetFullPath($receiptPath)
    screenshotPath = $receipt.screenshotPath
    acceptanceManifestPath = $receipt.acceptanceManifestPath
    requiresHumanReview = $true
} | ConvertTo-Json -Depth 4
