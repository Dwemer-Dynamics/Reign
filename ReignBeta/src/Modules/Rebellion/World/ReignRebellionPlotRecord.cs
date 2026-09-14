using TaleWorlds.SaveSystem;

namespace ReignBeta.World
{
    /// <summary>
    /// Save-authoritative state for a player conspiracy before it becomes a civil war.
    /// </summary>
    public sealed class ReignRebellionPlotRecord
    {
        [SaveableField(1)] public string PlotId;
        [SaveableField(2)] public string PlayerHeroStringId;
        [SaveableField(3)] public string PlayerClanStringId;
        [SaveableField(4)] public string ParentKingdomStringId;
        [SaveableField(5)] public string RulerHeroStringId;
        [SaveableField(6)] public string Status;
        [SaveableField(7)] public float CreatedDay;
        [SaveableField(8)] public float UpdatedDay;
        [SaveableField(9)] public string InformerHeroStringId;
        [SaveableField(10)] public string InformerClanStringId;
        [SaveableField(11)] public float ReportDispatchDay;
        [SaveableField(12)] public float ReportDeliveryDay;
        [SaveableField(13)] public string ReportLetterId;
        [SaveableField(14)] public string SummonsId;
        [SaveableField(15)] public string Resolution;
        [SaveableField(16)] public string RulerVerdict;
        [SaveableField(17)] public bool PledgesActivated;
        [SaveableField(18)] public string MovementId;
        [SaveableField(19)] public string CorrelationId;
        [SaveableField(20)] public int Revision;

        public ReignRebellionPlotRecord()
        {
            PlotId = string.Empty;
            PlayerHeroStringId = string.Empty;
            PlayerClanStringId = string.Empty;
            ParentKingdomStringId = string.Empty;
            RulerHeroStringId = string.Empty;
            Status = "planning";
            InformerHeroStringId = string.Empty;
            InformerClanStringId = string.Empty;
            ReportLetterId = string.Empty;
            SummonsId = string.Empty;
            Resolution = string.Empty;
            RulerVerdict = string.Empty;
            MovementId = string.Empty;
            CorrelationId = string.Empty;
            Revision = 1;
        }
    }
}
