using System;
using TaleWorlds.SaveSystem;

namespace ReignBeta.World
{
    public sealed class ReignWarOriginRecord
    {
        [SaveableField(1)] public string WarId;
        [SaveableField(2)] public string AggressorKingdomStringId;
        [SaveableField(3)] public string DefenderKingdomStringId;
        [SaveableField(4)] public string AggressorRulerHeroStringId;
        [SaveableField(5)] public string DefenderRulerHeroStringId;
        [SaveableField(6)] public float DeclarationDay;
        [SaveableField(7)] public string Cause;
        [SaveableField(8)] public string SourceActionId;
        [SaveableField(9)] public string ParentWarId;
        [SaveableField(10)] public string OriginKind;
        [SaveableField(11)] public bool IsActive;
        [SaveableField(12)] public float EndedDay;

        public ReignWarOriginRecord()
        {
            WarId = Guid.NewGuid().ToString("N");
            AggressorKingdomStringId = string.Empty;
            DefenderKingdomStringId = string.Empty;
            AggressorRulerHeroStringId = string.Empty;
            DefenderRulerHeroStringId = string.Empty;
            Cause = string.Empty;
            SourceActionId = string.Empty;
            ParentWarId = string.Empty;
            OriginKind = "direct";
            IsActive = true;
        }
    }
}
