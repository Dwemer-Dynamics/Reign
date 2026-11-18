param([Parameter(Mandatory=$true)][string]$ClientAssembly,[Parameter(Mandatory=$true)][string]$BannerlordPath,[Parameter(Mandatory=$true)][string]$EvidenceDirectory)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$probeRoot=[IO.Path]::GetFullPath($EvidenceDirectory)
$working=Join-Path $probeRoot 'sandbox\bin\Win64_Shipping_Client'
[IO.Directory]::CreateDirectory($working) | Out-Null
Set-Location -LiteralPath $working
[Environment]::CurrentDirectory=$working
$gameBin=Join-Path $BannerlordPath 'bin\Win64_Shipping_Client'
$script:meetingRoots=@($gameBin,[IO.Path]::GetDirectoryName($ClientAssembly),(Join-Path $BannerlordPath 'Modules\Bannerlord.Harmony\bin\Win64_Shipping_Client'),(Join-Path $BannerlordPath 'Modules\SandBox\bin\Win64_Shipping_Client'),(Join-Path $BannerlordPath 'Modules\NavalDLC\bin\Win64_Shipping_Client'))
$resolver=[ResolveEventHandler]{param($sender,$eventArgs)
    $name=([Reflection.AssemblyName]::new($eventArgs.Name)).Name+'.dll'
    foreach($root in $script:meetingRoots){$path=Join-Path $root $name;if([IO.File]::Exists($path)){return [Reflection.Assembly]::LoadFrom($path)}}
    return $null
}
[AppDomain]::CurrentDomain.add_AssemblyResolve($resolver)
$ok=$false;$failure='';$hooks=0;$beforeRemoval=$false;$preserved=$false;$rejected=$false;$modelChecks=0;$leaseChecks=0;$patches=$null;$rulerWaits=$false
$rescueBound=$false;$rescueNoOp=$false;$rescueRemoved=$false;$rescueTarget=$null;$rescuePrefix=$null;$announcementHoldChecks=0;$meetingFeatureChecks=0
$shipDlcAvailable=$false;$shipNativeContextVerified=$false
try {
    $client=[Reflection.Assembly]::LoadFrom([IO.Path]::GetFullPath($ClientAssembly))
    $native=[Reflection.Assembly]::LoadFrom((Join-Path $gameBin 'TaleWorlds.CampaignSystem.dll'))
    $static=[Reflection.BindingFlags]'Static,Public,NonPublic'
    $instance=[Reflection.BindingFlags]'Instance,Public,NonPublic'
    $patches=$client.GetType('ReignBeta.Campaign.ReignNpcLivesPatches',$true)
    $patches.GetMethod('Apply',$static).Invoke($null,@()) | Out-Null
    $patches.GetMethod('Apply',$static).Invoke($null,@()) | Out-Null
    $snapshot=$patches.GetMethod('Snapshot',$static).Invoke($null,@()).ToString() | ConvertFrom-Json
    $hooks=[int]$snapshot.installedHookCount
    if(-not $snapshot.ready -or $hooks -ne 13){throw ('NPC Lives hook inventory failed: '+$snapshot.lastError)}
    $rescueTarget=$native.GetType('TaleWorlds.CampaignSystem.Encounters.PlayerEncounter',$true).GetMethod('DoFreeOrCapturePrisonerHeroes',$instance)
    $rescuePrefix=$client.GetType('ReignBeta.Campaign.ReignNpcStoryRescuePrisonerPatch',$true).GetMethod('Prefix',$static)
    if($null -eq $rescueTarget -or $null -eq $rescuePrefix -or $rescueTarget.GetParameters().Count -ne 0 -or $rescueTarget.ReturnType -ne [void]){throw 'Native rescue prisoner boundary is unavailable or changed.'}
    $ownedRescue=@([HarmonyLib.Harmony]::GetPatchInfo($rescueTarget).Prefixes | Where-Object {$_.owner -eq 'com.bannerlordreign.reignbeta.npc_lives' -and $_.PatchMethod -eq $rescuePrefix})
    $rescueBound=$ownedRescue.Count -eq 1
    if(-not $rescueBound){throw 'Rescue prefix was not explicitly installed exactly once.'}
    [Runtime.CompilerServices.RuntimeHelpers]::PrepareMethod($rescuePrefix.MethodHandle)
    $rescuePrefix.Invoke($null,[object[]]@($null)) | Out-Null
    $rescueNoOp=$true
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
    # Exercise the compiled control's complete ownership cross-product and its
    # monotonic deadline, including nonfinite and negative elapsed values.
    $announcementGuard=$client.GetType('ReignBeta.Campaign.ReignNpcLivesTestControls',$true).GetMethod('AnnouncementTransportScopeAllows',$static)
    if($null -eq $announcementGuard){throw 'Compiled announcement transport scope guard is unavailable.'}
    $elapsedCases=@(0d,179.999d,180d,181d,-1d,[double]::NaN,[double]::PositiveInfinity,[double]::NegativeInfinity)
    for($mask=0;$mask -lt 256;$mask++){
        for($elapsedIndex=0;$elapsedIndex -lt $elapsedCases.Count;$elapsedIndex++){
            $arguments=[object[]]::new(9)
            for($bit=0;$bit -lt 8;$bit++){$arguments[$bit]=[bool]($mask -band (1 -shl $bit))}
            $arguments[8]=[double]$elapsedCases[$elapsedIndex]
            $actual=[bool]$announcementGuard.Invoke($null,$arguments)
            if($actual -ne ($mask -eq 255 -and $elapsedIndex -lt 2)){throw ('Announcement hold scope/deadline mismatch: '+$mask+'/'+$elapsedIndex)}
            $announcementHoldChecks++
        }
    }
    $meetingFeatureGuard=$livesType.GetMethod('MeetingFeatureAllows',$static)
    if($null -eq $meetingFeatureGuard){throw 'Compiled meeting feature guard is unavailable.'}
    $agreementType=$meetingFeatureGuard.GetParameters()[1].ParameterType
    foreach($enabled in @($false,$true)){
        if([bool]$meetingFeatureGuard.Invoke($null,[object[]]@($enabled,$null)) -ne $enabled){throw 'Null meeting bypassed the feature gate.'};$meetingFeatureChecks++
        for($signatureCase=0;$signatureCase -lt 8;$signatureCase++){
            $agreement=[Activator]::CreateInstance($agreementType)
            $agreement.Id='compiled_meeting_feature_probe';$agreement.PlayerId='player';$agreement.NpcId='issuer'
            $hash=$agreement.TermsHash()
            if($signatureCase -in @(1,3,5,6,7)){$agreement.PlayerAcceptedHash=$hash}
            if($signatureCase -in @(2,3,4,6,7)){$agreement.NpcAcceptedHash=$hash}
            if($signatureCase -eq 4){$agreement.PlayerAcceptedHash='wrong'}
            if($signatureCase -eq 5){$agreement.NpcAcceptedHash='wrong'}
            if($signatureCase -eq 6){$agreement.Revision++}
            if($signatureCase -eq 7){$agreement.ConversationId='foreign_context'}
            $actual=[bool]$meetingFeatureGuard.Invoke($null,[object[]]@($enabled,$agreement))
            if($actual -ne ($enabled -or $signatureCase -eq 3)){throw ('Meeting feature/signature mismatch: '+$enabled+'/'+$signatureCase)}
            $meetingFeatureChecks++
        }
    }
    # War Sails 1.3.7 fixes the away-governor crash natively. Inspect the actual
    # callback without executing a ship trade, perk or gold action.
    if($null -ne $client.GetType('ReignBeta.Campaign.ReignShipTradeGovernorPatch',$false)){throw 'Obsolete ship-trade patch is still shipped.'}
    $shipDlc=Join-Path $BannerlordPath 'Modules\NavalDLC\bin\Win64_Shipping_Client\NavalDLC.dll'
    $shipDlcAvailable=[IO.File]::Exists($shipDlc)
    if($shipDlcAvailable){
        $shipAssembly=[Reflection.Assembly]::LoadFrom($shipDlc)
        $shipTarget=$shipAssembly.GetType('NavalDLC.CampaignBehaviors.ShipTradeCampaignBehavior',$true).GetMethod('OnShipOwnerChanged',$instance)
        $heroType=$native.GetType('TaleWorlds.CampaignSystem.Hero',$true)
        $settlementType=$native.GetType('TaleWorlds.CampaignSystem.Settlements.Settlement',$true)
        $partyType=$native.GetType('TaleWorlds.CampaignSystem.Party.PartyBase',$true)
        $shipType=$native.GetType('TaleWorlds.CampaignSystem.Naval.Ship',$true)
        $currentSettlement=$heroType.GetProperty('CurrentSettlement').GetGetMethod()
        $partySettlement=$partyType.GetProperty('Settlement').GetGetMethod()
        $shipOwner=$shipType.GetProperty('Owner').GetGetMethod()
        $townField=$settlementType.GetField('Town')
        $shipInstructions=@($readInstructions.Invoke($null,[object[]]@($shipTarget,$null)))
        if(@($shipInstructions | Where-Object {$_.operand -eq $currentSettlement}).Count -ne 0){throw 'Native ship trade still reads a travelling governor location.'}
        $portSites=@()
        for($i=0;$i -lt $shipInstructions.Count;$i++){if($shipInstructions[$i].operand -eq $partySettlement){$portSites+=,$i}}
        if($portSites.Count -ne 2){throw 'Expected both former-owner and new-owner native trading-port branches.'}
        $first=$portSites[0];$second=$portSites[1]
        if($shipInstructions[$first-1].opcode -ne [Reflection.Emit.OpCodes]::Ldarg_2 -or
           $shipInstructions[$second-2].opcode -ne [Reflection.Emit.OpCodes]::Ldarg_1 -or $shipInstructions[$second-1].operand -ne $shipOwner){throw 'Native trade ports are not selected from the former owner and ship owner.'}
        foreach($site in $portSites){if($shipInstructions[$site+1].operand -ne $townField){throw 'Native trade port did not supply its town.'}}
        $perkCalls=@($shipInstructions | Where-Object {$_.operand -is [Reflection.MethodInfo] -and $_.operand.Name -eq 'AddPerkBonusForTown'})
        if($perkCalls.Count -ne 1 -or $perkCalls[0].operand.GetParameters().Count -ne 4){throw 'Native town perk dispatch changed.'}
        $shipNativeContextVerified=$true
    }
    $ok=$true
} catch {$failure=$_.Exception.ToString()+[Environment]::NewLine+$_.ScriptStackTrace}
finally {
    if($null -ne $patches){try{$patches.GetMethod('Unapply',[Reflection.BindingFlags]'Static,Public,NonPublic').Invoke($null,@()) | Out-Null}catch{}}
    if($null -ne $rescueTarget){
        $remaining=[HarmonyLib.Harmony]::GetPatchInfo($rescueTarget)
        $rescueRemoved=$null -eq $remaining -or @($remaining.Prefixes | Where-Object {$_.owner -eq 'com.bannerlordreign.reignbeta.npc_lives'}).Count -eq 0
        if(-not $rescueRemoved){$ok=$false;$failure+=' Rescue prefix survived module unapply.'}
    }
    [AppDomain]::CurrentDomain.remove_AssemblyResolve($resolver)
}
$report=[ordered]@{ok=$ok;error=$failure;installedHookCount=$hooks;rescuePrisonerHookBound=$rescueBound;rescuePrefixNoCampaignNoOp=$rescueNoOp;rescueHookRemoved=$rescueRemoved;replacementBeforeQueueRemoval=$beforeRemoval;preservedLabelsAndBlocks=$preserved;unsupportedShapesRejected=$rejected;customModelDispatchChecks=$modelChecks;returnLeaseChecks=$leaseChecks;rulerWaitsForReturnLease=$rulerWaits;announcementHoldGuardChecks=$announcementHoldChecks;meetingFeatureGateChecks=$meetingFeatureChecks;shipTradeDlcAvailable=$shipDlcAvailable;shipTradeNativeContextVerified=$shipNativeContextVerified;providerCalls=0;campaignMutations=0}
[IO.File]::WriteAllText((Join-Path $probeRoot 'report.json'),($report | ConvertTo-Json -Depth 8),[Text.UTF8Encoding]::new($false))
$report | ConvertTo-Json -Depth 8
if(-not $ok){exit 1}
