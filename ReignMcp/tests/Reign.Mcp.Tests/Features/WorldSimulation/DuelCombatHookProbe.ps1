param([Parameter(Mandatory=$true)][string]$ClientAssembly,[Parameter(Mandatory=$true)][string]$BannerlordPath,[Parameter(Mandatory=$true)][string]$EvidenceDirectory)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$working=Join-Path $EvidenceDirectory 'sandbox\bin\Win64_Shipping_Client'
[IO.Directory]::CreateDirectory($working) | Out-Null
Set-Location -LiteralPath $working
[Environment]::CurrentDirectory=$working
$gameBin=Join-Path $BannerlordPath 'bin\Win64_Shipping_Client'
$script:duelRoots=@($gameBin,[IO.Path]::GetDirectoryName($ClientAssembly),(Join-Path $BannerlordPath 'Modules\Bannerlord.Harmony\bin\Win64_Shipping_Client'),(Join-Path $BannerlordPath 'Modules\SandBox\bin\Win64_Shipping_Client'))
$resolver=[ResolveEventHandler]{param($sender,$eventArgs)
    $name=([Reflection.AssemblyName]::new($eventArgs.Name)).Name+'.dll'
    foreach($root in $script:duelRoots){$path=Join-Path $root $name;if([IO.File]::Exists($path)){return [Reflection.Assembly]::LoadFrom($path)}}
    return $null
}
[AppDomain]::CurrentDomain.add_AssemblyResolve($resolver)
$ok=$false;$failure='';$patches=$null;$bound=0;$scopeChecks=0;$preserved=$false;$collisionCopy=$false;$unpatched=$false
$static=[Reflection.BindingFlags]'Static,Public,NonPublic'
$instance=[Reflection.BindingFlags]'Instance,Public,NonPublic'
$owner='com.bannerlordreign.duel.training.v1'
try {
    $client=[Reflection.Assembly]::LoadFrom([IO.Path]::GetFullPath($ClientAssembly))
    $native=[Reflection.Assembly]::LoadFrom((Join-Path $gameBin 'TaleWorlds.MountAndBlade.dll'))
    $campaign=[Reflection.Assembly]::LoadFrom((Join-Path $gameBin 'TaleWorlds.CampaignSystem.dll'))
    $sandbox=[Reflection.Assembly]::LoadFrom((Join-Path $BannerlordPath 'Modules\SandBox\bin\Win64_Shipping_Client\SandBox.dll'))
    $patches=$client.GetType('ReignBeta.Campaign.ReignDuelCombatPatches',$true)
    $agent=$native.GetType('TaleWorlds.MountAndBlade.Agent',$true)
    $hero=$campaign.GetType('TaleWorlds.CampaignSystem.Hero',$true)
    $health=$agent.GetProperty('Health',$instance).GetSetMethod($true)
    $handle=$agent.GetMethod('HandleBlow',$instance)
    $targets=@($handle,$hero.GetMethod('AddSkillXp',$instance),$sandbox.GetType('SandBox.Missions.MissionLogics.BattleAgentLogic',$true).GetMethod('OnScoreHit',$instance),$native.GetType('TaleWorlds.MountAndBlade.Mission',$true).GetMethod('PrintAttackCollisionResults',$instance))
    $patches.GetMethod('Apply',$static).Invoke($null,@()) | Out-Null
    $patches.GetMethod('Apply',$static).Invoke($null,@()) | Out-Null
    foreach($target in $targets){
        $info=[HarmonyLib.Harmony]::GetPatchInfo($target)
        $owned=@(@($info.Prefixes)+@($info.Transpilers) | Where-Object {$_.owner -eq $owner})
        if($owned.Count -ne 1){throw 'Native hook missing or duplicated: '+$target.Name}
        $bound++
    }
    $read=[HarmonyLib.PatchProcessor].GetMethod('GetOriginalInstructions',$static,$null,[Type[]]@([Reflection.MethodBase],[Reflection.Emit.ILGenerator]),$null)
    $original=$read.Invoke($null,[object[]]@($handle,$null))
    $calls=@($original | Where-Object {$_.operand -eq $health})
    if($calls.Count -ne 1){throw 'Unexpected native health assignment count.'}
    $dynamic=[Reflection.Emit.DynamicMethod]::new('DuelProbeLabels',[void],[Type[]]@())
    $label=$dynamic.GetILGenerator().DefineLabel()
    $calls[0].labels.Add($label)
    $calls[0].blocks.Add([HarmonyLib.ExceptionBlock]::new([HarmonyLib.ExceptionBlockType]::BeginExceptionBlock))
    $transpiler=$patches.GetMethod('TranspileDamage',$static)
    $rewritten=@($transpiler.Invoke($null,[object[]](,$original)))
    $replacement=$patches.GetMethod('ApplyHitHealth',$static)
    $site=-1
    for($i=0;$i -lt $rewritten.Count;$i++){if($rewritten[$i].operand -eq $replacement){$site=$i}}
    if($site -lt 1 -or $rewritten.Count -ne $original.Count+1 -or $rewritten[$site-1].opcode -ne [Reflection.Emit.OpCodes]::Ldarg_1){throw 'Native health boundary was not replaced precisely.'}
    if(-not $rewritten[$site-1].labels.Contains($label) -or $rewritten[$site-1].blocks.Count -ne $calls[0].blocks.Count){throw 'Branch/exception boundaries lost.'}
    for($i=0;$i -lt $original.Count;$i++){
        if($i -eq $site-1){continue}
        $j=if($i -gt $site-1){$i+1}else{$i}
        if($rewritten[$j].opcode -ne $original[$i].opcode -or $rewritten[$j].operand -ne $original[$i].operand){throw 'Unrelated native instruction changed.'}
    }
    $preserved=$true
    $missing=[Collections.Generic.List[HarmonyLib.CodeInstruction]]::new()
    $duplicate=[Collections.Generic.List[HarmonyLib.CodeInstruction]]::new();$duplicate.Add($calls[0]);$duplicate.Add($calls[0])
    $shapes=[object[]]::new(2);$shapes[0]=$missing;$shapes[1]=$duplicate
    foreach($shape in $shapes){
        $rejected=$false
        try {@($transpiler.Invoke($null,[object[]](,$shape))) | Out-Null}
        catch {if($_.Exception.InnerException -is [InvalidOperationException]){$rejected=$true}else{throw}}
        if(-not $rejected){throw 'Ambiguous/missing native health assignment accepted.'}
    }
    $scale=$patches.GetMethod('ScaleSkillXp',$static)
    $scopeType=$client.GetType('ReignBeta.Campaign.ReignDuelCombatPatches+ExperienceScope',$true)
    $scopeCtor=$scopeType.GetConstructors($instance)[0]
    $fighter=[Runtime.Serialization.FormatterServices]::GetUninitializedObject($hero)
    $other=[Runtime.Serialization.FormatterServices]::GetUninitializedObject($hero)
    function CheckXp($recipient,[single]$raw,[single]$expected){
        $arguments=[object[]]@($recipient,$raw)
        $scale.Invoke($null,$arguments) | Out-Null
        if([Math]::Abs([single]$arguments[1]-$expected) -gt 0.0001){throw 'XP scope/rate mismatch.'}
        $script:scopeChecks++
    }
    CheckXp $fighter 100 100
    $outer=$scopeCtor.Invoke([object[]]@($fighter,$false))
    try {
        CheckXp $fighter 100 15
        CheckXp $other 100 100
        $inner=$scopeCtor.Invoke([object[]]@($fighter,$true))
        try {CheckXp $fighter 1 0.05} finally {$inner.Dispose()}
        CheckXp $fighter 100 15
    } finally {$outer.Dispose()}
    CheckXp $fighter 100 100
    $collisionType=$native.GetType('TaleWorlds.MountAndBlade.AttackCollisionData',$true)
    $collision=[Activator]::CreateInstance($collisionType)
    foreach($field in @('_attackBlockedWithShield','_missileBlockedWithWeapon','_collidedWithShieldOnBack')){$collisionType.GetField($field,$instance).SetValue($collision,$true)}
    $collisionType.GetField('BaseMagnitude',$instance).SetValue($collision,[single]77.5)
    $copy=$patches.GetMethod('ReferenceCollision',$static).Invoke($null,[object[]]@($collision))
    foreach($field in @('_attackBlockedWithShield','_missileBlockedWithWeapon','_collidedWithShieldOnBack')){
        if(-not $collisionType.GetField($field,$instance).GetValue($collision) -or $collisionType.GetField($field,$instance).GetValue($copy)){throw 'Blocked reference changed the original or retained blocking.'}
    }
    if($collisionType.GetField('BaseMagnitude',$instance).GetValue($copy) -ne [single]77.5){throw 'Reference lost attack physics.'}
    if(-not $collisionType.GetProperty('IsColliderAgent').GetValue($copy,$null) -or $collisionType.GetProperty('CollisionResult').GetValue($copy,$null).ToString() -ne 'StrikeAgent'){throw 'Reference is not an unblocked agent hit.'}
    $collisionCopy=$true
    if(-not $patches.GetMethod('NativeBattleHit',$static).Invoke($null,[object[]]@($null,$null))){throw 'Ordinary battle callback was suppressed without a duel.'}
    foreach($name in @('ApplyHitHealth','ScaleSkillXp','NativeBattleHit','BlockedReferenceDamage','CombatNotice')){[Runtime.CompilerServices.RuntimeHelpers]::PrepareMethod($patches.GetMethod($name,$static).MethodHandle)}
    $patches.GetMethod('Unapply',$static).Invoke($null,@()) | Out-Null
    foreach($target in $targets){
        $info=[HarmonyLib.Harmony]::GetPatchInfo($target)
        if($null -ne $info -and $info.Owners.Contains($owner)){throw 'Duel hook remained installed.'}
    }
    $unpatched=$true;$ok=$true
} catch {$failure=$_.Exception.ToString();if($_.Exception.InnerException){$failure+=' '+$_.Exception.InnerException.ToString()}}
finally {
    if($null -ne $patches){try{$patches.GetMethod('Unapply',$static).Invoke($null,@()) | Out-Null}catch{}}
    [AppDomain]::CurrentDomain.remove_AssemblyResolve($resolver)
    [ordered]@{schema='reign-duel-combat-hook-probe-v1';ok=$ok;error=$failure;boundHooks=$bound;scopeChecks=$scopeChecks;transpilerPreservedNativeInstructions=$preserved;collisionCopyOnly=$collisionCopy;unpatched=$unpatched;providerCalls=0;campaignMutations=0;nativeGameplayAcceptance=$false} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $EvidenceDirectory 'report.json') -Encoding UTF8
}
if(-not $ok){throw $failure}
