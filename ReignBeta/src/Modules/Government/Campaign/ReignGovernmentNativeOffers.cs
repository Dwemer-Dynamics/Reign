using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Election;

namespace ReignBeta.Government
{
    public sealed partial class ReignGovernmentCampaignBehavior
    {
        internal bool OwnsNativePeaceOffer(IFaction opponent)
        {
            var realm = Clan.PlayerClan?.Kingdom;
            return opponent is Kingdom && realm?.RulingClan != null && realm != opponent
                && Hero.MainHero?.MapFaction == realm && EnsureGovernment(realm) != null;
        }

        public bool TryCaptureNativePeaceOffer(IFaction opponent, int opponentDailyTribute, int durationDays)
        {
            if (!OwnsNativePeaceOffer(opponent)) return false;
            var realm = Clan.PlayerClan.Kingdom;
            // The notification measures payment BY the opponent. Native recipient decisions use the reverse sign.
            return TryCaptureNativeDecision(new MakePeaceKingdomDecision(realm.RulingClan, opponent,
                -opponentDailyTribute, durationDays, applyResults: true, isProposedByOpponent: true));
        }
    }

    [HarmonyPatch(typeof(PeaceOfferCampaignBehavior), "OnPeaceOffered")]
    internal static class ReignGovernmentNativePeaceOfferPatch
    {
        private static bool Prefix(IFaction opponentFaction, int tributeAmount, int tributeDuration) =>
            ReignGovernmentCampaignBehavior.Instance?.TryCaptureNativePeaceOffer(opponentFaction, tributeAmount, tributeDuration) != true;
    }

    [HarmonyPatch]
    internal static class ReignGovernmentNativePeaceCallbackPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (string method in new[] { "AcceptPeaceOffer", "DeclinePeaceOffer", "OkPeaceOffer" })
                yield return AccessTools.Method(typeof(PeaceOfferCampaignBehavior), method);
        }
        private static bool Prefix(ref IFaction ____opponentFaction, ref int ____currentPeaceOfferTributeAmount,
            ref int ____currentPeaceOfferTributeDuration)
        {
            if (ReignGovernmentCampaignBehavior.Instance?.TryCaptureNativePeaceOffer(____opponentFaction,
                ____currentPeaceOfferTributeAmount, ____currentPeaceOfferTributeDuration) != true) return true;
            // A stale native callback can neither enact peace nor spend decline influence after Reign takes ownership.
            ____opponentFaction = null;
            ____currentPeaceOfferTributeAmount = 0;
            ____currentPeaceOfferTributeDuration = 0;
            return false;
        }
    }

    [HarmonyPatch(typeof(PeaceOfferCampaignBehavior), "OnPeaceOfferResolved")]
    internal static class ReignGovernmentNativePeaceResolvedPatch
    {
        private static bool Prefix(IFaction opponentFaction) =>
            ReignGovernmentCampaignBehavior.Instance?.OwnsNativePeaceOffer(opponentFaction) != true;
    }

    [HarmonyPatch(typeof(ChangeRelationAction), "ApplyInternal")]
    internal static class ReignGovernmentNativeRelationPrivacyPatch
    {
        private static void Prefix(ref bool showQuickNotification)
        {
            if (ReignGovernmentCampaignBehavior.Instance?.IsApplyingGovernmentNativeEffect == true) showQuickNotification = false;
        }
    }
    // These campaign entrypoints can display notices or immediate one-clan inquiries without an election VM.
    [HarmonyPatch]
    internal static class ReignGovernmentNativeCallOfferPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (string name in new[] { "OnCallToWarAgreementProposedToPlayer", "OnCallToWarAgreementProposedToPlayerKingdom",
                "OnCallToWarAgreementProposedByPlayer", "OnCallToWarAgreementProposedByPlayerKingdom" })
                yield return AccessTools.Method(typeof(AllianceCampaignBehavior), name);
        }
        private static bool Prefix(MethodBase __originalMethod, Kingdom __0, Kingdom __1)
        {
            var behavior = ReignGovernmentCampaignBehavior.Instance;
            var realm = Clan.PlayerClan?.Kingdom;
            if (behavior == null || realm?.RulingClan == null || behavior.GetGovernment(realm) == null) return true;
            KingdomDecision decision = __originalMethod.Name.Contains("ToPlayer")
                ? (KingdomDecision)new AcceptCallToWarAgreementDecision(realm.RulingClan, __0, __1)
                : new ProposeCallToWarAgreementDecision(realm.RulingClan, __0, __1);
            return !behavior.TryCaptureNativeDecision(decision);
        }
    }

    [HarmonyPatch]
    internal static class ReignGovernmentNativeTreatyOfferPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(AllianceCampaignBehavior), "OnAllianceOfferedToPlayer");
            yield return AccessTools.Method(typeof(AllianceCampaignBehavior), "OnAllianceOfferedToPlayerKingdom");
            yield return AccessTools.Method(typeof(TradeAgreementsCampaignBehavior), "OnTradeAgreementOfferedToPlayer");
        }
        private static bool Prefix(MethodBase __originalMethod, Kingdom __0)
        {
            var behavior = ReignGovernmentCampaignBehavior.Instance;
            var realm = Clan.PlayerClan?.Kingdom;
            if (behavior == null || realm == null || __0?.RulingClan == null || behavior.GetGovernment(realm) == null) return true;
            KingdomDecision decision = __originalMethod.DeclaringType == typeof(TradeAgreementsCampaignBehavior)
                ? (KingdomDecision)new TradeAgreementDecision(__0.RulingClan, realm)
                : new StartAllianceDecision(__0.RulingClan, realm);
            return !behavior.TryCaptureNativeDecision(decision);
        }
    }
}
