using System;
using TaleWorlds.SaveSystem;

namespace ReignBeta.World
{
    public sealed class ReignRebellionMovementRecord
    {
        [SaveableField(1)] public string MovementId;
        [SaveableField(2)] public string ParentKingdomStringId;
        [SaveableField(3)] public string RebelKingdomStringId;
        [SaveableField(4)] public string LeaderClanStringId;
        [SaveableField(5)] public string LeaderHeroStringId;
        [SaveableField(6)] public string OriginalRulerHeroStringId;
        [SaveableField(7)] public string Objective;
        [SaveableField(8)] public string Stage;
        [SaveableField(9)] public float Pressure;
        [SaveableField(10)] public float RelationshipPressure;
        [SaveableField(11)] public float TraitPressure;
        [SaveableField(12)] public float FactualPressure;
        [SaveableField(13)] public float Viability;
        [SaveableField(14)] public float Readiness;
        [SaveableField(15)] public string DemandJson;
        [SaveableField(16)] public string CorrelationId;
        [SaveableField(17)] public float CreatedDay;
        [SaveableField(18)] public float UpdatedDay;
        [SaveableField(19)] public float CivilWarStartedDay;
        [SaveableField(20)] public float CooldownUntilDay;
        [SaveableField(21)] public string OriginalStrongholdIdsCsv;
        [SaveableField(22)] public string AdvantageSide;
        [SaveableField(23)] public float AdvantageSinceDay;
        [SaveableField(24)] public float RebelBattleScore;
        [SaveableField(25)] public bool IsPlayerKingdom;
        [SaveableField(26)] public bool UltimatumIssued;
        [SaveableField(27)] public bool StalemateNegotiationOpened;
        [SaveableField(28)] public bool ResolutionApplied;
        [SaveableField(29)] public string Resolution;
        [SaveableField(30)] public string ExecutionPhase;
        [SaveableField(31)] public string ExecutionSnapshotJson;
        [SaveableField(32)] public float LastEvaluationDay;
        [SaveableField(33)] public string OriginalParentStrongholdIdsCsv;
        [SaveableField(34)] public string OriginalRebelStrongholdIdsCsv;
        [SaveableField(35)] public bool IsPlayerLed;
        [SaveableField(36)] public string WinningSide;
        [SaveableField(37)] public string ResolutionCause;
        [SaveableField(38)] public string WinningLeaderHeroStringId;
        [SaveableField(39)] public bool JudgmentsPending;
        [SaveableField(40)] public string OriginalClanKingdomsJson;
        [SaveableField(41)] public bool RelationshipRulesMigrated;
        [SaveableField(42)] public int OutbreakWeekIndex;
        [SaveableField(43)] public string BackingStatus;
        [SaveableField(44)] public string BackingAskedKingdomStringId;
        [SaveableField(45)] public string BackingSponsorKingdomStringId;
        [SaveableField(46)] public float BackingRequestDay;
        [SaveableField(47)] public string BackingActionId;
        [SaveableField(48)] public string BackingTerminalReason;
        [SaveableField(49)] public float RebelLeaderCapturedDay;
        [SaveableField(50)] public float ChallengedRulerCapturedDay;

        public ReignRebellionMovementRecord()
        {
            MovementId = "reb_" + Guid.NewGuid().ToString("N");
            ParentKingdomStringId = string.Empty;
            RebelKingdomStringId = string.Empty;
            LeaderClanStringId = string.Empty;
            LeaderHeroStringId = string.Empty;
            OriginalRulerHeroStringId = string.Empty;
            Objective = "redress";
            Stage = "grievance";
            DemandJson = "{}";
            CorrelationId = MovementId;
            OriginalStrongholdIdsCsv = string.Empty;
            AdvantageSide = string.Empty;
            RebelBattleScore = 50f;
            Resolution = string.Empty;
            ExecutionPhase = "idle";
            ExecutionSnapshotJson = "{}";
            OriginalParentStrongholdIdsCsv = string.Empty;
            OriginalRebelStrongholdIdsCsv = string.Empty;
            WinningSide = string.Empty;
            ResolutionCause = string.Empty;
            WinningLeaderHeroStringId = string.Empty;
            OriginalClanKingdomsJson = "{}";
            OutbreakWeekIndex = -1;
            BackingStatus = "pending_evaluation";
            BackingAskedKingdomStringId = string.Empty;
            BackingSponsorKingdomStringId = string.Empty;
            BackingActionId = string.Empty;
            BackingTerminalReason = string.Empty;
            RebelLeaderCapturedDay = -1f;
            ChallengedRulerCapturedDay = -1f;
        }
    }
}
