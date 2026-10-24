using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Reign.Core.Contracts.Government;
using ReignBeta.Integration;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Election;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.GameComponents;

namespace ReignBeta.Government
{
    public sealed partial class ReignGovernmentCampaignBehavior
    {
        private string _capturingCounterpartOfBusinessId;
        private readonly Dictionary<KingdomDecision, string> _nativeDecisionPressureIds = new Dictionary<KingdomDecision, string>();
        internal static readonly IReadOnlyDictionary<string, string> NativeBusinessKinds = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["KingdomPolicyDecision"] = "policy", ["DeclareWarDecision"] = "war", ["MakePeaceKingdomDecision"] = "peace",
            ["SettlementClaimantDecision"] = "fief_allocation", ["SettlementClaimantPreliminaryDecision"] = "fief_reassignment",
            ["ExpelClanFromKingdomDecision"] = "expulsion", ["StartAllianceDecision"] = "alliance",
            ["TradeAgreementDecision"] = "trade_agreement", ["ProposeCallToWarAgreementDecision"] = "call_to_war",
            ["AcceptCallToWarAgreementDecision"] = "call_to_war"
        };

        public bool TryCaptureNativeDecision(KingdomDecision decision, string captureSource = "reign")
        {
            if (decision == null || decision is KingSelectionKingdomDecision || !NativeBusinessKinds.TryGetValue(decision.GetType().Name, out string kind)
                || decision.GetType().Namespace != typeof(KingdomDecision).Namespace) return false;
            if (_ownedNativeDecisions.Contains(decision)) return true;
            if (!ReignGovernmentPatches.VerifyReady() || EnsureGovernment(decision.Kingdom) == null) return false;
            string target = NativeTargetId(decision);
            string fingerprint = NativeRequestIdentity(decision, target);
            if (!string.IsNullOrEmpty(_capturingCounterpartOfBusinessId)) fingerprint += "|response:" + _capturingCounterpartOfBusinessId;
            var duplicate = _business.FirstOrDefault(x => !GovernmentBusinessRules.IsClosed(x.Status)
                && string.Equals(x.NativeFingerprint, fingerprint, StringComparison.Ordinal));
            if (duplicate != null)
            {
                // Retain this exact alias through reload, including after the original hearing closes.
                duplicate.NativeDecisionAliasIndices = duplicate.NativeDecisionAliasIndices ?? new List<int>();
                duplicate.NativeDecisionAliasIndices.Add(_ownedNativeDecisions.Count);
                _ownedNativeDecisions.Add(decision);
                ChangedBusiness(duplicate);
                return true;
            }
            var candidates = new List<DecisionOutcome>();
            JArray options;
            string adapterError = string.Empty;
            try
            {
                candidates = GovernmentNativeCandidates(decision).ToList();
                options = new JArray(candidates.Select((x, i) => NativeOption(x, i)));
                if (options.Count == 0 || options.OfType<JObject>().Select(x => x.Value<string>("id")).Distinct().Count() != options.Count)
                    throw new InvalidOperationException("The adapter did not produce unique valid choices.");
            }
            catch (Exception ex) { options = new JArray(); adapterError = "The government adapter requires repair: " + ex.GetType().Name; }
            bool incomingPeace = (bool)(AccessTools.Field(decision.GetType(), "_isProposedByOpponent")?.GetValue(decision) ?? false);
            bool mandatory = kind == "fief_allocation" || decision is AcceptCallToWarAgreementDecision || incomingPeace || !string.IsNullOrEmpty(_capturingCounterpartOfBusinessId);
            var record = new ReignGovernmentBusinessRecord
            {
                KingdomStringId = decision.Kingdom.StringId, Kind = kind,
                Title = decision.GetGeneralTitle()?.ToString() ?? kind,
                Summary = GovernmentBusinessRules.PublicMatterSummary(kind, decision.GetChooseDescription()?.ToString()),
                NativeDecisionType = decision.GetType().Name, NativeFingerprint = fingerprint, TargetId = target,
                NativeCaptureVersion = ReignGovernmentNativeSafety.CaptureVersion, NativeCaptureSource = captureSource,
                NativeCapturedOwnerClanId = NativeSettlementOwnerClanId(decision),
                NativeDecisionIndex = _ownedNativeDecisions.Count, CreatedDay = CurrentDay(),
                CounterpartOfBusinessId = _capturingCounterpartOfBusinessId ?? string.Empty,
                PetitionerHeroStringId = mandatory ? string.Empty : decision.ProposerClan?.Leader?.StringId ?? string.Empty,
                OptionsJson = options.ToString(Formatting.None), Status = adapterError.Length > 0 ? "execution_failed" : mandatory ? "hearing" : "awaiting_sponsorship",
                Outcome = adapterError,
                IsUrgent = kind == "fief_allocation" || kind == "peace" || kind == "war" || kind == "call_to_war"
            };
            if (decision is AcceptCallToWarAgreementDecision incomingCall)
                record.PetitionerHeroStringId = incomingCall.CallingKingdom?.Leader?.StringId ?? string.Empty;
            else if (incomingPeace && decision is MakePeaceKingdomDecision peaceOffer)
                record.PetitionerHeroStringId = peaceOffer.FactionToMakePeaceWith?.Leader?.StringId ?? string.Empty;
            record.RequestedOptionId = options.OfType<JObject>().FirstOrDefault(x => x.Value<bool>("affirmative"))?.Value<string>("id") ?? string.Empty;
            record.StatusQuoOptionId = options.OfType<JObject>().FirstOrDefault(x => !x.Value<bool>("affirmative") && string.IsNullOrEmpty(x.Value<string>("targetId")))?.Value<string>("id") ?? string.Empty;
            if (decision is KingdomPolicyDecision policy)
            {
                record.IsRepeal = (bool)(AccessTools.Field(typeof(KingdomPolicyDecision), "_isInvertedDecision")?.GetValue(policy) ?? false);
                record.Title = (record.IsRepeal ? "Repeal " : "Enact ") + policy.Policy.Name;
                record.Summary = policy.Policy.Description + "\n" + policy.Policy.SecondaryEffects;
                foreach (var option in options.OfType<JObject>()) option["label"] = option.Value<bool>("affirmative") ? record.Title : "Keep the current policy position";
                record.OptionsJson = options.ToString(Formatting.None);
            }
            var participants = new JArray();
            if (decision is SettlementClaimantDecision allocation)
            {
                Hero capturer = AccessTools.Field(typeof(SettlementClaimantDecision), "_capturerHero")?.GetValue(allocation) as Hero;
                if (capturer != null)
                {
                    var preferred = options.OfType<JObject>().OrderByDescending(x => NativeClanPreference(decision, capturer.Clan, x)).FirstOrDefault();
                    participants.Add(new JObject { ["heroId"] = capturer.StringId, ["role"] = "capturer", ["optionId"] = preferred?.Value<string>("id") });
                }
                foreach (var candidate in candidates.OfType<SettlementClaimantDecision.ClanAsDecisionOutcome>())
                    if (candidate.Clan.Leader != null) participants.Add(new JObject { ["heroId"] = candidate.Clan.Leader.StringId, ["role"] = "claimant", ["optionId"] = "clan:" + candidate.Clan.StringId });
            }
            if (decision is ExpelClanFromKingdomDecision expulsion && expulsion.ClanToExpel?.Leader != null)
                participants.Add(new JObject { ["heroId"] = expulsion.ClanToExpel.Leader.StringId, ["role"] = "target" });
            if (decision is SettlementClaimantPreliminaryDecision preliminary && preliminary.Settlement?.OwnerClan?.Leader != null)
                participants.Add(new JObject { ["heroId"] = preliminary.Settlement.OwnerClan.Leader.StringId, ["role"] = "target" });
            if (kind == "policy" || kind == "war" || kind == "peace")
            {
                // Strong native issue preferences identify affected lords independently of who sits in a senate.
                // Neutral clans acquire no synthetic grievance merely because they belong to the realm.
                foreach (var clan in decision.Kingdom.Clans.Where(x => x.Leader != null && x.Leader.IsAlive && !x.IsUnderMercenaryService))
                {
                    var ranked = options.OfType<JObject>().Select(x => new { Option = x, Support = NativeClanPreference(decision, clan, x) })
                        .OrderByDescending(x => x.Support).ToList();
                    if (ranked.Count < 2 || ranked[0].Support - ranked[1].Support < 40f) continue;
                    participants.Add(new JObject { ["heroId"] = clan.Leader.StringId, ["role"] = "affected_lord", ["optionId"] = ranked[0].Option.Value<string>("id") });
                }
            }
            record.ParticipantsJson = participants.ToString(Formatting.None);
            _ownedNativeDecisions.Add(decision); _business.Add(record);
            decision.NotifyPlayer = false; decision.PlayerExamined = true;
            if (record.Status == "awaiting_sponsorship") FindBusinessSponsor(record);
            ChangedBusiness(record);
            ReignLog.Info("Government captured native decision: business=" + record.BusinessId + "; type="
                + record.NativeDecisionType + "; kingdom=" + record.KingdomStringId + "; target=" + record.TargetId
                + "; source=" + record.NativeCaptureSource + "; owner=" + record.NativeCapturedOwnerClanId);
            return true;
        }

        private void OnNativeKingdomDecisionAdded(KingdomDecision decision, bool isPlayerDecision)
        {
            // NPC AddDecision starts its election immediately AFTER this event. Removing a decision here
            // cannot cancel that call stack. Never create a second executable hearing from this notification.
            if (decision != null && NativeBusinessKinds.ContainsKey(decision.GetType().Name))
                ReignLog.Warn("Native Government decision passed through AddDecision: " + decision.GetType().Name
                    + "; kingdom=" + decision.Kingdom?.StringId + "; Reign did not capture the late event.");
        }
        private void MigrateNativeGovernmentBusiness()
        {
            foreach (var record in _business.Where(x => !GovernmentBusinessRules.IsClosed(x.Status)
                && !string.IsNullOrEmpty(x.NativeDecisionType)).ToList())
            {
                // A completed effect is historical evidence, even if an old save lacks capture metadata.
                if (record.ExecutionApplied) continue;
                ValidateNativeBusiness(record, out _);
            }
            foreach (var kingdom in Kingdom.All.Where(IsEligibleKingdom).ToList())
                foreach (var decision in kingdom.UnresolvedDecisions.ToList())
                    if (TryCaptureNativeDecision(decision, "unresolved")) kingdom.RemoveDecision(decision);
        }
        private void OnNativeKingdomDecisionConcluded(KingdomDecision decision, DecisionOutcome outcome, bool isPlayerDecision)
        {
            if (!ReferenceEquals(_executingGovernmentDecision, decision))
                foreach (var record in _business.Where(x => !GovernmentBusinessRules.IsClosed(x.Status)
                    && !x.ExecutionApplied && ReferenceEquals(NativeBusinessDecision(x), decision)).ToList())
                    QuarantineNativeBusiness(record, "Bannerlord concluded this decision outside its Government hearing. Its effects will not be replayed.");
            if (decision is KingdomPolicyDecision && NativeOutcomeEnforced(outcome)) RecordNativePolicyResolutionProgress(decision.Kingdom, decision);
        }

        private void OnGovernmentSettlementOwnerChanged(Settlement settlement, bool openToClaim, Hero newOwner,
            Hero oldOwner, Hero capturer, ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail detail)
        {
            foreach (var kingdom in new[] { newOwner?.Clan?.Kingdom, oldOwner?.Clan?.Kingdom }.Where(x => x != null).Distinct())
            { var state = GetGovernment(kingdom); if (state != null) ReconcileMembership(kingdom, state, false); }
            if (_executingGovernmentBusiness != null && Same(_executingGovernmentBusiness.TargetId, settlement?.StringId))
                _executingGovernmentBusiness.NativeOwnerChangeEventCount++;
            foreach (var record in _business.Where(x => !GovernmentBusinessRules.IsClosed(x.Status)
                && (x.Kind == "fief_allocation" || x.Kind == "fief_reassignment")
                && Same(x.TargetId, settlement?.StringId) && !ReferenceEquals(x, _executingGovernmentBusiness)).ToList())
                QuarantineNativeBusiness(record, "Settlement ownership changed outside this hearing (" + detail
                    + "). Its old award is held for review, even if the former clan regains the settlement.");
        }

        private bool ValidateNativeBusiness(ReignGovernmentBusinessRecord record, out string result)
        {
            result = string.Empty;
            if (record.Kind == "admission" || record.Kind == "recall") return ValidateMandateBusiness(record, out result);
            if (record.Kind == "edict") return ValidateEdictBusiness(record, out result);
            if (record.Kind == "seasonal") return ValidateSeasonalBusiness(record, out result);
            if (record.Kind == "world_action") return ValidateWorldBusiness(record, out result);
            KingdomDecision decision = NativeBusinessDecision(record);
            if (decision == null || decision.Kingdom == null || !IsEligibleKingdom(decision.Kingdom))
            { CloseInvalidBusiness(record, result = "The original decision or kingdom is unavailable."); return false; }
            string fingerprint = NativeRequestIdentity(decision, NativeTargetId(decision));
            if (!string.IsNullOrEmpty(record.CounterpartOfBusinessId)) fingerprint += "|response:" + record.CounterpartOfBusinessId;
            string block = ReignGovernmentNativeSafety.ExecutionBlock(record, ReignGovernmentPatches.VerifyReady(),
                fingerprint, NativeSettlementOwnerClanId(decision));
            if (block.Length > 0)
            { QuarantineNativeBusiness(record, result = block); return false; }
            if (decision is KingdomPolicyDecision policy && decision.Kingdom.HasPolicy(policy.Policy) != record.IsRepeal)
            { CloseInvalidBusiness(record, result = "The policy position changed; this petition is no longer applicable."); return false; }
            if (decision is SettlementClaimantDecision allocation && allocation.Settlement.MapFaction != decision.Kingdom
                || decision is SettlementClaimantPreliminaryDecision preliminary && preliminary.Settlement.MapFaction != decision.Kingdom)
            { CloseInvalidBusiness(record, result = "The settlement no longer belongs to this kingdom."); return false; }
            if (decision is ExpelClanFromKingdomDecision expulsion && expulsion.ClanToExpel.Kingdom != decision.Kingdom)
            { CloseInvalidBusiness(record, result = "The clan no longer belongs to this kingdom."); return false; }
            if (decision is DeclareWarDecision war && decision.Kingdom.IsAtWarWith(war.FactionToDeclareWarOn)
                || decision is MakePeaceKingdomDecision peace && !decision.Kingdom.IsAtWarWith(peace.FactionToMakePeaceWith))
            { CloseInvalidBusiness(record, result = "Diplomatic circumstances changed before the decision."); return false; }
            if (!(decision is KingdomPolicyDecision) && !(decision is SettlementClaimantDecision) && !decision.IsAllowed())
            { CloseInvalidBusiness(record, result = "Campaign circumstances no longer permit this decision."); return false; }
            return true;
        }

        private bool ExecuteBusiness(ReignGovernmentBusinessRecord record, string optionId, bool overridden, out string result)
        {
            result = string.Empty;
            if (record.ExecutionApplied || GovernmentBusinessRules.IsClosed(record.Status))
            { result = "This decision has already been applied."; return false; }
            if (!BusinessOptions(record).Any(x => Same(x.Value<string>("id"), optionId)))
            { result = "That option was not part of this hearing."; return false; }
            if (!ValidateNativeBusiness(record, out result)) return false;
            if (record.AuthorityLevelAtDecision == 0)
            {
                record.DecisionRulerHeroStringId = FindKingdom(record.KingdomStringId)?.Leader?.StringId ?? string.Empty;
                record.AuthorityLevelAtDecision = GetGovernment(record.KingdomStringId)?.Level ?? 1;
            }
            if (record.Kind == "admission" || record.Kind == "recall") return ExecuteMandateBusiness(record, optionId, out result);
            if (record.Kind == "edict") return ExecuteEdictBusiness(record, optionId, overridden, out result);
            if (record.Kind == "seasonal") return ExecuteSeasonalBusiness(record, optionId, overridden, out result);
            if (record.Kind == "world_action") return ExecuteWorldBusiness(record, optionId, overridden, out result);
            KingdomDecision decision = NativeBusinessDecision(record);
            if (optionId == "accept" && string.IsNullOrEmpty(record.CounterpartOfBusinessId)
                && TryCreateCounterpartHearing(record, overridden, out result)) return true;
            var candidate = GovernmentNativeCandidates(decision).Select((x, i) => new { Outcome = x, Option = NativeOption(x, i) })
                .FirstOrDefault(x => Same(x.Option.Value<string>("id"), optionId));
            if (candidate == null)
            { CloseInvalidBusiness(record, result = "The selected candidate is no longer eligible; a new proposal is required."); return false; }
            if (!ReignGovernmentNativeSafety.TryBeginEffect(record))
            { result = "The native effect cannot be started again."; return false; }
            try
            {
                // No native election, influence spending, or duplicate secondary relationship effects.
                if (optionId == "accept" && !string.IsNullOrEmpty(record.CounterpartOfBusinessId))
                    ApplyBilateralNativeEffect(record);
                else if (optionId == "accept" && decision is MakePeaceKingdomDecision acceptedPeace
                    && !(bool)(AccessTools.Field(typeof(MakePeaceKingdomDecision), "_applyResults")?.GetValue(acceptedPeace) ?? true))
                {
                    var appliedPeace = new MakePeaceKingdomDecision(acceptedPeace.ProposerClan, acceptedPeace.FactionToMakePeaceWith,
                        acceptedPeace.DailyTributeToBePaid, acceptedPeace.DailyTributeDurationInDays, true, true);
                    ApplyAuthorizedNativeOutcome(record, appliedPeace, GovernmentNativeCandidates(appliedPeace).First(NativeOutcomeEnforced));
                }
                else ApplyAuthorizedNativeOutcome(record, decision, candidate.Outcome);
                VerifyNativeBusinessEffect(record, decision, optionId);
                RecordNativeDiplomaticRelationReceipt(record, decision);
                record.ExecutionApplied = true; record.ExecutedOptionId = optionId; record.RulerOverrode = overridden;
                ReignLog.Info("Government applied native decision once: business=" + record.BusinessId
                    + "; type=" + record.NativeDecisionType + "; target=" + record.TargetId + "; option=" + optionId
                    + "; invocations=" + record.NativeEffectInvocationCount);
                decision.Kingdom.RemoveDecision(decision); decision.Kingdom.OnKingdomDecisionConcluded();
                RecordNativePolicyResolutionProgress(decision.Kingdom, decision);
            }
            catch (Exception ex)
            {
                record.Status = "execution_failed";
                record.Outcome = "Execution needs review after " + ex.GetType().Name + ". The vote is preserved; no automatic retry will duplicate uncertain effects.";
                record.NativeQuarantineReason = record.Outcome;
                ReignLog.Exception("Government native effect needs review: " + record.BusinessId, ex);
                ChangedBusiness(record); result = record.Outcome; return false;
            }
            FinishBusiness(record, optionId, overridden); result = record.Outcome; return true;
        }

        private void FinishBusiness(ReignGovernmentBusinessRecord record, string optionId, bool overridden)
        {
            record.ExecutionApplied = true; record.ExecutedOptionId = optionId; record.RulerOverrode = overridden;
            ApplyBusinessConsequences(record);
            record.Status = "decided"; record.ResolvedDay = CurrentDay();
            RecordPoliticalWorldEffect(record, optionId);
            CompleteParticipationPetition(record);
            string label = BusinessOptions(record).FirstOrDefault(x => Same(x.Value<string>("id"), optionId))?.Value<string>("label") ?? optionId;
            record.Outcome = (record.AuthorityLevelAtDecision == 5 ? "The binding government decision: " : "The recorded decision: ") + label + ".";
            CompleteCounterpartRefusal(record);
            if (!record.DecisionNewsRecorded) { RecordGovernmentDecisionNews(record); record.DecisionNewsRecorded = true; }
            NotifyGovernmentBusinessClosed(record); ChangedBusiness(record);
        }

        private void ApplyBusinessConsequences(ReignGovernmentBusinessRecord record)
        {
            if (record.ConsequencesApplied) return;
            Kingdom kingdom = FindKingdom(record.KingdomStringId);
            int authority = record.AuthorityLevelAtDecision;
            Hero responsibleRuler = FindHero(record.DecisionRulerHeroStringId);
            // Legacy pending saves lack attribution; never invent a predecessor's responsibility.
            if (authority == 0 || responsibleRuler == null) { record.ConsequencesApplied = true; return; }
            var participants = JArray.Parse(record.ParticipantsJson ?? "[]").OfType<JObject>().ToList();
            var ballots = JArray.Parse(record.VotesJson ?? "[]").OfType<JObject>().Where(x => x.Value<string>("memberHeroId") != "crown").ToList();
            var receipts = JArray.Parse(record.ConsequenceReceiptsJson ?? "[]");
            var ids = participants.Select(x => x.Value<string>("heroId")).Concat(new[] { record.PetitionerHeroStringId, record.SponsorHeroStringId })
                .Concat(ballots.Select(x => x.Value<string>("memberHeroId"))).Where(x => !string.IsNullOrEmpty(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            foreach (string id in ids)
            {
                if (receipts.Values<string>().Contains(id, StringComparer.OrdinalIgnoreCase)) continue;
                var roles = participants.Where(x => Same(x.Value<string>("heroId"), id)).ToList();
                bool advocate = Same(id, record.PetitionerHeroStringId) || Same(id, record.SponsorHeroStringId);
                string preferred = advocate ? record.RequestedOptionId : roles.FirstOrDefault(x => !string.IsNullOrEmpty(x.Value<string>("optionId")))?.Value<string>("optionId")
                    ?? ballots.FirstOrDefault(x => Same(x.Value<string>("memberHeroId"), id))?.Value<string>("optionId");
                if (roles.Any(x => x.Value<string>("role") == "target")) preferred = record.StatusQuoOptionId;
                else if (string.IsNullOrEmpty(preferred) && roles.Any(x => x.Value<string>("role") == "beneficiary"))
                    preferred = record.RequestedOptionId;
                bool lost = !Same(preferred, record.ExecutedOptionId);
                bool crownSpoke = record.CrownVoteStatus == "recorded" && !string.IsNullOrEmpty(record.CrownOptionId);
                bool opposed = crownSpoke && !Same(preferred, record.CrownOptionId);
                int delta = string.IsNullOrEmpty(preferred) ? 0 : opposed
                    ? GovernmentSessionRules.OppositionRelationDelta(authority, true, lost)
                    : crownSpoke && !lost ? advocate ? 4 : 2 : 0;
                ApplyRelation(responsibleRuler, FindHero(id), delta);
                receipts.Add(id); record.ConsequenceReceiptsJson = receipts.ToString(Formatting.None);
            }
            record.ConsequencesApplied = true;
        }

        internal bool OwnsNativeDecision(KingdomDecision decision) => _ownedNativeDecisions.Contains(decision);
        internal bool IsApplyingGovernmentNativeEffect => _executingGovernmentDecision != null;

        private ReignGovernmentBusinessRecord _executingGovernmentBusiness;
        private DecisionOutcome _authorizedNativeOutcome;
        private bool _nativeEffectPermitAvailable;

        internal bool CanApplyNativeDecision(KingdomDecision decision, DecisionOutcome outcome = null)
        {
            if (ReferenceEquals(_executingGovernmentDecision, decision))
            {
                return ReferenceEquals(_authorizedNativeOutcome, outcome)
                    && ReignGovernmentNativeSafety.TryConsumeEffectPermit(_executingGovernmentBusiness, ref _nativeEffectPermitAvailable);
            }
            return !TryCaptureNativeDecision(decision, "outcome");
        }

        private void ApplyAuthorizedNativeOutcome(ReignGovernmentBusinessRecord record, KingdomDecision decision, DecisionOutcome outcome)
        {
            if (!record.NativeEffectStarted || record.NativeEffectInvocationCount != 0 || !ReignGovernmentPatches.VerifyReady())
                throw new InvalidOperationException("The native effect does not have a fresh verified execution permit.");
            var previousDecision = _executingGovernmentDecision;
            var previousRecord = _executingGovernmentBusiness;
            var previousOutcome = _authorizedNativeOutcome;
            bool previousPermit = _nativeEffectPermitAvailable;
            try
            {
                _executingGovernmentDecision = decision; _executingGovernmentBusiness = record;
                _authorizedNativeOutcome = outcome; _nativeEffectPermitAvailable = true;
                decision.ApplyChosenOutcome(outcome);
                if (_nativeEffectPermitAvailable || record.NativeEffectInvocationCount != 1)
                    throw new InvalidOperationException("The native outcome did not consume its exact Government execution permit.");
            }
            finally
            {
                _executingGovernmentDecision = previousDecision; _executingGovernmentBusiness = previousRecord;
                _authorizedNativeOutcome = previousOutcome; _nativeEffectPermitAvailable = previousPermit;
            }
        }

        private void QuarantineNativeBusiness(ReignGovernmentBusinessRecord record, string reason)
        {
            if (record.NativeQuarantineReason == reason && record.Status == "execution_failed") return;
            record.NativeQuarantineReason = reason; record.Status = "execution_failed"; record.Outcome = reason;
            NotifyGovernmentBusinessClosed(record);
            ChangedBusiness(record);
            ReignLog.Warn("Government hearing held: " + record.BusinessId + "; " + reason);
        }

        private static string NativeSettlementOwnerClanId(KingdomDecision decision) =>
            (decision as SettlementClaimantDecision)?.Settlement?.OwnerClan?.StringId
            ?? (decision as SettlementClaimantPreliminaryDecision)?.Settlement?.OwnerClan?.StringId ?? string.Empty;
        private static JObject NativeOption(DecisionOutcome outcome, int index)
        {
            string target = (outcome as SettlementClaimantDecision.ClanAsDecisionOutcome)?.Clan?.StringId ?? string.Empty;
            bool affirmative = NativeOutcomeEnforced(outcome);
            return new JObject { ["id"] = target.Length > 0 ? "clan:" + target : affirmative ? "accept" : "reject",
                ["label"] = outcome.GetDecisionTitle()?.ToString() ?? "Option", ["description"] = outcome.GetDecisionDescription()?.ToString() ?? string.Empty,
                ["targetId"] = target, ["nativeIndex"] = index, ["affirmative"] = affirmative };
        }
        private static bool NativeOutcomeEnforced(DecisionOutcome outcome)
        {
            if (outcome == null) return false;
            if (outcome is SettlementClaimantDecision.ClanAsDecisionOutcome) return true;
            foreach (string field in new[] { "ShouldDecisionBeEnforced", "ShouldWarBeDeclared", "ShouldPeaceBeDeclared", "ShouldBeExpelled", "ShouldSettlementOwnerChange",
                "ShouldAllianceBeStarted", "ShouldTradeAgreementStart", "ShouldCallToWar", "ShouldAcceptCallToWar" })
            {
                var property = outcome.GetType().GetProperty(field);
                if (property?.PropertyType == typeof(bool)) return (bool)property.GetValue(outcome, null);
                var info = outcome.GetType().GetField(field);
                if (info?.FieldType == typeof(bool)) return (bool)info.GetValue(outcome);
            }
            if (outcome.GetType().DeclaringType == typeof(KingSelectionKingdomDecision)) return true;
            throw new NotSupportedException("Unrecognized native government outcome: " + outcome.GetType().FullName);
        }
        private static string NativeTargetId(KingdomDecision decision)
        {
            if (decision is KingdomPolicyDecision policy) return policy.Policy.StringId;
            if (decision is SettlementClaimantDecision allocation) return allocation.Settlement.StringId;
            if (decision is SettlementClaimantPreliminaryDecision preliminary) return preliminary.Settlement.StringId;
            if (decision is ExpelClanFromKingdomDecision expulsion) return expulsion.ClanToExpel.StringId;
            var targets = new List<string>();
            foreach (string name in new[] { "FactionToDeclareWarOn", "FactionToMakePeaceWith", "KingdomToStartAllianceWith", "TargetKingdom", "CallingKingdom", "CalledKingdom", "KingdomToCallToWarAgainst" })
                if (decision.GetType().GetField(name)?.GetValue(decision) is IFaction faction) targets.Add(faction.StringId);
            return string.Join("|", targets);
        }
        private static string NativeRequestIdentity(KingdomDecision decision, string target)
        {
            var identity = new JObject { ["kingdom"] = decision.Kingdom.StringId, ["type"] = decision.GetType().FullName,
                ["target"] = target, ["proposerClan"] = decision.ProposerClan?.StringId ?? string.Empty };
            foreach (string name in new[] { "DailyTributeToBePaid", "DailyTributeDurationInDays", "CallToWarCost", "_isInvertedDecision", "_applyResults", "_isProposedByOpponent",
                "ClanToExclude", "_ownerClan", "_capturerHero" })
            {
                var field = AccessTools.Field(decision.GetType(), name);
                if (field == null) continue;
                object value = field.GetValue(decision);
                if (value is Hero hero) identity[name] = hero.StringId;
                else if (value is Clan clan) identity[name] = clan.StringId;
                else if (value is bool flag) identity[name] = flag;
                else if (value is int number) identity[name] = number;
                else identity[name] = JValue.CreateNull();
            }
            return identity.ToString(Formatting.None);
        }
        private static IEnumerable<DecisionOutcome> GovernmentNativeCandidates(KingdomDecision decision)
        {
            var candidates = decision.DetermineInitialCandidates().ToList();
            if (decision is SettlementClaimantDecision allocation)
            {
                Clan ruler = decision.Kingdom.RulingClan;
                if (ruler != null && ruler != allocation.ClanToExclude && ruler.Kingdom == decision.Kingdom && !ruler.IsEliminated
                    && !ruler.IsUnderMercenaryService && ruler.Leader?.IsAlive == true
                    && !candidates.OfType<SettlementClaimantDecision.ClanAsDecisionOutcome>().Any(x => x.Clan == ruler))
                    candidates.Add(new SettlementClaimantDecision.ClanAsDecisionOutcome(ruler));
                candidates = candidates.OfType<SettlementClaimantDecision.ClanAsDecisionOutcome>().GroupBy(x => x.Clan.StringId)
                    .Select(x => (DecisionOutcome)x.First()).ToList();
            }
            return candidates;
        }

        private static float NativeClanPreference(KingdomDecision decision, Clan clan, JObject option)
        {
            if (decision == null || clan == null) return option.Value<bool>("affirmative") ? 1f : 0f;
            var candidate = GovernmentNativeCandidates(decision).Select((x, i) => new { Outcome = x, Id = NativeOption(x, i).Value<string>("id") })
                .FirstOrDefault(x => Same(x.Id, option.Value<string>("id")));
            return candidate == null ? float.MinValue : decision.DetermineSupport(clan, candidate.Outcome);
        }
        private static PolicyObject ExtractNativeDecisionPolicy(KingdomDecision decision) => (decision as KingdomPolicyDecision)?.Policy;
        private static ReignGovernmentActionKind MapNativeDecisionKind(KingdomDecision decision)
        {
            if (decision == null || !NativeBusinessKinds.TryGetValue(decision.GetType().Name, out string kind)) return ReignGovernmentActionKind.Advice;
            switch (kind)
            {
                case "policy": return ReignGovernmentActionKind.Policy;
                case "war": case "call_to_war": return ReignGovernmentActionKind.War;
                case "peace": return ReignGovernmentActionKind.Peace;
                case "fief_allocation": case "fief_reassignment": return ReignGovernmentActionKind.FiefTransfer;
                case "expulsion": return ReignGovernmentActionKind.MajorJustice;
                default: return ReignGovernmentActionKind.Treaty;
            }
        }
        private void RecordNativePolicyResolutionProgress(Kingdom kingdom, KingdomDecision decision)
        {
            var policy = ExtractNativeDecisionPolicy(decision); if (policy == null) return;
            RecordResolutionProgress(kingdom.StringId, kingdom.HasPolicy(policy) ? ReignGovernmentResolutionAction.EnactPolicy : ReignGovernmentResolutionAction.RepealPolicy,
                policy.StringId, 1, "Government decision applied the recorded policy position.");
        }
    }
    // Government attendance can legitimately place a village notable in the capital.
    // Native recruitment assumes rural sellers are physically in a village. Resolve
    // their village origin without moving them or changing their attendance/volunteers.
    [HarmonyPatch]
    internal static class ReignGovernmentRuralVolunteerPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(DefaultVolunteerModel), nameof(DefaultVolunteerModel.GetBasicVolunteer));
        }

        private static bool Prefix(Hero sellerHero, ref CharacterObject __result)
        {
            if (sellerHero?.IsRuralNotable != true || sellerHero.Culture == null
                || sellerHero.CurrentSettlement?.Village?.Bound != null) return true;

            Settlement village = sellerHero.HomeSettlement?.Village?.Bound != null
                ? sellerHero.HomeSettlement : sellerHero.BornSettlement;
            CharacterObject volunteer = village?.Village?.Bound?.IsCastle == true
                ? sellerHero.Culture.EliteBasicTroop : sellerHero.Culture.BasicTroop;
            if (volunteer == null) return true;
            __result = volunteer;
            return false;
        }
    }

    // Village issue predicates assume headmen and rural notables have a bound village.
    // Visiting the capital for government business does not make that notable a
    // town quest giver. Defer new issue discovery until village context returns;
    // existing issue updates and ordinary village issues stay native. Gang-leader
    // predicates also dereference town data before checking settlement context.
    // Defer their new issue discovery while travelling or visiting a non-town.
    [HarmonyPatch]
    internal static class ReignGovernmentRuralIssueContextPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(CampaignEvents), nameof(CampaignEvents.OnCheckForIssue), new[] { typeof(Hero) });
        }

        private static bool Prefix(Hero __0)
        {
            Settlement settlement = __0?.CurrentSettlement;
            return ReignGovernmentNativeSafety.AllowNativeIssueDiscovery(
                __0?.IsHeadman == true, __0?.IsRuralNotable == true,
                settlement != null, settlement?.Village?.Bound?.Town != null,
                __0?.IsGangLeader == true, settlement?.IsTown == true && settlement.Town != null);
        }
    }

    [HarmonyPatch]
    internal static class ReignGovernmentNativeNoticePatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.PropertyGetter(typeof(KingdomDecision), "NotifyPlayer");
            yield return AccessTools.PropertyGetter(typeof(KingdomDecision), "NeedsPlayerResolution");
        }
        private static void Postfix(KingdomDecision __instance, ref bool __result)
        {
            if (ReignGovernmentCampaignBehavior.Instance?.OwnsNativeDecision(__instance) == true) __result = false;
        }
    }
    [HarmonyPatch(typeof(Kingdom), nameof(Kingdom.AddDecision))]
    internal static class ReignGovernmentNativeProposalPatch
    {
        private static bool Prefix(KingdomDecision kingdomDecision) => ReignGovernmentCampaignBehavior.Instance?.TryCaptureNativeDecision(kingdomDecision, "proposal") != true;
    }
    [HarmonyPatch]
    internal static class ReignGovernmentNativeOutcomePatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (string name in ReignGovernmentCampaignBehavior.NativeBusinessKinds.Keys)
            {
                var type = typeof(KingdomDecision).Assembly.GetType(typeof(KingdomDecision).Namespace + "." + name);
                var method = type?.GetMethod("ApplyChosenOutcome", new[] { typeof(DecisionOutcome) });
                if (method == null) throw new MissingMethodException(type?.FullName ?? name, "ApplyChosenOutcome");
                yield return method;
            }
        }
        private static bool Prefix(KingdomDecision __instance, DecisionOutcome __0) => ReignGovernmentCampaignBehavior.Instance?.CanApplyNativeDecision(__instance, __0) != false;
    }
    [HarmonyPatch]
    internal static class ReignGovernmentNativeElectionPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(KingdomElection), "StartElection");
            yield return AccessTools.Method(typeof(KingdomElection), "ApplyChosenOutcome");
        }
        private static bool Prefix(KingdomDecision ____decision) => ReignGovernmentCampaignBehavior.Instance?.TryCaptureNativeDecision(____decision, "election") != true;
    }
}
