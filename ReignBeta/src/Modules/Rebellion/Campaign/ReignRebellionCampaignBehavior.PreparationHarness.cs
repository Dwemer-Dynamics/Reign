using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using ReignBeta.World;
using ReignBeta.Shared.Rulers;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using ReignBeta.Characters.Integration;
using ReignBeta.Dialogue.Integration;
using ReignBeta.Platform.Campaign;
using ReignBeta.Platform.Integration;
using ReignBeta.Rebellion.Integration;
using ReignBeta.WorldSimulation.Campaign;
using ReignBeta.WorldSimulation.News;
using ReignBeta.WorldSimulation.Rulers;
using ReignBeta.WorldSimulation.World;
using ReignBeta.Platform.Runtime;

namespace ReignBeta.Rebellion.Campaign
{
    public sealed partial class ReignRebellionCampaignBehavior
    {
        internal JObject RunPreparationHarnessProfile(string profile,
            string fixtureRunId, string disposableSaveName, JObject command = null)
        {
            string fixture = "rebellion_test_" + SafeId(fixtureRunId);
            float now = CurrentDay();
            JObject before = FixtureCounts(fixture);
            List<JObject> assertions = new List<JObject>();
            Action<string, bool, string> check = (name, passed, detail) => assertions.Add(
                new JObject { ["name"] = name, ["passed"] = passed, ["detail"] = detail });
            bool mutating = profile != "smoke" && profile != "feature";
            string activeSave = ReignServerClient.ActiveNativeSaveName();
            bool authorizedSave = !mutating || (Same(activeSave, disposableSaveName)
                && IsRebellionHarnessSave(disposableSaveName));

            check("campaign_loaded", TaleWorlds.CampaignSystem.Campaign.Current != null && Hero.MainHero != null,
                "A loaded Bannerlord campaign and main hero are required.");
            check("behavior_registered", Instance == this,
                "The save-authoritative rebellion behavior is registered.");
            check("profile_is_distinct", !string.IsNullOrWhiteSpace(profile),
                "The harness records and evaluates the requested profile rather than a shared smoke alias.");
            check("exact_disposable_save", authorizedSave,
                mutating
                    ? "The supplied save must equal Bannerlord's active loaded Reign_/ReignTest_ disposable slot."
                    : "This profile is observational and does not create fixture state.");

            JObject evidence = new JObject();
            if (authorizedSave)
            {
                switch (profile)
                {
                    case "smoke":
                        AddFeatureContractAssertions(assertions);
                        break;
                    case "feature":
                        AddFeatureContractAssertions(assertions);
                        check("saved_collections", _plots != null && _pledges != null
                            && _summonses != null && _movements != null && _memberships != null,
                            "Every preparation and civil-war collection is save-authoritative.");
                        check("single_player_plot", _plots.Count(x => x != null
                            && Same(x.PlayerHeroStringId, Hero.MainHero.StringId)
                            && !IsTerminalPlotStatus(x.Status)) <= 1,
                            "At most one unresolved preparation plot belongs to the player.");
                        break;
                    case "pledges":
                        EnsurePledgeFixtures(fixture, now);
                        List<ReignRebellionPledgeRecord> pledges = FixturePledges(fixture).ToList();
                        int pledgeCountBeforeRetry = pledges.Count;
                        EnsurePledgeFixtures(fixture, now);
                        check("three_decisions", new[] { "pledge", "refuse", "report" }
                            .All(decision => pledges.Any(x => Same(x.Decision, decision))),
                            "Pledge, refusal, and report persist as independent decisions.");
                        check("direct_and_mail_channels", pledges.Any(x => Same(x.RequestChannel, "conversation"))
                            && pledges.Any(x => Same(x.RequestChannel, "correspondence")),
                            "Direct and correspondence recruitment channels are represented.");
                        check("one_decision_per_clan", pledges.Select(x => x.ClanStringId)
                            .Distinct(StringComparer.OrdinalIgnoreCase).Count() == pledges.Count,
                            "A clan has at most one preparation decision in the fixture.");
                        check("action_id_idempotency", pledges.Select(x => x.ActionId)
                            .Distinct(StringComparer.OrdinalIgnoreCase).Count() == pledges.Count,
                            "Stable action ids make repeated delivery idempotent.");
                        check("duplicate_retry_idempotent", FixturePledges(fixture).Count()
                            == pledgeCountBeforeRetry,
                            "Replaying the same prepared delivery leaves exactly one saved decision per clan.");
                        check("concurrent_requests", pledges.Count(x => Math.Abs(x.RequestedDay - now) < 0.001f)
                            >= 3,
                            "Multiple clan decisions share the same request boundary.");
                        check("out_of_order_delivery", pledges.OrderBy(x => x.PledgeId,
                                StringComparer.OrdinalIgnoreCase).Select(x => x.RespondedDay)
                            .Zip(pledges.OrderBy(x => x.PledgeId, StringComparer.OrdinalIgnoreCase)
                                .Select(x => x.RespondedDay).Skip(1), (left, right) => right < left).Any(x => x),
                            "Concurrent correspondence decisions can arrive out of request order without changing identity.");
                        check("delayed_mail_response", pledges.Any(x => Same(x.RequestChannel, "correspondence")
                            && x.RespondedDay > x.RequestedDay),
                            "At least one correspondence decision is delivered after its request.");
                        check("foreign_pledge", pledges.Any(x => Same(x.Decision, "pledge")
                            && x.OriginalKingdomStringId.StartsWith("foreign_", StringComparison.Ordinal)),
                            "A foreign-clan pledge remains keyed to its original realm.");
                        evidence["pledges"] = JArray.FromObject(pledges.Select(x => new
                        {
                            x.PledgeId, x.ClanStringId, x.Decision, x.RequestChannel,
                            x.RequestedDay, x.RespondedDay, x.ActionId, x.OriginalKingdomStringId
                        }));
                        break;
                    case "reporting":
                        EnsureReportingFixture(fixture, now);
                        ReignRebellionPlotRecord reportPlot = FixturePlot(fixture);
                        List<ReignRebellionPledgeRecord> reports = FixturePledges(fixture)
                            .Where(x => Same(x.Decision, "report")).ToList();
                        check("named_informer", !string.IsNullOrWhiteSpace(reportPlot?.InformerHeroStringId)
                            && !string.IsNullOrWhiteSpace(reportPlot?.InformerClanStringId),
                            "The report records the informing lord and clan by stable id.");
                        check("report_travel", reportPlot != null
                            && Math.Abs(reportPlot.ReportDeliveryDay - reportPlot.ReportDispatchDay - 1f) < 0.001f,
                            "The named report travels for one campaign day before reaching the ruler.");
                        check("single_report", reports.Count == 1 && reports[0].ReportedToRuler,
                            "Only one clan decision reports the conspiracy.");
                        check("recruitment_blocked_after_report", reportPlot != null
                            && !IsPreparationRecruitmentOpen(reportPlot),
                            "Secret recruitment closes as soon as the conspiracy is reported.");
                        evidence["plot"] = JObject.FromObject(reportPlot);
                        break;
                    case "summons":
                        EnsureSummonsFixture(fixture, now);
                        ReignRebellionSummonsRecord summons = FixtureSummonses(fixture).Single();
                        check("named_ruler_summons", !string.IsNullOrWhiteSpace(summons.RulerHeroStringId)
                            && !string.IsNullOrWhiteSpace(summons.InformerHeroStringId),
                            "The summons names both the ruler and informer.");
                        check("delivery_after_dispatch", summons.DeliveryDay > summons.IssuedDay,
                            "The summons is delivered after it is issued.");
                        check("seven_days_from_delivery", Math.Abs(summons.DeadlineDay
                            - summons.DeliveryDay - 7f) < 0.001f,
                            "The response deadline is exactly seven campaign days after delivery.");
                        check("deadline_minus_epsilon_open", !PreparationDeadlineExpired(
                            PreviousRepresentableCampaignDay(summons.DeadlineDay),
                            summons.DeadlineDay),
                            "At the immediately preceding representable campaign instant the summons remains answerable.");
                        check("exact_deadline_expired", PreparationDeadlineExpired(
                            summons.DeadlineDay, summons.DeadlineDay),
                            "The production greater-than-or-equal boundary expires the summons at the exact deadline.");
                        check("deadline_plus_epsilon_expired", PreparationDeadlineExpired(
                            summons.DeadlineDay + 0.001f, summons.DeadlineDay),
                            "Immediately after the deadline the summons remains expired.");
                        check("current_ruler_identity_stable", Same(summons.RulerHeroStringId,
                            FixturePlot(fixture)?.RulerHeroStringId),
                            "The summons stays keyed to the plot's challenged ruler identity.");
                        check("summons_idempotent", FixtureSummonses(fixture).Count() == 1,
                            "Retrying fixture preparation does not duplicate the summons.");
                        check("explicit_answers", NormalizeSummonsResponse("recant") == "renounce"
                            && NormalizeSummonsResponse("rebel") == "defy"
                            && string.IsNullOrWhiteSpace(NormalizeSummonsResponse("perhaps")),
                            "Renounce and defy are explicit and ambiguous responses fail closed.");
                        evidence["summons"] = JObject.FromObject(summons);
                        break;
                    case "verdicts":
                        EnsureVerdictFixtures(fixture, now);
                        List<ReignRebellionSummonsRecord> verdicts = FixtureSummonses(fixture)
                            .Where(x => x.ConsequenceApplied).ToList();
                        check("four_verdicts", new[] { "pardon", "imprisonment", "exile", "execution" }
                            .All(verdict => verdicts.Any(x => Same(x.RulerVerdict, verdict))),
                            "Pardon, imprisonment, exile, and execution outcomes are represented.");
                        check("death_disabled_fallback", verdicts.Any(x => Same(x.SummonsId,
                            fixture + "_summons_execution_fallback")
                            && Same(x.RulerVerdict, "execution")
                            && Same(x.Consequence, "imprisonment")),
                            "A disabled or failed execution falls back to imprisonment.");
                        check("verdicts_terminal", verdicts.All(x => x.ConsequenceApplied
                            && x.AnsweredDay >= x.DeliveryDay),
                            "Every applied judgment is terminal and occurs after delivery.");
                        evidence["verdicts"] = JArray.FromObject(verdicts.Select(x => new
                        { x.SummonsId, x.RulerVerdict, x.Consequence, x.ConsequenceApplied }));
                        break;
                    case "declaration":
                        EnsureDeclarationFixture(fixture, now);
                        ReignRebellionMovementRecord movement = FixtureMovements(fixture).Single();
                        List<ReignRebellionMembershipRecord> members = FixtureMemberships(fixture).ToList();
                        check("ordinary_and_forced_causes", Same(movement.ResolutionCause, "player_declaration")
                            && movement.DemandJson.Contains("summons_deadline_ignored"),
                            "The fixture records both ordinary declaration and forced-deadline activation causes.");
                        check("renounce_and_defy_transitions", NormalizeSummonsResponse("renounce") == "renounce"
                            && NormalizeSummonsResponse("defy") == "defy",
                            "The two explicit summons answers select submission or immediate declaration.");
                        check("ignored_deadline_forces_declaration",
                            movement.ExecutionSnapshotJson.Contains("summons_deadline_ignored"),
                            "Ignoring the exact delivered deadline is retained as a forced declaration cause.");
                        check("pledged_clans_committed", members.Count >= 2
                            && members.All(x => x.IsCommitted && Same(x.Side, "rebel")),
                            "Every pledged fixture clan is committed to the rebel side.");
                        check("holdings_manifest", !string.IsNullOrWhiteSpace(movement.OriginalStrongholdIdsCsv)
                            && movement.ExecutionSnapshotJson.Contains("boundSettlementIds"),
                            "The activation receipt includes current fortifications and bound settlements.");
                        check("foreign_origin_preserved", members.Any(x => x.OriginalKingdomStringId
                            .StartsWith("foreign_", StringComparison.Ordinal)),
                            "A pledged foreign clan retains its origin for reunification.");
                        evidence["movement"] = JObject.FromObject(movement);
                        evidence["memberships"] = JArray.FromObject(members);
                        break;
                    case "resolution":
                        EnsureResolutionFixtures(fixture, now);
                        List<ReignRebellionMovementRecord> resolutions = FixtureMovements(fixture).ToList();
                        List<ReignRebellionMembershipRecord> fates = FixtureMemberships(fixture).ToList();
                        check("both_leader_outcomes", resolutions.Any(x => Same(x.WinningSide, "rebel"))
                            && resolutions.Any(x => Same(x.WinningSide, "loyalist")),
                            "Rebel and loyalist leadership-bound outcomes are both represented.");
                        check("capture_death_deposition_causes", new[] { "rebel_leader_captured",
                            "challenged_ruler_killed", "challenged_ruler_deposed" }
                            .All(cause => resolutions.Any(x => Same(x.ResolutionCause, cause))),
                            "Capture, ruler death, and ruler deposition causes are explicit.");
                        check("realm_cooldown", resolutions.All(x => Math.Abs(x.CooldownUntilDay
                            - x.UpdatedDay - RealmCooldownDays) < 0.001f),
                            "Every resolution applies the exact 63-day realm cooldown.");
                        check("mercy_outcomes", new[] { "freedom", "imprisonment", "execution" }
                            .All(outcome => fates.Any(x => Same(x.FateOutcome, outcome) && x.FateApplied)),
                            "Freedom, imprisonment, and execution mercy outcomes are recorded.");
                        check("capture_grace_boundary", !LeadershipCaptureExpired(100f, 102.999f)
                            && LeadershipCaptureExpired(100f, 103f),
                            "Leadership captivity resolves only at the exact three-day grace boundary.");
                        check("flight_or_release_clears_capture", NextLeadershipCaptureDay(100f, false, 101f) < 0f
                            && Math.Abs(NextLeadershipCaptureDay(-1f, true, 101f) - 101f) < 0.001f,
                            "Escape or release clears the capture clock while a new capture starts it once.");
                        check("invalid_and_competing_rejected",
                            DirectedMovementPreflightFailure(false, true, false, false).Length > 0
                            && DirectedMovementPreflightFailure(true, false, false, false).Length > 0
                            && DirectedMovementPreflightFailure(true, true, true, false)
                                .Contains("already has an active rebellion")
                            && DirectedMovementPreflightFailure(true, true, false, true)
                                .Contains("63-day"),
                            "Invalid leaders, invalid vassal state, competing rebellions, and cooldown attempts fail closed.");
                        evidence["resolutions"] = JArray.FromObject(resolutions);
                        evidence["fates"] = JArray.FromObject(fates);
                        break;
                    case "save_prepare":
                        EnsurePledgeFixtures(fixture, now);
                        EnsureSummonsFixture(fixture, now);
                        EnsureDeclarationFixture(fixture, now);
                        FixturePlot(fixture).CorrelationId = fixture + "_persistence_marker";
                        check("persistence_marker_staged", FixturePlot(fixture)?.CorrelationId
                            == fixture + "_persistence_marker",
                            "Run-owned plot, pledge, summons, movement, and membership state is staged for native save.");
                        check("fixture_graph_complete", FixturePledges(fixture).Count() >= 4
                            && FixtureSummonses(fixture).Any() && FixtureMovements(fixture).Any()
                            && FixtureMemberships(fixture).Any(),
                            "All save-authoritative fixture collections contain linked records.");
                        evidence["persistenceMarker"] = fixture + "_persistence_marker";
                        break;
                    case "save_verify":
                        ReignRebellionPlotRecord savedPlot = FixturePlot(fixture);
                        check("persistence_marker_loaded", savedPlot?.CorrelationId
                            == fixture + "_persistence_marker",
                            "The run-owned marker survived native Bannerlord save and reload.");
                        check("linked_state_loaded", FixturePledges(fixture).Count() >= 4
                            && FixtureSummonses(fixture).Any() && FixtureMovements(fixture).Any()
                            && FixtureMemberships(fixture).Any(),
                            "Linked pledge, summons, movement, and membership records survived reload.");
                        check("referential_integrity", savedPlot != null
                            && FixturePledges(fixture).All(x => Same(x.PlotId, savedPlot.PlotId))
                            && FixtureSummonses(fixture).All(x => Same(x.PlotId, savedPlot.PlotId)),
                            "Reloaded preparation records retain exact plot references.");
                        evidence["persistenceMarker"] = savedPlot?.CorrelationId ?? string.Empty;
                        break;
                    case "native_setup":
                        Kingdom setupRealm = Clan.PlayerClan?.Kingdom;
                        Clan setupRuler = PrepareHarnessNonPlayerRuler(setupRealm);
                        string setupCaseId = command?.Value<string>("caseId") ?? string.Empty;
                        string setupTargetHeroId = command?.Value<string>("targetHeroId") ?? string.Empty;
                        bool correspondenceCase = Same(setupCaseId, "RB-LANG-004")
                            || Same(setupCaseId, "RB-LANG-005")
                            || Same(setupCaseId, "RB-LANG-006");
                        Hero correspondenceTarget = correspondenceCase
                            ? FindHero(setupTargetHeroId)
                            : null;
                        if (correspondenceTarget?.Clan != null
                            && correspondenceTarget.Clan.Leader == correspondenceTarget
                            && correspondenceTarget.Clan != Clan.PlayerClan
                            && correspondenceTarget.Clan != setupRuler)
                            MoveClanToKingdom(correspondenceTarget.Clan, setupRealm);
                        bool correspondenceTargetPreviouslyKnown = correspondenceTarget != null
                            && (correspondenceTarget.IsKnownToPlayer || correspondenceTarget.HasMet);
                        if (correspondenceCase && correspondenceTarget != null
                            && !correspondenceTargetPreviouslyKnown)
                            correspondenceTarget.SetHasMet();
                        List<Clan> setupEligibleClans = (setupRealm?.Clans ?? Enumerable.Empty<Clan>())
                            .Where(x => x != null && x != Clan.PlayerClan && x != setupRuler
                                && x.Leader != null && !x.Leader.IsDead && !x.Leader.IsPrisoner)
                            .ToList();
                        JArray reportRelationFixtures = new JArray();
                        bool reportCase = Same(setupCaseId, "RB-LANG-003")
                            || Same(setupCaseId, "RB-LANG-006")
                            || Same(setupCaseId, "RB-NATIVE-001");
                        if (reportCase
                            && Hero.MainHero != null && setupRuler?.Leader != null)
                        {
                            foreach (Hero reportTarget in setupEligibleClans
                                .Select(x => x.Leader)
                                .Where(x => x != null && x.IsAlive && !x.IsChild && !x.IsPrisoner
                                    && x.Clan != null && x.Clan.Leader == x)
                                .Distinct())
                            {
                                int priorPlayerRelation = Hero.MainHero.GetRelation(reportTarget);
                                int priorRulerRelation = setupRuler.Leader.GetRelation(reportTarget);
                                int priorHonor = reportTarget.GetTraitLevel(DefaultTraits.Honor);
                                ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero,
                                    reportTarget, -10 - priorPlayerRelation, false);
                                ChangeRelationAction.ApplyRelationChangeBetweenHeroes(setupRuler.Leader,
                                    reportTarget, 35 - priorRulerRelation, false);
                                reportTarget.SetTraitLevel(DefaultTraits.Honor, Math.Max(1, priorHonor));
                                reportRelationFixtures.Add(new JObject
                                {
                                    ["heroId"] = reportTarget.StringId,
                                    ["clanId"] = reportTarget.Clan.StringId,
                                    ["isClanLeader"] = reportTarget.Clan.Leader == reportTarget,
                                    ["priorPlayerRelation"] = priorPlayerRelation,
                                    ["priorRulerRelation"] = priorRulerRelation,
                                    ["priorHonor"] = priorHonor,
                                    ["playerRelation"] = Hero.MainHero.GetRelation(reportTarget),
                                    ["rulerRelation"] = setupRuler.Leader.GetRelation(reportTarget),
                                    ["honor"] = reportTarget.GetTraitLevel(DefaultTraits.Honor)
                                });
                            }
                        }
                        check("player_is_non_ruling_vassal", setupRealm != null
                            && setupRuler != null && setupRealm.RulingClan == setupRuler
                            && Clan.PlayerClan?.Kingdom == setupRealm
                            && Clan.PlayerClan != setupRealm.RulingClan,
                            "The guarded fixture makes the player a non-ruling vassal under a real active NPC ruler.");
                        check("ruler_identity_is_native", setupRuler?.Leader != null
                            && !setupRuler.Leader.IsDead && !setupRuler.Leader.IsPrisoner,
                            "The fixture ruler is a living, free native clan leader.");
                        evidence["rulerHeroId"] = setupRuler?.Leader?.StringId ?? string.Empty;
                        evidence["rulerClanId"] = setupRuler?.StringId ?? string.Empty;
                        check("correspondence_target_is_exact_enrolled_clan_leader", !correspondenceCase
                            || (correspondenceTarget != null
                                && correspondenceTarget.Clan?.Leader == correspondenceTarget
                                && correspondenceTarget.Clan?.Kingdom == setupRealm
                                && (correspondenceTarget.IsKnownToPlayer || correspondenceTarget.HasMet)),
                            "Correspondence cases enroll the exact known recipient clan leader under the fixture sovereign before native mail dispatch.");
                        evidence["correspondenceTargetHeroId"] = correspondenceTarget?.StringId ?? string.Empty;
                        evidence["correspondenceTargetClanId"] = correspondenceTarget?.Clan?.StringId ?? string.Empty;
                        evidence["correspondenceTargetPreviouslyKnown"] = correspondenceTargetPreviouslyKnown;
                        evidence["correspondenceTargetKnownAfterFixture"] = correspondenceTarget != null
                            && (correspondenceTarget.IsKnownToPlayer || correspondenceTarget.HasMet);
                        check("report_target_relation_fixture", !reportCase
                            || (reportRelationFixtures.Count > 0
                                && reportRelationFixtures.All(x => x.Value<int>("playerRelation") <= -10
                                && x.Value<int>("rulerRelation") >= 35)),
                            "Report cases use a moderate guarded sovereign relationship and mild player distrust rather than extreme calibration.");
                        check("report_target_clan_leader_fixture", !reportCase
                            || (reportRelationFixtures.Count > 0
                                && reportRelationFixtures.All(x => x.Value<bool>("isClanLeader"))),
                            "Every report target is a living, free active noble clan leader accepted by the production pledge action.");
                        check("report_target_honor_fixture", !reportCase
                            || (reportRelationFixtures.Count > 0
                                && reportRelationFixtures.All(x => x.Value<int>("honor") >= 1)),
                            "Report cases temporarily establish an honorable duty witness while leaving the final decision to natural dialogue.");
                        evidence["eligibleTargetHeroIds"] = new JArray((setupEligibleClans
                            .Select(x => x.Leader.StringId).Take(12)) ?? Enumerable.Empty<string>());
                        evidence["reportRelationFixtures"] = reportRelationFixtures;
                        break;
                    case "language_observe":
                        string expectedDecision = command?.Value<string>("expectedDecision") ?? string.Empty;
                        string expectedChannel = command?.Value<string>("expectedChannel") ?? string.Empty;
                        ReignRebellionPledgeRecord observed = _pledges
                            .Where(x => x != null && !x.PledgeId.StartsWith("rebellion_test_",
                                StringComparison.OrdinalIgnoreCase))
                            .OrderByDescending(x => x.RespondedDay).FirstOrDefault();
                        check("production_decision_recorded", observed != null
                            && Same(observed.Decision, expectedDecision),
                            "The production dialogue/correspondence action persisted the expected lord decision.");
                        check("production_channel_recorded", observed != null
                            && Same(observed.RequestChannel, expectedChannel),
                            "The persisted decision identifies the visible production channel.");
                        check("one_decision_for_observed_clan", observed != null
                            && _pledges.Count(x => x != null && Same(x.PlotId, observed.PlotId)
                                && Same(x.ClanStringId, observed.ClanStringId)) == 1,
                            "The observed clan owns exactly one decision for the plot.");
                        evidence["observedPledge"] = observed == null ? null : JObject.FromObject(observed);
                        break;
                    case "travel_prepare":
                        ReignRebellionPledgeRecord reportDecision = _pledges
                            .Where(x => x != null && Same(x.Decision, "report"))
                            .OrderByDescending(x => x.RespondedDay).FirstOrDefault();
                        ReignRebellionPlotRecord travelPlot = _plots.FirstOrDefault(x => x != null
                            && reportDecision != null && Same(x.PlotId, reportDecision.PlotId));
                        check("natural_report_recorded", reportDecision != null
                            && Same(reportDecision.RequestChannel, "individual_chat")
                            && !string.IsNullOrWhiteSpace(reportDecision.ActionId),
                            "A production Individual Chat action recorded the named lord's report without a direct fixture decision.");
                        check("report_travel_staged", travelPlot != null
                            && !string.IsNullOrWhiteSpace(travelPlot.InformerHeroStringId)
                            && Math.Abs(travelPlot.ReportDeliveryDay
                                - travelPlot.ReportDispatchDay - 1f) < 0.001f,
                            "The production report has exactly one native campaign day of travel.");
                        if (travelPlot != null)
                            travelPlot.CorrelationId = fixture + "_native_travel_marker";
                        evidence["plot"] = travelPlot == null ? null : JObject.FromObject(travelPlot);
                        evidence["reportDecision"] = reportDecision == null ? null
                            : JObject.FromObject(reportDecision);
                        break;
                    case "travel_verify":
                        ReignRebellionPlotRecord reloadedTravelPlot = _plots.FirstOrDefault(x => x != null
                            && Same(x.CorrelationId, fixture + "_native_travel_marker"));
                        ReignRebellionSummonsRecord deliveredSummons = _summonses
                            .Where(x => x != null && reloadedTravelPlot != null
                                && Same(x.PlotId, reloadedTravelPlot.PlotId))
                            .OrderByDescending(x => x.IssuedDay).FirstOrDefault();
                        check("native_marker_reloaded", reloadedTravelPlot != null,
                            "The exact production plot and report marker survived native save/reload.");
                        check("summons_issued_after_report_travel", deliveredSummons != null
                            && reloadedTravelPlot != null
                            && deliveredSummons.IssuedDay >= reloadedTravelPlot.ReportDeliveryDay,
                            "The current ruler issued the summons only after the report completed travel.");
                        check("summons_delivered_natively", deliveredSummons != null
                            && CurrentDay() >= deliveredSummons.DeliveryDay
                            && deliveredSummons.DeliveryDay > deliveredSummons.IssuedDay,
                            "Native time delivered the ruler's summons after its own travel interval.");
                        check("summons_deadline_from_delivery", deliveredSummons != null
                            && Math.Abs(deliveredSummons.DeadlineDay
                                - deliveredSummons.DeliveryDay - 7f) < 0.001f,
                            "The seven-day answer deadline is anchored to delivery, not dispatch.");
                        check("summons_identity_preserved", deliveredSummons != null
                            && Same(deliveredSummons.RulerHeroStringId,
                                FindKingdom(reloadedTravelPlot?.ParentKingdomStringId)?.Leader?.StringId)
                            && Same(deliveredSummons.InformerHeroStringId,
                                reloadedTravelPlot?.InformerHeroStringId),
                            "Reloaded summons identity names the current ruler and original informer.");
                        evidence["plot"] = reloadedTravelPlot == null ? null
                            : JObject.FromObject(reloadedTravelPlot);
                        evidence["summons"] = deliveredSummons == null ? null
                            : JObject.FromObject(deliveredSummons);
                        break;
                    case "mixed_transfer":
                        JObject transfer = RunMixedHoldingTransferHarness();
                        foreach (JObject assertion in transfer["assertions"] as JArray ?? new JArray())
                            assertions.Add(assertion);
                        evidence["mixedTransfer"] = transfer;
                        break;
                    case "foreign_reintegration":
                        JObject reintegration = RunForeignClanReintegrationHarness();
                        foreach (JObject assertion in reintegration["assertions"] as JArray ?? new JArray())
                            assertions.Add(assertion);
                        evidence["foreignReintegration"] = reintegration;
                        break;
                    case "cleanup":
                        int removed = RemoveHarnessFixtures(fixture);
                        check("fixture_records_removed", removed > 0,
                            "Cleanup removed at least one exact run-owned fixture record.");
                        check("exact_namespace_clean", FixtureRecordCount(fixture) == 0,
                            "No records remain in the exact fixture namespace.");
                        evidence["removedRecordCount"] = removed;
                        break;
                }
            }

            bool passed = assertions.All(x => x.Value<bool>("passed"));
            return new JObject
            {
                ["schemaVersion"] = 2,
                ["scenario"] = "rebellion_release_acceptance",
                ["profile"] = profile,
                ["fixtureRunId"] = fixtureRunId,
                ["fixtureNamespace"] = fixture,
                ["saveIdentifier"] = activeSave,
                ["disposableSaveArmed"] = authorizedSave,
                ["campaignId"] = ReignCampaignIdentity.CurrentCampaignId(),
                ["worldDay"] = now,
                ["preState"] = before,
                ["postState"] = FixtureCounts(fixture),
                ["evidence"] = evidence,
                ["assertions"] = new JArray(assertions),
                ["passed"] = passed,
                ["cleanupStatus"] = profile == "cleanup"
                    ? passed ? "removed_exact_fixture_state" : "cleanup_failed"
                    : "not_requested",
                ["error"] = passed ? string.Empty : "One or more rebellion profile assertions failed."
            };
        }

        private static Clan PrepareHarnessNonPlayerRuler(Kingdom kingdom)
        {
            if (kingdom == null || Clan.PlayerClan == null) return null;
            if (kingdom.RulingClan != null && kingdom.RulingClan != Clan.PlayerClan
                && kingdom.RulingClan.Leader != null && !kingdom.RulingClan.Leader.IsDead
                && !kingdom.RulingClan.Leader.IsPrisoner)
                return kingdom.RulingClan;
            Clan replacement = kingdom.Clans
                .Where(x => x != null && x != Clan.PlayerClan && !x.IsEliminated
                    && !x.IsUnderMercenaryService && !x.IsMinorFaction
                    && x.Leader != null && x.Leader.IsLord && !x.Leader.IsDead
                    && !x.Leader.IsPrisoner)
                .OrderByDescending(x => x.Tier).ThenBy(x => x.StringId)
                .FirstOrDefault();
            if (replacement != null) ChangeRulingClanAction.Apply(kingdom, replacement);
            return replacement;
        }

        private JObject RunMixedHoldingTransferHarness()
        {
            List<JObject> assertions = new List<JObject>();
            Action<string, bool, string> check = (name, passed, detail) => assertions.Add(
                new JObject { ["name"] = name, ["passed"] = passed, ["detail"] = detail });
            Kingdom parent = Clan.PlayerClan?.Kingdom;
            Clan rulerClan = PrepareHarnessNonPlayerRuler(parent);
            Hero ruler = rulerClan?.Leader;
            if (parent == null || ruler == null)
            {
                check("native_ruler_available", false,
                    "A real NPC ruler is required for the mixed-holding transfer.");
                return new JObject { ["assertions"] = new JArray(assertions) };
            }

            Town town = Clan.PlayerClan.Fiefs.FirstOrDefault(x => x?.Settlement?.IsTown == true);
            Town castle = Clan.PlayerClan.Fiefs.FirstOrDefault(x => x?.Settlement?.IsCastle == true);
            if (town == null)
            {
                Settlement candidate = Settlement.All.FirstOrDefault(x => x?.IsTown == true
                    && x.MapFaction == parent && x.OwnerClan != Clan.PlayerClan);
                if (candidate != null)
                {
                    ChangeOwnerOfSettlementAction.ApplyByGift(candidate, Hero.MainHero);
                    town = candidate.Town;
                }
            }
            if (castle == null)
            {
                Settlement candidate = Settlement.All.FirstOrDefault(x => x?.IsCastle == true
                    && x.MapFaction == parent && x.OwnerClan != Clan.PlayerClan);
                if (candidate != null)
                {
                    ChangeOwnerOfSettlementAction.ApplyByGift(candidate, Hero.MainHero);
                    castle = candidate.Town;
                }
            }
            List<Village> villages = new[] { town, castle }.Where(x => x != null)
                .SelectMany(x => x.Settlement.BoundVillages)
                .Where(x => x != null).Distinct().ToList();
            check("mixed_holding_precondition", town != null && castle != null && villages.Count > 0,
                "The exact disposable fixture contains a player town, player castle, and their bound villages.");
            if (town != null && castle != null && villages.Count > 0)
            {
                float now = CurrentDay();
                string id = "rebellion_test_mixed_transfer_" + Guid.NewGuid().ToString("N");
                ReignRebellionPlotRecord plot = new ReignRebellionPlotRecord
                {
                    PlotId = id + "_plot", PlayerHeroStringId = Hero.MainHero.StringId,
                    PlayerClanStringId = Clan.PlayerClan.StringId,
                    ParentKingdomStringId = parent.StringId,
                    RulerHeroStringId = ruler.StringId, Status = "summons_delivered",
                    CreatedDay = now, UpdatedDay = now
                };
                ReignRebellionSummonsRecord summons = new ReignRebellionSummonsRecord
                {
                    SummonsId = id + "_summons", PlotId = plot.PlotId,
                    RulerHeroStringId = ruler.StringId, Status = "delivered",
                    IssuedDay = now - 1f, DeliveryDay = now, DeadlineDay = now + 7f
                };
                _plots.Add(plot);
                _summonses.Add(summons);
                ApplyPreparationVerdict(plot, summons, ruler, "exile", out string result);
                check("town_transferred", town.Settlement.OwnerClan == rulerClan,
                    "The production exile verdict transferred the town to the current ruler's clan.");
                check("castle_transferred", castle.Settlement.OwnerClan == rulerClan,
                    "The production exile verdict transferred the castle to the current ruler's clan.");
                check("bound_villages_transferred", villages.All(x => x.Settlement.OwnerClan == rulerClan),
                    "Every village bound to the mixed holding follows the transferred fortification owner.");
                check("player_clan_exiled", Clan.PlayerClan.Kingdom == null,
                    "The production verdict removed the player clan from the realm after confiscation.");
                return new JObject
                {
                    ["assertions"] = new JArray(assertions), ["result"] = result,
                    ["townId"] = town.Settlement.StringId,
                    ["castleId"] = castle.Settlement.StringId,
                    ["boundVillageIds"] = new JArray(villages.Select(x => x.Settlement.StringId)),
                    ["newOwnerClanId"] = rulerClan.StringId
                };
            }
            return new JObject { ["assertions"] = new JArray(assertions) };
        }

        private JObject RunForeignClanReintegrationHarness()
        {
            List<JObject> assertions = new List<JObject>();
            Action<string, bool, string> check = (name, passed, detail) => assertions.Add(
                new JObject { ["name"] = name, ["passed"] = passed, ["detail"] = detail });
            Kingdom parent = Clan.PlayerClan?.Kingdom;
            Clan rulerClan = PrepareHarnessNonPlayerRuler(parent);
            Hero challengedRuler = rulerClan?.Leader;
            Clan foreignClan = Kingdom.All.Where(x => x != null && x != parent && !x.IsEliminated)
                .SelectMany(x => x.Clans).Where(x => x != null && !x.IsEliminated
                    && !x.IsUnderMercenaryService && !x.IsMinorFaction
                    && x.Leader != null && x.Leader.IsLord && !x.Leader.IsDead
                    && !x.Leader.IsPrisoner)
                .OrderByDescending(x => x.Heroes.Count).ThenBy(x => x.StringId)
                .FirstOrDefault();
            Kingdom origin = foreignClan?.Kingdom;
            List<Hero> livingFamily = foreignClan?.Heroes
                .Where(x => x != null && !x.IsDead).ToList() ?? new List<Hero>();
            check("foreign_fixture_available", parent != null && challengedRuler != null
                && foreignClan != null && origin != null && livingFamily.Count > 0,
                "A living foreign noble clan and family are required for reintegration proof.");
            if (parent == null || challengedRuler == null || foreignClan == null || origin == null)
                return new JObject { ["assertions"] = new JArray(assertions) };

            string recruitmentResult = string.Empty;
            string resolutionResult = string.Empty;
            bool declared = TryStartDirectedMovement(parent, Clan.PlayerClan,
                Enumerable.Empty<Clan>(), out string declarationResult);
            ReignRebellionMovementRecord movement = _movements.LastOrDefault(x => IsCivilWar(x)
                && x.IsPlayerLed && Same(x.LeaderClanStringId, Clan.PlayerClan.StringId));
            bool recruited = declared && movement != null
                && TryRecruitLord(Hero.MainHero, foreignClan.Leader, out recruitmentResult);
            bool joinedRebels = recruited && foreignClan.Kingdom != origin
                && Same(foreignClan.Kingdom?.StringId, movement.RebelKingdomStringId);
            bool heldSurrenderRejected = false;
            if (joinedRebels)
            {
                // Reproduce the phase retained by a save made during ordinary resolution.
                string originalPhase = movement.ExecutionPhase;
                try
                {
                    movement.ExecutionPhase = "resolving_reunification";
                    heldSurrenderRejected = !TrySurrender(challengedRuler, movement.MovementId, out _)
                        && !movement.ResolutionApplied && Same(movement.Stage, "civil_war")
                        && Same(foreignClan.Kingdom?.StringId, movement.RebelKingdomStringId);
                }
                finally
                {
                    movement.ExecutionPhase = originalPhase;
                }
            }
            bool resolved = heldSurrenderRejected
                && TrySurrender(challengedRuler, movement.MovementId, out resolutionResult);
            bool completedSurrenderReplayRejected = resolved
                && !TrySurrender(challengedRuler, movement.MovementId, out _);
            ReignRebellionMembershipRecord membership = movement == null ? null
                : MembersOf(movement).FirstOrDefault(x => Same(x.ClanStringId, foreignClan.StringId));
            check("foreign_clan_joined", joinedRebels && membership != null
                && Same(membership.OriginalKingdomStringId, origin.StringId),
                "The production recruitment path moved the clan into the civil war and preserved its origin.");
            check("foreign_clan_returned", resolved && foreignClan.Kingdom == origin,
                "Leadership-bound resolution returned the foreign clan to its exact origin realm.");
            check("living_family_reintegrated", resolved
                && livingFamily.All(x => x.Clan == foreignClan && x.MapFaction == origin),
                "Every living member of the clan follows the reintegrated clan back to its origin realm.");
            check("reunification_exactly_once", heldSurrenderRejected && completedSurrenderReplayRejected
                && membership != null && membership.IsCommitted
                && Same(membership.OriginalKingdomStringId, origin.StringId)
                && foreignClan.Kingdom == origin,
                "A held ordinary resolution cannot report a surrender, and a completed retry cannot move the clan again.");
            return new JObject
            {
                ["assertions"] = new JArray(assertions), ["foreignClanId"] = foreignClan.StringId,
                ["originKingdomId"] = origin.StringId,
                ["livingFamilyHeroIds"] = new JArray(livingFamily.Select(x => x.StringId)),
                ["declarationResult"] = declarationResult,
                ["recruitmentResult"] = recruitmentResult,
                ["resolutionResult"] = resolutionResult,
                ["heldSurrenderRejected"] = heldSurrenderRejected,
                ["completedSurrenderReplayRejected"] = completedSurrenderReplayRejected,
                ["movementId"] = movement?.MovementId ?? string.Empty
            };
        }

        private static void AddFeatureContractAssertions(List<JObject> assertions)
        {
            Action<string, bool, string> check = (name, passed, detail) => assertions.Add(
                new JObject { ["name"] = name, ["passed"] = passed, ["detail"] = detail });
            check("pledge_decisions", NormalizePreparationDecision("accept") == "pledge"
                && NormalizePreparationDecision("decline") == "refuse"
                && NormalizePreparationDecision("inform") == "report"
                && string.IsNullOrWhiteSpace(NormalizePreparationDecision("maybe")),
                "Pledge, refusal, and report normalize independently while ambiguity fails closed.");
            check("summons_answers", NormalizeSummonsResponse("recant") == "renounce"
                && NormalizeSummonsResponse("rebel") == "defy",
                "Renunciation and defiance are explicit answers.");
            check("four_verdicts", new[] { "pardon", "imprisonment", "exile", "execution" }
                .All(x => NormalizeRulerVerdict(x) == x),
                "All four ruler verdicts are available.");
            check("hard_constants", Math.Abs(RealmCooldownDays - 63f) < 0.001f
                && Math.Abs(LeadershipCaptureGraceDays - 3f) < 0.001f,
                "Resolution cooldown and leadership-capture grace are exact.");
        }

        private void EnsurePledgeFixtures(string fixture, float now)
        {
            ReignRebellionPlotRecord plot = EnsureFixturePlot(fixture, now);
            if (FixturePledges(fixture).Any()) return;
            string[] decisions = { "pledge", "refuse", "report", "pledge" };
            string[] channels = { "conversation", "correspondence", "correspondence", "correspondence" };
            float[] responseOffsets = { 0f, 3f, 1f, 2f };
            for (int i = 0; i < decisions.Length; i++)
            {
                _pledges.Add(new ReignRebellionPledgeRecord
                {
                    PledgeId = fixture + "_pledge_" + i,
                    PlotId = plot.PlotId,
                    LordHeroStringId = fixture + "_lord_" + i,
                    ClanStringId = fixture + "_clan_" + i,
                    OriginalKingdomStringId = i == 3 ? "foreign_" + fixture : plot.ParentKingdomStringId,
                    Decision = decisions[i],
                    DecisionReason = "Profile-owned deterministic decision " + decisions[i] + ".",
                    RequestedDay = now,
                    RespondedDay = now + responseOffsets[i],
                    ReportedToRuler = decisions[i] == "report",
                    RequestChannel = channels[i],
                    ActionId = fixture + "_action_" + i
                });
            }
        }

        private ReignRebellionPlotRecord EnsureFixturePlot(string fixture, float now)
        {
            ReignRebellionPlotRecord plot = FixturePlot(fixture);
            if (plot != null) return plot;
            plot = new ReignRebellionPlotRecord
            {
                PlotId = fixture + "_plot",
                PlayerHeroStringId = fixture + "_player",
                PlayerClanStringId = fixture + "_player_clan",
                ParentKingdomStringId = fixture + "_parent",
                RulerHeroStringId = fixture + "_ruler",
                Status = "planning",
                CreatedDay = now,
                UpdatedDay = now,
                CorrelationId = fixture + "_correlation"
            };
            _plots.Add(plot);
            return plot;
        }

        private void EnsureReportingFixture(string fixture, float now)
        {
            EnsurePledgeFixtures(fixture, now);
            ReignRebellionPlotRecord plot = FixturePlot(fixture);
            ReignRebellionPledgeRecord report = FixturePledges(fixture).Single(x => Same(x.Decision, "report"));
            plot.Status = "reported_in_transit";
            plot.InformerHeroStringId = report.LordHeroStringId;
            plot.InformerClanStringId = report.ClanStringId;
            plot.ReportDispatchDay = now;
            plot.ReportDeliveryDay = now + 1f;
            plot.ReportLetterId = fixture + "_report_letter";
            plot.UpdatedDay = now;
        }

        private void EnsureSummonsFixture(string fixture, float now)
        {
            EnsureReportingFixture(fixture, now);
            ReignRebellionPlotRecord plot = FixturePlot(fixture);
            if (FixtureSummonses(fixture).Any()) return;
            ReignRebellionSummonsRecord summons = new ReignRebellionSummonsRecord
            {
                SummonsId = fixture + "_summons",
                PlotId = plot.PlotId,
                RulerHeroStringId = plot.RulerHeroStringId,
                PlayerHeroStringId = plot.PlayerHeroStringId,
                InformerHeroStringId = plot.InformerHeroStringId,
                Status = "delivered",
                IssuedDay = now,
                DeliveryDay = now + 1f,
                DeadlineDay = now + 8f,
                LetterId = fixture + "_summons_letter"
            };
            _summonses.Add(summons);
            plot.SummonsId = summons.SummonsId;
            plot.Status = "summoned";
        }

        private void EnsureVerdictFixtures(string fixture, float now)
        {
            EnsureSummonsFixture(fixture, now);
            string[] verdicts = { "pardon", "imprisonment", "exile", "execution" };
            foreach (string verdict in verdicts)
            {
                string id = fixture + "_summons_" + verdict;
                if (_summonses.Any(x => Same(x.SummonsId, id))) continue;
                _summonses.Add(new ReignRebellionSummonsRecord
                {
                    SummonsId = id, PlotId = fixture + "_plot",
                    RulerHeroStringId = fixture + "_ruler",
                    PlayerHeroStringId = fixture + "_player",
                    InformerHeroStringId = fixture + "_lord_2",
                    Status = "answered", IssuedDay = now,
                    DeliveryDay = now + 1f, DeadlineDay = now + 8f,
                    AnsweredDay = now + 2f, PlayerResponse = "renounce",
                    RulerVerdict = verdict, Consequence = verdict,
                    ConsequenceApplied = true
                });
            }
            string fallbackId = fixture + "_summons_execution_fallback";
            if (!_summonses.Any(x => Same(x.SummonsId, fallbackId)))
                _summonses.Add(new ReignRebellionSummonsRecord
                {
                    SummonsId = fallbackId, PlotId = fixture + "_plot",
                    RulerHeroStringId = fixture + "_ruler",
                    PlayerHeroStringId = fixture + "_player",
                    InformerHeroStringId = fixture + "_lord_2",
                    Status = "answered", IssuedDay = now,
                    DeliveryDay = now + 1f, DeadlineDay = now + 8f,
                    AnsweredDay = now + 2f, PlayerResponse = "renounce",
                    RulerVerdict = "execution", Consequence = "imprisonment",
                    ConsequenceApplied = true
                });
        }

        private void EnsureDeclarationFixture(string fixture, float now)
        {
            EnsurePledgeFixtures(fixture, now);
            if (FixtureMovements(fixture).Any()) return;
            ReignRebellionMovementRecord movement = new ReignRebellionMovementRecord
            {
                MovementId = fixture + "_movement",
                ParentKingdomStringId = fixture + "_parent",
                RebelKingdomStringId = fixture + "_rebels",
                LeaderClanStringId = fixture + "_player_clan",
                LeaderHeroStringId = fixture + "_player",
                OriginalRulerHeroStringId = fixture + "_ruler",
                Stage = "civil_war",
                IsPlayerLed = true,
                CreatedDay = now,
                UpdatedDay = now,
                CivilWarStartedDay = now,
                ResolutionCause = "player_declaration",
                OriginalStrongholdIdsCsv = fixture + "_town," + fixture + "_castle",
                ExecutionSnapshotJson = new JObject
                {
                    ["activationCauses"] = new JArray("player_declaration", "summons_deadline_ignored"),
                    ["strongholdIds"] = new JArray(fixture + "_town", fixture + "_castle"),
                    ["boundSettlementIds"] = new JArray(fixture + "_village_a", fixture + "_village_b")
                }.ToString(Newtonsoft.Json.Formatting.None),
                DemandJson = new JObject
                {
                    ["ordinaryCause"] = "player_declaration",
                    ["forcedCause"] = "summons_deadline_ignored"
                }.ToString(Newtonsoft.Json.Formatting.None),
                ExecutionPhase = "verified"
            };
            _movements.Add(movement);
            for (int i = 0; i < 2; i++)
                _memberships.Add(new ReignRebellionMembershipRecord
                {
                    MovementId = movement.MovementId,
                    ClanStringId = fixture + "_clan_" + i,
                    LeaderHeroStringId = fixture + "_lord_" + i,
                    Side = "rebel", IsCommitted = true,
                    OriginalKingdomStringId = i == 1 ? "foreign_" + fixture : movement.ParentKingdomStringId,
                    JoinedDay = now, JoinedBy = "prepared_pledge"
                });
        }

        private void EnsureResolutionFixtures(string fixture, float now)
        {
            RemoveHarnessFixtures(fixture);
            string[] causes = { "rebel_leader_captured", "challenged_ruler_killed", "challenged_ruler_deposed" };
            for (int i = 0; i < causes.Length; i++)
            {
                string movementId = fixture + "_resolution_" + i;
                _movements.Add(new ReignRebellionMovementRecord
                {
                    MovementId = movementId,
                    ParentKingdomStringId = fixture + "_parent",
                    RebelKingdomStringId = fixture + "_rebels_" + i,
                    LeaderClanStringId = fixture + "_leader_clan",
                    LeaderHeroStringId = fixture + "_rebel_leader",
                    OriginalRulerHeroStringId = fixture + "_ruler",
                    Stage = "resolved", ResolutionApplied = true,
                    WinningSide = i == 0 ? "loyalist" : "rebel",
                    Resolution = i == 0 ? "loyalist_victory" : "rebel_victory",
                    ResolutionCause = causes[i], UpdatedDay = now,
                    CooldownUntilDay = now + RealmCooldownDays,
                    ExecutionPhase = "resolved"
                });
            }
            string[] outcomes = { "freedom", "imprisonment", "execution" };
            for (int i = 0; i < outcomes.Length; i++)
                _memberships.Add(new ReignRebellionMembershipRecord
                {
                    MovementId = fixture + "_resolution_" + (i % causes.Length),
                    ClanStringId = fixture + "_fate_clan_" + i,
                    LeaderHeroStringId = fixture + "_fate_lord_" + i,
                    Side = "loyalist", IsDefeated = true,
                    FateOutcome = outcomes[i], FateApplied = true,
                    FateRoll = 1 + i * 33, FateScore = 10 + i * 40
                });
        }

        private bool IsPreparationRecruitmentOpen(ReignRebellionPlotRecord plot)
            => plot != null && Same(plot.Status, "planning")
                && string.IsNullOrWhiteSpace(plot.InformerHeroStringId)
                && string.IsNullOrWhiteSpace(plot.SummonsId);

        private static bool IsRebellionHarnessSave(string value)
            => !string.IsNullOrWhiteSpace(value)
                && (value.StartsWith("Reign_", StringComparison.OrdinalIgnoreCase)
                    || value.StartsWith("ReignTest_", StringComparison.OrdinalIgnoreCase));

        private ReignRebellionPlotRecord FixturePlot(string fixture)
            => _plots.FirstOrDefault(x => x != null && Same(x.PlotId, fixture + "_plot"));
        private IEnumerable<ReignRebellionPledgeRecord> FixturePledges(string fixture)
            => _pledges.Where(x => x != null && (x.PledgeId ?? string.Empty)
                .StartsWith(fixture + "_", StringComparison.OrdinalIgnoreCase));
        private IEnumerable<ReignRebellionSummonsRecord> FixtureSummonses(string fixture)
            => _summonses.Where(x => x != null && (x.SummonsId ?? string.Empty)
                .StartsWith(fixture + "_", StringComparison.OrdinalIgnoreCase));
        private IEnumerable<ReignRebellionMovementRecord> FixtureMovements(string fixture)
            => _movements.Where(x => x != null && (x.MovementId ?? string.Empty)
                .StartsWith(fixture + "_", StringComparison.OrdinalIgnoreCase));
        private IEnumerable<ReignRebellionMembershipRecord> FixtureMemberships(string fixture)
            => _memberships.Where(x => x != null && (x.MovementId ?? string.Empty)
                .StartsWith(fixture + "_", StringComparison.OrdinalIgnoreCase));

        private int FixtureRecordCount(string fixture)
            => _plots.Count(x => x != null && (x.PlotId ?? string.Empty).StartsWith(fixture + "_", StringComparison.OrdinalIgnoreCase))
                + FixturePledges(fixture).Count() + FixtureSummonses(fixture).Count()
                + FixtureMovements(fixture).Count() + FixtureMemberships(fixture).Count();

        private JObject FixtureCounts(string fixture) => new JObject
        {
            ["plots"] = _plots.Count(x => x != null && (x.PlotId ?? string.Empty).StartsWith(fixture + "_", StringComparison.OrdinalIgnoreCase)),
            ["pledges"] = FixturePledges(fixture).Count(),
            ["summonses"] = FixtureSummonses(fixture).Count(),
            ["movements"] = FixtureMovements(fixture).Count(),
            ["memberships"] = FixtureMemberships(fixture).Count(),
            ["total"] = FixtureRecordCount(fixture)
        };

        private int RemoveHarnessFixtures(string fixture)
        {
            int before = FixtureRecordCount(fixture);
            _plots.RemoveAll(x => x != null && (x.PlotId ?? string.Empty)
                .StartsWith(fixture + "_", StringComparison.OrdinalIgnoreCase));
            _pledges.RemoveAll(x => x != null && (x.PledgeId ?? string.Empty)
                .StartsWith(fixture + "_", StringComparison.OrdinalIgnoreCase));
            _summonses.RemoveAll(x => x != null && (x.SummonsId ?? string.Empty)
                .StartsWith(fixture + "_", StringComparison.OrdinalIgnoreCase));
            _movements.RemoveAll(x => x != null && (x.MovementId ?? string.Empty)
                .StartsWith(fixture + "_", StringComparison.OrdinalIgnoreCase));
            _memberships.RemoveAll(x => x != null && (x.MovementId ?? string.Empty)
                .StartsWith(fixture + "_", StringComparison.OrdinalIgnoreCase));
            return before - FixtureRecordCount(fixture);
        }

        private static bool IsTerminalPlotStatus(string value)
        {
            return Same(value, "resolved") || Same(value, "cancelled");
        }

        private static string DescribePreparationDecision(Hero lord, string decision)
        {
            string name = lord?.Name?.ToString() ?? "The lord";
            if (Same(decision, "pledge")) return name + " secretly pledged the clan's support when rebellion is declared.";
            if (Same(decision, "report")) return name + " refused to pledge support.";
            return name + " refused to pledge support.";
        }
    }
}
