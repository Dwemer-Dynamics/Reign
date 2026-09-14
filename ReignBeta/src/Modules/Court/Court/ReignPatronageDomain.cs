using System.Collections.Generic;
using Reign.Core.Contracts.Court;

namespace ReignBeta.Court
{
    public sealed partial class ReignRulerDocketState
    {
        public List<ReignPatronageCommission> PatronageCommissions { get; set; } = new List<ReignPatronageCommission>();
        public List<ReignPatronageWork> PatronageWorks { get; set; } = new List<ReignPatronageWork>();
        public List<ReignPatronageCreator> PatronageCreators { get; set; } = new List<ReignPatronageCreator>();
    }
    public sealed class ReignPatronageCreator
    {
        public string ActorId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public string HomeSettlementId { get; set; } = string.Empty;
    }
    public sealed class ReignPatronageCommission
    {
        public string CommissionId { get; set; } = string.Empty;
        public string MatterId { get; set; } = string.Empty;
        public string CampaignId { get; set; } = string.Empty;
        public string TimelineId { get; set; } = string.Empty;
        public string ReignId { get; set; } = string.Empty;
        public string RulerHeroId { get; set; } = string.Empty;
        public string KingdomId { get; set; } = string.Empty;
        public string CreatorId { get; set; } = string.Empty;
        public string CreatorName { get; set; } = string.Empty;
        public string TemplateId { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public ReignPatronageObjective Objective { get; set; }
        public ReignPatronageReach Reach { get; set; }
        public int GoldPaid { get; set; }
        public double CreatedDay { get; set; }
        public double DueDay { get; set; }
        public bool Paid { get; set; }
        public bool Completed { get; set; }
        public string CompletionReport { get; set; } = string.Empty;
        public List<string> SettlementIds { get; set; } = new List<string>();
        public List<string> HeroIds { get; set; } = new List<string>();
        public List<string> DeliveryReceipts { get; set; } = new List<string>();
        public List<string> ExcludedRecipientIds { get; set; } = new List<string>();
        public List<ReignPatronageDeliveryEffect> NativeDeliveryEffects { get; set; } = new List<ReignPatronageDeliveryEffect>();
    }
    public sealed class ReignPatronageDeliveryEffect
    {
        public string ReceiptId { get; set; } = string.Empty;
        public string RecipientId { get; set; } = string.Empty;
        public string Stat { get; set; } = string.Empty;
        public double Before { get; set; }
        public double After { get; set; }
        public double WorldDay { get; set; }
    }
    public sealed class ReignPatronageWork
    {
        public string WorkId { get; set; } = string.Empty;
        public string CommissionId { get; set; } = string.Empty;
        public string CreatorId { get; set; } = string.Empty;
        public string PatronHeroId { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public double CompletedDay { get; set; }
    }
}
