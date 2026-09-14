using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ReignBeta.Integration;
using ReignBeta.Runtime;
using ReignBeta.Save;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Election;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.Siege;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace ReignBeta.Campaign
{
    public sealed class ReignRulerReputationCampaignBehavior : CampaignBehaviorBase
    {
        public static ReignRulerReputationCampaignBehavior Instance { get; private set; }
        private string _stateJson = string.Empty;
        private List<string> _stateChunks = new List<string>();
        private JObject _state = NewState();
        private JObject _governanceSettings = new JObject();
        private JObject _warScoreSettings = new JObject();

        public override void RegisterEvents()
        {
            Instance = this;
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
            CampaignEvents.DailyTickTownEvent.AddNonSerializedListener(this, OnDailyTickTown);
            CampaignEvents.DailyTickSettlementEvent.AddNonSerializedListener(this, OnDailyTickSettlement);
            CampaignEvents.WeeklyTickEvent.AddNonSerializedListener(this, OnWeeklyTick);
            CampaignEvents.RulingClanChanged.AddNonSerializedListener(this, OnRulingClanChanged);
            CampaignEvents.WarDeclared.AddNonSerializedListener(this, OnWarDeclared);
            CampaignEvents.MakePeace.AddNonSerializedListener(this, OnMakePeace);
            CampaignEvents.KingdomDestroyedEvent.AddNonSerializedListener(this, OnKingdomDestroyed);
            CampaignEvents.OnSettlementOwnerChangedEvent.AddNonSerializedListener(this, OnSettlementOwnerChanged);
            CampaignEvents.VillageLooted.AddNonSerializedListener(this, OnVillageLooted);
            CampaignEvents.VillageStateChanged.AddNonSerializedListener(this, OnVillageStateChanged);
            CampaignEvents.TownRebelliosStateChanged.AddNonSerializedListener(this, OnTownRebelliousStateChanged);
            CampaignEvents.OnSiegeEventStartedEvent.AddNonSerializedListener(this, OnSiegeStarted);
            CampaignEvents.OnSiegeEventEndedEvent.AddNonSerializedListener(this, OnSiegeEnded);
            CampaignEvents.OnClanChangedKingdomEvent.AddNonSerializedListener(this, OnClanChangedKingdom);
            CampaignEvents.OnClanDefectedEvent.AddNonSerializedListener(this, OnClanDefected);
            CampaignEvents.KingdomDecisionAdded.AddNonSerializedListener(this, OnKingdomDecisionAdded);
            CampaignEvents.KingdomDecisionConcluded.AddNonSerializedListener(this, OnKingdomDecisionConcluded);
        }

        public override void SyncData(IDataStore dataStore)
        {
            if (dataStore.IsSaving)
            {
                _stateChunks = ReignSavePayloadCodec.Encode(_state.ToString(Formatting.None));
                _stateJson = string.Empty;
            }
            dataStore.SyncData("_reign_rulerReputationState", ref _stateJson);
            dataStore.SyncData("_reign_rulerReputationStateChunks", ref _stateChunks);
            if (dataStore.IsLoading)
            {
                try
                {
                    string serialized = _stateChunks != null && _stateChunks.Count > 0
                        ? ReignSavePayloadCodec.Decode(_stateChunks)
                        : _stateJson;
                    _state = string.IsNullOrWhiteSpace(serialized) ? NewState() : JObject.Parse(serialized);
                }
                catch { _state = NewState(); }
                EnsureStateShape();
            }
        }

        private static JObject NewState()
        {
            return new JObject
            {
                ["currentTerms"] = new JObject(), ["termHistory"] = new JArray(),
                ["settlementSamples"] = new JArray(), ["villageSamples"] = new JArray(),
                ["integrations"] = new JObject(), ["villageEpisodes"] = new JObject(),
                ["siegeRecoveryUntil"] = new JObject(), ["clanMembership"] = new JObject(),
                ["pendingClanJoins"] = new JObject(), ["decisionSnapshots"] = new JObject(),
                ["wars"] = new JObject(), ["weeklyEmitted"] = new JObject(),
                ["discreteEmitted"] = new JObject()
            };
        }

        private void EnsureStateShape()
        {
            JObject defaults = NewState();
            foreach (JProperty property in defaults.Properties())
                if (_state[property.Name] == null) _state[property.Name] = property.Value;
        }

        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            // Prepared by ReignCampaignPreparationCampaignBehavior.
        }

        internal void PrepareInitialState()
        {
            InitializeSessionState();
        }

        private void InitializeSessionState()
        {
            EnsureStateShape();
            ReconcileRulerTerms(CurrentDay());
            SeedClanMembership();
            SeedActiveWars();
            _ = LoadCatalogSettingsAsync();
        }

        private async Task LoadCatalogSettingsAsync()
        {
            try
            {
                JObject catalog = await ReignServerClient.GetSocialCatalogAsync().ConfigureAwait(false);
                JObject governance = catalog?["governanceSettings"] as JObject;
                JObject war = catalog?["warScoreSettings"] as JObject;
                if (governance != null) _governanceSettings = (JObject)governance.DeepClone();
                if (war != null) _warScoreSettings = (JObject)war.DeepClone();
                ReignLog.Info("Loaded ruler reputation catalog settings revision=" + (catalog?.Value<int?>("revision") ?? 0));
            }
            catch (Exception ex)
            {
                ReignLog.Warn("Using built-in ruler reputation thresholds because the social catalog could not be loaded: " + ex.Message);
            }
        }

        private void OnDailyTick()
        {
            if (ReignCampaignInitializationGate.IsPending) return;
            double day = CurrentDay();
            ReconcileRulerTerms(day);
            UpdateWarLedgers();
            PruneSamples(day);
        }

        private void OnDailyTickTown(Town town)
        {
            if (ReignCampaignInitializationGate.IsPending) return;
            if (town?.Settlement == null || town.OwnerClan?.Kingdom == null) return;
            double day = Math.Floor(CurrentDay());
            Kingdom kingdom = town.OwnerClan.Kingdom;
            JObject term = CurrentTerm(kingdom);
            if (term == null) return;
            string id = town.Settlement.StringId;
            JArray samples = (JArray)_state["settlementSamples"];
            if (samples.OfType<JObject>().Any(x => Text(x, "settlementId") == id && Number(x, "day") == day)) return;
            bool excluded = town.IsUnderSiege || IntegrationActive(id)
                || Number((JObject)_state["siegeRecoveryUntil"], id) > day;
            samples.Add(new JObject
            {
                ["day"] = day, ["settlementId"] = id, ["kingdomId"] = kingdom.StringId,
                ["termId"] = Text(term, "termId"), ["isTown"] = town.IsTown, ["isCastle"] = town.IsCastle,
                ["prosperity"] = town.Prosperity, ["prosperityChange"] = town.ProsperityChange,
                ["prosperityLevel"] = town.GetProsperityLevel().ToString(),
                ["foodStocks"] = town.FoodStocks, ["foodCapacity"] = Math.Max(1, town.FoodStocksUpperLimit()),
                ["foodRatio"] = town.FoodStocks / Math.Max(1f, town.FoodStocksUpperLimit()),
                ["foodChange"] = town.FoodChange, ["loyalty"] = town.Loyalty,
                ["loyaltyChange"] = town.LoyaltyChange, ["security"] = town.Security,
                ["securityChange"] = town.SecurityChange, ["underSiege"] = town.IsUnderSiege,
                ["rebellious"] = town.InRebelliousState, ["ownerClanId"] = town.OwnerClan?.StringId ?? "",
                ["excluded"] = excluded
            });
        }

        private void OnDailyTickSettlement(Settlement settlement)
        {
            if (ReignCampaignInitializationGate.IsPending) return;
            Village village = settlement?.Village;
            Kingdom kingdom = village?.Bound?.OwnerClan?.Kingdom;
            if (village == null || kingdom == null) return;
            double day = Math.Floor(CurrentDay());
            JObject term = CurrentTerm(kingdom);
            if (term == null) return;
            JArray samples = (JArray)_state["villageSamples"];
            if (samples.OfType<JObject>().Any(x => Text(x, "villageId") == settlement.StringId && Number(x, "day") == day)) return;
            samples.Add(new JObject
            {
                ["day"] = day, ["villageId"] = settlement.StringId, ["kingdomId"] = kingdom.StringId,
                ["termId"] = Text(term, "termId"), ["state"] = village.VillageState.ToString(),
                ["hearth"] = village.Hearth, ["hearthChange"] = village.HearthChange,
                ["deserted"] = village.IsDeserted, ["boundSettlementId"] = village.Bound?.StringId ?? "",
                ["excluded"] = IntegrationActive(village.Bound?.StringId ?? "")
            });
        }

        private void OnWeeklyTick()
        {
            if (ReignCampaignInitializationGate.IsPending) return;
            double day = CurrentDay();
            ReconcileRulerTerms(day);
            foreach (Kingdom kingdom in Kingdom.All.Where(x => x != null && !x.IsEliminated && x.Leader != null))
            {
                JObject term = CurrentTerm(kingdom);
                if (term == null || day < Number(term, "startedDay") + GovernanceSetting("accessionGraceDays", 30d) + 7d) continue;
                EvaluateProsperity(kingdom, term, day);
                EvaluateFood(kingdom, term, day);
                EvaluateLoyalty(kingdom, term, day);
                EvaluateSecurity(kingdom, term, day);
                EvaluateVillages(kingdom, term, day);
                EvaluateIntegrations(kingdom, term, day);
                EvaluatePendingClanJoins(kingdom, term, day);
            }
            PruneWeeklyKeys(day);
        }

        private void EvaluateProsperity(Kingdom kingdom, JObject term, double day)
        {
            List<IGrouping<string, JObject>> groups = SettlementWindow(kingdom, term, day, 7d)
                .Where(x => Flag(x, "isTown")).GroupBy(x => Text(x, "settlementId")).ToList();
            if (groups.Count == 0) return;
            double nonnegativeShare = groups.Count(g => g.Count(x => Number(x, "prosperityChange") >= 0d) >= 5) / (double)groups.Count;
            List<JObject> currentLatest = groups.Select(Latest).ToList();
            List<JObject> baselineRows = SettlementBaseline(kingdom, term, day).Where(x => Flag(x, "isTown")).ToList();
            if (baselineRows.Count == 0) return;
            double currentMedian = Median(currentLatest.Select(x => Number(x, "prosperity")));
            double baselineMedian = Median(baselineRows.Select(x => Number(x, "prosperity")));
            double ratio = baselineMedian <= 0d ? 0d : (currentMedian - baselineMedian) / baselineMedian;
            double seriousShare = groups.Count(g =>
            {
                JObject latest = Latest(g);
                double ownBaseline = Median(baselineRows.Where(x => Text(x, "settlementId") == g.Key).Select(x => Number(x, "prosperity")));
                return g.Count(x => Number(x, "prosperityChange") < 0d) >= 5
                    && ownBaseline > 0d && (Number(latest, "prosperity") - ownBaseline) / ownBaseline <= -0.03d;
            }) / (double)groups.Count;
            double levelDrops = groups.Count(g => ProsperityRank(Text(g.OrderBy(x => Number(x, "day")).First(), "prosperityLevel"))
                > ProsperityRank(Text(Latest(g), "prosperityLevel"))) / (double)groups.Count;
            bool negative = (groups.Count(g => g.Count(x => Number(x, "prosperityChange") < 0d) >= 5) / (double)groups.Count >= 0.40d
                    && ratio <= -0.03d) || levelDrops >= 0.25d;
            bool levelGain = groups.Any(g => ProsperityRank(Text(g.OrderBy(x => Number(x, "day")).First(), "prosperityLevel"))
                < ProsperityRank(Text(Latest(g), "prosperityLevel")));
            bool positive = nonnegativeShare >= 0.60d && ratio >= 0.03d && seriousShare <= 0.20d || levelGain;
            EmitWeekly(kingdom, term, day, negative ? "realm_in_decline" : positive ? "realm_builder" : "",
                "prosperity", new JObject
                {
                    ["eligibleTowns"] = groups.Count, ["nonnegativeShare"] = nonnegativeShare,
                    ["medianChangeRatio"] = ratio, ["seriousDeclineShare"] = seriousShare,
                    ["nativeLevelDropShare"] = levelDrops
                });
        }

        private void EvaluateFood(Kingdom kingdom, JObject term, double day)
        {
            List<IGrouping<string, JObject>> groups = SettlementWindow(kingdom, term, day, 7d)
                .GroupBy(x => Text(x, "settlementId")).ToList();
            if (groups.Count == 0) return;
            bool anyStarving = groups.Any(g => g.Count(x => Number(x, "foodStocks") <= 0d && Number(x, "foodChange") < 0d) >= 5);
            double widespread = groups.Count(g => g.Count(x => Number(x, "foodRatio") < 0.10d && Number(x, "foodChange") < 0d) >= 5) / (double)groups.Count;
            bool negative = anyStarving || widespread >= 0.25d;
            List<JObject> latest = groups.Select(Latest).ToList();
            List<JObject> fourteen = SettlementWindow(kingdom, term, day, 14d).ToList();
            bool noStarvation = !fourteen.Any(x => Number(x, "foodStocks") <= 0d && Number(x, "foodChange") < 0d);
            bool broad = noStarvation && latest.Count(x => Number(x, "foodRatio") >= 0.30d) / (double)latest.Count >= 0.75d
                && Median(latest.Select(x => Number(x, "foodChange"))) >= 0d;
            bool recovery = groups.Any(g =>
            {
                List<JObject> history = fourteen.Where(x => Text(x, "settlementId") == g.Key).ToList();
                List<JObject> recent = g.OrderBy(x => Number(x, "day")).ToList();
                return history.Count(x => Number(x, "foodStocks") <= 0d && Number(x, "foodChange") < 0d) >= 5
                    && Number(recent.Last(), "foodRatio") >= 0.20d
                    && recent.All(x => Number(x, "foodChange") >= 0d);
            });
            EmitWeekly(kingdom, term, day, negative ? "starving_crown" : broad || recovery ? "provider_of_the_realm" : "",
                "food", new JObject
                {
                    ["eligibleFiefs"] = groups.Count, ["anyFiveDayStarvation"] = anyStarving,
                    ["widespreadCriticalShare"] = widespread, ["broadSupply"] = broad, ["recovery"] = recovery
                });
        }

        private void EvaluateLoyalty(Kingdom kingdom, JObject term, double day)
        {
            List<IGrouping<string, JObject>> groups = SettlementWindow(kingdom, term, day, 14d)
                .Where(x => Flag(x, "isTown")).GroupBy(x => Text(x, "settlementId")).ToList();
            if (groups.Count == 0) return;
            List<IGrouping<string, JObject>> seven = groups.SelectMany(g => g
                .Where(x => Number(x, "day") > day - 7d)
                .GroupBy(x => g.Key)).ToList();
            if (seven.Count == 0) return;
            double loyaltyDanger = GovernanceSetting("loyaltyDanger", 25d);
            double loyaltyStable = GovernanceSetting("loyaltyStable", 40d);
            double dangerous = seven.Count(g => Number(Latest(g), "loyalty") <= loyaltyDanger
                && g.Count(x => Number(x, "loyaltyChange") <= 0d) >= 5) / (double)seven.Count;
            bool negative = dangerous >= 0.25d;
            bool recovery = groups.Any(g =>
            {
                List<JObject> ordered = g.OrderBy(x => Number(x, "day")).ToList();
                return ordered.Any(x => Number(x, "loyalty") <= loyaltyDanger)
                    && LastRows(ordered, 7).All(x => Number(x, "loyalty") >= loyaltyStable)
                    && !Flag(ordered.Last(), "rebellious");
            });
            bool broad = groups.All(g => Number(Latest(g), "loyalty") >= 35d)
                && groups.Count(g => LastRows(g, 14).All(x => Number(x, "loyaltyChange") >= 0d)) / (double)groups.Count >= 0.75d;
            EmitWeekly(kingdom, term, day, negative ? "fractured_crown" : recovery || broad ? "unifier_of_the_realm" : "",
                "loyalty", new JObject { ["eligibleTowns"] = groups.Count, ["dangerousShare"] = dangerous, ["recovery"] = recovery, ["broadStability"] = broad });
        }

        private void EvaluateSecurity(Kingdom kingdom, JObject term, double day)
        {
            List<IGrouping<string, JObject>> groups = SettlementWindow(kingdom, term, day, 14d)
                .GroupBy(x => Text(x, "settlementId")).ToList();
            if (groups.Count == 0) return;
            List<IGrouping<string, JObject>> seven = groups.SelectMany(g => g
                .Where(x => Number(x, "day") > day - 7d)
                .GroupBy(x => g.Key)).ToList();
            if (seven.Count == 0) return;
            double securityLow = GovernanceSetting("securityLow", 30d);
            double securityCritical = GovernanceSetting("securityCritical", 15d);
            double securityRecovered = GovernanceSetting("securityRecovered", 50d);
            double securityStrong = GovernanceSetting("securityStrong", 60d);
            double lowShare = seven.Count(g => Number(Latest(g), "security") <= securityLow
                && g.Count(x => Number(x, "securityChange") <= 0d) >= 5) / (double)seven.Count;
            bool critical = seven.Any(g => g.Count(x => Number(x, "security") <= securityCritical) >= 7);
            bool negative = lowShare >= 0.25d || critical;
            bool recovery = groups.Any(g =>
            {
                List<JObject> ordered = g.OrderBy(x => Number(x, "day")).ToList();
                return ordered.Any(x => Number(x, "security") <= securityLow)
                    && LastRows(ordered, 7).All(x => Number(x, "security") >= securityRecovered);
            });
            bool broad = groups.All(g => Number(Latest(g), "security") >= 40d)
                && groups.Count(g => LastRows(g, 14).All(x => Number(x, "security") >= securityStrong)) / (double)groups.Count >= 0.75d;
            EmitWeekly(kingdom, term, day, negative ? "lawless_crown" : recovery || broad ? "keeper_of_order" : "",
                "security", new JObject { ["eligibleFiefs"] = groups.Count, ["lowShare"] = lowShare, ["critical"] = critical, ["recovery"] = recovery, ["broadOrder"] = broad });
        }

        private void EvaluateVillages(Kingdom kingdom, JObject term, double day)
        {
            JObject episodes = (JObject)_state["villageEpisodes"];
            List<JObject> currentEpisodes = episodes.Properties().Select(x => x.Value as JObject)
                .Where(x => x != null && Text(x, "kingdomId") == kingdom.StringId).ToList();
            bool negative = currentEpisodes.Any(x => day - Number(x, "lootedDay") >= 21d && !Flag(x, "recovered"));
            List<IGrouping<string, JObject>> groups = VillageWindow(kingdom, term, day, 14d)
                .GroupBy(x => Text(x, "villageId")).ToList();
            bool recovery = currentEpisodes.Any(x => Flag(x, "recovered") && !Flag(x, "credited")
                && day - Number(x, "recoveredDay") >= 7d
                && groups.Any(g => g.Key == Text(x, "villageId")
                    && Number(Latest(g), "hearth") >= 200d && LastRows(g, 7).All(r => Number(r, "hearthChange") >= 0d)));
            var parentGroups = groups.GroupBy(g => Text(Latest(g), "boundSettlementId")).ToList();
            bool broad = parentGroups.Count > 0
                && parentGroups.Count(pg => pg.Average(g => Number(Latest(g), "hearth")) >= 600d) / (double)parentGroups.Count >= 0.75d
                && parentGroups.SelectMany(pg => pg).SelectMany(g => g).Average(x => Number(x, "hearthChange")) >= 0d;
            string archetype = negative ? "lord_of_empty_fields" : recovery || broad ? "guardian_of_the_commons" : "";
            if (EmitWeekly(kingdom, term, day, archetype, "villages",
                new JObject { ["episodes"] = currentEpisodes.Count, ["unresolvedAfter21Days"] = negative, ["recovery"] = recovery, ["broadRecovery"] = broad })
                && recovery)
            {
                foreach (JObject episode in currentEpisodes.Where(x => Flag(x, "recovered"))) episode["credited"] = true;
            }
        }

        private void EvaluateIntegrations(Kingdom kingdom, JObject term, double day)
        {
            JObject integrations = (JObject)_state["integrations"];
            foreach (JProperty property in integrations.Properties().ToList())
            {
                JObject record = property.Value as JObject;
                if (record == null || Text(record, "kingdomId") != kingdom.StringId) continue;
                if (Text(record, "termId") != Text(term, "termId"))
                {
                    property.Remove();
                    continue;
                }
                double acquired = Number(record, "acquiredDay");
                Settlement settlement = Settlement.Find(property.Name);
                if (settlement?.Town == null || settlement.OwnerClan?.Kingdom != kingdom)
                {
                    property.Remove();
                    continue;
                }
                double recoveryUntil = Number((JObject)_state["siegeRecoveryUntil"], property.Name);
                if (settlement.IsUnderSiege || recoveryUntil > day) continue;
                if (day < acquired + GovernanceSetting("integrationFirstDay", 30d)) continue;
                Town town = settlement.Town;
                double foodRatio = town.FoodStocks / Math.Max(1f, town.FoodStocksUpperLimit());
                bool loyaltyGood = town.Loyalty >= GovernanceSetting("loyaltyStable", 40d);
                bool securityGood = town.Security >= GovernanceSetting("securityRecovered", 50d);
                bool foodGood = foodRatio >= 0.20d && town.FoodChange >= 0f;
                bool critical = town.Loyalty <= GovernanceSetting("loyaltyDanger", 25d)
                    || town.Security <= GovernanceSetting("securityLow", 30d)
                    || foodRatio < 0.10d && town.FoodChange < 0f;
                bool positive = !town.InRebelliousState && new[] { loyaltyGood, securityGood, foodGood }.Count(x => x) >= 2 && !critical;
                int bad = new[]
                {
                    town.Loyalty <= GovernanceSetting("loyaltyDanger", 25d) && town.LoyaltyChange <= 0f,
                    town.Security <= GovernanceSetting("securityLow", 30d) && town.SecurityChange <= 0f,
                    foodRatio < 0.10d && town.FoodChange < 0f
                }.Count(x => x);
                bool negative = town.InRebelliousState || bad >= 2;
                if (negative || positive)
                {
                    EmitDiscrete(kingdom, term, negative ? "overextended_crown" : "consolidator",
                        "integration|" + property.Name + "|" + acquired.ToString("0"),
                        negative ? "A newly acquired settlement remained dangerously unstable." : "A newly acquired settlement became stable.",
                        new JObject { ["settlementId"] = property.Name, ["foodRatio"] = foodRatio, ["loyalty"] = town.Loyalty, ["security"] = town.Security });
                    property.Remove();
                }
                else if (day >= acquired + GovernanceSetting("integrationHardExitDay", 45d)) property.Remove();
            }
        }

        private void EvaluatePendingClanJoins(Kingdom kingdom, JObject term, double day)
        {
            JObject pending = (JObject)_state["pendingClanJoins"];
            foreach (JProperty property in pending.Properties().ToList())
            {
                JObject record = property.Value as JObject;
                if (record == null || Text(record, "kingdomId") != kingdom.StringId || day < Number(record, "joinedDay") + 14d) continue;
                Clan clan = Clan.FindFirst(x => x.StringId == property.Name);
                bool valid = clan != null && clan.Kingdom == kingdom && !clan.IsUnderMercenaryService && !clan.IsRebelClan
                    && Text(record, "termId") == Text(term, "termId")
                    && day >= Number(term, "startedDay") + GovernanceSetting("accessionGraceDays", 30d);
                if (valid)
                    EmitDiscrete(kingdom, term, "gatherer_of_banners", "clan_join|" + property.Name + "|" + Number(record, "joinedDay").ToString("0"),
                        clan.Name + " joined and remained beneath " + kingdom.Name + "'s banner.",
                        new JObject { ["clanId"] = property.Name, ["retainedDays"] = day - Number(record, "joinedDay") });
                property.Remove();
            }
        }

        private void OnRulingClanChanged(Kingdom kingdom, Clan oldRulingClan)
        {
            ReconcileRulerTerm(kingdom, CurrentDay());
        }

        private void ReconcileRulerTerms(double day)
        {
            foreach (Kingdom kingdom in Kingdom.All.Where(x => x != null && !x.IsEliminated)) ReconcileRulerTerm(kingdom, day);
        }

        private void ReconcileRulerTerm(Kingdom kingdom, double day)
        {
            if (kingdom?.Leader == null || kingdom.RulingClan == null) return;
            JObject terms = (JObject)_state["currentTerms"];
            JObject current = terms[kingdom.StringId] as JObject;
            if (current != null && Text(current, "rulerId") == kingdom.Leader.StringId
                && Text(current, "rulingClanId") == kingdom.RulingClan.StringId) return;
            if (current != null)
            {
                current["endedDay"] = day;
                ((JArray)_state["termHistory"]).Add(current.DeepClone());
            }
            string termId = kingdom.StringId + "|" + kingdom.Leader.StringId + "|" + day.ToString("0.0000");
            terms[kingdom.StringId] = new JObject
            {
                ["termId"] = termId, ["kingdomId"] = kingdom.StringId,
                ["rulerId"] = kingdom.Leader.StringId, ["rulingClanId"] = kingdom.RulingClan.StringId,
                ["startedDay"] = day, ["endedDay"] = 0d
            };
            foreach (JProperty integration in ((JObject)_state["integrations"]).Properties()
                .Where(x => Text(x.Value as JObject, "kingdomId") == kingdom.StringId).ToList()) integration.Remove();
        }

        private JObject CurrentTerm(Kingdom kingdom)
        {
            return kingdom == null ? null : ((JObject)_state["currentTerms"])[kingdom.StringId] as JObject;
        }

        private void OnTownRebelliousStateChanged(Town town, bool rebellious)
        {
            Kingdom kingdom = town?.OwnerClan?.Kingdom;
            JObject term = CurrentTerm(kingdom);
            if (!rebellious || kingdom == null || term == null || InGrace(term, CurrentDay())) return;
            EmitDiscrete(kingdom, term, "fractured_crown_rebellion",
                "rebellion|" + town.Settlement.StringId + "|" + Math.Floor(CurrentDay()),
                town.Name + " entered open rebellion under " + kingdom.Leader.Name + ".",
                new JObject { ["settlementId"] = town.Settlement.StringId, ["actualRebellion"] = true });
        }

        private void OnVillageLooted(Village village)
        {
            Kingdom kingdom = village?.Bound?.OwnerClan?.Kingdom;
            JObject term = CurrentTerm(kingdom);
            if (village?.Settlement == null || kingdom == null || term == null) return;
            ((JObject)_state["villageEpisodes"])[village.Settlement.StringId] = new JObject
            {
                ["villageId"] = village.Settlement.StringId, ["kingdomId"] = kingdom.StringId,
                ["termId"] = Text(term, "termId"), ["lootedDay"] = CurrentDay(),
                ["recovered"] = false, ["credited"] = false
            };
        }

        private void OnVillageStateChanged(Village village, Village.VillageStates oldState,
            Village.VillageStates newState, TaleWorlds.CampaignSystem.Party.MobileParty raider)
        {
            if (village?.Settlement == null || newState != Village.VillageStates.Normal) return;
            JObject episode = ((JObject)_state["villageEpisodes"])[village.Settlement.StringId] as JObject;
            if (episode == null) return;
            episode["recovered"] = true;
            episode["recoveredDay"] = CurrentDay();
        }

        private void OnSiegeStarted(SiegeEvent siege)
        {
            string id = siege?.BesiegedSettlement?.StringId;
            if (!string.IsNullOrWhiteSpace(id)) ((JObject)_state["siegeRecoveryUntil"])[id] = double.MaxValue;
        }

        private void OnSiegeEnded(SiegeEvent siege)
        {
            string id = siege?.BesiegedSettlement?.StringId;
            if (!string.IsNullOrWhiteSpace(id))
                ((JObject)_state["siegeRecoveryUntil"])[id] = CurrentDay() + GovernanceSetting("postSiegeRecoveryDays", 7d);
        }

        private void OnSettlementOwnerChanged(Settlement settlement, bool openToClaim, Hero newOwner,
            Hero oldOwner, Hero capturer, ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail detail)
        {
            Kingdom newKingdom = newOwner?.Clan?.Kingdom;
            Kingdom oldKingdom = oldOwner?.Clan?.Kingdom;
            if (settlement?.Town == null || newKingdom == null || newKingdom == oldKingdom) return;
            JObject term = CurrentTerm(newKingdom);
            if (term == null) return;
            Town town = settlement.Town;
            ((JObject)_state["integrations"])[settlement.StringId] = new JObject
            {
                ["settlementId"] = settlement.StringId, ["kingdomId"] = newKingdom.StringId,
                ["termId"] = Text(term, "termId"), ["acquiredDay"] = CurrentDay(),
                ["initialProsperity"] = town.Prosperity,
                ["initialFoodRatio"] = town.FoodStocks / Math.Max(1f, town.FoodStocksUpperLimit()),
                ["initialLoyalty"] = town.Loyalty, ["initialSecurity"] = town.Security,
                ["initialRebellion"] = town.InRebelliousState, ["initialSiege"] = town.IsUnderSiege
            };
        }

        private void OnClanChangedKingdom(Clan clan, Kingdom oldKingdom, Kingdom newKingdom,
            ChangeKingdomAction.ChangeKingdomActionDetail detail, bool showNotification)
        {
            HandleClanTransition(clan, oldKingdom, newKingdom, "changed");
        }

        private void OnClanDefected(Clan clan, Kingdom oldKingdom, Kingdom newKingdom)
        {
            HandleClanTransition(clan, oldKingdom, newKingdom, "defected");
        }

        private void HandleClanTransition(Clan clan, Kingdom oldKingdom, Kingdom newKingdom, string source)
        {
            if (clan == null) return;
            double day = CurrentDay();
            string dedupe = clan.StringId + "|" + (oldKingdom?.StringId ?? "") + "|" + (newKingdom?.StringId ?? "") + "|" + Math.Floor(day);
            JObject emitted = (JObject)_state["discreteEmitted"];
            if (emitted[dedupe] != null) return;
            emitted[dedupe] = day;
            JObject membership = (JObject)_state["clanMembership"];
            JObject prior = membership[clan.StringId] as JObject;
            double joinedDay = Number(prior, "joinedDay", day);
            if (oldKingdom != null && newKingdom != oldKingdom)
            {
                JObject term = CurrentTerm(oldKingdom);
                bool valid = term != null && !InGrace(term, day) && !clan.IsUnderMercenaryService
                    && !clan.IsRebelClan && !clan.IsEliminated && !oldKingdom.IsEliminated
                    && day - joinedDay >= 30d;
                if (valid)
                    EmitDiscrete(oldKingdom, term, "scatterer_of_banners",
                        "clan_leave|" + dedupe, clan.Name + " abandoned " + oldKingdom.Name + ".",
                        new JObject { ["clanId"] = clan.StringId, ["servedDays"] = day - joinedDay, ["transitionSource"] = source });
            }
            if (newKingdom != null)
            {
                membership[clan.StringId] = new JObject { ["kingdomId"] = newKingdom.StringId, ["joinedDay"] = day };
                JObject term = CurrentTerm(newKingdom);
                if (term != null && !clan.IsUnderMercenaryService && !clan.IsRebelClan && clan != newKingdom.RulingClan)
                    ((JObject)_state["pendingClanJoins"])[clan.StringId] = new JObject
                    {
                        ["kingdomId"] = newKingdom.StringId, ["termId"] = Text(term, "termId"), ["joinedDay"] = day
                    };
            }
            else membership.Remove(clan.StringId);
        }

        private void OnKingdomDecisionAdded(KingdomDecision decision, bool isPlayerDecision)
        {
            SettlementClaimantDecision claimant = decision as SettlementClaimantDecision;
            if (claimant?.Settlement == null || claimant.Kingdom?.RulingClan == null) return;
            MBList<DecisionOutcome> initial = claimant.DetermineInitialCandidates().ToMBList();
            MBList<DecisionOutcome> narrowed = claimant.NarrowDownCandidates(initial, 3);
            JArray candidates = new JArray();
            foreach (SettlementClaimantDecision.ClanAsDecisionOutcome outcome in narrowed.OfType<SettlementClaimantDecision.ClanAsDecisionOutcome>())
            {
                Clan clan = outcome.Clan;
                if (clan == null || clan.IsUnderMercenaryService || clan.IsRebelClan || clan.IsEliminated) continue;
                double totalValue = clan.CalculateTotalSettlementValueForFaction(claimant.Kingdom);
                int adults = clan.Heroes.Count(IsAdultLivingLord);
                double load = totalValue / Math.Max(1d, clan.Tier + adults * 0.5d);
                candidates.Add(new JObject
                {
                    ["clanId"] = clan.StringId, ["landless"] = clan.Fiefs.Count == 0,
                    ["settlementValue"] = totalValue, ["load"] = load
                });
            }
            string key = claimant.Settlement.StringId + "|" + Math.Floor(CurrentDay());
            ((JObject)_state["decisionSnapshots"])[key] = new JObject
            {
                ["settlementId"] = claimant.Settlement.StringId, ["kingdomId"] = claimant.Kingdom.StringId,
                ["termId"] = Text(CurrentTerm(claimant.Kingdom), "termId"), ["day"] = CurrentDay(),
                ["candidates"] = candidates
            };
        }

        private void OnKingdomDecisionConcluded(KingdomDecision decision, DecisionOutcome outcome, bool isPlayerDecision)
        {
            SettlementClaimantDecision claimant = decision as SettlementClaimantDecision;
            SettlementClaimantDecision.ClanAsDecisionOutcome chosen = outcome as SettlementClaimantDecision.ClanAsDecisionOutcome;
            Kingdom kingdom = claimant?.Kingdom;
            JObject term = CurrentTerm(kingdom);
            if (claimant?.Settlement == null || chosen?.Clan == null || kingdom == null || term == null) return;
            JObject snapshots = (JObject)_state["decisionSnapshots"];
            JProperty property = snapshots.Properties().Where(x => Text(x.Value as JObject, "settlementId") == claimant.Settlement.StringId)
                .OrderByDescending(x => Number(x.Value as JObject, "day")).FirstOrDefault();
            JObject snapshot = property?.Value as JObject;
            property?.Remove();
            JArray candidates = snapshot?["candidates"] as JArray;
            if (candidates == null || candidates.Count < 2 || Text(snapshot, "termId") != Text(term, "termId")) return;
            List<JObject> rows = candidates.OfType<JObject>().ToList();
            JObject recipient = rows.FirstOrDefault(x => Text(x, "clanId") == chosen.Clan.StringId);
            if (recipient == null || chosen.Clan.Kingdom != kingdom) return;
            bool anyLandless = rows.Any(x => Flag(x, "landless"));
            double medianValue = Median(rows.Select(x => Number(x, "settlementValue")));
            int lowestThirdCount = Math.Max(1, (int)Math.Ceiling(rows.Count / 3d));
            HashSet<string> lowestThird = new HashSet<string>(rows.OrderBy(x => Number(x, "load")).Take(lowestThirdCount)
                .Select(x => Text(x, "clanId")), StringComparer.OrdinalIgnoreCase);
            bool rulingRecipient = chosen.Clan == kingdom.RulingClan;
            bool fair = (Flag(recipient, "landless") || !anyLandless && lowestThird.Contains(chosen.Clan.StringId))
                && !(rulingRecipient && Number(recipient, "settlementValue") > medianValue);
            bool hoarder = rulingRecipient && (rows.Any(x => Text(x, "clanId") != chosen.Clan.StringId && Flag(x, "landless"))
                || Number(recipient, "settlementValue") >= medianValue * 2d);
            string archetype = hoarder ? "hoarder_of_titles" : fair ? "fair_hand_of_the_crown" : "";
            if (string.IsNullOrWhiteSpace(archetype)) return;
            EmitDiscrete(kingdom, term, archetype,
                "fief_award|" + claimant.Settlement.StringId + "|" + Math.Floor(CurrentDay()),
                hoarder ? kingdom.Leader.Name + " accumulated another title while eligible clans lacked land."
                    : kingdom.Leader.Name + " awarded a fief to an objectively under-landed clan.",
                new JObject { ["settlementId"] = claimant.Settlement.StringId, ["recipientClanId"] = chosen.Clan.StringId, ["candidateCount"] = rows.Count });
        }

        private void SeedClanMembership()
        {
            JObject membership = (JObject)_state["clanMembership"];
            foreach (Clan clan in Clan.All.Where(x => x?.Kingdom != null))
                if (membership[clan.StringId] == null)
                    membership[clan.StringId] = new JObject
                    {
                        ["kingdomId"] = clan.Kingdom.StringId,
                        ["joinedDay"] = Math.Min(CurrentDay(), clan.LastFactionChangeTime.ToDays)
                    };
        }

        private void OnWarDeclared(IFaction first, IFaction second, DeclareWarAction.DeclareWarDetail detail)
        {
            Kingdom a = first as Kingdom;
            Kingdom b = second as Kingdom;
            if (a != null && b != null) StartWarLedger(a, b);
        }

        private void OnMakePeace(IFaction first, IFaction second, MakePeaceAction.MakePeaceDetail detail)
        {
            Kingdom a = first as Kingdom;
            Kingdom b = second as Kingdom;
            if (a == null || b == null) return;
            ResolveWar(a, b, false, null);
        }

        private void OnKingdomDestroyed(Kingdom kingdom)
        {
            if (kingdom == null) return;
            foreach (JProperty property in ((JObject)_state["wars"]).Properties().ToList())
            {
                JObject ledger = property.Value as JObject;
                if (ledger == null || Text(ledger, "kingdomAId") != kingdom.StringId && Text(ledger, "kingdomBId") != kingdom.StringId) continue;
                Kingdom other = Kingdom.All.FirstOrDefault(x => x.StringId ==
                    (Text(ledger, "kingdomAId") == kingdom.StringId ? Text(ledger, "kingdomBId") : Text(ledger, "kingdomAId")));
                if (other != null) ResolveWar(other, kingdom, true, kingdom);
            }
        }

        private void SeedActiveWars()
        {
            foreach (Kingdom a in Kingdom.All.Where(x => x != null && !x.IsEliminated))
                foreach (Kingdom b in Kingdom.All.Where(x => x != null && string.CompareOrdinal(x.StringId, a.StringId) > 0 && a.IsAtWarWith(x)))
                    if (((JObject)_state["wars"])[WarKey(a, b)] == null) StartWarLedger(a, b);
        }

        private void StartWarLedger(Kingdom a, Kingdom b)
        {
            string key = WarKey(a, b);
            if (((JObject)_state["wars"])[key] != null) return;
            ((JObject)_state["wars"])[key] = new JObject
            {
                ["kingdomAId"] = a.StringId, ["kingdomBId"] = b.StringId, ["startedDay"] = CurrentDay(),
                ["initialTownsA"] = new JArray(a.Fiefs.Where(x => x.IsTown).Select(x => x.Settlement.StringId)),
                ["initialCastlesA"] = new JArray(a.Fiefs.Where(x => x.IsCastle).Select(x => x.Settlement.StringId)),
                ["initialTownsB"] = new JArray(b.Fiefs.Where(x => x.IsTown).Select(x => x.Settlement.StringId)),
                ["initialCastlesB"] = new JArray(b.Fiefs.Where(x => x.IsCastle).Select(x => x.Settlement.StringId)),
                ["casualtiesA"] = 0, ["casualtiesB"] = 0, ["siegesA"] = 0, ["siegesB"] = 0,
                ["raidsA"] = 0, ["raidsB"] = 0, ["rulerCapturedA"] = false, ["rulerCapturedB"] = false
            };
            UpdateWarLedger(a, b, (JObject)((JObject)_state["wars"])[key]);
        }

        private void UpdateWarLedgers()
        {
            foreach (JProperty property in ((JObject)_state["wars"]).Properties().ToList())
            {
                JObject ledger = property.Value as JObject;
                Kingdom a = Kingdom.All.FirstOrDefault(x => x.StringId == Text(ledger, "kingdomAId"));
                Kingdom b = Kingdom.All.FirstOrDefault(x => x.StringId == Text(ledger, "kingdomBId"));
                if (a != null && b != null && a.IsAtWarWith(b)) UpdateWarLedger(a, b, ledger);
            }
        }

        private static void UpdateWarLedger(Kingdom a, Kingdom b, JObject ledger)
        {
            StanceLink stance = a.GetStanceWith(b);
            ledger["casualtiesA"] = stance.GetCasualties(a);
            ledger["casualtiesB"] = stance.GetCasualties(b);
            ledger["siegesA"] = stance.GetSuccessfulSieges(a);
            ledger["siegesB"] = stance.GetSuccessfulSieges(b);
            ledger["raidsA"] = stance.GetSuccessfulRaids(a);
            ledger["raidsB"] = stance.GetSuccessfulRaids(b);
            if (a.Leader?.IsPrisoner == true && a.Leader.PartyBelongedToAsPrisoner?.MapFaction == b) ledger["rulerCapturedA"] = true;
            if (b.Leader?.IsPrisoner == true && b.Leader.PartyBelongedToAsPrisoner?.MapFaction == a) ledger["rulerCapturedB"] = true;
        }

        private void ResolveWar(Kingdom first, Kingdom second, bool destroyed, Kingdom destroyedKingdom)
        {
            string key = WarKey(first, second);
            JObject wars = (JObject)_state["wars"];
            JObject ledger = wars[key] as JObject;
            if (ledger == null)
            {
                StartWarLedger(first, second);
                ledger = wars[key] as JObject;
            }
            if (ledger == null) return;
            Kingdom a = Kingdom.All.FirstOrDefault(x => x.StringId == Text(ledger, "kingdomAId")) ?? first;
            Kingdom b = Kingdom.All.FirstOrDefault(x => x.StringId == Text(ledger, "kingdomBId")) ?? second;
            if (!destroyed) UpdateWarLedger(a, b, ledger);
            double score = WarTerritoryScore(a, b, ledger);
            int casualtiesA = (int)Number(ledger, "casualtiesA");
            int casualtiesB = (int)Number(ledger, "casualtiesB");
            int totalCasualties = casualtiesA + casualtiesB;
            if (totalCasualties > 0)
                score += WarSetting("casualtyCap", 25d) * (casualtiesB - casualtiesA) / totalCasualties;
            score += WarSetting("siegeWeight", 5d) * (Number(ledger, "siegesA") - Number(ledger, "siegesB"));
            double raidScore = WarSetting("raidWeight", 1d) * (Number(ledger, "raidsA") - Number(ledger, "raidsB"));
            double raidCap = WarSetting("raidCap", 10d);
            score += Math.Max(-raidCap, Math.Min(raidCap, raidScore));
            if (Flag(ledger, "rulerCapturedB")) score += WarSetting("rulerCaptureWeight", 15d);
            if (Flag(ledger, "rulerCapturedA")) score -= WarSetting("rulerCaptureWeight", 15d);
            StanceLink peaceStance = a.GetStanceWith(b);
            int tributeA = peaceStance.GetDailyTributeToPay(a);
            int tributeB = peaceStance.GetDailyTributeToPay(b);
            if (tributeA > 0) score -= WarSetting("tributeWeight", 15d);
            if (tributeB > 0) score += WarSetting("tributeWeight", 15d);
            if (destroyed && destroyedKingdom != null) score = destroyedKingdom == a ? -10000d : 10000d;
            double victoryMargin = WarSetting("victoryMargin", 15d);
            Kingdom winner = score >= victoryMargin ? a : score <= -victoryMargin ? b : null;
            Kingdom loser = winner == a ? b : winner == b ? a : null;
            if (winner?.Leader != null && loser?.Leader != null)
            {
                string source = "war_resolution|" + key + "|" + Number(ledger, "startedDay").ToString("0.000");
                Record("war_crowned", winner.Leader, "ruler", source + "|winner",
                    winner.Leader.Name + " emerged as the ruler of the victorious kingdom.",
                    new JObject { ["score"] = score, ["opponentKingdomId"] = loser.StringId, ["destroyed"] = destroyed });
                Record("defeated_crown", loser.Leader, "ruler", source + "|loser",
                    loser.Leader.Name + " bore the crown of a defeated kingdom.",
                    new JObject { ["score"] = -score, ["opponentKingdomId"] = winner.StringId, ["destroyed"] = destroyed });
            }
            wars.Remove(key);
        }

        private double WarTerritoryScore(Kingdom a, Kingdom b, JObject ledger)
        {
            HashSet<string> initialTownsA = Strings(ledger["initialTownsA"]);
            HashSet<string> initialCastlesA = Strings(ledger["initialCastlesA"]);
            HashSet<string> initialTownsB = Strings(ledger["initialTownsB"]);
            HashSet<string> initialCastlesB = Strings(ledger["initialCastlesB"]);
            HashSet<string> currentTownsA = new HashSet<string>(a.Fiefs.Where(x => x.IsTown).Select(x => x.Settlement.StringId));
            HashSet<string> currentCastlesA = new HashSet<string>(a.Fiefs.Where(x => x.IsCastle).Select(x => x.Settlement.StringId));
            HashSet<string> currentTownsB = new HashSet<string>(b.Fiefs.Where(x => x.IsTown).Select(x => x.Settlement.StringId));
            HashSet<string> currentCastlesB = new HashSet<string>(b.Fiefs.Where(x => x.IsCastle).Select(x => x.Settlement.StringId));
            int netTownsA = currentTownsA.Count(initialTownsB.Contains) - currentTownsB.Count(initialTownsA.Contains);
            int netCastlesA = currentCastlesA.Count(initialCastlesB.Contains) - currentCastlesB.Count(initialCastlesA.Contains);
            return netTownsA * WarSetting("townWeight", 40d) + netCastlesA * WarSetting("castleWeight", 20d);
        }

        private List<JObject> SettlementWindow(Kingdom kingdom, JObject term, double day, double length)
        {
            return ((JArray)_state["settlementSamples"]).OfType<JObject>().Where(x =>
                Text(x, "kingdomId") == kingdom.StringId && Text(x, "termId") == Text(term, "termId")
                && Number(x, "day") > day - length && Number(x, "day") <= day && !Flag(x, "excluded")).ToList();
        }

        private List<JObject> SettlementBaseline(Kingdom kingdom, JObject term, double day)
        {
            return ((JArray)_state["settlementSamples"]).OfType<JObject>().Where(x =>
                Text(x, "kingdomId") == kingdom.StringId && Text(x, "termId") == Text(term, "termId")
                && Number(x, "day") > day - GovernanceSetting("sampleRetentionDays", 35d)
                && Number(x, "day") <= day - 7d && !Flag(x, "excluded")).ToList();
        }

        private List<JObject> VillageWindow(Kingdom kingdom, JObject term, double day, double length)
        {
            return ((JArray)_state["villageSamples"]).OfType<JObject>().Where(x =>
                Text(x, "kingdomId") == kingdom.StringId && Text(x, "termId") == Text(term, "termId")
                && Number(x, "day") > day - length && Number(x, "day") <= day && !Flag(x, "excluded")).ToList();
        }

        private bool EmitWeekly(Kingdom kingdom, JObject term, double day, string archetype, string domain, JObject evidence)
        {
            if (string.IsNullOrWhiteSpace(archetype) || kingdom?.Leader == null) return false;
            int week = (int)Math.Floor(day / 7d);
            string key = Text(term, "termId") + "|" + domain + "|" + week;
            JObject emitted = (JObject)_state["weeklyEmitted"];
            if (emitted[key] != null) return false;
            emitted[key] = day;
            evidence["kingdomId"] = kingdom.StringId;
            evidence["termId"] = Text(term, "termId");
            evidence["evaluationStart"] = day - 7d;
            evidence["evaluationEnd"] = day;
            Record(archetype, kingdom.Leader, "ruler", key,
                kingdom.Leader.Name + " became the subject of a kingdom-wide " + domain.Replace('_', ' ') + " report.", evidence);
            return true;
        }

        private void EmitDiscrete(Kingdom kingdom, JObject term, string archetype, string key, string summary, JObject evidence)
        {
            if (kingdom?.Leader == null || term == null || string.IsNullOrWhiteSpace(archetype)) return;
            JObject emitted = (JObject)_state["discreteEmitted"];
            if (emitted[key] != null) return;
            emitted[key] = CurrentDay();
            evidence["kingdomId"] = kingdom.StringId;
            evidence["termId"] = Text(term, "termId");
            Record(archetype, kingdom.Leader, "ruler", key, summary, evidence);
        }

        private static void Record(string archetype, Hero subject, string role, string source, string summary, JObject evidence)
        {
            ReignWorldHistoryCampaignBehavior.Instance?.RecordSocialOutcome(archetype, subject, role, source, summary, evidence);
        }

        private bool IntegrationActive(string settlementId)
        {
            return !string.IsNullOrWhiteSpace(settlementId) && ((JObject)_state["integrations"])[settlementId] != null;
        }

        private bool InGrace(JObject term, double day)
        {
            return term == null || day < Number(term, "startedDay") + GovernanceSetting("accessionGraceDays", 30d);
        }

        private void PruneSamples(double day)
        {
            double retention = GovernanceSetting("sampleRetentionDays", 35d);
            PruneArray((JArray)_state["settlementSamples"], x => Number(x, "day") < day - retention);
            PruneArray((JArray)_state["villageSamples"], x => Number(x, "day") < day - retention);
        }

        private void PruneWeeklyKeys(double day)
        {
            JObject weekly = (JObject)_state["weeklyEmitted"];
            foreach (JProperty property in weekly.Properties().Where(x => Number(weekly, x.Name) < day - 70d).ToList()) property.Remove();
            JObject discrete = (JObject)_state["discreteEmitted"];
            foreach (JProperty property in discrete.Properties().Where(x => Number(discrete, x.Name) < day - 365d).ToList()) property.Remove();
        }

        private static void PruneArray(JArray array, Func<JObject, bool> predicate)
        {
            foreach (JObject item in array.OfType<JObject>().Where(predicate).ToList()) item.Remove();
        }

        private static bool IsAdultLivingLord(Hero hero)
        {
            float adult = TaleWorlds.CampaignSystem.Campaign.Current?.Models?.AgeModel?.HeroComesOfAge ?? 18f;
            return hero != null && hero.IsAlive && hero.IsLord && hero.Age >= adult;
        }

        private static IEnumerable<JObject> LastRows(IEnumerable<JObject> rows, int count)
        {
            List<JObject> ordered = (rows ?? Enumerable.Empty<JObject>())
                .OrderBy(x => Number(x, "day")).ToList();
            return ordered.Skip(Math.Max(0, ordered.Count - Math.Max(0, count)));
        }

        private static JObject Latest(IEnumerable<JObject> rows)
        {
            return rows.OrderByDescending(x => Number(x, "day")).First();
        }

        private static double Median(IEnumerable<double> values)
        {
            List<double> list = values.OrderBy(x => x).ToList();
            if (list.Count == 0) return 0d;
            int middle = list.Count / 2;
            return list.Count % 2 == 1 ? list[middle] : (list[middle - 1] + list[middle]) / 2d;
        }

        private static int ProsperityRank(string level)
        {
            if (level.IndexOf("High", StringComparison.OrdinalIgnoreCase) >= 0) return 3;
            if (level.IndexOf("Mid", StringComparison.OrdinalIgnoreCase) >= 0) return 2;
            return 1;
        }

        private static string WarKey(Kingdom a, Kingdom b)
        {
            return string.CompareOrdinal(a.StringId, b.StringId) <= 0
                ? a.StringId + "|" + b.StringId : b.StringId + "|" + a.StringId;
        }

        private static HashSet<string> Strings(JToken token)
        {
            return new HashSet<string>((token as JArray)?.Values<string>() ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        }

        private static double CurrentDay()
        {
            return CampaignTime.Now.ToDays;
        }

        private static string Text(JObject value, string key, string fallback = "")
        {
            return value?[key]?.Value<string>() ?? fallback;
        }

        private static double Number(JObject value, string key, double fallback = 0d)
        {
            return value?[key]?.Value<double?>() ?? fallback;
        }

        private static bool Flag(JObject value, string key)
        {
            return value?[key]?.Value<bool?>() == true;
        }

        private double GovernanceSetting(string key, double fallback)
        {
            return _governanceSettings?[key]?.Value<double?>() ?? fallback;
        }

        private double WarSetting(string key, double fallback)
        {
            return _warScoreSettings?[key]?.Value<double?>() ?? fallback;
        }
    }
}
