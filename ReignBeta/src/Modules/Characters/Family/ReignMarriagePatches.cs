using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Reign.Core.Contracts.Actions;
using ReignBeta.Campaign;
using ReignBeta.Integration;
using ReignBeta.Settings;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.BarterSystem;
using TaleWorlds.CampaignSystem.BarterSystem.Barterables;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.Library;

namespace ReignBeta.Family
{
    internal static class ReignMarriagePatches
    {
        private const string HarmonyId = "com.bannerlordreign.reignbeta.marriage";
        private const string WeddingHarmonyId = HarmonyId + ".weddings";
        private static Harmony _harmony;
        private static Harmony _weddings;
        [ThreadStatic] private static int _nativeMatchmaking;

        internal static bool WeddingsReady { get; private set; }

        public static void Apply()
        {
            if (_harmony != null) return;
            try
            {
                _harmony = new Harmony(HarmonyId);
                Patch(_harmony, typeof(RomanceCampaignBehavior), "CheckNpcMarriages",
                    nameof(CheckNpcMarriagesPrefix), finalizer: nameof(ExitNativeMatchmaking));
                _weddings = new Harmony(WeddingHarmonyId);
                Patch(_weddings, typeof(MarriageAction), nameof(MarriageAction.Apply), nameof(MarriagePrefix));
                Patch(_weddings, typeof(DefaultMarriageModel), nameof(DefaultMarriageModel.IsSuitableForMarriage),
                    postfix: nameof(AvailablePostfix));
                Patch(_weddings, typeof(MarriageOfferCampaignBehavior), "DailyTickClan", nameof(NativeOfferPrefix));
                Patch(_weddings, typeof(MarriageOfferCampaignBehavior), "HourlyTick", nameof(LegacyPreparationsPrefix));
                Patch(_weddings, typeof(MarriageOfferCampaignBehavior), nameof(MarriageOfferCampaignBehavior.OnMarriageOfferAcceptedOnPopUp), nameof(RetireUnsignedOfferPrefix));
                Patch(_weddings, typeof(MarriageOfferCampaignBehavior), nameof(MarriageOfferCampaignBehavior.OnMarriageOfferDeclinedOnPopUp), nameof(RetireUnsignedOfferPrefix));
                Patch(_weddings, typeof(MarriageOfferCampaignBehavior), "MarryHeroesViaOffer", nameof(AcceptedNativeOfferPrefix));
                Patch(_weddings, typeof(RomanceCampaignBehavior), "conversation_discuss_marriage_alliance_on_condition",
                    postfix: nameof(NativeFamilyDialoguePostfix));
                Patch(_weddings, typeof(BarterManager), "ApplyBarterOffer", nameof(AcceptedBarterPrefix));
                WeddingsReady = true;
                ReignLog.Info("Reign wedding and arranged offer interception loaded.");
            }
            catch (Exception ex)
            {
                WeddingsReady = false;
                _weddings?.UnpatchAll(WeddingHarmonyId);
                ReignLog.Warn("Reign marriage interception failed: " + ex);
            }
        }

        private static void Patch(Harmony harmony, Type type, string method, string prefix = null, string postfix = null, string finalizer = null)
        {
            var target = AccessTools.Method(type, method) ?? throw new MissingMethodException(type.FullName, method);
            HarmonyMethod Handler(string name) => name == null ? null : new HarmonyMethod(typeof(ReignMarriagePatches), name);
            harmony.Patch(target, prefix: Handler(prefix), postfix: Handler(postfix), finalizer: Handler(finalizer));
        }

        public static void Unapply()
        {
            WeddingsReady = false;
            _weddings?.UnpatchAll(WeddingHarmonyId);
            _weddings = null;
            _harmony?.UnpatchAll(HarmonyId);
            _harmony = null;
        }

        private static bool CheckNpcMarriagesPrefix(out bool __state)
        {
            __state = ReignBetaSettings.Instance?.ReignControlledNpcMarriageEnabled == false;
            if (__state) _nativeMatchmaking++;
            return __state;
        }

        private static Exception ExitNativeMatchmaking(bool __state, Exception __exception)
        {
            if (__state) _nativeMatchmaking--;
            return __exception;
        }

        private static bool NativeOfferPrefix() => !ReignRelationshipCampaignBehavior.WeddingFeatureReady;

        private static bool RetireUnsignedOfferPrefix(MarriageOfferCampaignBehavior __instance)
        {
            if (!ReignRelationshipCampaignBehavior.WeddingFeatureReady) return true;
            var first = AccessTools.Field(typeof(MarriageOfferCampaignBehavior), "_currentOfferedPlayerClanHero")?.GetValue(__instance) as Hero;
            var other = AccessTools.Field(typeof(MarriageOfferCampaignBehavior), "_currentOfferedOtherClanHero")?.GetValue(__instance) as Hero;
            if (first != null && other != null)
                CampaignEventDispatcher.Instance.OnMarriageOfferCanceled(first.IsFemale ? other : first, first.IsFemale ? first : other);
            return false;
        }

        private static bool LegacyPreparationsPrefix(MarriageOfferCampaignBehavior __instance)
        {
            if (!ReignRelationshipCampaignBehavior.WeddingFeatureReady) return true;
            if (ReignSaveSyncCoordinator.IsAlignmentPending || ReignCampaignInitializationGate.IsPending) return false;
            RetireUnsignedOfferPrefix(__instance);
            var preparations = AccessTools.Field(typeof(MarriageOfferCampaignBehavior), "_acceptedMarriageOffersThatWaitingForAvailability")
                ?.GetValue(__instance) as Dictionary<Hero, Hero>;
            if (preparations == null || preparations.Count == 0) return false;

            // Native eligibility reads this dictionary to identify already engaged
            // heroes. Remove only the exact accepted entry while transferring it.
            foreach (var pair in preparations.ToList())
            {
                preparations.Remove(pair.Key);
                if (pair.Key?.IsAlive != true || pair.Value?.IsAlive != true || pair.Key.Spouse != null || pair.Value.Spouse != null) continue;
                string id = "legacy_native_preparation_" + pair.Key.StringId + "_" + pair.Value.StringId;
                var behavior = ReignRelationshipCampaignBehavior.Instance;
                if (!behavior.RequestWedding(id, pair.Key, pair.Value, null, pair.Value.Clan?.Leader?.StringId,
                    10, out _, legacyPreparation: true))
                    preparations[pair.Key] = pair.Value;
            }
            return false;
        }

        private static void NativeFamilyDialoguePostfix(ref bool __result)
        {
            if (ReignRelationshipCampaignBehavior.WeddingFeatureReady) __result = false;
        }

        private static void AvailablePostfix(Hero __0, ref bool __result)
        {
            if (__result && ReignRelationshipCampaignBehavior.WeddingFeatureReady
                && ReignRelationshipCampaignBehavior.Instance.ReservedForWedding(__0)) __result = false;
        }

        private static bool MarriagePrefix(Hero __0, Hero __1)
        {
            if (!ReignRelationshipCampaignBehavior.WeddingFeatureReady) return true;
            var behavior = ReignRelationshipCampaignBehavior.Instance;
            if (behavior.WeddingAuthorization(__0, __1)) return true;
            if (_nativeMatchmaking != 0)
            {
                behavior.RequestWedding("native_match_" + Guid.NewGuid().ToString("N"), __0, __1, null, "", 0, out _);
                return false;
            }
            return !behavior.ReservedForWedding(__0) && !behavior.ReservedForWedding(__1);
        }

        private static bool AcceptedNativeOfferPrefix(Hero __0, Hero __1)
        {
            if (!ReignRelationshipCampaignBehavior.WeddingFeatureReady) return true;
            var behavior = ReignRelationshipCampaignBehavior.Instance;
            string id = "native_offer_" + __0.StringId + "_" + __1.StringId + "_" + Math.Floor(CampaignTime.Now.ToDays);
            behavior.RequestWedding(id, __0, __1, null, __1.Clan?.Leader?.StringId, 10, out _);
            return false;
        }

        private static bool AcceptedBarterPrefix(BarterManager __instance, Hero __0, Hero __1, List<Barterable> __2)
        {
            if (!ReignRelationshipCampaignBehavior.WeddingFeatureReady || __2 == null) return true;
            var marriages = __2.OfType<MarriageBarterable>().ToList();
            if (marriages.Count == 0) return true;
            bool scheduled = false;
            string error = "";
            try
            {
                if (marriages.Count != 1) throw new InvalidOperationException("Settle one proposed marriage at a time.");
                var marriage = marriages[0];
                var terms = new ActionAgreement { PlayerId = __0.StringId, NpcId = __1.StringId };
                foreach (var asset in __2.Where(x => x != marriage && x.CurrentAmount > 0))
                {
                    var from = asset.OriginalOwner;
                    var to = from == __0 ? __1 : __0;
                    if (from == null || to == null || from != __0 && from != __1)
                        throw new InvalidOperationException("The native barter party could not be resolved.");
                    var leg = ReadAsset(asset, from, to);
                    terms.Clauses.Add(leg);
                }
                ReignWeddingBargain.Check(terms, marriage.ProposingHero, marriage.HeroBeingProposedTo);
                scheduled = ReignRelationshipCampaignBehavior.Instance.RequestWedding("native_barter_" + terms.Id,
                    marriage.ProposingHero, marriage.HeroBeingProposedTo, terms, "", 0, out _);
            }
            catch (Exception ex) { error = ex.Message; }
            if (__0 == Hero.MainHero)
            {
                // This acknowledgement closes the negotiation. The usual barter
                // settlement and its world event belong to the verified wedding.
                AccessTools.PropertySetter(typeof(BarterManager), nameof(BarterManager.LastBarterIsAccepted))
                    ?.Invoke(__instance, new object[] { false });
                __instance.Close();
                if (TaleWorlds.CampaignSystem.Campaign.Current.ConversationManager.IsConversationInProgress)
                    TaleWorlds.CampaignSystem.Campaign.Current.ConversationManager.ContinueConversation();
                InformationManager.DisplayMessage(new InformationMessage(scheduled
                    ? "The marriage bargain is agreed. The wedding choice is pending."
                    : "The marriage bargain could not be scheduled. " + error));
            }
            return false;
        }

        private static ActionClause ReadAsset(Barterable asset, Hero from, Hero to)
        {
            if (asset is GoldBarterable)
                return ReignWeddingBargain.Asset("give.gold", from, to, asset.CurrentAmount);
            if (asset is ItemBarterable item && item.OriginalParty == from.PartyBelongedTo?.Party)
            {
                var element = item.ItemRosterElement.EquipmentElement;
                return ReignWeddingBargain.Asset("give.item", from, to, item.CurrentAmount, element.Item.StringId,
                    element.ItemModifier?.StringId ?? "");
            }
            if (asset is FiefBarterable fief)
                return ReignWeddingBargain.Asset("give.settlement", from, to, 1, fief.TargetSettlement.StringId);
            if (asset is SetPrisonerFreeBarterable || asset is TransferPrisonerBarterable)
            {
                var field = AccessTools.Field(asset.GetType(), "_prisonerCharacter");
                var prisoner = field?.GetValue(asset) as Hero;
                if (prisoner != null)
                    return ReignWeddingBargain.Asset(asset is SetPrisonerFreeBarterable ? ReignWeddingBargain.ReleasePrisoner : "give.prisoner",
                        from, to, 1, asset is SetPrisonerFreeBarterable ? prisoner.StringId : prisoner.CharacterObject.StringId);
            }
            throw new InvalidOperationException("The native barter contains an unsupported marriage term.");
        }
    }
}
