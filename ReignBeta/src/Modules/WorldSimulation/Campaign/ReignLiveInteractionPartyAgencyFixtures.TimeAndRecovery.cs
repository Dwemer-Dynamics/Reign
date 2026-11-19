using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using ReignBeta.PartyAgency;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Encounters;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Party.PartyComponents;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.Siege;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.ObjectSystem;
using ReignBeta.Dialogue.Runtime;
using ReignBeta.Platform.Integration;
using ReignBeta.Reputation.Campaign;
using ReignBeta.WorldSimulation.Arrests;
using ReignBeta.WorldSimulation.PartyAgency;
using ReignBeta.Platform.Runtime;

namespace ReignBeta.WorldSimulation.Campaign
{
    public static partial class ReignLiveInteractionTestHost
    {
        private static async Task<LiveCommandResult> AdvancePartyAgencyTimeAsync(
            JObject command)
        {
            if (command?.Value<bool?>("confirmDisposableCampaign") != true)
                return LiveCommandResult.Failed(
                    "party_agency_advance_time requires confirmDisposableCampaign=true.");
            string expectedSave = command.Value<string>("expectedSaveName") ?? string.Empty;
            string activeSave = ActiveSaveName();
            if (!string.Equals(expectedSave, activeSave, StringComparison.Ordinal)
                || !expectedSave.StartsWith("ReignTest_", StringComparison.Ordinal)
                || !expectedSave.EndsWith("_Current", StringComparison.Ordinal))
                return LiveCommandResult.Failed(
                    "Party Agency time advancement is outside the exact active enrolled disposable save.");
            double days = Math.Max(0d, Math.Min(31.5d,
                command.Value<double?>("days") ?? 0d));
            if (days <= 0d)
                return LiveCommandResult.Failed(
                    "Party Agency time advancement requires a positive duration of at most 31.5 days.");
            if (_individualVm != null || _partyVm != null || _eventVm != null)
                await CloseSessionAsync(false).ConfigureAwait(false);

            string caseId = command.Value<string>("caseId") ?? string.Empty;
            string variant = (command.Value<string>("variant") ?? string.Empty)
                .Trim().ToLowerInvariant();
            string runId = command.Value<string>("runId") ?? _activeRunId ?? string.Empty;
            string key = runId + "|" + caseId + "|" + variant;
            if (!PartyAgencyNativeEvidence.TryGetValue(key, out JObject evidence))
                return LiveCommandResult.Failed(
                    "The matching game-generated Party Agency fixture receipt is unavailable.");
            Hero target = FindPartyAgencyFixtureHero();
            if (target == null)
                return LiveCommandResult.Failed("The Party Agency fixture target is unavailable.");

            ReignTemporaryPartyGuestRecord startingRecord =
                ReignServices.TemporaryPartyGuest?.Records
                    .FirstOrDefault(item => item?.HeroStringId == target.StringId);
            double plannedReturnHours = startingRecord != null
                && startingRecord.Phase == ReignTemporaryGuestPhase.Returning
                && startingRecord.ReturnDueDay > CampaignTime.Now.ToDays
                    ? (startingRecord.ReturnDueDay - CampaignTime.Now.ToDays)
                        * CampaignTime.HoursInDay
                    : 0d;
            string plannedReturnHoursBasis = plannedReturnHours > 0d
                ? "return_due_minus_current_day" : string.Empty;
            bool requireProtectedCampFidelity =
                command.Value<bool?>("requireProtectedCampFidelity") ?? true;

            double startDay = CampaignTime.Now.ToDays;
            double targetDay = startDay + days;
            var deferredUnrelatedReviewDueDays =
                new Dictionary<ReignTemporaryPartyGuestRecord, float>();
            bool nativeSettlementWaitUsed = false;
            await ReignMainThread.InvokeAsync(() =>
            {
                ReignTemporaryPartyGuestCampaignBehavior behavior =
                    ReignServices.TemporaryPartyGuest;
                if (behavior != null)
                {
                    foreach (ReignTemporaryPartyGuestRecord unrelated in behavior.Records
                        .Where(item => item != null
                            && !string.Equals(item.HeroStringId, target.StringId,
                                StringComparison.OrdinalIgnoreCase)
                            && item.Phase == ReignTemporaryGuestPhase.Active
                            && item.ReviewDueDay > startDay
                            && item.ReviewDueDay <= targetDay))
                    {
                        deferredUnrelatedReviewDueDays[unrelated] = unrelated.ReviewDueDay;
                        unrelated.ReviewDueDay = (float)(targetDay + 1d);
                    }
                }
                nativeSettlementWaitUsed = TryStartPartyAgencyNativeSettlementWait();
            }).ConfigureAwait(false);
            if (!nativeSettlementWaitUsed)
            {
                await ReignMainThread.InvokeAsync(() =>
                {
                    foreach (KeyValuePair<ReignTemporaryPartyGuestRecord, float> item
                        in deferredUnrelatedReviewDueDays)
                        if (item.Key != null) item.Key.ReviewDueDay = item.Value;
                }).ConfigureAwait(false);
                return LiveCommandResult.Failed(
                    "Party Agency native time advancement requires Bannerlord's safe settlement wait menu.");
            }
            DateTime deadline = DateTime.UtcNow.AddSeconds(Math.Max(120,
                Math.Min(3600, command.Value<int?>("timeoutSeconds") ?? 900)));
            try
            {
                while (CampaignTime.Now.ToDays < targetDay && DateTime.UtcNow < deadline)
                {
                    await Task.Delay(250).ConfigureAwait(false);
                    await ReignMainThread.InvokeAsync(() =>
                    {
                        if (nativeSettlementWaitUsed)
                        {
                            GameMenu waitMenu = TaleWorlds.CampaignSystem.Campaign.Current
                                ?.CurrentMenuContext?.GameMenu;
                            if (waitMenu?.IsWaitMenu == true && !waitMenu.IsWaitActive)
                                waitMenu.StartWait();
                        }
                        ReignServices.TemporaryPartyGuest?.ApplicationTick();
                        if (caseId == "PA-NATIVE-015" && plannedReturnHours <= 0d)
                        {
                            ReignTemporaryPartyGuestRecord returningRecord =
                                ReignServices.TemporaryPartyGuest?.Records
                                    .FirstOrDefault(item => item?.HeroStringId == target.StringId);
                            double currentDay = CampaignTime.Now.ToDays;
                            if (returningRecord?.Phase == ReignTemporaryGuestPhase.Returning
                                && returningRecord.ReturnDueDay > currentDay)
                            {
                                plannedReturnHours = (returningRecord.ReturnDueDay - currentDay)
                                    * CampaignTime.HoursInDay;
                                plannedReturnHoursBasis =
                                    "return_due_observed_on_returning_transition";
                            }
                        }
                    }).ConfigureAwait(false);
                    ReignTemporaryPartyGuestRecord current =
                        ReignServices.TemporaryPartyGuest?.Records
                            .FirstOrDefault(record => record?.HeroStringId == target.StringId
                                && record.Phase == ReignTemporaryGuestPhase.ReviewDue);
                    if (current != null) break;
                }
            }
            finally
            {
                await ReignMainThread.InvokeAsync(() =>
                {
                    GameMenu waitMenu = TaleWorlds.CampaignSystem.Campaign.Current
                        ?.CurrentMenuContext?.GameMenu;
                    if (nativeSettlementWaitUsed && waitMenu?.IsWaitMenu == true
                        && waitMenu.IsWaitActive)
                        waitMenu.EndWait();
                    TaleWorlds.CampaignSystem.Campaign.Current.TimeControlMode =
                        CampaignTimeControlMode.Stop;
                    foreach (KeyValuePair<ReignTemporaryPartyGuestRecord, float> item
                        in deferredUnrelatedReviewDueDays)
                        if (item.Key != null) item.Key.ReviewDueDay = item.Value;
                }).ConfigureAwait(false);
            }

            JObject after = await ReignMainThread.InvokeAsync(() =>
                CapturePartyAgencyNativeState(target)).ConfigureAwait(false);
            JObject before = evidence["beforeNativeState"] as JObject ?? new JObject();
            bool campFidelity = !requireProtectedCampFidelity
                || PartyAgencyNativeStateFidelity(before, after);
            JObject safetyEvidence = caseId == "PA-NATIVE-010"
                ? await ReignMainThread.InvokeAsync(() =>
                {
                    ReignTemporaryPartyGuestRecord liveRecord =
                        ReignServices.TemporaryPartyGuest?.Records
                            .FirstOrDefault(item => item?.HeroStringId == target.StringId);
                    MobileParty camp = MobileParty.All.FirstOrDefault(party => party != null
                        && string.Equals(party.StringId, liveRecord?.SourcePartyStringId,
                            StringComparison.OrdinalIgnoreCase));
                    return ReignTemporaryPartyGuestPatches.BuildProtectedCampSafetyEvidence(
                        camp, target.Clan);
                }).ConfigureAwait(false)
                : null;
            bool financeAndInteractionNeutral = safetyEvidence == null
                || safetyEvidence.Value<bool?>("ok") == true;
            ReignTemporaryPartyGuestRecord record =
                ReignServices.TemporaryPartyGuest?.Records
                    .FirstOrDefault(item => item?.HeroStringId == target.StringId);
            bool reached = CampaignTime.Now.ToDays >= targetDay;
            bool reviewBoundary = record?.Phase == ReignTemporaryGuestPhase.ReviewDue;
            bool returnDurationMatches = caseId != "PA-NATIVE-015"
                || PartyAgencyReturnDurationMatches(variant, plannedReturnHours);
            bool ok = (reached || reviewBoundary) && campFidelity
                && financeAndInteractionNeutral
                && returnDurationMatches;
            evidence["ok"] = ok;
            evidence["afterNativeState"] = after;
            evidence["financeAndInteractionSafety"] = safetyEvidence;
            evidence["timeAdvance"] = new JObject
            {
                ["startDay"] = startDay,
                ["requestedTargetDay"] = targetDay,
                ["completedDay"] = CampaignTime.Now.ToDays,
                ["requestedDays"] = days,
                ["nativeSettlementWaitUsed"] = nativeSettlementWaitUsed,
                ["deferredUnrelatedReviewCount"] = deferredUnrelatedReviewDueDays.Count,
                ["reached"] = reached,
                ["reviewBoundaryReached"] = reviewBoundary,
                ["protectedCampFidelityRequired"] = requireProtectedCampFidelity,
                ["campFidelity"] = campFidelity,
                ["financeAndInteractionNeutral"] = financeAndInteractionNeutral,
                ["plannedReturnHours"] = plannedReturnHours,
                ["plannedReturnHoursBasis"] = plannedReturnHoursBasis,
                ["returnDurationMatches"] = returnDurationMatches,
                ["phase"] = record?.Phase.ToString() ?? string.Empty
            };
            PartyAgencyNativeEvidence[key] = evidence;
            return ok
                ? LiveCommandResult.Completed(
                    "Native campaign time advanced and Party Agency fixture invariants held.",
                    evidence)
                : LiveCommandResult.Failed(
                    "Party Agency time advancement or native fidelity failed.", evidence);
        }

        private static bool TryStartPartyAgencyNativeSettlementWait()
        {
            TaleWorlds.CampaignSystem.Campaign campaign =
                TaleWorlds.CampaignSystem.Campaign.Current;
            Settlement settlement = Hero.MainHero?.CurrentSettlement;
            GameMenu waitMenu = campaign?.CurrentMenuContext?.GameMenu;
            if (campaign == null || settlement == null
                || (!settlement.IsTown && !settlement.IsCastle) || waitMenu == null)
                return false;
            if (!waitMenu.IsWaitMenu)
            {
                int waitOptionIndex = -1;
                for (int index = 0; index < waitMenu.MenuItemAmount; index++)
                {
                    string optionId = waitMenu.GetMenuOptionIdString(index) ?? string.Empty;
                    if (optionId.Equals("town_wait", StringComparison.OrdinalIgnoreCase)
                        || optionId.IndexOf("wait", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        waitOptionIndex = index;
                        break;
                    }
                }
                if (waitOptionIndex < 0) return false;
                GameMenuOption waitOption = waitMenu.GetGameMenuOption(waitOptionIndex);
                if (waitOption == null) return false;
                waitOption.RunConsequence(campaign.CurrentMenuContext);
            }
            waitMenu = campaign.CurrentMenuContext?.GameMenu;
            if (waitMenu == null || !waitMenu.IsWaitMenu) return false;
            waitMenu.StartWait();
            return true;
        }

        private static Task<LiveCommandResult> AggregatePartyAgencyFixturesAsync(
            JObject command)
        {
            if (!PartyAgencyDisposableCommandAuthorized(command, out string activeSave,
                    out LiveCommandResult failure))
                return Task.FromResult(failure);
            return ReignMainThread.InvokeAsync(() =>
            {
                string runId = command.Value<string>("runId") ?? _activeRunId ?? string.Empty;
                string caseId = command.Value<string>("caseId") ?? string.Empty;
                string variant = (command.Value<string>("variant") ?? string.Empty)
                    .Trim().ToLowerInvariant();
                string[] fixtureVariants = (command["fixtureVariants"] as JArray)?
                    .Values<string>().Where(value => !string.IsNullOrWhiteSpace(value))
                    .ToArray() ?? Array.Empty<string>();
                var receipts = new JArray();
                var heroIds = new JArray();
                foreach (string fixtureVariant in fixtureVariants)
                {
                    string fixtureKey = runId + "|" + caseId + "|"
                        + fixtureVariant.Trim().ToLowerInvariant();
                    if (!PartyAgencyNativeEvidence.TryGetValue(fixtureKey,
                            out JObject receipt)
                        || receipt.Value<bool?>("ok") != true)
                        return LiveCommandResult.Failed(
                            "A required game-generated Party Agency guest fixture receipt is missing.");
                    receipts.Add(receipt.Value<string>("receiptId") ?? string.Empty);
                    heroIds.Add(receipt.Value<string>("targetHeroId") ?? string.Empty);
                }
                string[] distinctHeroes = heroIds.Values<string>()
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                bool ok = fixtureVariants.Length >= 2
                    && distinctHeroes.Length == fixtureVariants.Length
                    && distinctHeroes.All(heroId =>
                        ReignServices.TemporaryPartyGuest?.Records.Any(
                            record => record?.HeroStringId == heroId
                                && record.Phase == ReignTemporaryGuestPhase.Active) == true);
                JObject evidence = new JObject
                {
                    ["ok"] = ok,
                    ["receiptId"] = "party_agency_native_" + Guid.NewGuid().ToString("N"),
                    ["operation"] = "party_agency_aggregate_fixtures",
                    ["caseId"] = caseId,
                    ["variant"] = variant,
                    ["activeSaveName"] = activeSave,
                    ["fixtureReceiptIds"] = receipts,
                    ["targetHeroIds"] = heroIds,
                    ["distinctActiveGuestCount"] = distinctHeroes.Length,
                    ["directGuestActionInvoked"] = false
                };
                PartyAgencyNativeEvidence[runId + "|" + caseId + "|" + variant] = evidence;
                return ok
                    ? LiveCommandResult.Completed(
                        "Two independently consenting native guests are ready for the shared review boundary.", evidence)
                    : LiveCommandResult.Failed(
                        "The simultaneous native guest fixtures were not independently active.", evidence);
            });
        }

        private static async Task<LiveCommandResult> ExercisePartyAgencyReviewProviderResultAsync(
            JObject command)
        {
            if (!PartyAgencyDisposableCommandAuthorized(command, out string activeSave,
                    out LiveCommandResult failure))
                return failure;
            string runId = command.Value<string>("runId") ?? _activeRunId ?? string.Empty;
            string caseId = command.Value<string>("caseId") ?? string.Empty;
            string variant = (command.Value<string>("variant") ?? string.Empty)
                .Trim().ToLowerInvariant();
            string key = runId + "|" + caseId + "|" + variant;
            if (!PartyAgencyNativeEvidence.TryGetValue(key, out JObject evidence))
                return LiveCommandResult.Failed(
                    "The matching game-generated Party Agency provider fixture receipt is unavailable.");
            Hero target = FindPartyAgencyFixtureHero();
            if (target == null) return LiveCommandResult.Failed(
                "The Party Agency review-provider fixture target is unavailable.");
            bool notified = await ReignMainThread.InvokeAsync(() =>
                ReignServices.TemporaryPartyGuest
                    ?.NotifyDisposableLiveTestReviewProviderFailure(target.StringId) == true)
                .ConfigureAwait(false);
            if (!notified) return LiveCommandResult.Failed(
                "The guest was not waiting at a mandatory review boundary.", evidence);

            await Task.Delay(3500).ConfigureAwait(false);
            await ReignMainThread.InvokeAsync(() =>
                ReignServices.TemporaryPartyGuest?.ApplicationTick())
                .ConfigureAwait(false);
            ReignTemporaryPartyGuestRecord retryRecord =
                ReignServices.TemporaryPartyGuest?.Records
                    .FirstOrDefault(item => item?.HeroStringId == target.StringId);
            bool retryPreserved = retryRecord?.Phase == ReignTemporaryGuestPhase.ReviewDue
                && (retryRecord.LastReviewReason ?? string.Empty)
                    .IndexOf("Retry or let the guest depart", StringComparison.OrdinalIgnoreCase) >= 0;
            bool departureSelected = false;
            if (variant == "provider_departure" && retryPreserved)
                departureSelected = await ReignMainThread.InvokeAsync(() =>
                    ReignServices.TemporaryPartyGuest
                        ?.LetDisposableLiveTestReviewGuestDepart(target.StringId) == true)
                    .ConfigureAwait(false);
            ReignTemporaryPartyGuestRecord finalRecord =
                ReignServices.TemporaryPartyGuest?.Records
                    .FirstOrDefault(item => item?.HeroStringId == target.StringId);
            bool expectedPhase = variant == "provider_departure"
                ? departureSelected && (finalRecord?.Phase == ReignTemporaryGuestPhase.Returning
                    || finalRecord?.Phase == ReignTemporaryGuestPhase.Completed)
                : finalRecord?.Phase == ReignTemporaryGuestPhase.ReviewDue;
            bool paused = TaleWorlds.CampaignSystem.Campaign.Current?.TimeControlMode
                == CampaignTimeControlMode.Stop;
            bool ok = retryPreserved && expectedPhase && paused;
            evidence["ok"] = ok;
            evidence["receiptId"] = "party_agency_native_" + Guid.NewGuid().ToString("N");
            evidence["operation"] = "party_agency_review_provider_result";
            evidence["activeSaveName"] = activeSave;
            evidence["providerFailureDidNotImplyDecision"] = retryPreserved;
            evidence["explicitDepartureSelected"] = departureSelected;
            evidence["timePaused"] = paused;
            evidence["phase"] = finalRecord?.Phase.ToString() ?? string.Empty;
            PartyAgencyNativeEvidence[key] = evidence;
            return ok
                ? LiveCommandResult.Completed(
                    "The provider failure preserved the mandatory review and the selected retry/departure path was verified.", evidence)
                : LiveCommandResult.Failed(
                    "The review provider-failure safeguard did not preserve its required state.", evidence);
        }

        private static Task<LiveCommandResult> ExercisePartyAgencyRecoveryFixtureAsync(
            JObject command)
        {
            if (!PartyAgencyDisposableCommandAuthorized(command, out string activeSave,
                    out LiveCommandResult failure))
                return Task.FromResult(failure);
            return ReignMainThread.InvokeAsync(() =>
            {
                string runId = command.Value<string>("runId") ?? _activeRunId ?? string.Empty;
                string caseId = command.Value<string>("caseId") ?? string.Empty;
                string variant = (command.Value<string>("variant") ?? string.Empty)
                    .Trim().ToLowerInvariant();
                string key = runId + "|" + caseId + "|" + variant;
                if (!PartyAgencyNativeEvidence.TryGetValue(key, out JObject evidence))
                    return LiveCommandResult.Failed(
                        "The matching game-generated Party Agency recovery receipt is unavailable.");
                Hero target = FindPartyAgencyFixtureHero();
                ReignTemporaryPartyGuestCampaignBehavior behavior =
                    ReignServices.TemporaryPartyGuest;
                ReignTemporaryPartyGuestRecord record = behavior?.Records
                    .FirstOrDefault(item => item?.HeroStringId == target?.StringId);
                if (target == null || record == null)
                    return LiveCommandResult.Failed(
                        "The natural-language invitation did not create a recoverable guest record.", evidence);

                ReignTemporaryGuestPhase startingPhase = record.Phase;
                bool terminalGuestState = variant == "death" || variant == "capture";
                bool naturalDepartureReachedReturn = record.Phase == ReignTemporaryGuestPhase.Returning
                    || record.Phase == ReignTemporaryGuestPhase.Completed;
                bool recoveryFixtureStagedReturn = false;
                bool recoveryMutationAppliedBeforeReturn = false;
                bool recoveryNativeStateObservedBeforeReturn = false;
                MobileParty releasedSource = null;
                string releasedSourceId = string.Empty;
                if (!terminalGuestState && !naturalDepartureReachedReturn)
                {
                    bool canStageReturn = record.Phase == ReignTemporaryGuestPhase.Active
                        || record.Phase == ReignTemporaryGuestPhase.ReviewDue
                        || record.Phase == ReignTemporaryGuestPhase.Departing;
                    if (!canStageReturn)
                        return LiveCommandResult.Failed(
                            "The guest is not in a state that the guarded recovery fixture can advance to return.",
                            evidence);
                    record.Phase = ReignTemporaryGuestPhase.Departing;
                    record.LastReviewReason =
                        "Disposable recovery fixture staged return after preserving the natural departure response.";
                    record.LastStateChangeDay = (float)CampaignTime.Now.ToDays;
                    if (variant == "missing_source_party" || variant == "invalid_return_target")
                    {
                        releasedSourceId = record.SourcePartyStringId;
                        releasedSource = behavior.ReleaseDisposableLiveTestSourceParty(target.StringId);
                        record.SourcePartyStringId = string.Empty;
                        if (releasedSource?.IsActive == true)
                            DestroyPartyAction.Apply(null, releasedSource);
                        bool sourceRemoved = releasedSource == null || !releasedSource.IsActive;
                        if (variant == "missing_source_party")
                        {
                            record.SourcePartyStringId = releasedSourceId;
                            recoveryNativeStateObservedBeforeReturn = sourceRemoved;
                        }
                        else
                        {
                            record.ReturnSettlementStringId = "reign_missing_return_target";
                            recoveryNativeStateObservedBeforeReturn = sourceRemoved
                                && string.IsNullOrWhiteSpace(record.SourcePartyStringId)
                                && record.ReturnSettlementStringId == "reign_missing_return_target";
                        }
                        record.ReturnDueDay = (float)CampaignTime.Now.ToDays;
                        recoveryMutationAppliedBeforeReturn = true;
                    }
                    behavior.RunDisposableLiveTestHourlyTick();
                    recoveryFixtureStagedReturn = true;
                    if (record.Phase != ReignTemporaryGuestPhase.Returning
                        && record.Phase != ReignTemporaryGuestPhase.Completed)
                        return LiveCommandResult.Failed(
                            "The guarded recovery fixture could not advance the preserved guest state to return.",
                            evidence);
                }

                switch (variant)
                {
                    case "death":
                        if (target.IsAlive)
                            KillCharacterAction.ApplyByRemove(target, false, true);
                        break;
                    case "capture":
                        if (!target.IsPrisoner)
                            TakePrisonerAction.Apply(PartyBase.MainParty, target);
                        break;
                    case "missing_source_party":
                        if (!recoveryMutationAppliedBeforeReturn)
                        {
                            releasedSourceId = record.SourcePartyStringId;
                            releasedSource = behavior.ReleaseDisposableLiveTestSourceParty(target.StringId);
                            record.SourcePartyStringId = string.Empty;
                            if (releasedSource?.IsActive == true)
                                DestroyPartyAction.Apply(null, releasedSource);
                            record.SourcePartyStringId = releasedSourceId;
                            recoveryNativeStateObservedBeforeReturn = releasedSource == null
                                || !releasedSource.IsActive;
                            recoveryMutationAppliedBeforeReturn = true;
                        }
                        record.ReturnDueDay = (float)CampaignTime.Now.ToDays;
                        break;
                    case "invalid_return_target":
                        if (!recoveryMutationAppliedBeforeReturn)
                        {
                            releasedSource = behavior.ReleaseDisposableLiveTestSourceParty(target.StringId);
                            record.SourcePartyStringId = string.Empty;
                            if (releasedSource?.IsActive == true)
                                DestroyPartyAction.Apply(null, releasedSource);
                            record.ReturnSettlementStringId = "reign_missing_return_target";
                            recoveryNativeStateObservedBeforeReturn = (releasedSource == null
                                    || !releasedSource.IsActive)
                                && string.IsNullOrWhiteSpace(record.SourcePartyStringId)
                                && record.ReturnSettlementStringId == "reign_missing_return_target";
                            recoveryMutationAppliedBeforeReturn = true;
                        }
                        record.ReturnDueDay = (float)CampaignTime.Now.ToDays;
                        break;
                    default:
                        return LiveCommandResult.Failed(
                            "Unsupported Party Agency recovery variant '" + variant + "'.", evidence);
                }
                behavior.RunDisposableLiveTestHourlyTick();
                bool completed = record.Phase == ReignTemporaryGuestPhase.Completed;
                MobileParty source = string.IsNullOrWhiteSpace(record.SourcePartyStringId)
                    ? null : MobileParty.All.FirstOrDefault(party => party != null
                        && string.Equals(party.StringId, record.SourcePartyStringId,
                            StringComparison.OrdinalIgnoreCase));
                bool released = source == null || !behavior.IsProtectedCamp(source);
                bool nativeStateReached = variant == "death" ? target.IsDead
                    : variant == "capture" ? target.IsPrisoner
                    : recoveryNativeStateObservedBeforeReturn;
                bool safeFallback = variant != "invalid_return_target"
                    || target.CurrentSettlement != null || target.PartyBelongedTo != null;
                bool ok = completed && released && nativeStateReached && safeFallback;
                evidence["ok"] = ok;
                evidence["receiptId"] = "party_agency_native_" + Guid.NewGuid().ToString("N");
                evidence["operation"] = "party_agency_recovery_fixture";
                evidence["activeSaveName"] = activeSave;
                evidence["nativeRecoveryVariant"] = variant;
                evidence["recoveryStartingPhase"] = startingPhase.ToString();
                evidence["naturalDepartureReachedReturn"] = naturalDepartureReachedReturn;
                evidence["recoveryFixtureStagedReturn"] = recoveryFixtureStagedReturn;
                evidence["recoveryMutationAppliedBeforeReturn"] = recoveryMutationAppliedBeforeReturn;
                evidence["recoveryNativeStateObservedBeforeReturn"] =
                    recoveryNativeStateObservedBeforeReturn;
                evidence["nativeStateReached"] = nativeStateReached;
                evidence["phase"] = record.Phase.ToString();
                evidence["campReleased"] = released;
                evidence["safeFallbackResolved"] = safeFallback;
                PartyAgencyNativeEvidence[key] = evidence;
                return ok
                    ? LiveCommandResult.Completed(
                        "The native unavailable or missing-target recovery completed and released protected state.", evidence)
                    : LiveCommandResult.Failed(
                        "The Party Agency native recovery did not complete cleanly.", evidence);
            });
        }

        private static Task<LiveCommandResult> VerifyPartyAgencyUnrelatedStateAsync(
            JObject command)
        {
            if (!PartyAgencyDisposableCommandAuthorized(command, out string activeSave,
                    out LiveCommandResult failure))
                return Task.FromResult(failure);
            return ReignMainThread.InvokeAsync(() =>
            {
                string runId = command.Value<string>("runId") ?? _activeRunId ?? string.Empty;
                string caseId = command.Value<string>("caseId") ?? string.Empty;
                string variant = (command.Value<string>("variant") ?? string.Empty)
                    .Trim().ToLowerInvariant();
                string key = runId + "|" + caseId + "|" + variant;
                if (!PartyAgencyNativeEvidence.TryGetValue(key, out JObject evidence))
                    return LiveCommandResult.Failed(
                        "The matching game-generated Party Agency regression receipt is unavailable.");
                Hero target = FindPartyAgencyFixtureHero();
                JObject baseline = evidence["unrelatedStateBaseline"] as JObject;
                JObject current = CapturePartyAgencyUnrelatedState(target);
                bool unchanged = baseline != null && JToken.DeepEquals(baseline, current);
                JObject nativeBefore = evidence["beforeNativeState"] as JObject ?? new JObject();
                JObject nativeAfter = CapturePartyAgencyNativeState(target);
                bool equipmentProtected = nativeAfter.Value<bool?>("temporaryGuestEquipmentLocked") == true
                    && nativeAfter.Value<bool?>("equipmentChangeAllowed") != true
                    && string.Equals(nativeBefore.Value<string>("battleEquipmentSignature"),
                        nativeAfter.Value<string>("battleEquipmentSignature"), StringComparison.Ordinal)
                    && string.Equals(nativeBefore.Value<string>("civilianEquipmentSignature"),
                        nativeAfter.Value<string>("civilianEquipmentSignature"), StringComparison.Ordinal);
                bool ok = unchanged && equipmentProtected;
                evidence["ok"] = ok;
                evidence["receiptId"] = "party_agency_native_" + Guid.NewGuid().ToString("N");
                evidence["operation"] = "party_agency_verify_unrelated_state";
                evidence["activeSaveName"] = activeSave;
                evidence["unrelatedStateBaseline"] = baseline;
                evidence["unrelatedStateAfter"] = current;
                evidence["unrelatedStateUnchanged"] = unchanged;
                evidence["guestEquipmentAfter"] = nativeAfter;
                evidence["guestEquipmentProtected"] = equipmentProtected;
                PartyAgencyNativeEvidence[key] = evidence;
                return ok
                    ? LiveCommandResult.Completed(
                        "Temporary guest consent left unrelated state unchanged and locked both battle and civilian equipment through Bannerlord's native permission gate.", evidence)
                    : LiveCommandResult.Failed(
                        "An unrelated state changed or the temporary guest's equipment was not fully protected.", evidence);
            });
        }
    }
}
