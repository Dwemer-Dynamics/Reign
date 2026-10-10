using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Linq;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.Election;
using TaleWorlds.CampaignSystem.ComponentInterfaces;

namespace ReignBeta.Runtime
{
    internal static class ReignDiplomacyPatches
    {
        private const string HarmonyId = "com.bannerlordreign.reignbeta.worlddiplomacy";
        private static Harmony _harmony;
        private static bool _allianceDurationPatchReady;
        [ThreadStatic] private static AllianceDurationScope _acceptedAlliance;

        internal static IDisposable UseAcceptedAllianceDuration(Kingdom actor, Kingdom target, float days)
        {
            if (actor == null || target == null || actor == target || float.IsNaN(days) || float.IsInfinity(days) || days <= 0)
                throw new ArgumentException("An accepted alliance requires distinct realms and a finite positive duration.");
            return _acceptedAlliance = new AllianceDurationScope(actor, target, days, _acceptedAlliance);
        }

        private sealed class AllianceDurationScope : IDisposable
        {
            internal readonly Kingdom Actor, Target;
            internal readonly float Days;
            private readonly AllianceDurationScope _previous;
            private bool _disposed;
            internal AllianceDurationScope(Kingdom actor, Kingdom target, float days, AllianceDurationScope previous)
            { Actor = actor; Target = target; Days = days; _previous = previous; }
            public void Dispose()
            {
                if (_disposed) return;
                if (_acceptedAlliance != this) throw new InvalidOperationException("Alliance duration scopes must close in order on their owning thread.");
                _acceptedAlliance = _previous;
                _disposed = true;
            }
        }

        internal static CampaignTime ResolveAllianceDuration(AllianceModel model, Kingdom first, Kingdom second)
        {
            var request = _acceptedAlliance;
            return request != null && ((request.Actor == first && request.Target == second)
                || (request.Actor == second && request.Target == first))
                ? CampaignTime.Days(request.Days) : model.MaxDurationOfAlliance;
        }

        public static void StartAcceptedAlliance(IAllianceCampaignBehavior behavior, Kingdom actor, Kingdom target, float days)
        {
            if (!_allianceDurationPatchReady)
                throw new InvalidOperationException("The native alliance duration adapter is unavailable; no alliance was started.");
            using (UseAcceptedAllianceDuration(actor, target, days)) behavior.StartAlliance(actor, target);
        }

        private static IEnumerable<CodeInstruction> AcceptedAllianceDuration(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList();
            var getter = AccessTools.PropertyGetter(typeof(AllianceModel), nameof(AllianceModel.MaxDurationOfAlliance));
            var matches = code.Where(c => c.Calls(getter)).ToList();
            if (matches.Count != 1)
                throw new InvalidOperationException("Native alliance creation must read exactly one alliance duration.");
            var replacement = AccessTools.Method(typeof(ReignDiplomacyPatches), nameof(ResolveAllianceDuration));
            foreach (var instruction in code)
            {
                if (!ReferenceEquals(instruction, matches[0])) { yield return instruction; continue; }
                // Keep branch labels and exception boundaries on the first replacement instruction.
                instruction.opcode = OpCodes.Ldarg_1;
                instruction.operand = null;
                yield return instruction;
                yield return new CodeInstruction(OpCodes.Ldarg_2);
                yield return new CodeInstruction(OpCodes.Call, replacement);
            }
            _allianceDurationPatchReady = true;
        }

        public static void Apply()
        {
            if (_harmony != null)
            {
                return;
            }

            _harmony = new Harmony(HarmonyId);
            var addAlliance = AccessTools.Method(typeof(AllianceCampaignBehavior), "AddAlliance", new[] { typeof(Kingdom), typeof(Kingdom) });
            if (addAlliance != null)
                _harmony.Patch(addAlliance, transpiler: new HarmonyMethod(typeof(ReignDiplomacyPatches), nameof(AcceptedAllianceDuration)));
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
            _allianceDurationPatchReady = false;
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
