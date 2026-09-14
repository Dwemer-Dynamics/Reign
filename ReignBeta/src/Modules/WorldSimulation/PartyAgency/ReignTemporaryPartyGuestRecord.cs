using System;
using TaleWorlds.SaveSystem;

namespace ReignBeta.PartyAgency
{
    public enum ReignTemporaryGuestPhase
    {
        Active = 0,
        ReviewDue = 1,
        Departing = 2,
        Returning = 3,
        Completed = 4,
        HostileBanishmentPending = 5
    }

    public enum ReignTemporaryGuestTermKind
    {
        OpenEnded = 0,
        Fixed = 1
    }

    public sealed class ReignTemporaryPartyGuestRecord
    {
        [SaveableField(1)] public string AgreementId;
        [SaveableField(2)] public string HeroStringId;
        [SaveableField(3)] public string OriginalClanStringId;
        [SaveableField(4)] public string OriginalKingdomStringId;
        [SaveableField(5)] public string OriginalCompanionClanStringId;
        [SaveableField(6)] public string SourcePartyStringId;
        [SaveableField(7)] public string Purpose;
        [SaveableField(8)] public int TermKindValue;
        [SaveableField(9)] public float StartedDay;
        [SaveableField(10)] public float ReviewDueDay;
        [SaveableField(11)] public float FixedTermDays;
        [SaveableField(12)] public int PhaseValue;
        [SaveableField(13)] public bool ArmyExitAccepted;
        [SaveableField(14)] public bool HostilityWarningAcknowledged;
        [SaveableField(15)] public string HostileFactionStringId;
        [SaveableField(16)] public string ReturnSettlementStringId;
        [SaveableField(17)] public float ReturnDueDay;
        [SaveableField(18)] public float CampX;
        [SaveableField(19)] public float CampY;
        [SaveableField(20)] public bool CampIsOnLand;
        [SaveableField(21)] public bool SourcePartyHadCustomName;
        [SaveableField(22)] public string SourcePartyOriginalName;
        [SaveableField(23)] public bool OwnFactionCombatPending;
        [SaveableField(24)] public bool TemporarilyExcludedFromBattle;
        [SaveableField(25)] public string LastReviewReason;
        [SaveableField(26)] public float LastStateChangeDay;
        [SaveableField(27)] public float ProtectedCampMorale;
        [SaveableField(28)] public bool ProtectedCampMoraleCaptured;

        public ReignTemporaryPartyGuestRecord()
        {
            AgreementId = "party_guest_" + Guid.NewGuid().ToString("N");
            HeroStringId = string.Empty;
            OriginalClanStringId = string.Empty;
            OriginalKingdomStringId = string.Empty;
            OriginalCompanionClanStringId = string.Empty;
            SourcePartyStringId = string.Empty;
            Purpose = string.Empty;
            TermKind = ReignTemporaryGuestTermKind.OpenEnded;
            Phase = ReignTemporaryGuestPhase.Active;
            HostileFactionStringId = string.Empty;
            ReturnSettlementStringId = string.Empty;
            SourcePartyOriginalName = string.Empty;
            LastReviewReason = string.Empty;
        }

        public ReignTemporaryGuestTermKind TermKind
        {
            get { return (ReignTemporaryGuestTermKind)TermKindValue; }
            set { TermKindValue = (int)value; }
        }

        public ReignTemporaryGuestPhase Phase
        {
            get { return (ReignTemporaryGuestPhase)PhaseValue; }
            set { PhaseValue = (int)value; }
        }

        public bool IsLive
        {
            get { return Phase != ReignTemporaryGuestPhase.Completed; }
        }
    }
}
