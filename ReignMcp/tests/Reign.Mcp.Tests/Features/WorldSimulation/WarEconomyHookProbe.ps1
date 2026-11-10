param([Parameter(Mandatory=$true)][string]$ClientAssembly,[Parameter(Mandatory=$true)][string]$BannerlordPath,[Parameter(Mandatory=$true)][string]$EvidenceDirectory)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$working=Join-Path $EvidenceDirectory 'sandbox\bin\Win64_Shipping_Client'
[IO.Directory]::CreateDirectory($working) | Out-Null
Set-Location -LiteralPath $working
[Environment]::CurrentDirectory=$working
$gameBin=Join-Path $BannerlordPath 'bin\Win64_Shipping_Client'
$script:economyRoots=@($gameBin,[IO.Path]::GetDirectoryName($ClientAssembly),(Join-Path $BannerlordPath 'Modules\Bannerlord.Harmony\bin\Win64_Shipping_Client'),(Join-Path $BannerlordPath 'Modules\SandBox\bin\Win64_Shipping_Client'))
$resolver=[ResolveEventHandler]{param($sender,$eventArgs)
    $name=([Reflection.AssemblyName]::new($eventArgs.Name)).Name+'.dll'
    foreach($root in $script:economyRoots){$path=Join-Path $root $name;if([IO.File]::Exists($path)){return [Reflection.Assembly]::LoadFrom($path)}}
    return $null
}
[AppDomain]::CurrentDomain.add_AssemblyResolve($resolver)
$garrisonStackRounding=$false;$pressureParsingBounded=$false;$upgradePayerBinding=$false;$purchaseLoopNegativeControls=$false;$purchaseStockGate=$false;$nativeFoodCensusBounded=$false;$reliefPolicyProbe=$false;$nativeSubsistenceForecast=$false
$fundingWalletCapacityBoundary=$false;$fundingWalletLimit=0;$fundingHistoryLimit=0;$populationObserverBinding=$false;$fundingOriginBinding=$false;$civilianFishingBinding=$false;$leaderlessWageStoreBinding=$false
$ok=$false;$failure='';$patches=$null;$bound=0;$preserved=$false;$unpatched=$false;$persistenceFields=0;$persistedAidObserved=$false;$navalRoute='unavailable';$prisonerBinding=$false;$financeSites=0;$aiSpawnLocality=$false;$wagePayerBinding=$false;$componentHashesObserved=$false;$tariffBinding=$false
$static=[Reflection.BindingFlags]'Static,Public,NonPublic'
$instance=[Reflection.BindingFlags]'Instance,Public,NonPublic'
$owner='com.bannerlordreign.reignbeta.economy'
$targets=@()
function Test-ExpectedWarProceedsRewrite($original,$replacement,$opcode,$calls) {
    if($original -isnot [Reflection.MethodInfo] -or $replacement -isnot [Reflection.MethodInfo] -or
        -not $calls.ContainsKey($original) -or $opcode -ne [Reflection.Emit.OpCodes]::Call){return $false}
    return [object]::Equals($replacement,$patches.GetMethod($calls[$original],$static))
}
function Get-WarProceedsBoundary($instruction) {
    return [pscustomobject]@{Labels=@($instruction.labels);Blocks=@($instruction.blocks | ForEach-Object {[pscustomobject]@{Kind=$_.blockType;Catch=$_.catchType}})}
}
function Test-WarProceedsBoundary($instruction,$snapshot) {
    if($instruction.labels.Count -ne $snapshot.Labels.Count -or $instruction.blocks.Count -ne $snapshot.Blocks.Count){return $false}
    for($j=0;$j -lt $snapshot.Labels.Count;$j++){if(-not [object]::Equals($instruction.labels[$j],$snapshot.Labels[$j])){return $false}}
    for($j=0;$j -lt $snapshot.Blocks.Count;$j++){
        if($instruction.blocks[$j].blockType -ne $snapshot.Blocks[$j].Kind -or -not [object]::Equals($instruction.blocks[$j].catchType,$snapshot.Blocks[$j].Catch)){return $false}
    }
    return $true
}
$medicalBinding=$false;$medicalRosterConservation=$false
$nativeRewriteNegativeControls=$false
$navalFundingBinding=$false;$navalFundingHooks=0;$navalAcquisitionIdentity=$false
$governmentHearthFlat=$false;$governmentHearthFlatCases=0;$cohortBinding=$false;$militaryServiceBinding=$false
try {
    $client=[Reflection.Assembly]::LoadFrom([IO.Path]::GetFullPath($ClientAssembly))
    $campaign=[Reflection.Assembly]::LoadFrom((Join-Path $gameBin 'TaleWorlds.CampaignSystem.dll'))
    $cohort=$client.GetType('ReignBeta.Economy.ReignWarEconomyCohortControl',$true)
    $contracts=[Reflection.Assembly]::LoadFrom((Join-Path ([IO.Path]::GetDirectoryName($ClientAssembly)) 'Reign.Core.Contracts.dll'))
    $cohortPlan=$contracts.GetType('Reign.Core.Contracts.Economy.WarEconomyCohortPlan',$true)
    $planField=$cohort.GetField('_plan',$static)
    $previousPlan=$planField.GetValue($null)
    try {
        foreach($suppressRebellions in @($true,$false)) {
            $plan=$cohortPlan.GetConstructors()[0].Invoke([object[]]@([double]126,[double]6,[double]1,$true,$suppressRebellions,'auto','auto'))
            $planField.SetValue($null,$plan)
            try {
                $cohort.GetMethod('Bind',$static).Invoke($null,@()) | Out-Null
                $cohortTargets=$cohort.GetField('Targets',$static).GetValue($null)
                $expectedCohortHooks=if($suppressRebellions){7}else{4}
                if($cohortTargets.Count -ne $expectedCohortHooks){throw 'Configurable cohort hook coverage is incomplete.'}
                foreach($target in $cohortTargets.Values){
                    if(-not (@([HarmonyLib.Harmony]::GetPatchInfo($target).Prefixes | Where-Object {$_.owner -eq 'com.bannerlordreign.reignbeta.economy.cohortfixture'}).Count -eq 1)){
                        throw 'Cohort hook did not bind exactly once.'
                    }
                }
                foreach($name in @('WarPrefix','PeacePrefix')) {
                    if(-not $cohort.GetMethod($name,$static).Invoke($null,[object[]]@($null,$null,$null))){throw 'Unarmed cohort isolation changed native relations.'}
                }
                if(-not $cohort.GetMethod('RebellionPrefix',$static).Invoke($null,[object[]](,$null))){throw 'Unarmed cohort isolation changed native rebellion behavior.'}
            } finally {$cohort.GetMethod('Release',$static).Invoke($null,[object[]]@('offline_probe',$false)) | Out-Null}
            foreach($target in $cohortTargets.Values){
                $info=[HarmonyLib.Harmony]::GetPatchInfo($target)
                if($null -ne $info -and @($info.Prefixes | Where-Object {$_.owner -eq 'com.bannerlordreign.reignbeta.economy.cohortfixture'}).Count){throw 'Cohort hook leaked after release.'}
            }
        }
    } finally {$planField.SetValue($null,$previousPlan)}
    $cohortBinding=$true
    $patches=$client.GetType('ReignBeta.Economy.ReignEconomyPatches',$true)
    $patches.GetMethod('Apply',$static).Invoke($null,@()) | Out-Null
    $patches.GetMethod('Apply',$static).Invoke($null,@()) | Out-Null
    $registered=$patches.GetField('HookTargets',$static).GetValue($null)
    if($registered.Count -ne 56){throw 'Expected fifty-six exact economy hooks; setup failed.'}
    $reignWageTarget=$client.GetType('ReignBeta.ClanAccords.ReignClanAccordsWageModel',$true).GetMethod('GetTotalWage',$instance)
    if($registered['KingdomWagePostfix'] -ne $reignWageTarget){throw 'Kingdom wage adjustment missed the direct Reign wage model.'}
    $views=[Reflection.Assembly]::LoadFrom((Join-Path $gameBin 'TaleWorlds.CampaignSystem.ViewModelCollection.dll'))
    $partyView=$views.GetType('TaleWorlds.CampaignSystem.ViewModelCollection.Party.PartyVM',$true)
    if($registered['FieldWageSelectionPostfix'] -ne $partyView.GetMethod('RefreshCurrentCharacterInformation',$instance)){throw 'Selected-unit wage quote hook is missing.'}
    $volunteerView=$views.GetType('TaleWorlds.CampaignSystem.ViewModelCollection.GameMenu.Recruitment.RecruitVolunteerTroopVM',$true)
    if($registered['RecruitFieldWagePostfix'].DeclaringType -ne $volunteerView -or $registered['RecruitFieldWagePostfix'].GetParameters().Count -ne 5){throw 'Volunteer field-wage constructor hook is missing.'}
    $tooltip=$views.GetType('TaleWorlds.CampaignSystem.ViewModelCollection.TooltipRefresherCollection',$true).GetMethod('RefreshCharacterTooltip',$static)
    if($registered['FieldWageTooltipTranspiler'] -ne $tooltip){throw 'Generic field-wage tooltip hook is missing.'}
    $tooltipOriginal=[HarmonyLib.PatchProcessor]::GetOriginalInstructions($tooltip)
    $tooltipRewritten=@($patches.GetMethod('FieldWageTooltipTranspiler',$static).Invoke($null,[object[]]@(,$tooltipOriginal)))
    if(@($tooltipRewritten | Where-Object {$_.operand -is [Reflection.MethodInfo] -and $_.operand.Name -eq 'FinalTooltipUnitWage'}).Count -ne 2 -or
       @($tooltipRewritten | Where-Object {$_.operand -is [Reflection.MethodInfo] -and $_.operand.Name -eq 'get_TroopWage'}).Count -ne 0){throw 'Generic troop tooltip still quotes legacy wages or changed unrelated labels.'}
    $patches.GetMethod('FieldWageSelectionPostfix',$static).Invoke($null,[object[]](,$null)) | Out-Null
    $patches.GetMethod('RecruitFieldWagePostfix',$static).Invoke($null,[object[]](,$null)) | Out-Null
    foreach($entry in $registered.GetEnumerator()){
        $target=$entry.Value
        $info=[HarmonyLib.Harmony]::GetPatchInfo($target)
        $owned=@(@($info.Prefixes)+@($info.Postfixes)+@($info.Transpilers)+@($info.Finalizers) | Where-Object {$_.owner -eq $owner -and $_.PatchMethod.Name -eq $entry.Key})
        if($owned.Count -ne 1){throw 'Native hook missing or duplicated: '+$entry.Key}
        $bound++;$targets+=,$target
    }
    $ransomTarget=$campaign.GetType('TaleWorlds.CampaignSystem.GameComponents.DefaultRansomValueCalculationModel',$true).GetMethod('PrisonerRansomValue',$instance)
    if($registered['RansomQuotePrefix'] -ne $ransomTarget -or $registered['RansomQuoteFinalizer'] -ne $ransomTarget){throw 'Ransom pricing scope bound to the wrong native model.'}
    $consumptionTarget=$campaign.GetType('TaleWorlds.CampaignSystem.CampaignBehaviors.ItemConsumptionBehavior',$true).GetMethod('MakeConsumption',$static)
    if($registered['CivilianConsumptionPrefix'] -ne $consumptionTarget -or $registered['CivilianConsumptionPostfix'] -ne $consumptionTarget){throw 'Civilian spending adapter bound to the wrong native consumption method.'}
    $ransomDepth=$patches.GetField('_ransomQuoteDepth',$static)
    $outer=[object[]]@(0)
    $patches.GetMethod('RansomQuotePrefix',$static).Invoke($null,$outer)|Out-Null
    $inner=[object[]]@(0)
    $patches.GetMethod('RansomQuotePrefix',$static).Invoke($null,$inner)|Out-Null
    if($ransomDepth.GetValue($null) -ne 2){throw 'Nested ransom quotation scope was lost.'}
    $expectedFailure=[InvalidOperationException]::new('expected quote failure')
    $returned=$patches.GetMethod('RansomQuoteFinalizer',$static).Invoke($null,[object[]]@($expectedFailure,$inner[0]))
    if($returned -ne $expectedFailure -or $ransomDepth.GetValue($null) -ne 1){throw 'Failed ransom quotation did not restore the outer scope.'}
    $patches.GetMethod('RansomQuoteFinalizer',$static).Invoke($null,[object[]]@($null,$outer[0]))|Out-Null
    if($ransomDepth.GetValue($null) -ne 0){throw 'Ransom quotation premium exemption leaked into ordinary recruitment.'}
    $diagnostics=$patches.GetMethod('BuildDiagnostics',$static).Invoke($null,@())
    if(-not [bool]$diagnostics['allHooksHealthy'].Value -or [int]$diagnostics['expectedHookCount'].Value -ne $registered.Count -or
       -not [bool]$diagnostics['kingdomWageModifierHook'].Value){throw 'Hook diagnostics are stale or incomplete.'}
    if([int]$diagnostics['nativeMarketSalesVersion'].Value -ne 1 -or -not [bool]$diagnostics['nativeHorseMarketSaleHook'].Value){throw 'Native horse and non-food market sale coverage is missing.'}
    $funding=$client.GetType('ReignBeta.Economy.ReignWarEconomyFundingAudit',$true)
    $fundingHooks=$funding.GetField('Hooks',$static).GetValue($null)
    if($fundingHooks.Count -ne 64){throw 'Expected sixty-four exact funding observation bindings.'}
    $nativeHearthSetter=[HarmonyLib.AccessTools]::PropertySetter([TaleWorlds.CampaignSystem.Settlements.Village], 'Hearth')
    if($null -eq $nativeHearthSetter -or $fundingHooks['HearthBefore'] -ne $nativeHearthSetter -or $fundingHooks['HearthAfter'] -ne $nativeHearthSetter){throw 'Population observer is not bound to the actual native village hearth setter.'}
    $fundingCoverage=$funding.GetMethod('Coverage',$static).Invoke($null,@())
    $fundingWalletLimit=[int]$fundingCoverage['walletLimit'].Value;$fundingHistoryLimit=[int]$fundingCoverage['historyRowLimit'].Value
    if($fundingWalletLimit -ne 32768 -or $fundingHistoryLimit -ne 8192){throw 'Whole-world wallet capacity must remain separate from bounded transaction history.'}
    $auditSessionType=$funding.GetNestedType('Session',[Reflection.BindingFlags]::NonPublic)
    $auditSession=[Activator]::CreateInstance($auditSessionType,$true)
    $addAuditWallet=$funding.GetMethod('AddWallet',$static)
    $auditRead=[Func[int]]{return 0}
    for($walletIndex=0;$walletIndex -lt $fundingWalletLimit;$walletIndex++){
        $addAuditWallet.Invoke($null,[object[]]@($auditSession,('isolated:'+ $walletIndex),$auditRead,[int]0)) | Out-Null
    }
    # Duplicate callbacks at capacity must retain the first opening value.
    $addAuditWallet.Invoke($null,[object[]]@($auditSession,'isolated:0',$auditRead,[int]999)) | Out-Null
    $auditWallets=$auditSessionType.GetField('Wallets',$instance).GetValue($auditSession)
    $auditOpening=$auditSessionType.GetField('Opening',$instance).GetValue($auditSession)
    $auditRows=$auditSessionType.GetField('Rows',$instance).GetValue($auditSession)
    $auditOwners=$auditSessionType.GetField('OpeningOwners',$instance).GetValue($auditSession)
    if($auditWallets.Count -ne 32768 -or $auditOpening['isolated:0'] -ne 0 -or $auditRows.Count -ne 0 -or $auditOwners.Count -ne 32768){throw 'Wallet capacity or duplicate registration changed history/opening evidence.'}
    if([bool]$auditOwners['isolated:0']['knownNode'].Value){throw 'An unmapped isolated wallet falsely claimed native ownership coverage.'}
    $walletOverflowRejected=$false
    try{$addAuditWallet.Invoke($null,[object[]]@($auditSession,'isolated:overflow',$auditRead,[int]0)) | Out-Null}
    catch{if($_.Exception.ToString() -like '*Funding wallet observation capacity exceeded*'){$walletOverflowRejected=$true}else{throw}}
    if(-not $walletOverflowRejected -or $auditWallets.Count -ne 32768 -or $auditSessionType.GetField('Dropped',$instance).GetValue($auditSession) -ne 1){throw 'The exact wallet limit did not reject overflow without extra registration.'}
    $failedReadSession=[Activator]::CreateInstance($auditSessionType,$true)
    $failedRead=[Func[int]]{throw 'expected isolated audit reader failure'}
    $readerRejected=$false
    try{$addAuditWallet.Invoke($null,[object[]]@($failedReadSession,'read-retry',$failedRead,$null)) | Out-Null}
    catch{if($_.Exception.ToString() -like '*expected isolated audit reader failure*'){$readerRejected=$true}else{throw}}
    foreach($fieldName in @('Wallets','Opening','Net','OpeningOwners')){
        if(-not $readerRejected -or $auditSessionType.GetField($fieldName,$instance).GetValue($failedReadSession).Count -ne 0){throw 'A failing opening reader left partial audit registration.'}
    }
    $addAuditWallet.Invoke($null,[object[]]@($failedReadSession,'read-retry',$auditRead,[int]0)) | Out-Null
    foreach($fieldName in @('Wallets','Opening','Net')){
        if($auditSessionType.GetField($fieldName,$instance).GetValue($failedReadSession).Count -ne 1){throw 'A retried opening reader could not register cleanly.'}
    }
    $fundingWalletCapacityBoundary=$true
    if([int]$fundingCoverage['populationObserverVersion'].Value -ne 1 -or [int]$fundingCoverage['fundingTraceVersion'].Value -ne 6 -or [int]$fundingCoverage['walletOwnershipVersion'].Value -ne 1){throw 'Population/ownership observer evidence version is stale.'}
    $populationObserverBinding=$true
    foreach($originBinding in @{
        'PartyGoldBefore'=[HarmonyLib.AccessTools]::PropertySetter([TaleWorlds.CampaignSystem.Party.MobileParty],'PartyTradeGold');
        'TownTaxBefore'=[HarmonyLib.AccessTools]::PropertySetter([TaleWorlds.CampaignSystem.Settlements.Town],'TradeTaxAccumulated');
        'VillageTaxBefore'=[HarmonyLib.AccessTools]::PropertySetter([TaleWorlds.CampaignSystem.Settlements.Village],'TradeTaxAccumulated');
        'WorkshopBefore'=[HarmonyLib.AccessTools]::PropertySetter([TaleWorlds.CampaignSystem.Settlements.Workshops.Workshop],'Capital');
        'OrdinaryPlunderBefore'=[HarmonyLib.AccessTools]::Method([TaleWorlds.CampaignSystem.MapEvents.MapEvent],'CalculatePlunderedAndLostGoldAmounts');
        'ShipPlunderBefore'=[HarmonyLib.AccessTools]::Method([TaleWorlds.CampaignSystem.MapEvents.MapEvent],'LootDefeatedPartyShips');
        'BattleCommitBefore'=[HarmonyLib.AccessTools]::Method([TaleWorlds.CampaignSystem.MapEvents.MapEventParty],'CommitGoldChanges')
    }.GetEnumerator()){
        if($null -eq $originBinding.Value -or $fundingHooks[$originBinding.Key] -ne $originBinding.Value){throw 'An exact money-origin boundary is missing.'}
    }
    if([int]$fundingCoverage['originObserverVersion'].Value -ne 2){throw 'Funding origin observation is stale.'}
    $fundingOriginBinding=$true

    foreach($entry in $fundingHooks.GetEnumerator()){
        $method=if($entry.Key.EndsWith(':before')){'ComponentBefore'}elseif($entry.Key.EndsWith(':after')){'ComponentAfter'}else{$entry.Key}
        $info=[HarmonyLib.Harmony]::GetPatchInfo($entry.Value)
        $owned=@(@($info.Prefixes)+@($info.Postfixes)+@($info.Finalizers) | Where-Object {$_.owner -eq $owner -and $_.PatchMethod -eq $funding.GetMethod($method,$static)})
        if($owned.Count -ne 1){throw 'Funding observation missing or duplicated: '+$entry.Key}
        $targets+=,$entry.Value
        [Runtime.CompilerServices.RuntimeHelpers]::PrepareMethod($funding.GetMethod($method,$static).MethodHandle)
    }
    $heroType=$campaign.GetType('TaleWorlds.CampaignSystem.Hero',$true)
    if($fundingHooks['HeroBefore'] -ne $heroType.GetProperty('Gold',$instance).GetSetMethod($true)){
        throw 'Funding audit is not observing the actual native hero wallet setter.'
    }
    $isolatedHero=[Runtime.Serialization.FormatterServices]::GetUninitializedObject($heroType)
    $goldProperty=$heroType.GetProperty('Gold',$instance)
    $goldProperty.SetValue($isolatedHero,103,$null)
    if($goldProperty.GetValue($isolatedHero,$null) -ne 103){throw 'Inactive funding observer changed a normal native wallet write.'}
    $goldProperty.SetValue($isolatedHero,-7,$null)
    if($goldProperty.GetValue($isolatedHero,$null) -ne 0){throw 'Inactive funding observer changed the native zero floor.'}
    $production=$campaign.GetType('TaleWorlds.CampaignSystem.GameComponents.DefaultVillageProductionCalculatorModel',$true).GetMethod('CalculateDailyProductionAmount',$instance)
    $read=[HarmonyLib.PatchProcessor].GetMethod('GetOriginalInstructions',$static,$null,[Type[]]@([Reflection.MethodBase],[Reflection.Emit.ILGenerator]),$null)
    # Follow the compiled government contribution, then exercise its actual native
    # ExplainedNumber adapter independently of any campaign or authored bonus state.
    $growthType=$client.GetType('ReignBeta.Economy.ReignSettlementProsperityModel',$true)
    $growthInstructions=@($read.Invoke($null,[object[]]@($growthType.GetMethod('CalculateHearthChange',$instance),$null)))
    function Get-GovernmentLocalIndex($instruction,[string]$operation) {
        $name=$instruction.opcode.Name
        if($name -match ('^'+$operation+'\.([0-3])$')){return [int]$Matches[1]}
        if($name -ne $operation -and $name -ne ($operation+'.s')){return -1}
        if($instruction.operand -is [Reflection.LocalVariableInfo]){return [int]$instruction.operand.LocalIndex}
        if($instruction.operand -is [byte] -or $instruction.operand -is [sbyte] -or $instruction.operand -is [int]){return [int]$instruction.operand}
        return -1
    }
    function Test-GovernmentHearthPath($instructions) {
        $body=@($instructions | Where-Object {$_.opcode -ne [Reflection.Emit.OpCodes]::Nop})
        $governmentCalls=@(for($gi=0;$gi -lt $body.Count;$gi++){
            $operand=$body[$gi].operand
            if($operand -is [Reflection.MethodInfo] -and $operand.DeclaringType.FullName -ceq 'ReignBeta.Government.ReignGovernmentCampaignBehavior' -and $operand.Name -ceq 'GetHearthDelta'){$gi}
        })
        $flatCalls=@(for($gi=0;$gi -lt $body.Count;$gi++){
            $operand=$body[$gi].operand
            if($operand -is [Reflection.MethodInfo] -and $operand.DeclaringType.FullName -ceq 'ReignBeta.ClanAccords.ReignClanAccordModelMath' -and $operand.Name -ceq 'AddFinalDelta'){$gi}
        })
        if($governmentCalls.Count -ne 1 -or $flatCalls.Count -ne 2){return $false}
        $governmentCall=$governmentCalls[0];$flatCall=$flatCalls[0]
        if($flatCall -ne $governmentCall+7 -or $flatCalls[1] -lt 5){return $false}
        # Native null coalescing legitimately branches to the government's store.
        # Neither completed adapter's argument sequence may be entered partway.
        foreach($callIndex in $flatCalls){
            for($ai=$callIndex-5;$ai -le $callIndex;$ai++){if($body[$ai].labels.Count -ne 0){return $false}}
        }
        if(@($body | Where-Object {$_.opcode -eq [Reflection.Emit.OpCodes]::Ret}).Count -ne 1){return $false}
        $stored=Get-GovernmentLocalIndex $body[$governmentCall+1] 'stloc'
        $loaded=Get-GovernmentLocalIndex $body[$flatCall-4] 'ldloc'
        $resultLocal=Get-GovernmentLocalIndex $body[$flatCall-5] 'ldloca'
        $returnedLocal=Get-GovernmentLocalIndex $body[$body.Count-2] 'ldloc'
        $accordLocal=Get-GovernmentLocalIndex $body[$flatCalls[1]-4] 'ldloc'
        return $stored -ge 0 -and $stored -eq $loaded -and $accordLocal -ge 0 -and $accordLocal -ne $stored -and
            $resultLocal -ge 0 -and $resultLocal -eq $returnedLocal -and $body[$body.Count-1].opcode -eq [Reflection.Emit.OpCodes]::Ret -and
            $body[$flatCall-3].opcode -eq [Reflection.Emit.OpCodes]::Ldstr -and $body[$flatCall-3].operand -ceq '{=!}Representative government authority' -and
            $body[$flatCall-2].opcode -eq [Reflection.Emit.OpCodes]::Ldnull -and $body[$flatCall-1].opcode -eq [Reflection.Emit.OpCodes]::Newobj -and
            $body[$flatCall-1].operand.DeclaringType.FullName -ceq 'TaleWorlds.Localization.TextObject' -and $body[$flatCall-1].operand.GetParameters().Count -eq 2
    }
    if(-not (Test-GovernmentHearthPath $growthInstructions)){throw 'Compiled government hearth path is scaled, missing, diverted or outside the reviewed native adapter shape.'}
    foreach($corruption in @('missing_call','wrong_value','wrong_local','wrong_label','double_government','unexpected_label')){
        $badGrowth=@($growthInstructions | ForEach-Object {[HarmonyLib.CodeInstruction]::new($_)} | Where-Object {$_.opcode -ne [Reflection.Emit.OpCodes]::Nop})
        $firstFlat=-1
        for($gi=0;$gi -lt $badGrowth.Count;$gi++){
            $operand=$badGrowth[$gi].operand
            if($operand -is [Reflection.MethodInfo] -and $operand.DeclaringType.FullName -ceq 'ReignBeta.ClanAccords.ReignClanAccordModelMath' -and $operand.Name -ceq 'AddFinalDelta'){$firstFlat=$gi;break}
        }
        if($firstFlat -lt 4){throw 'Missing reviewed government adapter for negative controls.'}
        if($corruption -eq 'missing_call'){$badGrowth[$firstFlat].opcode=[Reflection.Emit.OpCodes]::Nop}
        elseif($corruption -eq 'wrong_value'){$badGrowth[$firstFlat-4].opcode=[Reflection.Emit.OpCodes]::Ldc_R4;$badGrowth[$firstFlat-4].operand=[single]0}
        elseif($corruption -eq 'wrong_local'){
            $accordLoad=@($badGrowth | Where-Object {$_.operand -is [Reflection.MethodInfo] -and $_.operand.DeclaringType.FullName -ceq 'ReignBeta.ClanAccords.ReignClanAccordModelMath' -and $_.operand.Name -ceq 'AddFinalDelta'})
            $secondFlat=for($gi=$firstFlat+1;$gi -lt $badGrowth.Count;$gi++){if([object]::ReferenceEquals($badGrowth[$gi],$accordLoad[1])){$gi;break}}
            $badGrowth[$firstFlat-4]=[HarmonyLib.CodeInstruction]::new($badGrowth[$secondFlat-4])
        }
        elseif($corruption -eq 'unexpected_label'){$badGrowth[$firstFlat-4].labels.Add([Reflection.Emit.Label]::new())}
        elseif($corruption -eq 'double_government'){
            $secondFlat=-1
            for($gi=$firstFlat+1;$gi -lt $badGrowth.Count;$gi++){
                $operand=$badGrowth[$gi].operand
                if($operand -is [Reflection.MethodInfo] -and $operand.DeclaringType.FullName -ceq 'ReignBeta.ClanAccords.ReignClanAccordModelMath' -and $operand.Name -ceq 'AddFinalDelta'){$secondFlat=$gi;break}
            }
            if($secondFlat -lt 4){throw 'Missing accord adapter for the duplicate-government negative control.'}
            $badGrowth[$secondFlat-4]=[HarmonyLib.CodeInstruction]::new($badGrowth[$firstFlat-4])
        }
        else{$badGrowth[$firstFlat-3].operand='{=!}Clan Accords: agricultural exchange'}
        if(Test-GovernmentHearthPath $badGrowth){throw ('Invalid government hearth integration accepted: '+$corruption)}
    }
    $numberType=$campaign.GetType('TaleWorlds.CampaignSystem.ExplainedNumber',$true)
    $flatAdapter=$client.GetType('ReignBeta.ClanAccords.ReignClanAccordModelMath',$true).GetMethod('AddFinalDelta',$static)
    $numberCtor=$numberType.GetConstructor([Type[]]@([single],[bool],[TaleWorlds.Localization.TextObject]))
    $nativeFactor=$numberType.GetMethod('AddFactor',$instance)
    $nativeResult=$numberType.GetProperty('ResultNumber',$instance)
    $baseText=[TaleWorlds.Localization.TextObject]::new('{=!}Native growth contract')
    $bonusText=[TaleWorlds.Localization.TextObject]::new('{=!}Authored government contract')
    foreach($descriptions in @($false,$true)){
        foreach($factor in @([single]-1,[single]-0.2,[single]0,[single]0.4)){
            foreach($delta in @([single]-0.25,[single]0,[single]0.25)){
                foreach($limited in @($false,$true)){
                    $number=$numberCtor.Invoke([object[]]@([single]1.25,$descriptions,$baseText))
                    $nativeFactor.Invoke($number,[object[]]@($factor,$baseText))|Out-Null
                    if($limited){
                        $numberType.GetMethod('LimitMin',$instance).Invoke($number,[object[]]@([single]0.4))|Out-Null
                        $numberType.GetMethod('LimitMax',$instance).Invoke($number,[object[]]@([single]0.8,$baseText))|Out-Null
                    }
                    $opening=[double]$nativeResult.GetValue($number,$null)
                    $expectedOpening=1.25*(1+[double]$factor)
                    if($limited){$expectedOpening=[Math]::Max(0.4,[Math]::Min(0.8,$expectedOpening))}
                    if([Math]::Abs($opening-$expectedOpening) -gt 0.00001){throw 'Native factor/limit fixture did not mutate the same boxed number.'}
                    $flatArguments=[object[]]@($number,$delta,$bonusText)
                    $flatAdapter.Invoke($null,$flatArguments)|Out-Null
                    $expected=$expectedOpening+$delta
                    if($limited){$expected=[Math]::Max(0.4,[Math]::Min(0.8,$expected))}
                    if([Math]::Abs([double]$nativeResult.GetValue($flatArguments[0],$null)-$expected) -gt 0.00001){throw 'Authored hearth delta changed under a native factor, tooltip or limit.'}
                    $expectedMin=if($limited){[single]0.4}else{[single]::MinValue}
                    $expectedMax=if($limited){[single]0.8}else{[single]::MaxValue}
                    if($numberType.GetProperty('LimitMinValue',$instance).GetValue($flatArguments[0],$null) -ne $expectedMin -or
                        $numberType.GetProperty('LimitMaxValue',$instance).GetValue($flatArguments[0],$null) -ne $expectedMax){throw 'Final hearth contribution lost native limits.'}
                    $lines=@($numberType.GetMethod('GetLines',$instance).Invoke($flatArguments[0],@()))
                    if($descriptions){
                        $authored=@($lines | Where-Object {$_.Item1 -eq 'Authored government contract'})
                        if($delta -eq 0){if($authored.Count -ne 0){throw 'Zero government grant added an explanation.'}}
                        elseif($authored.Count -ne 1 -or [Math]::Abs([double]$authored[0].Item2-$delta) -gt 0.00001){throw 'Government explanation lost or rescaled the authored amount.'}
                        $lineTotal=0.0;foreach($line in $lines){$lineTotal+=[double]$line.Item2}
                        if([Math]::Abs($lineTotal-$expected) -gt 0.00001){throw 'Hearth explanation lines do not sum to the final value.'}
                    } elseif($lines.Count -ne 0){throw 'Description-free hearth quote emitted explanation lines.'}
                    $governmentHearthFlatCases++
                }
            }
        }
    }
    if($governmentHearthFlatCases -ne 48){throw 'Government hearth matrix is incomplete.'}
    $governmentHearthFlat=$true
    $upgradePayer=$patches.GetMethod('UpgradePayer',$static)
    $aliveOwner=[Runtime.Serialization.FormatterServices]::GetUninitializedObject($heroType)
    $aliveLeader=[Runtime.Serialization.FormatterServices]::GetUninitializedObject($heroType)
    $heroState=$heroType.GetField('_heroState',$instance)
    $heroState.SetValue($aliveOwner,[Enum]::Parse($heroState.FieldType,'Active'))
    $heroState.SetValue($aliveLeader,[Enum]::Parse($heroState.FieldType,'Active'))
    if(-not [object]::ReferenceEquals($upgradePayer.Invoke($null,[object[]]@($aliveOwner,$aliveLeader)),$aliveOwner)){throw 'Automatic upgrade did not use the native living owner payer.'}
    $heroState.SetValue($aliveOwner,[Enum]::Parse($heroState.FieldType,'Dead'))
    if(-not [object]::ReferenceEquals($upgradePayer.Invoke($null,[object[]]@($aliveOwner,$aliveLeader)),$aliveLeader) -or -not [object]::ReferenceEquals($upgradePayer.Invoke($null,[object[]]@($null,$aliveLeader)),$aliveLeader)){throw 'Automatic upgrade did not use the native living leader fallback.'}
    $heroState.SetValue($aliveLeader,[Enum]::Parse($heroState.FieldType,'Dead'))
    if($null -ne $upgradePayer.Invoke($null,[object[]]@($aliveOwner,$aliveLeader)) -or $null -ne $upgradePayer.Invoke($null,[object[]]@($null,$null))){throw 'Automatic upgrade invented an inactive payer.'}
    $nativeUpgradeEffects=$campaign.GetType('TaleWorlds.CampaignSystem.CampaignBehaviors.PartyUpgraderCampaignBehavior',$true).GetMethod('ApplyEffects',$instance)
    $nativeUpgradeBody=@($read.Invoke($null,[object[]]@($nativeUpgradeEffects,$null)))
    foreach($expected in @(@{Name='get_Owner';Count=3},@{Name='get_LeaderHero';Count=3},@{Name='get_IsAlive';Count=2},@{Name='ApplyBetweenCharacters';Count=2})){
        if(@($nativeUpgradeBody | Where-Object {$_.operand -is [Reflection.MethodInfo] -and $_.operand.Name -eq $expected.Name}).Count -ne $expected.Count){throw ('Native automatic upgrade payer shape changed: '+$expected.Name)}
    }
    $upgradePrefixBody=@($read.Invoke($null,[object[]]@($patches.GetMethod('AutomaticUpgradePrefix',$static),$null)))
    if(@($upgradePrefixBody | Where-Object {$_.operand -eq $upgradePayer}).Count -ne 1){throw 'Automatic upgrade funding gate did not use its verified native payer selection.'}
    $upgradePayerBinding=$true
    $tariffs=$client.GetType('ReignBeta.Economy.ReignWarEconomyTariffs',$true)
    $saleTarget=$campaign.GetType('TaleWorlds.CampaignSystem.Actions.SellItemsAction',$true).GetMethod('ApplyInternal',$static)
    $nativeSale=$read.Invoke($null,[object[]]@($saleTarget,$null))
    $saleOperands=@($nativeSale | ForEach-Object {,$_.operand});$saleCodes=@($nativeSale | ForEach-Object {$_.opcode})
    $saleLabels=@($nativeSale | ForEach-Object {$_.labels.Count});$saleBlocks=@($nativeSale | ForEach-Object {$_.blocks.Count})
    $saleRewrite=@($patches.GetMethod('TariffPurchaseTranspiler',$static).Invoke($null,[object[]](,$nativeSale)))
    if($saleRewrite.Count -ne $nativeSale.Count){throw 'Tariff integration changed native purchase control flow.'}
    $saleChanges=0
    for($i=0;$i -lt $nativeSale.Count;$i++){
        if($saleRewrite[$i].labels.Count -ne $saleLabels[$i] -or $saleRewrite[$i].blocks.Count -ne $saleBlocks[$i]){throw 'Tariff integration lost labels or exception blocks.'}
        if($saleRewrite[$i].opcode -eq $saleCodes[$i] -and [object]::Equals($saleRewrite[$i].operand,$saleOperands[$i])){continue}
        $expected=switch($saleOperands[$i].Name){'GetItemPrice'{'QuotePrice'} 'GetTownTaxRatio'{'TaxRatio'} 'RoundRandomized'{'RoundCommission'} 'AddToCounts'{'TransferQuotedUnit'} default{''}}
        if($expected -eq '' -or $saleRewrite[$i].operand.DeclaringType -ne $tariffs -or $saleRewrite[$i].operand.Name -ne $expected){throw 'Tariff integration changed unrelated price, item, cash or village-tax instructions.'}
        $saleChanges++
    }
    if($saleChanges -ne 5 -or $registered['TariffPurchaseTranspiler'] -ne $saleTarget -or
       $registered['TariffPurchasePrefix'] -ne $saleTarget -or $registered['TariffPurchaseFinalizer'] -ne $saleTarget){throw 'Native tariff purchase binding is incomplete.'}
    foreach($corruption in @('price_consumer','missing_stock','payment_in_loop','address_quote','interior_label')){
        $badSale=$read.Invoke($null,[object[]]@($saleTarget,$null))
        $priceIndex=-1
        for($j=0;$j -lt $badSale.Count;$j++){if($badSale[$j].operand -is [Reflection.MethodInfo] -and $badSale[$j].operand.Name -eq 'GetItemPrice'){$priceIndex=$j;break}}
        if($priceIndex -lt 0){throw 'Missing native purchase price for negative control.'}
        if($corruption -eq 'price_consumer'){$badSale[$priceIndex+4].opcode=[Reflection.Emit.OpCodes]::Mul}
        elseif($corruption -eq 'missing_stock'){$badSale[$priceIndex+11].opcode=[Reflection.Emit.OpCodes]::Nop}
        elseif($corruption -eq 'payment_in_loop'){
            $payment=@($badSale | Where-Object {$_.operand -is [Reflection.MethodInfo] -and $_.operand.DeclaringType.FullName -eq 'TaleWorlds.CampaignSystem.Actions.GiveGoldAction'})[0].operand
            $badSale[$priceIndex+23].opcode=[Reflection.Emit.OpCodes]::Call;$badSale[$priceIndex+23].operand=$payment
        }
        elseif($corruption -eq 'address_quote'){$badSale.Add([HarmonyLib.CodeInstruction]::new([Reflection.Emit.OpCodes]::Ldloca_S,$badSale[$priceIndex+1].operand))}
        else{$badSale[$priceIndex+3].labels.Add($badSale[$priceIndex+22].labels[0])}
        $rejected=$false
        try{$patches.GetMethod('TariffPurchaseTranspiler',$static).Invoke($null,[object[]](,$badSale))|Out-Null}
        catch{if($_.Exception.InnerException -is [InvalidOperationException]){$rejected=$true}else{throw}}
        if(-not $rejected){throw ('Unsafe native purchase shape accepted: '+$corruption)}
    }
    $purchaseLoopNegativeControls=$true
    $townType=$campaign.GetType('TaleWorlds.CampaignSystem.Settlements.Town',$true)
    $poolSetter=$townType.GetProperty('TradeTaxAccumulated',$instance).GetSetMethod($true)
    if($registered['TariffPoolPrefix'] -ne $poolSetter -or $registered['TariffPoolPostfix'] -ne $poolSetter){throw 'Tariff pool observation is not bound to the native accumulator.'}
    $foodTarget=$campaign.GetType('TaleWorlds.CampaignSystem.CampaignBehaviors.PartiesBuyFoodCampaignBehavior',$true).GetMethod('BuyFoodInternal',$instance)
    $horseTarget=$campaign.GetType('TaleWorlds.CampaignSystem.CampaignBehaviors.PartiesBuyHorseCampaignBehavior',$true).GetMethod('BuyHorses',$instance)
    if($registered['FoodProvisionPrefix'] -ne $foodTarget -or $registered['ProvisionFinalizer'] -ne $foodTarget -or
       $registered['HorseProvisionPrefix'] -ne $horseTarget -or $registered['HorseProvisionFinalizer'] -ne $horseTarget){throw 'Provision purpose is not bound to the exact native buying methods.'}
    foreach($target in @($foodTarget,$horseTarget)){
        $calls=@($read.Invoke($null,[object[]]@($target,$null)) | Where-Object {$_.operand -is [Reflection.MethodInfo] -and $_.operand.DeclaringType.FullName -eq 'TaleWorlds.CampaignSystem.Actions.SellItemsAction' -and $_.operand.Name -eq 'Apply'})
        if($calls.Count -ne 1){throw 'Native provisioning no longer enters the reviewed sale path.'}
    }
    $scopeField=$tariffs.GetField('_scope',$static)
    $foodScopeArgs=[object[]]@($null,$null,$null)
    $patches.GetMethod('FoodProvisionPrefix',$static).Invoke($null,$foodScopeArgs)|Out-Null
    $horseScopeArgs=[object[]]@($null,$null,$null)
    $patches.GetMethod('HorseProvisionPrefix',$static).Invoke($null,$horseScopeArgs)|Out-Null
    if(-not [object]::ReferenceEquals($scopeField.GetValue($null),$horseScopeArgs[2])){throw 'Nested provisioning scope missing.'}
    $provisionFailure=[InvalidOperationException]::new('expected provisioning failure')
    $returned=$patches.GetMethod('HorseProvisionFinalizer',$static).Invoke($null,[object[]]@($provisionFailure,$horseScopeArgs[2]))
    if(-not [object]::ReferenceEquals($returned,$provisionFailure) -or -not [object]::ReferenceEquals($scopeField.GetValue($null),$foodScopeArgs[2])){throw 'Failed nested provisioning leaked exemption context.'}
    $patches.GetMethod('ProvisionFinalizer',$static).Invoke($null,[object[]]@($null,$foodScopeArgs[2]))|Out-Null
    if($null -ne $scopeField.GetValue($null)){throw 'Provisioning scope survived completion.'}
    $saleField=$tariffs.GetField('_sale',$static)
    $element=[Activator]::CreateInstance([TaleWorlds.Core.ItemRosterElement])
    $emptySaleArgs=[object[]]@($null,$null,$element,1,$null)
    $patches.GetMethod('TariffPurchasePrefix',$static).Invoke($null,$emptySaleArgs)|Out-Null
    $patches.GetMethod('TariffPurchaseFinalizer',$static).Invoke($null,[object[]]@($provisionFailure,$emptySaleArgs[4]))|Out-Null
    if($null -ne $saleField.GetValue($null)){throw 'Failed native sale leaked another buyer context.'}
    $saleType=$tariffs.GetNestedType('Sale',[Reflection.BindingFlags]'Public,NonPublic')
    $blockedSale=[Activator]::CreateInstance($saleType,$true)
    $rosterType=$campaign.GetType('TaleWorlds.CampaignSystem.Roster.ItemRoster',$true)
    $sellerRoster=[Runtime.Serialization.FormatterServices]::GetUninitializedObject($rosterType)
    $buyerRoster=[Runtime.Serialization.FormatterServices]::GetUninitializedObject($rosterType)
    $saleType.GetField('GuardPurchases',$instance).SetValue($blockedSale,$true)
    $saleType.GetField('RejectUnit',$instance).SetValue($blockedSale,$true)
    $saleType.GetField('SellerRoster',$instance).SetValue($blockedSale,$sellerRoster)
    $saleType.GetField('BuyerRoster',$instance).SetValue($blockedSale,$buyerRoster)
    $saleField.SetValue($null,$blockedSale)
    try{
        $equipment=[Activator]::CreateInstance([TaleWorlds.Core.EquipmentElement])
        foreach($entry in @(@{Roster=$sellerRoster;Amount=-1},@{Roster=$buyerRoster;Amount=1})){
            $result=$tariffs.GetMethod('TransferQuotedUnit',$static).Invoke($null,[object[]]@($entry.Roster,$equipment,$entry.Amount))
            if($result -ne -1){throw 'A refused native unit reached a stock mutation.'}
        }
        $purchaseStockGate=$true
    }finally{
        $patches.GetMethod('TariffPurchaseFinalizer',$static).Invoke($null,[object[]]@($provisionFailure,$blockedSale))|Out-Null
    }
    if($null -ne $saleField.GetValue($null)){throw 'A refused purchase retained its transaction context.'}
    foreach($name in @('QuotePrice','TransferQuotedUnit','TaxRatio','RoundCommission','Before','After','PoolBefore','PoolAfter')){[Runtime.CompilerServices.RuntimeHelpers]::PrepareMethod($tariffs.GetMethod($name,$static).MethodHandle)}
    $priceObserver=$tariffs.GetMethod('QuotePrice',$static)
    $priceCalls=@($read.Invoke($null,[object[]]@($priceObserver,$null)) | Where-Object {$_.operand -is [Reflection.MethodInfo] -and $_.operand.Name -eq 'GetItemPrice'})
    if($priceCalls.Count -ne 1){throw 'Tariff price observation must call the actual native price exactly once.'}
    if([int]$diagnostics['tariffCollectionVersion'].Value -ne 3 -or [int]$diagnostics['foodMarketVersion'].Value -ne 3){throw 'Purchase or consumption evidence version is stale.'}
    $foodMarket=$client.GetType('ReignBeta.Economy.ReignWarEconomyFoodMarket',$true)
    $priorityPolicy=$client.GetType('ReignBeta.Economy.ReignWarEconomyPolicy',$true)
    $priorityMethod=$priorityPolicy.GetMethod('NeedsSubsistencePriority',$static)
    if($null -eq $priorityMethod){throw 'Compiled subsistence forecast method missing.'}
    if(-not $priorityMethod.Invoke($null,[object[]]@([double]87.7,[double]400,[double]-76,[double]44)) -or
        $priorityMethod.Invoke($null,[object[]]@([double]160,[double]400,[double]-76,[double]80))){throw 'Compiled subsistence forecast does not distinguish the original native plan.'}
    foreach($name in @('Before','Probe')){
        $method=$foodMarket.GetMethod($name,$static)
        if($null -eq $method){throw "Compiled food market method missing: $name"}
        [Runtime.CompilerServices.RuntimeHelpers]::PrepareMethod($method.MethodHandle)
    }
    $nativeSubsistenceForecast=$true
    $census=$foodMarket.GetMethod('DailyCensus',$static).Invoke($null,[object[]]@($true))
    if([int]$census['version'].Value -ne 2 -or [int]$census['subsistencePolicyVersion'].Value -ne 3 -or [int]$census['maximumSettlements'].Value -ne 256 -or
        [bool]$census['available'].Value -or [int]$census['dropped'].Value -ne 0 -or [int]$census['errors'].Value -ne 0){throw 'A fresh offline food census fabricated native evidence.'}
    $nativeFoodCensusBounded=$true
    $reliefPolicy=$client.GetType('ReignBeta.Economy.ReignWarEconomyPolicy',$true)
    $holdingType=$client.GetType('ReignBeta.Economy.ReignWarEconomyPolicy+Holding',$true)
    $reliefInputs=[Array]::CreateInstance($holdingType,5)
    for($i=0;$i -lt 5;$i++){
        $holding=[Activator]::CreateInstance($holdingType)
        $holdingType.GetField('Id').SetValue($holding,([string]$(if($i -eq 0){'town'}else{'donor'+$i})))
        $holdingType.GetField('Capacity').SetValue($holding,([double]$(if($i -eq 0){300}else{600})))
        $holdingType.GetField('Stock').SetValue($holding,([double]$(if($i -eq 0){15}else{550})))
        $holdingType.GetField('Change').SetValue($holding,([double]$(if($i -eq 0){-53}else{20})))
        $holdingType.GetField('Security').SetValue($holding,[double]100)
        $reliefInputs.SetValue($holding,$i)
    }
    $allocate=$reliefPolicy.GetMethod('AllocateRelief',$static)
    $scaledPlan=$allocate.Invoke($null,[object[]]@($reliefInputs,$false))
    $legacyPlan=$allocate.Invoke($null,[object[]]@($reliefInputs,$true))
    $scaledDelivered=0.0;$legacyDelivered=0.0
    foreach($shipment in $scaledPlan.GetType().GetField('Shipments').GetValue($scaledPlan)){$scaledDelivered += [double]$shipment.GetType().GetField('Delivered').GetValue($shipment)}
    foreach($shipment in $legacyPlan.GetType().GetField('Shipments').GetValue($legacyPlan)){$legacyDelivered += [double]$shipment.GetType().GetField('Delivered').GetValue($shipment)}
    if([int]$scaledPlan.GetType().GetField('Version').GetValue($scaledPlan) -ne 2 -or
        [Math]::Abs($scaledDelivered-73) -gt .00001 -or [Math]::Abs($legacyDelivered-15) -gt .00001 -or
        [double]$holdingType.GetField('Stock').GetValue($reliefInputs.GetValue(0)) -ne 15){throw 'Compiled relief policy did not conserve its inputs or distinguish the chronic-recipient cap.'}
    $reliefBehavior=$client.GetType('ReignBeta.Economy.ReignEconomyCampaignBehavior',$true)
    [Runtime.CompilerServices.RuntimeHelpers]::PrepareMethod($reliefBehavior.GetMethod('ReliefDiagnostics',[Reflection.BindingFlags]'Instance,Public,NonPublic').MethodHandle)
    $reliefPolicyProbe=$true
    $tariffBinding=$true
    $wagePostfix=$patches.GetMethod('NativeWagePaymentPostfix',$static)
    $wageCalls=@($read.Invoke($null,[object[]]@($wagePostfix,$null)) | Where-Object {$_.operand -is [Reflection.MethodInfo]} | ForEach-Object {$_.operand.Name})
    foreach($required in @('get_GarrisonPartyComponent','get_Settlement','get_OwnerClan','WagePayer','ObserveWages')){
        if($wageCalls -notcontains $required){throw 'Native wage observation lost current garrison ownership: '+$required}
    }
    $wageTarget=$campaign.GetType('TaleWorlds.CampaignSystem.GameComponents.DefaultClanFinanceModel',$true).GetMethod('CalculatePartyWage',$instance)
    if($registered['NativeWagePaymentPostfix'] -ne $wageTarget -or [int]$diagnostics['wageObservationVersion'].Value -ne 2){throw 'Wage observation2 is not bound to the actual native payment.'}
    $wagePayerBinding=$true
    $prisoner=$campaign.GetType('TaleWorlds.CampaignSystem.CampaignBehaviors.RecruitPrisonersCampaignBehavior',$true).GetMethod('RecruitPrisonersAi',$instance)
    $prisonerCalls=@($read.Invoke($null,[object[]]@($prisoner,$null)) | Where-Object {
        $_.operand -is [Reflection.MethodInfo] -and $_.operand.Name -eq 'OnTroopRecruited' -and
        $_.operand.DeclaringType.FullName -eq 'TaleWorlds.CampaignSystem.CampaignEventReceiver'
    })
    if($registered['AiPrisonerTransferPrefix'] -ne $prisoner -or $registered['AiPrisonerTransferFinalizer'] -ne $prisoner -or
       $prisonerCalls.Count -ne 1){throw 'The native AI prisoner transfer observation binding changed.'}
    $prisonerBinding=$true
    $clanTick=$campaign.GetType('TaleWorlds.CampaignSystem.CampaignBehaviors.ClanVariablesCampaignBehavior',$true).GetMethod('DailyTickClan',$instance)
    $nativeFinance=$read.Invoke($null,[object[]]@($clanTick,$null))
    $financeOperands=@($nativeFinance | ForEach-Object {,$_.operand})
    $financeCodes=@($nativeFinance | ForEach-Object {$_.opcode})
    $financeRewrite=@($patches.GetMethod('NativeDailyFinanceTranspiler',$static).Invoke($null,[object[]](,$nativeFinance)))
    if($financeRewrite.Count -ne $nativeFinance.Count -or $registered['NativeDailyFinanceTranspiler'] -ne $clanTick){
        throw 'Native finance observation bound to the wrong method.'
    }
    for($i=0;$i -lt $financeRewrite.Count;$i++){
        if($financeRewrite[$i].opcode -eq $financeCodes[$i] -and [object]::Equals($financeRewrite[$i].operand,$financeOperands[$i])){continue}
        $operand=$financeOperands[$i]
        if($operand -isnot [Reflection.MethodInfo] -or $financeRewrite[$i].operand.Name -notin @('ObserveNativeClanDailyPayout','ObserveNativeKingdomBudgetCredit') -or
           $operand.Name -notin @('ApplyBetweenCharacters','set_KingdomBudgetWallet')){throw 'Unrelated native finance instruction changed.'}
        $financeSites++
    }
    if($financeSites -ne 2){throw 'Expected one actual payout and one native budget-credit call.'}
    $financeModel=$campaign.GetType('TaleWorlds.CampaignSystem.GameComponents.DefaultClanFinanceModel',$true)
    $support=$financeModel.GetMethod('AddIncomeFromKingdomBudget',$instance)
    $service=$client.GetType('ReignBeta.Economy.ReignMilitaryServiceEligibility',$true)
    $commanderScore=$campaign.GetType('TaleWorlds.CampaignSystem.CampaignBehaviors.HeroSpawnCampaignBehavior',$true).GetMethod('GetHeroPartyCommandScore',$instance)
    if($registered['NpcCommanderEligibilityPrefix'] -ne $commanderScore){throw 'NPC fallback eligibility is not bound to the actual command score.'}
    $selector=$commanderScore.DeclaringType.GetMethod('GetBestAvailableCommander',$instance)
    $selectorBody=@($read.Invoke($null,[object[]]@($selector,$null)))
    if(@($selectorBody | Where-Object {$_.operand -eq $commanderScore}).Count -ne 2){throw 'Native commander selection no longer has two score-tested passes.'}
    $facts=$service.GetMethod('ServingPartyFacts',$static)
    # One valid active lord party, followed by each independent exclusion.
    $valid=[object[]]@($true,$true,$false,$true,$true,$true)
    if(-not $facts.Invoke($null,$valid)){throw 'An active military party cannot qualify for support.'}
    for($index=0;$index -lt 6;$index++){
        $invalid=$valid.Clone();$invalid[$index]=-not $invalid[$index]
        if($facts.Invoke($null,$invalid)){throw ('Invalid military service qualified at condition '+$index)}
    }
    $clanType=$campaign.GetType('TaleWorlds.CampaignSystem.Clan',$true)
    $isolatedClan=[Runtime.Serialization.FormatterServices]::GetUninitializedObject($clanType)
    $fiefs=$clanType.GetField('_fiefsCache',$instance)
    $fiefs.SetValue($isolatedClan,[Activator]::CreateInstance($fiefs.FieldType))
    if($registered.ContainsKey('LandlessReignTierTickPrefix') -or $registered.ContainsKey('LandlessReignCalculatedTierPostfix')){throw 'Rejected tier restrictions remain installed.'}
    $kingdomType=$campaign.GetType('TaleWorlds.CampaignSystem.Kingdom',$true)
    $isolatedKingdom=[Runtime.Serialization.FormatterServices]::GetUninitializedObject($kingdomType)
    $clanType.GetField('_kingdom',$instance).SetValue($isolatedClan,$isolatedKingdom)
    foreach($withdraw in @($false,$true)){
        $arguments=[object[]]@($isolatedClan,$withdraw,$null)
        if($patches.GetMethod('NativeKingdomSupportPrefix',$static).Invoke($null,$arguments) -or $null -ne $arguments[2]){throw 'An ineligible clan received quoted or withdrawn support.'}
    }
    # Execute the actual patched native transfer on isolated objects: quotes do
    # not spend, payments debit the kingdom once, native prices/caps stay intact.
    $townType=$campaign.GetType('TaleWorlds.CampaignSystem.Settlements.Town',$true)
    $fiefs.GetValue($isolatedClan).Add([Runtime.Serialization.FormatterServices]::GetUninitializedObject($townType))
    $warCache=$clanType.GetField('_warPartyComponentsCache',$instance)
    $warCache.SetValue($isolatedClan,[Activator]::CreateInstance($warCache.FieldType))
    if($service.GetMethod('CanReceiveKingdomSupport',$static).Invoke($null,[object[]]@($isolatedClan))){throw 'A landed clan without an active military party qualified.'}
    $serviceLeader=[Runtime.Serialization.FormatterServices]::GetUninitializedObject($heroType)
    $heroState.SetValue($serviceLeader,[Enum]::Parse($heroState.FieldType,'Active'))
    $heroType.GetField('_clan',$instance).SetValue($serviceLeader,$isolatedClan)
    $clanType.GetField('_leader',$instance).SetValue($isolatedClan,$serviceLeader)
    $mobileType=$campaign.GetType('TaleWorlds.CampaignSystem.Party.MobileParty',$true)
    $serviceParty=[Runtime.Serialization.FormatterServices]::GetUninitializedObject($mobileType)
    $mobileType.GetProperty('IsActive',$instance).SetValue($serviceParty,$true,$null)
    $mobileType.GetProperty('IsLordParty',$instance).SetValue($serviceParty,$true,$null)
    $mobileType.GetField('_actualClan',$instance).SetValue($serviceParty,$isolatedClan)
    $heroType.GetField('_partyBelongedTo',$instance).SetValue($serviceLeader,$serviceParty)
    $lordType=$campaign.GetType('TaleWorlds.CampaignSystem.Party.PartyComponents.LordPartyComponent',$true)
    $serviceComponent=[Runtime.Serialization.FormatterServices]::GetUninitializedObject($lordType)
    $lordType.GetField('_leader',$instance).SetValue($serviceComponent,$serviceLeader)
    $lordType.BaseType.BaseType.GetProperty('MobileParty',$instance).SetValue($serviceComponent,$serviceParty,$null)
    $mobileType.GetField('_partyComponent',$instance).SetValue($serviceParty,$serviceComponent)
    $warCache.GetValue($isolatedClan).Add($serviceComponent)
    if(-not $service.GetMethod('CanReceiveKingdomSupport',$static).Invoke($null,[object[]]@($isolatedClan))){throw 'A rebuilding active military party did not qualify.'}
    # The native finance class initializer reads Game.Current.GameTextManager.
    # Install only a process-local text fixture, then restore it even on failure.
    $gameType=[TaleWorlds.Core.Game]
    $currentGameField=$gameType.GetField('_current',$static)
    $priorGame=$currentGameField.GetValue($null)
    $textGame=[Runtime.Serialization.FormatterServices]::GetUninitializedObject($gameType)
    $gameType.GetProperty('GameTextManager',$instance).SetValue($textGame,[TaleWorlds.Core.GameTextManager]::new(),$null)
    $currentGameField.SetValue($null,$textGame)
    try {
        $supportInfo=[HarmonyLib.Harmony]::GetPatchInfo($support)
        if(@($supportInfo.Prefixes | Where-Object {$_.owner -eq $owner -and $_.PatchMethod -eq $patches.GetMethod('NativeKingdomSupportPrefix',$static)}).Count -ne 1){throw 'Native support eligibility prefix is missing or duplicated.'}
    $financeFixture=[Runtime.Serialization.FormatterServices]::GetUninitializedObject($financeModel)
    foreach($cash in @(0,4999,5000,9999,10000,19999,20000,25000)){
        $goldProperty.SetValue($serviceLeader,$cash,$null)
        $base=if($cash -lt 5000){2000}elseif($cash -lt 10000){1500}elseif($cash -lt 20000){1000}else{500}
        foreach($budget in @(0,500,1200,1000000,1000001)){
            foreach($withdraw in @($false,$true)){
                $kingdomType.GetProperty('KingdomBudgetWallet',$instance).SetValue($isolatedKingdom,$budget,$null)
                $expected=[Math]::Min($budget,($base * $(if($budget -gt 1000000){2}else{1})))
                $quote=[TaleWorlds.CampaignSystem.ExplainedNumber]::new([single]0,$false,$null)
                $arguments=[object[]]@($isolatedClan,$quote,$withdraw)
                $support.Invoke($financeFixture,$arguments) | Out-Null
                $remaining=if($withdraw){$budget-$expected}else{$budget}
                if($arguments[1].ResultNumber -ne $expected -or $isolatedKingdom.KingdomBudgetWallet -ne $remaining -or $serviceLeader.Gold -ne $cash){throw 'Native support amount, quote/payment parity, cap or wallet conservation changed.'}
            }
        }
    }
    # Native NPC rulers retain their authored support multiplier.
    $kingdomType.GetField('_rulingClan',$instance).SetValue($isolatedKingdom,$isolatedClan)
    $goldProperty.SetValue($serviceLeader,6000,$null)
    foreach($withdraw in @($false,$true)){
        $kingdomType.GetProperty('KingdomBudgetWallet',$instance).SetValue($isolatedKingdom,5000,$null)
        $arguments=[object[]]@($isolatedClan,[TaleWorlds.CampaignSystem.ExplainedNumber]::new([single]0,$false,$null),$withdraw)
        $support.Invoke($financeFixture,$arguments) | Out-Null
        $remaining=if($withdraw){2000}else{5000}
        if($arguments[1].ResultNumber -ne 3000 -or $isolatedKingdom.KingdomBudgetWallet -ne $remaining){throw 'Native NPC ruler support multiplier changed.'}
    }
    $kingdomType.GetField('_rulingClan',$instance).SetValue($isolatedKingdom,$null)
    $mobileType.GetProperty('IsLordParty',$instance).SetValue($serviceParty,$false,$null)
    if($service.GetMethod('CanReceiveKingdomSupport',$static).Invoke($null,[object[]]@($isolatedClan))){throw 'A civilian party qualified for kingdom support.'}
    foreach($withdraw in @($false,$true)){
        $kingdomType.GetProperty('KingdomBudgetWallet',$instance).SetValue($isolatedKingdom,10000,$null)
        $arguments=[object[]]@($isolatedClan,[TaleWorlds.CampaignSystem.ExplainedNumber]::new([single]0,$false,$null),$withdraw)
        $support.Invoke($financeFixture,$arguments) | Out-Null
        if($arguments[1].ResultNumber -ne 0 -or $isolatedKingdom.KingdomBudgetWallet -ne 10000){throw 'A civilian party caused a native support quote or treasury debit.'}
    }
    $mobileType.GetProperty('IsLordParty',$instance).SetValue($serviceParty,$true,$null)
    $fiefs.GetValue($isolatedClan).Clear()
    foreach($withdraw in @($false,$true)){
        $kingdomType.GetProperty('KingdomBudgetWallet',$instance).SetValue($isolatedKingdom,10000,$null)
        $arguments=[object[]]@($isolatedClan,[TaleWorlds.CampaignSystem.ExplainedNumber]::new([single]0,$false,$null),$withdraw)
        $support.Invoke($financeFixture,$arguments) | Out-Null
        if($arguments[1].ResultNumber -ne 0 -or $isolatedKingdom.KingdomBudgetWallet -ne 10000){throw 'A landless clan with a real military party received support.'}
    }

    } finally {$currentGameField.SetValue($null,$priorGame)}
    foreach($route in @(@('ReignBeta.World.ReignRegularActionExecutor','CreateParty'),@('ReignBeta.World.ReignCampaignCommandBehavior','ApplyNativeOrder'))){
        $routeType=$client.GetType($route[0],$true)
        $routeMethod=$routeType.GetMethod($route[1],[Reflection.BindingFlags]'Instance,Static,Public,NonPublic')
        $routeBody=@($read.Invoke($null,[object[]]@($routeMethod,$null)))
        if(@($routeBody | Where-Object {$_.operand -eq $service.GetMethod('CanEstablishNpcParty',$static)}).Count -ne 1){throw ('NPC direct creation route bypasses shared eligibility: '+$route[0])}
    }
    $militaryServiceBinding=$true
    $partyIncome=$financeModel.GetMethod('AddIncomeFromParty',$instance)
    if($registered['NativeKingdomSupportPrefix'] -ne $support -or $registered['NativeKingdomSupportPostfix'] -ne $support -or
       $registered['NativePartyIncomePostfix'] -ne $partyIncome -or
       $registered['NativeLordPurseTopUpPostfix'] -ne $financeModel.GetMethod('AddPartyExpense',$instance)){
        throw 'Actual support, party income, or lord purse transfers bound to the wrong method.'
    }
    $marketBalance=$campaign.GetType('TaleWorlds.CampaignSystem.CampaignBehaviors.ItemConsumptionBehavior',$true).GetMethod('UpdateTownGold',$instance)
    if($registered['NativeTownMarketBalancePrefix'] -ne $marketBalance -or $registered['NativeTownMarketBalancePostfix'] -ne $marketBalance){
        throw 'Town market wallet adjustment is not observed at its own native boundary.'
    }
    $ransom=$campaign.GetType('TaleWorlds.CampaignSystem.Actions.SellPrisonersAction',$true).GetMethod('ApplyInternal',$static)
    $lootSale=$campaign.GetType('TaleWorlds.CampaignSystem.CampaignBehaviors.PartiesSellLootCampaignBehavior',$true).GetMethod('OnSettlementEntered',$instance)
    $horseSale=$campaign.GetType('TaleWorlds.CampaignSystem.CampaignBehaviors.PartiesBuyHorseCampaignBehavior',$true).GetMethod('OnSettlementEntered',$instance)
    $giveGold=$campaign.GetType('TaleWorlds.CampaignSystem.Actions.GiveGoldAction',$true)
    $partyBase=$campaign.GetType('TaleWorlds.CampaignSystem.Party.PartyBase',$true)
    $settlementType=$campaign.GetType('TaleWorlds.CampaignSystem.Settlements.Settlement',$true)
    $heroPayout=$giveGold.GetMethod('ApplyBetweenCharacters',$static,$null,[Type[]]@($heroType,$heroType,[int],[bool]),$null)
    $settlementPayout=$giveGold.GetMethod('ApplyForPartyToSettlement',$static,$null,[Type[]]@($partyBase,$settlementType,[int],[bool]),$null)
    $sellApply=$campaign.GetType('TaleWorlds.CampaignSystem.Actions.SellItemsAction',$true).GetMethod('Apply',$static,$null,[Type[]]@($partyBase,$partyBase,[TaleWorlds.Core.ItemRosterElement],[int],$settlementType),$null)
    $boundaryControl=[HarmonyLib.CodeInstruction]::new([Reflection.Emit.OpCodes]::Nop)
    $boundaryLabel=([Reflection.Emit.DynamicMethod]::new('WarProceedsBoundary',[void],[Type[]]@())).GetILGenerator().DefineLabel()
    $boundaryControl.labels.Add($boundaryLabel)
    $boundaryControl.blocks.Add([HarmonyLib.ExceptionBlock]::new([HarmonyLib.ExceptionBlockType]::BeginExceptionBlock))
    $boundarySnapshot=Get-WarProceedsBoundary $boundaryControl
    if(-not (Test-WarProceedsBoundary $boundaryControl $boundarySnapshot)){throw 'Boundary oracle rejected preserved metadata.'}
    $boundaryControl.labels.Clear()
    if(Test-WarProceedsBoundary $boundaryControl $boundarySnapshot){throw 'Boundary oracle accepted a lost branch label.'}
    $boundaryControl.labels.Add($boundaryLabel);$boundaryControl.blocks.Clear()
    if(Test-WarProceedsBoundary $boundaryControl $boundarySnapshot){throw 'Boundary oracle accepted a lost exception block.'}
    foreach($case in @(
        @{hook='NativePrisonerPayoutTranspiler';target=$ransom;calls=@{$heroPayout='ObservePrisonerHeroPayout';$settlementPayout='ObservePrisonerSettlementPayout'}},
        @{hook='NativeLootSaleTranspiler';target=$lootSale;calls=@{$sellApply='ObserveLordLootSale'}},
        @{hook='NativeHorseMarketSaleTranspiler';target=$horseSale;calls=@{$sellApply='ObserveLordHorseMarketSale'};expectedChanges=2}
    )){
        $native=$read.Invoke($null,[object[]]@($case.target,$null))
        $operands=@($native | ForEach-Object {,$_.operand});$codes=@($native | ForEach-Object {$_.opcode})
        $boundaries=@($native | ForEach-Object {Get-WarProceedsBoundary $_})
        $rewrittenCalls=@($patches.GetMethod($case.hook,$static).Invoke($null,[object[]](,$native)))
        if($registered[$case.hook] -ne $case.target -or $rewrittenCalls.Count -ne $native.Count){throw 'Wrong native war proceeds target.'}
        $changes=0
        $known=@($operands | Where-Object {$_ -is [Reflection.MethodInfo] -and $case.calls.ContainsKey($_)})[0]
        $expected=$patches.GetMethod($case.calls[$known],$static)
        $wrongApply=[Reflection.Emit.DynamicMethod]::new($known.Name,[void],[Type[]]@())
        if(-not (Test-ExpectedWarProceedsRewrite $known $expected ([Reflection.Emit.OpCodes]::Call) $case.calls) -or
            (Test-ExpectedWarProceedsRewrite $known $null ([Reflection.Emit.OpCodes]::Nop) $case.calls) -or
            (Test-ExpectedWarProceedsRewrite ([object].GetMethod('ToString')) $null ([Reflection.Emit.OpCodes]::Nop) $case.calls) -or
            (Test-ExpectedWarProceedsRewrite $known $known ([Reflection.Emit.OpCodes]::Call) $case.calls) -or
            (Test-ExpectedWarProceedsRewrite $wrongApply $expected ([Reflection.Emit.OpCodes]::Call) $case.calls)){
            throw 'Native rewrite oracle accepted an unrelated/null/wrong-wrapper change.'
        }
        for($i=0;$i -lt $rewrittenCalls.Count;$i++){
            if(-not (Test-WarProceedsBoundary $rewrittenCalls[$i] $boundaries[$i])){throw 'Native war proceeds branch/exception metadata changed.'}
            if($rewrittenCalls[$i].opcode -eq $codes[$i] -and [object]::Equals($rewrittenCalls[$i].operand,$operands[$i])){continue}
            if(-not (Test-ExpectedWarProceedsRewrite $operands[$i] $rewrittenCalls[$i].operand $rewrittenCalls[$i].opcode $case.calls)){
                throw 'Unrelated native war proceeds instruction changed.'
            }
            $changes++
        }
        $expectedChanges=if($case.ContainsKey('expectedChanges')){$case.expectedChanges}else{$case.calls.Count}
        if($changes -ne $expectedChanges){throw 'Missing native war proceeds call.'}
    }
    $nativeRewriteNegativeControls=$true
    $lordSpawn=$campaign.GetType('TaleWorlds.CampaignSystem.CampaignBehaviors.HeroSpawnCampaignBehavior',$true).GetMethod('SpawnLordParty',$instance)
    $nativeSpawn=$read.Invoke($null,[object[]]@($lordSpawn,$null))
    $spawnOperands=@($nativeSpawn | ForEach-Object {,$_.operand})
    $spawnCodes=@($nativeSpawn | ForEach-Object {$_.opcode})
    $spawnRewrite=@($patches.GetMethod('NativeAiSpawnLocalityTranspiler',$static).Invoke($null,[object[]](,$nativeSpawn)))
    if($registered['NativeAiSpawnLocalityTranspiler'] -ne $lordSpawn -or $spawnRewrite.Count -ne ($nativeSpawn.Count + 1)){
        throw 'Native lord spawn locality did not bind to the exact call path.'
    }
    $extra=0;$wrapped=0
    for($i=0;$i -lt $nativeSpawn.Count;$i++){
        if($spawnOperands[$i] -is [Reflection.MethodInfo] -and $spawnOperands[$i].Name -eq 'get_GatePosition'){
            if($spawnRewrite[$i+$extra].opcode -ne [Reflection.Emit.OpCodes]::Dup){throw 'Native spawning settlement was not retained.'}
            $extra++
        }
        $after=$spawnRewrite[$i+$extra]
        if($after.opcode -eq $spawnCodes[$i] -and [object]::Equals($after.operand,$spawnOperands[$i])){continue}
        if($spawnOperands[$i] -isnot [Reflection.MethodInfo] -or $spawnOperands[$i].DeclaringType.FullName -ne 'Helpers.MobilePartyHelper' -or
           $spawnOperands[$i].Name -ne 'SpawnLordParty' -or $after.operand.Name -ne 'SpawnLordPartyWithLocality'){
            throw 'Unrelated lord spawn instruction changed.'
        }
        $wrapped++
    }
    if($extra -ne 1 -or $wrapped -ne 1){throw 'Expected one locality capture and one original spawn wrapper.'}
    $aiSpawnLocality=$true
    $original=$read.Invoke($null,[object[]]@($production,$null))
    $site=-1
    for($i=0;$i -lt $original.Count;$i++){if($original[$i].operand -is [Reflection.MethodInfo] -and $original[$i].operand.Name -eq 'GetHearthLevel'){$site=$i;break}}
    if($site -lt 0){throw 'Native hearth baseline missing.'}
    $opcodes=@($original | ForEach-Object {$_.opcode});$operands=@($original | ForEach-Object {,$_.operand})
    $dynamic=[Reflection.Emit.DynamicMethod]::new('EconomyProbeLabels',[void],[Type[]]@())
    $label=$dynamic.GetILGenerator().DefineLabel()
    $original[$site].labels.Add($label)
    $original[$site].blocks.Add([HarmonyLib.ExceptionBlock]::new([HarmonyLib.ExceptionBlockType]::BeginExceptionBlock))
    $transpiler=$patches.GetMethod('CommodityBaselineTranspiler',$static)
    $rewritten=@($transpiler.Invoke($null,[object[]](,$original)))
    if($rewritten.Count -ne $original.Count -or $rewritten[$site].operand.Name -ne 'CommodityBaseline'){throw 'Hearth baseline was not replaced precisely.'}
    if(-not $rewritten[$site].labels.Contains($label) -or $rewritten[$site].blocks.Count -ne 1){throw 'Branch/exception boundaries lost.'}
    for($i=0;$i -lt $original.Count;$i++){
        if($i -ge $site -and $i -le $site+5){continue}
        if($rewritten[$i].opcode -ne $opcodes[$i] -or $rewritten[$i].operand -ne $operands[$i]){throw 'Unrelated native production bonus changed.'}
    }
    $missing=[Collections.Generic.List[HarmonyLib.CodeInstruction]]::new();$rejected=$false
    try {@($transpiler.Invoke($null,[object[]](,$missing))) | Out-Null}
    catch {if($_.Exception.InnerException -is [InvalidOperationException]){$rejected=$true}else{throw}}
    if(-not $rejected){throw 'Missing native hearth baseline accepted.'}
    $leaderlessSetter=[HarmonyLib.AccessTools]::PropertySetter([TaleWorlds.CampaignSystem.Hero],'Gold')
    $leaderlessWrapper=$patches.GetMethod('ObserveLeaderlessWageStore',$static)
    $leaderlessRewrite=$patches.GetMethod('RewriteLeaderlessWageStore',$static)
    $leaderlessMethod=$campaign.GetType('TaleWorlds.CampaignSystem.GameComponents.DefaultClanFinanceModel',$true).GetMethod('AddPartyExpense',$instance)
    function Test-LeaderlessStoreRewrite($method){
        $original=$read.Invoke($null,[object[]]@($method,$null))
        $beforeCodes=@($original | ForEach-Object {$_.opcode});$beforeOperands=@($original | ForEach-Object {,$_.operand})
        $beforeBoundaries=@($original | ForEach-Object {Get-WarProceedsBoundary $_})
        $rewritten=@($leaderlessRewrite.Invoke($null,[object[]](,$original)))
        if($rewritten.Count -ne $beforeCodes.Count){throw 'Leaderless wage observer changed native instruction count.'}
        $changedStores=0
        for($li=0;$li -lt $rewritten.Count;$li++){
            if(-not (Test-WarProceedsBoundary $rewritten[$li] $beforeBoundaries[$li])){throw 'Leaderless wage observer changed branch/exception boundaries.'}
            if($beforeOperands[$li] -eq $leaderlessSetter){
                if($rewritten[$li].opcode -ne [Reflection.Emit.OpCodes]::Call -or $rewritten[$li].operand -ne $leaderlessWrapper){throw 'Leaderless wage observer did not retain the exact store boundary.'};$changedStores++
            }elseif($rewritten[$li].opcode -ne $beforeCodes[$li] -or -not [object]::Equals($rewritten[$li].operand,$beforeOperands[$li])){throw 'Leaderless wage observer changed unrelated native instructions.'}
        }
        if($changedStores -ne 1){throw 'Leaderless wage observer requires exactly one native hero store.'}
    }
    Test-LeaderlessStoreRewrite $leaderlessMethod
    if($registered['LeaderlessWageStoreTranspiler'] -ne $leaderlessMethod){throw 'Default leaderless wage source is uncovered.'}
    $deltaMethod=$funding.GetMethod('UnobservedCashDelta',$static)
    foreach($dc in @(@(788080,787408,0,0,-672),@(788080,787408,0,-672,0),@(100,80,0,-10,-10),@(300,0,0,0,-300),@(300,0,0,-300,0),@(100,100,0,0,0))){
        $delta=$deltaMethod.Invoke($null,[object[]]@([int]$dc[0],[int]$dc[1],[long]$dc[2],[long]$dc[3]))
        if($delta -ne [long]$dc[4]){throw 'Exact signed cash remainder double-counted or lost a native debit.'}
    }
    if(($leaderlessWrapper.GetMethodImplementationFlags() -band [Reflection.MethodImplAttributes]::NoInlining) -eq 0){throw 'Leaderless store wrapper may be inlined.'}
    $nullPayerRejected=$false
    try{$leaderlessWrapper.Invoke($null,[object[]]@($null,[int]0)) | Out-Null}
    catch{if($_.Exception.ToString() -like '*NullReferenceException*'){$nullPayerRejected=$true}else{throw}}
    if(-not $nullPayerRejected){throw 'Leaderless wage wrapper changed native null-payer behavior.'}
    foreach($storeCount in @(0,2)){
        $invalidStores=[Collections.Generic.List[HarmonyLib.CodeInstruction]]::new()
        for($si=0;$si -lt $storeCount;$si++){$invalidStores.Add([HarmonyLib.CodeInstruction]::new([Reflection.Emit.OpCodes]::Callvirt,$leaderlessSetter))}
        $invalidStoreRejected=$false
        try{@($leaderlessRewrite.Invoke($null,[object[]](,$invalidStores))) | Out-Null}
        catch{if($_.Exception.ToString() -like '*expected one exact hero cash write*'){$invalidStoreRejected=$true}else{throw}}
        if(-not $invalidStoreRejected){throw 'Unsupported native leaderless cash stores were accepted.'}
    }
    $leaderlessWageStoreBinding=$true
    $preserved=$true
    $navalRoot=Join-Path $BannerlordPath 'Modules\NavalDLC\bin\Win64_Shipping_Client'
    $navalPath=Join-Path $navalRoot 'NavalDLC.dll'
    if(Test-Path -LiteralPath $navalPath -PathType Leaf){
        $script:economyRoots+=,$navalRoot
        $naval=[Reflection.Assembly]::LoadFrom($navalPath)
        $patches.GetMethod('ApplyNavalRecruitment',$static).Invoke($null,@()) | Out-Null
        $navalFunding=$client.GetType('ReignBeta.Economy.ReignWarEconomyNaval',$true)
        $navalOwner='com.bannerlordreign.reignbeta.economy.naval'
        $navalBindings=$navalFunding.GetField('Bound',$static).GetValue($null)
        $navalCost=$naval.GetType('NavalDLC.GameComponents.NavalDLCShipCostModel',$true)
        $navalDistribution=$naval.GetType('NavalDLC.CampaignBehaviors.NavalShipDistributionCampaignBehavior',$true)
        $navalTrade=$naval.GetType('NavalDLC.CampaignBehaviors.ShipTradeCampaignBehavior',$true)
        $nativeOwnership=$campaign.GetType('TaleWorlds.CampaignSystem.Actions.ChangeShipOwnerAction',$true).GetMethod('ApplyInternal',$static)
        $nativeRepair=$campaign.GetType('TaleWorlds.CampaignSystem.Actions.RepairShipAction',$true).GetMethod('Apply',$static)
        $expectedNaval=@{
            PriceTranspiler=$navalCost.GetMethod('GetShipBaseValue',$static)
            UpgradePriceTranspiler=$navalCost.GetMethod('GetShipUpgradePieceValueInternal',$static)
            PricePrefix=$navalCost.GetMethod('GetShipTradeValue',$instance)
            PricePostfix=$navalCost.GetMethod('GetShipTradeValue',$instance)
            PriceFinalizer=$navalCost.GetMethod('GetShipTradeValue',$instance)
            RecoveryPrefix=$navalDistribution.GetMethod('RecoverGoldFromRemainingShipsAfterDistribution',$instance)
            PrizeTranspiler=$campaign.GetType('TaleWorlds.CampaignSystem.MapEvents.MapEvent',$true).GetMethod('LootDefeatedPartyShips',$instance)
            TradePrefix=$nativeOwnership;TradePostfix=$nativeOwnership;TradeFinalizer=$nativeOwnership;TradeTranspiler=$nativeOwnership
            TradeCommissionPrefix=$navalTrade.GetMethod('OnShipOwnerChanged',$instance)
            RepairCommissionPrefix=$navalTrade.GetMethod('OnShipRepaired',$instance)
            RepairPrefix=$nativeRepair;RepairFinalizer=$nativeRepair
        }
        if($navalBindings.Count -ne 15){throw 'Expected15 exact naval funding hooks.'}
        foreach($entry in $expectedNaval.GetEnumerator()){
            if($null -eq $entry.Value -or $navalBindings[$entry.Key] -ne $entry.Value){throw ('Wrong naval funding binding: '+$entry.Key)}
            $info=[HarmonyLib.Harmony]::GetPatchInfo($entry.Value)
            $hook=$navalFunding.GetMethod($entry.Key,$static)
            $owned=@(@($info.Prefixes)+@($info.Postfixes)+@($info.Transpilers)+@($info.Finalizers) | Where-Object {$_.owner -eq $navalOwner -and $_.PatchMethod -eq $hook})
            if($owned.Count -ne 1){throw ('Missing/duplicate naval funding hook: '+$entry.Key)}
            [Runtime.CompilerServices.RuntimeHelpers]::PrepareMethod($hook.MethodHandle)
        }
        $navalCoverage=$navalFunding.GetMethod('Coverage',$static).Invoke($null,@())
        if(-not [bool]$navalCoverage['healthy'].Value -or [int]$navalCoverage['expected'].Value -ne 15){throw 'Naval source-specific coverage is stale.'}
        foreach($transformName in @('PriceTranspiler','UpgradePriceTranspiler','PrizeTranspiler','TradeTranspiler')){
            $original=$read.Invoke($null,[object[]]@($expectedNaval[$transformName],$null))
            $codes=@($original | ForEach-Object {$_.opcode});$operands=@($original | ForEach-Object {,$_.operand})
            $boundaries=@($original | ForEach-Object {Get-WarProceedsBoundary $_})
            $transformed=@($navalFunding.GetMethod($transformName,$static).Invoke($null,[object[]](,$original)))
            if($transformed.Count -ne $codes.Count){throw ('Naval rewrite changed instruction count: '+$transformName)}
            $changes=0
            for($ni=0;$ni -lt $codes.Count;$ni++){
                if(-not (Test-WarProceedsBoundary $transformed[$ni] $boundaries[$ni])){throw 'Naval rewrite lost branch/exception boundaries.'}
                if($transformed[$ni].opcode -ne $codes[$ni] -or -not [object]::Equals($transformed[$ni].operand,$operands[$ni])){
                    $expectedHelper=if($transformName -eq 'PrizeTranspiler'){'UnclaimedShipValue'}elseif($transformName -like '*PriceTranspiler'){'AiValueFactor'}else{
                        switch($operands[$ni].Name){ApplyBetweenCharacters{'TradeBetween'} ApplyForPartyToCharacter{'TradePartyToHero'} ApplyForCharacterToParty{'TradeHeroToParty'} GetShipTradeValue{'QuotedValue'} default{throw 'Unexpected changed ship-trade instruction.'}}
                    }
                    if($transformed[$ni].opcode -ne [Reflection.Emit.OpCodes]::Call -or $transformed[$ni].operand -ne $navalFunding.GetMethod($expectedHelper,$static)){throw 'Wrong naval replacement helper.'}
                    $changes++
                }
            }
            $expectedChanges=if($transformName -eq 'TradeTranspiler'){9}else{1}
            if($changes -ne $expectedChanges){throw 'Wrong count of native naval financial rewrites.'}
            $empty=[Collections.Generic.List[HarmonyLib.CodeInstruction]]::new();$rejected=$false
            try {@($navalFunding.GetMethod($transformName,$static).Invoke($null,[object[]](,$empty)))|Out-Null}
            catch {$rejected=$true}
            if(-not $rejected){throw 'Missing native naval source shape was accepted.'}
        }
        $legacyRejected=$false
        try {$navalFunding.GetMethod('ObserveLegacyNative',$static).Invoke($null,@())|Out-Null}
        catch {$legacyRejected=$_.Exception.ToString() -like '*exact paused ship fixture scope*'}
        if(-not $legacyRejected){throw 'Legacy price rules were available outside the guarded fixture.'}
        $nativeSave=$campaign.GetType('TaleWorlds.CampaignSystem.SaveableCampaignTypeDefiner',$true).GetMethod('DefineContainerDefinitions',$instance)
        $saveInstructions=$read.Invoke($null,[object[]]@($nativeSave,$null))
        $shipList=[Collections.Generic.List[TaleWorlds.CampaignSystem.Naval.Ship]]
        if(@($saveInstructions | Where-Object {$_.opcode -eq [Reflection.Emit.OpCodes]::Ldtoken -and $_.operand -eq $shipList}).Count -ne 1){throw 'Native ship-reference list has no save registration.'}
        $behaviorType=$client.GetType('ReignBeta.Economy.ReignEconomyCampaignBehavior',$true)
        $isolatedBehavior=[Activator]::CreateInstance($behaviorType)
        $nativeShip=$campaign.GetType('TaleWorlds.CampaignSystem.Naval.Ship',$true)
        $hullField=$nativeShip.GetField('ShipHull',$instance)
        $hull=[Runtime.Serialization.FormatterServices]::GetUninitializedObject($hullField.FieldType)
        $ships=$behaviorType.GetField('NavalShips',$instance).GetValue($isolatedBehavior)
        $shipIds=$behaviorType.GetField('NavalShipIds',$instance).GetValue($isolatedBehavior)
        $economy=$behaviorType.GetProperty('EconomyState',$instance).GetValue($isolatedBehavior,$null)
        $acquisitions=$economy.GetType().GetField('NavalAcquisitions',$instance).GetValue($economy)
        $acquisitionType=$client.GetType('ReignBeta.Economy.EconomyShipAcquisition',$true)
        foreach($id in @('isolated_ship_a','isolated_ship_b')){
            $ship=[Runtime.Serialization.FormatterServices]::GetUninitializedObject($nativeShip)
            $hullField.SetValue($ship,$hull)
            $nativeShip.GetProperty('RandomValue',$instance).GetSetMethod($true).Invoke($ship,[object[]]@(7))|Out-Null
            $ships.Add($ship);$shipIds.Add($id)
            $entry=[Activator]::CreateInstance($acquisitionType,$true)
            $acquisitionType.GetField('Id',$instance).SetValue($entry,$id)
            $acquisitionType.GetField('Hull',$instance).SetValue($entry,$hull.StringId)
            $acquisitionType.GetField('Kind',$instance).SetValue($entry,'npc_purchase')
            $acquisitions.Add($id,$entry)
        }
        $restore=$navalFunding.GetMethod('RestoreAcquisitions',$static)
        $restore.Invoke($null,[object[]]@($isolatedBehavior))|Out-Null
        $index=$behaviorType.GetField('NavalShipIndex',$instance).GetValue($isolatedBehavior)
        if($index.Count -ne 2 -or $index[$ships[0]] -ne 'isolated_ship_a' -or $index[$ships[1]] -ne 'isolated_ship_b'){throw 'Same-random/same-hull ships lost reference identity.'}
        $shipIds.RemoveAt(1);$badReferencesRejected=$false
        try {$restore.Invoke($null,[object[]]@($isolatedBehavior))|Out-Null}
        catch {$badReferencesRejected=$_.Exception.ToString() -like '*reference/receipt lists do not match*'}
        if(-not $badReferencesRejected){throw 'Corrupt native ship-reference/receipt lists were accepted.'}
        $shipIds.Add('isolated_ship_b');$restore.Invoke($null,[object[]]@($isolatedBehavior))|Out-Null
        $navalAcquisitionIdentity=$true;$navalFundingBinding=$true;$navalFundingHooks=$navalBindings.Count
        [ordered]@{schema='reign-naval-funding-binding-v1';binding=$navalFundingBinding;hooks=$navalFundingHooks;nativeShipReferenceContainer=$navalAcquisitionIdentity;campaignMutations=0;nativeGameplayAcceptance=$false} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $EvidenceDirectory 'naval-funding.json') -Encoding UTF8
        $patches.GetMethod('ApplyNavalRecruitment',$static).Invoke($null,@()) | Out-Null
        $navalTarget=$naval.GetType('NavalDLC.CampaignBehaviors.ShipTradeCampaignBehavior',$true).GetMethod('OnSettlementEntered',$instance)
        if($registered.Count -ne 61 -or $registered['NavalGarrisonTranspiler'] -ne $navalTarget){throw 'Late naval economy bindings are incomplete.'}
        $navalWageMethod=$naval.GetType('NavalDLC.GameComponents.NavalDLCClanFinanceModel',$true).GetMethod('AddPartyExpense',$instance)
        if($registered['NavalLeaderlessWageStoreTranspiler'] -ne $navalWageMethod){throw 'Late naval leaderless wage source is uncovered.'}
        Test-LeaderlessStoreRewrite $navalWageMethod
        $targets+=,$navalWageMethod;$bound++
        $fishType=$naval.GetType('NavalDLC.CampaignBehaviors.FishingPartyCampaignBehavior',$true)
        $fishTransform=$patches.GetMethod('CivilianFishingHearthTranspiler',$static)
        $fishNoDebit=$patches.GetMethod('KeepCivilianFishingHearths',$static)
        foreach($pair in @(@('OnDailySettlementTick','FishingCreationHearthTranspiler'),@('TryReinforceParty','FishingReinforcementHearthTranspiler'))){
            $fishMethod=$fishType.GetMethod($pair[0],$instance)
            if($registered[$pair[1]] -ne $fishMethod){throw 'An exact fishing crew population route is uncovered.'}
            $fishOriginal=$read.Invoke($null,[object[]]@($fishMethod,$null))
            $fishBeforeCodes=@($fishOriginal | ForEach-Object {$_.opcode});$fishBeforeOperands=@($fishOriginal | ForEach-Object {,$_.operand})
            $fishRewritten=@($fishTransform.Invoke($null,[object[]](,$fishOriginal)))
            if($fishRewritten.Count -ne $fishBeforeCodes.Count){throw 'Fishing repair changed native instruction count.'}
            $changed=0
            for($fi=0;$fi -lt $fishRewritten.Count;$fi++){
                if($fishBeforeOperands[$fi] -eq $nativeHearthSetter){
                    if($fishRewritten[$fi].opcode -ne [Reflection.Emit.OpCodes]::Call -or $fishRewritten[$fi].operand -ne $fishNoDebit){throw 'Fishing hearth debit was not suppressed.'};$changed++
                }elseif($fishRewritten[$fi].opcode -ne $fishBeforeCodes[$fi] -or -not [object]::Equals($fishRewritten[$fi].operand,$fishBeforeOperands[$fi])){throw 'Fishing repair changed unrelated native instructions.'}
            }
            if($changed -ne 1){throw 'Fishing repair requires exactly one source-specific hearth debit.'}
            $targets+=,$fishMethod;$bound++
        }
        $isolatedVillage=[Runtime.Serialization.FormatterServices]::GetUninitializedObject([TaleWorlds.CampaignSystem.Settlements.Village])
        $isolatedVillage.Hearth=123.5
        foreach($proposedHearth in @([single]-5,[single]0,[single]10,[single]123,[single]200)){
            $fishNoDebit.Invoke($null,[object[]]@($isolatedVillage,$proposedHearth))|Out-Null
            if($isolatedVillage.Hearth -ne 123.5){throw 'Civilian fishing changed actual native fractional hearths.'}
        }
        $civilianFishingBinding=$true

        $nativeNaval=$read.Invoke($null,[object[]]@($navalTarget,$null))
        $navalOperands=@($nativeNaval | ForEach-Object {,$_.operand});$navalCodes=@($nativeNaval | ForEach-Object {$_.opcode})
        $rewrittenNaval=@($patches.GetMethod('NavalGarrisonTranspiler',$static).Invoke($null,[object[]](,$nativeNaval)))
        if($rewrittenNaval.Count -ne $nativeNaval.Count+2){throw 'Naval recruitment changed unrelated instructions.'}
        $extraNaval=0;$navalCalls=0
        for($i=0;$i -lt $nativeNaval.Count;$i++){
            if($navalOperands[$i] -is [Reflection.MethodInfo] -and $navalOperands[$i].Name -eq 'AddToCounts'){
                if($rewrittenNaval[$i+$extraNaval].opcode -ne [Reflection.Emit.OpCodes]::Ldarg_1 -or
                   $rewrittenNaval[$i+$extraNaval+1].opcode -ne [Reflection.Emit.OpCodes]::Ldarg_2){throw 'Naval arrival identity/locality missing.'}
                $extraNaval+=2
                if($rewrittenNaval[$i+$extraNaval].operand.Name -ne 'EnlistNavalGarrison'){throw 'Naval recruits bypass accounting.'}
                $navalCalls++;continue
            }
            if($rewrittenNaval[$i+$extraNaval].opcode -ne $navalCodes[$i] -or $rewrittenNaval[$i+$extraNaval].operand -ne $navalOperands[$i]){
                throw 'Naval perks or native limits changed outside the enlistment call.'
            }
        }
        if($navalCalls -ne 1){throw 'Expected one native naval enlistment call.'}
        $info=[HarmonyLib.Harmony]::GetPatchInfo($navalTarget)
        if(@($info.Transpilers | Where-Object {$_.owner -eq $owner -and $_.PatchMethod.Name -eq 'NavalGarrisonTranspiler'}).Count -ne 1){throw 'Naval hook duplicated or missing.'}
        $targets+=,$navalTarget;$bound++
        $navalDiagnostics=$patches.GetMethod('BuildDiagnostics',$static).Invoke($null,@())
        if(-not [bool]$navalDiagnostics['allHooksHealthy'].Value -or [int]$navalDiagnostics['expectedHookCount'].Value -ne 61 -or
           -not [bool]$navalDiagnostics['kingdomWageModifierHook'].Value){throw 'Late naval hook diagnostics are stale.'}
        $navalWageTarget=$naval.GetType('NavalDLC.GameComponents.NavalDLCPartyWageModel',$true).GetMethod('GetTotalWage',$instance)
        $wageInfo=[HarmonyLib.Harmony]::GetPatchInfo($navalWageTarget)
        $wageHooks=@($wageInfo.Postfixes | Where-Object {$_.owner -eq $owner -and $_.PatchMethod.Name -eq 'NavalKingdomWagePostfix'})
        if($registered['NavalKingdomWagePostfix'] -ne $navalWageTarget -or $wageHooks.Count -ne 1 -or $wageHooks[0].priority -ne [HarmonyLib.Priority]::Last){throw 'Kingdom wage adjustment missed the final War Sails wage quote.'}
        $targets+=,$navalWageTarget;$bound++
        $perkType=$naval.GetType('NavalDLC.CharacterDevelopment.NavalPerks+Boatswain',$true)
        if($null -eq $perkType.GetProperty('ShipwrightsHand',$static)){throw 'Naval fixture perk binding changed.'}
        $navalName='NavalDLCVillageProductionCalculatorModel'
        $outerType=$naval.GetType(('NavalDLC.GameComponents.'+$navalName),$true)
        $outer=[Runtime.Serialization.FormatterServices]::GetUninitializedObject($outerType)
        $reignType=$client.GetType('ReignBeta.Economy.ReignVillageProductionCalculatorModel',$true)
        $inner=[Runtime.Serialization.FormatterServices]::GetUninitializedObject($reignType)
        $describe=$patches.GetMethod('ModelRoute',$static)
        $probeArgs=[object[]]@($outer,$reignType,$navalName)
        $missing=$describe.Invoke($null,$probeArgs).ToString() | ConvertFrom-Json
        $outerType.GetMethod('Initialize',$instance).Invoke($outer,[object[]]@($inner)) | Out-Null
        $reviewed=$describe.Invoke($null,$probeArgs).ToString() | ConvertFrom-Json
        $unsupported=$describe.Invoke($null,[object[]]@($outer,$reignType,'UnknownModel')).ToString() | ConvertFrom-Json
        if($missing.healthy -or $unsupported.healthy -or -not $reviewed.healthy -or
           $reviewed.route -ne 'reviewed_naval_base' -or $reviewed.baseModel -ne $reignType.FullName){
            throw 'Native War Sails base-model coverage did not distinguish exact delegation from unsupported routes.'
        }
        $navalRoute='verified_binding'
    }
    $persistenceFields=0
    $savedField=$client.GetType('ReignBeta.Economy.ReignWarEconomyTestHost',$true).GetMethod('SavedField',$static)
    foreach($entry in @{
        'ReignBeta.Court.ReignCourtCampaignBehavior'=@('_rulerDocketState','_economicReportState','_operations','_obligations','_appliedNativeCommandIds');
        'ReignBeta.Government.ReignGovernmentCampaignBehavior'=@('_governments','_resolutions','_edicts');
        'ReignBeta.ClanAccords.ReignClanAccordsCampaignBehavior'=@('_state')
    }.GetEnumerator()){
        $ownerType=$client.GetType($entry.Key,$true)
        foreach($fieldName in $entry.Value){
            if($null -eq $ownerType.GetField($fieldName,$instance)){throw 'Missing saved assistance coverage: '+$fieldName}
            $persistenceFields++
        }
    }
    $governmentType=$client.GetType('ReignBeta.Government.ReignGovernmentCampaignBehavior',$true)
    $isolatedGovernment=[Runtime.Serialization.FormatterServices]::GetUninitializedObject($governmentType)
    $edictsField=$governmentType.GetField('_edicts',$instance)
    $isolatedEdicts=[Activator]::CreateInstance($edictsField.FieldType)
    $edictsField.SetValue($isolatedGovernment,$isolatedEdicts)
    $edict=[Activator]::CreateInstance($edictsField.FieldType.GetGenericArguments()[0])
    $isolatedEdicts.Add($edict)
    $before=$savedField.Invoke($null,[object[]]@($isolatedGovernment,'_edicts')).ToString()
    $budget=$edict.GetType().GetProperty('BudgetRemaining')
    $budget.SetValue($edict,[Convert]::ChangeType(37,$budget.PropertyType),$null)
    $after=$savedField.Invoke($null,[object[]]@($isolatedGovernment,'_edicts')).ToString()
    if($before -eq $after -or $isolatedEdicts.Count -ne 1 -or $budget.GetValue($edict,$null) -ne 37){throw 'Saved escrow evidence failed to observe a real value change without mutation.'}
    $persistedAidObserved=$true
    $testHost=$client.GetType('ReignBeta.Economy.ReignWarEconomyTestHost',$true)
    $financeInput=[Newtonsoft.Json.Linq.JArray]::Parse('[{"clanId":"one","cash":123,"dailyIncome":17,"incomeModelLines":[1]}]')
    $financeBefore=$financeInput.ToString()
    $financeResult=$testHost.GetMethod('FinanceSnapshot',$static).Invoke($null,[object[]](,$financeInput))
    if($financeResult.Count -ne 1 -or $financeResult[0]['cash'].ToString() -ne '123' -or
       $null -ne $financeResult[0]['incomeModelLines'] -or $financeInput.ToString() -ne $financeBefore){throw 'Passive finance snapshot lost cash or mutated/overexposed source rows.'}
    $villageType=$campaign.GetType('TaleWorlds.CampaignSystem.Settlements.Village',$true)
    $villageState=$villageType.GetField('_villageState',$instance)
    $isolatedVillage=[Runtime.Serialization.FormatterServices]::GetUninitializedObject($villageType)
    $productionType=$client.GetType('ReignBeta.Economy.ReignVillageProductionCalculatorModel',$true)
    $isolatedProduction=[Runtime.Serialization.FormatterServices]::GetUninitializedObject($productionType)
    foreach($stateName in @('Looted','BeingRaided')){
        $villageState.SetValue($isolatedVillage,[Enum]::Parse($villageState.FieldType,$stateName))
        $food=$productionType.GetMethod('CalculateDailyFoodProductionAmount',$instance).Invoke($isolatedProduction,[object[]]@($isolatedVillage))
        $goods=$productionType.GetMethod('CalculateDailyProductionAmount',$instance).Invoke($isolatedProduction,[object[]]@($isolatedVillage,$null))
        if($food -ne 0 -or $goods.ResultNumber -ne 0){throw 'Non-functioning village quoted nonzero output.'}
    }
    $foodType=$client.GetType('ReignBeta.Economy.ReignSettlementFoodModel',$true)
    $isolatedFood=[Runtime.Serialization.FormatterServices]::GetUninitializedObject($foodType)
    if($foodType.GetProperty('NumberOfMenOnGarrisonToEatOneFood',$instance).GetValue($isolatedFood,$null) -ne 25){throw 'Garrison food baseline did not apply.'}
    $counter=$patches.GetMethod('ObserveWithoutQuartering',$static)
    $foodQuote=[Func[single]]{return [single]$foodType.GetProperty('NumberOfMenOnGarrisonToEatOneFood',$instance).GetValue($isolatedFood,$null)}
    if($counter.Invoke($null,[object[]]@($foodQuote)) -ne 20 -or $foodQuote.Invoke() -ne 25){throw 'Quartering counterfactual leaked or lost native baseline.'}
    $failedCounter=[Func[single]]{throw 'expected quartering quote failure'}
    try {$counter.Invoke($null,[object[]]@($failedCounter)) | Out-Null;throw 'Counterfactual did not throw.'}
    catch {if($_.Exception.ToString() -notlike '*expected quartering quote failure*'){throw}}
    if($foodQuote.Invoke() -ne 25){throw 'Failed quartering quote changed live policy.'}
    $raidAction=$campaign.GetType('TaleWorlds.CampaignSystem.Actions.ChangeVillageStateAction',$true).GetMethod('ApplyBySettingToLooted',$static)
    if($null -eq $raidAction -or $raidAction.GetParameters().Count -ne 2){throw 'Native raid-state callback route unavailable.'}
    [Runtime.CompilerServices.RuntimeHelpers]::PrepareMethod($testHost.GetMethod('SetupRaid',$static).MethodHandle)
    $quarteringBound=$registered['GarrisonQuarteringTranspiler']
    if($quarteringBound -ne $campaign.GetType('TaleWorlds.CampaignSystem.GameComponents.DefaultPartyWageModel',$true).GetMethod('GetTotalWage',$instance)){throw 'Garrison wage hook bound to a wrapper instead of native baseline.'}
    $quarterOriginal=[HarmonyLib.PatchProcessor]::GetOriginalInstructions($quarteringBound)
    $quarterRewritten=@($patches.GetMethod('GarrisonQuarteringTranspiler',$static).Invoke($null,[object[]]@(,$quarterOriginal)))
    $quarterGetter=$campaign.GetType('TaleWorlds.CampaignSystem.CharacterObject',$true).GetProperty('TroopWage',$instance).GetGetMethod()
    if(@($quarterRewritten | Where-Object {$_.operand -eq $quarterGetter}).Count -ne 2){throw 'Field wage hook changed a hero salary quote.'}
    $fieldUnit=$client.GetType('ReignBeta.Economy.ReignWarEconomyOptions',$true).GetMethod('UnitWage',$static)
    $fieldSites=@(for($qi=0;$qi -lt $quarterRewritten.Count;$qi++){if($quarterRewritten[$qi].operand -eq $fieldUnit){$qi}})
    if($fieldSites.Count -ne 1 -or $quarterRewritten[$fieldSites[0]-1].opcode -ne [Reflection.Emit.OpCodes]::Ldarg_1 -or $quarterRewritten[$fieldSites[0]+3].opcode -ne [Reflection.Emit.OpCodes]::Mul){throw 'Field wage helper did not replace only the regular quote before count multiplication.'}
    $optionsType=[Reign.Core.Contracts.Economy.WarEconomyOptions]
    $fieldOptions=$optionsType::Defaults
    $fieldExpected=@(3,6,12,18,24,40,68,92)
    for($ti=0;$ti -lt $fieldExpected.Count;$ti++){
        if($fieldOptions.TroopWage($ti,$false,0) -ne $fieldExpected[$ti] -or $fieldOptions.TroopWage($ti,$true,0) -ne [Math]::Truncate($fieldExpected[$ti]*1.5)){throw 'Compiled field wage schedule or mercenary premium changed.'}
    }
    $preferences=$client.GetType('ReignBeta.Economy.ReignWarEconomyOptions',$true)
    $readPreferences=$preferences.GetMethod('TryRead',$static)
    $preferenceEnvelope=[Newtonsoft.Json.Linq.JObject]::new()
    $preferenceEnvelope['version']=[Newtonsoft.Json.Linq.JValue]::new([long]1)
    $preferenceEnvelope['revision']=[Newtonsoft.Json.Linq.JValue]::new('2026-11-06T00:00:00.0000000Z')
    $preferenceEnvelope['values']=[Newtonsoft.Json.Linq.JObject]::FromObject($fieldOptions.Values())
    $readArgs=[object[]]@($preferenceEnvelope,$null,$null)
    if(-not $readPreferences.Invoke($null,$readArgs) -or $readArgs[1].TroopWage(5,$false,0) -ne 40){throw 'Complete versioned preferences did not parse.'}
    $snapshotPreferences=$preferences.GetMethod('Snapshot',$static)
    $preferencesBefore=$snapshotPreferences.Invoke($null,@()).ToString()
    foreach($bad in @(@{Key='version';Json='1.0'},@{Key='version';Json='"1"'},@{Key='version';Json='9223372036854775808'},@{Key='revision';Json='{}'},@{Key='revision';Json='3'},@{Key='values';Json='[]'})){
        $invalid=$preferenceEnvelope.DeepClone()
        $invalid[$bad.Key]=[Newtonsoft.Json.Linq.JToken]::Parse($bad.Json)
        if($preferences.GetMethod('Apply',$static).Invoke($null,[object[]]@($invalid,$false))){throw 'Malformed preferences were accepted.'}
        if($snapshotPreferences.Invoke($null,@()).ToString() -ne $preferencesBefore){throw 'Malformed preferences changed live options or metadata.'}
    }
    $quarterSites=@(for($qi=0;$qi -lt $quarterRewritten.Count;$qi++){if($quarterRewritten[$qi].operand -is [Reflection.MethodInfo] -and $quarterRewritten[$qi].operand.Name -eq 'WageInQuarters'){$qi}})
    if($quarterSites.Count -ne 1 -or $quarterRewritten[$quarterSites[0]-1].opcode -ne [Reflection.Emit.OpCodes]::Ldarg_1 -or $quarterRewritten[$quarterSites[0]-2].opcode -ne [Reflection.Emit.OpCodes]::Mul){throw 'Quartering did not wrap exactly one native regular stack multiplication.'}
    $stackQuote=$patches.GetMethod('GarrisonStackWage',$static)
    if($stackQuote.Invoke($null,[object[]]@(200)) -ne 150 -or $stackQuote.Invoke($null,[object[]]@(300)) -ne 225 -or $stackQuote.Invoke($null,[object[]]@(0)) -ne 0){throw 'Garrison stack discount lost cheap-troop savings or legitimate zero wages.'}
    $wageAdapter=$client.GetType('ReignBeta.Economy.ReignWarEconomyOptions',$true)
    $wageMobileType=$campaign.GetType('TaleWorlds.CampaignSystem.Party.MobileParty',$true)
    $wageGarrison=[Runtime.Serialization.FormatterServices]::GetUninitializedObject($wageMobileType)
    $wageMobileType.GetProperty('IsGarrison',$instance).SetValue($wageGarrison,$true,$null)
    if(-not $wageAdapter.GetMethod('UsesTroopWages',$static).Invoke($null,[object[]]@($wageGarrison))){throw 'Garrison was excluded from the higher troop base wage schedule.'}
    # Native Tier requires a live Campaign.Models.CharacterStatsModel. Keep this
    # isolated oracle explicit: verify actual garrison routing, then each shared
    # golden base composed with the compiled quartering adapter. Native full-party
    # perk/payday acceptance remains separate after deployment.
    $garrisonDefaults=[Reign.Core.Contracts.Economy.WarEconomyOptions]::Defaults
    foreach($garrisonTier in 0..7){
        $garrisonBase=$garrisonDefaults.TroopWage($garrisonTier,$false,0)
        if($stackQuote.Invoke($null,[object[]]@([int]($garrisonBase*100))) -ne ($garrisonBase*75)){throw 'Garrison higher base and unchanged quartering factor do not compose.'}
    }
    $garrisonStackRounding=$true
    $nativeWageGetter=$wageMobileType.GetProperty('TotalWage',$instance).GetGetMethod()
    $nativeWageIl=@([HarmonyLib.PatchProcessor]::GetOriginalInstructions($nativeWageGetter))
    if(@($nativeWageIl | Where-Object {$_.opcode -eq [Reflection.Emit.OpCodes]::Conv_I4}).Count -ne 1 -or
       @($nativeWageIl | Where-Object {$_.operand -is [Reflection.MethodInfo] -and $_.operand.Name -eq 'get_ResultNumber'}).Count -ne 1){throw 'Native payroll integer conversion changed.'}
    $financeWageIl=@([HarmonyLib.PatchProcessor]::GetOriginalInstructions($registered['NativeWagePaymentPostfix']))
    if(@($financeWageIl | Where-Object {$_.operand -eq $nativeWageGetter}).Count -ne 1){throw 'Native payroll no longer reads the observed party total.'}
    foreach($quoteMethod in @($client.GetType('ReignBeta.Economy.ReignWarEconomyTransactions',$true).GetMethod('AdditionalWages',$static),$wageAdapter.GetMethod('FinalUnitWage',$static))){
        $quoteIl=@([HarmonyLib.PatchProcessor]::GetOriginalInstructions($quoteMethod))
        if(@($quoteIl | Where-Object {$_.operand -is [Reflection.MethodInfo] -and $_.operand.Name -eq 'get_RoundedResultNumber'}).Count -ne 0 -or
           @($quoteIl | Where-Object {$_.opcode -eq [Reflection.Emit.OpCodes]::Conv_I4}).Count -lt 2){throw 'Final wage quote and native payroll use different integer conversion.'}
    }
    $readiness=$client.GetType('ReignBeta.Economy.ReignMilitaryReadiness',$true)
    $pressureParser=$readiness.GetMethod('TryParsePressure',$static)
    $validPressure=[Newtonsoft.Json.Linq.JObject]::Parse('{"version":1,"complete":true,"campaignId":"test","timelineId":"main","pairs":[{"actor_kingdom_id":"a","target_kingdom_id":"b","pressure_value":100,"dominant_channel":"frontier"},{"actor_kingdom_id":"b","target_kingdom_id":"a","pressure_value":-100,"dominant_channel":null},{"actor_kingdom_id":"a","target_kingdom_id":"c","pressure_value":0}]}')
    $parseArguments=[object[]]@($validPressure,'test','main',$null)
    if(-not $pressureParser.Invoke($null,$parseArguments) -or $parseArguments[3].Count -ne 3 -or $parseArguments[3][2].Value -ne 0){throw 'Valid directed pressure, boundary values or consumed zero pressure were rejected.'}
    $priorPressureCulture=[Threading.Thread]::CurrentThread.CurrentCulture
    try {
        $pressureCulture=[Globalization.CultureInfo]::InvariantCulture.Clone()
        $pressureCulture.NumberFormat.NegativeSign='~'
        [Threading.Thread]::CurrentThread.CurrentCulture=$pressureCulture
        $parseArguments=[object[]]@($validPressure,'test','main',$null)
        if(-not $pressureParser.Invoke($null,$parseArguments) -or $parseArguments[3][1].Value -ne -100){throw 'Pressure parsing changed with localized negative-number formatting.'}
    } finally {[Threading.Thread]::CurrentThread.CurrentCulture=$priorPressureCulture}
    $invalidPressure=@(
        @{Path='version';Json='"1"'},@{Path='version';Json='"x"'},@{Path='version';Json='{}'},
        @{Path='version';Json='3000000000'},@{Path='complete';Json='"true"'},@{Path='complete';Json='false'},
        @{Path='campaignId';Json='{}'},@{Path='campaignId';Json='"other"'},@{Path='timelineId';Json='[]'},
        @{Path='timelineId';Json='"other"'},@{Path='pairs';Json='{}'},@{Path='pairs[0]';Json='[]'},
        @{Path='pairs[0].actor_kingdom_id';Json='{}'},@{Path='pairs[0].actor_kingdom_id';Json='""'},
        @{Path='pairs[0].target_kingdom_id';Json='"a"'},@{Path='pairs[0].dominant_channel';Json='{}'},
        @{Path='pairs[0].dominant_channel';Json='[]'},@{Path='pairs[0].pressure_value';Json='"1"'},
        @{Path='pairs[0].pressure_value';Json='1.5'},@{Path='pairs[0].pressure_value';Json='101'},
        @{Path='pairs[0].pressure_value';Json='-101'},@{Path='pairs[0].pressure_value';Json='3000000000'},
        @{Path='pairs[0].pressure_value';Json='999999999999999999999999999999999999999999'}
    )
    foreach($case in $invalidPressure){
        $payload=$validPressure.DeepClone()
        $payload.SelectToken($case.Path).Replace([Newtonsoft.Json.Linq.JToken]::Parse($case.Json))
        $parseArguments=[object[]]@($payload,'test','main',$null)
        if($pressureParser.Invoke($null,$parseArguments) -or $null -ne $parseArguments[3]){throw ('Malformed pressure was accepted: '+$case.Path+'='+$case.Json)}
    }
    foreach($shape in @('null','duplicate')){
        $payload=$validPressure.DeepClone()
        if($shape -eq 'null'){$payload=$null}
        else{$payload['pairs'].Add($payload['pairs'][0].DeepClone())}
        $parseArguments=[object[]]@($payload,'test','main',$null)
        if($pressureParser.Invoke($null,$parseArguments) -or $null -ne $parseArguments[3]){throw ('Invalid pressure snapshot accepted: '+$shape)}
    }
    $payload=$validPressure.DeepClone()
    $payload['pairs'].RemoveAll()
    for($i=0;$i -lt 1024;$i++){
        $payload['pairs'].Add([Newtonsoft.Json.Linq.JObject]::Parse(('{"actor_kingdom_id":"actor_'+$i+'","target_kingdom_id":"target_'+$i+'","pressure_value":0}')))
    }
    $parseArguments=[object[]]@($payload,'test','main',$null)
    if(-not $pressureParser.Invoke($null,$parseArguments) -or $parseArguments[3].Count -ne 1024){throw 'The unique 1024-pair pressure boundary was rejected.'}
    $payload['pairs'].Add([Newtonsoft.Json.Linq.JObject]::Parse('{"actor_kingdom_id":"actor_1024","target_kingdom_id":"target_1024","pressure_value":0}'))
    $parseArguments=[object[]]@($payload,'test','main',$null)
    if($pressureParser.Invoke($null,$parseArguments) -or $null -ne $parseArguments[3]){throw 'The unique 1025-pair pressure boundary was accepted.'}
    $pressureParsingBounded=$true
    foreach($name in @('RecruitmentRoom','TargetFraction','AcceptPressure','Order','Observation','UpdateNpcStandings','DrawdownParty','DrawdownDaily','SafeForDrawdown')){
        [Runtime.CompilerServices.RuntimeHelpers]::PrepareMethod($readiness.GetMethod($name,$static).MethodHandle)
    }
    $componentMethod=$testHost.GetMethod('ComponentFingerprints',$static)
    $firstState=[Newtonsoft.Json.Linq.JObject]::Parse('{"economy":{"Recovery":{"b":0.3,"a":1.0}},"persistedAssistance":{"receipts":["first","second"]}}')
    $reordered=[Newtonsoft.Json.Linq.JObject]::Parse('{"persistedAssistance":{"receipts":["first","second"]},"economy":{"Recovery":{"a":1.0,"b":0.3}}}')
    $changed=[Newtonsoft.Json.Linq.JObject]::Parse('{"economy":{"Recovery":{"b":0.4,"a":1.0}},"persistedAssistance":{"receipts":["second","first"]}}')
    $firstHashes=$componentMethod.Invoke($null,[object[]](,$firstState))
    $orderedHashes=$componentMethod.Invoke($null,[object[]](,$reordered))
    $changedHashes=$componentMethod.Invoke($null,[object[]](,$changed))
    foreach($name in @('economy.Recovery','persistedAssistance.receipts')){
        if($firstHashes['hashes'][$name].ToString() -ne $orderedHashes['hashes'][$name].ToString() -or
           $firstHashes['hashes'][$name].ToString() -eq $changedHashes['hashes'][$name].ToString()){
            throw 'Persistence component hashes lost object-order independence or a changed saved value/receipt order.'
        }
    }
    if($firstHashes['schema'].ToString() -ne 'reign-war-economy-persistence-components-v1' -or
       -not [bool]$firstHashes['arrayOrderPreserved'].Value){throw 'Persistence component diagnostic schema is stale.'}
    $componentHashesObserved=$true
    $randomType=$campaign.GetReferencedAssemblies() | Where-Object {$_.Name -eq 'TaleWorlds.Core'} | Select-Object -First 1
    $core=[Reflection.Assembly]::Load($randomType)
    $mbRandom=$core.GetType('TaleWorlds.Core.MBRandom',$true)
    $fastRandom=$core.GetType('TaleWorlds.Core.MBFastRandom',$true)
    $randomField=$mbRandom.GetField('_internalRandom',$static)
    $previousRandom=$randomField.GetValue($null)
    try {
        $rng=[Activator]::CreateInstance($fastRandom,[object[]]@([uint32]12345))
        $randomField.SetValue($null,$rng)
        $expectedRng=[Activator]::CreateInstance($fastRandom,[object[]]@([uint32]12345))
        $expected=$fastRandom.GetMethod('NextFloat',$instance).Invoke($expectedRng,@())
        $floatProperty=$mbRandom.GetProperty('RandomFloat',$static)
        $sample=[Func[single]]{return [single]$floatProperty.GetValue($null,$null)}
        $observer=$client.GetType('ReignBeta.Economy.ReignVillageProductionCalculatorModel',$true).GetMethod('ObserveWithoutRandomAdvance',$static)
        $first=$observer.Invoke($null,[object[]]@($sample))
        $second=$observer.Invoke($null,[object[]]@($sample))
        if($first -ne $expected -or $second -ne $expected){throw 'Production quotation changed the native random stream.'}
        $failedSample=[Func[single]]{[void]$floatProperty.GetValue($null,$null);throw 'expected observation failure'}
        try {$observer.Invoke($null,[object[]]@($failedSample)) | Out-Null;throw 'Failure probe did not throw.'}
        catch {if($_.Exception.ToString() -notlike '*expected observation failure*'){throw}}
        if($floatProperty.GetValue($null,$null) -ne $expected){throw 'Failed production quotation changed the native random stream.'}
    } finally {$randomField.SetValue($null,$previousRandom)}
    $upgrade=$campaign.GetType('TaleWorlds.CampaignSystem.CampaignBehaviors.PartyUpgraderCampaignBehavior+TroopUpgradeArgs',$true)
    $ctor=@($upgrade.GetConstructors($instance) | Where-Object {$_.GetParameters().Count -eq 6})[0]
    $argsValue=$ctor.Invoke([object[]]@($null,$null,3,17,21,[single]0.5))
    foreach($entry in @{PossibleUpgradeCount=3;UpgradeGoldCost=17;UpgradeXpCost=21}.GetEnumerator()){
        if($upgrade.GetField($entry.Key,$instance).GetValue($argsValue) -ne $entry.Value){throw 'Native upgrade argument contract changed.'}
    }
    foreach($name in @('AiRecruitmentPrefix','AiRecruitmentPostfix','AiRecruitmentFinalizer','PlayerRecruitmentCommitPrefix','PlayerRecruitmentCommitPostfix','PlayerRecruitmentFinalizer','LordPartyInitializedPostfix','LordPartyInitializationFinalizer','NativeWagePaymentPostfix','TournamentPaymentPrefix','TournamentPaymentPostfix','TournamentPrizePaymentPrefix','TournamentPrizePaymentPostfix','GarrisonRecruitmentPrefix','BaseGarrisonRecruitmentPrefix','AutomaticUpgradePrefix')){
        [Runtime.CompilerServices.RuntimeHelpers]::PrepareMethod($patches.GetMethod($name,$static).MethodHandle)
    }
    $patches.GetMethod('Unapply',$static).Invoke($null,@()) | Out-Null
    if($navalFundingBinding){foreach($target in $expectedNaval.Values){$info=[HarmonyLib.Harmony]::GetPatchInfo($target);if($null -ne $info -and $info.Owners.Contains($navalOwner)){throw 'Naval funding hook remained installed.'}}}
    foreach($target in $targets){$info=[HarmonyLib.Harmony]::GetPatchInfo($target);if($null -ne $info -and $info.Owners.Contains($owner)){throw 'Economy hook remained installed.'}}
    $raidFood=$client.GetType('ReignBeta.Economy.ReignRaidFoodDisruption',$true)
    try {
        $raidFood.GetMethod('Install',$static).Invoke($null,@()) | Out-Null
        $raidHooks=$raidFood.GetField('Hooks',$static).GetValue($null)
        $raidError=$raidFood.GetField('_hookError',$static).GetValue($null)
        $expectedRaidHooks=if(Test-Path -LiteralPath $navalPath){15}else{6}
        if($raidError.Length -gt 0 -or $raidHooks.Count -ne $expectedRaidHooks){throw ('Raid food hook binding failed: '+$raidError)}
        foreach($entry in $raidHooks.GetEnumerator()) {
            $info=[HarmonyLib.Harmony]::GetPatchInfo($entry.Value)
            $owned=@(@($info.Prefixes)+@($info.Postfixes)+@($info.Transpilers)+@($info.Finalizers) | Where-Object {$_.owner -eq 'com.bannerlordreign.reignbeta.raidfood' -and $_.PatchMethod.Name -eq $entry.Key})
            if($owned.Count -ne 1){throw ('Raid food hook duplicated or missing: '+$entry.Key)}
            [Runtime.CompilerServices.RuntimeHelpers]::PrepareMethod($owned[0].PatchMethod.MethodHandle)
        }
        $stringId=$campaign.GetType('TaleWorlds.CampaignSystem.MapEvents.MapEvent',$true).BaseType.GetProperty('StringId',$instance)
        $saved=@($stringId.GetCustomAttributesData() | Where-Object {$_.AttributeType.Name -eq 'SaveablePropertyAttribute' -and $_.ConstructorArguments[0].Value -eq 1})
        if($saved.Count -ne 1){throw 'Native raid StringId no longer has the verified saved identity contract.'}
        $depth=$raidFood.GetField('_outerFoodDepth',$static)
        $depth.SetValue($null,2)
        try {
            $quote=[Func[single]]{if($depth.GetValue($null) -ne 0){throw 'Nested quote inherited a production frame.'};return [single]17}
            if($raidFood.GetMethod('ObserveQuote',$static).Invoke($null,[object[]]@($quote)) -ne 17 -or $depth.GetValue($null) -ne 2){throw 'Nested food quote did not restore its caller frame.'}
            $failedQuote=[Func[single]]{throw 'expected nested quote failure'}
            try {$raidFood.GetMethod('ObserveQuote',$static).Invoke($null,[object[]]@($failedQuote)) | Out-Null;throw 'Nested quote did not fail.'}
            catch {if($_.Exception.ToString() -notlike '*expected nested quote failure*'){throw}}
            if($depth.GetValue($null) -ne 2){throw 'Failed nested quote leaked its frame.'}
        } finally {$depth.SetValue($null,0)}
        $applyFood=$raidFood.GetMethod('ApplyProduction',$static)
        $explainedType=$applyFood.GetParameters()[1].ParameterType.GetElementType()
        foreach($raw in @(-100,-1,0,100)){
            $number=[Activator]::CreateInstance($explainedType,[object[]]@([single]$raw,$true,$null))
            $foodArgs=[object[]]@($null,$number);$applyFood.Invoke($null,$foodArgs) | Out-Null
            if($explainedType.GetProperty('ResultNumber',$instance).GetValue($foodArgs[1],$null) -ne 0){throw 'Non-functioning or negative native food result was resurrected.'}
        }
        $fixture=$client.GetType('ReignBeta.Economy.ReignRaidFoodTestHost',$true)
        $jsonRestored=$fixture.GetMethod('RestoredJson',$static)
        $valuesRestored=$fixture.GetMethod('RecoveryValuesMatch',$static)
        foreach($fraction in @([single]0.05,[single]0.325,[single]0.4999997913837433)){
            $beforeValues=[Collections.Generic.Dictionary[string,single]]::new([StringComparer]::Ordinal)
            $beforeValues.Add('fraction',$fraction)
            $savedValues=[Newtonsoft.Json.JsonConvert]::SerializeObject($beforeValues)
            $afterValues=[Collections.Generic.Dictionary[string,single]]::new($beforeValues,[StringComparer]::Ordinal)
            if(-not $jsonRestored.Invoke($null,[object[]]@($savedValues,$afterValues)) -or -not $valuesRestored.Invoke($null,[object[]]@($beforeValues,$afterValues))){throw 'Unchanged fractional household recovery failed restoration verification.'}
            $afterValues['fraction']=[single]0.35
            if($jsonRestored.Invoke($null,[object[]]@($savedValues,$afterValues)) -or $valuesRestored.Invoke($null,[object[]]@($beforeValues,$afterValues))){throw 'Changed fractional recovery passed restoration verification.'}
            $afterValues.Clear();$afterValues.Add('Fraction',$fraction)
            if($jsonRestored.Invoke($null,[object[]]@($savedValues,$afterValues)) -or $valuesRestored.Invoke($null,[object[]]@($beforeValues,$afterValues))){throw 'Changed recovery identity passed restoration verification.'}
        }
        $fixtureScope=New-Object HarmonyLib.Harmony('com.bannerlordreign.reignbeta.raidfood.offlinebinding')
        try {
            if(Test-Path -LiteralPath $navalPath){
                $fixtureComponent=$fixture.GetMethod('FishingComponentType',$static).Invoke($null,[object[]]@($fishType))
                if($fixtureComponent -ne $naval.GetType('NavalDLC.FishingPartyComponent',$true)){throw 'Fishing fixture resolved a non-native component/factory.'}
                $fixtureCatch=$fishType.GetMethod('CatchFish',$instance,$null,[Type[]]@($fixtureComponent),$null)
                if($null -eq $fixtureCatch){throw 'Verified native fishing fixture callback is missing.'}
                $fixtureScope.Patch($fixtureCatch,$null,$null,[HarmonyLib.HarmonyMethod]::new($fixture.GetMethod('DeterministicCatchRound',$static)),$null) | Out-Null
            }
            $raidType=$campaign.GetType('TaleWorlds.CampaignSystem.MapEvents.RaidEventComponent',$true)
            $fixtureScope.Patch($raidType.GetMethod('Update',$instance),$null,$null,[HarmonyLib.HarmonyMethod]::new($fixture.GetMethod('UnopposedUpdate',$static)),$null) | Out-Null
            $fixtureScope.Patch($raidType.GetMethod('OnInitialize',$instance),[HarmonyLib.HarmonyMethod]::new($fixture.GetMethod('CaptureCreatedRaid',$static)),$null,$null,$null) | Out-Null
            $fixtureScope.Patch($campaign.GetType('TaleWorlds.CampaignSystem.GameComponents.DefaultRaidModel',$true).GetMethod('CalculateHitDamage',$instance),[HarmonyLib.HarmonyMethod]::new($fixture.GetMethod('NoExtraRaidDamage',$static)),$null,$null,$null) | Out-Null
            foreach($name in @('Run','Partial','Probe','Fishing','ScopedDefenderCount','PlannedOneFish')){[Runtime.CompilerServices.RuntimeHelpers]::PrepareMethod($fixture.GetMethod($name,$static).MethodHandle)}
        } finally {$fixtureScope.UnpatchAll($fixtureScope.Id)}
        @{schema='reign-raid-food-hook-probe-v1';boundHooks=$raidHooks.Count;exactlyOnce=$true;fixtureBinding=$true;savedNativeIdentity=$true;nativeGameplayAcceptance=$false;providerCalls=0;campaignMutations=0} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $EvidenceDirectory 'raid-food-hooks.json') -Encoding UTF8
    } finally {$raidFood.GetField('_harmony',$static).GetValue($null).UnpatchAll('com.bannerlordreign.reignbeta.raidfood')}
    # Separate owner: no campaign, no native time, no provider or roster mutation.
    $medical=$client.GetType('ReignBeta.Economy.ReignMedicalDischarge',$true)
    $medical.GetMethod('Install',$static).Invoke($null,@()) | Out-Null
    try {
        if($medical.GetField('BindingError',$static).GetValue($null)){throw 'Medical binding failed.'}
        $side=$campaign.GetType('TaleWorlds.CampaignSystem.MapEvents.MapEventSide',$true)
        $eventType=$campaign.GetType('TaleWorlds.CampaignSystem.MapEvents.MapEvent',$true)
        $join=$side.GetMethod('AddPartyInternal',$instance)
        $finish=$eventType.GetMethod('FinalizeEventAux',$instance)
        if(@([HarmonyLib.Harmony]::GetPatchInfo($join).Postfixes | Where-Object owner -eq 'com.bannerlordreign.medical-discharge.v1').Count -ne 1){throw 'Medical join hook missing'}
        $info=[HarmonyLib.Harmony]::GetPatchInfo($finish)
        foreach($category in @('Prefixes','Postfixes','Finalizers')){
            if(@($info.$category | Where-Object owner -eq 'com.bannerlordreign.medical-discharge.v1').Count -ne 1){throw 'Medical finalization hook missing'}
        }
        $upgrader=$campaign.GetType('TaleWorlds.CampaignSystem.CampaignBehaviors.PartyUpgraderCampaignBehavior',$true)
        foreach($method in @($eventType.GetMethod('LootDefeatedPartyPrisoners',$instance), $upgrader.GetMethod('MapEventEnded',$instance))){
            $info=[HarmonyLib.Harmony]::GetPatchInfo($method)
            foreach($category in @('Prefixes','Postfixes')){
                if(@($info.$category | Where-Object owner -eq 'com.bannerlordreign.medical-discharge.v1').Count -ne 1){throw "Medical reconciliation hook missing for $method"}
            }
        }
        $medicalRosterType=$campaign.GetType('TaleWorlds.CampaignSystem.Roster.TroopRoster',$true)
        $medicalCharacterType=$campaign.GetType('TaleWorlds.CampaignSystem.CharacterObject',$true)
        $medicalCharacter=[Runtime.Serialization.FormatterServices]::GetUninitializedObject($medicalCharacterType)
        $medicalRoster=$medicalRosterType.GetMethod('CreateDummyTroopRoster',$static).Invoke($null,@())
        $medicalAdd=$medicalRosterType.GetMethod('AddToCounts',$instance)
        $medicalAdd.Invoke($medicalRoster,[object[]]@($medicalCharacter,100,$false,60,1001,$true,-1)) | Out-Null
        $medicalAdd.Invoke($medicalRoster,[object[]]@($medicalCharacter,-10,$false,-10,-100,$true,-1)) | Out-Null
        $medicalElement=$medicalRosterType.GetMethod('GetElementCopyAtIndex',$instance).Invoke($medicalRoster,[object[]]@(0))
        if($medicalElement.Number -ne 90 -or $medicalElement.WoundedNumber -ne 50 -or $medicalElement.Xp -ne 901){throw 'Native medical total/wound/XP conservation failed'}
        if(($medicalElement.Number-$medicalElement.WoundedNumber) -ne 40){throw 'Medical removal changed healthy count'}
        # Re-resolve by CharacterObject after native compaction, as the shipping adapter does.
        $medicalSecond=[Runtime.Serialization.FormatterServices]::GetUninitializedObject($medicalCharacterType)
        $medicalAdd.Invoke($medicalRoster,[object[]]@($medicalSecond,8,$false,8,83,$true,-1)) | Out-Null
        $medicalAdd.Invoke($medicalRoster,[object[]]@($medicalCharacter,-90,$false,-50,-901,$true,-1)) | Out-Null
        $medicalAdd.Invoke($medicalRoster,[object[]]@($medicalSecond,-8,$false,-8,-83,$true,-1)) | Out-Null
        if($medicalRoster.GetType().GetProperty('Count',$instance).GetValue($medicalRoster) -ne 0){throw 'Medical compaction or final XP residue remained'}
        $medicalRosterConservation=$true
        $medicalBinding=$true
    } finally {[HarmonyLib.Harmony]::new('com.bannerlordreign.medical-discharge.v1').UnpatchAll('com.bannerlordreign.medical-discharge.v1')}
    $unpatched=$true;$ok=$true
} catch {$failure=$_.Exception.ToString();if($_.Exception.InnerException){$failure+=' '+$_.Exception.InnerException.ToString()}}
finally {
    if($null -ne $patches){try{$patches.GetMethod('Unapply',$static).Invoke($null,@()) | Out-Null}catch{}}
    [AppDomain]::CurrentDomain.remove_AssemblyResolve($resolver)
    [ordered]@{schema='reign-war-economy-hook-probe-v2';ok=$ok;error=$failure;boundHooks=$bound;fundingBoundHooks=if($ok){64}else{0};fundingWalletCapacityBoundary=$fundingWalletCapacityBoundary;fundingWalletLimit=$fundingWalletLimit;fundingHistoryLimit=$fundingHistoryLimit;populationObserverBinding=$populationObserverBinding;fundingOriginBinding=$fundingOriginBinding;civilianFishingBinding=$civilianFishingBinding;leaderlessWageStoreBinding=$leaderlessWageStoreBinding;transpilerPreservedNativeInstructions=$preserved;navalRoute=$navalRoute;prisonerTransferBinding=$prisonerBinding;nativeFinanceObservationSites=$financeSites;aiSpawnLocalityBinding=$aiSpawnLocality;savedAssistanceFields=$persistenceFields;persistedAidObserved=$persistedAidObserved;wagePayerBinding=$wagePayerBinding;upgradePayerBinding=$upgradePayerBinding;componentHashesObserved=$componentHashesObserved;tariffCollectionBinding=$tariffBinding;purchaseLoopNegativeControls=$purchaseLoopNegativeControls;purchaseStockGate=$purchaseStockGate;nativeFoodCensusBounded=$nativeFoodCensusBounded;nativeSubsistenceForecast=$nativeSubsistenceForecast;reliefPolicyProbe=$reliefPolicyProbe;garrisonStackRounding=$garrisonStackRounding;pressureParsingBounded=$pressureParsingBounded;nativeRewriteNegativeControls=$nativeRewriteNegativeControls;unpatched=$unpatched;providerCalls=0;campaignMutations=0;governmentHearthFlat=$governmentHearthFlat;governmentHearthFlatCases=$governmentHearthFlatCases;cohortControlBinding=$cohortBinding;nativeGameplayAcceptance=$false;medicalDischargeBinding=$medicalBinding;medicalRosterConservation=$medicalRosterConservation} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $EvidenceDirectory 'report.json') -Encoding UTF8
}
if(-not $ok){throw $failure}
