using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Reign.Core.Contracts.Dialogue;
using ReignBeta.Runtime;
using ReignBeta.Integration;
using ReignBeta.Save;
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
            // The legacy JSON can exceed Bannerlord's signed-short archive entry limit.
            string json = null;
            List<string> chunks = dataStore.IsSaving
                ? ReignSavePayloadCodec.Encode(JsonConvert.SerializeObject(_ledger)) : null;
            dataStore.SyncData("_reign_participation_xp_v1", ref json);
            dataStore.SyncData("_reign_participation_xp_chunks_v2", ref chunks);
            if (dataStore.IsLoading)
            {
                // Recovered saves may carry the complete old ledger as one compressed envelope.
                if (!string.IsNullOrEmpty(json) && json.StartsWith("reign-gzip-v1:", StringComparison.Ordinal))
                    json = ReignSavePayloadCodec.DecodeLegacyArchiveEntry(json);
                if (chunks != null && chunks.Count > 0) json = ReignSavePayloadCodec.Decode(chunks);
                _ledger = string.IsNullOrWhiteSpace(json) ? new ReignXpLedger()
                    : JsonConvert.DeserializeObject<ReignXpLedger>(json) ?? new ReignXpLedger();
                LastOptions = new ReignXpOptions();
            }
        }

        internal void Award(string receipt, ReignXpSkill skill, double baseXp, ReignXpOptions options, ReignXpReason reason)
        {
            if (this != Instance || Hero.MainHero == null || !_ledger.TryClaim(receipt)) return;
            float xp = ReignXpRules.Amount(baseXp, options, reason);
            if (xp <= 0) return;
            SkillObject native = skill == ReignXpSkill.Steward ? DefaultSkills.Steward
                : skill == ReignXpSkill.Trade ? DefaultSkills.Trade
                : skill == ReignXpSkill.Leadership ? DefaultSkills.Leadership
                : skill == ReignXpSkill.Tactics ? DefaultSkills.Tactics : DefaultSkills.Charm;
            try
            {
                Hero.MainHero.AddSkillXp(native, xp);
                ReignLog.Info("Reign XP receipt=" + receipt + " skill=" + skill + " baseXp=" + xp
                    + " reason=" + reason + " universalMultiplier=" + options.Multiplier
                    + " categoryMultiplier=" + ReignXpRules.CategoryMultiplier(reason, options));
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
            if (people.Count > 0) Award("charm:" + phaseKey, ReignXpSkill.Charm, ReignXpRules.ConversationXp, options, ReignXpReason.SocialCharm);
            return true;
        }

        internal void FinishSocialPhase(string phaseKey, ReignXpOptions options)
        {
            if (this != Instance || !_ledger.Phases.TryGetValue(phaseKey, out var people)) return;
            var skill = ReignXpRules.SelectPhaseSkill(phaseKey, people.Values);
            if (skill.HasValue) Award("bonus:" + phaseKey, skill.Value, ReignXpRules.SocialBonusXp, options, ReignXpReason.SocialBonus);
            else _ledger.TryClaim("bonus:" + phaseKey);
            _ledger.Phases.Remove(phaseKey);
        }
    }
}
