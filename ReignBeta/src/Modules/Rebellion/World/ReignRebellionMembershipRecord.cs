using TaleWorlds.SaveSystem;

namespace ReignBeta.World
{
    public sealed class ReignRebellionMembershipRecord
    {
        [SaveableField(1)] public string MovementId;
        [SaveableField(2)] public string ClanStringId;
        [SaveableField(3)] public string LeaderHeroStringId;
        [SaveableField(4)] public string Side;
        [SaveableField(5)] public float SupportScore;
        [SaveableField(6)] public int SwitchCount;
        [SaveableField(7)] public float LastSwitchDay;
        [SaveableField(8)] public bool PlayerChoiceRequired;
        [SaveableField(9)] public bool IsCommitted;
        [SaveableField(10)] public string LastReason;
        [SaveableField(11)] public string JudgmentIfLoyalistsWin;
        [SaveableField(12)] public string JudgmentIfRebelsWin;
        [SaveableField(13)] public string OriginalKingdomStringId;
        [SaveableField(14)] public int RelationToRebelAtJoin;
        [SaveableField(15)] public int RelationToRulerAtJoin;
        [SaveableField(16)] public float JoinedDay;
        [SaveableField(17)] public string JoinedBy;
        [SaveableField(18)] public int FateRoll;
        [SaveableField(19)] public int FateScore;
        [SaveableField(20)] public string FateOutcome;
        [SaveableField(21)] public bool FateApplied;
        [SaveableField(22)] public bool RelationBonusApplied;
        [SaveableField(23)] public bool IsDefeated;

        public ReignRebellionMembershipRecord()
        {
            MovementId = string.Empty;
            ClanStringId = string.Empty;
            LeaderHeroStringId = string.Empty;
            Side = "loyalist";
            LastReason = string.Empty;
            JudgmentIfLoyalistsWin = "conditional_pardon";
            JudgmentIfRebelsWin = "conditional_pardon";
            OriginalKingdomStringId = string.Empty;
            JoinedBy = string.Empty;
            FateOutcome = string.Empty;
        }
    }
}
