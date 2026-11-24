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
                    ReignServices.TemporaryPartyGuest?.Records
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
                ReignServices.TemporaryPartyGuest
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
                    ReignServices.TemporaryPartyGuest;
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
            return ReignMainThread.InvokeAsync(() =>
            {
                if (!PartyAgencyDisposableCommandAuthorized(command, out string activeSave,
                        out LiveCommandResult failure))
                    return failure;
                string runId = ReadPartyAgencyStageText(command, "runId");
                string caseId = ReadPartyAgencyStageText(command, "caseId");
                string variant = ReadPartyAgencyStageText(command, "variant");
                if (caseId != PartyAgencySaveRoundtripCaseId
                    || !TryReadPartyAgencySavePhase(variant, out ReignTemporaryGuestPhase phase))
                    return LiveCommandResult.Failed("The Party Agency save stage requires its exact case and a supported lifecycle phase.");
                string key = PartyAgencySaveStageKey(runId, caseId, variant);
                if (!PartyAgencyNativeEvidence.TryGetValue(key, out JObject evidence))
                    return LiveCommandResult.Failed(
                        "The matching game-generated Party Agency save-stage receipt is unavailable.");
                Hero target = FindPartyAgencyFixtureHero();
                ReignTemporaryPartyGuestCampaignBehavior behavior =
                    ReignServices.TemporaryPartyGuest;
                ReignTemporaryPartyGuestRecord record = behavior?.Records
                    .FirstOrDefault(item => item?.HeroStringId == target?.StringId);
                if (target == null || record == null
                    || FindUniquePartyAgencySavedAgreement(behavior, record.AgreementId) != record)
                    return LiveCommandResult.Failed(
                        "The Party Agency save-stage guest or its exact agreement is unavailable.", evidence);
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
                evidence = (JObject)evidence.DeepClone();
                evidence["ok"] = false;
                evidence["receiptId"] = "party_agency_save_stage_" + Guid.NewGuid().ToString("N");
                evidence["operation"] = "party_agency_stage_save_phase";
                evidence["caseId"] = caseId;
                evidence["variant"] = variant;
                evidence["activeSaveName"] = activeSave;
                evidence["saveRoundtripStageReady"] = staged;
                evidence["stagedPhase"] = record.Phase.ToString();
                evidence["stagedAgreement"] = CapturePartyAgencySavedAgreement(record);
                evidence["stagedGameInstanceId"] = _gameInstanceId;
                evidence["stagedLiveRunId"] = runId;
                evidence["stagedCommandId"] = ReadPartyAgencyStageText(command, "commandId");
                evidence["stagedExecutionLeaseId"] = ReadPartyAgencyStageText(command, "leaseId");
                evidence["stagedRetryGeneration"] = command.Value<int?>("executionRetryGeneration") ?? 0;
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
            string expectedGameInstance = command?.Value<string>("expectedRestartGameInstanceId");
            if (command?.Value<bool?>("restartVerified") != true
                || string.IsNullOrWhiteSpace(command.Value<string>("restartReceipt"))
                || string.IsNullOrWhiteSpace(expectedGameInstance))
                return Task.FromResult(LiveCommandResult.Failed(
                    "Party Agency save verification requires the guarded MCP checkpoint-and-restart receipt and its native load."));
            return ReignMainThread.InvokeAsync(() =>
            {
                if (!PartyAgencyDisposableCommandAuthorized(command, out string activeSave,
                        out LiveCommandResult failure))
                    return failure;
                if (!string.Equals(_gameInstanceId, expectedGameInstance, StringComparison.Ordinal))
                    return LiveCommandResult.Failed(
                        "The Party Agency reload receipt belongs to a different native game instance.");

                string runId = ReadPartyAgencyStageText(command, "runId");
                string caseId = ReadPartyAgencyStageText(command, "caseId");
                string variant = ReadPartyAgencyStageText(command, "variant");
                var stage = command["partyAgencyStage"] as JObject;
                if (!IsPartyAgencySaveStageBinding(stage, caseId, variant, out ReignTemporaryGuestPhase expectedPhase)
                    || ReadPartyAgencyStageText(stage, "liveRunId") == runId
                    || ReadPartyAgencyStageText(stage, "gameInstanceId") == _gameInstanceId)
                    return LiveCommandResult.Failed(
                        "The Party Agency reload requires a bound stage from the original native game instance.");

                var expectedAgreement = (JObject)stage["agreement"];
                ReignTemporaryPartyGuestCampaignBehavior behavior =
                    ReignServices.TemporaryPartyGuest;
                ReignTemporaryPartyGuestRecord record = FindUniquePartyAgencySavedAgreement(
                    behavior, ReadPartyAgencyStageText(expectedAgreement, "agreementId"));
                JObject reloadedAgreement = record == null ? null : CapturePartyAgencySavedAgreement(record);
                bool exactAgreement = reloadedAgreement != null && JToken.DeepEquals(expectedAgreement, reloadedAgreement);
                Hero target = record == null ? null : Hero.AllAliveHeroes
                    .Concat(Hero.DeadOrDisabledHeroes).FirstOrDefault(hero => hero != null
                        && string.Equals(hero.StringId, record.HeroStringId,
                            StringComparison.OrdinalIgnoreCase));
                _partyAgencyFixtureTargetId = exactAgreement ? target?.StringId ?? string.Empty : string.Empty;
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
                bool identity = exactAgreement && target != null
                    && string.Equals(record.OriginalClanStringId,
                        target.Clan?.StringId ?? string.Empty, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(record.OriginalKingdomStringId,
                        target.Clan?.Kingdom?.StringId ?? string.Empty,
                        StringComparison.OrdinalIgnoreCase);
                bool campState = expectedPhase == ReignTemporaryGuestPhase.Completed
                    ? source == null || !behavior.IsProtectedCamp(source)
                    : string.IsNullOrWhiteSpace(record?.SourcePartyStringId)
                        || behavior.IsProtectedCamp(source);
                bool noDuplicates = record != null && heroMatches == 1
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
                    ["reloadedGameInstanceId"] = _gameInstanceId,
                    ["reloadedAgreement"] = reloadedAgreement,
                    ["phase"] = record?.Phase.ToString() ?? string.Empty,
                    ["recordReloaded"] = record != null,
                    ["identityPreserved"] = identity,
                    ["campStateRestored"] = campState,
                    ["noDuplicateHeroOrParty"] = noDuplicates,
                    ["directGuestActionInvoked"] = false
                };
                if (ok) evidence["partyAgencyStage"] = stage.DeepClone();
                PartyAgencyNativeEvidence[PartyAgencySaveStageKey(runId, caseId, variant)] = evidence;
                return ok
                    ? LiveCommandResult.Completed(
                        "The Party Agency lifecycle phase survived the guarded native save/reload without duplicates.", evidence)
                    : LiveCommandResult.Failed(
                        "The Party Agency lifecycle phase did not survive native save/reload cleanly.", evidence);
            });
        }

        private const string PartyAgencySaveRoundtripCaseId = "PA-NATIVE-017";

        private static string PartyAgencySaveStageKey(string runId, string caseId, string variant) =>
            runId + "|" + caseId + "|" + variant.ToLowerInvariant();

        private static string ReadPartyAgencyStageText(JObject source, string field)
        {
            JToken token = source?[field];
            return token?.Type == JTokenType.String ? token.Value<string>() : string.Empty;
        }

        private static bool TryReadPartyAgencySavePhase(string variant, out ReignTemporaryGuestPhase phase) =>
            Enum.TryParse(variant, true, out phase)
            && string.Equals(phase.ToString(), variant, StringComparison.OrdinalIgnoreCase)
            && (phase == ReignTemporaryGuestPhase.Active || phase == ReignTemporaryGuestPhase.ReviewDue
                || phase == ReignTemporaryGuestPhase.Departing || phase == ReignTemporaryGuestPhase.Returning
                || phase == ReignTemporaryGuestPhase.Completed || phase == ReignTemporaryGuestPhase.HostileBanishmentPending);

        private static bool IsPartyAgencySaveStageBinding(JObject stage, string caseId, string variant,
            out ReignTemporaryGuestPhase phase)
        {
            const string receiptPrefix = "party_agency_save_stage_";
            string receiptId = ReadPartyAgencyStageText(stage, "receiptId");
            return TryReadPartyAgencySavePhase(variant, out phase)
                && caseId == PartyAgencySaveRoundtripCaseId
                && ReadPartyAgencyStageText(stage, "caseId") == caseId
                && string.Equals(ReadPartyAgencyStageText(stage, "variant"), variant, StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(ReadPartyAgencyStageText(stage, "certificationRunId"))
                && !string.IsNullOrWhiteSpace(ReadPartyAgencyStageText(stage, "liveRunId"))
                && !string.IsNullOrWhiteSpace(ReadPartyAgencyStageText(stage, "commandId"))
                && !string.IsNullOrWhiteSpace(ReadPartyAgencyStageText(stage, "gameInstanceId"))
                && !string.IsNullOrWhiteSpace(ReadPartyAgencyStageText(stage, "executionLeaseId"))
                && receiptId.StartsWith(receiptPrefix, StringComparison.Ordinal)
                && Guid.TryParseExact(receiptId.Substring(receiptPrefix.Length), "N", out _)
                && stage["retryGeneration"]?.Type == JTokenType.Integer
                && int.TryParse(stage["retryGeneration"].ToString(), out int generation) && generation >= 0
                && stage["agreement"] is JObject agreement
                && !string.IsNullOrWhiteSpace(ReadPartyAgencyStageText(agreement, "agreementId"))
                && ReadPartyAgencyStageText(agreement, "phase") == phase.ToString();
        }

        private static ReignTemporaryPartyGuestRecord FindUniquePartyAgencySavedAgreement(
            ReignTemporaryPartyGuestCampaignBehavior behavior, string agreementId)
        {
            if (behavior == null || string.IsNullOrWhiteSpace(agreementId)) return null;
            var matches = behavior.Records
                .Where(record => string.Equals(record.AgreementId, agreementId, StringComparison.Ordinal))
                .Take(2).ToArray();
            return matches.Length == 1 ? matches[0] : null;
        }

        private static JObject CapturePartyAgencySavedAgreement(ReignTemporaryPartyGuestRecord record) =>
            new JObject
            {
                ["agreementId"] = record.AgreementId ?? string.Empty,
                ["agreementRevision"] = record.AgreementRevision,
                ["heroId"] = record.HeroStringId ?? string.Empty,
                ["originalClanId"] = record.OriginalClanStringId ?? string.Empty,
                ["originalKingdomId"] = record.OriginalKingdomStringId ?? string.Empty,
                ["sourcePartyId"] = record.SourcePartyStringId ?? string.Empty,
                ["phase"] = record.Phase.ToString()
            };

        // Called on the native thread immediately before the requested SaveAs.
        private static bool PartyAgencyCheckpointMatchesStage(JObject stage, string saveName)
        {
            string caseId = ReadPartyAgencyStageText(stage, "caseId");
            string variant = ReadPartyAgencyStageText(stage, "variant");
            if (!IsPartyAgencySaveStageBinding(stage, caseId, variant, out _)
                || ReadPartyAgencyStageText(stage, "gameInstanceId") != _gameInstanceId
                || !saveName.StartsWith("ReignTest_", StringComparison.Ordinal)
                || !saveName.EndsWith("_Current", StringComparison.Ordinal)
                || ActiveSaveName() != saveName
                || TaleWorlds.CampaignSystem.Campaign.Current?.TimeControlMode != CampaignTimeControlMode.Stop)
                return false;

            string runId = ReadPartyAgencyStageText(stage, "liveRunId");
            var agreement = (JObject)stage["agreement"];
            if (!PartyAgencyNativeEvidence.TryGetValue(PartyAgencySaveStageKey(runId, caseId, variant), out JObject native)
                || native.Value<bool?>("saveRoundtripStageReady") != true
                || ReadPartyAgencyStageText(native, "operation") != "party_agency_stage_save_phase"
                || ReadPartyAgencyStageText(native, "receiptId") != ReadPartyAgencyStageText(stage, "receiptId")
                || ReadPartyAgencyStageText(native, "caseId") != caseId
                || !string.Equals(ReadPartyAgencyStageText(native, "variant"), variant, StringComparison.OrdinalIgnoreCase)
                || ReadPartyAgencyStageText(native, "activeSaveName") != saveName
                || ReadPartyAgencyStageText(native, "stagedGameInstanceId") != _gameInstanceId
                || ReadPartyAgencyStageText(native, "stagedLiveRunId") != runId
                || ReadPartyAgencyStageText(native, "stagedCommandId") != ReadPartyAgencyStageText(stage, "commandId")
                || ReadPartyAgencyStageText(native, "stagedExecutionLeaseId") != ReadPartyAgencyStageText(stage, "executionLeaseId")
                || !JToken.DeepEquals(native["stagedRetryGeneration"], stage["retryGeneration"])
                || !JToken.DeepEquals(native["stagedAgreement"], agreement))
                return false;

            var record = FindUniquePartyAgencySavedAgreement(ReignServices.TemporaryPartyGuest,
                ReadPartyAgencyStageText(agreement, "agreementId"));
            return record != null && JToken.DeepEquals(CapturePartyAgencySavedAgreement(record), agreement);
        }
    }
}
