using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Reign.Core.Contracts.Government;
using ReignBeta.Government.Domain;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.Settlements.Buildings;

namespace ReignBeta.Government.Campaign
{
    public sealed partial class ReignGovernmentCampaignBehavior
    {
        private const string ConstructionTemplateId = "R083";
        private const int ConstructionEvidenceVersion = 1;
        private const string ConstructionUnavailable = "The recorded construction project is no longer available for this resolution.";

        private static bool IsNamedConstruction(ReignGovernmentResolutionRecord record) =>
            record != null && Same(record.TemplateId, ConstructionTemplateId);

        private static bool HasActiveConstructionScope(ReignGovernmentResolutionRecord record, string settlementId) =>
            IsNamedConstruction(record) && record.Status == "active"
                && record.RouteActionValue == (int)ReignGovernmentResolutionAction.ChangeConstruction
                && record.TargetSettlementStringId == settlementId;

        private static JObject ReadResolutionEvidence(ReignGovernmentResolutionRecord record)
        {
            try
            {
                return JObject.Parse(record?.EvidenceJson ?? "{}",
                    new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
            }
            catch (JsonException) { return new JObject(); }
        }

        private static JObject ReadConstructionOffer(ReignGovernmentResolutionRecord record, JObject evidence)
        {
            if (!IsNamedConstruction(record) || !(evidence["construction"] is JObject offer)
                || !JToken.DeepEquals(offer["schemaVersion"], new JValue(ConstructionEvidenceVersion))
                || ReadConstructionString(offer, "settlementId") != record.TargetSettlementStringId
                || string.IsNullOrWhiteSpace(ReadConstructionString(offer, "projectId")))
                return null;
            return offer;
        }

        private static string ReadConstructionString(JObject value, string key) =>
            value?[key]?.Type == JTokenType.String ? (string)value[key] : string.Empty;

        private static bool ConstructionAwaitingDecision(ReignGovernmentResolutionRecord record) =>
            record != null && (record.Status == "debate" || record.Status == "proposed");

        private static bool HasExactConstructionRoute(ReignGovernmentBusinessRecord business, string optionId)
        {
            if (business == null) return true; // Proposal before a business record has been imported.
            var matches = BusinessOptions(business).Where(x => Same(x.Value<string>("id"), optionId)).Take(2).ToList();
            return matches.Count == 1 && matches[0].Value<string>("id") == optionId;
        }

        private Building FindLiveConstructionProject(Kingdom realm, ReignGovernmentResolutionRecord record)
        {
            var offer = ReadConstructionOffer(record, ReadResolutionEvidence(record));
            if (offer == null || !ConstructionAwaitingDecision(record)
                || !ReferenceEquals(FindUniqueSeasonalResolution(record.ResolutionId), record)
                || !HasExactConstructionRoute(FindSeasonalBusinessForResolution(record), "route:0")
                || !ReignGovernmentConstructionNativeObservation.VerifyReady()
                || !ReignGovernmentConstructionNativeObservation.TryCaptureOwner(out var owner)
                || !ReferenceEquals(owner.Behavior, this)
                || _resolutions.Any(x => HasActiveConstructionScope(x, record.TargetSettlementStringId)
                    && x.KingdomStringId == realm?.StringId))
                return null;
            Town town = FindOwnedResolutionSettlement(realm, record)?.Town;
            return ReignGovernmentConstructionNativeObservation.AvailableProject(town, realm,
                record.TargetSettlementStringId, ReadConstructionString(offer, "projectId"), true);
        }

        private bool CanOfferConstructionTreasury(Kingdom realm, ReignGovernmentResolutionRecord record)
        {
            if (!ConstructionAwaitingDecision(record) || !ReferenceEquals(FindUniqueSeasonalResolution(record.ResolutionId), record)
                || !HasExactConstructionRoute(FindSeasonalBusinessForResolution(record), "route:1"))
                return false;
            var route = ReignGovernmentResolutionCatalog.Find(record.TemplateId)?.SecondRoute;
            return route != null && UsesImmediateGold(route.Action) && realm?.Leader != null && realm.Leader.Gold >= route.Target
                && (!ResolutionRequiresDomesticSettlement(record.TemplateId, route.Action)
                    || FindOwnedResolutionSettlement(realm, record) != null);
        }

        internal static string ConstructionProjectLabel(ReignGovernmentResolutionRecord record, Building project = null)
        {
            var offer = ReadConstructionOffer(record, ReadResolutionEvidence(record));
            if (offer == null) return string.Empty;
            string name = project?.Name?.ToString();
            if (string.IsNullOrWhiteSpace(name)) name = ReadConstructionString(offer, "projectName");
            return GovernmentBusinessRules.PublicText(string.IsNullOrWhiteSpace(name)
                ? ReadConstructionString(offer, "projectId") : name);
        }

        private static Building ChooseUsefulConstruction(Town town, Kingdom realm)
        {
            var current = town?.CurrentBuilding;
            if (current?.BuildingType == null || town.Buildings == null) return null;
            var effects = new List<BuildingEffectEnum>();
            if (town.FoodChange < 0f || town.FoodStocks < 20f)
            {
                effects.Add(BuildingEffectEnum.FoodProduction);
                effects.Add(BuildingEffectEnum.FoodStock);
            }
            if (town.Loyalty < 40f) effects.Add(BuildingEffectEnum.Loyalty);
            if (town.Security < 40f) effects.Add(BuildingEffectEnum.SecurityPerDay);
            if (town.Prosperity < 3000f) effects.Add(BuildingEffectEnum.Prosperity);
            foreach (BuildingEffectEnum effect in effects)
            {
                if (current.CurrentLevel >= BuildingType.MaxLevel || NextConstructionGain(current, effect) > 0f) continue;
                var match = town.Buildings.Select(x => x?.BuildingType?.StringId)
                    .Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal)
                    .Select(id => ReignGovernmentConstructionNativeObservation.AvailableProject(
                        town, realm, town.Settlement?.StringId, id, true))
                    .Where(building => building != null)
                    .Select(building => (Project: building, Gain: NextConstructionGain(building, effect)))
                    .Where(candidate => candidate.Gain > 0f && !float.IsInfinity(candidate.Gain))
                    .OrderByDescending(candidate => candidate.Gain)
                    .ThenBy(candidate => candidate.Project.BuildingType.StringId, StringComparer.Ordinal)
                    .Select(candidate => candidate.Project).FirstOrDefault();
                if (match != null) return match;
            }
            return null;
        }

        private static float NextConstructionGain(Building building, BuildingEffectEnum effect)
        {
            BuildingType type = building.BuildingType;
            int level = building.CurrentLevel;
            if (level < 0 || level >= BuildingType.MaxLevel || !type.HasEffect(effect)) return 0f;
            float before = level == 0 ? 0f : type.GetBaseBuildingEffectAmount(effect, level);
            return type.GetBaseBuildingEffectAmount(effect, level + 1) - before;
        }

        private bool TryAcceptConstruction(ReignGovernmentResolutionRecord record, Kingdom realm,
            ReignGovernmentResolutionRoute route, out ReignGovernmentConstructionTerms terms)
        {
            terms = null;
            var project = FindLiveConstructionProject(realm, record);
            if (project == null || !ReignGovernmentConstructionNativeObservation.TryCaptureOwner(out var owner)
                || !ReferenceEquals(owner.Behavior, this) || !owner.IsCurrent()) return false;
            var queue = project.Town.BuildingsInProgress;
            string headId = queue?.Count > 0 ? queue.Peek()?.BuildingType?.StringId : null;
            double acceptedDay = CampaignTime.Now.ToDays;
            var candidate = new ReignGovernmentConstructionTerms(record.ResolutionId, owner.CampaignId, owner.TimelineId,
                realm.StringId, record.TargetSettlementStringId, headId, project.BuildingType.StringId,
                project.CurrentLevel, BuildingType.MaxLevel, acceptedDay, acceptedDay + route.DeadlineDays);
            if (!candidate.IsValid) return false;
            var evidence = ReadResolutionEvidence(record);
            var offer = ReadConstructionOffer(record, evidence);
            // An older accepted obligation cannot be rebound to new terms.
            if (offer == null || offer["acceptedTerms"] != null || offer["observation"] != null) return false;
            offer["acceptedTerms"] = candidate.ToJson();
            record.EvidenceJson = evidence.ToString(Formatting.None);
            terms = candidate;
            return true;
        }

        internal static ReignGovernmentConstructionTerms GetAcceptedConstructionTerms(ReignGovernmentResolutionRecord record) =>
            ReadConstructionTerms(record, ReadConstructionOffer(record, ReadResolutionEvidence(record)));

        private static ReignGovernmentConstructionTerms ReadConstructionTerms(ReignGovernmentResolutionRecord record, JObject offer)
        {
            if (!(offer?["acceptedTerms"] is JObject row) || row["levelAtAcceptance"]?.Type != JTokenType.Integer
                || row["levelCapAtAcceptance"]?.Type != JTokenType.Integer
                || row["acceptedDay"]?.Type != JTokenType.Float || row["dueDay"]?.Type != JTokenType.Float)
                return null;
            try
            {
                var terms = new ReignGovernmentConstructionTerms(ReadConstructionString(row, "resolutionId"),
                    ReadConstructionString(row, "campaignId"), ReadConstructionString(row, "timelineId"),
                    ReadConstructionString(row, "kingdomId"), ReadConstructionString(row, "settlementId"),
                    ReadConstructionString(row, "previousProjectId"), ReadConstructionString(row, "projectId"),
                    (int)row["levelAtAcceptance"], (int)row["levelCapAtAcceptance"],
                    (double)row["acceptedDay"], (double)row["dueDay"]);
                if (!terms.IsValid || terms.ResolutionId != record.ResolutionId || terms.KingdomId != record.KingdomStringId
                    || terms.SettlementId != record.TargetSettlementStringId
                    || terms.ProjectId != ReadConstructionString(offer, "projectId")
                    || record.SelectedRoute != 0
                    || (float)terms.AcceptedDay != record.AcceptedDay || (float)terms.DueDay != record.DueDay
                    || !JToken.DeepEquals(row, terms.ToJson()))
                    return null;
                return terms;
            }
            catch (Exception ex) when (ex is OverflowException || ex is FormatException) { return null; }
        }

        partial void FindActiveConstructionTerms(Town town, Kingdom realm, string campaignId, string timelineId,
            ref ReignGovernmentConstructionTerms terms)
        {
            if (string.IsNullOrWhiteSpace(town?.Settlement?.StringId)
                || realm == null || !ReferenceEquals(town.Settlement.MapFaction, realm)) return;
            var matches = _resolutions.Where(x => HasActiveConstructionScope(x, town.Settlement.StringId)
                && x.KingdomStringId == realm.StringId).Take(2).ToList();
            if (matches.Count != 1) return;
            var record = matches[0];
            if (!ReferenceEquals(record, FindUniqueSeasonalResolution(record.ResolutionId))) return;
            var offer = ReadConstructionOffer(record, ReadResolutionEvidence(record));
            var active = ReadConstructionTerms(record, offer);
            if (active != null && active.CampaignId == campaignId && active.TimelineId == timelineId
                && offer["observation"] == null)
                terms = active;
        }

        partial void StoreConstructionObservation(ReignGovernmentConstructionObservation observation)
        {
            if (observation == null || !ReignGovernmentConstructionNativeObservation.TryCaptureOwner(out var owner)
                || !ReferenceEquals(owner.Behavior, this) || !owner.IsCurrent()) return;
            var record = FindUniqueSeasonalResolution(observation.Terms.ResolutionId);
            if (record == null) return;
            Kingdom realm = FindKingdom(record.KingdomStringId);
            Town town = FindOwnedResolutionSettlement(realm, record)?.Town;
            ReignGovernmentConstructionTerms active = null;
            FindActiveConstructionTerms(town, realm, owner.CampaignId, owner.TimelineId, ref active);
            if (active?.Matches(observation.Terms) != true || observation.ObservedDay > CampaignTime.Now.ToDays) return;
            var evidence = ReadResolutionEvidence(record);
            var offer = ReadConstructionOffer(record, evidence);
            offer["observation"] = observation.ToJson();
            record.EvidenceJson = evidence.ToString(Formatting.None);
            record.Revision++;
        }

        private void EvaluateNamedConstruction(Kingdom realm, ReignGovernmentStateRecord state,
            ReignGovernmentResolutionRecord record, ReignGovernmentResolutionTemplate template)
        {
            var offer = ReadConstructionOffer(record, ReadResolutionEvidence(record));
            var terms = ReadConstructionTerms(record, offer);
            if (terms == null)
            {
                InvalidateConstruction(record, "The named construction terms could not be verified.");
                return;
            }
            if (!ReignGovernmentConstructionNativeObservation.TryCaptureOwner(out var owner)
                || !ReferenceEquals(owner.Behavior, this) || !owner.IsCurrent()) return;
            if (terms.CampaignId != owner.CampaignId || terms.TimelineId != owner.TimelineId)
            {
                InvalidateConstruction(record, "The named construction terms do not belong to this campaign timeline.");
                return;
            }

            var observation = ReignGovernmentConstructionObservation.Read(offer["observation"], terms);
            double now = CampaignTime.Now.ToDays;
            if (observation != null && observation.ObservedDay <= now)
            {
                record.CurrentValue = Math.Max(record.CurrentValue, record.RequiredAmount);
                CompleteResolution(realm, record, template, "Native construction verified the named project " + terms.ProjectId + ".");
                return;
            }
            if (offer["observation"] != null || !ReignGovernmentConstructionNativeObservation.VerifyReady())
            {
                InvalidateConstruction(record, "Native construction evidence could not be verified.");
                return;
            }
            if (double.IsNaN(now) || double.IsInfinity(now) || now <= terms.DueDay) return;
            record.Status = "failed";
            record.Outcome = "The named construction project was not verified before its deadline.";
            record.ResolvedDay = CurrentDay();
            record.Revision++;
            ApplyResolutionFailure(realm, state, record, template, false);
        }

        private static void InvalidateConstruction(ReignGovernmentResolutionRecord record, string reason)
        {
            record.Status = "invalidated";
            record.Outcome = reason + " This obligation is closed without a failure penalty.";
            record.ResolvedDay = CurrentDay();
            record.Revision++;
        }

        private void InvalidateConstructionAfterSettlementTransfer(Settlement settlement, Kingdom newRealm)
        {
            if (settlement == null) return;
            foreach (var record in _resolutions.Where(x => HasActiveConstructionScope(x, settlement.StringId)
                && x.KingdomStringId != newRealm?.StringId))
                InvalidateConstruction(record, "The recorded settlement changed realms.");
        }
    }
}
