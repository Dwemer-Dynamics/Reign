param(
    [string]$WorkspaceRoot = (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)),
    [string]$BannerlordRoot = 'D:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord'
)

$ErrorActionPreference = 'Stop'
$checks = New-Object System.Collections.Generic.List[object]

function Add-Check {
    param([string]$Name, [bool]$Passed, [string]$Detail)
    $checks.Add([pscustomobject]@{ Name = $Name; Passed = $Passed; Detail = $Detail })
}

$settingsPath = Join-Path $WorkspaceRoot 'ReignBeta\src\Settings\ReignBetaSettings.cs'
$policyPath = Join-Path $WorkspaceRoot 'ReignBeta\src\Family\ReignPregnancyRestrictionPolicy.cs'
$behaviorPath = Join-Path $WorkspaceRoot 'ReignBeta\src\Campaign\ReignFamilyCampaignBehavior.cs'
$patchPath = Join-Path $WorkspaceRoot 'ReignBeta\src\Family\ReignPregnancyPatches.cs'
$editorPath = Join-Path $WorkspaceRoot 'ReignBeta\src\Campaign\ReignCharacterEditorCampaignBehavior.cs'
$dllPath = Join-Path $WorkspaceRoot 'ReignBeta\bin\Win64_Shipping_Client\ReignBeta.dll'

$settings = Get-Content -LiteralPath $settingsPath -Raw
$policy = Get-Content -LiteralPath $policyPath -Raw
$behavior = Get-Content -LiteralPath $behaviorPath -Raw
$patch = Get-Content -LiteralPath $patchPath -Raw
$editor = Get-Content -LiteralPath $editorPath -Raw

Add-Check 'mcm.default_on' ($settings -match 'private bool _pregnancyCombatRestrictionsEnabled = true;') 'The MCM backing field defaults to true.'
Add-Check 'mcm.live_toggle' ($settings -match 'Restrict Pregnant NPCs From Combat".*RequireRestart = false' -and $settings -match 'OnPregnancyRestrictionSettingsChanged') 'The setting is live and notifies the campaign behavior.'
Add-Check 'policy.master_and_toggle' ($policy -match 'settings.Enabled && settings.PregnancyCombatRestrictionsEnabled') 'The Reign master switch and pregnancy toggle both gate enforcement.'
Add-Check 'policy.player_excluded' ($policy -match 'hero != Hero.MainHero' -and $policy -match 'hero.IsPregnant') 'Only pregnant NPCs are restricted.'

$expectedActions = @(
    'StrategyRecruitAndRecover','StrategyFormArmy','StrategyAttackSettlement','StrategyCaptureSettlement',
    'RegularFollowOnMap','RegularStopFollowing','RegularGoToSettlement','RegularPatrolAroundSettlement',
    'RegularWaitNearSettlement','RegularRaidVillage','RegularBesiegeSettlement','RegularCreateParty',
    'RegularAttackParty','RegularAttackPlayerParty','RegularSurrenderToPlayer','RegularLeavePlayerAlone',
    'RegularKillCharacter','RegularDuelPlayer'
) | Sort-Object
$actualActions = [regex]::Matches($policy, 'case ReignWorldActionType\.([A-Za-z0-9_]+):') |
    ForEach-Object { $_.Groups[1].Value } | Sort-Object
Add-Check 'policy.action_matrix' (($expectedActions -join '|') -eq ($actualActions -join '|')) ('Restricted actions: ' + ($actualActions -join ', '))

$eventNames = @(
    'HourlyTickEvent','OnGameLoadedEvent','OnChildConceivedEvent','OnGivenBirthEvent',
    'OnHeroJoinedPartyEvent','MobilePartyCreated','OnPartyLeaderChangedEvent','MapEventEnded',
    'CanHeroLeadPartyEvent'
)
$missingEvents = @($eventNames | Where-Object { $behavior -notmatch [regex]::Escape($_) })
Add-Check 'behavior.event_coverage' ($missingEvents.Count -eq 0) ($(if ($missingEvents.Count) { 'Missing: ' + ($missingEvents -join ', ') } else { 'All transition and reconciliation events are registered.' }))
Add-Check 'behavior.prisoner_safe' ($behavior -match 'hero.IsPrisoner' -and $behavior -match 'PartyBelongedToAsPrisoner') 'Prisoners are not teleported.'
Add-Check 'behavior.combat_deferred' ($behavior -match 'party.MapEvent != null' -and $behavior -match 'party.SiegeEvent != null' -and $behavior -match 'map_event_ended') 'Active battles and sieges defer roster mutation and retry.'
Add-Check 'behavior.safe_fortification' ($behavior -match 'FindNearestFortificationToMobileParty' -and $behavior -match 'IsSafeFortification' -and $behavior -match 'IsAtWarAgainstFaction') 'Withdrawal selects a safe non-hostile fortification.'
Add-Check 'behavior.besieged_town_exception' ($behavior -match 'IsInsideBesiegedTown' -and $behavior -match 'settlement.IsTown' -and $behavior -match 'settlement.IsUnderSiege') 'Pregnant NPCs already inside a besieged town remain for its normal defense and capture outcome.'
Add-Check 'behavior.leader_resolution' ($behavior -match 'FindReplacementLeader' -and $behavior -match 'party.IsDisbanding = true') 'Leaders hand off or enter Bannerlord''s disband lifecycle.'
Add-Check 'behavior.no_forced_return' ($behavior -match 'without creating a new party') 'Birth restores ordinary eligibility without forced remobilization.'
Add-Check 'managed_and_editor_paths' ($behavior -match 'NotifyPregnancyStateChanged\(mother\)' -and $editor -match 'NotifyPregnancyStateChanged\(hero\)') 'Managed and editor-applied pregnancies reconcile immediately.'
Add-Check 'patch.native_spawn_guard' ($patch -match 'HeroSpawnCampaignBehavior' -and $patch -match 'SpawnLordPartyPrefix') 'The native fallback lord-party spawn is guarded.'
Add-Check 'patch.leader_assignment_guard' ($patch -match 'MobileParty.ChangePartyLeader' -and $patch -match 'ChangePartyLeaderPrefix') 'Direct and delayed party-leader assignments are guarded.'
Add-Check 'build.client_dll' (Test-Path -LiteralPath $dllPath) $dllPath

$campaignAssemblyPath = Join-Path $BannerlordRoot 'bin\Win64_Shipping_Client\TaleWorlds.CampaignSystem.dll'
$targetDetails = New-Object System.Collections.Generic.List[string]
$targetsFound = $false
if (Test-Path -LiteralPath $campaignAssemblyPath) {
    $bin = Split-Path -Parent $campaignAssemblyPath
    Get-ChildItem -LiteralPath $bin -Filter 'TaleWorlds.*.dll' | ForEach-Object {
        try { [void][Reflection.Assembly]::LoadFrom($_.FullName) } catch {}
    }
    $assembly = [Reflection.Assembly]::LoadFrom($campaignAssemblyPath)
    $pregnancyType = $assembly.GetType('TaleWorlds.CampaignSystem.CampaignBehaviors.PregnancyCampaignBehavior', $false)
    $spawnType = $assembly.GetType('TaleWorlds.CampaignSystem.CampaignBehaviors.HeroSpawnCampaignBehavior', $false)
    $mobilePartyType = $assembly.GetType('TaleWorlds.CampaignSystem.Party.MobileParty', $false)
    $flags = [Reflection.BindingFlags]'Instance,Static,Public,NonPublic'
    $refresh = if ($pregnancyType) { $pregnancyType.GetMethod('RefreshSpouseVisit', $flags) } else { $null }
    $deliver = if ($pregnancyType) { $pregnancyType.GetMethod('CheckOffspringsToDeliver', $flags) } else { $null }
    $spawn = if ($spawnType) { $spawnType.GetMethod('SpawnLordParty', $flags) } else { $null }
    $changeLeader = if ($mobilePartyType) { $mobilePartyType.GetMethod('ChangePartyLeader', $flags) } else { $null }
    $targetsFound = $null -ne $refresh -and $null -ne $deliver -and $null -ne $spawn -and $null -ne $changeLeader
    if ($refresh) { $targetDetails.Add('RefreshSpouseVisit') }
    if ($deliver) { $targetDetails.Add('CheckOffspringsToDeliver') }
    if ($spawn) { $targetDetails.Add('SpawnLordParty') }
    if ($changeLeader) { $targetDetails.Add('ChangePartyLeader') }
}
Add-Check 'runtime.harmony_targets' $targetsFound ('Resolved: ' + ($targetDetails -join ', '))

foreach ($check in $checks) {
    $status = if ($check.Passed) { 'PASS' } else { 'FAIL' }
    Write-Host ("[{0}] {1} - {2}" -f $status, $check.Name, $check.Detail)
}

$failed = @($checks | Where-Object { -not $_.Passed })
Write-Host ("Pregnancy restriction verification: {0}/{1} passed." -f ($checks.Count - $failed.Count), $checks.Count)
if ($failed.Count -gt 0) {
    exit 1
}
