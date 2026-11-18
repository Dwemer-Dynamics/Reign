param(
    [Parameter(Mandatory=$true)][string]$ClientAssembly,
    [Parameter(Mandatory=$true)][string]$BannerlordPath,
    [Parameter(Mandatory=$true)][string]$EvidenceDirectory
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$working = Join-Path $EvidenceDirectory 'sandbox\bin\Win64_Shipping_Client'
[IO.Directory]::CreateDirectory($working) | Out-Null
Set-Location -LiteralPath $working
[Environment]::CurrentDirectory = $working
$gameBin = Join-Path $BannerlordPath 'bin\Win64_Shipping_Client'
$workshop = [IO.Path]::GetFullPath((Join-Path $BannerlordPath '..\..\workshop\content\261550'))
$script:feudRoots = @([IO.Path]::GetDirectoryName($ClientAssembly), $gameBin,
    (Join-Path $BannerlordPath 'Modules\Bannerlord.Harmony\bin\Win64_Shipping_Client'),
    (Join-Path $workshop '2859188632\bin\Win64_Shipping_Client'))
$resolver = [ResolveEventHandler] {
    param($sender, $eventArgs)
    $file = ([Reflection.AssemblyName]::new($eventArgs.Name)).Name + '.dll'
    foreach ($directory in $script:feudRoots) {
        $path = Join-Path $directory $file
        if ([IO.File]::Exists($path)) { return [Reflection.Assembly]::LoadFrom($path) }
    }
    return $null
}
[AppDomain]::CurrentDomain.add_AssemblyResolve($resolver)
$flags = [Reflection.BindingFlags]'Static,Instance,Public,NonPublic'
$script:feudChecks = 0
function Require([bool]$condition, [string]$message) {
    if (!$condition) { throw $message }
    $script:feudChecks++
}
$owner = 'com.bannerlordreign.blood-feuds-disabled.v1'
$sentinelOwner = 'com.bannerlordreign.test.blood-feud.sentinel'
$policy = $null
$sentinel = $null
$ok = $false
$failure = ''
$targets = @()
try {
    $client = [Reflection.Assembly]::LoadFrom($ClientAssembly)
    $campaign = [Reflection.Assembly]::LoadFrom((Join-Path $gameBin 'TaleWorlds.CampaignSystem.dll'))
    $harmonyPath = @($script:feudRoots | ForEach-Object { Join-Path $_ '0Harmony.dll' } | Where-Object { Test-Path -LiteralPath $_ })[0]
    [Reflection.Assembly]::LoadFrom($harmonyPath) | Out-Null
    Add-Type -ReferencedAssemblies @($campaign.Location) -TypeDefinition @'
using TaleWorlds.CampaignSystem;
public sealed class FeudProbeStore : IDataStore {
    public bool Loading = true;
    public int Calls;
    public bool IsSaving { get { return !Loading; } }
    public bool IsLoading { get { return Loading; } }
    public bool SyncData<T>(string key, ref T data) { Calls++; return true; }
}
public static class FeudProbeSentinel { public static void Prefix() {} }
'@
    $clanType = $campaign.GetType('TaleWorlds.CampaignSystem.Clan', $true)
    $behaviorType = $campaign.GetType('TaleWorlds.CampaignSystem.CampaignBehaviors.ExecutionCampaignBehavior', $true)
    $actionType = $campaign.GetType('TaleWorlds.CampaignSystem.Actions.ChangeBloodFeudStateAction', $true)
    $heroType = $campaign.GetType('TaleWorlds.CampaignSystem.Hero', $true)
    $getter = $clanType.GetProperty('HasBloodFeudWithPlayer').GetGetMethod()
    $setter = $clanType.GetProperty('HasBloodFeudWithPlayer').GetSetMethod()
    $clan = [Runtime.Serialization.FormatterServices]::GetUninitializedObject($clanType)
    $rawFlag = $clanType.GetField('_hasBloodFeudWithPlayer', $flags)
    $rawFlag.SetValue($clan, $true)
    Require ([bool]$getter.Invoke($clan, @())) 'Native flag control was not active.'
    $sentinel = [HarmonyLib.Harmony]::new($sentinelOwner)
    $sentinel.Patch($getter, [HarmonyLib.HarmonyMethod]::new([FeudProbeSentinel].GetMethod('Prefix')), $null, $null, $null) | Out-Null
    $policy = $client.GetType('ReignBeta.Platform.Runtime.ReignBloodFeudPatches', $true)
    $apply = $policy.GetMethod('Apply', $flags)
    $unapply = $policy.GetMethod('Unapply', $flags)
    $apply.Invoke($null, @()) | Out-Null
    $apply.Invoke($null, @()) | Out-Null
    $targets = @($getter, $setter, $actionType.GetMethod('ApplyInternal', $flags))
    foreach ($name in @('OnPlayerExecutedHero','OnPlayerClanMemberExecuted','OnBloodFeudStateChanged',
        'OnHeroTakenPrisoner','OnPrisonerDonatedToSettlement','DailyTickHero','OnGameMenuOpened',
        'OnSessionLaunched','QuarterHourlyTick','CanHeroBeReleased','SyncData')) {
        $targets += $behaviorType.GetMethod($name, $flags)
    }
    foreach ($target in $targets) {
        Require ($null -ne $target) 'Required native target is absent.'
        $info = [HarmonyLib.Harmony]::GetPatchInfo($target)
        $patches = @($info.Prefixes) + @($info.Postfixes)
        Require (@($patches | Where-Object { $_.owner -eq $owner }).Count -eq 1) ('Missing/duplicated hook: ' + $target.Name)
    }
    Require (!$getter.Invoke($clan, @())) 'Inherited active feud was visible.'
    Require ($rawFlag.GetValue($clan)) 'Reading a feud rewrote historical save state.'
    $received = $clanType.GetField('BloodFeudExecutionsReceivedCount')
    $done = $clanType.GetField('BloodFeudExecutionsDoneCount')
    $received.SetValue($clan,11)
    $done.SetValue($clan,13)
    $clans = [Array]::CreateInstance($clanType,1)
    $clans.SetValue($clan,0)
    $cleanupArguments = [object[]]::new(1)
    $cleanupArguments[0] = $clans
    $policy.GetMethod('ClearClanFeuds',$flags).Invoke($null,$cleanupArguments) | Out-Null
    Require (!$rawFlag.GetValue($clan)) 'Persisted flag survived initialization cleanup.'
    Require ($received.GetValue($clan) -eq 11 -and $done.GetValue($clan) -eq 13) 'Feud cleanup rewrote historical counters.'
    $setter.Invoke($clan, [object[]]@($true)) | Out-Null
    Require (!$rawFlag.GetValue($clan)) 'Native setter created a feud.'
    foreach ($method in $actionType.GetMethods($flags) | Where-Object { $_.IsPublic -and $_.DeclaringType -eq $actionType }) {
        $actionArguments = [object[]]::new($method.GetParameters().Count)
        $method.Invoke($null, $actionArguments) | Out-Null
        Require (!$getter.Invoke($clan, @())) ('Native feud action escaped suppression: ' + $method.Name)
    }
    $behavior = [Activator]::CreateInstance($behaviorType)
    foreach ($name in @('OnPlayerExecutedHero','OnPlayerClanMemberExecuted','OnBloodFeudStateChanged',
        'OnHeroTakenPrisoner','OnPrisonerDonatedToSettlement','DailyTickHero','OnGameMenuOpened')) {
        $method = $behaviorType.GetMethod($name, $flags)
        $arguments = @($method.GetParameters() | ForEach-Object {
            if ($_.ParameterType.IsValueType) { [Activator]::CreateInstance($_.ParameterType) } else { $null }
        })
        $method.Invoke($behavior, [object[]]$arguments) | Out-Null
        Require ($true) ('Automatic callback did not return: ' + $name)
    }
    $pending = $behaviorType.GetField('_pendingClanMemberSettlementExecutions', $flags).GetValue($behavior)
    $pendingMap = $behaviorType.GetField('_heroesPendingMapEventEndToBeExecuted', $flags).GetValue($behavior)
    $automatic = [Runtime.Serialization.FormatterServices]::GetUninitializedObject($heroType)
    $explicit = [Runtime.Serialization.FormatterServices]::GetUninitializedObject($heroType)
    $mark = $heroType.GetProperty('DeathMark')
    $mark.GetSetMethod($true).Invoke($explicit, @([Enum]::Parse($mark.PropertyType, 'ExecutionAfterMapEvent'))) | Out-Null
    $pending.Add($automatic, [Activator]::CreateInstance($pending.GetType().GetGenericArguments()[1]))
    $pendingMap.Add($automatic)
    $pendingMap.Add($explicit)
    $mainFlag = $behaviorType.GetField('_isMainHeroExecuted', $flags)
    $mainFlag.SetValue($behavior, $true)
    $store = [FeudProbeStore]::new()
    $behaviorType.GetMethod('SyncData', $flags).Invoke($behavior, @($store)) | Out-Null
    Require ($store.Calls -eq 3) 'Native save keys were removed or renamed.'
    Require ($pending.Count -eq 0) 'Inherited automatic settlement execution survived load.'
    Require (!$mainFlag.GetValue($behavior)) 'Inherited automatic player execution survived load.'
    Require ($pendingMap.Count -eq 1 -and [object]::ReferenceEquals($pendingMap[0], $explicit)) 'Explicit death mark was lost or retaliation survived.'
    $release = [object[]]@($automatic, $true)
    $behaviorType.GetMethod('CanHeroBeReleased', $flags).Invoke($behavior, $release) | Out-Null
    Require ([bool]$release[1]) 'Feud state still vetoed ordinary prisoner release.'
    $release = [object[]]@($automatic, $false)
    $behaviorType.GetMethod('CanHeroBeReleased', $flags).Invoke($behavior, $release) | Out-Null
    Require (!$release[1]) 'Feud suppression overrode another listener release veto.'
    $release = [object[]]@($explicit, $true)
    $behaviorType.GetMethod('CanHeroBeReleased', $flags).Invoke($behavior, $release) | Out-Null
    Require (!$release[1]) 'An explicit execution order lost its native release veto.'
    $behaviorType.GetMethod('OnSessionLaunched', $flags).Invoke($behavior, [object[]]@($null)) | Out-Null
    Require ($pendingMap.Count -eq 1) 'Session cleanup removed an explicit order.'
    $nativeLoyalty = $campaign.GetType('TaleWorlds.CampaignSystem.GameComponents.DefaultSettlementLoyaltyModel', $true)
    $loyalty = [Runtime.Serialization.FormatterServices]::GetUninitializedObject($nativeLoyalty)
    foreach ($pair in @(@('SettlementLoyaltyChangeDueToSecurityThreshold',60),@('LowSecurityLoyaltyEffect',-2.5),@('SettlementOwnerDifferentCultureLoyaltyEffect',-3))) {
        Require ($nativeLoyalty.GetProperty($pair[0]).GetValue($loyalty,$null) -eq $pair[1]) ('Native stability default changed: ' + $pair[0])
    }
    $reignLoyalty = $client.GetType('ReignBeta.WorldSimulation.Economy.ReignSettlementLoyaltyModel', $true)
    foreach ($name in @('RebelliousStateStartLoyaltyThreshold','RebellionStartLoyaltyThreshold')) {
        Require ($reignLoyalty.GetProperty($name).GetGetMethod().DeclaringType -eq $nativeLoyalty) ('Reign replaced a native rebellion threshold: ' + $name)
    }
    Require ($null -eq $nativeLoyalty.GetProperty('GovernorDifferentCultureLoyaltyEffect')) 'Native governor-culture API unexpectedly returned.'
    $rawFlag.SetValue($clan, $true)
    $unapply.Invoke($null, @()) | Out-Null
    $unapply.Invoke($null, @()) | Out-Null
    Require ([bool]$getter.Invoke($clan, @())) 'Unloading did not restore native getter behavior.'
    Require ([HarmonyLib.Harmony]::GetPatchInfo($getter).Owners.Contains($sentinelOwner)) 'Unloading removed a competing Harmony owner.'
    foreach ($target in $targets) {
        $info = [HarmonyLib.Harmony]::GetPatchInfo($target)
        Require ($null -eq $info -or !$info.Owners.Contains($owner)) ('Owned hook survived unload: ' + $target.Name)
    }
    $ok = $true
} catch {
    $failure = $_.Exception.ToString()
} finally {
    if ($policy) { $policy.GetMethod('Unapply', $flags).Invoke($null, @()) | Out-Null }
    if ($sentinel) { $sentinel.UnpatchAll($sentinelOwner) }
    [AppDomain]::CurrentDomain.remove_AssemblyResolve($resolver)
    [ordered]@{schema='reign-bannerlord-blood-feud-probe-v1';ok=$ok;checks=$script:feudChecks;bindings=$targets.Count;failure=$failure} |
        ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $EvidenceDirectory 'report.json') -Encoding UTF8
}
if (!$ok) { throw $failure }
