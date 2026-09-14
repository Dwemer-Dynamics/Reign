using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;
using TaleWorlds.CampaignSystem;

namespace ReignBeta.World
{
    internal static class ReignActionReceiptFormatter
    {
        private static readonly string[] TransferEffects =
        {
            "gold_transfer", "item_transfer", "equipped_item_transfer", "marriage_completed",
            "settlement_transfer", "settlement_transferred", "workshop_transfer", "prisoner_release", "ship_transfer", "alliance_started"
        };

        public static string BuildVerifiedReceipt(ReignActionResult result)
        {
            if (result?.Effects == null)
            {
                return string.Empty;
            }

            List<string> lines = new List<string>();
            foreach (Dictionary<string, string> effect in result.Effects)
            {
                string type = Value(effect, "effectType");
                string name = Value(effect, "name");
                Dictionary<string, string> detail = ParseDetail(Value(effect, "detail"));
                if (!detail.ContainsKey("to"))
                {
                    detail["to"] = Value(effect, "id");
                }
                if (type == "gold_transfer")
                {
                    int amount = ReadInt(detail, "amount");
                    lines.Add(DescribeTransfer(detail, amount.ToString("N0", CultureInfo.InvariantCulture) + " denars", name));
                }
                else if (type == "item_transfer" || type == "equipped_item_transfer")
                {
                    int amount = Math.Max(1, ReadInt(detail, "amount"));
                    lines.Add(DescribeTransfer(detail, amount.ToString(CultureInfo.InvariantCulture) + " x " + name, name));
                }
                else if (type == "marriage_completed")
                {
                    lines.Add("Marriage completed: " + DetailValue(detail, "hero1Name", "hero1") + " married " + DetailValue(detail, "hero2Name", "hero2"));
                }
                else if (type == "settlement_transfer" || type == "settlement_transferred")
                {
                    lines.Add("Settlement transferred: " + name);
                }
                else if (type == "workshop_transfer")
                {
                    lines.Add("Workshop transferred: " + name);
                }
                else if (type == "prisoner_release")
                {
                    lines.Add("Prisoner released: " + name);
                }
                else if (type == "ship_transfer")
                {
                    lines.Add("Ship transferred: " + name);
                }
                else if (type == "alliance_started")
                {
                    lines.Add("Alliance activated with " + name);
                }
            }

            return string.Join("; ", lines.Where(x => !string.IsNullOrWhiteSpace(x)));
        }

        public static bool ValidatePromisedEffects(ReignWorldActionRecord action, ReignActionResult result, out string reason)
        {
            reason = string.Empty;
            if (action == null || result == null || !result.Success || !result.Completed)
            {
                reason = result?.Message ?? "No completed execution result was recorded.";
                return false;
            }

            JObject terms = ReignValueService.ParseTerms(action);
            int gold = ReignValueService.ReadIntTerm(terms, "gold", ReignValueService.ReadIntTerm(terms, "GoldAmount", ReignValueService.ReadIntTerm(terms, "goldAmount", 0)));
            if (gold > 0 && SumEffectAmounts(result, "gold_transfer", null) != gold)
            {
                reason = "Verified receipt does not contain the promised " + gold + " denar transfer.";
                return false;
            }

            List<JObject> itemTerms = ReignValueService.ReadItemTermObjects(terms);
            if (itemTerms.Count == 0)
            {
                string itemId = ReignValueService.ReadStringTerm(terms, "itemId", ReignValueService.ReadStringTerm(terms, "item", string.Empty));
                if (!string.IsNullOrWhiteSpace(itemId))
                {
                    itemTerms.Add(terms);
                }
            }

            foreach (IGrouping<string, JObject> group in itemTerms
                .Where(x => !string.IsNullOrWhiteSpace(ReignValueService.ReadStringTerm(x, "itemId", ReignValueService.ReadStringTerm(x, "item", string.Empty))))
                .GroupBy(x => ReignObjectResolver.FindItem(ReignValueService.ReadStringTerm(x, "itemId", ReignValueService.ReadStringTerm(x, "item", string.Empty)))?.StringId
                    ?? ReignValueService.ReadStringTerm(x, "itemId", ReignValueService.ReadStringTerm(x, "item", string.Empty)), StringComparer.OrdinalIgnoreCase))
            {
                int promised = group.Sum(x => ReignValueService.ReadIntTerm(x, "amount", ReignValueService.ReadIntTerm(x, "Amount", 1)));
                int received = SumEffectAmounts(result, "item_transfer", group.Key) + SumEffectAmounts(result, "equipped_item_transfer", group.Key);
                if (received != promised)
                {
                    reason = "Verified receipt for " + group.Key + " is " + received + ", but the agreement promised " + promised + ".";
                    return false;
                }
            }

            string prisonerId = ReignValueService.ReadStringTerm(terms, "prisonerHeroStringId", ReignValueService.ReadStringTerm(terms, "prisonerHero", string.Empty));
            Hero prisoner = ReignObjectResolver.FindHero(prisonerId);
            if (!string.IsNullOrWhiteSpace(prisonerId) && !HasEffect(result, "prisoner_release", prisoner?.StringId ?? prisonerId))
            {
                reason = "Verified receipt does not show the promised prisoner release for " + prisonerId + ".";
                return false;
            }

            List<string> settlementIds = ReignValueService.ReadStringListTerm(terms, "settlementIds");
            if (!string.IsNullOrWhiteSpace(action.TargetSettlementStringId) && !settlementIds.Contains(action.TargetSettlementStringId))
            {
                settlementIds.Add(action.TargetSettlementStringId);
            }

            foreach (string settlementId in settlementIds)
            {
                if (!HasEffect(result, "settlement_transfer", settlementId) && !HasEffect(result, "settlement_transferred", settlementId))
                {
                    reason = "Verified receipt does not show the promised settlement transfer for " + settlementId + ".";
                    return false;
                }
            }

            string workshopId = ReignValueService.ReadStringTerm(terms, "workshopId", ReignValueService.ReadStringTerm(terms, "workshop", string.Empty));
            string resolvedWorkshopId = ReignObjectResolver.FindWorkshop(workshopId) == null
                ? workshopId
                : ReignObjectResolver.WorkshopId(ReignObjectResolver.FindWorkshop(workshopId));
            if (!string.IsNullOrWhiteSpace(workshopId) && !HasEffect(result, "workshop_transfer", resolvedWorkshopId))
            {
                reason = "Verified receipt does not show the promised workshop transfer for " + workshopId + ".";
                return false;
            }

            if (ReignMarriageService.HasMarriageIntent(action) && !HasEffect(result, "marriage_completed"))
            {
                reason = "Marriage agreement did not produce a verified marriage with named heroes.";
                return false;
            }

            if (RequiresVerifiedReceipt(action.Type) && !result.Effects.Any(x => TransferEffects.Contains(Value(x, "effectType"))))
            {
                reason = "Action completed without any verified transfer or marriage receipt.";
                return false;
            }

            return true;
        }

        public static bool RequiresVerifiedReceipt(ReignWorldActionType type)
        {
            return type == ReignWorldActionType.RegularGiveGoldToPlayer
                || type == ReignWorldActionType.RegularTransferGold
                || type == ReignWorldActionType.RegularTransferItem
                || type == ReignWorldActionType.RegularTradePackage
                || type == ReignWorldActionType.DiplomacyPackage
                || type == ReignWorldActionType.PoliticsMarriageAlliance;
        }

        private static string DescribeTransfer(Dictionary<string, string> detail, string asset, string recipientName)
        {
            string from = DetailValue(detail, "from", "fromHero");
            string to = DetailValue(detail, "to", "toHero");
            if (IsPlayer(from))
            {
                return "Lost " + asset;
            }

            if (IsPlayer(to))
            {
                return "Gained " + asset;
            }

            string fromName = ReignObjectResolver.FindHero(from)?.Name?.ToString() ?? from;
            string toName = ReignObjectResolver.FindHero(to)?.Name?.ToString() ?? recipientName;
            return fromName + " gave " + asset + " to " + toName;
        }

        private static bool IsPlayer(string id)
        {
            return string.Equals(id, "player", StringComparison.OrdinalIgnoreCase)
                || string.Equals(id, "main_hero", StringComparison.OrdinalIgnoreCase)
                || string.Equals(id, Hero.MainHero?.StringId, StringComparison.OrdinalIgnoreCase);
        }

        private static int SumEffectAmounts(ReignActionResult result, string type, string itemId)
        {
            int total = 0;
            foreach (Dictionary<string, string> effect in result.Effects.Where(x => string.Equals(Value(x, "effectType"), type, StringComparison.OrdinalIgnoreCase)))
            {
                if (!string.IsNullOrWhiteSpace(itemId) && !string.Equals(Value(effect, "id"), itemId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                Dictionary<string, string> detail = ParseDetail(Value(effect, "detail"));
                total += Math.Max(type == "gold_transfer" ? 0 : 1, ReadInt(detail, "amount"));
            }

            return total;
        }

        private static bool HasEffect(ReignActionResult result, string type)
        {
            return result.Effects.Any(x => string.Equals(Value(x, "effectType"), type, StringComparison.OrdinalIgnoreCase));
        }

        private static bool HasEffect(ReignActionResult result, string type, string id)
        {
            return result.Effects.Any(x => string.Equals(Value(x, "effectType"), type, StringComparison.OrdinalIgnoreCase)
                && string.Equals(Value(x, "id"), id, StringComparison.OrdinalIgnoreCase));
        }

        private static Dictionary<string, string> ParseDetail(string value)
        {
            Dictionary<string, string> result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string part in (value ?? string.Empty).Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int split = part.IndexOf('=');
                if (split > 0)
                {
                    result[part.Substring(0, split).Trim()] = part.Substring(split + 1).Trim();
                }
            }

            return result;
        }

        private static string DetailValue(Dictionary<string, string> detail, string key, string fallbackKey)
        {
            if (detail.TryGetValue(key, out string value) && !string.IsNullOrWhiteSpace(value))
            {
                return value;
            }

            return detail.TryGetValue(fallbackKey, out value) ? value : string.Empty;
        }

        private static int ReadInt(Dictionary<string, string> detail, string key)
        {
            return detail.TryGetValue(key, out string value)
                && int.TryParse(value, NumberStyles.Integer | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out int parsed)
                    ? parsed
                    : 0;
        }

        private static string Value(Dictionary<string, string> values, string key)
        {
            return values != null && values.TryGetValue(key, out string value) ? value ?? string.Empty : string.Empty;
        }
    }
}
