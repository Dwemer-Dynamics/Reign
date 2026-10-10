using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Reign.Core.Contracts.Actions;
using Reign.Core.Contracts.Government;
using ReignBeta.Actions;
using TaleWorlds.CampaignSystem;

namespace ReignBeta.Government
{
    public sealed partial class ReignGovernmentCampaignBehavior
    {
        private Kingdom AgreementRealm(ActionClause c)
        {
            string id = ReignActionService.Arg(c, "kingdomId");
            var business = _business.FirstOrDefault(b => b.BusinessId == ReignActionService.Arg(c, "businessId"));
            var resolution = _resolutions.FirstOrDefault(r => r.ResolutionId == ReignActionService.Arg(c, "resolutionId"));
            return FindKingdom(business?.KingdomStringId ?? resolution?.KingdomStringId ?? id);
        }

        internal JObject CaptureGovernmentAgreement(ActionClause c)
        {
            var realm = AgreementRealm(c);
            return new JObject { ["kingdomId"] = realm?.StringId ?? "", ["rulerId"] = realm?.Leader?.StringId ?? "",
                ["level"] = GetGovernment(realm)?.Level ?? -1,
                ["proposalState"] = c.ActionId == "government.introduce_proposal" ? ProposalState(c) : "",
                ["contributionBefore"] = _resolutions.FirstOrDefault(r => r.ResolutionId == ReignActionService.Arg(c, "resolutionId"))?.CurrentValue ?? 0 };
        }

        private string ProposalState(ActionClause c) => GetGovernmentProposalChoices(AgreementRealm(c)?.StringId)
            .OfType<JObject>().FirstOrDefault(p => p.Value<string>("proposalId") == ReignActionService.Arg(c, "proposalId"))?.ToString(Formatting.None) ?? "";

        private List<string> CheckGovernmentCommand(ActionClause c)
        {
            var errors = new List<string>();
            var actor = ReignActionService.HeroById(c.ActorId);
            var realm = AgreementRealm(c);
            var state = GetGovernment(realm);
            if (c.ActionId == "government.contribute_goods")
            {
                string error = CheckGoodsContribution(ReignActionService.Arg(c, "resolutionId"), int.Parse(ReignActionService.Arg(c, "quantity")), actor, true);
                if (error.Length > 0) errors.Add(error);
                return errors;
            }
            if (actor == null || realm == null || realm.Leader != actor || state == null)
            { errors.Add("Only this realm's current ruler may commit its government to this action."); return errors; }
            string id = c.ActionId.Substring("government.".Length);
            if (id == "introduce_proposal")
            {
                string proposal = ProposalState(c);
                if (proposal.Length == 0 || JObject.Parse(proposal).Value<bool?>("available") != true)
                    errors.Add("That exact government proposal is no longer available.");
                return errors;
            }
            if (id == "increase_authority" || id == "reduce_authority")
            {
                if (id == "increase_authority" && state.Level >= ReignGovernmentRules.MaximumLevel) errors.Add("The government already holds maximum authority.");
                if (id == "reduce_authority" && state.Level <= ReignGovernmentRules.MinimumLevel) errors.Add("The government already holds minimum authority.");
                return errors;
            }
            var resolution = _resolutions.FirstOrDefault(r => r.ResolutionId == ReignActionService.Arg(c, "resolutionId"));
            var business = _business.FirstOrDefault(b => b.BusinessId == ReignActionService.Arg(c, "businessId"));
            if (id == "fulfill_resolution" || id == "decline_resolution")
            {
                business = resolution == null ? null : _business.FirstOrDefault(b => b.Kind == "seasonal" && b.TargetId == resolution.ResolutionId);
                if (resolution == null || !GovernmentBusinessRules.CanDecideResolution(resolution.Status, business?.Status, business != null))
                { errors.Add("Select a resolution awaiting the ruler's decision."); return errors; }
                var template = ReignGovernmentResolutionCatalog.Find(resolution.TemplateId);
                if (template == null) { errors.Add("The resolution definition is unavailable."); return errors; }
                if (id == "fulfill_resolution")
                {
                    string routeId = ReignActionService.Arg(c, "routeId");
                    if (routeId != "0" && routeId != "1") { errors.Add("Choose one of this resolution's exact routes."); return errors; }
                    var route = routeId == "0" ? template.FirstRoute : template.SecondRoute;
                    if (UsesImmediateGold(route.Action) && actor.Gold < route.Target) errors.Add("The ruler cannot afford the resolution's treasury commitment.");
                }
                if (business == null) return errors;
            }
            if (business == null || GovernmentBusinessRules.IsClosed(business.Status)) { errors.Add("This hearing is no longer available."); return errors; }
            string option = id == "decline_resolution" ? "reject" : id == "fulfill_resolution" ? "route:" + ReignActionService.Arg(c, "routeId") : ReignActionService.Arg(c, "optionId");
            if (id == "recommend" && !GovernmentBusinessRules.CanRecommend(business.Status)) errors.Add("This hearing cannot take a recommendation now.");
            if (id == "adopt_petition" && business.Status != "awaiting_sponsorship") errors.Add("This petition is not awaiting sponsorship.");
            if (id == "postpone" && !GovernmentBusinessRules.CanPostpone(business.Status, business.IsUrgent, business.Postponed)) errors.Add("This hearing cannot take another recess.");
            if (id == "reconvene" && business.Status != "postponed") errors.Add("This hearing is not postponed.");
            if (id == "call_vote" && !GovernmentBusinessRules.CanVote(business.Status)) errors.Add("This hearing is not ready for voting.");
            if (id == "resolve" || id == "fulfill_resolution" || id == "decline_resolution")
            {
                if (state.Level == 5) errors.Add("The binding government vote decides this outcome.");
                if (business.Status != "awaiting_ruler" && business.Status != "reconsideration") errors.Add("The government vote must be recorded first.");
                bool opposed = !Same(option, business.WinningOptionId);
                bool overrideAccepted = ReignActionService.Arg(c, "overrideOpposition") == "true";
                if (opposed && state.Level >= 3 && !overrideAccepted) errors.Add("This opposed outcome needs an explicit override.");
                if (opposed && state.Level == 3 && (business.Status != "reconsideration" || CurrentDay() < business.ReconsiderUntilDay)) errors.Add("The required reconsideration must finish first.");
            }
            if (option.Length > 0 && !BusinessOptions(business).Any(o => o.Value<string>("id") == option)) errors.Add("The selected outcome is not an option for this hearing.");
            return errors;
        }

        private void ApplyGovernmentCommand(ActionClause c, AgreementStep step)
        {
            var errors = CheckGovernmentCommand(c);
            if (errors.Count > 0) throw new InvalidOperationException(string.Join(" ", errors));
            var before = JObject.Parse(step.Snapshot);
            var realm = AgreementRealm(c);
            if (before.Value<string>("kingdomId") != realm?.StringId || before.Value<string>("rulerId") != realm?.Leader?.StringId)
                throw new InvalidOperationException("The ruler or government has changed since this agreement was accepted.");
            if ((c.ActionId == "government.increase_authority" || c.ActionId == "government.reduce_authority")
                && before.Value<int>("level") != GetGovernment(realm)?.Level)
                throw new InvalidOperationException("Government authority changed before the agreed one-level adjustment could occur.");
            if (c.ActionId == "government.introduce_proposal" && before.Value<string>("proposalState") != ProposalState(c))
                throw new InvalidOperationException("The proposal changed after this agreement; review its current terms.");
            string businessId = ReignActionService.Arg(c, "businessId"), option = ReignActionService.Arg(c, "optionId");
            string resolutionId = ReignActionService.Arg(c, "resolutionId"), message;
            var actor = ReignActionService.HeroById(c.ActorId);
            if (c.ActionId == "government.contribute_goods")
                before["contributionBefore"] = _resolutions.First(r => r.ResolutionId == resolutionId).CurrentValue;
            bool succeeded;
            switch (c.ActionId)
            {
                case "government.introduce_proposal": succeeded = CreateGovernmentBusiness(realm.StringId, ReignActionService.Arg(c, "proposalId"), actor, out businessId, out message); break;
                case "government.contribute_goods": succeeded = ContributeGoods(resolutionId, int.Parse(ReignActionService.Arg(c, "quantity")), actor, true, out message); break;
                case "government.recommend": succeeded = RecommendBusiness(businessId, option, actor, out message); break;
                case "government.adopt_petition": succeeded = AdoptBusinessPetition(businessId, actor, out message); break;
                case "government.postpone": succeeded = PostponeBusiness(businessId, actor, out message); break;
                case "government.reconvene": succeeded = ReconveneBusiness(businessId, actor, out message); break;
                case "government.call_vote": succeeded = VoteBusiness(businessId, actor, out message); break;
                case "government.resolve": succeeded = ResolveBusiness(businessId, option, actor, ReignActionService.Arg(c, "overrideOpposition") == "true", out message); break;
                case "government.increase_authority": succeeded = IncreaseGovernmentLevel(realm, out message); break;
                case "government.reduce_authority": succeeded = ReduceGovernmentLevel(realm, ReignActionService.Arg(c, "force") == "true", out message); break;
                case "government.fulfill_resolution": succeeded = AcceptResolution(resolutionId, int.Parse(ReignActionService.Arg(c, "routeId")), actor, out message); break;
                case "government.decline_resolution": succeeded = DeclineResolution(resolutionId, actor, out message); break;
                default: throw new InvalidOperationException("No government adapter is registered for this action.");
            }
            if (!succeeded) throw new InvalidOperationException(message);
            // Save the real service result with the agreement step. This is a receipt,
            // not consent, and cannot be supplied as an editable sentence parameter.
            before["applied"] = true; before["message"] = message;
            before["afterLevel"] = GetGovernment(realm)?.Level ?? -1;
            var business = _business.FirstOrDefault(b => b.BusinessId == businessId);
            before["businessStatus"] = business?.Status ?? "";
            before["businessRevision"] = business?.Revision ?? 0;
            before["recommendedOption"] = business?.RecommendedOptionId ?? "";
            before["ballots"] = business?.VotesJson ?? "[]";
            before["businessId"] = businessId;
            before["contributionAfter"] = _resolutions.FirstOrDefault(r => r.ResolutionId == resolutionId)?.CurrentValue ?? 0;
            step.Snapshot = before.ToString(Formatting.None);
        }

        private bool ObserveGovernmentCommand(ActionClause c, AgreementStep step)
        {
            var receipt = JObject.Parse(step.Snapshot);
            if (receipt.Value<bool?>("applied") != true) return false;
            if (c.ActionId == "government.introduce_proposal"
                && !_business.Any(b => b.BusinessId == receipt.Value<string>("businessId") && b.KingdomStringId == receipt.Value<string>("kingdomId")))
                throw new InvalidOperationException("The promised government hearing was not retained.");
            if (c.ActionId == "government.contribute_goods" && receipt.Value<double>("contributionAfter") - receipt.Value<double>("contributionBefore") != int.Parse(ReignActionService.Arg(c, "quantity")))
                throw new AgreementRecoveryException("The recorded contribution differs from the exact promised quantity.", receipt.ToString(Formatting.None), null);
            if (c.ActionId == "government.fulfill_resolution")
            {
                var resolution = _resolutions.FirstOrDefault(r => r.ResolutionId == ReignActionService.Arg(c, "resolutionId"));
                if (resolution == null || resolution.Status == "failed" || resolution.Status == "invalidated" || resolution.Status == "refused")
                    throw new InvalidOperationException("The promised resolution could not be fulfilled.");
                return resolution.Status == "completed";
            }
            if (c.ActionId == "government.increase_authority" || c.ActionId == "government.reduce_authority")
            {
                int difference = receipt.Value<int>("afterLevel") - receipt.Value<int>("level");
                if (difference != (c.ActionId == "government.increase_authority" ? 1 : -1))
                    throw new InvalidOperationException("The government authority change did not match the agreed one-level change.");
            }
            if (c.ActionId == "government.recommend" && receipt.Value<string>("recommendedOption") != ReignActionService.Arg(c, "optionId"))
                throw new InvalidOperationException("The recorded recommendation differs from the agreed outcome.");
            if (c.ActionId == "government.call_vote" && JArray.Parse(receipt.Value<string>("ballots") ?? "[]").Count == 0)
                throw new InvalidOperationException("The vote did not produce any recorded ballots.");
            return true;
        }
    }
}
