using System;
using System.Collections.Generic;
using TaleWorlds.SaveSystem;

namespace ReignBeta.Campaign
{
    public enum ReignArrestPhase
    {
        Proposed = 0,
        AwaitingConfirmation = 1,
        Refused = 2,
        DuelPending = 3,
        BattlePending = 4,
        Captured = 5,
        Released = 6,
        Escaped = 7,
        Rescinded = 8,
        Cancelled = 9
    }

    public enum ReignArrestContextKind
    {
        Unknown = 0,
        PlayerSettlement = 1,
        PlayerParty = 2,
        PartyEncounter = 3
    }

    public enum ReignArrestChargeSeverity
    {
        Minor = 0,
        Serious = 1,
        Grave = 2,
        Capital = 3
    }

    public sealed class ReignArrestCase
    {
        [SaveableField(1)] public int Version = 1;
        [SaveableField(2)] public string CaseId = "arrest_" + Guid.NewGuid().ToString("N");
        [SaveableField(3)] public string PlayerHeroStringId = string.Empty;
        [SaveableField(4)] public string AccusedHeroStringId = string.Empty;
        [SaveableField(5)] public int PhaseValue;
        [SaveableField(6)] public int ContextKindValue;
        [SaveableField(7)] public string SettlementStringId = string.Empty;
        [SaveableField(8)] public string CustodyPartyStringId = string.Empty;
        [SaveableField(9)] public string RawAccusation = string.Empty;
        [SaveableField(10)] public string ChargeCategory = string.Empty;
        [SaveableField(11)] public int SeverityValue;
        [SaveableField(12)] public bool CauseEstablished;
        [SaveableField(13)] public string EvidenceIdsCsv = string.Empty;
        [SaveableField(14)] public string EvidenceStatus = "unsupported";
        [SaveableField(15)] public int RelationshipDeltaApplied;
        [SaveableField(16)] public bool RelationshipEffectSynchronized;
        [SaveableField(17)] public int ReputationValue;
        [SaveableField(18)] public string ReputationTagId = string.Empty;
        [SaveableField(19)] public bool ReputationSynchronized;
        [SaveableField(20)] public bool AccusationActive = true;
        [SaveableField(21)] public bool SurrenderAccepted;
        [SaveableField(22)] public string SurrenderDisposition = "pending";
        [SaveableField(23)] public string DuelActionId = string.Empty;
        [SaveableField(24)] public string BattlePartyStringId = string.Empty;
        [SaveableField(25)] public float ProposedDay;
        [SaveableField(26)] public float ConfirmedDay = -1f;
        [SaveableField(27)] public float CapturedDay = -1f;
        [SaveableField(28)] public float ReleasedDay = -1f;
        [SaveableField(29)] public string LastOutcome = string.Empty;
        [SaveableField(30)] public List<string> History = new List<string>();
        [SaveableField(31)] public string ReactionSnapshot = string.Empty;
        [SaveableField(32)] public float ReactionDay = -1f;

        public ReignArrestPhase Phase
        {
            get { return (ReignArrestPhase)PhaseValue; }
            set { PhaseValue = (int)value; }
        }

        public ReignArrestContextKind ContextKind
        {
            get { return (ReignArrestContextKind)ContextKindValue; }
            set { ContextKindValue = (int)value; }
        }

        public ReignArrestChargeSeverity Severity
        {
            get { return (ReignArrestChargeSeverity)SeverityValue; }
            set { SeverityValue = (int)value; }
        }

        public bool IsOpen
        {
            get
            {
                return Phase != ReignArrestPhase.Released
                    && Phase != ReignArrestPhase.Escaped
                    && Phase != ReignArrestPhase.Rescinded
                    && Phase != ReignArrestPhase.Cancelled;
            }
        }
    }
}
