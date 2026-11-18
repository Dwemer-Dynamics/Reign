using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ReignBeta.Court.WarCouncil;
using ReignBeta.Shared.WarCouncil;
using ReignBeta.UI.ViewModels;
using ReignBeta.World;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Party.PartyComponents;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using ReignBeta.Characters.Integration;
using ReignBeta.Platform.Integration;
using ReignBeta.Platform.Settings;
using ReignBeta.WorldSimulation.CampaignCommands;
using ReignBeta.WorldSimulation.Integration;
using ReignBeta.WorldSimulation.World;
#if !REIGN_EXCLUDE_COURT
using ReignBeta.Court.UI;
#endif
using ReignBeta.Platform.Runtime;

namespace ReignBeta.WorldSimulation.Campaign
{
    public static partial class ReignLiveInteractionTestHost
    {
        private static JObject RunCampaignCommandNativeProfile(string profile, int seed, string phase,
            string certificationRunId, bool restartVerified, string restartReceipt)
        {
            if (MobileParty.MainParty == null || ReignServices.CampaignCommand == null)
                return new JObject { ["ok"] = false, ["fatal"] = true,
                    ["failureClass"] = "bridge_desynchronization", ["error"] = "Campaign command runtime is unavailable." };
            string runScope = new string((certificationRunId ?? string.Empty).Where(ch =>
                    char.IsLetterOrDigit(ch) || ch == '-' || ch == '_').Take(64).ToArray());
            if (string.IsNullOrWhiteSpace(runScope)) runScope = "unknown";
            if (profile == "native_primitives") return RunCampaignCommandNativeCapabilityMatrix(seed, runScope);
            MobileParty commanderParty = MobileParty.All.FirstOrDefault(x => x != null && x.IsActive
                && !x.IsMainParty && x.LeaderHero != null && !x.LeaderHero.IsPrisoner
                && x.MapEvent == null && x.BesiegedSettlement == null);
            if (commanderParty == null)
                return new JObject { ["ok"] = false, ["error"] = "No eligible NPC-led party is available." };

            Settlement friendly = Settlement.All.Where(x => x != null && !x.IsHideout
                    && x.MapFaction == commanderParty.MapFaction && !x.IsUnderSiege)
                .OrderBy(x => commanderParty.GetPosition2D.DistanceSquared(x.GetPosition2D)).FirstOrDefault();
            Settlement friendlyVillage = Settlement.All.Where(x => x != null && x.IsVillage
                    && x.MapFaction == commanderParty.MapFaction && !x.IsUnderSiege)
                .OrderBy(x => commanderParty.GetPosition2D.DistanceSquared(x.GetPosition2D)).FirstOrDefault();
            string objective = profile == "correspondence" ? "timed_hold"
                : profile == "fault_recovery" ? "patrol" : "move";
            JObject terms = new JObject
            {
                ["objective"] = objective, ["durationHours"] = 2,
                ["issuerHeroStringId"] = Hero.MainHero?.StringId ?? string.Empty,
                ["region"] = profile == "fault_recovery" ? "southern kingdom" : string.Empty
            };
            if (profile == "composite_orders")
            {
                terms.Remove("objective");
                terms.Remove("durationHours");
                terms["steps"] = new JArray
                {
                    new JObject { ["objective"] = "recruit_resupply",
                        ["targetSettlementStringId"] = friendly?.StringId ?? string.Empty,
                        ["minimumTroops"] = Math.Max(1, commanderParty.MemberRoster.TotalRegulars),
                        ["minimumFoodDays"] = 0 },
                    new JObject { ["objective"] = "timed_hold", ["durationHours"] = 2 },
                    new JObject { ["objective"] = "patrol",
                        ["targetSettlementStringId"] = friendly?.StringId ?? string.Empty }
                };
            }
            ReignWorldActionRecord issue = new ReignWorldActionRecord
            {
                ActionId = "campaign_command_test_" + runScope + "_" + profile + "_" + seed,
                Type = ReignWorldActionType.RegularIssueCampaignOrder,
                Source = "dialogue_auto_commit_certification",
                ActorHeroStringId = commanderParty.LeaderHero.StringId,
                TargetSettlementStringId = friendly?.StringId ?? string.Empty,
                TermsJson = terms.ToString(Formatting.None),
                AuthorizationMode = "accepted_recommendation",
                AcceptedByHeroStringId = commanderParty.LeaderHero.StringId,
                RequiresAcceptance = false,
                Reason = "Disposable Campaign Command certification case."
            };
            if (profile == "save_roundtrip" && phase == "verify")
            {
                ReignCampaignOrderRecord persisted = ReignServices.CampaignCommand.Orders
                    .FirstOrDefault(x => x.OrderId == issue.ActionId);
                bool payloadRoundTrip = persisted != null
                    && persisted.CommanderHeroStringId == commanderParty.LeaderHero.StringId
                    && ReignServices.CampaignCommand.VerifySaveRoundTripForCertification();
                var reloadChecks = new JArray
                {
                    new JObject { ["id"] = "save_payload_roundtrip", ["passed"] = payloadRoundTrip },
                    new JObject { ["id"] = "native_checkpoint_observed", ["passed"] = restartVerified },
                    new JObject { ["id"] = "different_game_instance_observed",
                        ["passed"] = restartVerified && !string.IsNullOrWhiteSpace(restartReceipt) }
                };
                bool cancelledPersisted = persisted == null || persisted.IsTerminal;
                if (persisted != null && !persisted.IsTerminal)
                {
                    ReignActionResult cancelPersisted = ReignServices.CampaignCommand.ExecuteControlAction(
                        new ReignWorldActionRecord
                        {
                            Type = ReignWorldActionType.RegularCancelCampaignOrder,
                            Source = "campaign_command_certification_reload_cleanup",
                            ActorHeroStringId = persisted.CommanderHeroStringId,
                            Reason = "Certification reload cleanup.",
                            TermsJson = new JObject { ["orderId"] = persisted.OrderId }.ToString(Formatting.None)
                        });
                    cancelledPersisted = cancelPersisted.Success && persisted.Status == "cancelled";
                }
                reloadChecks.Add(new JObject { ["id"] = "safe_cancellation", ["passed"] = cancelledPersisted });
                int reloadPassed = reloadChecks.OfType<JObject>().Count(x => x.Value<bool>("passed"));
                JObject reloadMetrics = new JObject
                {
                    ["checks"] = reloadChecks.Count, ["passed"] = reloadPassed,
                    ["save_payload_roundtrip"] = payloadRoundTrip,
                    ["native_checkpoint_observed"] = restartVerified,
                    ["different_game_instance_observed"] = restartVerified
                        && !string.IsNullOrWhiteSpace(restartReceipt),
                    ["safe_cancellation"] = cancelledPersisted,
                    ["restartReceipt"] = restartReceipt
                };
                return new JObject
                {
                    ["ok"] = reloadPassed == reloadChecks.Count,
                    ["caseCount"] = reloadChecks.Count, ["passedCaseCount"] = reloadPassed,
                    ["checks"] = reloadChecks, ["metrics"] = reloadMetrics,
                    ["orderStatus"] = persisted?.Status ?? "missing"
                };
            }
            ReignActionResult issued = ReignServices.CampaignCommand.ExecuteControlAction(issue);
            ReignCampaignOrderRecord order = ReignServices.CampaignCommand.Orders
                .FirstOrDefault(x => x.OrderId == issue.ActionId);
            bool mainUnaffected = !commanderParty.IsMainParty && MobileParty.MainParty != commanderParty;
            bool durable = issued.Success && order != null && order.CommanderHeroStringId == commanderParty.LeaderHero.StringId;
            JArray checks = new JArray
            {
                new JObject { ["id"] = "issued", ["passed"] = issued.Success },
                new JObject { ["id"] = "durable_order", ["passed"] = durable },
                new JObject { ["id"] = "main_party_untouched", ["passed"] = mainUnaffected }
            };
            if (profile == "composite_orders" && order != null && !order.IsTerminal)
            {
                bool planShape = order.PlanVersion == 2 && order.Steps?.Count == 3
                    && order.Steps.Select(x => x.Objective).SequenceEqual(
                        new[] { "recruit_resupply", "timed_hold", "patrol" })
                    && !string.IsNullOrWhiteSpace(order.PlanHash);
                checks.Add(new JObject { ["id"] = "multistage_plan_shape", ["passed"] = planShape });
                int transitions = 0;
                while (!order.IsTerminal && order.Objective != "patrol" && transitions < 3)
                {
                    if (!ReignServices.CampaignCommand.CompleteCurrentStepForCertification(
                        order.OrderId, "Deterministic multi-stage transition fixture.")) break;
                    transitions++;
                }
                checks.Add(new JObject { ["id"] = "ordered_step_transitions",
                    ["passed"] = !order.IsTerminal && order.Objective == "patrol"
                        && order.CurrentStepIndex == 2 && order.CompletedStepCount == 2 });
                checks.Add(new JObject { ["id"] = "indefinite_patrol_semantics",
                    ["passed"] = order.Objective == "patrol" && order.DeadlineDay <= 0d });

                string[][] distinctFlows =
                {
                    new[] { "move", "timed_hold" },
                    new[] { "move", "scout_report" },
                    new[] { "withdraw", "return_home" }
                };
                List<MobileParty> flowCommanders = MobileParty.All.Where(x => x != null && x.IsActive
                        && !x.IsMainParty && x != commanderParty && x.LeaderHero != null
                        && !x.LeaderHero.IsPrisoner && x.MapEvent == null && x.BesiegedSettlement == null)
                    .Take(distinctFlows.Length).ToList();
                int distinctFlowReceipts = 0;
                for (int flowIndex = 0; flowIndex < distinctFlows.Length; flowIndex++)
                {
                    if (flowIndex >= flowCommanders.Count) break;
                    MobileParty flowCommander = flowCommanders[flowIndex];
                    Settlement flowTarget = Settlement.All.Where(x => x != null && !x.IsHideout
                            && x.MapFaction == flowCommander.MapFaction && !x.IsUnderSiege)
                        .OrderBy(x => flowCommander.GetPosition2D.DistanceSquared(x.GetPosition2D)).FirstOrDefault();
                    JArray flowSteps = new JArray();
                    foreach (string flowObjective in distinctFlows[flowIndex])
                    {
                        JObject flowStep = new JObject { ["objective"] = flowObjective };
                        if (flowObjective == "move" || flowObjective == "scout_report")
                            flowStep["targetSettlementStringId"] = flowTarget?.StringId ?? string.Empty;
                        if (flowObjective == "timed_hold" || flowObjective == "scout_report")
                            flowStep["durationHours"] = 2;
                        flowSteps.Add(flowStep);
                    }
                    ReignWorldActionRecord flowIssue = new ReignWorldActionRecord
                    {
                        ActionId = issue.ActionId + "_flow_" + flowIndex,
                        Type = ReignWorldActionType.RegularIssueCampaignOrder,
                        Source = "dialogue_auto_commit_certification",
                        ActorHeroStringId = flowCommander.LeaderHero.StringId,
                        TargetSettlementStringId = flowTarget?.StringId ?? string.Empty,
                        TermsJson = new JObject { ["steps"] = flowSteps,
                            ["issuerHeroStringId"] = Hero.MainHero?.StringId ?? string.Empty }
                            .ToString(Formatting.None),
                        AuthorizationMode = "accepted_recommendation",
                        AcceptedByHeroStringId = flowCommander.LeaderHero.StringId,
                        RequiresAcceptance = false,
                        Reason = "Materially distinct composite certification flow."
                    };
                    ReignActionResult flowIssued = ReignServices.CampaignCommand.ExecuteControlAction(flowIssue);
                    ReignCampaignOrderRecord flowOrder = ReignServices.CampaignCommand.Orders
                        .FirstOrDefault(x => x.OrderId == flowIssue.ActionId);
                    bool flowShape = flowIssued.Success && flowOrder?.PlanVersion == 2
                        && flowOrder.Steps.Select(x => x.Objective)
                            .SequenceEqual(distinctFlows[flowIndex], StringComparer.Ordinal)
                        && !string.IsNullOrWhiteSpace(flowOrder.PlanHash);
                    if (flowShape) distinctFlowReceipts++;
                    checks.Add(new JObject { ["id"] = "distinct_composite_flow_" + flowIndex,
                        ["passed"] = flowShape, ["objectives"] = new JArray(distinctFlows[flowIndex]) });
                    if (flowOrder != null && !flowOrder.IsTerminal)
                        ReignServices.CampaignCommand.ExecuteControlAction(new ReignWorldActionRecord
                        {
                            Type = ReignWorldActionType.RegularCancelCampaignOrder,
                            Source = "campaign_command_certification_cleanup",
                            ActorHeroStringId = flowCommander.LeaderHero.StringId,
                            TermsJson = new JObject { ["orderId"] = flowOrder.OrderId }.ToString(Formatting.None)
                        });
                }
                checks.Add(new JObject { ["id"] = "materially_distinct_composite_receipts",
                    ["passed"] = distinctFlowReceipts == distinctFlows.Length,
                    ["receiptCount"] = distinctFlowReceipts });

                Hero partyless = Hero.AllAliveHeroes.FirstOrDefault(x => x != null && x != Hero.MainHero
                    && x.IsLord && !x.IsChild && !x.IsPrisoner && x.Clan?.Kingdom != null
                    && x.PartyBelongedTo == null && x.Clan.Kingdom.Settlements.Any(s => !s.IsUnderSiege));
                ReignCampaignOrderRecord partylessOrder = null;
                if (partyless != null)
                {
                    Settlement spawn = partyless.Clan.Kingdom.Settlements.First(s => !s.IsUnderSiege);
                    ReignWorldActionRecord partylessIssue = new ReignWorldActionRecord
                    {
                        ActionId = issue.ActionId + "_partyless",
                        Type = ReignWorldActionType.RegularIssueCampaignOrder,
                        Source = "dialogue_auto_commit_certification",
                        ActorHeroStringId = partyless.StringId,
                        TargetSettlementStringId = spawn.StringId,
                        AuthorizationMode = "accepted_recommendation",
                        AcceptedByHeroStringId = partyless.StringId,
                        RequiresAcceptance = false,
                        Reason = "Morwyn-class partyless recruit then patrol regression.",
                        TermsJson = new JObject
                        {
                            ["steps"] = new JArray
                            {
                                new JObject { ["objective"] = "recruit_resupply",
                                    ["targetSettlementStringId"] = spawn.StringId,
                                    ["minimumTroops"] = 1, ["minimumFoodDays"] = 0 },
                                new JObject { ["objective"] = "patrol",
                                    ["targetSettlementStringId"] = spawn.StringId }
                            }
                        }.ToString(Formatting.None)
                    };
                    ReignActionResult partylessResult = ReignServices.CampaignCommand
                        .ExecuteControlAction(partylessIssue);
                    partylessOrder = ReignServices.CampaignCommand.Orders
                        .FirstOrDefault(x => x.OrderId == partylessIssue.ActionId);
                    checks.Add(new JObject { ["id"] = "partyless_recruit_patrol_regression",
                        ["passed"] = partylessResult.Success && partylessOrder?.Steps?.Count == 3
                            && partylessOrder.Steps[0].Objective == "establish_party"
                            && partyless.PartyBelongedTo?.LeaderHero == partyless });
                }
                else
                {
                    checks.Add(new JObject { ["id"] = "partyless_recruit_patrol_regression",
                        ["passed"] = false, ["error"] = "No eligible partyless lord exists in the disposable fixture." });
                }
                if (partylessOrder != null && !partylessOrder.IsTerminal)
                {
                    ReignServices.CampaignCommand.ExecuteControlAction(new ReignWorldActionRecord
                    {
                        Type = ReignWorldActionType.RegularCancelCampaignOrder,
                        ActorHeroStringId = partyless.StringId,
                        Source = "campaign_command_certification_cleanup",
                        TermsJson = new JObject { ["orderId"] = partylessOrder.OrderId }.ToString(Formatting.None)
                    });
                }
            }
            if (profile == "correspondence" && order != null && !order.IsTerminal)
            {
                bool requested = ReignServices.CampaignCommand.RequestGuidanceForCertification(
                    order.OrderId, "Enemy strength changed; urgent sovereign guidance is required.");
                bool twelveHours = requested && Math.Abs((order.GuidanceDeadlineDay - CampaignTime.Now.ToDays) * 24d - 12d) < 0.1d;
                checks.Add(new JObject { ["id"] = "urgent_report_queued", ["passed"] = requested });
                checks.Add(new JObject { ["id"] = "twelve_hour_deadline", ["passed"] = twelveHours });
                ReignActionResult response = ReignServices.CampaignCommand.ExecuteControlAction(
                    new ReignWorldActionRecord
                    {
                        Type = ReignWorldActionType.RegularRespondToOrderReport,
                        Source = "campaign_command_certification",
                        ActorHeroStringId = commanderParty.LeaderHero.StringId,
                        Reason = "Certification response.",
                        TermsJson = new JObject { ["orderId"] = order.OrderId,
                            ["response"] = "continue" }.ToString(Formatting.None)
                    });
                checks.Add(new JObject { ["id"] = "response_delivered", ["passed"] = response.Success && !order.AwaitingGuidance });
                bool requestedAgain = ReignServices.CampaignCommand.RequestGuidanceForCertification(
                    order.OrderId, "No-response branch fixture.");
                bool expired = requestedAgain && ReignServices.CampaignCommand
                    .ExpireGuidanceForCertification(order.OrderId);
                checks.Add(new JObject { ["id"] = "no_response_branch", ["passed"] = expired });
            }
            if (profile == "fault_recovery" && order != null && !order.IsTerminal)
            {
                ReignActionResult duplicate = ReignServices.CampaignCommand.ExecuteControlAction(issue);
                checks.Add(new JObject { ["id"] = "duplicate_suppressed",
                    ["passed"] = duplicate.ResultCode == "campaign_order_duplicate_suppressed" });
                int beforeReassert = order.NativeReassertions;
                commanderParty.SetMoveModeHold();
                bool reviewed = ReignServices.CampaignCommand.ReviewForCertification(order.OrderId);
                checks.Add(new JObject { ["id"] = "native_ai_reasserted",
                    ["passed"] = reviewed && order.NativeReassertions > beforeReassert });
                ReignWorldActionRecord obsolete = new ReignWorldActionRecord
                {
                    ActionId = issue.ActionId + "_obsolete",
                    Type = ReignWorldActionType.RegularIssueCampaignOrder,
                    Source = "dialogue_auto_commit_certification",
                    ActorHeroStringId = commanderParty.LeaderHero.StringId,
                    TargetSettlementStringId = (friendlyVillage ?? friendly)?.StringId ?? string.Empty,
                    Reason = "Changed-world invalid-target fixture.",
                    AuthorizationMode = "accepted_recommendation",
                    AcceptedByHeroStringId = commanderParty.LeaderHero.StringId,
                    RequiresAcceptance = false,
                    TermsJson = new JObject { ["objective"] = "raid",
                        ["issuerHeroStringId"] = Hero.MainHero?.StringId ?? string.Empty }.ToString(Formatting.None)
                };
                ReignActionResult obsoleteResult = ReignServices.CampaignCommand.ExecuteControlAction(obsolete);
                ReignCampaignOrderRecord obsoleteOrder = ReignServices.CampaignCommand.Orders
                    .FirstOrDefault(x => x.OrderId == obsolete.ActionId);
                checks.Add(new JObject { ["id"] = "obsolete_target_classified",
                    ["passed"] = obsoleteOrder?.Status == "failed"
                        && (obsoleteOrder.LastFailure.IndexOf("hostile", StringComparison.OrdinalIgnoreCase) >= 0
                            || obsoleteOrder.LastFailure.IndexOf("village", StringComparison.OrdinalIgnoreCase) >= 0),
                    ["classification"] = obsoleteOrder?.LastFailure ?? obsoleteResult?.Message ?? string.Empty });
            }
            if (profile == "save_roundtrip")
            {
                checks.Add(new JObject { ["id"] = "save_payload_roundtrip",
                    ["passed"] = ReignServices.CampaignCommand.VerifySaveRoundTripForCertification() });
            }

            bool cancelled = true;
            if (order != null && !order.IsTerminal && !(profile == "save_roundtrip" && phase == "prepare"))
            {
                ReignActionResult cancel = ReignServices.CampaignCommand.ExecuteControlAction(
                    new ReignWorldActionRecord
                    {
                        Type = ReignWorldActionType.RegularCancelCampaignOrder,
                        Source = "campaign_command_certification",
                        ActorHeroStringId = commanderParty.LeaderHero.StringId,
                        Reason = "Certification cleanup.",
                        TermsJson = new JObject { ["orderId"] = order.OrderId }.ToString(Formatting.None)
                    });
                cancelled = cancel.Success && order.Status == "cancelled";
            }
            checks.Add(new JObject { ["id"] = "safe_cancellation", ["passed"] = cancelled });
            int passedChecks = checks.OfType<JObject>().Count(x => x.Value<bool>("passed"));
            JObject profileMetrics = new JObject { ["checks"] = checks.Count, ["passed"] = passedChecks };
            foreach (JObject check in checks.OfType<JObject>())
                profileMetrics[check.Value<string>("id") ?? "unknown"] = check.Value<bool>("passed");
            return new JObject
            {
                ["ok"] = passedChecks == checks.Count, ["caseCount"] = checks.Count,
                ["passedCaseCount"] = passedChecks, ["checks"] = checks,
                ["issued"] = issued.Success, ["message"] = issued.Message,
                ["orderStatus"] = order?.Status ?? "missing", ["mainPartyUnaffected"] = mainUnaffected,
                ["nativeSignature"] = order?.LastNativeSignature ?? string.Empty,
                ["metrics"] = profileMetrics
            };
        }

        private static JObject RunCampaignCommandNativeCapabilityMatrix(int seed, string runScope)
        {
            List<MobileParty> eligible = MobileParty.All.Where(x => x != null && x.IsActive
                && !x.IsMainParty && x.LeaderHero != null && !x.LeaderHero.IsPrisoner
                && x.MapEvent == null && x.BesiegedSettlement == null).ToList();
            List<MobileParty> commanderCandidates = eligible.Where(x => x.MapFaction is Kingdom
                && eligible.Any(y => y != x && y.MapFaction == x.MapFaction)).ToList();
            MobileParty commander = commanderCandidates.OrderByDescending(x =>
                (eligible.Any(y => y != x && FactionManager.IsAtWarAgainstFaction(x.MapFaction,
                    y.MapFaction)) ? 100 : 0)
                + (Settlement.All.Any(s => s != null && s.IsFortification && s.MapFaction == x.MapFaction
                    && !s.IsUnderSiege && s != MobileParty.MainParty?.CurrentSettlement) ? 10 : 0)
                + (Settlement.All.Any(s => s != null && s.IsFortification && s.MapFaction == x.MapFaction
                    && s.IsUnderSiege) ? 1000 : 0)).FirstOrDefault();
            if (commander == null)
                return new JObject { ["ok"] = false, ["caseCount"] = 18, ["passedCaseCount"] = 0,
                    ["error"] = "No kingdom has two eligible NPC-led parties for the native capability matrix." };

            Hero commanderHero = commander.LeaderHero;
            MobileParty auxiliary = eligible.First(x => x != commander && x.MapFaction == commander.MapFaction);
            Settlement friendly = Settlement.All.Where(x => x != null && !x.IsHideout
                    && x.MapFaction == commander.MapFaction && !x.IsUnderSiege)
                .OrderByDescending(x => commander.GetPosition2D.DistanceSquared(x.GetPosition2D)).FirstOrDefault();
            Settlement hostileVillage = Settlement.All.Where(x => x != null && x.IsVillage
                    && FactionManager.IsAtWarAgainstFaction(commander.MapFaction, x.MapFaction))
                .OrderBy(x => commander.GetPosition2D.DistanceSquared(x.GetPosition2D)).FirstOrDefault();
            Settlement hostileFortification = Settlement.All.Where(x => x != null && x.IsFortification
                    && FactionManager.IsAtWarAgainstFaction(commander.MapFaction, x.MapFaction))
                .OrderBy(x => commander.GetPosition2D.DistanceSquared(x.GetPosition2D)).FirstOrDefault();
            Settlement besiegedFriendly = Settlement.All.FirstOrDefault(x => x != null && x.IsFortification
                && x.MapFaction == commander.MapFaction && x.IsUnderSiege && x.SiegeEvent?.BesiegerCamp?.LeaderParty != null);
            MobileParty hostileParty = eligible.Where(x => x != commander
                    && FactionManager.IsAtWarAgainstFaction(commander.MapFaction, x.MapFaction))
                .OrderBy(x => commander.GetPosition2D.DistanceSquared(x.GetPosition2D)).FirstOrDefault();

            MobileParty armyCommander = null;
            MobileParty armyAuxiliary = null;
            Settlement armyFriendly = null;
            List<MobileParty> createdArmyFixtureParties = new List<MobileParty>();
            Clan provisionedInfluenceClan = null;
            float originalInfluence = 0f;
            List<MobileParty> armyCandidates = commanderCandidates.Where(x => x.Army == null
                    && x.LeaderHero?.Clan != null
                    && eligible.Any(y => y != x && y.Army == null && y.MapFaction == x.MapFaction))
                .OrderByDescending(x => x == commander ? 100000 : 0)
                .ThenByDescending(x => x.MapFaction == Clan.PlayerClan?.Kingdom ? 10000 : 0)
                .ThenByDescending(x => x.MemberRoster?.TotalManCount ?? 0).ToList();
            foreach (MobileParty candidate in armyCandidates)
            {
                if (!TaleWorlds.CampaignSystem.Campaign.Current.Models.ArmyManagementCalculationModel.CanLordCreateArmy(
                    candidate, out MBList<MobileParty> _)) continue;
                armyCommander = candidate;
                provisionedInfluenceClan = candidate.LeaderHero.Clan;
                originalInfluence = provisionedInfluenceClan.Influence;
                break;
            }
            if (armyCommander == null)
            {
                foreach (MobileParty candidate in armyCandidates)
                {
                    Clan clan = candidate.LeaderHero.Clan;
                    float before = clan.Influence;
                    if (before < 5000f) ChangeClanInfluenceAction.Apply(clan, 5000f - before);
                    if (TaleWorlds.CampaignSystem.Campaign.Current.Models.ArmyManagementCalculationModel.CanLordCreateArmy(
                        candidate, out MBList<MobileParty> _))
                    {
                        armyCommander = candidate;
                        provisionedInfluenceClan = clan;
                        originalInfluence = before;
                        break;
                    }
                    if (Math.Abs(clan.Influence - before) > 0.01f)
                        ChangeClanInfluenceAction.Apply(clan, before - clan.Influence);
                }
            }
            if (armyCommander == null)
            {
                CharacterObject fixtureTroop = TaleWorlds.ObjectSystem.MBObjectManager.Instance
                    .GetObject<CharacterObject>("imperial_elite_cataphract");
                foreach (Kingdom kingdom in Kingdom.All.Where(x => x != null && !x.IsEliminated
                    && x.FactionsAtWarWith.Any(faction => faction?.Fiefs?.Any() == true)))
                {
                    Settlement spawn = Settlement.All.FirstOrDefault(x => x != null && x.IsFortification
                        && x.MapFaction == kingdom && !x.IsUnderSiege);
                    Hero fixtureLeader = Hero.AllAliveHeroes.FirstOrDefault(x => x != null
                        && x != Hero.MainHero && !x.IsPrisoner && x.PartyBelongedTo == null
                        && x.Clan?.Kingdom == kingdom && x.Clan.Leader == x
                        && !x.Clan.IsUnderMercenaryService && x.CanLeadParty());
                    List<Hero> fixtureMembers = Hero.AllAliveHeroes.Where(x => x != null
                            && x != Hero.MainHero && x != fixtureLeader && x != kingdom.Leader
                            && !x.IsPrisoner && x.PartyBelongedTo == null && x.Clan?.Kingdom == kingdom
                            && x.CanLeadParty()).Take(4).ToList();
                    if (spawn == null || fixtureLeader == null || fixtureMembers.Count == 0
                        || fixtureTroop == null || DefaultItems.Grain == null) continue;
                    try
                    {
                        List<Hero> fixtureHeroes = new[] { fixtureLeader }.Concat(fixtureMembers).ToList();
                        for (int fixtureIndex = 0; fixtureIndex < fixtureHeroes.Count; fixtureIndex++)
                        {
                            Hero fixtureHero = fixtureHeroes[fixtureIndex];
                            MobileParty fixtureParty = LordPartyComponent.CreateLordParty(
                                fixtureHero.StringId + "_reign_campaign_command_army_" + seed + "_" + fixtureIndex,
                                fixtureHero, spawn.GatePosition, 2f, spawn, fixtureHero);
                            fixtureParty.MemberRoster.AddToCounts(fixtureTroop, 180);
                            fixtureParty.ItemRoster.AddToCounts(DefaultItems.Grain, 300);
                            fixtureParty.SetMoveModeHold();
                            createdArmyFixtureParties.Add(fixtureParty);
                        }
                        MobileParty fixtureCandidate = createdArmyFixtureParties.FirstOrDefault(x =>
                            x?.LeaderHero == fixtureLeader && x.Army == null);
                        if (fixtureCandidate == null)
                            throw new InvalidOperationException("The native army leader fixture was not created.");
                        provisionedInfluenceClan = fixtureLeader.Clan;
                        originalInfluence = provisionedInfluenceClan.Influence;
                        if (originalInfluence < 5000f)
                            ChangeClanInfluenceAction.Apply(provisionedInfluenceClan, 5000f - originalInfluence);
                        if (TaleWorlds.CampaignSystem.Campaign.Current.Models.ArmyManagementCalculationModel
                            .CanLordCreateArmy(fixtureCandidate, out MBList<MobileParty> fixturePossibleMembers))
                        {
                            armyCommander = fixtureCandidate;
                            armyAuxiliary = fixturePossibleMembers.FirstOrDefault(x => x != null
                                && x != fixtureCandidate && x.Army == null);
                            armyFriendly = spawn;
                            break;
                        }
                        if (Math.Abs(provisionedInfluenceClan.Influence - originalInfluence) > 0.01f)
                            ChangeClanInfluenceAction.Apply(provisionedInfluenceClan,
                                originalInfluence - provisionedInfluenceClan.Influence);
                        provisionedInfluenceClan = null;
                    }
                    catch (Exception ex)
                    {
                        ReignLog.Warn("Campaign Command native army ecology fixture failed: " + ex.Message);
                    }
                    if (armyCommander == null && provisionedInfluenceClan != null
                        && Math.Abs(provisionedInfluenceClan.Influence - originalInfluence) > 0.01f)
                        ChangeClanInfluenceAction.Apply(provisionedInfluenceClan,
                            originalInfluence - provisionedInfluenceClan.Influence);
                    if (armyCommander == null) provisionedInfluenceClan = null;
                    foreach (MobileParty fixtureParty in createdArmyFixtureParties.ToList())
                    {
                        try { if (fixtureParty?.IsActive == true) DestroyPartyAction.Apply(null, fixtureParty); }
                        catch (Exception ex)
                        {
                            ReignLog.Warn("Campaign Command native army ecology rollback failed: " + ex.Message);
                        }
                    }
                    createdArmyFixtureParties.Clear();
                }
            }
            if (armyCommander != null)
            {
                armyAuxiliary = armyAuxiliary ?? eligible.FirstOrDefault(x => x != armyCommander && x.Army == null
                    && x.MapFaction == armyCommander.MapFaction);
                armyFriendly = armyFriendly ?? Settlement.All.Where(x => x != null && x.IsFortification
                            && x.MapFaction == armyCommander.MapFaction && !x.IsUnderSiege)
                        .OrderByDescending(x => armyCommander.GetPosition2D.DistanceSquared(x.GetPosition2D))
                        .FirstOrDefault();
            }
            Hero armyCommanderHero = armyCommander?.LeaderHero;
            Hero armyAuxiliaryHero = armyAuxiliary?.LeaderHero;

            TaleWorlds.CampaignSystem.Siege.SiegeEvent certificationSiege = null;
            if (besiegedFriendly == null && hostileParty != null)
            {
                Settlement siegeFixture = Settlement.All.Where(x => x != null && x.IsFortification
                        && x.MapFaction == commander.MapFaction && !x.IsUnderSiege
                        && x != MobileParty.MainParty?.CurrentSettlement)
                    .OrderBy(x => hostileParty.GetPosition2D.DistanceSquared(x.GetPosition2D)).FirstOrDefault();
                if (siegeFixture != null)
                {
                    try
                    {
                        certificationSiege = TaleWorlds.CampaignSystem.Campaign.Current.SiegeEventManager.StartSiegeEvent(
                            siegeFixture, hostileParty);
                        besiegedFriendly = siegeFixture;
                    }
                    catch (Exception ex)
                    {
                        ReignLog.Warn("Campaign Command siege fixture could not be created: " + ex.Message);
                    }
                }
            }

            JArray receipts = new JArray();
            int passed = 0;
            try
            {
                foreach (ReignCampaignCommandCapability capability in ReignCampaignCommandCapabilityRegistry.All)
                {
                    MobileParty actor = commander;
                    Settlement settlement = null;
                    MobileParty targetParty = null;
                    JObject terms = new JObject
                    {
                        ["objective"] = capability.Id,
                        ["issuerHeroStringId"] = Hero.MainHero?.StringId ?? string.Empty,
                        ["durationHours"] = 2
                    };
                    string prerequisite = string.Empty;
                    switch (capability.Id)
                    {
                    case "establish_party": break;
                    case "move":
                    case "patrol":
                    case "scout_report":
                    case "defend":
                    case "return_home": settlement = friendly; break;
                    case "timed_hold": break;
                    case "hold_position":
                        terms["positionX"] = Math.Max(0f, Math.Min(1040f, commander.Position.X + 4f));
                        terms["positionY"] = Math.Max(0f, Math.Min(1040f, commander.Position.Y + 4f));
                        terms["durationHours"] = 0;
                        break;
                    case "escort": targetParty = auxiliary; break;
                    case "recruit_resupply":
                        settlement = friendly;
                        terms["minimumTroops"] = Math.Min(commander.Party.PartySizeLimit,
                            Math.Max(1, commander.MemberRoster.TotalManCount + 1));
                        break;
                    case "form_army":
                        actor = armyCommander;
                        settlement = armyFriendly;
                        if (actor == null || armyAuxiliary == null || settlement == null)
                            prerequisite = "no native-eligible independent army fixture exists";
                        break;
                    case "join_army":
                        actor = armyAuxiliary;
                        targetParty = armyCommander?.Army?.LeaderParty == armyCommander ? armyCommander : null;
                        if (targetParty == null) prerequisite = "form_army did not produce a native army";
                        break;
                    case "leave_army":
                        actor = armyAuxiliary;
                        terms["armyDetachmentAccepted"] = true;
                        if (actor?.Army == null) prerequisite = "join_army did not produce native membership";
                        break;
                    case "disband_army":
                        actor = armyCommander;
                        if (actor?.Army?.LeaderParty != actor) prerequisite = "the harness-created army is unavailable";
                        break;
                    case "raid": settlement = hostileVillage;
                        if (settlement == null) prerequisite = "no hostile village exists"; break;
                    case "besiege_capture": settlement = hostileFortification;
                        if (settlement == null) prerequisite = "no hostile fortification exists"; break;
                    case "relieve_siege": settlement = besiegedFriendly;
                        if (settlement == null) prerequisite = "no friendly active siege exists"; break;
                    case "hunt_enemy_parties": terms["region"] = "frontier";
                        if (hostileParty == null) prerequisite = "no hostile active party exists"; break;
                    case "engage_party": targetParty = hostileParty;
                        if (targetParty == null) prerequisite = "no hostile active party exists"; break;
                    case "withdraw": settlement = friendly; break;
                    }
                    if (friendly == null && capability.RequiresSettlement && settlement == null
                        && string.IsNullOrWhiteSpace(prerequisite)) prerequisite = "no friendly settlement anchor exists";

                    Hero actorHero = actor?.LeaderHero;
                    if (actorHero == null && ReferenceEquals(actor, commander)) actorHero = commanderHero;
                    if (actorHero == null && ReferenceEquals(actor, armyCommander)) actorHero = armyCommanderHero;
                    if (actorHero == null && ReferenceEquals(actor, armyAuxiliary)) actorHero = armyAuxiliaryHero;
                    if (actorHero == null && string.IsNullOrWhiteSpace(prerequisite))
                        prerequisite = "selected native actor no longer has a resolvable leader";

                    string actionId = "campaign_command_native_" + runScope + "_" + seed + "_" + capability.Id;
                    ReignActionResult issued = null;
                    ReignCampaignOrderRecord order = null;
                    if (string.IsNullOrWhiteSpace(prerequisite))
                    {
                        if (targetParty != null) terms["targetPartyId"] = targetParty.StringId;
                        ReignWorldActionRecord action = new ReignWorldActionRecord
                        {
                            ActionId = actionId,
                            Type = ReignWorldActionType.RegularIssueCampaignOrder,
                            Source = "dialogue_auto_commit_certification",
                            ActorHeroStringId = actorHero.StringId,
                            TargetHeroStringId = targetParty?.LeaderHero?.StringId ?? string.Empty,
                            TargetSettlementStringId = settlement?.StringId ?? string.Empty,
                            TermsJson = terms.ToString(Formatting.None),
                            AuthorizationMode = "accepted_recommendation",
                            AcceptedByHeroStringId = actorHero.StringId,
                            RequiresAcceptance = false,
                            Reason = "Disposable native capability certification."
                        };
                        issued = ReignServices.CampaignCommand.ExecuteControlAction(action);
                        order = ReignServices.CampaignCommand.Orders.FirstOrDefault(x => x.OrderId == actionId);
                    }
                    bool nativeReceipt = issued?.Success == true && order != null
                        && (order.Status == "active" || order.Status == "completed")
                        && (order.Status == "completed" || !string.IsNullOrWhiteSpace(order.LastNativeSignature));
                    if (nativeReceipt) passed++;
                    receipts.Add(new JObject
                    {
                        ["capability"] = capability.Id,
                        ["passed"] = nativeReceipt,
                        ["prerequisiteFailure"] = prerequisite,
                        ["actionResult"] = issued?.Message ?? string.Empty,
                        ["orderStatus"] = order?.Status ?? "missing",
                        ["nativeSignature"] = order?.LastNativeSignature ?? string.Empty
                    });
                    if (order != null && !order.IsTerminal)
                    {
                        ReignServices.CampaignCommand.ExecuteControlAction(new ReignWorldActionRecord
                        {
                            Type = ReignWorldActionType.RegularCancelCampaignOrder,
                            Source = "campaign_command_certification_cleanup",
                            ActorHeroStringId = order.CommanderHeroStringId,
                            Reason = "Capability case cleanup.",
                            TermsJson = new JObject { ["orderId"] = order.OrderId }.ToString(Formatting.None)
                        });
                    }
                }
            }
            finally
            {
                if (armyCommander?.Army?.LeaderParty == armyCommander)
                {
                    try { DisbandArmyAction.ApplyByUnknownReason(armyCommander.Army); }
                    catch (Exception ex)
                    {
                        ReignLog.Warn("Campaign Command army fixture cleanup failed: " + ex.Message);
                    }
                }
                if (provisionedInfluenceClan != null
                    && Math.Abs(provisionedInfluenceClan.Influence - originalInfluence) > 0.01f)
                    ChangeClanInfluenceAction.Apply(provisionedInfluenceClan,
                        originalInfluence - provisionedInfluenceClan.Influence);
                foreach (MobileParty fixtureParty in createdArmyFixtureParties.ToList())
                {
                    try { if (fixtureParty?.IsActive == true) DestroyPartyAction.Apply(null, fixtureParty); }
                    catch (Exception ex)
                    {
                        ReignLog.Warn("Campaign Command native army ecology cleanup failed: " + ex.Message);
                    }
                }
                if (certificationSiege != null)
                {
                    try { certificationSiege.FinalizeSiegeEvent(); }
                    catch (Exception ex)
                    {
                        ReignLog.Warn("Campaign Command siege fixture cleanup failed: " + ex.Message);
                    }
                }
            }
            return new JObject
            {
                ["ok"] = passed == ReignCampaignCommandCapabilityRegistry.All.Count,
                ["caseCount"] = ReignCampaignCommandCapabilityRegistry.All.Count,
                ["passedCaseCount"] = passed,
                ["receipts"] = receipts,
                ["metrics"] = new JObject
                {
                    ["capabilitiesAttempted"] = ReignCampaignCommandCapabilityRegistry.All.Count,
                    ["capabilitiesPassed"] = passed,
                    ["nativeReceiptCount"] = receipts.OfType<JObject>().Count(x => x.Value<bool>("passed"))
                },
                ["error"] = passed == ReignCampaignCommandCapabilityRegistry.All.Count ? string.Empty
                    : "One or more registered native adapters lacked a successful native receipt."
            };
        }
    }
}
