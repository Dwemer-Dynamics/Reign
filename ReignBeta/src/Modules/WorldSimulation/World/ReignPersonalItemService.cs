using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json.Linq;
using ReignBeta.Campaign;
using ReignBeta.PartyAgency;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;
using TaleWorlds.ObjectSystem;

namespace ReignBeta.World
{
    // A hero's gift is distinct from their party's shared supplies. The native save
    // owns personal holdings and delivery receipts, including exact item modifiers.
    public sealed class ReignPersonalItemService : CampaignBehaviorBase
    {
        private JObject _state = EmptyState();
        private string _serialized = string.Empty;
        public static ReignPersonalItemService Instance { get; private set; }

        private static JObject EmptyState() => new JObject { ["version"] = 1, ["items"] = new JArray(), ["receipts"] = new JObject() };
        public override void RegisterEvents() { Instance = this; }
        public override void SyncData(IDataStore dataStore)
        {
            if (dataStore.IsSaving) _serialized = _state.ToString(Newtonsoft.Json.Formatting.None);
            dataStore.SyncData("_reign_personal_items_v1", ref _serialized);
            if (dataStore.IsLoading)
            {
                _state = string.IsNullOrWhiteSpace(_serialized) ? EmptyState() : JObject.Parse(_serialized);
                if (_state.Value<int?>("version") != 1 || !(_state["items"] is JArray) || !(_state["receipts"] is JObject))
                    throw new InvalidDataException("The Reign personal item save payload has an unsupported schema.");
            }
        }

        public JArray Snapshot(Hero hero)
        {
            var result = new JArray();
            if (hero == null) return result;
            foreach (JObject stored in ((JArray)_state["items"]).OfType<JObject>().Where(x => x.Value<string>("heroId") == hero.StringId))
            {
                var row = (JObject)stored.DeepClone();
                ItemObject item = ReignObjectResolver.FindItem(row.Value<string>("itemId"));
                row["name"] = item?.Name?.ToString() ?? row.Value<string>("itemId");
                row["source"] = "personal";
                result.Add(row);
            }
            return result;
        }

        private sealed class Plan
        {
            internal Hero From, To;
            internal MobileParty FromParty, ToParty;
            internal Equipment SourceEquipment, DestinationEquipment;
            internal EquipmentIndex SourceSlot, DestinationSlot;
            internal EquipmentElement Element;
            internal string Source, Delivery;
            internal int Amount;
        }

        private static string Fingerprint(ReignWorldActionRecord action)
        {
            using (var hash = SHA256.Create()) return Convert.ToBase64String(hash.ComputeHash(Encoding.UTF8.GetBytes(
                action.ActorHeroStringId + "|" + action.TargetHeroStringId + "|" + action.TermsJson)));
        }

        public bool Validate(ReignWorldActionRecord action, Hero actor, Hero target, out string reason)
        {
            reason = string.Empty;
            if (action == null || string.IsNullOrWhiteSpace(action.ActionId))
            { reason = "Item delivery requires a durable action ID."; return false; }
            if (((JObject)_state["receipts"])[action.ActionId] is JObject prior)
            {
                if (prior.Value<string>("fingerprint") == Fingerprint(action)) return true;
                reason = "An item delivery ID cannot be reused with different terms.";
                return false;
            }
            return TryPlan(action, actor, target, out Plan _, out reason);
        }

        private bool TryPlan(ReignWorldActionRecord action, Hero actor, Hero target, out Plan plan, out string reason)
        {
            plan = null;
            reason = string.Empty;
            JObject terms;
            try { terms = string.IsNullOrWhiteSpace(action?.TermsJson) ? new JObject() : JObject.Parse(action.TermsJson); }
            catch (Exception) { reason = "Item transfer terms must be valid JSON."; return false; }
            string fromId = terms.Value<string>("fromHeroStringId");
            string toId = terms.Value<string>("toHeroStringId");
            Hero from = string.IsNullOrWhiteSpace(fromId) ? actor : ReignObjectResolver.FindHero(fromId);
            Hero to = string.IsNullOrWhiteSpace(toId) ? target ?? Hero.MainHero : ReignObjectResolver.FindHero(toId);
            string itemId = terms.Value<string>("itemId") ?? terms.Value<string>("item");
            ItemObject item = ReignObjectResolver.FindItem(itemId);
            JToken quantity = terms["amount"] ?? terms["Amount"];
            if (quantity != null && (quantity.Type != JTokenType.Integer || quantity.Value<long>() <= 0 || quantity.Value<long>() > int.MaxValue))
            { reason = "Item quantity must be an exact positive integer."; return false; }
            int amount = quantity?.Value<int>() ?? 1;
            if (from == null || to == null || from == to || !from.IsAlive || !to.IsAlive || item == null || amount <= 0)
            { reason = "Item transfer requires distinct living heroes, a resolved item and a positive quantity."; return false; }
            var p = new Plan { From = from, To = to, Amount = amount,
                FromParty = from == Hero.MainHero ? MobileParty.MainParty : from.PartyBelongedTo,
                ToParty = to == Hero.MainHero ? MobileParty.MainParty : to.PartyBelongedTo };
            p.Delivery = (terms.Value<string>("delivery") ?? "").Trim().ToLowerInvariant();
            if (p.Delivery.Length == 0) p.Delivery = to == Hero.MainHero ? "inventory"
                : p.ToParty == null || p.FromParty == p.ToParty ? "personal" : "inventory";
            if (p.Delivery == "personal" && to == Hero.MainHero) p.Delivery = "inventory";
            if (p.Delivery != "personal" && p.Delivery != "equip" && p.Delivery != "inventory")
            { reason = "Item delivery must be personal, equip or inventory."; return false; }
            if (p.Delivery == "inventory" && p.ToParty == null)
            { reason = "Inventory delivery requires a recipient party."; return false; }

            string modifier = terms.Value<string>("itemModifierId");
            bool Match(EquipmentElement element) => element.Item == item && (modifier == null
                || string.Equals(element.ItemModifier?.StringId ?? "", modifier, StringComparison.Ordinal));
            bool explicitEquipment = !string.IsNullOrWhiteSpace(terms.Value<string>("sourceEquipmentSlot"))
                || !string.IsNullOrWhiteSpace(terms.Value<string>("sourceEquipmentSet"));
            var personal = Snapshot(from).OfType<JObject>().Where(x => x.Value<string>("itemId") == item.StringId
                && x.Value<int>("amount") >= amount && (modifier == null || x.Value<string>("itemModifierId") == modifier)).ToList();
            if (!explicitEquipment && personal.Count == 1)
            {
                string storedModifier = personal[0].Value<string>("itemModifierId") ?? "";
                ItemModifier resolved = storedModifier.Length == 0 ? null : MBObjectManager.Instance.GetObject<ItemModifier>(storedModifier);
                if (storedModifier.Length != 0 && resolved == null)
                { reason = "The personal item's saved modifier is unavailable; its holding has been preserved."; return false; }
                p.Element = new EquipmentElement(item, resolved);
                p.Source = "personal";
            }
            else if (!explicitEquipment && personal.Count > 1)
            { reason = "Specify which modifier of the personal item to give."; return false; }
            if (p.Source == null && !explicitEquipment && p.FromParty != null
                && (from == Hero.MainHero || p.FromParty.LeaderHero == from))
            {
                var stacks = p.FromParty.ItemRoster.Where(x => Match(x.EquipmentElement) && x.Amount >= amount).ToList();
                if (stacks.Count > 1) { reason = "Specify which item modifier to transfer."; return false; }
                if (stacks.Count == 1) { p.Element = stacks[0].EquipmentElement; p.Source = "inventory"; }
            }
            if (p.Source == null && amount == 1)
            {
                string wantedSet = (terms.Value<string>("sourceEquipmentSet") ?? "").ToLowerInvariant();
                string wantedSlot = terms.Value<string>("sourceEquipmentSlot") ?? "";
                if (wantedSet.Length > 0 && wantedSet != "battle" && wantedSet != "civilian")
                { reason = "Source equipment set must be battle or civilian."; return false; }
                var equipped = new List<Tuple<Equipment, EquipmentIndex, EquipmentElement>>();
                foreach (string set in new[] { "battle", "civilian" })
                {
                    if (wantedSet.Length > 0 && wantedSet != set) continue;
                    Equipment equipment = set == "battle" ? from.BattleEquipment : from.CivilianEquipment;
                    if (equipment == null) continue;
                    for (int i = 0; i < (int)EquipmentIndex.NumEquipmentSetSlots; i++)
                    {
                        EquipmentIndex slot = (EquipmentIndex)i;
                        if (wantedSlot.Length > 0 && (!Enum.TryParse(wantedSlot, true, out EquipmentIndex requested) || requested != slot)) continue;
                        if (Match(equipment[slot])) equipped.Add(Tuple.Create(equipment, slot, equipment[slot]));
                    }
                }
                if (equipped.Count > 1)
                { reason = "Specify which equipped copy and equipment set to transfer."; return false; }
                if (equipped.Count == 1)
                { p.Source = "equipment"; p.SourceEquipment = equipped[0].Item1; p.SourceSlot = equipped[0].Item2; p.Element = equipped[0].Item3; }
            }
            if (p.Source == null)
            { reason = from.Name + " does not personally own the requested item or control enough of it in their party inventory."; return false; }
            if (p.Source == "inventory" && p.Delivery == "inventory" && p.FromParty == p.ToParty)
            { reason = "A shared-party gift needs personal ownership or equipment delivery."; return false; }
            if (p.Source == "equipment" && ReignTemporaryPartyGuestCampaignBehavior.Instance?.IsTemporaryGuestEquipmentLocked(from) == true
                && !(terms.Value<bool?>("sourceConsentConfirmed") == true && action.AcceptedByHeroStringId == from.StringId))
            { reason = "Transferring a temporary guest's equipment requires that guest's explicit consent."; return false; }

            if (p.Delivery == "equip")
            {
                if (amount != 1) { reason = "Equipment delivery requires exactly one item."; return false; }
                if (to != Hero.MainHero && !(terms.Value<bool?>("recipientConsentConfirmed") == true && action.AcceptedByHeroStringId == to.StringId))
                { reason = "Equipping a gift requires the receiving hero's explicit acceptance."; return false; }
                string set = (terms.Value<string>("targetEquipmentSet") ?? "battle").ToLowerInvariant();
                if (set != "battle" && set != "civilian") { reason = "Target equipment set must be battle or civilian."; return false; }
                p.DestinationEquipment = set == "civilian" ? to.CivilianEquipment : to.BattleEquipment;
                if (p.DestinationEquipment == null) { reason = "The recipient's equipment set is unavailable."; return false; }
                string slotText = terms.Value<string>("targetEquipmentSlot") ?? "";
                if (slotText.Length > 0)
                {
                    if (!Enum.TryParse(slotText, true, out p.DestinationSlot) || (int)p.DestinationSlot < 0
                        || (int)p.DestinationSlot >= (int)EquipmentIndex.NumEquipmentSetSlots
                        || !Equipment.IsItemFitsToSlot(p.DestinationSlot, item))
                    { reason = "The gift does not fit the requested equipment slot."; return false; }
                }
                else
                {
                    var slots = Enumerable.Range(0, (int)EquipmentIndex.NumEquipmentSetSlots).Select(x => (EquipmentIndex)x)
                        .Where(x => Equipment.IsItemFitsToSlot(x, item)).ToList();
                    if (slots.Count == 0) { reason = "This item cannot be equipped."; return false; }
                    var empty = slots.Where(x => p.DestinationEquipment[x].IsEmpty).ToList();
                    if (slots.Count > 1 && empty.Count == 0)
                    { reason = "Choose the weapon slot to replace; the recipient's equipment will be preserved."; return false; }
                    p.DestinationSlot = empty.Count > 0 ? empty[0] : slots[0];
                }
                if (p.DestinationEquipment == null) { reason = "The recipient's equipment set is unavailable."; return false; }
            }
            plan = p;
            return true;
        }

        private static int Count(ItemRoster roster, EquipmentElement element)
        {
            int index = roster.FindIndexOfElement(element);
            return index < 0 ? 0 : roster.GetElementNumber(index);
        }

        private int PersonalCount(Hero hero, EquipmentElement element)
            => PersonalRow(hero, element)?.Value<int>("amount") ?? 0;
        private JObject PersonalRow(Hero hero, EquipmentElement element)
            => ((JArray)_state["items"]).OfType<JObject>().FirstOrDefault(x => x.Value<string>("heroId") == hero.StringId
                && x.Value<string>("itemId") == element.Item.StringId
                && (x.Value<string>("itemModifierId") ?? "") == (element.ItemModifier?.StringId ?? ""));
        private void ChangePersonal(Hero hero, EquipmentElement element, int delta)
        {
            JObject row = PersonalRow(hero, element);
            int next = checked((row?.Value<int>("amount") ?? 0) + delta);
            if (next < 0) throw new InvalidOperationException("Personal item balance would become negative.");
            if (next == 0) { row?.Remove(); return; }
            if (row == null)
            {
                row = new JObject { ["heroId"] = hero.StringId, ["itemId"] = element.Item.StringId,
                    ["itemModifierId"] = element.ItemModifier?.StringId ?? "" };
                ((JArray)_state["items"]).Add(row);
            }
            row["amount"] = next;
        }

        public ReignActionResult Transfer(ReignWorldActionRecord action, Hero actor, Hero target)
        {
            string fingerprint = Fingerprint(action);
            var receipts = (JObject)_state["receipts"];
            if (receipts[action.ActionId] is JObject prior)
                return prior.Value<string>("fingerprint") == fingerprint
                    ? ReignActionResult.NoOp("This exact item delivery already completed.").WithResultCode("item_already_delivered")
                    : ReignActionResult.ValidationFailed("An item delivery ID cannot be reused with different terms.");
            if (!TryPlan(action, actor, target, out Plan p, out string reason)) return ReignActionResult.ValidationFailed(reason);
            JObject stateBefore = (JObject)_state.DeepClone();
            var rosterCopies = new Dictionary<ItemRoster, ItemRoster>();
            foreach (var party in new[] { p.FromParty, p.ToParty }.Where(x => x != null).Distinct())
                rosterCopies[party.ItemRoster] = new ItemRoster(party.ItemRoster);
            EquipmentElement sourceBefore = p.SourceEquipment == null ? default : p.SourceEquipment[p.SourceSlot];
            EquipmentElement destinationBefore = p.DestinationEquipment == null ? default : p.DestinationEquipment[p.DestinationSlot];
            int sourceCount = p.Source == "inventory" ? Count(p.FromParty.ItemRoster, p.Element)
                : p.Source == "personal" ? PersonalCount(p.From, p.Element) : 1;
            int destinationCount = p.Delivery == "inventory" ? Count(p.ToParty.ItemRoster, p.Element)
                : PersonalCount(p.To, p.Element);
            try
            {
                if (p.Source == "inventory") p.FromParty.ItemRoster.AddToCounts(p.Element, -p.Amount);
                else if (p.Source == "personal") ChangePersonal(p.From, p.Element, -p.Amount);
                else p.SourceEquipment[p.SourceSlot] = default;
                if (p.Delivery == "inventory") p.ToParty.ItemRoster.AddToCounts(p.Element, p.Amount);
                else if (p.Delivery == "personal") ChangePersonal(p.To, p.Element, p.Amount);
                else
                {
                    if (!destinationBefore.IsEmpty) ChangePersonal(p.To, destinationBefore, 1);
                    p.DestinationEquipment[p.DestinationSlot] = p.Element;
                }
                int remaining = p.Source == "inventory" ? Count(p.FromParty.ItemRoster, p.Element)
                    : p.Source == "personal" ? PersonalCount(p.From, p.Element) : p.SourceEquipment[p.SourceSlot].IsEmpty ? 0 : 1;
                bool received = p.Delivery == "inventory" ? Count(p.ToParty.ItemRoster, p.Element) == destinationCount + p.Amount
                    : p.Delivery == "personal" ? PersonalCount(p.To, p.Element) == destinationCount + p.Amount
                    : p.DestinationEquipment[p.DestinationSlot].Equals(p.Element);
                if (remaining != sourceCount - p.Amount || !received) throw new InvalidOperationException("Native item delivery failed conservation verification.");
                RefreshActiveEquipment(p.From, p.SourceEquipment);
                RefreshActiveEquipment(p.To, p.DestinationEquipment);
                receipts[action.ActionId] = new JObject { ["fingerprint"] = fingerprint, ["toHeroId"] = p.To.StringId,
                    ["itemId"] = p.Element.Item.StringId, ["amount"] = p.Amount, ["delivery"] = p.Delivery };
                return ReignActionResult.Done(p.From.Name + " gave " + p.Amount + " " + p.Element.Item.Name + " to " + p.To.Name + ".")
                    .WithResultCode("item_delivery_verified")
                    .WithEffect(p.Delivery == "equip" ? "item_equipped" : "personal_item_transfer", "item", p.Element.Item.StringId,
                        p.Element.Item.Name.ToString(), "from=" + p.From.StringId + ";to=" + p.To.StringId + ";amount=" + p.Amount
                        + ";delivery=" + p.Delivery + ";modifier=" + (p.Element.ItemModifier?.StringId ?? ""))
                    .WithChangedEntity("hero", p.From.StringId, p.From.Name.ToString(), "items_changed")
                    .WithChangedEntity("hero", p.To.StringId, p.To.Name.ToString(), p.Delivery == "equip" ? "equipment_changed" : "personal_items_changed");
            }
            catch (Exception ex)
            {
                _state = stateBefore;
                try
                {
                    foreach (var pair in rosterCopies) { pair.Key.Clear(); pair.Key.Add(pair.Value); }
                    if (p.SourceEquipment != null) p.SourceEquipment[p.SourceSlot] = sourceBefore;
                    if (p.DestinationEquipment != null) p.DestinationEquipment[p.DestinationSlot] = destinationBefore;
                    if (rosterCopies.Any(x => !ItemRoster.RostersAreIdentical(x.Key, x.Value))
                        || (p.SourceEquipment != null && !p.SourceEquipment[p.SourceSlot].Equals(sourceBefore))
                        || (p.DestinationEquipment != null && !p.DestinationEquipment[p.DestinationSlot].Equals(destinationBefore)))
                        throw new InvalidOperationException("Restored item rosters or equipment do not match.");
                    RefreshActiveEquipment(p.From, p.SourceEquipment);
                    RefreshActiveEquipment(p.To, p.DestinationEquipment);
                }
                catch (Exception rollback)
                { return ReignActionResult.FailTerminal(ex.Message + " Rollback failed: " + rollback.Message, "item_rollback_failed", "state_verification"); }
                return ReignActionResult.FailTerminal(ex.Message + " The transfer was rolled back.", "item_delivery_rolled_back", "state_verification");
            }
        }

        private static void RefreshActiveEquipment(Hero hero, Equipment changed)
        {
            if (changed == null || hero == null || Mission.Current == null) return;
            foreach (Agent agent in Mission.Current.Agents.Where(x => x.IsActive() && x.IsHuman
                && (x.Character == hero.CharacterObject
                    || ReignEncounteredResidentsCampaignBehavior.Instance?.ResolveAgent(x) == hero)).ToList())
            {
                // Keep battle/civilian sets distinct. Update only the set currently
                // worn in this scene, including custom resident mission agents.
                if (agent.SpawnEquipment == null || agent.SpawnEquipment.IsCivilian != changed.IsCivilian) continue;
                agent.UpdateSpawnEquipmentAndRefreshVisuals(new Equipment(changed));
                for (int i = 0; i < (int)EquipmentIndex.NumEquipmentSetSlots; i++)
                    if (!agent.SpawnEquipment[(EquipmentIndex)i].Equals(changed[(EquipmentIndex)i]))
                        throw new InvalidOperationException("The active agent did not retain the changed equipment.");
            }
        }
    }
}
