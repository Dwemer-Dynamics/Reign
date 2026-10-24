param(
    [Parameter(Mandatory = $true)][string]$ClientAssembly,
    [Parameter(Mandatory = $true)][string]$BannerlordPath,
    [Parameter(Mandatory = $true)][string]$EvidenceDirectory
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# Canonical test child only: compile the actual managed methods without invoking
# an engine callback, provider, campaign, listener or game process. The mailbox
# editor fixture invokes managed input on constructor-free, isolated text state;
# it never renders, polls real keyboard input, attaches a screen or calls native code.
$probeRoot = [IO.Path]::GetFullPath($EvidenceDirectory)
$working = Join-Path $probeRoot 'sandbox\bin\Win64_Shipping_Client'
[IO.Directory]::CreateDirectory($working) | Out-Null
Set-Location -LiteralPath $working
[Environment]::CurrentDirectory = $working
$clientPath = [IO.Path]::GetFullPath($ClientAssembly)
$gameBin = Join-Path $BannerlordPath 'bin\Win64_Shipping_Client'
# Resolve the game's vector implementation first. Never let a copied NuGet
# facade establish a different identity for native method signatures.
$script:assemblyRoots = @($gameBin, [IO.Path]::GetDirectoryName($clientPath),
    (Join-Path $BannerlordPath 'Modules\SandBox\bin\Win64_Shipping_Client'),
    (Join-Path $BannerlordPath 'Modules\Bannerlord.Harmony\bin\Win64_Shipping_Client'))
$resolver = [ResolveEventHandler] {
    param($sender, $eventArgs)
    $name = ([Reflection.AssemblyName]::new($eventArgs.Name)).Name + '.dll'
    foreach ($root in $script:assemblyRoots) {
        $path = Join-Path $root $name
        if ([IO.File]::Exists($path)) { return [Reflection.Assembly]::LoadFrom($path) }
    }
    return $null
}
[AppDomain]::CurrentDomain.add_AssemblyResolve($resolver)
$prepared = [Collections.Generic.List[string]]::new()
$nativeVectorName = $null
$fieldsCompatible = $false
$failure = $null
$selectionChecks = 0
$custodySnapshotChecks = 0
$nativeObservationChecks = 0
$bribeObservationChecks = 0
$nativeFaultChecks = 0
$pendingOfferChecks = 0
$nativeSafetyChecks = 0
$allianceDurationChecks = 0
$goldObservationChecks = 0
$tributeObservationChecks = 0
$romanceObservationChecks = 0
$rebellionPreparationLifecycleChecks = 0
$rebellionSavedLoadChecks = 0
$mailboxEditorChecks = 0
$mailboxPortraitChecks = 0
$heroKnowledgeChecks = 0
try {
    $native = [Reflection.Assembly]::LoadFrom((Join-Path $gameBin 'TaleWorlds.GauntletUI.dll'))
    $widget = $native.GetType('TaleWorlds.GauntletUI.BaseTypes.Widget', $true)
    $vector = $widget.GetProperty('Size').PropertyType
    $nativeVectorName = $vector.Assembly.GetName().Name
    if ($nativeVectorName -ne 'System.Numerics.Vectors') { throw 'Unexpected native Vector2 assembly identity.' }
    $twoDimension = [Reflection.Assembly]::LoadFrom((Join-Path $gameBin 'TaleWorlds.TwoDimension.dll'))
    $richText = $twoDimension.GetType('TaleWorlds.TwoDimension.RichText', $true)
    $update = $richText.GetMethods() | Where-Object { $_.Name -eq 'Update' -and $_.GetParameters().Length -eq 7 }
    if ($update.GetParameters()[2].ParameterType -ne $vector) { throw 'Native Size and RichText.Update vector identities disagree.' }
    $client = [Reflection.Assembly]::LoadFrom($clientPath)
    $reference = @($client.GetReferencedAssemblies() | Where-Object Name -eq 'System.Numerics.Vectors')
    if ($reference.Count -ne 1 -or $reference[0].FullName -ne $vector.Assembly.FullName) {
        throw 'Compiled client does not reference the exact installed native vector assembly.'
    }
    $flags = [Reflection.BindingFlags]'Instance,Static,NonPublic,Public,DeclaredOnly'
    # Invoke only the compiled managed lifecycle helper. These are isolated saved
    # records: no campaign, government, native clan mutation or live save exists.
    $rebellionType = $client.GetType('ReignBeta.Campaign.ReignRebellionCampaignBehavior', $true)
    $closePlots = $rebellionType.GetMethod('CloseResolvedPreparationPlots', $flags)
    $plotType = $client.GetType('ReignBeta.World.ReignRebellionPlotRecord', $true)
    $movementType = $client.GetType('ReignBeta.World.ReignRebellionMovementRecord', $true)
    foreach ($case in @(
        @('applied', 'active_rebellion', 'civil_war', $true, 'movement', 'hero', 'clan', 10, $true),
        @('resolved_stage', 'active_rebellion', 'resolved', $false, 'movement', 'hero', 'clan', 10, $true),
        @('active_war', 'active_rebellion', 'civil_war', $false, 'movement', 'hero', 'clan', 10, $false),
        @('wrong_movement', 'active_rebellion', 'resolved', $true, 'other', 'hero', 'clan', 10, $false),
        @('wrong_hero', 'active_rebellion', 'resolved', $true, 'movement', 'other', 'clan', 10, $false),
        @('wrong_clan', 'active_rebellion', 'resolved', $true, 'movement', 'hero', 'other', 10, $false),
        @('already_terminal', 'resolved', 'resolved', $true, 'movement', 'hero', 'clan', 10, $false),
        @('planning', 'planning', 'resolved', $true, 'movement', 'hero', 'clan', 10, $false),
        @('missing_link', 'active_rebellion', 'resolved', $true, '', 'hero', 'clan', 10, $false),
        @('newer_plot_date', 'active_rebellion', 'resolved', $true, 'movement', 'hero', 'clan', 50, $true))) {
        $plot = [Activator]::CreateInstance($plotType)
        $plot.Status = $case[1]; $plot.MovementId = $case[4]
        $plot.PlayerHeroStringId = $case[5]; $plot.PlayerClanStringId = $case[6]
        $plot.UpdatedDay = [single]$case[7]; $plot.Resolution = 'original_history'
        $movement = [Activator]::CreateInstance($movementType)
        $movement.MovementId = 'movement'; $movement.Stage = $case[2]
        $movement.ResolutionApplied = $case[3]; $movement.LeaderHeroStringId = 'hero'
        $movement.LeaderClanStringId = 'clan'; $movement.Resolution = 'recognized_independence'
        $movement.UpdatedDay = [single]20; $movement.CooldownUntilDay = [single]83
        $plots = [Array]::CreateInstance($plotType, 1); $plots.SetValue($plot, 0)
        $movements = [Array]::CreateInstance($movementType, 1); $movements.SetValue($movement, 0)
        $count = $closePlots.Invoke($null, @($plots, $movements))
        if ($count -ne [int]$case[8]) { throw "Wrong resolved preparation count: $($case[0])" }
        if ($case[8]) {
            if ($plot.Status -ne 'resolved' -or $plot.Resolution -ne 'recognized_independence' -or
                $plot.UpdatedDay -ne [Math]::Max([single]$case[7], [single]20)) {
                throw "Resolved preparation history/date lost: $($case[0])"
            }
        } elseif ($plot.Status -ne $case[1] -or $plot.Resolution -ne 'original_history' -or $plot.UpdatedDay -ne $case[7]) {
            throw "Unrelated or active preparation mutated: $($case[0])"
        }
        if ($movement.Stage -ne $case[2] -or $movement.ResolutionApplied -ne $case[3] -or $movement.CooldownUntilDay -ne 83) {
            throw 'Preparation cleanup altered the war or its cooldown.'
        }
        if ($closePlots.Invoke($null, @($plots, $movements)) -ne 0) { throw 'Preparation cleanup is not idempotent.' }
        $rebellionPreparationLifecycleChecks++
    }
    # Exercise the actual save-load entry point, which sealed campaigns still
    # execute even though they skip initial world preparation. No native state.
    Add-Type -TypeDefinition @'
public sealed class ReignRebellionSavedRecordDataStore : TaleWorlds.CampaignSystem.IDataStore
{
    public bool IsLoading { get { return true; } }
    public bool IsSaving { get { return false; } }
    public bool SyncData<T>(string key, ref T value) { return true; }
}
'@ -ReferencedAssemblies (Join-Path $gameBin 'TaleWorlds.CampaignSystem.dll')
    foreach ($resolved in @($true, $false)) {
        $behavior = [Runtime.Serialization.FormatterServices]::GetUninitializedObject($rebellionType)
        $plot = [Activator]::CreateInstance($plotType)
        $plot.Status = 'active_rebellion'; $plot.MovementId = 'loaded_movement'
        $plot.PlayerHeroStringId = 'loaded_hero'; $plot.PlayerClanStringId = 'loaded_clan'
        $movement = [Activator]::CreateInstance($movementType)
        $movement.MovementId = 'loaded_movement'; $movement.Stage = 'civil_war'
        $movement.LeaderHeroStringId = 'loaded_hero'; $movement.LeaderClanStringId = 'loaded_clan'
        $movement.ResolutionApplied = $resolved; $movement.Resolution = 'recognized_independence'
        $movement.CooldownUntilDay = [single]83
        $plots = [Activator]::CreateInstance([Collections.Generic.List[object]].GetGenericTypeDefinition().MakeGenericType($plotType))
        $plots.Add($plot)
        $movements = [Activator]::CreateInstance([Collections.Generic.List[object]].GetGenericTypeDefinition().MakeGenericType($movementType))
        $movements.Add($movement)
        $rebellionType.GetField('_plots', $flags).SetValue($behavior, $plots)
        $rebellionType.GetField('_movements', $flags).SetValue($behavior, $movements)
        $store = [ReignRebellionSavedRecordDataStore]::new()
        $rebellionType.GetMethod('SyncData', $flags).Invoke($behavior, @($store)) | Out-Null
        $expected = if ($resolved) { 'resolved' } else { 'active_rebellion' }
        if ($plot.Status -ne $expected -or $plots.Count -ne 1 -or $movements.Count -ne 1 -or
            $movement.CooldownUntilDay -ne 83 -or $movement.ResolutionApplied -ne $resolved) {
            throw 'Saved preparation cleanup missed a resolved war or altered an active one.'
        }
        $rebellionType.GetMethod('SyncData', $flags).Invoke($behavior, @($store)) | Out-Null
        if ($plot.Status -ne $expected) { throw 'Saved preparation cleanup is not idempotent.' }
        $rebellionSavedLoadChecks++
    }
    foreach ($typeName in @('ReignBeta.UI.Widgets.ReignActionEditableTextWidget',
        'ReignBeta.UI.Widgets.ReignPortraitWidget', 'ReignBeta.Campaign.ReignHeroKnowledge',
        'ReignBeta.UI.Calibration.ReignUiCalibrationSession', 'ReignBeta.UI.Calibration.ReignUiCalibrationSession+WidgetGeometry')) {
        $type = $client.GetType($typeName, $true)
        $members = @($type.GetMethods($flags) | Where-Object { !$_.ContainsGenericParameters }) + @($type.GetConstructors($flags))
        foreach ($method in $members) {
            # Unlike field checks or a build, this resolves every native call in
            # OnLateUpdate, even when the empty composer would return immediately.
            [Runtime.CompilerServices.RuntimeHelpers]::PrepareMethod($method.MethodHandle)
            $prepared.Add($typeName + '.' + $method.Name)
        }
    }
    $editor = $client.GetType('ReignBeta.UI.Widgets.ReignActionEditableTextWidget', $true)
    $fieldsCompatible = $editor.GetProperty('IsNativeTextCompatible').GetValue($null, $null)
    if (!$fieldsCompatible) { throw 'Native visible text fields are incompatible.' }
    # Execute the actual managed key path, with the game's empty input manager
    # and text implementation. No platform, renderer, event loop or live UI exists.
    $contextType = $native.GetType('TaleWorlds.GauntletUI.UIContext', $true)
    $context = [Runtime.Serialization.FormatterServices]::GetUninitializedObject($contextType)
    $fontFactoryType = $native.GetType('TaleWorlds.GauntletUI.FontFactory', $true)
    $fontFactory = [Runtime.Serialization.FormatterServices]::GetUninitializedObject($fontFactoryType)
    $language = [Runtime.Serialization.FormatterServices]::GetUninitializedObject($native.GetType('TaleWorlds.GauntletUI.Language', $true))
    $fontFactoryType.GetField('_currentLangugage', $flags).SetValue($fontFactory, $language)
    $contextType.GetField('<FontFactory>k__BackingField', $flags).SetValue($context, $fontFactory)
    $events = [Runtime.Serialization.FormatterServices]::GetUninitializedObject($native.GetType('TaleWorlds.GauntletUI.EventManager', $true))
    $contextType.GetField('<EventManager>k__BackingField', $flags).SetValue($context, $events)
    $inputAssembly = [Reflection.Assembly]::LoadFrom((Join-Path $gameBin 'TaleWorlds.InputSystem.dll'))
    $inputType = $inputAssembly.GetType('TaleWorlds.InputSystem.Input', $true)
    $inputField = $inputType.GetField('_inputManager', $flags)
    $priorInput = $inputField.GetValue($null)
    $emptyInput = [Activator]::CreateInstance($inputAssembly.GetType('TaleWorlds.InputSystem.EmptyInputManager', $true), $true)
    $inputField.SetValue($null, $emptyInput)
    $composerType = $client.GetType('ReignBeta.UI.ViewModels.ReignActionComposerVM', $true)
    $composer = [Activator]::CreateInstance($composerType, @([Action] {}))
    try {
        $textWidget = [Runtime.Serialization.FormatterServices]::GetUninitializedObject($editor)
        $widget.GetField('<Context>k__BackingField', $flags).SetValue($textWidget, $context)
        $nativeText = [Activator]::CreateInstance($twoDimension.GetType('TaleWorlds.TwoDimension.EditableText', $true), @(0, 0, $null, $null))
        $editableWidget = $native.GetType('TaleWorlds.GauntletUI.BaseTypes.EditableTextWidget', $true)
        $editableWidget.GetField('_editableText', $flags).SetValue($textWidget, $nativeText)
        $textWidget.MaxLength = [int]::MaxValue
        $textWidget.ComposerId = 'missing-binding'
        $editor.GetMethod('BindComposer').Invoke($textWidget, @($composer)) | Out-Null
        if ($textWidget.ComposerId -ne $composer.Id) { throw 'Mailbox explicit composer binding failed.' }
        $mailboxEditorChecks++
        $textWidget.HandleInput([int[]]@(72, 105))
        if ($textWidget.Text -ne 'Hi' -or $composer.State.Text -ne 'Hi' -or $nativeText.CursorPosition -ne 2) { throw 'Actual editor dropped typed text.' }
        $mailboxEditorChecks++
        $nativeText.SetCursor(1, $false, $false)
        $textWidget.HandleInput([int[]]@(33, 60, 62, 10))
        if ($composer.State.Text -ne 'H!i') { throw 'Caret insertion or markup/control filtering failed.' }
        $mailboxEditorChecks++
        $nativeText.SelectAll()
        $textWidget.HandleInput([int[]]@(82, 101, 112, 108, 121))
        if ($composer.State.Text -ne 'Reply') { throw 'Selection replacement failed.' }
        $mailboxEditorChecks++
        $composer.Dispose()
        $rejected = $false
        try { $editor.GetMethod('BindComposer').Invoke($textWidget, @($composer)) | Out-Null }
        catch { $rejected = $true }
        if (!$rejected) { throw 'Disposed mailbox composer was accepted.' }
        $mailboxEditorChecks++
        $mailboxType = $client.GetType('ReignBeta.UI.ViewModels.ReignCorrespondenceScreenVM', $true)
        $mailbox = [Runtime.Serialization.FormatterServices]::GetUninitializedObject($mailboxType)
        $cancellation = [Threading.CancellationTokenSource]::new()
        $mailboxType.GetField('_loadCancellation', $flags).SetValue($mailbox, $cancellation)
        $mailbox.CancelPendingLoads()
        $mailbox.CancelPendingLoads()
        if (!$cancellation.IsCancellationRequested -or !$mailboxType.GetField('_finalized', $flags).GetValue($mailbox)) { throw 'Cancel did not invalidate mailbox loads immediately.' }
        $mailboxEditorChecks++
        $cancellation.Dispose()
    } finally {
        $composer.Dispose()
        $inputField.SetValue($null, $priorInput)
    }
    # Actual compiled clipping, lazy binding and cancellation; no engine upload.
    $portraitType = $client.GetType('ReignBeta.UI.Widgets.ReignPortraitWidget', $true)
    $intersects = $portraitType.GetMethod('Intersects', $flags)
    foreach ($case in @(
        @(0, 0, 75, 75, $true), @(0, 619, 75, 75, $false),
        @(0, 610, 75, 75, $true), @([single]::NaN, 0, 75, 75, $false))) {
        $arguments = [object[]]@([single]$case[0], [single]$case[1], [single]$case[2], [single]$case[3],
            [single]0, [single]0, [single]389, [single]619)
        if ($intersects.Invoke($null, $arguments) -ne $case[4]) { throw 'Portrait clipping guard failed.' }
        $mailboxPortraitChecks++
    }
    foreach ($scrollTop in @([single]0, [single](600 * 84 - 619))) {
        $visible = 0
        for ($row = 0; $row -lt 600; $row++) {
            if ($intersects.Invoke($null, [object[]]@([single]0, [single]($row * 84), [single]75, [single]75,
                [single]0, $scrollTop, [single]389, [single]619))) { $visible++ }
        }
        if ($visible -ne 8) { throw 'Portrait work was not bounded to the visible contact rail.' }
        $mailboxPortraitChecks++
    }
    $portraitFixture = [Runtime.Serialization.FormatterServices]::GetUninitializedObject($portraitType)
    # XML may bind the key before its behavior flag. Neither order may do IO.
    for ($row = 0; $row -lt 1000; $row++) { $portraitFixture.PortraitCacheKey = 'isolated_unloaded_' + $row }
    if ($null -ne $portraitType.GetField('_thumbnailPending', $flags).GetValue($portraitFixture)) { throw 'Binding eagerly prepared a portrait.' }
    $mailboxPortraitChecks++
    $portraitFixture.VisibleThumbnailOnly = $true
    $portraitFixture.PortraitCacheKey = ''
    $portraitType.GetMethod('RenderVisibleThumbnail', $flags).Invoke($portraitFixture, @($null, $null)) | Out-Null
    if ($null -ne $portraitType.GetField('_thumbnailPending', $flags).GetValue($portraitFixture)) { throw 'An unknown contact requested a portrait.' }
    $mailboxPortraitChecks++
    $ownerTable = $portraitType.GetField('ThumbnailOwners', $flags).GetValue($null)
    $otherContext = [Runtime.Serialization.FormatterServices]::GetUninitializedObject($contextType)
    $firstOwner = [Threading.CancellationTokenSource]::new()
    $secondOwner = [Threading.CancellationTokenSource]::new()
    $ownerTable.Add($context, $firstOwner); $ownerTable.Add($otherContext, $secondOwner)
    $cancelPortraits = $portraitType.GetMethod('CancelThumbnailLoads', $flags)
    $cancelPortraits.Invoke($null, @($context)) | Out-Null
    $cancelPortraits.Invoke($null, @($context)) | Out-Null
    if (!$firstOwner.IsCancellationRequested -or $secondOwner.IsCancellationRequested) { throw 'Closing one mailbox cancelled another UI owner or failed to cancel.' }
    $mailboxPortraitChecks++
    $factoryType = $client.GetType('AIPortraits.TextureFactory', $true)
    $cancelledThumbnail = $factoryType.GetMethod('GetOrBuildThumbnailAsync').Invoke($null,
        @('isolated_cancelled_portrait', [single]1, $firstOwner.Token))
    if (!$cancelledThumbnail.IsCompleted -or $null -ne $cancelledThumbnail.GetAwaiter().GetResult()) { throw 'Cancelled thumbnail work did not finish before IO/upload.' }
    $mailboxPortraitChecks++
    $firstOwner.Dispose(); $secondOwner.Dispose()
    # Native HasMet and IsKnownToPlayer are independent. Exercise the actual
    # compiled decision against isolated callbacks, retaining existing meeting dates.
    Add-Type -TypeDefinition @'
public sealed class ReignKnownHeroProbeState
{
    public bool Met, Known;
    public int Date = 7, Reveals, Meetings;
    public void Reveal() { Known = true; Reveals++; }
    public void Meet() { Met = true; Meetings++; Date = 999; }
}
'@
    $knowledgeType = $client.GetType('ReignBeta.Campaign.ReignHeroKnowledge', $true)
    $applyKnowledge = $knowledgeType.GetMethod('ApplyKnownState', $flags)
    foreach ($met in @($false, $true)) { foreach ($known in @($false, $true)) {
        $knowledgeState = [ReignKnownHeroProbeState]::new()
        $knowledgeState.Met = $met; $knowledgeState.Known = $known
        $reveal = [Action]{ $knowledgeState.Reveal() }
        $meet = [Action]{ $knowledgeState.Meet() }
        $applyKnowledge.Invoke($null, @($met, $known, $reveal, $meet)) | Out-Null
        $expectedReveals = if ($known) { 0 } else { 1 }
        $expectedMeetings = if ($met) { 0 } else { 1 }
        $expectedDate = if ($met) { 7 } else { 999 }
        if (!$knowledgeState.Met -or !$knowledgeState.Known -or $knowledgeState.Reveals -ne $expectedReveals -or
            $knowledgeState.Meetings -ne $expectedMeetings -or $knowledgeState.Date -ne $expectedDate) { throw 'Native hero knowledge/date decision failed.' }
        $applyKnowledge.Invoke($null, @($knowledgeState.Met, $knowledgeState.Known, $reveal, $meet)) | Out-Null
        if ($knowledgeState.Reveals -ne $expectedReveals -or $knowledgeState.Meetings -ne $expectedMeetings -or
            $knowledgeState.Date -ne $expectedDate) { throw 'Repeated interaction rewrote an existing meeting.' }
        $heroKnowledgeChecks++
    } }
    # Exercise only managed selector helpers on an uninitialized fixture: no
    # campaign constructor, entity lookup, selection callback or widget runs.
    $builderType = $client.GetType('ReignBeta.UI.ViewModels.ReignActionBuilderVM', $true)
    $builder = [Runtime.Serialization.FormatterServices]::GetUninitializedObject($builderType)
    $clauseField = $builderType.GetField('_clause', $flags)
    $wageCheck = $builderType.GetMethod('IsUnusedWageDetail', $flags)
    $parameterType = $wageCheck.GetParameters()[0].ParameterType
    $parameter = [Activator]::CreateInstance($parameterType)
    foreach ($action in @('accept_temporary_party_guest', 'renew_temporary_party_guest', 'unrelated_action')) {
        foreach ($wage in @('', '0', '100')) {
            $clause = [Activator]::CreateInstance($clauseField.FieldType)
            $clause.ActionId = $action
            if ($wage) { $clause.Arguments['terms.wageGold'] = $wage }
            $clauseField.SetValue($builder, $clause)
            foreach ($key in @('terms.wagePeriodDays', 'terms.wageRecipientHeroStringId', 'terms.purpose')) {
                $parameter.Key = $key
                $actual = $wageCheck.Invoke($builder, @($parameter))
                $expected = $action -ne 'unrelated_action' -and $key -ne 'terms.purpose' -and $wage -ne '100'
                if ($actual -ne $expected) { throw "Incorrect wage selector for $action / $wage / $key" }
                $selectionChecks++
            }
        }
    }
    $choicesField = $builderType.GetField('<Choices>k__BackingField', $flags)
    $choices = [Activator]::CreateInstance($choicesField.FieldType)
    $choicesField.SetValue($builder, $choices)
    $builderType.GetField('_search', $flags).SetValue($builder, 'no entity matches this search')
    $unspecified = $builderType.GetMethod('UnspecifiedChoice', $flags)
    $parameter.Required = $false
    $unspecified.Invoke($builder, @($parameter)) | Out-Null
    if ($choices.Count -ne 1 -or $choices[0].Label -ne 'Leave unspecified') { throw 'Search hid the optional first choice.' }
    $selectionChecks++
    # Exercise the compiled custody snapshot with the actual installed native
    # struct. No roster, campaign constructor, hero or game callback is invoked.
    $campaignAssembly = [Reflection.Assembly]::LoadFrom((Join-Path $gameBin 'TaleWorlds.CampaignSystem.dll'))
    $rowType = $campaignAssembly.GetType('TaleWorlds.CampaignSystem.Roster.TroopRosterElement', $true)
    $assetType = $client.GetType('ReignBeta.Actions.ReignAssetTransfer', $true)
    $snapshot = $assetType.GetMethod('RosterRowState', $flags)
    $emptyRow = [Activator]::CreateInstance($rowType)
    $emptyState = $snapshot.Invoke($null, @($emptyRow))
    foreach ($key in @('count', 'wounded', 'xp')) {
        if ($emptyState[$key].ToString() -ne '0') { throw "Absent native roster row has nonzero $key." }
    }
    $custodySnapshotChecks++
    # A nonhero CharacterObject has no HeroObject. Bypass its game constructor
    # and set only the public struct fields needed by the read-only getter.
    $characterType = $campaignAssembly.GetType('TaleWorlds.CampaignSystem.CharacterObject', $true)
    $nonhero = [Runtime.Serialization.FormatterServices]::GetUninitializedObject($characterType)
    $populatedRow = [Activator]::CreateInstance($rowType)
    $rowType.GetField('Character').SetValue($populatedRow, $nonhero)
    $rowType.GetProperty('Number').SetValue($populatedRow, 3, $null)
    $rowType.GetProperty('WoundedNumber').SetValue($populatedRow, 2, $null)
    $rowType.GetProperty('Xp').SetValue($populatedRow, 101, $null)
    $populatedState = $snapshot.Invoke($null, @($populatedRow))
    if ($populatedState['count'].ToString() -ne '3' -or $populatedState['wounded'].ToString() -ne '2' -or $populatedState['xp'].ToString() -ne '101') {
        throw 'Native custody snapshot lost troop count, wounds or experience.'
    }
    $custodySnapshotChecks++
    $choices.Clear()
    $parameter.Required = $true
    $unspecified.Invoke($builder, @($parameter)) | Out-Null
    if ($choices.Count -ne 0) { throw 'A required parameter became skippable.' }
    $selectionChecks++
    # IsPregnant is a public native field, not a property. Exercise the actual
    # compiled allowlisted reader with a constructor-free native Hero fixture.
    $heroType = $campaignAssembly.GetType('TaleWorlds.CampaignSystem.Hero', $true)
    $heroFixture = [Runtime.Serialization.FormatterServices]::GetUninitializedObject($heroType)
    $nativeConsole = $client.GetType('ReignBeta.Actions.ReignNativeActionConsole', $true)
    $observeMember = $nativeConsole.GetMethod('ObservePublicMember', $flags)
    foreach ($pregnant in @($false, $true)) {
        $heroType.GetField('IsPregnant').SetValue($heroFixture, $pregnant)
        $observed = $observeMember.Invoke($null, @($heroFixture, 'IsPregnant'))
        if ($observed.ToString() -ne $pregnant.ToString()) { throw 'Native pregnancy field was omitted or misread.' }
        $nativeObservationChecks++
    }
    foreach ($missing in @('IsInRaftState', '_unexpectedPrivateField')) {
        if ($null -ne $observeMember.Invoke($null, @($heroFixture, $missing))) { throw 'Missing or unlisted native member was exposed.' }
        $nativeObservationChecks++
    }
    # Exercise the actual compiled projection with constructor-free native
    # records. No live Romance list, campaign or relationship action is accessed.
    $observeRomance = $nativeConsole.GetMethod('ObserveRomanticStates', $flags)
    $romanticType = $observeRomance.GetParameters()[0].ParameterType.GetGenericArguments()[0]
    function New-RomanceHeroFixture([string]$id) {
        $fixture = [Runtime.Serialization.FormatterServices]::GetUninitializedObject($heroType)
        $heroType.GetProperty('StringId').SetValue($fixture, $id, $null)
        return $fixture
    }
    function New-RomanceStateFixture($first, $second, [int]$level) {
        $fixture = [Runtime.Serialization.FormatterServices]::GetUninitializedObject($romanticType)
        $romanticType.GetField('Person1').SetValue($fixture, $first)
        $romanticType.GetField('Person2').SetValue($fixture, $second)
        $levelField = $romanticType.GetField('Level')
        $levelField.SetValue($fixture, [Enum]::ToObject($levelField.FieldType, $level))
        return $fixture
    }
    $romanceA = New-RomanceHeroFixture 'fixture_a'
    $romanceB = New-RomanceHeroFixture 'fixture_b'
    $romanceOther = New-RomanceHeroFixture 'fixture_other'
    $romanceBlank = New-RomanceHeroFixture ''
    $romanceScope = [Array]::CreateInstance($heroType, 5)
    $romanceScope.SetValue($romanceB, 0); $romanceScope.SetValue($romanceA, 1)
    $romanceScope.SetValue($romanceA, 2); $romanceScope.SetValue($romanceBlank, 3)
    $romanceRows = [Array]::CreateInstance($romanticType, 6)
    $romanceRows.SetValue((New-RomanceStateFixture $romanceA $romanceB 1), 0)
    $romanceRows.SetValue((New-RomanceStateFixture $romanceB $romanceA 2), 1)
    $romanceRows.SetValue((New-RomanceStateFixture $romanceA $romanceB 3), 2)
    $romanceRows.SetValue((New-RomanceStateFixture $romanceA $romanceOther 4), 3)
    $romanceRows.SetValue((New-RomanceStateFixture $romanceA $romanceBlank 0), 5)
    $romance = $observeRomance.Invoke($null, @($romanceRows, $romanceScope))
    if ($romance['available'].ToString() -ne 'True' -or $romance['total'].ToString() -ne '3' -or
        $romance['rows'].Count -ne 3 -or $romance['truncated'].ToString() -ne 'False') {
        throw 'Romance projection leaked another participant, a blank identity, or lost duplicate native records.'
    }
    $romanceObservationChecks++
    if ($romance['rows'][0]['nativeIndex'].ToString() -ne '0' -or $romance['rows'][1]['nativeIndex'].ToString() -ne '2' -or
        $romance['rows'][2]['nativeIndex'].ToString() -ne '1' -or $romance['rows'][2]['person1Id'].ToString() -ne 'fixture_b' -or
        $romance['rows'][2]['levelValue'].ToString() -ne '2') { throw 'Romance projection lost direction, native level or stable ordering.' }
    $romanceObservationChecks++
    foreach ($row in $romance['rows']) {
        $keys = @($row.Properties() | ForEach-Object Name | Sort-Object)
        if (($keys -join ',') -ne 'level,levelValue,nativeIndex,person1Id,person2Id') { throw 'Romance observer exposed unreviewed native fields.' }
    }
    $romanceObservationChecks++
    if ($romanticType.GetField('Level').GetValue($romanceRows.GetValue(0)).value__ -ne 1 -or
        $romanceRows.Length -ne 6 -or $romanceScope.Length -ne 5) { throw 'Romance observation mutated its inputs.' }
    $romanceObservationChecks++
    foreach ($unavailableArguments in @(@($null, $romanceScope), @($romanceRows, $null))) {
        $unavailable = $observeRomance.Invoke($null, $unavailableArguments)
        if ($unavailable['available'].ToString() -ne 'False' -or $unavailable['rows'].Count -ne 0) {
            throw 'Missing romance state or scope became an available empty observation.'
        }
        $romanceObservationChecks++
    }
    $emptyRomance = [Array]::CreateInstance($romanticType, 0)
    $emptyObservation = $observeRomance.Invoke($null, @($emptyRomance, $romanceScope))
    if ($emptyObservation['available'].ToString() -ne 'True' -or $emptyObservation['total'].ToString() -ne '0') {
        throw 'An available empty romance list was confused with missing native state.'
    }
    $romanceObservationChecks++
    foreach ($onlyHero in @($romanceA, $romanceBlank)) {
        $singleScope = [Array]::CreateInstance($heroType, 1); $singleScope.SetValue($onlyHero, 0)
        $isolated = $observeRomance.Invoke($null, @($romanceRows, $singleScope))
        if ($isolated['total'].ToString() -ne '0') { throw 'Romance scope did not require both identified participants.' }
        $romanceObservationChecks++
    }
    $manyRomance = [Array]::CreateInstance($romanticType, 40)
    for ($i = 0; $i -lt 40; $i++) { $manyRomance.SetValue($romanceRows.GetValue(0), $i) }
    $boundedRomance = $observeRomance.Invoke($null, @($manyRomance, $romanceScope))
    if ($boundedRomance['total'].ToString() -ne '40' -or $boundedRomance['truncated'].ToString() -ne 'True' -or
        $boundedRomance['rows'].Count -ne 32 -or $boundedRomance['rows'][31]['nativeIndex'].ToString() -ne '31') {
        throw 'Romance projection concealed duplicate overflow or exceeded its 32-row bound.'
    }
    $romanceObservationChecks++
    # Only the existing model-aware war query may first resolve a native stance.
    # Exhaust all guard combinations without invoking that query or a campaign.
    $tributeGuard = $nativeConsole.GetMethod('CanObservePersistentTribute', $flags)
    for ($mask = 0; $mask -lt 16; $mask++) {
        $guardArgs = [object[]]@([bool]($mask -band 1), [bool]($mask -band 2), [bool]($mask -band 4), [bool]($mask -band 8))
        $actual = $tributeGuard.Invoke($null, $guardArgs)
        if ($actual -ne ($mask -eq 3)) { throw 'An unavailable, constant or shallow stance passed the persistent tribute guard.' }
        $tributeObservationChecks++
    }
    $pairObserver = $nativeConsole.GetMethod('ObserveFactionPair', $flags)
    [Runtime.CompilerServices.RuntimeHelpers]::PrepareMethod($pairObserver.MethodHandle)
    $prepared.Add($nativeConsole.FullName + '.' + $pairObserver.Name)
    # Read the actual public native bribe counter through the compiled fixed
    # observer. This constructor-free fixture never invokes a bribe or campaign.
    $settlementType = $campaignAssembly.GetType('TaleWorlds.CampaignSystem.Settlements.Settlement', $true)
    $settlementFixture = [Runtime.Serialization.FormatterServices]::GetUninitializedObject($settlementType)
    $bribeField = $settlementType.GetField('BribePaid')
    $bribeProperty = $settlementType.GetProperty('BribePaid')
    foreach ($paid in @(0, 150)) {
        if ($bribeField) { $bribeField.SetValue($settlementFixture, $paid) }
        elseif ($bribeProperty -and $bribeProperty.SetMethod.IsPublic) { $bribeProperty.SetValue($settlementFixture, $paid, $null) }
        else { throw 'Licensed Settlement lacks the reviewed public BribePaid counter.' }
        $observed = $observeMember.Invoke($null, @($settlementFixture, 'BribePaid'))
        if ($null -eq $observed -or $observed.ToString() -ne $paid.ToString()) { throw 'Native guard-bribe counter was omitted or misread.' }
        $bribeObservationChecks++
    }
    # Pure compiled fault formatting only; no native action is invoked. A CLR
    # exception fixture supplies long stack text to exercise the retained bound.
    $observeFault = $nativeConsole.GetMethod('ObserveExceptionFault', $flags)
    $faultFixture = [InvalidOperationException]::new('isolated native fault fixture')
    $fault = $observeFault.Invoke($null, @($faultFixture))
    if ($fault['errorType'].ToString() -ne 'System.InvalidOperationException' -or
        $fault['error'].ToString() -ne 'isolated native fault fixture') { throw 'Native fault identity was omitted.' }
    $nativeFaultChecks++
    # These compiled helpers observe isolated agreements and validate supplied
    # context facts only. They never access a campaign, sign a live offer or invoke
    # a native action. Proposal IDs must come from the saved client, not its server preview.
    $hostType = $client.GetType('ReignBeta.Campaign.ReignLiveInteractionTestHost', $true)
    $observeOffers = $hostType.GetMethod('ObservePendingNpcOffers', $flags)
    $agreementType = $observeOffers.GetParameters()[0].ParameterType.GetGenericArguments()[0]
    function New-ObservedOffer([string]$id, [string]$npc = 'lord_1_1') {
        $offer = [Activator]::CreateInstance($agreementType)
        $offer.Id = $id; $offer.PlayerId = 'main_hero'; $offer.NpcId = $npc; $offer.ProposerId = $npc
        $offer.Revision = 2
        $offer.State = [Enum]::Parse($agreementType.GetProperty('State').PropertyType, 'Counteroffered')
        $term = [Activator]::CreateInstance($clauseField.FieldType)
        $term.Id = $id + '-clause'; $term.ActionId = 'give.gold'; $term.ActorId = $npc; $term.RecipientId = 'main_hero'
        $term.Arguments['quantity'] = '20'
        $offer.Clauses.Add($term)
        $offer.NpcAcceptedHash = $offer.TermsHash()
        return $offer
    }
    $savedOffer = New-ObservedOffer 'client-native-offer'
    $offerArray = [Array]::CreateInstance($agreementType, 1); $offerArray.SetValue($savedOffer, 0)
    $originalHash = $savedOffer.TermsHash()
    $observedOffers = $observeOffers.Invoke($null, @($offerArray, 'main_hero', [string[]]@('lord_1_1')))
    $observedOffer = $observedOffers['rows'][0]['agreement']
    if ($observedOffer['Id'].ToString() -ne 'client-native-offer' -or
        $observedOffer['Clauses'][0]['Id'].ToString() -ne 'client-native-offer-clause' -or
        $observedOffer['NpcAcceptedHash'].ToString() -ne $originalHash -or
        $observedOffers['rows'][0]['termsHash'].ToString() -ne $originalHash) { throw 'Saved offer identity or signature was replaced by preview terms.' }
    $pendingOfferChecks++
    if ($savedOffer.TermsHash() -ne $originalHash -or $savedOffer.PlayerAcceptedHash -ne '' -or
        $savedOffer.Clauses[0].DisplayText -ne '') { throw 'Offer observation altered wording or player consent.' }
    $pendingOfferChecks++
    $offerArray = [Array]::CreateInstance($agreementType, 5); $offerArray.SetValue($savedOffer, 0)
    $foreign = New-ObservedOffer 'foreign-npc' 'lord_1_2'; $offerArray.SetValue($foreign, 1)
    $wrongPlayer = New-ObservedOffer 'wrong-player'; $wrongPlayer.PlayerId = 'other-player'; $offerArray.SetValue($wrongPlayer, 2)
    $wrongAuthor = New-ObservedOffer 'wrong-author'; $wrongAuthor.ProposerId = 'main_hero'; $offerArray.SetValue($wrongAuthor, 3)
    $rejected = New-ObservedOffer 'rejected'; $rejected.State = [Enum]::Parse($agreementType.GetProperty('State').PropertyType, 'Rejected'); $offerArray.SetValue($rejected, 4)
    $observedOffers = $observeOffers.Invoke($null, @($offerArray, 'main_hero', [string[]]@('lord_1_1')))
    if ($observedOffers['total'].ToString() -ne '1' -or $observedOffers['rows'].Count -ne 1) { throw 'Unselected, foreign or terminal offers leaked into the snapshot.' }
    $pendingOfferChecks++
    $offerArray = [Array]::CreateInstance($agreementType, 9)
    for ($i = 0; $i -lt 9; $i++) { $offer = New-ObservedOffer ('bounded-offer-' + $i); $offer.CreatedDay = $i; $offerArray.SetValue($offer, $i) }
    $observedOffers = $observeOffers.Invoke($null, @($offerArray, 'main_hero', [string[]]@('lord_1_1')))
    if ($observedOffers['total'].ToString() -ne '9' -or $observedOffers['rows'].Count -ne 8 -or
        ![bool]::Parse($observedOffers['truncated'].ToString()) -or
        $observedOffers['rows'][0]['agreement']['Id'].ToString() -ne 'bounded-offer-8') { throw 'Pending offer row bound or order failed.' }
    $pendingOfferChecks++
    $largeOffer = New-ObservedOffer 'oversized-offer'; $largeOffer.Clauses[0].DisplayText = 'x' * 70000
    $offerArray = [Array]::CreateInstance($agreementType, 1); $offerArray.SetValue($largeOffer, 0)
    $observedOffers = $observeOffers.Invoke($null, @($offerArray, 'main_hero', [string[]]@('lord_1_1')))
    if ($observedOffers['rows'].Count -ne 0 -or $observedOffers['oversized'].ToString() -ne '1' -or
        ![bool]::Parse($observedOffers['truncated'].ToString())) { throw 'An oversized offer produced partial executable terms.' }
    $pendingOfferChecks++
    $observedOffers = $observeOffers.Invoke($null, @($null, 'main_hero', [string[]]@()))
    if ($observedOffers['total'].ToString() -ne '0' -or $observedOffers['rows'].Count -ne 0) { throw 'Empty offer observation failed.' }
    $pendingOfferChecks++
    $limitation = $nativeConsole.GetMethod('KnownInvocationLimitation', $flags)
    $knownGiftHash = '1f8e33e2ed73e6ec653d7629180afb70649ddc6e5bd1657a802a264efda1c3ae'
    foreach ($input in @(
        @('native.GiveItem.ApplyForHeroes', $false, $false, $knownGiftHash, $true),
        @('native.GiveItem.ApplyForHeroes', $false, $false, 'other-assembly', $false),
        @('native.RemoveCompanion.ApplyByFire', $true, $false, '', $true),
        @('native.RemoveCompanion.ApplyByFire', $true, $true, '', $false),
        @('native.RemoveCompanion.ApplyByFire', $false, $false, '', $false),
        @('native.GiveItem.ApplyForParties', $true, $false, $knownGiftHash, $false))) {
        $reason = $limitation.Invoke($null, @($input[0], $input[1], $input[2], $input[3]))
        if (($reason.Length -gt 0) -ne $input[4]) { throw 'Known native invocation safety guard selected the wrong context.' }
        $nativeSafetyChecks++
    }
    $partyEntryGuard = $nativeConsole.GetMethod('CanInvokePartySettlementEntry', $flags)
    foreach ($input in @(
        @($false, $false, $false, $true),
        @($true, $true, $true, $true),
        @($true, $false, $true, $false),
        @($true, $true, $false, $false),
        @($true, $false, $false, $false))) {
        if ([bool]$partyEntryGuard.Invoke($null, @($input[0], $input[1], $input[2])) -ne $input[3]) {
            throw 'Player party entry accepted a missing/mismatched native encounter or blocked ordinary NPC entry.'
        }
        $nativeSafetyChecks++
    }
    $playerEncounterFixture = $hostType.GetMethod('ApplyStructuredPlayerSettlementEncounterFixture', $flags)
    [Runtime.CompilerServices.RuntimeHelpers]::PrepareMethod($playerEncounterFixture.MethodHandle)
    $prepared.Add($hostType.FullName + '.' + $playerEncounterFixture.Name)
    $contextGuard = $nativeConsole.GetMethod('ValidateContext', $flags)
    [Runtime.CompilerServices.RuntimeHelpers]::PrepareMethod($contextGuard.MethodHandle)
    $prepared.Add('ReignBeta.Actions.ReignNativeActionConsole.ValidateContext')
    if ($fault['errorStack'].ToString() -ne '' -or [bool]::Parse($fault['errorStackTruncated'].ToString())) {
        throw 'A stackless exception invented diagnostic frames.'
    }
    $nativeFaultChecks++
    $stackField = [Exception].GetField('_stackTraceString', [Reflection.BindingFlags]'Instance,NonPublic')
    if ($null -eq $stackField) { throw 'The isolated CLR stack fixture is unavailable.' }
    $stackField.SetValue($faultFixture, ('frame' * 1000))
    $wrappedFault = [Reflection.TargetInvocationException]::new([Reflection.TargetInvocationException]::new($faultFixture))
    $fault = $observeFault.Invoke($null, @($wrappedFault))
    if ($fault['errorType'].ToString() -ne 'System.InvalidOperationException' -or
        $fault['error'].ToString() -ne 'isolated native fault fixture') { throw 'Native reflection wrapper hid the real fault.' }
    $nativeFaultChecks++
    if ($fault['errorStack'].ToString() -ne ('frame' * 1000).Substring(0, 4096) -or
        ![bool]::Parse($fault['errorStackTruncated'].ToString())) { throw 'Native fault stack exceeded or misreported its bound.' }
    $nativeFaultChecks++
    # Exercise the compiled balance projection, including partial/clamped transfers
    # and overflow. These supplied balances are contract fixtures, not native payment proof.
    $diplomacyExecutor = $client.GetType('ReignBeta.World.ReignDiplomacyExecutor', $true)
    $goldObservation = $diplomacyExecutor.GetMethod('ObserveGoldTransferBalances', $flags)
    foreach ($method in @($goldObservation, $diplomacyExecutor.GetMethod('ApplyGoldTransfer', $flags))) {
        [Runtime.CompilerServices.RuntimeHelpers]::PrepareMethod($method.MethodHandle)
        $prepared.Add('ReignBeta.World.ReignDiplomacyExecutor.' + $method.Name)
    }
    foreach ($goldCase in @(
        @{ args = @(917, 1000, 83, 100, 1017, $true, $true); debit = 917L; credit = 917L; exact = $true; conserved = $true },
        @{ args = @(917, 500, 0, 100, 600, $true, $true); debit = 500L; credit = 500L; exact = $false; conserved = $true },
        @{ args = @(0, 1000, 1000, 100, 100, $false, $true); debit = 0L; credit = 0L; exact = $false; conserved = $true },
        @{ args = @(917, 1000, 1000, 100, 100, $false, $true); debit = 0L; credit = 0L; exact = $false; conserved = $true },
        @{ args = @(917, 0, 0, 100, 1017, $true, $false); debit = 0L; credit = 917L; exact = $false; conserved = $false },
        @{ args = @(100, 1000, 900, 100, 199, $true, $true); debit = 100L; credit = 99L; exact = $false; conserved = $false },
        @{ args = @(1, [int]::MaxValue, [int]::MinValue, [int]::MaxValue, [int]::MinValue, $true, $true); debit = 4294967295L; credit = -4294967295L; exact = $false; conserved = $false },
        @{ args = @([int]::MaxValue, [int]::MaxValue, 0, 0, [int]::MaxValue, $true, $true); debit = 2147483647L; credit = 2147483647L; exact = $true; conserved = $true })) {
        $observed = $goldObservation.Invoke($null, [object[]]$goldCase.args)
        if ($observed['schema'].ToString() -ne 'reign-diplomacy-gold-observation-v1' -or
            [long]::Parse($observed['debited'].ToString()) -ne $goldCase.debit -or
            [long]::Parse($observed['credited'].ToString()) -ne $goldCase.credit -or
            [bool]::Parse($observed['conserved'].ToString()) -ne $goldCase.conserved -or
            [bool]::Parse($observed['exactPayment'].ToString()) -ne $goldCase.exact -or
            [int]::Parse($observed['requestedAmount'].ToString()) -ne $goldCase.args[0] -or
            [int]::Parse($observed['fromBefore'].ToString()) -ne $goldCase.args[1] -or
            [int]::Parse($observed['fromAfter'].ToString()) -ne $goldCase.args[2] -or
            [int]::Parse($observed['toBefore'].ToString()) -ne $goldCase.args[3] -or
            [int]::Parse($observed['toAfter'].ToString()) -ne $goldCase.args[4] -or
            [bool]::Parse($observed['invoked'].ToString()) -ne $goldCase.args[5]) {
            throw 'Diplomatic gold observation falsely claimed exact conservation or changed supplied balances.'
        }
        $goldObservationChecks++
    }
    # Exercise the actual compiled duration scope against the licensed native model,
    # without constructing a campaign or executing any alliance/world action.
    $campaignAssembly = [Reflection.Assembly]::LoadFrom((Join-Path $gameBin 'TaleWorlds.CampaignSystem.dll'))
    $kingdomType = $campaignAssembly.GetType('TaleWorlds.CampaignSystem.Kingdom', $true)
    $firstRealm = [Runtime.Serialization.FormatterServices]::GetUninitializedObject($kingdomType)
    $secondRealm = [Runtime.Serialization.FormatterServices]::GetUninitializedObject($kingdomType)
    $otherRealm = [Runtime.Serialization.FormatterServices]::GetUninitializedObject($kingdomType)
    $allianceModel = [Activator]::CreateInstance($campaignAssembly.GetType('TaleWorlds.CampaignSystem.GameComponents.DefaultAllianceModel', $true))
    $durationPatches = $client.GetType('ReignBeta.Runtime.ReignDiplomacyPatches', $true)
    $scopeMethod = $durationPatches.GetMethod('UseAcceptedAllianceDuration', $flags)
    $durationMethod = $durationPatches.GetMethod('ResolveAllianceDuration', $flags)
    foreach ($method in @($scopeMethod, $durationMethod, $durationPatches.GetMethod('StartAcceptedAlliance', $flags))) {
        [Runtime.CompilerServices.RuntimeHelpers]::PrepareMethod($method.MethodHandle)
        $prepared.Add('ReignBeta.Runtime.ReignDiplomacyPatches.' + $method.Name)
    }
    function Assert-AllianceDuration($a, $b, [double]$expected) {
        $time = $durationMethod.Invoke($null, @($allianceModel, $a, $b))
        if ([Math]::Abs($time.ToDays - $expected) -gt 0.001) { throw 'Accepted native alliance duration scope leaked or selected the wrong realm pair.' }
        $script:allianceDurationChecks++
    }
    Assert-AllianceDuration $firstRealm $secondRealm 84
    $scope = $scopeMethod.Invoke($null, @($firstRealm, $secondRealm, [single]7))
    try {
        Assert-AllianceDuration $firstRealm $secondRealm 7
        Assert-AllianceDuration $secondRealm $firstRealm 7
        Assert-AllianceDuration $firstRealm $otherRealm 84
        $nested = $scopeMethod.Invoke($null, @($firstRealm, $secondRealm, [single]3))
        try { Assert-AllianceDuration $firstRealm $secondRealm 3 } finally { $nested.Dispose() }
        Assert-AllianceDuration $firstRealm $secondRealm 7
    } finally { $scope.Dispose() }
    Assert-AllianceDuration $firstRealm $secondRealm 84
    try {
        $scope = $scopeMethod.Invoke($null, @($firstRealm, $secondRealm, [single]11))
        try { throw 'isolated duration cleanup' } finally { $scope.Dispose() }
    } catch { if ($_.Exception.Message -ne 'isolated duration cleanup') { throw } }
    Assert-AllianceDuration $firstRealm $secondRealm 84
    foreach ($durationCase in @(
        @($firstRealm, $secondRealm, [single]::NaN), @($firstRealm, $secondRealm, [single]::PositiveInfinity),
        @($firstRealm, $secondRealm, [single]0), @($firstRealm, $secondRealm, [single]-1),
        @($null, $secondRealm, [single]7), @($firstRealm, $firstRealm, [single]7))) {
        $rejected = $false
        try { $invalid = $scopeMethod.Invoke($null, $durationCase); $invalid.Dispose() }
        catch {
            $cause = $_.Exception; while ($cause.InnerException) { $cause = $cause.InnerException }
            if ($cause -isnot [ArgumentException]) { throw }
            $rejected = $true
        }
        if (!$rejected) { throw 'Invalid alliance duration or realm pair was accepted.' }
        $allianceDurationChecks++
    }
    try {
        $durationPatches.GetMethod('Apply', $flags).Invoke($null, @()) | Out-Null
        if (!$durationPatches.GetField('_allianceDurationPatchReady', $flags).GetValue($null)) { throw 'Licensed native AddAlliance duration adapter did not install.' }
        $allianceDurationChecks++
    } finally { $durationPatches.GetMethod('Unapply', $flags).Invoke($null, @()) | Out-Null }
    if ($durationPatches.GetField('_allianceDurationPatchReady', $flags).GetValue($null)) { throw 'Native alliance duration adapter did not unapply.' }
    $allianceDurationChecks++
} catch {
    $exception = $_.Exception
    while ($exception.InnerException) { $exception = $exception.InnerException }
    $failure = $exception.ToString()
} finally {
    [AppDomain]::CurrentDomain.remove_AssemblyResolve($resolver)
}
$report = [ordered]@{
    schema = 'reign-action-composer-native-probe-v1'
    ok = !$failure
    clientAssembly = $clientPath
    clientSha256 = (Get-FileHash -LiteralPath $clientPath -Algorithm SHA256).Hash
    nativeVectorAssemblyName = $nativeVectorName
    visibleTextFieldsCompatible = $fieldsCompatible
    managedSelectionChecks = $selectionChecks
    custodySnapshotChecks = $custodySnapshotChecks
    nativeObservationChecks = $nativeObservationChecks
    bribeObservationChecks = $bribeObservationChecks
    nativeFaultChecks = $nativeFaultChecks
    pendingOfferChecks = $pendingOfferChecks
    nativeSafetyChecks = $nativeSafetyChecks
    allianceDurationChecks = $allianceDurationChecks
    goldObservationChecks = $goldObservationChecks
    tributeObservationChecks = $tributeObservationChecks
    romanceObservationChecks = $romanceObservationChecks
    rebellionPreparationLifecycleChecks = $rebellionPreparationLifecycleChecks
    rebellionSavedLoadChecks = $rebellionSavedLoadChecks
    mailboxEditorChecks = $mailboxEditorChecks
    mailboxPortraitChecks = $mailboxPortraitChecks
    heroKnowledgeChecks = $heroKnowledgeChecks
    preparedMethods = @($prepared.ToArray())
    error = $failure
    nativeRenderedAcceptance = 'not-run'
}
$json = $report | ConvertTo-Json -Depth 5
[IO.File]::WriteAllText((Join-Path $probeRoot 'report.json'), $json, [Text.UTF8Encoding]::new($false))
Write-Output $json
if ($failure) { exit 1 }
