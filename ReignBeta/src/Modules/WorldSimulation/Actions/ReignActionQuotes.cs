using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;
using Reign.Core.Contracts.Actions;
using ReignBeta.World;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.ObjectSystem;

namespace ReignBeta.Actions
{
    public static class ReignActionQuotes
    {
        public static long Value(ActionClause c, out string source)
        {
            var actor = ReignActionService.HeroById(c.ActorId); var recipient = ReignActionService.HeroById(c.RecipientId);
            int count = int.Parse(ReignActionService.Arg(c, "quantity", "1"), CultureInfo.InvariantCulture);
            var models = TaleWorlds.CampaignSystem.Campaign.Current.Models;
            string id = ReignActionService.Arg(c, "assetId");
            double unit;
            switch (c.ActionId)
            {
                case "give.gold": source = "denars"; unit = 1; break;
                case "give.item":
                    var item = ReignObjectResolver.FindItem(id) ?? throw new InvalidOperationException("Item appraisal is unavailable.");
                    string variant = ReignActionService.Arg(c, "variantId");
                    var modifier = variant.Length == 0 ? null : MBObjectManager.Instance.GetObject<ItemModifier>(variant);
                    unit = ReignValueService.EstimateItemValue(item, new EquipmentElement(item, modifier), 1, actor?.PartyBelongedTo, out source);
                    break;
                case "give.troop":
                    source = "native_recruitment_cost";
                    var troop = MBObjectManager.Instance.GetObject<CharacterObject>(id) ?? throw new InvalidOperationException("Troop appraisal is unavailable.");
                    unit = models.PartyWageModel.GetTroopRecruitmentCost(troop, recipient, false).ResultNumber; break;
                case "give.prisoner":
                    source = "native_prisoner_ransom";
                    var prisoner = MBObjectManager.Instance.GetObject<CharacterObject>(id) ?? throw new InvalidOperationException("Prisoner appraisal is unavailable.");
                    unit = models.RansomValueCalculationModel.PrisonerRansomValue(prisoner, actor); break;
                case "give.settlement":
                    source = "native_settlement_value";
                    var settlement = ReignObjectResolver.FindSettlement(id) ?? throw new InvalidOperationException("Settlement appraisal is unavailable.");
                    unit = Math.Max(Math.Abs(models.SettlementValueModel.CalculateSettlementValueForFaction(settlement, actor.MapFaction)),
                        Math.Abs(models.SettlementValueModel.CalculateSettlementValueForFaction(settlement, recipient.MapFaction))); break;
                case "give.workshop":
                    source = "native_workshop_cost";
                    var workshop = ReignObjectResolver.FindWorkshop(id) ?? throw new InvalidOperationException("Workshop appraisal is unavailable.");
                    unit = Math.Max(models.WorkshopModel.GetCostForPlayer(workshop), models.WorkshopModel.GetCostForNotable(workshop)); break;
                case "give.ship":
                    source = "native_ship_trade_value";
                    var ship = actor?.PartyBelongedTo?.Ships.SingleOrDefault(x => ReignAssetTransfer.ShipId(x) == id)
                        ?? throw new InvalidOperationException("Ship appraisal is unavailable.");
                    unit = models.ShipCostModel.GetShipTradeValue(ship, actor.PartyBelongedTo.Party, recipient?.PartyBelongedTo?.Party); break;
                default: throw new InvalidOperationException("This service has no native asset appraisal.");
            }
            double total = Math.Ceiling(unit * count * c.Installments);
            if (unit <= 0 || double.IsNaN(total) || double.IsInfinity(total) || total > long.MaxValue)
                throw new InvalidOperationException("A positive native appraisal is unavailable.");
            return checked((long)total);
        }

        public static IReadOnlyList<string> ValidateNpcQuote(ActionAgreement a)
        {
            var errors = new List<string>();
            // Gifts need no exchange rate. Mixed service bargains cannot be valued as if
            // their service were free; their terms are still explicitly reviewed.
            if (!a.Clauses.All(c => c.ActionId.StartsWith("give.")) || !a.Clauses.Any(c => c.ActorId == a.PlayerId)
                || !a.Clauses.Any(c => c.ActorId == a.NpcId)) return errors;
            try
            {
                long player = 0, npc = 0;
                foreach (var c in a.Clauses)
                {
                    long value = Value(c, out _);
                    if (c.ActorId == a.PlayerId) player = checked(player + value); else npc = checked(npc + value);
                }
                if (!AgreementRules.IsReasonableQuote(player, npc, 0.5, 2.0))
                    errors.Add("The NPC's proposed asset exchange is outside the native appraisal range (50% to 200%).");
            }
            catch (Exception ex) { errors.Add("The NPC quote cannot be verified: " + ex.Message); }
            return errors;
        }

        public static JObject Context(string player, string npc, ActionAgreement offered, string conversationText = "")
        {
            var candidates = new List<ActionPromptAsset>();
            foreach (string actor in new[] { player, npc })
            {
                foreach (var definition in ReignActionCatalog.All.Where(d => d.Id.StartsWith("give.")))
                {
                    var parameter = definition.Parameters.FirstOrDefault(p => p.Key == "assetId");
                    if (parameter == null) continue;
                    var scope = new ActionClause { ActionId = definition.Id, ActorId = actor, RecipientId = actor == player ? npc : player };
                    foreach (var option in ReignActionEntities.Find(parameter.Kind, actor, clause: scope).Where(x => x.Available))
                    {
                        var clause = new ActionClause { ActionId = definition.Id, ActorId = actor, RecipientId = actor == player ? npc : player };
                        clause.Arguments["assetId"] = option.Id; clause.Arguments["quantity"] = "1";
                        foreach (var pair in option.Arguments) clause.Arguments[pair.Key] = pair.Value;
                        candidates.Add(new ActionPromptAsset { Clause = clause, Name = option.Label, Available = Math.Max(1, option.Quantity) });
                    }
                }
            }
            var rows = new JArray();
            foreach (var asset in ActionPromptScope.Select(candidates, offered, conversationText))
            {
                var clause = asset.Clause;
                var row = new JObject { ["actionId"] = clause.ActionId, ["actorId"] = clause.ActorId, ["assetId"] = clause.Arguments["assetId"],
                    ["name"] = asset.Name, ["available"] = asset.Available, ["arguments"] = JObject.FromObject(clause.Arguments) };
                try { row["unitDenars"] = Value(clause, out string source); row["valueSource"] = source; }
                catch { row["appraisalAvailable"] = false; }
                rows.Add(row);
            }
            return new JObject { ["assets"] = rows, ["scope"] = ActionPromptScope.Policy, ["eligibleAssetCount"] = candidates.Count,
                ["selectionComplete"] = rows.Count == candidates.Count,
                ["npcExchangeMinimumRatio"] = 0.5, ["npcExchangeMaximumRatio"] = 2.0,
                ["playerGold"] = ReignActionService.HeroById(player)?.Gold ?? 0, ["npcGold"] = ReignActionService.HeroById(npc)?.Gold ?? 0 };
        }
    }
}
