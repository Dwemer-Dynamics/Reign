using ReignBeta.Settings;
using ReignBeta.Integration;
using ReignBeta.UI;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Library;
using TaleWorlds.CampaignSystem.Conversation;
using TaleWorlds.MountAndBlade;
using HarmonyLib;

namespace ReignBeta.Campaign
{
    public sealed class ReignIndividualConversationBehavior : CampaignBehaviorBase
    {
        private const string OutputToken = "reignbeta_individual_chat_done";
        private static ConversationSentence _residentOption;
        private static ConversationSentence _residentLeave;
        private static ConversationSentence _residentReturn;

        public override void RegisterEvents()
        {
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
        }

        public override void SyncData(IDataStore dataStore)
        {
        }

        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            _ = ReignServerClient.RecoverConversationSessionsAsync();
            starter.AddPlayerLine(
                "reignbeta_speak_with_them",
                "hero_main_options",
                OutputToken,
                "Speak with them.",
                CanSpeakWithThem,
                SpeakWithThem,
                108);
            starter.AddDialogLine("reignbeta_speak_with_them_return", OutputToken, "hero_main_options", "{=*}...", null, null);
            _residentOption = starter.AddPlayerLine("reignbeta_meet_resident", "reignbeta_resident_input",
                "reignbeta_resident_chat_done", "Speak with them.", CanMeetResident, SpeakWithResident, 108);
            _residentLeave = starter.AddPlayerLine("reignbeta_leave_resident", "reignbeta_resident_input",
                "close_window", "{=1IJouNaM}Carry on, then. Farewell.", CanMeetResident, null, 1);
            _residentReturn = starter.AddDialogLine("reignbeta_resident_chat_return", "reignbeta_resident_chat_done", "start", "{=*}...", null, null);
        }

        internal static void PrepareResidentConversationOption(int activeToken)
        {
            // Bind our options to the current native service menu without replacing its options.
            // Native jobs and DLC can use different input tokens; no hard-coded token inventory is needed.
            if (_residentOption != null && CanMeetResident())
            {
                AccessTools.Property(typeof(ConversationSentence), "InputToken").SetValue(_residentOption, activeToken);
                AccessTools.Property(typeof(ConversationSentence), "InputToken").SetValue(_residentLeave, activeToken);
                // Resume the native job's actual options. "start" expects an NPC
                // greeting and would leave the player stranded with only our line.
                AccessTools.Property(typeof(ConversationSentence), "OutputToken").SetValue(_residentReturn, activeToken);
            }
        }

        private static bool CanMeetResident()
        {
            if (ReignCampaignInitializationGate.IsPending || ReignBetaSettings.Instance?.UseLocalServer == false) return false;
            var agent = TaleWorlds.CampaignSystem.Campaign.Current?.ConversationManager.OneToOneConversationAgent as Agent;
            return ReignEncounteredResidentsCampaignBehavior.Instance?.CanMeet(agent) == true;
        }

        private static void SpeakWithResident()
        {
            try
            {
                var agent = TaleWorlds.CampaignSystem.Campaign.Current?.ConversationManager.OneToOneConversationAgent as Agent;
                Hero hero = ReignEncounteredResidentsCampaignBehavior.Instance?.Meet(agent);
                if (hero != null) ReignIndividualChatScreenManager.OpenForHero(hero);
            }
            catch (System.Exception ex)
            {
                ReignLog.Exception("Resident first contact could not finish", ex);
                InformationManager.DisplayMessage(new InformationMessage("[Bannerlord Reign] This person's record could not be prepared. You can try speaking with them again."));
            }
        }

        private bool CanSpeakWithThem()
        {
            if (ReignCampaignInitializationGate.IsPending) return false;
            ReignBetaSettings settings = ReignBetaSettings.Instance;
            if (settings != null && !settings.UseLocalServer)
            {
                return false;
            }

            Hero hero = GetConversationHero();
            return hero != null && hero != Hero.MainHero && hero.IsAlive && !string.IsNullOrWhiteSpace(hero.StringId);
        }

        private void SpeakWithThem()
        {
            Hero hero = GetConversationHero();
            if (hero == null)
            {
                InformationManager.DisplayMessage(new InformationMessage("[Bannerlord Reign] No conversation hero found.", Color.FromUint(0xFFFFCC66)));
                return;
            }

            ReignIndividualChatScreenManager.OpenForHero(hero);
        }

        private static Hero GetConversationHero()
        {
            return ReignEncounteredResidentsCampaignBehavior.Instance?.ConversationHero
                ?? CharacterObject.OneToOneConversationCharacter?.HeroObject;
        }
    }
}
