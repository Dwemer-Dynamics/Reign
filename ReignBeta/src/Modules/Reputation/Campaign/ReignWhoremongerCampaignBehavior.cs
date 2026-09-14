using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ReignBeta.Integration;
using ReignBeta.Save;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;

namespace ReignBeta.Campaign
{
    /// <summary>
    /// Records genuine NPC arrivals through the existing durable world-history outbox.
    /// Entry identity is saved in the native save; no provider or server call runs here.
    /// </summary>
    public sealed class ReignWhoremongerCampaignBehavior : CampaignBehaviorBase
    {
        private List<string> _stateChunks = new List<string>();
        private JObject _state = new JObject();
        private bool _sessionReady;

        public override void RegisterEvents()
        {
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
            CampaignEvents.SettlementEntered.AddNonSerializedListener(this, OnSettlementEntered);
            CampaignEvents.OnSettlementLeftEvent.AddNonSerializedListener(this, OnSettlementLeft);
            CampaignEvents.HourlyTickEvent.AddNonSerializedListener(this, OnHourlyTick);
        }

        public override void SyncData(IDataStore dataStore)
        {
            if (dataStore.IsSaving)
                _stateChunks = ReignSavePayloadCodec.Encode(_state.ToString(Formatting.None));
            dataStore.SyncData("_reign_whoremongerTownEntryChunks", ref _stateChunks);
            if (dataStore.IsLoading)
            {
                _sessionReady = false;
                string text = _stateChunks != null && _stateChunks.Count > 0
                    ? ReignSavePayloadCodec.Decode(_stateChunks) : string.Empty;
                // Corrupt state must not silently reset durable entry identities.
                _state = string.IsNullOrWhiteSpace(text) ? new JObject() : JObject.Parse(text);
            }
        }

        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            // The current location is a baseline, never a fresh arrival on load/spawn.
            foreach (Hero hero in Hero.AllAliveHeroes.Where(hero => hero != null))
                ObserveLocation(hero, false);
            _sessionReady = true;
        }

        private void OnHourlyTick()
        {
            if (!_sessionReady || ReignCampaignInitializationGate.IsPending) return;
            foreach (Hero hero in Hero.AllAliveHeroes.Where(hero => hero != null))
            {
                if (_state[hero.StringId] == null) ObserveLocation(hero, false);
                else if (HeroLocation(hero) == null)
                    ((JObject)_state[hero.StringId])["settlementId"] = string.Empty;
            }
        }

        private JObject ObserveLocation(Hero hero, bool preserveLocation)
        {
            JObject record = _state[hero.StringId] as JObject;
            if (record == null)
            {
                record = new JObject { ["sequence"] = 0L };
                _state[hero.StringId] = record;
            }
            if (!preserveLocation)
                record["settlementId"] = HeroLocation(hero)?.StringId ?? string.Empty;
            return record;
        }

        private static Settlement HeroLocation(Hero hero)
        {
            return hero.CurrentSettlement ?? hero.PartyBelongedTo?.CurrentSettlement;
        }

        private static IEnumerable<Hero> ArrivingHeroes(MobileParty party, Hero hero)
        {
            var candidates = new List<Hero>();
            if (hero != null) candidates.Add(hero);
            if (party?.LeaderHero != null) candidates.Add(party.LeaderHero);
            if (party?.MemberRoster != null)
                foreach (var member in party.MemberRoster.GetTroopRoster())
                    if (member.Character?.IsHero == true && member.Character.HeroObject != null)
                        candidates.Add(member.Character.HeroObject);
            return candidates.Where(candidate => candidate != null)
                .GroupBy(candidate => candidate.StringId, StringComparer.Ordinal)
                .Select(group => group.First());
        }

        private void OnSettlementLeft(MobileParty party, Settlement settlement)
        {
            if (!_sessionReady) return;
            foreach (Hero hero in ArrivingHeroes(party, null))
            {
                JObject record = ObserveLocation(hero, true);
                if (string.Equals(record.Value<string>("settlementId"), settlement?.StringId, StringComparison.Ordinal))
                    record["settlementId"] = string.Empty;
            }
        }

        private void OnSettlementEntered(MobileParty party, Settlement settlement, Hero enteredHero)
        {
            if (!_sessionReady || settlement == null || ReignCampaignInitializationGate.IsPending) return;
            foreach (Hero hero in ArrivingHeroes(party, enteredHero))
            {
                JObject record = _state[hero.StringId] as JObject;
                if (record == null)
                {
                    ObserveLocation(hero, false);
                    continue; // Newly spawned Heroes establish a baseline only.
                }
                if (string.Equals(record.Value<string>("settlementId"), settlement.StringId, StringComparison.Ordinal))
                    continue; // Party and Hero callbacks describe the same arrival.
                record["settlementId"] = settlement.StringId;
                float adultAge = TaleWorlds.CampaignSystem.Campaign.Current?.Models?.AgeModel?.HeroComesOfAge ?? 18f;
                if (!settlement.IsTown || hero == Hero.MainHero || !hero.IsAlive
                    || hero.IsPrisoner || hero.Age < Math.Max(18f, adultAge)) continue;
                if (ReignWorldHistoryCampaignBehavior.Instance == null) continue;
                long sequence = (record.Value<long?>("sequence") ?? 0L) + 1L;
                record["sequence"] = sequence;
                double day = CampaignTime.Now.ToDays;
                string source = "whoremonger_town_entry|" + hero.StringId + "|"
                    + sequence.ToString(CultureInfo.InvariantCulture) + "|" + settlement.StringId;
                ReignWorldHistoryCampaignBehavior.Instance.RecordSocialOutcome("whoremonger", hero,
                    "visitor", source, hero.Name + " arrived in " + settlement.Name + ".",
                    new JObject { ["genuineTownEntry"] = true, ["isTown"] = true,
                        ["isPrisoner"] = false, ["townId"] = settlement.StringId,
                        ["entryDay"] = day, ["entrySequence"] = sequence });
            }
        }
    }
}
