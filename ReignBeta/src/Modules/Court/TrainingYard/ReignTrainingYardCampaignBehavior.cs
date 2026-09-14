#if !REIGN_EXCLUDE_COURT
using ReignBeta.UI;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;

namespace ReignBeta.Court.TrainingYard
{
    public sealed class ReignTrainingYardCampaignBehavior : CampaignBehaviorBase
    {
        public override void RegisterEvents()
        {
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
            CampaignEvents.HourlyTickEvent.AddNonSerializedListener(this, OnHourlyTick);
        }

        private static void OnSessionLaunched(CampaignGameStarter starter)
        {
            starter.AddGameMenuOption("town_keep", "reign_visit_training_yard", "{=!}Visit the training yard",
                TrainingYardCondition, TrainingYardConsequence, false, 2);
        }

        private static bool TrainingYardCondition(MenuCallbackArgs args)
        {
            bool available = Settlement.CurrentSettlement?.IsFortification == true
                && MobileParty.MainParty?.MemberRoster != null;
            args.optionLeaveType = GameMenuOption.LeaveType.Wait;
            args.IsEnabled = available;
            return available;
        }

        private static void TrainingYardConsequence(MenuCallbackArgs args)
        {
            Settlement settlement = Settlement.CurrentSettlement;
            if (settlement?.IsFortification != true) return;
            args.MapState?.ExitMenuMode();
            ReignTrainingYardScreenManager.Open(settlement);
        }

        private static void OnHourlyTick()
        {
            ReignTrainingYardScreenManager.OnCampaignHourlyTick();
        }

        public override void SyncData(IDataStore dataStore)
        {
            // Training is deliberately scoped to the currently open visit.
        }
    }
}
#endif
