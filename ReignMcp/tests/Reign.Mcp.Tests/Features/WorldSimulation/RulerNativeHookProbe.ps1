param([Parameter(Mandatory=$true)][string]$ClientAssembly,[Parameter(Mandatory=$true)][string]$BannerlordPath,[Parameter(Mandatory=$true)][string]$EvidenceDirectory)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$probeRoot=[IO.Path]::GetFullPath($EvidenceDirectory)
$working=Join-Path $probeRoot 'sandbox\bin\Win64_Shipping_Client'
[IO.Directory]::CreateDirectory($working) | Out-Null
Set-Location -LiteralPath $working
[Environment]::CurrentDirectory=$working
$gameBin=Join-Path $BannerlordPath 'bin\Win64_Shipping_Client'
$script:rulerRoots=@($gameBin,[IO.Path]::GetDirectoryName($ClientAssembly),(Join-Path $BannerlordPath 'Modules\Bannerlord.Harmony\bin\Win64_Shipping_Client'),(Join-Path $BannerlordPath 'Modules\SandBox\bin\Win64_Shipping_Client'))
$resolver=[ResolveEventHandler]{param($sender,$eventArgs)
    $name=([Reflection.AssemblyName]::new($eventArgs.Name)).Name+'.dll'
    foreach($root in $script:rulerRoots){$path=Join-Path $root $name;if([IO.File]::Exists($path)){return [Reflection.Assembly]::LoadFrom($path)}}
    return $null
}
[AppDomain]::CurrentDomain.add_AssemblyResolve($resolver)
$ok=$false;$failure='';$hooks=0;$type=$null;$partyCountChecks=0;$prisonerFixtureChecks=0;$judgmentDrawChecks=0;$imprisonmentCustodyChecks=0
$nativeMathAssertions=0;$nativeMathProviderCalls=-1;$nativeMathCampaignMutations=-1;$nativeMathProfileRejected=$false
$campaignOptionsObserved=$false
try {
    $client=[Reflection.Assembly]::LoadFrom([IO.Path]::GetFullPath($ClientAssembly))
    $flags=[Reflection.BindingFlags]'Static,Public,NonPublic'
    $type=$client.GetType('ReignBeta.World.ReignRulerNativePatches',$true)
    $type.GetMethod('Apply',$flags).Invoke($null,@()) | Out-Null
    $hooks=[int]$type.GetProperty('HookCount',$flags).GetValue($null,$null)
    $failure=[string]$type.GetProperty('Error',$flags).GetValue($null,$null)
    if($hooks -lt 20 -or $failure.Length -gt 0){throw ('Native ruler hook binding failed: '+$failure)}
    $native=[Reflection.Assembly]::LoadFrom((Join-Path $gameBin 'TaleWorlds.CampaignSystem.dll'))
    $campaign=$native.GetType('TaleWorlds.CampaignSystem.Campaign',$true)
    if($null -eq $campaign.GetProperty('PlayerDefaultFaction',[Reflection.BindingFlags]'Instance,Public,NonPublic').GetSetMethod($true)){throw 'Player exile faction setter unavailable.'}
    $behaviorType=$client.GetType('ReignBeta.Campaign.ReignRulerCampaignBehavior',$true)
    $optionJson=$behaviorType.GetMethod('ObserveCampaignOptions',$flags).Invoke($null,@()).ToString()
    [IO.File]::WriteAllText((Join-Path $probeRoot 'campaign-options-observation.json'),$optionJson,[Text.UTF8Encoding]::new($false))
    $options=$optionJson | ConvertFrom-Json
    if($options.schema -ne 'reign-ruler-campaign-options-observation-v1' -or -not $options.readOnly -or @($options.rows).Count -ne 9 -or @($options.rows.name | Select-Object -Unique).Count -ne 9){throw 'Compiled native option observation lost its bounded read-only contract.'}
    foreach($row in $options.rows){
        if($row.available -isnot [bool]){throw 'Native option observation must explicitly report unavailable getters.'}
        if($row.available -and $null -eq $row.PSObject.Properties['value']){throw 'Available native option lacks an observed value.'}
    }
    $campaignOptionsObserved=$true
    $drawInput=$behaviorType.GetMethod('ValidateJudgmentDrawInput',$flags)
    foreach($drawCase in @(@(0,'prisoner','victor'),@(2000,'prisoner','victor'),@(3999,'prisoner','victor'))){
        $drawInput.Invoke($null,[object[]]$drawCase) | Out-Null
        $judgmentDrawChecks++
    }
    foreach($drawCase in @(@(-1,'prisoner','victor'),@(4000,'prisoner','victor'),@(1,'prisoner','prisoner'),@(1,'prisoner',''))){
        $rejected=$false
        try{$drawInput.Invoke($null,[object[]]$drawCase) | Out-Null}catch{if($_.Exception.InnerException -is [ArgumentException]){$rejected=$true}else{throw}}
        if(-not $rejected){throw 'Compiled judgment draw accepted an unsafe identity or draw.'}
        $judgmentDrawChecks++
    }
    $takeDraw=$behaviorType.GetMethod('TakeJudgmentDrawControl',$flags)
    $jobjectType=$takeDraw.ReturnType
    $dictionaryType=[System.Collections.Generic.Dictionary[string,object]].GetGenericTypeDefinition().MakeGenericType([Type[]]@([string],$jobjectType))
    $drawControls=[Activator]::CreateInstance($dictionaryType)
    $drawControl=[Activator]::CreateInstance($jobjectType)
    $drawControls.Add('prisoner',$drawControl);$drawControls.Add('unrelated',[Activator]::CreateInstance($jobjectType))
    if(-not [Object]::ReferenceEquals($drawControl,$takeDraw.Invoke($null,[object[]]@($drawControls,'prisoner')))){throw 'Judgment draw did not consume its exact control.'}
    if($null -ne $takeDraw.Invoke($null,[object[]]@($drawControls,'prisoner')) -or -not $drawControls.ContainsKey('unrelated')){throw 'Judgment draw replayed or removed another target.'}
    $judgmentDrawChecks+=2
    $drawEligible=$behaviorType.GetMethod('JudgmentDrawEligible',$flags)
    $judgmentType=$drawEligible.GetParameters()[0].ParameterType
    $stageType=$judgmentType.GetProperty('Stage').PropertyType
    $judgment=[Activator]::CreateInstance($judgmentType)
    $judgment.PrisonerId='prisoner';$judgment.VictorId='victor';$judgment.Stage=[Enum]::Parse($stageType,'AwaitingDecision')
    foreach($drawCase in @(@('prisoner','victor',$true,$true,$true,0.0),@('prisoner','victor',$true,$true,$true,0.99975))){
        if(-not $drawEligible.Invoke($null,[object[]](@($judgment)+$drawCase))){throw 'Compiled judgment draw rejected an eligible AI decision.'}
        $judgmentDrawChecks++
    }
    foreach($drawCase in @(@('wrong','victor',$true,$true,$true,0.5),@('prisoner','wrong',$true,$true,$true,0.5),@('prisoner','victor',$false,$true,$true,0.5),@('prisoner','victor',$true,$false,$true,0.5),@('prisoner','victor',$true,$true,$false,0.5),@('prisoner','victor',$true,$true,$true,1.0),@('prisoner','victor',$true,$true,$true,[double]::NaN))){
        if($drawEligible.Invoke($null,[object[]](@($judgment)+$drawCase))){throw 'Compiled judgment draw crossed an identity, enrollment, custody, AI or draw fence.'}
        $judgmentDrawChecks++
    }
    $judgment.Draw=0.5
    if($drawEligible.Invoke($null,[object[]]@($judgment,'prisoner','victor',$true,$true,$true,0.25))){throw 'Saved judgment draw could be replaced.'}
    $judgment.Draw=-1;$judgment.Stage=[Enum]::Parse($stageType,'Sentenced')
    if($drawEligible.Invoke($null,[object[]]@($judgment,'prisoner','victor',$true,$true,$true,0.25))){throw 'Sentenced judgment accepted a new draw.'}
    $judgmentDrawChecks+=2
    $inputMethod=$behaviorType.GetMethod('ValidatePrisonerFixtureInput',$flags)
    foreach($rulerFixtureInput in @(@('prison_guard',1,'guard','prisoner'),@('ransom_offer',1,'prisoner','payer'),@('ransom_offer',4000,'prisoner','payer'),@('barter_release',1000,'prisoner','payer'),@('barter_transfer',1000,'prisoner','payer'))){
        $inputMethod.Invoke($null,[object[]]$rulerFixtureInput) | Out-Null
        $prisonerFixtureChecks++
    }
    foreach($rulerFixtureInput in @(@('prison_guard',0,'guard','prisoner'),@('prison_guard',2,'guard','prisoner'),@('ransom_offer',0,'prisoner','payer'),@('barter_release',4001,'prisoner','payer'),@('barter_transfer',1000,'prisoner',''),@('ransom_offer',1000,'prisoner','prisoner'))){
        $rejected=$false
        try{$inputMethod.Invoke($null,[object[]]$rulerFixtureInput) | Out-Null}catch{if($_.Exception.InnerException -is [ArgumentException]){$rejected=$true}else{throw}}
        if(-not $rejected){throw 'Compiled prisoner fixture accepted unsafe roles or amount.'}
        $prisonerFixtureChecks++
    }
    $instanceFlags=[Reflection.BindingFlags]'Instance,Public,NonPublic'
    $heroType=$native.GetType('TaleWorlds.CampaignSystem.Hero',$true)
    $partyBaseType=$native.GetType('TaleWorlds.CampaignSystem.Party.PartyBase',$true)
    $ransomType=$native.GetType('TaleWorlds.CampaignSystem.CampaignBehaviors.RansomOfferCampaignBehavior',$true)
    if($null -eq $ransomType.GetMethod('AcceptRansomOffer',$instanceFlags,$null,[Type[]]@([int]),$null)){throw 'Native paid ransom handler signature changed.'}
    $prisonerFixtureChecks++
    foreach($field in @('_currentRansomHero','_currentRansomPayer')){
        if($ransomType.GetField($field,$instanceFlags).FieldType -ne $heroType){throw 'Native ransom context field changed.'}
        $prisonerFixtureChecks++
    }
    $barterType=$native.GetType('TaleWorlds.CampaignSystem.BarterSystem.Barterables.Barterable',$true)
    $barterManagerType=$native.GetType('TaleWorlds.CampaignSystem.BarterSystem.BarterManager',$true)
    $barterListType=[Collections.Generic.List``1].MakeGenericType([Type[]]@($barterType))
    if($null -eq $barterManagerType.GetMethod('ApplyBarterOffer',$instanceFlags,$null,[Type[]]@($heroType,$heroType,$barterListType),$null)){throw 'Native paid barter handler signature changed.'}
    $prisonerFixtureChecks++
    $goldType=$native.GetType('TaleWorlds.CampaignSystem.BarterSystem.Barterables.GoldBarterable',$true)
    $freeType=$native.GetType('TaleWorlds.CampaignSystem.BarterSystem.Barterables.SetPrisonerFreeBarterable',$true)
    $transferType=$native.GetType('TaleWorlds.CampaignSystem.BarterSystem.Barterables.TransferPrisonerBarterable',$true)
    if($null -eq $goldType.GetConstructor([Type[]]@($heroType,$heroType,$partyBaseType,$partyBaseType,[int]))){throw 'Native gold barter constructor changed.'}
    if($null -eq $freeType.GetConstructor([Type[]]@($heroType,$heroType,$partyBaseType,$heroType))){throw 'Native prisoner release constructor changed.'}
    if($null -eq $transferType.GetConstructor([Type[]]@($heroType,$heroType,$partyBaseType,$heroType,$partyBaseType))){throw 'Native prisoner transfer constructor changed.'}
    $prisonerFixtureChecks+=3
    foreach($barter in @($freeType,$transferType)){
        if($barter.GetField('_prisonerCharacter',$instanceFlags).FieldType -ne $heroType){throw 'Native barter prisoner guard field changed.'}
        $prisonerFixtureChecks++
    }
    foreach($method in @('PreparePrisonGuard','RunRansomOfferFixture','RunBarterFixture','ObservePrisonGuard','ObservePrisonerPayment')){
        [Runtime.CompilerServices.RuntimeHelpers]::PrepareMethod($behaviorType.GetMethod($method,$flags).MethodHandle)
        $prisonerFixtureChecks++
    }
    $behavior=[Activator]::CreateInstance($behaviorType)
    # Exercise the actual release hook with native Hero and release-reason types.
    # No Campaign or native captivity action is created by these isolated checks.
    $releasePrefix=$type.GetMethod('ReleasePrefix',$flags)
    $detailType=$releasePrefix.GetParameters()[1].ParameterType
    $verdictType=$judgmentType.GetProperty('Verdict').PropertyType
    $instanceProperty=$behaviorType.GetProperty('Instance',$flags)
    $priorInstance=$instanceProperty.GetValue($null,$null)
    $applyingField=$behaviorType.GetField('ApplyingNative',$instanceFlags)
    $custodyState=$behaviorType.GetField('State',$instanceFlags).GetValue($behavior)
    $custodyHero=[Runtime.Serialization.FormatterServices]::GetUninitializedObject($heroType)
    $custodyHero.StringId='custody_prisoner'
    $custodyJudgment=[Activator]::CreateInstance($judgmentType)
    $custodyJudgment.PrisonerId=$custodyHero.StringId
    $custodyJudgment.CivilWar=$true
    $custodyJudgment.Stage=[Enum]::Parse($stageType,'Applying')
    $custodyJudgment.Verdict=[Enum]::Parse($verdictType,'Imprisonment')
    $custodyState.Judgments.Add($custodyJudgment)
    $custodyResults=[Collections.Generic.List[object]]::new()
    function Assert-CustodyRelease([object]$hero,[object]$detail,[bool]$expected,[string]$label) {
        $actual=[bool]$releasePrefix.Invoke($null,[object[]]@($hero,$detail))
        $custodyResults.Add([ordered]@{case=$label;detail=$detail.ToString();expected=$expected;actual=$actual})
        if($actual -ne $expected){throw ('Compiled custody release guard failed: '+$label)}
        $script:imprisonmentCustodyChecks++
    }
    try {
        $instanceProperty.SetValue($null,$behavior,$null)
        $applyingField.SetValue($behavior,$true)
        $peaceDetail=[Enum]::Parse($detailType,'ReleasedAfterPeace')
        foreach($detail in [Enum]::GetValues($detailType)){
            Assert-CustodyRelease $custodyHero $detail ($detail -ne $peaceDetail) ('applying_imprisonment_'+$detail)
        }
        foreach($verdict in [Enum]::GetValues($verdictType)){
            $custodyJudgment.Verdict=$verdict
            Assert-CustodyRelease $custodyHero $peaceDetail ($verdict.ToString() -ne 'Imprisonment') ('verdict_'+$verdict)
        }
        $custodyJudgment.Verdict=[Enum]::Parse($verdictType,'Imprisonment')
        foreach($stage in [Enum]::GetValues($stageType)){
            $custodyJudgment.Stage=$stage
            Assert-CustodyRelease $custodyHero $peaceDetail ($stage.ToString() -ne 'Applying') ('stage_'+$stage)
        }
        $custodyJudgment.Stage=[Enum]::Parse($stageType,'Applying')
        $custodyJudgment.CivilWar=$false
        Assert-CustodyRelease $custodyHero $peaceDetail $true 'ordinary_war_unaffected'
        $custodyJudgment.CivilWar=$true;$custodyJudgment.RebelVictory=$true
        Assert-CustodyRelease $custodyHero $peaceDetail $true 'rebel_victory_unaffected'
        $custodyJudgment.RebelVictory=$false;$custodyJudgment.DefeatedLord=$true
        Assert-CustodyRelease $custodyHero $peaceDetail $true 'individual_lord_unaffected'
        $custodyJudgment.DefeatedLord=$false;$custodyJudgment.PrisonerId='unrelated_prisoner'
        Assert-CustodyRelease $custodyHero $peaceDetail $true 'unrelated_prisoner_unaffected'
        $custodyJudgment.PrisonerId=$custodyHero.StringId
        Assert-CustodyRelease $null $peaceDetail $true 'missing_prisoner_unaffected'
        $applyingField.SetValue($behavior,$false)
        Assert-CustodyRelease $custodyHero $peaceDetail $true 'outside_native_resolution_unaffected'
        $custodyJudgment.Stage=[Enum]::Parse($stageType,'Completed')
        foreach($detail in [Enum]::GetValues($detailType)){
            Assert-CustodyRelease $custodyHero $detail $true ('completed_imprisonment_'+$detail)
        }
        $custodyJudgment.Stage=[Enum]::Parse($stageType,'Sentenced')
        $custodyJudgment.Verdict=[Enum]::Parse($verdictType,'CleanExecution')
        Assert-CustodyRelease $custodyHero $peaceDetail $false 'pending_execution_still_blocks_peace_release'
        foreach($detailName in @('ReleasedAfterEscape','ReleasedAfterBattle','Death')){
            Assert-CustodyRelease $custodyHero ([Enum]::Parse($detailType,$detailName)) $true ('pending_execution_allows_'+$detailName)
        }
    } finally {
        $instanceProperty.SetValue($null,$priorInstance,$null)
        $applyingField.SetValue($behavior,$false)
        $custodyState.Judgments.Clear()
        $custodyResults | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $probeRoot 'custody-release-guards.json') -Encoding UTF8
    }
    $healthyMethod=$behaviorType.GetMethod('ArmyHealthy',$flags)
    if([int]$healthyMethod.Invoke($null,[object[]]@($null)) -ne 0){throw 'A partyless ruler must have zero present army troops.'}
    $partyCountChecks++
    $partyType=$native.GetType('TaleWorlds.CampaignSystem.Party.MobileParty',$true)
    $independent=[Runtime.Serialization.FormatterServices]::GetUninitializedObject($partyType)
    if([int]$healthyMethod.Invoke($null,[object[]]@($independent)) -ne 0){throw 'A party outside an army must have zero present army troops.'}
    $partyCountChecks++
    $statusMethod=$behaviorType.GetMethod('RuntimeStatus',[Reflection.BindingFlags]'Instance,NonPublic')
    $boundStatus=$statusMethod.Invoke($behavior,@()).ToString() | ConvertFrom-Json
    if(-not $boundStatus.available -or $boundStatus.nativeHooks -ne $hooks){throw 'Runtime health did not advertise the bound ruler hooks.'}
    $type.GetMethod('Unapply',$flags).Invoke($null,@()) | Out-Null
    $unboundStatus=$statusMethod.Invoke($behavior,@()).ToString() | ConvertFrom-Json
    if($unboundStatus.available -or $unboundStatus.nativeHooks -ne 0){throw 'Runtime health falsely accepted missing ruler hooks.'}
    # Invoke the actual compiled diagnostic, so its success gate and all native
    # ExplainedNumber assertions are checked even before in-game acceptance.
    $mathType=$client.GetType('ReignBeta.ClanAccords.ReignClanAccordNativeTests',$true)
    $mathMethod=$mathType.GetMethod('Run',$flags)
    $mathJson=$mathMethod.Invoke($null,[object[]]@('isolated_ruler_native_math','contracts')).ToString()
    [IO.File]::WriteAllText((Join-Path $probeRoot 'native-math-report.json'),$mathJson,[Text.UTF8Encoding]::new($false))
    $mathReport=$mathJson | ConvertFrom-Json
    $nativeMathAssertions=[int]$mathReport.assertionCount
    $nativeMathProviderCalls=[int]$mathReport.providerCalls
    $nativeMathCampaignMutations=[int]$mathReport.campaignMutations
    if(-not $mathReport.ok -or $nativeMathAssertions -ne 177 -or $mathReport.expectedAssertionCount -ne 177 -or @($mathReport.assertions).Count -ne 177 -or @($mathReport.assertions | Where-Object {-not $_.passed}).Count -ne 0 -or $nativeMathProviderCalls -ne 0 -or $nativeMathCampaignMutations -ne 0){throw ('Compiled native math contracts failed: '+(Join-Path $probeRoot 'native-math-report.json'))}
    $rejectedProfile=$mathMethod.Invoke($null,[object[]]@('isolated_ruler_native_math_reject','apply')).ToString() | ConvertFrom-Json
    $nativeMathProfileRejected=-not $rejectedProfile.ok -and $rejectedProfile.error.Contains('contracts')
    if(-not $nativeMathProfileRejected){throw 'Compiled native math diagnostic accepted a mutation profile.'}
    $ok=$true
} catch { $failure=$_.Exception.ToString() }
finally {
    if($null -ne $type){$type.GetMethod('Unapply',[Reflection.BindingFlags]'Static,Public,NonPublic').Invoke($null,@()) | Out-Null}
    [AppDomain]::CurrentDomain.remove_AssemblyResolve($resolver)
    [ordered]@{schema='reign-ruler-native-binding-v1';ok=$ok;hookCount=$hooks;partyCountChecks=$partyCountChecks;prisonerFixtureChecks=$prisonerFixtureChecks;judgmentDrawChecks=$judgmentDrawChecks;imprisonmentCustodyChecks=$imprisonmentCustodyChecks;nativeMathAssertions=$nativeMathAssertions;nativeMathProviderCalls=$nativeMathProviderCalls;nativeMathCampaignMutations=$nativeMathCampaignMutations;nativeMathProfileRejected=$nativeMathProfileRejected;campaignOptionsObserved=$campaignOptionsObserved;error=$failure;clientAssembly=$ClientAssembly;nativeGameStarted=$false;campaignMutated=$false} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $probeRoot 'report.json') -Encoding UTF8
}
if(-not $ok){Write-Error $failure;exit 1}
