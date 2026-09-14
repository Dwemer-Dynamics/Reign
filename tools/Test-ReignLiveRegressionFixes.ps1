param(
    [string]$WorkspaceRoot = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = 'Stop'
$checks = [System.Collections.Generic.List[object]]::new()

function Add-Check([string]$Name, [bool]$Passed, [string]$Detail) {
    $checks.Add([pscustomobject]@{ name = $Name; passed = $Passed; detail = $Detail })
    if (-not $Passed) { throw "Regression check failed: $Name - $Detail" }
}

function Read-Source([string]$RelativePath) {
    Get-Content -Raw -LiteralPath (Join-Path $WorkspaceRoot $RelativePath)
}

$history = Read-Source 'ReignBeta\src\Integration\ReignWorldHistoryClient.cs'
Add-Check 'history.bounded_batch' ($history -match 'UploadBatchSize\s*=\s*1000') 'Offline history upload is capped at 1,000 records.'
Add-Check 'history.streaming_read' ($history -match 'File\.ReadLines\(path\)' -and $history -match 'ReadPendingBatch') 'The outbox reads only the next bounded batch.'
Add-Check 'history.backoff' ($history -match 'RegisterUploadFailure' -and $history -match 'Math\.Min\(120') 'Unavailable-server retries back off to 120 seconds.'
Add-Check 'history.streaming_rewrite' ($history -match 'StreamReader' -and $history -match 'StreamWriter') 'Acknowledged records are removed without retaining the full outbox.'

$endpoint = Read-Source 'ReignBeta\src\Integration\ReignServerEndpoint.cs'
Add-Check 'server.default_endpoint' ($endpoint -match '127\.0\.0\.1:5101') 'The packaged server port is the client default.'
Add-Check 'server.stale_loopback_fallback' ($endpoint -match 'ShouldTryDefaultFallback' -and $endpoint -match 'CandidateBaseUrls') 'A stale custom loopback port falls back to the packaged endpoint.'
Add-Check 'server.circuit_breaker' ($endpoint -match '_unavailableUntilUtc' -and $endpoint -match 'ReportTransportFailure') 'Repeated transport failure is circuit-broken.'

[xml]$mailXml = Read-Source 'ReignBeta\GUI\Prefabs\ReignCorrespondenceScreen.xml'
$mailRoot = $mailXml.Prefab.Window.Widget
$cancelCount = ([regex]::Matches($mailXml.OuterXml, 'Command\.Click="ExecuteCancel"')).Count
Add-Check 'mail.root_passes_events' (-not $mailRoot.HasAttribute('DoNotPassEventsToChildren')) 'The modal root no longer swallows Cancel clicks.'
Add-Check 'mail.cancel_bindings' ($cancelCount -eq 2) 'Both visible Cancel buttons bind to ExecuteCancel.'
$mailManager = Read-Source 'ReignBeta\src\UI\ReignCorrespondenceScreenManager.cs'
Add-Check 'mail.deferred_close' ($mailManager -match 'RequestClose' -and $mailManager -match '_closeRequested') 'Button-driven layer removal is deferred until the next application tick.'

$eventBehavior = Read-Source 'ReignBeta\src\Campaign\ReignSocialEventsCampaignBehavior.cs'
$eventCatalog = Read-Source 'ReignBeta\src\Events\SocialEventTemplateCatalog.cs'
$eventVm = Read-Source 'ReignBeta\src\UI\ViewModels\ReignSocialEventScreenVM.cs'
[xml]$eventXml = Read-Source 'ReignBeta\GUI\Prefabs\ReignSocialEventScreen.xml'
$eventXmlText = $eventXml.OuterXml
$artCount = @(Get-ChildItem -LiteralPath (Join-Path $WorkspaceRoot 'ReignBeta\EventArt') -Recurse -Filter *.png |
    Where-Object { $_.FullName -match '\\(feast|fair|dance)_' }).Count
Add-Check 'events.typed_tournament_template' ($eventBehavior -match 'GetTournamentCelebrationForCulture' -and $eventBehavior -match 'DisplayName = template\.DisplayName') 'Tournament celebrations keep their culture-specific feast, fair, or dance type.'
Add-Check 'events.saved_record_repair' ($eventBehavior -match 'RepairTournamentCelebrationPresentation') 'Existing generic tournament records are migrated on session launch.'
Add-Check 'events.proven_legacy_types' ($eventCatalog -match 'Villa Supper' -and $eventCatalog -match 'Artisan Salon' -and $eventCatalog -match 'Courtyard Dance') 'The proven typed celebration names are present.'
Add-Check 'events.phase_art_binding' ($eventVm -match 'BuildImageId\(_session\.Template\.TemplateId, _session\.CurrentPhase\.PhaseId\)') 'Every non-wilderness phase requests its event art.'
Add-Check 'events.gauntlet_art_widget' ($eventXmlText -match 'ReignBeta\.UI\.Widgets\.ReignEventArtWidget' -and $eventXmlText -notmatch 'AIEventsAndIntrigue\.UI\.Widgets') 'The restored layout uses ReignBeta art and scrolling widgets.'
Add-Check 'events.legacy_art_complete' ($artCount -eq 105) "Found $artCount of 105 expected culture-and-phase art PNGs."
Add-Check 'events.target_layout_contract' (($eventXmlText -match 'SuggestedWidth="540"') -and (($eventXmlText | Select-String -Pattern 'Brush="ButtonBrush2"' -AllMatches).Matches.Count -eq 4) -and $eventXmlText -match 'SPGeneral\\TownManagement\\title_divider') 'The event screen keeps the supplied attendee/main composition, shaped controls, and ornamental divider.'
Add-Check 'events.large_portrait_cards' (($eventXmlText | Select-String -Pattern 'SuggestedHeight="132"' -AllMatches).Matches.Count -ge 2 -and ($eventXmlText | Select-String -Pattern 'SuggestedWidth="112" SuggestedHeight="112"' -AllMatches).Matches.Count -ge 2) 'Active and available cards use the enlarged 132px card and 112px portrait frames.'

$eventManager = Read-Source 'ReignBeta\src\UI\ReignSocialEventScreenManager.cs'
$portraitBridge = Read-Source 'ReignBeta\src\Integration\ReignPortraitBridge.cs'
$eventRecord = Read-Source 'ReignBeta\src\Events\SocialEventRecord.cs'
$eventSession = Read-Source 'ReignBeta\src\Runtime\ReignSocialEventSession.cs'
Add-Check 'events.player_excluded_from_npc_roster' ($eventBehavior -match 'RepairAttendeeRosters' -and $eventBehavior -notmatch 'attendees\.Insert\(0, Hero\.MainHero\)' -and $eventRecord -match 'hero != Hero\.MainHero' -and $eventSession -match 'hero == Hero\.MainHero') 'The player is implicit at an event and cannot enter the saved, displayed, or active NPC roster.'
Add-Check 'events.encyclopedia_deferred_handoff' ($portraitBridge -match '_queuedEncyclopediaDelayTicks\s*=\s*2' -and $portraitBridge -match 'ProcessQueuedEncyclopediaOpen' -and $eventManager -match 'QueueOpenHeroEncyclopedia' -and $eventManager -match 'ArmAndQueueOpen') 'Name and portrait actions defer Encyclopedia navigation past the originating UI input frame.'
Add-Check 'events.encyclopedia_overlay_lifecycle' ($eventManager -match 'ReignSocialEventScreen : ScreenBase' -and $eventManager -match 'EventLayerOrder\s*=\s*250' -and $eventManager -match '_externalOverlayObserved' -and $eventManager -match 'ScreenManager\.FocusedLayer != _gauntletLayer' -and $eventManager -match 'IsEncyclopediaOpenQueued' -and $eventManager -match '_escapeSuppressionSeconds' -and $eventManager -notmatch 'SetOverlayVisible\(false\)') 'The native Encyclopedia remains above the still-visible event screen until the player closes it, then event focus returns without consuming the same Escape release.'

$portraitPrompt = Read-Source 'ReignBeta\src\AIPortraits\NanoGptClient.cs'
$portraitContext = Read-Source 'ReignBeta\src\AIPortraits\PortraitPromptContext.cs'
$portraitClient = Read-Source 'ReignBeta\src\Integration\ReignServerClient.cs'
$portraitServer = Read-Source 'ReignBetaServer\PortraitGeneration.cs'
Add-Check 'portraits.custom_identity_tokens' ($portraitPrompt -match 'BuildCustomPromptStyle\(AIEventsSettings settings, PortraitPromptContext context\)' -and $portraitPrompt -match 'ExpandIdentityTokens\(settings\.PromptSuffix, context\)' -and $portraitPrompt -match 'BuildIdentityRequirement\(context\)') 'Custom portrait prompts expand the standard identity tokens and append an explicit age requirement.'
Add-Check 'portraits.structured_identity_metadata' ($portraitContext -match 'HeroStringId' -and $portraitContext -match 'AgeYears' -and $portraitContext -match 'Gender' -and $portraitClient -match 'payload\["ageYears"\]' -and $portraitClient -match 'payload\["cultureName"\]' -and $portraitServer -match 'ExpandPortraitIdentityTokens') 'Hero id, name, age, gender, and culture cross the client/server boundary as structured metadata.'
Add-Check 'portraits.event_thumbnails_contain' ((Read-Source 'ReignBeta\src\AIPortraits\PortraitPatch.cs') -match 'AIEventsActivePortrait.*AIEventsAvailablePortrait' -and (Read-Source 'ReignBeta\src\UI\ViewModels\ReignSocialEventAttendeeVM.cs') -match 'PortraitCropImageWidth => 104f') 'Event thumbnails contain the full generated portrait inside the enlarged frame instead of center-cropping faces.'

$bootstrap = Read-Source 'ReignBeta\src\Campaign\ReignBetaDebugActions.cs'
Add-Check 'test_realm.no_minor_factions' ($bootstrap -match '!clan\.IsMinorFaction' -and $bootstrap -match 'IsInvalidTestVassal') 'The test realm excludes and repairs minor-faction vassals.'
Add-Check 'test_realm.no_mercenary_service' ($bootstrap -match '!clan\.IsUnderMercenaryService') 'The test realm excludes clans under mercenary service.'

$reputation = Read-Source 'ReignBetaServer\ReputationSystem.cs'
Add-Check 'server.reputation_schema_migration' ($reputation -match 'EnsureSqliteColumn\(connection, "reputation_evidence", "claim_id"' -and $reputation -match 'EnsureSqliteColumn\(connection, "reputation_evidence", "status"') 'Older campaign databases receive the missing claim_id and status columns before indexes are created.'
Add-Check 'reputation.seasonal_neutral_drift' ($reputation -match 'last_processed_season' -and $reputation -match 'score - \(5d \* elapsed\)' -and $reputation -match 'score \+ \(5d \* elapsed\)' -and $reputation -notmatch 'ApplyReputationDecayDay') 'Natural reputation drift runs once per season and moves either sign exactly 5 points toward zero.'

$passed = @($checks | Where-Object passed).Count
Write-Output ("Reign live regression checks passed: {0}/{1}" -f $passed, $checks.Count)
$checks | ForEach-Object { Write-Output ("PASS {0}: {1}" -f $_.name, $_.detail) }
