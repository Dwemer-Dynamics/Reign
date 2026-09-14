using System.Collections.Generic;

namespace ReignBeta.Court
{
    public sealed partial class ReignRulerDocketState
    {
        public List<ReignNobleVisitorStay> NobleVisitorStays { get; set; } = new List<ReignNobleVisitorStay>();
    }
    public sealed class ReignNobleVisitorStay
    {
        public string StayId { get; set; } = string.Empty;
        public string MatterId { get; set; } = string.Empty;
        public string CampaignId { get; set; } = string.Empty;
        public string TimelineId { get; set; } = string.Empty;
        public string ReignId { get; set; } = string.Empty;
        public string RulerHeroId { get; set; } = string.Empty;
        public string HostKingdomId { get; set; } = string.Empty;
        public string HostSettlementId { get; set; } = string.Empty;
        public string Purpose { get; set; } = string.Empty;
        public string CandidateHeroId { get; set; } = string.Empty;
        public string InitiatorHeroId { get; set; } = string.Empty;
        public bool IsMatchmaking { get; set; }
        public bool IsSoloRomanticApproach { get; set; }
        public double ArrivalDay { get; set; }
        public double ActualArrivalDay { get; set; } = -1;
        public int StayDays { get; set; }
        public double DepartureDay { get; set; }
        public bool Arrived { get; set; }
        public bool Departed { get; set; }
        public string DepartureReason { get; set; } = string.Empty;
        public string LastError { get; set; } = string.Empty;
        public List<ReignNobleVisitorMember> Members { get; set; } = new List<ReignNobleVisitorMember>();
        public List<ReignNobleVisitorInvitation> Invitations { get; set; } = new List<ReignNobleVisitorInvitation>();
    }
    public sealed class ReignNobleVisitorMember
    {
        public string HeroId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public string HomeSettlementId { get; set; } = string.Empty;
        public string OriginalSettlementId { get; set; } = string.Empty;
        public bool Arrived { get; set; }
        public bool Released { get; set; }
        public string ReturnSettlementId { get; set; } = string.Empty;
        public string ReleaseReason { get; set; } = string.Empty;
    }
    public sealed class ReignNobleVisitorInvitation
    {
        public string InvitationId { get; set; } = string.Empty;
        public string HeroId { get; set; } = string.Empty;
        public string TurnId { get; set; } = string.Empty;
        public string AgreedWords { get; set; } = string.Empty;
        public double CreatedDay { get; set; }
        public bool NpcAccepted { get; set; }
    }
}
