using TaleWorlds.SaveSystem;

namespace ReignBeta.World
{
    /// <summary>
    /// The ruler's delivered order to answer a reported conspiracy in person.
    /// The deadline is measured from delivery and does not pause campaign time.
    /// </summary>
    public sealed class ReignRebellionSummonsRecord
    {
        [SaveableField(1)] public string SummonsId;
        [SaveableField(2)] public string PlotId;
        [SaveableField(3)] public string RulerHeroStringId;
        [SaveableField(4)] public string PlayerHeroStringId;
        [SaveableField(5)] public string InformerHeroStringId;
        [SaveableField(6)] public string Status;
        [SaveableField(7)] public float IssuedDay;
        [SaveableField(8)] public float DeliveryDay;
        [SaveableField(9)] public float DeadlineDay;
        [SaveableField(10)] public float AnsweredDay;
        [SaveableField(11)] public string PlayerResponse;
        [SaveableField(12)] public string RulerVerdict;
        [SaveableField(13)] public string LetterId;
        [SaveableField(14)] public bool ConsequenceApplied;
        [SaveableField(15)] public string Consequence;

        public ReignRebellionSummonsRecord()
        {
            SummonsId = string.Empty;
            PlotId = string.Empty;
            RulerHeroStringId = string.Empty;
            PlayerHeroStringId = string.Empty;
            InformerHeroStringId = string.Empty;
            Status = "dispatched";
            PlayerResponse = string.Empty;
            RulerVerdict = string.Empty;
            LetterId = string.Empty;
            Consequence = string.Empty;
        }
    }
}
