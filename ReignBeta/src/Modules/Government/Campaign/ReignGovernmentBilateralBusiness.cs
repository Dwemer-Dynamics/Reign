using System;
using System.Linq;
using HarmonyLib;
using Reign.Core.Contracts.Government;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Election;

namespace ReignBeta.Government
{
    public sealed partial class ReignGovernmentCampaignBehavior
    {
        private bool TryCreateCounterpartHearing(ReignGovernmentBusinessRecord record, bool overridden, out string result)
        {
            result = string.Empty;
            KingdomDecision original = NativeBusinessDecision(record);
            Kingdom counterpart = null;
            Func<Clan, KingdomDecision> factory = null;
            if (original is MakePeaceKingdomDecision peace)
            {
                // A native request already identified as coming from the opponent carries their acceptance.
                if ((bool)(AccessTools.Field(typeof(MakePeaceKingdomDecision), "_isProposedByOpponent")?.GetValue(peace) ?? false)) return false;
                counterpart = peace.FactionToMakePeaceWith as Kingdom;
                factory = clan => new MakePeaceKingdomDecision(clan, original.Kingdom, -peace.DailyTributeToBePaid,
                    peace.DailyTributeDurationInDays, true, true);
            }
            else if (original is StartAllianceDecision alliance)
            {
                counterpart = alliance.KingdomToStartAllianceWith;
                factory = clan => new StartAllianceDecision(clan, original.Kingdom);
            }
            else if (original is TradeAgreementDecision trade)
            {
                counterpart = trade.TargetKingdom;
                factory = clan => new TradeAgreementDecision(clan, original.Kingdom);
            }
            else if (original is ProposeCallToWarAgreementDecision call)
            {
                counterpart = call.CalledKingdom;
                factory = clan => new AcceptCallToWarAgreementDecision(clan, original.Kingdom, call.KingdomToCallToWarAgainst);
            }
            if (factory == null) return false;
            if (counterpart?.RulingClan == null || EnsureGovernment(counterpart) == null)
            {
                record.Status = "execution_failed";
                record.Outcome = result = "The counterpart government is unavailable; no agreement was enacted.";
                ChangedBusiness(record); return true;
            }
            record.RulerOverrode = overridden;
            record.Status = "awaiting_counterpart";
            record.Outcome = "This government approved the exact proposal. The counterpart government must now decide.";
            try
            {
                _capturingCounterpartOfBusinessId = record.BusinessId;
                KingdomDecision reply = factory(counterpart.RulingClan);
                if (!TryCaptureNativeDecision(reply)) throw new InvalidOperationException("The counterpart adapter did not take ownership.");
                var response = _business.FirstOrDefault(x => Same(x.CounterpartOfBusinessId, record.BusinessId));
                if (response == null) throw new InvalidOperationException("The counterpart hearing was not recorded.");
                response.PetitionerHeroStringId = original.Kingdom.Leader?.StringId ?? string.Empty;
                response.Status = "hearing";
                response.SponsorHeroStringId = string.Empty;
                response.SponsorReason = "A request from the counterpart government requires no domestic sponsor.";
                record.CounterpartBusinessId = response.BusinessId;
                NotifyGovernmentBusinessClosed(record); // Domestic attendance ends once its own decision is made.
                ChangedBusiness(response); ChangedBusiness(record);
                result = record.Outcome; return true;
            }
            catch (Exception ex)
            {
                record.Status = "execution_failed";
                record.Outcome = result = "Counterpart review could not be prepared: " + ex.GetType().Name + ". No treaty effect was applied.";
                ChangedBusiness(record); return true;
            }
            finally { _capturingCounterpartOfBusinessId = null; }
        }

        private void ApplyBilateralNativeEffect(ReignGovernmentBusinessRecord response)
        {
            var source = _business.FirstOrDefault(x => Same(x.BusinessId, response.CounterpartOfBusinessId));
            if (source == null || source.Status != "awaiting_counterpart" || source.ExecutionApplied)
                throw new InvalidOperationException("The source authorization is no longer pending.");
            if (!ValidateNativeBusiness(source, out string reason)) throw new InvalidOperationException(reason);
            KingdomDecision execution = NativeBusinessDecision(source);
            // Native advisory peace proposals intentionally set applyResults=false; their approved bilateral
            // execution uses the same exact terms with effects enabled, never a synthetic acceptance popup.
            if (execution is MakePeaceKingdomDecision peace)
                execution = new MakePeaceKingdomDecision(peace.ProposerClan, peace.FactionToMakePeaceWith,
                    peace.DailyTributeToBePaid, peace.DailyTributeDurationInDays, true, true);
            var outcome = execution.DetermineInitialCandidates().First(NativeOutcomeEnforced);
            _executingGovernmentDecision = execution;
            execution.ApplyChosenOutcome(outcome);
            VerifyNativeBusinessEffect(source, execution, "accept");
            RecordNativeDiplomaticRelationReceipt(source, execution);
            RecordNativeDiplomaticRelationReceipt(response, NativeBusinessDecision(response));
            source.ExecutionApplied = true; source.ExecutedOptionId = "accept";
            response.ExecutionApplied = true; response.ExecutedOptionId = "accept";
            FinishBusiness(source, "accept", source.RulerOverrode);
        }

        private void CompleteCounterpartRefusal(ReignGovernmentBusinessRecord response)
        {
            if (string.IsNullOrEmpty(response.CounterpartOfBusinessId) || response.ExecutedOptionId != "reject") return;
            var source = _business.FirstOrDefault(x => Same(x.BusinessId, response.CounterpartOfBusinessId));
            if (source == null || source.Status != "awaiting_counterpart") return;
            source.Status = "decided"; source.ResolvedDay = CurrentDay();
            source.Outcome = "The counterpart government refused the proposal. No agreement was enacted.";
            // The initiating ruler did not personally refuse its domestic petition.
            source.ConsequencesApplied = true;
            NotifyGovernmentBusinessClosed(source); ChangedBusiness(source);
        }

        private void ObserveCounterpartBusiness(ReignGovernmentBusinessRecord record)
        {
            if (record.Status != "awaiting_counterpart" || string.IsNullOrEmpty(record.CounterpartBusinessId)) return;
            var response = _business.FirstOrDefault(x => Same(x.BusinessId, record.CounterpartBusinessId));
            if (response == null || response.Status == "invalidated" || response.Status == "expired")
                CloseInvalidBusiness(record, "The counterpart hearing is no longer available. No agreement was enacted.");
            else if (response.Status == "execution_failed")
            {
                record.Status = "execution_failed";
                record.Outcome = "Counterpart execution needs review. The recorded authorizations are preserved; uncertain effects will not be retried.";
                NotifyGovernmentBusinessClosed(record); ChangedBusiness(record);
            }
        }
    }
}
