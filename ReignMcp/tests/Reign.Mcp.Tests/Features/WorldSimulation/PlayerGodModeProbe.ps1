param(
    [Parameter(Mandatory=$true)][string]$ClientAssembly,
    [Parameter(Mandatory=$true)][string]$BannerlordPath,
    [Parameter(Mandatory=$true)][string]$EvidenceDirectory,
    [ValidateSet('Ownership','UnsafeEarlyPatch','AnimationStartup')][string]$Mode='Ownership'
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$working = Join-Path $EvidenceDirectory 'sandbox\bin\Win64_Shipping_Client'
[IO.Directory]::CreateDirectory($working) | Out-Null
Set-Location -LiteralPath $working
[Environment]::CurrentDirectory = $working
$gameBin = Join-Path $BannerlordPath 'bin\Win64_Shipping_Client'
$workshop = [IO.Path]::GetFullPath((Join-Path $BannerlordPath '..\..\workshop\content\261550'))
$script:godModeRoots = @(
    [IO.Path]::GetDirectoryName($ClientAssembly), $gameBin,
    (Join-Path $BannerlordPath 'Modules\SandBox\bin\Win64_Shipping_Client'),
    (Join-Path $BannerlordPath 'Modules\Native\bin\Win64_Shipping_Client'),
    (Join-Path $BannerlordPath 'Modules\NavalDLC\bin\Win64_Shipping_Client'),
    (Join-Path $BannerlordPath 'Modules\Bannerlord.Harmony\bin\Win64_Shipping_Client'),
    (Join-Path $workshop '2859188632\bin\Win64_Shipping_Client'),
    (Join-Path $BannerlordPath 'Modules\Bannerlord.UIExtenderEx\bin\Win64_Shipping_Client'),
    (Join-Path $workshop '2859222409\bin\Win64_Shipping_Client')
)
$resolver = [ResolveEventHandler] {
    param($sender, $eventArgs)
    $file = ([Reflection.AssemblyName]::new($eventArgs.Name)).Name + '.dll'
    foreach ($directory in $script:godModeRoots) {
        $path = Join-Path $directory $file
        if ([IO.File]::Exists($path)) { return [Reflection.Assembly]::LoadFrom($path) }
    }
    return $null
}
[AppDomain]::CurrentDomain.add_AssemblyResolve($resolver)
function Require([bool]$condition, [string]$message) { if (!$condition) { throw $message } }
$flags = [Reflection.BindingFlags]'Static,Public,NonPublic'
$owner = 'com.bannerlordreign.player-god-mode.v1'
$sentinelOwner = 'com.bannerlordreign.test.player-god-mode.sentinel'
$lookupOwner = 'com.bannerlordreign.test.player-god-mode.animation-table'
$animation = @{}
$patches = $null
$ok = $false
$failure = ''
try {
    $client = [Reflection.Assembly]::LoadFrom($ClientAssembly)
    $native = [Reflection.Assembly]::LoadFrom((Join-Path $gameBin 'TaleWorlds.MountAndBlade.dll'))
    $harmonyPath = @($script:godModeRoots | ForEach-Object { Join-Path $_ '0Harmony.dll' } | Where-Object { Test-Path -LiteralPath $_ })[0]
    [Reflection.Assembly]::LoadFrom($harmonyPath) | Out-Null
    $patches = $client.GetType('ReignBeta.WorldSimulation.Combat.ReignPlayerGodModeHealthPatch', $true)
    if ($true) {
        # Only the native engine lookup is replaced. Native Agent/cache static
        # initialization and production registration run unchanged in this process.
        Add-Type -TypeDefinition @'
using System.Collections.Generic;
public static class GodModeActionTable {
    public static bool Ready;
    public static int Calls;
    private static readonly Dictionary<string,int> Codes = new Dictionary<string,int>();
    public static int Code(string name) {
        int code;
        if (!Codes.TryGetValue(name,out code)) { code=1000+Codes.Count; Codes.Add(name,code); }
        return code;
    }
    public static bool Prefix(string __0,ref int __result) {
        Calls++; __result=Ready ? Code(__0) : -1; return false;
    }
    public static void Sentinel() {}
}
'@
        $lookup = $native.GetType('TaleWorlds.MountAndBlade.MBAnimation',$true).GetMethod('GetActionCodeWithName',$flags)
        [HarmonyLib.Harmony]::new($lookupOwner).Patch($lookup,
            [HarmonyLib.HarmonyMethod]::new([GodModeActionTable].GetMethod('Prefix')), $null,$null,$null) | Out-Null
        if ($Mode -eq 'Ownership') { [GodModeActionTable]::Ready=$true }
        if ($Mode -ne 'Ownership') {
        if ($Mode -eq 'UnsafeEarlyPatch') {
            $earlyAgent=$native.GetType('TaleWorlds.MountAndBlade.Agent',$true)
            [HarmonyLib.Harmony]::new($sentinelOwner).Patch($earlyAgent.GetProperty('Health').GetSetMethod(),
                [HarmonyLib.HarmonyMethod]::new($patches.GetMethod('GuardHealth',$flags)), $null,$null,$null) | Out-Null
            $lookupsAfterPatch=[GodModeActionTable]::Calls
            # The isolated CLR can defer beforefieldinit types more than the game.
            # Explicitly exercise the legal early-initialization ordering here;
            # this is not a claim that patch registration alone reproduced it.
            [Runtime.CompilerServices.RuntimeHelpers]::RunClassConstructor($earlyAgent.TypeHandle)
            Require ([GodModeActionTable]::Calls -gt 1) 'Early Agent patch did not reproduce action-cache initialization.'
        } else {
            Require (![bool]$patches.GetMethod('TryApply',$flags).Invoke($null,@())) 'Unready animation table must defer registration.'
            Require ([GodModeActionTable]::Calls -eq 1) 'Early registration initialized the native action cache.'
        }
        $earlyCalls=[GodModeActionTable]::Calls
        [GodModeActionTable]::Ready=$true
        if ($Mode -eq 'AnimationStartup') {
            Require ([bool]$patches.GetMethod('TryApply',$flags).Invoke($null,@())) 'Ready animation table did not permit registration.'
            Require ([bool]$patches.GetMethod('TryApply',$flags).Invoke($null,@())) 'Repeated ready registration failed.'
        }
        $cache=$native.GetType('TaleWorlds.MountAndBlade.ActionIndexCache',$true)
        $named=@($cache.GetFields([Reflection.BindingFlags]'Static,Public') | Where-Object {$_.Name -ne 'act_none'})
        $invalid=0
        foreach($field in $named) { if($cache.GetProperty('Index').GetValue($field.GetValue($null),$null) -lt 0){$invalid++} }
        $idle=$cache.GetProperty('Index').GetValue($cache.GetField('act_inventory_idle_start').GetValue($null),$null)
        $nativeIdle=[GodModeActionTable]::Code('act_inventory_idle_start')
        if($Mode -eq 'UnsafeEarlyPatch') { Require ($invalid -eq $named.Count -and $idle -eq -1) 'Unsafe early patch did not poison native cached actions.' }
        else { Require ($invalid -eq 0 -and $idle -eq $nativeIdle) 'Production startup retained invalid native cached actions.' }
        $animation=@{mode=$Mode;earlyLookupCalls=$earlyCalls;namedCachedActions=$named.Count;invalidNamedActions=$invalid;idleCacheIndex=$idle;nativeIdleIndex=$nativeIdle;unsafeInitializerExplicitlyForced=($Mode -eq 'UnsafeEarlyPatch');lookupsFromUnsafePatchAlone=$(if($Mode -eq 'UnsafeEarlyPatch'){$lookupsAfterPatch}else{$null})}
        $ok=$true
        return
        }
    }
    $agentType = $native.GetType('TaleWorlds.MountAndBlade.Agent', $true)
    $setter = $agentType.GetProperty('Health').GetSetMethod()
    Require ($setter.GetParameters().Count -eq 1 -and $setter.GetParameters()[0].ParameterType -eq [single]) 'Native health signature changed.'
    $patches = $client.GetType('ReignBeta.WorldSimulation.Combat.ReignPlayerGodModeHealthPatch', $true)
    Add-Type -TypeDefinition 'public static class PlayerGodModeSentinel { public static void Prefix() {} }'
    $sentinel = [HarmonyLib.Harmony]::new($sentinelOwner)
    $sentinelMethod = [HarmonyLib.HarmonyMethod]::new([PlayerGodModeSentinel].GetMethod('Prefix'))
    $sentinel.Patch($setter, $sentinelMethod, $null, $null, $null) | Out-Null
    Require ([bool]$patches.GetMethod('TryApply', $flags).Invoke($null, @())) 'Production health guard did not install.'
    Require ([bool]$patches.GetMethod('TryApply', $flags).Invoke($null, @())) 'Repeated install failed.'
    $info = [HarmonyLib.Harmony]::GetPatchInfo($setter)
    Require (@($info.Prefixes | Where-Object { $_.owner -eq $owner }).Count -eq 1) 'God-mode prefix was absent or duplicated.'
    $production = @($info.Prefixes | Where-Object { $_.owner -eq $owner })[0]
    Require ($production.PatchMethod -eq $patches.GetMethod('GuardHealth', $flags)) 'Unexpected prefix method.'
    Require ($info.Owners.Contains($sentinelOwner)) 'Installing god mode removed another owner.'
    # SetMortalityState and its getter are managed assignments in the licensed
    # assembly. Exercise them without constructing an agent or invoking engine APIs.
    $agent = [Runtime.Serialization.FormatterServices]::GetUninitializedObject($agentType)
    $mortality = $native.GetType('TaleWorlds.MountAndBlade.Agent+MortalityState', $true)
    $invulnerable = [Enum]::Parse($mortality, 'Invulnerable')
    $agentType.GetMethod('SetMortalityState').Invoke($agent, @($invulnerable)) | Out-Null
    Require ($agentType.GetProperty('CurrentMortalityState').GetValue($agent, $null) -eq $invulnerable) 'Native mortality flag did not round trip.'
    $inputAssembly = [Reflection.Assembly]::LoadFrom((Join-Path $gameBin 'TaleWorlds.InputSystem.dll'))
    Require ([Enum]::Parse($inputAssembly.GetType('TaleWorlds.InputSystem.InputKey', $true), 'Equals').ToString() -eq 'Equals') 'Native Equals key was missing.'
    $patches.GetMethod('Unapply', $flags).Invoke($null, @()) | Out-Null
    $info = [HarmonyLib.Harmony]::GetPatchInfo($setter)
    Require (!$info.Owners.Contains($owner) -and $info.Owners.Contains($sentinelOwner)) 'Cleanup removed another owner or retained god mode.'
    $ok = $true
} catch {
    $failure = $_.Exception.ToString()
    if ($_.Exception.InnerException) { $failure += ' ' + $_.Exception.InnerException.ToString() }
} finally {
    if ($patches) { $patches.GetMethod('Unapply', $flags).Invoke($null, @()) | Out-Null }
    if ('HarmonyLib.Harmony' -as [type]) {
        [HarmonyLib.Harmony]::new($sentinelOwner).UnpatchAll($sentinelOwner)
        [HarmonyLib.Harmony]::new($lookupOwner).UnpatchAll($lookupOwner)
    }
    [AppDomain]::CurrentDomain.remove_AssemblyResolve($resolver)
    @{ ok=$ok; mode=$Mode; animation=$animation; hookBound=($ok -and $Mode -eq 'Ownership'); idempotent=$ok; otherOwnerPreserved=($ok -and $Mode -eq 'Ownership'); nativeMortalityRoundTrip=($ok -and $Mode -eq 'Ownership'); equalsKeyBound=($ok -and $Mode -eq 'Ownership'); failure=$failure } |
        ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $EvidenceDirectory 'report.json')
}
if (!$ok) { throw $failure }
