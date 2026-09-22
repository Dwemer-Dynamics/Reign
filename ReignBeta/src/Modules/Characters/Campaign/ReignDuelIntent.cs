using TaleWorlds.CampaignSystem;
using TaleWorlds.MountAndBlade;

namespace ReignBeta.Campaign
{
    internal sealed class ReignDuelIntent
    {
        public string ActionId { get; set; }
        public string OpponentHeroStringId { get; set; }
        public string OpponentName { get; set; }
        public string Mode { get; set; }
        public string Purpose { get; set; }
        public string ArrestCaseId { get; set; }
        public string Reason { get; set; }
        public bool Lethal { get; set; }
        public bool AllowPlayerDeath { get; set; }
        public int TargetDeathRiskPercent { get; set; }
        public int PlayerDeathRiskPercent { get; set; }
        public bool PauseNearbyAgents { get; set; }
        public float PauseRadius { get; set; }
        public float PlayerHealth { get; set; }
        public float OpponentHealth { get; set; }
        public float TrainingStopHealth { get; set; }
        public float CreatedDay { get; set; }
        public Agent CapturedOpponentAgent { get; set; }

        public Hero OpponentHero
        {
            get { return World.ReignObjectResolver.FindHero(OpponentHeroStringId); }
        }
    }
}
