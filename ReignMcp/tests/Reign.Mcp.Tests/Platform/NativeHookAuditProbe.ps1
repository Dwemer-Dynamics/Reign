param([string]$ClientAssembly,[string]$BannerlordPath,[string]$EvidenceDirectory,[string]$SourceRoot)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
[IO.Directory]::CreateDirectory($EvidenceDirectory) | Out-Null
$sandbox=Join-Path $EvidenceDirectory 'sandbox/bin/Win64_Shipping_Client'
[IO.Directory]::CreateDirectory($sandbox) | Out-Null
Set-Location $sandbox
[Environment]::CurrentDirectory=$sandbox
$roots=@([IO.Path]::GetDirectoryName($ClientAssembly),(Join-Path $BannerlordPath 'bin/Win64_Shipping_Client'))
foreach($module in @('Native','SandBox','StoryMode','NavalDLC','Bannerlord.Harmony','Bannerlord.ButterLib','Bannerlord.UIExtenderEx','Bannerlord.MBOptionScreen')){
    $roots+=Join-Path $BannerlordPath ('Modules/'+$module+'/bin/Win64_Shipping_Client')
}
$resolver=[ResolveEventHandler]{param($sender,$eventArgs)
    $name=([Reflection.AssemblyName]::new($eventArgs.Name)).Name+'.dll'
    foreach($root in $roots){$p=Join-Path $root $name;if([IO.File]::Exists($p)){return [Reflection.Assembly]::LoadFrom($p)}}
    return $null
}
[AppDomain]::CurrentDomain.add_AssemblyResolve($resolver)
$flags=[Reflection.BindingFlags]'Static,Instance,Public,NonPublic'
$owner='com.bannerlordreign.encountered_residents'
$garrisonOwner='reign.fixture.native-garrison-arguments'
$garrisonChecks=0;$legacyGarrisonArityRejected=$false
$ok=$false;$failure='';$bindings=@();$rows=[Collections.Generic.List[object]]::new();$unresolved=[Collections.Generic.List[object]]::new()
try {
    [Reflection.Assembly]::LoadFrom((Join-Path $BannerlordPath 'Modules/Bannerlord.Harmony/bin/Win64_Shipping_Client/0Harmony.dll')) | Out-Null
    foreach($root in $roots){if(Test-Path $root){foreach($file in Get-ChildItem $root -Filter '*.dll'){
        if($file.Name -match '^(TaleWorlds\.|SandBox|StoryMode|NavalDLC)'){try{[Reflection.Assembly]::LoadFrom($file.FullName)|Out-Null}catch{}}
    }}}
    $client=[Reflection.Assembly]::LoadFrom($ClientAssembly)
    $arena=Join-Path ([IO.Path]::GetDirectoryName($ClientAssembly)) 'ArenaOverhaul.1.4.6.dll'
    if(Test-Path $arena){[Reflection.Assembly]::LoadFrom($arena)|Out-Null}
    $types=[Collections.Generic.List[Type]]::new()
    foreach($a in [AppDomain]::CurrentDomain.GetAssemblies()){
        try{$loaded=$a.GetTypes()}catch [Reflection.ReflectionTypeLoadException]{$loaded=$_.Exception.Types}
        foreach($t in $loaded){if($null -ne $t){$types.Add($t)}}
    }
    $byFull=[Collections.Generic.Dictionary[string,Type]]::new([StringComparer]::Ordinal)
    $byName=[Collections.Generic.Dictionary[string,Collections.Generic.List[Type]]]::new([StringComparer]::Ordinal)
    foreach($t in $types){
        if($t.FullName){$byFull[$t.FullName]=$t;$byFull[$t.FullName.Replace('+','.') ]=$t}
        if(!$byName.ContainsKey($t.Name)){$byName[$t.Name]=[Collections.Generic.List[Type]]::new()}
        $byName[$t.Name].Add($t)
    }
    function Resolve-TypeName([string]$name,[string]$source){
        $name=$name.Trim()
        $primitive=@{bool=[bool];int=[int];float=[single];double=[double];string=[string];object=[object];void=[void]}
        if($primitive.ContainsKey($name)){return $primitive[$name]}
        if($name -match '^(.+)\[\]$'){$t=Resolve-TypeName $Matches[1] $source;if($t){return $t.MakeArrayType()};return $null}
        if($name -match '^([^<]+)<(.+)>$'){
            $outer=$Matches[1];$inner=$Matches[2];$t=Resolve-TypeName ($outer+'`1') $source;$arg=Resolve-TypeName $inner $source
            if($t -and $arg){return $t.MakeGenericType([Type[]]@($arg))};return $null
        }
        $alias=[regex]::Match($source,'using\s+'+[regex]::Escape($name)+'\s*=\s*([^;]+);')
        if($alias.Success){$name=$alias.Groups[1].Value.Trim()}
        if($byFull.ContainsKey($name)){return $byFull[$name]}
        if($byName.ContainsKey($name) -and $byName[$name].Count -eq 1){return $byName[$name][0]}
        foreach($ns in [regex]::Matches($source,'using\s+([\w.]+)\s*;')){
            $full=$ns.Groups[1].Value+'.'+$name
            if($byFull.ContainsKey($full)){return $byFull[$full]}
        }
        return $null
    }
    # Static native lookup audit. Dynamic receivers/target factories are reported
    # separately; existing feature probes remain the behavioral proof for them.
    $pattern='(?<kind>AccessTools\.(?:Method|DeclaredMethod|PropertyGetter|PropertySetter)|HarmonyPatch|Add|Bind|Patch|Required)\s*\(\s*(?:(?:_weddings|harmony)\s*,\s*)?(?:typeof\((?<type>[^)]+)\)|"(?<qualified>[^"\r\n]+:[^"\r\n]+)")\s*(?:,\s*(?:"(?<name>[^"\r\n]+)"|nameof\((?<named>[^)]+)\)))?'
    foreach($file in Get-ChildItem (Join-Path $SourceRoot 'ReignBeta/src') -Filter '*.cs' -Recurse){
        if($file.Name -like 'ReignRbm*'){continue} # RBM is explicitly outside this task.
        $source=[IO.File]::ReadAllText($file.FullName)
        foreach($m in [regex]::Matches($source,$pattern)){
            $kind=$m.Groups['kind'].Value;$name=$m.Groups['name'].Value
            if($m.Groups['qualified'].Success -and $kind -notlike 'AccessTools.*'){continue}
            if($m.Groups['qualified'].Success){$parts=$m.Groups['qualified'].Value.Split(':');$typeName=$parts[0];$name=$parts[1]}else{$typeName=$m.Groups['type'].Value}
            if($m.Groups['named'].Success){$name=($m.Groups['named'].Value.Split('.'))[-1]}
            if(!$name){continue}
            $type=Resolve-TypeName $typeName $source
            $line=1+([regex]::Matches($source.Substring(0,$m.Index),"\n")).Count
            if(!$type){$unresolved.Add(@{path=$file.FullName;line=$line;type=$typeName;member=$name;reason='type_not_resolved'});continue}
            if($type.Assembly.GetName().Name -notmatch '^(TaleWorlds\.|SandBox|StoryMode|NavalDLC)'){continue}
            $tail=$source.Substring($m.Index+$m.Length)
            if($kind -eq 'HarmonyPatch' -and $tail -match '^\s*,\s*MethodType\.(Getter|Setter)'){$kind='AccessTools.Property'+$Matches[1]}
            $array=[regex]::Match($tail,'^\s*,\s*(?:new\s*(?:Type)?\s*\[\s*\]\s*\{(?<body>[^}]+)\}|\[(?<body>[^\]]+)\])')
            $parameters=$null;$parameterError=$false
            if($array.Success){
                $parameters=[Collections.Generic.List[Type]]::new()
                foreach($p in [regex]::Matches($array.Groups['body'].Value,'typeof\((?<type>[^)]+)\)(?<byref>\.MakeByRefType\(\))?')){
                    $pt=Resolve-TypeName $p.Groups['type'].Value $source
                    if(!$pt){$parameterError=$true;break};if($p.Groups['byref'].Success){$pt=$pt.MakeByRefType()};$parameters.Add($pt)
                }
            }
            if($parameterError){$unresolved.Add(@{path=$file.FullName;line=$line;type=$typeName;member=$name;reason='parameter_type_not_resolved'});continue}
            try {
                $target=if($kind -like '*PropertyGetter'){[HarmonyLib.AccessTools]::PropertyGetter($type,$name)}elseif($kind -like '*PropertySetter'){[HarmonyLib.AccessTools]::PropertySetter($type,$name)}elseif($kind -eq 'AccessTools.DeclaredMethod'){[HarmonyLib.AccessTools]::DeclaredMethod($type,$name,$(if($parameters){[Type[]]$parameters.ToArray()}else{$null}))}elseif($null -ne $parameters){[HarmonyLib.AccessTools]::Method($type,$name,[Type[]]$parameters.ToArray())}else{[HarmonyLib.AccessTools]::Method($type,$name)}
                $rows.Add(@{path=$file.FullName;line=$line;type=$type.FullName;member=$name;exactParameters=$array.Success;found=($null -ne $target);resolved=if($target){$target.ToString()}else{''}})
            }catch{$rows.Add(@{path=$file.FullName;line=$line;type=$type.FullName;member=$name;found=$false;error=$_.Exception.Message})}
        }
    }
    # Execute the compiled private-native quote bridge, rather than merely
    # resolving a method name. Intercept only the campaign-dependent body.
    Add-Type -ReferencedAssemblies @(
        ([Reflection.Assembly]::Load('netstandard, Version=2.0.0.0, Culture=neutral, PublicKeyToken=cc7b13ffcd2ddd51').Location),
        (Join-Path $BannerlordPath 'bin/Win64_Shipping_Client/TaleWorlds.CampaignSystem.dll'),
        (Join-Path $BannerlordPath 'bin/Win64_Shipping_Client/TaleWorlds.Core.dll'),
        (Join-Path $BannerlordPath 'bin/Win64_Shipping_Client/TaleWorlds.Library.dll'),
        (Join-Path $BannerlordPath 'bin/Win64_Shipping_Client/TaleWorlds.Localization.dll')) -TypeDefinition @'
using TaleWorlds.CampaignSystem;
public static class NativeGarrisonQuoteFixture {
    public static object ExpectedInstance, ExpectedTown;
    public static float ExpectedResult;
    public static int Calls;
    public static bool Prefix(object __instance, object[] __args, ref ExplainedNumber __result) {
        if (!object.ReferenceEquals(__instance, ExpectedInstance) || __args.Length != 2
            || !object.ReferenceEquals(__args[0], ExpectedTown) || (bool)__args[1])
            throw new System.InvalidOperationException("Native garrison invocation arguments changed.");
        Calls++;
        __result = new ExplainedNumber(ExpectedResult, false, null);
        return false;
    }
}
'@
    $garrisonType=[TaleWorlds.CampaignSystem.CampaignBehaviors.GarrisonRecruitmentCampaignBehavior]
    $townType=[TaleWorlds.CampaignSystem.Settlements.Town]
    $behavior=[Runtime.Serialization.FormatterServices]::GetUninitializedObject($garrisonType)
    $town=[Runtime.Serialization.FormatterServices]::GetUninitializedObject($townType)
    [NativeGarrisonQuoteFixture]::ExpectedInstance=$behavior
    [NativeGarrisonQuoteFixture]::ExpectedTown=$town
    $garrisonHarmony=[HarmonyLib.Harmony]::new($garrisonOwner)
    $bridge=$client.GetType('ReignBeta.WorldSimulation.Economy.ReignEconomyPatches',$true).GetMethod('NativeGarrisonChange',$flags)
    if(!$bridge){throw 'Compiled native garrison quote bridge missing'}
    foreach($automatic in @($true,$false)){
        $name=if($automatic){'GetAutoRecruitmentGarrisonChangeExplainedNumber'}else{'GetBaseGarrisonChangeExplainedNumber'}
        $target=[HarmonyLib.AccessTools]::DeclaredMethod($garrisonType,$name,[Type[]]@($townType,[bool]))
        if(!$target -or $target.ReturnType -ne [TaleWorlds.CampaignSystem.ExplainedNumber]){throw 'Native garrison signature changed'}
        if($automatic){
            try{$target.Invoke($behavior,[object[]]@($town))|Out-Null;throw 'Legacy garrison arity unexpectedly accepted'}
            catch [Reflection.TargetParameterCountException]{$legacyGarrisonArityRejected=$true}
        }
        $prefix=[HarmonyLib.HarmonyMethod]::new([NativeGarrisonQuoteFixture].GetMethod('Prefix'))
        $garrisonHarmony.Patch($target,$prefix,$null,$null,$null)|Out-Null
        foreach($value in @([single]-2,[single]0,[single]3.5)){
            [NativeGarrisonQuoteFixture]::ExpectedResult=$value
            $result=$bridge.Invoke($null,[object[]]@($behavior,$town,$automatic))
            if($result.ResultNumber -ne $value){throw 'Native garrison quote result changed'}
            $garrisonChecks++
        }
    }
    if([NativeGarrisonQuoteFixture]::Calls -ne 6 -or !$legacyGarrisonArityRejected){throw 'Native garrison invocation proof incomplete'}
    $garrisonHarmony.UnpatchAll($garrisonOwner)
    $resident=$client.GetType('ReignBeta.Characters.Campaign.ReignEncounteredResidentPatches',$true)
    $bindings=@($resident.GetMethod('ResolveNativeTargets',$flags).Invoke($null,@()))
    if($bindings.Count -ne 9 -or @($bindings|Where-Object {$null -eq $_}).Count){throw 'Resident target set is incomplete'}
    if($bindings[3].GetParameters().Count -ne 4){throw 'Resident SpawnAgent must bind the actual four-parameter native method'}
    $resident.GetMethod('Install',$flags).Invoke($null,@()) | Out-Null
    $resident.GetMethod('Install',$flags).Invoke($null,@()) | Out-Null
    foreach($target in $bindings){
        $info=[HarmonyLib.Harmony]::GetPatchInfo($target)
        $owned=@(@($info.Prefixes)+@($info.Postfixes)|Where-Object {$_.owner -eq $owner})
        $expected=if($target -eq $bindings[3]){2}else{1}
        if($owned.Count -ne $expected){throw ('Resident hook missing/duplicated: '+$target)}
    }
    if(@($rows|Where-Object {!$_.found}).Count){throw 'Static native hook audit found missing targets; inspect report.json'}
    $ok=$true
}catch{$failure=$_.Exception.ToString()+[Environment]::NewLine+$_.ScriptStackTrace}
finally {
    [HarmonyLib.Harmony]::new($garrisonOwner).UnpatchAll($garrisonOwner)
    [HarmonyLib.Harmony]::new($owner).UnpatchAll($owner)
    [AppDomain]::CurrentDomain.remove_AssemblyResolve($resolver)
    [ordered]@{schema='reign-native-hook-audit-v1';ok=$ok;failure=$failure;residentBindings=$bindings.Count;garrisonInvocationChecks=$garrisonChecks;legacyGarrisonArityRejected=$legacyGarrisonArityRejected;staticLookups=$rows.ToArray();unresolved=$unresolved.ToArray();providerCalls=0;campaignMutations=0;rbm='excluded; unchanged';isolation='process-local lookup/registration and garrison quote invocation with engine body intercepted; no game/campaign/native time/save/provider/listener'} | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $EvidenceDirectory 'report.json') -Encoding UTF8
}
if(!$ok){throw $failure}
