using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json.Linq;
using Reign.Core.Contracts.Court;
using ReignBeta.Integration;
using ReignBeta.UI;
using ReignBeta.UI.Calibration;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;

namespace ReignBeta.Court
{
    public sealed partial class ReignCourtCampaignBehavior
    {
        private const string RulerDocketFixtureConfirmation =
            "prepare Reign ruler docket fixture on disposable save";

        internal JObject RunRulerDocketTestProfile(string runId, string phase,
            JObject options, string gameInstanceId)
        {
            options = options ?? new JObject();
            if (!TryRequireRoyalCouncilTestSave(options, out string expectedSave,
                    out string activeSave, out string saveError))
                return RulerDocketTestFailure(runId, phase, saveError, expectedSave, activeSave);
            if ((phase ?? string.Empty).StartsWith("court_life_", StringComparison.Ordinal))
                return RunCourtLifeTestPhase(runId, phase, options, expectedSave, gameInstanceId);
            switch ((phase ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "preflight": return RulerDocketPreflight(runId, expectedSave);
                case "prepare": return PrepareRulerDocketFixture(runId, options, expectedSave, gameInstanceId);
                case "open": return OpenRulerDocketFixture(runId, expectedSave);
                case "snapshot": return SnapshotRulerDocketFixture(runId, expectedSave);
                case "decide": return DecideRulerDocketFixture(runId, options, expectedSave);
                case "guardrail_insufficient": return VerifyInsufficientPetitionGuardrail(runId, expectedSave);
                case "invalidate_identity": return InvalidatePetitionerIdentityFixture(runId, expectedSave);
                case "technical_return": return VerifyTechnicalPetitionReturn(runId, expectedSave);
                case "observe": return ObserveRulerDocketFixture(runId, expectedSave, false, gameInstanceId);
                case "no_expiry": return ObserveRulerDocketFixture(runId, expectedSave, true, gameInstanceId);
                case "expedition_prepare": return PrepareExpeditionReturnFixture(runId, options, expectedSave);
                case "expedition_verify": return VerifyExpeditionReturnFixture(runId, expectedSave);
                case "save_prepare": return MarkRulerDocketReload(runId, expectedSave, gameInstanceId);
                case "save_verify": return VerifyRulerDocketReload(runId, expectedSave, gameInstanceId);
                case "chancellor_preflight": return ChancellorPreflight(runId, expectedSave);
                case "chancellor_prepare": return PrepareChancellorFixture(runId, options, expectedSave, gameInstanceId);
                case "chancellor_exercise": return ExerciseChancellorFixture(runId, options, expectedSave);
                case "chancellor_schedule_prepare": return PrepareChancellorScheduleFixture(runId, expectedSave);
                case "chancellor_schedule_verify": return VerifyChancellorScheduleFixture(runId, expectedSave);
                case "chancellor_eligibility_dismissal": return VerifyChancellorEligibilityAndDismissal(runId, options, expectedSave);
                case "emergency_prepare": return PrepareEmergencyChancellorFixture(runId, options, expectedSave, gameInstanceId);
                case "emergency_release": return ReleaseEmergencyChancellorFixture(runId, expectedSave);
                case "emergency_complete": return CompleteEmergencyChancellorFixture(runId, expectedSave);
                case "emergency_observe": return ObserveEmergencyChancellorFixture(runId, expectedSave);
                case "legacy_migration": return VerifyLegacyDocketMigration(runId, options, expectedSave);
                case "natural_soak_prepare": return PrepareNaturalDocketSoak(runId, options, expectedSave, gameInstanceId);
                case "natural_soak_verify": return VerifyNaturalDocketSoak(runId, options, expectedSave, gameInstanceId);
                case "noble_preflight": return NobleDocketPreflight(runId, expectedSave);
                case "noble_prepare": return PrepareNobleDocketFixture(runId, options, expectedSave, gameInstanceId);
                case "noble_open": return OpenNobleDocketFixture(runId, expectedSave);
                case "noble_snapshot": return SnapshotNobleDocketFixture(runId, expectedSave);
                case "noble_decide": return DecideNobleDocketFixture(runId, options, expectedSave);
                case "noble_observe": return ObserveNobleDocketFixture(runId, expectedSave);
                case "noble_reverse": return ReverseNobleDocketFixture(runId, expectedSave);
                case "noble_execute": return ExecuteNobleDocketSentenceFixture(runId, expectedSave);
                case "noble_stay_prepare": return PrepareNobleCourtStayFixture(runId, expectedSave);
                case "noble_stay_verify": return VerifyNobleCourtStayFixture(runId, expectedSave);
                case "noble_investigation_verify": return VerifyNobleInvestigationFixture(runId, expectedSave);
                case "noble_save_prepare": return MarkNobleDocketReload(runId, expectedSave, gameInstanceId);
                case "noble_save_verify": return VerifyNobleDocketReload(runId, expectedSave, gameInstanceId);
                case "royal_proclamation": return VerifyRoyalProclamationFixture(runId, options, expectedSave);
                case "cleanup_marker": return CleanupRulerDocketMarker(runId, expectedSave);
                default: return RulerDocketTestFailure(runId, phase,
                    "Unsupported ruler-docket test phase.", expectedSave, activeSave);
            }
        }

        private JObject RulerDocketPreflight(string runId, string saveName)
        {
            Settlement capital = CurrentCapital;
            Kingdom kingdom = Clan.PlayerClan?.Kingdom;
            Village village = capital?.Town?.Villages.FirstOrDefault(x => x?.Settlement != null);
            Hero petitioner = capital == null ? null : SelectPetitioner(capital,
                village?.Settlement ?? capital, ReignPetitionKind.Food, CurrentDay());
            MobileParty garrison = capital?.Town?.GarrisonParty;
            int healthy = garrison?.MemberRoster?.GetTroopRoster()
                .Where(x => x.Character != null && !x.Character.IsHero)
                .Sum(x => x.Number - x.WoundedNumber) ?? 0;
            JArray assertions = new JArray
            {
                Assertion("player_is_current_ruler", kingdom?.Leader == Hero.MainHero),
                Assertion("capital_court_has_royal_command_access", HasRoyalCommandAccess),
                Assertion("capital_is_safe_owned_town", capital?.Town != null && !capital.IsUnderSiege
                    && capital.OwnerClan?.Kingdom == kingdom),
                Assertion("dependent_village_available", village != null),
                Assertion("eligible_petitioner_available", petitioner != null),
                Assertion("capital_garrison_available", garrison?.MemberRoster != null)
            };
            bool ok = assertions.All(x => x.Value<bool?>("passed") == true);
            return new JObject
            {
                ["ok"] = ok, ["runId"] = runId, ["profile"] = "ruler_docket",
                ["phase"] = "preflight", ["saveName"] = saveName,
                ["capitalId"] = capital?.StringId ?? string.Empty,
                ["villageId"] = village?.Settlement?.StringId ?? string.Empty,
                ["petitionerHeroId"] = petitioner?.StringId ?? string.Empty,
                ["healthyGarrison"] = healthy, ["assertions"] = assertions
            };
        }

        private JObject PrepareRulerDocketFixture(string runId, JObject options,
            string saveName, string gameInstanceId)
        {
            if (!string.Equals(options.Value<string>("confirmation"),
                    RulerDocketFixtureConfirmation, StringComparison.Ordinal))
                return RulerDocketTestFailure(runId, "prepare",
                    "The exact disposable-save ruler-docket fixture confirmation is required.",
                    saveName, saveName);
            if (!Enum.TryParse(options.Value<string>("kind") ?? string.Empty, true,
                    out ReignPetitionKind kind)
                || !Enum.TryParse(options.Value<string>("severity") ?? string.Empty, true,
                    out ReignPetitionSeverity severity)
                || severity == ReignPetitionSeverity.None)
                return RulerDocketTestFailure(runId, "prepare",
                    "kind must be Food, TownGold, VillageGold, or Soldiers and severity must be Minor, Serious, or Severe.",
                    saveName, saveName);

            Settlement capital = CurrentCapital;
            if (!HasRoyalCommandAccess || capital?.Town == null || capital.IsUnderSiege)
                return RulerDocketTestFailure(runId, "prepare",
                    "Hold production Court in a safe valid capital before preparing the fixture.", saveName, saveName);
            Settlement target = kind == ReignPetitionKind.Food || kind == ReignPetitionKind.VillageGold
                ? capital.Town.Villages.FirstOrDefault(x => x?.Settlement != null)?.Settlement : capital;
            if (target == null) return RulerDocketTestFailure(runId, "prepare",
                "The capital has no dependent village for this fixture.", saveName, saveName);

            ReignRulerDocketState state = EnsureRulerDocketState();
            state.Cooldowns.RemoveAll(x => x.Kind == kind
                && string.Equals(x.TargetSettlementId, target.StringId, StringComparison.OrdinalIgnoreCase));
            state.Commitments.RemoveAll(x => x.Active && x.Kind == kind
                && string.Equals(x.TargetSettlementId, target.StringId, StringComparison.OrdinalIgnoreCase));
            state.Petitions.RemoveAll(x => x.PetitionId.StartsWith("petition_test_" + runId + "_", StringComparison.Ordinal));

            int day = CurrentDay();
            double beforeHearth = target.Village?.Hearth ?? 0d;
            double beforeProsperity = target.Town?.Prosperity ?? 0d;
            double beforeSecurity = target.Town?.Security ?? 0d;
            double beforeFood = capital.Town.FoodStocks;
            int beforeGold = Hero.MainHero?.Gold ?? 0;
            ApplyRulerDocketNeedFixture(state, capital, target, kind, severity, day);

            var candidates = new List<NativePetitionCandidate>();
            BuildTownPetitionCandidates(capital, day, candidates);
            NativePetitionCandidate candidate = candidates.FirstOrDefault(x => x.Score.Kind == kind
                && x.Score.Severity == severity
                && string.Equals(x.Target.StringId, target.StringId, StringComparison.OrdinalIgnoreCase));
            if (candidate == null) return RulerDocketTestFailure(runId, "prepare",
                "The created native condition did not produce the requested production candidate.", saveName, saveName);

            string campaignId = ReignCampaignIdentity.CurrentCampaignId();
            string timelineId = ReignBeta.Campaign.ReignWorldHistoryCampaignBehavior.Instance?.TimelineId ?? "main";
            ReignDocketPetition petition = CreatePetitionSnapshot(candidate, campaignId, timelineId, day);
            petition.PetitionId = "petition_test_" + runId + "_" + Guid.NewGuid().ToString("N");
            string action = NormalizeFixtureAction(options.Value<string>("action"));
            state.Petitions.Add(petition);
            PrepareRulerDocketResources(capital, petition, action);
            ReignPetitionDecisionQuote quote = GetRulerPetitionQuote(petition.PetitionId);
            JObject marker = new JObject
            {
                ["runId"] = runId, ["petitionId"] = petition.PetitionId,
                ["kind"] = kind.ToString(), ["severity"] = severity.ToString(),
                ["action"] = action, ["preparedDay"] = day,
                ["gameInstanceId"] = gameInstanceId ?? string.Empty,
                ["saveName"] = saveName, ["targetSettlementId"] = target.StringId,
                ["beforeHearth"] = beforeHearth, ["beforeProsperity"] = beforeProsperity,
                ["beforeSecurity"] = beforeSecurity, ["beforeFood"] = beforeFood,
                ["beforeGold"] = beforeGold, ["termsHash"] = petition.TermsHash
            };
            StoreRulerDocketMarker(marker);
            return new JObject
            {
                ["ok"] = quote.Valid && (action == "refuse"
                    || action == "grant-direct" && quote.CanGrantDirect
                    || action == "fund-instead" && quote.CanGrantWithGold),
                ["runId"] = runId, ["profile"] = "ruler_docket", ["phase"] = "prepare",
                ["petition"] = PetitionTestJson(petition), ["quote"] = QuoteTestJson(quote),
                ["nativeConditionCreated"] = true, ["decisionStillUsesProductionUi"] = true,
                ["requiresBaselineRollback"] = true
            };
        }

        private void ApplyRulerDocketNeedFixture(ReignRulerDocketState state,
            Settlement capital, Settlement target, ReignPetitionKind kind,
            ReignPetitionSeverity severity, int day)
        {
            state.SettlementSamples.RemoveAll(x => x.Day >= day - 3
                && string.Equals(x.SettlementId, target.StringId, StringComparison.OrdinalIgnoreCase));
            if (kind == ReignPetitionKind.Food)
                target.Village.Hearth = severity == ReignPetitionSeverity.Minor ? 300f
                    : severity == ReignPetitionSeverity.Serious ? 150f : 50f;
            else if (kind == ReignPetitionKind.VillageGold)
            {
                target.Village.Hearth = 500f;
                double ratio = severity == ReignPetitionSeverity.Minor ? 0.70d
                    : severity == ReignPetitionSeverity.Serious ? 0.50d : 0.30d;
                state.SettlementSamples.Add(new ReignDocketSettlementSample
                { SettlementId = target.StringId, Day = day, Hearth = 500d,
                    HealthyVillageOutput = 100d, ActualVillageOutput = 100d * ratio });
            }
            else if (kind == ReignPetitionKind.TownGold)
            {
                capital.Town.Prosperity = severity == ReignPetitionSeverity.Minor ? 4500f
                    : severity == ReignPetitionSeverity.Serious ? 2500f : 1000f;
                state.SettlementSamples.Add(new ReignDocketSettlementSample
                { SettlementId = capital.StringId, Day = day - 1,
                    Prosperity = capital.Town.Prosperity + 100f });
                state.SettlementSamples.Add(new ReignDocketSettlementSample
                { SettlementId = capital.StringId, Day = day, Prosperity = capital.Town.Prosperity });
            }
            else
                capital.Town.Security = severity == ReignPetitionSeverity.Minor ? 65f
                    : severity == ReignPetitionSeverity.Serious ? 40f : 20f;
        }

        private void PrepareRulerDocketResources(Settlement capital,
            ReignDocketPetition petition, string action)
        {
            if (action == "refuse") return;
            if (petition.Kind == ReignPetitionKind.Food && action == "grant-direct")
                capital.Town.FoodStocks = Math.Max(capital.Town.FoodStocks, petition.FoodStockCost + 10f);
            if (petition.Kind == ReignPetitionKind.TownGold || petition.Kind == ReignPetitionKind.VillageGold)
                Hero.MainHero.Gold = Math.Max(Hero.MainHero.Gold, petition.GoldCost + 1000);
            if (petition.Kind == ReignPetitionKind.Soldiers && action == "grant-direct")
            {
                MobileParty garrison = capital.Town.GarrisonParty;
                int healthy = garrison?.MemberRoster?.GetTroopRoster()
                    .Where(x => x.Character != null && !x.Character.IsHero)
                    .Sum(x => x.Number - x.WoundedNumber) ?? 0;
                int needed = petition.SoldierCount
                    + ReignRulerDocketRules.MinimumHealthyGarrisonAfterDispatch(healthy + petition.SoldierCount)
                    - healthy;
                CharacterObject troop = garrison?.MemberRoster?.GetTroopRoster()
                    .Where(x => x.Character != null && !x.Character.IsHero)
                    .OrderBy(x => x.Character.Tier).Select(x => x.Character).FirstOrDefault()
                    ?? Clan.PlayerClan?.Culture?.BasicTroop;
                if (needed > 0 && troop != null) garrison.MemberRoster.AddToCounts(troop, needed);
            }
            ReignPetitionDecisionQuote quote = GetRulerPetitionQuote(petition.PetitionId);
            if (action == "fund-instead")
                Hero.MainHero.Gold = Math.Max(Hero.MainHero.Gold, quote.GoldSubstituteCost + 1000);
        }

        private JObject OpenRulerDocketFixture(string runId, string saveName)
        {
            ReignDocketPetition petition = FindRulerDocketTestPetition(runId);
            bool opened = ReignCourtPetitionScreenManager.TryOpenForAutomation(this, petition, out string error);
            return new JObject { ["ok"] = opened, ["runId"] = runId,
                ["profile"] = "ruler_docket", ["phase"] = "open", ["saveName"] = saveName,
                ["petitionId"] = petition?.PetitionId ?? string.Empty,
                ["productionUiOpen"] = opened, ["calibrationMode"] = false, ["error"] = error };
        }

        private JObject SnapshotRulerDocketFixture(string runId, string saveName)
        {
            bool saved = ReignUiCalibrationService.TrySaveSnapshot(
                "ReignCourtPetitionScreen", out string path, out string error);
            return new JObject { ["ok"] = saved, ["runId"] = runId,
                ["profile"] = "ruler_docket", ["phase"] = "snapshot", ["saveName"] = saveName,
                ["snapshotPath"] = path ?? string.Empty, ["error"] = error ?? string.Empty };
        }

        private JObject DecideRulerDocketFixture(string runId, JObject options, string saveName)
        {
            JObject marker = FindRulerDocketMarker(runId);
            string action = NormalizeFixtureAction(options.Value<string>("action")
                ?? marker?.Value<string>("action"));
            bool executed = ReignCourtPetitionScreenManager.TryExecuteAutomationAction(action, out string error);
            ReignDocketPetition petition = FindRulerDocketTestPetition(runId);
            bool expectedState = action == "refuse"
                ? petition?.State == ReignDocketPetitionState.Refused
                : petition?.State == ReignDocketPetitionState.Granted;
            return new JObject { ["ok"] = executed && expectedState, ["runId"] = runId,
                ["profile"] = "ruler_docket", ["phase"] = "decide", ["saveName"] = saveName,
                ["action"] = action, ["petition"] = PetitionTestJson(petition),
                ["productionViewModelCommand"] = true, ["error"] = error };
        }

        private JObject ObserveRulerDocketFixture(string runId, string saveName,
            bool requireNoExpiry, string gameInstanceId)
        {
            JObject marker = FindRulerDocketMarker(runId);
            ReignDocketPetition petition = FindRulerDocketTestPetition(runId);
            if (marker == null || petition == null) return RulerDocketTestFailure(runId,
                requireNoExpiry ? "no_expiry" : "observe", "The run-owned petition marker is missing.", saveName, saveName);
            string action = marker.Value<string>("action") ?? string.Empty;
            bool pending = petition.IsPending;
            bool decision = action == "refuse" ? petition.State == ReignDocketPetitionState.Refused
                : petition.State == ReignDocketPetitionState.Granted;
            bool history = EnsureRulerDocketState().History.Any(x => x.PetitionId == petition.PetitionId
                && x.Type == "petition");
            bool commitment = action == "refuse" || EnsureRulerDocketState().Commitments.Any(x => x.PetitionId == petition.PetitionId);
            bool noExpiry = !requireNoExpiry || pending && CurrentDay() > petition.ReceivedDay;
            return new JObject { ["ok"] = requireNoExpiry ? noExpiry : decision && history && commitment,
                ["runId"] = runId, ["profile"] = "ruler_docket",
                ["phase"] = requireNoExpiry ? "no_expiry" : "observe", ["saveName"] = saveName,
                ["currentDay"] = CurrentDay(), ["petition"] = PetitionTestJson(petition),
                ["pendingAcrossDayBoundary"] = noExpiry, ["decisionStateMatched"] = decision,
                ["historyRecorded"] = history, ["commitmentRecorded"] = commitment,
                ["gameInstanceId"] = gameInstanceId ?? string.Empty };
        }

        private JObject PrepareExpeditionReturnFixture(string runId, JObject options, string saveName)
        {
            JObject marker = FindRulerDocketMarker(runId);
            ReignDocketPetition petition = FindRulerDocketTestPetition(runId);
            ReignRulerDocketState state = EnsureRulerDocketState();
            ReignSoldierExpedition expedition = state.Expeditions.LastOrDefault(x => x?.PetitionId == petition?.PetitionId);
            if (marker == null || petition?.Kind != ReignPetitionKind.Soldiers
                || petition.State != ReignDocketPetitionState.Granted
                || petition.GrantMethod != ReignDocketGrantMethod.Direct || expedition == null || expedition.Returned)
                return RulerDocketTestFailure(runId, "expedition_prepare",
                    "A run-owned active direct soldier expedition is required.", saveName, saveName);

            int manifestTotal = (expedition.Manifest ?? new List<ReignSoldierManifestEntry>()).Sum(x => x.Count);
            int manifestCasualties = (expedition.Manifest ?? new List<ReignSoldierManifestEntry>()).Sum(x => x.Casualties);
            bool forceFallback = options.Value<bool?>("forceFallback") == true;
            string expectedReturnSettlementId = string.Empty;
            if (forceFallback)
            {
                expectedReturnSettlementId = CurrentCapital?.StringId ?? string.Empty;
                expedition.OriginalOwnerClanId = "missing_test_owner_" + runId;
                expedition.OriginalCapitalId = "missing_test_capital_" + runId;
            }
            marker["expeditionId"] = expedition.ExpeditionId;
            marker["expeditionReturnDay"] = expedition.ReturnDay;
            marker["expeditionManifestTotal"] = manifestTotal;
            marker["expeditionCasualties"] = expedition.HiddenCasualtyCount;
            marker["expeditionSurvivors"] = manifestTotal - expedition.HiddenCasualtyCount;
            marker["expeditionFallbackForced"] = forceFallback;
            marker["expectedReturnSettlementId"] = expectedReturnSettlementId;
            StoreRulerDocketMarker(marker);
            bool valid = manifestTotal == petition.SoldierCount
                && manifestCasualties == expedition.HiddenCasualtyCount
                && expedition.HiddenCasualtyCount >= 0
                && expedition.HiddenCasualtyCount <= manifestTotal
                && expedition.ReturnDay == expedition.DepartureDay + petition.DurationDays
                && (!forceFallback || !string.IsNullOrWhiteSpace(expectedReturnSettlementId));
            return new JObject
            {
                ["ok"] = valid, ["runId"] = runId, ["profile"] = "ruler_docket",
                ["phase"] = "expedition_prepare", ["saveName"] = saveName,
                ["currentDay"] = CurrentDay(), ["returnDay"] = expedition.ReturnDay,
                ["durationDays"] = petition.DurationDays, ["manifestTotal"] = manifestTotal,
                ["hiddenCasualties"] = expedition.HiddenCasualtyCount,
                ["expectedSurvivors"] = manifestTotal - expedition.HiddenCasualtyCount,
                ["fallbackForced"] = forceFallback,
                ["expectedReturnSettlementId"] = expectedReturnSettlementId,
                ["requiresGuardedAdvancement"] = true
            };
        }

        private JObject VerifyExpeditionReturnFixture(string runId, string saveName)
        {
            JObject marker = FindRulerDocketMarker(runId);
            ReignDocketPetition petition = FindRulerDocketTestPetition(runId);
            ReignRulerDocketState state = EnsureRulerDocketState();
            string expeditionId = marker?.Value<string>("expeditionId") ?? string.Empty;
            ReignSoldierExpedition expedition = state.Expeditions.LastOrDefault(x => x?.ExpeditionId == expeditionId);
            if (marker == null || petition == null || expedition == null)
                return RulerDocketTestFailure(runId, "expedition_verify",
                    "The prepared run-owned expedition marker is missing.", saveName, saveName);

            int manifestTotal = (expedition.Manifest ?? new List<ReignSoldierManifestEntry>()).Sum(x => x.Count);
            int manifestCasualties = (expedition.Manifest ?? new List<ReignSoldierManifestEntry>()).Sum(x => x.Casualties);
            int survivors = manifestTotal - manifestCasualties;
            int expectedSurvivors = marker.Value<int?>("expeditionSurvivors") ?? -1;
            bool fallbackForced = marker.Value<bool?>("expeditionFallbackForced") == true;
            string expectedDestination = marker.Value<string>("expectedReturnSettlementId") ?? string.Empty;
            Settlement destination = Settlement.Find(expedition.ReturnSettlementId);
            bool destinationValid = destination?.Town?.GarrisonParty?.MemberRoster != null
                && destination.OwnerClan?.Kingdom == Clan.PlayerClan?.Kingdom;
            bool destinationMatched = !fallbackForced || string.Equals(expedition.ReturnSettlementId,
                expectedDestination, StringComparison.OrdinalIgnoreCase);
            bool historyRecorded = state.History.Any(x => x.PetitionId == petition.PetitionId
                && x.Type == "soldier_expedition" && x.Outcome == "completed");
            ReignDocketCommitment commitment = state.Commitments.LastOrDefault(x => x.PetitionId == petition.PetitionId);
            bool commitmentCompleted = commitment != null && !commitment.Active
                && commitment.EndReason == "term_completed";
            bool passed = CurrentDay() >= expedition.ReturnDay && expedition.Returned
                && expedition.ReturnStatus == "returned" && destinationValid && destinationMatched
                && manifestTotal == marker.Value<int?>("expeditionManifestTotal")
                && manifestCasualties == marker.Value<int?>("expeditionCasualties")
                && survivors == expectedSurvivors && historyRecorded && commitmentCompleted;
            return new JObject
            {
                ["ok"] = passed, ["runId"] = runId, ["profile"] = "ruler_docket",
                ["phase"] = "expedition_verify", ["saveName"] = saveName,
                ["currentDay"] = CurrentDay(), ["returnDay"] = expedition.ReturnDay,
                ["returned"] = expedition.Returned, ["returnStatus"] = expedition.ReturnStatus,
                ["returnSettlementId"] = expedition.ReturnSettlementId,
                ["destinationValid"] = destinationValid, ["fallbackForced"] = fallbackForced,
                ["fallbackDestinationMatched"] = destinationMatched,
                ["manifestTotal"] = manifestTotal, ["casualties"] = manifestCasualties,
                ["survivors"] = survivors, ["historyRecorded"] = historyRecorded,
                ["commitmentCompleted"] = commitmentCompleted
            };
        }

        private JObject MarkRulerDocketReload(string runId, string saveName, string gameInstanceId)
        {
            JObject marker = FindRulerDocketMarker(runId);
            if (marker == null) return RulerDocketTestFailure(runId, "save_prepare",
                "The run-owned petition marker is missing.", saveName, saveName);
            marker["reloadFingerprint"] = RulerDocketFingerprint(runId);
            marker["reloadGameInstanceId"] = gameInstanceId ?? string.Empty;
            StoreRulerDocketMarker(marker);
            return new JObject { ["ok"] = true, ["runId"] = runId, ["phase"] = "save_prepare",
                ["fingerprint"] = marker.Value<string>("reloadFingerprint"),
                ["requiresGuardedCheckpointRestart"] = true };
        }

        private JObject VerifyRulerDocketReload(string runId, string saveName, string gameInstanceId)
        {
            JObject marker = FindRulerDocketMarker(runId);
            string current = RulerDocketFingerprint(runId);
            bool differentInstance = marker != null && !string.Equals(marker.Value<string>("reloadGameInstanceId"),
                gameInstanceId, StringComparison.OrdinalIgnoreCase);
            bool same = marker != null && string.Equals(marker.Value<string>("reloadFingerprint"), current,
                StringComparison.OrdinalIgnoreCase);
            return new JObject { ["ok"] = differentInstance && same, ["runId"] = runId,
                ["phase"] = "save_verify", ["differentGameInstance"] = differentInstance,
                ["fingerprintMatched"] = same, ["currentFingerprint"] = current };
        }

        private JObject CleanupRulerDocketMarker(string runId, string saveName)
        {
            int removed = _rulerDocketTestMarkers.RemoveAll(x => ParseRulerDocketMarker(x)?.Value<string>("runId") == runId);
            return new JObject { ["ok"] = true, ["runId"] = runId,
                ["phase"] = "cleanup_marker", ["removed"] = removed, ["saveName"] = saveName };
        }

        private JObject ChancellorPreflight(string runId, string saveName)
        {
            Hero candidate = SelectEmergencyChancellorCandidate();
            ReignChancellorOffice office = EnsureRulerDocketState().Chancellor;
            JArray assertions = new JArray
            {
                Assertion("player_is_current_ruler", Clan.PlayerClan?.Kingdom?.Leader == Hero.MainHero),
                Assertion("capital_court_has_royal_command_access", HasRoyalCommandAccess),
                Assertion("eligible_chancellor_candidate_available", candidate != null),
                Assertion("chancellor_office_vacant_for_fixture", office.IsVacant)
            };
            return new JObject
            {
                ["ok"] = assertions.All(x => x.Value<bool?>("passed") == true),
                ["runId"] = runId, ["profile"] = "ruler_docket",
                ["phase"] = "chancellor_preflight", ["saveName"] = saveName,
                ["candidateHeroId"] = candidate?.StringId ?? string.Empty,
                ["candidateName"] = candidate?.Name?.ToString() ?? string.Empty,
                ["assertions"] = assertions
            };
        }

        private JObject PrepareChancellorFixture(string runId, JObject options,
            string saveName, string gameInstanceId)
        {
            if (!string.Equals(options.Value<string>("confirmation"),
                    RulerDocketFixtureConfirmation, StringComparison.Ordinal))
                return RulerDocketTestFailure(runId, "chancellor_prepare",
                    "The exact disposable-save ruler-docket fixture confirmation is required.", saveName, saveName);
            ReignRulerDocketState state = EnsureRulerDocketState();
            if (!state.Chancellor.IsVacant)
                return RulerDocketTestFailure(runId, "chancellor_prepare",
                    "The Chancellor office must be vacant on this disposable branch.", saveName, saveName);
            Hero candidate = SelectEmergencyChancellorCandidate();
            int salary = Math.Max(0, options.Value<int?>("salary")
                ?? ReignRulerDocketRules.DefaultActiveChancellorSalary);
            string receipt = string.Empty;
            if (candidate == null || !TryAppointChancellor(candidate, salary, true, out receipt))
                return RulerDocketTestFailure(runId, "chancellor_prepare",
                    receipt ?? "No eligible Chancellor candidate is available.", saveName, saveName);
            JObject marker = new JObject
            {
                ["runId"] = runId, ["fixtureType"] = "chancellor",
                ["candidateHeroId"] = candidate.StringId, ["termId"] = state.Chancellor.TermId,
                ["salary"] = salary, ["preparedDay"] = CurrentDay(),
                ["gameInstanceId"] = gameInstanceId ?? string.Empty, ["saveName"] = saveName
            };
            StoreRulerDocketMarker(marker);
            return new JObject { ["ok"] = state.Chancellor.State == ReignChancellorOfficeState.Inactive,
                ["runId"] = runId, ["profile"] = "ruler_docket", ["phase"] = "chancellor_prepare",
                ["office"] = ChancellorTestJson(state.Chancellor), ["receipt"] = receipt,
                ["explicitAgreementSeam"] = true, ["naturalConversationRequiredForLaunch"] = true };
        }

        private JObject ExerciseChancellorFixture(string runId, JObject options, string saveName)
        {
            JObject marker = FindRulerDocketMarker(runId);
            ReignRulerDocketState state = EnsureRulerDocketState();
            ReignChancellorOffice office = state.Chancellor;
            if (marker == null || office.IsVacant || marker.Value<string>("termId") != office.TermId)
                return RulerDocketTestFailure(runId, "chancellor_exercise",
                    "The run-owned Chancellor fixture is missing.", saveName, saveName);
            string scenario = (options.Value<string>("scenario") ?? string.Empty).Trim().ToLowerInvariant().Replace('_', '-');
            int day = CurrentDay();
            int rulerGoldBefore = Hero.MainHero?.Gold ?? 0;
            int paidBefore = office.TotalPaidGold;
            int failedBefore = office.FailedPaymentDays;
            int petitionCountBefore = state.Petitions.Count;
            bool passed;
            if (scenario == "inactive-zero-pay")
            {
                office.State = ReignChancellorOfficeState.Inactive;
                state.LastOfficeTickDay = day - 1;
                ProcessRulerDocketOfficeTick(day);
                passed = Hero.MainHero.Gold == rulerGoldBefore && office.TotalPaidGold == paidBefore
                    && office.State == ReignChancellorOfficeState.Inactive;
            }
            else if (scenario == "active-paid")
            {
                Hero.MainHero.Gold = Math.Max(Hero.MainHero.Gold, office.Salary + 1000);
                rulerGoldBefore = Hero.MainHero.Gold;
                office.State = ReignChancellorOfficeState.Active;
                office.LastPaidDay = day - 2;
                if (!state.ActiveChancellorDays.Contains(day - 1)) state.ActiveChancellorDays.Add(day - 1);
                state.LastOfficeTickDay = day - 1;
                ProcessRulerDocketOfficeTick(day);
                passed = office.TotalPaidGold == paidBefore + office.Salary
                    && Hero.MainHero.Gold == rulerGoldBefore - office.Salary
                    && office.State == ReignChancellorOfficeState.Active;
            }
            else if (scenario == "unpaid-inactive")
            {
                if (office.Salary <= 0)
                    return RulerDocketTestFailure(runId, "chancellor_exercise",
                        "The unpaid-inactive scenario requires a positive Active-day salary.", saveName, saveName);
                office.State = ReignChancellorOfficeState.Active;
                office.LastPaidDay = day - 2;
                if (!state.ActiveChancellorDays.Contains(day - 1)) state.ActiveChancellorDays.Add(day - 1);
                Hero.MainHero.Gold = Math.Max(0, office.Salary - 1);
                rulerGoldBefore = Hero.MainHero.Gold;
                state.LastOfficeTickDay = day - 1;
                ProcessRulerDocketOfficeTick(day);
                passed = office.State == ReignChancellorOfficeState.Inactive
                    && office.TotalPaidGold == paidBefore && Hero.MainHero.Gold == rulerGoldBefore
                    && office.FailedPaymentDays == failedBefore + 1;
            }
            else if (scenario == "suppression")
            {
                office.State = ReignChancellorOfficeState.Active;
                state.LastGeneratedDay = day - 1;
                GenerateRulerDocketForDay(day);
                passed = state.Petitions.Count == petitionCountBefore && state.LastGeneratedDay == day;
            }
            else return RulerDocketTestFailure(runId, "chancellor_exercise",
                "scenario must be inactive-zero-pay, active-paid, unpaid-inactive, or suppression.", saveName, saveName);
            return new JObject { ["ok"] = passed, ["runId"] = runId,
                ["profile"] = "ruler_docket", ["phase"] = "chancellor_exercise",
                ["scenario"] = scenario, ["office"] = ChancellorTestJson(office),
                ["rulerGoldBefore"] = rulerGoldBefore, ["rulerGoldAfter"] = Hero.MainHero?.Gold ?? 0,
                ["petitionCountBefore"] = petitionCountBefore, ["petitionCountAfter"] = state.Petitions.Count };
        }

        private JObject PrepareEmergencyChancellorFixture(string runId, JObject options,
            string saveName, string gameInstanceId)
        {
            if (!string.Equals(options.Value<string>("confirmation"),
                    RulerDocketFixtureConfirmation, StringComparison.Ordinal))
                return RulerDocketTestFailure(runId, "emergency_prepare",
                    "The exact disposable-save ruler-docket fixture confirmation is required.", saveName, saveName);
            ReignRulerDocketState state = EnsureRulerDocketState();
            if (!state.Chancellor.IsVacant)
                return RulerDocketTestFailure(runId, "emergency_prepare",
                    "The Chancellor office must be vacant before emergency succession is tested.", saveName, saveName);
            int day = CurrentDay();
            MaintainChancellorCaptivityLifecycle(day, true);
            ReignChancellorOffice office = state.Chancellor;
            if (office.IsVacant || !office.Emergency)
                return RulerDocketTestFailure(runId, "emergency_prepare",
                    "No eligible emergency Chancellor assumed authority.", saveName, saveName);
            StoreRulerDocketMarker(new JObject
            {
                ["runId"] = runId, ["fixtureType"] = "emergency_chancellor",
                ["candidateHeroId"] = office.HeroId, ["termId"] = office.TermId,
                ["correlationId"] = office.EmergencyCorrelationId, ["startDay"] = day,
                ["gameInstanceId"] = gameInstanceId ?? string.Empty, ["saveName"] = saveName
            });
            return new JObject { ["ok"] = office.State == ReignChancellorOfficeState.EmergencyActive
                    && office.Salary == 0 && office.SuppressesPetitions,
                ["runId"] = runId, ["profile"] = "ruler_docket", ["phase"] = "emergency_prepare",
                ["office"] = ChancellorTestJson(office), ["syntheticCaptivityOverride"] = true,
                ["mechanicsRemainProduction"] = true };
        }

        private JObject ReleaseEmergencyChancellorFixture(string runId, string saveName)
        {
            JObject marker = FindRulerDocketMarker(runId);
            ReignChancellorOffice office = EnsureRulerDocketState().Chancellor;
            if (marker == null || marker.Value<string>("termId") != office.TermId)
                return RulerDocketTestFailure(runId, "emergency_release",
                    "The run-owned emergency Chancellor fixture is missing.", saveName, saveName);
            MaintainChancellorCaptivityLifecycle(CurrentDay(), false);
            return new JObject { ["ok"] = office.State == ReignChancellorOfficeState.Handoff
                    && office.HandoffDueDay == CurrentDay() + 3 && office.Salary == 0,
                ["runId"] = runId, ["phase"] = "emergency_release",
                ["office"] = ChancellorTestJson(office) };
        }

        private JObject CompleteEmergencyChancellorFixture(string runId, string saveName)
        {
            JObject marker = FindRulerDocketMarker(runId);
            ReignChancellorOffice office = EnsureRulerDocketState().Chancellor;
            if (marker == null || marker.Value<string>("termId") != office.TermId
                || office.State != ReignChancellorOfficeState.Handoff)
                return RulerDocketTestFailure(runId, "emergency_complete",
                    "The run-owned emergency Chancellor is not in handoff.", saveName, saveName);
            int completionDay = office.HandoffDueDay;
            MaintainChancellorCaptivityLifecycle(completionDay, false);
            ReignRulerDocketState state = EnsureRulerDocketState();
            string heroId = marker.Value<string>("candidateHeroId") ?? string.Empty;
            bool memoryQueued = state.PendingMemoryJobs.Any(x => x.HeroId == heroId
                && x.EventType == "emergency_chancellor_service");
            bool startedHistory = state.PendingWorldHistoryJobs.Any(x => x.ChancellorHeroId == heroId && x.Phase == "started");
            bool completedHistory = state.PendingWorldHistoryJobs.Any(x => x.ChancellorHeroId == heroId && x.Phase == "completed");
            return new JObject { ["ok"] = state.Chancellor.IsVacant && memoryQueued
                    && startedHistory && completedHistory,
                ["runId"] = runId, ["phase"] = "emergency_complete",
                ["officeVacant"] = state.Chancellor.IsVacant, ["memoryQueued"] = memoryQueued,
                ["takeoverWorldHistoryQueued"] = startedHistory,
                ["conclusionWorldHistoryQueued"] = completedHistory,
                ["completionDay"] = completionDay };
        }

        private JObject ObserveEmergencyChancellorFixture(string runId, string saveName)
        {
            TryProcessPendingDocketWorldHistoryJobs();
            TryProcessPendingDocketMemoryJobs();
            JObject marker = FindRulerDocketMarker(runId);
            if (marker == null) return RulerDocketTestFailure(runId, "emergency_observe",
                "The run-owned emergency Chancellor marker is missing.", saveName, saveName);
            string heroId = marker.Value<string>("candidateHeroId") ?? string.Empty;
            ReignRulerDocketState state = EnsureRulerDocketState();
            List<ReignDocketWorldHistoryJob> history = state.PendingWorldHistoryJobs
                .Where(x => x.ChancellorHeroId == heroId).ToList();
            ReignDocketMemoryJob memory = state.PendingMemoryJobs.LastOrDefault(x => x.HeroId == heroId
                && x.EventType == "emergency_chancellor_service");
            bool serviceHistory = state.History.Any(x => x.ChancellorHeroId == heroId
                && x.Outcome == "emergency_handoff_completed");
            bool complete = history.Any(x => x.Phase == "started" && x.Applied)
                && history.Any(x => x.Phase == "completed" && x.Applied)
                && memory?.Applied == true && !string.IsNullOrWhiteSpace(memory.ReceiptId);
            return new JObject { ["ok"] = complete && serviceHistory, ["runId"] = runId,
                ["phase"] = "emergency_observe", ["worldHistoryDelivered"] = complete && history.All(x => x.Applied),
                ["fullPrivateMemoryDelivered"] = memory?.Applied == true,
                ["memoryReceiptId"] = memory?.ReceiptId ?? string.Empty,
                ["serviceHistoryRecorded"] = serviceHistory,
                ["worldHistoryJobs"] = new JArray(history.Select(x => new JObject
                    { ["phase"] = x.Phase, ["applied"] = x.Applied, ["jobId"] = x.JobId })) };
        }

        private string RulerDocketFingerprint(string runId)
        {
            ReignDocketPetition petition = FindRulerDocketTestPetition(runId);
            JObject value = new JObject
            {
                ["petition"] = PetitionTestJson(petition),
                ["commitments"] = new JArray(EnsureRulerDocketState().Commitments
                    .Where(x => x.PetitionId == petition?.PetitionId).Select(x => new JObject
                    { ["kind"] = x.Kind.ToString(), ["start"] = x.StartDay, ["end"] = x.EndDay,
                        ["active"] = x.Active, ["lastApplied"] = x.LastAppliedDay })),
                ["expeditions"] = new JArray(EnsureRulerDocketState().Expeditions
                    .Where(x => x.PetitionId == petition?.PetitionId).Select(x => new JObject
                    { ["departure"] = x.DepartureDay, ["return"] = x.ReturnDay,
                        ["casualties"] = x.HiddenCasualtyCount, ["returned"] = x.Returned }))
            };
            using (SHA256 sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(
                    value.ToString(Newtonsoft.Json.Formatting.None)))).Replace("-", string.Empty).ToLowerInvariant();
        }

        private void StoreRulerDocketMarker(JObject marker)
        {
            string runId = marker?.Value<string>("runId") ?? string.Empty;
            _rulerDocketTestMarkers.RemoveAll(x => ParseRulerDocketMarker(x)?.Value<string>("runId") == runId);
            _rulerDocketTestMarkers.Add(marker.ToString(Newtonsoft.Json.Formatting.None));
            while (_rulerDocketTestMarkers.Count > 16) _rulerDocketTestMarkers.RemoveAt(0);
        }

        private JObject FindRulerDocketMarker(string runId) => (_rulerDocketTestMarkers ?? new List<string>())
            .Select(ParseRulerDocketMarker).LastOrDefault(x => x?.Value<string>("runId") == runId);
        private static JObject ParseRulerDocketMarker(string value)
        { try { return string.IsNullOrWhiteSpace(value) ? null : JObject.Parse(value); } catch { return null; } }
        private ReignDocketPetition FindRulerDocketTestPetition(string runId) => EnsureRulerDocketState().Petitions
            .LastOrDefault(x => x?.PetitionId?.StartsWith("petition_test_" + runId + "_", StringComparison.Ordinal) == true);
        private static string NormalizeFixtureAction(string value)
        {
            string action = (value ?? "grant-direct").Trim().ToLowerInvariant().Replace('_', '-');
            return action == "fund-instead" || action == "refuse" ? action : "grant-direct";
        }
        private static JObject Assertion(string id, bool passed) => new JObject { ["id"] = id, ["passed"] = passed };
        private static JObject PetitionTestJson(ReignDocketPetition petition) => petition == null ? new JObject() : new JObject
        { ["petitionId"] = petition.PetitionId, ["kind"] = petition.Kind.ToString(), ["severity"] = petition.Severity.ToString(),
            ["state"] = petition.State.ToString(), ["grantMethod"] = petition.GrantMethod.ToString(),
            ["receivedDay"] = petition.ReceivedDay, ["decidedDay"] = petition.DecidedDay,
            ["targetSettlementId"] = petition.TargetSettlementId, ["petitionerHeroId"] = petition.PetitionerHeroId,
            ["termsHash"] = petition.TermsHash, ["durationDays"] = petition.DurationDays,
            ["hiddenCasualties"] = petition.HiddenCasualtyCount };
        private static JObject QuoteTestJson(ReignPetitionDecisionQuote quote) => quote == null ? new JObject() : new JObject
        { ["valid"] = quote.Valid, ["canGrantDirect"] = quote.CanGrantDirect,
            ["canGrantWithGold"] = quote.CanGrantWithGold, ["directGoldCost"] = quote.DirectGoldCost,
            ["goldSubstituteCost"] = quote.GoldSubstituteCost, ["foodStockCost"] = quote.FoodStockCost,
            ["soldierCount"] = quote.SoldierCount, ["healthyGarrison"] = quote.HealthyGarrison,
            ["requiredGarrisonReserve"] = quote.RequiredGarrisonReserve, ["error"] = quote.Error };
        private static JObject ChancellorTestJson(ReignChancellorOffice office) => office == null ? new JObject() : new JObject
        { ["termId"] = office.TermId, ["heroId"] = office.HeroId, ["heroName"] = office.HeroName,
            ["state"] = office.State.ToString(), ["salary"] = office.Salary,
            ["desiredActive"] = office.DesiredActive, ["effectiveDay"] = office.DesiredActiveEffectiveDay,
            ["lastPaidDay"] = office.LastPaidDay, ["totalPaidGold"] = office.TotalPaidGold,
            ["paidActiveDays"] = office.PaidActiveDays, ["failedPaymentDays"] = office.FailedPaymentDays,
            ["emergency"] = office.Emergency, ["emergencyStartedDay"] = office.EmergencyStartedDay,
            ["rulerReleasedDay"] = office.RulerReleasedDay, ["handoffDueDay"] = office.HandoffDueDay,
            ["suppressesPetitions"] = office.SuppressesPetitions };
        private static JObject RulerDocketTestFailure(string runId, string phase, string error,
            string expected, string active) => new JObject { ["ok"] = false, ["runId"] = runId ?? string.Empty,
            ["profile"] = "ruler_docket", ["phase"] = phase ?? string.Empty, ["error"] = error ?? string.Empty,
            ["expectedSaveName"] = expected ?? string.Empty, ["activeSaveName"] = active ?? string.Empty };
    }
}
