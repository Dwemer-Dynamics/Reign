param(
    [Parameter(Mandatory=$true)][string]$ClientAssembly,
    [Parameter(Mandatory=$true)][string]$BannerlordPath,
    [Parameter(Mandatory=$true)][string]$EvidenceDirectory,
    [ValidateSet('Policy','Bindings')][string]$Mode = 'Policy'
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$working = Join-Path $EvidenceDirectory 'sandbox\bin\Win64_Shipping_Client'
[IO.Directory]::CreateDirectory($working) | Out-Null
Set-Location -LiteralPath $working
[Environment]::CurrentDirectory = $working
$gameBin = Join-Path $BannerlordPath 'bin\Win64_Shipping_Client'
$workshop = [IO.Path]::GetFullPath((Join-Path $BannerlordPath '..\..\workshop\content\261550'))
$script:rbmProbeRoots = @(
    [IO.Path]::GetDirectoryName($ClientAssembly), $gameBin,
    # Match the game's explicit Harmony dependency before optional RBM payloads.
    (Join-Path $BannerlordPath 'Modules\Bannerlord.Harmony\bin\Win64_Shipping_Client'),
    (Join-Path $workshop '2859188632\bin\Win64_Shipping_Client'),
    (Join-Path $BannerlordPath 'Modules\SandBox\bin\Win64_Shipping_Client'),
    (Join-Path $BannerlordPath 'Modules\RBM\bin\Win64_Shipping_Client'),
    (Join-Path $BannerlordPath 'Modules\Native\bin\Win64_Shipping_Client'),
    (Join-Path $BannerlordPath 'Modules\StoryMode\bin\Win64_Shipping_Client'),
    (Join-Path $BannerlordPath 'Modules\NavalDLC\bin\Win64_Shipping_Client')
)
$resolver = [ResolveEventHandler] {
    param($sender, $eventArgs)
    $file = ([Reflection.AssemblyName]::new($eventArgs.Name)).Name + '.dll'
    foreach ($directory in $script:rbmProbeRoots) {
        $path = Join-Path $directory $file
        if ([IO.File]::Exists($path)) { return [Reflection.Assembly]::LoadFrom($path) }
    }
    return $null
}
[AppDomain]::CurrentDomain.add_AssemblyResolve($resolver)
$flags = [Reflection.BindingFlags]'Static,Public,NonPublic'
$script:rbmChecks = 0
function Require([bool]$condition, [string]$message) {
    if (!$condition) { throw $message }
    $script:rbmChecks++
}
$policy = $null
$ok = $false
$failure = ''
$settings = @()
$contracts = @()
$autoResolveGateVerified = $false
$startupCallerVerified = $false
$legacyCallerFailureReproduced = $false
$generatedCallerFailureKind = ''
try {
    $client = [Reflection.Assembly]::LoadFrom($ClientAssembly)
    $harmonyPath = @($script:rbmProbeRoots | ForEach-Object { Join-Path $_ '0Harmony.dll' } | Where-Object { Test-Path -LiteralPath $_ })[0]
    [Reflection.Assembly]::LoadFrom($harmonyPath) | Out-Null
    $policy = $client.GetType('ReignBeta.Compatibility.ReignRbmCompatibility', $true)
    $settings = @($policy.GetField('DisabledSettings', $flags).GetValue($null))
    $contracts = @($policy.GetField('Suppressions', $flags).GetValue($null))
    Require ($settings.Count -eq 24) 'The reviewed off policy changed.'
    Require ($contracts.Count -eq 11) 'The reviewed noncombat hook list changed.'
    Require (@($settings | Where-Object { $_[0] -in @('rbmCombatEnabled','rbmAiEnabled','playerPostureMultiplier','bluntTraumaBonus') }).Count -eq 0) 'Combat/AI choices or numeric settings were included in the off policy.'
    if ($Mode -eq 'Bindings') {
        Add-Type -ReferencedAssemblies @('System.dll','System.Core.dll',$harmonyPath) -TypeDefinition @'
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
public static class RbmBindingDiagnostics {
    public static readonly List<string> Calls = new List<string>();
    public static bool GeneratedCallerHasNoDeclaringType;
    public static void Report(string message) {}
    public static bool CaptureExplicit(Harmony __instance, Assembly __0) {
        if (__instance.Id != "com.rbmmain" && __instance.Id != "com.rbmcombat"
            && __instance.Id != "com.rbmstance" && __instance.Id != "reign.test.rbm.implicit") return true;
        Calls.Add(__instance.Id + ":" + __0.GetName().Name);
        return false; // Process-local assembly-discovery boundary, no native patch set.
    }
    public static bool SkipEngine() { return false; }
    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    public static void ImplicitCaller(Harmony harmony) { harmony.PatchAll(); }
    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    public static void GeneratedCaller(Harmony harmony, Module intendedModule) {
        var method = new DynamicMethod("RBM_legacy_generated_caller", typeof(void), new[] { typeof(Harmony) }, intendedModule, true);
        GeneratedCallerHasNoDeclaringType = method.ReflectedType == null;
        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, typeof(Harmony).GetMethod("PatchAll", System.Type.EmptyTypes));
        il.Emit(OpCodes.Ret);
        ((System.Action<Harmony>)method.CreateDelegate(typeof(System.Action<Harmony>)))(harmony);
    }
}
'@
        $policy.GetField('Report', $flags).SetValue($null, [Delegate]::CreateDelegate([Action[string]], [RbmBindingDiagnostics].GetMethod('Report')))
        $primary = [Reflection.Assembly]::LoadFrom((Join-Path $BannerlordPath 'Modules\RBM\bin\Win64_Shipping_Client\RBM.dll'))
        $policy.GetMethod('Bootstrap', $flags).Invoke($null, @()) | Out-Null
        $policy.GetMethod('BeforeSubmoduleLoads', $flags).Invoke($null, @()) | Out-Null
        Require ($policy.GetField('_installed', $flags).GetValue($null)) 'Real RBM signatures/dependencies did not bind before callbacks.'
        $config = $policy.GetField('_config', $flags).GetValue($null)
        foreach ($setting in $settings) { Require (!$config.GetField($setting[0]).GetValue($null)) ('Real config policy did not disable ' + $setting[0]) }
        $autoResolve=[AppDomain]::CurrentDomain.GetAssemblies() | Where-Object {$_.GetName().Name -eq 'RBMAutoResolve'} | Select-Object -First 1
        $simulationGate=$autoResolve.GetType('RBMAutoResolve.RBMAutoResolvePatcher',$true).GetProperty('Enabled',$flags)
        Require (-not $simulationGate.GetValue($null,$null)) 'Real standalone auto-resolve remained enabled with the combat-only policy.'
        $configAssembly = $config.Assembly
        $utilities = $configAssembly.GetType('RBMConfig.Utilities', $true)
        $defaults = [System.Xml.XmlDocument]::new()
        $arguments = [object[]]@($defaults)
        $utilities.GetMethod('createXmlConfig', $flags).Invoke($null, $arguments) | Out-Null
        Require ($arguments[0].SelectSingleNode('/Config/RBMCampaign/Enabled').InnerText -eq '1') 'Real default generation persisted an enforced false Campaign preference.'
        # 4.8.1 creates this newer setting lazily in ReadOrCreate, not in the
        # default-document factory. Neither path may persist the live false overlay.
        $simulationNode=$arguments[0].SelectSingleNode('/Config/RBMCampaign/SimulationEquipmentEnabled')
        Require ($null -eq $simulationNode -or $simulationNode.InnerText -eq '1') 'Real default generation persisted an enforced false auto-resolve preference.'
        $xmlField=$config.GetField('xmlConfig',$flags)
        $previousXml=$xmlField.GetValue($null)
        try {
            $xmlField.SetValue($null,$arguments[0])
            $read=$config.GetMethod('ReadOrCreate',$flags).Invoke($null,[object[]]@('/Config/RBMCampaign','SimulationEquipmentEnabled','1'))
            Require ($read -eq '1') 'Real lazy setting migration changed the saved simulation preference.'
            Require ($arguments[0].SelectSingleNode('/Config/RBMCampaign/SimulationEquipmentEnabled').InnerText -eq '1') 'Real lazy setting migration wrote the live false overlay.'
        } finally {$xmlField.SetValue($null,$previousXml)}
        Require (-not $simulationGate.GetValue($null,$null)) 'Real default generation enabled standalone auto-resolve.'
        Require (!$config.GetField('rbmCampaignEnabled').GetValue($null)) 'Real default generation enabled live Campaign.'
        $policy.GetMethod('AfterLoad', $flags).Invoke($null, @()) | Out-Null
        Require ($policy.GetField('_economicHooksInstalled', $flags).GetValue($null)) 'Real licensed economic hooks did not install before item initialization.'
        foreach ($contract in $contracts) {
            $assemblyName = if ($contract[0] -eq 'com.rbmai') {'RBMAI'} else {'RBMCombat'}
            $assembly = [AppDomain]::CurrentDomain.GetAssemblies() | Where-Object {$_.GetName().Name -eq $assemblyName} | Select-Object -First 1
            $type = $assembly.GetType($contract[1], $true)
            $patchMethod=$type.GetMethod($contract[2], $flags)
            Require ($null -ne $patchMethod) ('Real suppression method was not bound: ' + $contract[1] + '.' + $contract[2])
            $attributes=@($type.GetCustomAttributes([HarmonyLib.HarmonyPatch],$true))+@($patchMethod.GetCustomAttributes([HarmonyLib.HarmonyPatch],$true))
            $declaringTypes=@($attributes | ForEach-Object {$_.info.declaringType} | Where-Object {$null -ne $_})
            $methodNames=@($attributes | ForEach-Object {$_.info.methodName} | Where-Object {$null -ne $_})
            Require ($methodNames -contains $contract[3]) ('Real Harmony target changed: ' + $contract[1])
            $nativeTargets=@($declaringTypes | ForEach-Object {$_.GetMethods([Reflection.BindingFlags]'Public,NonPublic,Instance,Static')} | Where-Object {$_.Name -eq $contract[3]})
            Require ($nativeTargets.Count -gt 0) ('Real Harmony target is absent from licensed native assemblies: ' + $contract[3])
        }
        $startupScope=[HarmonyLib.Harmony]::new('reign.test.rbm.startup')
        try {
            $explicit=[HarmonyLib.Harmony].GetMethod('PatchAll',[Type[]]@([Reflection.Assembly]))
            $startupScope.Patch($explicit,[HarmonyLib.HarmonyMethod]::new([RbmBindingDiagnostics].GetMethod('CaptureExplicit')),$null,$null,$null) | Out-Null
            $implicit=[RbmBindingDiagnostics].GetMethod('ImplicitCaller')
            $testHarmony=[HarmonyLib.Harmony]::new('reign.test.rbm.implicit')
            $implicit.Invoke($null,[object[]]@($testHarmony)) | Out-Null
            Require ([RbmBindingDiagnostics]::Calls.Count -eq 1) 'Unpatched implicit caller did not reach explicit assembly discovery.'
            Require ([RbmBindingDiagnostics]::Calls[0] -eq ('reign.test.rbm.implicit:'+([RbmBindingDiagnostics].Assembly.GetName().Name))) 'Unpatched implicit caller discovered the wrong assembly.'
            [RbmBindingDiagnostics]::Calls.Clear()
            try {
                [RbmBindingDiagnostics].GetMethod('GeneratedCaller').Invoke($null,[object[]]@($testHarmony,$primary.ManifestModule)) | Out-Null
                $legacyCallerFailureReproduced=(([RbmBindingDiagnostics]::Calls -join '|') -ne 'reign.test.rbm.implicit:RBM')
                if($legacyCallerFailureReproduced){$generatedCallerFailureKind='wrong_assembly'}
            }
            catch {
                $failureException=$_.Exception
                while($null -ne $failureException.InnerException){$failureException=$failureException.InnerException}
                if($failureException -isnot [NullReferenceException]){throw}
                $legacyCallerFailureReproduced=$true
                $generatedCallerFailureKind='null_reflected_type'
            }
            Require ($legacyCallerFailureReproduced -and [RbmBindingDiagnostics]::GeneratedCallerHasNoDeclaringType) 'Generated implicit caller did not expose missing/wrong assembly discovery.'
            $aiAssembly=[AppDomain]::CurrentDomain.GetAssemblies() | Where-Object {$_.GetName().Name -eq 'RBMAI'} | Select-Object -First 1
            $aiFirst=$aiAssembly.GetType('RBMAI.RBMAiPatcher',$true).GetMethod('FirstPatch',$flags)
            $combatAssembly=[AppDomain]::CurrentDomain.GetAssemblies() | Where-Object {$_.GetName().Name -eq 'RBMCombat'} | Select-Object -First 1
            $display=$combatAssembly.GetType('RBMCombat.TroopPerkDisplay',$true).GetMethod('ApplyDeferred',$flags)
            foreach($engineBoundary in @($aiFirst,$display)){
                $startupScope.Patch($engineBoundary,[HarmonyLib.HarmonyMethod]::new([RbmBindingDiagnostics].GetMethod('SkipEngine')),$null,$null,$null) | Out-Null
            }
            $apply=$primary.GetType('RBM.SubModule',$true).GetMethod('ApplyHarmonyPatches',$flags)
            for($pass=0;$pass -lt 2;$pass++){
                [RbmBindingDiagnostics]::Calls.Clear()
                $apply.Invoke($null,@()) | Out-Null
                Require (([RbmBindingDiagnostics]::Calls -join '|') -eq 'com.rbmmain:RBM|com.rbmcombat:RBMCombat|com.rbmstance:RBMStance') 'Real RBM startup/reapply selected a missing or wrong patch assembly.'
                foreach($setting in $settings){Require (-not $config.GetField($setting[0]).GetValue($null)) ('Real reapply escaped the off policy: '+$setting[0])}
            }
            $startupCallerVerified=$true
        } finally {$startupScope.UnpatchAll($startupScope.Id)}
        $policy.GetMethod('Unload', $flags).Invoke($null, @()) | Out-Null
        Require ($config.GetField('rbmCampaignEnabled').GetValue($null)) 'Unloading real bindings did not restore original default fields.'
        Require ($simulationGate.GetValue($null,$null)) 'Unloading real bindings did not restore the auto-resolve preference.'
        $autoResolveGateVerified=$true
        $ok = $true
        return
    }
    $fields = ($settings | ForEach-Object { 'public static bool ' + $_[0] + '=true;' }) -join "`n"
    $harness = @'
using System;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Xml;
public static class RbmPolicyFixture {
    public static XmlDocument xmlConfig = new XmlDocument();
    public static bool rbmCombatEnabled = true, rbmAiEnabled = true;
    public static float ArmorPriceModifier = 1;
    public static int playerPostureMultiplier = 0, bluntTraumaBonus = 0;
    FIELDS
}
public static class RbmPolicyNativeControls {
    public static int SentinelCalls;
    public static int Warnings;
    public static string SavePath;
    public static bool FailPath;
    public static string GetSavePath() { if (FailPath) throw new IOException("injected preference-path failure"); return SavePath; }
    public static void Report(string message) { Warnings++; }
    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    public static int GetBasicVolunteer(bool castleBound) { return castleBound ? 2 : 1; }
    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    public static int CombatDamage() { return 5; }
    public static void Sentinel() { SentinelCalls++; }
    public static bool UnknownPrefix() { return true; }
    public static MethodInfo MakePatch(string assembly, string parent, string nested, string method) {
        var builder = AppDomain.CurrentDomain.DefineDynamicAssembly(new AssemblyName(assembly), AssemblyBuilderAccess.Run);
        var module = builder.DefineDynamicModule(assembly);
        var outer = module.DefineType(parent, TypeAttributes.Public);
        var inner = outer.DefineNestedType(nested, TypeAttributes.NestedPublic);
        var prefix = inner.DefineMethod(method, MethodAttributes.Public | MethodAttributes.Static, typeof(bool), Type.EmptyTypes);
        var il = prefix.GetILGenerator(); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Ret);
        var type = inner.CreateType(); outer.CreateType(); return type.GetMethod(method);
    }
}
'@
    Add-Type -TypeDefinition $harness.Replace('FIELDS', $fields).Replace('using System;', 'using System; using System.IO;') -ReferencedAssemblies @('System.dll','System.Core.dll','System.Xml.dll')
    $policy.GetField('Report', $flags).SetValue($null, [Delegate]::CreateDelegate([Action[string]], [RbmPolicyNativeControls].GetMethod('Report')))
    [RbmPolicyNativeControls]::SavePath = Join-Path $EvidenceDirectory 'rbm-preferences.xml'
    $policy.GetField('ConfigPath', $flags).SetValue($null, [Delegate]::CreateDelegate([Func[string]], [RbmPolicyNativeControls].GetMethod('GetSavePath')))
    $configType = [RbmPolicyFixture]
    $document = [RbmPolicyFixture]::xmlConfig
    $document.LoadXml('<Config version="2"><RBMTournament/><RBMCampaign/><RBMAI/><RBMCombat><Global/><PriceModifiers><ArmorPriceModifier>1.0</ArmorPriceModifier></PriceModifiers></RBMCombat></Config>')
    foreach ($setting in $settings) {
        $parentPath = $setting[1].Substring(0, $setting[1].LastIndexOf('/'))
        $parent = $document.SelectSingleNode($parentPath)
        $node = $document.CreateElement($setting[1].Substring($setting[1].LastIndexOf('/') + 1))
        $node.InnerText = if ($setting[0] -eq 'developerMode') { 'true' } else { '1' }
        $parent.AppendChild($node) | Out-Null
    }
    $original = $document.OuterXml
    [IO.File]::WriteAllText([RbmPolicyNativeControls]::SavePath, $original)
    $originalBytes = [Convert]::ToBase64String([IO.File]::ReadAllBytes([RbmPolicyNativeControls]::SavePath))
    $policy.GetMethod('ValidateConfig', $flags).Invoke($null, @($configType)) | Out-Null
    $policy.GetMethod('AfterParse', $flags).Invoke($null, @()) | Out-Null
    $policy.GetMethod('AfterLoad', $flags).Invoke($null, @()) | Out-Null
    foreach ($setting in $settings) {
        Require (!$configType.GetField($setting[0]).GetValue($null)) ('Policy did not disable ' + $setting[0])
    }
    Require ([RbmPolicyFixture]::rbmCombatEnabled -and [RbmPolicyFixture]::rbmAiEnabled) 'The policy changed Combat/AI choices.'
    Require ([RbmPolicyFixture]::playerPostureMultiplier -eq 0 -and [RbmPolicyFixture]::bluntTraumaBonus -eq 0) 'The policy changed numeric controls.'
    foreach ($setting in $settings) { $document.SelectSingleNode($setting[1]).InnerText = '0' }
    $document.SelectSingleNode('/Config/RBMCombat/PriceModifiers/ArmorPriceModifier').InnerText = '1'
    $saveArgs = [object[]]@($document)
    $write = $policy.GetMethod('PrepareSave', $flags).Invoke($null, $saveArgs)
    Require (!$write) 'A no-edit save would rewrite disk preferences.'
    Require ([Convert]::ToBase64String([IO.File]::ReadAllBytes([RbmPolicyNativeControls]::SavePath)) -eq $originalBytes) 'A no-edit save changed file bytes.'
    Require ($document.SelectSingleNode('/Config/DeveloperMode').InnerText -eq '0') 'Preparing a save mutated the live document.'
    $document.SelectSingleNode('/Config/RBMCombat/PriceModifiers/ArmorPriceModifier').InnerText = '1.5'
    $saveArgs = [object[]]@($document)
    Require ($policy.GetMethod('PrepareSave', $flags).Invoke($null, $saveArgs)) 'A legitimate non-forced edit was discarded.'
    Require ($saveArgs[0].SelectSingleNode('/Config/DeveloperMode').InnerText -eq 'true') 'The serialized preference text was lost.'
    Require ($saveArgs[0].SelectSingleNode('/Config/RBMCombat/PriceModifiers/ArmorPriceModifier').InnerText -eq '1.5') 'The non-forced edit was not serialized.'
    $saveArgs[0].Save([RbmPolicyNativeControls]::SavePath)
    $document.SelectSingleNode('/Config/RBMCombat/PriceModifiers/ArmorPriceModifier').InnerText = '1'
    $revertArgs = [object[]]@($document)
    Require ($policy.GetMethod('PrepareSave', $flags).Invoke($null, $revertArgs)) 'Saving a revert was lost against a stale load-time baseline.'
    $revertArgs[0].Save([RbmPolicyNativeControls]::SavePath)
    $noEditArgs = [object[]]@($document)
    Require (!$policy.GetMethod('PrepareSave', $flags).Invoke($null, $noEditArgs)) 'A repeated save after reverting was not a no-op.'
    $policy.GetField('_preferenceSourceReady', $flags).SetValue($null, $false)
    $beforeFailure = [Convert]::ToBase64String([IO.File]::ReadAllBytes([RbmPolicyNativeControls]::SavePath))
    Require (!$policy.GetMethod('PrepareSave', $flags).Invoke($null, [object[]]@($document))) 'An unproven preference snapshot was serialized after parse failure.'
    Require ([Convert]::ToBase64String([IO.File]::ReadAllBytes([RbmPolicyNativeControls]::SavePath)) -eq $beforeFailure) 'A parse-failure save changed file bytes.'
    $policy.GetField('_preferenceSourceReady', $flags).SetValue($null, $true)
    [RbmPolicyNativeControls]::FailPath = $true
    Require (!$policy.GetMethod('PrepareSave', $flags).Invoke($null, [object[]]@($document))) 'A preference-path failure did not block serialization safely.'
    [RbmPolicyNativeControls]::FailPath = $false
    $malformed = '<Config broken'
    [IO.File]::WriteAllText([RbmPolicyNativeControls]::SavePath, $malformed)
    $recoveryArgs = [object[]]@($document)
    Require ($policy.GetMethod('PrepareSave', $flags).Invoke($null, $recoveryArgs)) 'A verified native-default document could not recover corrupt XML.'
    $backups = @(Get-ChildItem -LiteralPath $EvidenceDirectory -Filter 'rbm-preferences.xml.reign-corrupt-*.bak' -File)
    Require ($backups.Count -eq 1 -and [IO.File]::ReadAllText($backups[0].FullName) -eq $malformed) 'Corrupt config recovery did not preserve exact original bytes.'
    $recoveryArgs[0].Save([RbmPolicyNativeControls]::SavePath)
    Require (![RbmPolicyFixture]::rbmCampaignEnabled) 'Serialization temporarily enabled Campaign.'
    $injected = [IO.IOException]::new('fixture failure')
    $policy.GetMethod('BeforeSave', $flags).Invoke($null, @()) | Out-Null
    $policy.GetMethod('AfterParse', $flags).Invoke($null, @()) | Out-Null
    $returned = $policy.GetMethod('AfterSave', $flags).Invoke($null, @($injected))
    Require ([object]::ReferenceEquals($injected, $returned)) 'Save finalization swallowed the injected error.'
    Require ($document.SelectSingleNode('/Config/DeveloperMode').InnerText -eq 'true') 'Save finalization did not restore the preference document.'
    Require (![RbmPolicyFixture]::rbmCampaignEnabled) 'Save finalization failed to restore the effective policy.'
    $defaults = [System.Xml.XmlDocument]::new()
    $defaults.LoadXml($original)
    foreach ($setting in $settings) { $defaults.SelectSingleNode($setting[1]).InnerText = '0' }
    $defaultArgs = [object[]]@($defaults)
    $policy.GetMethod('AfterDefaults', $flags).Invoke($null, $defaultArgs) | Out-Null
    Require ($defaults.SelectSingleNode('/Config/RBMCampaign/Enabled').InnerText -eq '1') 'Creating defaults captured an enforced false flag as a preference.'
    Require ($defaults.SelectSingleNode('/Config/DeveloperMode').InnerText -eq 'true') 'Creating defaults lost original preference text.'
    Require (![RbmPolicyFixture]::rbmCampaignEnabled) 'Creating defaults temporarily enabled the live Campaign flag.'
    [RbmPolicyFixture]::rbmCampaignEnabled = $true
    $returned = $policy.GetMethod('EnforceFinally', $flags).Invoke($null, @($injected))
    Require ([object]::ReferenceEquals($injected, $returned) -and ![RbmPolicyFixture]::rbmCampaignEnabled) 'Load/parse failure did not retain the off policy.'

    $target = [RbmPolicyNativeControls].GetMethod('GetBasicVolunteer')
    $prefix = [RbmPolicyNativeControls]::MakePatch('RBMCombat','RBMCombat.CampaignChanges','DefaultVolunteerModelPatch','Prefix')
    $sentinel = [HarmonyLib.Harmony]::new('reign.test.rbm.sentinel')
    $sentinel.Patch($target, [HarmonyLib.HarmonyMethod]::new([RbmPolicyNativeControls].GetMethod('Sentinel'))) | Out-Null
    $rbm = [HarmonyLib.Harmony]::new('com.rbmcombat')
    for ($battle = 0; $battle -lt 2; $battle++) {
        $rbm.Patch($target, [HarmonyLib.HarmonyMethod]::new($prefix)) | Out-Null
        Require ([RbmPolicyNativeControls]::GetBasicVolunteer($true) -eq 0) 'The RBM competition prefix did not run.'
        $policy.GetMethod('Reconcile', $flags).Invoke($null, @()) | Out-Null
        Require ([RbmPolicyNativeControls]::GetBasicVolunteer($true) -eq 2) 'Native castle control was not restored.'
        Require ([RbmPolicyNativeControls]::GetBasicVolunteer($false) -eq 1) 'Native regular control was not restored.'
        Require ([HarmonyLib.Harmony]::GetPatchInfo($target).Owners.Contains('reign.test.rbm.sentinel')) 'Reconciliation removed an unrelated owner.'
    }
    Require ([RbmPolicyNativeControls]::SentinelCalls -ge 4) 'The retained sentinel was not executed.'
    # The same MethodInfo registered under another owner must survive removal.
    $other = [HarmonyLib.Harmony]::new('reign.test.rbm.same-method')
    $other.Patch($target, [HarmonyLib.HarmonyMethod]::new($prefix)) | Out-Null
    $rbm.Patch($target, [HarmonyLib.HarmonyMethod]::new($prefix)) | Out-Null
    $policy.GetMethod('Reconcile', $flags).Invoke($null, @()) | Out-Null
    Require ([HarmonyLib.Harmony]::GetPatchInfo($target).Owners.Contains('reign.test.rbm.same-method')) 'Method-only unpatch removed another owner.'
    Require (![HarmonyLib.Harmony]::GetPatchInfo($target).Owners.Contains('com.rbmcombat')) 'The reviewed owner was retained unexpectedly.'
    $other.UnpatchAll('reign.test.rbm.same-method')
    # An unknown prefix sharing the reviewed owner blocks removal on that target.
    $rbm.Patch($target, [HarmonyLib.HarmonyMethod]::new($prefix)) | Out-Null
    $rbm.Patch($target, [HarmonyLib.HarmonyMethod]::new([RbmPolicyNativeControls].GetMethod('UnknownPrefix'))) | Out-Null
    $policy.GetMethod('Reconcile', $flags).Invoke($null, @()) | Out-Null
    Require (@([HarmonyLib.Harmony]::GetPatchInfo($target).Prefixes | Where-Object { $_.owner -eq 'com.rbmcombat' }).Count -eq 2) 'An unexpected same-owner prefix was removed.'
    Require ([RbmPolicyNativeControls]::Warnings -ge 1) 'An incomplete suppression was not reported.'
    $rbm.UnpatchAll('com.rbmcombat')
    $combatTarget = [RbmPolicyNativeControls].GetMethod('CombatDamage')
    $rbm.Patch($combatTarget, [HarmonyLib.HarmonyMethod]::new($prefix)) | Out-Null
    $policy.GetMethod('Unload', $flags).Invoke($null, @()) | Out-Null
    Require ([HarmonyLib.Harmony]::GetPatchInfo($target).Owners.Contains('com.rbmcombat')) 'Unload did not restore the exact suppressed RBM prefix.'
    Require ([RbmPolicyFixture]::rbmCampaignEnabled) 'Unload did not restore the original preference field.'
    $policy.GetMethod('ValidateConfig', $flags).Invoke($null, @($configType)) | Out-Null
    $policy.GetMethod('AfterParse', $flags).Invoke($null, @()) | Out-Null
    $policy.GetMethod('Reconcile', $flags).Invoke($null, @()) | Out-Null
    [RbmPolicyFixture]::rbmCombatEnabled = $false
    $rbm.UnpatchAll('com.rbmcombat')
    $policy.GetMethod('Unload', $flags).Invoke($null, @()) | Out-Null
    Require (![HarmonyLib.Harmony]::GetPatchInfo($target).Owners.Contains('com.rbmcombat')) 'Unload resurrected a deliberately disabled RBM owner.'
    $ok = $true
}
catch { $failure = $_.Exception.ToString() }
finally {
    foreach ($id in @('com.rbmcombat','reign.test.rbm.sentinel','reign.test.rbm.same-method')) {
        if ('HarmonyLib.Harmony' -as [type]) { [HarmonyLib.Harmony]::new($id).UnpatchAll($id) }
    }
    [AppDomain]::CurrentDomain.remove_AssemblyResolve($resolver)
    @{ ok=$ok; checks=$script:rbmChecks; disabledSettings=$settings.Count; suppressionContracts=$contracts.Count; autoResolveGateVerified=$autoResolveGateVerified; startupCallerVerified=$startupCallerVerified; legacyCallerFailureReproduced=$legacyCallerFailureReproduced; generatedCallerFailureKind=$generatedCallerFailureKind;
        scope='isolated production policy plus real Harmony; not native campaign/market or cold-start acceptance'; failure=$failure } |
        ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $EvidenceDirectory 'report.json') -Encoding UTF8
}
if (!$ok) { throw $failure }
