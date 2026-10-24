using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Reign.Core.Contracts.Actions;
using ReignBeta.Campaign;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;

namespace ReignBeta.Actions
{
    internal static class ReignSpouseSeparationAction
    {
        internal static bool Available(Hero actor) => actor != null && actor != Hero.MainHero
            && actor.IsAlive && !actor.IsChild && !actor.IsPrisoner && actor.Spouse != null
            && actor.Spouse.IsAlive && !actor.Spouse.IsChild && actor.Spouse.Spouse == actor
            && ReignWorldHistoryCampaignBehavior.Instance?.TimelineReady == true;

        internal static void Bind(ActionAgreement agreement)
        {
            foreach (var clause in agreement.Clauses.Where(c => c.ActionId == ReignSpouseSeparation.ActionId))
            {
                var actor = ReignActionService.HeroById(clause.ActorId);
                ReignSpouseSeparation.Bind(agreement, clause, actor?.Spouse?.StringId ?? "", actor?.Spouse?.Clan?.StringId ?? "");
            }
        }

        internal static List<string> Check(ActionAgreement agreement, ActionClause clause)
        {
            var errors = ReignSpouseSeparation.Validate(agreement, clause).ToList();
            var actor = ReignActionService.HeroById(clause.ActorId);
            if (!Available(actor)) errors.Add("A living adult NPC in an existing reciprocal marriage must be available to send the letter.");
            else if (actor.Spouse.StringId != ReignActionService.Arg(clause, "spouseId")
                || (actor.Spouse.Clan?.StringId ?? "") != ReignActionService.Arg(clause, "spouseClanId"))
                errors.Add("The marriage or spouse's clan changed. Build a new request.");
            return errors;
        }

        internal static string Capture(ActionAgreement agreement, ActionClause clause)
        {
            var errors = Check(agreement, clause);
            if (errors.Count > 0) throw new InvalidOperationException(string.Join(" ", errors));
            var actor = ReignActionService.HeroById(clause.ActorId);
            var spouse = actor.Spouse;
            var affected = ReignSpouseSeparation.AffectedHeroes(actor.StringId, spouse.StringId,
                spouse.Clan?.Heroes.Where(h => h.IsAlive && !h.IsChild).Select(h => h.StringId) ?? Enumerable.Empty<string>());
            return new JObject {
                ["source"] = ReignSpouseSeparation.Source, ["actorId"] = actor.StringId,
                ["actorName"] = actor.Name.ToString(), ["spouseId"] = spouse.StringId,
                ["spouseName"] = spouse.Name.ToString(), ["spouseClanId"] = spouse.Clan?.StringId ?? "",
                ["letterBody"] = ReignSpouseSeparation.Letter(spouse.Name.ToString()),
                ["relations"] = new JArray(affected.Select(id => {
                    var target = ReignActionService.HeroById(id);
                    int before = actor.GetRelation(target);
                    return new JObject { ["heroId"] = id, ["before"] = before, ["after"] = ReignSpouseSeparation.RelationAfter(before) };
                }))
            }.ToString(Formatting.None);
        }

        internal static void Apply(ActionAgreement agreement, ActionClause clause, string snapshot, string key)
        {
            var behavior = ReignAgreementBehavior.Instance;
            if (behavior.NativeOperation(key) != null) return;
            var errors = Check(agreement, clause);
            if (errors.Count > 0) throw new InvalidOperationException(string.Join(" ", errors));
            var data = JObject.Parse(snapshot);
            var actor = ReignActionService.HeroById(clause.ActorId);
            var spouse = actor.Spouse;
            if (data.Value<string>("actorId") != actor.StringId || data.Value<string>("spouseId") != spouse.StringId)
                throw new InvalidOperationException("The captured marriage no longer matches this agreement.");
            var romance = Romance.RomanticStateList?.Where(r => r != null &&
                (r.Person1 == actor && r.Person2 == spouse || r.Person1 == spouse && r.Person2 == actor)).ToList();
            // No provider, movement, clan transfer or new relationship. All native work stays on the game thread.
            foreach (JObject row in (JArray)data["relations"])
            {
                var target = ReignActionService.HeroById(row.Value<string>("heroId"));
                if (target == null || actor.GetRelation(target) != row.Value<int>("before"))
                    throw new InvalidOperationException("Relationship state changed before the letter could be sent.");
            }
            try
            {
                actor.Spouse = null; spouse.Spouse = null;
                if (romance != null) foreach (var state in romance) Romance.RomanticStateList.Remove(state);
                foreach (JObject row in (JArray)data["relations"])
                {
                    var target = ReignActionService.HeroById(row.Value<string>("heroId"));
                    ChangeRelationAction.ApplyRelationChangeBetweenHeroes(actor, target, row.Value<int>("after") - actor.GetRelation(target), false);
                    if (actor.GetRelation(target) != row.Value<int>("after")) throw new InvalidOperationException("The relationship consequence was not applied.");
                }
                if (actor.Spouse != null || spouse.Spouse != null) throw new InvalidOperationException("The marriage did not end.");
                data["operationId"] = key; data["termsHash"] = agreement.TermsHash();
                ReignWorldHistoryCampaignBehavior.Instance.RecordReignSystemEvent("divorce", "completed", "relationships", key,
                    actor.Name + " ended their marriage to " + spouse.Name + " by letter.", "ordinary", actor.StringId,
                    actor.Clan?.Kingdom?.StringId ?? "", data, new JArray(spouse.StringId));
                behavior.SaveNativeOperation(key, data);
            }
            catch (Exception failure)
            {
                try
                {
                    actor.Spouse = spouse; spouse.Spouse = actor;
                    if (romance != null) foreach (var state in romance)
                        if (!Romance.RomanticStateList.Contains(state)) Romance.RomanticStateList.Add(state);
                    foreach (JObject row in (JArray)data["relations"])
                    {
                        var target = ReignActionService.HeroById(row.Value<string>("heroId"));
                        if (target != null) ChangeRelationAction.ApplyRelationChangeBetweenHeroes(actor, target,
                            row.Value<int>("before") - actor.GetRelation(target), false);
                    }
                }
                catch (Exception rollback) { throw new AgreementRecoveryException("Marriage consequences require recovery.", rollback.Message, failure); }
                throw;
            }
        }

        internal static bool Observe(ActionClause clause, string key)
        {
            var receipt = ReignAgreementBehavior.Instance.NativeOperation(key);
            if (receipt == null) return false;
            var actor = ReignActionService.HeroById(clause.ActorId);
            var spouse = ReignActionService.HeroById(receipt.Value<string>("spouseId"));
            return actor != null && spouse != null && actor.Spouse == null && spouse.Spouse == null;
        }
    }
}
