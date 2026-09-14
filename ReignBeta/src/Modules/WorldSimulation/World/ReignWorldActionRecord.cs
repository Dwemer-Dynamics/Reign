using System;
using TaleWorlds.SaveSystem;

namespace ReignBeta.World
{
    public class ReignWorldActionRecord
    {
        [SaveableField(1)]
        public string ActionId;

        [SaveableField(2)]
        public int TypeValue;

        [SaveableField(3)]
        public int StatusValue;

        [SaveableField(4)]
        public string Source;

        [SaveableField(5)]
        public string ActorHeroStringId;

        [SaveableField(6)]
        public string ActorKingdomStringId;

        [SaveableField(7)]
        public string TargetHeroStringId;

        [SaveableField(8)]
        public string TargetKingdomStringId;

        [SaveableField(9)]
        public string TargetSettlementStringId;

        [SaveableField(10)]
        public string Reason;

        [SaveableField(11)]
        public string TermsJson;

        [SaveableField(12)]
        public float CreatedDay;

        [SaveableField(13)]
        public float ExecuteAfterDay;

        [SaveableField(14)]
        public float LastAttemptDay;

        [SaveableField(15)]
        public int AttemptCount;

        [SaveableField(16)]
        public int MaxAttempts;

        [SaveableField(17)]
        public string FailureReason;

        [SaveableField(18)]
        public int PlanStageValue;

        [SaveableField(19)]
        public int DesiredStrength;

        [SaveableField(20)]
        public int MinimumTroops;

        [SaveableField(21)]
        public bool RequiresAcceptance;

        [SaveableField(22)]
        public string AcceptedByHeroStringId;

        [SaveableField(23)]
        public float AcceptedDay;

        [SaveableField(24)]
        public string ActorClanStringId;

        [SaveableField(25)]
        public string TargetClanStringId;

        [SaveableField(26)]
        public string SupporterClanIdsCsv;

        [SaveableField(27)]
        public string AuthorizationMode;

        [SaveableField(28)]
        public string NegotiationId;

        [SaveableField(29)]
        public string TermsHash;

        [SaveableField(30)]
        public string ExecutionPhase;

        [SaveableField(31)]
        public string ExecutionSnapshotJson;

        [SaveableField(32)]
        public string NegotiatedCommand;

        public ReignWorldActionRecord()
        {
            ActionId = Guid.NewGuid().ToString("N");
            Type = ReignWorldActionType.Unknown;
            Status = ReignWorldActionStatus.Proposed;
            Source = string.Empty;
            ActorHeroStringId = string.Empty;
            ActorKingdomStringId = string.Empty;
            TargetHeroStringId = string.Empty;
            TargetKingdomStringId = string.Empty;
            TargetSettlementStringId = string.Empty;
            ActorClanStringId = string.Empty;
            TargetClanStringId = string.Empty;
            SupporterClanIdsCsv = string.Empty;
            AcceptedByHeroStringId = string.Empty;
            AuthorizationMode = string.Empty;
            NegotiationId = string.Empty;
            TermsHash = string.Empty;
            ExecutionPhase = "idle";
            ExecutionSnapshotJson = "{}";
            NegotiatedCommand = string.Empty;
            Reason = string.Empty;
            TermsJson = string.Empty;
            FailureReason = string.Empty;
            PlanStage = ReignStrategicPlanStage.None;
            MaxAttempts = 3;
            MinimumTroops = 40;
            DesiredStrength = 350;
        }

        public ReignWorldActionType Type
        {
            get { return (ReignWorldActionType)TypeValue; }
            set { TypeValue = (int)value; }
        }

        public ReignWorldActionStatus Status
        {
            get { return (ReignWorldActionStatus)StatusValue; }
            set { StatusValue = (int)value; }
        }

        public ReignStrategicPlanStage PlanStage
        {
            get { return (ReignStrategicPlanStage)PlanStageValue; }
            set { PlanStageValue = (int)value; }
        }

        public bool IsTerminal
        {
            get
            {
                return Status == ReignWorldActionStatus.Completed
                    || Status == ReignWorldActionStatus.Failed
                    || Status == ReignWorldActionStatus.Rejected
                    || Status == ReignWorldActionStatus.Cancelled;
            }
        }
    }
}
