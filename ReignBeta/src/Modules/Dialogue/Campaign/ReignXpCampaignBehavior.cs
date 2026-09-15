using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Reign.Core.Contracts.Dialogue;
using ReignBeta.Runtime;
using ReignBeta.Integration;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;

namespace ReignBeta.Campaign
{
    public sealed class ReignXpCampaignBehavior : CampaignBehaviorBase
    {
        private ReignXpLedger _ledger = new ReignXpLedger();
        internal ReignXpOptions LastOptions { get; set; } = new ReignXpOptions();
        public static ReignXpCampaignBehavior Instance => TaleWorlds.CampaignSystem.Campaign.Current?
            .GetCampaignBehavior<ReignXpCampaignBehavior>();

        public override void RegisterEvents() { }

        public override void SyncData(IDataStore dataStore)
        {
            string json = dataStore.IsSaving ? JsonConvert.SerializeObject(_ledger) : string.Empty;
            dataStore.SyncData("_reign_participation_xp_v1", ref json);
            if (dataStore.IsLoading)
            {
                _ledger = string.IsNullOrWhiteSpace(json) ? new ReignXpLedger()
                    : JsonConvert.DeserializeObject<ReignXpLedger>(json) ?? new ReignXpLedger();
                LastOptions = new ReignXpOptions();
            }
        }

        internal void Award(string receipt, ReignXpSkill skill, double baseXp, ReignXpOptions options)
        {
            if (this != Instance || Hero.MainHero == null || !_ledger.TryClaim(receipt)) return;
            float xp = ReignXpRules.Amount(baseXp, options);
            if (xp <= 0) return;
            SkillObject native = skill == ReignXpSkill.Steward ? DefaultSkills.Steward
                : skill == ReignXpSkill.Trade ? DefaultSkills.Trade
                : skill == ReignXpSkill.Leadership ? DefaultSkills.Leadership
                : skill == ReignXpSkill.Tactics ? DefaultSkills.Tactics : DefaultSkills.Charm;
            try
            {
                Hero.MainHero.AddSkillXp(native, xp);
                ReignLog.Info("Reign XP receipt=" + receipt + " skill=" + skill + " baseXp=" + xp);
            }
            catch (Exception ex)
            {
                _ledger.Consumed.Remove(receipt);
                ReignLog.Warn("Reign XP award failed receipt=" + receipt + ": " + ex.Message);
            }
        }

        internal bool RecordSocialExchange(string phaseKey, string turnId, IEnumerable<Hero> heroes, ReignXpOptions options)
        {
            if (this != Instance || Hero.MainHero == null) return false;
            var people = (heroes ?? Enumerable.Empty<Hero>()).Where(h => h != null && h != Hero.MainHero)
                .Select(h => new ReignXpParticipant { HeroId = h.StringId,
                    Steward = h.GetSkillValue(DefaultSkills.Steward), Trade = h.GetSkillValue(DefaultSkills.Trade),
                    Leadership = h.GetSkillValue(DefaultSkills.Leadership), Tactics = h.GetSkillValue(DefaultSkills.Tactics) }).ToList();
            if (!_ledger.RecordExchange(phaseKey, turnId, people)) return false;
            if (people.Count > 0) Award("charm:" + phaseKey, ReignXpSkill.Charm, ReignXpRules.ConversationXp, options);
            return true;
        }

        internal void FinishSocialPhase(string phaseKey, ReignXpOptions options)
        {
            if (this != Instance || !_ledger.Phases.TryGetValue(phaseKey, out var people)) return;
            var skill = ReignXpRules.SelectPhaseSkill(phaseKey, people.Values);
            if (skill.HasValue) Award("bonus:" + phaseKey, skill.Value, ReignXpRules.SocialBonusXp, options);
            else _ledger.TryClaim("bonus:" + phaseKey);
            _ledger.Phases.Remove(phaseKey);
        }
    }
}
