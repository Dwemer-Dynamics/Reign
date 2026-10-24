using System;
using Newtonsoft.Json.Linq;
using Reign.Core.Contracts.Actions;
using Reign.Core.Contracts.Dialogue;
using ReignBeta.Campaign;
using ReignBeta.Integration;
using ReignBeta.Runtime;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Library;

namespace ReignBeta.UI.ViewModels
{
    public sealed partial class ReignIndividualChatScreenVM
    {
        private readonly ConversationRetryState _failedTurn = new ConversationRetryState();
        private ActionAgreement _failedActionDraft;
        private JObject _failedRecoveredInput;
        private object _failedCampaignInstance;
        private string _generationErrorStatus = "";
        private JArray _recoverableInputs = new JArray();
        private readonly ReignRecoveredDraftReview _recoveredDraftReview = new ReignRecoveredDraftReview();

        private string ConversationRetryScope => string.Join("|", ReignServerClient.GetCampaignId(),
            ReignWorldHistoryCampaignBehavior.Instance?.TimelineId ?? "main", Hero.MainHero?.StringId ?? "",
            _hero?.StringId ?? "", _conversationContext.Value<string>("sessionId") ?? "", ConversationGeneration);

        [DataSourceProperty]
        public string SendButtonText => !IsBusy && !IsOutfitVisible && _failedTurn.HasPending
            && string.IsNullOrWhiteSpace(InputText) ? "Retry"
            : !IsBusy && !IsOutfitVisible && _recoverableInputs.Count > 0 && string.IsNullOrWhiteSpace(InputText) ? "Recover" : "Send";

        private bool TryRecoverSavedInput()
        {
            if (_failedTurn.HasPending || _recoverableInputs.Count == 0 || IsBusy || IsPregnancyWarningVisible
                || !string.IsNullOrWhiteSpace(InputText)) return false;
            var draft = _recoverableInputs[0] as JObject;
            string restored = _recoveredDraftReview.Restore(draft, string.IsNullOrWhiteSpace(InputText), DateTime.UtcNow);
            if (restored == null) return false;
            InputText = restored;
            BusyText = "Earlier delivery is unconfirmed. Review the draft; Send submits a new message and can repeat actions.";
            return true;
        }

        private void ClearFailedConversationTurn()
        {
            _failedTurn.Clear(); _failedActionDraft = null; _failedCampaignInstance = null; _failedRecoveredInput = null;
            _generationErrorStatus = "";
            OnPropertyChanged(nameof(SendButtonText));
        }

        private void RetainFailedConversationTurn(string text, string correlation, string sceneTurn,
            string scope, object campaign, ActionAgreement draft, string error, JObject recoveredInput = null)
        {
            if (_isFinalized || campaign != TaleWorlds.CampaignSystem.Campaign.Current || scope != ConversationRetryScope)
            { ClearFailedConversationTurn(); return; }
            _failedTurn.Failed(new ConversationRetryTurn(scope, text, correlation, sceneTurn));
            _failedActionDraft = draft == null ? null : ReignBeta.Actions.ReignActionService.Copy(draft);
            _failedRecoveredInput = recoveredInput == null ? null : (JObject)recoveredInput.DeepClone();
            _failedCampaignInstance = campaign;
            string reason = FriendlyServerError(error);
            AddSystemLine(reason + " Your message is saved. Select Retry to try this turn again, or write a new message.");
            _generationErrorStatus = (reason.Length > 94 ? reason.Substring(0, 94).TrimEnd() + "…" : reason) + " Select Retry.";
            BusyText = _generationErrorStatus;
            OnPropertyChanged(nameof(SendButtonText));
        }

        private ConversationRetryTurn BeginFailedConversationRetry()
        {
            if (_failedCampaignInstance != TaleWorlds.CampaignSystem.Campaign.Current)
            { ClearFailedConversationTurn(); BusyText = "The campaign changed. Reopen this conversation."; return null; }
            var turn = _failedTurn.Begin(ConversationRetryScope);
            OnPropertyChanged(nameof(SendButtonText));
            return turn;
        }
    }
}
