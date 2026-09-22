param(
    [int]$Port = 5177,
    [int]$CodexAgentPort = 5178,
    [int]$CodexBridgePort = 5179,
    [switch]$NoBrowser,
    [switch]$NoCodexAgent,
    [string]$BannerlordRoot = 'D:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord'
)

$ErrorActionPreference = "Stop"
$root = Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..\..")
$workspaceRoot = [IO.Path]::GetFullPath((Join-Path $root ".."))
$installedModuleRoot = Join-Path $BannerlordRoot "Modules\ReignBeta"
$hostName = "127.0.0.1"
$url = "http://$hostName`:$Port/tools/GauntletXmlPreviewer/index.html"
$codexAgentUrl = "ws://$hostName`:$CodexAgentPort"
$codexBrowserUrl = "ws://$hostName`:$CodexBridgePort"
$codexAgentProcess = $null
$codexBridgeProcess = $null
$codexAgentError = ''
$uiCatalogPath = Join-Path $PSScriptRoot "calibration\ui-catalog.json"

function Get-UiCatalog {
    if (-not (Test-Path -LiteralPath $uiCatalogPath -PathType Leaf)) { throw "The Reign UI catalog is missing at '$uiCatalogPath'." }
    return Get-Content -Raw -LiteralPath $uiCatalogPath | ConvertFrom-Json
}

function Get-OwnedPrefabFileNames {
    return @(
        (Get-UiCatalog).interfaces |
            ForEach-Object { [IO.Path]::GetFileName(([string]$_.prefab).Replace('/', '\')) } |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
            Sort-Object -Unique
    )
}

function Test-OwnedPrefabFileName {
    param([string]$FileName)

    if ([IO.Path]::GetFileName($FileName) -ne $FileName) { return $false }
    return @((Get-OwnedPrefabFileNames) | Where-Object { $_ -eq $FileName }).Count -gt 0
}

function Get-CatalogInterfaceByMovie {
    param([string]$MovieName)

    return @((Get-UiCatalog).interfaces | Where-Object { [string]$_.movie -eq $MovieName }) | Select-Object -First 1
}

function Get-ContentType {
    param([string]$Path)

    switch ([IO.Path]::GetExtension($Path).ToLowerInvariant()) {
        ".html" { "text/html; charset=utf-8"; break }
        ".htm"  { "text/html; charset=utf-8"; break }
        ".js"   { "application/javascript; charset=utf-8"; break }
        ".css"  { "text/css; charset=utf-8"; break }
        ".xml"  { "application/xml; charset=utf-8"; break }
        ".json" { "application/json; charset=utf-8"; break }
        ".png"  { "image/png"; break }
        ".jpg"  { "image/jpeg"; break }
        ".jpeg" { "image/jpeg"; break }
        ".gif"  { "image/gif"; break }
        ".svg"  { "image/svg+xml; charset=utf-8"; break }
        default { "application/octet-stream" }
    }
}

function Write-Response {
    param(
        [System.Net.Sockets.NetworkStream]$Stream,
        [int]$StatusCode,
        [string]$StatusText,
        [byte[]]$Body,
        [string]$ContentType = "text/plain; charset=utf-8"
    )

    $header = "HTTP/1.1 $StatusCode $StatusText`r`nContent-Type: $ContentType`r`nContent-Length: $($Body.Length)`r`nConnection: close`r`nCache-Control: no-store`r`nAccess-Control-Allow-Origin: *`r`n`r`n"
    $headerBytes = [Text.Encoding]::UTF8.GetBytes($header)
    $Stream.Write($headerBytes, 0, $headerBytes.Length)
    if ($Body.Length -gt 0) {
        $Stream.Write($Body, 0, $Body.Length)
    }
}

function Write-JsonResponse {
    param(
        [System.Net.Sockets.NetworkStream]$Stream,
        [int]$StatusCode,
        [string]$StatusText,
        [object]$Value
    )

    $json = $Value | ConvertTo-Json -Depth 24 -Compress
    $body = [Text.Encoding]::UTF8.GetBytes($json)
    Write-Response -Stream $Stream -StatusCode $StatusCode -StatusText $StatusText -Body $body -ContentType "application/json; charset=utf-8"
}

function Read-HttpRequest {
    param([System.Net.Sockets.NetworkStream]$Stream)

    $maximumRequestBytes = 4MB
    $memory = [IO.MemoryStream]::new()
    $buffer = New-Object byte[] 8192
    $headerEnd = -1
    $contentLength = 0
    $lines = $null

    while ($true) {
        $read = $Stream.Read($buffer, 0, $buffer.Length)
        if ($read -le 0) { break }
        $memory.Write($buffer, 0, $read)
        if ($memory.Length -gt $maximumRequestBytes) { throw "Request exceeds the 4 MB calibration limit." }

        $raw = $memory.ToArray()
        if ($headerEnd -lt 0) {
            $ascii = [Text.Encoding]::ASCII.GetString($raw)
            $headerEnd = $ascii.IndexOf("`r`n`r`n", [StringComparison]::Ordinal)
            if ($headerEnd -ge 0) {
                $lines = $ascii.Substring(0, $headerEnd) -split "`r`n"
                foreach ($line in $lines | Select-Object -Skip 1) {
                    if ($line -match '^Content-Length:\s*(\d+)\s*$') { $contentLength = [int]$Matches[1] }
                }
                if ($contentLength -gt $maximumRequestBytes) { throw "Request body exceeds the 4 MB calibration limit." }
            }
        }

        if ($headerEnd -ge 0 -and $raw.Length -ge $headerEnd + 4 + $contentLength) { break }
    }

    if ($headerEnd -lt 0 -or -not $lines -or $lines.Count -eq 0) { throw "Malformed HTTP request." }
    $requestParts = $lines[0] -split " "
    if ($requestParts.Length -lt 2) { throw "Malformed HTTP request line." }
    $allBytes = $memory.ToArray()
    $bodyBytes = New-Object byte[] $contentLength
    if ($contentLength -gt 0) { [Array]::Copy($allBytes, $headerEnd + 4, $bodyBytes, 0, $contentLength) }
    return [ordered]@{
        Method = $requestParts[0].ToUpperInvariant()
        Path = $requestParts[1]
        Body = [Text.Encoding]::UTF8.GetString($bodyBytes)
    }
}

function Get-TextSha256 {
    param([string]$Text)

    $algorithm = [Security.Cryptography.SHA256]::Create()
    try {
        $bytes = [Text.Encoding]::UTF8.GetBytes($Text)
        return ([BitConverter]::ToString($algorithm.ComputeHash($bytes))).Replace("-", "").ToLowerInvariant()
    } finally {
        $algorithm.Dispose()
    }
}

function Assert-CalibrationPatch {
    param([object]$Patch, [switch]$ForApply)

    if ($null -eq $Patch -or $Patch.schema -ne "reign-ui-calibration-patch-v1") { throw "Unsupported calibration patch schema." }
    if ([string]::IsNullOrWhiteSpace([string]$Patch.fileName)) { throw "The patch does not identify a prefab file." }
    if ([IO.Path]::GetFileName([string]$Patch.fileName) -ne [string]$Patch.fileName) { throw "Calibration patches may target prefab file names only." }
    if ($null -eq $Patch.edits -or @($Patch.edits).Count -eq 0) { throw "The calibration patch contains no edits." }
    foreach ($edit in @($Patch.edits)) {
        if ([string]::IsNullOrWhiteSpace([string]$edit.path)) { throw "Every calibration edit requires an XML path." }
        $operation = if ([string]::IsNullOrWhiteSpace([string]$edit.operation)) { "attributes" } else { [string]$edit.operation }
        if ($operation -notin @("attributes", "delete")) { throw "Unsupported calibration operation '$operation'." }
        if ($operation -eq "attributes" -and $null -eq $edit.attributes) { throw "Attribute edits require an attributes object." }
    }
    if ($ForApply) {
        if ($Patch.confirmation -notin @("apply Reign XML calibration", "apply and install Reign XML calibration")) { throw "Explicit source-apply confirmation is missing." }
        if (-not (Test-OwnedPrefabFileName -FileName ([string]$Patch.fileName))) { throw "Only catalog-owned Reign prefab XML can be changed by the calibration endpoint." }
        if ([string]::IsNullOrWhiteSpace([string]$Patch.sourceSha256)) { throw "The source hash is missing." }
    }
}

function Get-XmlOpeningTagMap {
    param([string]$Text)

    $result = @{}
    $stack = [Collections.Generic.List[object]]::new()
    $stack.Add([pscustomobject]@{ Path = ""; Counts = @{}; Location = $null })
    $pattern = [regex]::new('<(?<closing>/)?(?<name>[A-Za-z_][A-Za-z0-9_.:-]*)(?<attributes>[^<>]*?)(?<self>/)?>', [Text.RegularExpressions.RegexOptions]::Singleline)

    foreach ($match in $pattern.Matches($Text)) {
        if ($match.Groups['closing'].Success) {
            if ($stack.Count -gt 1) {
                $frame = $stack[$stack.Count - 1]
                $frame.Location.ElementLength = ($match.Index + $match.Length) - $frame.Location.Index
                $stack.RemoveAt($stack.Count - 1)
            }
            continue
        }

        $parent = $stack[$stack.Count - 1]
        $name = $match.Groups['name'].Value
        $nextIndex = if ($parent.Counts.ContainsKey($name)) { [int]$parent.Counts[$name] + 1 } else { 1 }
        $parent.Counts[$name] = $nextIndex
        $path = "$($parent.Path)/$name`[$nextIndex`]"
        $location = [pscustomobject]@{ Index = $match.Index; OpeningLength = $match.Length; ElementLength = $match.Length; Tag = $name }
        $result[$path] = $location
        if (-not $match.Groups['self'].Success) { $stack.Add([pscustomobject]@{ Path = $path; Counts = @{}; Location = $location }) }
    }
    return $result
}

function Set-OpeningTagAttributes {
    param([string]$OpeningTag, [object]$Attributes)

    $updated = $OpeningTag
    foreach ($property in $Attributes.PSObject.Properties) {
        if ($property.Name -notmatch '^[A-Za-z_][A-Za-z0-9_.:-]*$') { throw "Invalid XML attribute name '$($property.Name)'." }
        $attributePattern = '(?s)\s+' + [regex]::Escape($property.Name) + '\s*=\s*(["''])(.*?)\1'
        if ($null -eq $property.Value -or [string]$property.Value -eq "") {
            $updated = [regex]::Replace($updated, $attributePattern, "", 1)
            continue
        }

        $encoded = [Security.SecurityElement]::Escape([string]$property.Value)
        if ([regex]::IsMatch($updated, $attributePattern)) {
            $replacement = " $($property.Name)=`"$encoded`""
            $updated = [regex]::Replace($updated, $attributePattern, $replacement, 1)
        } else {
            $insertion = " $($property.Name)=`"$encoded`""
            $updated = [regex]::Replace($updated, '\s*/?>$', { param($match) "$insertion$($match.Value)" }, 1)
        }
    }
    return $updated
}

function Get-XmlDeletionRange {
    param([string]$Text, [object]$Location)

    $start = [int]$Location.Index
    $length = [int]$Location.ElementLength
    $elementEnd = $start + $length
    $lineStart = if ($start -gt 0) { $Text.LastIndexOf("`n", $start - 1) + 1 } else { 0 }
    $lineEnd = $Text.IndexOf("`n", $elementEnd)
    $before = $Text.Substring($lineStart, $start - $lineStart)
    $afterEnd = if ($lineEnd -ge 0) { $lineEnd } else { $Text.Length }
    $after = $Text.Substring($elementEnd, $afterEnd - $elementEnd)

    if ([string]::IsNullOrWhiteSpace($before) -and [string]::IsNullOrWhiteSpace($after)) {
        $start = $lineStart
        $length = if ($lineEnd -ge 0) { ($lineEnd + 1) - $lineStart } else { $Text.Length - $lineStart }
    }

    return [pscustomobject]@{ Index = $start; Length = $length }
}

function Save-CalibrationPatch {
    param([object]$Patch)

    Assert-CalibrationPatch -Patch $Patch
    $patchRoot = Join-Path $workspaceRoot ".codex-build\ui-preview\calibration-patches"
    [IO.Directory]::CreateDirectory($patchRoot) | Out-Null
    $baseName = [IO.Path]::GetFileNameWithoutExtension([string]$Patch.fileName) -replace '[^A-Za-z0-9_.-]', '_'
    $stamp = [DateTime]::UtcNow.ToString("yyyyMMdd-HHmmss-fff")
    $path = Join-Path $patchRoot "$baseName-$stamp.calibration.json"
    $json = $Patch | ConvertTo-Json -Depth 24
    [IO.File]::WriteAllText($path, $json, [Text.UTF8Encoding]::new($false))
    return $path.Substring($workspaceRoot.Length).TrimStart('\')
}

function Apply-CalibrationPatch {
    param([object]$Patch)

    Assert-CalibrationPatch -Patch $Patch -ForApply
    $prefabRoot = [IO.Path]::GetFullPath((Join-Path $root "GUI\Prefabs"))
    $sourcePath = [IO.Path]::GetFullPath((Join-Path $prefabRoot ([string]$Patch.fileName)))
    if (-not $sourcePath.StartsWith($prefabRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw "The target is outside the Reign prefab directory." }
    if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) { throw "Prefab '$($Patch.fileName)' was not found." }

    $source = [IO.File]::ReadAllText($sourcePath)
    $actualHash = Get-TextSha256 $source
    if ($actualHash -ne ([string]$Patch.sourceSha256).ToLowerInvariant()) { throw "The prefab changed after calibration began. Reload it before applying; no source was changed." }

    $openingTags = Get-XmlOpeningTagMap $source
    $replacements = @()
    foreach ($edit in @($Patch.edits)) {
        $location = $openingTags[[string]$edit.path]
        if ($null -eq $location) { throw "XML path '$($edit.path)' no longer exists in the source." }
        if (-not [string]::IsNullOrWhiteSpace([string]$edit.tag) -and $location.Tag -ne [string]$edit.tag) { throw "XML path '$($edit.path)' no longer identifies the expected widget type." }
        $operation = if ([string]::IsNullOrWhiteSpace([string]$edit.operation)) { "attributes" } else { [string]$edit.operation }
        if ($operation -eq "delete") {
            $range = Get-XmlDeletionRange -Text $source -Location $location
            $replacements += [pscustomobject]@{ Index = $range.Index; Length = $range.Length; Text = ""; Path = [string]$edit.path }
        } else {
            $openingTag = $source.Substring($location.Index, $location.OpeningLength)
            $replacement = Set-OpeningTagAttributes -OpeningTag $openingTag -Attributes $edit.attributes
            $replacements += [pscustomobject]@{ Index = $location.Index; Length = $location.OpeningLength; Text = $replacement; Path = [string]$edit.path }
        }
    }

    $ascending = @($replacements | Sort-Object Index)
    for ($index = 1; $index -lt $ascending.Count; $index++) {
        $prior = $ascending[$index - 1]
        $current = $ascending[$index]
        if ($current.Index -lt $prior.Index + $prior.Length) {
            throw "Calibration operations overlap at '$($prior.Path)' and '$($current.Path)'. Undo the parent or child change before applying."
        }
    }

    $updated = $source
    foreach ($replacement in $replacements | Sort-Object Index -Descending) {
        $updated = $updated.Remove($replacement.Index, $replacement.Length).Insert($replacement.Index, $replacement.Text)
    }

    $backupRoot = Join-Path $workspaceRoot ".codex-build\ui-preview\calibration-backups"
    [IO.Directory]::CreateDirectory($backupRoot) | Out-Null
    $stamp = [DateTime]::UtcNow.ToString("yyyyMMdd-HHmmss-fff")
    $backupPath = Join-Path $backupRoot "$([IO.Path]::GetFileNameWithoutExtension($sourcePath))-$stamp.xml"
    $temporaryPath = Join-Path ([IO.Path]::GetDirectoryName($sourcePath)) ".$([IO.Path]::GetFileName($sourcePath)).calibration-$([Guid]::NewGuid().ToString('N')).tmp"
    [IO.File]::WriteAllText($temporaryPath, $updated, [Text.UTF8Encoding]::new($false))
    try {
        [IO.File]::Replace($temporaryPath, $sourcePath, $backupPath)
    } finally {
        if (Test-Path -LiteralPath $temporaryPath) { [IO.File]::Delete($temporaryPath) }
    }

    return [ordered]@{
        applied = @($Patch.edits).Count
        backup = $backupPath.Substring($workspaceRoot.Length).TrimStart('\')
        sourceSha256 = Get-TextSha256 $updated
    }
}

function Test-BannerlordRunning {
    return [bool](Get-Process -ErrorAction SilentlyContinue | Where-Object {
        $_.ProcessName -like 'Bannerlord*' -or $_.ProcessName -eq 'TaleWorlds.MountAndBlade.Launcher'
    } | Select-Object -First 1)
}

function Resolve-PrefabPaths {
    param([string]$FileName)

    if (-not (Test-OwnedPrefabFileName -FileName $FileName)) { throw "A catalog-owned Reign prefab file name is required." }
    $sourceRoot = [IO.Path]::GetFullPath((Join-Path $root "GUI\Prefabs"))
    $sourcePath = [IO.Path]::GetFullPath((Join-Path $sourceRoot $FileName))
    if (-not $sourcePath.StartsWith($sourceRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw "The source prefab path is outside the Reign prefab directory." }
    if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) { throw "Prefab '$FileName' was not found." }
    $installedRoot = [IO.Path]::GetFullPath((Join-Path $installedModuleRoot "GUI\Prefabs"))
    $installedPath = [IO.Path]::GetFullPath((Join-Path $installedRoot $FileName))
    if (-not $installedPath.StartsWith($installedRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw "The installed prefab path is outside the Reign module." }
    return [pscustomobject]@{ Source = $sourcePath; Installed = $installedPath; InstalledRoot = $installedRoot }
}

function Get-PrefabSyncStatus {
    param([string]$FileName)

    $paths = Resolve-PrefabPaths -FileName $FileName
    $sourceText = [IO.File]::ReadAllText($paths.Source)
    $sourceHash = Get-TextSha256 $sourceText
    $installedExists = Test-Path -LiteralPath $paths.Installed -PathType Leaf
    $installedHash = if ($installedExists) { Get-TextSha256 ([IO.File]::ReadAllText($paths.Installed)) } else { '' }
    return [ordered]@{
        schema = 'reign-ui-prefab-sync-v1'
        fileName = $FileName
        catalogOwned = $true
        sourceSha256 = $sourceHash
        installedSha256 = $installedHash
        installedExists = $installedExists
        inSync = $installedExists -and $sourceHash -eq $installedHash
        bannerlordRunning = Test-BannerlordRunning
        installedModuleExists = Test-Path -LiteralPath $installedModuleRoot -PathType Container
        restartRequired = $installedExists -and $sourceHash -ne $installedHash
    }
}

function Get-AllPrefabSyncStatus {
    $rows = @(Get-OwnedPrefabFileNames | ForEach-Object { Get-PrefabSyncStatus -FileName $_ })
    return [ordered]@{
        schema = 'reign-ui-prefab-sync-summary-v1'
        prefabCount = $rows.Count
        matchingCount = @($rows | Where-Object { $_.inSync }).Count
        staleCount = @($rows | Where-Object { $_.installedExists -and -not $_.inSync }).Count
        missingCount = @($rows | Where-Object { -not $_.installedExists }).Count
        bannerlordRunning = Test-BannerlordRunning
        installedModuleExists = Test-Path -LiteralPath $installedModuleRoot -PathType Container
        prefabs = $rows
    }
}

function Get-NativeParityReadiness {
    $auditPath = Join-Path $PSScriptRoot 'audit-native-parity.mjs'
    $renderReportRoot = Join-Path $workspaceRoot '.codex-build\ui-preview'
    $renderReportPath = Get-ChildItem -LiteralPath $renderReportRoot -Recurse -Filter 'rendered-preview-audit.json' -File -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTimeUtc -Descending |
        Select-Object -First 1 -ExpandProperty FullName
    $evidenceRoot = Join-Path $workspaceRoot '.codex-build\ui-calibration\native-parity'
    $outputPath = Join-Path $workspaceRoot '.codex-build\ui-preview\native-parity-readiness.json'
    if (-not (Test-Path -LiteralPath $auditPath -PathType Leaf)) { throw "The native parity auditor is missing at '$auditPath'." }
    if ([string]::IsNullOrWhiteSpace($renderReportPath) -or -not (Test-Path -LiteralPath $renderReportPath -PathType Leaf)) { throw "Run the rendered preview audit before requesting native parity readiness." }
    $node = Get-Command node -ErrorAction Stop | Select-Object -First 1
    $arguments = @(
        $auditPath,
        '--render-report', $renderReportPath,
        '--installed-root', $installedModuleRoot,
        '--evidence-root', $evidenceRoot,
        '--output', $outputPath
    )
    $auditOutput = & $node.Source @arguments 2>&1
    if ($LASTEXITCODE -ne 0) { throw "Native parity audit failed: $($auditOutput -join [Environment]::NewLine)" }
    if (-not (Test-Path -LiteralPath $outputPath -PathType Leaf)) { throw "Native parity audit did not write its readiness report." }
    return Get-Content -Raw -LiteralPath $outputPath | ConvertFrom-Json
}

function Install-Prefab {
    param([object]$Request)

    if ($null -eq $Request -or $Request.confirmation -notin @('install Reign XML preview change', 'apply and install Reign XML calibration')) { throw "Explicit game-install confirmation is missing." }
    $fileName = [string]$Request.fileName
    $paths = Resolve-PrefabPaths -FileName $fileName
    if (-not (Test-Path -LiteralPath $installedModuleRoot -PathType Container)) { throw "The installed ReignBeta module was not found at '$installedModuleRoot'." }
    $bannerlordRunning = Test-BannerlordRunning
    $sourceText = [IO.File]::ReadAllText($paths.Source)
    $sourceHash = Get-TextSha256 $sourceText
    if ([string]::IsNullOrWhiteSpace([string]$Request.sourceSha256) -or $sourceHash -ne ([string]$Request.sourceSha256).ToLowerInvariant()) {
        throw "The workspace prefab changed after the install request was prepared. Reload it before installing; no game file was changed."
    }

    [IO.Directory]::CreateDirectory($paths.InstalledRoot) | Out-Null
    $backupPath = ''
    if (Test-Path -LiteralPath $paths.Installed -PathType Leaf) {
        $backupRoot = Join-Path $installedModuleRoot 'UiCalibration\previewer-backups'
        [IO.Directory]::CreateDirectory($backupRoot) | Out-Null
        $stamp = [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fff')
        $backupPath = Join-Path $backupRoot "$([IO.Path]::GetFileNameWithoutExtension($fileName))-$stamp.xml"
        [IO.File]::Copy($paths.Installed, $backupPath, $false)
    }

    $temporaryPath = Join-Path $paths.InstalledRoot ".$fileName.previewer-$([Guid]::NewGuid().ToString('N')).tmp"
    $replacementRollbackPath = Join-Path $paths.InstalledRoot ".$fileName.previewer-replace-$([Guid]::NewGuid().ToString('N')).bak"
    [IO.File]::WriteAllText($temporaryPath, $sourceText, [Text.UTF8Encoding]::new($false))
    try {
        if (Test-Path -LiteralPath $paths.Installed -PathType Leaf) { [IO.File]::Replace($temporaryPath, $paths.Installed, $replacementRollbackPath) }
        else { [IO.File]::Move($temporaryPath, $paths.Installed) }
    } finally {
        if (Test-Path -LiteralPath $temporaryPath) { [IO.File]::Delete($temporaryPath) }
        if (Test-Path -LiteralPath $replacementRollbackPath) { [IO.File]::Delete($replacementRollbackPath) }
    }
    $installedHash = Get-TextSha256 ([IO.File]::ReadAllText($paths.Installed))
    if ($installedHash -ne $sourceHash) { throw "Installed prefab verification failed after replacement." }
    return [ordered]@{
        installed = $true
        fileName = $fileName
        sourceSha256 = $sourceHash
        installedSha256 = $installedHash
        backup = $backupPath
        bannerlordRunning = $bannerlordRunning
        screenReloadRequired = $bannerlordRunning
        restartRequired = $false
    }
}

function Start-CodexPreviewAgent {
    if ($NoCodexAgent) { $script:codexAgentError = 'Disabled by -NoCodexAgent.'; return }
    try {
        if (Test-LoopbackPortInUse -PortNumber $CodexAgentPort) { throw "Port $CodexAgentPort is already in use; the previewer will not attach to an unknown service." }
        if (Test-LoopbackPortInUse -PortNumber $CodexBridgePort) { throw "Port $CodexBridgePort is already in use; the previewer will not attach to an unknown browser bridge." }
        $codex = Get-Command codex -ErrorAction Stop | Select-Object -First 1
        $script:codexAgentProcess = Start-Process -FilePath $codex.Source -ArgumentList @('app-server', '--listen', $codexAgentUrl) -WindowStyle Hidden -PassThru
        if (-not (Wait-LoopbackPort -PortNumber $CodexAgentPort -Process $script:codexAgentProcess)) { throw "Codex App Server did not begin listening on $codexAgentUrl." }

        $node = Get-Command node -ErrorAction Stop | Select-Object -First 1
        $bridgeScript = Join-Path $PSScriptRoot 'codex-preview-websocket-proxy.mjs'
        $quotedBridgeScript = '"' + $bridgeScript + '"'
        $script:codexBridgeProcess = Start-Process -FilePath $node.Source -ArgumentList @($quotedBridgeScript, '--listen-port', [string]$CodexBridgePort, '--upstream', $codexAgentUrl) -WindowStyle Hidden -PassThru
        if (-not (Wait-LoopbackPort -PortNumber $CodexBridgePort -Process $script:codexBridgeProcess)) { throw "Codex browser bridge did not begin listening on $codexBrowserUrl." }
    } catch {
        $script:codexAgentError = $_.Exception.Message
        if ($script:codexBridgeProcess -and -not $script:codexBridgeProcess.HasExited) { Stop-Process -Id $script:codexBridgeProcess.Id -Force }
        if ($script:codexAgentProcess -and -not $script:codexAgentProcess.HasExited) { Stop-Process -Id $script:codexAgentProcess.Id -Force }
        $script:codexBridgeProcess = $null
        $script:codexAgentProcess = $null
    }
}

function Test-LoopbackPortInUse {
    param([int]$PortNumber)

    $probe = [Net.Sockets.TcpClient]::new()
    try {
        $connection = $probe.ConnectAsync($hostName, $PortNumber)
        if (-not $connection.Wait(150)) { return $false }
        return $connection.Status -eq [Threading.Tasks.TaskStatus]::RanToCompletion
    } catch {
        return $false
    } finally {
        $probe.Dispose()
    }
}

function Wait-LoopbackPort {
    param([int]$PortNumber, [Diagnostics.Process]$Process)

    for ($attempt = 0; $attempt -lt 30; $attempt++) {
        Start-Sleep -Milliseconds 100
        if ($Process.HasExited) { return $false }
        if (Test-LoopbackPortInUse -PortNumber $PortNumber) { return $true }
    }
    return $false
}

function Resolve-RequestPath {
    param([string]$RequestPath)

    $pathOnly = $RequestPath.Split("?")[0]
    if ([string]::IsNullOrWhiteSpace($pathOnly) -or $pathOnly -eq "/") {
        $pathOnly = "/tools/GauntletXmlPreviewer/index.html"
    }

    $decoded = [Uri]::UnescapeDataString($pathOnly).TrimStart("/")
    $decoded = $decoded -replace "/", [IO.Path]::DirectorySeparatorChar
    $combined = Join-Path $root $decoded
    $full = [IO.Path]::GetFullPath($combined)
    $rootFull = [IO.Path]::GetFullPath($root)

    if (-not $full.StartsWith($rootFull, [StringComparison]::OrdinalIgnoreCase)) {
        return $null
    }

    return $full
}

function Get-PrefabLabel {
    param([string]$FileName)

    $name = [IO.Path]::GetFileNameWithoutExtension($FileName)
    $name = $name -replace '^Reign', ''
    $name = $name -replace 'Screen$', ''
    return ($name -creplace '([a-z0-9])([A-Z])', '$1 $2').Trim()
}

function Get-ReignPrefabCatalog {
    $prefabRoot = Join-Path $root "GUI\Prefabs"
    $catalogInterfaces = @((Get-UiCatalog).interfaces)
    return @(
        Get-ChildItem -LiteralPath $prefabRoot -File -Filter "*.xml" |
            Sort-Object Name |
            ForEach-Object {
                $fileName = $_.Name
                $entry = @($catalogInterfaces | Where-Object { [IO.Path]::GetFileName(([string]$_.prefab).Replace('/', '\')) -eq $fileName }) | Select-Object -First 1
                [ordered]@{
                    fileName = $fileName
                    label = if ($entry -and -not [string]::IsNullOrWhiteSpace([string]$entry.label)) { [string]$entry.label } else { Get-PrefabLabel $fileName }
                    path = "../../GUI/Prefabs/$fileName"
                    kind = if ($entry -and $entry.supportUi) { "auxiliary" } else { "standalone" }
                }
            }
    )
}

function Get-RequestQueryValue {
    param([string]$RequestPath, [string]$Name)

    $uri = [Uri]::new("http://127.0.0.1$RequestPath")
    foreach ($pair in $uri.Query.TrimStart('?').Split('&', [StringSplitOptions]::RemoveEmptyEntries)) {
        $parts = $pair.Split('=', 2)
        if ([Uri]::UnescapeDataString($parts[0]) -eq $Name) {
            if ($parts.Count -gt 1) { return [Uri]::UnescapeDataString($parts[1].Replace('+', ' ')) }
            return ''
        }
    }
    return ''
}

function Get-NativeEvidence {
    param([string]$MovieName, [string]$Resolution = '', [string]$UiScale = '')

    if ($MovieName -notmatch '^[A-Za-z][A-Za-z0-9_-]{1,100}$') { throw "A valid cataloged Reign movie name is required." }
    $entry = Get-CatalogInterfaceByMovie -MovieName $MovieName
    if (-not $entry) { throw "Movie '$MovieName' is not present in the Reign UI catalog." }
    $interfaceId = ''
    if (-not $entry.supportUi) { $interfaceId = [string]$entry.id }

    $requestedUiScale = 0.0
    $hasExactCaseRequest = $interfaceId -and $Resolution -match '^\d{3,5}x\d{3,5}$' -and
        [double]::TryParse($UiScale, [Globalization.NumberStyles]::Float, [Globalization.CultureInfo]::InvariantCulture, [ref]$requestedUiScale) -and
        $requestedUiScale -gt 0
    $exactCaseRoot = ''
    $snapshot = $null
    if ($hasExactCaseRequest) {
        $scalePercent = [int][Math]::Round($requestedUiScale * 100.0)
        $exactCaseRoot = Join-Path $workspaceRoot ".codex-build\ui-calibration\native-parity\$Resolution-ui$scalePercent\$interfaceId"
        $exactSnapshotPath = Join-Path $exactCaseRoot 'runtime-snapshot.json'
        if (Test-Path -LiteralPath $exactSnapshotPath -PathType Leaf) { $snapshot = Get-Item -LiteralPath $exactSnapshotPath }
    } elseif (-not $hasExactCaseRequest) {
        $snapshotRoot = Join-Path $installedModuleRoot "UiCalibration\snapshots"
        $snapshot = if (Test-Path -LiteralPath $snapshotRoot -PathType Container) {
            Get-ChildItem -LiteralPath $snapshotRoot -File -Filter "$MovieName-*.json" |
                Sort-Object LastWriteTimeUtc -Descending |
                Select-Object -First 1
        }
    }

    $capture = if ($exactCaseRoot -and (Test-Path -LiteralPath (Join-Path $exactCaseRoot 'native.png') -PathType Leaf)) {
        Get-Item -LiteralPath (Join-Path $exactCaseRoot 'native.png')
    } else { $null }
    if (-not $capture -and $interfaceId -and -not $hasExactCaseRequest) {
        $evidenceRoot = Join-Path $workspaceRoot ".codex-build\ui-calibration\$interfaceId"
        if (Test-Path -LiteralPath $evidenceRoot -PathType Container) {
            $capture = Get-ChildItem -LiteralPath $evidenceRoot -Recurse -File -Filter "live*.png" |
                Where-Object { $_.Name -notmatch 'overlay|difference|preview' } |
                Sort-Object LastWriteTimeUtc -Descending |
                Select-Object -First 1
        }
    }

    $prefabFileName = [IO.Path]::GetFileName(([string]$entry.prefab).Replace('/', '\'))
    $prefabPaths = Resolve-PrefabPaths -FileName $prefabFileName
    $sourcePrefabSha256 = if (Test-Path -LiteralPath $prefabPaths.Source -PathType Leaf) { (Get-FileHash -Algorithm SHA256 -LiteralPath $prefabPaths.Source).Hash.ToLowerInvariant() } else { '' }
    $installedPrefabSha256 = if (Test-Path -LiteralPath $prefabPaths.Installed -PathType Leaf) { (Get-FileHash -Algorithm SHA256 -LiteralPath $prefabPaths.Installed).Hash.ToLowerInvariant() } else { '' }
    $snapshotPayload = $null
    if ($snapshot) { $snapshotPayload = Get-Content -Raw -LiteralPath $snapshot.FullName | ConvertFrom-Json }
    $snapshotPrefabSha256 = if ($snapshotPayload) { [string]$snapshotPayload.prefabSha256 } else { '' }
    return [ordered]@{
        schema = 'reign-ui-native-evidence-v1'
        available = [bool]$snapshot
        movieName = $MovieName
        interfaceId = $interfaceId
        requestedResolution = $Resolution
        requestedUiScale = if ($hasExactCaseRequest) { $requestedUiScale } else { 0 }
        exactCaseMatch = [bool]($hasExactCaseRequest -and $snapshot)
        evidenceSource = if ($hasExactCaseRequest) { 'matrix-case' } else { 'installed-latest' }
        snapshot = $snapshotPayload
        snapshotName = if ($snapshot) { $snapshot.Name } else { '' }
        snapshotModifiedUtc = if ($snapshot) { $snapshot.LastWriteTimeUtc.ToString('o') } else { '' }
        snapshotSha256 = if ($snapshot) { (Get-FileHash -Algorithm SHA256 -LiteralPath $snapshot.FullName).Hash.ToLowerInvariant() } else { '' }
        sourcePrefabSha256 = $sourcePrefabSha256
        installedPrefabSha256 = $installedPrefabSha256
        snapshotPrefabSha256 = $snapshotPrefabSha256
        snapshotMatchesSource = -not [string]::IsNullOrWhiteSpace($snapshotPrefabSha256) -and $snapshotPrefabSha256 -eq $sourcePrefabSha256
        snapshotMatchesInstalled = -not [string]::IsNullOrWhiteSpace($snapshotPrefabSha256) -and $snapshotPrefabSha256 -eq $installedPrefabSha256
        captureAvailable = [bool]$capture
        captureName = if ($capture) { $capture.Name } else { '' }
        captureModifiedUtc = if ($capture) { $capture.LastWriteTimeUtc.ToString('o') } else { '' }
        captureSha256 = if ($capture) { (Get-FileHash -Algorithm SHA256 -LiteralPath $capture.FullName).Hash.ToLowerInvariant() } else { '' }
        capturePath = if ($capture) { $capture.FullName } else { '' }
    }
}

function Get-NativeBasePrefab {
    param([string]$Target)

    if ($Target -notmatch '^[A-Za-z][A-Za-z0-9_-]{1,100}$') { throw "A valid native augmentation target is required." }
    $entry = @((Get-UiCatalog).nativeAugmentations.targets | Where-Object { [string]$_.target -eq $Target }) | Select-Object -First 1
    if (-not $entry) { throw "Native augmentation target '$Target' is not present in the Reign UI catalog." }
    $modulesRoot = [IO.Path]::GetFullPath((Join-Path $BannerlordRoot 'Modules'))
    $relative = ([string]$entry.basePrefab).Replace('/', [IO.Path]::DirectorySeparatorChar)
    $basePath = [IO.Path]::GetFullPath((Join-Path $modulesRoot $relative))
    if (-not $basePath.StartsWith($modulesRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw "The native base-prefab path is outside the Bannerlord Modules directory." }
    if (-not (Test-Path -LiteralPath $basePath -PathType Leaf)) { throw "The installed native base prefab for '$Target' was not found at '$basePath'." }
    $xml = [IO.File]::ReadAllText($basePath)
    $document = [Xml.XmlDocument]::new()
    $document.LoadXml($xml)
    return [ordered]@{
        schema = 'reign-ui-native-base-prefab-v1'
        ok = $true
        target = $Target
        basePrefab = [string]$entry.basePrefab
        sha256 = Get-TextSha256 $xml
        resolvedConstants = Get-ReignNativePrefabConstants -Document $document
        xml = $xml
    }
}

$script:reignNativeBrushMetricCache = $null

function Get-ReignNativePrefabConstants {
    param([Xml.XmlDocument]$Document)

    $metrics = Get-ReignNativeBrushMetrics
    $values = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::OrdinalIgnoreCase)
    $nodes = @($Document.SelectNodes('//Constants/Constant[@Name]'))

    foreach ($node in $nodes) {
        $name = [string]$node.GetAttribute('Name')
        if ($node.HasAttribute('Value')) {
            $raw = [string]$node.GetAttribute('Value')
            $number = 0.0
            $values[$name] = if ([double]::TryParse($raw, [Globalization.NumberStyles]::Float, [Globalization.CultureInfo]::InvariantCulture, [ref]$number)) { $number } else { $raw }
            continue
        }

        $brushName = [string]$node.GetAttribute('BrushName')
        $brushLayer = [string]$node.GetAttribute('BrushLayer')
        $valueType = [string]$node.GetAttribute('BrushValueType')
        if (-not [string]::IsNullOrWhiteSpace($brushName) -and -not [string]::IsNullOrWhiteSpace($valueType)) {
            $key = "$brushName`n$brushLayer"
            if ($metrics.ContainsKey($key)) {
                $metric = $metrics[$key]
                $resolved = if ($valueType -eq 'Height') { [double]$metric.Height } else { [double]$metric.Width }
                if ($node.HasAttribute('MultiplyResult')) {
                    $multiplier = 0.0
                    if ([double]::TryParse([string]$node.GetAttribute('MultiplyResult'), [Globalization.NumberStyles]::Float, [Globalization.CultureInfo]::InvariantCulture, [ref]$multiplier)) {
                        $resolved *= $multiplier
                    }
                }
                $values[$name] = $resolved
            }
        }
    }

    # Conditional constants cannot be evaluated against a native VM offline.
    # The preview fixture uses the native false/default branch unless a future
    # captured-state adapter supplies a different value.
    foreach ($node in $nodes) {
        $name = [string]$node.GetAttribute('Name')
        if ($values.ContainsKey($name) -or -not $node.HasAttribute('BooleanCheck')) { continue }
        $candidate = [string]$node.GetAttribute('OnFalse')
        if ($candidate.StartsWith('!')) { $candidate = $candidate.Substring(1) }
        if ($values.ContainsKey($candidate)) { $values[$name] = $values[$candidate] }
    }

    $result = [ordered]@{}
    foreach ($name in ($values.Keys | Sort-Object)) { $result[$name] = $values[$name] }
    return $result
}

function Get-ReignNativeBrushMetrics {
    if ($null -ne $script:reignNativeBrushMetricCache) { return $script:reignNativeBrushMetricCache }

    $modulesRoot = Join-Path $BannerlordRoot 'Modules'
    $spriteMetrics = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($module in @(Get-ChildItem -LiteralPath $modulesRoot -Directory -ErrorAction SilentlyContinue)) {
        $guiRoot = Join-Path $module.FullName 'GUI'
        if (-not (Test-Path -LiteralPath $guiRoot -PathType Container)) { continue }
        foreach ($file in @(Get-ChildItem -LiteralPath $guiRoot -Filter '*SpriteData.xml' -File -ErrorAction SilentlyContinue)) {
            try {
                $document = [Xml.XmlDocument]::new()
                $document.Load($file.FullName)
                foreach ($part in @($document.SelectNodes('//SpritePart[Name and Width and Height]'))) {
                    $spriteMetrics[[string]$part.Name] = [pscustomobject]@{
                        Width = [double]$part.Width
                        Height = [double]$part.Height
                    }
                }
            } catch {
            }
        }
    }

    $brushMetrics = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($module in @(Get-ChildItem -LiteralPath $modulesRoot -Directory -ErrorAction SilentlyContinue)) {
        $brushRoot = Join-Path $module.FullName 'GUI\Brushes'
        if (-not (Test-Path -LiteralPath $brushRoot -PathType Container)) { continue }
        foreach ($file in @(Get-ChildItem -LiteralPath $brushRoot -Filter '*.xml' -File -Recurse -ErrorAction SilentlyContinue)) {
            try {
                $document = [Xml.XmlDocument]::new()
                $document.Load($file.FullName)
                foreach ($brush in @($document.SelectNodes('//Brush[@Name]'))) {
                    foreach ($layer in @($brush.SelectNodes('./Layers/BrushLayer[@Name and @Sprite]'))) {
                        $spriteName = [string]$layer.GetAttribute('Sprite')
                        if (-not $spriteMetrics.ContainsKey($spriteName)) { continue }
                        $brushMetrics["$([string]$brush.GetAttribute('Name'))`n$([string]$layer.GetAttribute('Name'))"] = $spriteMetrics[$spriteName]
                    }
                }
            } catch {
            }
        }
    }

    $script:reignNativeBrushMetricCache = $brushMetrics
    return $script:reignNativeBrushMetricCache
}

$listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Parse($hostName), $Port)

try {
    $listener.Start()
} catch [Net.Sockets.SocketException] {
    Write-Host "Preview server appears to already be running on port $Port."
    if (-not $NoBrowser) {
        Start-Process $url
    }
    return
}

Start-CodexPreviewAgent

Write-Host "Bannerlord Reign Gauntlet XML Previewer"
Write-Host "Serving: $root"
Write-Host "Open:    $url"
if ($codexAgentProcess -and $codexBridgeProcess) { Write-Host "Codex:   $codexBrowserUrl -> $codexAgentUrl (previewer-owned editing task)" }
elseif ($codexAgentError) { Write-Warning "Codex preview agent unavailable: $codexAgentError" }
Write-Host "Press Ctrl+C to stop."

if (-not $NoBrowser) {
    Start-Process $url
}

try {
    while ($true) {
        $client = $listener.AcceptTcpClient()
        try {
            $stream = $client.GetStream()
            $request = Read-HttpRequest -Stream $stream
            $method = $request.Method
            $requestPath = $request.Path.Split("?")[0]

            if ($method -eq "OPTIONS") {
                Write-Response -Stream $stream -StatusCode 204 -StatusText "No Content" -Body ([byte[]]@())
                continue
            }

            if ($method -eq "POST" -and $requestPath -in @(
                "/tools/GauntletXmlPreviewer/api/calibration/save",
                "/tools/GauntletXmlPreviewer/api/calibration/apply",
                "/tools/GauntletXmlPreviewer/api/calibration/install",
                "/tools/GauntletXmlPreviewer/api/calibration/apply-and-install"
            )) {
                try {
                    $patch = $request.Body | ConvertFrom-Json
                    if ($requestPath.EndsWith("/save", [StringComparison]::OrdinalIgnoreCase)) {
                        $savedPath = Save-CalibrationPatch -Patch $patch
                        Write-JsonResponse -Stream $stream -StatusCode 200 -StatusText "OK" -Value ([ordered]@{ ok = $true; path = $savedPath })
                    } elseif ($requestPath.EndsWith("/install", [StringComparison]::OrdinalIgnoreCase)) {
                        $result = Install-Prefab -Request $patch
                        Write-JsonResponse -Stream $stream -StatusCode 200 -StatusText "OK" -Value ([ordered]@{ ok = $true; install = $result })
                    } elseif ($requestPath.EndsWith("/apply-and-install", [StringComparison]::OrdinalIgnoreCase)) {
                        $applyResult = Apply-CalibrationPatch -Patch $patch
                        $patch.sourceSha256 = $applyResult.sourceSha256
                        try {
                            $installResult = Install-Prefab -Request $patch
                            Write-JsonResponse -Stream $stream -StatusCode 200 -StatusText "OK" -Value ([ordered]@{
                                ok = $true
                                sourceApplied = $true
                                applied = $applyResult.applied
                                backup = $applyResult.backup
                                sourceSha256 = $applyResult.sourceSha256
                                install = $installResult
                            })
                        } catch {
                            Write-JsonResponse -Stream $stream -StatusCode 409 -StatusText "Conflict" -Value ([ordered]@{
                                ok = $false
                                sourceApplied = $true
                                installed = $false
                                applied = $applyResult.applied
                                backup = $applyResult.backup
                                sourceSha256 = $applyResult.sourceSha256
                                error = $_.Exception.Message
                            })
                        }
                    } else {
                        $result = Apply-CalibrationPatch -Patch $patch
                        Write-JsonResponse -Stream $stream -StatusCode 200 -StatusText "OK" -Value ([ordered]@{ ok = $true; applied = $result.applied; backup = $result.backup; sourceSha256 = $result.sourceSha256 })
                    }
                } catch {
                    Write-JsonResponse -Stream $stream -StatusCode 409 -StatusText "Conflict" -Value ([ordered]@{ ok = $false; error = $_.Exception.Message })
                }
                continue
            }

            if ($method -ne "GET") {
                $body = [Text.Encoding]::UTF8.GetBytes("Only preview GET requests and calibration POST requests are supported.")
                Write-Response -Stream $stream -StatusCode 405 -StatusText "Method Not Allowed" -Body $body
                continue
            }

            if ($requestPath -eq "/tools/GauntletXmlPreviewer/api/reign-prefabs") {
                $allPrefabs = @(Get-ReignPrefabCatalog)
                $catalog = [ordered]@{
                    prefabs = @($allPrefabs | Where-Object { $_.kind -eq "standalone" })
                    auxiliaryPrefabs = @($allPrefabs | Where-Object { $_.kind -eq "auxiliary" })
                }
                $json = $catalog | ConvertTo-Json -Depth 5 -Compress
                $body = [Text.Encoding]::UTF8.GetBytes($json)
                Write-Response -Stream $stream -StatusCode 200 -StatusText "OK" -Body $body -ContentType "application/json; charset=utf-8"
                continue
            }

            if ($requestPath -eq "/tools/GauntletXmlPreviewer/api/preview-environment") {
                $value = [ordered]@{
                    schema = 'reign-ui-preview-environment-v1'
                    workspaceRoot = $workspaceRoot
                    installedModuleRoot = $installedModuleRoot
                    codex = [ordered]@{
                        available = [bool]($codexAgentProcess -and $codexBridgeProcess)
                        endpoint = if ($codexAgentProcess -and $codexBridgeProcess) { $codexBrowserUrl } else { '' }
                        error = $codexAgentError
                        taskModel = 'configured Codex default'
                        isolation = 'dedicated previewer editing task'
                    }
                }
                Write-JsonResponse -Stream $stream -StatusCode 200 -StatusText "OK" -Value $value
                continue
            }

            if ($requestPath -eq "/tools/GauntletXmlPreviewer/api/native-prefab") {
                try {
                    $target = Get-RequestQueryValue -RequestPath $request.Path -Name 'target'
                    Write-JsonResponse -Stream $stream -StatusCode 200 -StatusText "OK" -Value (Get-NativeBasePrefab -Target $target)
                } catch {
                    Write-JsonResponse -Stream $stream -StatusCode 400 -StatusText "Bad Request" -Value ([ordered]@{ ok = $false; error = $_.Exception.Message })
                }
                continue
            }

            if ($requestPath -eq "/tools/GauntletXmlPreviewer/api/prefab-sync") {
                try {
                    $fileName = Get-RequestQueryValue -RequestPath $request.Path -Name 'file'
                    Write-JsonResponse -Stream $stream -StatusCode 200 -StatusText "OK" -Value (Get-PrefabSyncStatus -FileName $fileName)
                } catch {
                    Write-JsonResponse -Stream $stream -StatusCode 400 -StatusText "Bad Request" -Value ([ordered]@{ ok = $false; error = $_.Exception.Message })
                }
                continue
            }

            if ($requestPath -eq "/tools/GauntletXmlPreviewer/api/prefab-sync-all") {
                try {
                    Write-JsonResponse -Stream $stream -StatusCode 200 -StatusText "OK" -Value (Get-AllPrefabSyncStatus)
                } catch {
                    Write-JsonResponse -Stream $stream -StatusCode 500 -StatusText "Internal Server Error" -Value ([ordered]@{ ok = $false; error = $_.Exception.Message })
                }
                continue
            }

            if ($requestPath -eq "/tools/GauntletXmlPreviewer/api/native-parity-readiness") {
                try {
                    Write-JsonResponse -Stream $stream -StatusCode 200 -StatusText "OK" -Value (Get-NativeParityReadiness)
                } catch {
                    Write-JsonResponse -Stream $stream -StatusCode 500 -StatusText "Internal Server Error" -Value ([ordered]@{ ok = $false; error = $_.Exception.Message })
                }
                continue
            }

            if ($requestPath -eq "/tools/GauntletXmlPreviewer/api/native-evidence") {
                try {
                    $movieName = Get-RequestQueryValue -RequestPath $request.Path -Name 'movie'
                    $resolution = Get-RequestQueryValue -RequestPath $request.Path -Name 'resolution'
                    $uiScale = Get-RequestQueryValue -RequestPath $request.Path -Name 'uiScale'
                    $evidence = Get-NativeEvidence -MovieName $movieName -Resolution $resolution -UiScale $uiScale
                    $evidence['captureUrl'] = if ($evidence.captureAvailable) {
                        "api/native-evidence/image?movie=$([Uri]::EscapeDataString($movieName))&resolution=$([Uri]::EscapeDataString($resolution))&uiScale=$([Uri]::EscapeDataString($uiScale))&revision=$($evidence.captureSha256.Substring(0, 12))"
                    } else { '' }
                    $evidence.Remove('capturePath')
                    Write-JsonResponse -Stream $stream -StatusCode 200 -StatusText "OK" -Value $evidence
                } catch {
                    Write-JsonResponse -Stream $stream -StatusCode 400 -StatusText "Bad Request" -Value ([ordered]@{ ok = $false; error = $_.Exception.Message })
                }
                continue
            }

            if ($requestPath -eq "/tools/GauntletXmlPreviewer/api/native-evidence/image") {
                try {
                    $movieName = Get-RequestQueryValue -RequestPath $request.Path -Name 'movie'
                    $resolution = Get-RequestQueryValue -RequestPath $request.Path -Name 'resolution'
                    $uiScale = Get-RequestQueryValue -RequestPath $request.Path -Name 'uiScale'
                    $evidence = Get-NativeEvidence -MovieName $movieName -Resolution $resolution -UiScale $uiScale
                    if (-not $evidence.captureAvailable -or -not (Test-Path -LiteralPath $evidence.capturePath -PathType Leaf)) { throw "No native capture is available for $movieName." }
                    Write-Response -Stream $stream -StatusCode 200 -StatusText "OK" -Body ([IO.File]::ReadAllBytes($evidence.capturePath)) -ContentType "image/png"
                } catch {
                    Write-JsonResponse -Stream $stream -StatusCode 404 -StatusText "Not Found" -Value ([ordered]@{ ok = $false; error = $_.Exception.Message })
                }
                continue
            }

            $filePath = Resolve-RequestPath $request.Path
            if ($null -eq $filePath -or -not (Test-Path -LiteralPath $filePath -PathType Leaf)) {
                $body = [Text.Encoding]::UTF8.GetBytes("Not found: $($request.Path)")
                Write-Response -Stream $stream -StatusCode 404 -StatusText "Not Found" -Body $body
                continue
            }

            $bytes = [IO.File]::ReadAllBytes($filePath)
            Write-Response -Stream $stream -StatusCode 200 -StatusText "OK" -Body $bytes -ContentType (Get-ContentType $filePath)
        } catch {
            try {
                $body = [Text.Encoding]::UTF8.GetBytes("Preview server error: $($_.Exception.Message)")
                Write-Response -Stream $stream -StatusCode 500 -StatusText "Internal Server Error" -Body $body
            } catch {
            }
        } finally {
            $client.Close()
        }
    }
} finally {
    $listener.Stop()
    if ($codexBridgeProcess -and -not $codexBridgeProcess.HasExited) {
        Stop-Process -Id $codexBridgeProcess.Id -Force -ErrorAction SilentlyContinue
    }
    if ($codexAgentProcess -and -not $codexAgentProcess.HasExited) {
        Stop-Process -Id $codexAgentProcess.Id -Force -ErrorAction SilentlyContinue
    }
}
