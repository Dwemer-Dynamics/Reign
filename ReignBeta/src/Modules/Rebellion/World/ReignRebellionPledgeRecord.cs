using TaleWorlds.SaveSystem;

namespace ReignBeta.World
{
    /// <summary>
    /// One lord's durable answer to a request for support before declaration.
    /// The clan remains in its current realm until the plot becomes an active rebellion.
    /// </summary>
    public sealed class ReignRebellionPledgeRecord
    {
        [SaveableField(1)] public string PledgeId;
        [SaveableField(2)] public string PlotId;
        [SaveableField(3)] public string LordHeroStringId;
        [SaveableField(4)] public string ClanStringId;
        [SaveableField(5)] public string OriginalKingdomStringId;
        [SaveableField(6)] public string Decision;
        [SaveableField(7)] public string DecisionReason;
        [SaveableField(8)] public float RequestedDay;
        [SaveableField(9)] public float RespondedDay;
        [SaveableField(10)] public bool ReportedToRuler;
        [SaveableField(11)] public bool Activated;
        [SaveableField(12)] public float ActivatedDay;
        [SaveableField(13)] public string MovementId;
        [SaveableField(14)] public int RelationToPlayerAtDecision;
        [SaveableField(15)] public int RelationToRulerAtDecision;
        [SaveableField(16)] public int DecisionRoll;
        [SaveableField(17)] public int DecisionScore;
        [SaveableField(18)] public string RequestChannel;
        [SaveableField(19)] public string ActionId;

        public ReignRebellionPledgeRecord()
        {
            PledgeId = string.Empty;
            PlotId = string.Empty;
            LordHeroStringId = string.Empty;
            ClanStringId = string.Empty;
            OriginalKingdomStringId = string.Empty;
            Decision = "pending";
            DecisionReason = string.Empty;
            MovementId = string.Empty;
            RequestChannel = "conversation";
            ActionId = string.Empty;
        }
    }
}
