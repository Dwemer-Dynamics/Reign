using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Reign.Core.Contracts.Court;
using ReignBeta.UI;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using static ReignBeta.Court.Campaign.ReignCourtCampaignBehavior;
using ReignBeta.Court.UI;

namespace ReignBeta.Court.Campaign
{
    internal static partial class ReignCourtTestControls
    {
        private static JObject VerifyInsufficientPetitionGuardrail(ReignCourtCampaignBehavior court, string runId, string saveName)
        {
            JObject marker = FindRulerDocketMarker(court, runId);
            ReignDocketPetition petition = FindRulerDocketTestPetition(court, runId);
            if (marker == null || petition == null || !petition.IsPending)
                return RulerDocketTestFailure(runId, "guardrail_insufficient",
                    "A run-owned pending petition is required.", saveName, saveName);

            Settlement capital = court.CurrentCapital;
            ReignRulerDocketState state = court.EnsureRulerDocketState();
            int historyBefore = state.History.Count;
            int commitmentsBefore = state.Commitments.Count;
            int expeditionsBefore = state.Expeditions.Count;
            if (petition.Kind == ReignPetitionKind.Food)
                capital.Town.FoodStocks = Math.Max(0f, petition.FoodStockCost - 1f);
            else if (petition.Kind == ReignPetitionKind.TownGold
                || petition.Kind == ReignPetitionKind.VillageGold)
                Hero.MainHero.Gold = Math.Max(0, petition.GoldCost - 1);
            else
            {
                MobileParty garrison = capital?.Town?.GarrisonParty;
                int healthy = garrison?.MemberRoster?.GetTroopRoster()
                    .Where(x => x.Character != null && !x.Character.IsHero)
                    .Sum(x => x.Number - x.WoundedNumber) ?? 0;
                int retain = Math.Max(0, Reign.Core.Contracts.Court.ReignRulerDocketRules
                    .MinimumHealthyGarrisonAfterDispatch(healthy));
                int remove = Math.Max(0, healthy - retain);
                foreach (var element in garrison?.MemberRoster?.GetTroopRoster()
                    .Where(x => x.Character != null && !x.Character.IsHero).ToList()
                    ?? new List<TaleWorlds.CampaignSystem.Roster.TroopRosterElement>())
                {
                    int take = Math.Min(remove, Math.Max(0, element.Number - element.WoundedNumber));
                    if (take > 0) garrison.MemberRoster.AddToCounts(element.Character, -take);
                    remove -= take;
                    if (remove <= 0) break;
                }
            }

            ReignPetitionDecisionQuote quote = court.GetRulerPetitionQuote(petition.PetitionId);
            bool commandCompleted = ReignCourtPetitionScreenManager.TryExecuteAutomationAction(
                "grant-direct", out string commandError);
            bool pendingAfterAttempt = petition.IsPending;
            bool unchanged = state.History.Count == historyBefore
                && state.Commitments.Count == commitmentsBefore
                && state.Expeditions.Count == expeditionsBefore;
            bool cleanupRefused = ReignCourtPetitionScreenManager.TryExecuteAutomationAction(
                "refuse", out string cleanupError);
            return new JObject
            {
                ["ok"] = quote.Valid && !quote.CanGrantDirect && !commandCompleted
                    && pendingAfterAttempt && unchanged && cleanupRefused,
                ["runId"] = runId, ["profile"] = "ruler_docket",
                ["phase"] = "guardrail_insufficient", ["saveName"] = saveName,
                ["kind"] = petition.Kind.ToString(), ["quote"] = QuoteTestJson(quote),
                ["productionViewModelRejectedGrant"] = !commandCompleted,
                ["pendingAfterFailedGrant"] = pendingAfterAttempt,
                ["historyAndEffectsUnchanged"] = unchanged,
                ["cleanupRefusedAfterAssertion"] = cleanupRefused,
                ["commandError"] = commandError ?? string.Empty,
                ["cleanupError"] = cleanupError ?? string.Empty
            };
        }

        private static JObject InvalidatePetitionerIdentityFixture(ReignCourtCampaignBehavior court, string runId, string saveName)
        {
            ReignDocketPetition petition = FindRulerDocketTestPetition(court, runId);
            if (petition == null || !petition.IsPending)
                return RulerDocketTestFailure(runId, "invalidate_identity",
                    "A run-owned pending petition is required.", saveName, saveName);
            petition.PetitionerHeroId = "missing_petitioner_" + runId;
            ReignPetitionDecisionQuote quote = court.GetRulerPetitionQuote(petition.PetitionId);
            return new JObject
            {
                ["ok"] = !quote.Valid && petition.IsPending,
                ["runId"] = runId, ["phase"] = "invalidate_identity",
                ["petition"] = PetitionTestJson(petition), ["quote"] = QuoteTestJson(quote),
                ["noDecisionApplied"] = petition.IsPending
            };
        }

        private static JObject VerifyTechnicalPetitionReturn(ReignCourtCampaignBehavior court, string runId, string saveName)
        {
            ReignDocketPetition petition = FindRulerDocketTestPetition(court, runId);
            ReignRulerDocketState state = court.EnsureRulerDocketState();
            int historyBefore = state.History.Count;
            int commitmentsBefore = state.Commitments.Count;
            bool available = ReignCourtPetitionScreenManager.AutomationCanTechnicalReturn;
            bool returned = ReignCourtPetitionScreenManager.TryExecuteAutomationAction(
                "technical-return", out string error);
            bool untouched = petition?.IsPending == true && state.History.Count == historyBefore
                && state.Commitments.Count == commitmentsBefore;
            return new JObject
            {
                ["ok"] = available && returned && !ReignCourtPetitionScreenManager.IsOpen && untouched,
                ["runId"] = runId, ["phase"] = "technical_return",
                ["technicalReturnAvailable"] = available,
                ["productionAudienceClosed"] = !ReignCourtPetitionScreenManager.IsOpen,
                ["petitionRemainedPending"] = petition?.IsPending == true,
                ["noDecisionOrEffectApplied"] = untouched, ["error"] = error ?? string.Empty
            };
        }

        private static JObject PrepareChancellorScheduleFixture(ReignCourtCampaignBehavior court, string runId, string saveName)
        {
            JObject marker = FindRulerDocketMarker(court, runId);
            ReignChancellorOffice office = court.EnsureRulerDocketState().Chancellor;
            if (marker == null || office.IsVacant || marker.Value<string>("termId") != office.TermId)
                return RulerDocketTestFailure(runId, "chancellor_schedule_prepare",
                    "The run-owned Chancellor fixture is missing.", saveName, saveName);
            Hero.MainHero.Gold = Math.Max(Hero.MainHero.Gold, office.Salary + 1000);
            int day = CurrentDay();
            bool scheduled = court.TryScheduleChancellorActive(true, out string error);
            marker["scheduledDay"] = day;
            marker["effectiveDay"] = office.DesiredActiveEffectiveDay;
            StoreRulerDocketMarker(court, marker);
            return new JObject
            {
                ["ok"] = scheduled && office.State == ReignChancellorOfficeState.Inactive
                    && office.DesiredActive && office.DesiredActiveEffectiveDay == day + 1,
                ["runId"] = runId, ["phase"] = "chancellor_schedule_prepare",
                ["office"] = ChancellorTestJson(office), ["currentDay"] = day,
                ["requiresGuardedAdvancementToDay"] = day + 1,
                ["requiresEightAmBoundary"] = true, ["error"] = error ?? string.Empty
            };
        }

        private static JObject VerifyChancellorScheduleFixture(ReignCourtCampaignBehavior court, string runId, string saveName)
        {
            JObject marker = FindRulerDocketMarker(court, runId);
            ReignChancellorOffice office = court.EnsureRulerDocketState().Chancellor;
            int expectedDay = marker?.Value<int?>("effectiveDay") ?? -1;
            bool history = court.EnsureRulerDocketState().History.Any(x => x.Type == "chancellor_service"
                && x.Outcome == "activated" && x.ChancellorHeroId == office.HeroId && x.Day >= expectedDay);
            return new JObject
            {
                ["ok"] = marker != null && CurrentDay() >= expectedDay
                    && office.State == ReignChancellorOfficeState.Active
                    && office.DesiredActiveEffectiveDay == -1 && history,
                ["runId"] = runId, ["phase"] = "chancellor_schedule_verify",
                ["currentDay"] = CurrentDay(), ["expectedEffectiveDay"] = expectedDay,
                ["office"] = ChancellorTestJson(office), ["activationHistoryRecorded"] = history
            };
        }

        private static JObject VerifyChancellorEligibilityAndDismissal(ReignCourtCampaignBehavior court, string runId, JObject options,
            string saveName)
        {
            if (!string.Equals(options.Value<string>("confirmation"),
                    RulerDocketFixtureConfirmation, StringComparison.Ordinal))
                return RulerDocketTestFailure(runId, "chancellor_eligibility_dismissal",
                    "The exact disposable-save ruler-docket fixture confirmation is required.", saveName, saveName);
            ReignRulerDocketState state = court.EnsureRulerDocketState();
            if (!state.Chancellor.IsVacant)
                return RulerDocketTestFailure(runId, "chancellor_eligibility_dismissal",
                    "The Chancellor office must be vacant.", saveName, saveName);
            bool rulerRejected = !court.TryAppointChancellor(Hero.MainHero, 500, true, out string rulerError);
            Hero eligible = court.SelectEmergencyChancellorCandidate();
            string appointmentReceipt = string.Empty;
            string unvalidatedReceipt = string.Empty;
            string dismissalReceipt = string.Empty;
            bool appointed = eligible != null && court.TryAppointChancellor(eligible, 500, true, out appointmentReceipt);
            string heroId = state.Chancellor.HeroId;
            bool unvalidatedRejected = appointed && !court.TryDismissChancellor(false, out unvalidatedReceipt)
                && !state.Chancellor.IsVacant;
            bool dismissed = unvalidatedRejected && court.TryDismissChancellor(true, out dismissalReceipt)
                && state.Chancellor.IsVacant;
            bool history = state.History.Any(x => x.Type == "chancellor_service"
                && x.Outcome == "dismissed_by_ruler" && x.ChancellorHeroId == heroId);
            return new JObject
            {
                ["ok"] = rulerRejected && appointed && unvalidatedRejected && dismissed && history,
                ["runId"] = runId, ["phase"] = "chancellor_eligibility_dismissal",
                ["rulerRejectedAsIneligible"] = rulerRejected,
                ["eligibleCandidateAppointed"] = appointed,
                ["unvalidatedDismissalRejected"] = unvalidatedRejected,
                ["validatedDismissalCompleted"] = dismissed,
                ["dismissalHistoryRecorded"] = history,
                ["rulerError"] = rulerError ?? string.Empty,
                ["appointmentReceipt"] = appointmentReceipt ?? string.Empty,
                ["unvalidatedReceipt"] = unvalidatedReceipt ?? string.Empty,
                ["dismissalReceipt"] = dismissalReceipt ?? string.Empty
            };
        }

        private static JObject VerifyLegacyDocketMigration(ReignCourtCampaignBehavior court, string runId, JObject options, string saveName)
        {
            if (!string.Equals(options.Value<string>("confirmation"),
                    RulerDocketFixtureConfirmation, StringComparison.Ordinal))
                return RulerDocketTestFailure(runId, "legacy_migration",
                    "The exact disposable-save ruler-docket fixture confirmation is required.", saveName, saveName);
            string matterId = "legacy_test_matter_" + runId;
            string regentId = "legacy_test_regent_" + runId;
            CourtMatter matter = new CourtMatter
            {
                MatterId = matterId, SourceKey = "settlement_supply:" + runId,
                Title = "Legacy docket launch fixture", State = ReignCourtMatterState.Queued,
                SettlementStringId = court.CurrentCapital?.StringId ?? string.Empty
            };
            CourtRegentAssignment regent = new CourtRegentAssignment
            {
                AssignmentId = regentId,
                HeroStringId = court.SelectEmergencyChancellorCandidate()?.StringId ?? string.Empty,
                IsActive = true
            };
            court._matters.Add(matter);
            court._agenda.Add(new CourtAgendaItem { MatterId = matterId, AgendaDay = CurrentDay() });
            court._regents.Add(regent);
            ReignRulerDocketState state = court.EnsureRulerDocketState();
            state.LegacyMigrationComplete = false;
            int historyBefore = state.History.Count;
            court.MigrateLegacyDocketOnce();
            bool matterHistory = state.History.Any(x => x.Type == "legacy_matter"
                && x.Outcome == "legacy_retired" && x.Summary == matter.Title);
            bool regentHistory = state.History.Any(x => x.Type == "legacy_regent"
                && x.Outcome == "legacy_retired" && x.ChancellorHeroId == regent.HeroStringId);
            int historyRecordsAdded = state.History.Count - historyBefore;
            return new JObject
            {
                ["ok"] = state.LegacyMigrationComplete
                    && matter.State == ReignCourtMatterState.Invalidated
                    && matter.ResolutionMode == "legacy_docket_migration"
                    && !court._agenda.Any(x => x.MatterId == matterId)
                    && !regent.IsActive && regent.DismissalReason == "legacy_regent_replaced_by_chancellor"
                    && matterHistory && regentHistory && historyRecordsAdded >= 2,
                ["runId"] = runId, ["phase"] = "legacy_migration",
                ["legacyMatterRetired"] = matter.State == ReignCourtMatterState.Invalidated,
                ["legacyAgendaRemoved"] = !court._agenda.Any(x => x.MatterId == matterId),
                ["legacyRegentRetired"] = !regent.IsActive,
                ["historyRecordsAdded"] = historyRecordsAdded,
                ["fixtureHistoryRecordsFound"] = matterHistory && regentHistory,
                ["migrationComplete"] = state.LegacyMigrationComplete
            };
        }

        private static JObject PrepareNaturalDocketSoak(ReignCourtCampaignBehavior court, string runId, JObject options,
            string saveName, string gameInstanceId)
        {
            if (!string.Equals(options.Value<string>("confirmation"),
                    RulerDocketFixtureConfirmation, StringComparison.Ordinal))
                return RulerDocketTestFailure(runId, "natural_soak_prepare",
                    "The exact disposable-save ruler-docket fixture confirmation is required.", saveName, saveName);
            int days = Math.Max(7, Math.Min(180, options.Value<int?>("soakDays") ?? 30));
            ReignRulerDocketState state = court.EnsureRulerDocketState();
            JObject marker = new JObject
            {
                ["runId"] = runId, ["fixtureType"] = "natural_soak",
                ["startDay"] = CurrentDay(), ["requiredDays"] = days,
                ["pendingAtStart"] = state.Petitions.Count(x => x.IsPending),
                ["pendingIdsAtStart"] = new JArray(state.Petitions.Where(x => x.IsPending)
                    .Select(x => x.PetitionId)),
                ["pendingNobleAtStart"] = state.NobleMatters.Count(x => x.IsPending),
                ["pendingNobleIdsAtStart"] = new JArray(state.NobleMatters.Where(x => x.IsPending)
                    .Select(x => x.MatterId)),
                ["historyAtStart"] = state.History.Count,
                ["gameInstanceId"] = gameInstanceId ?? string.Empty, ["saveName"] = saveName
            };
            StoreRulerDocketMarker(court, marker);
            return new JObject
            {
                ["ok"] = state.LegacyMigrationComplete,
                ["runId"] = runId, ["phase"] = "natural_soak_prepare",
                ["startDay"] = CurrentDay(), ["requiredDays"] = days,
                ["targetDay"] = CurrentDay() + days,
                ["requiresGuardedNaturalAdvancement"] = true
            };
        }

        private static JObject VerifyNaturalDocketSoak(ReignCourtCampaignBehavior court, string runId, JObject options,
            string saveName, string gameInstanceId)
        {
            JObject marker = FindRulerDocketMarker(court, runId);
            ReignRulerDocketState state = court.EnsureRulerDocketState();
            int startDay = marker?.Value<int?>("startDay") ?? int.MaxValue;
            int requiredDays = marker?.Value<int?>("requiredDays") ?? 30;
            int day = CurrentDay();
            bool uniqueIds = state.Petitions.Where(x => x != null)
                .GroupBy(x => x.PetitionId, StringComparer.Ordinal).All(x => x.Count() == 1);
            bool uniqueNobleIds = state.NobleMatters.Where(x => x != null)
                .GroupBy(x => x.MatterId, StringComparer.Ordinal).All(x => x.Count() == 1);
            List<string> pendingIdsAtStart = (marker?["pendingIdsAtStart"] as JArray
                ?? new JArray()).Values<string>().Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
            bool noPendingPetitionExpired = pendingIdsAtStart.All(id => state.Petitions.Any(x => x != null
                && x.PetitionId == id && x.IsPending));
            List<string> pendingNobleIdsAtStart = (marker?["pendingNobleIdsAtStart"] as JArray
                ?? new JArray()).Values<string>().Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
            bool noPendingNobleExpired = pendingNobleIdsAtStart.All(id =>
                state.NobleMatters.Any(x => x != null && x.MatterId == id && x.IsPending));
            bool noStaleCommitments = state.Commitments.All(x => x == null || !x.Active || x.EndDay >= day);
            bool noStaleExpeditions = state.Expeditions.All(x => x == null || x.Returned || x.ReturnDay > day);
            bool ticksAdvanced = state.LastOfficeTickDay >= startDay
                && state.LastGeneratedDay >= startDay;
            return new JObject
            {
                ["ok"] = marker != null && day >= startDay + requiredDays && uniqueIds
                    && uniqueNobleIds && noPendingPetitionExpired && noPendingNobleExpired
                    && noStaleCommitments && noStaleExpeditions
                    && ticksAdvanced && state.LegacyMigrationComplete,
                ["runId"] = runId, ["phase"] = "natural_soak_verify",
                ["startDay"] = startDay, ["currentDay"] = day,
                ["advancedDays"] = day - startDay, ["requiredDays"] = requiredDays,
                ["uniquePetitionIds"] = uniqueIds,
                ["uniqueNobleMatterIds"] = uniqueNobleIds,
                ["noPetitionExpired"] = noPendingPetitionExpired,
                ["noNobleMatterExpired"] = noPendingNobleExpired,
                ["noStaleCommitments"] = noStaleCommitments,
                ["noStaleExpeditions"] = noStaleExpeditions,
                ["dailyTicksAdvanced"] = ticksAdvanced,
                ["pendingAtStart"] = marker?.Value<int?>("pendingAtStart") ?? 0,
                ["pendingAtEnd"] = state.Petitions.Count(x => x.IsPending),
                ["pendingNobleAtStart"] = marker?.Value<int?>("pendingNobleAtStart") ?? 0,
                ["pendingNobleAtEnd"] = state.NobleMatters.Count(x => x.IsPending),
                ["historyAdded"] = state.History.Count - (marker?.Value<int?>("historyAtStart") ?? state.History.Count),
                ["gameInstanceId"] = gameInstanceId ?? string.Empty
            };
        }
    }
}
