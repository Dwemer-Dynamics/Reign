using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Reign.Core.Contracts.Court;
using ReignBeta.UI;
using ReignBeta.UI.Calibration;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Settlements;

namespace ReignBeta.Court
{
    public sealed partial class ReignCourtCampaignBehavior
    {
        private JObject RunCourtLifeTestPhase(string runId, string phase, JObject options, string save, string instance)
        {
            if (!new[] { "court_life_check", "court_life_preflight", "court_life_family_fixture", "court_life_prepare", "court_life_open", "court_life_converse",
                "court_life_choose_confirm", "court_life_snapshot", "court_life_observe", "court_life_clock_observe", "court_life_save_prepare", "court_life_save_verify" }.Contains(phase))
                return RulerDocketTestFailure(runId, phase, "Unsupported court-life evidence phase.", save, save);
            if (phase == "court_life_check") return new JObject { ["ok"] = true, ["saveName"] = save };
            if (phase == "court_life_preflight") return new JObject { ["ok"] = HasRoyalCommandAccess && CurrentCapital?.IsUnderSiege == false,
                ["schema"] = "reign-court-life-evidence-v1", ["runId"] = runId, ["phase"] = phase,
                ["saveName"] = save, ["capitalId"] = CurrentCapital?.StringId, ["playerGold"] = Hero.MainHero?.Gold,
                ["familyInventory"] = CourtLifeFamilyFixtureInventory(),
                ["captiveInventory"] = CourtLifeCaptiveInventory(),
                ["patronageEligibility"] = CourtLifePatronageEligibility(options.Value<string>("courtLifeTemplateId")),
                ["pendingMatterInventory"] = CourtLifePendingMatterInventory(options),
                ["sources"] = new JArray("International", "Family", "Patronage", "Visitor"),
                ["internationalTemplateCount"] = ReignInternationalDocketCatalog.Templates.Count,
                ["patronageTemplateCount"] = ReignPatronageCatalog.Templates.Count };
            if (phase == "court_life_family_fixture") return PrepareCourtLifeFamilyFixture(runId, options, save, instance);
            if (phase == "court_life_prepare") return PrepareCourtLifeFixture(runId, options, save, instance);
            JObject marker = FindRulerDocketMarker(runId);
            ReignCourtLifeMatter matter = FindCourtLifeTestMatter(runId);
            if (marker == null || matter == null) return RulerDocketTestFailure(runId, phase, "No exact court-life fixture is bound to this run.", save, save);
            if (phase == "court_life_snapshot")
            {
                bool saved = ReignUiCalibrationService.TrySaveSnapshot("ReignCourtPetitionScreen", out string path, out string error);
                JObject snapshot = CourtLifeEvidence(runId, phase, save, matter);
                snapshot["audienceArt"] = ReignCourtPetitionScreenManager.AutomationSceneEvidence;
                snapshot["ok"] = saved; snapshot["snapshotPath"] = path; snapshot["error"] = error; return snapshot;
            }
            if (phase == "court_life_open")
            {
                bool opened = ReignCourtPetitionScreenManager.TryOpenForAutomation(this, matter, out string error);
                JObject result = CourtLifeEvidence(runId, phase, save, matter);
                result["ok"] = opened; result["error"] = error; return result;
            }
            if (phase == "court_life_choose_confirm")
            {
                string selected = options.Value<string>("courtLifeOptionId") ?? string.Empty;
                if (!matter.Options.Any(x => x.OptionId == selected))
                    return RulerDocketTestFailure(runId, phase, "The requested exact option is absent from this production quote.", save, save);
                if (!ReignCourtPetitionScreenManager.AutomationNaturalConversationComplete)
                    return RulerDocketTestFailure(runId, phase, "A completed natural player turn is required before confirmation.", save, save);
                marker["beforeDecision"] = CourtLifeNativeState(matter); StoreRulerDocketMarker(marker);
                bool chosen = ReignCourtPetitionScreenManager.TryExecuteAutomationAction("choose", selected, out string chooseError);
                bool confirmed = chosen && ReignCourtPetitionScreenManager.TryExecuteAutomationAction("confirm", "", out chooseError);
                JObject result = CourtLifeEvidence(runId, phase, save, matter);
                JObject payload = ParseObject(matter.PayloadJson);
                JObject receipt = payload["nativeReceipt"] as JObject;
                JObject native = result["nativeState"] as JObject;
                JObject international = native?["international"] as JObject;
                JObject action = international?["nativeDiplomaticAction"] as JObject;
                string decisionExpectation = options.Value<string>("courtLifeExpectation") ?? "snapshot";
                bool expectedPending = decisionExpectation == "pending" && chosen && !confirmed
                    && selected == "accept" && payload.Value<string>("nativeOptionId") == selected
                    && matter.Source == ReignDocketSource.International && matter.IsPending && !matter.EffectsCommitted
                    && string.IsNullOrEmpty(matter.ResolutionReceiptId) && payload.Value<bool?>("nativeEffectCommitted") != true
                    && receipt?.Value<bool?>("Success") == true && receipt.Value<bool?>("Completed") == false
                    && action?.Value<bool?>("serviceAvailable") == true && action.Value<int?>("count") == 1
                    && action.Value<string>("status") == "Executing"
                    && native.Value<int?>("rulerGold") == marker["beforeDecision"]?.Value<int?>("rulerGold")
                    && (native["relations"] as JArray)?.Count == 0 && (native["history"] as JArray)?.Count == 0;
                result["ok"] = confirmed || expectedPending;
                result["usedProductionChooseAndConfirm"] = chosen;
                result["productionConfirmationCompleted"] = confirmed;
                result["expectedPendingDiplomaticAction"] = expectedPending;
                result["error"] = expectedPending ? string.Empty : chooseError;
                if (options.Value<string>("courtLifeExpectation") == "pending")
                {
                    result["ok"] = expectedPending;
                    result["assertions"] = new JArray(Assertion("pending_diplomacy_confirmation_has_no_settlement_effects", expectedPending));
                }
                else if (decisionExpectation == "resolved")
                {
                    var decisionAssertions = new JArray(Assertion("matter_resolved_with_receipt", chosen && confirmed
                        && matter.EffectsCommitted && matter.State == ReignCourtLifeMatterState.Resolved
                        && !string.IsNullOrWhiteSpace(matter.ResolutionReceiptId)));
                    AddCourtLifeDecisionAssertions(decisionAssertions, marker, matter);
                    result["assertions"] = decisionAssertions;
                }
                return result;
            }
            if (phase == "court_life_save_prepare")
            {
                if (HasPendingFamilyVisitWork || HasPendingInternationalCourtLifeWork || HasPendingCourtLifeDeliveryWork
                    || EnsureRulerDocketState().PendingRelationAdjustments.Any(x => !x.Applied))
                    return RulerDocketTestFailure(runId, phase, "Wait for family, international and relation delivery queues to drain before checkpoint preparation.", save, save);
                marker["courtLifeSaveFingerprint"] = CourtLifeFingerprint(matter); marker["courtLifeSaveInstance"] = instance;
                StoreRulerDocketMarker(marker);
            }
            JObject evidence = CourtLifeEvidence(runId, phase, save, matter);
            var assertions = new JArray { Assertion("exact_matter_bound", marker.Value<string>("courtLifeMatterId") == matter.MatterId),
                Assertion("no_hidden_statistics_in_npc_speech", matter.ConversationLines.Where(x => x.Role == "assistant")
                    .All(x => !ReignRulerDocketRules.ContainsForbiddenNobleDocketStatistics(x.Text))) };
            if (phase == "court_life_save_prepare") AddCourtLifeFingerprintContractAssertions(assertions);
            if (phase == "court_life_save_verify")
            {
                assertions.Add(Assertion("different_game_instance", !string.IsNullOrWhiteSpace(marker.Value<string>("courtLifeSaveInstance")) && marker.Value<string>("courtLifeSaveInstance") != instance));
                assertions.Add(Assertion("state_fingerprint_survived_reload", marker.Value<string>("courtLifeSaveFingerprint") == CourtLifeFingerprint(matter)));
            }
            if (phase == "court_life_clock_observe") assertions.Add(Assertion("native_campaign_time_advanced", CurrentCourtLifeDay() > marker.Value<double>("preparedDay")));
            string expectation = options.Value<string>("courtLifeExpectation") ?? "snapshot";
            if (phase == "court_life_clock_observe") assertions.Add(Assertion("clock_observation_names_required_behavior", expectation != "snapshot"));
            int expectedTurns = options.Value<int?>("courtLifeExpectedTurns") ?? -1;
            if (expectedTurns >= 0) assertions.Add(Assertion("exact_completed_player_turns", matter.CompletedPlayerTurnIds.Count == expectedTurns));
            if (expectation == "pending") assertions.Add(Assertion("matter_pending_without_effects", matter.IsPending && !matter.EffectsCommitted));
            else if (expectation == "resolved")
            {
                assertions.Add(Assertion("matter_resolved_with_receipt", matter.EffectsCommitted && matter.State == ReignCourtLifeMatterState.Resolved && !string.IsNullOrWhiteSpace(matter.ResolutionReceiptId)));
                AddCourtLifeDecisionAssertions(assertions, marker, matter);
            }
            else if (expectation == "delivery_complete")
            {
                ReignPatronageCommission c = EnsureRulerDocketState().PatronageCommissions.FirstOrDefault(x => x.MatterId == matter.MatterId);
                assertions.Add(Assertion("commission_completed_after_due_day", c != null && c.Paid && c.Completed && CurrentCourtLifeDay() >= c.DueDay));
                if (c != null)
                {
                    int expected = c.Objective == ReignPatronageObjective.Loyalty ? c.SettlementIds.Count : c.Objective == ReignPatronageObjective.Praise ? c.HeroIds.Count : 0;
                    assertions.Add(Assertion("all_recipients_have_one_delivery_or_exclusion", c.DeliveryReceipts.Distinct().Count() == c.DeliveryReceipts.Count && c.DeliveryReceipts.Count + c.ExcludedRecipientIds.Count == expected));
                    assertions.Add(Assertion("durable_work_and_single_completion_history", EnsureRulerDocketState().PatronageWorks.Count(w => w.CommissionId == c.CommissionId) == 1
                        && EnsureRulerDocketState().History.Count(h => h.RecordId == c.CommissionId + "_complete") == 1));
                    assertions.Add(Assertion("praise_adjustments_have_real_receipts", c.Objective != ReignPatronageObjective.Praise || EnsureRulerDocketState().PendingRelationAdjustments
                        .Where(a => a.AdjustmentId.StartsWith(c.CommissionId + "_praise_", StringComparison.Ordinal)).All(a => a.Applied && !string.IsNullOrWhiteSpace(a.ReceiptId) && a.Delta == 3 && a.SubjectHeroId == c.RulerHeroId)));
                    assertions.Add(Assertion("loyalty_native_before_after_exactly_plus_five", c.Objective != ReignPatronageObjective.Loyalty
                        || (c.NativeDeliveryEffects.Count == c.DeliveryReceipts.Count && c.NativeDeliveryEffects.Select(x => x.ReceiptId).Distinct().Count() == c.NativeDeliveryEffects.Count
                            && c.NativeDeliveryEffects.All(x => x.Stat == "settlement_loyalty" && Math.Abs(x.After - Math.Min(100, x.Before + 5)) < 0.001))));
                }
            }
            else if (expectation == "arrived")
            {
                assertions.Add(Assertion("all_visitors_physically_resident", AreNobleVisitorsAvailable(matter, out _)));
                ReignNobleVisitorStay stay = EnsureRulerDocketState().NobleVisitorStays.FirstOrDefault(x => x.MatterId == matter.MatterId);
                IReadOnlyList<CastleRoomSessionRecord> schedule = EnsureCastleSchedule();
                List<string> visitorIds = (stay?.Members ?? new List<ReignNobleVisitorMember>())
                    .Where(x => x.Arrived && !x.Released).Select(x => x.HeroId).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                CastleRoomSessionRecord guestBedrooms = schedule.FirstOrDefault(x => x.Room == (int)CastleChat.CastleRoom.GuestBedrooms);
                List<string> guestIds = SplitIds(guestBedrooms?.OccupantHeroIdsCsv);
                assertions.Add(Assertion("active_visitors_exposed_once_in_guest_bedrooms", visitorIds.Count > 0
                    && visitorIds.All(id => guestIds.Contains(id, StringComparer.OrdinalIgnoreCase)
                        && schedule.Sum(room => SplitIds(room.OccupantHeroIdsCsv).Count(x => string.Equals(x, id, StringComparison.OrdinalIgnoreCase))) == 1)));
            }
            else if (expectation == "departed")
            {
                ReignNobleVisitorStay stay = EnsureRulerDocketState().NobleVisitorStays.FirstOrDefault(x => x.MatterId == matter.MatterId);
                assertions.Add(Assertion("all_visitors_released_to_native_ai", stay != null && stay.Departed && stay.Members.All(m => m.Released && !IsNobleVisitorReserved(FindHero(m.HeroId)))));
                if (stay != null) assertions.Add(Assertion("recorded_native_return_locations", stay.Members.Where(m => !string.IsNullOrWhiteSpace(m.ReturnSettlementId))
                    .All(m => FindHero(m.HeroId)?.CurrentSettlement?.StringId == m.ReturnSettlementId)));
            }
            else if (expectation == "dismissed" || expectation == "neglected" || expectation == "reconciled")
            {
                AddCourtLifeFamilyTransitionAssertions(assertions, marker, matter, expectation, evidence);
            }
            else if (expectation != "snapshot") assertions.Add(Assertion("known_expectation", false));
            evidence["assertions"] = assertions; evidence["ok"] = assertions.All(a => a.Value<bool?>("passed") == true);
            return evidence;
        }

        private JObject PrepareCourtLifeFixture(string runId, JObject options, string save, string instance)
        {
            if (options.Value<string>("confirmation") != RulerDocketFixtureConfirmation || !HasRoyalCommandAccess)
                return RulerDocketTestFailure(runId, "court_life_prepare", "The armed disposable fixture confirmation and production capital Court are required.", save, save);
            if (FindCourtLifeTestMatter(runId) != null) return RulerDocketTestFailure(runId, "court_life_prepare", "This fixtureRunId already has a matter. Reuse it for observations or choose a new run ID.", save, save);
            if (!Enum.TryParse(options.Value<string>("courtLifeSource"), true, out ReignDocketSource source)
                || source == ReignDocketSource.Notable || source == ReignDocketSource.DomesticNoble)
                return RulerDocketTestFailure(runId, "court_life_prepare", "Select International, Family, Patronage, or Visitor.", save, save);
            string template = options.Value<string>("courtLifeTemplateId") ?? "";
            string existingId = options.Value<string>("courtLifeExistingMatterId") ?? "";
            if (!string.IsNullOrEmpty(existingId))
                return BindExactExistingCourtLifeFixture(runId, source, existingId, template, options, save, instance);
            if (source == ReignDocketSource.Patronage && !string.IsNullOrWhiteSpace(template))
            {
                JObject existing = TryBindExistingPatronageFixture(runId, template, options, save, instance);
                if (existing != null) return existing;
            }
            int slot = ReignCourtLifeRules.StableRoll(runId, 1000000);
            string reason = "No eligible production participants or required historical facts were available for the exact requested template.";
            double preparedWorldDay = CurrentCourtLifeDay();
            string captiveHeroId = options.Value<string>("courtLifeFixtureCaptiveHeroId") ?? string.Empty;
            if (!string.IsNullOrEmpty(captiveHeroId) && source != ReignDocketSource.International)
                return RulerDocketTestFailure(runId, "court_life_prepare", "Captive setup is restricted to International captive templates.", save, save);
            JObject captiveReceipt = null;
            ReignCourtLifeMatter matter = !string.IsNullOrEmpty(captiveHeroId)
                ? PrepareCourtLifeCaptiveMatter(runId, captiveHeroId, template, slot, save, instance, out captiveReceipt, out reason)
                : source == ReignDocketSource.Patronage ? TryCreatePatronageMatterForSlot(Session.CampaignId, Session.TimelineId, CurrentDay(), slot, template)
                : source == ReignDocketSource.Visitor ? TryCreateNobleVisitorMatterForSlot(Session.CampaignId, Session.TimelineId, CurrentDay(), slot, template)
                : source == ReignDocketSource.Family ? TryCreateFamilyVisitMatterForSlot(Session.CampaignId, Session.TimelineId, CurrentDay(), slot, options.Value<string>("courtLifeHeroId"), template)
                : TryCreateInternationalTemplateFixture(template, Session.CampaignId, Session.TimelineId, CurrentDay(), slot, out reason);
            if (matter == null)
            {
                JObject failure = RulerDocketTestFailure(runId, "court_life_prepare", reason, save, save);
                failure["captiveFixture"] = captiveReceipt ?? FindRulerDocketMarker(runId)?["captiveFixture"];
                if (source == ReignDocketSource.Patronage)
                    failure["patronageEligibility"] = CourtLifePatronageEligibility(template);
                return failure;
            }
            EnsureRulerDocketState().CourtLifeMatters.Add(matter);
            int gold = options.Value<int?>("courtLifeFixtureGold") ?? -1;
            if (gold >= 0)
            {
                int delta = gold - Hero.MainHero.Gold;
                if (delta > 0) GiveGoldAction.ApplyBetweenCharacters(null, Hero.MainHero, delta, true);
                if (delta < 0) GiveGoldAction.ApplyBetweenCharacters(Hero.MainHero, null, -delta, true);
            }
            int loyalty = options.Value<int?>("courtLifeFixtureLoyalty") ?? -1;
            if (source == ReignDocketSource.Patronage && loyalty >= 0 && loyalty <= 100)
                foreach (Settlement settlement in EligiblePatronageSettlements(matter.KingdomId, CurrentCapital)) settlement.Town.Loyalty = loyalty;
            var marker = new JObject { ["runId"] = runId, ["courtLifeMatterId"] = matter.MatterId, ["courtLifeSource"] = source.ToString(),
                ["preparedDay"] = CurrentCourtLifeDay(), ["preparedGameInstance"] = instance, ["beforeDecision"] = CourtLifeNativeState(matter) };
            if (captiveReceipt != null) marker["captiveFixture"] = captiveReceipt;
            if (source == ReignDocketSource.Family) marker["familyBaseline"] = CourtLifeFamilyObservation(matter);
            StoreRulerDocketMarker(marker); StateChanged?.Invoke();
            JObject result = CourtLifeEvidence(runId, "court_life_prepare", save, matter);
            result["ok"] = true; result["productionEligibility"] = true; result["requiresBaselineRollback"] = true;
            result["fixtureChanges"] = new JObject { ["gold"] = gold, ["kingdomSettlementLoyalty"] = loyalty,
                ["captive"] = captiveReceipt,
                ["templateWasForcedWithinEligibility"] = !string.IsNullOrWhiteSpace(template), ["nativeTimeAdvanced"] = false };
            if (source == ReignDocketSource.Visitor)
            {
                ReignNobleVisitorStay stay = EnsureRulerDocketState().NobleVisitorStays.SingleOrDefault(s => s.MatterId == matter.MatterId);
                bool immediate = stay != null && stay.Arrived && !stay.Departed
                    && stay.ActualArrivalDay == preparedWorldDay && stay.ArrivalDay == preparedWorldDay
                    && stay.DepartureDay == preparedWorldDay + stay.StayDays
                    && matter.State == ReignCourtLifeMatterState.Pending && matter.AvailableDay == preparedWorldDay
                    && CurrentCourtLifeDay() == preparedWorldDay && stay.Members.Count > 0
                    && stay.Members.All(m => m.Arrived && !m.Released
                        && FindHero(m.HeroId)?.CurrentSettlement?.StringId == stay.HostSettlementId);
                result["assertions"] = new JArray(new JObject { ["id"] = "visitors_placed_immediately_without_time_advance", ["passed"] = immediate });
                result["ok"] = immediate;
                if (!immediate) result["error"] = "Visitor preparation must immediately establish the complete group at court and start its full stay without advancing time.";
            }
            return result;
        }

        private JObject CourtLifePendingMatterInventory(JObject options)
        {
            string source = options.Value<string>("courtLifeSource") ?? "";
            string template = options.Value<string>("courtLifeTemplateId") ?? "";
            var matches = EnsureRulerDocketState().CourtLifeMatters.Where(m => m.IsPending
                && (string.IsNullOrEmpty(source) || string.Equals(m.Source.ToString(), source, StringComparison.OrdinalIgnoreCase))
                && (string.IsNullOrEmpty(template) || m.TemplateId == template)).OrderBy(m => m.MatterId, StringComparer.Ordinal).ToList();
            return new JObject { ["totalCount"] = matches.Count, ["limit"] = 100, ["truncated"] = matches.Count > 100,
                ["sourceFilter"] = source, ["templateFilter"] = template,
                ["matters"] = new JArray(matches.Take(100).Select(m => new JObject {
                    ["matterId"] = m.MatterId, ["templateId"] = m.TemplateId, ["source"] = m.Source.ToString(),
                    ["title"] = m.Title, ["state"] = m.State.ToString(), ["kingdomId"] = m.KingdomId,
                    ["effectsCommitted"] = m.EffectsCommitted, ["completedPlayerTurns"] = m.CompletedPlayerTurnIds.Count,
                    ["boundFixtureRunIds"] = new JArray(_rulerDocketTestMarkers.Select(ParseRulerDocketMarker)
                        .Where(x => x?.Value<string>("courtLifeMatterId") == m.MatterId).Select(x => x.Value<string>("runId"))) })) };
        }

        private JObject BindExactExistingCourtLifeFixture(string runId, ReignDocketSource source, string matterId,
            string templateId, JObject options, string save, string instance)
        {
            var state = EnsureRulerDocketState();
            var matches = state.CourtLifeMatters.Where(m => m.MatterId == matterId).ToList();
            if (matches.Count != 1)
                return RulerDocketTestFailure(runId, "court_life_prepare", "Exactly one existing matter must match the supplied ID; no fixture was created.", save, save);
            ReignCourtLifeMatter matter = matches[0];
            JObject payload = ParseObject(matter.PayloadJson);
            string error = null;
            if (source != ReignDocketSource.International && source != ReignDocketSource.Patronage)
                error = "Exact existing binding supports International and Patronage only.";
            if (!matter.IsPending || matter.EffectsCommitted || !string.IsNullOrEmpty(matter.ResolutionReceiptId)
                || !string.IsNullOrEmpty(matter.PendingPlayerTurnId) || payload.Value<bool?>("nativeActionStarted") == true
                || payload.Value<bool?>("nativeEffectCommitted") == true || HasPendingInternationalCourtLifeWork || HasPendingCourtLifeDeliveryWork)
                error = "The existing matter must be pending with no committed or in-flight effects or conversation.";
            bool kingdomMatches = source == ReignDocketSource.International
                ? payload["player"]?.Value<string>("kingdomId") == Clan.PlayerClan?.Kingdom?.StringId
                    && payload["player"]?.Value<string>("leaderHeroId") == Hero.MainHero?.StringId
                    && payload["foreign"]?.Value<string>("kingdomId") == matter.KingdomId
                    && !string.IsNullOrEmpty(matter.KingdomId) && matter.KingdomId != Clan.PlayerClan?.Kingdom?.StringId
                : matter.KingdomId == Clan.PlayerClan?.Kingdom?.StringId;
            if (matter.Source != source || matter.CampaignId != Session.CampaignId || matter.TimelineId != Session.TimelineId
                || matter.ReignId != CurrentReignId() || matter.SettlementId != CurrentCapital?.StringId || !kingdomMatches
                || (!string.IsNullOrEmpty(templateId) && matter.TemplateId != templateId))
                error = "The existing matter does not match the exact source, template, ruler, kingdom, campaign, timeline and capital.";
            if ((options.Value<int?>("courtLifeFixtureGold") ?? -1) >= 0 || (options.Value<int?>("courtLifeFixtureLoyalty") ?? -1) >= 0
                || options.Value<bool?>("courtLifeFixtureAgeRulerTo34") == true
                || !string.IsNullOrEmpty(options.Value<string>("courtLifeFixtureCaptiveHeroId"))
                || !string.IsNullOrEmpty(options.Value<string>("courtLifeHeroId")))
                error = "Existing matter binding cannot be combined with fixture mutations or participant selection.";
            if (FindRulerDocketMarker(runId) != null || _rulerDocketTestMarkers.Select(ParseRulerDocketMarker)
                .Any(m => m?.Value<string>("courtLifeMatterId") == matterId))
                error = "The fixture ID or existing matter is already bound. Continue its original fixture.";
            if (error != null) return RulerDocketTestFailure(runId, "court_life_prepare", error, save, save);
            string before = CourtLifeFingerprint(matter);
            int count = state.CourtLifeMatters.Count;
            StoreRulerDocketMarker(new JObject { ["runId"] = runId, ["courtLifeMatterId"] = matter.MatterId,
                ["courtLifeSource"] = source.ToString(), ["preparedDay"] = CurrentCourtLifeDay(), ["preparedGameInstance"] = instance,
                ["beforeDecision"] = CourtLifeNativeState(matter), ["reusedExistingMatter"] = true, ["existingMatterFingerprint"] = before });
            bool preserved = before == CourtLifeFingerprint(matter) && count == state.CourtLifeMatters.Count;
            JObject result = CourtLifeEvidence(runId, "court_life_prepare", save, matter);
            result["ok"] = preserved; result["reusedExistingMatter"] = true; result["requiresBaselineRollback"] = true;
            result["fixtureChanges"] = new JObject { ["markerOnly"] = true, ["nativeTimeAdvanced"] = false };
            result["assertions"] = new JArray(Assertion("existing_matter_binding_preserves_matter_and_native_state", preserved));
            return result;
        }

        private JObject TryBindExistingPatronageFixture(string runId, string templateId, JObject options, string save, string instance)
        {
            var state = EnsureRulerDocketState();
            var pending = state.CourtLifeMatters.Where(m => m.IsPending && m.TemplateId == templateId).ToList();
            if (pending.Count == 0) return null;
            string error = null;
            if (pending.Count != 1) error = "The exact template has multiple pending matters; automatic fixture binding is ambiguous.";
            ReignCourtLifeMatter matter = pending.Count == 1 ? pending[0] : null;
            if (matter != null && (matter.Source != ReignDocketSource.Patronage || matter.EffectsCommitted
                || matter.CampaignId != Session.CampaignId || matter.TimelineId != Session.TimelineId
                || matter.ReignId != CurrentReignId() || matter.KingdomId != Clan.PlayerClan?.Kingdom?.StringId
                || matter.SettlementId != CurrentCapital?.StringId))
                error = "The pending matter does not belong to this ruler, campaign, timeline and capital.";
            if ((options.Value<int?>("courtLifeFixtureGold") ?? -1) >= 0
                || (options.Value<int?>("courtLifeFixtureLoyalty") ?? -1) >= 0
                || !string.IsNullOrEmpty(options.Value<string>("courtLifeFixtureCaptiveHeroId")))
                error = "Existing matters cannot be combined with fixture gold, loyalty or captive mutations.";
            if (matter != null && _rulerDocketTestMarkers.Select(ParseRulerDocketMarker)
                .Any(m => m?.Value<string>("courtLifeMatterId") == matter.MatterId))
                error = "This pending matter is already bound to a fixture. Continue that fixture instead.";
            if (error != null)
            {
                JObject failure = RulerDocketTestFailure(runId, "court_life_prepare", error, save, save);
                failure["patronageEligibility"] = CourtLifePatronageEligibility(templateId);
                return failure;
            }
            string before = CourtLifeFingerprint(matter);
            int matterCount = state.CourtLifeMatters.Count;
            var marker = new JObject { ["runId"] = runId, ["courtLifeMatterId"] = matter.MatterId,
                ["courtLifeSource"] = ReignDocketSource.Patronage.ToString(), ["preparedDay"] = CurrentCourtLifeDay(),
                ["preparedGameInstance"] = instance, ["beforeDecision"] = CourtLifeNativeState(matter),
                ["reusedExistingMatter"] = true, ["existingMatterFingerprint"] = before };
            StoreRulerDocketMarker(marker);
            bool preserved = before == CourtLifeFingerprint(matter) && state.CourtLifeMatters.Count == matterCount;
            JObject result = CourtLifeEvidence(runId, "court_life_prepare", save, matter);
            result["ok"] = preserved;
            result["reusedExistingMatter"] = true;
            result["requiresBaselineRollback"] = true;
            result["fixtureChanges"] = new JObject { ["markerOnly"] = true, ["nativeTimeAdvanced"] = false };
            result["assertions"] = new JArray(Assertion("existing_patronage_binding_preserves_matter_and_native_state", preserved));
            return result;
        }

        private JObject CourtLifePatronageEligibility(string requestedTemplateId)
        {
            var state = EnsureRulerDocketState();
            Hero ruler = Hero.MainHero;
            Settlement host = FindCurrentCapital();
            Kingdom kingdom = Clan.PlayerClan?.Kingdom;
            var templates = new JArray();
            foreach (ReignPatronageTemplate template in ReignPatronageCatalog.Templates.Where(t =>
                string.IsNullOrWhiteSpace(requestedTemplateId) || t.Id == requestedTemplateId))
            {
                var missing = new JArray();
                if (host == null) missing.Add("capital_missing");
                if (kingdom == null || ruler == null || kingdom.Leader != ruler) missing.Add("player_not_ruler");
                if (template.RequiresSpouse && ruler?.Spouse?.IsAlive != true) missing.Add("living_spouse_required");
                if (template.RequiresChild && (ruler == null || !ruler.Children.Any(c => c != null && c.IsAlive))) missing.Add("living_child_required");
                if (template.Id == "patronage-coming-of-age" && (ruler == null || !PatronageComingOfAgeChildren(ruler).Any())) missing.Add("coming_of_age_child_required");
                if ((template.Id == "patronage-returning-artist" || template.Id == "patronage-popular-performer")
                    && !state.PatronageCreators.Any(c => c.Role == template.CreatorRole && c.HomeSettlementId == host?.StringId
                        && state.PatronageWorks.Any(w => w.CreatorId == c.ActorId))) missing.Add("prior_creator_work_required");
                var subjects = PatronageSubjectHistory(template).Take(5).ToList();
                if (template.RequiresHistoricalSubject && subjects.Count == 0) missing.Add("public_historical_subject_required");
                var pending = state.CourtLifeMatters.Where(m => m.IsPending && m.TemplateId == template.Id).ToList();
                if (pending.Count > 0) missing.Add("same_template_pending");
                templates.Add(new JObject { ["templateId"] = template.Id, ["eligible"] = missing.Count == 0,
                    ["unmetPrerequisites"] = missing, ["requiredContext"] = template.RequiredContext,
                    ["historicalSubjects"] = new JArray(subjects.Select(s => s.Summary)),
                    ["pendingCount"] = pending.Count, ["pendingMatterIds"] = new JArray(pending.Take(5).Select(m => m.MatterId)) });
            }
            return new JObject { ["readOnly"] = true, ["requestedTemplateId"] = requestedTemplateId ?? "",
                ["templateKnown"] = string.IsNullOrWhiteSpace(requestedTemplateId) || templates.Count > 0,
                ["capitalId"] = host?.StringId, ["templates"] = templates };
        }

        internal ReignCourtLifeMatter FindCourtLifeTestMatter(string runId)
        {
            string id = FindRulerDocketMarker(runId)?.Value<string>("courtLifeMatterId");
            return EnsureRulerDocketState().CourtLifeMatters.FirstOrDefault(x => x.MatterId == id);
        }

        private JObject CourtLifeEvidence(string runId, string phase, string save, ReignCourtLifeMatter matter)
        {
            return new JObject { ["schema"] = "reign-court-life-evidence-v1", ["ok"] = true, ["runId"] = runId, ["phase"] = phase,
                ["saveName"] = save, ["worldDay"] = CurrentCourtLifeDay(), ["matterId"] = matter.MatterId,
                ["source"] = matter.Source.ToString(), ["matter"] = JObject.FromObject(matter),
                ["nativeState"] = CourtLifeNativeState(matter), ["fixture"] = FindRulerDocketMarker(runId),
                ["nativeTimeAdvancedByHarness"] = false, ["savedByHarness"] = false };
        }

        private JObject CourtLifeNativeState(ReignCourtLifeMatter matter)
        {
            var state = EnsureRulerDocketState();
            JObject family = FamilyMirror();
            return new JObject { ["rulerGold"] = Hero.MainHero?.Gold ?? 0,
                ["international"] = matter.Source == ReignDocketSource.International ? CourtLifeInternationalNativeState(matter) : null,
                ["settlements"] = new JArray(Settlement.All.Where(s => s.Town != null && s.OwnerClan?.Kingdom?.StringId == matter.KingdomId)
                    .OrderBy(s => s.StringId).Select(s => new JObject { ["id"] = s.StringId, ["loyalty"] = s.Town.Loyalty, ["kingdomId"] = s.OwnerClan?.Kingdom?.StringId })),
                ["participants"] = new JArray(matter.Participants.Select(p => new JObject { ["heroId"] = p.HeroId, ["actorId"] = p.ActorId,
                    ["age"] = FindHero(p.HeroId)?.Age ?? p.Age, ["settlementId"] = FindHero(p.HeroId)?.CurrentSettlement?.StringId,
                    ["partyId"] = FindHero(p.HeroId)?.PartyBelongedTo?.StringId, ["isPrisoner"] = FindHero(p.HeroId)?.IsPrisoner ?? false })),
                ["commissions"] = JArray.FromObject(state.PatronageCommissions.Where(c => c.MatterId == matter.MatterId)),
                ["works"] = JArray.FromObject(state.PatronageWorks.Where(w => state.PatronageCommissions.Any(c => c.MatterId == matter.MatterId && c.CommissionId == w.CommissionId))),
                ["stays"] = JArray.FromObject(state.NobleVisitorStays.Where(s => s.MatterId == matter.MatterId)),
                ["relations"] = JArray.FromObject(state.PendingRelationAdjustments.Where(a => a.AdjustmentId.Contains(matter.MatterId))),
                ["familyAttention"] = family,
                ["familyNativeAges"] = new JArray((family["members"] as JArray ?? new JArray()).OfType<JObject>()
                    .OrderBy(m => m.Value<string>("hero_id"), StringComparer.Ordinal).Select(m => {
                        string id = m.Value<string>("hero_id"); Hero hero = FindHero(id);
                        return new JObject { ["heroId"] = id, ["exists"] = hero != null, ["age"] = hero == null ? JValue.CreateNull() : new JValue(hero.Age) };
                    })),
                ["familyOutboxCount"] = state.FamilyVisitOutbox?.Count ?? 0,
                ["familyAttentionPending"] = HasPendingFamilyVisitWork,
                ["history"] = JArray.FromObject(state.History.Where(h => h.PetitionId == matter.MatterId || h.RecordId.Contains(matter.MatterId))) };
        }

        private string CourtLifeFingerprint(ReignCourtLifeMatter matter)
            => CourtLifeSnapshotFingerprint(new JObject { ["matter"] = JObject.FromObject(matter), ["native"] = CourtLifeNativeState(matter) });

        private static string CourtLifeSnapshotFingerprint(JObject snapshot)
        {
            JObject stable = (JObject)snapshot.DeepClone();
            // Compare native ages separately from the family mirror's last-observed age.
            // Keep all durable family values; observation time is not saved game state.
            JObject family = stable["native"]?["familyAttention"] as JObject;
            if (stable["native"]?["familyNativeAges"] is JArray)
            {
                family?.Remove("worldDay");
                foreach (JObject member in (family?["members"] as JArray ?? new JArray()).OfType<JObject>())
                {
                    string raw = member.Value<string>("profile_json");
                    if (string.IsNullOrWhiteSpace(raw)) continue;
                    try { JObject profile = JObject.Parse(raw); profile.Remove("age"); member["profile_json"] = profile.ToString(Formatting.None); }
                    catch (JsonException) { /* Preserve malformed data verbatim so it cannot disappear from the comparison. */ }
                }
            }
            foreach (var collection in new[] { new { Name = "members", Id = "hero_id" }, new { Name = "visits", Id = "visit_id" } })
                if (family?[collection.Name] is JArray rows)
                    family[collection.Name] = new JArray(rows.OrderBy(row => (row as JObject)?.Value<string>(collection.Id) ?? "", StringComparer.Ordinal)
                        .ThenBy(row => row.ToString(Formatting.None), StringComparer.Ordinal).Select(row => row.DeepClone()));
            return ReignCourtTerms.Hash(stable.ToString(Formatting.None));
        }

        private static void AddCourtLifeFingerprintContractAssertions(JArray assertions)
        {
            var snapshot = new JObject { ["native"] = new JObject { ["familyAttention"] = new JObject {
                ["members"] = new JArray(new JObject { ["hero_id"] = "child", ["dismissals"] = 1 }, new JObject { ["hero_id"] = "spouse", ["dismissals"] = 2 }),
                ["visits"] = new JArray(new JObject { ["visit_id"] = "visit_b", ["status"] = "dismissed" }, new JObject { ["visit_id"] = "visit_a", ["status"] = "pending" }) } } };
            string original = snapshot.ToString(Formatting.None), fingerprint = CourtLifeSnapshotFingerprint(snapshot);
            JObject reversed = (JObject)snapshot.DeepClone();
            foreach (string key in new[] { "members", "visits" })
                reversed["native"]["familyAttention"][key] = new JArray(((JArray)reversed["native"]["familyAttention"][key]).Reverse().Select(row => row.DeepClone()));
            assertions.Add(Assertion("family_row_order_does_not_change_fingerprint", fingerprint == CourtLifeSnapshotFingerprint(reversed)
                && snapshot.ToString(Formatting.None) == original));
            reversed["native"]["familyAttention"]["members"][0]["dismissals"] = 3;
            assertions.Add(Assertion("family_value_change_changes_fingerprint", fingerprint != CourtLifeSnapshotFingerprint(reversed)));
            var observed = (JObject)snapshot.DeepClone();
            observed["native"]["familyNativeAges"] = new JArray(new JObject { ["heroId"] = "child", ["exists"] = true, ["age"] = 4.5 });
            observed["native"]["familyAttention"]["worldDay"] = 100.1;
            observed["native"]["familyAttention"]["members"][0]["profile_json"] = "{\"age\":4.49,\"isChild\":true}";
            string observedFingerprint = CourtLifeSnapshotFingerprint(observed);
            JObject refreshed = (JObject)observed.DeepClone();
            refreshed["native"]["familyAttention"]["worldDay"] = 100.2;
            refreshed["native"]["familyAttention"]["members"][0]["profile_json"] = "{\"age\":4.5,\"isChild\":true}";
            assertions.Add(Assertion("family_cached_age_refresh_does_not_change_fingerprint", observedFingerprint == CourtLifeSnapshotFingerprint(refreshed)));
            refreshed["native"]["familyNativeAges"][0]["age"] = 4.6;
            assertions.Add(Assertion("family_native_age_change_changes_fingerprint", observedFingerprint != CourtLifeSnapshotFingerprint(refreshed)));
            refreshed = (JObject)observed.DeepClone();
            refreshed["native"]["familyAttention"]["members"][0]["profile_json"] = "{\"age\":4.49,\"isChild\":false}";
            assertions.Add(Assertion("family_profile_value_change_changes_fingerprint", observedFingerprint != CourtLifeSnapshotFingerprint(refreshed)));
        }

        private JObject CourtLifeFamilyObservation(ReignCourtLifeMatter matter)
        {
            JObject mirror = FamilyMirror();
            string hero = matter.Participants.FirstOrDefault()?.HeroId ?? "", player = Hero.MainHero?.StringId ?? "";
            JObject member = (mirror["members"] as JArray ?? new JArray()).OfType<JObject>().FirstOrDefault(x => x.Value<string>("hero_id") == hero);
            JObject visit = (mirror["visits"] as JArray ?? new JArray()).OfType<JObject>().FirstOrDefault(x => x.Value<string>("visit_id") == matter.MatterId);
            bool scoped = matter.Source == ReignDocketSource.Family && !string.IsNullOrWhiteSpace(hero) && !string.IsNullOrWhiteSpace(player)
                && mirror.Value<string>("campaignId") == matter.CampaignId && mirror.Value<string>("timelineId") == matter.TimelineId
                && mirror.Value<string>("playerId") == player;
            return new JObject { ["visitId"] = matter.MatterId, ["heroId"] = hero, ["playerId"] = player,
                ["campaignId"] = matter.CampaignId, ["timelineId"] = matter.TimelineId, ["scoped"] = scoped,
                ["worldDay"] = mirror.Value<double?>("worldDay"), ["member"] = member?.DeepClone(), ["visit"] = visit?.DeepClone(),
                ["quiescent"] = !HasPendingFamilyVisitWork && (EnsureRulerDocketState().FamilyVisitOutbox?.Count ?? 0) == 0 };
        }

        private static bool SameCourtLifeFamilyObservationScope(JObject left, JObject right)
            => left?.Value<bool?>("scoped") == true && right?.Value<bool?>("scoped") == true
                && new[] { "campaignId", "timelineId", "playerId", "heroId", "visitId" }.All(key =>
                    !string.IsNullOrWhiteSpace(left.Value<string>(key)) && left.Value<string>(key) == right.Value<string>(key));

        private void AddCourtLifeFamilyTransitionAssertions(JArray assertions, JObject marker, ReignCourtLifeMatter matter, string expectation, JObject evidence)
        {
            JObject baseline = marker["familyBaseline"] as JObject, current = CourtLifeFamilyObservation(matter);
            JObject before = baseline?["member"] as JObject, member = current["member"] as JObject, visit = current["visit"] as JObject;
            bool bound = SameCourtLifeFamilyObservationScope(baseline, current) && before != null && member != null;
            bool ready = bound && current.Value<bool?>("quiescent") == true;
            bool exactVisit = ready && visit?.Value<string>("visit_id") == matter.MatterId
                && visit.Value<string>("hero_id") == current.Value<string>("heroId") && visit.Value<string>("player_id") == current.Value<string>("playerId")
                && visit.Value<string>("campaign_id") == matter.CampaignId && visit.Value<string>("timeline_id") == matter.TimelineId;
            string beforeStatus = (baseline?["visit"] as JObject)?.Value<string>("status");
            bool newlyDismissed = exactVisit && (string.IsNullOrEmpty(beforeStatus) || beforeStatus == "pending")
                && visit.Value<string>("status") == "dismissed" && visit.Value<int?>("player_turns") < ReignFamilyVisitRules.RequiredPlayerTurns
                && current.Value<double?>("worldDay") >= visit.Value<double?>("deadline")
                && before.Value<int?>("dismissals") >= 0 && member.Value<int?>("dismissals") == before.Value<int?>("dismissals") + 1;
            int patience = member?.Value<int?>("patiencePercent") ?? -1;
            bool newlyNeglected = newlyDismissed && before.Value<int?>("neglected") == 0 && member.Value<int?>("neglected") == 1
                && patience >= 0 && patience <= 100 && !ReignFamilyVisitRules.IsNeglected(before.Value<int>("dismissals"), patience)
                && ReignFamilyVisitRules.IsNeglected(member.Value<int>("dismissals"), patience);
            assertions.Add(Assertion("family_exact_scoped_baseline_and_quiescent_mirror", ready));
            if (expectation == "dismissed" || expectation == "neglected")
                assertions.Add(Assertion("exact_visit_new_dismissal_and_counter_increment", newlyDismissed));
            if (expectation == "neglected")
            {
                assertions.Add(Assertion("exact_visit_crossed_patience_into_neglect", newlyNeglected));
                if (newlyNeglected && assertions.All(a => a.Value<bool?>("passed") == true))
                { marker["familyNeglectEvidence"] = current.DeepClone(); StoreRulerDocketMarker(marker); }
            }
            if (expectation == "reconciled")
            {
                JObject neglected = marker["familyNeglectEvidence"] as JObject;
                bool priorNeglect = SameCourtLifeFamilyObservationScope(neglected, current)
                    && (neglected?["member"] as JObject)?.Value<int?>("neglected") == 1
                    && (neglected?["visit"] as JObject)?.Value<string>("status") == "dismissed";
                assertions.Add(Assertion("reconciliation_follows_proven_neglect_for_exact_visit", ready && priorNeglect
                    && current.Value<double?>("worldDay") >= neglected.Value<double?>("worldDay")
                    && member.Value<int?>("neglected") == 0 && member.Value<int?>("dismissals") == 0));
            }
            evidence["familyTransition"] = new JObject { ["baseline"] = baseline?.DeepClone(), ["current"] = current,
                ["verifiedNeglect"] = marker["familyNeglectEvidence"]?.DeepClone() };
            evidence["fixture"] = marker.DeepClone();
        }

        private void AddCourtLifeDecisionAssertions(JArray assertions, JObject marker, ReignCourtLifeMatter matter)
        {
            if (matter.Source == ReignDocketSource.International)
            {
                JObject payload = ParseObject(matter.PayloadJson);
                if (matter.SelectedOptionId == "accept" && (payload["normalizedAction"] is JObject
                    || payload.Value<string>("remedy") == "sign_trade_agreement"))
                {
                    JObject action = (JObject)CourtLifeInternationalNativeState(matter)["nativeDiplomaticAction"];
                    assertions.Add(Assertion("accepted_diplomacy_has_one_completed_native_action",
                        action.Value<bool?>("serviceAvailable") == true && action.Value<int?>("count") == 1
                        && action.Value<string>("status") == "Completed"));
                    JObject receipt = payload["nativeReceipt"] as JObject;
                    assertions.Add(Assertion("accepted_diplomacy_receipt_is_completed",
                        receipt?.Value<string>("status") == "completed"
                        || (receipt?.Value<bool?>("Success") == true && receipt.Value<bool?>("Completed") == true)));
                }
                return;
            }
            if (matter.Source != ReignDocketSource.Patronage) return;
            ReignPatronageCommission c = EnsureRulerDocketState().PatronageCommissions.FirstOrDefault(x => x.MatterId == matter.MatterId);
            int before = marker["beforeDecision"]?.Value<int?>("rulerGold") ?? -1;
            assertions.Add(Assertion("gold_matches_actual_commission", before >= 0 && before - (Hero.MainHero?.Gold ?? 0) == (c?.GoldPaid ?? 0)));
            if (matter.SelectedOptionId == "decline") assertions.Add(Assertion("decline_creates_no_commission", c == null));
            else assertions.Add(Assertion("one_paid_commission_exact_cost", c != null && c.Paid && c.GoldPaid == ReignPatronageRules.Cost(c.Reach)
                && EnsureRulerDocketState().PatronageCommissions.Count(x => x.MatterId == matter.MatterId) == 1));
        }
    }
}
