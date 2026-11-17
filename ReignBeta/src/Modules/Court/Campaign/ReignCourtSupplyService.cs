using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Extensions;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;

namespace ReignBeta.Court
{
    public sealed class ReignSettlementSupplySnapshot
    {
        public string SettlementStringId;
        public string Name;
        public string OwnerClanStringId;
        public string OwnerClanName;
        public string OwnerKingdomStringId;
        public string OwnerKingdomName;
        public string GovernorHeroStringId;
        public string GovernorName;
        public float Prosperity;
        public float Loyalty;
        public float LoyaltyChange;
        public float Security;
        public float SecurityChange;
        public float FoodStocks;
        public float FoodChange;
        public int FoodCapacity;
        public int StrategicSupplyUnits;
        public int GrainCount;
        public int MarketGold;
        public int DailyTaxIncome;
        public int GarrisonWages;
        public int DailyNetIncome;
        public float Construction;
        public string CurrentConstruction;
        public float CurrentConstructionProgress;
        public int ConstructionBoostGold;
        public int GarrisonCount;
        public int GarrisonWounded;
        public float Militia;
        public float MilitiaChange;
        public int VillageCount;
        public int RaidedVillageCount;
        public bool IsBesieged;
        public bool HasShortage;
        public float AttentionScore;
        public List<ReignSettlementSupplyItem> Items = new List<ReignSettlementSupplyItem>();
    }

    public sealed class ReignSettlementSupplyItem
    {
        public string ItemStringId;
        public string Name;
        public int Count;
        public int LocalPrice;
        public bool IsFood;
        public bool IsTradeGood;
    }

    public sealed class ReignSupplyTransferRequest
    {
        public string CommandId;
        public string SourceSettlementStringId;
        public string TargetSettlementStringId;
        public string ItemStringId;
        public int ItemAmount;
        public int GoldAmount;
        public string GoldPayerKind;
        public string GoldPayerStringId;
        public string GoldRecipientKind;
        public string GoldRecipientStringId;
        public bool VassalConsentGranted;
    }

    public sealed class ReignSupplyTransferResult
    {
        public bool Success;
        public string Error;
        public string ReceiptId;
        public int SourceBefore;
        public int SourceAfter;
        public int TargetBefore;
        public int TargetAfter;
        public int GoldTransferred;
    }

    public static class ReignCourtSupplyService
    {
        private static readonly HashSet<string> AppliedCommands = new HashSet<string>(StringComparer.Ordinal);

        public static List<ReignSettlementSupplySnapshot> GetScopedSnapshots(ReignCourtAuthority authority)
        {
            if (authority != ReignCourtAuthority.Royal) return new List<ReignSettlementSupplySnapshot>();
            Kingdom playerKingdom = Clan.PlayerClan?.Kingdom;
            return Town.AllFiefs
                .Where(t => t?.Settlement != null)
                .Where(t => authority == ReignCourtAuthority.Royal
                    ? playerKingdom != null && t.OwnerClan?.Kingdom == playerKingdom
                    : t.OwnerClan == Clan.PlayerClan)
                .Select(CreateSnapshot)
                .Where(x => x != null)
                .OrderByDescending(x => x.AttentionScore)
                .ToList();
        }

        public static List<ReignSettlementSupplySnapshot> GetAllFortificationSnapshots()
        {
            return Town.AllFiefs
                .Where(t => t?.Settlement != null && t.Settlement.IsFortification)
                .Select(CreateSnapshot)
                .Where(x => x != null)
                .OrderBy(x => x.Name)
                .ToList();
        }

        public static int GetStrategicSupplyUnits(ReignCourtAuthority authority)
        {
            return GetScopedSnapshots(authority).Sum(x => x.StrategicSupplyUnits);
        }

        public static ReignSettlementSupplySnapshot CreateSnapshot(Town town)
        {
            Settlement settlement = town?.Settlement;
            if (settlement == null) return null;

            int foodUnits = 0;
            List<ReignSettlementSupplyItem> items = new List<ReignSettlementSupplyItem>();
            for (int i = 0; i < settlement.ItemRoster.Count; i++)
            {
                ItemRosterElement element = settlement.ItemRoster.GetElementCopyAtIndex(i);
                ItemObject item = element.EquipmentElement.Item;
                if (item == null || element.Amount <= 0) continue;
                if (item.IsFood) foodUnits += element.Amount;
                if (!item.IsFood && !item.IsTradeGood) continue;
                items.Add(new ReignSettlementSupplyItem
                {
                    ItemStringId = item.StringId ?? string.Empty,
                    Name = item.Name?.ToString() ?? item.StringId ?? string.Empty,
                    Count = element.Amount,
                    LocalPrice = town.GetItemPrice(item),
                    IsFood = item.IsFood,
                    IsTradeGood = item.IsTradeGood
                });
            }

            int capacity = town.FoodStocksUpperLimit();
            int grain = town.MarketData?.GetItemCountOfCategory(DefaultItemCategories.Grain) ?? 0;
            bool shortage = town.FoodChange < 0f || (capacity > 0 && town.FoodStocks / capacity < 0.25f) || grain < 100;
            float attention = (100f - town.Loyalty) * 1.4f + (100f - town.Security) + Math.Max(0f, -town.FoodChange) * 8f;
            if (shortage) attention += 75f;
            if (settlement.IsUnderSiege) attention += 200f;
            int dailyTaxIncome = (int)Math.Round(TaleWorlds.CampaignSystem.Campaign.Current.Models.SettlementTaxModel.CalculateTownTax(town).ResultNumber);
            int garrisonCount = settlement.Parties.Where(x => x?.IsGarrison == true).Sum(x => x.MemberRoster.TotalManCount);
            int garrisonWages = garrisonCount * 6;

            return new ReignSettlementSupplySnapshot
            {
                SettlementStringId = settlement.StringId ?? string.Empty,
                Name = settlement.Name?.ToString() ?? string.Empty,
                OwnerClanStringId = town.OwnerClan?.StringId ?? string.Empty,
                OwnerClanName = town.OwnerClan?.Name?.ToString() ?? "Unowned",
                OwnerKingdomStringId = town.OwnerClan?.Kingdom?.StringId ?? string.Empty,
                OwnerKingdomName = town.OwnerClan?.Kingdom?.Name?.ToString() ?? "Independent",
                GovernorHeroStringId = town.Governor?.StringId ?? string.Empty,
                GovernorName = town.Governor?.Name?.ToString() ?? "None",
                Prosperity = town.Prosperity,
                Loyalty = town.Loyalty,
                LoyaltyChange = town.LoyaltyChange,
                Security = town.Security,
                SecurityChange = town.SecurityChange,
                FoodStocks = town.FoodStocks,
                FoodChange = town.FoodChange,
                FoodCapacity = capacity,
                StrategicSupplyUnits = foodUnits,
                GrainCount = grain,
                MarketGold = settlement.SettlementComponent?.Gold ?? 0,
                DailyTaxIncome = dailyTaxIncome,
                GarrisonWages = garrisonWages,
                DailyNetIncome = dailyTaxIncome - garrisonWages,
                Construction = town.Construction,
                CurrentConstruction = town.CurrentBuilding?.Name?.ToString() ?? "No construction selected",
                CurrentConstructionProgress = town.CurrentBuilding?.BuildingProgress ?? 0f,
                ConstructionBoostGold = town.BoostBuildingProcess,
                GarrisonCount = garrisonCount,
                GarrisonWounded = settlement.Parties.Where(x => x?.IsGarrison == true).Sum(x => x.MemberRoster.TotalWounded),
                Militia = settlement.Militia,
                MilitiaChange = town.MilitiaChange,
                VillageCount = town.Villages.Count,
                RaidedVillageCount = town.Villages.Count(x => x.VillageState != Village.VillageStates.Normal),
                IsBesieged = settlement.IsUnderSiege,
                HasShortage = shortage,
                AttentionScore = attention,
                Items = items.OrderByDescending(x => x.IsFood).ThenByDescending(x => x.Count).ToList()
            };
        }

        public static ReignSupplyTransferResult ApplyTransfer(ReignSupplyTransferRequest request, ReignCourtAuthority authority)
        {
            string error = ValidateTransfer(request, authority, out Settlement source, out Settlement target, out ItemObject item,
                out Hero payerHero, out Settlement payerSettlement, out Hero recipientHero, out Settlement recipientSettlement);
            if (!string.IsNullOrEmpty(error)) return Failed(error);

            lock (AppliedCommands)
            {
                if (AppliedCommands.Contains(request.CommandId)) return Failed("This court command has already been applied.");
                int sourceBefore = source.ItemRoster.GetItemNumber(item);
                int targetBefore = target.ItemRoster.GetItemNumber(item);
                try
                {
                    source.ItemRoster.AddToCounts(item, -request.ItemAmount);
                    target.ItemRoster.AddToCounts(item, request.ItemAmount);
                    if (request.GoldAmount > 0)
                    {
                        ApplyGold(payerHero, payerSettlement, recipientHero, recipientSettlement, request.GoldAmount);
                    }
                    AppliedCommands.Add(request.CommandId);
                    return new ReignSupplyTransferResult
                    {
                        Success = true,
                        ReceiptId = "supply_receipt_" + request.CommandId,
                        SourceBefore = sourceBefore,
                        SourceAfter = source.ItemRoster.GetItemNumber(item),
                        TargetBefore = targetBefore,
                        TargetAfter = target.ItemRoster.GetItemNumber(item),
                        GoldTransferred = request.GoldAmount
                    };
                }
                catch (Exception ex)
                {
                    int sourceNow = source.ItemRoster.GetItemNumber(item);
                    int targetNow = target.ItemRoster.GetItemNumber(item);
                    source.ItemRoster.AddToCounts(item, sourceBefore - sourceNow);
                    target.ItemRoster.AddToCounts(item, targetBefore - targetNow);
                    return Failed("The native transfer failed and item stock was rolled back: " + ex.Message);
                }
            }
        }

        private static string ValidateTransfer(ReignSupplyTransferRequest request, ReignCourtAuthority authority,
            out Settlement source, out Settlement target, out ItemObject item, out Hero payerHero,
            out Settlement payerSettlement, out Hero recipientHero, out Settlement recipientSettlement)
        {
            source = FindSettlement(request?.SourceSettlementStringId);
            target = FindSettlement(request?.TargetSettlementStringId);
            item = string.IsNullOrWhiteSpace(request?.ItemStringId) ? null : TaleWorlds.CampaignSystem.Campaign.Current?.ObjectManager?.GetObject<ItemObject>(request.ItemStringId);
            payerHero = null; payerSettlement = null; recipientHero = null; recipientSettlement = null;
            if (authority != ReignCourtAuthority.Royal) return "Court supply transfers require royal authority.";
            if (request == null || string.IsNullOrWhiteSpace(request.CommandId)) return "A unique commandId is required.";
            if (source == null || !source.IsFortification) return "The source must be a current town or castle.";
            if (target == null || !target.IsFortification || target == source) return "The target must be a different current town or castle.";
            if (item == null || request.ItemAmount <= 0) return "A real item and positive amount are required.";
            if (source.ItemRoster.GetItemNumber(item) < request.ItemAmount) return "The source no longer has the full requested stock.";
            if (request.GoldAmount < 0) return "Gold cannot be negative.";

            Kingdom playerKingdom = Clan.PlayerClan?.Kingdom;
            bool sourceInScope = authority == ReignCourtAuthority.Royal
                ? playerKingdom != null && source.OwnerClan?.Kingdom == playerKingdom
                : source.OwnerClan == Clan.PlayerClan;
            if (!sourceInScope) return "The source is outside this court's authority.";
            if (source.OwnerClan != Clan.PlayerClan && !request.VassalConsentGranted) return "A vassal-owned export requires recorded consent.";

            if (request.GoldAmount == 0) return string.Empty;
            if (!ResolveParticipant(request.GoldPayerKind, request.GoldPayerStringId, out payerHero, out payerSettlement)
                || !ResolveParticipant(request.GoldRecipientKind, request.GoldRecipientStringId, out recipientHero, out recipientSettlement))
                return "Both gold participants must resolve to a current hero or settlement.";
            if ((payerHero != null && payerHero == recipientHero) || (payerSettlement != null && payerSettlement == recipientSettlement)) return "Gold participants cannot be identical.";
            int availableGold = payerHero?.Gold ?? payerSettlement?.SettlementComponent?.Gold ?? 0;
            return availableGold < request.GoldAmount ? "The payer cannot afford the complete atomic payment." : string.Empty;
        }

        private static void ApplyGold(Hero payerHero, Settlement payerSettlement, Hero recipientHero, Settlement recipientSettlement, int amount)
        {
            if (payerHero != null && recipientHero != null) GiveGoldAction.ApplyBetweenCharacters(payerHero, recipientHero, amount, true);
            else if (payerHero != null) GiveGoldAction.ApplyForCharacterToSettlement(payerHero, recipientSettlement, amount, true);
            else if (recipientHero != null) GiveGoldAction.ApplyForSettlementToCharacter(payerSettlement, recipientHero, amount, true);
            else GiveGoldAction.ApplyForPartyToParty(payerSettlement.Party, recipientSettlement.Party, amount, true);
        }

        private static bool ResolveParticipant(string kind, string id, out Hero hero, out Settlement settlement)
        {
            hero = null; settlement = null;
            if (string.Equals(kind, "hero", StringComparison.OrdinalIgnoreCase))
            {
                hero = Hero.AllAliveHeroes.FirstOrDefault(x => x.StringId == id);
                return hero != null;
            }
            if (string.Equals(kind, "settlement", StringComparison.OrdinalIgnoreCase))
            {
                settlement = FindSettlement(id);
                return settlement?.SettlementComponent != null;
            }
            return false;
        }

        private static Settlement FindSettlement(string id) { return Settlement.All.FirstOrDefault(x => x.StringId == id); }
        private static ReignSupplyTransferResult Failed(string error) { return new ReignSupplyTransferResult { Success = false, Error = error ?? string.Empty }; }
    }
}
