using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Election;
using TaleWorlds.CampaignSystem.ViewModelCollection.KingdomManagement.Policies;
using TaleWorlds.CampaignSystem.ViewModelCollection.KingdomManagement.Settlements;
using TaleWorlds.CampaignSystem.ViewModelCollection.KingdomManagement.Clans;
using TaleWorlds.CampaignSystem.ViewModelCollection.KingdomManagement.Diplomacy;
using TaleWorlds.CampaignSystem.ViewModelCollection.KingdomManagement.Decisions;
using TaleWorlds.Localization;

namespace ReignBeta.Government
{
    internal static class ReignGovernmentNativeControlAuthority
    {
        internal const string Explanation = "Handled through Reign Government. Open Government to petition, recommend, or review this matter.";
        internal static bool Active => Clan.PlayerClan?.Kingdom != null
            && ReignGovernmentCampaignBehavior.Instance?.EnsureGovernment(Clan.PlayerClan.Kingdom) != null;
    }

    [HarmonyPatch]
    internal static class ReignGovernmentNativeControlEnabledPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.PropertyGetter(typeof(KingdomPoliciesVM), "CanProposeOrDisavowPolicy");
            yield return AccessTools.PropertyGetter(typeof(KingdomSettlementVM), "CanAnnexCurrentSettlement");
            yield return AccessTools.PropertyGetter(typeof(KingdomClanVM), "CanExpelCurrentClan");
            yield return AccessTools.PropertyGetter(typeof(KingdomDiplomacyProposalActionItemVM), "IsEnabled");
        }
        private static void Postfix(ref bool __result) { if (ReignGovernmentNativeControlAuthority.Active) __result = false; }
    }

    [HarmonyPatch]
    internal static class ReignGovernmentNativeControlReasonPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(KingdomPoliciesVM), "GetCanProposeOrDisavowPolicyWithReason");
            yield return AccessTools.Method(typeof(KingdomSettlementVM), "GetCanAnnexSettlementWithReason");
            yield return AccessTools.Method(typeof(KingdomClanVM), "GetCanExpelCurrentClanWithReason");
        }
        private static bool Prefix(ref bool __result, ref TextObject disabledReason)
        {
            if (!ReignGovernmentNativeControlAuthority.Active) return true;
            __result = false; disabledReason = new TextObject(ReignGovernmentNativeControlAuthority.Explanation); return false;
        }
    }

    [HarmonyPatch]
    internal static class ReignGovernmentNativeControlExecutePatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(KingdomPoliciesVM), "ExecuteProposeOrDisavow");
            yield return AccessTools.Method(typeof(KingdomSettlementVM), "ExecuteAnnex");
            yield return AccessTools.Method(typeof(KingdomClanVM), "ExecuteExpelCurrentClan");
            yield return AccessTools.Method(typeof(KingdomDiplomacyProposalActionItemVM), "ExecuteAction");
        }
        private static bool Prefix()
        {
            if (!ReignGovernmentNativeControlAuthority.Active) return true;
            ReignBeta.UI.ReignGovernmentScreenManager.Open(Clan.PlayerClan.Kingdom,
                Clan.PlayerClan.Kingdom.Leader == Hero.MainHero, null);
            return false;
        }
    }

    [HarmonyPatch(typeof(KingdomDiplomacyProposalActionItemVM), nameof(KingdomDiplomacyProposalActionItemVM.RefreshValues))]
    internal static class ReignGovernmentNativeDiplomacyExplanationPatch
    {
        private static void Postfix(KingdomDiplomacyProposalActionItemVM __instance)
        {
            if (!ReignGovernmentNativeControlAuthority.Active) return;
            __instance.IsEnabled = false;
            __instance.Explanation = ReignGovernmentNativeControlAuthority.Explanation;
        }
    }

    [HarmonyPatch]
    internal static class ReignGovernmentNativeDecisionScreenPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(KingdomDecisionsVM), "HandleDecision");
            yield return AccessTools.Method(typeof(KingdomDecisionsVM), "RefreshWith");
        }
        private static bool Prefix(KingdomDecision __0)
        {
            if (ReignGovernmentCampaignBehavior.Instance?.TryCaptureNativeDecision(__0) != true) return true;
            __0.Kingdom.RemoveDecision(__0);
            return false;
        }
    }
}
