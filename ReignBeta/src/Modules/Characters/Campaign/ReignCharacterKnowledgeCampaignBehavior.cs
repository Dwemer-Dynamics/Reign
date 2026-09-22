using System;
using System.Collections.Generic;
using System.Linq;
using ReignBeta.Knowledge;
using TaleWorlds.CampaignSystem;

namespace ReignBeta.Campaign
{
    /// <summary>
    /// Persistent authorization foundation for the future Spymaster dossier.
    /// Native Bannerlord/Reign presentation deliberately does not consult it.
    /// </summary>
    public sealed class ReignCharacterKnowledgeCampaignBehavior : CampaignBehaviorBase
    {
        private List<ReignCharacterKnowledgeRecord> _records = new List<ReignCharacterKnowledgeRecord>();

        public static ReignCharacterKnowledgeCampaignBehavior Instance { get; private set; }

        public override void RegisterEvents()
        {
            Instance = this;
        }

        public override void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData("_reign_character_knowledge", ref _records);
            _records = (_records ?? new List<ReignCharacterKnowledgeRecord>())
                .Where(record => record != null && !string.IsNullOrWhiteSpace(record.HeroStringId))
                .GroupBy(record => record.HeroStringId, StringComparer.OrdinalIgnoreCase)
                .Select(MergeRecords)
                .OrderBy(record => record.HeroStringId, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public bool IsDiscovered(Hero hero, ReignCharacterKnowledgeAspect aspect)
        {
            if (hero == null || aspect == ReignCharacterKnowledgeAspect.None)
            {
                return false;
            }

            if (hero == Hero.MainHero)
            {
                return true;
            }

            return Find(hero.StringId)?.Knows(aspect) == true;
        }

        public bool Discover(Hero hero, ReignCharacterKnowledgeAspect aspect, string sourceId, float campaignDay)
        {
            if (hero == null || hero == Hero.MainHero)
            {
                return false;
            }

            ReignCharacterKnowledgeRecord record = Find(hero.StringId);
            if (record == null)
            {
                record = new ReignCharacterKnowledgeRecord { HeroStringId = hero.StringId };
                _records.Add(record);
            }

            return record.Discover(aspect, sourceId, campaignDay);
        }

        public IReadOnlyList<ReignCharacterKnowledgeRecord> Snapshot()
        {
            return (_records ?? new List<ReignCharacterKnowledgeRecord>()).ToList();
        }

        private ReignCharacterKnowledgeRecord Find(string heroStringId)
        {
            return (_records ?? new List<ReignCharacterKnowledgeRecord>())
                .FirstOrDefault(record => string.Equals(record?.HeroStringId, heroStringId, StringComparison.OrdinalIgnoreCase));
        }

        private static ReignCharacterKnowledgeRecord MergeRecords(IGrouping<string, ReignCharacterKnowledgeRecord> group)
        {
            ReignCharacterKnowledgeRecord merged = new ReignCharacterKnowledgeRecord { HeroStringId = group.Key };
            foreach (ReignCharacterKnowledgeRecord record in group)
            {
                if (record.Knows(ReignCharacterKnowledgeAspect.Skills)
                    && !merged.Knows(ReignCharacterKnowledgeAspect.Skills))
                {
                    merged.Discover(ReignCharacterKnowledgeAspect.Skills, record.SkillsSourceId, record.SkillsDiscoveredDay);
                }
                if (record.Knows(ReignCharacterKnowledgeAspect.Relationships)
                    && !merged.Knows(ReignCharacterKnowledgeAspect.Relationships))
                {
                    merged.Discover(ReignCharacterKnowledgeAspect.Relationships, record.RelationshipsSourceId, record.RelationshipsDiscoveredDay);
                }
            }
            return merged;
        }
    }
}
