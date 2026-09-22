using System;
using System.Collections.Generic;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.Election;

namespace ReignBeta.Runtime
{
    internal static class ReignDiplomacyPatches
    {
        private const string HarmonyId = "com.bannerlordreign.reignbeta.worlddiplomacy";
        private static Harmony _harmony;

        public static void Apply()
        {
            if (_harmony != null)
            {
                return;
            }

            _harmony = new Harmony(HarmonyId);
            HarmonyMethod prefix = new HarmonyMethod(typeof(ReignDiplomacyPatches), nameof(BlockAutonomousProposal));
            foreach (string methodName in new[]
            {
                "GetRandomWarDecision",
                "GetRandomPeaceDecision",
                "GetRandomStartingAllianceDecision",
                "GetRandomTradeAgreementDecision"
            })
            {
                var method = AccessTools.Method(typeof(KingdomDecisionProposalBehavior), methodName);
                if (method == null)
                {
                    Integration.ReignLog.Warn("World diplomacy patch could not find " + methodName + ".");
                    continue;
                }

                _harmony.Patch(method, prefix: prefix);
            }

            Integration.ReignLog.Info("Ruler-driven diplomacy suppression patches loaded.");
        }

        public static void Unapply()
        {
            _harmony?.UnpatchAll(HarmonyId);
            _harmony = null;
        }

        private static bool BlockAutonomousProposal(ref KingdomDecision __result)
        {
            __result = null;
            return false;
        }

        public static int CancelUnresolvedNpcDiplomacy()
        {
            int removed = 0;
            Kingdom playerKingdom = Clan.PlayerClan?.Kingdom;
            foreach (Kingdom kingdom in Kingdom.All)
            {
                if (kingdom == null || kingdom == playerKingdom || kingdom.UnresolvedDecisions == null)
                {
                    continue;
                }

                List<KingdomDecision> stale = new List<KingdomDecision>();
                foreach (KingdomDecision decision in kingdom.UnresolvedDecisions)
                {
                    if (decision is DeclareWarDecision
                        || decision is MakePeaceKingdomDecision
                        || decision is StartAllianceDecision
                        || decision is TradeAgreementDecision)
                    {
                        stale.Add(decision);
                    }
                }

                foreach (KingdomDecision decision in stale)
                {
                    kingdom.RemoveDecision(decision);
                    removed++;
                }
            }

            if (removed > 0)
            {
                Integration.ReignLog.Info("Removed " + removed + " unresolved native NPC diplomacy decisions.");
            }

            return removed;
        }
    }
}
