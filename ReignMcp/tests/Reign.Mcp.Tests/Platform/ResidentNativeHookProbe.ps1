param([string]$ClientAssembly,[string]$BannerlordPath,[string]$EvidenceDirectory)
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

$ok=$false;$failure='';$bindings=@()
try {
    [Reflection.Assembly]::LoadFrom((Join-Path $BannerlordPath 'Modules/Bannerlord.Harmony/bin/Win64_Shipping_Client/0Harmony.dll')) | Out-Null
    foreach($root in $roots){if(Test-Path $root){foreach($file in Get-ChildItem $root -Filter '*.dll'){
        if($file.Name -match '^(TaleWorlds\.|SandBox|StoryMode|NavalDLC)'){try{[Reflection.Assembly]::LoadFrom($file.FullName)|Out-Null}catch{}}
    }}}
    $client=[Reflection.Assembly]::LoadFrom($ClientAssembly)
    $arena=Join-Path ([IO.Path]::GetDirectoryName($ClientAssembly)) 'ArenaOverhaul.1.4.6.dll'
    if(Test-Path $arena){[Reflection.Assembly]::LoadFrom($arena)|Out-Null}
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
    $ok=$true
}catch{$failure=$_.Exception.ToString()+[Environment]::NewLine+$_.ScriptStackTrace}
finally {
    if ('HarmonyLib.Harmony' -as [type]) { [HarmonyLib.Harmony]::new($owner).UnpatchAll($owner) }
    [AppDomain]::CurrentDomain.remove_AssemblyResolve($resolver)
    [ordered]@{schema='reign-resident-native-hooks-v1';ok=$ok;failure=$failure;clientSha256=(Get-FileHash -LiteralPath $ClientAssembly -Algorithm SHA256).Hash;residentBindings=$bindings.Count;idempotent=$ok;targets=@($bindings | ForEach-Object { $_.ToString() });providerCalls=0;campaignMutations=0;isolation='fresh process; production registration twice on licensed assemblies; no game/campaign/native time/save/provider/listener'} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $EvidenceDirectory 'report.json') -Encoding UTF8
}
if(!$ok){throw $failure}
