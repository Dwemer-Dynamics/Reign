using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Reign.Core.Contracts.Actions;
using Reign.Core.Contracts.Government;
using ReignBeta.Government;
using ReignBeta.World;
using ReignBeta.Campaign;
using SandBox.Objects.Usables;
using TaleWorlds.MountAndBlade;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.ObjectSystem;
using ReignBeta.Actions;
using ReignBeta.Characters.Campaign;
using ReignBeta.Government.Campaign;
using ReignBeta.Rebellion.Campaign;
using ReignBeta.WorldSimulation.Arrests;
using ReignBeta.WorldSimulation.CampaignCommands;
using ReignBeta.WorldSimulation.Rulers;
using ReignBeta.WorldSimulation.World;
using ReignBeta.Platform.Runtime;

namespace ReignBeta.WorldSimulation.Actions
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
            if (kind == "pregnancy") return ReignServices.Family?.PregnancyExposureOptions(clause?.RecipientId ?? "") ?? rows;
            Hero owner = Hero.AllAliveHeroes.FirstOrDefault(h => h.StringId == ownerId);
            MobileParty party = owner?.PartyBelongedTo;
            string governmentId = ReignServices.Government?.Seats.FirstOrDefault(x => x.HeroStringId == ownerId)?.KingdomStringId
                ?? owner?.Clan?.Kingdom?.StringId;
            void Add(string id, string label, bool available = true, string reason = "", int quantity = 0)
                => rows.Add(new ReignActionOption { Id = id, Label = label, Available = available, Reason = reason, Quantity = quantity });
            switch (kind)
            {
                case "rulerJudgment":
                    var rulers = ReignServices.Ruler;
                    if (clause != null && rulers != null)
                        foreach (var target in rulers.InfluenceTargets(ReignActionService.HeroById(clause.RecipientId)))
                            Add(target.Id, ReignActionService.HeroById(target.SubjectId)?.Name.ToString() ?? target.SubjectId);
                    break;
                case "rulerVerdict":
                    var adjudicator = ReignServices.Ruler;
                    var pending = adjudicator?.FindInfluenceTarget(ReignActionService.Arg(clause, "judgmentId"), clause?.RecipientId ?? "");
                    if (pending != null)
                        foreach (var verdict in adjudicator.InfluenceVerdicts(pending))
                            Add(verdict.ToString(), ReignRulerCampaignBehavior.InfluenceVerdictLabel(verdict));
                    break;
                case "npcMeetingPlace":
                    rows.AddRange(ReignServices.NpcLives?.MeetingPlaceOptions(clause) ?? Array.Empty<ReignActionOption>());
                    break;
                case "npcMeeting":
                    rows.AddRange(ReignServices.NpcLives?.MeetingOptions(ownerId) ?? Array.Empty<ReignActionOption>());
                    break;
#if !REIGN_EXCLUDE_COURT
                case "spySocialItem":
                    if (clause != null && ReignServices.CourtWorld != null)
                        rows.AddRange(ReignServices.CourtWorld.AgreementSocialChoices(clause));
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
                    if (ReignServices.CampaignCommand != null) foreach (var order in ReignServices.CampaignCommand.Orders.Where(x => x.CommanderHeroStringId == ownerId))
                        Add(order.OrderId, order.Objective.Replace('_', ' ') + " — " + order.Status, !order.IsTerminal, "This order has ended.");
                    break;
                case "clanAccord":
                    if (ReignServices.ClanAccords != null)
                        foreach (var accord in ReignServices.ClanAccords.Records.Where(x => x.PartnerClanId == owner?.Clan?.StringId))
                            Add(accord.Id, accord.Type + " — " + accord.PartnerClanName, accord.IsActive, "This accord has ended.");
                    break;
                case "arrestCase":
                    if (ReignServices.Arrest != null) foreach (var entry in ReignServices.Arrest.Cases)
                        Add(entry.CaseId, (ReignActionService.HeroById(entry.AccusedHeroStringId)?.Name?.ToString() ?? entry.AccusedHeroStringId) + " — " + entry.Phase);
                    break;
                case "rebellion":
                    if (ReignServices.Rebellion != null) foreach (var movement in ReignServices.Rebellion.Movements)
                        Add(movement.MovementId, movement.MovementId + " — " + movement.Stage, !movement.ResolutionApplied, "This rebellion has ended.");
                    break;
                case "sceneLocation":
                    if (Mission.Current != null) foreach (var location in Mission.Current.ActiveMissionObjects.OfType<Passage>().Where(x => x.ToLocation != null).Select(x => x.ToLocation).Distinct())
                        Add(location.StringId, location.Name.ToString());
                    break;
                case "item":
                    if (owner != null && (party == null || party.LeaderHero != owner && owner != Hero.MainHero))
                        foreach (var item in (ReignServices.PersonalItemService?.Snapshot(owner) ?? new JArray()).OfType<JObject>())
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
                    if (ReignServices.Government != null)
                        foreach (var p in ReignServices.Government.Parties.Where(p => p.KingdomStringId == governmentId))
                            Add(p.PartyId, p.Name);
                    break;
                case "governmentPetition":
                    var government = ReignServices.Government;
                    if (government != null)
                    foreach (var petition in government.Participation.Petitions.Where(x => x.AuthorId == clause?.RecipientId || GovernmentParticipationRules.CanRead(x, ownerId)))
                    {
                        string operation = ReignGovernmentCampaignBehavior.PetitionOperation(clause?.ActionId ?? "");
                        bool allowed = government.CheckPetitionAction(operation, petition.Id, petition.Revision, owner,
                            ReignActionService.HeroById(clause?.RecipientId), out string reason);
                        rows.Add(new ReignActionOption { Id = petition.Id, Label = petition.Title, Available = allowed, Reason = allowed ? petition.Terms : reason,
                            Arguments = new Dictionary<string, string> { ["petitionRevision"] = petition.Revision.ToString() } });
                    }
                    break;
                case "governmentNomination":
                    var nominationGovernment = ReignServices.Government;
                    if (nominationGovernment != null)
                    foreach (var caucus in nominationGovernment.Participation.Caucuses.Where(c => !c.Closed && c.Roll.Contains(ownerId)))
                    foreach (var petition in nominationGovernment.Participation.Petitions.Where(p => caucus.Nominations.Contains(p.Id)
                        && GovernmentParticipationRules.CanRead(p, ownerId)))
                    {
                        string caucusId = GovernmentParticipationRules.CaucusId(caucus);
                        bool allowed = nominationGovernment.CheckNominationSupport(ownerId, petition.Id, petition.Revision, caucusId, out string reason);
                        rows.Add(new ReignActionOption { Id = petition.Id, Label = petition.Title + " — "
                            + nominationGovernment.GetParties(caucus.KingdomId).FirstOrDefault(p => p.PartyId == caucus.PartyId)?.Name
                            + " · " + ReignGovernmentCampaignBehavior.CaucusSessionLabel(caucus), Available = allowed,
                            Reason = allowed ? "Your own nomination vote; a majority and a later chamber vote remain separate." : reason,
                            Arguments = new Dictionary<string, string> { ["petitionRevision"] = petition.Revision.ToString(), ["caucusId"] = caucusId } });
                    }
                    break;
                case "governmentPartyMember":
                    var ownSeat = ReignServices.Government?.Seats.FirstOrDefault(x => x.HeroStringId == ownerId);
                    if (ownSeat != null) foreach (var member in ReignServices.Government.GetSeats(ownSeat.KingdomStringId).Where(x => x.PartyId == ownSeat.PartyId))
                        Add(member.HeroStringId, ReignActionService.HeroById(member.HeroStringId)?.Name?.ToString() ?? "Unavailable member");
                    break;
                case "governmentBusiness":
                    if (ReignServices.Government != null)
                        foreach (var b in ReignServices.Government.AgreementBusinesses.Where(b => b.KingdomStringId == governmentId))
                        {
                            bool active = !GovernmentBusinessRules.IsClosed(b.Status);
                            bool memberVote = clause?.ActionId == "government.support_vote";
                            bool available = active && (!memberVote || ReignServices.Government.GetParticipationAccess(b.KingdomStringId, owner, b.BusinessId).Ballot);
                            Add(b.BusinessId, ReignServices.Government.DocketLabel(b.BusinessId) + " — " + b.Title, available, available ? "" : "This business is not awaiting this member's vote.");
                        }
                    break;
                case "governmentResolution":
                    if (ReignServices.Government != null)
                        foreach (var r in ReignServices.Government.Resolutions.Where(r => r.KingdomStringId == governmentId))
                        {
                            var hearing = ReignServices.Government.AgreementBusinesses
                                .FirstOrDefault(b => b.Kind == "seasonal" && b.TargetId == r.ResolutionId);
                            bool available = clause?.ActionId == "government.contribute_goods" ? r.Status == "active"
                                : GovernmentBusinessRules.CanDecideResolution(r.Status, hearing?.Status, hearing != null);
                            Add(r.ResolutionId, ReignGovernmentResolutionCatalog.Find(r.TemplateId)?.Title ?? r.TemplateId,
                                available, "This resolution is not awaiting the requested contribution or decision.");
                        }
                    break;
                case "governmentProposal":
                    if (ReignServices.Government != null)
                        foreach (var p in ReignServices.Government.GetGovernmentProposalChoices(ReignActionService.Arg(clause, "kingdomId", governmentId)).OfType<JObject>())
                            Add(p.Value<string>("proposalId"), p.Value<string>("label"), p.Value<bool?>("available") == true, "This proposal already has a hearing or is unavailable.");
                    break;
                case "governmentRoute":
                    var resolution = ReignServices.Government?.Resolutions.FirstOrDefault(r => r.ResolutionId == ReignActionService.Arg(clause, "resolutionId"));
                    var template = ReignGovernmentResolutionCatalog.Find(resolution?.TemplateId);
                    if (template != null)
                    {
                        Add("0", template.FirstRoute.Description + " — " + template.FirstRoute.Target + " " + template.FirstRoute.Unit + ", within " + template.FirstRoute.DeadlineDays + " days");
                        Add("1", template.SecondRoute.Description + " — " + template.SecondRoute.Target + " " + template.SecondRoute.Unit + ", within " + template.SecondRoute.DeadlineDays + " days");
                    }
                    break;
                case "governmentOption":
                    string businessId = ReignActionService.Arg(clause, "businessId");
                    var business = ReignServices.Government?.AgreementBusinesses.FirstOrDefault(b => b.BusinessId == businessId);
                    if (business != null) foreach (var o in JArray.Parse(business.OptionsJson).OfType<JObject>()) Add(o.Value<string>("id") ?? "", o.Value<string>("label") ?? o.Value<string>("id") ?? "");
                    break;
            }
            return rows.Where(r => query.Length == 0 || r.Label.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0 || r.Id.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                .OrderByDescending(r => r.Available).ThenBy(r => r.Label, StringComparer.CurrentCultureIgnoreCase).ThenBy(r => r.Id, StringComparer.Ordinal).ToList();
        }
    }
}
