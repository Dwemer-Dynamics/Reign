param(
    [Parameter(Mandatory = $true)][string]$ClientAssembly,
    [Parameter(Mandatory = $true)][string]$Workspace,
    [Parameter(Mandatory = $true)][string]$EvidenceDirectory,
    [Parameter(Mandatory = $true)][string]$ScratchDirectory,
    [string]$BannerlordPath
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
trap { [Console]::Error.WriteLine($_.Exception.ToString() + [Environment]::NewLine + $_.ScriptStackTrace); exit 1 }
if ($env:REIGN_VALIDATION_MODE -ne '1') { throw 'This probe requires canonical isolated validation.' }
$probeRoot = [IO.Path]::GetFullPath($EvidenceDirectory)
[IO.Directory]::CreateDirectory($probeRoot) | Out-Null
$scratch = [IO.Path]::GetFullPath($ScratchDirectory)
$temporaryRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')
if (-not [string]::Equals([IO.Path]::GetDirectoryName($scratch), $temporaryRoot, [StringComparison]::OrdinalIgnoreCase) -or
    [IO.Path]::GetFileName($scratch) -notmatch '^reign-tavern-seed-[a-f0-9]{32}$') {
    throw 'The portrait probe requires its parent-owned temporary scratch directory.'
}
[IO.Directory]::CreateDirectory($scratch) | Out-Null
function Progress([string]$Stage) {
    [IO.File]::WriteAllText((Join-Path $probeRoot 'progress.json'),
        (@{ stage = $Stage; utc = [DateTime]::UtcNow.ToString('o') } | ConvertTo-Json -Compress))
}
function Require([bool]$Condition, [string]$Message) { if (!$Condition) { throw $Message } }
function Remove-ProbeDirectory([string]$Path) {
    $resolved = [IO.Path]::GetFullPath($Path)
    Require ($resolved.StartsWith($scratch + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) 'Unsafe probe cleanup path.'
    if ([IO.Directory]::Exists($resolved)) { Remove-Item -LiteralPath $resolved -Recurse -Force }
}
function Hash([string]$Path) { return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant() }
Progress 'loading-client'
$clientPath = [IO.Path]::GetFullPath($ClientAssembly)
$script:assemblyRoots = @([IO.Path]::GetDirectoryName($clientPath))
if ($BannerlordPath) { $script:assemblyRoots += Join-Path $BannerlordPath 'bin\Win64_Shipping_Client' }
$resolver = [ResolveEventHandler] {
    param($sender, $eventArgs)
    $name = ([Reflection.AssemblyName]::new($eventArgs.Name)).Name + '.dll'
    foreach ($root in $script:assemblyRoots) {
        $path = Join-Path $root $name
        if ([IO.File]::Exists($path)) { return [Reflection.Assembly]::LoadFrom($path) }
    }
    return $null
}
[AppDomain]::CurrentDomain.add_AssemblyResolve($resolver)
[Reflection.Assembly]::Load('Newtonsoft.Json') | Out-Null
$client = [Reflection.Assembly]::LoadFrom($clientPath)
$flags = [Reflection.BindingFlags]'Public,NonPublic,Static'
$install = $client.GetType('ReignBeta.Portraits.Integration.ReignTavernPortraitPack', $true).GetMethod('Install', $flags)
$seed = $client.GetType('ReignBeta.Portraits.Integration.ReignTavernHousePortraitSeed', $true)
$requiresRepair = $seed.GetMethod('RequiresSeedRepair', $flags)
$retryType = $seed.GetNestedType('VisitRetries', [Reflection.BindingFlags]::NonPublic)
$retryMethod = $retryType.GetMethod('TryHandle', [Reflection.BindingFlags]'Instance,NonPublic')
$visit = [Activator]::CreateInstance($retryType, $true)
$script:attempts = 0
$attempt = [Func[bool]] { $script:attempts++; return $script:attempts -ge 3 }
$finished = [Func[bool]] { return $true }
$unexpected = [Func[bool]] { throw 'A completed card was checked again.' }
Require (-not $retryMethod.Invoke($visit, [object[]]@('first', $attempt))) 'First pending lookup was treated as settled.'
Require (-not $retryMethod.Invoke($visit, [object[]]@('first', $attempt))) 'A pending eye click bypassed the lookup.'
Require ($retryMethod.Invoke($visit, [object[]]@('second', $finished))) 'A second card could not settle independently.'
Require ($retryMethod.Invoke($visit, [object[]]@('first', $attempt))) 'The delayed lookup was not retried.'
Require ($retryMethod.Invoke($visit, [object[]]@('first', $unexpected))) 'The admitted card did not remain settled.'
Require ($retryMethod.Invoke($visit, [object[]]@('second', $unexpected))) 'The existing portrait was checked again.'
$reopened = [Activator]::CreateInstance($retryType, $true)
$unavailable = [Func[bool]] { return $false }
Require (-not $retryMethod.Invoke($reopened, [object[]]@('first', $unavailable))) 'A new visit reused a prior admission.'
$core = $client.GetType('ReignPortraits.PortraitDerivativeCore', $true)
$current = $core.GetMethod('IsCurrentV2', $flags)
$manifestType = $client.GetType('ReignPortraits.PortraitDerivativeManifestV2', $true)
function Read-Manifest([string]$Directory) {
    return [Newtonsoft.Json.JsonConvert]::DeserializeObject(
        [IO.File]::ReadAllText((Join-Path $Directory '.portrait_derivatives.json')), $manifestType)
}
function Install-Pack([string]$Root, [string]$Cast, [string]$Target) {
    return $install.Invoke($null, [object[]]@([string]$Root, [string]$Cast, [string]$Target))
}
function Is-Current($Manifest, [string]$Master, [string]$Directory) {
    return $current.Invoke($null, [object[]]@($Manifest.PSObject.BaseObject, [string]$Master, [string]$Directory))
}
$pack = Join-Path $Workspace 'ReignBeta\TavernHousePortraits'
$inventoryPath = Join-Path $pack 'portrait-inventory.json'
$inventoryHash = Hash $inventoryPath
$inventory = Get-Content -LiteralPath $inventoryPath -Raw | ConvertFrom-Json
$expected = @{}
foreach ($file in $inventory.files) { $expected[$file.path] = $file.sha256 }
$catalog = Get-Content -LiteralPath (Join-Path $Workspace 'ReignBeta\ModuleData\reign_tavern_cast.json') -Raw | ConvertFrom-Json
$castCount = @($catalog.Towns | ForEach-Object { $_.People }).Count
Require ($castCount -eq $inventory.castCount) 'Catalog and portrait inventory disagree.'
Require ($inventory.files.Count -eq $castCount * 6) 'Every cast must have six inventory entries.'
$imageFiles = @('portrait.png', 'thumbnail.png', 'thumbnail_wide.png', 'portrait_chest.png', 'zoom.png')
function Assert-Cache([string]$Cast, [string]$Target) {
    $manifest = Read-Manifest $Target
    Require (Is-Current $manifest (Join-Path $Target 'portrait.png') $Target) ('Cache manifest rejected: ' + $Cast)
    Require ($manifest.SourcePath -eq (Join-Path $Target 'portrait.png')) ('Cache manifest retained a foreign path: ' + $Cast)
    foreach ($name in $imageFiles) {
        Require ((Hash (Join-Path $Target $name)) -eq $expected[$Cast + '/' + $name]) ('Cache pixels changed: ' + $Cast + '/' + $name)
    }
}
$firstVisit = 0; $repair = 0; $oldRejections = 0
$townReports = @()
foreach ($town in $catalog.Towns) {
    Progress ('town:' + $town.TownId)
    $townCasts = @()
    foreach ($person in $town.People) {
        $source = Join-Path $pack $person.Id
        $sourceManifestHash = Hash (Join-Path $source '.portrait_derivatives.json')
        $oldManifest = Read-Manifest $source
        $rejectedBefore = -not (Is-Current $oldManifest (Join-Path $source 'portrait.png') $source)
        if ($rejectedBefore) { $oldRejections++ }
        $target = Join-Path $scratch $person.Id
        try {
            $masterHash = Install-Pack $pack $person.Id $target
            Require ($masterHash -eq $expected[$person.Id + '/portrait.png']) 'Installed wrong master.'
            Assert-Cache $person.Id $target
            $firstVisit++
            # Reproduce the observed cache: master plus a stale manifest, with all four derivatives absent.
            foreach ($name in $imageFiles | Where-Object { $_ -ne 'portrait.png' }) { Remove-Item -LiteralPath (Join-Path $target $name) }
            Copy-Item -LiteralPath (Join-Path $source '.portrait_derivatives.json') -Destination (Join-Path $target '.portrait_derivatives.json') -Force
            Install-Pack $pack $person.Id $target | Out-Null
            Assert-Cache $person.Id $target
            $repair++
            Require ((Hash (Join-Path $source '.portrait_derivatives.json')) -eq $sourceManifestHash) 'Source manifest was modified.'
            $townCasts += @{ castId = $person.Id; oldTimestampCheckRejected = $rejectedBefore; firstVisit = $true; repair = $true }
        }
        finally { Remove-ProbeDirectory $target }
    }
    $townReports += @{ townId = $town.TownId; townName = $town.TownName; castCount = $town.People.Count; casts = $townCasts }
}
Progress 'relocated-package-and-corruption-controls'
$fixture = Join-Path $scratch 'relocated-pack'
$target = Join-Path $scratch 'relocated-cache'
$cast = $catalog.Towns[0].People[0].Id
$source = Join-Path $fixture $cast
[IO.Directory]::CreateDirectory($source) | Out-Null
Copy-Item -LiteralPath $inventoryPath -Destination (Join-Path $fixture 'portrait-inventory.json')
foreach ($name in ($imageFiles + '.portrait_derivatives.json')) {
    Copy-Item -LiteralPath (Join-Path (Join-Path $pack $cast) $name) -Destination (Join-Path $source $name)
    [IO.File]::SetLastWriteTimeUtc((Join-Path $source $name), [DateTime]::UtcNow.AddDays(-3))
}
$relocated = Read-Manifest $source
Require (-not (Is-Current $relocated (Join-Path $source 'portrait.png') $source)) 'Fixture did not reproduce old timestamp rejection.'
Install-Pack $fixture $cast $target | Out-Null
Assert-Cache $cast $target
$seedReceipt = Join-Path $target '.tavern_seed.json'
$cachedMaster = Join-Path $target 'portrait.png'
$sourceMaster = Join-Path $source 'portrait.png'
$masterHash = Hash $cachedMaster
@{ castId = $cast; masterSha256 = $masterHash; sourceSha256 = $masterHash; derivativesReady = $true } |
    ConvertTo-Json -Compress | Set-Content -LiteralPath $seedReceipt -Encoding UTF8
$repairArgs = [object[]]@([string]$seedReceipt, [string]$cast, [string]$sourceMaster, [string]$target)
Require (-not $requiresRepair.Invoke($null, $repairArgs)) 'An intact shipped portrait was scheduled for repair.'
Remove-Item -LiteralPath (Join-Path $target 'thumbnail.png')
Require ($requiresRepair.Invoke($null, $repairArgs)) 'The shipped master could not repair its missing derivative.'
$otherCast = $catalog.Towns[0].People[1].Id
Copy-Item -LiteralPath (Join-Path (Join-Path $pack $otherCast) 'portrait.png') -Destination $cachedMaster -Force
Require (-not $requiresRepair.Invoke($null, $repairArgs)) 'A stale seed receipt would overwrite a different portrait.'
Set-Content -LiteralPath $seedReceipt -Value '{' -Encoding UTF8
Require (-not $requiresRepair.Invoke($null, $repairArgs)) 'A corrupt receipt would overwrite a different portrait.'
Install-Pack $fixture $cast $target | Out-Null
Require ($requiresRepair.Invoke($null, $repairArgs)) 'An exact shipped master could not recover a corrupt receipt.'
$corruptionCases = 0
foreach ($name in @('portrait.png', 'thumbnail.png', '.portrait_derivatives.json')) {
    $file = Join-Path $source $name
    $bytes = [IO.File]::ReadAllBytes($file)
    $originalByte = $bytes[$bytes.Length - 1]
    $bytes[$bytes.Length - 1] = $originalByte -bxor 1
    [IO.File]::WriteAllBytes($file, $bytes)
    $cachedManifestHash = Hash (Join-Path $target '.portrait_derivatives.json')
    $rejected = $false
    try { Install-Pack $fixture $cast $target | Out-Null }
    catch { $rejected = $_.Exception.ToString().Contains('inventory hash') }
    Require $rejected ('Corrupt product was accepted: ' + $name)
    Assert-Cache $cast $target
    Require ((Hash (Join-Path $target '.portrait_derivatives.json')) -eq $cachedManifestHash) 'Rejected product modified the cache.'
    $bytes[$bytes.Length - 1] = $originalByte
    [IO.File]::WriteAllBytes($file, $bytes)
    $corruptionCases++
}
Remove-ProbeDirectory $target
Remove-ProbeDirectory $fixture
Require ((Hash $inventoryPath) -eq $inventoryHash) 'Source inventory was modified.'
$report = [ordered]@{
    schema = 'reign-tavern-portrait-seed-probe-v1'; ok = $true
    clientSha256 = Hash $clientPath; inventorySha256 = $inventoryHash
    catalogTownCount = $catalog.Towns.Count; catalogCastCount = $castCount
    oldTimestampCheckRejections = $oldRejections; firstVisitPassed = $firstVisit; repairPassed = $repair
    relocatedTimestampCasePassed = $true; corruptionCasesRejectedWithoutCacheChanges = $corruptionCases
    delayedLookupRetried = $true; unrelatedMasterPreserved = $true
    sourcePackUnchanged = $true; towns = $townReports
    scratchRoot = $scratch
}
$report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $probeRoot 'report.json') -Encoding UTF8
Progress 'complete'
Write-Output ('Passed: ' + $catalog.Towns.Count + ' towns, ' + $firstVisit + ' first visits, ' + $repair + ' repairs, relocated timestamps, and corruption rejection.')
