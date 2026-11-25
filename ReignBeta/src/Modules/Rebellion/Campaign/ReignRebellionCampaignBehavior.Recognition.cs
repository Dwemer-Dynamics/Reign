using System;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Reign.Core.Contracts.World;
using ReignBeta.Characters.Integration;
using ReignBeta.Persistence.Campaign;
using ReignBeta.Platform.Integration;
using ReignBeta.Platform.Runtime;
using ReignBeta.Shared.Dialogue;
using ReignBeta.World;
using ReignBeta.WorldSimulation.World;
using ReignBeta.WorldSimulation.Rulers;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;

namespace ReignBeta.Rebellion.Campaign
{
    public sealed partial class ReignRebellionCampaignBehavior
    {
        private const string RecognitionKey = "recognizedIndependenceV1";
        private const string RecognitionPhase = "resolving_recognized_independence";
        private ReignRebellionMovementRecord _recognitionApplying;

        private static bool HasRecognitionAttempt(ReignRebellionMovementRecord movement) => movement != null
            && (movement.ExecutionPhase == RecognitionPhase
                || ParseObject(movement.ExecutionSnapshotJson).Property(RecognitionKey) != null);

        internal static bool CanMutateLiveCivilWar(ReignRebellionMovementRecord movement)
            => ReignRecognitionReceipt.AllowsCivilWarMutation(IsCivilWar(movement), HasRecognitionAttempt(movement),
                () => ObserveCivilWar(FindKingdom(movement.RebelKingdomStringId), FindKingdom(movement.ParentKingdomStringId)));

        private static bool? ObserveCivilWar(Kingdom rebels, Kingdom parent)
        {
            if (rebels == null || parent == null || rebels == parent || rebels.IsEliminated || parent.IsEliminated)
                return null;
            bool rebelAtWar = rebels.IsAtWarWith(parent);
            return rebelAtWar == parent.IsAtWarWith(rebels) ? (bool?)rebelAtWar : null;
        }

        private static string RecognitionActionBinding(ReignWorldActionRecord action)
            => PlayerActionRules.Hash(new JArray(action.ActionId, action.TypeValue, action.Source, action.ActorKingdomStringId,
                action.TargetKingdomStringId, action.ActorClanStringId, action.TargetClanStringId, action.ActorHeroStringId,
                action.TargetHeroStringId, action.TargetSettlementStringId, action.AuthorizationMode, action.NegotiationId,
                action.NegotiatedCommand, action.TermsHash, action.TermsJson, action.RequiresAcceptance, action.Reason)
                .ToString(Formatting.None));

        private static string RecognitionAgreementId(string campaignId, string timelineId, string movementId, string actionId)
            => "recognition_" + PlayerActionRules.Hash(new JArray(campaignId, timelineId, movementId, actionId).ToString(Formatting.None));

        internal bool TryRecoverRecognizedIndependence(ReignWorldActionRecord action, out ReignActionResult result)
        {
            result = null;
            if (action?.Type != ReignWorldActionType.PoliticsResolveCivilWar) return false;
            JObject terms = ParseObject(action.TermsJson);
            string movementId = ReadString(terms, "movementId");
            if (string.IsNullOrWhiteSpace(movementId)) return false;
            ReignRebellionMovementRecord movement = _movements.FirstOrDefault(row =>
                row != null && Same(row.MovementId, movementId));
            if (!HasRecognitionAttempt(movement)) return false;

            ReignRecognitionReceipt receipt;
            try { receipt = ParseObject(movement.ExecutionSnapshotJson)[RecognitionKey]?.ToObject<ReignRecognitionReceipt>(); }
            catch (JsonException) { receipt = null; }
            if (receipt == null || movement.MovementId != movementId
                || !receipt.Matches(ReignCampaignIdentity.CurrentCampaignId(), ReignServices.WorldHistory?.TimelineId,
                    movement.MovementId, action.ActionId, RecognitionActionBinding(action))
                || receipt.AgreementId != RecognitionAgreementId(receipt.CampaignId, receipt.TimelineId, receipt.MovementId, receipt.ActionId))
            {
                result = RecognitionHeld("The saved recognition does not identify this exact admitted action and timeline.");
                return true;
            }

            result = receipt.Phase != ReignRecognitionPhase.Published && _recognitionApplying != null
                ? ReignActionResult.Progress("The exact recognition is still collecting its native outcome.")
                : AdvanceRecognition(movement, action, receipt, allowInitialNativePeace: false);
            return true;
        }

        private ReignActionResult BeginRecognition(ReignRebellionMovementRecord movement, ReignWorldActionRecord action,
            Kingdom rebels, Kingdom parent, Hero rebelRuler, Hero parentRuler)
        {
            string campaignId = ReignCampaignIdentity.CurrentCampaignId();
            string timelineId = ReignServices.WorldHistory?.TimelineId;
            if (_recognitionApplying != null || action.RequiresAcceptance || string.IsNullOrWhiteSpace(action.ActionId)
                || string.IsNullOrWhiteSpace(campaignId)
                || string.IsNullOrWhiteSpace(timelineId) || !OwnsRecognitionWorld(movement, campaignId, timelineId)
                || ReignServices.AI == null || ReignRulerCampaignBehavior.HoldsCivilWar(movement.MovementId))
                return ReignActionResult.ValidationFailed("The original recognition action and its loaded civil war cannot be verified.");

            var receipt = new ReignRecognitionReceipt
            {
                CampaignId = campaignId, TimelineId = timelineId, MovementId = movement.MovementId,
                ActionId = action.ActionId, ActionBinding = RecognitionActionBinding(action),
                RebelKingdomId = rebels.StringId, ParentKingdomId = parent.StringId,
                RebelRulerId = rebelRuler.StringId, ParentRulerId = parentRuler.StringId,
                AgreementId = RecognitionAgreementId(campaignId, timelineId, movement.MovementId, action.ActionId),
                ParentName = parent.InformalName.ToString(), RebelName = rebels.InformalName.ToString(),
                Summary = parentRuler.Name + " recognized " + rebels.InformalName
                    + " as a separate sovereign kingdom; both realms remain independent and are now at peace."
            };
            // This intent belongs to the movement already saved by this behavior.
            PersistRecognition(movement, receipt);
            return AdvanceRecognition(movement, action, receipt, allowInitialNativePeace: true);
        }

        private ReignActionResult AdvanceRecognition(ReignRebellionMovementRecord movement, ReignWorldActionRecord action,
            ReignRecognitionReceipt receipt, bool allowInitialNativePeace)
        {
            var recorder = ReignServices.AI;
            var campaign = TaleWorlds.CampaignSystem.Campaign.Current;
            var world = ReignServices.WorldHistory;
            string generation = world?.SharedActivityGeneration;
            bool OwnsScope() => ReferenceEquals(world, ReignServices.WorldHistory) && generation == world?.SharedActivityGeneration
                && ReferenceEquals(campaign, TaleWorlds.CampaignSystem.Campaign.Current)
                && OwnsRecognitionWorld(movement, receipt.CampaignId, receipt.TimelineId)
                && receipt.MovementId == movement.MovementId && action.ActionId == receipt.ActionId
                && receipt.ActionBinding == RecognitionActionBinding(action)
                && receipt.RebelKingdomId == movement.RebelKingdomStringId && receipt.ParentKingdomId == movement.ParentKingdomStringId
                && receipt.RebelRulerId == movement.LeaderHeroStringId && receipt.ParentRulerId == movement.OriginalRulerHeroStringId;

            if (receipt.Phase == ReignRecognitionPhase.Published)
                return OwnsScope() ? PublishedRecognitionResult(movement, receipt)
                    : RecognitionHeld("The original recognition belongs to a different campaign or timeline.");

            bool Owns() => recorder != null && ReferenceEquals(recorder, ReignServices.AI) && OwnsScope();
            if (!Owns())
                return RecognitionHeld("The original recognition authority or campaign is unavailable.");

            Kingdom rebels = FindKingdom(receipt.RebelKingdomId), parent = FindKingdom(receipt.ParentKingdomId);
            Hero rebelRuler = FindHero(receipt.RebelRulerId), parentRuler = FindHero(receipt.ParentRulerId);
            bool? ObserveWar()
            {
                if (!Owns() || rebels == null || parent == null || rebels == parent || rebels.IsEliminated || parent.IsEliminated
                    || rebels != FindKingdom(receipt.RebelKingdomId) || parent != FindKingdom(receipt.ParentKingdomId)
                    || rebelRuler?.IsAlive != true || parentRuler?.IsAlive != true
                    || rebelRuler.StringId != receipt.RebelRulerId || parentRuler.StringId != receipt.ParentRulerId
                    || rebels.Leader != rebelRuler || parent.Leader != parentRuler)
                    return null;
                return ObserveCivilWar(rebels, parent);
            }

            bool Publish()
            {
                if (!IsCivilWar(movement) || ObserveWar() != false || !receipt.PeaceDay.HasValue
                    || recorder.RecordRecognizedIndependenceAgreement(action, receipt.AgreementId, receipt.PeaceDay.Value) == null)
                    return false;
                if (!Owns() || ObserveWar() != false) return false;

                movement.WinningSide = "negotiated";
                movement.Resolution = "recognized_independence";
                movement.ResolutionCause = "recognized_by_challenged_ruler";
                movement.WinningLeaderHeroStringId = receipt.RebelRulerId;
                // The existing finalizer emits history and the notice once. A
                // save captured during publication remains held on any ambiguity.
                FinalizeResolution(movement, receipt.PeaceDay.Value);
                return ObserveWar() == false && movement.ResolutionApplied && movement.Stage == "resolved"
                    && movement.Resolution == "recognized_independence";
            }

            bool completed;
            var previousApplying = _recognitionApplying;
            _recognitionApplying = movement;
            try
            {
                completed = receipt.Advance(allowInitialNativePeace, ObserveWar,
                    () => MakePeaceAction.Apply(rebels, parent), CurrentDay, Publish,
                    () => PersistRecognition(movement, receipt));
            }
            finally { _recognitionApplying = previousApplying; }
            return completed ? PublishedRecognitionResult(movement, receipt)
                : RecognitionHeld("Recognition has an incomplete or unverifiable saved outcome. Do not repeat native peace.")
                    .WithDiagnostic("recognitionPhase", receipt.Phase.ToString()).WithDiagnostic("reason", receipt.Failure);
        }

        private static ReignActionResult PublishedRecognitionResult(ReignRebellionMovementRecord movement,
            ReignRecognitionReceipt receipt)
        {
            if (!receipt.HasPublishedOutcome() || !movement.ResolutionApplied || movement.Stage != "resolved"
                || movement.Resolution != "recognized_independence" || movement.WinningSide != "negotiated"
                || movement.WinningLeaderHeroStringId != receipt.RebelRulerId)
                return RecognitionHeld("Recognition has an incomplete or unverifiable saved outcome. Do not repeat native peace.")
                    .WithDiagnostic("recognitionPhase", receipt.Phase.ToString()).WithDiagnostic("reason", receipt.Failure);

            return ReignActionResult.Done(receipt.Summary)
                .WithChangedEntity("kingdom", receipt.RebelKingdomId, receipt.RebelName, "independence_recognized")
                .WithChangedEntity("kingdom", receipt.ParentKingdomId, receipt.ParentName, "civil_war_peace_accepted")
                .WithEffect("peace_made", "kingdom", receipt.ParentKingdomId, receipt.ParentName, "counterpart=" + receipt.RebelKingdomId)
                .WithEffect("treaty_recorded", "kingdom", receipt.RebelKingdomId, receipt.RebelName, "kind=recognized_independence;durationDays=63")
                .WithDiagnostic("movementId", receipt.MovementId)
                .WithDiagnostic("sovereignty", "separate_kingdoms");
        }

        private bool OwnsRecognitionWorld(ReignRebellionMovementRecord movement, string campaignId, string timelineId)
            => ReferenceEquals(ReignServices.Rebellion, this) && _movements.Contains(movement)
                && ReignCampaignIdentity.HasActiveCampaign() && ReignCampaignIdentity.CurrentCampaignId() == campaignId
                && !string.IsNullOrWhiteSpace(TaleWorlds.CampaignSystem.Campaign.Current?.UniqueGameId)
                && ReignServices.WorldHistory?.TimelineReady == true && ReignServices.WorldHistory.TimelineId == timelineId
                && !ReignCampaignInitializationGate.IsPending && !ReignSaveSyncCampaignBehavior.IsNativeSaveInProgress;

        private static void PersistRecognition(ReignRebellionMovementRecord movement, ReignRecognitionReceipt receipt)
        {
            JObject snapshot = ParseObject(movement.ExecutionSnapshotJson);
            snapshot[RecognitionKey] = JObject.FromObject(receipt);
            movement.ExecutionSnapshotJson = snapshot.ToString(Formatting.None);
            movement.ExecutionPhase = receipt.Phase == ReignRecognitionPhase.Published ? "verified" : RecognitionPhase;
        }

        private static ReignActionResult RecognitionHeld(string reason)
            => ReignActionResult.FailTerminal(reason, "recognition_recovery_required", "state_verification");
    }
}
