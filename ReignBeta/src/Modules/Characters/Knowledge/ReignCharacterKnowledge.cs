using System;
using TaleWorlds.SaveSystem;

namespace ReignBeta.Knowledge
{
    [Flags]
    public enum ReignCharacterKnowledgeAspect
    {
        None = 0,
        Skills = 1,
        Relationships = 2
    }

    public sealed class ReignCharacterKnowledgeRecord
    {
        [SaveableField(1)] public string HeroStringId = string.Empty;
        [SaveableField(2)] public int KnownAspectMask;
        [SaveableField(3)] public float SkillsDiscoveredDay = -1f;
        [SaveableField(4)] public string SkillsSourceId = string.Empty;
        [SaveableField(5)] public float RelationshipsDiscoveredDay = -1f;
        [SaveableField(6)] public string RelationshipsSourceId = string.Empty;

        public bool Knows(ReignCharacterKnowledgeAspect aspect)
        {
            return aspect != ReignCharacterKnowledgeAspect.None
                && (KnownAspectMask & (int)aspect) == (int)aspect;
        }

        public bool Discover(ReignCharacterKnowledgeAspect aspect, string sourceId, float campaignDay)
        {
            if (aspect != ReignCharacterKnowledgeAspect.Skills
                && aspect != ReignCharacterKnowledgeAspect.Relationships)
            {
                throw new ArgumentOutOfRangeException(nameof(aspect), "Discover exactly one knowledge aspect at a time.");
            }

            if (Knows(aspect))
            {
                return false;
            }

            KnownAspectMask |= (int)aspect;
            if (aspect == ReignCharacterKnowledgeAspect.Skills)
            {
                SkillsDiscoveredDay = campaignDay;
                SkillsSourceId = sourceId ?? string.Empty;
            }
            else
            {
                RelationshipsDiscoveredDay = campaignDay;
                RelationshipsSourceId = sourceId ?? string.Empty;
            }

            return true;
        }
    }
}
