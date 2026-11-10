using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ReignBeta.Campaign;
#if !REIGN_EXCLUDE_COURT
using ReignBeta.Court;
#endif
using ReignBeta.Integration;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Party.PartyComponents;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;

namespace ReignBeta.Economy
{
    // Read-only native observations. Mutation profiles use the enrolled disposable-save bridge.
    internal static class ReignWarEconomyTestHost
    {
        [ThreadStatic] private static Hero _navalProbeGovernor;
        [ThreadStatic] private static PerkObject _navalProbePerk;
        private static bool NavalProbePerkPrefix(Hero __instance, PerkObject perk, ref bool __result)
        {
            if (__instance != _navalProbeGovernor || perk != _navalProbePerk) return true;
            __result = true;
            return false;
        }

        private static PerkObject NavalRecruitmentPerk()
        {
            var type = ReignEconomyPatches.NavalRecruitmentType?.Assembly
                .GetType("NavalDLC.CharacterDevelopment.NavalPerks+Boatswain");
            return type == null ? null : AccessTools.Property(type, "ShipwrightsHand")?.GetValue(null, null) as PerkObject;
        }

        private static JObject ProbeNavalRecruitment(MobileParty party, Settlement locality, Hero payer, Func<JObject> observe)
        {
            var type = ReignEconomyPatches.NavalRecruitmentType;
            var perk = NavalRecruitmentPerk();
            var governor = locality.Town?.Governor;
            var caravans = MobileParty.All.Where(p => p.IsActive && p.IsCaravan && p.HasNavalNavigationCapability
                && p.MapEvent == null && p != MobileParty.MainParty && p.ActualClan != Clan.PlayerClan)
                .OrderBy(p => p.StringId, StringComparer.Ordinal).Take(2).ToList();
            if (type == null || perk == null || governor == null || governor.Clan == Clan.PlayerClan
                || locality.OwnerClan == Clan.PlayerClan || caravans.Count != 2 || perk.SecondaryBonus < 1 || perk.SecondaryBonus > 20)
                throw new InvalidOperationException("Naval probe requires an existing NPC governor, garrison and two actual naval caravans; no identities or resources are invented.");
            var native = typeof(TaleWorlds.CampaignSystem.Campaign).GetMethod("GetCampaignBehavior")
                .MakeGenericMethod(type).Invoke(TaleWorlds.CampaignSystem.Campaign.Current, null);
            if (native == null) throw new InvalidOperationException("Native naval campaign behavior unavailable.");
            var method = AccessTools.Method(type, "OnSettlementEntered");
            var target = AccessTools.Method(typeof(Hero), nameof(Hero.GetPerkValue), new[] { typeof(PerkObject) });
            var prefix = AccessTools.Method(typeof(ReignWarEconomyTestHost), nameof(NavalProbePerkPrefix));
            var harmony = new Harmony("com.bannerlordreign.reignbeta.economy.navalfixture");
            bool originalPerk = governor.GetPerkValue(perk);
            JObject before = observe(), once = null, emptyBefore = null, emptyAfter = null;
            int originalGold = payer.Gold;
            try
            {
                // Explicit, thread-local synthetic perk only for this callback. No saved skill/perk is edited.
                _navalProbeGovernor = governor; _navalProbePerk = perk;
                harmony.Patch(target, prefix: new HarmonyMethod(prefix));
                method.Invoke(native, new object[] { caravans[0], locality, null });
                once = observe();
                method.Invoke(native, new object[] { caravans[0], locality, null });
                if (!JToken.DeepEquals(once, observe())) throw new InvalidOperationException("Duplicate naval arrival changed economy state.");
                originalGold = payer.Gold;
                payer.ChangeHeroGold(-payer.Gold);
                emptyBefore = observe();
                method.Invoke(native, new object[] { caravans[1], locality, null });
                emptyAfter = observe();
                foreach (string resource in new[] { "troops", "payerGold", "liabilities", "hearth", "pending" })
                    if (!JToken.DeepEquals(emptyBefore[resource], emptyAfter[resource]))
                        throw new InvalidOperationException("Empty naval payer changed " + resource);
            }
            finally
            {
                payer.ChangeHeroGold(originalGold - payer.Gold);
                harmony.Unpatch(target, prefix);
                _navalProbeGovernor = null; _navalProbePerk = null;
            }
            var after = observe();
            int enlisted = after.Value<int>("troops") - before.Value<int>("troops");
            int quote = ReignWarEconomyTransactions.Quote(locality.MapFaction.BasicTroop, payer);
            double debit = before.Value<double>("hearth") - after.Value<double>("hearth");
            int expense = before.Value<int>("payerGold") - after.Value<int>("payerGold");
            return new JObject { ["ok"] = enlisted > 0 && enlisted <= (int)Math.Round(perk.SecondaryBonus)
                    && Math.Abs(debit - .5 * enlisted) < .001 && expense == quote * enlisted
                    && before.Value<int>("liabilities") == after.Value<int>("liabilities")
                    && after.Value<int>("pending") == 0 && governor.GetPerkValue(perk) == originalPerk,
                ["source"] = ReignWarEconomyTransactions.NavalGarrison, ["before"] = before, ["after"] = after,
                ["enlisted"] = enlisted, ["hearthDebited"] = debit, ["cashExpense"] = expense,
                ["newLiability"] = 0, ["emptyPayerBefore"] = emptyBefore, ["emptyPayerAfter"] = emptyAfter,
                ["syntheticNativeArrival"] = true, ["syntheticPerkOverride"] = true,
                ["persistedPerksChanged"] = false, ["fullCurrentRestoreRequired"] = true,
                ["caravans"] = new JArray(caravans.Select(x => x.StringId)), ["providerCalls"] = 0 };
        }
        [ThreadStatic] private static Village _civilianProbeVillage;
        [ThreadStatic] private static int _civilianProbeFishingCapacity;
        [ThreadStatic] private static bool _civilianProbeCreation;
        [ThreadStatic] private static int _civilianChanceHits, _civilianCapacityHits;
        private static bool CivilianProbeChancePrefix(ref float __result)
        { if (!_civilianProbeCreation) return true; _civilianChanceHits++; __result = .75f; return false; }
        private static bool CivilianProbeCapacityPrefix(Village village, ref int __result)
        { if (!_civilianProbeCreation || village != _civilianProbeVillage) return true; _civilianCapacityHits++; __result = _civilianProbeFishingCapacity; return false; }

        private static JObject ProbeCivilianCrews(JObject command)
        {
            var settlement = Settlement.Find(command.Value<string>("settlementId"));
            var village = settlement?.Village;
            var town = village?.Bound;
            var type = ReignEconomyPatches.NativeFishingType;
            if (village == null || type == null || village.VillageState != Village.VillageStates.Normal
                || village.Hearth < 80 || ReignRaidFoodDisruption.Capacity(village)<=0 || village.Settlement.Party.MapEvent != null || town?.Town == null
                || town.IsUnderSiege || town.OwnerClan == Clan.PlayerClan
                || ReignWarEconomyFundingAudit.Coverage()["active"]?.Type != JTokenType.Boolean
                || ReignWarEconomyFundingAudit.Coverage().Value<bool>("active"))
                throw new InvalidOperationException("Civilian crew probe requires a functioning safe coastal NPC village with positive fishing capacity, at least80 hearths, bound NPC town and no active funding trace.");
            var native = typeof(TaleWorlds.CampaignSystem.Campaign).GetMethod("GetCampaignBehavior").MakeGenericMethod(type)
                .Invoke(TaleWorlds.CampaignSystem.Campaign.Current, null);
            var coastal = AccessTools.Method(type, "CanHaveFishingParties");
            if (native == null || coastal?.Invoke(native, new object[] { village }) is not bool hasFishing || !hasFishing)
                throw new InvalidOperationException("The exact village has no native fishing capability.");
            var owner = town.Notables.FirstOrDefault(h => h.IsMerchant && h.IsActive && !h.IsDead && !h.IsPrisoner && h != Hero.MainHero && h.Clan != Clan.PlayerClan)
                ?? throw new InvalidOperationException("Choose a bound town with an existing free NPC merchant for the temporary caravan.");
            var template = Game.Current.ObjectManager.GetObject<PartyTemplateObject>("caravan_template_" + town.Culture.StringId)
                ?? throw new InvalidOperationException("The loaded native culture caravan template is unavailable.");
            if (template.ShipHulls.Any()) throw new InvalidOperationException("This fixed caravan initialization case requires the native land template; naval commercial arrivals remain a separate route.");
            var originalParties = new HashSet<MobileParty>(MobileParty.All, ReignWarEconomyReferenceComparer<MobileParty>.Instance);
            var hearths = Village.All.ToDictionary(v => v, v => v.Hearth);
            var inventory = settlement.ItemRoster.ToList();
            if (inventory.Sum(i => (long)i.Amount) > 2000) throw new InvalidOperationException("Civilian fixture warehouse input exceeds2000 units.");
            var observations = new JArray();
            var fixtureParties = new List<MobileParty>();
            var chance = AccessTools.PropertyGetter(typeof(MBRandom), nameof(MBRandom.RandomFloat));
            var capacity = AccessTools.Method(type, "GetIdealFishingPartyCount");
            if (chance == null || capacity == null) throw new InvalidOperationException("Native fishing fixture callback shape changed.");
            var harmony = new Harmony("com.bannerlordreign.reignbeta.economy.civilianfixture." + Guid.NewGuid().ToString("N"));
            var cleanupErrors = new JArray();
            long suppressedBefore = ReignEconomyPatches.CivilianFishingSuppressedWrites;
            _civilianChanceHits = _civilianCapacityHits = 0;
            int originalFishing = MobileParty.All.Count(x => x.PartyComponent?.GetType().FullName == "NavalDLC.FishingPartyComponent"
                && (x.PartyComponent as VillagerPartyComponent)?.Village == village);
            if (originalFishing > 2) throw new InvalidOperationException("The native village exceeds the bounded fishing-party fixture baseline.");
            Action<string, int, int> record = (source, before, after) =>
            {
                double delta = hearths.Sum(v => (double)v.Key.Hearth - v.Value);
                bool unchanged = hearths.All(v => v.Key.Hearth == v.Value);
                observations.Add(new JObject { ["source"] = source, ["troopsBefore"] = before, ["troopsAfter"] = after,
                    ["allVillageHearthsUnchanged"] = unchanged, ["netHearthDelta"] = delta,
                    ["passed"] = after > before && unchanged });
            };
            try
            {
                // Explicit bounded warehouse/capacity/chance inputs; no gold or hearth grant.
                settlement.ItemRoster.Clear();
                _civilianProbeVillage = village; _civilianProbeFishingCapacity = originalFishing + 1; _civilianProbeCreation = true;
                harmony.Patch(chance, prefix: new HarmonyMethod(typeof(ReignWarEconomyTestHost), nameof(CivilianProbeChancePrefix)));
                harmony.Patch(capacity, prefix: new HarmonyMethod(typeof(ReignWarEconomyTestHost), nameof(CivilianProbeCapacityPrefix)));
                AccessTools.Method(type, "OnDailySettlementTick").Invoke(native, new object[] { settlement });
                _civilianProbeCreation = false;
                var createdFishing = MobileParty.All.Where(x => !originalParties.Contains(x) && x.PartyComponent?.GetType().FullName == "NavalDLC.FishingPartyComponent").ToList();
                fixtureParties.AddRange(createdFishing);
                var fishing = createdFishing.Single();
                if (fishing.MemberRoster.TotalManCount < 2 || fishing.MemberRoster.TotalManCount > 100 || fishing.MemberRoster.GetTroopRoster().Any(r => r.Character.IsHero))
                    throw new InvalidOperationException("Native fishing crew is outside the fixture's2-100 regular troop bound.");
                record("native_fishing_creation", 0, fishing.MemberRoster.TotalManCount);
                bool kept = false;
                foreach (var row in fishing.MemberRoster.GetTroopRoster().ToList())
                {
                    int keep = !kept && row.Number > 0 ? 1 : 0; kept |= keep == 1;
                    fishing.MemberRoster.AddToCounts(row.Character, -(row.Number - keep), false, -row.WoundedNumber);
                }
                int beforeReinforce = fishing.MemberRoster.TotalManCount;
                AccessTools.Method(type, "TryReinforceParty").Invoke(native, new object[] { fishing.PartyComponent });
                record("native_fishing_reinforcement", beforeReinforce, fishing.MemberRoster.TotalManCount);
                var caravan = TaleWorlds.CampaignSystem.Party.PartyComponents.CaravanPartyComponent.CreateCaravanParty(owner, town, template);
                fixtureParties.Add(caravan);
                if (caravan.MemberRoster.TotalManCount < 1 || caravan.MemberRoster.TotalManCount > 100 || caravan.MemberRoster.GetTroopRoster().Any(r => r.Character.IsHero))
                    throw new InvalidOperationException("Native caravan crew is outside the fixture's1-100 regular troop bound.");
                record("native_caravan_creation", 0, caravan.MemberRoster.TotalManCount);
                caravan.MemberRoster.Clear();
                var argsType = typeof(TaleWorlds.CampaignSystem.Party.PartyComponents.CaravanPartyComponent).GetNestedType("InitializationArgs", BindingFlags.NonPublic);
                var ctor = argsType?.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
                    new[] { typeof(PartyTemplateObject), typeof(Hero), typeof(ItemRoster) }, null)
                    ?? throw new InvalidOperationException("Native caravan initialization shape changed.");
                var init = ctor.Invoke(new object[] { template, null, null });
                AccessTools.Method(argsType, "InitializeCaravanOnCreation").Invoke(init, new object[] { caravan, town });
                record("native_caravan_crew_reinitialization", 0, caravan.MemberRoster.TotalManCount);
            }
            finally
            {
                _civilianProbeCreation = false; _civilianProbeVillage = null; _civilianProbeFishingCapacity = 0;
                try { harmony.UnpatchAll(harmony.Id); }
                catch (Exception ex) { cleanupErrors.Add("unpatch: " + ex.Message); }
                foreach (var party in fixtureParties.Where(x => MobileParty.All.Contains(x) && !originalParties.Contains(x)))
                    try { DestroyPartyAction.Apply(null, party); }
                    catch (Exception ex) { cleanupErrors.Add("party cleanup: " + ex.Message); }
                try
                {
                    settlement.ItemRoster.Clear();
                    foreach (var item in inventory) settlement.ItemRoster.AddToCounts(item.EquipmentElement, item.Amount);
                }
                catch (Exception ex) { cleanupErrors.Add("warehouse cleanup: " + ex.Message); }
            }
            int remainingCreated = MobileParty.All.Count(x => !originalParties.Contains(x));
            bool warehouseRestored = inventory.Count == settlement.ItemRoster.Count
                && inventory.All(i => settlement.ItemRoster.ToList().Any(r => r.EquipmentElement.Equals(i.EquipmentElement) && r.Amount == i.Amount));
            long suppressed = ReignEconomyPatches.CivilianFishingSuppressedWrites - suppressedBefore;
            return new JObject { ["ok"] = observations.Count == 4 && observations.All(x => x.Value<bool>("passed"))
                    && hearths.All(v => v.Key.Hearth == v.Value) && suppressed == 2
                    && _civilianChanceHits > 0 && _civilianCapacityHits > 0
                    && remainingCreated == 0 && warehouseRestored && cleanupErrors.Count == 0,
                ["villageId"] = settlement.StringId, ["townId"] = town.StringId, ["cases"] = observations,
                ["maximumCreatedParties"] = 2, ["observedCreatedParties"] = fixtureParties.Count,
                ["remainingCreatedParties"] = remainingCreated, ["cleanupErrors"] = cleanupErrors,
                ["suppressedHearthWrites"] = suppressed, ["chanceOverrideHits"] = _civilianChanceHits,
                ["capacityOverrideHits"] = _civilianCapacityHits, ["warehouseRestored"] = warehouseRestored,
                ["syntheticChanceAndCapacity"] = true,
                ["warehouseUnitsRestored"] = inventory.Sum(i => i.Amount), ["hearthGrants"] = 0,
                ["caravanFundingTested"] = false, ["fullCurrentRestoreRequired"] = true,
                ["nativeEffectsAndRandomStateDiscardByReload"] = true, ["providerCalls"] = 0 };
        }

        internal static JObject Run(JObject command, string gameInstanceId)
        {
            var campaign = TaleWorlds.CampaignSystem.Campaign.Current;
            string expected = command.Value<string>("expectedSaveName") ?? "";
            string active = ReignServerClient.ActiveNativeSaveName();
            if (campaign == null || campaign.TimeControlMode != CampaignTimeControlMode.Stop
                || expected.Length == 0 || !string.Equals(expected, active, StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(command.Value<string>("campaignTestRunId")))
                throw new InvalidOperationException("Economy fixtures require the exact MCP-enrolled paused disposable save.");
            string profile = command.Value<string>("profile") ?? "snapshot";
            var state = ReignWarEconomyTransactions.State ?? throw new InvalidOperationException("Economy state unavailable.");
            string key = (command.Value<string>("fixtureRunId") ?? "") + ":" + (command.Value<string>("operationId") ?? "");
            bool fixture = profile == "raid_food_probe" || profile == "raid_partial_setup" || profile == "recruitment" || profile == "production_probe" || profile == "relief_probe" || profile == "funding_probe" || profile == "market_food_probe" || profile == "household_probe" || profile == "raid_setup" || profile == "readiness_probe" || profile == "civilian_crew_probe" || profile == "ship_funding_probe" || profile == "replacement_setup" || profile == "npc_caravan_probe" || profile == "household_stipend_probe";
            if (fixture && state.NativeTestReceipts.TryGetValue(key, out string previous))
            {
                var retained = JObject.Parse(previous);
                if ((profile == "raid_food_probe" || profile == "raid_partial_setup" || retained.Value<string>("profile") == "raid_food_probe" || retained.Value<string>("profile") == "raid_partial_setup")
                    && (retained.Value<string>("profile") != profile || retained.Value<string>("villageId") != command.Value<string>("settlementId")
                        || (profile == "raid_partial_setup" && (retained.Value<int>("damagePercent") != (command.Value<int?>("damagePercent") ?? 10)
                            || retained.Value<string>("raiderPartyId") != command.Value<string>("partyId")))))
                    throw new InvalidOperationException("Food fixture identity changed; restore Current rather than replay.");
                if (profile == "replacement_setup" || retained.Value<string>("profile") == "replacement_setup")
                {
                    var currentParty = MobileParty.All.SingleOrDefault(p => p.StringId == command.Value<string>("partyId"));
                    if (retained.Value<string>("profile") != profile || currentParty == null
                        || retained.Value<string>("partyId") != currentParty.StringId
                        || retained.Value<string>("nativePartyId") != currentParty.Id.ToString())
                        throw new InvalidOperationException("Replacement scenario identity changed or was interrupted; restore Current rather than repeat losses.");
                }
                if ((profile == "readiness_probe" || retained.Value<string>("profile") == "readiness_probe")
                    && (retained.Value<string>("profile") != profile || retained.Value<string>("partyId") != command.Value<string>("partyId")))
                    throw new InvalidOperationException("Readiness probe identity changed or was interrupted; restore Current instead of replaying drawdown.");
                if ((profile == "raid_setup" || retained.Value<string>("profile") == "raid_setup")
                    && (retained.Value<string>("profile") != profile
                        || retained.Value<string>("villageId") != command.Value<string>("settlementId")
                        || retained.Value<string>("raiderPartyId") != command.Value<string>("partyId")))
                    throw new InvalidOperationException("Raid setup identity changed or was interrupted; restore Current instead of replaying callbacks.");
                if ((profile == "civilian_crew_probe" || retained.Value<string>("profile") == "civilian_crew_probe")
                    && (retained.Value<string>("profile") != profile || retained.Value<string>("villageId") != command.Value<string>("settlementId")))
                    throw new InvalidOperationException("Civilian crew probe identity changed or was interrupted; restore Current.");
                if ((profile == "npc_caravan_probe" || profile == "household_stipend_probe"
                    || retained.Value<string>("profile") == "npc_caravan_probe" || retained.Value<string>("profile") == "household_stipend_probe")
                    && (retained.Value<string>("profile") != profile || retained.Value<string>("settlementId") != command.Value<string>("settlementId")
                        || (profile == "npc_caravan_probe" && retained.Value<int>("formationPricingVersion") != 2)
                        || retained.Value<string>(profile == "npc_caravan_probe" ? "clanId" : "recipientClanId") != command.Value<string>("partyId")))
                    throw new InvalidOperationException("Caravan or stipend fixture identity changed; restore Current.");
                if (profile == "ship_funding_probe" && (retained.Value<string>("profile") != profile
                    || retained.Value<string>("sourceParty") != command.Value<string>("partyId")
                    || retained.Value<string>("settlementId") != command.Value<string>("settlementId")))
                    throw new InvalidOperationException("Ship fixture identity changed or was interrupted; restore Current.");
                return retained;
            }
            JObject result;
            if (fixture)
            {
                if (state.NativeTestReceipts.Count >= 128) throw new InvalidOperationException("Fixture receipt limit reached; restore the run-owned checkpoint.");
                // Reserve before invoking native code. An interrupted fixture is never silently replayed.
                state.NativeTestReceipts[key] = new JObject { ["ok"] = false, ["status"] = "interrupted",
                    ["operationId"] = key, ["activeSaveName"] = active }.ToString(Formatting.None);
                result = profile == "civilian_crew_probe" ? ProbeCivilianCrews(command)
                    : profile == "npc_caravan_probe" ? ReignNpcCaravanTestHost.Probe(command)
                    : profile == "household_stipend_probe" ? ReignHouseholdStipends.Probe(command)
                    : profile == "replacement_setup" ? SetupReplacementLoss(command)
                    : profile == "ship_funding_probe" ? ReignWarEconomyNavalTestHost.Run(command)
                    : profile == "funding_probe" ? ReignWarEconomyFundingAudit.Probe(command)
                    : profile == "raid_food_probe" || profile == "raid_partial_setup" ? ReignRaidFoodTestHost.Run(command)
                    : profile == "raid_setup" ? SetupRaid(command)
                    : profile == "readiness_probe" ? ProbeReadiness(command)
                    : profile == "market_food_probe" ? ReignWarEconomyFoodMarket.Probe(command)
                    : profile == "household_probe" ? ProbeHousehold(command)
                    : profile == "recruitment" ? ProbeRecruitment(command)
                    : profile == "production_probe" ? ProbeProduction(command) : ProbeRelief(command);
            }
            else if (profile == "cohort_control") result = ReignWarEconomyCohortControl.Control(command, gameInstanceId);
            else if (profile == "funding_trace") result = ReignWarEconomyFundingAudit.Trace(command);
            else if (profile == "snapshot" || profile == "save_prepare" || profile == "save_verify")
            {
                result = Capture();
                result["ok"] = true;
                var saved = PersistencePayload();
                result["featureFingerprint"] = HashToken(saved);
                result["featureFingerprintSchema"] = "reign-war-economy-persistence-v3";
                result["persistenceComponentFingerprints"] = ComponentFingerprints(saved);
                if (profile == "save_verify")
                    result["ok"] = !string.Equals(gameInstanceId, command.Value<string>("previousGameInstanceId"), StringComparison.Ordinal)
                        && string.Equals(result.Value<string>("featureFingerprint"), command.Value<string>("expectedFeatureFingerprint"), StringComparison.Ordinal)
                        && state.Pending.Count == 0 && state.NavalPending.Count == 0 && state.CaravanInvestments.Pending == null;
            }
            else throw new ArgumentException("Unsupported economy fixture profile.");
            result["schema"] = "reign-war-economy-native-test-v2";
            result["economyOptions"] = ReignWarEconomyOptions.Snapshot();
            result["productionCurveVersion"] = ReignWarEconomyPolicy.ProductionCurveVersion;
            result["recruitmentPriceVersion"] = ReignWarEconomyPolicy.RecruitmentPriceVersion;
            result["profile"] = profile; result["operationId"] = key;
            result["activeSaveName"] = active; result["gameInstanceId"] = gameInstanceId;
            result["campaignTestRunId"] = command.Value<string>("campaignTestRunId");
            result["nativeGameplayAcceptanceComplete"] = false;
            if (fixture) state.NativeTestReceipts[key] = result.ToString(Formatting.None);
            return result;
        }

        private static JObject SetupReplacementLoss(JObject command)
        {
            var party = MobileParty.All.SingleOrDefault(p => p.StringId == command.Value<string>("partyId"));
            if (party == null || !party.IsActive || !party.IsLordParty || party == MobileParty.MainParty
                || party.ActualClan == null || party.ActualClan == Clan.PlayerClan || party.ActualClan.Kingdom == null || party.ActualClan.Leader == null
                || party.LeaderHero == null || party.LeaderHero.PartyBelongedTo != party || !party.LeaderHero.IsAlive
                || party.LeaderHero.IsPrisoner || party.MapEvent != null || party.SiegeEvent != null || party.Army != null
                || party.IsDisbanding || party.IsCurrentlyAtSea || party.IsCurrentlyUsedByAQuest)
                throw new InvalidOperationException("Replacement setup requires one free active NPC kingdom lord party without battle, siege, army or quest involvement.");
            var regulars = party.MemberRoster.GetTroopRoster().Where(e => !e.Character.IsHero && e.Number > e.WoundedNumber)
                .OrderBy(e => e.Character.StringId, StringComparer.Ordinal).ToList();
            int healthy = regulars.Sum(e => e.Number - e.WoundedNumber);
            if (healthy < 20 || healthy > 32768 || regulars.Any(e => e.Xp < 0))
                throw new InvalidOperationException("Replacement setup needs20-32768 healthy regulars and valid native experience.");
            int target = Math.Min(100, healthy / 2);
            var economy = ReignEconomyCampaignBehavior.Instance ?? throw new InvalidOperationException("Economy behavior unavailable.");
            var losses = regulars.ToDictionary(e => e.Character, e => (e.Number - e.WoundedNumber) * target / healthy);
            int remaining = target - losses.Values.Sum();
            foreach (var element in regulars.OrderByDescending(e => (long)(e.Number - e.WoundedNumber) * target % healthy)
                .ThenBy(e => e.Character.StringId, StringComparer.Ordinal).Take(remaining)) losses[element.Character]++;
            var basicTroop = party.ActualClan.Culture?.BasicTroop ?? throw new InvalidOperationException("No culture-basic troop for the replacement quote.");
            var rosterBefore = party.MemberRoster.GetTroopRoster().ToList();
            Func<IEnumerable<TroopRosterElement>, JArray> rosterView = values => new JArray(values
                .OrderBy(e => e.Character.StringId, StringComparer.Ordinal).Select(e => new JObject {
                    ["id"] = e.Character.StringId, ["number"] = e.Number, ["wounded"] = e.WoundedNumber, ["xp"] = e.Xp }));
            Func<IEnumerable<ItemRosterElement>, JArray> itemView = values => new JArray(values
                .OrderBy(e => e.EquipmentElement.Item?.StringId, StringComparer.Ordinal)
                .ThenBy(e => e.EquipmentElement.ItemModifier?.StringId, StringComparer.Ordinal).Select(e => new JObject {
                    ["id"] = e.EquipmentElement.Item?.StringId, ["modifier"] = e.EquipmentElement.ItemModifier?.StringId, ["number"] = e.Amount }));
            Func<JObject> protectedState = () => new JObject {
                ["heroCash"] = new JArray(Hero.AllAliveHeroes.Concat(Hero.DeadOrDisabledHeroes)
                    .Distinct(ReignWarEconomyReferenceComparer<Hero>.Instance).OrderBy(h => h.Id.ToString(), StringComparer.Ordinal)
                    .Select(h => new JObject { ["id"] = h.Id.ToString(), ["gold"] = h.Gold, ["hp"] = h.HitPoints,
                        ["volunteers"] = new JArray((h.VolunteerTypes ?? Array.Empty<CharacterObject>()).Select(t => t?.StringId)) })),
                ["kingdomCash"] = new JArray(Kingdom.All.OrderBy(k => k.StringId, StringComparer.Ordinal).Select(k => new JObject { ["id"] = k.StringId, ["gold"] = k.KingdomBudgetWallet })),
                ["clanObligations"] = new JArray(Clan.All.OrderBy(c => c.StringId, StringComparer.Ordinal).Select(c => new JObject {
                    ["id"] = c.StringId, ["liabilities"] = c.AutoRecruitmentExpenses, ["influence"] = c.Influence })),
                ["settlements"] = new JArray(Settlement.All.OrderBy(s => s.StringId, StringComparer.Ordinal).Select(s => new JObject {
                    ["id"] = s.StringId, ["hearth"] = s.Village?.Hearth, ["marketGold"] = s.SettlementComponent?.Gold,
                    ["marketItems"] = HashToken(itemView(s.ItemRoster)), ["food"] = s.Town?.FoodStocks, ["militia"] = s.Militia,
                    ["prosperity"] = s.Town?.Prosperity, ["security"] = s.Town?.Security, ["loyalty"] = s.Town?.Loyalty })),
                ["parties"] = new JArray(MobileParty.All.OrderBy(p => p.Id.ToString(), StringComparer.Ordinal).Select(p => new JObject {
                    ["id"] = p.Id.ToString(), ["cash"] = ReignWarEconomyFundingAudit.RawPartyGold(p),
                    ["roster"] = HashToken(rosterView(p.MemberRoster.GetTroopRoster().Where(e => p != party || e.Character.IsHero))),
                    ["prisoners"] = HashToken(rosterView(p.PrisonRoster.GetTroopRoster())), ["items"] = HashToken(itemView(p.ItemRoster)) })),
                ["wounded"] = party.MemberRoster.TotalWounded,
                ["strain"] = new JObject(Settlement.All.Where(s => s.Village != null).OrderBy(s => s.StringId, StringComparer.Ordinal)
                    .Select(s => new JProperty(s.StringId, economy.GetMobilizationStrain(s.Village)))),
                ["day"] = CampaignTime.Now.ToDays };
            int membersBefore = party.MemberRoster.TotalManCount;
            var before = protectedState(); var rows = new JArray();
            int basicQuote = ReignWarEconomyTransactions.Quote(basicTroop, party.LeaderHero);
            var quotes = regulars.ToDictionary(e => e.Character, e => ReignWarEconomyTransactions.Quote(e.Character, party.LeaderHero));
            if (!JToken.DeepEquals(before, protectedState()))
                throw new InvalidOperationException("Recruitment quote changed protected state; restore Current before continuing.");
            foreach (var element in regulars)
            {
                int removed = losses[element.Character];
                if (removed == 0) continue;
                int xpRemoved = (int)((long)element.Xp * removed / element.Number);
                party.MemberRoster.AddToCounts(element.Character, -removed, false, 0, -xpRemoved);
                rows.Add(new JObject { ["troop"] = element.Character.StringId, ["tier"] = element.Character.Tier,
                    ["removed"] = removed, ["xpRemoved"] = xpRemoved,
                    ["sameTroopHireQuote"] = quotes[element.Character] });
            }
            var after = protectedState();
            bool conserved = JToken.DeepEquals(before, after);
            var rosterAfter = party.MemberRoster.GetTroopRoster().ToList();
            bool exactRoster = rosterBefore.All(e => {
                var current = rosterAfter.FirstOrDefault(x => x.Character == e.Character);
                int removed = losses.TryGetValue(e.Character, out int count) ? count : 0;
                int xpRemoved = e.Number == 0 ? 0 : (int)((long)e.Xp * removed / e.Number);
                // FirstOrDefault returns a null-Character struct after a whole stack is removed.
                // Its native WoundedNumber getter dereferences Character, so verify absence first.
                if (current.Character == null)
                    return e.Number == removed && e.WoundedNumber == 0 && e.Xp == xpRemoved;
                return current.Number == e.Number - removed && current.WoundedNumber == e.WoundedNumber && current.Xp == e.Xp - xpRemoved;
            }) && rosterAfter.All(e => rosterBefore.Any(x => x.Character == e.Character));
            return new JObject { ["ok"] = conserved && exactRoster && membersBefore - party.MemberRoster.TotalManCount == target,
                ["partyId"] = party.StringId, ["nativePartyId"] = party.Id.ToString(), ["clanId"] = party.ActualClan.StringId,
                ["kingdomId"] = party.ActualClan.Kingdom.StringId, ["removedRegulars"] = target,
                ["membersBefore"] = membersBefore, ["membersAfter"] = party.MemberRoster.TotalManCount, ["losses"] = rows,
                ["exactRosterAndXp"] = exactRoster, ["quoteBuyerHeroId"] = party.LeaderHero.StringId,
                ["protectedResourcesUnchanged"] = conserved, ["protectedBeforeHash"] = HashToken(before), ["protectedAfterHash"] = HashToken(after),
                ["standardForce"] = new JObject { ["kind"] = "100_culture_basic_recruits", ["troop"] = basicTroop.StringId,
                    ["quotedHireCost"] = (long)100 * basicQuote, ["stockAvailabilityGuaranteed"] = false, ["upgradesEquipmentExperienceExcluded"] = true },
                ["syntheticFieldLoss"] = true, ["simulatesFullNativeBattle"] = false,
                ["scenarioPersistsUntilCurrentRestore"] = true, ["fullCurrentRestoreAfterBranchRequired"] = true,
                ["resourcesGranted"] = 0, ["providerCalls"] = 0, ["timeAdvanced"] = false };
        }

        private static JObject ProbeReadiness(JObject command)
        {
            var party = MobileParty.All.FirstOrDefault(x => x.StringId == command.Value<string>("partyId"));
            if (!ReignMilitaryReadiness.SafeForDrawdown(party) || party.ActualClan == Clan.PlayerClan)
                throw new InvalidOperationException("Readiness probe requires one existing safe NPC peace field party without an army or protected player force order.");
            var state = ReignWarEconomyTransactions.State;
            var pressureChecks = new JArray();
            var priorPressure = state.MilitaryPressure;
            string priorCampaign = state.MilitaryPressureCampaign, priorTimeline = state.MilitaryPressureTimeline;
            double priorDay = state.MilitaryPressureDay;
            string realmId = party.ActualClan.Kingdom.StringId;
            bool hadStanding = state.MilitaryStandings.TryGetValue(realmId, out var priorStanding);
            try
            {
                var realm = party.ActualClan.Kingdom;
                // Quote only the pressure policy, independent of an explicit saved percentage.
                // Restore the exact order object; do not publish an order or news during a quote.
                state.MilitaryStandings[realmId] = new EconomyStandingOrder { Mode = "peace", Automatic = true, Ruler = realm.Leader.StringId };
                var other = Kingdom.All.FirstOrDefault(x => x != realm && !x.IsEliminated && x.Fiefs.Count > 0);
                if (other == null) throw new InvalidOperationException("Readiness quote controls need an existing foreign kingdom.");
                state.MilitaryPressureCampaign = ReignServerClient.GetCampaignId();
                state.MilitaryPressureTimeline = ReignWorldHistoryCampaignBehavior.Instance?.TimelineId ?? "main";
                state.MilitaryPressureDay = CampaignTime.Now.ToDays;
                foreach (int value in new[] { 0, -100, 100 })
                {
                    state.MilitaryPressure = new List<EconomyPressurePair> { new EconomyPressurePair { Actor = other.StringId, Target = realm.StringId, Value = value } };
                    double fraction = ReignMilitaryReadiness.TargetFraction(party);
                    pressureChecks.Add(new JObject { ["storedPressure"] = value, ["targetFraction"] = fraction,
                        ["passed"] = value <= 0 ? Math.Abs(fraction - .45) < .000001 : fraction >= .60 && fraction <= .75 });
                }
                state.MilitaryPressureDay -= 1.01;
                double stale = ReignMilitaryReadiness.TargetFraction(party);
                pressureChecks.Add(new JObject { ["stale"] = true, ["targetFraction"] = stale, ["passed"] = Math.Abs(stale - .45) < .000001 });
            }
            finally
            {
                state.MilitaryPressure = priorPressure; state.MilitaryPressureCampaign = priorCampaign;
                state.MilitaryPressureTimeline = priorTimeline; state.MilitaryPressureDay = priorDay;
                if (hadStanding) state.MilitaryStandings[realmId] = priorStanding;
                else state.MilitaryStandings.Remove(realmId);
            }
            Func<JObject> observe = () => new JObject {
                ["members"] = party.MemberRoster.TotalManCount, ["heroes"] = party.MemberRoster.GetTroopRoster().Where(x => x.Character.IsHero).Sum(x => x.Number),
                ["wounded"] = party.MemberRoster.TotalWounded, ["prisoners"] = party.PrisonRoster.TotalManCount,
                ["leaderCash"] = party.LeaderHero.Gold, ["clanCash"] = party.ActualClan.Gold,
                ["liabilities"] = party.ActualClan.AutoRecruitmentExpenses,
                ["fieldWageQuote"] = party.TotalWage,
                ["healthyByTier"] = JObject.FromObject(ReignMilitaryReadiness.HealthyByTier(party)),
                ["worldHearths"] = Settlement.All.Where(x => x.Village != null).Sum(x => (double)x.Village.Hearth),
                ["roster"] = new JArray(party.MemberRoster.GetTroopRoster().Where(x => !x.Character.IsHero).Select(x => new JObject {
                    ["id"] = x.Character.StringId, ["tier"] = x.Character.Tier, ["number"] = x.Number, ["wounded"] = x.WoundedNumber, ["xp"] = x.Xp })) };
            double day = CampaignTime.Now.ToDays;
            var before = observe();
            var receipt = ReignMilitaryReadiness.DrawdownParty(party);
            var after = observe();
            var duplicate = ReignMilitaryReadiness.DrawdownParty(party);
            var afterDuplicate = observe();
            int removed = before.Value<int>("members") - after.Value<int>("members");
            bool tierReceiptMatches = removed == 0
                || receipt.Value<int?>("AllocationVersion") == ReignWarEconomyPolicy.DrawdownAllocationVersion
                && JToken.DeepEquals(before["healthyByTier"], receipt["HealthyByTierBefore"])
                && JToken.DeepEquals(after["healthyByTier"], receipt["HealthyByTierAfter"]);
            bool conserved = new[] { "heroes", "wounded", "prisoners", "leaderCash", "clanCash", "liabilities" }
                .All(field => JToken.DeepEquals(before[field], after[field]));
            double hearthReturned = after.Value<double>("worldHearths") - before.Value<double>("worldHearths");
            return new JObject { ["ok"] = conserved && tierReceiptMatches && pressureChecks.All(x => x.Value<bool>("passed")) && removed >= 0 && removed <= 10 && removed == receipt.Value<int>("removed")
                    && hearthReturned >= 0 && hearthReturned <= removed * ReignWarEconomyPolicy.HearthReturnPerDischarge + .000001
                    && Math.Abs(hearthReturned - (receipt.Value<double?>("HearthReturned") ?? 0)) < .00001
                    && JToken.DeepEquals(after, afterDuplicate) && state.DrawdownPending.Count == 0 && CampaignTime.Now.ToDays == day,
                ["partyId"] = party.StringId, ["before"] = before, ["after"] = after, ["receipt"] = receipt,
                ["syntheticPressureQuotes"] = pressureChecks, ["politicalServerStateMutated"] = false,
                ["drawdownExercised"] = removed > 0,
                ["drawdownAllocationVersion"] = ReignWarEconomyPolicy.DrawdownAllocationVersion,
                ["tierReceiptMatches"] = tierReceiptMatches,
                ["duplicate"] = duplicate, ["removed"] = removed, ["fullCurrentRestoreAfterProbeRequired"] = true,
                ["timeAdvanced"] = false, ["providerCalls"] = 0 };
        }

        private static JObject SetupRaid(JObject command)
        {
            var locality = Settlement.Find(command.Value<string>("settlementId"));
            var village = locality?.Village;
            var raider = MobileParty.All.FirstOrDefault(x => x.StringId == command.Value<string>("partyId"));
            if (village?.VillageState != Village.VillageStates.Normal || village.Bound?.Town == null
                || locality.OwnerClan == Clan.PlayerClan || village.Bound.IsUnderSiege || locality.IsUnderRaid
                || locality.Party.MapEvent != null || raider?.IsActive != true || !raider.IsLordParty
                || raider == MobileParty.MainParty || raider.ActualClan == Clan.PlayerClan || raider.MapEvent != null
                || raider.LeaderHero?.IsAlive != true || raider.LeaderHero.IsPrisoner
                || raider.MapFaction == null || locality.MapFaction == null || !raider.MapFaction.IsAtWarWith(locality.MapFaction))
                throw new InvalidOperationException("Raid setup requires one safe NPC village and an existing active enemy NPC lord party without a live map event.");
            double day = CampaignTime.Now.ToDays;
            Func<JObject> observe = () => new JObject { ["village"] = VillageObservation(village),
                ["state"] = village.VillageState.ToString(), ["hitPoints"] = locality.SettlementHitPoints,
                ["recruitCapacity"] = ReignEconomyCampaignBehavior.Instance.GetRecruitCapacity(locality),
                ["boundTownFoodStocks"] = village.Bound.Town.FoodStocks,
                ["boundTownFoodModel"] = HoldingFoodModelObservation(village.Bound),
                ["security"] = village.Bound.Town.Security };
            var before = observe();
            // Intentional persistent scenario setup, not a simulated battle or loot award.
            var hitPointsSetter = AccessTools.PropertySetter(typeof(Settlement), "SettlementHitPoints")
                ?? throw new InvalidOperationException("Native raid hit-point setter is unavailable.");
            hitPointsSetter.Invoke(locality, new object[] { 0f });
            ChangeVillageStateAction.ApplyBySettingToLooted(locality, raider);
            var after = observe();
            return new JObject { ["ok"] = village.VillageState == Village.VillageStates.Looted
                    && ReignEconomyCampaignBehavior.Instance.GetRecoveryModifier(village) == 0
                    && ReignRaidFoodDisruption.Capacity(village)==0
                    && ReignEconomyCampaignBehavior.Instance.GetRecruitCapacity(locality) == 0
                    && CampaignTime.Now.ToDays == day,
                ["villageId"] = locality.StringId, ["raiderPartyId"] = raider.StringId,
                ["before"] = before, ["after"] = after, ["syntheticScenario"] = true,
                ["nativeVillageStateCallbacks"] = true, ["simulatedCombat"] = false,
                ["syntheticHearthDestruction"] = false, ["timeAdvanced"] = false, ["providerCalls"] = 0,
                ["scenarioLeftActive"] = true, ["fullCurrentRestoreAfterBranchRequired"] = true };
        }

        private static JObject ProbeProduction(JObject command)
        {
            if (!ReignWarEconomyPolicy.Options.Enabled("economyVillageCurveEnabled"))
                throw new InvalidOperationException("Continuous production probe requires Continuous village output enabled; native bands are an intentional discontinuity. Restore the chosen preferences after testing.");
            var village = Settlement.Find(command.Value<string>("settlementId"))?.Village;
            if (village == null || village.VillageState != Village.VillageStates.Normal
                || village.Bound?.Town == null || village.Bound.IsUnderSiege || village.Settlement.IsUnderRaid)
                throw new InvalidOperationException("Production probe requires an exact functioning village bound to a safe fortification.");
            var behavior = ReignEconomyCampaignBehavior.Instance;
            var state = behavior.EconomyState;
            float originalHearth = village.Hearth;
            bool hadRecovery = state.Recovery.TryGetValue(village.StringId, out float originalRecovery);
            bool hadFoodCapacity=state.RaidFood.Villages.TryGetValue(village.StringId,out var originalFoodCapacity);
            double day = CampaignTime.Now.ToDays;
            var observations = new JArray();
            var checks = new JArray();
            try
            {
                // Isolate the old curve/recovery probe from existing food damage.
                state.RaidFood.Villages[village.StringId]=new RaidFoodVillage{Capacity=1,Normal=true,LastHours=CampaignTime.Now.ToHours};
                foreach (float hearth in new[] { 0f, 1f, 9.99f, 10f, 10.01f, 50f, 100f, 199.99f, 200f, 200.01f, 299.99f, 300f, 300.01f, 400f, 599.99f, 600f, 600.01f, 1200f, 2000f })
                {
                    village.Hearth = hearth;
                    foreach (float recovery in new[] { 0f, .3f, 1f })
                    {
                        state.Recovery[village.StringId] = recovery;
                        double actual = 0, potential = 0;
                        foreach (var recipe in village.VillageType.Productions.Where(x => x.Item1 != null))
                        {
                            var item = recipe.Item1;
                            actual += ReignVillageProductionCalculatorModel.ObserveWithoutRandomAdvance(() =>
                                TaleWorlds.CampaignSystem.Campaign.Current.Models.VillageProductionCalculatorModel
                                    .CalculateDailyProductionAmount(village, item).ResultNumber);
                            potential += ReignVillageProductionCalculatorModel.PotentialProduction(village, item);
                        }
                        observations.Add(new JObject { ["hearth"] = village.Hearth, ["recovery"] = recovery,
                            ["actualGoods"] = actual, ["potentialGoods"] = potential,
                            ["supplementaryFood"] = TaleWorlds.CampaignSystem.Campaign.Current.Models.VillageProductionCalculatorModel.CalculateDailyFoodProductionAmount(village),
                            ["boundTownFood"] = TaleWorlds.CampaignSystem.Campaign.Current.Models.SettlementFoodModel.CalculateTownFoodStocksChange(village.Bound.Town, false).ResultNumber,
                            ["nativeHearthChange"] = village.HearthChange });
                    }
                }
                foreach (var row in observations.Cast<JObject>())
                    checks.Add(new JObject { ["id"] = "recovery_once_" + row["hearth"] + "_" + row["recovery"],
                        ["passed"] = row.Value<double>("actualGoods") >= -.0001
                            && Math.Abs(row.Value<double>("actualGoods") - row.Value<double>("potentialGoods") * row.Value<double>("recovery")) < .005 });
                foreach (float hearth in new[] { 10f, 200f, 300f, 600f })
                {
                    var rows = observations.Cast<JObject>().Where(x => Math.Abs(x.Value<double>("hearth") - hearth) < .02).ToList();
                    var zero = rows.Single(x => x.Value<double>("hearth") == hearth && x.Value<double>("recovery") == 0);
                    var full = rows.Single(x => x.Value<double>("hearth") == hearth && x.Value<double>("recovery") == 1);
                    var partial = rows.Single(x => x.Value<double>("hearth") == hearth && x.Value<double>("recovery") > 0 && x.Value<double>("recovery") < 1);
                    checks.Add(new JObject { ["id"] = "bound_food_recovery_once_" + hearth,
                        ["passed"] = Math.Abs(partial.Value<double>("boundTownFood") - (zero.Value<double>("boundTownFood")
                            + partial.Value<double>("recovery") * (full.Value<double>("boundTownFood") - zero.Value<double>("boundTownFood")))) < .02 });
                    foreach (string channel in new[] { "actualGoods", "supplementaryFood", "boundTownFood" })
                    {
                        var healthy = rows.Where(x => x.Value<double>("recovery") == 1).ToList();
                        checks.Add(new JObject { ["id"] = "continuous_" + channel + "_" + hearth,
                            ["passed"] = healthy.Max(x => x.Value<double>(channel)) - healthy.Min(x => x.Value<double>(channel))
                                < Math.Max(.05, healthy.Max(x => Math.Abs(x.Value<double>(channel))) * .001) });
                    }
                }
            }
            finally
            {
                village.Hearth = originalHearth;
                if (hadRecovery) state.Recovery[village.StringId] = originalRecovery;
                else state.Recovery.Remove(village.StringId);
                if(hadFoodCapacity)state.RaidFood.Villages[village.StringId]=originalFoodCapacity;
                else state.RaidFood.Villages.Remove(village.StringId);
            }
            bool restored = village.Hearth == originalHearth && state.Recovery.ContainsKey(village.StringId) == hadRecovery
                && (!hadRecovery || state.Recovery[village.StringId] == originalRecovery) && CampaignTime.Now.ToDays == day;
            return new JObject { ["ok"] = restored && checks.All(x => x.Value<bool>("passed")),
                ["villageId"] = village.StringId, ["observations"] = observations, ["checks"] = checks,
                ["resourceMutationsRestored"] = restored, ["timeAdvanced"] = false, ["providerCalls"] = 0 };
        }

        private static JObject ProbeRelief(JObject command)
        {
            var donor = Settlement.Find(command.Value<string>("settlementId"))?.Town;
            if (donor == null || donor.IsUnderSiege || donor.FoodChange >= 0
                || .45 * donor.FoodStocksUpperLimit() + 7 * donor.FoodChange <= .2 * donor.FoodStocksUpperLimit())
                throw new InvalidOperationException("Choose a safe declining donor whose projected 45-percent stock can stay above 20 percent.");
            string networkKey = ReignEconomyCampaignBehavior.ReliefNetworkKey(donor);
            var network = Settlement.All.Where(x => x.Town != null && (x.IsTown || x.IsCastle) && x.OwnerClan != null)
                .Select(x => x.Town).Where(x => ReignEconomyCampaignBehavior.ReliefNetworkKey(x) == networkKey)
                .OrderBy(x => x.Settlement.StringId, StringComparer.Ordinal).ToList();
            if (network.Count < 2 || network.Count > 64 || network.Any(x => x.IsUnderSiege) || network.Sum(x => x.FoodChange) >= 0)
                throw new InvalidOperationException("Relief probe requires 2-64 safe current-realm holdings with aggregate food decline.");
            if (network.Any(x => x.FoodStocksUpperLimit() > 10000) || network.Sum(x => (double)x.FoodStocksUpperLimit()) > 100000)
                throw new InvalidOperationException("Relief fixture exceeds the 10000-per-holding or 100000-total synthetic stock bound.");
            var recipient = network.Where(x => x != donor)
                .OrderBy(x => (x.Settlement.GetPosition2D - donor.Settlement.GetPosition2D).LengthSquared)
                .ThenBy(x => x.Settlement.StringId, StringComparer.Ordinal).First();
            var behavior = ReignEconomyCampaignBehavior.Instance;
            var originalStocks = network.ToDictionary(x => x, x => x.FoodStocks);
            string[] fieldNames = { "_currentReliefDonors", "_currentReliefRecipients", "_currentReliefUnmet",
                "_currentReliefFoodShipped", "_currentReliefFoodDelivered", "_currentReliefFoodLost", "_reliefBlockedRequests",
                "_reliefShipmentCount", "_reliefFoodShipped", "_reliefFoodDelivered", "_reliefFoodLost", "_reliefLoyaltyModifiers" };
            var fields = fieldNames.Select(name => AccessTools.Field(typeof(ReignEconomyCampaignBehavior), name)
                ?? throw new InvalidOperationException("Relief restoration binding missing: " + name)).ToList();
            var originalFields = fields.ToDictionary(field => field, field => field.GetValue(behavior));
            var originalFieldJson = fields.ToDictionary(field => field, field => JsonConvert.SerializeObject(originalFields[field]));
            var isolatedFields = fields.ToDictionary(field => field, field => {
                object value = field.GetValue(behavior);
                if (value is Dictionary<string, float> values) return (object)new Dictionary<string, float>(values, values.Comparer);
                if (value is int || value is float) return value;
                throw new InvalidOperationException("Unsupported relief fixture restoration field: " + field.Name);
            });
            float originalUnmet = behavior.EconomyState.UnmetReliefFood;
            bool originalFixture = behavior.ReliefFixtureActive;
            long suppressedBefore = behavior.ReliefFixtureSuppressedPasses;
            int expectedSuppressedPasses = 1;
            JObject evidenceBefore = behavior.ReliefDiagnostics(true);
            double day = CampaignTime.Now.ToDays;
            JObject result = null;
            try
            {
                behavior.ReliefFixtureActive = true;
                // Mutate isolated collection copies; outside references retain the exact original dictionary.
                foreach (var pair in isolatedFields) pair.Key.SetValue(behavior, pair.Value);
                foreach (var town in network) town.FoodStocks = town.FoodStocksUpperLimit() * .2f;
                donor.FoodStocks = donor.FoodStocksUpperLimit() * .45f;
                recipient.FoodStocks = 0;
                var before = network.Select(ReignEconomyCampaignBehavior.ReliefHolding).ToList();
                var plan = ReignWarEconomyPolicy.AllocateRelief(before);
                var donorShipments = plan.Shipments.Where(x => x.Donor == donor.Settlement.StringId).ToList();
                if (donorShipments.Count == 0) throw new InvalidOperationException("The controlled native inputs cannot safely dispatch this donor; choose another reported candidate.");
                behavior.RouteReliefNetwork(network);
                var after = network.Select(ReignEconomyCampaignBehavior.ReliefHolding).ToList();
                double shipped = plan.Shipments.Sum(x => x.Shipped), delivered = plan.Shipments.Sum(x => x.Delivered);
                bool stocksMatch = before.All(b => Math.Abs(after.Single(a => a.Id == b.Id).Stock - (b.Stock
                    - plan.Shipments.Where(s => s.Donor == b.Id).Sum(s => s.Shipped)
                    + plan.Shipments.Where(s => s.Recipient == b.Id).Sum(s => s.Delivered))) < .002);
                var donorAfter = after.Single(x => x.Id == donor.Settlement.StringId);
                bool floors = donorAfter.Stock / donorAfter.Capacity + .00001 >= plan.DonorFloor
                    && donorAfter.Projected / donorAfter.Capacity + .00001 >= plan.DonorFloor;
                bool measuredConservation = Math.Abs(before.Sum(x => x.Stock) - after.Sum(x => x.Stock) - (shipped - delivered)) < .02;
                bool limits = ReliefProbeLimits(before, after, plan);
                result = new JObject { ["ok"] = stocksMatch && floors && measuredConservation && limits,
                    ["donorId"] = donor.Settlement.StringId, ["recipientId"] = recipient.Settlement.StringId,
                    ["fixtureStockAdjustment"] = before.Sum(x => x.Stock) - originalStocks.Values.Sum(x => (double)x),
                    ["donorInitialRatio"] = .45, ["donorFinalRatio"] = donorAfter.Stock / donorAfter.Capacity,
                    ["donorProjectedRatio"] = donorAfter.Projected / donorAfter.Capacity, ["donorFloor"] = plan.DonorFloor,
                    ["shipped"] = shipped, ["delivered"] = delivered, ["spoilage"] = shipped - delivered,
                    ["stockConserved"] = measuredConservation, ["nativeStocksMatchPlan"] = stocksMatch, ["floorsRespected"] = floors,
                    ["nativeThroughputLimitsRespected"] = limits,
                    ["shipments"] = JArray.FromObject(plan.Shipments), ["before"] = JArray.FromObject(before), ["after"] = JArray.FromObject(after),
                    ["reliefPolicyVersion"] = plan.Version, ["recipientDecisions"] = JArray.FromObject(plan.Decisions),
                    ["donorDailyLimits"] = JObject.FromObject(plan.DonorLimits),
                    ["providerCalls"] = 0, ["timeAdvanced"] = false };
                var chronic = network.Where(x => x.IsTown && x.FoodChange < -15)
                    .OrderBy(x => x.FoodChange).ThenBy(x => x.Settlement.StringId, StringComparer.Ordinal).FirstOrDefault();
                result["chronicRecipientCase"] = new JObject { ["available"] = false,
                    ["reason"] = "No safe actual town with a native deficit above15 food/day in this realm; not a pass for chronic-recipient coverage." };
                if (chronic != null)
                {
                    foreach (var town in network) town.FoodStocks = town.FoodStocksUpperLimit() * .9f;
                    chronic.FoodStocks = Math.Min(15, chronic.FoodStocksUpperLimit());
                    var chronicBefore = network.Select(ReignEconomyCampaignBehavior.ReliefHolding).ToList();
                    var active = ReignWarEconomyPolicy.AllocateRelief(chronicBefore);
                    var legacy = ReignWarEconomyPolicy.AllocateRelief(chronicBefore, true);
                    expectedSuppressedPasses++;
                    behavior.RouteReliefNetwork(network);
                    var chronicAfter = network.Select(ReignEconomyCampaignBehavior.ReliefHolding).ToList();
                    double sent = active.Shipments.Sum(x => x.Shipped), arrived = active.Shipments.Sum(x => x.Delivered);
                    double activeTownDelivery = active.Shipments.Where(x => x.Recipient == chronic.Settlement.StringId).Sum(x => x.Delivered);
                    double legacyTownDelivery = legacy.Shipments.Where(x => x.Recipient == chronic.Settlement.StringId).Sum(x => x.Delivered);
                    bool match = chronicBefore.All(b => Math.Abs(chronicAfter.Single(a => a.Id == b.Id).Stock - (b.Stock
                        - active.Shipments.Where(s => s.Donor == b.Id).Sum(s => s.Shipped)
                        + active.Shipments.Where(s => s.Recipient == b.Id).Sum(s => s.Delivered))) < .002);
                    bool conserved = Math.Abs(chronicBefore.Sum(x => x.Stock) - chronicAfter.Sum(x => x.Stock) - (sent - arrived)) < .02;
                    bool chronicLimits = ReliefProbeLimits(chronicBefore, chronicAfter, active);
                    var chronicCase = new JObject { ["available"] = true, ["recipientId"] = chronic.Settlement.StringId,
                        ["actualNativeNet"] = chronicBefore.Single(x => x.Id == chronic.Settlement.StringId).Change,
                        ["activeDelivered"] = activeTownDelivery, ["sameInputLegacyDelivered"] = legacyTownDelivery,
                        ["syntheticStockFraction"] = .9, ["counterfactualLegacyOnly"] = true,
                        ["before"] = JArray.FromObject(chronicBefore), ["after"] = JArray.FromObject(chronicAfter),
                        ["recipientDecisions"] = JArray.FromObject(active.Decisions), ["shipments"] = JArray.FromObject(active.Shipments),
                        ["nativeStocksMatchPlan"] = match, ["stockConserved"] = conserved,
                        ["nativeThroughputLimitsRespected"] = chronicLimits,
                        ["coverageFailureReason"] = !match || !conserved || !chronicLimits ? "native_stock_or_throughput_invariant_failure"
                            : activeTownDelivery <= 15 ? "chronic_delivery_did_not_exceed_legacy_cap_check_route_and_budget_diagnostics"
                            : activeTownDelivery <= legacyTownDelivery + .01 ? "active_did_not_improve_same_input_legacy_quote" : "",
                        ["passed"] = match && conserved && chronicLimits && activeTownDelivery > legacyTownDelivery + .01 && activeTownDelivery > 15 };
                    result["chronicRecipientCase"] = chronicCase;
                    result["ok"] = result.Value<bool>("ok") && chronicCase.Value<bool>("passed");
                }
            }
            finally
            {
                var restoreErrors = new List<string>();
                try
                {
                    foreach (var pair in originalStocks)
                        try { pair.Key.FoodStocks = pair.Value; } catch (Exception ex) { restoreErrors.Add(pair.Key.Settlement.StringId + ":" + ex.GetType().Name); }
                    foreach (var pair in originalFields)
                        try { pair.Key.SetValue(behavior, pair.Value); } catch (Exception ex) { restoreErrors.Add(pair.Key.Name + ":" + ex.GetType().Name); }
                    try { behavior.EconomyState.UnmetReliefFood = originalUnmet; } catch (Exception ex) { restoreErrors.Add("unmet:" + ex.GetType().Name); }
                }
                finally { behavior.ReliefFixtureActive = originalFixture; }
                if (restoreErrors.Count > 0) throw new InvalidOperationException("Relief fixture restore incomplete; restore exact Current: " + string.Join(",", restoreErrors));
            }
            bool restored = originalStocks.All(x => x.Key.FoodStocks == x.Value) && CampaignTime.Now.ToDays == day
                && behavior.EconomyState.UnmetReliefFood == originalUnmet
                && originalFields.All(x => JsonConvert.SerializeObject(x.Key.GetValue(behavior)) == originalFieldJson[x.Key])
                && originalFields.Where(x => x.Value is Dictionary<string, float>).All(x => ReferenceEquals(x.Key.GetValue(behavior), x.Value));
            result["resourceMutationsRestored"] = restored;
            var evidenceAfter = behavior.ReliefDiagnostics(true);
            evidenceBefore.Remove("suppressedFixturePasses"); evidenceAfter.Remove("suppressedFixturePasses");
            bool evidenceUnchanged = JToken.DeepEquals(evidenceBefore, evidenceAfter);
            long suppressed = behavior.ReliefFixtureSuppressedPasses - suppressedBefore;
            result["actualReliefEvidenceUnchanged"] = evidenceUnchanged;
            result["suppressedFixturePasses"] = suppressed;
            result["chronicRecipientCoverageComplete"] = result["chronicRecipientCase"].Value<bool>("available")
                && result["chronicRecipientCase"].Value<bool>("passed");
            result["ok"] = result.Value<bool>("ok") && restored && evidenceUnchanged && suppressed == expectedSuppressedPasses;
            return result;
        }

        private static bool ReliefProbeLimits(List<ReignWarEconomyPolicy.Holding> before,
            List<ReignWarEconomyPolicy.Holding> after, ReignWarEconomyPolicy.ReliefPlan plan)
        {
            if (plan.Shipments.Select(x => x.Donor).Intersect(plan.Shipments.Select(x => x.Recipient)).Any()) return false;
            if (after.Sum(x => x.Stock) > before.Sum(x => x.Stock) + .02) return false;
            foreach (var group in plan.Shipments.GroupBy(x => x.Donor))
            {
                var donor = after.Single(x => x.Id == group.Key);
                if (group.Sum(x => x.Shipped) > plan.DonorLimits[group.Key] + 1e-6
                    || donor.Stock + .002 < plan.DonorFloor * donor.Capacity
                    || donor.Projected + .002 < plan.DonorFloor * donor.Capacity) return false;
            }
            foreach (var group in plan.Shipments.GroupBy(x => x.Recipient))
            {
                var recipient = before.Single(x => x.Id == group.Key);
                if (group.Sum(x => x.Delivered) > ReignWarEconomyPolicy.ReliefRecipientLimit(recipient, plan.Target) + 1e-6
                    || after.Single(x => x.Id == group.Key).Stock > recipient.Capacity + .002) return false;
            }
            foreach (var s in plan.Shipments)
            {
                double efficiency = ReignWarEconomyPolicy.RouteEfficiency(before.Single(x => x.Id == s.Donor), before.Single(x => x.Id == s.Recipient));
                if (efficiency <= 0 || s.Shipped <= 0 || s.Delivered < 0 || s.Delivered > s.Shipped
                    || Math.Abs(s.Delivered - s.Shipped * efficiency) > 1e-6) return false;
            }
            return true;
        }

        private static JObject PersistencePayload()
        {
            var behavior = ReignEconomyCampaignBehavior.Instance;
            return new JObject { ["economy"] = JObject.FromObject(behavior.EconomyState),
                ["navalShipReferences"] = new JArray(behavior.NavalShips.Select((ship, i) => new JObject {
                    ["receiptId"] = behavior.NavalShipIds[i], ["hull"] = ship.ShipHull.StringId,
                    ["randomValue"] = ship.RandomValue, ["owner"] = ship.Owner?.MobileParty?.StringId ?? ship.Owner?.Settlement?.StringId,
                    ["nativeOwnerReferenceMatches"] = ship.Owner == null || ship.Owner.Ships.Any(s => ReferenceEquals(s, ship)) })),
                ["strain"] = new JObject(Settlement.All.Where(x => x.Village != null).OrderBy(x => x.StringId, StringComparer.Ordinal)
                    .Select(x => new JProperty(x.StringId, behavior.GetMobilizationStrain(x.Village)))),
                ["legacyHearthDebited"] = behavior.RecruitHearthDebited,
                ["legacyReliefShipped"] = behavior.ReliefFoodShipped,
                ["legacyReliefDelivered"] = behavior.ReliefFoodDelivered,
                ["persistedAssistance"] = PersistedAssistance() };
        }

        private static string HashToken(JToken saved)
        {
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(saved.ToString(Formatting.None)))).Replace("-", "").ToLowerInvariant();
        }

        // Diagnostic hashes only. Keep the existing v3 digest and all actual saved values
        // unchanged. Object key order is irrelevant to these component comparisons;
        // arrays retain their order, so receipts, timers and amounts cannot disappear.
        private static JToken CanonicalObjectKeys(JToken token)
        {
            if (token is JObject obj) return new JObject(obj.Properties().OrderBy(x => x.Name, StringComparer.Ordinal)
                .Select(x => new JProperty(x.Name, CanonicalObjectKeys(x.Value))));
            if (token is JArray array) return new JArray(array.Select(CanonicalObjectKeys));
            return token.DeepClone();
        }

        private static JObject ComponentFingerprints(JObject saved)
        {
            var hashes = new JObject();
            foreach (var property in saved.Properties())
            {
                if ((property.Name == "economy" || property.Name == "persistedAssistance") && property.Value is JObject fields)
                    foreach (var field in fields.Properties())
                        hashes[property.Name + "." + field.Name] = HashToken(CanonicalObjectKeys(field.Value));
                else hashes[property.Name] = HashToken(CanonicalObjectKeys(property.Value));
            }
            return new JObject { ["schema"] = "reign-war-economy-persistence-components-v1",
                ["objectKeysCanonical"] = true, ["arrayOrderPreserved"] = true, ["hashes"] = hashes };
        }

        // Exact save-backed fields only. Serialize their values without invoking normalizing getters
        // or SyncData (which can prune history). Only the digest leaves this method's caller.
        private static JObject PersistedAssistance()
        {
            var court = ReignBeta.Court.ReignCourtCampaignBehavior.Instance;
            var government = ReignBeta.Government.ReignGovernmentCampaignBehavior.Instance;
            var accords = ReignBeta.ClanAccords.ReignClanAccordsCampaignBehavior.Instance;
            return new JObject {
                ["schema"] = "reign-war-economy-persisted-assistance-v1",
                ["courtAidAndExpeditions"] = SavedField(court, "_rulerDocketState"),
                ["shipments"] = SavedField(court, "_economicReportState"),
                ["courtOperations"] = SavedField(court, "_operations"),
                ["courtObligations"] = SavedField(court, "_obligations"),
                ["courtCommandReceipts"] = SavedField(court, "_appliedNativeCommandIds"),
                ["authorityBenefits"] = SavedField(government, "_governments"),
                ["resolutions"] = SavedField(government, "_resolutions"),
                ["edictsAndEscrow"] = SavedField(government, "_edicts"),
                ["accords"] = SavedField(accords, "_state") };
        }

        private static JToken SavedField(object owner, string fieldName)
        {
            if (owner == null) throw new InvalidOperationException("Persistence owner unavailable: " + fieldName);
            var field = AccessTools.Field(owner.GetType(), fieldName)
                ?? throw new InvalidOperationException("Persistence coverage missing: " + fieldName);
            object value = field.GetValue(owner);
            return value == null ? JValue.CreateNull() : JToken.FromObject(value);
        }

        private static JObject ProbeHousehold(JObject command)
        {
            var locality = Settlement.Find(command.Value<string>("settlementId"));
            var behavior = ReignEconomyCampaignBehavior.Instance;
            if (locality?.Town == null || locality.IsUnderSiege || locality.IsUnderRaid
                || locality.OwnerClan == Clan.PlayerClan || behavior.GetRecruitCapacity(locality) < 30)
                throw new InvalidOperationException("Household probe requires a safe non-player fortification supporting thirty recruits.");
            var owner = Hero.AllAliveHeroes.Where(x => x != Hero.MainHero && x.Clan != null && x.Clan != Clan.PlayerClan
                && x.MapFaction == locality.MapFaction && x.Clan.Leader != null && !x.Clan.IsEliminated && x.IsActive && x.Issue == null
                && !x.Clan.IsUnderMercenaryService && !x.IsPrisoner && !x.IsNotable && x.PartyBelongedTo == null
                && x.GovernorOf == null && x.CompanionOf == null && x.CanLeadParty()
                && x.Clan.Gold - x.Clan.AutoRecruitmentExpenses >= 7 * ReignWarEconomyTransactions.ClanWages(x.Clan) + 10000)
                .OrderBy(x => x.StringId, StringComparer.Ordinal).FirstOrDefault()
                ?? throw new InvalidOperationException("No safe free NPC lord with an actually funded clan; no fixture lord or treasury invented.");
            var payer = owner.Clan.Leader;
            var state = behavior.EconomyState;
            int gold = payer.Gold;
            var population = behavior.GetEligibleVillages(locality).ToDictionary(x => x,
                x => new[] { x.Hearth, behavior.GetMobilizationStrain(x) });
            var observations = new JArray();
            MobileParty created = null;
            string firstId = null, firstGeneration = null;
            bool passed = true;
            try
            {
                for (int index = 0; index < 3; index++)
                {
                    if (index == 2) payer.ChangeHeroGold(-payer.Gold);
                    int beforeGold = payer.Gold;
                    double beforeHearth = population.Keys.Sum(v => (double)v.Hearth);
                    long beforeOperation = state.NextOperation;
                    created = LordPartyComponent.CreateLordParty(owner.StringId + "_reign_economy_generation_probe", owner,
                        locality.GatePosition, 2f, locality, owner);
                    string generation = created.Id.ToString();
                    var receipts = state.Recent.Where(x => long.Parse(x.Id) > beforeOperation && x.Party == created.StringId
                        && x.Source == ReignWarEconomyTransactions.Retinue).ToList();
                    int enlisted = created.MemberRoster.TotalRegulars;
                    double hearth = beforeHearth - population.Keys.Sum(v => (double)v.Hearth);
                    int cash = beforeGold - payer.Gold;
                    state.PartyGenerations.TryGetValue(created.StringId + ":" + generation, out string status);
                    bool identity = generation != "0" && (index == 0 || (created.StringId == firstId && generation != firstGeneration))
                        && status == "committed:" + enlisted && receipts.All(x => x.PartyGeneration == generation);
                    long once = state.NextOperation;
                    new LordPartyComponent.InitializationArgs(locality.GatePosition, 2f, locality)
                        .InitializeLordPartyProperties(created, owner);
                    bool duplicate = created.MemberRoster.TotalRegulars == enlisted && payer.Gold == beforeGold - cash
                        && state.NextOperation == once
                        && Math.Abs(beforeHearth - population.Keys.Sum(v => (double)v.Hearth) - hearth) < .001;
                    bool cancellation = true;
                    if (enlisted > 0)
                    {
                        var troop = created.MemberRoster.GetTroopRoster().First(x => !x.Character.IsHero).Character;
                        int cancellationTroops = created.MemberRoster.TotalRegulars, cancellationGold = payer.Gold;
                        double cancellationHearth = population.Keys.Sum(v => (double)v.Hearth);
                        var strainBefore = population.Keys.ToDictionary(v => v, v => behavior.GetMobilizationStrain(v));
                        var trial = ReignWarEconomyTransactions.Begin(ReignWarEconomyTransactions.Retinue, locality, created, payer, troop, 1);
                        created.MemberRoster.AddToCounts(troop, 1);
                        payer.ChangeHeroGold(-trial.QuotedCost);
                        behavior.ConsumeRecruitHearth(locality, 1);
                        ReignWarEconomyTransactions.Rollback(trial, "controlled household cancellation");
                        cancellation = created.MemberRoster.TotalRegulars == cancellationTroops && payer.Gold == cancellationGold
                            && Math.Abs(cancellationHearth - population.Keys.Sum(v => (double)v.Hearth)) < .001
                            && population.Keys.All(v => Math.Abs(behavior.GetMobilizationStrain(v) - strainBefore[v]) < .001)
                            && trial.Status == "rolled_back" && trial.Accepted == 0 && trial.Hearth == 0 && trial.CashExpense == 0 && trial.Liability == 0;
                    }
                    bool conserved = receipts.Sum(x => x.Accepted) == enlisted && Math.Abs(hearth - enlisted * .5) < .001
                        && Math.Abs(receipts.Sum(x => x.CashExpense) - cash) < .001 && receipts.All(x => x.Liability == 0)
                        && state.Pending.Count == 0;
                    bool resources = index == 2 ? enlisted == 0 && cash == 0 && hearth == 0 : enlisted >= 1 && enlisted <= 15;
                    passed &= identity && duplicate && conserved && resources && cancellation;
                    observations.Add(new JObject { ["case"] = index == 0 ? "creation" : index == 1 ? "same_id_recreation" : "empty_payer",
                        ["partyId"] = created.StringId, ["generation"] = generation, ["enlisted"] = enlisted,
                        ["hearthDebited"] = hearth, ["cashExpense"] = cash, ["identityFinalized"] = identity,
                        ["registeredStatus"] = status, ["receiptGenerations"] = new JArray(receipts.Select(x => x.PartyGeneration).Distinct()),
                        ["duplicateCallbackConserved"] = duplicate, ["conserved"] = conserved, ["resourceEligibility"] = resources,
                        ["cancellationExercised"] = enlisted > 0, ["cancellationConserved"] = cancellation });
                    if (index == 0) { firstId = created.StringId; firstGeneration = generation; }
                    DestroyPartyAction.Apply(null, created);
                    created = null;
                    if (owner.PartyBelongedTo != null) throw new InvalidOperationException("Native destroyed fixture retained its hero party link.");
                }
            }
            finally
            {
                if (created?.IsActive == true) DestroyPartyAction.Apply(null, created);
                payer.ChangeHeroGold(gold - payer.Gold);
                foreach (var pair in population) behavior.RestorePopulation(pair.Key.StringId, pair.Value[0], pair.Value[1]);
            }
            return new JObject { ["ok"] = passed && observations.Count == 3, ["generationRegistrationVersion"] = 2,
                ["ownerId"] = owner.StringId, ["payerId"] = payer.StringId, ["observations"] = observations,
                ["requiresCheckpointRestore"] = true, ["nativeSideEffectsDiscardByReload"] = true,
                ["providerCalls"] = 0, ["timeAdvanced"] = false };
        }

        private static JObject ProbeRecruitment(JObject command)
        {
            var campaign = TaleWorlds.CampaignSystem.Campaign.Current;
            string source = command.Value<string>("source");
            var party = MobileParty.All.FirstOrDefault(x => x.StringId == command.Value<string>("partyId"));
            var locality = Settlement.Find(command.Value<string>("settlementId"));
            if (party == null || locality == null || !party.IsActive || party == MobileParty.MainParty
                || party.MapEvent != null || locality.IsUnderSiege || locality.IsUnderRaid)
                throw new InvalidOperationException("Probe requires an active non-player party and safe exact locality.");
            bool garrison = source == ReignWarEconomyTransactions.Garrison || source == ReignWarEconomyTransactions.BaseGarrison
                || source == ReignWarEconomyTransactions.NavalGarrison;
            if (garrison && (!party.IsGarrison || locality.Town?.GarrisonParty != party))
                throw new InvalidOperationException("Exact native garrison required.");
            // Native garrison recruitment charges the fortification's current owner.
            // An existing garrison may still carry its old creation clan after conquest.
            var payer = garrison ? locality.Town.OwnerClan?.Leader : party.LeaderHero;
            if (payer?.Clan == null || (garrison ? payer.Clan != locality.Town.OwnerClan : party.MapFaction != locality.MapFaction))
                throw new InvalidOperationException("Probe payer/locality ownership mismatch.");
            Func<JObject> observe = () => new JObject { ["troops"] = party.MemberRoster.TotalRegulars,
                ["payerGold"] = payer.Gold, ["liabilities"] = payer.Clan.AutoRecruitmentExpenses,
                ["hearth"] = Settlement.All.Where(x => x.Village != null).Sum(x => (double)x.Village.Hearth),
                ["sources"] = JObject.FromObject(ReignWarEconomyTransactions.State.Sources),
                ["pending"] = ReignWarEconomyTransactions.State.Pending.Count };
            JObject before = observe();
            if (source == ReignWarEconomyTransactions.NavalGarrison)
                return ProbeNavalRecruitment(party, locality, payer, observe);
            if (source == ReignWarEconomyTransactions.Ai)
            {
                if (party.CurrentSettlement != locality || !party.IsLordParty)
                    throw new InvalidOperationException("Volunteer probe requires the lord party to be physically present.");
                var candidate = locality.Notables.OrderBy(x => x.StringId, StringComparer.Ordinal)
                    .SelectMany(n => n.VolunteerTypes.Take(campaign.Models.VolunteerModel.MaximumIndexHeroCanRecruitFromHero(payer, n))
                        .Select((troop, index) => new { Notable = n, Troop = troop, Index = index }))
                    .FirstOrDefault(x => x.Troop != null);
                if (candidate == null) throw new InvalidOperationException("No accessible native volunteer; no fixture resources are invented.");
                var native = campaign.GetCampaignBehavior<RecruitmentCampaignBehavior>();
                var method = AccessTools.Method(typeof(RecruitmentCampaignBehavior), "ApplyInternal");
                object[] args = { party, locality, candidate.Notable, candidate.Troop, 1, candidate.Index,
                    RecruitmentCampaignBehavior.RecruitingDetail.VolunteerFromIndividual };
                method.Invoke(native, args);
                // Same volunteer callback again must consume neither troops nor money.
                var once = observe(); method.Invoke(native, args);
                if (!JToken.DeepEquals(once, observe()))
                {
                    // Block counters are expected to increase; resource observations are compared below.
                    foreach (string resource in new[] { "troops", "payerGold", "liabilities", "hearth", "pending" })
                        if (!JToken.DeepEquals(once[resource], observe()[resource]))
                            throw new InvalidOperationException("Duplicate volunteer callback changed " + resource);
                }
            }
            else if (source == ReignWarEconomyTransactions.Garrison || source == ReignWarEconomyTransactions.BaseGarrison)
            {
                var native = campaign.GetCampaignBehavior<GarrisonRecruitmentCampaignBehavior>();
                bool automatic = source == ReignWarEconomyTransactions.Garrison;
                var getter = AccessTools.Method(typeof(GarrisonRecruitmentCampaignBehavior), automatic
                    ? "GetAutoRecruitmentGarrisonChangeExplainedNumber" : "GetBaseGarrisonChangeExplainedNumber");
                var change = (TaleWorlds.CampaignSystem.ExplainedNumber)getter.Invoke(native,
                    automatic ? new object[] { locality.Town } : new object[] { locality.Town, false });
                if (change.ResultNumber > 20) throw new InvalidOperationException("Native garrison increment exceeds the bounded fixture limit of 20.");
                var method = AccessTools.Method(typeof(GarrisonRecruitmentCampaignBehavior), automatic
                    ? "TickAutoRecruitmentGarrisonChange" : "TickGarrisonChangeForTown");
                method.Invoke(native, new object[] { locality.Town });
                var once = observe(); method.Invoke(native, new object[] { locality.Town });
                if (!JToken.DeepEquals(once, observe())) throw new InvalidOperationException("Duplicate daily garrison callback changed state.");
            }
            else throw new ArgumentException("Source requires a separate native scenario.");
            JObject after = observe();
            int enlisted = after.Value<int>("troops") - before.Value<int>("troops");
            double debit = before.Value<double>("hearth") - after.Value<double>("hearth");
            double cash = before.Value<double>("payerGold") - after.Value<double>("payerGold");
            double liability = after.Value<double>("liabilities") - before.Value<double>("liabilities");
            return new JObject { ["ok"] = enlisted > 0 && Math.Abs(debit - .5 * enlisted) < .001
                    && cash >= 0 && liability >= 0 && !(cash > 0 && liability > 0) && after.Value<int>("pending") == 0,
                ["source"] = source, ["partyId"] = party.StringId, ["settlementId"] = locality.StringId,
                ["before"] = before, ["after"] = after, ["enlisted"] = enlisted, ["hearthDebited"] = debit,
                ["cashExpense"] = cash, ["newLiability"] = liability, ["providerCalls"] = 0,
                ["status"] = enlisted == 0 ? "blocked_or_already_processed_today" : "observed" };
        }

        internal static JObject VillageObservation(Village village)
        {
            var behavior = ReignEconomyCampaignBehavior.Instance;
            double recovery = behavior?.GetRecoveryModifier(village) ?? 1;
            bool functioning = village.VillageState == Village.VillageStates.Normal;
            double potential = 0;
            foreach (var recipe in village.VillageType.Productions)
                if (recipe.Item1 != null) potential += Math.Max(0, ReignVillageProductionCalculatorModel.PotentialProduction(village, recipe.Item1));
            double natural = behavior?.GetNaturalGrowth(village) ?? 0;
            double delta = village.HearthChange;
            return new JObject {
                ["villageId"] = village.StringId, ["settlementId"] = village.Settlement.StringId,
                ["boundSettlementId"] = village.Bound?.StringId, ["ownerClanId"] = village.Settlement.OwnerClan?.StringId,
                ["kingdomId"] = village.Settlement.OwnerClan?.Kingdom?.StringId, ["state"] = village.VillageState.ToString(),
                ["hearth"] = village.Hearth, ["recovery"] = recovery, ["strain"] = behavior?.GetMobilizationStrain(village) ?? 0,
                ["naturalGrowth"] = natural, ["modelHearthChange"] = delta, ["otherModelGrowth"] = delta - natural,
                ["governmentGrowth"] = ReignBeta.Government.ReignGovernmentCampaignBehavior.Instance?.GetHearthDelta(village) ?? 0,
                ["accordGrowth"] = ReignBeta.ClanAccords.ReignClanAccordsCampaignBehavior.Instance?.GetBonuses(village.Settlement.OwnerClan).HearthGrowth ?? 0,
                ["observedHearthChange"] = behavior?.EconomyState.VillageHearthChange.TryGetValue(village.StringId, out double observed) == true ? (double?)observed : null,
                ["returnedVeteranHearths"] = behavior?.EconomyState.DemobilizationHearthByVillage.TryGetValue(village.StringId, out double returnedVeterans) == true ? returnedVeterans : 0,
                ["healthyFood"] = ReignWarEconomyPolicy.HealthyFood(village.Hearth),
                ["nativeVillageTaxRatio"] = TaleWorlds.CampaignSystem.Campaign.Current.Models.SettlementTaxModel.GetVillageTaxRatio(village),
                ["actualBoundFoodBeforeEvents"] = ReignWarEconomyPolicy.Production(village.Hearth, behavior?.GetFoodRecoveryModifier(village) ?? 1, functioning),
                ["raidFood"] = ReignRaidFoodDisruption.Describe(village),
                ["npcFoodSabotageFactor"] = ReignNpcFoodSabotage.Factor(village),
                ["potentialGoods"] = potential, ["actualGoods"] = ReignVillageProductionCalculatorModel.TotalProduction(village, false),
                ["actualFoodGoods"] = ReignVillageProductionCalculatorModel.TotalProduction(village, false, true),
                ["potentialFoodGoods"] = ReignVillageProductionCalculatorModel.TotalProduction(village, true, true),
                ["supplementaryFood"] = TaleWorlds.CampaignSystem.Campaign.Current.Models.VillageProductionCalculatorModel.CalculateDailyFoodProductionAmount(village),
                ["recruitCapacity"] = behavior?.GetRecruitCapacity(village.Settlement) ?? 0 };
        }

        private static JObject CriminalPressure(bool detailed)
        {
            const int partyLimit = 8192, rowLimit = 1024;
            var active = MobileParty.All.Where(p => p.IsActive).Take(partyLimit + 1).ToList();
            bool complete = active.Count <= partyLimit;
            var rows = new JArray(); var realms = new JObject(); var errors = new JArray();
            int outlaws = 0, outlawMembers = 0, outlawShips = 0;
            foreach (var party in active.Take(partyLimit))
            {
                try
                {
                    string component = party.PartyComponent?.GetType().FullName ?? "";
                    string faction = party.MapFaction?.StringId ?? "";
                    string identity = (component + " " + faction).ToLowerInvariant();
                    string role = identity.Contains("deserter") ? "deserter"
                        : identity.Contains("pirat") ? "pirate" : identity.Contains("looter") ? "looter"
                        : party.IsBandit && party.Ships.Count > 0 ? "naval_bandit" : party.IsBandit ? "bandit" : "";
                    if (role.Length > 0)
                    {
                        outlaws++; outlawMembers += party.MemberRoster.TotalManCount; outlawShips += party.Ships.Count;
                        if (detailed && rows.Count < rowLimit) rows.Add(new JObject {
                            ["partyId"] = party.StringId, ["role"] = role, ["component"] = component,
                            ["factionId"] = faction, ["members"] = party.MemberRoster.TotalManCount,
                            ["strength"] = party.Party.EstimatedStrength, ["ships"] = party.Ships.Count,
                            ["atSea"] = party.IsCurrentlyAtSea,
                            ["tierMix"] = new JArray(party.MemberRoster.GetTroopRoster().Where(r => r.Number > 0)
                                .GroupBy(r => r.Character.Tier).OrderBy(g => g.Key).Select(g => new JObject {
                                    ["tier"] = g.Key, ["members"] = g.Sum(r => r.Number) })) });
                    }
                    string kingdom = party.ActualClan?.Kingdom?.StringId;
                    bool patrol = identity.Contains("patrol");
                    if (kingdom != null && (party.IsLordParty || patrol))
                    {
                        var r = realms[kingdom] as JObject;
                        if (r == null) realms[kingdom] = r = new JObject { ["parties"] = 0, ["members"] = 0,
                            ["strength"] = 0d, ["ships"] = 0, ["shipParties"] = 0, ["patrols"] = 0 };
                        r["parties"] = r.Value<int>("parties") + 1;
                        r["members"] = r.Value<int>("members") + party.MemberRoster.TotalManCount;
                        r["strength"] = r.Value<double>("strength") + party.Party.EstimatedStrength;
                        r["ships"] = r.Value<int>("ships") + party.Ships.Count;
                        r["shipParties"] = r.Value<int>("shipParties") + (party.Ships.Count > 0 ? 1 : 0);
                        r["patrols"] = r.Value<int>("patrols") + (patrol ? 1 : 0);
                    }
                }
                catch (Exception ex) { complete = false; if (errors.Count < 32) errors.Add(party.StringId + ":" + ex.GetType().Name); }
            }
            return new JObject { ["version"] = 1, ["day"] = CampaignTime.Now.ToDays,
                ["partyLimit"] = partyLimit, ["rowLimit"] = rowLimit, ["complete"] = complete,
                ["countsComplete"] = complete, ["rowsTruncated"] = detailed && outlaws > rowLimit,
                ["outlawParties"] = outlaws, ["outlawMembers"] = outlawMembers, ["outlawShips"] = outlawShips,
                ["parties"] = rows, ["kingdomForces"] = realms, ["errors"] = errors,
                ["classification"] = "explicit_native_bandit_flag_or_component_and_faction_deserter_pirate_looter_names",
                ["attacksAndSuppressionRequireWorldHistory"] = true, ["resourceMutations"] = 0 };
        }

        internal static JObject Capture(bool detailed = true)
        {
            var behavior = ReignEconomyCampaignBehavior.Instance;
            var state = behavior?.EconomyState;
            var villages = Settlement.All.Where(x => x.Village != null).Select(x => VillageObservation(x.Village)).ToList();
            // Native finance calls the leader's workshops without a null check.
            // Bandit factions are outside this model's payable clan economy.
            var financeClans = Clan.All.Where(x => x != null && !x.IsEliminated && !x.IsBanditFaction).ToList();
            var unobservedFinance = financeClans.Where(x => x.Leader == null).Select(x => x.StringId).ToList();
            var financeErrors = new JObject();
            var clans = new JArray();
            foreach (var clan in financeClans.Where(x => x.Leader != null))
            {
                try { clans.Add(ClanObservation(clan, state, detailed)); }
                catch (Exception ex)
                {
                    unobservedFinance.Add(clan.StringId);
                    financeErrors[clan.StringId] = ex.GetType().Name;
                    if (detailed) ReignLog.Exception("Native war economy finance observation for " + clan.StringId, ex);
                }
            }
            var result = new JObject { ["schema"] = "reign-war-economy-observation-v2", ["schemaVersion"] = ReignWarEconomyPolicy.Version,
                ["observedDay"] = CampaignTime.Now.ToDays, ["balanceCensusVersion"] = 1,
                ["nativeAssembly"] = typeof(Clan).Assembly.ManifestModule.ModuleVersionId.ToString(),
                ["providerCalls"] = 0, ["resourceMutations"] = 0, ["hooks"] = ReignEconomyPatches.BuildDiagnostics(),
                ["fundingAuditCoverage"] = ReignWarEconomyFundingAudit.Coverage(),
                ["tariffCollection"] = ReignWarEconomyTariffs.Diagnostics(detailed),
                ["nativeFoodCensus"] = ReignWarEconomyFoodMarket.DailyCensus(detailed),
                ["cohortControl"] = ReignWarEconomyCohortControl.Status(detailed),
                ["criminalPressure"] = CriminalPressure(detailed),
                ["npcFoodSabotage"] = ReignNpcFoodSabotage.Observation(detailed),
                ["reliefAllocation"] = ReignEconomyCampaignBehavior.Instance?.ReliefDiagnostics(detailed),
                ["fiscalObservationVersion"] = 2,
                ["financeSnapshotVersion"] = 1, ["financeObservationClans"] = FinanceSnapshot(clans),
                ["militaryReadinessVersion"] = 1,
                ["drawdownAllocationVersion"] = ReignWarEconomyPolicy.DrawdownAllocationVersion,
                ["militaryReadiness"] = new JArray(Kingdom.All.Where(x => !x.IsEliminated).Select(ReignMilitaryReadiness.Observation)),
                ["readinessRecruitmentBlocks"] = state?.ReadinessRecruitmentBlocks ?? 0,
                ["medicalDischarge"] = new JObject { ["version"] = ReignMedicalDischargePolicy.Version,
                    ["bindingError"] = ReignMedicalDischarge.BindingError,
                    ["coverageVersion"] = 1,
                    ["observedBattles"] = state?.MedicalObservedBattles ?? 0,
                    ["nativeRegularWounds"] = state?.MedicalNativeWounded ?? 0,
                    ["enrolledMilitaryWounds"] = state?.MedicalObservedWounded ?? 0,
                    ["eligibleWounds"] = state?.MedicalEligibleWounded ?? 0,
                    ["selectedWounds"] = state?.MedicalSelectedWounded ?? 0,
                    ["discharged"] = state?.MedicalDischarged ?? 0, ["hearthReturned"] = state?.MedicalHearthReturned ?? 0,
                    ["excludedParties"] = state?.MedicalExcluded ?? 0, ["activeEvents"] = state?.MedicalActive.Count ?? 0,
                    ["recent"] = detailed && state != null ? JArray.FromObject(state.MedicalRecent) : new JArray() },
                ["demobilizedRegulars"] = state?.DemobilizedRegulars ?? 0,
                ["demobilizationHearthReturned"] = state?.DemobilizationHearthReturned ?? 0,
                ["drawdownPending"] = state?.DrawdownPending.Count ?? 0,
                ["settlementEconomyModel"] = TaleWorlds.CampaignSystem.Campaign.Current.Models.SettlementEconomyModel.GetType().AssemblyQualifiedName,
                ["settlementTaxModel"] = TaleWorlds.CampaignSystem.Campaign.Current.Models.SettlementTaxModel.GetType().AssemblyQualifiedName,
                ["accounting"] = ReignWarEconomyTransactions.Diagnostics(detailed),
                ["unobservedFinanceClans"] = new JArray(unobservedFinance),
                ["financeObservationErrors"] = financeErrors,
                ["financeObservationComplete"] = unobservedFinance.Count == 0,
                ["naturalGrowth"] = villages.Sum(x => x.Value<double>("naturalGrowth")),
                ["otherModelGrowth"] = villages.Sum(x => x.Value<double>("otherModelGrowth")),
                ["potentialGoods"] = villages.Sum(x => x.Value<double>("potentialGoods")),
                ["actualGoods"] = villages.Sum(x => x.Value<double>("actualGoods")),
                ["wagesOwed"] = clans.Sum(x => x.Value<double>("wagesOwed")),
                ["wagesPaidAtLastNativePaymentRate"] = clans.Sum(x => x.Value<double>("wagesPaidAtLastNativePaymentRate")),
                ["observedWagesOwedSinceMigration"] = state?.Finance.Values.Sum(x => x.WagesOwed) ?? 0,
                ["observedWagesPaidSinceMigration"] = state?.Finance.Values.Sum(x => x.WagesPaid) ?? 0,
                ["wageObservationVersion"] = 2,
                ["unattributedWagePayments"] = state?.UnattributedWagePayments ?? 0,
                ["historicalWageCoverageComplete"] = false,
                ["observedTournamentGoldSinceMigration"] = state?.Finance.Values.Sum(x => x.TournamentGold) ?? 0,
                ["recruitmentLiabilities"] = clans.Sum(x => x.Value<double>("recruitmentLiabilities")),
                ["currentUnmetFood"] = state?.UnmetReliefFood ?? 0,
                ["realms"] = new JArray(Kingdom.All.Where(x => !x.IsEliminated).Select(x => {
                    var stress = ReignWarEconomyTransactions.Stress(x); stress["kingdomId"] = x.StringId; return stress; })) };
            if (detailed)
            {
                result["shipFundingProbeCandidates"] = new JArray(MobileParty.AllLordParties.Where(p => p.IsActive
                    && p.ActualClan != null && p.ActualClan != Clan.PlayerClan && p.ActualClan.Leader != null
                    && p.MapEvent == null && p.SiegeEvent == null && p.LeaderHero?.PartyBelongedTo == p
                    && p.LeaderHero.IsAlive && !p.LeaderHero.IsPrisoner)
                    .OrderBy(p => p.StringId, StringComparer.Ordinal).Select(p => new { Party = p,
                        Port = Town.AllTowns.Where(t => t.Settlement.HasPort && !t.IsUnderSiege
                            && t.OwnerClan != Clan.PlayerClan && t.Governor?.Clan != Clan.PlayerClan && t.Governor != p.ActualClan.Leader
                            && !p.MapFaction.IsAtWarWith(t.Settlement.MapFaction)
                            && t.AvailableShips.Any(s => s.IsTradeable && !s.IsUsedByQuest && s.ShipHull.Value > 0))
                            .OrderBy(t => t.StringId, StringComparer.Ordinal).FirstOrDefault() })
                    .Where(x => x.Port != null).Take(8).Select(x => new JObject {
                        ["partyId"] = x.Party.StringId, ["settlementId"] = x.Port.Settlement.StringId }));
                result["drawdownReceipts"] = state == null ? new JArray() : JArray.FromObject(state.DrawdownRecent);
                result["replacementCandidates"] = new JArray(MobileParty.AllLordParties.Where(p => p.IsActive
                    && p.ActualClan != null && p.ActualClan != Clan.PlayerClan && p.ActualClan.Kingdom != null && p.ActualClan.Leader != null
                    && p.MapEvent == null && p.SiegeEvent == null && p.Army == null && !p.IsDisbanding && !p.IsCurrentlyAtSea && !p.IsCurrentlyUsedByAQuest
                    && p.LeaderHero?.PartyBelongedTo == p && p.LeaderHero.IsAlive && !p.LeaderHero.IsPrisoner
                    && p.MemberRoster.GetTroopRoster().Where(e => !e.Character.IsHero).Sum(e => e.Number - e.WoundedNumber) >= 20)
                    .OrderBy(p => p.StringId, StringComparer.Ordinal).Take(256).Select(p => new JObject {
                        ["partyId"] = p.StringId, ["nativePartyId"] = p.Id.ToString(), ["clanId"] = p.ActualClan.StringId,
                        ["kingdomId"] = p.ActualClan.Kingdom.StringId, ["members"] = p.MemberRoster.TotalManCount,
                        ["healthyRegulars"] = p.MemberRoster.GetTroopRoster().Where(e => !e.Character.IsHero).Sum(e => e.Number - e.WoundedNumber) }));
                result["readinessProbeCandidates"] = new JArray(MobileParty.AllLordParties.Where(x => x.ActualClan != Clan.PlayerClan
                    && ReignMilitaryReadiness.SafeForDrawdown(x)).OrderBy(x => x.StringId, StringComparer.Ordinal).Take(32).Select(x => new JObject {
                        ["partyId"] = x.StringId, ["members"] = x.MemberRoster.TotalManCount,
                        ["targetFraction"] = ReignMilitaryReadiness.TargetFraction(x), ["nativeCapacity"] = x.Party.PartySizeLimit }));
                result["fundingCandidates"] = new JArray(MobileParty.All.Where(p => p.IsLordParty && p.IsActive && p.MapEvent == null && !p.IsMainParty
                    && p.LeaderHero?.IsAlive == true && !p.LeaderHero.IsPrisoner && p.LeaderHero.PartyBelongedTo == p
                    && p.LeaderHero?.Clan != null && p.LeaderHero.Clan != Clan.PlayerClan && p.LeaderHero.Clan.Leader != null
                    && p.LeaderHero != p.LeaderHero.Clan.Leader && p.LeaderHero.Clan.Kingdom != null)
                    .OrderBy(p => p.StringId, StringComparer.Ordinal).Take(32).Select(p => new JObject {
                        ["partyId"] = p.StringId, ["leaderId"] = p.LeaderHero.StringId, ["clanId"] = p.LeaderHero.Clan.StringId,
                        ["townId"] = Settlement.All.Where(t => t.IsTown && !t.IsUnderSiege
                            && t.OwnerClan != Clan.PlayerClan && t.OwnerClan != p.LeaderHero.Clan && !p.MapFaction.IsAtWarWith(t.MapFaction) && t.OwnerClan?.Leader?.Gold >= 1000)
                            .OrderBy(t => t.StringId, StringComparer.Ordinal).FirstOrDefault()?.StringId }));
                result["villages"] = new JArray(villages);
                result["clans"] = clans;
                result["holdings"] = new JArray(Settlement.All.Where(x => x.Town != null).Select(x => {
                    var h = ReignEconomyCampaignBehavior.ReliefHolding(x.Town);
                    return new JObject { ["settlementId"] = h.Id, ["kingdomId"] = x.OwnerClan?.Kingdom?.StringId,
                        ["clanId"] = x.OwnerClan?.StringId, ["foodStocks"] = h.Stock, ["capacity"] = h.Capacity,
                        ["security"] = x.Town.Security, ["loyalty"] = x.Town.Loyalty,
                        ["prosperity"] = x.Town.Prosperity,
                        ["nativeTownTaxRatio"] = TaleWorlds.CampaignSystem.Campaign.Current.Models.SettlementTaxModel.GetTownTaxRatio(x.Town),
                        ["tradeTaxAccumulated"] = x.Town.TradeTaxAccumulated,
                        ["garrisonMembers"] = x.Town.GarrisonParty?.MemberRoster.TotalManCount ?? 0,
                        ["garrisonWageQuote"] = x.Town.GarrisonParty?.TotalWage ?? 0,
                        ["garrisonWageWithoutQuarteringQuote"] = x.Town.GarrisonParty == null ? 0 : ReignEconomyPatches.ObserveWithoutQuartering(() => x.Town.GarrisonParty.TotalWage),
                        ["foodChangeWithoutQuarteringQuote"] = ReignEconomyPatches.ObserveWithoutQuartering(() =>
                            TaleWorlds.CampaignSystem.Campaign.Current.Models.SettlementFoodModel.CalculateTownFoodStocksChange(x.Town).ResultNumber),
                        ["militiaMembers"] = x.MilitiaPartyComponent?.MobileParty?.MemberRoster.TotalManCount ?? 0,
                        ["foodChange"] = h.Change, ["projectedFood"] = h.Projected, ["besieged"] = h.Besieged,
                        ["foodModel"] = HoldingFoodModelObservation(x) }; }));
                result["recruitmentCandidates"] = new JArray(MobileParty.All.Where(p => p.IsActive && p.IsLordParty
                    && p != MobileParty.MainParty && p.MapEvent == null && p.CurrentSettlement != null
                    && p.LeaderHero?.Clan != null && p.CurrentSettlement.MapFaction == p.MapFaction
                    && !p.CurrentSettlement.IsUnderSiege && !p.CurrentSettlement.IsUnderRaid)
                    .OrderBy(p => p.StringId, StringComparer.Ordinal).Take(64).Select(p => new JObject {
                        ["partyId"] = p.StringId, ["settlementId"] = p.CurrentSettlement.StringId,
                        ["payerId"] = p.LeaderHero.StringId, ["cash"] = p.LeaderHero.Gold,
                        ["capacity"] = behavior.GetRecruitCapacity(p.CurrentSettlement),
                        ["accessibleVolunteers"] = p.CurrentSettlement.Notables.Sum(n => n.VolunteerTypes
                            .Take(TaleWorlds.CampaignSystem.Campaign.Current.Models.VolunteerModel.MaximumIndexHeroCanRecruitFromHero(p.LeaderHero, n)).Count(t => t != null)) }));
                result["garrisonCandidates"] = new JArray(Settlement.All.Where(s => s.Town?.GarrisonParty != null
                    && s.OwnerClan?.Leader != null && !s.IsUnderSiege && behavior.GetRecruitCapacity(s) > 0)
                    .OrderBy(s => s.StringId, StringComparer.Ordinal).Take(160).Select(s => new JObject {
                        ["settlementId"] = s.StringId, ["partyId"] = s.Town.GarrisonParty.StringId,
                        ["clanId"] = s.OwnerClan.StringId, ["cash"] = s.OwnerClan.Leader.Gold,
                        ["troops"] = s.Town.GarrisonParty.MemberRoster.TotalRegulars,
                        ["capacity"] = behavior.GetRecruitCapacity(s) }));
                result["navalGarrisonProbeCandidates"] = new JArray(Settlement.All.Where(s => s.IsTown
                    && s.Town.GarrisonParty != null && s.Town.Governor != null && s.Town.Governor.Clan != Clan.PlayerClan
                    && s.OwnerClan?.Leader != null && s.OwnerClan != Clan.PlayerClan && !s.IsUnderSiege
                    && s.Party.MapEvent == null && behavior.GetRecruitCapacity(s) > 0)
                    .OrderBy(s => s.StringId, StringComparer.Ordinal).Take(64).Select(s => new JObject {
                        ["settlementId"] = s.StringId, ["partyId"] = s.Town.GarrisonParty.StringId,
                        ["governorId"] = s.Town.Governor.StringId, ["payerHeroId"] = s.OwnerClan.Leader.StringId,
                        ["cash"] = s.OwnerClan.Leader.Gold, ["wagePaymentLimit"] = s.GarrisonWagePaymentLimit,
                        ["nativePerkPresent"] = NavalRecruitmentPerk() != null && s.Town.Governor.GetPerkValue(NavalRecruitmentPerk()) }));
                result["productionProbeCandidates"] = new JArray(Settlement.All.Where(s => s.Village?.VillageState == Village.VillageStates.Normal
                    && s.Village.Bound?.Town != null && !s.Village.Bound.IsUnderSiege && !s.IsUnderRaid)
                    .OrderBy(s => s.StringId, StringComparer.Ordinal).Take(32).Select(s => s.StringId));
                result["raidSetupCandidates"] = new JArray(Settlement.All.Where(s => s.Village?.VillageState == Village.VillageStates.Normal
                    && s.OwnerClan != Clan.PlayerClan && s.Village.Bound?.Town != null && !s.Village.Bound.IsUnderSiege
                    && !s.IsUnderRaid && s.Party.MapEvent == null).OrderBy(s => s.StringId, StringComparer.Ordinal)
                    .Select(s => new { Settlement = s, Raider = MobileParty.All.Where(p => p.IsActive && p.IsLordParty
                        && p != MobileParty.MainParty && p.ActualClan != Clan.PlayerClan && p.MapEvent == null
                        && p.LeaderHero?.IsAlive == true && !p.LeaderHero.IsPrisoner && p.MapFaction != null
                        && s.MapFaction != null && p.MapFaction.IsAtWarWith(s.MapFaction))
                        .OrderBy(p => p.StringId, StringComparer.Ordinal).FirstOrDefault() })
                    .Where(x => x.Raider != null).Take(32).Select(x => new JObject {
                        ["settlementId"] = x.Settlement.StringId, ["partyId"] = x.Raider.StringId }));
                result["reliefProbeCandidates"] = new JArray(Settlement.All.Where(s => s.Town != null && !s.IsUnderSiege
                    && s.Town.FoodChange < 0 && .45 * s.Town.FoodStocksUpperLimit() + 7 * s.Town.FoodChange > .2 * s.Town.FoodStocksUpperLimit())
                    .OrderBy(s => s.StringId, StringComparer.Ordinal).Take(32).Select(s => s.StringId));
            }
            return result;
        }

        internal static JArray FinanceSnapshot(JArray clans)
        {
            string[] fields = { "clanId", "kingdomId", "payerHeroId", "cash", "recruitmentLiabilities", "wagesOwed",
                "dailyIncome", "dailyCashChangeModel", "observedCashChange", "fieldMembers", "sevenDayFunding" };
            return new JArray(clans.OfType<JObject>().Select(clan => new JObject(fields.Where(f => clan[f] != null)
                .Select(f => new JProperty(f, clan[f].DeepClone())))));
        }

        private static JObject HoldingFoodModelObservation(Settlement settlement)
        {
            const int maxLines = 32;
            var town = settlement.Town;
            try
            {
                var explained = town.FoodChangeExplanation;
                var lines = explained.GetLines().ToArray();
                float withoutMarket = town.FoodChangeWithoutMarketStocks;
                var foodModel = TaleWorlds.CampaignSystem.Campaign.Current.Models.SettlementFoodModel;
                var economy = ReignEconomyCampaignBehavior.Instance;
                var villages = settlement.BoundVillages;
                var roster = settlement.ItemRoster;
                int marketFood = 0;
                for (int i = 0; i < roster.Count; i++)
                {
                    var entry = roster.GetElementCopyAtIndex(i);
                    if (entry.EquipmentElement.Item?.ItemCategory?.Properties == ItemCategory.Property.BonusToFoodStores)
                        marketFood += entry.Amount;
                }
                float spy = 0;
#if !REIGN_EXCLUDE_COURT
                spy = ReignCourtCampaignBehavior.Instance?.GetSpymasterFoodFactor(town) ?? 0;
#endif
                return new JObject {
                    ["available"] = true, ["prosperity"] = town.Prosperity,
                    ["garrisonMembers"] = town.GarrisonParty?.Party.NumberOfAllMembers ?? 0,
                    ["prosperityPerFood"] = foodModel.NumberOfProsperityToEatOneFood,
                    ["garrisonMembersPerFood"] = foodModel.NumberOfMenOnGarrisonToEatOneFood,
                    ["boundVillages"] = villages.Count(),
                    ["fullyRecoveredBoundVillages"] = villages.Count(village => village.VillageState == Village.VillageStates.Normal
                        && economy != null && economy.GetRecoveryModifier(village) >= 1f),
                    ["foodChangeWithoutMarket"] = withoutMarket,
                    ["marketFoodContribution"] = explained.ResultNumber - withoutMarket,
                    ["marketFoodUnits"] = marketFood,
                    ["townMarketCash"] = town.Gold,
                    ["lastCivilianConsumption"] = ReignWarEconomyFoodMarket.Observe(town),
                    ["eventProductionFactor"] = ReignKingdomEventsCampaignBehavior.Instance?.GetFoodProductionFactor(town) ?? 0,
                    ["espionageProductionFactor"] = spy, ["lineCount"] = lines.Length,
                    ["truncated"] = lines.Length > maxLines,
                    ["lines"] = new JArray(lines.Take(maxLines).Select(line => new JObject { ["source"] = line.name, ["amount"] = line.number })) };
            }
            catch (Exception ex)
            {
                return new JObject { ["available"] = false, ["errorType"] = ex.GetType().Name };
            }
        }

        private static JObject ClanObservation(Clan clan, ReignEconomyState state, bool detailed)
        {
            double wages = ReignWarEconomyTransactions.ClanWages(clan);
            double unpaid = ReignWarEconomyTransactions.ClanMilitaryParties(clan)
                .Sum(x => Math.Max(0, x.TotalWage) * ReignWarEconomyPolicy.Clamp(x.HasUnpaidWages, 0, 1));
            var finance = TaleWorlds.CampaignSystem.Campaign.Current.Models.ClanFinanceModel;
            var income = finance.CalculateClanIncome(clan, true, false, true);
            var result = new JObject { ["clanId"] = clan.StringId, ["kingdomId"] = clan.Kingdom?.StringId,
                ["payerHeroId"] = clan.Leader.StringId,
                ["cash"] = clan.Gold, ["recruitmentLiabilities"] = clan.AutoRecruitmentExpenses,
                ["fieldParties"] = ReignWarEconomyTransactions.ClanMilitaryParties(clan).Count(p => p.IsLordParty),
                ["fieldMembers"] = ReignWarEconomyTransactions.ClanMilitaryParties(clan).Where(p => p.IsLordParty).Sum(p => p.MemberRoster.TotalManCount),
                ["wagesOwed"] = wages, ["wagesPaidAtLastNativePaymentRate"] = wages - unpaid,
                ["sevenDayFunding"] = clan.Gold - clan.AutoRecruitmentExpenses >= 7 * wages,
                ["dailyIncome"] = income.ResultNumber,
                ["incomeModelLines"] = new JArray(income.GetLines().Select(line => new JObject { ["source"] = line.name, ["amount"] = line.number })),
                ["observedFinance"] = state?.Finance.TryGetValue(clan.StringId, out var observedFinance) == true ? JObject.FromObject(observedFinance) : null,
                ["dailyCashChangeModel"] = finance.CalculateClanGoldChange(clan).ResultNumber,
                ["observedCashChange"] = state?.ClanCashChange.TryGetValue(clan.StringId, out double change) == true ? (double?)change : null,
                ["accordIncome"] = ReignBeta.ClanAccords.ReignClanAccordsCampaignBehavior.Instance?.GetBonuses(clan).TradeIncome ?? 0 };
            if (detailed)
            {
                const int maxLords = 32;
                var commanders = clan.AliveLords.Where(lord => lord != clan.Leader && lord.PartyBelongedTo?.IsLordParty == true
                    && lord.PartyBelongedTo.IsActive && lord.PartyBelongedTo.LeaderHero == lord)
                    .OrderBy(lord => lord.StringId, StringComparer.Ordinal).ToArray();
                result["fieldLordPurses"] = new JObject { ["nativeRefillThreshold"] = finance.PartyGoldLowerThreshold,
                    ["total"] = commanders.Length, ["totalCash"] = commanders.Sum(lord => (double)lord.Gold),
                    ["truncated"] = commanders.Length > maxLords,
                    ["rows"] = new JArray(commanders.Take(maxLords).Select(lord => new JObject {
                        ["heroId"] = lord.StringId, ["partyId"] = lord.PartyBelongedTo.StringId, ["cash"] = lord.Gold,
                        ["partyTradeGoldAliasesCash"] = true })) };
            }
            return result;
        }
    }
}
