using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AIPortraits;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ReignBeta.UI;
using ReignBeta.World;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Encounters;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.ObjectSystem;
using ReignBeta.Dialogue.UI;
using ReignBeta.Diplomacy.Campaign;
using ReignBeta.Diplomacy.Integration;
using ReignBeta.KingdomEvents.Campaign;
using ReignBeta.Platform.Campaign;
using ReignBeta.Platform.Integration;
using ReignBeta.Platform.Settings;
using ReignBeta.Portraits.Integration;
using ReignBeta.WorldSimulation.World;
using ReignBeta.Platform.Runtime;

namespace ReignBeta.WorldSimulation.Campaign
{
    public static partial class ReignBetaDebugActions
    {
        public static void QueueTestDeclareWar()
        {
            if (!RequireTestMode() || !TryGetPlayerKingdom(out Kingdom actor))
            {
                return;
            }

            Kingdom target = FindPeacefulKingdom(actor);
            if (target == null)
            {
                Show("No peaceful kingdom available for declare war test.");
                return;
            }

            QueueAction(new ReignWorldActionRecord
            {
                Type = ReignWorldActionType.DiplomacyDeclareWar,
                Source = "mcm_test",
                ActorKingdomStringId = actor.StringId,
                TargetKingdomStringId = target.StringId,
                Reason = "MCM test: declare war."
            }, "declare war");
        }

        public static void TriggerRandomDiplomacyPopupTest()
        {
            if (!RequireTestMode())
            {
                return;
            }

            ReignBetaSettings settings = ReignBetaSettings.Instance;
            if (settings != null && (!settings.Enabled || !settings.UseLocalServer || !settings.ExecuteDiplomacyActions || !settings.AllowAutonomousWorldTicks))
            {
                Show("Enable Bannerlord Reign, Local Server, Execute Diplomacy Actions, and Autonomous World Ticks before running the diplomacy popup test.");
                return;
            }
            if (TaleWorlds.CampaignSystem.Campaign.Current == null || ReignServices.AI == null)
            {
                Show("Load a campaign before running the diplomacy popup test.");
                return;
            }
            int npcKingdoms = Kingdom.All.Count(x => x != null && !x.IsEliminated && x.Leader != null && x != Clan.PlayerClan?.Kingdom);
            if (npcKingdoms < 2)
            {
                Show("At least two living non-player kingdoms are required for the diplomacy popup test.");
                return;
            }

            Show("Selecting one eligible NPC ruler and making the normal diplomacy initiative rolls...");
            _ = RunRandomDiplomacyPopupTestAsync();
        }

        private static async Task RunRandomDiplomacyPopupTestAsync()
        {
            ReignRandomDiplomacyTestResult test = await ReignDiplomacyServerClient.QueueRandomDiplomacyPopupTestAsync().ConfigureAwait(false);
            if (!test.Ok)
            {
                await ReignMainThread.InvokeAsync(() => Show("Diplomacy initiative test could not run: " + (string.IsNullOrWhiteSpace(test.Error) ? "unknown server error" : test.Error))).ConfigureAwait(false);
                return;
            }

            if (string.Equals(test.Status, "no_initiative", StringComparison.OrdinalIgnoreCase)
                || string.Equals(test.Status, "no_ruler_eligible", StringComparison.OrdinalIgnoreCase)
                || string.Equals(test.Status, "ruler_chose_no_action", StringComparison.OrdinalIgnoreCase))
            {
                string ruler = string.IsNullOrWhiteSpace(test.RulerName) ? "The selected ruler" : test.RulerName;
                string detail = string.IsNullOrWhiteSpace(test.InitiativeSummary) ? string.Empty : " Rolls: " + test.InitiativeSummary + ".";
                string outcome = string.Equals(test.Status, "ruler_chose_no_action", StringComparison.OrdinalIgnoreCase)
                    ? ruler + " passed an initiative roll but chose no valid diplomatic action."
                    : ruler + " did not pass a normal diplomacy initiative roll.";
                await ReignMainThread.InvokeAsync(() => Show(outcome + detail)).ConfigureAwait(false);
                return;
            }

            if (!test.Idempotent && test.Action != null)
            {
                Tuple<bool, string> validation = await ReignMainThread.InvokeAsync(() =>
                {
                    bool valid = ReignActionValidator.Validate(test.Action, out string failure);
                    return Tuple.Create(valid, failure ?? string.Empty);
                }).ConfigureAwait(false);
                if (!validation.Item1)
                {
                    ReignActionResult rejected = ReignActionResult.ValidationFailed(validation.Item2);
                    await ReignServerClient.ReportActionAsync(test.Action, "failed", validation.Item2, rejected).ConfigureAwait(false);
                    await ReignMainThread.InvokeAsync(() => Show("Random diplomacy test was rejected safely: " + validation.Item2)).ConfigureAwait(false);
                    return;
                }

                ReignActionResult execution = await ReignMainThread.InvokeAsync(() => ReignServices.AI?.ExecuteActionForTest(test.Action)).ConfigureAwait(false);
                if (execution == null || !execution.Success || !execution.Completed)
                {
                    string failure = execution?.Message ?? "The Bannerlord action behavior was unavailable.";
                    await ReignMainThread.InvokeAsync(() => Show("Random diplomacy test did not complete: " + failure)).ConfigureAwait(false);
                    return;
                }
            }

            ReignWorldDiplomacyCampaignBehavior diplomacy = await ReignMainThread.InvokeAsync(() =>
                TaleWorlds.CampaignSystem.Campaign.Current?.GetCampaignBehavior<ReignWorldDiplomacyCampaignBehavior>()).ConfigureAwait(false);
            bool popupQueued = !string.IsNullOrWhiteSpace(test.EventId)
                && diplomacy != null
                && await diplomacy.WaitForAnnouncementAndShowForTestAsync(test.EventId).ConfigureAwait(false);
            string description = test.ActorKingdomName + " -> " + test.TargetKingdomName + ": " + test.ActionLabel;
            await ReignMainThread.InvokeAsync(() => Show(popupQueued
                ? "Diplomacy initiative passed and completed (" + description + "). The real event popup is queued; close MCM to return to the campaign map if it is not already visible."
                : "Diplomacy initiative passed (" + description + "), but its popup receipt is still pending. Normal diplomacy polling will continue trying after MCM closes.")).ConfigureAwait(false);
        }

        public static void QueueTestMakePeace()
        {
            QueuePeaceAction(ReignWorldActionType.DiplomacyMakePeace, "MCM test: make peace.", null, "make peace");
        }

        public static void QueueTestTributePeace()
        {
            JObject terms = new JObject
            {
                ["dailyTribute"] = 150,
                ["durationDays"] = 30
            };
            QueuePeaceAction(ReignWorldActionType.DiplomacyOfferTributePeace, "MCM test: offer tribute peace.", terms, "tribute peace");
        }

        public static void QueueTestRecordPromise()
        {
            if (!RequireTestMode() || !TryGetPlayerKingdom(out Kingdom actor))
            {
                return;
            }

            Kingdom target = FindEnemyKingdom(actor) ?? FindPeacefulKingdom(actor);
            if (target == null)
            {
                Show("No kingdom target available for promise test.");
                return;
            }

            QueueAction(new ReignWorldActionRecord
            {
                Type = ReignWorldActionType.DiplomacyRecordPromise,
                Source = "mcm_test",
                ActorKingdomStringId = actor.StringId,
                TargetKingdomStringId = target.StringId,
                Reason = "MCM test: diplomatic promise recorded for future memory."
            }, "record promise");
        }

        public static void QueueTestReparationsPeace()
        {
            JObject terms = new JObject
            {
                ["reparationsGold"] = 15000,
                ["dailyTribute"] = 200,
                ["durationDays"] = 30
            };
            QueuePeaceAction(ReignWorldActionType.DiplomacyDemandReparationsPeace, "MCM test: demand reparations for peace.", terms, "reparations peace");
        }

        public static void QueueTestSettlementSurrenderPeace()
        {
            QueueSurrenderAction(ReignWorldActionType.DiplomacyDemandSettlementPeace, "MCM test: demand settlement surrender for peace.", false);
        }

        public static void QueueTestFullSurrenderPeace()
        {
            QueueSurrenderAction(ReignWorldActionType.DiplomacyDemandSurrenderPeace, "MCM test: demand full surrender terms.", true);
        }

        public static void QueueTestRecruitAndRecover()
        {
            QueueStrategyAction(ReignWorldActionType.StrategyRecruitAndRecover, "MCM test: recruit and recover.", "recruit and recover", false);
        }

        public static void QueueTestFormArmy()
        {
            QueueStrategyAction(ReignWorldActionType.StrategyFormArmy, "MCM test: form army.", "form army", false);
        }

        public static void QueueTestAttackSettlement()
        {
            QueueStrategyAction(ReignWorldActionType.StrategyAttackSettlement, "MCM test: attack settlement.", "attack settlement", true);
        }

        public static void QueueTestCapturePlan()
        {
            QueueStrategyAction(ReignWorldActionType.StrategyCaptureSettlement, "MCM test: capture settlement plan.", "capture settlement plan", true);
        }

        public static void QueueTestRulingClanRebellion()
        {
            if (!RequireTestMode())
            {
                return;
            }
            Show("NPC rebellion starts now come only from the saved weekly native-relationship roll. To test a player rebellion, serve as a non-ruling vassal and explicitly declare rebellion to the current ruler in direct conversation or by letter.");
        }

        public static void QueueTestInstallRulingClan()
        {
            if (!RequireTestMode() || !TryGetPlayerKingdom(out Kingdom kingdom))
            {
                return;
            }

            Clan claimant = FindClaimantClan(kingdom);
            if (claimant == null)
            {
                Show("No non-ruling clan available for install ruling clan test.");
                return;
            }

            QueueAction(new ReignWorldActionRecord
            {
                Type = ReignWorldActionType.PoliticsInstallRulingClan,
                Source = "mcm_test",
                ActorKingdomStringId = kingdom.StringId,
                ActorClanStringId = claimant.StringId,
                Reason = "MCM test: install new ruling clan."
            }, "install ruling clan");
        }

        private static void QueuePeaceAction(ReignWorldActionType type, string reason, JObject terms, string label)
        {
            if (!RequireTestMode() || !TryGetPlayerKingdom(out Kingdom actor))
            {
                return;
            }

            Kingdom target = FindEnemyKingdom(actor);
            if (target == null)
            {
                Show("No enemy kingdom available. Use Declare War test first.");
                return;
            }

            QueueAction(new ReignWorldActionRecord
            {
                Type = type,
                Source = "mcm_test",
                ActorKingdomStringId = actor.StringId,
                TargetKingdomStringId = target.StringId,
                TermsJson = terms == null ? string.Empty : terms.ToString(Formatting.None),
                Reason = reason
            }, label);
        }

        private static void QueueSurrenderAction(ReignWorldActionType type, string reason, bool includeReparations)
        {
            if (!RequireTestMode() || !TryGetPlayerKingdom(out Kingdom actor))
            {
                return;
            }

            Kingdom target = FindEnemyKingdom(actor);
            if (target == null)
            {
                Show("No enemy kingdom available. Use Declare War test first.");
                return;
            }

            Settlement settlement = FindEnemyFortificationOwnedBy(target);
            if (settlement == null)
            {
                Show("Enemy kingdom has no town/castle available for surrender test.");
                return;
            }

            JObject terms = new JObject
            {
                ["settlementIds"] = new JArray(settlement.StringId)
            };

            if (includeReparations)
            {
                terms["reparationsGold"] = 25000;
                terms["dailyTribute"] = 300;
                terms["durationDays"] = 45;
            }

            QueueAction(new ReignWorldActionRecord
            {
                Type = type,
                Source = "mcm_test",
                ActorKingdomStringId = actor.StringId,
                TargetKingdomStringId = target.StringId,
                TargetSettlementStringId = settlement.StringId,
                TermsJson = terms.ToString(Formatting.None),
                Reason = reason
            }, type == ReignWorldActionType.DiplomacyDemandSurrenderPeace ? "full surrender peace" : "settlement surrender peace");
        }

        private static void QueueStrategyAction(ReignWorldActionType type, string reason, string label, bool requireHostile)
        {
            if (!RequireTestMode() || !TryGetPlayerKingdom(out Kingdom kingdom))
            {
                return;
            }

            Hero actor = FindTestLord(kingdom);
            if (actor == null)
            {
                Show("No non-player lord with an active party found.");
                return;
            }

            Settlement target = requireHostile ? FindHostileFortification(kingdom, actor) : FindHostileFortification(kingdom, actor) ?? FindAnyUsefulSettlement(kingdom);
            if (target == null)
            {
                Show("No valid settlement target found for " + label + ".");
                return;
            }

            QueueAction(new ReignWorldActionRecord
            {
                Type = type,
                Source = "mcm_test",
                ActorHeroStringId = actor.StringId,
                ActorKingdomStringId = kingdom.StringId,
                TargetSettlementStringId = target.StringId,
                Reason = reason,
                MinimumTroops = 40,
                DesiredStrength = 350,
                MaxAttempts = type == ReignWorldActionType.StrategyCaptureSettlement ? 24 : 3
            }, label);
        }

        private static void QueueAction(ReignWorldActionRecord action, string label)
        {
            ReignAICampaignBehavior behavior = ReignServices.AI;
            if (behavior == null)
            {
                Show("Bannerlord Reign behavior is not active.");
                return;
            }

            if (!ReignActionValidator.Validate(action, out string failure))
            {
                Show("Cannot queue " + label + ": " + failure);
                return;
            }

            behavior.EnqueueAction(action);
            Show("MCM test queued " + label + ".");
        }
    }
}
