using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using ReignBeta.Court;
using ReignBeta.Integration;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;

namespace ReignBeta.Campaign
{
    internal static class ReignArrestCustodyPatches
    {
        private const string HarmonyId = "com.bannerlordreign.reignbeta.arrest_custody";
        private static Harmony _harmony;
        private static readonly Dictionary<string, PendingSettlementRelease> PendingSettlementReleases
            = new Dictionary<string, PendingSettlementRelease>(StringComparer.OrdinalIgnoreCase);

        internal static void Apply()
        {
            if (_harmony != null) return;
            try
            {
                MethodInfo target = AccessTools.Method(typeof(EndCaptivityAction), "ApplyInternal");
                if (target == null) throw new MissingMethodException(typeof(EndCaptivityAction).FullName, "ApplyInternal");
                MethodInfo ownerTarget = AccessTools.Method(
                    typeof(ChangeOwnerOfSettlementAction), "ApplyInternal");
                if (ownerTarget == null) throw new MissingMethodException(
                    typeof(ChangeOwnerOfSettlementAction).FullName, "ApplyInternal");
                _harmony = new Harmony(HarmonyId);
                _harmony.Patch(target, prefix: new HarmonyMethod(typeof(ReignArrestCustodyPatches),
                    nameof(EndCaptivityPrefix)));
                _harmony.Patch(ownerTarget, postfix: new HarmonyMethod(
                    typeof(ReignArrestCustodyPatches), nameof(ChangeOwnerPostfix)));
                ReignLog.Info("Reign arrest protected-custody release guard loaded.");
            }
            catch (Exception ex)
            {
                _harmony?.UnpatchAll(HarmonyId);
                _harmony = null;
                ReignLog.Warn("Arrest custody patch failed; native release behavior remains active: " + ex);
            }
        }

        internal static void Unapply()
        {
            _harmony?.UnpatchAll(HarmonyId);
            _harmony = null;
            PendingSettlementReleases.Clear();
        }

        internal static bool ConsumeSovereigntyRelease(Hero prisoner, Settlement settlement,
            Hero newOwner)
        {
            if (prisoner == null || settlement == null) return false;
            if (!PendingSettlementReleases.TryGetValue(prisoner.StringId, out PendingSettlementRelease pending))
                return false;
            PendingSettlementReleases.Remove(prisoner.StringId);
            return string.Equals(pending.SettlementStringId, settlement.StringId,
                       StringComparison.OrdinalIgnoreCase)
                && string.Equals(pending.OwnerClanStringId, newOwner?.Clan?.StringId ?? string.Empty,
                       StringComparison.OrdinalIgnoreCase)
                && DateTime.UtcNow - pending.RecordedUtc <= TimeSpan.FromSeconds(15);
        }

        internal static void RecordSovereigntyTransition(Hero prisoner, Settlement settlement,
            Hero newOwner)
        {
            if (prisoner == null || settlement == null || newOwner?.Clan == null) return;
            PendingSettlementReleases[prisoner.StringId] = new PendingSettlementRelease
            {
                SettlementStringId = settlement.StringId,
                OwnerClanStringId = newOwner.Clan.StringId,
                RecordedUtc = DateTime.UtcNow
            };
        }

        internal static void ClearExpiredSovereigntyReleases()
        {
            DateTime cutoff = DateTime.UtcNow - TimeSpan.FromSeconds(15);
            foreach (string heroId in new List<string>(PendingSettlementReleases.Keys))
            {
                if (PendingSettlementReleases[heroId].RecordedUtc < cutoff)
                    PendingSettlementReleases.Remove(heroId);
            }
        }

        internal static void ResetSovereigntyReleases()
        {
            PendingSettlementReleases.Clear();
        }

        private static bool EndCaptivityPrefix(Hero prisoner, EndCaptivityDetail detail)
        {
            bool protectedCustody = ReignArrestCampaignBehavior.Instance?.IsProtectedCustody(prisoner) == true
                || ReignCourtCampaignBehavior.Instance?.IsProtectedDocketCustody(prisoner) == true;
            if (detail == EndCaptivityDetail.Death
                || detail == EndCaptivityDetail.ReleasedByChoice
                || detail == EndCaptivityDetail.ReleasedAfterBattle)
            {
                return true;
            }
            if (protectedCustody)
                ReignLog.Info("Blocked automatic protected-custody release hero="
                    + (prisoner?.StringId ?? "") + " detail=" + detail);
            return !protectedCustody;
        }

        private static void ChangeOwnerPostfix(Settlement settlement, Hero newOwner)
        {
            ReignArrestCampaignBehavior.Instance?
                .ReconcileSovereigntyCustodyAfterOwnerChange(settlement, newOwner);
        }

        private sealed class PendingSettlementRelease
        {
            internal string SettlementStringId = string.Empty;
            internal string OwnerClanStringId = string.Empty;
            internal DateTime RecordedUtc;
        }
    }
}
