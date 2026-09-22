using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Reign.Core.Contracts.Government;
using ReignBeta.World;
using TaleWorlds.CampaignSystem;

namespace ReignBeta.Government
{
    public sealed partial class ReignGovernmentCampaignBehavior
    {
        private bool TryAuthorizeWorldCounterpart(ReignGovernmentBusinessRecord record, out string result)
        {
            result = string.Empty;
            if (!string.IsNullOrEmpty(record.CounterpartOfBusinessId))
            {
                var source = _business.FirstOrDefault(x => Same(x.BusinessId, record.CounterpartOfBusinessId));
                if (source == null || source.Status != "awaiting_counterpart")
                { CloseInvalidBusiness(record, result = "The originating proposal is no longer awaiting acceptance."); return true; }
                source.Status = record.Status = "authorized";
                source.Outcome = record.Outcome = result = "Both governments approved the exact proposal. Its original execution checks remain required.";
                NotifyGovernmentBusinessClosed(record); ChangedBusiness(source); ChangedBusiness(record); return true;
            }
            JObject identity = JObject.Parse(record.ActionPayloadJson);
            string type = ((ReignWorldActionType)identity.Value<int>("type")).ToString();
            if (!GovernmentBusinessRules.RequiresCounterpartWorldApproval(type)) return false;
            Kingdom counterpart = FindKingdom(identity.Value<string>("targetKingdom"));
            if (counterpart == null || Same(counterpart.StringId, record.KingdomStringId) || EnsureGovernment(counterpart) == null)
            { CloseInvalidBusiness(record, result = "The counterpart government is unavailable; the proposal cannot be authorized."); return true; }
            var response = new ReignGovernmentBusinessRecord
            {
                KingdomStringId = counterpart.StringId, Kind = "world_action", ActionKindValue = record.ActionKindValue,
                Title = record.Title, Summary = "Proposal from " + FindKingdom(record.KingdomStringId)?.Name + ".\n" + record.Summary,
                TargetId = record.KingdomStringId, ActionCorrelationId = record.ActionCorrelationId,
                ActionPayloadJson = record.ActionPayloadJson, CounterpartOfBusinessId = record.BusinessId,
                PetitionerHeroStringId = record.DecisionRulerHeroStringId,
                SponsorReason = "An incoming state proposal requires no domestic sponsor.",
                OptionsJson = record.OptionsJson, RequestedOptionId = "accept", StatusQuoOptionId = "reject",
                CreatedDay = CurrentDay(), IsUrgent = record.IsUrgent
            };
            _business.Add(response); record.CounterpartBusinessId = response.BusinessId;
            record.Status = "awaiting_counterpart";
            record.Outcome = result = "This government approved the proposal. The counterpart government must now decide on these same terms.";
            NotifyGovernmentBusinessClosed(record); ChangedBusiness(record); ChangedBusiness(response); return true;
        }

        private static string GovernmentActionTitle(ReignWorldActionRecord action)
        {
            string name = Regex.Replace(action.Type.ToString(), "^(Diplomacy|Politics|Regular)", string.Empty);
            return Regex.Replace(name, "([a-z])([A-Z])", "$1 $2");
        }

        private static string GovernmentActionSummary(ReignWorldActionRecord action)
        {
            var lines = new List<string>();
            var actor = ResolveActorKingdom(action);
            var target = ReignObjectResolver.FindKingdom(action.TargetKingdomStringId);
            if (actor != null) lines.Add("Proposing realm: " + actor.Name + ".");
            if (target != null) lines.Add("Other realm: " + target.Name + ".");
            if (!string.IsNullOrWhiteSpace(action.Reason)) lines.Add(GovernmentBusinessRules.PublicText(action.Reason));
            JObject terms;
            try { terms = JObject.Parse(string.IsNullOrWhiteSpace(action.TermsJson) ? "{}" : action.TermsJson); }
            catch (JsonException) { return string.Join("\n", lines) + "\nThe proposal's terms are invalid; execution cannot proceed."; }
            // Only the actual transaction terms are public. Never expose arbitrary provider metadata or relationships.
            foreach (string key in new[] { "gold", "GoldAmount", "indemnityGold", "loanGold", "reparationsGold", "ransomGold",
                "dailyTribute", "reparationsDailyTribute", "durationDays", "amount", "Amount", "shipCount", "maxPrisoners", "maxEachSide",
                "allTargetFortifications", "militaryCommitment", "warSupport", "guaranteeIndependence", "allMatchingAssets" })
                if (terms[key] is JValue value && (value.Type == JTokenType.Integer || value.Type == JTokenType.Float || value.Type == JTokenType.Boolean))
                    lines.Add(Regex.Replace(key, "([a-z])([A-Z])", "$1 $2") + ": " + value.ToString() + ".");
            foreach (string key in new[] { "agreementKind", "kind", "treatyKind", "assetClass", "asset", "assetName", "item", "Item", "packageText" })
                if (terms[key]?.Type == JTokenType.String && !string.IsNullOrWhiteSpace(terms.Value<string>(key)))
                    lines.Add(Regex.Replace(key, "([a-z])([A-Z])", "$1 $2") + ": " + GovernmentBusinessRules.PublicText(terms.Value<string>(key)) + ".");
            foreach (string key in new[] { "enemyKingdomId", "thirdKingdomId", "settlementFromKingdomId", "settlementToKingdomId" })
            {
                var realm = ReignObjectResolver.FindKingdom(terms.Value<string>(key));
                if (realm != null) lines.Add(Regex.Replace(key.Replace("Id", ""), "([a-z])([A-Z])", "$1 $2") + ": " + realm.Name + ".");
            }
            foreach (string id in (terms["settlementIds"] as JArray ?? new JArray()).Values<string>().Concat(new[] { action.TargetSettlementStringId }).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct())
            {
                var settlement = ReignObjectResolver.FindSettlement(id);
                if (settlement != null) lines.Add("Settlement: " + settlement.Name + ".");
            }
            foreach (string key in new[] { "prisonerHeroStringId", "hostageHeroStringId", "assetFromHeroStringId", "assetToHeroStringId" })
            {
                var hero = ReignObjectResolver.FindHero(terms.Value<string>(key));
                if (hero != null) lines.Add(Regex.Replace(key.Replace("HeroStringId", ""), "([a-z])([A-Z])", "$1 $2") + ": " + hero.Name + ".");
            }
            return string.Join("\n", lines);
        }
    }
}
