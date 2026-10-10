using ReignBeta.Settings;
using ReignBeta.Integration;
using ReignBeta.UI;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Library;
using TaleWorlds.CampaignSystem.Conversation;
using TaleWorlds.MountAndBlade;
using HarmonyLib;
using System;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Reign.Core.Contracts.Dialogue;
using ReignBeta.Runtime;

namespace ReignBeta.Campaign
{
    public sealed class ReignIndividualConversationBehavior : CampaignBehaviorBase
    {
        private const string OutputToken = "reignbeta_individual_chat_done";
        private static ConversationSentence _residentOption;
        private static ConversationSentence _residentLeave;
        private static ConversationSentence _residentReturn;
        private static ConversationSentence _residentResume;
        internal static ReignIndividualConversationBehavior Instance { get; private set; }
        private readonly ConversationContinuationSlot _continuation = new ConversationContinuationSlot();
        private string _savedContinuation = string.Empty;
        private Task<bool> _recovery = Task.FromResult(true);
        private readonly object _recoveryGate = new object();

        public ReignIndividualConversationBehavior() { Instance = this; }

        internal void BeginConversation(string generation) => _continuation.Begin(generation);
        internal void EndConversation() => _continuation.Discard();
        internal void RetainContinuation(ConversationContinuationToken token) => _continuation.Offer(token);
        internal Task<bool> WaitForRecoveryAsync()
        {
            lock (_recoveryGate)
            {
                // A failed startup connection must not disable dialogue for the
                // rest of the campaign. Retry only on a later explicit attempt;
                // concurrent screens still share the in-flight recovery.
                if (_recovery.IsCompleted && (_recovery.Status != TaskStatus.RanToCompletion || !_recovery.Result))
                    _recovery = ReignServerClient.RecoverConversationSessionsAsync();
                return _recovery;
            }
        }

        public override void RegisterEvents()
        {
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
        }

        public override void SyncData(IDataStore dataStore)
        {
            if (dataStore.IsSaving)
            {
                var token = ReignIndividualChatScreenManager.ActiveViewModel?.CaptureContinuation("campaign_reload")
                    ?? _continuation.Snapshot;
                _savedContinuation = token == null ? string.Empty : JsonConvert.SerializeObject(token);
            }
            dataStore.SyncData("ReignDialogueContinuationV1", ref _savedContinuation);
            if (dataStore.IsLoading)
            {
                try { _continuation.Restore(string.IsNullOrWhiteSpace(_savedContinuation) ? null
                    : JsonConvert.DeserializeObject<ConversationContinuationToken>(_savedContinuation)); }
                catch (Exception ex) { _continuation.Discard(); ReignLog.Warn("Conversation handoff could not load: " + ex.Message); }
            }
        }

        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            _recovery = ReignServerClient.RecoverConversationSessionsAsync();
            starter.AddPlayerLine("reignbeta_resume_conversation", "hero_main_options", OutputToken,
                "Continue our conversation.", () => CanSpeakWithThem() && CanResume(GetConversationHero()),
                () => ResumeConversation(GetConversationHero()), 109);
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
            _residentResume = starter.AddPlayerLine("reignbeta_resume_resident", "reignbeta_resident_input",
                "reignbeta_resident_chat_done", "Continue our conversation.",
                () => CanMeetResident() && CanResume(GetConversationHero()), () => ResumeConversation(GetConversationHero()), 109);
            _residentLeave = starter.AddPlayerLine("reignbeta_leave_resident", "reignbeta_resident_input",
                "close_window", "{=1IJouNaM}Carry on, then. Farewell.", CanMeetResident, null, 1);
            _residentReturn = starter.AddDialogLine("reignbeta_resident_chat_return", "reignbeta_resident_chat_done", "start", "{=*}...", null, null);
        }

        internal static void PrepareResidentConversationOption(int activeToken)
        {
            ReignRescueConversationBehavior.PrepareListenOption(activeToken);
            // Bind our options to the current native service menu without replacing its options.
            // Native jobs and DLC can use different input tokens; no hard-coded token inventory is needed.
            if (_residentOption != null && CanMeetResident())
            {
                AccessTools.Property(typeof(ConversationSentence), "InputToken").SetValue(_residentOption, activeToken);
                if (_residentResume != null) AccessTools.Property(typeof(ConversationSentence), "InputToken").SetValue(_residentResume, activeToken);
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

        internal bool CanResume(Hero hero) => hero != null && _continuation.Snapshot?.Matches(
            ReignServerClient.GetCampaignId(), ReignWorldHistoryCampaignBehavior.Instance?.TimelineId ?? "main",
            Hero.MainHero?.StringId ?? string.Empty, hero.StringId) == true;

        internal async void ResumeConversation(Hero hero)
        {
            try
            {
                if (!await WaitForRecoveryAsync().ConfigureAwait(false))
                {
                    await ReignMainThread.InvokeAsync(() => InformationManager.DisplayMessage(new InformationMessage(
                        "[Bannerlord Reign] The conversation has not reconnected yet. Please try again."))).ConfigureAwait(false);
                    return;
                }
                await ReignMainThread.InvokeAsync(() => {
                    var token = _continuation.Consume(ReignServerClient.GetCampaignId(),
                        ReignWorldHistoryCampaignBehavior.Instance?.TimelineId ?? "main", Hero.MainHero?.StringId ?? string.Empty,
                        hero?.StringId ?? string.Empty);
                    if (token == null) return;
                    ReignIndividualChatScreenManager.OpenForHero(hero, new JObject {
                        ["continuedFromSessionId"] = token.SessionId, ["logicalConversationId"] = token.LogicalConversationId,
                        ["continuationReason"] = token.Reason
                    }, null);
                }).ConfigureAwait(false);
            }
            catch (Exception ex) { ReignLog.Warn("Conversation resume failed: " + ex.Message); }
        }
    }
}
