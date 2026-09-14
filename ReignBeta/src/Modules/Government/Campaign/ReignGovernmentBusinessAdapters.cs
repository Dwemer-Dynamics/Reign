using System;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Reign.Core.Contracts.Government;
using ReignBeta.World;
using TaleWorlds.CampaignSystem;

namespace ReignBeta.Government
{
    public sealed partial class ReignGovernmentCampaignBehavior
    {
        private string _executingSeasonalBusinessId;
        private void ImportSeasonalBusiness()
        {
            foreach (var existing in _business) NormalizeBusinessPublicText(existing);
            foreach (var resolution in _resolutions.Where(x => x.Status == "debate" || x.Status == "proposed").ToList())
            {
                if (_business.Any(x => x.Kind == "seasonal" && Same(x.TargetId, resolution.ResolutionId))) continue;
                var template = ReignGovernmentResolutionCatalog.Find(resolution.TemplateId);
                if (template == null) continue;
                _business.Add(new ReignGovernmentBusinessRecord
                {
                    KingdomStringId = resolution.KingdomStringId, Kind = "seasonal", Title = template.Title,
                    TargetId = resolution.ResolutionId, CreatedDay = resolution.ProposedDay,
                    PetitionerHeroStringId = resolution.SpeakerHeroStringId, SponsorHeroStringId = resolution.SpeakerHeroStringId,
                    SponsorReason = "Introduced by the party's seated speaker.", RequestedOptionId = "route:0", StatusQuoOptionId = "reject",
                    Summary = "The government asks the realm to address this demand. Choose a remedy or recommend rejection.",
                    OptionsJson = new JArray(
                        new JObject { ["id"] = "route:0", ["label"] = template.FirstRoute.Description, ["description"] = "Required: " + template.FirstRoute.Target + "; deadline: " + template.FirstRoute.DeadlineDays + " days.", ["affirmative"] = true },
                        new JObject { ["id"] = "route:1", ["label"] = template.SecondRoute.Description, ["description"] = "Required: " + template.SecondRoute.Target + "; deadline: " + template.SecondRoute.DeadlineDays + " days.", ["affirmative"] = true },
                        new JObject { ["id"] = "reject", ["label"] = "Reject the proposed remedies", ["description"] = "The resolution will not become an obligation.", ["affirmative"] = false }).ToString(Formatting.None)
                });
            }
            foreach (var record in _business.Where(x => x.Kind == "seasonal")) NormalizeBusinessPublicText(record);
        }
        private static bool IsPrivateRelationProgress(ReignGovernmentResolutionAction action) =>
            action == ReignGovernmentResolutionAction.ImproveClanRelation || action == ReignGovernmentResolutionAction.ImproveForeignRelation;

        public JArray GetPublicBusinessOptions(ReignGovernmentBusinessRecord record)
        {
            var options = JArray.Parse(record.OptionsJson ?? "[]");
            var resolution = record.Kind == "seasonal" ? _resolutions.FirstOrDefault(x => Same(x.ResolutionId, record.TargetId)) : null;
            var template = ReignGovernmentResolutionCatalog.Find(resolution?.TemplateId);
            foreach (var option in options.OfType<JObject>())
            {
                string id = option.Value<string>("id");
                if (record.Kind == "seasonal" && (id == "route:0" || id == "route:1"))
                {
                    var route = id == "route:0" ? template?.FirstRoute : template?.SecondRoute;
                    if (route == null)
                    {
                        option["label"] = "Review the recorded remedy";
                        option["description"] = "The original remedy details are unavailable. Its completion must be verified.";
                    }
                    else if (IsPrivateRelationProgress(route.Action))
                    {
                        option["label"] = route.Action == ReignGovernmentResolutionAction.ImproveClanRelation
                            ? "Seek reconciliation with the affected clan" : "Seek diplomatic reconciliation";
                        option["description"] = "Completion is verified privately; deadline: " + route.DeadlineDays + " days.";
                    }
                    else
                    {
                        option["label"] = GovernmentBusinessRules.PublicText(route.Description);
                        option["description"] = "Required: " + route.Target + "; deadline: " + route.DeadlineDays + " days.";
                    }
                }
                else
                {
                    option["label"] = GovernmentBusinessRules.PublicText(option.Value<string>("label"));
                    option["description"] = GovernmentBusinessRules.PublicText(option.Value<string>("description"));
                }
            }
            return options;
        }

        private void NormalizeBusinessPublicText(ReignGovernmentBusinessRecord record)
        {
            record.Title = GovernmentBusinessRules.PublicText(record.Title);
            record.Summary = GovernmentBusinessRules.PublicText(record.Summary);
            record.Outcome = GovernmentBusinessRules.PublicText(record.Outcome);
            record.OptionsJson = GetPublicBusinessOptions(record).ToString(Formatting.None);
        }

        private bool ValidateSeasonalBusiness(ReignGovernmentBusinessRecord record, out string result)
        {
            result = string.Empty;
            var resolution = _resolutions.FirstOrDefault(x => Same(x.ResolutionId, record.TargetId));
            if (resolution != null && (resolution.Status == "debate" || resolution.Status == "proposed")) return true;
            CloseInvalidBusiness(record, result = "This seasonal resolution is no longer awaiting a decision.");
            return false;
        }
        private bool ExecuteSeasonalBusiness(ReignGovernmentBusinessRecord record, string optionId, bool overridden, out string result)
        {
            var resolution = _resolutions.First(x => Same(x.ResolutionId, record.TargetId));
            var kingdom = FindKingdom(record.KingdomStringId);
            _executingSeasonalBusinessId = record.BusinessId;
            try
            {
                if (optionId == "reject")
                {
                    if (GetGovernment(kingdom)?.Level < 5 && record.WinningOptionId != "reject")
                    {
                        resolution.Status = "proposed";
                        if (!DeclineResolution(resolution.ResolutionId, kingdom.Leader, out result)) return false;
                        // The established demand-failure pipeline already applied speaker and settlement consequences.
                        record.ConsequencesApplied = true;
                    }
                    else
                    {
                        resolution.Status = "rejected"; resolution.ResolvedDay = CurrentDay();
                        resolution.Outcome = "The government rejected its proposed remedies."; resolution.Revision++;
                    }
                }
                else
                {
                    resolution.Status = "proposed";
                    if (!AcceptResolution(resolution.ResolutionId, optionId == "route:0" ? 0 : 1, kingdom.Leader, out result))
                    { record.Status = "execution_failed"; record.Outcome = result; ChangedBusiness(record); return false; }
                }
                FinishBusiness(record, optionId, overridden); result = record.Outcome; return true;
            }
            finally { _executingSeasonalBusinessId = null; }
        }

        private bool TryAuthorizeBusinessAction(ReignWorldActionRecord action, out ReignActionResult blockedResult)
        {
            blockedResult = null;
            Kingdom kingdom = ResolveActorKingdom(action);
            ReignGovernmentActionKind kind = MapActionKind(action?.Type ?? ReignWorldActionType.Unknown);
            if (kingdom == null || kind == ReignGovernmentActionKind.Advice || kingdom.Leader == null || EnsureGovernment(kingdom) == null) return true;
            string payload = GovernmentActionIdentity(action);
            var record = _business.FirstOrDefault(x => Same(x.ActionCorrelationId, action.ActionId) && string.IsNullOrEmpty(x.CounterpartOfBusinessId));
            if (record == null)
            {
                record = new ReignGovernmentBusinessRecord
                {
                    KingdomStringId = kingdom.StringId, Kind = "world_action", ActionKindValue = (int)kind,
                    Title = GovernmentActionTitle(action), Summary = GovernmentActionSummary(action),
                    TargetId = action.TargetKingdomStringId ?? action.TargetSettlementStringId ?? string.Empty,
                    ActionCorrelationId = action.ActionId, ActionPayloadJson = payload,
                    PetitionerHeroStringId = string.IsNullOrWhiteSpace(action.ActorHeroStringId) ? kingdom.Leader.StringId : action.ActorHeroStringId,
                    CreatedDay = CurrentDay(), RequestedOptionId = "accept", StatusQuoOptionId = "reject",
                    IsUrgent = kind == ReignGovernmentActionKind.War || kind == ReignGovernmentActionKind.Peace,
                    RecommendedOptionId = "accept", RecommendationRulerHeroStringId = kingdom.Leader.StringId,
                    OptionsJson = new JArray(new JObject { ["id"] = "accept", ["label"] = "Authorize the proposed action", ["description"] = GovernmentActionSummary(action), ["affirmative"] = true },
                        new JObject { ["id"] = "reject", ["label"] = "Refuse authorization", ["affirmative"] = false }).ToString(Formatting.None)
                };
                var affected = ReignObjectResolver.FindHero(action.TargetHeroStringId)
                    ?? ReignObjectResolver.FindClan(action.TargetClanStringId)?.Leader;
                if (affected != null && (kind == ReignGovernmentActionKind.MajorJustice || action.Type == ReignWorldActionType.RegularDismissPlayerVassal))
                    record.ParticipantsJson = new JArray(action.Type == ReignWorldActionType.PoliticsRestoreExiledClan
                        ? new JObject { ["heroId"] = affected.StringId, ["role"] = "beneficiary", ["optionId"] = "accept" }
                        : new JObject { ["heroId"] = affected.StringId, ["role"] = "target" }).ToString(Formatting.None);
                _business.Add(record);
                if (!Same(record.PetitionerHeroStringId, kingdom.Leader.StringId))
                {
                    record.Status = "awaiting_sponsorship";
                    record.RecommendedOptionId = string.Empty;
                    record.RecommendationRulerHeroStringId = string.Empty;
                    FindBusinessSponsor(record);
                }
                ChangedBusiness(record);
            }
            if (!string.Equals(record.ActionPayloadJson, payload, StringComparison.Ordinal))
            { blockedResult = ReignActionResult.FailTerminal("The action's actors, targets, or terms changed after its hearing.", "government_terms_changed", "government_authority"); return false; }
            if (record.Status == "authorized") return true;
            if (record.Status == "decided" && record.ExecutedOptionId == "accept")
            { blockedResult = ReignActionResult.NoOp("This government-authorized action already completed."); return false; }
            if (GovernmentBusinessRules.IsRecordedAuthorizationRefusal(record.Status, record.ExecutedOptionId))
            { blockedResult = RecordedAuthorizationRefusal(record); return false; }
            if (GovernmentBusinessRules.IsClosed(record.Status))
            { blockedResult = ReignActionResult.FailTerminal("The government process did not authorize this action.", "government_refused", "government_authority"); return false; }
            // Progress holds the existing campaign queue without spending its finite execution retry budget.
            var pending = record.Status == "awaiting_counterpart"
                ? _business.FirstOrDefault(x => Same(x.BusinessId, record.CounterpartBusinessId)) ?? record : record;
            Kingdom pendingKingdom = FindKingdom(pending.KingdomStringId);
            blockedResult = ReignActionResult.Progress("Awaiting the government of "
                + (pendingKingdom?.Name?.ToString() ?? pending.KingdomStringId) + " to decide: " + pending.Title)
                .WithResultCode("government_hearing_pending").WithNextAttemptDelay(0.25f)
                .WithDiagnostic("governmentBusinessId", pending.BusinessId)
                .WithDiagnostic("governmentKingdomId", pending.KingdomStringId)
                .WithDiagnostic("governmentStatus", pending.Status);
            return false;
        }

        internal bool TryGetRecordedAuthorizationRefusal(ReignWorldActionRecord action, out ReignActionResult result)
        {
            result = null;
            if (action == null) return false;
            var record = _business.FirstOrDefault(x => Same(x.ActionCorrelationId, action.ActionId)
                && string.IsNullOrEmpty(x.CounterpartOfBusinessId));
            if (record == null
                || !GovernmentBusinessRules.IsRecordedAuthorizationRefusal(record.Status, record.ExecutedOptionId)
                || !string.Equals(record.ActionPayloadJson, GovernmentActionIdentity(action), StringComparison.Ordinal))
                return false;
            result = RecordedAuthorizationRefusal(record);
            return true;
        }

        private static ReignActionResult RecordedAuthorizationRefusal(ReignGovernmentBusinessRecord record) =>
            ReignActionResult.Rejected("The government process did not authorize this action.",
                "government_authorization_refused", "government_authority")
                .WithDiagnostic("governmentBusinessId", record.BusinessId)
                .WithDiagnostic("governmentStatus", record.Status)
                .WithDiagnostic("governmentExecutedOptionId", record.ExecutedOptionId);
        private static string GovernmentActionIdentity(ReignWorldActionRecord action) => new JObject
        {
            ["type"] = action.TypeValue, ["actorHero"] = action.ActorHeroStringId, ["actorClan"] = action.ActorClanStringId,
            ["actorKingdom"] = action.ActorKingdomStringId, ["targetHero"] = action.TargetHeroStringId, ["targetClan"] = action.TargetClanStringId,
            ["targetKingdom"] = action.TargetKingdomStringId, ["targetSettlement"] = action.TargetSettlementStringId,
            ["terms"] = action.TermsJson, ["termsHash"] = action.TermsHash, ["negotiation"] = action.NegotiationId,
            ["authorization"] = action.AuthorizationMode
        }.ToString(Formatting.None);
        private bool ValidateWorldBusiness(ReignGovernmentBusinessRecord record, out string result)
        {
            result = string.Empty;
            if (!string.IsNullOrEmpty(record.ActionCorrelationId) && FindKingdom(record.KingdomStringId)?.Leader != null) return true;
            CloseInvalidBusiness(record, result = "The action's kingdom or saved identity is unavailable."); return false;
        }
        private bool ExecuteWorldBusiness(ReignGovernmentBusinessRecord record, string optionId, bool overridden, out string result)
        {
            if (optionId == "reject") { FinishBusiness(record, optionId, overridden); result = record.Outcome; return true; }
            record.RulerOverrode = overridden;
            if (TryAuthorizeWorldCounterpart(record, out result)) return true;
            record.Status = "authorized";
            record.Outcome = "Government authorization is recorded. The original action queue must still validate and apply the exact terms.";
            ChangedBusiness(record); result = record.Outcome; return true;
        }
        private void CompleteGovernmentBusinessAction(ReignWorldActionRecord action)
        {
            foreach (var record in _business.Where(x => Same(x.ActionCorrelationId, action.ActionId) && x.Status == "authorized").ToList())
                if (string.Equals(record.ActionPayloadJson, GovernmentActionIdentity(action), StringComparison.Ordinal)) FinishBusiness(record, "accept", record.RulerOverrode);
        }

        private void ObserveGovernmentActionQueue(ReignGovernmentBusinessRecord record)
        {
            if (record.Kind != "world_action" || GovernmentBusinessRules.IsClosed(record.Status)) return;
            var queue = ReignBeta.Campaign.ReignAICampaignBehavior.Instance;
            if (queue == null) return;
            var action = queue.Actions.FirstOrDefault(x => Same(x.ActionId, record.ActionCorrelationId));
            if (action == null)
            {
                if (CurrentDay() > record.CreatedDay + 1f) CloseInvalidBusiness(record, "The originating action is no longer in the campaign queue.");
                return;
            }
            if (!action.IsTerminal) return;
            if (action.Status == ReignWorldActionStatus.Completed && record.Status == "authorized") CompleteGovernmentBusinessAction(action);
            else CloseInvalidBusiness(record, "The originating action ended before government execution: " + action.Status + ". No success consequences were applied.");
        }
    }
}
