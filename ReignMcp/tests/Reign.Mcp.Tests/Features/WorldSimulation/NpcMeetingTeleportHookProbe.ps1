param([Parameter(Mandatory=$true)][string]$ClientAssembly,[Parameter(Mandatory=$true)][string]$BannerlordPath,[Parameter(Mandatory=$true)][string]$EvidenceDirectory)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$probeRoot=[IO.Path]::GetFullPath($EvidenceDirectory)
$working=Join-Path $probeRoot 'sandbox\bin\Win64_Shipping_Client'
[IO.Directory]::CreateDirectory($working) | Out-Null
Set-Location -LiteralPath $working
[Environment]::CurrentDirectory=$working
$gameBin=Join-Path $BannerlordPath 'bin\Win64_Shipping_Client'
$script:meetingRoots=@($gameBin,[IO.Path]::GetDirectoryName($ClientAssembly),(Join-Path $BannerlordPath 'Modules\Bannerlord.Harmony\bin\Win64_Shipping_Client'),(Join-Path $BannerlordPath 'Modules\SandBox\bin\Win64_Shipping_Client'))
$resolver=[ResolveEventHandler]{param($sender,$eventArgs)
    $name=([Reflection.AssemblyName]::new($eventArgs.Name)).Name+'.dll'
    foreach($root in $script:meetingRoots){$path=Join-Path $root $name;if([IO.File]::Exists($path)){return [Reflection.Assembly]::LoadFrom($path)}}
    return $null
}
[AppDomain]::CurrentDomain.add_AssemblyResolve($resolver)
$ok=$false;$failure='';$hooks=0;$beforeRemoval=$false;$preserved=$false;$rejected=$false;$modelChecks=0;$leaseChecks=0;$patches=$null;$rulerWaits=$false
try {
    $client=[Reflection.Assembly]::LoadFrom([IO.Path]::GetFullPath($ClientAssembly))
    $native=[Reflection.Assembly]::LoadFrom((Join-Path $gameBin 'TaleWorlds.CampaignSystem.dll'))
    $static=[Reflection.BindingFlags]'Static,Public,NonPublic'
    $instance=[Reflection.BindingFlags]'Instance,Public,NonPublic'
    $patches=$client.GetType('ReignBeta.Campaign.ReignNpcLivesPatches',$true)
    $patches.GetMethod('Apply',$static).Invoke($null,@()) | Out-Null
    $snapshot=$patches.GetMethod('Snapshot',$static).Invoke($null,@()).ToString() | ConvertFrom-Json
    $hooks=[int]$snapshot.installedHookCount
    if(-not $snapshot.ready -or $hooks -ne 12){throw ('Meeting hook inventory failed: '+$snapshot.lastError)}
    $guard=$client.GetType('ReignBeta.Campaign.NpcMeetingQueuedTeleportPatch',$true)
    $transpiler=$guard.GetMethod('Transpiler',$static)
    $replacement=$guard.GetMethod('CanPerformImmediateTeleport',$static)
    $hourly=$native.GetType('TaleWorlds.CampaignSystem.CampaignBehaviors.TeleportationCampaignBehavior',$true).GetMethod('HourlyTick',$instance)
    $readInstructions=[HarmonyLib.PatchProcessor].GetMethod('GetOriginalInstructions',$static,$null,
        [Type[]]@([Reflection.MethodBase],[Reflection.Emit.ILGenerator]),$null)
    if($null -eq $readInstructions){throw 'Unambiguous Harmony native instruction reader is unavailable.'}
    $original=$readInstructions.Invoke($null,[object[]]@($hourly,$null))
    $nativeCall=@($original | Where-Object {$_.operand -is [Reflection.MethodInfo] -and $_.operand.Name -eq 'CanPerformImmediateTeleport'})
    if($nativeCall.Count -ne 1){throw 'Licensed native eligibility call is ambiguous.'}
    $dynamic=[Reflection.Emit.DynamicMethod]::new('MeetingProbeLabels',[void],[Type[]]@())
    $label=$dynamic.GetILGenerator().DefineLabel()
    $nativeCall[0].labels.Add($label)
    $nativeCall[0].blocks.Add([HarmonyLib.ExceptionBlock]::new([HarmonyLib.ExceptionBlockType]::BeginExceptionBlock))
    $rewritten=@($transpiler.Invoke($null,[object[]](,$original)))
    $site=-1;$remove=-1;$apply=-1
    for($i=0;$i -lt $rewritten.Count;$i++){
        $op=$rewritten[$i].operand
        if($op -eq $replacement){$site=$i}
        if($op -is [Reflection.MethodInfo] -and $op.Name -eq 'RemoveTeleportationData'){$remove=$i}
        if($op -is [Reflection.MethodInfo] -and $op.Name -eq 'ApplyImmediateTeleport'){$apply=$i}
    }
    $beforeRemoval=$site -ge 0 -and $remove -gt $site -and $apply -gt $remove
    if(-not $beforeRemoval){throw 'Guard is not ahead of queue removal and application.'}
    if($rewritten.Count -ne $original.Count -or $rewritten[$site].opcode -ne [Reflection.Emit.OpCodes]::Call){throw 'Unexpected native instruction change.'}
    $preserved=$rewritten[$site].labels.Contains($label) -and $rewritten[$site].blocks.Count -eq $nativeCall[0].blocks.Count
    if($nativeCall[0].opcode -ne [Reflection.Emit.OpCodes]::Callvirt){throw 'Transpiler mutated its input.'}
    for($i=0;$i -lt $original.Count;$i++){
        if($i -ne $site -and ($rewritten[$i].opcode -ne $original[$i].opcode -or $rewritten[$i].operand -ne $original[$i].operand)){throw 'Unrelated native instruction was changed.'}
    }
    if(-not $preserved){throw 'Native labels or exception boundaries were lost.'}
    $instructionList=[Collections.Generic.List[HarmonyLib.CodeInstruction]]
    $empty=$instructionList::new();$duplicate=$instructionList::new()
    $duplicate.Add($nativeCall[0]);$duplicate.Add($nativeCall[0])
    $shapeFailures=0
    $shapes=[object[]]::new(2);$shapes[0]=$empty;$shapes[1]=$duplicate
    foreach($shape in $shapes){
        try{$transpiler.Invoke($null,[object[]](,$shape)) | Out-Null;throw 'Unsupported shape accepted.'}
        catch{if($_.Exception.InnerException -isnot [InvalidOperationException]){throw};$shapeFailures++}
    }
    $rejected=$shapeFailures -eq 2
    # A dynamic subclass verifies dispatch to the supplied model, rather than a
    # hardcoded Default model. No Campaign, native job or provider is started.
    $base=$replacement.GetParameters()[0].ParameterType
    $assembly=[AppDomain]::CurrentDomain.DefineDynamicAssembly([Reflection.AssemblyName]::new('MeetingTeleportModelProbe'),[Reflection.Emit.AssemblyBuilderAccess]::Run)
    $builder=$assembly.DefineDynamicModule('Probe').DefineType('CustomMeetingTeleportModel',[Reflection.TypeAttributes]'Public,Class',$base)
    $calls=$builder.DefineField('Calls',[int],[Reflection.FieldAttributes]::Public)
    $allowed=$builder.DefineField('Allowed',[bool],[Reflection.FieldAttributes]::Public)
    $builder.DefineDefaultConstructor([Reflection.MethodAttributes]::Public) | Out-Null
    foreach($method in $base.GetMethods($instance) | Where-Object {$_.IsAbstract}){
        $parameters=[Type[]]@($method.GetParameters() | ForEach-Object {$_.ParameterType})
        $override=$builder.DefineMethod($method.Name,[Reflection.MethodAttributes]'Public,Virtual,HideBySig',$method.ReturnType,$parameters)
        $il=$override.GetILGenerator()
        if($method.Name -eq 'CanPerformImmediateTeleport'){
            $il.Emit([Reflection.Emit.OpCodes]::Ldarg_0);$il.Emit([Reflection.Emit.OpCodes]::Dup)
            $il.Emit([Reflection.Emit.OpCodes]::Ldfld,$calls);$il.Emit([Reflection.Emit.OpCodes]::Ldc_I4_1)
            $il.Emit([Reflection.Emit.OpCodes]::Add);$il.Emit([Reflection.Emit.OpCodes]::Stfld,$calls)
            $il.Emit([Reflection.Emit.OpCodes]::Ldarg_0);$il.Emit([Reflection.Emit.OpCodes]::Ldfld,$allowed)
        } elseif($method.ReturnType -ne [void]) {
            if($method.ReturnType.IsValueType){$local=$il.DeclareLocal($method.ReturnType);$il.Emit([Reflection.Emit.OpCodes]::Ldloca,$local);$il.Emit([Reflection.Emit.OpCodes]::Initobj,$method.ReturnType);$il.Emit([Reflection.Emit.OpCodes]::Ldloc,$local)}
            else{$il.Emit([Reflection.Emit.OpCodes]::Ldnull)}
        }
        $il.Emit([Reflection.Emit.OpCodes]::Ret);$builder.DefineMethodOverride($override,$method)
    }
    $model=[Activator]::CreateInstance($builder.CreateType())
    foreach($expected in @($false,$true)){
        $model.Calls=0;$model.Allowed=$expected
        $actual=[bool]$replacement.Invoke($null,[object[]]@($model,$null,$null,$null))
        if($actual -ne $expected -or $model.Calls -ne 1){throw 'Native model rejection or single virtual dispatch changed.'}
        $modelChecks++
    }
    $livesType=$client.GetType('ReignBeta.Campaign.ReignNpcLivesBehavior',$true)
    $lives=[Activator]::CreateInstance($livesType)
    $owns=$livesType.GetMethod('HasOwnedMeetingTeleportDeferral',$instance)
    $rulerHourly=$client.GetType('ReignBeta.Campaign.ReignRulerCampaignBehavior',$true).GetMethod('Hourly',$instance)
    $rulerInstructions=@($readInstructions.Invoke($null,[object[]]@($rulerHourly,$null)))
    $ownedCall=-1;$capitalMove=-1
    for($i=0;$i -lt $rulerInstructions.Count;$i++){
        $op=$rulerInstructions[$i].operand
        if($op -eq $owns){$ownedCall=$i}
        if($capitalMove -lt 0 -and $op -is [Reflection.MethodInfo] -and $op.DeclaringType.Name -in @('LeaveSettlementAction','EnterSettlementAction','TeleportHeroAction')){$capitalMove=$i}
    }
    $rulerWaits=$ownedCall -ge 0 -and $capitalMove -gt $ownedCall
    if(-not $rulerWaits){throw 'Capital enforcement does not honor the attendance and return lease before native movement.'}
    $duties=$livesType.GetField('_lifeDuties',$instance).GetValue($lives)
    foreach($heroId in @('','unrelated')){if($owns.Invoke($lives,[object[]]@($heroId))){throw 'Unowned hero acquired a teleport hold.'};$leaseChecks++}
    $duty=[Activator]::CreateInstance($client.GetType('ReignBeta.Campaign.NpcLifeDuty',$true));$duty.HeroId='returning'
    $duties.Add('returning',$duty)
    if(-not $owns.Invoke($lives,[object[]]@('returning'))){throw 'Return cleanup lease did not defer native travel.'};$leaseChecks++
    $duties.Remove('returning') | Out-Null
    if($owns.Invoke($lives,[object[]]@('returning'))){throw 'Native travel remained blocked after return lease retirement.'};$leaseChecks++
    $ok=$true
} catch {$failure=$_.Exception.ToString()}
finally {
    if($null -ne $patches){try{$patches.GetMethod('Unapply',[Reflection.BindingFlags]'Static,Public,NonPublic').Invoke($null,@()) | Out-Null}catch{}}
    [AppDomain]::CurrentDomain.remove_AssemblyResolve($resolver)
}
$report=[ordered]@{ok=$ok;error=$failure;installedHookCount=$hooks;replacementBeforeQueueRemoval=$beforeRemoval;preservedLabelsAndBlocks=$preserved;unsupportedShapesRejected=$rejected;customModelDispatchChecks=$modelChecks;returnLeaseChecks=$leaseChecks;rulerWaitsForReturnLease=$rulerWaits;providerCalls=0;campaignMutations=0}
[IO.File]::WriteAllText((Join-Path $probeRoot 'report.json'),($report | ConvertTo-Json -Depth 8),[Text.UTF8Encoding]::new($false))
$report | ConvertTo-Json -Depth 8
if(-not $ok){exit 1}
