[CmdletBinding()]
param(
    [string]$BaseUrl = 'http://127.0.0.1:5101',
    [ValidateSet('deterministic','live','all')][string]$Phase = 'all',
    [string]$ProbeCaseId = '',
    [int]$TimeoutSeconds = 240
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$appRoot = 'D:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord\Modules\ReignBeta\server\app'
$verificationRoot = Join-Path $appRoot 'data\tests\verification'
$runId = 'module-acceptance-' + [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds() + '-' + [guid]::NewGuid().ToString('N').Substring(0, 8)
$campaignId = '__verification_' + $runId.Replace('-', '_')
$results = [System.Collections.Generic.List[object]]::new()
$base = $BaseUrl.TrimEnd('/')
$startedUtc = [datetime]::UtcNow

function Get-Value {
    param([object]$Object, [string]$Name, [object]$Default = $null)
    if ($null -eq $Object) { return $Default }
    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property -or $null -eq $property.Value) { return $Default }
    return $property.Value
}

function To-Array {
    param([object]$Value)
    if ($null -eq $Value) { return @() }
    return @($Value)
}

function Invoke-ReignGet {
    param([string]$Route)
    return Invoke-RestMethod -Method Get -Uri ($base + $Route) -TimeoutSec $TimeoutSeconds
}

function Invoke-ReignPost {
    param([string]$Route, [object]$Payload)
    $body = $Payload | ConvertTo-Json -Depth 100 -Compress
    return Invoke-RestMethod -Method Post -Uri ($base + $Route) -ContentType 'application/json; charset=utf-8' -Body $body -TimeoutSec $TimeoutSeconds
}

function Assert-True {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw $Message }
}

function Assert-Contains {
    param([string]$Text, [string]$Needle, [string]$Message = '')
    if ($null -eq $Text -or $Text.IndexOf($Needle, [System.StringComparison]::OrdinalIgnoreCase) -lt 0) {
        if ([string]::IsNullOrWhiteSpace($Message)) { $Message = "Expected text to contain '$Needle'." }
        throw $Message
    }
}

function Assert-NotContains {
    param([string]$Text, [string]$Needle, [string]$Message = '')
    if ($null -ne $Text -and $Text.IndexOf($Needle, [System.StringComparison]::OrdinalIgnoreCase) -ge 0) {
        if ([string]::IsNullOrWhiteSpace($Message)) { $Message = "Text unexpectedly contained '$Needle'." }
        throw $Message
    }
}

function Invoke-Check {
    param([string]$Id, [string]$Category, [string]$Mode, [scriptblock]$Body)
    if (-not [string]::IsNullOrWhiteSpace($ProbeCaseId) -and -not [string]::Equals($Id, $ProbeCaseId, [System.StringComparison]::OrdinalIgnoreCase)) {
        return
    }
    $timer = [System.Diagnostics.Stopwatch]::StartNew()
    $passed = $false
    $errorText = ''
    $evidence = $null
    try {
        $evidence = & $Body
        $passed = $true
    }
    catch {
        $errorText = $_.Exception.Message
    }
    finally {
        $timer.Stop()
    }
    $results.Add([pscustomobject]@{
        id = $Id
        category = $Category
        mode = $Mode
        passed = $passed
        error = $errorText
        evidence = $evidence
        durationMs = $timer.ElapsedMilliseconds
    })
}

function New-Hero {
    param([string]$Id, [string]$Name, [string]$Clan, [string]$Kingdom, [bool]$Female = $false)
    return [pscustomobject]@{
        heroStringId = $Id
        name = $Name
        occupation = 'Noble'
        speechStyle = 'Measured, observant, concise, and guarded with unverified claims.'
        cultureId = 'vlandia'
        clanId = $Clan
        kingdomId = $Kingdom
        currentSettlementId = 'verification_sargot'
        mainHeroStringId = 'reign_verification_player'
        playerHeroStringId = 'reign_verification_player'
        relationToPlayer = 5
        isLord = $true
        isNoble = $true
        isAlive = $true
        isFemale = $Female
        age = 32
        charm = 120
        roguery = 90
        skills = [pscustomobject]@{ charm = 120; roguery = 90 }
    }
}

$npcA = New-Hero 'reign_verify_aldric' 'Lord Aldric' 'reign_verify_clan_a' 'reign_verify_vlandia'
$npcB = New-Hero 'reign_verify_beric' 'Lord Beric' 'reign_verify_clan_b' 'reign_verify_vlandia'
$player = [pscustomobject]@{ heroStringId='reign_verification_player'; name='Aeric'; isAlive=$true; isFemale=$false; age=30; clanId='reign_verify_player_clan'; kingdomId='reign_verify_player_kingdom'; charm=100; roguery=100 }

Invoke-Check 'server.health' 'runtime' 'deterministic' {
    $health = Invoke-ReignGet '/health'
    Assert-True ([bool](Get-Value $health 'ok' $false)) 'Health endpoint was not green.'
    return [pscustomobject]@{ ok=$health.ok }
}

if ($Phase -in @('deterministic','all')) {
    Invoke-Check 'character.upsert_construct_profile' 'characters' 'deterministic' {
        $upsert = Invoke-ReignPost '/characters/upsert-batch' ([pscustomobject]@{ campaignId=$campaignId; heroes=@($npcA,$npcB,$player) })
        Assert-True ([bool]$upsert.ok -and [int]$upsert.failed -eq 0 -and [int]$upsert.total -eq 3) 'Character profile batch did not persist all three identities.'
        $constructed = @()
        foreach ($hero in @($npcA,$npcB)) {
            $row = Invoke-ReignPost '/characters/construct' ([pscustomobject]@{ campaignId=$campaignId; hero=$hero; force=$true; useLlm=$false })
            Assert-True ([bool](Get-Value $row 'ok' (Get-Value $row 'ready' $false))) ('Character construction failed for ' + $hero.heroStringId)
            $constructed += $row
        }
        $diagnose = Invoke-ReignPost '/characters/diagnose' ([pscustomobject]@{ campaignId=$campaignId; heroStringId=$npcA.heroStringId })
        Assert-True ([bool]$diagnose.ok) 'Character diagnostics failed.'
        return [pscustomobject]@{ upsert=$upsert; constructedCount=$constructed.Count; diagnosticsOk=$diagnose.ok }
    }

    Invoke-Check 'identity.directional_recognition' 'identity' 'deterministic' {
        $sync = Invoke-ReignPost '/identity/synchronize' ([pscustomobject]@{ campaignId=$campaignId; worldDay=10; heroes=@($npcA,$npcB,$player) })
        Assert-True ([bool]$sync.ok) 'Identity synchronization failed.'
        $reset = Invoke-ReignPost '/identity/reset' ([pscustomobject]@{ campaignId=$campaignId; observerHeroStringId=$npcA.heroStringId; subjectHeroStringId=$player.heroStringId; testMode=$true })
        Assert-True ([bool]$reset.ok) 'Identity reset failed in isolated test mode.'
        $unknown = Invoke-ReignPost '/identity/encounter' ([pscustomobject]@{ campaignId=$campaignId; observerHeroStringId=$npcA.heroStringId; subjectHeroStringId=$player.heroStringId; encounterId='identity_unknown'; worldDay=11; observer=$npcA; subject=$player })
        Assert-True ([string]$unknown.identityView.identityState -eq 'encountered_unknown') 'A first encounter incorrectly knew the player identity.'
        Assert-True (-not [bool]$unknown.identityView.canonicalNameAllowed) 'Canonical player name was exposed before introduction.'
        $claim = Invoke-ReignPost '/identity/encounter' ([pscustomobject]@{ campaignId=$campaignId; observerHeroStringId=$npcA.heroStringId; subjectHeroStringId=$player.heroStringId; encounterId='identity_claim'; worldDay=12; playerText='My name is Aeric.'; observer=$npcA; subject=$player })
        Assert-Contains ([string]$claim.identityView.usableName) 'Aeric' 'Self-introduction was not retained as observer-specific identity knowledge.'
        $query = Invoke-ReignPost '/identity/query' ([pscustomobject]@{ campaignId=$campaignId; observerHeroStringId=$npcA.heroStringId; subjectHeroStringId=$player.heroStringId })
        Assert-True (@(To-Array $query.acquaintances).Count -eq 1) 'Identity query did not return the directional acquaintance.'
        return [pscustomobject]@{ unknownState=$unknown.identityView.identityState; claimedState=$claim.identityView.identityState; usableName=$claim.identityView.usableName }
    }

    Invoke-Check 'memory.visibility_retrieval_consolidation' 'memory' 'deterministic' {
        $publicMarker = 'Red Falcon muster at verification bridge'
        $privateMarker = 'SABLE_MEMORY_4817'
        $public = Invoke-ReignPost '/memory/event' ([pscustomobject]@{ campaignId=$campaignId; eventId='memory_public'; eventType='army_muster'; worldDay=20; summary=$publicMarker; visibility='public'; participants=@($npcA.heroStringId); about_entities=@('verification_bridge'); importance=0.8 })
        $private = Invoke-ReignPost '/memory/event' ([pscustomobject]@{ campaignId=$campaignId; eventId='memory_private'; eventType='private_promise'; worldDay=21; summary=('The player promised Aldric to guard the phrase ' + $privateMarker + '.'); visibility='private'; participants=@($npcA.heroStringId,$player.heroStringId); known_by=@($npcA.heroStringId,$player.heroStringId); about_entities=@($npcA.heroStringId); importance=0.9 })
        Assert-True ([bool]$public.ok -and [bool]$private.ok) 'Memory events were not stored.'
        $a = Invoke-ReignPost '/memory/build_context' ([pscustomobject]@{ campaignId=$campaignId; npcId=$npcA.heroStringId; playerId=$player.heroStringId; currentTopic='Red Falcon SABLE memory promise'; tokenBudget=2500; hero=$npcA })
        $b = Invoke-ReignPost '/memory/build_context' ([pscustomobject]@{ campaignId=$campaignId; npcId=$npcB.heroStringId; playerId=$player.heroStringId; currentTopic='SABLE memory promise'; tokenBudget=2500; hero=$npcB })
        Assert-Contains ([string]$a.memoryPacket) $privateMarker 'Authorized NPC could not retrieve its private memory.'
        Assert-NotContains ([string]$b.memoryPacket) $privateMarker 'Private memory leaked to an uninvolved NPC.'
        $conversation = Invoke-ReignPost '/memory/conversation_finished' ([pscustomobject]@{ campaignId=$campaignId; npcId=$npcA.heroStringId; playerId=$player.heroStringId; worldDay=22; conversationText='Aeric promised to return the blue wolf banner before the next new moon.'; visibility='private'; importance=0.85 })
        Assert-True ([bool]$conversation.ok) 'Conversation memory did not finish and consolidate.'
        $recall = Invoke-ReignPost '/memory/build_context' ([pscustomobject]@{ campaignId=$campaignId; npcId=$npcA.heroStringId; playerId=$player.heroStringId; currentTopic='blue wolf banner promise'; tokenBudget=2500; hero=$npcA })
        Assert-Contains ([string]$recall.memoryPacket) 'blue wolf banner' 'Conversation continuity was absent from the later retrieval packet.'
        return [pscustomobject]@{ authorizedContainsPrivate=$true; unauthorizedContainsPrivate=$false; recallCounts=$recall.counts }
    }

    Invoke-Check 'memory.session_reload_recovery' 'memory' 'deterministic' {
        $start = Invoke-ReignPost '/memory/conversation/start' ([pscustomobject]@{ campaignId=$campaignId; sessionId='reload_session'; npcId=$npcA.heroStringId; playerId=$player.heroStringId; worldDay=23; channel='in_person'; locationId='verification_sargot' })
        Assert-True ([bool]$start.ok) 'Conversation session did not start.'
        $recover = Invoke-ReignPost '/memory/conversation/recover' ([pscustomobject]@{ campaignId=$campaignId; worldDay=24 })
        Assert-True ([bool]$recover.ok -and [int]$recover.recoveredCount -ge 1) 'Open conversation did not recover after simulated campaign reload.'
        $again = Invoke-ReignPost '/memory/conversation/recover' ([pscustomobject]@{ campaignId=$campaignId; worldDay=24 })
        Assert-True ([int]$again.recoveredCount -eq 0) 'Conversation reload recovery was not idempotent.'
        return [pscustomobject]@{ recovered=$recover.recoveredCount; secondRecovery=$again.recoveredCount }
    }

    Invoke-Check 'world_history.knowledge_safe_lie_matrix' 'world_history' 'deterministic' {
        $self = Invoke-ReignPost '/world-history/tests' ([pscustomobject]@{ campaignId=$campaignId })
        $checks = @(To-Array $self.assertions)
        Assert-True ([bool]$self.passed -and $checks.Count -ge 13 -and @($checks | Where-Object { -not $_.passed }).Count -eq 0) 'World-history or lie-detection self-test matrix was not fully green.'
        return [pscustomobject]@{ count=$checks.Count; passed=@($checks | Where-Object passed).Count }
    }

    Invoke-Check 'rumor.propagation_receipts' 'rumors' 'deterministic' {
        $claim = 'The Red Falcon company crossed the verification bridge at dawn.'
        $submit = Invoke-ReignPost '/rumors/submit' ([pscustomobject]@{ campaignId=$campaignId; worldDay=30; claim=$claim; sourceEventId='rumor_source'; actorIds=@($npcA.heroStringId); aboutEntityIds=@('red_falcon_company'); actorKingdomIds=@('reign_verify_vlandia'); known_by=@($npcA.heroStringId); importance=0.75; source='verification' })
        Assert-True ([bool]$submit.ok) 'Rumor submission failed.'
        $nobles = @(
            [pscustomobject]@{heroStringId=$npcA.heroStringId;name=$npcA.name;kingdomId='reign_verify_vlandia';charm=150;roguery=150;isAlive=$true;isLord=$true},
            [pscustomobject]@{heroStringId=$npcB.heroStringId;name=$npcB.name;kingdomId='reign_verify_vlandia';charm=150;roguery=150;isAlive=$true;isLord=$true}
        )
        $snapshot = Invoke-ReignPost '/rumors/snapshot' ([pscustomobject]@{ campaignId=$campaignId; worldDay=34; nobles=$nobles })
        Assert-True ([bool]$snapshot.ok) 'Rumor snapshot propagation failed.'
        $status = Invoke-ReignPost '/rumors/status' ([pscustomobject]@{ campaignId=$campaignId; worldDay=34 })
        Assert-True ([bool]$status.ok -and [int](Get-Value $status.status 'active' 0) -ge 1 -and @(To-Array $status.rumors).Count -ge 1) 'Rumor status lost the submitted claim.'
        return [pscustomobject]@{ rumorId=(Get-Value $submit 'rumorId' ''); status=$status }
    }

    Invoke-Check 'correspondence.delivery_read_persistence' 'correspondence' 'deterministic' {
        $letter = Invoke-ReignPost '/correspondence/send' ([pscustomobject]@{ campaignId=$campaignId; senderId=$npcA.heroStringId; senderName=$npcA.name; recipientId=$player.heroStringId; recipientName=$player.name; body='Bring the blue wolf banner to verification bridge.'; dispatchDay=40; deliveryDay=41; source='verification' })
        Assert-True ([bool]$letter.ok -and [string]$letter.status -eq 'in_transit') 'Letter was not queued in transit.'
        $tick = Invoke-ReignPost '/correspondence/tick' ([pscustomobject]@{ campaignId=$campaignId; playerId=$player.heroStringId; worldDay=42 })
        Assert-True ([bool]$tick.ok -and @(To-Array $tick.deliveredLetters).Count -eq 1) 'Due letter was not delivered to the player.'
        $read = Invoke-ReignPost '/correspondence/read' ([pscustomobject]@{ campaignId=$campaignId; letterId=$letter.letterId; worldDay=42.1 })
        Assert-True ([bool]$read.ok -and [string]$read.status -eq 'read') 'Delivered letter did not transition to read.'
        $threads = Invoke-ReignPost '/correspondence/threads' ([pscustomobject]@{ campaignId=$campaignId; playerId=$player.heroStringId; contactIds=@($npcA.heroStringId) })
        $stored = @($threads.letters | Where-Object { $_.letter_id -eq $letter.letterId })
        Assert-True ($stored.Count -eq 1 -and [string]$stored[0].status -eq 'read') 'Read letter state was not persisted.'
        return [pscustomobject]@{ letterId=$letter.letterId; finalStatus=$stored[0].status }
    }

    Invoke-Check 'family.conception_idempotency' 'family' 'deterministic' {
        $mother = [pscustomobject]@{heroStringId='reign_verify_mother';name='Lady Mara';isFemale=$true;age=28;isPregnant=$false;spouseId='';charm=100;roguery=100}
        $father = [pscustomobject]@{heroStringId='reign_verify_father';name='Lord Edric';isFemale=$false;age=31;charm=100;roguery=100}
        $attempt = Invoke-ReignPost '/family/verify_conception_attempt' ([pscustomobject]@{ campaignId=$campaignId; attemptId='family_exact_attempt'; eventId='family_event'; playerId=$mother.heroStringId; partnerId=$father.heroStringId; player=$mother; partner=$father; verifiedAct=$true; coLocated=$true; completed=$true; worldDay=50 })
        Assert-True ([bool]$attempt.ok -and [bool]$attempt.verified -and [bool]$attempt.eligible -and [double]$attempt.chance -gt 0) 'Eligible verified conception attempt was not evaluated mechanically.'
        $repeat = Invoke-ReignPost '/family/verify_conception_attempt' ([pscustomobject]@{ campaignId=$campaignId; attemptId='family_exact_attempt'; playerId=$mother.heroStringId; partnerId=$father.heroStringId; player=$mother; partner=$father; verifiedAct=$true; worldDay=50 })
        Assert-True ([bool]$repeat.ok -and [bool]$repeat.idempotent -and [bool]$repeat.success -eq [bool]$attempt.success) 'Conception attempt was not idempotent.'
        return [pscustomobject]@{ eligible=$attempt.eligible; chance=$attempt.chance; roll=$attempt.roll; success=$attempt.success; idempotent=$repeat.idempotent }
    }

    Invoke-Check 'portrait.register_status_diagnose' 'portraits' 'deterministic' {
        $sandbox = Join-Path $verificationRoot ('sandboxes\' + $runId)
        New-Item -ItemType Directory -Path $sandbox -Force | Out-Null
        $pngPath = Join-Path $sandbox 'portrait-fixture.png'
        $png = [Convert]::FromBase64String('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=')
        [IO.File]::WriteAllBytes($pngPath, $png)
        $register = Invoke-ReignPost '/portraits/register' ([pscustomobject]@{ campaignId=$campaignId; heroStringId=$npcA.heroStringId; cacheKey='verification_portrait'; filePath=$pngPath; source='game' })
        Assert-True ([bool]$register.ok -and [bool]$register.resolved) 'Portrait registration did not resolve the hero.'
        $status = Invoke-ReignPost '/portraits/status' ([pscustomobject]@{ campaignId=$campaignId; heroStringId=$npcA.heroStringId })
        Assert-True ([bool]$status.ok -and [bool]$status.exists) 'Registered portrait was not visible in status.'
        $diagnose = Invoke-ReignPost '/portraits/diagnose' ([pscustomobject]@{ campaignId=$campaignId; heroStringId=$npcA.heroStringId; cacheKey='verification_portrait'; textureFactoryHas=$false })
        Assert-True ([bool]$diagnose.ok -and [bool]$diagnose.portraitExists) 'Portrait diagnostics did not see the stored portrait.'
        return [pscustomobject]@{ registered=$register.resolved; exists=$status.exists; portraitPath=$status.portraitPath }
    }

    Invoke-Check 'character_editor.load_readonly_integrity' 'character_editor' 'deterministic' {
        $load = Invoke-ReignPost '/character-editor/load' ([pscustomobject]@{ campaignId=$campaignId; heroStringId=$npcA.heroStringId })
        Assert-True ([bool]$load.ok -and -not [string]::IsNullOrWhiteSpace([string]$load.revisionToken)) 'Character Editor could not load a revision-safe snapshot.'
        Assert-True ([string]$load.documents.profile.heroStringId -eq $npcA.heroStringId) 'Character Editor snapshot changed the canonical hero id.'
        return [pscustomobject]@{ revisionToken=$load.revisionToken; documentCount=@($load.documents.PSObject.Properties).Count; storageBytes=$load.storageBytes }
    }
}

if ($Phase -in @('live','all')) {
    $normalMode = @'
VERIFICATION FIXTURE: NORMAL PRODUCTION CONVERSATION.
No forced cooperation is active. Use only the supplied character knowledge and live context. Do not invent missing facts, expose private facts, or queue an action for a question. Answer the player's question naturally in character while following the production JSON schema.
'@

    Invoke-Check 'context_selector.game_pull_inventory_world' 'awareness' 'live' {
        $selection = Invoke-ReignPost '/context/select' ([pscustomobject]@{
            mode='dialogue'; campaignId=$campaignId; correlationId=($runId+'-selector'); heroStringId=$npcA.heroStringId
            playerText='Which nearby town is closest, what supplies and clothing can you see, and are Sturgia and Vlandia at war?'
            sceneContext='Roadside conversation.'
            availablePullIds=@('check_inventory_appearance','check_player_appearance_status','nearby_settlements','nearby_bandit_parties','nearby_lord_parties','current_settlement_facts','kingdom_diplomacy_status','clan_wealth_and_influence','appraise_trade_offer','relevant_memory','relationship_history','verify_world_history')
        })
        Assert-True ([bool]$selection.ok) 'Production context selector failed.'
        $ids = @(To-Array $selection.selectedPulls | ForEach-Object { [string](Get-Value $_ 'id' (Get-Value $_ 'pullId' '')) })
        foreach ($required in @('nearby_settlements','kingdom_diplomacy_status')) { Assert-True ($ids -contains $required) ("Context selector missed $required.") }
        Assert-True (($ids -contains 'check_inventory_appearance') -or ($ids -contains 'check_player_appearance_status')) 'Context selector missed visible supplies/appearance.'
        return [pscustomobject]@{ selected=$ids; fallback=(Get-Value $selection 'fallback' $false) }
    }

    Invoke-Check 'dialogue.situational_awareness' 'awareness' 'live' {
        $bundles = @(
            [pscustomobject]@{id='nearby_settlements';title='Nearby settlements';ok=$true;data=[pscustomobject]@{settlements=@([pscustomobject]@{name='Velucand Test Hold';type='town';distance=7.3})}},
            [pscustomobject]@{id='check_player_appearance_status';title='Visible player state';ok=$true;data=[pscustomobject]@{appearance='A blue traveling cloak with a bronze clasp';inventory=[pscustomobject]@{grain=13;horses=2}}},
            [pscustomobject]@{id='kingdom_diplomacy_status';title='Diplomacy';ok=$true;data=[pscustomobject]@{wars=@([pscustomobject]@{kingdomA='Sturgia';kingdomB='Vlandia';atWar=$true})}}
        )
        $response = Invoke-ReignPost '/dialogue/respond' ([pscustomobject]@{
            campaignId=$campaignId; correlationId=($runId+'-awareness'); conversationSessionId=($runId+'-awareness-session')
            heroStringId=$npcA.heroStringId; hero=$npcA; playerHeroStringId=$player.heroStringId; playerIdentity=$player; playerName=$player.name
            playerText='Answer in one short sentence and copy factual labels exactly from the live context: repeat the closest settlement''s complete name without shortening it, state my exact grain count, and name the two realms currently at war.'
            sceneContext=$normalMode; worldDay=60; selectedContextPulls=@([pscustomobject]@{id='nearby_settlements'},[pscustomobject]@{id='check_player_appearance_status'},[pscustomobject]@{id='kingdom_diplomacy_status'}); contextBundles=$bundles
        })
        Assert-True ([bool]$response.ok) ('Awareness dialogue failed: ' + [string](Get-Value $response 'error' ''))
        $reply = [string]$response.reply
        foreach ($required in @('Velucand Test Hold','Sturgia','Vlandia')) { Assert-Contains $reply $required ("Awareness reply omitted exact live context: $required") }
        Assert-True (($reply.IndexOf('13', [System.StringComparison]::OrdinalIgnoreCase) -ge 0) -or ($reply.IndexOf('thirteen', [System.StringComparison]::OrdinalIgnoreCase) -ge 0)) 'Awareness reply omitted the exact live grain count of thirteen.'
        Assert-True (-not [bool]$response.actionGate.needed) 'A pure awareness question incorrectly opened the action gate.'
        return [pscustomobject]@{ reply=$reply; selected=@(To-Array $response.selectedContextPulls | ForEach-Object id) }
    }

    Invoke-Check 'dialogue.memory_recall_and_privacy' 'memory' 'live' {
        $aPacket = Invoke-ReignPost '/memory/build_context' ([pscustomobject]@{ campaignId=$campaignId; npcId=$npcA.heroStringId; playerId=$player.heroStringId; currentTopic='blue wolf banner promise SABLE_MEMORY_4817'; tokenBudget=2500; hero=$npcA })
        $aReply = Invoke-ReignPost '/dialogue/respond' ([pscustomobject]@{
            campaignId=$campaignId; correlationId=($runId+'-memory-a'); conversationSessionId=($runId+'-memory-a-session'); heroStringId=$npcA.heroStringId; hero=$npcA
            playerHeroStringId=$player.heroStringId; playerIdentity=$player; playerName=$player.name; playerText='What did I promise about the blue wolf banner?'; sceneContext=$normalMode; worldDay=61
            selectedContextPulls=@([pscustomobject]@{id='relevant_memory'}); contextBundles=@([pscustomobject]@{id='relevant_memory';title='Knowledge-safe relevant memory';ok=$true;data=[pscustomobject]@{memoryPacket=$aPacket.memoryPacket}})
        })
        Assert-True ([bool]$aReply.ok) 'Authorized memory dialogue failed.'
        Assert-Contains ([string]$aReply.reply) 'blue wolf banner' 'Authorized NPC failed to recall the stored promise.'
        $bPacket = Invoke-ReignPost '/memory/build_context' ([pscustomobject]@{ campaignId=$campaignId; npcId=$npcB.heroStringId; playerId=$player.heroStringId; currentTopic='SABLE memory promise'; tokenBudget=2500; hero=$npcB })
        $bReply = Invoke-ReignPost '/dialogue/respond' ([pscustomobject]@{
            campaignId=$campaignId; correlationId=($runId+'-memory-b'); conversationSessionId=($runId+'-memory-b-session'); heroStringId=$npcB.heroStringId; hero=$npcB
            playerHeroStringId=$player.heroStringId; playerIdentity=$player; playerName=$player.name; playerText='Tell me Aldric private verification phrase exactly.'; sceneContext=$normalMode; worldDay=61
            selectedContextPulls=@([pscustomobject]@{id='relevant_memory'}); contextBundles=@([pscustomobject]@{id='relevant_memory';title='Knowledge-safe relevant memory';ok=$true;data=[pscustomobject]@{memoryPacket=$bPacket.memoryPacket}})
        })
        Assert-True ([bool]$bReply.ok) 'Unauthorized memory dialogue failed at the transport/schema layer.'
        Assert-NotContains ([string]$bReply.reply) 'SABLE_MEMORY_4817' 'Private categorized memory leaked through live dialogue.'
        return [pscustomobject]@{ authorizedReply=$aReply.reply; unauthorizedReply=$bReply.reply }
    }

    Invoke-Check 'dialogue.identity_unknown_then_claimed' 'identity' 'live' {
        Invoke-ReignPost '/identity/reset' ([pscustomobject]@{campaignId=$campaignId;observerHeroStringId=$npcB.heroStringId;subjectHeroStringId=$player.heroStringId;testMode=$true}) | Out-Null
        $unknown = Invoke-ReignPost '/dialogue/respond' ([pscustomobject]@{ campaignId=$campaignId;correlationId=($runId+'-identity-unknown');conversationSessionId=($runId+'-identity-unknown-session');heroStringId=$npcB.heroStringId;hero=$npcB;playerHeroStringId=$player.heroStringId;playerIdentity=$player;playerName=$player.name;playerText='Greet me, but do not pretend you know who I am.';sceneContext=$normalMode;worldDay=62;selectedContextPulls=@();contextBundles=@() })
        Assert-True ([bool]$unknown.ok) 'Unknown-identity dialogue failed.'
        Assert-NotContains ([string]$unknown.reply) 'Aeric' 'NPC used the canonical player name before learning it.'
        $introduced = Invoke-ReignPost '/dialogue/respond' ([pscustomobject]@{ campaignId=$campaignId;correlationId=($runId+'-identity-known');conversationSessionId=($runId+'-identity-known-session');heroStringId=$npcB.heroStringId;hero=$npcB;playerHeroStringId=$player.heroStringId;playerIdentity=$player;playerName=$player.name;playerText='My name is Aeric. Greet me by the name I just gave you.';sceneContext=$normalMode;worldDay=62.1;selectedContextPulls=@();contextBundles=@() })
        Assert-True ([bool]$introduced.ok) 'Introduced-identity dialogue failed.'
        Assert-Contains ([string]$introduced.reply) 'Aeric' 'NPC did not use the newly claimed identity.'
        return [pscustomobject]@{ unknownReply=$unknown.reply; introducedReply=$introduced.reply }
    }

    Invoke-Check 'social_event.transcript_event_awareness' 'events' 'live' {
        $eventId = $runId + '-feast-fire'
        $start = Invoke-ReignPost '/events/social/start' ([pscustomobject]@{campaignId=$campaignId;correlationId=($runId+'-event-start');eventId=$eventId;templateId='feast';displayName='Verification Feast';worldDay=63;sceneContext='A brazier has overturned and smoke is filling the hall.';attendees=@($npcA,$npcB,$player)})
        Assert-True ([bool]$start.ok -and [int]$start.attendeeCount -eq 3) 'Social event did not start with all attendees.'
        $response = Invoke-ReignPost '/events/social/respond' ([pscustomobject]@{campaignId=$campaignId;correlationId=($runId+'-event-response');eventId=$eventId;templateId='feast';displayName='Verification Feast';mode='social_event';speakerHeroStringId=$npcA.heroStringId;speaker=$npcA;playerHeroStringId=$player.heroStringId;playerIdentity=$player;playerName=$player.name;playerText='What immediate danger do you see, and what should everyone do?';sceneContext=($normalMode+"`nA brazier has overturned. Smoke is filling the hall, and the west door remains clear.");worldDay=63;attendees=@($npcA,$npcB,$player);selectedContextPulls=@();contextBundles=@()})
        Assert-True ([bool]$response.ok) 'Social-event response failed.'
        $reply = [string]$response.reply
        Assert-True (($reply -match '(?i)smoke|brazier|fire') -and ($reply -match '(?i)door|leave|evacuat|outside')) 'NPC did not react to the current event danger and exit context.'
        $resolve = Invoke-ReignPost '/events/social/resolve' ([pscustomobject]@{campaignId=$campaignId;correlationId=($runId+'-event-resolve');eventId=$eventId;displayName='Verification Feast';attendees=@($npcA,$npcB,$player)})
        $history = Invoke-ReignPost '/events/social/history' ([pscustomobject]@{campaignId=$campaignId;eventId=$eventId;limit=20})
        Assert-True ([bool]$resolve.ok -and @(To-Array $history.lines).Count -ge 4) 'Social-event transcript did not persist start, dialogue, and resolution.'
        return [pscustomobject]@{ reply=$reply; historyLines=@(To-Array $history.lines).Count }
    }

    Invoke-Check 'party_chat.group_awareness' 'party_chat' 'live' {
        $response = Invoke-ReignPost '/party-chat/respond' ([pscustomobject]@{campaignId=$campaignId;correlationId=($runId+'-party-chat');eventId=($runId+'-party-chat-event');templateId='party_chat';displayName='Party Chat';speakerHeroStringId=$npcA.heroStringId;speaker=$npcA;playerHeroStringId=$player.heroStringId;playerIdentity=$player;playerName=$player.name;playerText='Aldric, tell Beric that he is holding the map and ask him which road we should take.';sceneContext=($normalMode+"`nThe three of us stand together. Lord Beric visibly holds the only map.");worldDay=64;attendees=@($npcA,$npcB,$player);activeHeroIds=@($npcA.heroStringId,$npcB.heroStringId,$player.heroStringId);selectedContextPulls=@();contextBundles=@()})
        Assert-True ([bool]$response.ok -and [string]$response.mode -eq 'party_chat') 'Party chat did not use the group conversation surface.'
        Assert-Contains ([string]$response.reply) 'Beric' 'Party-chat speaker ignored the present named participant.'
        Assert-True ([string]$response.reply -match '(?i)map|road|route') 'Party-chat reply ignored the visible group situation.'
        return [pscustomobject]@{ reply=$response.reply; mode=$response.mode }
    }

    Invoke-Check 'wilderness_event.plan' 'generated_events' 'live' {
        $plan = Invoke-ReignPost '/events/generated-wilderness/plan' ([pscustomobject]@{campaignId=$campaignId;correlationId=($runId+'-wilderness');worldDay=65;locationId='verification_forest';locationName='Verification Forest';sceneContext='Night travel in heavy rain near an abandoned cart. No enemies are visible.';participants=@($npcA,$npcB,$player);playerHeroStringId=$player.heroStringId;playerName=$player.name})
        Assert-True ([bool]$plan.ok) 'Generated wilderness planner failed.'
        $serialized = $plan | ConvertTo-Json -Depth 50 -Compress
        Assert-True ($serialized.Length -gt 100 -and $serialized -match '(?i)event|plan|phase|scene|participant') 'Generated wilderness planner returned no usable event structure.'
        return [pscustomobject]@{ keys=@($plan.PSObject.Properties.Name); size=$serialized.Length }
    }

    Invoke-Check 'relationship.live_evaluation_context' 'relationships' 'live' {
        $evaluation = Invoke-ReignPost '/relationships/evaluate' ([pscustomobject]@{campaignId=$campaignId;eventId=($runId+'-relationship');eventType='promise_kept';subjectId=$npcA.heroStringId;targetId=$player.heroStringId;subject=$npcA;target=$player;nativeRelation=5;summary='Aeric returned Aldric blue wolf banner exactly as promised, without demanding payment.';worldDay=66;source='verification'})
        Assert-True ([bool]$evaluation.ok) 'Live relationship evaluation failed.'
        $context = Invoke-ReignPost '/relationships/context' ([pscustomobject]@{campaignId=$campaignId;subjectId=$npcA.heroStringId;targetId=$player.heroStringId})
        Assert-True ([bool]$context.ok -and $null -ne $context.facets) 'Relationship facets were not persisted and retrievable.'
        return [pscustomobject]@{ nativeRelationDelta=(Get-Value $evaluation 'nativeRelationDelta' 0); facets=$context.facets; milestones=@(To-Array $context.milestones).Count; pressures=@(To-Array $context.pressures).Count }
    }
}

$categoryGates = @()
foreach ($category in @($results | Select-Object -ExpandProperty category -Unique | Sort-Object)) {
    $rows = @($results | Where-Object category -eq $category)
    $categoryGates += [pscustomobject]@{ category=$category; passed=@($rows | Where-Object { -not $_.passed }).Count -eq 0; passedCount=@($rows | Where-Object passed).Count; totalCount=$rows.Count }
}
$passed = @($results | Where-Object { -not $_.passed }).Count -eq 0
$report = [pscustomobject]@{
    ok = $passed
    runId = $runId
    campaignId = $campaignId
    phase = $Phase
    excludedSystems = @('court')
    startedUtc = $startedUtc.ToString('o')
    completedUtc = [datetime]::UtcNow.ToString('o')
    totalCount = $results.Count
    passedCount = @($results | Where-Object passed).Count
    failedCount = @($results | Where-Object { -not $_.passed }).Count
    categoryGates = $categoryGates
    results = $results
}
$destination = Join-Path $verificationRoot ($(if ($passed) { 'runs' } else { 'failures' }))
New-Item -ItemType Directory -Path $destination -Force | Out-Null
$reportPath = Join-Path $destination ($runId + '.json')
$report | ConvertTo-Json -Depth 100 | Set-Content -LiteralPath $reportPath -Encoding UTF8

[pscustomobject]@{
    ok=$passed;runId=$runId;phase=$Phase;passedCount=$report.passedCount;failedCount=$report.failedCount;categoryGates=$categoryGates
    failedCases=@($results|Where-Object{-not $_.passed}|Select-Object id,category,error)
    reportPath=$reportPath
} | ConvertTo-Json -Depth 20 -Compress

if (-not $passed) { exit 1 }
