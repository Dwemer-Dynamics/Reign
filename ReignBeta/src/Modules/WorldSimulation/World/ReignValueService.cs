using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;
using ReignBeta.PartyAgency;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Naval;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.Settlements.Workshops;
using TaleWorlds.Core;

namespace ReignBeta.World
{
    public sealed class ReignTradeTermQuote
    {
        public string AssetType = string.Empty;
        public string AssetId = string.Empty;
        public string AssetName = string.Empty;
        public string FromId = string.Empty;
        public string ToId = string.Empty;
        public int Amount;
        public int UnitValue;
        public int TotalValue;
        public string ValueSource = string.Empty;
        public string Side = string.Empty;
    }

    public sealed class ReignTradeAppraisal
    {
        public readonly List<ReignTradeTermQuote> Quotes = new List<ReignTradeTermQuote>();
        public int PlayerOfferValue;
        public int NpcOfferValue;
        public int NetForNpc;
        public double FairnessRatio;
        public int RecommendedCounter;
        public bool IsFair;
        public bool BypassedFairness;
        public string Summary = string.Empty;
    }

    public static class ReignValueService
    {
        public static bool ValidateTradePackage(ReignWorldActionRecord action, out string reason, out ReignTradeAppraisal appraisal)
        {
            reason = string.Empty;
            JObject terms = ParseTerms(action);
            if (!ValidateSettlementTradeAuthority(action, terms, out reason))
            {
                appraisal = new ReignTradeAppraisal();
                return false;
            }
            if (!ValidateMaterialTransfers(action, out reason))
            {
                appraisal = new ReignTradeAppraisal();
                return false;
            }

            appraisal = BuildTradeAppraisal(action, out reason);
            if (!string.IsNullOrWhiteSpace(reason))
            {
                return false;
            }

            if (appraisal.Quotes.Count == 0)
            {
                reason = "Trade package requires at least one concrete gold, item, ship, settlement, workshop, or prisoner term.";
                return false;
            }

            if (!appraisal.BypassedFairness && !appraisal.IsFair)
            {
                reason = "Trade package is unfair: player offer " + appraisal.PlayerOfferValue.ToString(CultureInfo.InvariantCulture)
                    + " vs requested value " + appraisal.NpcOfferValue.ToString(CultureInfo.InvariantCulture)
                    + ". Recommended counter is at least " + appraisal.RecommendedCounter.ToString(CultureInfo.InvariantCulture) + " denars.";
                return false;
            }

            return true;
        }

        public static bool ValidateSettlementTradeAuthority(ReignWorldActionRecord action,
            JObject terms, out string reason)
        {
            reason = string.Empty;
            List<string> settlementIds = ReadSettlementIds(action, terms);
            if (settlementIds.Count == 0)
            {
                return true;
            }

            if (HasNonGoldSettlementPackageAsset(terms))
            {
                reason = "Settlement trades currently support settlement-and-gold terms only; items, workshops, prisoners, and ships require a separately atomic package.";
                return false;
            }

            Hero recipient = ResolveHero(ReadStringTerm(terms,
                "settlementToHeroStringId", ReadStringTerm(terms, "toHeroStringId",
                    action?.TargetHeroStringId))) ?? Hero.MainHero;
            Hero declaredOwner = ResolveHero(ReadStringTerm(terms,
                "settlementFromHeroStringId", ReadStringTerm(terms,
                    "assetFromHeroStringId", ReadStringTerm(terms, "fromHeroStringId",
                        action?.ActorHeroStringId))));
            if (recipient?.Clan == null || declaredOwner == null)
            {
                reason = "Settlement trade requires exact source and receiving heroes with clans.";
                return false;
            }
            if (recipient == declaredOwner)
            {
                reason = "Settlement trade source and recipient cannot be the same hero.";
                return false;
            }

            bool dialogueAuthorized = string.Equals(action?.AuthorizationMode,
                "dialogue_acceptance", StringComparison.OrdinalIgnoreCase);
            Hero acceptedBy = ResolveHero(action?.AcceptedByHeroStringId);
            if (dialogueAuthorized)
            {
                bool exactParticipants = acceptedBy != null && Hero.MainHero != null
                    && ((declaredOwner == acceptedBy && recipient == Hero.MainHero)
                        || (declaredOwner == Hero.MainHero && recipient == acceptedBy));
                if (!exactParticipants)
                {
                    reason = "The settlement source and recipient do not match the player and the NPC who accepted the trade.";
                    return false;
                }
                if (!string.Equals(ReadStringTerm(terms, "settlementAuthorityKind", ""),
                    "owner_clan_leader", StringComparison.OrdinalIgnoreCase))
                {
                    reason = "Dialogue settlement trade is not bound to owner-clan-leader authority.";
                    return false;
                }
            }

            string boundClanId = ReadStringTerm(terms, "settlementFromClanStringId", "");
            string authorizerId = ReadStringTerm(terms,
                "settlementAuthorizingHeroStringId", "");
            Clan packageOwnerClan = null;
            foreach (string settlementId in settlementIds)
            {
                Settlement settlement = ReignObjectResolver.FindSettlement(settlementId);
                Clan ownerClan = settlement?.OwnerClan;
                Hero ownerLeader = ownerClan?.Leader;
                if (settlement == null || !settlement.IsFortification)
                {
                    reason = "Only a current town or castle can be transferred in a settlement trade: "
                        + settlementId;
                    return false;
                }
                if (ownerClan == null || ownerLeader == null || ownerLeader.IsDead)
                {
                    reason = "Settlement has no live owner-clan leader authorized to transfer it: "
                        + settlement.Name;
                    return false;
                }
                if (ownerLeader != declaredOwner)
                {
                    reason = "Only the current owner-clan leader may voluntarily transfer "
                        + settlement.Name + ".";
                    return false;
                }
                if (packageOwnerClan != null && packageOwnerClan != ownerClan)
                {
                    reason = "A single settlement trade cannot combine fiefs owned by different clans.";
                    return false;
                }
                if (recipient.Clan == ownerClan)
                {
                    reason = settlement.Name + " is already owned by the receiving clan.";
                    return false;
                }
                if (dialogueAuthorized
                    && (!string.Equals(boundClanId, ownerClan.StringId,
                            StringComparison.OrdinalIgnoreCase)
                        || !string.Equals(authorizerId, ownerLeader.StringId,
                            StringComparison.OrdinalIgnoreCase)))
                {
                    reason = "Settlement ownership changed after the dialogue agreement; fresh consent is required.";
                    return false;
                }
                packageOwnerClan = ownerClan;
            }

            reason = string.Empty;
            return true;
        }

        public static List<string> ReadSettlementIds(ReignWorldActionRecord action,
            JObject terms)
        {
            List<string> settlementIds = ReadStringListTerm(terms, "settlementIds");
            if (!string.IsNullOrWhiteSpace(action?.TargetSettlementStringId)
                && !settlementIds.Contains(action.TargetSettlementStringId,
                    StringComparer.OrdinalIgnoreCase))
            {
                settlementIds.Insert(0, action.TargetSettlementStringId);
            }
            string termSettlement = ReadStringTerm(terms, "targetSettlementStringId", "");
            if (!string.IsNullOrWhiteSpace(termSettlement)
                && !settlementIds.Contains(termSettlement,
                    StringComparer.OrdinalIgnoreCase))
            {
                settlementIds.Add(termSettlement);
            }
            return settlementIds.Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static bool HasNonGoldSettlementPackageAsset(JObject terms)
        {
            string assetText = string.Join(" ", new[]
            {
                ReadStringTerm(terms, "assetClass", ""),
                ReadStringTerm(terms, "asset", ""),
                ReadStringTerm(terms, "assetName", ""),
                ReadStringTerm(terms, "packageText", "")
            }).ToLowerInvariant();
            return ReadItemTermObjects(terms).Count > 0
                || !string.IsNullOrWhiteSpace(ReadStringTerm(terms, "itemId", ""))
                || !string.IsNullOrWhiteSpace(ReadStringTerm(terms, "item", ""))
                || !string.IsNullOrWhiteSpace(ReadStringTerm(terms, "workshopId",
                    ReadStringTerm(terms, "workshop", "")))
                || !string.IsNullOrWhiteSpace(ReadStringTerm(terms,
                    "prisonerHeroStringId", ReadStringTerm(terms, "prisonerHero", "")))
                || ReadIntTerm(terms, "shipCount", 0) > 0
                || assetText.Contains("ship") || assetText.Contains("fleet")
                || assetText.Contains("vessel") || assetText.Contains("boat")
                || assetText.Contains("naval");
        }

        public static bool ValidateMaterialTransfers(ReignWorldActionRecord action, out string reason)
        {
            reason = string.Empty;
            JObject terms = ParseTerms(action);
            ReignTradeAppraisal scratch = new ReignTradeAppraisal();
            if (!TryAddGoldQuote(action, terms, scratch, out reason))
            {
                return false;
            }

            List<JObject> itemTerms = ReadItemTermObjects(terms);
            if (itemTerms.Count == 0)
            {
                if (!TryAddItemQuote(action, terms, scratch, out reason))
                {
                    return false;
                }

                string itemId = ReadStringTerm(terms, "itemId", ReadStringTerm(terms, "item", string.Empty));
                if (!string.IsNullOrWhiteSpace(itemId))
                {
                    itemTerms.Add(terms);
                }
            }
            else if (!TryAddItemListQuotes(action, terms, scratch, out reason))
            {
                return false;
            }

            foreach (IGrouping<string, JObject> group in itemTerms.GroupBy(x =>
            {
                Hero from = ResolveItemHero(action, terms, x, true);
                ItemObject item = ReignObjectResolver.FindItem(ReadStringTerm(x, "itemId", ReadStringTerm(x, "item", string.Empty)));
                return (from?.StringId ?? string.Empty) + "|" + (item?.StringId ?? string.Empty);
            }, StringComparer.OrdinalIgnoreCase))
            {
                JObject firstTerm = group.First();
                Hero from = ResolveItemHero(action, terms, firstTerm, true);
                ItemObject item = ReignObjectResolver.FindItem(ReadStringTerm(firstTerm, "itemId", ReadStringTerm(firstTerm, "item", string.Empty)));
                MobileParty fromParty = PartyForInventory(from);
                int promised = group.Sum(x => ReadIntTerm(x, "amount", ReadIntTerm(x, "Amount", 1)));
                int available = fromParty?.ItemRoster?.GetItemNumber(item) ?? 0;
                if (available >= promised)
                {
                    continue;
                }

                int equippedNeeded = promised - available;
                bool equipmentLocked = ReignTemporaryPartyGuestCampaignBehavior.Instance
                    ?.IsTemporaryGuestEquipmentLocked(from) == true;
                bool hasEquipped = !equipmentLocked && equippedNeeded == 1 && group.Any(x => ReignObjectResolver.TryFindEquippedItem(
                    from,
                    item?.StringId ?? string.Empty,
                    ReadStringTerm(x, "sourceEquipmentSlot", string.Empty),
                    ReadStringTerm(x, "sourceEquipmentSet", string.Empty),
                    out Equipment _,
                    out EquipmentIndex _,
                    out EquipmentElement _,
                    out string _));
                if (!hasEquipped)
                {
                    reason = (from?.Name?.ToString() ?? "Transfer source") + " has " + available + " "
                        + (item?.Name?.ToString() ?? "items") + " but the combined agreement promises " + promised + ".";
                    return false;
                }
            }

            return true;
        }

        public static ReignTradeAppraisal BuildTradeAppraisal(ReignWorldActionRecord action, out string reason)
        {
            reason = string.Empty;
            ReignTradeAppraisal appraisal = new ReignTradeAppraisal();
            JObject terms = ParseTerms(action);
            appraisal.BypassedFairness = IsFairnessBypassed(action, terms);

            if (!TryAddGoldQuote(action, terms, appraisal, out reason)) return appraisal;
            if (ReadItemTermObjects(terms).Count == 0)
            {
                if (!TryAddItemQuote(action, terms, appraisal, out reason)) return appraisal;
            }
            else if (!TryAddItemListQuotes(action, terms, appraisal, out reason)) return appraisal;
            if (!TryAddSettlementQuotes(action, terms, appraisal, out reason)) return appraisal;
            if (!TryAddWorkshopQuote(action, terms, appraisal, out reason)) return appraisal;
            if (!TryAddPrisonerQuote(action, terms, appraisal, out reason)) return appraisal;
            if (!TryAddShipQuotes(action, terms, appraisal, out reason)) return appraisal;

            appraisal.NetForNpc = appraisal.PlayerOfferValue - appraisal.NpcOfferValue;
            appraisal.FairnessRatio = appraisal.NpcOfferValue <= 0
                ? 1d
                : (double)appraisal.PlayerOfferValue / Math.Max(1, appraisal.NpcOfferValue);
            double threshold = FairnessThreshold(action, terms);
            appraisal.RecommendedCounter = Math.Max(0, (int)Math.Ceiling(appraisal.NpcOfferValue * threshold) - appraisal.PlayerOfferValue);
            appraisal.IsFair = appraisal.NpcOfferValue <= 0 || appraisal.PlayerOfferValue >= (int)Math.Ceiling(appraisal.NpcOfferValue * threshold);
            appraisal.Summary = "playerOffer=" + appraisal.PlayerOfferValue.ToString(CultureInfo.InvariantCulture)
                + ";npcOffer=" + appraisal.NpcOfferValue.ToString(CultureInfo.InvariantCulture)
                + ";ratio=" + appraisal.FairnessRatio.ToString("0.00", CultureInfo.InvariantCulture)
                + ";recommendedCounter=" + appraisal.RecommendedCounter.ToString(CultureInfo.InvariantCulture)
                + ";bypass=" + appraisal.BypassedFairness;
            return appraisal;
        }

        public static int EstimateItemValue(ItemObject item, EquipmentElement element, int amount, MobileParty tradingParty, out string source)
        {
            source = "item_value";
            if (item == null)
            {
                return 0;
            }

            int unitValue = item.Value;
            try
            {
                Settlement settlement = tradingParty?.CurrentSettlement ?? MobileParty.MainParty?.CurrentSettlement;
                if (settlement?.SettlementComponent != null)
                {
                    int settlementPrice = !element.IsEmpty
                        ? settlement.SettlementComponent.GetItemPrice(element, tradingParty)
                        : settlement.SettlementComponent.GetItemPrice(item, tradingParty);
                    if (settlementPrice > 0)
                    {
                        unitValue = settlementPrice;
                        source = "current_settlement_market_price";
                    }
                }
                else
                {
                    List<int> prices = Town.AllTowns
                        .Where(x => x != null)
                        .Select(x => !element.IsEmpty ? x.GetItemPrice(element, tradingParty) : x.GetItemPrice(item, tradingParty))
                        .Where(x => x > 0)
                        .Take(12)
                        .ToList();
                    if (prices.Count > 0)
                    {
                        unitValue = (int)Math.Round(prices.Average());
                        source = "average_town_market_price";
                    }
                }
            }
            catch
            {
                unitValue = item.Value;
                source = "item_value_fallback";
            }

            return Math.Max(1, unitValue) * Math.Max(1, amount);
        }

        public static int EstimateSettlementValue(Settlement settlement, IFaction buyerFaction, IFaction sellerFaction, out string source)
        {
            source = "settlement_value_model";
            if (settlement == null)
            {
                return 0;
            }

            try
            {
                float buyerValue = buyerFaction == null ? 0f : global::TaleWorlds.CampaignSystem.Campaign.Current.Models.SettlementValueModel.CalculateSettlementValueForFaction(settlement, buyerFaction);
                float sellerValue = sellerFaction == null ? 0f : global::TaleWorlds.CampaignSystem.Campaign.Current.Models.SettlementValueModel.CalculateSettlementValueForFaction(settlement, sellerFaction);
                float selected = Math.Max(Math.Abs(buyerValue), Math.Abs(sellerValue));
                if (selected > 0f && !float.IsInfinity(selected) && !float.IsNaN(selected))
                {
                    return Math.Max(1, (int)Math.Round(selected));
                }
            }
            catch
            {
                source = "settlement_formula_fallback";
            }

            int prosperity = settlement.Town == null ? 0 : (int)Math.Round(settlement.Town.Prosperity);
            int baseValue = settlement.IsTown ? 250000 : settlement.IsCastle ? 120000 : 25000;
            return Math.Max(1, baseValue + prosperity * 40);
        }

        public static int EstimateWorkshopValue(Workshop workshop, out string source)
        {
            source = "workshop_model_average";
            if (workshop == null)
            {
                return 0;
            }

            try
            {
                int playerCost = global::TaleWorlds.CampaignSystem.Campaign.Current.Models.WorkshopModel.GetCostForPlayer(workshop);
                int notableCost = global::TaleWorlds.CampaignSystem.Campaign.Current.Models.WorkshopModel.GetCostForNotable(workshop);
                int selected = Math.Max(playerCost, notableCost);
                if (selected > 0)
                {
                    return selected;
                }
            }
            catch
            {
                source = "workshop_formula_fallback";
            }

            return 15000;
        }

        public static int EstimatePrisonerValue(Hero prisoner, out string source)
        {
            source = "prisoner_rank_formula";
            if (prisoner == null)
            {
                return 0;
            }

            int level = prisoner.CharacterObject?.Level ?? 1;
            int clanTier = prisoner.Clan?.Tier ?? 0;
            int value = 1000 + level * 150 + clanTier * 2500;
            if (prisoner.IsLord) value += 8000;
            if (prisoner.Clan?.Kingdom?.Leader == prisoner) value += 25000;
            return Math.Max(1000, value);
        }

        public static int EstimateShipValue(Ship ship, PartyBase seller, PartyBase buyer, out string source)
        {
            source = "ship_cost_model";
            if (ship == null)
            {
                return 0;
            }

            try
            {
                float modelValue = global::TaleWorlds.CampaignSystem.Campaign.Current.Models.ShipCostModel.GetShipTradeValue(ship, seller, buyer);
                if (modelValue > 0f && !float.IsInfinity(modelValue) && !float.IsNaN(modelValue))
                {
                    return Math.Max(1, (int)Math.Round(modelValue));
                }
            }
            catch
            {
                source = "ship_hull_value_fallback";
            }

            source = "ship_hull_value_fallback";
            try
            {
                int capacityValue = ship.TotalCrewCapacity * 250
                    + ship.MainDeckCrewCapacity * 100
                    + ship.SkeletalCrewCapacity * 150
                    + ship.SeaWorthiness * 80
                    + (int)Math.Round(ship.InventoryCapacity / 4f);
                if (capacityValue > 0)
                {
                    return Math.Max(6000, capacityValue);
                }
            }
            catch
            {
                // Some modded hull objects may not expose all value fields.
            }

            return 12000;
        }

        public static JObject ParseTerms(ReignWorldActionRecord action)
        {
            if (action == null || string.IsNullOrWhiteSpace(action.TermsJson))
            {
                return new JObject();
            }

            try
            {
                return JObject.Parse(action.TermsJson);
            }
            catch
            {
                return new JObject();
            }
        }

        public static string ReadStringTerm(JObject terms, string key, string fallback = "")
        {
            JToken token = terms?[key];
            return token == null ? fallback : token.ToString();
        }

        public static int ReadIntTerm(JObject terms, string key, int fallback = 0)
        {
            string value = ReadStringTerm(terms, key, string.Empty);
            return int.TryParse(value, NumberStyles.Integer | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out int parsed)
                ? parsed
                : fallback;
        }

        public static bool ReadBoolTerm(JObject terms, string key, bool fallback = false)
        {
            string value = ReadStringTerm(terms, key, string.Empty);
            return bool.TryParse(value, out bool parsed) ? parsed : fallback;
        }

        public static List<string> ReadStringListTerm(JObject terms, string key)
        {
            List<string> result = new List<string>();
            JToken token = terms?[key];
            if (token is JArray array)
            {
                result.AddRange(array.Select(x => x.ToString()).Where(x => !string.IsNullOrWhiteSpace(x)));
                return result;
            }

            string value = token?.ToString() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(value))
            {
                result.AddRange(value.Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()));
            }

            return result;
        }

        public static List<JObject> ReadItemTermObjects(JObject terms)
        {
            JToken token = terms?["items"] ?? terms?["itemTerms"];
            if (token is JArray array)
            {
                return array.OfType<JObject>().ToList();
            }

            if (token is JObject obj)
            {
                return new List<JObject> { obj };
            }

            return new List<JObject>();
        }

        public static Hero ResolveHero(string id)
        {
            if (string.Equals(id, "player", StringComparison.OrdinalIgnoreCase))
            {
                return Hero.MainHero;
            }

            if (string.Equals(id, "speaker", StringComparison.OrdinalIgnoreCase) || string.Equals(id, "current_npc", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return ReignObjectResolver.FindHero(id);
        }

        public static MobileParty PartyForInventory(Hero hero)
        {
            if (hero == Hero.MainHero)
            {
                return MobileParty.MainParty;
            }

            return hero?.PartyBelongedTo;
        }

        private static Hero ResolveItemHero(ReignWorldActionRecord action, JObject terms, JObject itemTerm, bool source)
        {
            if (source)
            {
                return ResolveHero(ReadStringTerm(itemTerm, "fromHeroStringId",
                    ReadStringTerm(itemTerm, "itemFromHeroStringId",
                        ReadStringTerm(terms, "itemFromHeroStringId",
                            ReadStringTerm(terms, "assetFromHeroStringId",
                                ReadStringTerm(terms, "fromHeroStringId", action.ActorHeroStringId))))));
            }

            return ResolveHero(ReadStringTerm(itemTerm, "toHeroStringId",
                ReadStringTerm(itemTerm, "itemToHeroStringId",
                    ReadStringTerm(terms, "itemToHeroStringId",
                        ReadStringTerm(terms, "assetToHeroStringId",
                            ReadStringTerm(terms, "toHeroStringId", action.TargetHeroStringId))))));
        }

        public static MobileParty ResolveShipParty(Hero hero, Kingdom kingdom)
        {
            if (hero == Hero.MainHero)
            {
                return MobileParty.MainParty;
            }

            if (hero?.PartyBelongedTo?.Party != null)
            {
                return hero.PartyBelongedTo;
            }

            if (hero?.Clan != null)
            {
                MobileParty clanParty = MobileParty.All.FirstOrDefault(x => x != null && x.IsActive && x.ActualClan == hero.Clan && (x.Ships?.Count ?? 0) > 0);
                if (clanParty != null)
                {
                    return clanParty;
                }
            }

            return kingdom == null
                ? null
                : MobileParty.All.FirstOrDefault(x => x != null && x.IsActive && x.MapFaction == kingdom && (x.Ships?.Count ?? 0) > 0);
        }

        private static bool TryAddGoldQuote(ReignWorldActionRecord action, JObject terms, ReignTradeAppraisal appraisal, out string reason)
        {
            reason = string.Empty;
            int gold = ReadIntTerm(terms, "gold", ReadIntTerm(terms, "GoldAmount", ReadIntTerm(terms, "goldAmount", 0)));
            if (gold <= 0)
            {
                return true;
            }

            Hero from = ResolveHero(ReadStringTerm(terms, "goldFromHeroStringId", ReadStringTerm(terms, "fromHeroStringId", action.ActorHeroStringId)));
            Hero to = ResolveHero(ReadStringTerm(terms, "goldToHeroStringId", ReadStringTerm(terms, "toHeroStringId", action.TargetHeroStringId)));
            if (from == null || to == null)
            {
                reason = "Trade gold term requires valid FromHero and ToHero.";
                return false;
            }

            if (from == to)
            {
                reason = "Trade gold payer and receiver must be different heroes.";
                return false;
            }

            if (from.Gold < gold)
            {
                reason = from.Name + " does not have enough denars for this trade package.";
                return false;
            }

            AddQuote(appraisal, "gold", "denars", "Denars", from, to, gold, 1, gold, "currency");
            return true;
        }

        private static bool TryAddItemQuote(ReignWorldActionRecord action, JObject terms, ReignTradeAppraisal appraisal, out string reason)
        {
            reason = string.Empty;
            string itemId = ReadStringTerm(terms, "itemId", ReadStringTerm(terms, "item", string.Empty));
            if (string.IsNullOrWhiteSpace(itemId))
            {
                return true;
            }

            Hero from = ResolveHero(ReadStringTerm(terms, "itemFromHeroStringId", ReadStringTerm(terms, "assetFromHeroStringId", ReadStringTerm(terms, "fromHeroStringId", action.ActorHeroStringId))));
            Hero to = ResolveHero(ReadStringTerm(terms, "itemToHeroStringId", ReadStringTerm(terms, "assetToHeroStringId", ReadStringTerm(terms, "toHeroStringId", action.TargetHeroStringId))));
            MobileParty fromParty = PartyForInventory(from);
            MobileParty toParty = PartyForInventory(to);
            ItemObject item = ReignObjectResolver.FindItem(itemId);
            int amount = ReadIntTerm(terms, "amount", ReadIntTerm(terms, "Amount", 1));
            if (from == null || to == null || fromParty == null || toParty == null || fromParty == toParty || item == null || amount <= 0)
            {
                reason = "Trade item term requires different source/destination parties plus valid heroes, item, and amount.";
                return false;
            }

            int available = fromParty?.ItemRoster?.GetItemNumber(item) ?? 0;
            EquipmentElement valuedElement = EquipmentElement.Invalid;
            bool equipped = false;
            if (available < amount && amount == 1
                && ReignTemporaryPartyGuestCampaignBehavior.Instance
                    ?.IsTemporaryGuestEquipmentLocked(from) != true)
            {
                equipped = ReignObjectResolver.TryFindEquippedItem(
                    from,
                    itemId,
                    ReadStringTerm(terms, "sourceEquipmentSlot", string.Empty),
                    ReadStringTerm(terms, "sourceEquipmentSet", string.Empty),
                    out Equipment _,
                    out EquipmentIndex _,
                    out valuedElement,
                    out string _);
            }

            if (available < amount && !equipped)
            {
                reason = from.Name + " does not have enough " + item.Name + " for this trade package.";
                return false;
            }

            string source;
            int value = EstimateItemValue(item, equipped ? valuedElement : EquipmentElement.Invalid, amount, fromParty, out source);
            AddQuote(appraisal, "item", item.StringId, item.Name.ToString(), from, to, amount, value / Math.Max(1, amount), value, source);
            return true;
        }

        private static bool TryAddItemListQuotes(ReignWorldActionRecord action, JObject terms, ReignTradeAppraisal appraisal, out string reason)
        {
            reason = string.Empty;
            foreach (JObject itemTerm in ReadItemTermObjects(terms))
            {
                string itemId = ReadStringTerm(itemTerm, "itemId", ReadStringTerm(itemTerm, "item", string.Empty));
                if (string.IsNullOrWhiteSpace(itemId))
                {
                    continue;
                }

                Hero from = ResolveHero(ReadStringTerm(itemTerm, "fromHeroStringId",
                    ReadStringTerm(itemTerm, "itemFromHeroStringId",
                        ReadStringTerm(terms, "itemFromHeroStringId",
                            ReadStringTerm(terms, "assetFromHeroStringId",
                                ReadStringTerm(terms, "fromHeroStringId", action.ActorHeroStringId))))));
                Hero to = ResolveHero(ReadStringTerm(itemTerm, "toHeroStringId",
                    ReadStringTerm(itemTerm, "itemToHeroStringId",
                        ReadStringTerm(terms, "itemToHeroStringId",
                            ReadStringTerm(terms, "assetToHeroStringId",
                                ReadStringTerm(terms, "toHeroStringId", action.TargetHeroStringId))))));
                MobileParty fromParty = PartyForInventory(from);
                MobileParty toParty = PartyForInventory(to);
                ItemObject item = ReignObjectResolver.FindItem(itemId);
                int amount = ReadIntTerm(itemTerm, "amount", ReadIntTerm(itemTerm, "Amount", 1));
                if (from == null || to == null || fromParty == null || toParty == null || fromParty == toParty || item == null || amount <= 0)
                {
                    reason = "Trade item list term requires different source/destination parties plus valid heroes, item, and amount.";
                    return false;
                }

                int available = fromParty?.ItemRoster?.GetItemNumber(item) ?? 0;
                EquipmentElement valuedElement = EquipmentElement.Invalid;
                bool equipped = false;
                if (available < amount && amount == 1
                    && ReignTemporaryPartyGuestCampaignBehavior.Instance
                        ?.IsTemporaryGuestEquipmentLocked(from) != true)
                {
                    equipped = ReignObjectResolver.TryFindEquippedItem(
                        from,
                        itemId,
                        ReadStringTerm(itemTerm, "sourceEquipmentSlot", string.Empty),
                        ReadStringTerm(itemTerm, "sourceEquipmentSet", string.Empty),
                        out Equipment _,
                        out EquipmentIndex _,
                        out valuedElement,
                        out string _);
                }

                if (available < amount && !equipped)
                {
                    reason = from.Name + " does not have enough " + item.Name + " for this trade package.";
                    return false;
                }

                string source;
                int value = EstimateItemValue(item, equipped ? valuedElement : EquipmentElement.Invalid, amount, fromParty, out source);
                AddQuote(appraisal, "item", item.StringId, item.Name.ToString(), from, to, amount, value / Math.Max(1, amount), value, source);
            }

            return true;
        }

        private static bool TryAddSettlementQuotes(ReignWorldActionRecord action, JObject terms, ReignTradeAppraisal appraisal, out string reason)
        {
            reason = string.Empty;
            List<string> settlementIds = ReadStringListTerm(terms, "settlementIds");
            if (!string.IsNullOrWhiteSpace(action.TargetSettlementStringId) && !settlementIds.Contains(action.TargetSettlementStringId))
            {
                settlementIds.Insert(0, action.TargetSettlementStringId);
            }

            if (settlementIds.Count == 0)
            {
                string termSettlement = ReadStringTerm(terms, "targetSettlementStringId", string.Empty);
                if (!string.IsNullOrWhiteSpace(termSettlement))
                {
                    settlementIds.Add(termSettlement);
                }
            }

            Hero to = ResolveHero(ReadStringTerm(terms, "settlementToHeroStringId", ReadStringTerm(terms, "toHeroStringId", action.TargetHeroStringId))) ?? Hero.MainHero;
            foreach (string settlementId in settlementIds.Distinct())
            {
                Settlement settlement = ReignObjectResolver.FindSettlement(settlementId);
                if (settlement == null)
                {
                    reason = "Trade package settlement could not be found: " + settlementId;
                    return false;
                }

                if (!settlement.IsFortification)
                {
                    reason = "Only towns and castles can be transferred in trade packages right now: " + settlement.Name;
                    return false;
                }

                Hero from = settlement.OwnerClan?.Leader ?? (settlement.MapFaction as Kingdom)?.Leader;
                if (from == null || to == null)
                {
                    reason = "Trade settlement term requires current owner and receiving hero.";
                    return false;
                }

                string source;
                int value = EstimateSettlementValue(settlement, to.MapFaction, settlement.MapFaction, out source);
                AddQuote(appraisal, "settlement", settlement.StringId, settlement.Name.ToString(), from, to, 1, value, value, source);
            }

            return true;
        }

        private static bool TryAddWorkshopQuote(ReignWorldActionRecord action, JObject terms, ReignTradeAppraisal appraisal, out string reason)
        {
            reason = string.Empty;
            string workshopId = ReadStringTerm(terms, "workshopId", ReadStringTerm(terms, "workshop", string.Empty));
            if (string.IsNullOrWhiteSpace(workshopId))
            {
                return true;
            }

            Workshop workshop = ReignObjectResolver.FindWorkshop(workshopId);
            Hero to = ResolveHero(ReadStringTerm(terms, "workshopToHeroStringId", ReadStringTerm(terms, "toHeroStringId", action.TargetHeroStringId))) ?? Hero.MainHero;
            if (workshop == null || workshop.Owner == null || to == null)
            {
                reason = "Trade workshop term requires a valid workshop, owner, and receiving hero.";
                return false;
            }

            string source;
            int value = EstimateWorkshopValue(workshop, out source);
            AddQuote(appraisal, "workshop", ReignObjectResolver.WorkshopId(workshop), workshop.Name.ToString(), workshop.Owner, to, 1, value, value, source);
            return true;
        }

        private static bool TryAddPrisonerQuote(ReignWorldActionRecord action, JObject terms, ReignTradeAppraisal appraisal, out string reason)
        {
            reason = string.Empty;
            string prisonerId = ReadStringTerm(terms, "prisonerHeroStringId", ReadStringTerm(terms, "prisonerHero", string.Empty));
            if (string.IsNullOrWhiteSpace(prisonerId))
            {
                return true;
            }

            Hero prisoner = ReignObjectResolver.FindHero(prisonerId);
            Hero to = ResolveHero(ReadStringTerm(terms, "prisonerToHeroStringId", ReadStringTerm(terms, "toHeroStringId", action.TargetHeroStringId))) ?? Hero.MainHero;
            Hero from = prisoner?.PartyBelongedToAsPrisoner?.Owner
                ?? prisoner?.PartyBelongedToAsPrisoner?.LeaderHero
                ?? ReignObjectResolver.FindHero(action.ActorHeroStringId);
            if (prisoner == null || !prisoner.IsPrisoner || to == null)
            {
                reason = "Trade prisoner term requires a real captive hero.";
                return false;
            }

            string source;
            int value = EstimatePrisonerValue(prisoner, out source);
            AddQuote(appraisal, "prisoner", prisoner.StringId, prisoner.Name.ToString(), from, to, 1, value, value, source);
            return true;
        }

        private static bool TryAddShipQuotes(ReignWorldActionRecord action, JObject terms, ReignTradeAppraisal appraisal, out string reason)
        {
            reason = string.Empty;
            string assetText = string.Join(" ", new[]
            {
                ReadStringTerm(terms, "assetClass", string.Empty),
                ReadStringTerm(terms, "asset", string.Empty),
                ReadStringTerm(terms, "assetName", string.Empty),
                ReadStringTerm(terms, "packageText", string.Empty)
            }).ToLowerInvariant();
            bool shipTrade = assetText.Contains("ship") || assetText.Contains("fleet") || assetText.Contains("vessel") || assetText.Contains("boat") || assetText.Contains("naval") || ReadIntTerm(terms, "shipCount", 0) > 0;
            if (!shipTrade)
            {
                return true;
            }

            Hero from = ResolveHero(ReadStringTerm(terms, "assetFromHeroStringId", ReadStringTerm(terms, "fromHeroStringId", action.ActorHeroStringId)));
            Hero to = ResolveHero(ReadStringTerm(terms, "assetToHeroStringId", ReadStringTerm(terms, "toHeroStringId", action.TargetHeroStringId))) ?? Hero.MainHero;
            Kingdom actorKingdom = ReignObjectResolver.FindKingdom(action.ActorKingdomStringId);
            Kingdom targetKingdom = ReignObjectResolver.FindKingdom(action.TargetKingdomStringId);
            MobileParty sourceParty = ResolveShipParty(from, actorKingdom ?? targetKingdom);
            MobileParty destinationParty = ResolveShipParty(to, to?.Clan?.Kingdom ?? targetKingdom ?? actorKingdom);
            if (sourceParty?.Party == null || destinationParty?.Party == null)
            {
                reason = "Trade ship term requires source and destination parties with ship ownership support.";
                return false;
            }

            List<Ship> ships = sourceParty.Ships?
                .Where(x => x != null && (!x.IsUsedByQuest || ReadBoolTerm(terms, "includeQuestShips", false)))
                .ToList() ?? new List<Ship>();
            if (ships.Count == 0)
            {
                reason = sourceParty.Name + " has no transferable ships.";
                return false;
            }

            int requested = ReadIntTerm(terms, "shipCount", ReadIntTerm(terms, "amount", ReadIntTerm(terms, "Amount", 0)));
            bool all = ReadBoolTerm(terms, "allMatchingAssets", requested <= 0);
            int count = all || requested <= 0 ? ships.Count : Math.Min(requested, ships.Count);
            for (int i = 0; i < count; i++)
            {
                Ship ship = ships[i];
                string source;
                int value = EstimateShipValue(ship, sourceParty.Party, destinationParty.Party, out source);
                string shipId = ship.ShipHull?.StringId ?? "ship_" + i.ToString(CultureInfo.InvariantCulture);
                string shipName = ship.Name?.ToString() ?? shipId;
                ReignTradeTermQuote quote = AddQuote(appraisal, "ship", shipId, shipName, from, to, 1, value, value, source);
                quote.FromId = sourceParty.StringId ?? quote.FromId;
                quote.ToId = destinationParty.StringId ?? quote.ToId;
            }

            return true;
        }

        private static ReignTradeTermQuote AddQuote(ReignTradeAppraisal appraisal, string assetType, string assetId, string assetName, Hero from, Hero to, int amount, int unitValue, int totalValue, string source)
        {
            ReignTradeTermQuote quote = new ReignTradeTermQuote
            {
                AssetType = assetType ?? string.Empty,
                AssetId = assetId ?? string.Empty,
                AssetName = assetName ?? string.Empty,
                FromId = from?.StringId ?? string.Empty,
                ToId = to?.StringId ?? string.Empty,
                Amount = Math.Max(1, amount),
                UnitValue = Math.Max(1, unitValue),
                TotalValue = Math.Max(1, totalValue),
                ValueSource = source ?? string.Empty,
                Side = IsPlayerSide(from) ? "player_offer" : "npc_offer"
            };
            appraisal.Quotes.Add(quote);
            if (quote.Side == "player_offer")
            {
                appraisal.PlayerOfferValue += quote.TotalValue;
            }
            else
            {
                appraisal.NpcOfferValue += quote.TotalValue;
            }

            return quote;
        }

        private static bool IsPlayerSide(Hero hero)
        {
            if (hero == null)
            {
                return false;
            }

            return hero == Hero.MainHero
                || hero.Clan == Clan.PlayerClan;
        }

        private static bool IsFairnessBypassed(ReignWorldActionRecord action, JObject terms)
        {
            return ReadBoolTerm(terms, "bypassFairness", false)
                || ReadBoolTerm(terms, "forceExecution", false)
                || (action?.Source ?? string.Empty).IndexOf("prompt_override", StringComparison.OrdinalIgnoreCase) >= 0
                || (action?.Source ?? string.Empty).IndexOf("test", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static double FairnessThreshold(ReignWorldActionRecord action, JObject terms)
        {
            Hero npc = ResolveHero(action?.ActorHeroStringId) ?? ResolveHero(action?.TargetHeroStringId);
            float relation = npc == null || Hero.MainHero == null ? 0f : npc.GetRelationWithPlayer();
            double threshold = 0.85d;
            if (relation >= 40) threshold -= 0.15d;
            else if (relation >= 20) threshold -= 0.08d;
            else if (relation <= -40) threshold += 0.20d;
            else if (relation <= -20) threshold += 0.10d;
            return Math.Max(0.55d, Math.Min(1.35d, threshold));
        }
    }
}
