using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using ReignBeta.Integration;
using ReignBeta.PartyAgency;
using ReignBeta.Runtime;
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

namespace ReignBeta.Campaign
{
    public static partial class ReignLiveInteractionTestHost
    {
        internal static bool IsDisposablePartyAgencyFixtureConversationActive(Hero hero)
        {
            if (hero == null || _individualVm == null
                || string.IsNullOrWhiteSpace(_activeRunId)
                || !string.Equals(_partyAgencyFixtureTargetId, hero.StringId,
                    StringComparison.OrdinalIgnoreCase))
                return false;

            string activeSave = ActiveSaveName();
            return activeSave.StartsWith("ReignTest_", StringComparison.Ordinal)
                && activeSave.EndsWith("_Current", StringComparison.Ordinal)
                && TaleWorlds.CampaignSystem.Campaign.Current?.TimeControlMode
                    == CampaignTimeControlMode.Stop;
        }

        private static async Task<LiveCommandResult> PreparePartyAgencyFixtureAsync(
            JObject command)
        {
            if (command?.Value<bool?>("confirmDisposableCampaign") != true)
                return LiveCommandResult.Failed(
                    "prepare_party_agency_fixture requires confirmDisposableCampaign=true.");
            string expectedSave = command.Value<string>("expectedSaveName") ?? string.Empty;
            string activeSave = ActiveSaveName();
            if (!expectedSave.StartsWith("ReignTest_", StringComparison.Ordinal)
                || !expectedSave.EndsWith("_Current", StringComparison.Ordinal)
                || !string.Equals(expectedSave, activeSave, StringComparison.Ordinal))
                return LiveCommandResult.Failed(
                    "The Party Agency fixture is not authorized for the exact active enrolled disposable save.",
                    new JObject
                    {
                        ["expectedSaveName"] = expectedSave,
                        ["activeSaveName"] = activeSave
                    });
            if (TaleWorlds.CampaignSystem.Campaign.Current?.TimeControlMode
                != CampaignTimeControlMode.Stop)
                return LiveCommandResult.Failed(
                    "Native campaign time must be paused before preparing a Party Agency fixture.");
            if (_individualVm != null || _partyVm != null || _eventVm != null)
                await CloseSessionAsync(false).ConfigureAwait(false);

            return await ReignMainThread.InvokeAsync(() =>
            {
                try
                {
                    string caseId = command.Value<string>("caseId") ?? string.Empty;
                    string variant = (command.Value<string>("variant") ?? string.Empty)
                        .Trim().ToLowerInvariant();
                    string fixtureRole = (command.Value<string>("fixtureRole") ?? variant)
                        .Trim().ToLowerInvariant();
                    string runId = command.Value<string>("runId") ?? _activeRunId ?? string.Empty;
                    string candidateHint = command.Value<string>("candidateHint") ?? string.Empty;
                    Settlement anchor = Settlement.CurrentSettlement
                        ?? MobileParty.MainParty?.CurrentSettlement
                        ?? Settlement.All.FirstOrDefault(settlement => settlement?.IsFortification == true);
                    if (anchor == null)
                        return LiveCommandResult.Failed(
                            "No safe settlement anchor exists for the native Party Agency fixture.");

                    Hero target = caseId == "PA-NATIVE-018" || caseId == "PA-NATIVE-019"
                        ? SelectPartyAgencyHostilityTarget(candidateHint)
                        : SelectPartyAgencyFixtureTarget(fixtureRole, candidateHint);
                    if (target == null)
                        return LiveCommandResult.Failed(
                            "No native hero can represent Party Agency variant '" + variant + "'.",
                            new JObject
                            {
                                ["variant"] = variant,
                                ["fixtureRole"] = fixtureRole,
                                ["candidateHint"] = candidateHint,
                                ["requiresBaselineReload"] = true
                            });

                    var createdParties = new List<MobileParty>();
                    MobileParty targetParty = target.PartyBelongedTo;
                    MobileParty helperParty = null;
                    switch (fixtureRole)
                    {
                        case "partyless":
                        case "clan_leader":
                        case "kingdom_ruler":
                            break;
                        case "ordinary_party_member":
                            helperParty = CreatePartyAgencyFixtureParty(
                                SelectPartyAgencyHelper(target), anchor, runId, "member_host");
                            createdParties.Add(helperParty);
                            AddHeroToPartyAction.Apply(target, helperParty, false);
                            targetParty = helperParty;
                            break;
                        case "party_leader":
                        case "quest_bound":
                            targetParty = CreatePartyAgencyFixtureParty(
                                target, anchor, runId, "target");
                            createdParties.Add(targetParty);
                            if (fixtureRole == "quest_bound") targetParty.SetPartyUsedByQuest(true);
                            break;
                        case "army_member":
                        case "army_leader":
                            targetParty = CreatePartyAgencyFixtureParty(
                                target, anchor, runId, "target");
                            createdParties.Add(targetParty);
                            Hero helper = SelectPartyAgencyHelper(target);
                            helperParty = CreatePartyAgencyFixtureParty(
                                helper, anchor, runId, "army_helper");
                            createdParties.Add(helperParty);
                            MobileParty leaderParty = fixtureRole == "army_leader"
                                ? targetParty : helperParty;
                            MobileParty memberParty = fixtureRole == "army_leader"
                                ? helperParty : targetParty;
                            Kingdom kingdom = leaderParty?.LeaderHero?.Clan?.Kingdom;
                            if (kingdom == null || memberParty?.MapFaction != kingdom)
                                throw new InvalidOperationException(
                                    "The native army fixture requires two parties in the same kingdom.");
                            var members = new MBList<MobileParty> { memberParty };
                            kingdom.CreateArmy(leaderParty.LeaderHero, anchor,
                                Army.ArmyTypes.Besieger, members);
                            if (leaderParty.Army == null)
                                throw new InvalidOperationException(
                                    "Bannerlord did not create the requested native army formation.");
                            if (memberParty.Army != leaderParty.Army)
                                memberParty.Army = leaderParty.Army;
                            break;
                        case "governor":
                            if (target.GovernorOf == null)
                            {
                                Town town = Settlement.All
                                    .FirstOrDefault(settlement => settlement?.Town != null
                                        && settlement.OwnerClan == target.Clan)?.Town;
                                if (town == null) throw new InvalidOperationException(
                                    "No target-clan town exists for the governor rejection fixture.");
                                if (target.CurrentSettlement != town.Settlement)
                                    EnterSettlementAction.ApplyForCharacterOnly(
                                        target, town.Settlement);
                                ChangeGovernorAction.Apply(town, target);
                                if (target.GovernorOf != town || town.Governor != target)
                                    throw new InvalidOperationException(
                                        "Bannerlord did not install the requested native governor fixture.");
                            }
                            break;
                        case "prisoner":
                            if (!target.IsPrisoner)
                                TakePrisonerAction.Apply(PartyBase.MainParty, target);
                            break;
                        case "dead":
                        case "child":
                            break;
                        case "companion":
                            if (target.CompanionOf == null)
                            {
                                if (Clan.PlayerClan == null)
                                    throw new InvalidOperationException(
                                        "The native companion fixture requires the player clan.");
                                AddCompanionAction.Apply(Clan.PlayerClan, target);
                                if (target.CompanionOf != Clan.PlayerClan)
                                    throw new InvalidOperationException(
                                        "Bannerlord did not install the requested native companion fixture.");
                            }
                            break;
                        default:
                            throw new InvalidOperationException(
                                "Unsupported Party Agency fixture role '" + fixtureRole + "'.");
                    }

                    if (caseId == "PA-NATIVE-015" && targetParty != null)
                        PositionPartyAgencyReturnFixture(targetParty, anchor, variant);

                    _partyAgencyFixtureTargetId = target.StringId;
                    string key = runId + "|" + caseId + "|" + variant;
                    JObject role = PartyAgencyFixtureRoleEvidence(target);
                    JObject evidence = new JObject
                    {
                        ["ok"] = true,
                        ["receiptId"] = "party_agency_native_" + Guid.NewGuid().ToString("N"),
                        ["operation"] = "prepare_party_agency_fixture",
                        ["caseId"] = caseId,
                        ["variant"] = variant,
                        ["fixtureRole"] = fixtureRole,
                        ["targetHeroId"] = target.StringId,
                        ["targetName"] = target.Name?.ToString() ?? string.Empty,
                        ["activeSaveName"] = activeSave,
                        ["anchorSettlementId"] = anchor.StringId,
                        ["createdPartyIds"] = new JArray(createdParties
                            .Where(party => party != null).Select(party => party.StringId)),
                        ["role"] = role,
                        ["beforeNativeState"] = CapturePartyAgencyNativeState(target),
                        ["unrelatedStateBaseline"] = caseId == "PA-NATIVE-021"
                            ? CapturePartyAgencyUnrelatedState(target) : null,
                        ["naturalLanguageDecisionPending"] = true,
                        ["directGuestActionInvoked"] = false,
                        ["requiresBaselineReload"] = true
                    };
                    PartyAgencyNativeEvidence[key] = evidence;
                    return LiveCommandResult.Completed(
                        "The guarded native Party Agency role fixture is ready; guest consent remains pending in production dialogue.",
                        evidence);
                }
                catch (Exception ex)
                {
                    return LiveCommandResult.Failed(
                        "Native Party Agency fixture preparation failed: " + ex.Message,
                        new JObject
                        {
                            ["requiresBaselineReload"] = true,
                            ["directGuestActionInvoked"] = false
                        });
                }
            }).ConfigureAwait(false);
        }

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
                ReignTemporaryPartyGuestCampaignBehavior.Instance?.Records
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
                    ReignTemporaryPartyGuestCampaignBehavior.Instance;
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
                        ReignTemporaryPartyGuestCampaignBehavior.Instance?.ApplicationTick();
                        if (caseId == "PA-NATIVE-015" && plannedReturnHours <= 0d)
                        {
                            ReignTemporaryPartyGuestRecord returningRecord =
                                ReignTemporaryPartyGuestCampaignBehavior.Instance?.Records
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
                        ReignTemporaryPartyGuestCampaignBehavior.Instance?.Records
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
                        ReignTemporaryPartyGuestCampaignBehavior.Instance?.Records
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
                ReignTemporaryPartyGuestCampaignBehavior.Instance?.Records
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
                        ReignTemporaryPartyGuestCampaignBehavior.Instance?.Records.Any(
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
                ReignTemporaryPartyGuestCampaignBehavior.Instance
                    ?.NotifyDisposableLiveTestReviewProviderFailure(target.StringId) == true)
                .ConfigureAwait(false);
            if (!notified) return LiveCommandResult.Failed(
                "The guest was not waiting at a mandatory review boundary.", evidence);

            await Task.Delay(3500).ConfigureAwait(false);
            await ReignMainThread.InvokeAsync(() =>
                ReignTemporaryPartyGuestCampaignBehavior.Instance?.ApplicationTick())
                .ConfigureAwait(false);
            ReignTemporaryPartyGuestRecord retryRecord =
                ReignTemporaryPartyGuestCampaignBehavior.Instance?.Records
                    .FirstOrDefault(item => item?.HeroStringId == target.StringId);
            bool retryPreserved = retryRecord?.Phase == ReignTemporaryGuestPhase.ReviewDue
                && (retryRecord.LastReviewReason ?? string.Empty)
                    .IndexOf("Retry or let the guest depart", StringComparison.OrdinalIgnoreCase) >= 0;
            bool departureSelected = false;
            if (variant == "provider_departure" && retryPreserved)
                departureSelected = await ReignMainThread.InvokeAsync(() =>
                    ReignTemporaryPartyGuestCampaignBehavior.Instance
                        ?.LetDisposableLiveTestReviewGuestDepart(target.StringId) == true)
                    .ConfigureAwait(false);
            ReignTemporaryPartyGuestRecord finalRecord =
                ReignTemporaryPartyGuestCampaignBehavior.Instance?.Records
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
                    ReignTemporaryPartyGuestCampaignBehavior.Instance;
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

        private static Task<LiveCommandResult> PreparePartyAgencyHostilityAsync(
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
                        "The matching game-generated Party Agency hostility receipt is unavailable.");
                Hero target = FindPartyAgencyFixtureHero();
                Kingdom playerKingdom = Hero.MainHero?.Clan?.Kingdom;
                Kingdom originalKingdom = target?.Clan?.Kingdom;
                ReignTemporaryPartyGuestRecord record =
                    ReignTemporaryPartyGuestCampaignBehavior.Instance?.Records
                        .FirstOrDefault(item => item?.HeroStringId == target?.StringId);
                if (record == null || record.Phase != ReignTemporaryGuestPhase.Active
                    || playerKingdom == null || originalKingdom == null
                    || playerKingdom == originalKingdom)
                    return LiveCommandResult.Failed(
                        "The hostility fixture requires an active foreign guest and two kingdoms.", evidence);
                bool createdWar = false;
                if (!FactionManager.IsAtWarAgainstFaction(playerKingdom, originalKingdom))
                {
                    DeclareWarAction.ApplyByDefault(playerKingdom, originalKingdom);
                    createdWar = true;
                }
                ReignTemporaryPartyGuestCampaignBehavior.Instance
                    .RunDisposableLiveTestHourlyStateOnly();
                bool due = record.Phase == ReignTemporaryGuestPhase.ReviewDue
                    && !record.HostilityWarningAcknowledged
                    && string.Equals(record.HostileFactionStringId,
                        originalKingdom.StringId, StringComparison.OrdinalIgnoreCase);
                evidence["ok"] = due;
                evidence["receiptId"] = "party_agency_native_" + Guid.NewGuid().ToString("N");
                evidence["operation"] = "party_agency_prepare_hostility";
                evidence["activeSaveName"] = activeSave;
                evidence["nativeWarCreated"] = createdWar;
                evidence["originalKingdomId"] = originalKingdom.StringId;
                evidence["mandatoryReviewDue"] = due;
                PartyAgencyNativeEvidence[key] = evidence;
                return due
                    ? LiveCommandResult.Completed(
                        "Native war declaration queued the mandatory own-faction review.", evidence)
                    : LiveCommandResult.Failed(
                        "Native hostility did not queue the mandatory review.", evidence);
            });
        }

        private static Task<LiveCommandResult> ExercisePartyAgencyHostileBattleAsync(
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
                        "The matching game-generated Party Agency battle receipt is unavailable.");
                Hero target = FindPartyAgencyFixtureHero();
                ReignTemporaryPartyGuestCampaignBehavior behavior =
                    ReignTemporaryPartyGuestCampaignBehavior.Instance;
                ReignTemporaryPartyGuestRecord record = behavior?.Records
                    .FirstOrDefault(item => item?.HeroStringId == target?.StringId);
                if (target == null || record == null)
                    return LiveCommandResult.Failed(
                        "The hostile-battle guest record is unavailable.", evidence);

                Settlement anchor = Settlement.All.FirstOrDefault(settlement => settlement != null
                    && settlement.IsFortification && settlement.MapFaction == target.MapFaction)
                    ?? Settlement.All.FirstOrDefault(settlement => settlement?.IsFortification == true);
                Hero enemyLeader = SelectPartyAgencyHelper(target);
                MobileParty enemy = CreatePartyAgencyFixtureParty(enemyLeader, anchor,
                    runId + variant, "hostile_battle");
                CampaignVec2 playerPosition = MobileParty.MainParty.Position;
                Settlement playerSettlement = MobileParty.MainParty.CurrentSettlement
                    ?? Settlement.CurrentSettlement;
                bool playerAtSea = MobileParty.MainParty.IsCurrentlyAtSea;
                bool enemyAtSea = enemy.IsCurrentlyAtSea;
                SiegeEvent siege = null;
                MapEvent mapEvent = null;
                bool transientExclusion = false;
                bool pendingBanishment = false;
                bool verifiedPostBattle = false;
                bool playerVictory = false;
                bool playerSafetyPreserved = false;
                bool typeMatched = false;
                bool protectedCampExcluded = false;
                try
                {
                    if (MobileParty.MainParty.CurrentSettlement != null)
                        LeaveSettlementAction.ApplyForParty(MobileParty.MainParty);
                    if (variant == "raid")
                    {
                        Settlement village = Settlement.All.FirstOrDefault(settlement =>
                            settlement?.IsVillage == true && settlement.MapFaction == target.MapFaction);
                        if (village == null) throw new InvalidOperationException(
                            "No hostile village exists for the native raid fixture.");
                        MobileParty.MainParty.Position = village.GatePosition;
                        StartBattleAction.ApplyStartRaid(MobileParty.MainParty, village);
                    }
                    else if (variant == "siege")
                    {
                        Settlement fortification = Settlement.All.FirstOrDefault(settlement =>
                            settlement?.IsFortification == true && !settlement.IsUnderSiege
                            && settlement.MapFaction == target.MapFaction);
                        if (fortification == null) throw new InvalidOperationException(
                            "No hostile fortification exists for the native siege fixture.");
                        MobileParty.MainParty.Position = fortification.GatePosition;
                        siege = TaleWorlds.CampaignSystem.Campaign.Current.SiegeEventManager
                            .StartSiegeEvent(fortification, MobileParty.MainParty);
                        StartBattleAction.ApplyStartAssaultAgainstWalls(
                            MobileParty.MainParty, fortification);
                    }
                    else
                    {
                        if (variant == "naval")
                        {
                            MobileParty seaParty = MobileParty.All.FirstOrDefault(party =>
                                party != null && party.IsCurrentlyAtSea);
                            if (seaParty == null) throw new InvalidOperationException(
                                "No native sea position exists for the naval fixture.");
                            MobileParty.MainParty.Position = seaParty.Position;
                            enemy.Position = seaParty.Position;
                            MobileParty.MainParty.IsCurrentlyAtSea = true;
                            enemy.IsCurrentlyAtSea = true;
                        }
                        else enemy.Position = MobileParty.MainParty.Position;
                        StartBattleAction.ApplyStartBattle(MobileParty.MainParty, enemy);
                    }
                    mapEvent = PartyBase.MainParty.MapEvent ?? enemy.MapEvent;
                    if (mapEvent == null || !mapEvent.IsPlayerMapEvent)
                        throw new InvalidOperationException(
                            "Bannerlord did not create the required native player map event.");
                    MobileParty protectedCamp = MobileParty.All.FirstOrDefault(party =>
                        party?.StringId == record.SourcePartyStringId);
                    protectedCampExcluded = protectedCamp != null
                        && behavior.IsProtectedCamp(protectedCamp)
                        && protectedCamp.MapEvent == null
                        && !mapEvent.InvolvedParties.Any(party => party == protectedCamp.Party);
                    transientExclusion = record.TemporarilyExcludedFromBattle;
                    pendingBanishment = record.OwnFactionCombatPending
                        && record.Phase == ReignTemporaryGuestPhase.HostileBanishmentPending;
                    typeMatched = variant == "naval" ? mapEvent.IsNavalMapEvent
                        : variant == "raid" ? mapEvent.EventType.ToString()
                            .IndexOf("raid", StringComparison.OrdinalIgnoreCase) >= 0
                        : variant == "siege" ? !mapEvent.IsFieldBattle
                            && mapEvent.MapEventSettlement?.IsFortification == true
                        : mapEvent.IsFieldBattle && !mapEvent.IsNavalMapEvent;
                    if (variant == "auto_resolve") mapEvent.IsPlayerSimulation = true;
                    mapEvent.SetOverrideWinner(mapEvent.PlayerSide);
                    mapEvent.DoSurrender(mapEvent.PlayerSide.GetOppositeSide());
                    playerVictory = mapEvent.HasWinner
                        && mapEvent.WinningSide == mapEvent.PlayerSide
                        && mapEvent.Winner?.Parties.Any(
                            party => party.Party == PartyBase.MainParty) == true;
                    playerSafetyPreserved = Hero.MainHero?.IsPrisoner != true
                        && MobileParty.MainParty?.IsActive == true;
                    verifiedPostBattle = playerVictory && playerSafetyPreserved;
                    if (PlayerEncounter.Current != null) PlayerEncounter.Finish();
                    if (pendingBanishment && verifiedPostBattle)
                        behavior.ApplyVerifiedOwnFactionCombatResult(record, target);
                }
                finally
                {
                    if (PlayerEncounter.Current != null) PlayerEncounter.Finish();
                    MobileParty.MainParty.IsCurrentlyAtSea = playerAtSea;
                    MobileParty.MainParty.Position = playerSettlement?.GatePosition
                        ?? playerPosition;
                    if (playerSettlement != null
                        && MobileParty.MainParty.CurrentSettlement == null
                        && MobileParty.MainParty.MapEvent == null)
                        EnterSettlementAction.ApplyForParty(
                            MobileParty.MainParty, playerSettlement);
                    if (enemy?.IsActive == true && enemy.MapEvent == null)
                    {
                        enemy.IsCurrentlyAtSea = enemyAtSea;
                        DestroyPartyAction.Apply(null, enemy);
                    }
                }
                bool unwarned = caseId == "PA-NATIVE-018";
                bool outcome = unwarned
                    ? transientExclusion && !record.HostilityWarningAcknowledged
                    : pendingBanishment && verifiedPostBattle
                        && target.Clan == Clan.PlayerClan
                        && record.Phase == ReignTemporaryGuestPhase.Completed;
                bool ok = typeMatched && protectedCampExcluded && outcome;
                evidence["ok"] = ok;
                evidence["receiptId"] = "party_agency_native_" + Guid.NewGuid().ToString("N");
                evidence["operation"] = "party_agency_hostile_battle";
                evidence["activeSaveName"] = activeSave;
                evidence["requestedBattleVariant"] = variant;
                evidence["nativeBattleType"] = mapEvent?.EventType.ToString() ?? string.Empty;
                evidence["nativeNavalBattle"] = mapEvent?.IsNavalMapEvent == true;
                evidence["battleTypeMatched"] = typeMatched;
                evidence["protectedCampExcludedFromBattle"] = protectedCampExcluded;
                evidence["temporarilyExcludedFromBattle"] = transientExclusion;
                evidence["hostileBanishmentPendingObserved"] = pendingBanishment;
                evidence["playerSideWon"] = playerVictory;
                evidence["playerSafetyPreserved"] = playerSafetyPreserved;
                evidence["verifiedPostBattleResult"] = verifiedPostBattle;
                evidence["banishedToPlayerClan"] = target.Clan == Clan.PlayerClan;
                evidence["phase"] = record.Phase.ToString();
                evidence["siegeFixtureCreated"] = siege != null;
                PartyAgencyNativeEvidence[key] = evidence;
                return ok
                    ? LiveCommandResult.Completed(
                        "The native own-faction battle safeguard completed for " + variant + ".", evidence)
                    : LiveCommandResult.Failed(
                        "The native own-faction battle safeguard failed for " + variant + ".", evidence);
            });
        }

        private static Task<LiveCommandResult> StagePartyAgencySavePhaseAsync(
            JObject command)
        {
            if (!PartyAgencyDisposableCommandAuthorized(command, out string activeSave,
                    out LiveCommandResult failure))
                return Task.FromResult(failure);
            return ReignMainThread.InvokeAsync(() =>
            {
                string runId = command.Value<string>("runId") ?? _activeRunId ?? string.Empty;
                string caseId = command.Value<string>("caseId") ?? string.Empty;
                string variant = command.Value<string>("variant") ?? string.Empty;
                string key = runId + "|" + caseId + "|" + variant.ToLowerInvariant();
                if (!PartyAgencyNativeEvidence.TryGetValue(key, out JObject evidence))
                    return LiveCommandResult.Failed(
                        "The matching game-generated Party Agency save-stage receipt is unavailable.");
                Hero target = FindPartyAgencyFixtureHero();
                ReignTemporaryPartyGuestCampaignBehavior behavior =
                    ReignTemporaryPartyGuestCampaignBehavior.Instance;
                ReignTemporaryPartyGuestRecord record = behavior?.Records
                    .FirstOrDefault(item => item?.HeroStringId == target?.StringId);
                if (target == null || record == null)
                    return LiveCommandResult.Failed(
                        "The Party Agency save-stage guest is unavailable.", evidence);
                ReignTemporaryGuestPhase phase;
                if (!Enum.TryParse(variant, true, out phase))
                    return LiveCommandResult.Failed(
                        "Unknown Party Agency lifecycle phase '" + variant + "'.", evidence);
                switch (phase)
                {
                    case ReignTemporaryGuestPhase.Active:
                        if (record.Phase != ReignTemporaryGuestPhase.Active)
                            return LiveCommandResult.Failed("The accepted guest is not Active.", evidence);
                        break;
                    case ReignTemporaryGuestPhase.ReviewDue:
                        record.ReviewDueDay = (float)CampaignTime.Now.ToDays;
                        behavior.RunDisposableLiveTestHourlyStateOnly();
                        break;
                    case ReignTemporaryGuestPhase.Departing:
                        if (target.PartyBelongedTo != MobileParty.MainParty)
                        {
                            if (target.CurrentSettlement != null)
                                LeaveSettlementAction.ApplyForCharacterOnly(target);
                            target.ChangeState(Hero.CharacterStates.Active);
                            AddHeroToPartyAction.Apply(target, MobileParty.MainParty, false);
                        }
                        record.Phase = ReignTemporaryGuestPhase.Departing;
                        record.LastStateChangeDay = (float)CampaignTime.Now.ToDays;
                        break;
                    case ReignTemporaryGuestPhase.Returning:
                        if (record.Phase == ReignTemporaryGuestPhase.Departing)
                            behavior.RunDisposableLiveTestHourlyStateOnly();
                        if (record.Phase != ReignTemporaryGuestPhase.Returning)
                            return LiveCommandResult.Failed(
                                "The natural-language departure did not enter Returning.", evidence);
                        break;
                    case ReignTemporaryGuestPhase.Completed:
                        if (record.Phase == ReignTemporaryGuestPhase.Departing)
                            behavior.RunDisposableLiveTestHourlyStateOnly();
                        if (record.Phase == ReignTemporaryGuestPhase.Returning)
                        {
                            record.ReturnDueDay = (float)CampaignTime.Now.ToDays;
                            behavior.RunDisposableLiveTestHourlyTick();
                        }
                        break;
                    case ReignTemporaryGuestPhase.HostileBanishmentPending:
                        if (!record.HostilityWarningAcknowledged)
                            return LiveCommandResult.Failed(
                                "The guest did not naturally acknowledge the own-faction warning.", evidence);
                        record.OwnFactionCombatPending = true;
                        record.Phase = ReignTemporaryGuestPhase.HostileBanishmentPending;
                        record.LastStateChangeDay = (float)CampaignTime.Now.ToDays;
                        break;
                }
                bool staged = record.Phase == phase;
                evidence["ok"] = false;
                evidence["receiptId"] = "party_agency_save_stage_" + Guid.NewGuid().ToString("N");
                evidence["operation"] = "party_agency_stage_save_phase";
                evidence["activeSaveName"] = activeSave;
                evidence["saveRoundtripStageReady"] = staged;
                evidence["stagedPhase"] = record.Phase.ToString();
                evidence["requiresNativeCheckpointAndRestart"] = true;
                PartyAgencyNativeEvidence[key] = evidence;
                return staged
                    ? LiveCommandResult.Completed(
                        "The lifecycle phase is staged; this is intentionally not passing evidence until a native checkpoint and restart are verified.", evidence)
                    : LiveCommandResult.Failed(
                        "The requested lifecycle phase could not be staged.", evidence);
            });
        }

        private static Task<LiveCommandResult> VerifyPartyAgencySavePhaseAsync(
            JObject command)
        {
            if (!PartyAgencyDisposableCommandAuthorized(command, out string activeSave,
                    out LiveCommandResult failure))
                return Task.FromResult(failure);
            if (command?.Value<bool?>("restartVerified") != true
                || string.IsNullOrWhiteSpace(command.Value<string>("restartReceipt")))
                return Task.FromResult(LiveCommandResult.Failed(
                    "Party Agency save verification requires the guarded MCP checkpoint-and-restart receipt."));
            return ReignMainThread.InvokeAsync(() =>
            {
                string runId = command.Value<string>("runId") ?? _activeRunId ?? string.Empty;
                string caseId = command.Value<string>("caseId") ?? string.Empty;
                string variant = command.Value<string>("variant") ?? string.Empty;
                if (!Enum.TryParse(variant, true, out ReignTemporaryGuestPhase expectedPhase))
                    return LiveCommandResult.Failed(
                        "Unknown Party Agency lifecycle phase '" + variant + "'.");
                ReignTemporaryPartyGuestCampaignBehavior behavior =
                    ReignTemporaryPartyGuestCampaignBehavior.Instance;
                List<ReignTemporaryPartyGuestRecord> matches = behavior?.Records
                    .Where(record => record != null && record.Phase == expectedPhase)
                    .OrderByDescending(record => record.LastStateChangeDay).ToList()
                    ?? new List<ReignTemporaryPartyGuestRecord>();
                ReignTemporaryPartyGuestRecord record = matches.FirstOrDefault();
                Hero target = record == null ? null : Hero.AllAliveHeroes
                    .Concat(Hero.DeadOrDisabledHeroes).FirstOrDefault(hero => hero != null
                        && string.Equals(hero.StringId, record.HeroStringId,
                            StringComparison.OrdinalIgnoreCase));
                _partyAgencyFixtureTargetId = target?.StringId ?? string.Empty;
                MobileParty source = string.IsNullOrWhiteSpace(record?.SourcePartyStringId)
                    ? null : MobileParty.All.FirstOrDefault(party => party != null
                        && string.Equals(party.StringId, record.SourcePartyStringId,
                            StringComparison.OrdinalIgnoreCase));
                int heroMatches = target == null ? 0 : Hero.AllAliveHeroes
                    .Concat(Hero.DeadOrDisabledHeroes).Count(hero => hero != null
                        && string.Equals(hero.StringId, target.StringId,
                            StringComparison.OrdinalIgnoreCase));
                int partyMatches = source == null ? 0 : MobileParty.All.Count(party => party != null
                    && string.Equals(party.StringId, source.StringId,
                        StringComparison.OrdinalIgnoreCase));
                bool identity = record != null && target != null
                    && string.Equals(record.OriginalClanStringId,
                        target.Clan?.StringId ?? string.Empty, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(record.OriginalKingdomStringId,
                        target.Clan?.Kingdom?.StringId ?? string.Empty,
                        StringComparison.OrdinalIgnoreCase);
                bool campState = expectedPhase == ReignTemporaryGuestPhase.Completed
                    ? source == null || !behavior.IsProtectedCamp(source)
                    : string.IsNullOrWhiteSpace(record?.SourcePartyStringId)
                        || behavior.IsProtectedCamp(source);
                bool noDuplicates = matches.Count == 1 && heroMatches == 1
                    && (source == null || partyMatches == 1);
                bool ok = record != null && identity && campState && noDuplicates;
                JObject evidence = new JObject
                {
                    ["ok"] = ok,
                    ["receiptId"] = "party_agency_save_reload_" + Guid.NewGuid().ToString("N"),
                    ["operation"] = "party_agency_verify_save_phase",
                    ["caseId"] = caseId,
                    ["variant"] = variant,
                    ["activeSaveName"] = activeSave,
                    ["restartReceipt"] = command.Value<string>("restartReceipt"),
                    ["phase"] = record?.Phase.ToString() ?? string.Empty,
                    ["recordReloaded"] = record != null,
                    ["identityPreserved"] = identity,
                    ["campStateRestored"] = campState,
                    ["noDuplicateHeroOrParty"] = noDuplicates,
                    ["directGuestActionInvoked"] = false
                };
                PartyAgencyNativeEvidence[runId + "|" + caseId + "|"
                    + variant.ToLowerInvariant()] = evidence;
                return ok
                    ? LiveCommandResult.Completed(
                        "The Party Agency lifecycle phase survived the guarded native save/reload without duplicates.", evidence)
                    : LiveCommandResult.Failed(
                        "The Party Agency lifecycle phase did not survive native save/reload cleanly.", evidence);
            });
        }

        private static bool PartyAgencyDisposableCommandAuthorized(JObject command,
            out string activeSave, out LiveCommandResult failure)
        {
            activeSave = ActiveSaveName();
            string expectedSave = command?.Value<string>("expectedSaveName") ?? string.Empty;
            bool ok = command?.Value<bool?>("confirmDisposableCampaign") == true
                && expectedSave.StartsWith("ReignTest_", StringComparison.Ordinal)
                && expectedSave.EndsWith("_Current", StringComparison.Ordinal)
                && string.Equals(expectedSave, activeSave, StringComparison.Ordinal)
                && TaleWorlds.CampaignSystem.Campaign.Current?.TimeControlMode
                    == CampaignTimeControlMode.Stop;
            failure = ok ? null : LiveCommandResult.Failed(
                "The Party Agency native exercise requires paused time on the exact active enrolled disposable save.");
            return ok;
        }

        private static Hero FindPartyAgencyFixtureHero()
        {
            if (string.IsNullOrWhiteSpace(_partyAgencyFixtureTargetId)) return null;
            return Hero.AllAliveHeroes.Concat(Hero.DeadOrDisabledHeroes)
                .FirstOrDefault(hero => hero != null && string.Equals(
                    hero.StringId, _partyAgencyFixtureTargetId,
                    StringComparison.OrdinalIgnoreCase));
        }

        private static Hero SelectPartyAgencyFixtureTarget(string variant, string hint)
        {
            IEnumerable<Hero> allHeroes = Hero.AllAliveHeroes.Concat(Hero.DeadOrDisabledHeroes)
                .Where(hero => hero != null && hero != Hero.MainHero);
            IEnumerable<Hero> all = allHeroes.Where(hero => hero.IsLord);
            Func<Hero, bool> ordinary = hero => hero.IsAlive && !hero.IsPrisoner
                && hero.Clan != null && hero.Clan != Clan.PlayerClan
                && hero.CompanionOf == null && hero.GovernorOf == null
                && ReignConversationEligibility.IsAdultLivingNpc(hero);
            Func<Hero, bool> partyless = hero => ordinary(hero)
                && hero.PartyBelongedTo == null && hero.CanLeadParty();
            Func<Hero, bool> predicate;
            switch (variant)
            {
                case "clan_leader": predicate = hero => ordinary(hero)
                    && hero.Clan?.Leader == hero; break;
                case "kingdom_ruler": predicate = hero => ordinary(hero)
                    && hero.Clan?.Kingdom?.Leader == hero; break;
                case "governor": predicate = hero => hero.IsAlive
                    && hero.GovernorOf != null; break;
                case "prisoner": predicate = hero => hero.IsAlive
                    && hero.IsPrisoner; break;
                case "dead": predicate = hero => hero.IsDead; break;
                case "child": predicate = hero => hero.IsAlive
                    && !ReignConversationEligibility.IsAdultLivingNpc(hero); break;
                case "companion": predicate = hero => hero.IsAlive
                    && hero.CompanionOf != null; break;
                case "partyless": predicate = partyless; break;
                default: predicate = partyless; break;
            }
            IEnumerable<Hero> candidatePool = variant == "companion" ? allHeroes : all;
            List<Hero> candidates = candidatePool.Where(predicate).ToList();
            if (candidates.Count == 0 && variant == "governor")
                candidates = all.Where(partyless).Where(hero => Settlement.All.Any(
                    settlement => settlement?.Town != null
                        && settlement.OwnerClan == hero.Clan)).ToList();
            else if (candidates.Count == 0 && variant == "prisoner")
                candidates = all.Where(partyless).ToList();
            else if (candidates.Count == 0 && variant == "companion")
                candidates = allHeroes.Where(hero => hero.IsAlive && !hero.IsPrisoner
                    && hero.CompanionOf == null && hero.GovernorOf == null
                    && hero.PartyBelongedTo == null
                    && hero.CharacterObject?.Occupation == Occupation.Wanderer
                    && ReignConversationEligibility.IsAdultLivingNpc(hero)).ToList();
            if (!string.IsNullOrWhiteSpace(hint))
            {
                Hero hinted = candidates.FirstOrDefault(hero =>
                    string.Equals(hero.StringId, hint, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(hero.Name?.ToString(), hint,
                        StringComparison.OrdinalIgnoreCase));
                if (hinted != null) return hinted;
            }
            return candidates.OrderBy(hero => hero.Name?.ToString() ?? hero.StringId,
                StringComparer.OrdinalIgnoreCase).FirstOrDefault();
        }

        private static Hero SelectPartyAgencyHostilityTarget(string hint)
        {
            IFaction playerFaction = Hero.MainHero?.MapFaction;
            List<Hero> candidates = Hero.AllAliveHeroes.Where(hero => hero != null
                    && hero != Hero.MainHero && hero.IsLord && hero.IsAlive
                    && !hero.IsPrisoner && hero.Clan != null
                    && hero.Clan != Clan.PlayerClan && hero.Clan.Kingdom != null
                    && hero.CompanionOf == null && hero.GovernorOf == null
                    && hero.PartyBelongedTo == null
                    && ReignConversationEligibility.IsAdultLivingNpc(hero)
                    && hero.CanLeadParty() && playerFaction != null
                    && hero.MapFaction != playerFaction
                    && !FactionManager.IsAtWarAgainstFaction(
                        playerFaction, hero.MapFaction))
                .OrderBy(hero => hero.Name?.ToString() ?? hero.StringId,
                    StringComparer.OrdinalIgnoreCase).ToList();
            if (!string.IsNullOrWhiteSpace(hint))
            {
                Hero hinted = candidates.FirstOrDefault(hero =>
                    string.Equals(hero.StringId, hint, StringComparison.OrdinalIgnoreCase)
                    || (hero.Name?.ToString() ?? string.Empty)
                        .IndexOf(hint, StringComparison.OrdinalIgnoreCase) >= 0);
                if (hinted != null) return hinted;
            }
            return candidates.FirstOrDefault();
        }

        private static Hero SelectPartyAgencyHelper(Hero target)
        {
            Kingdom kingdom = target?.Clan?.Kingdom;
            Hero helper = Hero.AllAliveHeroes.FirstOrDefault(hero => hero != null
                && hero != target && hero != Hero.MainHero && hero.IsLord
                && hero.IsAlive && !hero.IsPrisoner && hero.PartyBelongedTo == null
                && hero.CompanionOf == null && hero.GovernorOf == null
                && hero.Clan?.Kingdom == kingdom && hero.CanLeadParty());
            if (helper == null)
                throw new InvalidOperationException(
                    "No same-kingdom partyless helper lord exists for this fixture.");
            return helper;
        }

        private static MobileParty CreatePartyAgencyFixtureParty(Hero leader,
            Settlement anchor, string runId, string role)
        {
            if (leader == null || leader.PartyBelongedTo != null)
                throw new InvalidOperationException(
                    "A native fixture party requires a partyless leader.");
            string suffix = Math.Abs((runId + role + leader.StringId).GetHashCode())
                .ToString("x8");
            MobileParty party = LordPartyComponent.CreateLordParty(
                leader.StringId + "_reign_party_agency_" + suffix,
                leader, anchor.GatePosition, 2f, anchor, leader);
            CharacterObject troop = MBObjectManager.Instance
                .GetObject<CharacterObject>("imperial_infantryman")
                ?? MBObjectManager.Instance.GetObject<CharacterObject>("imperial_recruit");
            if (troop != null) party.MemberRoster.AddToCounts(troop, 24);
            if (DefaultItems.Grain != null) party.ItemRoster.AddToCounts(DefaultItems.Grain, 60);
            party.SetMoveModeHold();
            return party;
        }

        private static void PositionPartyAgencyReturnFixture(MobileParty party,
            Settlement anchor, string variant)
        {
            CampaignVec2 origin = MobileParty.MainParty?.Position ?? anchor.GatePosition;
            if (PartyAgencyReturnVariantMatches(variant, "min", "minimum"))
            {
                party.Position = origin;
                return;
            }

            var safeFortifications = Settlement.All.Where(settlement => settlement != null
                    && settlement.IsFortification && !settlement.IsUnderSiege
                    && party.MapFaction != null
                    && !party.MapFaction.IsAtWarWith(settlement.MapFaction))
                .Select(settlement => new
                {
                    Settlement = settlement,
                    Distance = settlement.GatePosition.ToVec2()
                        .Distance(origin.ToVec2())
                }).ToList();
            if (safeFortifications.Count == 0)
                throw new InvalidOperationException(
                    "No safe friendly fortification exists for the return fixture.");
            party.Position = PartyAgencyReturnVariantMatches(variant, "max", "maximum")
                ? safeFortifications.OrderByDescending(item => item.Distance)
                    .First().Settlement.GatePosition
                : safeFortifications.OrderBy(item => Math.Abs(item.Distance - 60f))
                    .First().Settlement.GatePosition;
        }

        private static bool PartyAgencyReturnDurationMatches(string variant,
            double plannedReturnHours)
        {
            if (PartyAgencyReturnVariantMatches(variant, "min", "minimum"))
                return plannedReturnHours >= 0.9d && plannedReturnHours <= 1.2d;
            if (PartyAgencyReturnVariantMatches(variant, "max", "maximum"))
                return plannedReturnHours >= 11.8d && plannedReturnHours <= 12.2d;
            return plannedReturnHours > 1.2d && plannedReturnHours < 11.8d;
        }

        private static bool PartyAgencyReturnVariantMatches(string variant,
            string shortName, string manifestName)
        {
            return string.Equals(variant, shortName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(variant, manifestName,
                    StringComparison.OrdinalIgnoreCase);
        }

        private static JObject PartyAgencyFixtureRoleEvidence(Hero hero)
        {
            MobileParty party = hero?.PartyBelongedTo;
            return new JObject
            {
                ["alive"] = hero?.IsAlive == true,
                ["adultLivingNpc"] = ReignConversationEligibility.IsAdultLivingNpc(hero),
                ["prisoner"] = hero?.IsPrisoner == true,
                ["companion"] = hero?.CompanionOf != null,
                ["governor"] = hero?.GovernorOf != null,
                ["partyId"] = party?.StringId ?? string.Empty,
                ["partyLeader"] = party?.LeaderHero == hero,
                ["ordinaryPartyMember"] = party != null && party.LeaderHero != hero,
                ["armyMember"] = party?.Army != null
                    && party.Army.LeaderParty != party,
                ["armyLeader"] = party?.Army?.LeaderParty == party,
                ["clanLeader"] = hero?.Clan?.Leader == hero,
                ["kingdomRuler"] = hero?.Clan?.Kingdom?.Leader == hero,
                ["questBoundParty"] = party?.IsCurrentlyUsedByAQuest == true
            };
        }

        private static JObject CapturePartyAgencyNativeState(Hero hero)
        {
            ReignTemporaryPartyGuestRecord record =
                ReignTemporaryPartyGuestCampaignBehavior.Instance?.Records
                    .FirstOrDefault(item => item?.HeroStringId == hero?.StringId);
            MobileParty source = record == null ? hero?.PartyBelongedTo
                : MobileParty.All.FirstOrDefault(party => party != null
                    && string.Equals(party.StringId, record.SourcePartyStringId,
                        StringComparison.OrdinalIgnoreCase));
            return new JObject
            {
                ["heroId"] = hero?.StringId ?? string.Empty,
                ["clanId"] = hero?.Clan?.StringId ?? string.Empty,
                ["kingdomId"] = hero?.Clan?.Kingdom?.StringId ?? string.Empty,
                ["companionClanId"] = hero?.CompanionOf?.StringId ?? string.Empty,
                ["sourcePartyId"] = source?.StringId ?? string.Empty,
                ["sourcePartyExists"] = source != null,
                ["sourcePartyVisible"] = source?.IsVisible == true,
                ["sourcePartyActive"] = source?.IsActive == true,
                ["sourcePartyDisbanding"] = source?.IsDisbanding == true,
                ["sourcePartyInMapEvent"] = source?.MapEvent != null,
                ["sourcePartyInSiege"] = source?.SiegeEvent != null
                    || source?.BesiegerCamp != null,
                ["sourcePartyUsedByQuest"] = source?.IsCurrentlyUsedByAQuest == true,
                ["sourcePartyDefaultBehavior"] = source?.DefaultBehavior.ToString()
                    ?? string.Empty,
                ["sourcePartyTargetPartyId"] = source?.TargetParty?.StringId ?? string.Empty,
                ["sourcePartyTargetSettlementId"] = source?.TargetSettlement?.StringId
                    ?? string.Empty,
                ["sourcePartyClanId"] = source?.ActualClan?.StringId ?? string.Empty,
                ["sourcePartyIsLordParty"] = source?.LordPartyComponent != null,
                ["sourcePartyProtected"] = source == null
                    || ReignTemporaryPartyGuestCampaignBehavior.Instance?.IsProtectedCamp(source) == true,
                ["sourcePartyX"] = source?.Position.X ?? 0f,
                ["sourcePartyY"] = source?.Position.Y ?? 0f,
                ["troopSignature"] = source == null ? string.Empty : string.Join("|",
                    source.MemberRoster.GetTroopRoster()
                        .OrderBy(element => element.Character?.StringId)
                        .Select(element => (element.Character?.StringId ?? string.Empty)
                            + ":" + element.Number + ":" + element.WoundedNumber)),
                ["protectedTroopSignature"] = source == null ? string.Empty : string.Join("|",
                    source.MemberRoster.GetTroopRoster()
                        .Where(element => !string.Equals(element.Character?.StringId,
                            hero?.StringId, StringComparison.OrdinalIgnoreCase))
                        .OrderBy(element => element.Character?.StringId)
                        .Select(element => (element.Character?.StringId ?? string.Empty)
                            + ":" + element.Number + ":" + element.WoundedNumber)),
                ["prisonerSignature"] = source == null ? string.Empty : string.Join("|",
                    source.PrisonRoster.GetTroopRoster()
                        .OrderBy(element => element.Character?.StringId)
                        .Select(element => (element.Character?.StringId ?? string.Empty)
                            + ":" + element.Number + ":" + element.WoundedNumber)),
                ["itemSignature"] = source == null ? string.Empty : string.Join("|",
                    source.ItemRoster.OrderBy(element =>
                            element.EquipmentElement.Item?.StringId)
                        .Select(element =>
                            (element.EquipmentElement.Item?.StringId ?? string.Empty)
                            + ":" + element.Amount)),
                ["battleEquipmentSignature"] = PartyAgencyEquipmentSignature(hero?.BattleEquipment),
                ["civilianEquipmentSignature"] = PartyAgencyEquipmentSignature(hero?.CivilianEquipment),
                ["equipmentChangeAllowed"] = hero?.CanHeroEquipmentBeChanged() == true,
                ["temporaryGuestEquipmentLocked"] = ReignTemporaryPartyGuestCampaignBehavior.Instance
                    ?.IsTemporaryGuestEquipmentLocked(hero) == true,
                ["morale"] = source?.Morale ?? 0f,
                ["recordPhase"] = record?.Phase.ToString() ?? string.Empty
            };
        }

        private static string PartyAgencyEquipmentSignature(Equipment equipment)
        {
            if (equipment == null) return string.Empty;
            return string.Join("|", Enumerable.Range(0, (int)EquipmentIndex.NumEquipmentSetSlots)
                .Select(index => new
                {
                    Slot = (EquipmentIndex)index,
                    Element = equipment[(EquipmentIndex)index]
                })
                .Select(value => value.Slot + ":"
                    + (value.Element.Item?.StringId ?? string.Empty) + ":"
                    + (value.Element.ItemModifier?.StringId ?? string.Empty)));
        }

        private static JObject CapturePartyAgencyUnrelatedState(Hero hero)
        {
            ReignArrestCampaignBehavior arrests = ReignArrestCampaignBehavior.Instance;
            int arrestCases = arrests?.Cases.Count(item => item != null
                && string.Equals(item.AccusedHeroStringId, hero?.StringId,
                    StringComparison.OrdinalIgnoreCase)) ?? 0;
            return new JObject
            {
                ["nativeRelation"] = hero?.GetRelation(Hero.MainHero) ?? 0,
                ["traits"] = new JObject
                {
                    ["valor"] = hero?.GetTraitLevel(DefaultTraits.Valor) ?? 0,
                    ["generosity"] = hero?.GetTraitLevel(DefaultTraits.Generosity) ?? 0,
                    ["honor"] = hero?.GetTraitLevel(DefaultTraits.Honor) ?? 0,
                    ["mercy"] = hero?.GetTraitLevel(DefaultTraits.Mercy) ?? 0,
                    ["calculating"] = hero?.GetTraitLevel(DefaultTraits.Calculating) ?? 0
                },
                ["isPrisoner"] = hero?.IsPrisoner == true,
                ["arrestCaseCount"] = arrestCases,
                ["rulerReputationState"] = CapturePrivatePartyAgencyState(
                    ReignRulerReputationCampaignBehavior.Instance, "_state"),
                ["courtPersonalityReputationState"] = CapturePrivatePartyAgencyState(
                    ReignCourtPersonalityReputationCampaignBehavior.Instance, "_state"),
                ["combatReputationState"] = CapturePrivatePartyAgencyState(
                    TaleWorlds.CampaignSystem.Campaign.Current
                        ?.GetCampaignBehavior<ReignCombatReputationCampaignBehavior>(),
                    "_persistedState")
            };
        }

        private static JToken CapturePrivatePartyAgencyState(object instance,
            string fieldName)
        {
            if (instance == null) return JValue.CreateNull();
            FieldInfo field = instance.GetType().GetField(fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            object value = field?.GetValue(instance);
            if (value is JToken token) return token.DeepClone();
            return value == null ? JValue.CreateNull() : new JValue(value.ToString());
        }

        private static bool PartyAgencyNativeStateFidelity(JObject before, JObject after)
        {
            if (before?.Value<bool?>("sourcePartyExists") != true) return true;
            if (after?.Value<bool?>("sourcePartyExists") != true
                || after.Value<bool?>("sourcePartyVisible") != true
                || after.Value<bool?>("sourcePartyProtected") != true
                || after.Value<bool?>("sourcePartyActive") != true
                || after.Value<bool?>("sourcePartyDisbanding") == true
                || after.Value<bool?>("sourcePartyInMapEvent") == true
                || after.Value<bool?>("sourcePartyInSiege") == true
                || after.Value<bool?>("sourcePartyUsedByQuest") != true
                || !string.Equals(after.Value<string>("sourcePartyDefaultBehavior"),
                    "Hold", StringComparison.OrdinalIgnoreCase)
                || !string.IsNullOrEmpty(after.Value<string>("sourcePartyTargetPartyId"))
                || !string.IsNullOrEmpty(after.Value<string>("sourcePartyTargetSettlementId")))
                return false;
            foreach (string key in new[]
            {
                "sourcePartyId", "sourcePartyClanId", "protectedTroopSignature",
                "prisonerSignature", "itemSignature"
            })
                if (!string.Equals(before.Value<string>(key), after.Value<string>(key),
                        StringComparison.Ordinal)) return false;
            if (before.Value<bool?>("sourcePartyIsLordParty")
                != after.Value<bool?>("sourcePartyIsLordParty")) return false;
            return Math.Abs(before.Value<float>("sourcePartyX")
                    - after.Value<float>("sourcePartyX")) < 0.01f
                && Math.Abs(before.Value<float>("sourcePartyY")
                    - after.Value<float>("sourcePartyY")) < 0.01f
                && Math.Abs(before.Value<float>("morale")
                    - after.Value<float>("morale")) < 0.01f;
        }
    }
}
