using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ReignBeta.Integration;
using ReignBeta.Runtime;
using ReignBeta.Save;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;

namespace ReignBeta.Campaign
{
    public sealed class ReignCourtPersonalityReputationCampaignBehavior : CampaignBehaviorBase
    {
        public static ReignCourtPersonalityReputationCampaignBehavior Instance { get; private set; }
        private string _stateJson = string.Empty;
        private List<string> _stateChunks = new List<string>();
        private JObject _state = NewState();

        public override void RegisterEvents()
        {
            Instance = this;
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
            CampaignEvents.DailyTickHeroEvent.AddNonSerializedListener(this, OnDailyTickHero);
            CampaignEvents.OnChildConceivedEvent.AddNonSerializedListener(this, OnChildConceived);
            CampaignEvents.OnGivenBirthEvent.AddNonSerializedListener(this, OnGivenBirth);
        }

        public override void SyncData(IDataStore dataStore)
        {
            if (dataStore.IsSaving)
            {
                PruneStateForSave();
                _stateChunks = ReignSavePayloadCodec.Encode(_state.ToString(Formatting.None));
                _stateJson = string.Empty;
            }
            dataStore.SyncData("_reign_courtPersonalityReputationState", ref _stateJson);
            dataStore.SyncData("_reign_courtPersonalityReputationStateChunks", ref _stateChunks);
            if (dataStore.IsLoading)
            {
                try
                {
                    string serialized = _stateChunks != null && _stateChunks.Count > 0
                        ? ReignSavePayloadCodec.Decode(_stateChunks)
                        : _stateJson;
                    _state = string.IsNullOrWhiteSpace(serialized) ? NewState() : JObject.Parse(serialized);
                }
                catch
                {
                    _state = NewState();
                }
                EnsureState();
            }
        }

        private void PruneStateForSave()
        {
            EnsureState();
            JObject presence = (JObject)_state["presence"];
            foreach (JProperty property in presence.Properties().ToList())
            {
                Hero hero = FindHero(property.Name);
                if (!TryGoverningScope(hero, out _, out _))
                    property.Remove();
            }

            JObject dynastyCounts = (JObject)_state["dynastyCounts"];
            foreach (JProperty property in dynastyCounts.Properties().ToList())
                if ((property.Value.Value<int?>() ?? 0) <= 0) property.Remove();
        }

        private static JObject NewState()
        {
            return new JObject
            {
                ["presence"] = new JObject(),
                ["familySeasons"] = new JObject(),
                ["dynastyCounts"] = new JObject(),
                ["rulerFavoringPresence"] = new JObject(),
                ["lastSeasonIndex"] = -1
            };
        }

        private void EnsureState()
        {
            JObject defaults = NewState();
            foreach (JProperty property in defaults.Properties())
                if (_state[property.Name] == null) _state[property.Name] = property.Value;
        }

        internal JObject SocialBalancePersistenceSnapshot()
        {
            EnsureState();
            JObject presence = _state["presence"] as JObject ?? new JObject();
            JObject rulerFavoring = _state["rulerFavoringPresence"] as JObject ?? new JObject();
            JObject familySeasons = _state["familySeasons"] as JObject ?? new JObject();
            JObject dynastyCounts = _state["dynastyCounts"] as JObject ?? new JObject();
            return new JObject
            {
                ["schemaVersion"] = 1,
                ["presenceCount"] = presence.Properties().Count(),
                ["presenceHash"] = PersistenceHash(presence),
                ["rulerFavoringPresenceCount"] = rulerFavoring.Properties().Count(),
                ["rulerFavoringPresenceHash"] = PersistenceHash(rulerFavoring),
                ["familySeasonCount"] = familySeasons.Properties().Count(),
                ["familySeasonHash"] = PersistenceHash(familySeasons),
                ["dynastyCount"] = dynastyCounts.Properties().Count(),
                ["dynastyHash"] = PersistenceHash(dynastyCounts),
                ["lastSeasonIndex"] = _state.Value<int?>("lastSeasonIndex") ?? -1
            };
        }

        private static string PersistenceHash(JToken value)
        {
            byte[] bytes = Encoding.UTF8.GetBytes((value ?? new JObject()).ToString(Formatting.None));
            using (SHA256 sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", string.Empty).ToLowerInvariant();
        }

        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            EnsureState();
            // Season initialization is owned by the readiness coordinator.
        }

        internal void PrepareInitialState()
        {
            InitializeSeason();
        }

        private void InitializeSeason()
        {
            EnsureState();
            if (_state.Value<int?>("lastSeasonIndex") == null
                || _state.Value<int>("lastSeasonIndex") < 0)
                _state["lastSeasonIndex"] = SeasonIndex(CurrentDay());
        }

        private void OnDailyTick()
        {
            if (ReignCampaignInitializationGate.IsPending || TaleWorlds.CampaignSystem.Campaign.Current == null) return;
            EnsureState();
            int currentSeason = SeasonIndex(CurrentDay());
            int previousSeason = _state.Value<int?>("lastSeasonIndex") ?? currentSeason;
            if (previousSeason < 0)
            {
                _state["lastSeasonIndex"] = currentSeason;
                return;
            }
            while (previousSeason < currentSeason)
            {
                EvaluateCompletedSeason(previousSeason);
                previousSeason++;
            }
            _state["lastSeasonIndex"] = currentSeason;
            ProcessNpcRulerFavoring(Math.Floor(CurrentDay() + 0.000001d));
        }

        private void ProcessNpcRulerFavoring(
            double day, string forcedPairKey = "", string socialBalanceRunId = "")
        {
            JObject records = (JObject)_state["rulerFavoringPresence"];
            List<Hero> adults = Hero.AllAliveHeroes.Where(IsEligibleFavoringAdult).ToList();
            Dictionary<string, List<Hero>> byLocation = adults
                .Select(hero => new { Hero = hero, Location = FavoringLocationKey(hero) })
                .Where(item => !string.IsNullOrWhiteSpace(item.Location))
                .GroupBy(item => item.Location, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Select(item => item.Hero).ToList(),
                    StringComparer.OrdinalIgnoreCase);
            HashSet<string> activeKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Kingdom kingdom in Kingdom.All.Where(item => item != null && !item.IsEliminated))
            {
                Hero ruler = kingdom.Leader;
                if (!IsEligibleFavoringAdult(ruler) || ruler == Hero.MainHero) continue;
                string location = FavoringLocationKey(ruler);
                if (string.IsNullOrWhiteSpace(location) || !byLocation.TryGetValue(location, out List<Hero> colocated))
                    continue;
                foreach (Hero favorite in colocated.Where(hero => hero != ruler && hero != Hero.MainHero))
                {
                    string key = ruler.StringId + "|" + favorite.StringId;
                    activeKeys.Add(key);
                    ProcessNpcRulerFavoringPair(records, ruler, favorite, location, day,
                        forcedPairKey, socialBalanceRunId);
                }
            }
            foreach (JProperty property in records.Properties().ToList())
            {
                JObject record = property.Value as JObject;
                if (record == null || activeKeys.Contains(property.Name)) continue;
                if ((record.Value<double?>("lastDay") ?? -1d) < day) property.Remove();
            }
        }

        private JObject ProcessNpcRulerFavoringPair(
            JObject records, Hero ruler, Hero favorite, string location, double day,
            string forcedPairKey = "", string socialBalanceRunId = "")
        {
            string key = ruler.StringId + "|" + favorite.StringId;
            JObject record = records[key] as JObject;
            if (record == null)
            {
                record = new JObject
                {
                    ["rulerId"] = ruler.StringId, ["favoriteId"] = favorite.StringId,
                    ["consecutiveDays"] = 0, ["lastDay"] = day - 1d,
                    ["lastRollDay"] = -1d, ["locationKey"] = location
                };
                records[key] = record;
            }
            double lastDay = record.Value<double?>("lastDay") ?? day - 1d;
            bool continuous = Math.Abs(lastDay - (day - 1d)) < 0.001d
                && string.Equals(record.Value<string>("locationKey"), location, StringComparison.OrdinalIgnoreCase);
            int consecutive = continuous ? (record.Value<int?>("consecutiveDays") ?? 0) + 1 : 1;
            record["consecutiveDays"] = consecutive;
            record["lastDay"] = day;
            record["locationKey"] = location;
            if (consecutive < 11 || Math.Abs((record.Value<double?>("lastRollDay") ?? -1d) - day) < 0.001d)
                return record;
            record["lastRollDay"] = day;
            string sourceKey = "ruler_favoring_presence|" + ruler.StringId + "|"
                + favorite.StringId + "|" + day.ToString("0");
            bool forcedRareBranch = !string.IsNullOrWhiteSpace(forcedPairKey)
                && key.Equals(forcedPairKey, StringComparison.OrdinalIgnoreCase);
            RecordOccurrence("ruler_favoring_presence", ruler, "ruler",
                sourceKey,
                ruler.Name + " remained in close daily company with " + favorite.Name
                    + " for " + consecutive + " consecutive days.",
                new JObject
                {
                    ["linkedHeroId"] = favorite.StringId,
                    ["linkedHeroName"] = favorite.Name?.ToString() ?? favorite.StringId,
                    ["consecutiveDays"] = consecutive, ["locationKey"] = location,
                    ["socialBalanceTestRunId"] = socialBalanceRunId ?? string.Empty,
                    ["forcedRareBranch"] = forcedRareBranch,
                    ["sourceKey"] = sourceKey
                }, forcedRareBranch, forcedRareBranch);
            return record;
        }

        internal bool RunNpcRulerFavoringPresenceForSocialBalance(
            Hero ruler, Hero favorite, string runId, out JObject evidence, out string error)
        {
            evidence = new JObject();
            error = string.Empty;
            if (!IsEligibleFavoringAdult(ruler) || !IsEligibleFavoringAdult(favorite)
                || ruler == Hero.MainHero || favorite == Hero.MainHero || ruler == favorite
                || ruler.Clan?.Kingdom?.Leader != ruler)
            {
                error = "The controlled presence profile requires one non-player active ruler and one distinct non-player adult favorite.";
                return false;
            }
            string location = FavoringLocationKey(ruler);
            if (string.IsNullOrWhiteSpace(location)
                || !location.Equals(FavoringLocationKey(favorite), StringComparison.OrdinalIgnoreCase))
            {
                error = "The controlled ruler and favorite must share one native settlement or party location.";
                return false;
            }

            EnsureState();
            JObject records = (JObject)_state["rulerFavoringPresence"];
            string key = ruler.StringId + "|" + favorite.StringId;
            double day = Math.Floor(CurrentDay() + 0.000001d);
            records[key] = new JObject
            {
                ["rulerId"] = ruler.StringId, ["favoriteId"] = favorite.StringId,
                ["consecutiveDays"] = 5, ["lastDay"] = day - 3d,
                ["lastRollDay"] = -1d, ["locationKey"] = location
            };
            JObject resetRecord = ProcessNpcRulerFavoringPair(
                records, ruler, favorite, location, day - 1d);
            int resetCount = resetRecord.Value<int?>("consecutiveDays") ?? -1;
            long beforeDayTenSequence = ReignWorldHistoryCampaignBehavior.Instance?.Sequence ?? 0L;

            records[key] = new JObject
            {
                ["rulerId"] = ruler.StringId, ["favoriteId"] = favorite.StringId,
                ["consecutiveDays"] = 9, ["lastDay"] = day - 2d,
                ["lastRollDay"] = -1d, ["locationKey"] = location
            };
            JObject dayTenRecord = ProcessNpcRulerFavoringPair(
                records, ruler, favorite, location, day - 1d);
            int dayTenCount = dayTenRecord.Value<int?>("consecutiveDays") ?? -1;
            long dayTenSequence = ReignWorldHistoryCampaignBehavior.Instance?.Sequence ?? 0L;
            JObject finalRecord = ProcessNpcRulerFavoringPair(
                records, ruler, favorite, location, day, key, runId);
            long finalSequence = ReignWorldHistoryCampaignBehavior.Instance?.Sequence ?? 0L;
            string sourceKey = "ruler_favoring_presence|" + ruler.StringId + "|"
                + favorite.StringId + "|" + day.ToString("0");
            evidence = new JObject
            {
                ["ok"] = true, ["productionStateMachineUsed"] = true,
                ["pairProcessorRevision"] = 2, ["pairProcessorCallCount"] = 3,
                ["forcedRareBranch"] = true, ["runId"] = runId ?? string.Empty,
                ["rulerId"] = ruler.StringId, ["rulerName"] = ruler.Name?.ToString() ?? ruler.StringId,
                ["rulerClanTier"] = ruler.Clan?.Tier ?? 0,
                ["favoriteId"] = favorite.StringId, ["favoriteName"] = favorite.Name?.ToString() ?? favorite.StringId,
                ["locationKey"] = location, ["separationResetCount"] = resetCount,
                ["dayTenCount"] = dayTenCount,
                ["dayTenProducedNoEvent"] = dayTenSequence == beforeDayTenSequence,
                ["dayElevenCount"] = finalRecord.Value<int?>("consecutiveDays") ?? -1,
                ["sourceKey"] = sourceKey, ["worldHistorySequence"] = finalSequence,
                ["dayElevenQueuedExactEvent"] = finalSequence > dayTenSequence
            };
            ReignSocialBalanceHarnessCampaignBehavior.Instance?.RecordOverride(
                "npc_ruler_favoring_presence_forced", new JObject(evidence));
            return resetCount == 1 && dayTenCount == 10
                && dayTenSequence == beforeDayTenSequence && finalSequence > dayTenSequence;
        }

        private static bool IsEligibleFavoringAdult(Hero hero)
        {
            float adult = TaleWorlds.CampaignSystem.Campaign.Current?.Models?.AgeModel?.HeroComesOfAge ?? 18f;
            return hero != null && hero.IsAlive && !hero.IsChild && hero.Age >= adult
                && hero.CharacterObject != null && !hero.CharacterObject.IsTemplate;
        }

        private static string FavoringLocationKey(Hero hero)
        {
            Settlement settlement = EffectiveSettlement(hero);
            if (settlement != null) return "settlement:" + settlement.StringId;
            MobileParty party = hero?.PartyBelongedTo?.AttachedTo ?? hero?.PartyBelongedTo;
            return party == null ? string.Empty : "party:" + party.StringId;
        }

        private void OnDailyTickHero(Hero hero)
        {
            if (ReignCampaignInitializationGate.IsPending || TaleWorlds.CampaignSystem.Campaign.Current == null || hero == null) return;
            double day = Math.Floor(CurrentDay() + 0.000001d);
            ProcessPresence(hero, day);
            ProcessFamilyOpportunity(hero, day);
            ProcessDynasty(hero, day);
        }

        private void ProcessPresence(Hero hero, double day)
        {
            JObject states = (JObject)_state["presence"];
            if (!TryGoverningScope(hero, out HashSet<string> towns, out HashSet<string> fortifications))
            {
                states.Property(hero.StringId)?.Remove();
                return;
            }
            JObject record = states[hero.StringId] as JObject ?? new JObject
            {
                ["lastDay"] = -1d,
                ["attentiveDays"] = 0,
                ["absentDays"] = 0,
                ["attentiveBlocks"] = 0,
                ["absentBlocks"] = 0
            };
            states[hero.StringId] = record;
            if (record.Value<double?>("lastDay") == day) return;
            record["lastDay"] = day;

            Settlement location = EffectiveSettlement(hero);
            string locationId = location?.StringId ?? string.Empty;
            int attentive = record.Value<int?>("attentiveDays") ?? 0;
            int absent = record.Value<int?>("absentDays") ?? 0;
            if (towns.Contains(locationId))
            {
                attentive++;
                absent = 0;
            }
            else if (fortifications.Contains(locationId))
            {
                attentive = 0;
                absent = 0;
            }
            else
            {
                attentive = 0;
                absent++;
            }

            if (attentive >= 5)
            {
                attentive -= 5;
                int block = (record.Value<int?>("attentiveBlocks") ?? 0) + 1;
                record["attentiveBlocks"] = block;
                RecordOccurrence("attentive_lord", hero, "governor",
                    "court_presence|" + hero.StringId + "|attentive|" + block,
                    hero.Name + " completed five consecutive days attending towns under their authority.",
                    new JObject
                    {
                        ["completedBlockDays"] = 5,
                        ["locationId"] = locationId,
                        ["eligibleTownIds"] = new JArray(towns.OrderBy(x => x))
                    });
            }
            if (absent >= 15)
            {
                absent -= 15;
                int block = (record.Value<int?>("absentBlocks") ?? 0) + 1;
                record["absentBlocks"] = block;
                RecordOccurrence("absent_lord", hero, "governor",
                    "court_presence|" + hero.StringId + "|absent|" + block,
                    hero.Name + " completed fifteen consecutive days away from every major settlement under their authority.",
                    new JObject
                    {
                        ["completedBlockDays"] = 15,
                        ["currentLocationId"] = locationId,
                        ["governingSettlementIds"] = new JArray(fortifications.OrderBy(x => x))
                    });
            }
            record["attentiveDays"] = attentive;
            record["absentDays"] = absent;
        }

        private void ProcessFamilyOpportunity(Hero hero, double day)
        {
            Hero spouse = hero?.Spouse;
            if (spouse == null || string.Compare(hero.StringId, spouse.StringId, StringComparison.OrdinalIgnoreCase) > 0)
                return;
            int season = SeasonIndex(day);
            string pairKey = PairKey(hero, spouse);
            JObject seasons = (JObject)_state["familySeasons"];
            JObject record = seasons[pairKey] as JObject;
            if (record == null || record.Value<int?>("seasonIndex") != season)
            {
                record = new JObject
                {
                    ["seasonIndex"] = season,
                    ["heroAId"] = hero.StringId,
                    ["heroBId"] = spouse.StringId,
                    ["coLocationDays"] = 0,
                    ["conceptionOccurred"] = false,
                    ["birthOccurred"] = false,
                    ["lastOpportunityDay"] = -1d
                };
                seasons[pairKey] = record;
            }
            if (record.Value<double?>("lastOpportunityDay") == day) return;
            record["lastOpportunityDay"] = day;
            if (IsValidConceptionOpportunity(hero, spouse) && SameEligibleLocation(hero, spouse))
                record["coLocationDays"] = (record.Value<int?>("coLocationDays") ?? 0) + 1;
        }

        private void ProcessDynasty(Hero hero, double day)
        {
            if (hero == null || string.IsNullOrWhiteSpace(hero.StringId)) return;
            JObject counts = (JObject)_state["dynastyCounts"];
            int previous = counts[hero.StringId]?.Value<int>() ?? -1;
            int current = IsEligibleNoble(hero)
                ? LivingAcknowledgedChildren(hero).Count
                : 0;
            if (current == previous) return;
            if (previous < 0 && current == 0) return;
            if (current > 0) counts[hero.StringId] = current;
            else counts.Property(hero.StringId)?.Remove();
            string description = current == 1
                ? "Their dynasty is supported by one living and publicly acknowledged child."
                : "Their dynasty is supported by " + current + " living and publicly acknowledged children.";
            ReignWorldHistoryCampaignBehavior.Instance?.RecordDynamicSocialReputation(
                hero, "dynasty_secure", current > 0, Math.Min(50, current),
                "court_dynasty|" + hero.StringId + "|" + day.ToString("0") + "|" + current,
                current > 0
                    ? hero.Name + "'s publicly acknowledged living children currently secure their dynasty."
                    : hero.Name + " no longer has a living publicly acknowledged child securing their dynasty.",
                description,
                new JObject
                {
                    ["livingAcknowledgedChildCount"] = current,
                    ["childIds"] = new JArray(LivingAcknowledgedChildren(hero).Select(child => child.StringId))
                });
        }

        private void EvaluateCompletedSeason(int seasonIndex)
        {
            JObject seasons = (JObject)_state["familySeasons"];
            foreach (JProperty property in seasons.Properties().ToList())
            {
                JObject record = property.Value as JObject;
                if (record == null || record.Value<int?>("seasonIndex") != seasonIndex) continue;
                Hero first = FindHero(record.Value<string>("heroAId"));
                Hero second = FindHero(record.Value<string>("heroBId"));
                bool qualifies = first != null && second != null
                    && first.Spouse == second && second.Spouse == first
                    && IsValidConceptionOpportunity(first, second)
                    && LivingAcknowledgedChildren(first).Count == 0
                    && LivingAcknowledgedChildren(second).Count == 0
                    && (record.Value<int?>("coLocationDays") ?? 0) >= 5
                    && record.Value<bool?>("conceptionOccurred") != true
                    && record.Value<bool?>("birthOccurred") != true;
                if (qualifies)
                {
                    foreach (Hero subject in new[] { first, second })
                    {
                        RecordOccurrence("infertile", subject, "spouse",
                            "court_fertility|" + subject.StringId + "|season|" + seasonIndex,
                            subject.Name + " completed a childless season of marriage with meaningful opportunity but without conception or birth.",
                            new JObject
                            {
                                ["seasonIndex"] = seasonIndex,
                                ["coLocationDays"] = record.Value<int?>("coLocationDays") ?? 0,
                                ["spouseId"] = subject.Spouse?.StringId ?? string.Empty,
                                ["conceptionOccurred"] = false,
                                ["birthOccurred"] = false
                            });
                    }
                }
                property.Remove();
            }

            ReignFamilyCampaignBehavior family = ReignFamilyCampaignBehavior.Instance;
            if (family == null) return;
            foreach (Hero parent in Hero.AllAliveHeroes.Where(hero => IsEligibleNoble(hero)
                || hero == Hero.MainHero && IsEligibleFavoringAdult(hero)))
            {
                IReadOnlyList<Hero> bastards = family.LivingPubliclyKnownBastardChildren(parent);
                if (bastards.Count == 0) continue;
                RecordOccurrence("the_unchaste", parent, "parent",
                    "court_unchaste|" + parent.StringId + "|season|" + seasonIndex,
                    parent.Name + " remained publicly associated with a living illegitimate child during the season.",
                    new JObject
                    {
                        ["seasonIndex"] = seasonIndex,
                        ["publiclyKnownLivingBastardCount"] = bastards.Count,
                        ["childIds"] = new JArray(bastards.Select(child => child.StringId))
                    });
            }
        }

        private void OnChildConceived(Hero mother)
        {
            if (mother == null) return;
            MarkFamilyEvent(mother, mother.Spouse, "conceptionOccurred");
            CorrectInfertileRumor(mother, "pregnancy");
            CorrectInfertileRumor(mother.Spouse, "spouse_pregnancy");
        }

        private void OnGivenBirth(Hero mother, List<Hero> children, int stillborn)
        {
            if (mother == null) return;
            MarkFamilyEvent(mother, mother.Spouse, "birthOccurred");
            CorrectInfertileRumor(mother, "birth");
            CorrectInfertileRumor(mother.Spouse, "spouse_birth");
            ProcessDynasty(mother, Math.Floor(CurrentDay()));
            if (mother.Spouse != null) ProcessDynasty(mother.Spouse, Math.Floor(CurrentDay()));
        }

        private void MarkFamilyEvent(Hero first, Hero second, string key)
        {
            if (first == null || second == null) return;
            string pairKey = PairKey(first, second);
            JObject record = ((JObject)_state["familySeasons"])[pairKey] as JObject;
            if (record != null && record.Value<int?>("seasonIndex") == SeasonIndex(CurrentDay()))
                record[key] = true;
        }

        private static void CorrectInfertileRumor(Hero subject, string source)
        {
            if (subject == null) return;
            double day = Math.Floor(CurrentDay());
            ReignWorldHistoryCampaignBehavior.Instance?.RecordSocialRumorCorrection(
                subject, "infertile",
                "court_fertility_correction|" + subject.StringId + "|" + source + "|" + day.ToString("0"),
                subject.Name + "'s pregnancy or childbirth contradicted active claims of infertility.",
                new JObject { ["correctionSource"] = source });
        }

        private static bool TryGoverningScope(
            Hero hero,
            out HashSet<string> towns,
            out HashSet<string> fortifications)
        {
            towns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            fortifications = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!IsEligibleNoble(hero) || !hero.IsActive || hero.IsPrisoner
                || hero.Clan == null || hero.Clan.IsUnderMercenaryService || hero.Clan.IsRebelClan)
                return false;
            IEnumerable<Town> governed;
            Kingdom kingdom = hero.Clan.Kingdom;
            if (kingdom?.Leader == hero)
                governed = kingdom.Fiefs;
            else if (hero.Clan.Leader == hero)
                governed = hero.Clan.Fiefs;
            else
                return false;
            foreach (Town town in governed.Where(x => x?.Settlement != null))
            {
                fortifications.Add(town.Settlement.StringId);
                if (town.IsTown) towns.Add(town.Settlement.StringId);
            }
            return fortifications.Count > 0;
        }

        private static bool IsValidConceptionOpportunity(Hero first, Hero second)
        {
            if (!IsEligibleNoble(first) || !IsEligibleNoble(second)
                || first.Spouse != second || second.Spouse != first
                || first.IsPrisoner || second.IsPrisoner || !first.IsActive || !second.IsActive
                || first.IsPregnant || second.IsPregnant)
                return false;
            Hero woman = first.IsFemale ? first : second.IsFemale ? second : null;
            Hero man = first.IsFemale ? second : second.IsFemale ? first : null;
            if (woman == null || man == null || woman.Clan == null) return false;
            try
            {
                return TaleWorlds.CampaignSystem.Campaign.Current?.Models?.PregnancyModel != null
                    && TaleWorlds.CampaignSystem.Campaign.Current.Models.PregnancyModel.GetDailyChanceOfPregnancyForHero(woman) > 0f;
            }
            catch
            {
                return woman.Age >= 18f && woman.Age <= 45f;
            }
        }

        private static bool SameEligibleLocation(Hero first, Hero second)
        {
            Settlement firstSettlement = EffectiveSettlement(first);
            Settlement secondSettlement = EffectiveSettlement(second);
            if (firstSettlement != null && firstSettlement == secondSettlement) return true;
            MobileParty firstParty = first?.PartyBelongedTo?.AttachedTo ?? first?.PartyBelongedTo;
            MobileParty secondParty = second?.PartyBelongedTo?.AttachedTo ?? second?.PartyBelongedTo;
            return firstParty != null && firstParty == secondParty;
        }

        private static Settlement EffectiveSettlement(Hero hero)
        {
            return hero?.CurrentSettlement
                ?? hero?.PartyBelongedTo?.CurrentSettlement
                ?? hero?.PartyBelongedTo?.AttachedTo?.CurrentSettlement;
        }

        private static IReadOnlyList<Hero> LivingAcknowledgedChildren(Hero parent)
        {
            ReignFamilyCampaignBehavior family = ReignFamilyCampaignBehavior.Instance;
            return family != null
                ? family.LivingPubliclyAcknowledgedChildren(parent)
                : (parent?.Children ?? new List<Hero>()).Where(child => child != null && child.IsAlive).ToList();
        }

        private static bool IsEligibleNoble(Hero hero)
        {
            float adult = TaleWorlds.CampaignSystem.Campaign.Current?.Models?.AgeModel?.HeroComesOfAge ?? 18f;
            return hero != null && hero.IsAlive && !hero.IsChild && hero.Age >= adult
                && hero.IsLord && hero.Clan != null && !hero.Clan.IsEliminated;
        }

        private static Hero FindHero(string id)
        {
            return string.IsNullOrWhiteSpace(id) ? null
                : Hero.FindFirst(hero => string.Equals(hero?.StringId, id, StringComparison.OrdinalIgnoreCase));
        }

        private static string PairKey(Hero first, Hero second)
        {
            return string.Compare(first.StringId, second.StringId, StringComparison.OrdinalIgnoreCase) <= 0
                ? first.StringId + "|" + second.StringId
                : second.StringId + "|" + first.StringId;
        }

        private static int SeasonIndex(double day)
        {
            return Math.Max(0, (int)Math.Floor(Math.Max(0d, day) / ReignCalendarService.DaysPerSeason));
        }

        private static double CurrentDay()
        {
            return CampaignTime.Now.ToDays;
        }

        internal bool RecordUnchasteForSocialBalance(
            Hero parent,
            int seasonIndex,
            string runId,
            out JObject evidence,
            out string error)
        {
            evidence = new JObject();
            error = string.Empty;
            ReignFamilyCampaignBehavior family = ReignFamilyCampaignBehavior.Instance;
            if (family == null || !IsEligibleNoble(parent))
            {
                error = "A living adult noble parent and the Reign family behavior are required.";
                return false;
            }
            IReadOnlyList<Hero> bastards = family.LivingPubliclyKnownBastardChildren(parent);
            if (bastards.Count == 0)
            {
                error = "The selected parent has no living publicly known illegitimate child.";
                return false;
            }
            string sourceKey = "court_unchaste_test|" + (runId ?? string.Empty)
                + "|" + parent.StringId + "|season|" + seasonIndex;
            evidence = new JObject
            {
                ["seasonIndex"] = seasonIndex,
                ["publiclyKnownLivingBastardCount"] = bastards.Count,
                ["childIds"] = new JArray(bastards.Select(child => child.StringId)),
                ["socialBalanceTestRunId"] = runId ?? string.Empty,
                ["forcedRareBranch"] = true,
                ["sourceKey"] = sourceKey
            };
            RecordOccurrence("the_unchaste", parent, "parent", sourceKey,
                parent.Name + " remained publicly associated with a living illegitimate child during the season.",
                evidence, true, true);
            return true;
        }

        private static void RecordOccurrence(
            string archetype,
            Hero subject,
            string role,
            string source,
            string summary,
            JObject evidence,
            bool forceExposure = false,
            bool forcePromotion = false)
        {
            ReignWorldHistoryCampaignBehavior.Instance?.RecordSocialOutcome(
                archetype, subject, role, source, summary, evidence, null,
                forceExposure, forcePromotion);
        }
    }
}
