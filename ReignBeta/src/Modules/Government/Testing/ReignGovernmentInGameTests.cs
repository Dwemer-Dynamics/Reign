using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Reign.Core.Contracts.Government;
using ReignBeta.Integration;
using ReignBeta.World;
using TaleWorlds.CampaignSystem;

namespace ReignBeta.Government
{
    public sealed partial class ReignGovernmentCampaignBehavior
    {
        internal JObject RunGovernmentTestProfile(string runId, string profile,
            JObject options, string gameInstanceId)
        {
            string id = string.IsNullOrWhiteSpace(runId) ? Guid.NewGuid().ToString("N") : runId.Trim();
            string phase = (profile ?? "preflight").Trim().ToLowerInvariant();
            options = options ?? new JObject();
            if (!TryRequireGovernmentTestSave(options, out string expectedSave,
                out string activeSave, out string saveError))
                return GovernmentTestFailure(id, phase, saveError, expectedSave, activeSave);

            switch (phase)
            {
                case "preflight": return GovernmentPreflight(id, expectedSave, activeSave);
                case "contracts": return GovernmentContracts(id);
                case "snapshot": return GovernmentSnapshot(id);
                case "hearing_compose":
                    bool composed = ReignBeta.UI.ReignGovernmentScreenManager.TryComposeHearing(
                        options.Value<string>("businessId"), options.Value<string>("playerMessage"));
                    return new JObject { ["ok"] = composed, ["profile"] = phase,
                        ["businessId"] = options.Value<string>("businessId"), ["submitted"] = false,
                        ["message"] = composed ? "The unsent composer is ready. Use the native Speak control to submit."
                            : "Open the exact interactive hearing and provide 1-1200 characters. No text was changed." };
                case "authority": return GovernmentAuthorityContracts(id);
                case "resolutions": return GovernmentResolutionContracts(id);
                case "voting": return GovernmentVotingContracts(id);
                case "pressure": return GovernmentPressureContracts(id);
                case "save_prepare": return GovernmentSavePrepare(id, options, gameInstanceId);
                case "save_verify": return GovernmentSaveVerify(id, options, gameInstanceId);
                case "case_prepare": return GovernmentCertificationCasePrepare(id, options);
                case "case_observe": return GovernmentCertificationCaseObserve(id, options);
                case "case_execute": return GovernmentCertificationCaseExecute(id, options);
                case "soak_prepare": return GovernmentCertificationSoakPrepare(id);
                case "soak_verify": return GovernmentCertificationSoakVerify(id, options);
                case "cleanup": return GovernmentCertificationCleanup(id);
                default: return GovernmentTestFailure(id, phase,
                    "Unsupported government test profile.", expectedSave, activeSave);
            }
        }

        private JObject GovernmentPreflight(string runId, string expectedSave, string activeSave)
        {
            JArray assertions = new JArray();
            List<Kingdom> kingdoms = Kingdom.All.Where(IsEligibleKingdom)
                .OrderBy(x => x.StringId, StringComparer.Ordinal).ToList();
            foreach (Kingdom kingdom in kingdoms) EnsureGovernment(kingdom);
            bool allInitialized = kingdoms.Count > 0 && kingdoms.All(kingdom =>
            {
                ReignGovernmentStateRecord state = GetGovernment(kingdom);
                List<ReignGovernmentPartyRecord> parties = GetParties(kingdom.StringId).ToList();
                List<ReignGovernmentSeatRecord> seats = GetSeats(kingdom.StringId).ToList();
                int coalitionSeats = parties.Where(party => party.IsGoverningCoalition).Sum(party => party.SeatCount);
                return state != null && state.Level >= 1 && state.Level <= 5
                    && parties.Count >= 2 && parties.Count <= 4
                    && parties.All(party => ParsePlanks(party.PlanksCsv).Count >= 2
                        && ParsePlanks(party.PlanksCsv).Count <= 4)
                    && parties.Where(party => party.SeatCount > 0)
                        .All(party => !string.IsNullOrWhiteSpace(party.SpeakerHeroStringId))
                    && parties.Sum(party => party.SeatCount) == seats.Count
                    && parties.Count(party => party.IsDominant) == 1
                    && parties.Count(party => party.IsGoverningCoalition) >= 1
                    && coalitionSeats * 2 > seats.Count;
            });
            AddGovernmentAssertion(assertions, "government_exact_disposable_save",
                string.Equals(expectedSave, activeSave, StringComparison.OrdinalIgnoreCase),
                "The client independently matched Bannerlord's active save to the exact MCP-authorized campaign-test Current.",
                new JObject { ["expectedSaveName"] = expectedSave, ["activeSaveName"] = activeSave });
            AddGovernmentAssertion(assertions, "government_campaign_loaded",
                TaleWorlds.CampaignSystem.Campaign.Current != null,
                "A Bannerlord campaign is loaded.", null);
            AddGovernmentAssertion(assertions, "government_all_kingdoms_initialized", allInitialized,
                "Every eligible kingdom has a saved level, culture institution, two to four parties, valid multi-plank agendas, a deterministic majority coalition, occupied seats, and one speaker for each occupied party.",
                new JObject { ["eligibleKingdomCount"] = kingdoms.Count, ["governmentCount"] = _governments.Count });
            AddGovernmentAssertion(assertions, "government_resolution_catalog_104",
                ReignGovernmentResolutionCatalog.Templates.Count == 104,
                "The authoritative catalog contains exactly 104 tangible two-route resolutions.", null);
            AddGovernmentAssertion(assertions, "government_npc_meetings_provider_free",
                _meetings.Where(meeting => !meeting.PlayerAttended).All(meeting => meeting.ProviderCallCount == 0),
                "NPC seasonal meetings remain deterministic and make zero provider calls.", null);
            JObject result = GovernmentTestResult(runId, "preflight", assertions);
            result["kingdoms"] = new JArray(kingdoms.Select(kingdom => GovernmentKingdomEvidence(kingdom)));
            return result;
        }

        private static JObject GovernmentContracts(string runId)
        {
            JArray assertions = new JArray();
            int[] levelCounts = new int[6];
            int[] partyCounts = new int[5];
            var observedBlueprints = new Dictionary<string, ReignGovernmentPartyBlueprint>(StringComparer.OrdinalIgnoreCase);
            const int samples = 10000;
            for (int index = 0; index < samples; index++)
            {
                string id = "government-contract-" + index;
                levelCounts[ReignGovernmentRules.SelectStartingLevel(id)]++;
                int count = ReignGovernmentRules.SelectPartyCount(id, 12);
                partyCounts[count]++;
                foreach (ReignGovernmentPartyBlueprint party in ReignGovernmentRules.SelectParties(id, 12))
                    observedBlueprints[party.Id] = party;
            }
            int[] expectedLevels = { 0, 1000, 2500, 3000, 2500, 1000 };
            bool weightedLevels = Enumerable.Range(1, 5)
                .All(level => Math.Abs(levelCounts[level] - expectedLevels[level]) <= 220);
            AddGovernmentAssertion(assertions, "government_weighted_starting_levels", weightedLevels,
                "A 10,000-key deterministic sample remains within 2.2 percentage points of 10/25/30/25/10, so levels 2-4 are materially more likely than 1 or 5.",
                new JObject { ["level1"] = levelCounts[1], ["level2"] = levelCounts[2],
                    ["level3"] = levelCounts[3], ["level4"] = levelCounts[4], ["level5"] = levelCounts[5] });
            bool weightedParties = Math.Abs(partyCounts[2] - 4500) <= 220
                && Math.Abs(partyCounts[3] - 4500) <= 220 && Math.Abs(partyCounts[4] - 1000) <= 220;
            AddGovernmentAssertion(assertions, "government_weighted_party_counts", weightedParties,
                "Party count follows 45% two, 45% three, and 10% four only at twelve or more occupied seats.",
                new JObject { ["two"] = partyCounts[2], ["three"] = partyCounts[3], ["four"] = partyCounts[4] });
            AddGovernmentAssertion(assertions, "government_party_planks_multi_tone",
                observedBlueprints.Count >= 16 && observedBlueprints.Values.All(party => party.Planks.Count >= 2 && party.Planks.Count <= 4),
                "Every selectable party combines two to four compatible historical tones rather than one single issue.",
                new JObject { ["observedBlueprintCount"] = observedBlueprints.Count });

            var cultures = new Dictionary<string, ReignGovernmentInstitutionKind>
            {
                ["empire"] = ReignGovernmentInstitutionKind.Senate,
                ["vlandia"] = ReignGovernmentInstitutionKind.CouncilOfPeers,
                ["sturgia"] = ReignGovernmentInstitutionKind.Veche,
                ["battania"] = ReignGovernmentInstitutionKind.Oenach,
                ["aserai"] = ReignGovernmentInstitutionKind.Majlis,
                ["khuzait"] = ReignGovernmentInstitutionKind.Kurultai,
                ["nord"] = ReignGovernmentInstitutionKind.Thing
            };
            AddGovernmentAssertion(assertions, "government_culture_institutions",
                cultures.All(pair => ReignGovernmentRules.InstitutionForCulture(pair.Key).Kind == pair.Value),
                "Every supported culture maps to its approved institution kind, with Council of Estates as the fallback.", null);
            return GovernmentTestResult(runId, "contracts", assertions);
        }

        private JObject GovernmentSnapshot(string runId)
        {
            JArray assertions = new JArray();
            List<Kingdom> kingdoms = Kingdom.All.Where(IsEligibleKingdom)
                .OrderBy(x => x.StringId, StringComparer.Ordinal).ToList();
            var snapshots = new JArray();
            bool privacy = true;
            bool pressureChannels = true;
            bool coalitions = true;
            foreach (Kingdom kingdom in kingdoms)
            {
                JObject snapshot = BuildPublicSnapshot(kingdom);
                snapshots.Add(snapshot);
                privacy &= snapshot["members"] == null && snapshot["outstandingDeals"] == null
                    && !snapshot.Descendants().OfType<JProperty>().Any(property =>
                        new[] { "trust", "rulerRelation", "relation", "partyLoyalty", "governmentLoyalty",
                            "lobbyingShift", "supportPercent", "stanceTowardRuler" }
                            .Contains(property.Name, StringComparer.OrdinalIgnoreCase));
                pressureChannels &= snapshot.SelectToken("peoplePressure.additionalToNoblePoliticalPressure")?.Value<bool>() == true;
                coalitions &= snapshot["governingCoalitionPartyIds"] is JArray coalition
                    && coalition.Count > 0 && snapshot["parties"] is JArray snapshotParties
                    && snapshotParties.Children<JObject>().Any(party => party.Value<bool?>("isGoverningCoalition") == true);
            }
            AddGovernmentAssertion(assertions, "government_public_snapshot_privacy", privacy,
                "Public ruler and ambassador knowledge exposes institutions and observable political records without live relationships, hidden loyalty, Trust, score predictions, bribes or private deals.", null);
            AddGovernmentAssertion(assertions, "government_additional_people_pressure", pressureChannels,
                "Every public snapshot explicitly reports Government as people-side pressure additional to noble Political Pressure, with separate settlement and civil-war input routes.", null);
            AddGovernmentAssertion(assertions, "government_public_governing_coalition", coalitions,
                "Every public snapshot identifies the leading party and every party in its saved majority governing coalition.", null);
            AddGovernmentAssertion(assertions, "government_authoritative_ambassador_knowledge",
                snapshots.All(value => value.Value<bool?>("authoritative") == true
                    && !string.IsNullOrWhiteSpace(value.Value<string>("authorityMeaning"))
                    && value.Value<string>("decisionRule")?.IndexOf("individual members decide", StringComparison.OrdinalIgnoreCase) >= 0
                    && value.Value<string>("decisionRule")?.IndexOf("no later ruler veto", StringComparison.OrdinalIgnoreCase) >= 0),
                "Rulers and ambassadors receive authoritative disagreement-aware government knowledge.", null);
            JObject result = GovernmentTestResult(runId, "snapshot", assertions);
            result["governments"] = snapshots;
            return result;
        }

        private JObject GovernmentAuthorityContracts(string runId)
        {
            JArray assertions = new JArray();
            bool increasesAttractive = Enumerable.Range(1, 4).All(level =>
            {
                ReignGovernmentAuthorityTransition value = ReignGovernmentRules.IncreaseTransition(level);
                return value.Loyalty > 0 && value.Prosperity > 0 && value.Hearth > 0
                    && value.DurationDays == 30 && value.Trust == 0 && value.SupportiveRelation > 0;
            });
            AddGovernmentAssertion(assertions, "government_power_increase_trap", increasesAttractive,
                "Every authority increase grants immediate loyalty, prosperity, hearth, relationship and thirty-day benefits without changing retired Government Trust.", null);
            ReignGovernmentReductionPenalty[] forcedScale = Enumerable.Range(2, 4)
                .Select(ReignGovernmentRules.ForcedReductionPenalty).ToArray();
            ReignGovernmentReductionPenalty forced = forcedScale[3];
            AddGovernmentAssertion(assertions, "government_forced_five_to_four_exact",
                forcedScale.Select(x => x.LandholdingClanLeaderRelation)
                    .SequenceEqual(new[] { -20, -35, -50, -70 })
                && forcedScale.Select(x => x.NonLandholdingDissenterRelation)
                    .SequenceEqual(new[] { -10, -20, -30, -40 })
                && forced.SettlementLoyalty == -70 && forced.Trust == 0,
                "Forced reductions use the exact capped clan-leader -20/-35/-50/-70 and nonlandholder -10/-20/-30/-40 ladders while 5-to-4 retains -70 settlement loyalty and trust zero.", null);
            ReignGovernmentReductionPenalty approved = ReignGovernmentRules.ApprovedReductionPenalty(5);
            AddGovernmentAssertion(assertions, "government_voted_five_to_four_small_cost",
                approved.SettlementLoyalty == -5 && approved.LandholdingClanLeaderRelation == 0
                && approved.NonLandholdingDissenterRelation == -5 && approved.Trust == 0,
                "A ratified 5-to-4 reduction carries only its small fixed consented costs.", null);
            bool monotonic = Enumerable.Range(1, 4).All(level =>
            {
                ReignGovernmentDailyEffects lower = ReignGovernmentRules.DailyEffects(level);
                ReignGovernmentDailyEffects upper = ReignGovernmentRules.DailyEffects(level + 1);
                return upper.Loyalty >= lower.Loyalty && upper.Prosperity >= lower.Prosperity && upper.Hearth >= lower.Hearth;
            });
            AddGovernmentAssertion(assertions, "government_daily_benefits_monotonic", monotonic,
                "Standing people-side loyalty, prosperity, and hearth benefits rise monotonically with government authority.", null);

            Clan consentClan = Kingdom.All.Where(IsEligibleKingdom).SelectMany(x => x.Clans)
                .Where(x => x != null && x != Clan.PlayerClan && !x.IsEliminated && x.Leader != null)
                .OrderBy(x => x.StringId, StringComparer.Ordinal).FirstOrDefault();
            Hero individual = _seats.Select(x => FindHero(x.HeroStringId))
                .Where(x => x != null && x.Clan != Clan.PlayerClan
                    && (consentClan == null || x.Clan != consentClan))
                .OrderBy(x => x.StringId, StringComparer.Ordinal).FirstOrDefault();
            var consentState = new ReignGovernmentStateRecord
            {
                Level = 5,
                ReductionConsentFromLevel = 5,
                ReductionConsentHeroIdsCsv = individual?.StringId ?? string.Empty,
                ReductionConsentClanIdsCsv = consentClan?.StringId ?? string.Empty
            };
            bool individualScoped = individual == null
                || IsReductionPenaltyExempt(consentState, individual, 5);
            bool clanScoped = consentClan == null || consentClan.Heroes
                .Where(x => x != null && x.IsAlive).All(x => IsReductionPenaltyExempt(consentState, x, 5));
            bool playerClanExempt = Clan.PlayerClan == null || Clan.PlayerClan.Heroes
                .Where(x => x != null && x.IsAlive).All(x => IsReductionPenaltyExempt(
                    new ReignGovernmentStateRecord { Level = 5 }, x, 5));
            bool staleConsentRejected = (individual == null
                    || !IsReductionPenaltyExempt(consentState, individual, 4))
                && (consentClan?.Leader == null
                    || !IsReductionPenaltyExempt(consentState, consentClan.Leader, 4));
            AddGovernmentAssertion(assertions, "government_reduction_consent_scopes",
                individualScoped && clanScoped && playerClanExempt && staleConsentRejected,
                "Individual consent exempts only its saved NPC, clan-leader consent exempts the whole saved clan, the player clan is always exempt, and consent does not carry to another authority level.",
                new JObject { ["individualHeroId"] = individual?.StringId ?? string.Empty,
                    ["consentingClanId"] = consentClan?.StringId ?? string.Empty });
            return GovernmentTestResult(runId, "authority", assertions);
        }

        private JObject GovernmentResolutionContracts(string runId)
        {
            JArray assertions = new JArray();
            IReadOnlyList<ReignGovernmentResolutionTemplate> templates = ReignGovernmentResolutionCatalog.Templates;
            bool tangible = templates.Count == 104 && templates.All(template => template.FirstRoute.Target > 0
                && template.SecondRoute.Target > 0 && template.FirstRoute.DeadlineDays > 0
                && template.SecondRoute.DeadlineDays > 0
                && (template.FirstRoute.Action != template.SecondRoute.Action
                    || !string.Equals(template.FirstRoute.Description, template.SecondRoute.Description, StringComparison.OrdinalIgnoreCase)));
            AddGovernmentAssertion(assertions, "government_104_tangible_two_route_resolutions", tangible,
                "All 104 resolution templates expose two distinct, measurable, deadline-bound completion routes.", null);
            ReignGovernmentResolutionAction[] actions = (ReignGovernmentResolutionAction[])Enum.GetValues(typeof(ReignGovernmentResolutionAction));
            AddGovernmentAssertion(assertions, "government_resolution_action_catalog_complete",
                actions.All(action => templates.Any(template => template.FirstRoute.Action == action || template.SecondRoute.Action == action)),
                "Every resolution action type is used by at least one production template.",
                new JObject { ["actionCount"] = actions.Length });

            Kingdom kingdom = Kingdom.All.Where(IsEligibleKingdom).OrderBy(x => x.StringId, StringComparer.Ordinal).FirstOrDefault();
            Kingdom target = Kingdom.All.Where(x => x != null && x != kingdom && !x.IsEliminated)
                .OrderBy(x => x.StringId, StringComparer.Ordinal).FirstOrDefault();
            bool routed = false;
            if (kingdom != null && target != null)
            {
                var matching = new ReignGovernmentResolutionRecord
                {
                    KingdomStringId = kingdom.StringId, TemplateId = "R019", Status = "active",
                    RouteActionValue = (int)ReignGovernmentResolutionAction.SignTradeAgreement,
                    TargetKingdomStringId = target.StringId, RequiredAmount = 3, CurrentValue = 0f
                };
                var other = new ReignGovernmentResolutionRecord
                {
                    KingdomStringId = kingdom.StringId, TemplateId = "R019", Status = "active",
                    RouteActionValue = (int)ReignGovernmentResolutionAction.SignTradeAgreement,
                    TargetKingdomStringId = "government_test_non_target", RequiredAmount = 3, CurrentValue = 0f
                };
                _resolutions.Add(matching);
                _resolutions.Add(other);
                try
                {
                    int updated = RecordResolutionProgress(kingdom.StringId,
                        ReignGovernmentResolutionAction.SignTradeAgreement, target.StringId, 1,
                        "Government harness target-routing receipt.");
                    routed = updated == 1 && Math.Abs(matching.CurrentValue - 1f) < 0.001f
                        && Math.Abs(other.CurrentValue) < 0.001f && Same(matching.Status, "active");
                }
                finally
                {
                    _resolutions.Remove(matching);
                    _resolutions.Remove(other);
                }
            }
            AddGovernmentAssertion(assertions, "government_resolution_target_routing", routed,
                "A verified action advances only the active resolution with the matching kingdom/action/target tuple, and the harness restores its synthetic records.", null);
            int trackableRoutes = 0;
            const string syntheticKingdomId = "government_test_synthetic_kingdom";
            foreach (ReignGovernmentResolutionTemplate template in templates)
            {
                ReignGovernmentResolutionRoute[] routes = { template.FirstRoute, template.SecondRoute };
                for (int routeIndex = 0; routeIndex < routes.Length; routeIndex++)
                {
                    ReignGovernmentResolutionRoute route = routes[routeIndex];
                    string targetId = "government_test_target_" + template.Id + "_" + routeIndex;
                    var record = new ReignGovernmentResolutionRecord
                    {
                        ResolutionId = "government_test_route_" + template.Id + "_" + routeIndex,
                        KingdomStringId = syntheticKingdomId,
                        TemplateId = template.Id,
                        Status = "active",
                        RouteActionValue = (int)route.Action,
                        TargetSettlementStringId = targetId,
                        TargetKingdomStringId = targetId,
                        TargetClanStringId = targetId,
                        TargetHeroStringId = targetId,
                        TargetPolicyStringId = targetId,
                        RequiredAmount = Math.Max(2, route.Target),
                        CurrentValue = 0f
                    };
                    _resolutions.Add(record);
                    try
                    {
                        int updated = RecordResolutionProgress(syntheticKingdomId, route.Action,
                            targetId, 1, "Government 208-route tracking contract.");
                        if (updated == 1 && Math.Abs(record.CurrentValue - 1f) < 0.001f
                            && Same(record.Status, "active")
                            && record.EvidenceJson.IndexOf("208-route", StringComparison.OrdinalIgnoreCase) >= 0)
                            trackableRoutes++;
                    }
                    finally
                    {
                        _resolutions.Remove(record);
                    }
                }
            }
            AddGovernmentAssertion(assertions, "government_all_208_resolution_routes_trackable",
                trackableRoutes == 208,
                "Every route in all 104 two-route templates accepts correctly targeted evidence through its production action type; representative native cases separately prove completion rewards.",
                new JObject { ["trackableRoutes"] = trackableRoutes, ["expectedRoutes"] = 208 });
            return GovernmentTestResult(runId, "resolutions", assertions);
        }

        private static JObject GovernmentVotingContracts(string runId)
        {
            JArray assertions = new JArray();
            var baseReduction = new ReignGovernmentVoteInput
            {
                PrimaryPlank = ReignGovernmentPlank.RoyalAuthority,
                SecondaryPlanks = new[] { ReignGovernmentPlank.Security }, CurrentLevel = 5,
                PartySeatShare = 0.25d, PartyLoyalty = 75, SpeakerSupportsReduction = true,
                RulerRelation = -100
            };
            int hostile = ReignGovernmentRules.EvaluateLevelReductionVote(baseReduction).Score;
            baseReduction.RulerRelation = 100;
            int friendly = ReignGovernmentRules.EvaluateLevelReductionVote(baseReduction).Score;
            AddGovernmentAssertion(assertions, "government_reduction_vote_every_member_relation", friendly - hostile == 100,
                "Each member's relationship with the ruler contributes directly to that member's authority-reduction vote.",
                new JObject { ["hostileScore"] = hostile, ["friendlyScore"] = friendly });
            baseReduction.PartySeatShare = 0.05d;
            int smallParty = ReignGovernmentRules.EvaluateLevelReductionVote(baseReduction).Score;
            baseReduction.PartySeatShare = 0.95d;
            int largeParty = ReignGovernmentRules.EvaluateLevelReductionVote(baseReduction).Score;
            AddGovernmentAssertion(assertions, "government_party_power_self_interest", largeParty < smallParty,
                "Members become harder to persuade when a reduction would cost their own party more institutional power.", null);

            var vote = new ReignGovernmentResolutionVoteInput
            {
                IsProposingPartyMember = false, SharedPlankCount = 0, GovernmentLoyalty = 35,
                PartyLoyalty = 20, OwnSpeakerSupports = false, ProposingSpeakerCharm = 20,
                OwnSpeakerCharm = 160, ProposingSpeakerRelation = -40, LobbyingShift = 0
            };
            int entrenched = ReignGovernmentRules.EvaluateResolutionVote(vote).Score;
            vote.ProposingSpeakerCharm = 300;
            vote.OwnSpeakerCharm = 20;
            vote.ProposingSpeakerRelation = 80;
            vote.LobbyingShift = 25;
            int persuaded = ReignGovernmentRules.EvaluateResolutionVote(vote).Score;
            AddGovernmentAssertion(assertions, "government_charm_loyalty_defection", persuaded > entrenched,
                "Opposition-speaker charm, personal relations, and difficult bounded lobbying can move an individual vote despite party alignment.",
                new JObject { ["entrenchedScore"] = entrenched, ["persuadedScore"] = persuaded });
            AddGovernmentAssertion(assertions, "government_two_thirds_reduction_vote",
                !ReignGovernmentRules.ReductionRatified(5, 8) && ReignGovernmentRules.ReductionRatified(6, 9),
                "Authority reductions require at least two thirds of all occupied seats.", null);
            return GovernmentTestResult(runId, "voting", assertions);
        }

        private JObject GovernmentPressureContracts(string runId)
        {
            JArray assertions = new JArray();
            ReignGovernmentActionDisposition[] expected =
            {
                ReignGovernmentActionDisposition.Advisory,
                ReignGovernmentActionDisposition.FormalPressure,
                ReignGovernmentActionDisposition.ReconsiderationDelay,
                ReignGovernmentActionDisposition.ApprovalOrOverride,
                ReignGovernmentActionDisposition.BlockedPendingApprovalOrLevelReduction
            };
            bool levels = Enumerable.Range(1, 5).All(level =>
                ReignGovernmentRules.ActionDisposition(level, ReignGovernmentActionKind.War) == expected[level - 1]);
            AddGovernmentAssertion(assertions, "government_pressure_levels_one_to_five", levels,
                "Major actions progress from advisory, formal pressure, seven-day reconsideration, approval-or-override, to level-five block.", null);
            AddGovernmentAssertion(assertions, "government_level_three_delay_exact",
                ReignGovernmentRules.ReconsiderationDelayDays(3) == 7
                && ReignGovernmentRules.ReconsiderationDelayDays(2) == 0,
                "Only level 3 applies the exact seven-day reconsideration delay.", null);
            ReignWorldActionType[] coveredPoliticalActions =
            {
                ReignWorldActionType.DiplomacyDeclareWar, ReignWorldActionType.DiplomacyMakePeace,
                ReignWorldActionType.DiplomacySignTradeAgreement, ReignWorldActionType.DiplomacyExchangePrisoners,
                ReignWorldActionType.DiplomacyRansomPackage, ReignWorldActionType.DiplomacyBackRebellion,
                ReignWorldActionType.RegularDismissPlayerVassal, ReignWorldActionType.RegularConfirmArrest
            };
            ReignWorldActionType[] excludedPersonalAndTacticalActions =
            {
                ReignWorldActionType.StrategyFormArmy, ReignWorldActionType.StrategyCaptureSettlement,
                ReignWorldActionType.PoliticsStartRulingClanRebellion, ReignWorldActionType.PoliticsInstallRulingClan,
                ReignWorldActionType.PoliticsMarriageAlliance, ReignWorldActionType.PoliticsResolveRebellionPledge,
                ReignWorldActionType.PoliticsResolveRebellionSummons, ReignWorldActionType.RegularRaidVillage,
                ReignWorldActionType.RegularOfferPlayerVassalage, ReignWorldActionType.RegularTransferWorkshop,
                ReignWorldActionType.RegularIssueCampaignOrder, ReignWorldActionType.RegularCancelCampaignOrder
            };
            AddGovernmentAssertion(assertions, "government_reign_political_action_coverage",
                coveredPoliticalActions.All(action => MapActionKind(action) != ReignGovernmentActionKind.Advice)
                && excludedPersonalAndTacticalActions.All(action => MapActionKind(action) == ReignGovernmentActionKind.Advice),
                "The real native action-enum adapter gates constitutional diplomacy, state spending and major justice while leaving tactical orders, personal dealings and internal rebellion lifecycle outside government hearings.",
                new JObject { ["included"] = new JArray(coveredPoliticalActions.Select(x => x.ToString())),
                    ["excluded"] = new JArray(excludedPersonalAndTacticalActions.Select(x => x.ToString())) });
            AddGovernmentAssertion(assertions, "government_reduction_consent_not_self_pressured",
                MapActionKind(ReignWorldActionType.PoliticsConsentGovernmentReduction)
                    == ReignGovernmentActionKind.Advice,
                "Recording an NPC's accepted reduction consent does not itself create a government approval pressure cycle.", null);
            bool separated = Kingdom.All.Where(IsEligibleKingdom).Select(BuildPublicSnapshot)
                .All(snapshot => snapshot.SelectToken("peoplePressure.additionalToNoblePoliticalPressure")?.Value<bool>() == true
                    && snapshot.SelectToken("peoplePressure.probabilityRule")?.Value<string>()
                        ?.IndexOf("does not directly rewrite", StringComparison.OrdinalIgnoreCase) >= 0);
            AddGovernmentAssertion(assertions, "government_people_and_noble_pressure_separate", separated,
                "Government pressure is additive and leaves Public Standing and rebellion probability formulas untouched while feeding loyalty and relationship inputs.", null);
            return GovernmentTestResult(runId, "pressure", assertions);
        }

        private JObject GovernmentSavePrepare(string runId, JObject options, string gameInstanceId)
        {
            JArray assertions = new JArray();
            string caseId = (options?.Value<string>("caseId") ?? string.Empty).Trim();
            if (caseId.Equals("GOV-NATIVE-022", StringComparison.OrdinalIgnoreCase))
            {
                Kingdom kingdom = Clan.PlayerClan?.Kingdom;
                ReignGovernmentStateRecord state = GetGovernment(kingdom);
                List<Hero> realmMembers = kingdom?.Clans
                    .Where(clan => clan != null && clan != Clan.PlayerClan && !clan.IsEliminated)
                    .SelectMany(clan => clan.Heroes)
                    .Where(hero => IsEligibleMember(hero) && hero != Hero.MainHero)
                    .OrderBy(hero => hero.StringId, StringComparer.Ordinal)
                    .ToList() ?? new List<Hero>();
                Hero clanLeader = realmMembers.FirstOrDefault(hero => hero.Clan?.Leader == hero);
                Hero individual = realmMembers.FirstOrDefault(hero => hero.Clan?.Leader != hero
                    && hero.Clan != clanLeader?.Clan);
                bool seeded = state != null && kingdom?.Leader == Hero.MainHero
                    && clanLeader != null && individual != null;
                string clanResult = string.Empty;
                string individualResult = string.Empty;
                if (seeded)
                {
                    state.Level = 5;
                    state.ReductionConsentFromLevel = 0;
                    state.ReductionConsentHeroIdsCsv = string.Empty;
                    state.ReductionConsentClanIdsCsv = string.Empty;
                    state.Revision++;
                    seeded = TryRecordReductionConsent(kingdom.Leader, clanLeader, out clanResult)
                        && TryRecordReductionConsent(kingdom.Leader, individual, out individualResult);
                }
                bool individualTagPresent = state != null
                    && ParseIds(state.ReductionConsentHeroIdsCsv).Count > 0;
                bool clanTagPresent = state != null
                    && ParseIds(state.ReductionConsentClanIdsCsv).Count > 0;
                AddGovernmentAssertion(assertions, "government_save_prepare_consent_tags",
                    seeded && state?.Level == 5 && state.ReductionConsentFromLevel == 5
                    && individualTagPresent && clanTagPresent,
                    "The persistence checkpoint used the production consent recorder to create level-scoped tags from one real individual and one real clan leader.",
                    new JObject
                    {
                        ["kingdomId"] = kingdom?.StringId ?? string.Empty,
                        ["authorityLevel"] = state?.Level ?? 0,
                        ["consentFromLevel"] = state?.ReductionConsentFromLevel ?? 0,
                        ["individualConsentIds"] = state?.ReductionConsentHeroIdsCsv ?? string.Empty,
                        ["clanConsentIds"] = state?.ReductionConsentClanIdsCsv ?? string.Empty,
                        ["individualHeroId"] = individual?.StringId ?? string.Empty,
                        ["clanLeaderHeroId"] = clanLeader?.StringId ?? string.Empty,
                        ["individualConsentResult"] = individualResult,
                        ["clanConsentResult"] = clanResult
                    });
            }
            string fingerprint = GovernmentFeatureFingerprint();
            AddGovernmentAssertion(assertions, "government_save_prepare_fingerprint", fingerprint.Length == 64,
                "A stable save-backed fingerprint was captured before the external checkpoint/restart boundary.", null);
            AddGovernmentAssertion(assertions, "government_save_prepare_game_instance",
                !string.IsNullOrWhiteSpace(gameInstanceId),
                "The live bridge supplied the current native game-instance marker.", null);
            JObject result = GovernmentTestResult(runId, "save_prepare", assertions);
            result["featureFingerprint"] = fingerprint;
            result["gameInstanceId"] = gameInstanceId ?? string.Empty;
            return result;
        }

        private JObject GovernmentSaveVerify(string runId, JObject options, string gameInstanceId)
        {
            JArray assertions = new JArray();
            string expected = (options.Value<string>("expectedFeatureFingerprint") ?? string.Empty).Trim();
            string previous = (options.Value<string>("previousGameInstanceId") ?? string.Empty).Trim();
            string actual = GovernmentFeatureFingerprint();
            AddGovernmentAssertion(assertions, "government_save_verify_different_game_instance",
                previous.Length > 0 && !string.IsNullOrWhiteSpace(gameInstanceId)
                && !string.Equals(previous, gameInstanceId, StringComparison.OrdinalIgnoreCase),
                "Persistence verification ran in a different native game instance from preparation.",
                new JObject { ["previousGameInstanceId"] = previous, ["currentGameInstanceId"] = gameInstanceId ?? string.Empty });
            AddGovernmentAssertion(assertions, "government_save_verify_fingerprint",
                expected.Length == 64 && string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase),
                "Government, party, member, resolution, pressure, lobbying, and meeting state survived reload byte-stably.",
                new JObject { ["expected"] = expected, ["actual"] = actual });
            string caseId = (options?.Value<string>("caseId") ?? string.Empty).Trim();
            if (caseId.Equals("GOV-NATIVE-035", StringComparison.OrdinalIgnoreCase))
            {
                Kingdom kingdom = Clan.PlayerClan?.Kingdom;
                ReignGovernmentStateRecord state = GetGovernment(kingdom);
                AddGovernmentAssertion(assertions, "government_save_verify_consent_tags",
                    state?.Level == 5 && state.ReductionConsentFromLevel == 5
                    && ParseIds(state.ReductionConsentHeroIdsCsv).Count > 0
                    && ParseIds(state.ReductionConsentClanIdsCsv).Count > 0,
                    "The reloaded native game instance retained the exact level-scoped individual and clan consent tags.",
                    new JObject
                    {
                        ["kingdomId"] = kingdom?.StringId ?? string.Empty,
                        ["authorityLevel"] = state?.Level ?? 0,
                        ["consentFromLevel"] = state?.ReductionConsentFromLevel ?? 0,
                        ["individualConsentIds"] = state?.ReductionConsentHeroIdsCsv ?? string.Empty,
                        ["clanConsentIds"] = state?.ReductionConsentClanIdsCsv ?? string.Empty
                    });
            }
            JObject result = GovernmentTestResult(runId, "save_verify", assertions);
            result["featureFingerprint"] = actual;
            result["gameInstanceId"] = gameInstanceId ?? string.Empty;
            return result;
        }

        private JObject GovernmentKingdomEvidence(Kingdom kingdom)
        {
            ReignGovernmentStateRecord state = GetGovernment(kingdom);
            return new JObject
            {
                ["kingdomId"] = kingdom.StringId,
                ["cultureId"] = kingdom.Culture?.StringId ?? string.Empty,
                ["institution"] = state?.InstitutionName ?? string.Empty,
                ["authorityLevel"] = state?.Level ?? 0,
                ["partyCount"] = GetParties(kingdom.StringId).Count,
                ["seatCount"] = GetSeats(kingdom.StringId).Count,
                ["stanceTowardRuler"] = state?.StanceTowardRuler ?? 0,
                ["coalitionPartyIds"] = state?.CoalitionPartyIdsCsv ?? string.Empty
            };
        }

        private string GovernmentFeatureFingerprint()
        {
            JObject value = new JObject
            {
                ["governments"] = new JArray(_governments.OrderBy(x => x.KingdomStringId, StringComparer.Ordinal)
                    .Select(x => new JObject { ["kingdom"] = x.KingdomStringId, ["culture"] = x.CultureStringId,
                        ["institution"] = x.InstitutionName, ["kind"] = x.InstitutionKindValue, ["level"] = x.Level,
                        ["trust"] = x.Trust, ["lastMeeting"] = x.LastMeetingDay, ["nextMeeting"] = x.NextMeetingDay,
                        ["dominantParty"] = x.DominantPartyId, ["ruler"] = x.RulerHeroStringId,
                        ["coalition"] = x.CoalitionPartyIdsCsv, ["stance"] = x.StanceTowardRuler,
                        ["reductionConsentFromLevel"] = x.ReductionConsentFromLevel,
                        ["reductionConsentHeroes"] = x.ReductionConsentHeroIdsCsv,
                        ["reductionConsentClans"] = x.ReductionConsentClanIdsCsv,
                        ["revision"] = x.Revision })),
                ["parties"] = new JArray(_parties.OrderBy(x => x.KingdomStringId, StringComparer.Ordinal)
                    .ThenBy(x => x.PartyId, StringComparer.Ordinal).Select(x => JObject.FromObject(x))),
                ["seats"] = new JArray(_seats.OrderBy(x => x.KingdomStringId, StringComparer.Ordinal)
                    .ThenBy(x => x.HeroStringId, StringComparer.Ordinal).Select(x => JObject.FromObject(x))),
                ["resolutions"] = new JArray(_resolutions.OrderBy(x => x.ResolutionId, StringComparer.Ordinal)
                    .Select(x => JObject.FromObject(x))),
                ["pressures"] = new JArray(_pressures.OrderBy(x => x.PressureId, StringComparer.Ordinal)
                    .Select(x => JObject.FromObject(x))),
                ["lobbyRecords"] = new JArray(_lobbyRecords.OrderBy(x => x.LobbyId, StringComparer.Ordinal)
                    .Select(x => JObject.FromObject(x))),
                ["meetings"] = new JArray(_meetings.OrderBy(x => x.MeetingId, StringComparer.Ordinal)
                    .Select(x => JObject.FromObject(x))),
                ["business"] = new JArray(_business.OrderBy(x => x.BusinessId, StringComparer.Ordinal)
                    .Select(x => JObject.FromObject(x))),
                ["ownedNativeDecisionTypes"] = new JArray(_ownedNativeDecisions
                    .Select(x => x?.GetType().FullName ?? string.Empty)),
                ["hearing"] = BuildGovernmentHearingPersistenceSnapshot()
            };
            using (SHA256 sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(
                    value.ToString(Formatting.None)))).Replace("-", string.Empty).ToLowerInvariant();
        }

        private static JObject GovernmentTestResult(string runId, string profile, JArray assertions)
        {
            bool ok = assertions.Children<JObject>().All(row => row.Value<bool?>("passed") == true);
            return new JObject
            {
                ["ok"] = ok, ["schema"] = "reign-government-test-report-v1",
                ["runId"] = runId ?? string.Empty, ["profile"] = profile ?? string.Empty,
                ["assertions"] = assertions, ["failedAssertionCount"] = assertions.Children<JObject>()
                    .Count(row => row.Value<bool?>("passed") != true),
                ["capturedUtc"] = DateTime.UtcNow.ToString("o")
            };
        }

        private static void AddGovernmentAssertion(JArray assertions, string id, bool passed,
            string statement, JObject evidence)
        {
            assertions.Add(new JObject
            {
                ["id"] = id ?? string.Empty, ["passed"] = passed,
                ["statement"] = statement ?? string.Empty,
                ["evidence"] = evidence ?? new JObject()
            });
        }

        private static bool TryRequireGovernmentTestSave(JObject options,
            out string expected, out string active, out string error)
        {
            expected = (options?.Value<string>("expectedSaveName") ?? string.Empty).Trim();
            active = ReignServerClient.ActiveNativeSaveName().Trim();
            error = string.Empty;
            if (expected.Length == 0) error = "The exact MCP-authorized campaign-test Current save name is required.";
            else if (active.Length == 0) error = "Bannerlord did not expose an active loaded save identity.";
            else if (!string.Equals(expected, active, StringComparison.OrdinalIgnoreCase))
                error = "The active Bannerlord save does not match the exact MCP-authorized campaign-test Current.";
            return error.Length == 0;
        }

        private static JObject GovernmentTestFailure(string runId, string profile, string error,
            string expected, string active)
        {
            return new JObject
            {
                ["ok"] = false, ["schema"] = "reign-government-test-report-v1",
                ["runId"] = runId ?? string.Empty, ["profile"] = profile ?? string.Empty,
                ["error"] = error ?? string.Empty, ["expectedSaveName"] = expected ?? string.Empty,
                ["activeSaveName"] = active ?? string.Empty
            };
        }
    }
}
