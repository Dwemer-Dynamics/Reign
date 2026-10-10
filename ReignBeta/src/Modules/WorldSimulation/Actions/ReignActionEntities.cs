using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Reign.Core.Contracts.Actions;
using Reign.Core.Contracts.Government;
using ReignBeta.Government;
#if !REIGN_EXCLUDE_COURT
using ReignBeta.Court;
#endif
using ReignBeta.World;
using ReignBeta.Campaign;
using SandBox.Objects.Usables;
using TaleWorlds.MountAndBlade;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.ObjectSystem;

namespace ReignBeta.Actions
{
    public sealed class ReignActionOption
    {
        public string Id = "";
        public string Label = "";
        public string Reason = "";
        public bool Available = true;
        public int Quantity;
        public Dictionary<string, string> Arguments = new Dictionary<string, string>();
    }

    public static class ReignActionEntities
    {
        public static IReadOnlyList<ReignActionOption> Find(string kind, string ownerId, string query = "", string category = "", ActionClause clause = null)
        {
            var rows = new List<ReignActionOption>();
            Hero owner = Hero.AllAliveHeroes.FirstOrDefault(h => h.StringId == ownerId);
            MobileParty party = owner?.PartyBelongedTo;
            string governmentId = ReignGovernmentCampaignBehavior.Instance?.Seats.FirstOrDefault(x => x.HeroStringId == ownerId)?.KingdomStringId
                ?? owner?.Clan?.Kingdom?.StringId;
            void Add(string id, string label, bool available = true, string reason = "", int quantity = 0)
                => rows.Add(new ReignActionOption { Id = id, Label = label, Available = available, Reason = reason, Quantity = quantity });
            switch (kind)
            {
#if !REIGN_EXCLUDE_COURT
                case "spySocialItem":
                    if (clause != null && ReignCourtCampaignBehavior.Instance != null)
                        rows.AddRange(ReignCourtCampaignBehavior.Instance.AgreementSocialChoices(clause));
                    if (rows.Count == 0) Add("", "No loaded rumors or reputations", false, "The list is loaded from this person's current social record.");
                    break;
#endif
                case "hero": foreach (var h in Hero.AllAliveHeroes) Add(h.StringId, h.Name.ToString()); break;
                case "clan": foreach (var c in Clan.All.Where(c => !c.IsEliminated)) Add(c.StringId, c.Name.ToString()); break;
                case "kingdom": foreach (var k in Kingdom.All.Where(k => !k.IsEliminated)) Add(k.StringId, k.Name.ToString()); break;
                case "party": foreach (var p in MobileParty.All) Add(p.StringId, p.Name.ToString()); break;
                case "faction":
                    foreach (var k in Kingdom.All.Where(k => !k.IsEliminated)) Add("kingdom:" + k.StringId, k.Name.ToString());
                    foreach (var c in Clan.All.Where(c => !c.IsEliminated)) Add("clan:" + c.StringId, c.Name.ToString());
                    break;
                case "partyBase": case "mapPoint":
                    foreach (var p in MobileParty.All) Add("party:" + p.StringId, p.Name.ToString());
                    foreach (var s in Settlement.All) Add("settlement:" + s.StringId, s.Name.ToString());
                    break;
                case "army":
                    foreach (var p in MobileParty.All.Where(p => p.Army?.LeaderParty == p)) Add(p.StringId, p.Name + "'s army");
                    break;
                case "workshopType": foreach (var w in MBObjectManager.Instance.GetObjectTypeList<TaleWorlds.CampaignSystem.Settlements.Workshops.WorkshopType>()) Add(w.StringId, w.Name.ToString()); break;
                case "alley":
                    foreach (var s in Settlement.All.Where(s => s.IsTown)) for (int i = 0; i < s.Alleys.Count(); i++) Add(s.StringId + ":" + i, s.Name + " — alley " + i);
                    break;
                case "settlement":
                    foreach (var s in Settlement.All.Where(s => category == "" || category == "town" && s.IsTown || category == "castle" && s.IsCastle || category == "village" && s.IsVillage))
                    {
                        bool gift = clause?.ActionId == "give.settlement";
                        bool permitted = !gift || owner != null && ActionPromptScope.CanGiveSettlement(
                            owner.Clan?.Leader == owner, s.OwnerClan == owner.Clan, s.IsTown || s.IsCastle);
                        Add(s.StringId, s.Name.ToString(), permitted, permitted ? "" : s.IsVillage ? "Villages belong to their bound town or castle." : "The owner clan's leader must agree.");
                    }
                    break;
                case "inventoryItem":
                    if (party != null) foreach (var item in party.ItemRoster.Where(x => x.Amount > 0).Select(x => x.EquipmentElement.Item).Distinct())
                        Add(item.StringId, item.Name.ToString());
                    break;
                case "region": foreach (string region in new[] { "all", "central", "east", "frontier", "north", "south", "west" }) Add(region, char.ToUpperInvariant(region[0]) + region.Substring(1)); break;
                case "campaignOrder":
                    if (ReignCampaignCommandBehavior.Instance != null) foreach (var order in ReignCampaignCommandBehavior.Instance.Orders.Where(x => x.CommanderHeroStringId == ownerId))
                        Add(order.OrderId, order.Objective.Replace('_', ' ') + " — " + order.Status, !order.IsTerminal, "This order has ended.");
                    break;
                case "clanAccord":
                    if (ReignBeta.ClanAccords.ReignClanAccordsCampaignBehavior.Instance != null)
                        foreach (var accord in ReignBeta.ClanAccords.ReignClanAccordsCampaignBehavior.Instance.Records.Where(x => x.PartnerClanId == owner?.Clan?.StringId))
                            Add(accord.Id, accord.Type + " — " + accord.PartnerClanName, accord.IsActive, "This accord has ended.");
                    break;
                case "arrestCase":
                    if (ReignArrestCampaignBehavior.Instance != null) foreach (var entry in ReignArrestCampaignBehavior.Instance.Cases)
                        Add(entry.CaseId, (ReignActionService.HeroById(entry.AccusedHeroStringId)?.Name?.ToString() ?? entry.AccusedHeroStringId) + " — " + entry.Phase);
                    break;
                case "rebellion":
                    if (ReignRebellionCampaignBehavior.Instance != null) foreach (var movement in ReignRebellionCampaignBehavior.Instance.Movements)
                        Add(movement.MovementId, movement.MovementId + " — " + movement.Stage, !movement.ResolutionApplied, "This rebellion has ended.");
                    break;
                case "sceneLocation":
                    if (Mission.Current != null) foreach (var location in Mission.Current.ActiveMissionObjects.OfType<Passage>().Where(x => x.ToLocation != null).Select(x => x.ToLocation).Distinct())
                        Add(location.StringId, location.Name.ToString());
                    break;
                case "item":
                    if (owner != null && (party == null || party.LeaderHero != owner && owner != Hero.MainHero))
                        foreach (var item in (ReignPersonalItemService.Instance?.Snapshot(owner) ?? new JArray()).OfType<JObject>())
                        {
                            string itemId = item.Value<string>("itemId") ?? "";
                            int amount = item.Value<int?>("amount") ?? 0;
                            if (amount <= 0 || itemId.Length == 0) continue;
                            string variant = item.Value<string>("itemModifierId") ?? "";
                            var exactItem = ReignObjectResolver.FindItem(itemId);
                            var modifier = variant.Length == 0 ? null : MBObjectManager.Instance.GetObject<ItemModifier>(variant);
                            if (exactItem == null) continue;
                            Add(itemId, new EquipmentElement(exactItem, modifier).GetModifiedItemName() + " (personal)", !exactItem.IsUniqueItem,
                                exactItem.IsUniqueItem ? "This item is reserved by a quest." : "", amount);
                            rows.Last().Arguments["variantId"] = variant;
                        }
                    if (party != null && (party.LeaderHero == owner || owner == Hero.MainHero))
                        foreach (var r in party.ItemRoster)
                        {
                            if (r.Amount < 1 || r.EquipmentElement.Item == null) continue;
                            var item = r.EquipmentElement;
                            Add(item.Item.StringId, item.GetModifiedItemName().ToString(), true, "", r.Amount);
                            rows.Last().Arguments["variantId"] = item.ItemModifier?.StringId ?? "";
                        }
                    if (owner != null)
                        foreach (var set in new[] { new { Name = "battle", Value = owner.BattleEquipment }, new { Name = "civilian", Value = owner.CivilianEquipment } })
                            for (int i = 0; i < (int)EquipmentIndex.NumEquipmentSetSlots; i++)
                            {
                                var e = set.Value[(EquipmentIndex)i]; if (e.IsEmpty) continue;
                                Add(e.Item.StringId, e.GetModifiedItemName() + " (" + set.Name + ")", !e.Item.IsUniqueItem, e.Item.IsUniqueItem ? "This item is reserved by a quest." : "", 1);
                                var extra = rows.Last().Arguments; extra["variantId"] = e.ItemModifier?.StringId ?? ""; extra["equipmentSet"] = set.Name; extra["equipmentSlot"] = i.ToString(System.Globalization.CultureInfo.InvariantCulture);
                            }
                    break;
                case "troop":
                case "prisoner":
                    if (party != null)
                        foreach (var r in (kind == "troop" ? party.MemberRoster : party.PrisonRoster).GetTroopRoster())
                        {
                            bool allowed = party.LeaderHero == owner && (kind == "prisoner" || !r.Character.IsHero);
                            Add(r.Character.StringId, r.Character.Name.ToString(), allowed, allowed ? "" : "Only the commanding owner may transfer these people.", r.Number);
                        }
                    break;
                case "workshop":
                    foreach (var town in Town.AllTowns)
                        for (int i = 0; i < town.Workshops.Length; i++)
                        {
                            var w = town.Workshops[i]; if (w == null) continue;
                            bool owns = w.Owner == owner;
                            Add(ReignObjectResolver.WorkshopId(w, i), w.Name + " — " + town.Name, owns, owns ? "" : "This person does not own this workshop.");
                        }
                    break;
                case "ship":
                    if (party?.Ships != null)
                        foreach (var ship in party.Ships)
                            Add(ReignAssetTransfer.ShipId(ship), ship.Name.ToString(), party.LeaderHero == owner && !ship.IsUsedByQuest && ship.IsTradeable, "Only an owned ship that is free of quest duties can be transferred.");
                    break;
                case "politicalParty":
                    if (ReignGovernmentCampaignBehavior.Instance != null)
                        foreach (var p in ReignGovernmentCampaignBehavior.Instance.Parties.Where(p => p.KingdomStringId == governmentId))
                            Add(p.PartyId, p.Name);
                    break;
                case "governmentBusiness":
                    if (ReignGovernmentCampaignBehavior.Instance != null)
                        foreach (var b in ReignGovernmentCampaignBehavior.Instance.AgreementBusinesses.Where(b => b.KingdomStringId == governmentId))
                        {
                            bool active = !GovernmentBusinessRules.IsClosed(b.Status);
                            bool memberVote = clause?.ActionId == "government.support_vote";
                            bool available = active && (!memberVote || b.Status == "hearing" || b.Status == "recess" || b.Status == "postponed");
                            Add(b.BusinessId, b.Title, available, available ? "" : "This business is not awaiting the requested decision.");
                        }
                    break;
                case "governmentResolution":
                    if (ReignGovernmentCampaignBehavior.Instance != null)
                        foreach (var r in ReignGovernmentCampaignBehavior.Instance.Resolutions.Where(r => r.KingdomStringId == governmentId))
                        {
                            var hearing = ReignGovernmentCampaignBehavior.Instance.AgreementBusinesses
                                .FirstOrDefault(b => b.Kind == "seasonal" && b.TargetId == r.ResolutionId);
                            bool available = clause?.ActionId == "government.contribute_goods" ? r.Status == "active"
                                : GovernmentBusinessRules.CanDecideResolution(r.Status, hearing?.Status, hearing != null);
                            Add(r.ResolutionId, ReignGovernmentResolutionCatalog.Find(r.TemplateId)?.Title ?? r.TemplateId,
                                available, "This resolution is not awaiting the requested contribution or decision.");
                        }
                    break;
                case "governmentProposal":
                    if (ReignGovernmentCampaignBehavior.Instance != null)
                        foreach (var p in ReignGovernmentCampaignBehavior.Instance.GetGovernmentProposalChoices(ReignActionService.Arg(clause, "kingdomId", governmentId)).OfType<JObject>())
                            Add(p.Value<string>("proposalId"), p.Value<string>("label"), p.Value<bool?>("available") == true, "This proposal already has a hearing or is unavailable.");
                    break;
                case "governmentRoute":
                    var resolution = ReignGovernmentCampaignBehavior.Instance?.Resolutions.FirstOrDefault(r => r.ResolutionId == ReignActionService.Arg(clause, "resolutionId"));
                    var template = ReignGovernmentResolutionCatalog.Find(resolution?.TemplateId);
                    if (template != null)
                    {
                        Add("0", template.FirstRoute.Description + " — " + template.FirstRoute.Target + " " + template.FirstRoute.Unit + ", within " + template.FirstRoute.DeadlineDays + " days");
                        Add("1", template.SecondRoute.Description + " — " + template.SecondRoute.Target + " " + template.SecondRoute.Unit + ", within " + template.SecondRoute.DeadlineDays + " days");
                    }
                    break;
                case "governmentOption":
                    string businessId = ReignActionService.Arg(clause, "businessId");
                    var business = ReignGovernmentCampaignBehavior.Instance?.AgreementBusinesses.FirstOrDefault(b => b.BusinessId == businessId);
                    if (business != null) foreach (var o in JArray.Parse(business.OptionsJson).OfType<JObject>()) Add(o.Value<string>("id") ?? "", o.Value<string>("label") ?? o.Value<string>("id") ?? "");
                    break;
            }
            return rows.Where(r => query.Length == 0 || r.Label.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0 || r.Id.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                .OrderByDescending(r => r.Available).ThenBy(r => r.Label, StringComparer.CurrentCultureIgnoreCase).ThenBy(r => r.Id, StringComparer.Ordinal).ToList();
        }
    }
}
