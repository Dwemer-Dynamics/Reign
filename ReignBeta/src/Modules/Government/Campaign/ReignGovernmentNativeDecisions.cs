using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Reign.Core.Contracts.Government;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Election;

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

        public bool TryCaptureNativeDecision(KingdomDecision decision)
        {
            if (decision == null || decision is KingSelectionKingdomDecision || !NativeBusinessKinds.TryGetValue(decision.GetType().Name, out string kind)
                || decision.GetType().Namespace != typeof(KingdomDecision).Namespace) return false;
            if (EnsureGovernment(decision.Kingdom) == null) return false;
            if (_ownedNativeDecisions.Contains(decision)) return true;
            string target = NativeTargetId(decision);
            string fingerprint = NativeRequestIdentity(decision, target);
            if (!string.IsNullOrEmpty(_capturingCounterpartOfBusinessId)) fingerprint += "|response:" + _capturingCounterpartOfBusinessId;
            if (_business.Any(x => !GovernmentBusinessRules.IsClosed(x.Status) && string.Equals(x.NativeFingerprint, fingerprint, StringComparison.Ordinal))) return true;
            var candidates = new List<DecisionOutcome>();
            JArray options;
            string adapterError = string.Empty;
            try
            {
                candidates = decision.DetermineInitialCandidates().ToList();
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
                Title = decision.GetGeneralTitle()?.ToString() ?? kind, Summary = decision.GetChooseDescription()?.ToString() ?? string.Empty,
                NativeDecisionType = decision.GetType().Name, NativeFingerprint = fingerprint, TargetId = target,
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
            return true;
        }

        private void OnNativeKingdomDecisionAdded(KingdomDecision decision, bool isPlayerDecision)
        { if (TryCaptureNativeDecision(decision)) decision.Kingdom.RemoveDecision(decision); }
        private void MigrateNativeGovernmentBusiness()
        {
            foreach (var kingdom in Kingdom.All.Where(IsEligibleKingdom).ToList())
                foreach (var decision in kingdom.UnresolvedDecisions.ToList())
                    if (TryCaptureNativeDecision(decision)) kingdom.RemoveDecision(decision);
        }
        private void OnNativeKingdomDecisionConcluded(KingdomDecision decision, DecisionOutcome outcome, bool isPlayerDecision)
        { if (decision is KingdomPolicyDecision && NativeOutcomeEnforced(outcome)) RecordNativePolicyResolutionProgress(decision.Kingdom, decision); }

        private bool ValidateNativeBusiness(ReignGovernmentBusinessRecord record, out string result)
        {
            result = string.Empty;
            if (record.Kind == "seasonal") return ValidateSeasonalBusiness(record, out result);
            if (record.Kind == "world_action") return ValidateWorldBusiness(record, out result);
            KingdomDecision decision = NativeBusinessDecision(record);
            if (decision == null || decision.Kingdom == null || !IsEligibleKingdom(decision.Kingdom))
            { CloseInvalidBusiness(record, result = "The original decision or kingdom is unavailable."); return false; }
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
            if (record.Kind == "seasonal") return ExecuteSeasonalBusiness(record, optionId, overridden, out result);
            if (record.Kind == "world_action") return ExecuteWorldBusiness(record, optionId, overridden, out result);
            KingdomDecision decision = NativeBusinessDecision(record);
            if (optionId == "accept" && string.IsNullOrEmpty(record.CounterpartOfBusinessId)
                && TryCreateCounterpartHearing(record, overridden, out result)) return true;
            var candidate = decision.DetermineInitialCandidates().Select((x, i) => new { Outcome = x, Option = NativeOption(x, i) })
                .FirstOrDefault(x => Same(x.Option.Value<string>("id"), optionId));
            if (candidate == null)
            { CloseInvalidBusiness(record, result = "The selected candidate is no longer eligible; a new proposal is required."); return false; }
            try
            {
                _executingGovernmentDecision = decision;
                // No native election, influence spending, or duplicate secondary relationship effects.
                if (optionId == "accept" && !string.IsNullOrEmpty(record.CounterpartOfBusinessId))
                    ApplyBilateralNativeEffect(record);
                else if (optionId == "accept" && decision is MakePeaceKingdomDecision acceptedPeace
                    && !(bool)(AccessTools.Field(typeof(MakePeaceKingdomDecision), "_applyResults")?.GetValue(acceptedPeace) ?? true))
                {
                    var appliedPeace = new MakePeaceKingdomDecision(acceptedPeace.ProposerClan, acceptedPeace.FactionToMakePeaceWith,
                        acceptedPeace.DailyTributeToBePaid, acceptedPeace.DailyTributeDurationInDays, true, true);
                    _executingGovernmentDecision = appliedPeace;
                    appliedPeace.ApplyChosenOutcome(appliedPeace.DetermineInitialCandidates().First(NativeOutcomeEnforced));
                }
                else decision.ApplyChosenOutcome(candidate.Outcome);
                VerifyNativeBusinessEffect(record, decision, optionId);
                RecordNativeDiplomaticRelationReceipt(record, decision);
                record.ExecutionApplied = true; record.ExecutedOptionId = optionId; record.RulerOverrode = overridden;
                decision.Kingdom.RemoveDecision(decision); decision.Kingdom.OnKingdomDecisionConcluded();
                RecordNativePolicyResolutionProgress(decision.Kingdom, decision);
            }
            catch (Exception ex)
            {
                record.Status = "execution_failed";
                record.Outcome = "Execution needs review after " + ex.GetType().Name + ". The vote is preserved; no automatic retry will duplicate uncertain effects.";
                ChangedBusiness(record); result = record.Outcome; return false;
            }
            finally { _executingGovernmentDecision = null; }
            FinishBusiness(record, optionId, overridden); result = record.Outcome; return true;
        }

        private void FinishBusiness(ReignGovernmentBusinessRecord record, string optionId, bool overridden)
        {
            record.ExecutionApplied = true; record.ExecutedOptionId = optionId; record.RulerOverrode = overridden;
            ApplyBusinessConsequences(record);
            record.Status = "decided"; record.ResolvedDay = CurrentDay();
            string label = BusinessOptions(record).FirstOrDefault(x => Same(x.Value<string>("id"), optionId))?.Value<string>("label") ?? optionId;
            record.Outcome = (record.AuthorityLevelAtDecision == 5 ? "The binding government decision: " : "The recorded decision: ") + label + ".";
            CompleteCounterpartRefusal(record);
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
            bool granted = record.Kind == "seasonal"
                ? BusinessOptions(record).FirstOrDefault(x => Same(x.Value<string>("id"), record.ExecutedOptionId))?.Value<bool>("affirmative") == true
                : Same(record.ExecutedOptionId, record.RequestedOptionId);
            bool rulerRecommendedRequest = record.Kind == "seasonal"
                ? BusinessOptions(record).FirstOrDefault(x => Same(x.Value<string>("id"), record.RecommendedOptionId))?.Value<bool>("affirmative") == true
                : Same(record.RecommendedOptionId, record.RequestedOptionId);
            var participants = JArray.Parse(record.ParticipantsJson ?? "[]").OfType<JObject>().ToList();
            var receipts = JArray.Parse(record.ConsequenceReceiptsJson ?? "[]");
            var ids = participants.Select(x => x.Value<string>("heroId")).Concat(new[] { record.PetitionerHeroStringId, record.SponsorHeroStringId })
                .Where(x => !string.IsNullOrEmpty(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            foreach (string id in ids)
            {
                if (receipts.Values<string>().Contains(id, StringComparer.OrdinalIgnoreCase)) continue;
                var roles = participants.Where(x => Same(x.Value<string>("heroId"), id)).ToList();
                bool claimant = !Same(id, record.PetitionerHeroStringId) && !Same(id, record.SponsorHeroStringId)
                    && roles.Any(x => !string.IsNullOrEmpty(x.Value<string>("optionId")));
                int delta = GovernmentBusinessRules.ParticipantRelationDelta(claimant ? roles.Any(x => Same(x.Value<string>("optionId"), record.ExecutedOptionId)) : granted,
                    claimant ? roles.Any(x => Same(x.Value<string>("optionId"), record.RecommendedOptionId)) : rulerRecommendedRequest,
                    authority == 5, Same(id, record.PetitionerHeroStringId), Same(id, record.SponsorHeroStringId), claimant,
                    roles.Any(x => x.Value<string>("role") == "target"));
                ApplyRelation(responsibleRuler, FindHero(id), delta);
                receipts.Add(id);
                record.ConsequenceReceiptsJson = receipts.ToString(Formatting.None);
            }
            if (record.RulerOverrode && authority > 1)
            {
                int loss = authority >= 4 ? 5 : authority == 3 ? 2 : 1;
                foreach (var ballot in JArray.Parse(record.VotesJson).OfType<JObject>().Where(x => !Same(x.Value<string>("optionId"), record.ExecutedOptionId)))
                {
                    string memberId = ballot.Value<string>("memberHeroId");
                    if (!ids.Contains(memberId, StringComparer.OrdinalIgnoreCase) && !receipts.Values<string>().Contains(memberId, StringComparer.OrdinalIgnoreCase))
                    {
                        ApplyRelation(responsibleRuler, FindHero(memberId), -loss);
                        receipts.Add(memberId); record.ConsequenceReceiptsJson = receipts.ToString(Formatting.None);
                    }
                }
                if (authority >= 3 && !record.OverrideSettlementConsequenceApplied)
                { ApplySettlementChange(kingdom, authority == 4 ? -3 : -1, 0, 0); record.OverrideSettlementConsequenceApplied = true; }
            }
            record.ConsequencesApplied = true;
        }

        internal bool OwnsNativeDecision(KingdomDecision decision) => _ownedNativeDecisions.Contains(decision);
        internal bool IsApplyingGovernmentNativeEffect => _executingGovernmentDecision != null;

        internal bool CanApplyNativeDecision(KingdomDecision decision) => ReferenceEquals(_executingGovernmentDecision, decision) || !TryCaptureNativeDecision(decision);
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
        private static float NativeClanPreference(KingdomDecision decision, Clan clan, JObject option)
        {
            if (decision == null || clan == null) return option.Value<bool>("affirmative") ? 1f : 0f;
            var candidate = decision.DetermineInitialCandidates().Select((x, i) => new { Outcome = x, Id = NativeOption(x, i).Value<string>("id") })
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
        private static bool Prefix(KingdomDecision kingdomDecision) => ReignGovernmentCampaignBehavior.Instance?.TryCaptureNativeDecision(kingdomDecision) != true;
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
                if (method != null) yield return method;
            }
        }
        private static bool Prefix(KingdomDecision __instance) => ReignGovernmentCampaignBehavior.Instance?.CanApplyNativeDecision(__instance) != false;
    }
    [HarmonyPatch(typeof(KingdomElection), "ApplyChosenOutcome")]
    internal static class ReignGovernmentNativeElectionPatch
    {
        private static bool Prefix(KingdomDecision ____decision) => ReignGovernmentCampaignBehavior.Instance?.TryCaptureNativeDecision(____decision) != true;
    }
}
