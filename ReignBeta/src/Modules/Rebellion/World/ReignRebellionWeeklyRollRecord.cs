using TaleWorlds.SaveSystem;

namespace ReignBeta.World
{
    /// <summary>
    /// The latest authoritative weekly rebellion roll for one vassal clan.
    /// Saving the roll prevents reloads or server availability from rerolling history.
    /// </summary>
    public sealed class ReignRebellionWeeklyRollRecord
    {
        [SaveableField(1)] public string KingdomStringId;
        [SaveableField(2)] public string ClanStringId;
        [SaveableField(3)] public int WeekIndex;
        [SaveableField(4)] public int Roll;
        [SaveableField(5)] public int RulerRelation;
        [SaveableField(6)] public bool WasEligible;
        [SaveableField(7)] public bool Triggered;
        [SaveableField(8)] public float RolledDay;
        [SaveableField(9)] public bool TestOverride;
        [SaveableField(10)] public string TestRunId;
        [SaveableField(11)] public string ExclusionReason;
        [SaveableField(12)] public int PersonalAffinityToRuler;
        [SaveableField(13)] public int RulerPublicStanding;
        [SaveableField(14)] public int RulerStandingRevision;

        public ReignRebellionWeeklyRollRecord()
        {
            KingdomStringId = string.Empty;
            ClanStringId = string.Empty;
            WeekIndex = -1;
            TestRunId = string.Empty;
            ExclusionReason = string.Empty;
        }
    }
}
