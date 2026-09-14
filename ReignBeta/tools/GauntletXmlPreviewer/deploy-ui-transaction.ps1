[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$InstalledRoot,

    [Parameter(Mandatory = $true)]
    [string]$ValidationRunRoot,

    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[0-9a-fA-F]{64}$')]
    [string]$ExpectedSourceFingerprint,

    [ValidateSet('Plan', 'Apply')]
    [string]$Mode = 'Plan',

    [ValidatePattern('^$|^[0-9a-fA-F]{64}$')]
    [string]$ExpectedPlanFingerprint = '',

    [string]$EvidenceParent = ''
)

$ErrorActionPreference = 'Stop'
$workspaceRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..'))
$moduleRoot = [System.IO.Path]::GetFullPath((Join-Path $workspaceRoot 'ReignBeta'))
$modulePrefix = $moduleRoot.TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
$installedRootPath = [System.IO.Path]::GetFullPath($InstalledRoot)
$installedPathRoot = [System.IO.Path]::GetPathRoot($installedRootPath)
if ($installedRootPath.Length -gt $installedPathRoot.Length) {
    $installedRootPath = $installedRootPath.TrimEnd(
        [char[]]@([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar))
}
$installedPrefix = $installedRootPath.TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
$validationRoot = [System.IO.Path]::GetFullPath($ValidationRunRoot)
$validationPrefix = $validationRoot.TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
$validationReportPath = Join-Path $validationRoot 'validation-report.json'
$catalogPath = Join-Path $moduleRoot 'tools\GauntletXmlPreviewer\calibration\ui-catalog.json'
$atlasManifestPath = Join-Path $moduleRoot 'GUI\RuntimeSpriteSheets\manifest.json'
$styleManifestPath = Join-Path $moduleRoot 'artwork\ui-modern-style-kit\asset-manifest.json'
$expectedUiAuthorityFileCount = 318
$expectedAuxiliaryUiAuthorityFileCount = 11
$expectedProtectedMapFileCount = 17
$expectedTotalUiRuntimeAuthorityFileCount = $expectedUiAuthorityFileCount + $expectedAuxiliaryUiAuthorityFileCount + 2
$fixedRuntimeUiContractSha256 = [ordered]@{
    'GUI\Brushes\ReignPortraitMasks.xml' = 'b389e1a7f6bd8c5ec2cbaa50e166b57739c54b6fb6213d29b459ccc0a43725af'
    'GUI\UiCalibration\modern-style-contract.json' = '0d4eabe1f4a2fbea28bbb4b0eb665f7c2df72fca2e60d0a4a0e64ef1a7abecc4'
}
$approvedProtectedTileSha256 = [ordered]@{
    'reign_war_council_tile_0_0.png' = '24fe538ca063966c6b17064d05b8d7a964dd90f404567aa88b827594b9cce07e'
    'reign_war_council_tile_0_1.png' = 'ffe56f48ac139e1e8fe39c362ac3a0d0aaa62d928530af6006f8592383472e08'
    'reign_war_council_tile_0_2.png' = '8f34fae5e2d8ac8619f54c9627df1fed1355cf4fb60f97fbcea6620b13479b41'
    'reign_war_council_tile_0_3.png' = '08c645fe8b3660b355a2fb37b99350a2b105bdea20cd4b584303f1556625e9da'
    'reign_war_council_tile_1_0.png' = 'e69acd64ece8cd04b85400b7c5747745ec38cda28b03d796a6afddf2d33a0212'
    'reign_war_council_tile_1_1.png' = 'f49b3d8590401cce0274ed01630fde48162bd25045761f6216bfec0314cb5acc'
    'reign_war_council_tile_1_2.png' = '19a4657c71988ba3add9f148738fbae77621cddbdf0767463568beee8ab6b4b2'
    'reign_war_council_tile_1_3.png' = '08c8cff985d97161554354da063526e2e02c9a65e7dd5670e19878ab7eb19ea1'
    'reign_war_council_tile_2_0.png' = '1308581a590e7e7fc014ee6f76851baa598591c17b5559a74633c714fdc14ee7'
    'reign_war_council_tile_2_1.png' = 'ccb5a8547037037b8ad281e3fcea3cf5bbbb0839db68129d05a9ad731c5803ee'
    'reign_war_council_tile_2_2.png' = '25298216cb12896d075d275ea02f88a0e71e75f727cdf741a3d7e6e99554f416'
    'reign_war_council_tile_2_3.png' = '61ca925426f6e6dda5fc907d2fd2750a1b02457927524d5661b31dcd6c288c2c'
    'reign_war_council_tile_3_0.png' = '1c61495e7939c5162980ebfce0f94c174f73952996ceb07e61b19652d8d1de99'
    'reign_war_council_tile_3_1.png' = '21dbabce42e55f873aa2dfc997140eb16d2a75dda3ae443e58589d6fa879fc3b'
    'reign_war_council_tile_3_2.png' = '07f25ace7d79728da4772d6a42c7d146014312bbb5ce49a4235af06d535493e3'
    'reign_war_council_tile_3_3.png' = 'c26fc06848688d0f29c0713d40a33aeb30aef467ee8e16c31a5ebb2f4c54162d'
}

function Get-Sha256 {
    param([Parameter(Mandatory = $true)][string]$Path)
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Test-PathUnderRoot {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$RootPrefix
    )
    return $Path.StartsWith($RootPrefix, [System.StringComparison]::OrdinalIgnoreCase)
}

function Assert-ContainedWithoutReparsePoint {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Root,
        [Parameter(Mandatory = $true)][string]$Description
    )

    $rootPath = [System.IO.Path]::GetFullPath($Root).TrimEnd([System.IO.Path]::DirectorySeparatorChar)
    $pathValue = [System.IO.Path]::GetFullPath($Path)
    $rootValuePrefix = $rootPath + [System.IO.Path]::DirectorySeparatorChar
    if ($pathValue -ne $rootPath -and -not (Test-PathUnderRoot -Path $pathValue -RootPrefix $rootValuePrefix)) {
        throw "$Description escapes its approved root: $pathValue"
    }

    $ancestor = [System.IO.DirectoryInfo]::new($rootPath)
    while ($null -ne $ancestor) {
        if ($ancestor.Exists -and
            ($ancestor.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "$Description traverses a reparse-point ancestor: $($ancestor.FullName)"
        }
        $ancestor = $ancestor.Parent
    }

    $current = $rootPath
    $segments = if ($pathValue.Length -gt $rootPath.Length) {
        $pathValue.Substring($rootPath.Length).TrimStart('\').Split(
            [System.IO.Path]::DirectorySeparatorChar,
            [System.StringSplitOptions]::RemoveEmptyEntries)
    } else {
        @()
    }
    foreach ($segment in @('', $segments)) {
        if ($segment) {
            $current = Join-Path $current $segment
        }
        if (Test-Path -LiteralPath $current) {
            $item = Get-Item -LiteralPath $current -Force
            if (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "$Description traverses a reparse point: $current"
            }
        }
    }
}

function Get-TextSha256 {
    param([Parameter(Mandatory = $true)][string]$Text)
    $algorithm = [System.Security.Cryptography.SHA256]::Create()
    try {
        $bytes = [System.Text.Encoding]::UTF8.GetBytes($Text)
        return [System.BitConverter]::ToString($algorithm.ComputeHash($bytes)).Replace('-', '').ToLowerInvariant()
    } finally {
        $algorithm.Dispose()
    }
}

function Invoke-AtomicFileReplace {
    param(
        [Parameter(Mandatory = $true)][string]$ReplacementPath,
        [Parameter(Mandatory = $true)][string]$DestinationPath,
        [Parameter(Mandatory = $true)][string]$Description
    )

    $replacement = [System.IO.Path]::GetFullPath($ReplacementPath)
    $destination = [System.IO.Path]::GetFullPath($DestinationPath)
    if (-not (Test-Path -LiteralPath $replacement -PathType Leaf)) {
        throw "$Description replacement file is missing: $replacement"
    }
    if (-not (Test-Path -LiteralPath $destination -PathType Leaf)) {
        throw "$Description destination file is missing: $destination"
    }

    $backupPath = Join-Path (Split-Path -Parent $destination) ('.reign-replace-backup-' + [System.Guid]::NewGuid().ToString('N') + '.bak')
    $replaceSucceeded = $false
    try {
        [System.IO.File]::Replace($replacement, $destination, $backupPath)
        $replaceSucceeded = $true
    } catch {
        $recovery = if (Test-Path -LiteralPath $backupPath -PathType Leaf) {
            " Recovery backup retained at $backupPath."
        } else {
            ''
        }
        throw "$Description failed.$recovery $($_.Exception.Message)"
    } finally {
        if ($replaceSucceeded -and (Test-Path -LiteralPath $backupPath -PathType Leaf)) {
            Remove-Item -LiteralPath $backupPath -Force
        }
    }
}

function Write-DurableJson {
    param(
        [Parameter(Mandatory = $true)]$Value,
        [Parameter(Mandatory = $true)][string]$Path
    )

    $targetPath = [System.IO.Path]::GetFullPath($Path)
    $directory = Split-Path -Parent $targetPath
    [System.IO.Directory]::CreateDirectory($directory) | Out-Null
    $temporaryPath = Join-Path $directory ('.reign-evidence-' + [System.Guid]::NewGuid().ToString('N') + '.tmp')
    try {
        $json = $Value | ConvertTo-Json -Depth 12
        $bytes = [System.Text.UTF8Encoding]::new($false).GetBytes($json + "`n")
        $stream = [System.IO.FileStream]::new(
            $temporaryPath,
            [System.IO.FileMode]::CreateNew,
            [System.IO.FileAccess]::Write,
            [System.IO.FileShare]::None,
            4096,
            [System.IO.FileOptions]::WriteThrough)
        try {
            $stream.Write($bytes, 0, $bytes.Length)
            $stream.Flush($true)
        } finally {
            $stream.Dispose()
        }
        if ([System.IO.File]::Exists($targetPath)) {
            Invoke-AtomicFileReplace -ReplacementPath $temporaryPath -DestinationPath $targetPath -Description 'Durable JSON update'
        } else {
            [System.IO.File]::Move($temporaryPath, $targetPath)
        }
    } finally {
        if (Test-Path -LiteralPath $temporaryPath -PathType Leaf) {
            Remove-Item -LiteralPath $temporaryPath -Force
        }
    }
}

function Get-CanonicalValidationSourceFingerprint {
    param(
        [Parameter(Mandatory = $true)]$Validation,
        [Parameter(Mandatory = $true)][string]$WorkspaceRoot
    )

    $extensions = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    @('.cs', '.csproj', '.props', '.targets', '.json', '.xml', '.md', '.ps1', '.cmd', '.toml',
        '.sln', '.slnx', '.yml', '.yaml') | ForEach-Object { [void]$extensions.Add($_) }
    $excluded = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    @('.git', '.tmp', '.codex-build', 'artifacts', 'bin', 'obj', 'build', 'dist', 'logs', 'node_modules',
        '.venv', '.pytest_cache', 'staging', 'deployment-backups', 'verification_contracts', 'publish',
        'TestResults', 'decompiled', 'third_party', 'server', 'PortraitCache') |
        ForEach-Object { [void]$excluded.Add($_) }
    $files = [System.Collections.Generic.SortedSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    @('reign-projects.json', 'reign.modules.json', 'reign.testing.json', 'reign.repository.json', '.gitignore',
        '.gitattributes', 'AGENTS.md', '.codex\hooks.json') |
        ForEach-Object { [void]$files.Add((Join-Path $WorkspaceRoot $_)) }

    $roots = @(
        $Validation.Plan.Projects |
            ForEach-Object { (([string]$_.ProjectPath).Replace('\', '/') -split '/')[0] } |
            Where-Object { $_ } |
            Sort-Object -Unique
    )
    foreach ($relativeRoot in $roots) {
        $pending = [System.Collections.Generic.Stack[System.IO.DirectoryInfo]]::new()
        $pending.Push([System.IO.DirectoryInfo]::new((Join-Path $WorkspaceRoot $relativeRoot)))
        while ($pending.Count -gt 0) {
            $directory = $pending.Pop()
            if (-not $directory.Exists -or
                ($directory.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0 -or
                $excluded.Contains($directory.Name)) {
                continue
            }
            foreach ($file in $directory.EnumerateFiles()) {
                if ($extensions.Contains($file.Extension)) {
                    [void]$files.Add($file.FullName)
                }
            }
            foreach ($child in $directory.EnumerateDirectories()) {
                $pending.Push($child)
            }
        }
    }

    $hash = [System.Security.Cryptography.IncrementalHash]::CreateHash(
        [System.Security.Cryptography.HashAlgorithmName]::SHA256)
    try {
        $buffer = [byte[]]::new(64 * 1024)
        $workspacePrefix = [System.IO.Path]::GetFullPath($WorkspaceRoot).TrimEnd(
            [System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
        foreach ($path in $files) {
            if (-not [System.IO.File]::Exists($path)) {
                continue
            }
            $fullPath = [System.IO.Path]::GetFullPath($path)
            if (-not (Test-PathUnderRoot -Path $fullPath -RootPrefix $workspacePrefix)) {
                throw "Canonical validation source escaped the workspace: $fullPath"
            }
            $relative = $fullPath.Substring($workspacePrefix.Length).Replace('\', '/').ToLowerInvariant()
            $hash.AppendData([System.Text.Encoding]::UTF8.GetBytes($relative + "`n"))
            $stream = [System.IO.File]::OpenRead($path)
            try {
                while (($read = $stream.Read($buffer, 0, $buffer.Length)) -gt 0) {
                    $hash.AppendData($buffer, 0, $read)
                }
            } finally {
                $stream.Dispose()
            }
        }
        return [System.BitConverter]::ToString($hash.GetHashAndReset()).Replace('-', '').ToLowerInvariant()
    } finally {
        $hash.Dispose()
    }
}

if (-not (Test-Path -LiteralPath $moduleRoot -PathType Container)) {
    throw "Workspace module root is missing: $moduleRoot"
}
if (-not (Test-Path -LiteralPath $installedRootPath -PathType Container)) {
    throw "Installed module root is missing: $installedRootPath"
}
if (-not (Test-Path -LiteralPath $validationReportPath -PathType Leaf)) {
    throw "Validation report is missing: $validationReportPath"
}
Assert-ContainedWithoutReparsePoint -Path $moduleRoot -Root $moduleRoot -Description 'Workspace module root'
Assert-ContainedWithoutReparsePoint -Path $installedRootPath -Root $installedRootPath -Description 'Installed module root'
Assert-ContainedWithoutReparsePoint -Path $validationReportPath -Root $validationRoot -Description 'Validation report'

$applyLockStream = $null
$applyLockPath = ''
if ($Mode -eq 'Apply') {
    $lockDirectory = Join-Path $workspaceRoot '.codex-build\ui-deployment\locks'
    [System.IO.Directory]::CreateDirectory($lockDirectory) | Out-Null
    Assert-ContainedWithoutReparsePoint -Path $lockDirectory -Root $workspaceRoot -Description 'Deployment lock directory'
    $lockIdentity = Get-TextSha256 -Text $installedRootPath.ToLowerInvariant()
    $applyLockPath = Join-Path $lockDirectory ($lockIdentity + '.lock')
    try {
        $applyLockStream = [System.IO.FileStream]::new(
            $applyLockPath,
            [System.IO.FileMode]::OpenOrCreate,
            [System.IO.FileAccess]::ReadWrite,
            [System.IO.FileShare]::None,
            4096,
            [System.IO.FileOptions]::WriteThrough)
    } catch {
        throw "Another UI deployment transaction is active for this installed module: $installedRootPath"
    }
}

try {

$validation = Get-Content -Raw -LiteralPath $validationReportPath | ConvertFrom-Json
if (-not [bool]$validation.Ok) {
    throw 'The selected validation report is not green.'
}
if ([string]$validation.SourceFingerprintSha256 -ne $ExpectedSourceFingerprint.ToLowerInvariant()) {
    throw 'The validation source fingerprint does not match the reviewed deployment authority.'
}
if ([System.IO.Path]::GetFullPath([string]$validation.ArtifactRoot) -ne $validationRoot) {
    throw 'The validation report artifact root does not match ValidationRunRoot.'
}
if ([System.IO.Path]::GetFullPath([string]$validation.ReportPath) -ne $validationReportPath) {
    throw 'The validation report path does not match ValidationRunRoot.'
}
$currentSourceFingerprint = Get-CanonicalValidationSourceFingerprint -Validation $validation -WorkspaceRoot $workspaceRoot
if ($currentSourceFingerprint -ne ([string]$validation.SourceFingerprintSha256).ToLowerInvariant()) {
    throw 'The current canonical workspace source fingerprint no longer matches the selected validation report.'
}
$requiredValidatedBuilds = @('client', 'server', 'live-test', 'verification')
$validatedBuildOutputRoots = @{}
$validatedBuildInventories = [ordered]@{}
foreach ($projectId in $requiredValidatedBuilds) {
    $successfulBuilds = @(
        $validation.Results |
            Where-Object {
                [string]$_.Project.Id -eq $projectId -and
                [string]$_.Operation -eq 'build' -and
                [bool]$_.Process.Ok
            }
    )
    if ($successfulBuilds.Count -ne 1) {
        throw "The validation report must contain exactly one successful $projectId build."
    }
    $artifactDirectory = [System.IO.Path]::GetFullPath([string]$successfulBuilds[0].Process.ArtifactDirectory)
    $expectedArtifactDirectory = [System.IO.Path]::GetFullPath((Join-Path $validationRoot $projectId))
    if ($artifactDirectory -ne $expectedArtifactDirectory) {
        throw "The successful $projectId build artifact directory does not match the selected validation run."
    }
    Assert-ContainedWithoutReparsePoint -Path $artifactDirectory -Root $validationRoot -Description "$projectId build artifact directory"
    $outputRoot = [System.IO.Path]::GetFullPath((Join-Path $artifactDirectory 'out'))
    Assert-ContainedWithoutReparsePoint -Path $outputRoot -Root $validationRoot -Description "$projectId build output"
    if (-not (Test-Path -LiteralPath $outputRoot -PathType Container)) {
        throw "The successful $projectId build output directory is missing: $outputRoot"
    }
    $validatedBuildOutputRoots[$projectId] = $outputRoot
    $inventoryRows = @(
        Get-ChildItem -LiteralPath $outputRoot -Recurse -File |
            Sort-Object FullName |
            ForEach-Object {
                [pscustomobject]@{
                    relativePath = $_.FullName.Substring($outputRoot.Length + 1).Replace('\', '/')
                    sha256 = Get-Sha256 -Path $_.FullName
                    bytes = $_.Length
                }
            }
    )
    if ($inventoryRows.Count -eq 0) {
        throw "The successful $projectId build output inventory is empty."
    }
    $validatedBuildInventories[$projectId] = $inventoryRows
}

foreach ($authorityPath in @($catalogPath, $atlasManifestPath, $styleManifestPath)) {
    if (-not (Test-Path -LiteralPath $authorityPath -PathType Leaf)) {
        throw "UI deployment authority is missing: $authorityPath"
    }
    Assert-ContainedWithoutReparsePoint -Path $authorityPath -Root $moduleRoot -Description 'UI deployment authority'
}
$catalog = Get-Content -Raw -LiteralPath $catalogPath | ConvertFrom-Json
$atlas = Get-Content -Raw -LiteralPath $atlasManifestPath | ConvertFrom-Json
$styleManifest = Get-Content -Raw -LiteralPath $styleManifestPath | ConvertFrom-Json
$atlasSourceAuthoritySha256 = @{}
foreach ($categoryProperty in $atlas.categories.PSObject.Properties) {
    foreach ($partProperty in $categoryProperty.Value.parts.PSObject.Properties) {
        $part = $partProperty.Value
        $partRelativePath = ([string]$part.sourcePath).Replace('/', '\')
        $partSha256 = ([string]$part.sourceSha256).ToLowerInvariant()
        if (-not $partRelativePath -or $partSha256 -notmatch '^[0-9a-f]{64}$') {
            throw "Runtime atlas part '$($partProperty.Name)' has an invalid source authority."
        }
        if ($atlasSourceAuthoritySha256.ContainsKey($partRelativePath) -and
            [string]$atlasSourceAuthoritySha256[$partRelativePath] -ne $partSha256) {
            throw "Runtime atlas source authority is inconsistent for $partRelativePath."
        }
        $atlasSourceAuthoritySha256[$partRelativePath] = $partSha256
    }
}
$protectedMap = $styleManifest.implementationSnapshot.protectedDynamicArt
$protectedMapRelativePath = ([string]$protectedMap.warCouncilMapPath) -replace '^ReignBeta[\\/]', ''
$protectedMapRelativePath = $protectedMapRelativePath.Replace('/', '\')
$protectedMapSha256 = ([string]$protectedMap.warCouncilMapSha256).ToLowerInvariant()
$protectedTileLegacyAggregateSha256 = ([string]$protectedMap.warCouncilTileSetFingerprintSha256).ToLowerInvariant()
$approvedProtectedTileLegacyAggregateSha256 = 'da720f16e73a093a5d581e9687df5109b46d985d23f79b164486dd556bc7f962'
if (-not $protectedMapRelativePath -or $protectedMapSha256 -notmatch '^[0-9a-f]{64}$') {
    throw 'The approved style authority does not contain a valid protected War Council map contract.'
}
if ($protectedTileLegacyAggregateSha256 -ne $approvedProtectedTileLegacyAggregateSha256) {
    throw 'The opaque legacy War Council tile-set authority has drifted from its frozen approved value.'
}

$liveFont = $styleManifest.implementationSnapshot.liveFont
$auxiliaryUiAuthorities = [System.Collections.Generic.List[object]]::new()
$liveFontRelativePath = (([string]$liveFont.fntPath) -replace '^ReignBeta[\\/]', '').Replace('/', '\')
if ($liveFontRelativePath -and ([string]$liveFont.fntSha256) -match '^[0-9a-fA-F]{64}$') {
    [void]$auxiliaryUiAuthorities.Add([pscustomobject]@{
        relativePath = $liveFontRelativePath
        sha256 = ([string]$liveFont.fntSha256).ToLowerInvariant()
        kind = 'approved-live-font-definition'
    })
}
foreach ($asset in @($styleManifest.generatedRuntimeAssets)) {
    $assetPath = ([string]$asset.path).Trim()
    $assetSha256 = ([string]$asset.sha256).ToLowerInvariant()
    if ($assetPath -and $assetSha256 -match '^[0-9a-f]{64}$') {
        $assetRelativePath = ($assetPath -replace '^ReignBeta[\\/]', '').Replace('/', '\')
        if ($atlasSourceAuthoritySha256.ContainsKey($assetRelativePath)) {
            if ([string]$atlasSourceAuthoritySha256[$assetRelativePath] -ne $assetSha256) {
                throw "Generated runtime asset authority disagrees with the runtime atlas source authority: $assetRelativePath"
            }
            continue
        }
        [void]$auxiliaryUiAuthorities.Add([pscustomobject]@{
            relativePath = $assetRelativePath
            sha256 = $assetSha256
            kind = 'approved-generated-runtime-asset'
        })
    }
}
$actionRuntimeFiles = @($styleManifest.conversationActions.runtimeFiles)
$requiredActionPaths = @('GUI/Fonts/ReignSerifActionItalic/ReignSerifActionItalic.fnt', 'GUI/Brushes/ReignChatActions.xml')
if ($actionRuntimeFiles.Count -ne 2 -or
    @($actionRuntimeFiles | ForEach-Object { [string]$_.path } | Sort-Object -Unique).Count -ne 2) {
    throw 'Conversation actions require exactly two distinct approved font and brush files.'
}
foreach ($asset in $actionRuntimeFiles) {
    $relativePath = ([string]$asset.path).Replace('\', '/')
    if ($requiredActionPaths -cnotcontains $relativePath -or
        ([string]$asset.sha256) -notmatch '^[0-9a-f]{64}$' -or
        ([string]$asset.kind) -notin @('approved-action-font-definition', 'approved-action-brush')) {
        throw 'Conversation action runtime authority contains an unexpected path, hash, or kind.'
    }
    [void]$auxiliaryUiAuthorities.Add([pscustomobject]@{
        relativePath = $relativePath.Replace('/', '\')
        sha256 = [string]$asset.sha256
        kind = [string]$asset.kind
    })
}
if ($auxiliaryUiAuthorities.Count -ne $expectedAuxiliaryUiAuthorityFileCount) {
    throw "The approved auxiliary UI runtime authority must contain exactly $expectedAuxiliaryUiAuthorityFileCount files; found $($auxiliaryUiAuthorities.Count)."
}

$rows = [System.Collections.Generic.List[object]]::new()

function Add-DeploymentRow {
    param(
        [Parameter(Mandatory = $true)][string]$Source,
        [Parameter(Mandatory = $true)][string]$RelativePath,
        [Parameter(Mandatory = $true)][string]$Kind,
        [string]$AuthoritySha256 = '',
        [string]$ValidatedBuildProjectId = ''
    )

    $sourcePath = [System.IO.Path]::GetFullPath($Source)
    $relative = $RelativePath.Replace('/', '\').TrimStart('\')
    $destinationPath = [System.IO.Path]::GetFullPath((Join-Path $installedRootPath $relative))
    if (-not (Test-PathUnderRoot -Path $destinationPath -RootPrefix $installedPrefix)) {
        throw "Destination escapes the installed module root: $destinationPath"
    }
    Assert-ContainedWithoutReparsePoint -Path $destinationPath -Root $installedRootPath -Description 'Deployment destination'
    $sourceIsModuleBacked = Test-PathUnderRoot -Path $sourcePath -RootPrefix $modulePrefix
    $sourceIsValidationBacked = Test-PathUnderRoot -Path $sourcePath -RootPrefix $validationPrefix
    if (-not $sourceIsModuleBacked -and -not $sourceIsValidationBacked) {
        throw "Deployment source escapes the workspace module and validation roots: $sourcePath"
    }
    if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) {
        throw "Deployment source is missing: $sourcePath"
    }
    if ($sourceIsModuleBacked) {
        Assert-ContainedWithoutReparsePoint -Path $sourcePath -Root $moduleRoot -Description 'Workspace module deployment source'
    } else {
        Assert-ContainedWithoutReparsePoint -Path $sourcePath -Root $validationRoot -Description 'Validation deployment source'
    }
    if ($ValidatedBuildProjectId) {
        if (-not $validatedBuildOutputRoots.ContainsKey($ValidatedBuildProjectId)) {
            throw "Deployment row names an unknown validated build: $ValidatedBuildProjectId"
        }
        $buildOutputRoot = [string]$validatedBuildOutputRoots[$ValidatedBuildProjectId]
        $buildOutputPrefix = $buildOutputRoot.TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
        if (-not (Test-PathUnderRoot -Path $sourcePath -RootPrefix $buildOutputPrefix)) {
            throw "Deployment source is not contained by the successful $ValidatedBuildProjectId build output: $sourcePath"
        }
        Assert-ContainedWithoutReparsePoint -Path $sourcePath -Root $buildOutputRoot -Description "$ValidatedBuildProjectId deployment source"
        $inventoryRelativePath = $sourcePath.Substring($buildOutputRoot.Length + 1).Replace('\', '/')
        $inventoryMatches = @(
            $validatedBuildInventories[$ValidatedBuildProjectId] |
                Where-Object { [string]$_.relativePath -eq $inventoryRelativePath }
        )
        if ($inventoryMatches.Count -ne 1) {
            throw "Deployment source is not uniquely present in the successful $ValidatedBuildProjectId output inventory: $inventoryRelativePath"
        }
    }

    $sourceSha256 = Get-Sha256 -Path $sourcePath
    if ($ValidatedBuildProjectId -and
        [string]$inventoryMatches[0].sha256 -ne $sourceSha256) {
        throw "Deployment source hash no longer matches the successful $ValidatedBuildProjectId output inventory: $inventoryRelativePath"
    }
    if ($AuthoritySha256 -and $sourceSha256 -ne $AuthoritySha256.ToLowerInvariant()) {
        throw "Source hash does not match its manifest authority: $relative"
    }
    $destinationExists = Test-Path -LiteralPath $destinationPath -PathType Leaf
    $beforeSha256 = if ($destinationExists) { Get-Sha256 -Path $destinationPath } else { '' }
    $isProtectedMapArt =
        $relative -eq $protectedMapRelativePath -or
        $relative -match '^GUI\\SpriteParts\\ui_reignbeta_war_council\\reign_war_council_tile_[0-3]_[0-3]\.png$'
    if ($relative -eq $protectedMapRelativePath -and $sourceSha256 -ne $protectedMapSha256) {
        throw 'The protected War Council parchment source hash changed.'
    }
    if ($isProtectedMapArt -and $beforeSha256 -ne $sourceSha256) {
        throw "Protected War Council map art is absent or differs in the installed module: $relative"
    }

    [void]$rows.Add([pscustomobject]@{
        relativePath = $relative
        kind = $Kind
        sourcePath = $sourcePath
        destinationPath = $destinationPath
        sourceSha256 = $sourceSha256
        authoritySha256 = if ($AuthoritySha256) { $AuthoritySha256.ToLowerInvariant() } else { '' }
        validatedBuildProjectId = $ValidatedBuildProjectId
        sourceBytes = (Get-Item -LiteralPath $sourcePath).Length
        destinationExisted = $destinationExists
        beforeSha256 = $beforeSha256
        needsCopy = ($beforeSha256 -ne $sourceSha256)
        protectedMapArt = $isProtectedMapArt
        backupPath = ''
        afterSha256 = $beforeSha256
        status = if ($beforeSha256 -eq $sourceSha256) { 'already-matching' } else { 'planned-copy' }
    })
}

function Add-ValidatedDirectoryFiles {
    param(
        [Parameter(Mandatory = $true)][string]$SourceRoot,
        [Parameter(Mandatory = $true)][string]$DestinationRelativeRoot,
        [Parameter(Mandatory = $true)][string]$Kind,
        [Parameter(Mandatory = $true)][string]$ValidatedBuildProjectId
    )

    $sourceRootPath = [System.IO.Path]::GetFullPath($SourceRoot)
    if (-not (Test-PathUnderRoot -Path ($sourceRootPath + [System.IO.Path]::DirectorySeparatorChar) -RootPrefix $validationPrefix)) {
        throw "Validated directory escapes the selected validation run: $sourceRootPath"
    }
    if (-not (Test-Path -LiteralPath $sourceRootPath -PathType Container)) {
        throw "Validated deployment directory is missing: $sourceRootPath"
    }

    $files = @(Get-ChildItem -LiteralPath $sourceRootPath -Recurse -File | Sort-Object FullName)
    if ($files.Count -eq 0) {
        throw "Validated deployment directory is empty: $sourceRootPath"
    }
    foreach ($file in $files) {
        $relative = $file.FullName.Substring($sourceRootPath.Length + 1)
        Add-DeploymentRow `
            -Source $file.FullName `
            -RelativePath (Join-Path $DestinationRelativeRoot $relative) `
            -Kind $Kind `
            -ValidatedBuildProjectId $ValidatedBuildProjectId
    }
    return $files.Count
}

$prefabs = @($catalog.interfaces.prefab | Sort-Object -Unique)
foreach ($prefab in $prefabs) {
    Add-DeploymentRow `
        -Source (Join-Path $moduleRoot $prefab) `
        -RelativePath $prefab `
        -Kind 'catalog-prefab'
}

$sheetCount = 0
$partCount = 0
foreach ($categoryProperty in $atlas.categories.PSObject.Properties) {
    $category = $categoryProperty.Value
    foreach ($sheet in @($category.sheets)) {
        $sheetCount++
        Add-DeploymentRow `
            -Source (Join-Path $moduleRoot ([string]$sheet.path)) `
            -RelativePath ([string]$sheet.path) `
            -Kind 'runtime-atlas-sheet' `
            -AuthoritySha256 ([string]$sheet.sha256)
    }
    foreach ($partProperty in $category.parts.PSObject.Properties) {
        $partCount++
        $part = $partProperty.Value
        Add-DeploymentRow `
            -Source (Join-Path $moduleRoot ([string]$part.sourcePath)) `
            -RelativePath ([string]$part.sourcePath) `
            -Kind 'runtime-sprite-source' `
            -AuthoritySha256 ([string]$part.sourceSha256)
    }
}

Add-DeploymentRow `
    -Source (Join-Path $moduleRoot 'GUI\SpriteParts\Config.xml') `
    -RelativePath 'GUI\SpriteParts\Config.xml' `
    -Kind 'runtime-sprite-registry'
Add-DeploymentRow `
    -Source (Join-Path $moduleRoot 'GUI\ReignBetaSpriteData.xml') `
    -RelativePath 'GUI\ReignBetaSpriteData.xml' `
    -Kind 'runtime-sprite-registry' `
    -AuthoritySha256 ([string]$atlas.spriteDataSha256)
Add-DeploymentRow `
    -Source $atlasManifestPath `
    -RelativePath 'GUI\RuntimeSpriteSheets\manifest.json' `
    -Kind 'runtime-atlas-manifest'
foreach ($auxiliaryAuthority in $auxiliaryUiAuthorities) {
    Add-DeploymentRow `
        -Source (Join-Path $moduleRoot $auxiliaryAuthority.relativePath) `
        -RelativePath $auxiliaryAuthority.relativePath `
        -Kind $auxiliaryAuthority.kind `
        -AuthoritySha256 $auxiliaryAuthority.sha256
}
foreach ($relativePath in @($fixedRuntimeUiContractSha256.Keys | Sort-Object)) {
    Add-DeploymentRow `
        -Source (Join-Path $moduleRoot $relativePath) `
        -RelativePath $relativePath `
        -Kind 'fixed-runtime-ui-contract' `
        -AuthoritySha256 $fixedRuntimeUiContractSha256[$relativePath]
}

Add-DeploymentRow `
    -Source (Join-Path $moduleRoot $protectedMapRelativePath) `
    -RelativePath $protectedMapRelativePath `
    -Kind 'protected-war-council-map-source' `
    -AuthoritySha256 $protectedMapSha256
$protectedTileRelativeRoot = 'GUI\SpriteParts\ui_reignbeta_war_council'
foreach ($tileName in @($approvedProtectedTileSha256.Keys | Sort-Object)) {
    $tileRelativePath = Join-Path $protectedTileRelativeRoot $tileName
    Add-DeploymentRow `
        -Source (Join-Path $moduleRoot $tileRelativePath) `
        -RelativePath $tileRelativePath `
        -Kind 'protected-war-council-map-tile-source' `
        -AuthoritySha256 $approvedProtectedTileSha256[$tileName]
}
$protectedTileEvidenceLines = @(
    $approvedProtectedTileSha256.Keys |
        Sort-Object |
        ForEach-Object {
            $relativePath = (Join-Path $protectedTileRelativeRoot $_).Replace('\', '/')
            "$relativePath $($approvedProtectedTileSha256[$_])"
        }
)
$protectedTilePathHashEvidenceSha256 = Get-TextSha256 -Text (($protectedTileEvidenceLines -join "`n") + "`n")

Add-DeploymentRow `
    -Source (Join-Path $validatedBuildOutputRoots['client'] 'ReignBeta.dll') `
    -RelativePath 'bin\Win64_Shipping_Client\ReignBeta.dll' `
    -Kind 'validated-client-artifact' `
    -ValidatedBuildProjectId 'client'
Add-DeploymentRow `
    -Source (Join-Path $validatedBuildOutputRoots['client'] 'Reign.Core.Contracts.dll') `
    -RelativePath 'bin\Win64_Shipping_Client\Reign.Core.Contracts.dll' `
    -Kind 'validated-client-artifact' `
    -ValidatedBuildProjectId 'client'

$validatedSupportMappings = @(
    @('client', 'ModuleData\reign_tavern_cast.json', 'ModuleData\reign_tavern_cast.json', 'validated-client-content'),
    @('client', 'ReignBeta.pdb', 'bin\Win64_Shipping_Client\ReignBeta.pdb', 'validated-client-symbol'),
    @('client', 'Reign.Core.Contracts.pdb', 'bin\Win64_Shipping_Client\Reign.Core.Contracts.pdb', 'validated-client-symbol'),
    @('server', 'ReignBetaServer.exe', 'server\app\ReignBetaServer.exe', 'validated-server-artifact'),
    @('server', 'ReignBetaServer.pdb', 'server\app\ReignBetaServer.pdb', 'validated-server-symbol'),
    @('server', 'ReignBetaServer.exe.config', 'server\app\ReignBetaServer.exe.config', 'validated-server-config'),
    @('server', 'Reign.Core.Contracts.dll', 'server\app\Reign.Core.Contracts.dll', 'validated-server-artifact'),
    @('server', 'Reign.Core.Contracts.pdb', 'server\app\Reign.Core.Contracts.pdb', 'validated-server-symbol'),
    @('server', 'Reign.Relationships.dll', 'server\app\Reign.Relationships.dll', 'validated-server-artifact'),
    @('server', 'Reign.Relationships.pdb', 'server\app\Reign.Relationships.pdb', 'validated-server-symbol'),
    @('live-test', 'ReignLiveTest.exe', 'server\app\ReignLiveTest.exe', 'validated-live-test-artifact'),
    @('live-test', 'ReignLiveTest.pdb', 'server\app\ReignLiveTest.pdb', 'validated-live-test-symbol'),
    @('live-test', 'ReignLiveTest.exe.config', 'server\app\ReignLiveTest.exe.config', 'validated-live-test-config'),
    @('verification', 'ReignVerification.exe', 'server\app\ReignVerification.exe', 'validated-verification-artifact'),
    @('verification', 'ReignVerification.pdb', 'server\app\ReignVerification.pdb', 'validated-verification-symbol'),
    @('verification', 'ReignVerification.exe.config', 'server\app\ReignVerification.exe.config', 'validated-verification-config')
)
foreach ($mapping in $validatedSupportMappings) {
    Add-DeploymentRow `
        -Source (Join-Path $validatedBuildOutputRoots[$mapping[0]] $mapping[1]) `
        -RelativePath $mapping[2] `
        -Kind $mapping[3] `
        -ValidatedBuildProjectId $mapping[0]
}

$scenarioFileCount = Add-ValidatedDirectoryFiles `
    -SourceRoot (Join-Path $validatedBuildOutputRoots['live-test'] 'scenarios') `
    -DestinationRelativeRoot 'server\app\scenarios' `
    -Kind 'validated-live-test-scenario' `
    -ValidatedBuildProjectId 'live-test'
$verificationContractFileCount = Add-ValidatedDirectoryFiles `
    -SourceRoot (Join-Path $validatedBuildOutputRoots['server'] 'verification_contracts') `
    -DestinationRelativeRoot 'server\app\verification_contracts' `
    -Kind 'validated-verification-contract' `
    -ValidatedBuildProjectId 'server'

$nativePortraitGeneratorFileCount = Add-ValidatedDirectoryFiles `
    -SourceRoot (Join-Path $validatedBuildOutputRoots['server'] 'native-portrait-generator') `
    -DestinationRelativeRoot 'server\app\native-portrait-generator' `
    -Kind 'validated-native-portrait-generator' `
    -ValidatedBuildProjectId 'server'

$duplicates = @($rows | Group-Object destinationPath | Where-Object Count -gt 1)
if ($duplicates.Count -ne 0) {
    $duplicatePaths = @($duplicates | ForEach-Object Name) -join ', '
    throw "Deployment plan contains duplicate destinations: $duplicatePaths"
}
$uiAuthorityFileCount = $prefabs.Count + $sheetCount + $partCount + 3 + 2
$auxiliaryUiAuthorityFileCount = $auxiliaryUiAuthorities.Count
$fixedRuntimeUiContractFileCount = $fixedRuntimeUiContractSha256.Count
$totalUiRuntimeAuthorityFileCount = $uiAuthorityFileCount + $auxiliaryUiAuthorityFileCount + $fixedRuntimeUiContractFileCount
$protectedMapRows = @($rows | Where-Object protectedMapArt)
if ($uiAuthorityFileCount -ne $expectedUiAuthorityFileCount) {
    throw "The independently frozen core UI authority must contain exactly $expectedUiAuthorityFileCount files; found $uiAuthorityFileCount."
}
if ($auxiliaryUiAuthorityFileCount -ne $expectedAuxiliaryUiAuthorityFileCount -or $fixedRuntimeUiContractFileCount -ne 2 -or $totalUiRuntimeAuthorityFileCount -ne $expectedTotalUiRuntimeAuthorityFileCount) {
    throw "The total UI runtime authority must contain exactly $expectedTotalUiRuntimeAuthorityFileCount files ($expectedUiAuthorityFileCount core + $expectedAuxiliaryUiAuthorityFileCount auxiliary + 2 fixed contracts); found $totalUiRuntimeAuthorityFileCount."
}
if ($protectedMapRows.Count -ne $expectedProtectedMapFileCount) {
    throw "The protected War Council map authority must contain exactly $expectedProtectedMapFileCount files; found $($protectedMapRows.Count)."
}
$expectedFileCount =
    $uiAuthorityFileCount +
    $auxiliaryUiAuthorityFileCount +
    $fixedRuntimeUiContractFileCount +
    $protectedMapRows.Count +
    $validatedSupportMappings.Count +
    $scenarioFileCount +
    $verificationContractFileCount +
    $nativePortraitGeneratorFileCount
if ($rows.Count -ne $expectedFileCount) {
    throw "Manifest enumeration mismatch: expected $expectedFileCount rows; received $($rows.Count)."
}

$validatedBuildInventoryPlanLines = @(
    foreach ($projectId in @($validatedBuildInventories.Keys | Sort-Object)) {
        foreach ($inventoryRow in @($validatedBuildInventories[$projectId] | Sort-Object relativePath)) {
            "validated-build-output $projectId $($inventoryRow.relativePath) $($inventoryRow.sha256) $($inventoryRow.bytes)"
        }
    }
)
$planLines = @(
    "installedRoot $($installedRootPath.Replace('\', '/'))"
    "validationRunId $([string]$validation.RunId)"
    "validationSourceFingerprint $(([string]$validation.SourceFingerprintSha256).ToLowerInvariant())"
    "currentCanonicalSourceFingerprint $currentSourceFingerprint"
    "protectedMapSha256 $protectedMapSha256"
    "protectedTileLegacyAggregateSha256 $protectedTileLegacyAggregateSha256"
    "protectedTilePathHashEvidenceSha256 $protectedTilePathHashEvidenceSha256"
    $validatedBuildInventoryPlanLines
    $rows |
        Sort-Object relativePath |
        ForEach-Object {
            $before = if ($_.destinationExisted) { $_.beforeSha256 } else { '<absent>' }
            $existed = ([string][bool]$_.destinationExisted).ToLowerInvariant()
            $copy = ([string][bool]$_.needsCopy).ToLowerInvariant()
            "$($_.kind) $($_.relativePath.Replace('\', '/')) $($_.sourceSha256) $($_.sourceBytes) before=$before existed=$existed copy=$copy build=$($_.validatedBuildProjectId) authority=$($_.authoritySha256)"
        }
)
$planText = ($planLines -join "`n") + "`n"
$planSha256 = Get-TextSha256 -Text $planText
if ($Mode -eq 'Apply' -and -not $ExpectedPlanFingerprint) {
    throw 'Apply mode requires ExpectedPlanFingerprint from a reviewed Plan run.'
}
if ($ExpectedPlanFingerprint -and $planSha256 -ne $ExpectedPlanFingerprint.ToLowerInvariant()) {
    throw 'The deployment plan fingerprint does not match the reviewed plan.'
}

$forbiddenProcessNames = @(
    'Bannerlord',
    'Bannerlord.Native',
    'Bannerlord.BLSE.Standalone',
    'Bannerlord.BLSE.Launcher',
    'TaleWorlds.MountAndBlade.Launcher',
    'ReignBetaServer',
    'ReignLiveTest',
    'ReignVerification',
    'ReignVectorWorker'
)
$processQueryError = ''
$listenerQueryError = ''
try {
    $runningProcesses = @(
        Get-Process -ErrorAction Stop |
            Where-Object { $forbiddenProcessNames -contains $_.ProcessName }
    )
} catch {
    $runningProcesses = @()
    $processQueryError = $_.Exception.Message
}
try {
    $portListeners = @(Get-NetTCPConnection -LocalPort 5101 -State Listen -ErrorAction Stop)
} catch [Microsoft.PowerShell.Cmdletization.Cim.CimJobException] {
    if ($_.Exception.Message -match 'No matching MSFT_NetTCPConnection objects found') {
        $portListeners = @()
    } else {
        $portListeners = @()
        $listenerQueryError = $_.Exception.Message
    }
} catch {
    $portListeners = @()
    $listenerQueryError = $_.Exception.Message
}
$runtimeQuerySucceeded = -not $processQueryError -and -not $listenerQueryError
$runtimeStopped = $runtimeQuerySucceeded -and $runningProcesses.Count -eq 0 -and $portListeners.Count -eq 0
if ($Mode -eq 'Apply' -and -not $runtimeQuerySucceeded) {
    throw "Deployment refused because runtime lifecycle state could not be queried. processQueryError=$processQueryError; listenerQueryError=$listenerQueryError"
}
if ($Mode -eq 'Apply' -and -not $runtimeStopped) {
    $runningSummary = @($runningProcesses | ForEach-Object { "$($_.ProcessName):$($_.Id)" }) -join ', '
    throw "Deployment refused because the visible Reign lifetime group is not fully stopped. Processes: $runningSummary; port5101Listeners=$($portListeners.Count)."
}

$stamp = [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fff') + '-' + [System.Guid]::NewGuid().ToString('N').Substring(0, 8)
$evidenceParentPath = if ($EvidenceParent) {
    [System.IO.Path]::GetFullPath($EvidenceParent)
} else {
    Join-Path $workspaceRoot '.codex-build\ui-deployment'
}
$evidenceRoot = Join-Path $evidenceParentPath ("$($Mode.ToLowerInvariant())-$stamp")
$backupRoot = Join-Path $evidenceRoot 'backups'
[System.IO.Directory]::CreateDirectory($evidenceRoot) | Out-Null
Assert-ContainedWithoutReparsePoint -Path $evidenceRoot -Root $evidenceParentPath -Description 'Deployment evidence root'
$backupPrefix = [System.IO.Path]::GetFullPath($backupRoot).TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
$mutatedRows = [System.Collections.Generic.List[object]]::new()
$terminalError = ''
$rollbackErrors = [System.Collections.Generic.List[string]]::new()
$rolledBack = $false
$reportPath = Join-Path $evidenceRoot 'deployment-evidence.json'
$journalPath = Join-Path $evidenceRoot 'deployment-journal.json'
$journalSequence = [ref]0
$reportWritten = $false

function New-DeploymentReport {
    return [ordered]@{
        schema = 'reign-ui-exact-deployment-v3'
        generatedUtc = [DateTimeOffset]::UtcNow.ToString('O')
        mode = $Mode.ToLowerInvariant()
        ok = if ($Mode -eq 'Plan') { $true } else { -not $terminalError -and $rollbackErrors.Count -eq 0 }
        terminalError = $terminalError
        rolledBack = $rolledBack
        rollbackErrors = @($rollbackErrors)
        workspaceRoot = $workspaceRoot
        installedRoot = $installedRootPath
        validationRunId = [string]$validation.RunId
        validationReportPath = $validationReportPath
        validationSourceFingerprintSha256 = ([string]$validation.SourceFingerprintSha256).ToLowerInvariant()
        currentCanonicalSourceFingerprintSha256 = $currentSourceFingerprint
        planFingerprintSha256 = $planSha256
        applyLockPath = $applyLockPath
        journalPath = if ($Mode -eq 'Apply') { $journalPath } else { '' }
        validatedBuildOutputRoots = $validatedBuildOutputRoots
        validatedBuildInventories = $validatedBuildInventories
        authority = [ordered]@{
            catalogPath = $catalogPath
            atlasManifestPath = $atlasManifestPath
            styleManifestPath = $styleManifestPath
            catalogPrefabCount = $prefabs.Count
            atlasSheetCount = $sheetCount
            atlasPartCount = $partCount
            fixedRegistryFileCount = 3
            validatedClientArtifactCount = 2
            coreUiAuthorityFileCount = $uiAuthorityFileCount
            auxiliaryUiAuthorityFileCount = $auxiliaryUiAuthorityFileCount
            fixedRuntimeUiContractFileCount = $fixedRuntimeUiContractFileCount
            totalUiRuntimeAuthorityFileCount = $totalUiRuntimeAuthorityFileCount
            validatedSupportArtifactCount = $validatedSupportMappings.Count
            validatedScenarioFileCount = $scenarioFileCount
            validatedVerificationContractFileCount = $verificationContractFileCount
            validatedNativePortraitGeneratorFileCount = $nativePortraitGeneratorFileCount
            uniqueDestinationCount = $rows.Count
            duplicateDestinationCount = $duplicates.Count
        }
        runtimeQuerySucceeded = $runtimeQuerySucceeded
        runtimeStopped = $runtimeStopped
        processQueryError = $processQueryError
        listenerQueryError = $listenerQueryError
        runningProcesses = @($runningProcesses | ForEach-Object { "$($_.ProcessName):$($_.Id)" })
        port5101ListenerCount = $portListeners.Count
        protectedWarCouncilMap = [ordered]@{
            relativePath = $protectedMapRelativePath
            sha256 = $protectedMapSha256
            legacyOpaqueTileSetAggregateSha256 = $protectedTileLegacyAggregateSha256
            deterministicTilePathHashEvidenceSha256 = $protectedTilePathHashEvidenceSha256
            approvedTileSha256 = $approvedProtectedTileSha256
            protectedRowCount = $protectedMapRows.Count
            allInstalledByteIdentical = @($rows | Where-Object { $_.protectedMapArt -and $_.needsCopy }).Count -eq 0
        }
        exactFileCount = $rows.Count
        plannedCopyFileCount = @($rows | Where-Object needsCopy).Count
        installedVerifiedFileCount = @($rows | Where-Object status -eq 'installed-verified').Count
        alreadyMatchingFileCount = @($rows | Where-Object status -eq 'already-matching').Count
        backupRoot = if ($Mode -eq 'Apply') { $backupRoot } else { '' }
        rows = @($rows)
    }
}

function Write-TransactionJournal {
    param(
        [Parameter(Mandatory = $true)][string]$Phase,
        [string]$CurrentRelativePath = ''
    )

    $journalSequence.Value++
    $journal = [ordered]@{
        schema = 'reign-ui-deployment-journal-v1'
        updatedUtc = [DateTimeOffset]::UtcNow.ToString('O')
        sequence = $journalSequence.Value
        phase = $Phase
        currentRelativePath = $CurrentRelativePath
        processId = $PID
        installedRoot = $installedRootPath
        validationRunId = [string]$validation.RunId
        validationSourceFingerprintSha256 = ([string]$validation.SourceFingerprintSha256).ToLowerInvariant()
        planFingerprintSha256 = $planSha256
        backupRoot = $backupRoot
        terminalError = $terminalError
        rollbackErrors = @($rollbackErrors)
        rows = @($rows)
    }
    Write-DurableJson -Value $journal -Path $journalPath
}

function Invoke-DeploymentRollback {
    $errors = [System.Collections.Generic.List[string]]::new()
    $rollbackRows = @($mutatedRows)
    [System.Array]::Reverse($rollbackRows)
    foreach ($row in $rollbackRows) {
        try {
            Assert-ContainedWithoutReparsePoint -Path $row.destinationPath -Root $installedRootPath -Description 'Rollback destination'
            if ($row.destinationExisted) {
                if (-not (Test-Path -LiteralPath $row.backupPath -PathType Leaf) -or
                    (Get-Sha256 -Path $row.backupPath) -ne $row.beforeSha256) {
                    throw "Rollback backup is missing or changed: $($row.relativePath)"
                }
                if (-not (Test-Path -LiteralPath $row.destinationPath -PathType Leaf)) {
                    throw "Rollback conflict; existing destination is missing or no longer a file: $($row.relativePath)"
                }
                $currentSha256 = Get-Sha256 -Path $row.destinationPath
                if ($currentSha256 -ne $row.sourceSha256 -and $currentSha256 -ne $row.beforeSha256) {
                    throw "Rollback conflict; preserving foreign replacement: $($row.relativePath)"
                }
                if ($currentSha256 -eq $row.sourceSha256 -and $currentSha256 -ne $row.beforeSha256) {
                    $rollbackTemporaryPath = Join-Path (Split-Path -Parent $row.destinationPath) ('.reign-rollback-' + [System.Guid]::NewGuid().ToString('N') + '.tmp')
                    try {
                        Copy-Item -LiteralPath $row.backupPath -Destination $rollbackTemporaryPath
                        if ((Get-Sha256 -Path $rollbackTemporaryPath) -ne $row.beforeSha256) {
                            throw "Rollback temporary hash mismatch: $($row.relativePath)"
                        }
                        Invoke-AtomicFileReplace -ReplacementPath $rollbackTemporaryPath -DestinationPath $row.destinationPath -Description "Rollback replacement for $($row.relativePath)"
                    } finally {
                        if (Test-Path -LiteralPath $rollbackTemporaryPath -PathType Leaf) {
                            Remove-Item -LiteralPath $rollbackTemporaryPath -Force
                        }
                    }
                }
                if ((Get-Sha256 -Path $row.destinationPath) -ne $row.beforeSha256) {
                    throw "Rollback hash mismatch: $($row.relativePath)"
                }
            } elseif (Test-Path -LiteralPath $row.destinationPath) {
                if (-not (Test-Path -LiteralPath $row.destinationPath -PathType Leaf)) {
                    throw "Rollback conflict; preserving non-file replacement: $($row.relativePath)"
                }
                if ((Get-Sha256 -Path $row.destinationPath) -ne $row.sourceSha256) {
                    throw "Rollback conflict; preserving foreign replacement: $($row.relativePath)"
                }
                Remove-Item -LiteralPath $row.destinationPath -Force
                if (Test-Path -LiteralPath $row.destinationPath) {
                    throw "Rollback could not remove transaction-created file: $($row.relativePath)"
                }
            }
            $row.status = 'rolled-back'
            $row.afterSha256 = if ($row.destinationExisted) { $row.beforeSha256 } else { '' }
            try {
                Write-TransactionJournal -Phase 'rollback-row-complete' -CurrentRelativePath $row.relativePath
            } catch {
                [void]$errors.Add("Rollback journal update failed for $($row.relativePath): $($_.Exception.Message)")
            }
        } catch {
            [void]$errors.Add($_.Exception.Message)
        }
    }
    return @($errors)
}

if ($Mode -eq 'Apply') {
    try {
        [System.IO.Directory]::CreateDirectory($backupRoot) | Out-Null
        Assert-ContainedWithoutReparsePoint -Path $backupRoot -Root $evidenceRoot -Description 'Deployment backup root'
        $preMutationSourceFingerprint = Get-CanonicalValidationSourceFingerprint -Validation $validation -WorkspaceRoot $workspaceRoot
        if ($preMutationSourceFingerprint -ne $currentSourceFingerprint) {
            throw 'The canonical workspace source fingerprint changed after deployment planning.'
        }
        Write-TransactionJournal -Phase 'apply-started'
        foreach ($row in @($rows | Where-Object needsCopy)) {
            if ($row.protectedMapArt) {
                throw "Protected War Council map art cannot be copied: $($row.relativePath)"
            }

            if ((Get-Sha256 -Path $row.sourcePath) -ne $row.sourceSha256) {
                throw "Deployment source changed after planning: $($row.relativePath)"
            }
            Assert-ContainedWithoutReparsePoint -Path $row.destinationPath -Root $installedRootPath -Description 'Deployment destination'
            $currentDestinationExists = Test-Path -LiteralPath $row.destinationPath -PathType Leaf
            if ($currentDestinationExists -ne [bool]$row.destinationExisted) {
                throw "Deployment destination existence changed after planning: $($row.relativePath)"
            }
            if ($currentDestinationExists -and (Get-Sha256 -Path $row.destinationPath) -ne $row.beforeSha256) {
                throw "Deployment destination hash changed after planning: $($row.relativePath)"
            }
            $destinationDirectory = Split-Path -Parent $row.destinationPath
            [System.IO.Directory]::CreateDirectory($destinationDirectory) | Out-Null
            Assert-ContainedWithoutReparsePoint -Path $destinationDirectory -Root $installedRootPath -Description 'Deployment destination directory'
            if ($row.destinationExisted) {
                $backupPath = [System.IO.Path]::GetFullPath((Join-Path $backupRoot $row.relativePath))
                if (-not (Test-PathUnderRoot -Path $backupPath -RootPrefix $backupPrefix)) {
                    throw "Backup path escapes the evidence root: $backupPath"
                }
                [System.IO.Directory]::CreateDirectory((Split-Path -Parent $backupPath)) | Out-Null
                Copy-Item -LiteralPath $row.destinationPath -Destination $backupPath
                if ((Get-Sha256 -Path $backupPath) -ne $row.beforeSha256) {
                    throw "Backup hash mismatch: $($row.relativePath)"
                }
                $row.backupPath = $backupPath
            }

            $temporaryPath = Join-Path $destinationDirectory ('.reign-ui-' + [System.Guid]::NewGuid().ToString('N') + '.tmp')
            try {
                Copy-Item -LiteralPath $row.sourcePath -Destination $temporaryPath
                if ((Get-Sha256 -Path $temporaryPath) -ne $row.sourceSha256) {
                    throw "Temporary copy hash mismatch: $($row.relativePath)"
                }
                $row.status = 'move-pending'
                if ($row.destinationExisted) {
                    [void]$mutatedRows.Add($row)
                }
                Write-TransactionJournal -Phase 'before-move' -CurrentRelativePath $row.relativePath
                if ($row.destinationExisted) {
                    if (-not (Test-Path -LiteralPath $row.destinationPath -PathType Leaf) -or
                        (Get-Sha256 -Path $row.destinationPath) -ne $row.beforeSha256) {
                        throw "Deployment destination changed immediately before replacement: $($row.relativePath)"
                    }
                    Invoke-AtomicFileReplace -ReplacementPath $temporaryPath -DestinationPath $row.destinationPath -Description "Deployment replacement for $($row.relativePath)"
                } else {
                    [System.IO.File]::Move($temporaryPath, $row.destinationPath)
                    [void]$mutatedRows.Add($row)
                }
                $afterSha256 = Get-Sha256 -Path $row.destinationPath
                if ($afterSha256 -ne $row.sourceSha256) {
                    throw "Installed hash mismatch: $($row.relativePath)"
                }
                $row.afterSha256 = $afterSha256
                $row.status = 'installed-verified'
                Write-TransactionJournal -Phase 'after-move' -CurrentRelativePath $row.relativePath
            }
            finally {
                if (Test-Path -LiteralPath $temporaryPath -PathType Leaf) {
                    Remove-Item -LiteralPath $temporaryPath -Force
                }
            }
        }
        Write-TransactionJournal -Phase 'copies-complete'
        $report = New-DeploymentReport
        Write-DurableJson -Value $report -Path $reportPath
        $reportWritten = $true
    }
    catch {
        $terminalError = $_.Exception.Message
        try {
            Write-TransactionJournal -Phase 'rollback-started'
        } catch {
            [void]$rollbackErrors.Add("Rollback-start journal update failed: $($_.Exception.Message)")
        }
        foreach ($rollbackError in @(Invoke-DeploymentRollback)) {
            [void]$rollbackErrors.Add([string]$rollbackError)
        }
        $rolledBack = $true
        try {
            Write-TransactionJournal -Phase 'rollback-complete'
        } catch {
            [void]$rollbackErrors.Add("Rollback-complete journal update failed: $($_.Exception.Message)")
        }
    }
}

if (-not $reportWritten) {
    $report = New-DeploymentReport
    try {
        Write-DurableJson -Value $report -Path $reportPath
        $reportWritten = $true
    } catch {
        if (-not $terminalError) {
            $terminalError = "Deployment evidence write failed: $($_.Exception.Message)"
        } else {
            $terminalError += "; deployment evidence write failed: $($_.Exception.Message)"
        }
    }
}

if ($terminalError) {
    $rollbackSummary = if ($rollbackErrors.Count -eq 0) { 'rollback completed' } else { "rollback errors: $($rollbackErrors -join '; ')" }
    throw "Deployment failed; $rollbackSummary. Evidence: $reportPath; journal: $journalPath. Error: $terminalError"
}

[pscustomobject]@{
    ok = $true
    mode = $Mode.ToLowerInvariant()
    reportPath = $reportPath
    validationRunId = [string]$validation.RunId
    validationSourceFingerprintSha256 = ([string]$validation.SourceFingerprintSha256).ToLowerInvariant()
    currentCanonicalSourceFingerprintSha256 = $currentSourceFingerprint
    planFingerprintSha256 = $planSha256
    coreUiAuthorityFileCount = $uiAuthorityFileCount
    auxiliaryUiAuthorityFileCount = $auxiliaryUiAuthorityFileCount
    fixedRuntimeUiContractFileCount = $fixedRuntimeUiContractFileCount
    totalUiRuntimeAuthorityFileCount = $totalUiRuntimeAuthorityFileCount
    exactFileCount = $rows.Count
    plannedCopyFileCount = @($rows | Where-Object needsCopy).Count
    installedVerifiedFileCount = @($rows | Where-Object status -eq 'installed-verified').Count
    alreadyMatchingFileCount = @($rows | Where-Object status -eq 'already-matching').Count
    protectedMapArtFileCount = @($rows | Where-Object protectedMapArt).Count
    runtimeStopped = $runtimeStopped
    journalPath = if ($Mode -eq 'Apply') { $journalPath } else { '' }
} | ConvertTo-Json -Compress
}
finally {
    if ($null -ne $applyLockStream) {
        $applyLockStream.Dispose()
    }
}
