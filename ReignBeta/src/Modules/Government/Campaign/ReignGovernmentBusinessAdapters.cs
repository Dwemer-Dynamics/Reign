using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Reign.Core.Contracts.Government;
using ReignBeta.World;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using ReignBeta.Government.Domain;
using ReignBeta.WorldSimulation.World;
using ReignBeta.Platform.Runtime;

namespace ReignBeta.Government.Campaign
{
    public sealed partial class ReignGovernmentCampaignBehavior
    {
        private string _executingSeasonalBusinessId;
        internal void ImportSeasonalBusiness()
        {
            foreach (var existing in _business) NormalizeBusinessPublicText(existing);
            foreach (var resolution in _resolutions.Where(x => x.Status == "debate" || x.Status == "proposed").ToList())
            {
                if (string.IsNullOrWhiteSpace(resolution.KingdomStringId)
                    || FindUniqueSeasonalResolution(resolution.ResolutionId) != resolution
                    || FindSeasonalBusinessForResolution(resolution) != null)
                    continue;
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
            foreach (var record in _business.Where(x => x.Kind == "seasonal"))
            {
                NormalizeBusinessPublicText(record);
                InitializeBusinessSchedule(record);
            }
        }
        internal static bool IsPrivateRelationProgress(ReignGovernmentResolutionAction action) =>
            action == ReignGovernmentResolutionAction.ImproveClanRelation || action == ReignGovernmentResolutionAction.ImproveForeignRelation;

        public JArray GetPublicBusinessOptions(ReignGovernmentBusinessRecord record) =>
            RenderBusinessOptions(record, live: !ConstructionOptionsAreHistorical(record));

        private bool IsConstructionBusiness(ReignGovernmentBusinessRecord record) =>
            record?.Kind == "seasonal" && IsNamedConstruction(FindSeasonalResolutionForBusiness(record));

        private static bool ConstructionOptionsAreHistorical(ReignGovernmentBusinessRecord record) =>
            GovernmentBusinessRules.IsClosed(record.Status) || record.ExecutionApplied
                || record.Status == "execution_failed" || !string.IsNullOrWhiteSpace(record.WinningOptionId);

        private List<JObject> LiveBusinessOptions(ReignGovernmentBusinessRecord record) =>
            IsConstructionBusiness(record) ? RenderBusinessOptions(record, live: true).OfType<JObject>().ToList()
                : BusinessOptions(record);

        private bool CanSelectBusinessOption(ReignGovernmentBusinessRecord record, string optionId) =>
            record != null && !GovernmentBusinessRules.IsClosed(record.Status)
                && (!IsConstructionBusiness(record) || !ConstructionOptionsAreHistorical(record))
                && LiveBusinessOptions(record).Any(x => x.Value<string>("id") == optionId);

        private string PublicBusinessRecommendation(ReignGovernmentBusinessRecord record) =>
            !IsConstructionBusiness(record) || ConstructionOptionsAreHistorical(record)
                || CanSelectBusinessOption(record, record.RecommendedOptionId) ? record.RecommendedOptionId : string.Empty;

        private JArray RenderBusinessOptions(ReignGovernmentBusinessRecord record, bool live)
        {
            var options = JArray.Parse(record.OptionsJson ?? "[]");
            var resolution = record.Kind == "seasonal" ? FindSeasonalResolutionForBusiness(record) : null;
            var template = ReignGovernmentResolutionCatalog.Find(resolution?.TemplateId);
            var namedConstruction = IsNamedConstruction(resolution);
            if (namedConstruction && ConstructionOptionsAreHistorical(record)) return options;
            var realm = live && namedConstruction ? FindKingdom(record.KingdomStringId) : null;
            var project = realm != null ? FindLiveConstructionProject(realm, resolution) : null;
            if (live && namedConstruction)
            {
                bool treasury = CanOfferConstructionTreasury(realm, resolution);
                foreach (var option in options.OfType<JObject>().ToList())
                {
                    string id = option.Value<string>("id");
                    if (id == "route:0" && project != null || id == "route:1" && treasury || id == "reject") continue;
                    option.Remove();
                }
            }
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
                    else if (namedConstruction && route.Action == ReignGovernmentResolutionAction.ChangeConstruction)
                    {
                        string projectName = ConstructionProjectLabel(resolution, project);
                        option["label"] = projectName.Length > 0 ? "Prioritize " + projectName : route.Description;
                        option["description"] = SeasonalTargetDescription(resolution, route.Action) + " "
                            + "Make this the active construction project by " + route.DeadlineDays
                            + " days after acceptance. The native change will be verified.";
                    }
                    else if (IsPrivateRelationProgress(route.Action))
                    {
                        option["label"] = route.Action == ReignGovernmentResolutionAction.ImproveClanRelation
                            ? "Seek reconciliation with the affected clan" : "Seek diplomatic reconciliation";
                        option["description"] = SeasonalTargetDescription(resolution, route.Action)
                            + " Completion is verified privately; deadline: " + route.DeadlineDays + " days after acceptance.";
                    }
                    else
                    {
                        option["label"] = GovernmentBusinessRules.PublicText(route.Description);
                        option["description"] = SeasonalTargetDescription(resolution, route.Action) + " "
                            + SeasonalRequirementDescription(route, template.Scale);
                    }
                }
                else
                {
                    option["label"] = GovernmentBusinessRules.PublicText(option.Value<string>("label"));
                    var policy = record.Kind == "policy" ? PolicyObject.All.FirstOrDefault(x => x.StringId == record.TargetId) : null;
                    option["description"] = policy != null && id == "accept" ? PolicyGovernmentDescription(policy)
                        : record.Kind == "edict" && id == "accept" ? PublicEdictDescription(record)
                        : GovernmentBusinessRules.PublicText(option.Value<string>("description"));
                }
            }
            return options;
        }

        private static string SeasonalTargetDescription(ReignGovernmentResolutionRecord resolution, ReignGovernmentResolutionAction action)
        {
            var details = new List<string>();
            var settlement = Settlement.Find(resolution.TargetSettlementStringId);
            details.Add("Recorded local target: " + (settlement?.Name?.ToString() ?? "unavailable; verify before undertaking this remedy") + ".");
            switch (action)
            {
                case ReignGovernmentResolutionAction.MakePeace:
                case ReignGovernmentResolutionAction.DeclareWar:
                case ReignGovernmentResolutionAction.WinEnemyEngagements:
                case ReignGovernmentResolutionAction.ImproveForeignRelation:
                case ReignGovernmentResolutionAction.SignTradeAgreement:
                case ReignGovernmentResolutionAction.SignNonAggressionPact:
                case ReignGovernmentResolutionAction.ExchangePrisoners:
                    details.Add("Foreign realm: " + (FindKingdom(resolution.TargetKingdomStringId)?.Name?.ToString() ?? "unavailable") + "."); break;
                case ReignGovernmentResolutionAction.CompensateClan:
                case ReignGovernmentResolutionAction.ImproveClanRelation:
                case ReignGovernmentResolutionAction.GrantFief:
                case ReignGovernmentResolutionAction.ReturnFief:
                    details.Add("Affected clan: " + (Clan.FindFirst(x => Same(x.StringId, resolution.TargetClanStringId))?.Name?.ToString() ?? "unavailable") + "."); break;
                case ReignGovernmentResolutionAction.HuntRaiderLord:
                case ReignGovernmentResolutionAction.CaptureCulprit:
                case ReignGovernmentResolutionAction.ReplaceGovernor:
                case ReignGovernmentResolutionAction.ReleasePrisoners:
                case ReignGovernmentResolutionAction.RansomPrisoners:
                    details.Add("Recorded character: " + (FindHero(resolution.TargetHeroStringId)?.Name?.ToString() ?? "unavailable") + "."); break;
                case ReignGovernmentResolutionAction.EnactPolicy:
                case ReignGovernmentResolutionAction.RepealPolicy:
                    details.Add("Recorded policy: " + (PolicyObject.All.FirstOrDefault(x => x.StringId == resolution.TargetPolicyStringId)?.Name?.ToString() ?? "unavailable") + "."); break;
            }
            return GovernmentBusinessRules.PublicText(string.Join(" ", details));
        }

        internal static string SeasonalRequirementDescription(ReignGovernmentResolutionRoute route, ReignGovernmentResolutionScale scale)
        {
            if (IsPrivateRelationProgress(route.Action))
                return "Completion is verified privately; deadline: " + route.DeadlineDays + " days after acceptance.";
            string amount = route.Target.ToString("N0", CultureInfo.InvariantCulture);
            string requirement;
            if (UsesImmediateGold(route.Action)) requirement = "The ruler pays " + amount + " denars immediately on acceptance.";
            else if (route.Action == ReignGovernmentResolutionAction.GarrisonMinimum)
                requirement = "Reach a garrison of at least " + amount + " troops.";
            else if (route.Action == ReignGovernmentResolutionAction.GarrisonMaximum)
                requirement = "Reduce the garrison to at most " + amount + " troops.";
            else if (route.Action == ReignGovernmentResolutionAction.FieldArmyMinimum)
                requirement = "Reach at least " + amount + " troops in the realm's field armies.";
            else if (route.Action == ReignGovernmentResolutionAction.CreateMilitia)
                requirement = "Reach at least " + amount + " militia.";
            else if (UsesWorldMeasurement(route.Action))
                requirement = "Increase " + SeasonalUnitLabel(route.Unit) + " by " + amount + " from its value at acceptance.";
            else requirement = "Required: " + amount + " " + SeasonalUnitLabel(route.Unit) + ".";
            int dailyCost = DailyCommitmentGoldCost(route.Action, scale);
            if (dailyCost > 0) requirement += " The ruler pays " + dailyCost.ToString("N0", CultureInfo.InvariantCulture)
                + " denars for each honored commitment day; an unfunded day earns no progress.";
            return requirement + " Deadline: " + route.DeadlineDays + " days after acceptance.";
        }

        private static string SeasonalUnitLabel(ReignGovernmentResolutionUnit unit)
        {
            switch (unit)
            {
                case ReignGovernmentResolutionUnit.Gold: return "denars";
                case ReignGovernmentResolutionUnit.Goods: return "goods units";
                case ReignGovernmentResolutionUnit.Troops: return "troops";
                case ReignGovernmentResolutionUnit.Days: return "honored days";
                case ReignGovernmentResolutionUnit.Parties: return "parties defeated";
                case ReignGovernmentResolutionUnit.Engagements: return "engagements won";
                case ReignGovernmentResolutionUnit.Actions: return "verified actions";
                case ReignGovernmentResolutionUnit.Issues: return "issues completed";
                default: return unit.ToString().ToLowerInvariant();
            }
        }

        private void NormalizeBusinessPublicText(ReignGovernmentBusinessRecord record)
        {
            record.Title = GovernmentBusinessRules.PublicText(record.Title);
            record.Summary = record.Kind == "edict" ? PublicEdictDescription(record) : GovernmentBusinessRules.PublicText(record.Summary);
            record.Outcome = GovernmentBusinessRules.PublicText(record.Outcome);
            record.OptionsJson = RenderBusinessOptions(record, live: false).ToString(Formatting.None);
        }

        private ReignGovernmentResolutionRecord FindUniqueSeasonalResolution(string resolutionId)
        {
            if (string.IsNullOrWhiteSpace(resolutionId)) return null;
            // Direct decision entry points use the resolution ID, so an ambiguous ID cannot be admitted.
            var matches = _resolutions.Where(x => Same(x.ResolutionId, resolutionId)).Take(2).ToList();
            return matches.Count == 1 ? matches[0] : null;
        }

        private ReignGovernmentResolutionRecord FindSeasonalResolutionForBusiness(ReignGovernmentBusinessRecord record)
        {
            if (string.IsNullOrWhiteSpace(record.KingdomStringId)) return null;
            var resolution = FindUniqueSeasonalResolution(record.TargetId);
            return resolution != null && Same(resolution.KingdomStringId, record.KingdomStringId) ? resolution : null;
        }

        private ReignGovernmentBusinessRecord FindSeasonalBusinessForResolution(ReignGovernmentResolutionRecord resolution) =>
            _business.FirstOrDefault(x => x.Kind == "seasonal" && Same(x.TargetId, resolution.ResolutionId)
                && Same(x.KingdomStringId, resolution.KingdomStringId));

        private bool ValidateSeasonalBusiness(ReignGovernmentBusinessRecord record, out string result)
        {
            result = string.Empty;
            var resolution = FindSeasonalResolutionForBusiness(record);
            if (resolution != null && (resolution.Status == "debate" || resolution.Status == "proposed"))
            {
                var template = ReignGovernmentResolutionCatalog.Find(resolution.TemplateId);
                if (HasRecordedSeasonalPrisonerTarget(resolution, template)) return true;
                CloseInvalidBusiness(record, result = "The recorded prisoner is unavailable. This proposal is closed without imposing an obligation.");
                return false;
            }
            CloseInvalidBusiness(record, result = "This seasonal resolution is no longer awaiting a decision.");
            return false;
        }
        private bool ExecuteSeasonalBusiness(ReignGovernmentBusinessRecord record, string optionId, bool overridden, out string result)
        {
            result = string.Empty;
            var resolution = FindSeasonalResolutionForBusiness(record);
            if (resolution == null)
            {
                CloseInvalidBusiness(record, result = "This seasonal resolution is no longer awaiting a decision.");
                return false;
            }
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
                    var template = ReignGovernmentResolutionCatalog.Find(resolution.TemplateId);
                    var route = optionId == "route:0" ? template?.FirstRoute : optionId == "route:1" ? template?.SecondRoute : null;
                    if (route == null) { result = "The recorded remedy is unavailable."; return false; }
                    if (route.Action == ReignGovernmentResolutionAction.ChangeConstruction
                        && FindLiveConstructionProject(kingdom, resolution) == null)
                    {
                        result = ConstructionUnavailable + " The vote remains recorded. No payment or obligation was created.";
                        resolution.Status = "invalidated";
                        resolution.ResolvedDay = CurrentDay();
                        resolution.Outcome = result;
                        resolution.Revision++;
                        return false;
                    }
                    if (ResolutionRequiresDomesticSettlement(template.Id, route.Action)
                        && InvalidateResolutionWithoutOwnedSettlement(kingdom, resolution))
                    {
                        result = "The recorded vote was not implemented because the named settlement is unavailable "
                            + "or no longer belongs to this realm. No payment or obligation was created. "
                            + "The vote remains recorded. A fresh proposal may be introduced at a regular session.";
                        resolution.Outcome = result;
                        CloseInvalidBusiness(record, result);
                        return false;
                    }
                    // This refusal occurs before AcceptResolution changes the obligation,
                    // transfers gold, or applies any fulfillment consequences.
                    if (kingdom?.Leader != null && UsesImmediateGold(route.Action)
                        && kingdom.Leader.Gold < route.Target)
                    {
                        result = "The recorded vote was not implemented because the ruler could not fund the required "
                            + route.Target + " denars. No payment or obligation was created. The motion is closed; a fresh proposal may be introduced at a regular session.";
                        resolution.Status = "cancelled";
                        resolution.ResolvedDay = CurrentDay();
                        resolution.Outcome = result;
                        resolution.Revision++;
                        CloseInvalidBusiness(record, result);
                        return false;
                    }
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
            if (HasRecordedAuthorizationRefusal(record))
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
                || !HasRecordedAuthorizationRefusal(record)
                || !string.Equals(record.ActionPayloadJson, GovernmentActionIdentity(action), StringComparison.Ordinal))
                return false;
            result = RecordedAuthorizationRefusal(record);
            return true;
        }

        private bool HasRecordedAuthorizationRefusal(ReignGovernmentBusinessRecord record) =>
            GovernmentBusinessRules.IsRecordedAuthorizationRefusal(record.Status, record.ExecutedOptionId)
            || ReignGovernmentNativeSafety.IsRecordedCounterpartRefusal(record,
                _business.FirstOrDefault(x => x.BusinessId == record.CounterpartBusinessId));

        private static ReignActionResult RecordedAuthorizationRefusal(ReignGovernmentBusinessRecord record) =>
            ReignActionResult.Rejected("The government process did not authorize this action.",
                "government_authorization_refused", "government_authority")
                .WithDiagnostic("governmentBusinessId", record.BusinessId)
                .WithDiagnostic("governmentStatus", record.Status)
                .WithDiagnostic("governmentExecutedOptionId", "reject");
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
            var queue = ReignServices.AI;
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
