using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ReignBeta.Campaign;
using ReignBeta.Characters.Integration;
using ReignBeta.Persistence.Campaign;
using ReignBeta.Platform.Integration;
using ReignBeta.Platform.Runtime;
using ReignBeta.Shared.Dialogue;
using ReignBeta.World;
using ReignBeta.WorldSimulation.Campaign;
using ReignBeta.WorldSimulation.Rulers;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;

namespace ReignBeta.WorldSimulation.World
{
    internal sealed class ReignNativeRansom
    {
        private readonly TaleWorlds.CampaignSystem.Campaign _campaign = TaleWorlds.CampaignSystem.Campaign.Current;
        private readonly ReignWorldHistoryCampaignBehavior _world = ReignServices.WorldHistory;
        private readonly Hero _player = Hero.MainHero;
        private readonly ReignGoldTransferService _service = ReignServices.GoldTransferService;
        private readonly NativeRansomJournal _journal = ReignServices.GoldTransferService?.Ransoms;
        private readonly string _campaignId = ReignCampaignIdentity.CurrentCampaignId();
        private readonly string _timelineId = ReignServices.WorldHistory?.TimelineId;
        private readonly string _generation = ReignServices.WorldHistory?.SharedActivityGeneration;

        internal string TimelineId => _timelineId;
        internal string CampaignId => _campaignId;
        internal NativeRansomJournal Journal => _journal;
        internal bool OwnsLoadedWorld() => _campaign != null && _campaign == TaleWorlds.CampaignSystem.Campaign.Current
            && _player != null && _player == Hero.MainHero && _world != null && _world == ReignServices.WorldHistory
            && _service != null && _service == ReignServices.GoldTransferService
            && _journal != null && _journal == _service.Ransoms
            && _campaignId == ReignCampaignIdentity.CurrentCampaignId()
            && !string.IsNullOrWhiteSpace(_campaign.UniqueGameId) && !string.IsNullOrWhiteSpace(TimelineId) && TimelineId == _world.TimelineId
            && _generation == _world.SharedActivityGeneration && !ReignCampaignInitializationGate.IsPending
            && !ReignSaveSyncCampaignBehavior.IsNativeSaveInProgress;

        internal static string Key(string route, string campaignId, string timelineId, string id)
            => PlayerActionRules.Hash(new JArray(route, campaignId, timelineId, id).ToString(Formatting.None));

        internal NativeRansomAttempt Execute(string key, string binding, JObject material, IReadOnlyList<Captive> captives,
            Func<bool> owns, Hero releaseAuthority, Hero payer, Hero recipient, int? gold, NativeRansomWorldAction worldAction = null)
        {
            var attempt = new NativeRansomAttempt
            {
                Binding = binding, MaterialJson = material.ToString(Formatting.None), WorldAction = worldAction,
                Releases = captives.Select(captive => captive.Leg).ToList(),
                Payment = gold.HasValue ? new NativeRansomPayment
                {
                    PayerId = payer?.StringId ?? "", RecipientId = recipient?.StringId ?? "", Amount = gold.Value
                } : null
            };
            var liveCaptives = captives.ToDictionary(captive => captive.Leg, captive => captive);
            return Journal?.Execute(key, attempt, () => OwnsLoadedWorld() && owns(),
                leg => liveCaptives[leg].CanRelease(), leg => liveCaptives[leg].Observe(),
                leg => liveCaptives[leg].Release(releaseAuthority),
                () => IsExactLivingHero(payer) && IsExactLivingHero(recipient)
                    ? new NativeRansomBalances { Payer = payer.Gold, Recipient = recipient.Gold } : null,
                amount => GiveGoldAction.ApplyBetweenCharacters(payer, recipient, amount, true));
        }

        internal static JObject PaymentReceipt(NativeRansomPayment payment) => new JObject
        {
            ["gold"] = payment.Amount, ["payerId"] = payment.PayerId, ["recipientId"] = payment.RecipientId,
            ["payerBefore"] = payment.Before.Payer, ["payerAfter"] = payment.After.Payer,
            ["recipientBefore"] = payment.Before.Recipient, ["recipientAfter"] = payment.After.Recipient
        };

        private static bool IsExactLivingHero(Hero hero)
            => hero?.IsAlive == true && ReferenceEquals(ReignObjectResolver.FindHero(hero.StringId), hero);

        internal sealed class Captive
        {
            private readonly Hero _hero;
            private readonly PartyBase _captor;
            private readonly TroopRoster _roster;
            private readonly CharacterObject _character;
            private readonly Func<PartyBase, bool> _ownsCaptor;

            internal Captive(Hero hero, string method, Func<PartyBase, bool> ownsCaptor)
            {
                _hero = hero;
                _captor = hero?.PartyBelongedToAsPrisoner;
                _roster = _captor?.PrisonRoster;
                _character = hero?.CharacterObject;
                _ownsCaptor = ownsCaptor;
                string mobile = _captor?.MobileParty?.StringId, settlement = _captor?.Settlement?.StringId;
                Leg = new NativeRansomRelease
                {
                    HeroId = hero?.StringId ?? "",
                    CaptorId = !string.IsNullOrWhiteSpace(mobile) ? "mobile:" + mobile
                        : !string.IsNullOrWhiteSpace(settlement) ? "settlement:" + settlement : "",
                    RosterCount = _roster != null && _character != null ? _roster.GetTroopCount(_character) : 0,
                    Method = method
                };
            }

            internal NativeRansomRelease Leg { get; }

            internal bool CanRelease() => _captor != null && _ownsCaptor(_captor)
                && !ReignRulerCampaignBehavior.BlocksRelease(_hero);

            internal NativeRansomCustody Observe() => new NativeRansomCustody
            {
                SameLivingHero = IsExactLivingHero(_hero) && ReferenceEquals(_hero.CharacterObject, _character),
                IsPrisoner = _hero?.IsPrisoner == true,
                HasCaptor = _hero?.PartyBelongedToAsPrisoner != null,
                OriginalCaptor = _captor != null && ReferenceEquals(_hero?.PartyBelongedToAsPrisoner, _captor),
                OriginalRosterCount = _roster != null && _character != null && ReferenceEquals(_captor?.PrisonRoster, _roster)
                    ? (int?)_roster.GetTroopCount(_character) : null
            };

            internal void Release(Hero authority)
            {
                if (Leg.Method == "choice") EndCaptivityAction.ApplyByReleasedByChoice(_hero, authority);
                else if (Leg.Method == "ransom") EndCaptivityAction.ApplyByRansom(_hero, authority);
                else EndCaptivityAction.ApplyByPeace(_hero, authority);
            }
        }
    }
}
