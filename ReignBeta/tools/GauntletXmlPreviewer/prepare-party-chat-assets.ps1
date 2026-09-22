param(
    [string]$ModuleRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")),
    [string]$SourceRoot = "C:\Users\speed\Downloads"
)

$ErrorActionPreference = "Stop"

$python = "C:\Users\speed\.cache\codex-runtimes\codex-primary-runtime\dependencies\python\python.exe"
$keyHelper = "C:\Users\speed\.codex\skills\.system\imagegen\scripts\remove_chroma_key.py"
$assembleScript = Join-Path $PSScriptRoot "prepare-party-chat-standalone-assets.py"
$outputDir = Join-Path $ModuleRoot "GUI\SpriteParts\ui_reignbeta_party_chat"
$workDir = Join-Path $env:TEMP "reign-party-chat-standalone"

$sources = [ordered]@{
    shell         = "ChatGPT Image Jul 15, 2026, 07_45_53 PM (1).png"
    header        = "ChatGPT Image Jul 15, 2026, 07_45_53 PM (2).png"
    roster        = "ChatGPT Image Jul 15, 2026, 07_45_53 PM (3).png"
    card_hover    = "ChatGPT Image Jul 15, 2026, 07_45_54 PM (4).png"
    card_normal   = "ChatGPT Image Jul 15, 2026, 07_45_55 PM (5).png"
    card_active   = "ChatGPT Image Jul 15, 2026, 07_45_55 PM (6).png"
    message_left  = "ChatGPT Image Jul 15, 2026, 07_45_56 PM (7).png"
    message_right = "ChatGPT Image Jul 15, 2026, 07_45_56 PM (8).png"
    divider       = "ChatGPT Image Jul 15, 2026, 07_45_57 PM (9).png"
    log           = "ChatGPT Image Jul 15, 2026, 07_45_58 PM (10).png"
    system        = "ChatGPT Image Jul 15, 2026, 07_53_46 PM (3).png"
    input         = "ChatGPT Image Jul 15, 2026, 07_53_46 PM (4).png"
    button_normal = "ChatGPT Image Jul 15, 2026, 07_53_46 PM (6).png"
    button_hover  = "ChatGPT Image Jul 15, 2026, 07_53_47 PM (7).png"
    scroll_track  = "ChatGPT Image Jul 15, 2026, 07_53_47 PM (8).png"
    scroll_handle = "ChatGPT Image Jul 15, 2026, 07_53_47 PM (9).png"
    preview       = "ChatGPT Image Jul 15, 2026, 07_53_47 PM (10).png"
}

foreach ($required in @($python, $keyHelper, $assembleScript)) {
    if (-not (Test-Path -LiteralPath $required)) {
        throw "Required Party Chat asset tool was not found: $required"
    }
}

Remove-Item -LiteralPath $workDir -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $workDir | Out-Null
New-Item -ItemType Directory -Force -Path $outputDir | Out-Null

foreach ($entry in $sources.GetEnumerator()) {
    $sourcePath = Join-Path $SourceRoot $entry.Value
    $destinationPath = Join-Path $workDir ($entry.Key + ".png")
    if (-not (Test-Path -LiteralPath $sourcePath)) {
        throw "Party Chat standalone source was not found: $sourcePath"
    }

    if ($entry.Key -eq "shell") {
        Copy-Item -LiteralPath $sourcePath -Destination $destinationPath -Force
        continue
    }

    & $python $keyHelper `
        --input $sourcePath `
        --out $destinationPath `
        --auto-key border `
        --soft-matte `
        --transparent-threshold 10 `
        --opaque-threshold 120 `
        --edge-feather 0.3 `
        --edge-contract 0 `
        --despill `
        --force
    if ($LASTEXITCODE -ne 0) {
        throw "Chroma-key removal failed for $sourcePath with exit code $LASTEXITCODE."
    }
}

& $python $assembleScript --input-dir $workDir --output-dir $outputDir
if ($LASTEXITCODE -ne 0) {
    throw "Standalone Party Chat asset assembly failed with exit code $LASTEXITCODE."
}

Remove-Item -LiteralPath $workDir -Recurse -Force -ErrorAction SilentlyContinue
Write-Host "Prepared standalone Party Chat sprites in $outputDir"
