using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;
using Reign.Core.Contracts.Actions;
using ReignBeta.World;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Naval;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.ObjectSystem;

namespace ReignBeta.Actions
{
    internal static class ReignAssetTransfer
    {
        internal static string ShipId(Ship ship) => "ship:" + ship.RandomValue.ToString(CultureInfo.InvariantCulture) + ":" + ship.ShipHull.StringId;
        private static string Kind(ActionClause c) => c.ActionId.Substring(5);
        private static int Quantity(ActionClause c) => int.Parse(ReignActionService.Arg(c, "quantity", "1"), CultureInfo.InvariantCulture);
        private static Hero Owner(ActionClause c) => ReignActionService.HeroById(c.ActorId) ?? throw new InvalidOperationException("The giver is no longer available.");
        private static Hero Receiver(ActionClause c) => ReignActionService.HeroById(c.RecipientId) ?? throw new InvalidOperationException("The recipient is no longer available.");
        private static ItemObject Item(ActionClause c) => ReignObjectResolver.FindItem(ReignActionService.Arg(c, "assetId")) ?? throw new InvalidOperationException("The exact item no longer exists.");
        private static EquipmentElement Element(ActionClause c)
        {
            string variant = ReignActionService.Arg(c, "variantId");
            var modifier = variant.Length == 0 ? null : MBObjectManager.Instance.GetObject<ItemModifier>(variant);
            if (variant.Length > 0 && modifier == null) throw new InvalidOperationException("The exact item modifier no longer exists.");
            return new EquipmentElement(Item(c), modifier);
        }
        private static CharacterObject Troop(ActionClause c) => MBObjectManager.Instance.GetObject<CharacterObject>(ReignActionService.Arg(c, "assetId")) ?? throw new InvalidOperationException("The exact troop or prisoner no longer exists.");
        private static Equipment Equipment(ActionClause c) => ReignActionService.Arg(c, "equipmentSet") == "civilian" ? Owner(c).CivilianEquipment : Owner(c).BattleEquipment;
        private static EquipmentIndex Slot(ActionClause c) => (EquipmentIndex)int.Parse(ReignActionService.Arg(c, "equipmentSlot"), CultureInfo.InvariantCulture);
        private static int ItemCount(Hero hero, EquipmentElement e)
        {
            var roster = hero.PartyBelongedTo?.ItemRoster;
            if (roster == null || hero != hero.PartyBelongedTo.LeaderHero && hero != Hero.MainHero) return ReignPersonalItemService.Instance?.AgreementCount(hero, e) ?? 0;
            int i = roster.FindIndexOfElement(e); return i < 0 ? 0 : roster.GetElementNumber(i);
        }
        private static void ChangeItem(Hero hero, EquipmentElement e, int delta, JObject saved = null, bool source = false)
        {
            string key = source ? "source" : "recipient";
            bool personal = saved != null ? saved.Value<string>(key + "Storage") == "personal"
                : hero.PartyBelongedTo == null || hero != hero.PartyBelongedTo.LeaderHero && hero != Hero.MainHero;
            var roster = personal ? null : (saved == null ? hero.PartyBelongedTo : SavedParty(saved, key))?.ItemRoster;
            if (personal)
            {
                if (ReignPersonalItemService.Instance == null) throw new InvalidOperationException("Personal possessions are unavailable.");
                ReignPersonalItemService.Instance.AgreementChange(hero, e, delta); return;
            }
            if (roster == null) throw new InvalidOperationException("The saved item party is unavailable.");
            int index = roster.FindIndexOfElement(e);
            int before = index < 0 ? 0 : roster.GetElementNumber(index);
            if ((long)before + delta < 0 || (long)before + delta > int.MaxValue) throw new InvalidOperationException("Item quantity is out of range.");
            roster.AddToCounts(e, delta);
            index = roster.FindIndexOfElement(e);
            if ((index < 0 ? 0 : roster.GetElementNumber(index)) != before + delta) throw new InvalidOperationException("The native item roster did not retain the transfer.");
        }
        private static TroopRoster Roster(ActionClause c, Hero h) => (Kind(c) == "troop" ? h.PartyBelongedTo?.MemberRoster : h.PartyBelongedTo?.PrisonRoster)
            ?? throw new InvalidOperationException("This transfer requires an existing party.");
        private static MobileParty SavedParty(JObject saved, string key)
        {
            string id = saved.Value<string>(key + "PartyId") ?? "";
            return MobileParty.All.FirstOrDefault(p => p.StringId == id) ?? throw new InvalidOperationException("The agreed " + key + " party is no longer available.");
        }
        private static TroopRoster SavedRoster(ActionClause c, JObject saved, string key) => Kind(c) == "troop"
            ? SavedParty(saved, key).MemberRoster : SavedParty(saved, key).PrisonRoster;

        private static JObject SourceState(ActionClause c, JObject saved)
        {
            var from = Owner(c);
            if (Kind(c) == "gold") return new JObject { ["count"] = from.Gold };
            if (Kind(c) == "item")
            {
                if (ReignActionService.Arg(c, "equipmentSet").Length > 0) return new JObject { ["count"] = Equipment(c)[Slot(c)].Equals(Element(c)) ? 1 : 0 };
                return new JObject { ["count"] = SavedItemCount(from, Element(c), saved, "source") };
            }
            if (Kind(c) == "troop" || Kind(c) == "prisoner")
            {
                var row = SavedRoster(c, saved, "source").GetTroopRoster().FirstOrDefault(x => x.Character == Troop(c));
                return RosterRowState(row);
            }
            return new JObject();
        }
        private static int SavedItemCount(Hero hero, EquipmentElement element, JObject saved, string key)
        {
            if (saved.Value<string>(key + "Storage") == "personal") return ReignPersonalItemService.Instance?.AgreementCount(hero, element) ?? 0;
            var roster = SavedParty(saved, key).ItemRoster; int i = roster.FindIndexOfElement(element);
            return i < 0 ? 0 : roster.GetElementNumber(i);
        }

        internal static void Check(ActionAgreement a, ActionClause c)
        {
            var from = Owner(c); var to = Receiver(c); int count = Quantity(c);
            if (from == to || count < 1) throw new InvalidOperationException("Select a different recipient and a positive quantity.");
            int total = checked(count * c.Installments);
            switch (Kind(c))
            {
                case "gold":
                    if (from.Gold < total) throw new InvalidOperationException(from.Name + " does not have enough unreserved denars.");
                    if ((long)to.Gold + total > int.MaxValue) throw new InvalidOperationException("The recipient cannot hold that much gold.");
                    break;
                case "item":
                    var e = Element(c);
                    if (e.Item.IsUniqueItem) throw new InvalidOperationException("A quest item cannot be bargained away.");
                    if (ReignActionService.Arg(c, "equipmentSet").Length > 0)
                    {
                        int slot = (int)Slot(c);
                        if (total != 1 || slot < 0 || slot >= (int)EquipmentIndex.NumEquipmentSetSlots || !Equipment(c)[Slot(c)].Equals(e)) throw new InvalidOperationException("The selected equipment slot no longer contains that exact item.");
                    }
                    else if (ItemCount(from, e) < total) throw new InvalidOperationException("There are not enough of that exact item variant.");
                    break;
                case "troop":
                case "prisoner":
                    var troop = Troop(c);
                    if (from.PartyBelongedTo?.LeaderHero != from) throw new InvalidOperationException("Only this party's commander can transfer its roster.");
                    if (Kind(c) == "troop" && troop.IsHero) throw new InvalidOperationException("A hero must personally agree to change parties.");
                    if (troop.HeroObject == Hero.MainHero) throw new InvalidOperationException("An agreement cannot transfer the player into captivity.");
                    if (Roster(c, from).GetTroopCount(troop) < total) throw new InvalidOperationException("The selected roster no longer has that quantity.");
                    if (from.PartyBelongedTo == to.PartyBelongedTo || to.PartyBelongedTo == null) throw new InvalidOperationException("Select a recipient with a different party.");
                    if (Kind(c) == "troop" && to.PartyBelongedTo.MemberRoster.TotalManCount + total > to.PartyBelongedTo.Party.PartySizeLimit) throw new InvalidOperationException("The recipient's party has insufficient room for these troops.");
                    break;
                case "settlement":
                    var s = ReignObjectResolver.FindSettlement(ReignActionService.Arg(c, "assetId"));
                    if (s == null || s.IsVillage || s.OwnerClan != from.Clan || from.Clan?.Leader != from || to.Clan == null || to.Clan == from.Clan) throw new InvalidOperationException("Only the owning clan's leader may grant a town or castle to another clan.");
                    if (s.IsUnderSiege) throw new InvalidOperationException("A besieged settlement cannot be transferred.");
                    break;
                case "workshop":
                    if (ReignObjectResolver.FindWorkshop(ReignActionService.Arg(c, "assetId"))?.Owner != from) throw new InvalidOperationException("The workshop is no longer owned by the giver.");
                    break;
                case "ship":
                    if (from.PartyBelongedTo?.LeaderHero != from || to.PartyBelongedTo == null) throw new InvalidOperationException("Both ship parties must exist and the giver must command the source party.");
                    var ships = from.PartyBelongedTo.Ships.Where(s1 => ShipId(s1) == ReignActionService.Arg(c, "assetId")).ToList();
                    if (ships.Count != 1 || ships[0].IsUsedByQuest || !ships[0].IsTradeable) throw new InvalidOperationException("Select one exact tradeable ship; the saved identity must be unambiguous.");
                    break;
                default: throw new InvalidOperationException("Unsupported asset type.");
            }
            if (Kind(c) != "gold" && Kind(c) != "item" && Kind(c) != "troop" && Kind(c) != "prisoner" && c.Installments != 1) throw new InvalidOperationException("Ownership transfers cannot be split into installments.");
        }

        internal static IEnumerable<string> CheckAggregate(ActionAgreement a)
        {
            foreach (var group in a.Clauses.Where(c => c.ActionId.StartsWith("give.", StringComparison.Ordinal))
                .GroupBy(c => c.ActorId + "|" + c.ActionId + "|" + ReignActionService.Arg(c, "assetId") + "|" + ReignActionService.Arg(c, "variantId") + "|" + ReignActionService.Arg(c, "equipmentSet") + "|" + ReignActionService.Arg(c, "equipmentSlot")))
            {
                string error = null;
                try
                {
                    var c = JsonConvertClause(group.First());
                    if (group.Count() > 1 && new[] { "give.settlement", "give.workshop", "give.ship" }.Contains(c.ActionId)) throw new InvalidOperationException("The same owned asset appears twice in this bargain.");
                    long sum = group.Sum(x => (long)Quantity(x) * x.Installments);
                    if (sum > int.MaxValue) throw new InvalidOperationException("The combined quantity is too large.");
                    c.Arguments["quantity"] = sum.ToString(CultureInfo.InvariantCulture); c.Installments = 1;
                    Check(a, c);
                }
                catch (Exception ex) { error = ex.Message; }
                if (error != null) yield return error;
            }
            foreach (var group in a.Clauses.Where(c => c.ActionId == "give.troop").GroupBy(c => c.RecipientId))
            {
                var target = ReignActionService.HeroById(group.Key)?.PartyBelongedTo;
                long total = group.Sum(c => (long)Quantity(c) * c.Installments);
                if (target != null && target.MemberRoster.TotalManCount + total > target.Party.PartySizeLimit) yield return "The combined troop package exceeds the recipient's party capacity.";
            }
            foreach (var group in a.Clauses.Where(c => c.ActionId == "give.gold").GroupBy(c => c.RecipientId))
            {
                var target = ReignActionService.HeroById(group.Key);
                if (target != null && (long)target.Gold + group.Sum(c => (long)Quantity(c) * c.Installments) > int.MaxValue)
                    yield return "The combined payment exceeds the recipient's gold capacity.";
            }
        }
        private static ActionClause JsonConvertClause(ActionClause c) => Newtonsoft.Json.JsonConvert.DeserializeObject<ActionClause>(Newtonsoft.Json.JsonConvert.SerializeObject(c));

        internal static JObject Capture(ActionAgreement a, ActionClause c)
        {
            Check(a, c);
            var result = new JObject { ["total"] = checked(Quantity(c) * c.Installments), ["wounded"] = 0, ["xp"] = 0 };
            foreach (var participant in new[] { new { Key = "source", Hero = Owner(c) }, new { Key = "recipient", Hero = Receiver(c) } })
            {
                var party = participant.Hero.PartyBelongedTo;
                result[participant.Key + "PartyId"] = party?.StringId ?? "";
                result[participant.Key + "ClanId"] = participant.Hero.Clan?.StringId ?? "";
                result[participant.Key + "Storage"] = party == null || party.LeaderHero != participant.Hero && participant.Hero != Hero.MainHero ? "personal" : "party";
            }
            if (Kind(c) == "troop" || Kind(c) == "prisoner")
            {
                var row = Roster(c, Owner(c)).GetTroopRoster().Single(r => r.Character == Troop(c));
                int preceding = checked((int)a.Clauses.TakeWhile(x => x.Id != c.Id)
                    .Where(x => x.ActionId == c.ActionId && x.ActorId == c.ActorId && ReignActionService.Arg(x, "assetId") == ReignActionService.Arg(c, "assetId"))
                    .Sum(x => (long)Quantity(x) * x.Installments));
                result["wounded"] = Math.Max(0, Math.Min(row.WoundedNumber - preceding, result.Value<int>("total")));
                result["xp"] = row.Number == 0 ? 0 : (int)((long)row.Xp * (preceding + result.Value<int>("total")) / row.Number - (long)row.Xp * preceding / row.Number);
                result["hero"] = row.Character.IsHero;
            }
            return result;
        }
        internal static void Reserve(ActionClause c, JObject saved)
        {
            var before = SourceState(c, saved);
            try
            {
                ReserveCore(c, saved);
                if (new[] { "gold", "item", "troop", "prisoner" }.Contains(Kind(c)) && saved.Value<bool?>("hero") != true
                    && SourceState(c, saved).Value<int>("count") != before.Value<int>("count") - saved.Value<int>("total"))
                    throw new InvalidOperationException("The native source did not reserve the exact quantity.");
            }
            catch (Exception original)
            {
                try
                {
                    var current = SourceState(c, saved); int delta = before.Value<int>("count") - current.Value<int>("count");
                    if (Kind(c) == "gold") Owner(c).ChangeHeroGold(delta);
                    else if (Kind(c) == "item")
                    {
                        if (ReignActionService.Arg(c, "equipmentSet").Length > 0) Equipment(c)[Slot(c)] = before.Value<int>("count") == 1 ? Element(c) : EquipmentElement.Invalid;
                        else ChangeItem(Owner(c), Element(c), delta, saved, true);
                    }
                    else if ((Kind(c) == "troop" || Kind(c) == "prisoner") && saved.Value<bool?>("hero") != true)
                        SavedRoster(c, saved, "source").AddToCounts(Troop(c), delta, false, before.Value<int>("wounded") - current.Value<int>("wounded"), before.Value<int>("xp") - current.Value<int>("xp"));
                    if (!JToken.DeepEquals(before, SourceState(c, saved))) throw new InvalidOperationException("The source state could not be restored exactly.");
                }
                catch (Exception rollback) { throw new AgreementRecoveryException("Native reservation rollback requires recovery: " + rollback.Message,
                    new JObject { ["clauseId"] = c.Id, ["before"] = before, ["custody"] = saved }.ToString(), original); }
                throw;
            }
        }
        private static void ReserveCore(ActionClause c, JObject saved)
        {
            int total = saved.Value<int>("total"); var from = Owner(c);
            switch (Kind(c))
            {
                case "gold": from.ChangeHeroGold(-total); break;
                case "item":
                    if (ReignActionService.Arg(c, "equipmentSet").Length > 0) Equipment(c)[Slot(c)] = EquipmentElement.Invalid;
                    else ChangeItem(from, Element(c), -total, saved, true);
                    break;
                case "troop":
                case "prisoner":
                    if (saved.Value<bool?>("hero") == true) break;
                    SavedRoster(c, saved, "source").AddToCounts(Troop(c), -total, false, -saved.Value<int>("wounded"), -saved.Value<int>("xp")); break;
            }
        }
        internal static void Return(ActionClause c, JObject saved, int fulfilledInstallments)
        {
            int fulfilled = checked(Quantity(c) * fulfilledInstallments);
            int total = saved.Value<int>("total") - fulfilled;
            if (total <= 0) return;
            var from = Owner(c);
            var before = SourceState(c, saved);
            try
            {
            switch (Kind(c))
            {
                case "gold":
                    if ((long)from.Gold + total > int.MaxValue) throw new InvalidOperationException("The source cannot receive this refund yet.");
                    from.ChangeHeroGold(total); break;
                case "item":
                    if (ReignActionService.Arg(c, "equipmentSet").Length > 0 && Equipment(c)[Slot(c)].IsEmpty) Equipment(c)[Slot(c)] = Element(c);
                    else ChangeItem(from, Element(c), total, saved, true);
                    break;
                case "troop":
                case "prisoner":
                    if (saved.Value<bool?>("hero") != true) SavedRoster(c, saved, "source").AddToCounts(Troop(c), total, false,
                        Math.Max(0, saved.Value<int>("wounded") - fulfilled), saved.Value<int>("xp") - saved.Value<int>("xp") / c.Installments * fulfilledInstallments); break;
            }
                // A replaced equipment slot legitimately returns the item to its saved
                // inventory; the same-slot count alone cannot observe that route.
                if (ReignActionService.Arg(c, "equipmentSet").Length == 0 && saved.Value<bool?>("hero") != true
                    && new[] { "gold", "item", "troop", "prisoner" }.Contains(Kind(c))
                    && (long)SourceState(c, saved).Value<int>("count") != (long)before.Value<int>("count") + total)
                    throw new InvalidOperationException("The native source did not receive the complete refund.");
            }
            catch (Exception ex)
            {
                throw new AgreementRecoveryException("Refund requires exact custody recovery: " + ex.Message,
                    new JObject { ["clauseId"] = c.Id, ["beforeRefund"] = before, ["custody"] = saved, ["refundQuantity"] = total }.ToString(), ex);
            }
        }
        internal static void Deliver(ActionClause c, JObject saved)
        {
            var to = Receiver(c); var from = Owner(c); int count = Quantity(c);
            if (Kind(c) == "troop" || Kind(c) == "prisoner" || Kind(c) == "ship"
                || Kind(c) == "item" && saved.Value<string>("recipientStorage") == "party")
                if (to.PartyBelongedTo != SavedParty(saved, "recipient")) throw new InvalidOperationException("The recipient changed parties before delivery.");
            if (Kind(c) == "settlement" && ((from.Clan?.StringId ?? "") != saved.Value<string>("sourceClanId")
                || (to.Clan?.StringId ?? "") != saved.Value<string>("recipientClanId")))
                throw new InvalidOperationException("The agreed settlement clans changed before delivery.");
            switch (Kind(c))
            {
                case "gold":
                    if ((long)to.Gold + count > int.MaxValue) throw new InvalidOperationException("Recipient gold capacity changed.");
                    to.ChangeHeroGold(count); break;
                case "item": ChangeItem(to, Element(c), count, saved); break;
                case "troop":
                case "prisoner":
                    if (Kind(c) == "troop" && (long)Roster(c, to).TotalManCount + count > to.PartyBelongedTo.Party.PartySizeLimit) throw new InvalidOperationException("The recipient's party no longer has room for these troops.");
                    if (saved.Value<bool?>("hero") == true)
                    {
                        if (SavedRoster(c, saved, "source").GetTroopCount(Troop(c)) != 1) throw new InvalidOperationException("The named prisoner is no longer in custody.");
                        TransferPrisonerAction.Apply(Troop(c), SavedParty(saved, "source").Party, SavedParty(saved, "recipient").Party);
                    }
                    else Roster(c, to).AddToCounts(Troop(c), count, false, Math.Min(count, saved.Value<int>("wounded")), saved.Value<int>("xp"));
                    break;
                case "settlement":
                    var settlement = ReignObjectResolver.FindSettlement(ReignActionService.Arg(c, "assetId"));
                    if (settlement?.OwnerClan != from.Clan) throw new InvalidOperationException("Settlement ownership changed before delivery.");
                    ChangeOwnerOfSettlementAction.ApplyByGift(settlement, to); break;
                case "workshop":
                    var workshop = ReignObjectResolver.FindWorkshop(ReignActionService.Arg(c, "assetId"));
                    if (workshop?.Owner != from) throw new InvalidOperationException("Workshop ownership changed before delivery.");
                    ChangeOwnerOfWorkshopAction.ApplyByWar(workshop, to, workshop.WorkshopType); break;
                case "ship":
                    var ship = SavedParty(saved, "source").Ships.SingleOrDefault(s => ShipId(s) == ReignActionService.Arg(c, "assetId"));
                    if (ship == null) throw new InvalidOperationException("The named ship is no longer owned by the giver.");
                    ChangeShipOwnerAction.ApplyByTransferring(to.PartyBelongedTo.Party, ship); break;
            }
        }

        internal static void DeliverWeddingReversible(IReadOnlyList<ActionClause> clauses, Dictionary<string, JObject> captures,
            Action marry, Func<bool> nativeChanged, Action<ActionClause, JObject> observe)
        {
            var reserved = new List<ActionClause>();
            var delivered = new List<Tuple<ActionClause, JObject>>();
            try
            {
                foreach (var clause in clauses)
                {
                    var capture = captures[clause.Id];
                    var source = SourceState(clause, capture);
                    capture["sourceBefore"] = source;
                    Reserve(clause, capture);
                    capture["sourceAfter"] = SourceState(clause, capture);
                    reserved.Add(clause);
                }
                foreach (var clause in clauses)
                {
                    var capture = captures[clause.Id];
                    var before = DeliveryState(clause, capture);
                    delivered.Add(Tuple.Create(clause, before));
                    Deliver(clause, capture);
                    var after = DeliveryState(clause, capture);
                    if (after.Value<int>("count") != before.Value<int>("count") + Quantity(clause))
                        throw new InvalidOperationException("The agreed consideration did not reach the recipient.");
                    observe(clause, new JObject { ["sourceBefore"] = capture["sourceBefore"], ["sourceAfter"] = capture["sourceAfter"],
                        ["recipientBefore"] = before, ["recipientAfter"] = after, ["verified"] = true });
                }
                marry();
            }
            catch (Exception original) when (!nativeChanged())
            {
                try
                {
                    foreach (var row in delivered.AsEnumerable().Reverse())
                        RestoreDeliveryState(row.Item1, row.Item2, captures[row.Item1.Id]);
                    foreach (var clause in reserved.AsEnumerable().Reverse())
                        Return(clause, captures[clause.Id], 0);
                }
                catch (Exception rollback)
                {
                    throw new AgreementRecoveryException("Wedding rollback needs recovery: " + rollback.Message,
                        new JArray(reserved.Select(c => new JObject { ["clauseId"] = c.Id, ["custody"] = captures[c.Id] })).ToString(), original);
                }
                throw;
            }
        }

        internal static void DeliverWeddingCustody(ActionClause clause, JObject capture, JObject receipt)
        {
            string kind = Kind(clause);
            var before = DeliveryState(clause, capture);
            var sourceBefore = SourceState(clause, capture);
            receipt["recipientBefore"] = before;
            receipt["sourceBefore"] = sourceBefore;
            string expectedOwner = kind == "settlement" ? Receiver(clause).Clan.Leader.StringId : clause.RecipientId;
            bool Verified()
            {
                var after = DeliveryState(clause, capture);
                var sourceAfter = SourceState(clause, capture);
                receipt["recipientAfter"] = after;
                receipt["sourceAfter"] = sourceAfter;
                bool transferred = kind == "settlement" || kind == "ship"
                    ? after.Value<string>("owner") == expectedOwner
                    : after.Value<int>("count") == before.Value<int>("count") + Quantity(clause)
                        && sourceAfter.Value<int>("count") == sourceBefore.Value<int>("count") - Quantity(clause);
                receipt["verified"] = transferred;
                return transferred;
            }
            try
            {
                Reserve(clause, capture);
                receipt["reserved"] = true;
                Deliver(clause, capture);
            }
            catch (Exception ex)
            {
                receipt["callbackError"] = ex.Message;
                if (!Verified()) throw;
                return;
            }
            if (!Verified()) throw new InvalidOperationException("The wedding transfer did not reach the agreed custodian.");
        }

        internal static void DeliverBatch(ActionAgreement agreement, IReadOnlyList<AgreementStep> steps)
        {
            var completed = new List<Tuple<ActionClause, JObject, JObject>>();
            // Resolve every recipient and native asset before the first effect. The full batch
            // runs synchronously on the game thread, so saves cannot interleave these mutations.
            foreach (var step in steps)
            {
                var c = agreement.Clauses.Single(x => x.Id == step.ClauseId);
                Owner(c); Receiver(c);
                if (Kind(c) == "item") Element(c);
                if (Kind(c) == "troop" || Kind(c) == "prisoner") Roster(c, Receiver(c));
            }
            try
            {
                foreach (var step in steps)
                {
                    var c = agreement.Clauses.Single(x => x.Id == step.ClauseId);
                    var escrow = JObject.Parse(step.Snapshot);
                    JObject before = DeliveryState(c, escrow);
                    completed.Add(Tuple.Create(c, before, escrow));
                    // Allocate wounded troops and XP once across installments, preserving remainders.
                    int count = Quantity(c);
                    escrow["wounded"] = Math.Max(0, Math.Min(count, escrow.Value<int>("wounded") - step.Installment * count));
                    int xp = escrow.Value<int>("xp");
                    escrow["xp"] = xp / c.Installments + (step.Installment == c.Installments - 1 ? xp % c.Installments : 0);
                    Deliver(c, escrow);
                    var after = DeliveryState(c, escrow);
                    if (Kind(c) == "gold" || Kind(c) == "item" || Kind(c) == "troop" || Kind(c) == "prisoner")
                    {
                        if (after.Value<int>("count") != before.Value<int>("count") + count) throw new InvalidOperationException("The native recipient did not receive the complete asset quantity.");
                    }
                    else if (after.Value<string>("owner") != (Kind(c) == "settlement" ? Receiver(c).Clan.Leader.StringId : c.RecipientId)) throw new InvalidOperationException("Native ownership did not change to the agreed recipient.");
                }
            }
            catch (Exception original)
            {
                var failures = new List<string>();
                foreach (var row in completed.AsEnumerable().Reverse())
                    try { RestoreDeliveryState(row.Item1, row.Item2, row.Item3); }
                    catch (Exception ex) { failures.Add(ex.Message); }
                if (failures.Count > 0) throw new AgreementRecoveryException("Native rollback needs recovery: " + string.Join(" ", failures),
                    new JArray(completed.Select(row => new JObject { ["clauseId"] = row.Item1.Id, ["before"] = row.Item2, ["custody"] = row.Item3 })).ToString(), original);
                throw;
            }
        }

        private static JObject DeliveryState(ActionClause c, JObject saved)
        {
            var to = Receiver(c); string id = ReignActionService.Arg(c, "assetId");
            switch (Kind(c))
            {
                case "gold": return new JObject { ["count"] = to.Gold };
                case "item": return new JObject { ["count"] = SavedItemCount(to, Element(c), saved, "recipient") };
                case "troop":
                case "prisoner":
                    var row = SavedRoster(c, saved, "recipient").GetTroopRoster().FirstOrDefault(x => x.Character == Troop(c));
                    return RosterRowState(row);
                case "settlement": return new JObject { ["owner"] = ReignObjectResolver.FindSettlement(id)?.OwnerClan?.Leader?.StringId ?? "" };
                case "workshop": return new JObject { ["owner"] = ReignObjectResolver.FindWorkshop(id)?.Owner?.StringId ?? "" };
                case "ship":
                    var owners = MobileParty.All.Where(p => p.Ships.Any(s => ShipId(s) == id)).ToList();
                    if (owners.Count != 1) throw new InvalidOperationException("The exact ship identity is missing or ambiguous.");
                    return new JObject { ["owner"] = owners[0].LeaderHero?.StringId ?? "" };
                default: throw new InvalidOperationException("Unknown asset.");
            }
        }
        private static JObject RosterRowState(TroopRosterElement row)
        {
            // An absent troop yields the default native struct. Its WoundedNumber
            // getter dereferences Character, so read it only for an existing row.
            return new JObject { ["count"] = row.Number, ["wounded"] = row.Character == null ? 0 : row.WoundedNumber, ["xp"] = row.Xp };
        }
        private static void RestoreDeliveryState(ActionClause c, JObject before, JObject saved)
        {
            var current = DeliveryState(c, saved); var to = Receiver(c); string id = ReignActionService.Arg(c, "assetId");
            switch (Kind(c))
            {
                case "gold": to.ChangeHeroGold(before.Value<int>("count") - current.Value<int>("count")); break;
                case "item": ChangeItem(to, Element(c), before.Value<int>("count") - current.Value<int>("count"), saved); break;
                case "troop":
                case "prisoner":
                    if (Troop(c).IsHero && current.Value<int>("count") > before.Value<int>("count")) TransferPrisonerAction.Apply(Troop(c), SavedParty(saved, "recipient").Party, SavedParty(saved, "source").Party);
                    else SavedRoster(c, saved, "recipient").AddToCounts(Troop(c), before.Value<int>("count") - current.Value<int>("count"), false,
                        before.Value<int>("wounded") - current.Value<int>("wounded"), before.Value<int>("xp") - current.Value<int>("xp"));
                    break;
                case "settlement":
                    if (before.Value<string>("owner") != current.Value<string>("owner")) ChangeOwnerOfSettlementAction.ApplyByGift(ReignObjectResolver.FindSettlement(id), ReignActionService.HeroById(before.Value<string>("owner"))); break;
                case "workshop":
                    if (before.Value<string>("owner") != current.Value<string>("owner")) { var w = ReignObjectResolver.FindWorkshop(id); ChangeOwnerOfWorkshopAction.ApplyByWar(w, ReignActionService.HeroById(before.Value<string>("owner")), w.WorkshopType); } break;
                case "ship":
                    if (before.Value<string>("owner") != current.Value<string>("owner"))
                    { var ship = MobileParty.All.SelectMany(p => p.Ships).Single(s => ShipId(s) == id); ChangeShipOwnerAction.ApplyByTransferring(ReignActionService.HeroById(before.Value<string>("owner")).PartyBelongedTo.Party, ship); } break;
            }
            if (!JToken.DeepEquals(before, DeliveryState(c, saved))) throw new InvalidOperationException("The native asset could not be restored exactly.");
        }
    }
}
