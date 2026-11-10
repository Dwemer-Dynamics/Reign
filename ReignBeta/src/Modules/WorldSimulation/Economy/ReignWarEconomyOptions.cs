using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Reign.Core.Contracts.Economy;
using ReignBeta.Integration;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.ViewModelCollection.Party;

namespace ReignBeta.Economy
{
    internal static class ReignWarEconomyOptions
    {
        // Native model construction resolves game texts. Diagnostics also run before
        // there is a GameTextManager, so never construct it in this type initializer.
        private static readonly Lazy<DefaultPartyWageModel> NativeWages = new Lazy<DefaultPartyWageModel>(() => new DefaultPartyWageModel());
        private static bool _loaded;
        private static string _persistedText, _policyText;
        private static string CachePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Reign", "WarEconomy", "options-v1.json");
        internal static string Source { get; private set; } = "defaults";
        internal static string Revision { get; private set; } = "";
        internal static void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;
            try
            {
                if (File.Exists(CachePath))
                {
                    string text = File.ReadAllText(CachePath);
                    using (var reader = new JsonTextReader(new StringReader(text)) { DateParseHandling = DateParseHandling.None })
                        if (Apply(JObject.Load(reader), false)) _persistedText = text;
                }
            }
            catch (Exception ex) { ReignLog.Warn("War economy cache unavailable; using local defaults: " + ex.Message); }
        }
        internal static bool Apply(JObject envelope, bool persist = true)
        {
            if (!TryRead(envelope, out var options, out string revision)) return false;
            EnsureLoaded();
            string policyText = JObject.FromObject(options.Values()).ToString(Formatting.None);
            string text = new JObject { ["version"] = WarEconomyOptions.Version, ["revision"] = revision,
                ["values"] = JObject.Parse(policyText) }.ToString(Formatting.None);
            // Caller is the native main thread; no mutable dictionaries escape this snapshot.
            if (policyText != _policyText)
            {
                ReignRaidFoodDisruption.BeforeSettingsChange();
                ReignWarEconomyPolicy.ApplyOptions(options); _policyText = policyText;
            }
            Source = persist ? "server" : "cache";
            Revision = revision;
            if (persist && text != _persistedText)
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(CachePath));
                    string temp = CachePath + ".tmp";
                    File.WriteAllText(temp, text);
                    if (File.Exists(CachePath)) File.Replace(temp, CachePath, null); else File.Move(temp, CachePath);
                    _persistedText = text;
                }
                catch (Exception ex) { ReignLog.Warn("War economy preferences applied; local cache write failed: " + ex.Message); }
            }
            return true;
        }
        // Pure envelope validation precedes loading or changing any local preferences.
        internal static bool TryRead(JObject envelope, out WarEconomyOptions options, out string revision)
        {
            options = null; revision = "";
            if (envelope?["version"]?.Type != JTokenType.Integer || envelope["version"].ToString(Formatting.None) != "1"
                || !(envelope["values"] is JObject values)) return false;
            var stamp = envelope["revision"];
            if (stamp != null && stamp.Type != JTokenType.Null && stamp.Type != JTokenType.String && stamp.Type != JTokenType.Date) return false;
            // The existing HTTP JObject parser reads ISO revision timestamps as dates.
            if (stamp?.Type == JTokenType.Date)
            {
                object date = ((JValue)stamp).Value;
                if (date is DateTime time) revision = time.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);
                else if (date is DateTimeOffset offset) revision = offset.UtcDateTime.ToString("o", CultureInfo.InvariantCulture);
                else return false;
            }
            else revision = stamp?.Type == JTokenType.String ? stamp.Value<string>() : "";
            var fields = values.Properties().ToDictionary(p => p.Name,
                p => p.Value is JValue value ? value.Value : (object)p.Value, StringComparer.Ordinal);
            foreach (string key in WarEconomyOptions.DefaultValues().Keys)
            {
                if (fields.ContainsKey(key)) continue;
                if (!key.StartsWith("economyRaid", StringComparison.Ordinal)
                    && !key.StartsWith(WarEconomyOptions.NpcTradePrefix, StringComparison.Ordinal)) return false;
                fields[key] = ReignWarEconomyPolicy.Options.Values()[key];
            }
            // Additive options: old persisted envelopes retain their explicit prices and receive
            // only missing new keys. Never replace a user's schedule with factory defaults.
            return WarEconomyOptions.TryCreate(fields, false, out options, out _);
        }
        internal static JObject Snapshot() => new JObject {
            ["version"] = WarEconomyOptions.Version, ["source"] = Source, ["revision"] = Revision,
            ["uiQuoteVersion"] = 2, ["troopWageScope"] = "military_including_garrisons",
            ["values"] = JObject.FromObject(ReignWarEconomyPolicy.Options.Values())
        };
        internal static bool UsesTroopWages(MobileParty party) => party != null
            && (party.IsGarrison || party.IsMainParty || party.IsLordParty || party.PatrolPartyComponent != null);
        internal static int UnitWage(CharacterObject troop, MobileParty party)
        {
            if (troop == null) return 0;
            if (troop.IsHero || !UsesTroopWages(party)) return troop.TroopWage;
            return BaseTroopWage(troop);
        }
        internal static int BaseTroopWage(CharacterObject troop)
        {
            if (troop == null) return 0;
            if (troop.IsHero) return troop.TroopWage;
            var options = ReignWarEconomyPolicy.Options;
            int native = options.Enabled("economyTroopWagesEnabled") ? 0 : NativeWages.Value.GetCharacterWage(troop);
            return options.TroopWage(troop.Tier, troop.Occupation == Occupation.Mercenary, native);
        }
        internal static int BudgetCount(CharacterObject troop, MobileParty party, int requested, int budget)
        {
            // Compare complete native stack deltas. Per-unit quartering rounding would
            // overcharge large garrisons and omit category/perk discounts.
            return budget < 0 ? 0 : ReignWarEconomyPolicy.AffordableCountExact(requested, budget, budget, 0, 0, 0, 1,
                count => ReignWarEconomyTransactions.AdditionalWages(party, troop, count));
        }
        // Final rounded unit burden in this actual party. Comparing complete bills
        // cancels fixed ship and hero salaries and retains all native/accord modifiers.
        // Only dummy rosters are changed; no recruitment, transfers or payments occur.
        internal static int FinalUnitWage(CharacterObject troop, MobileParty party, bool retained)
        {
            if (troop == null) return 0;
            if (troop.IsHero) return troop.TroopWage;
            if (party == null) return 0;
            if (!retained || party.MemberRoster.GetTroopCount(troop) <= 0)
                return (int)ReignWarEconomyTransactions.AdditionalWages(party, troop, 1);
            var roster = TroopRoster.CreateDummyTroopRoster();
            foreach (var element in party.MemberRoster.GetTroopRoster())
                roster.AddToCounts(element.Character, element.Number, false, element.WoundedNumber, element.Xp);
            var current = roster.GetTroopRoster().First(element => element.Character == troop);
            var model = TaleWorlds.CampaignSystem.Campaign.Current.Models.PartyWageModel;
            int before = (int)model.GetTotalWage(party, roster).ResultNumber;
            roster.AddToCounts(troop, -1, false, current.Number == current.WoundedNumber ? -1 : 0);
            return Math.Max(0, before - (int)model.GetTotalWage(party, roster).ResultNumber);
        }
        internal static int FinalPlayerUnitWage(CharacterObject troop)
            => TaleWorlds.CampaignSystem.Campaign.Current == null ? 0 : FinalUnitWage(troop, MobileParty.MainParty, false);
        internal static int FinalTooltipUnitWage(CharacterObject troop)
        {
            if (troop == null || TaleWorlds.CampaignSystem.Campaign.Current == null) return 0;
            // Native party focus owns this delegate while its screen is open and
            // clears it on finalization. Use the hovered row's exact roster owner,
            // rather than applying main-party perks to a garrison or another lord.
            var view = PartyCharacterVM.OnFocus?.Target as PartyVM;
            var row = view?.CurrentFocusedCharacter;
            if (row?.Character == troop && row.Type == PartyScreenLogic.TroopType.Member)
            {
                var logic = view.PartyScreenLogic;
                var owner = row.Side == PartyScreenLogic.PartyRosterSide.Right ? logic?.RightOwnerParty
                    : row.Side == PartyScreenLogic.PartyRosterSide.Left ? logic?.LeftOwnerParty : null;
                if (owner?.MobileParty != null) return FinalUnitWage(troop, owner.MobileParty, true);
            }
            // Recruitment/encyclopedia previews have no roster owner; native upgrade
            // quotations in the same tooltip also use the main party as recipient.
            return FinalPlayerUnitWage(troop);
        }
    }
}
