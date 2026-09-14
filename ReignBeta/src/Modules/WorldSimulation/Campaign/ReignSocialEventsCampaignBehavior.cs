using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ReignBeta.Events;
using ReignBeta.Integration;
using ReignBeta.Runtime;
using ReignBeta.Settings;
using ReignBeta.UI;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameState;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.TournamentGames;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace ReignBeta.Campaign
{
    public sealed class ReignSocialEventsCampaignBehavior : CampaignBehaviorBase
    {
        private const float MinimumDaysBetweenScheduleAttempts = 3f;
        private const float MinimumDaysBetweenGeneratedWildernessEvents = 3f;
        private const float GeneratedWildernessHourlyChance = 0.02f;
        private const float TournamentVictoryCelebrationDays = 0.5f;
        private const int MaxOpenEvents = 2;
        private const int MaxEventAttendees = 25;
        private const int MaxGeneratedWildernessParticipants = 3;
        private const string TournamentCelebrationEventIdPrefix = "reign_tournament_";
        private const string TournamentCelebrationAfterEventIdPrefix = "reign_tournament_after_";
        private const string GeneratedWildernessEventIdPrefix = "reign_wilderness_";

        private List<SocialEventRecord> _events = new List<SocialEventRecord>();
        private float _lastScheduleAttemptDay = -1000f;
        private float _lastGeneratedWildernessEventAttemptDay = -1000f;
        private ReignSocialEventSession _currentSession;
        private bool _skipFirstDailyTickAfterLoad;
        private bool _generatedWildernessRequestRunning;

        public static ReignSocialEventsCampaignBehavior Instance { get; private set; }

        public override void RegisterEvents()
        {
            Instance = this;
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
            CampaignEvents.HourlyTickEvent.AddNonSerializedListener(this, OnHourlyTick);
            CampaignEvents.TournamentStarted.AddNonSerializedListener(this, OnTournamentStarted);
            CampaignEvents.TournamentFinished.AddNonSerializedListener(this, OnTournamentFinished);
            CampaignEvents.TournamentCancelled.AddNonSerializedListener(this, OnTournamentCancelled);
        }

        public override void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData("_reign_social_events", ref _events);
            dataStore.SyncData("_reign_social_last_schedule_day", ref _lastScheduleAttemptDay);
            dataStore.SyncData("_reign_social_last_generated_wilderness_day", ref _lastGeneratedWildernessEventAttemptDay);
            if (_events == null)
            {
                _events = new List<SocialEventRecord>();
            }
        }

        public void ForceEventInCurrentTown()
        {
            ReignLog.Info("ForceEventInCurrentTown invoked.");
            if (!SettingsAllowEvents())
            {
                Show("Bannerlord Reign events are disabled in MCM.");
                return;
            }

            Settlement settlement = Settlement.CurrentSettlement ?? MobileParty.MainParty?.CurrentSettlement;
            if (settlement?.Town == null || !settlement.IsTown)
            {
                Show("Enter a town first, then use Force Event In Current Town.");
                return;
            }

            if (TryCreateEventForTown(settlement.Town, CurrentCampaignDay(), true))
            {
                _lastScheduleAttemptDay = CurrentCampaignDay();
            }
        }

        public bool StartRelationshipDevelopmentEncounter(Hero npc, ReignRelationshipDevelopment development)
        {
            if (npc == null || development == null || Hero.MainHero == null || ReignSocialEventScreenManager.IsOpen || ReignCorrespondenceScreenManager.IsOpen)
            {
                return false;
            }

            float now = CurrentCampaignDay();
            Settlement settlement = Settlement.CurrentSettlement ?? MobileParty.MainParty?.CurrentSettlement;
            string location = settlement?.Name?.ToString() ?? "On the road";
            SocialEventRecord record = new SocialEventRecord
            {
                EventId = "reign_relationship_" + development.DevelopmentId,
                TemplateId = "generated_wilderness",
                DisplayName = "A Personal Matter",
                SettlementStringId = settlement?.StringId ?? string.Empty,
                HostHeroStringId = npc.StringId,
                AttendeeHeroStringIds = new List<string> { npc.StringId },
                MovedHeroStringIds = new List<string>(),
                AnnouncementDay = now,
                ExpiresDay = now + 0.25f,
                RequiredPlayerClanTier = 0,
                Status = SocialEventStatus.Attended,
                AnnouncementSent = true,
                IsGeneratedWildernessEvent = true,
                GeneratedTerrainKey = "plain",
                GeneratedLocationText = location,
                GeneratedTimeOfDayText = GetGeneratedWildernessTimeOfDayText(now),
                GeneratedTitle = "A Personal Matter",
                GeneratedApproachText = npc.Name + " asks to speak with you privately.",
                GeneratedOpeningText = string.Empty,
                GeneratedPlayerHook = "The matter cannot be left unspoken.",
                GeneratedEmotionalPressure = development.Polarity ?? "mixed",
                GeneratedSurfaceClues = string.Empty,
                GeneratedHiddenContext = development.Summary ?? string.Empty,
                GeneratedDiscoveryRoutes = "The player may address the matter directly, evade it, apologize, challenge the character, or end the exchange."
            };

            _events.Add(record);
            _currentSession = new ReignSocialEventSession(record);
            ReignLog.Info("Relationship development encounter opened id=" + development.DevelopmentId + " hero=" + npc.StringId + ".");
            ReignSocialEventScreenManager.Open(_currentSession, ResolveCurrentSession);
            return true;
        }

        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            _skipFirstDailyTickAfterLoad = true;
            starter.AddGameMenuOption(
                "town",
                "reign_return_social_event",
                "{=!}Return to the Bannerlord Reign social event",
                ReturnCurrentEventOnCondition,
                ReturnCurrentEventOnConsequence,
                false,
                4);

            starter.AddGameMenuOption(
                "town",
                "reign_attend_social_event",
                "{=!}Attend the announced Bannerlord Reign social event",
                AttendEventOnCondition,
                AttendEventOnConsequence,
                false,
                4);

            // Loaded-session repair is owned by the readiness coordinator.
        }

        internal void PrepareInitialState()
        {
            RepairLoadedSessionState();
        }

        private void RepairLoadedSessionState()
        {
            RepairAttendeeRosters();
            RepairTournamentCelebrationPresentation();
        }

        private void OnDailyTick()
        {
            if (ReignCampaignInitializationGate.IsPending) return;
            if (!SettingsAllowEvents())
            {
                return;
            }

            float now = CurrentCampaignDay();
            RefreshTournamentCelebrations(now);
            ExpireOldEvents(now);
            PruneClosedEvents();

            if (ReignBetaSettings.Instance?.TournamentCelebrationsEnabled != false)
            {
                TryScheduleTournamentCelebrations(now);
            }

            if (_skipFirstDailyTickAfterLoad)
            {
                _skipFirstDailyTickAfterLoad = false;
                return;
            }

            if (now - _lastScheduleAttemptDay < MinimumDaysBetweenScheduleAttempts)
            {
                return;
            }

            _lastScheduleAttemptDay = now;
            if (_events.Count(x => x.IsOpen(now)) >= MaxOpenEvents)
            {
                return;
            }

            if (MBRandom.RandomFloat > 0.25f)
            {
                return;
            }

            Town town = PickScheduleTown();
            if (town != null)
            {
                TryCreateEventForTown(town, now, false);
            }
        }

        private void OnHourlyTick()
        {
            if (ReignCampaignInitializationGate.IsPending) return;
            ReignBetaSettings settings = ReignBetaSettings.Instance;
            if (settings != null && (!settings.Enabled || !settings.EventsEnabled || !settings.GeneratedWildernessEventsEnabled))
            {
                return;
            }

            float now = CurrentCampaignDay();
            if (now - _lastGeneratedWildernessEventAttemptDay < MinimumDaysBetweenGeneratedWildernessEvents)
            {
                return;
            }

            if (MBRandom.RandomFloat <= GeneratedWildernessHourlyChance)
            {
                _ = TryStartGeneratedWildernessEventAsync(now, false);
            }
        }

        public void ForceGeneratedWildernessEvent()
        {
            ReignLog.Info("ForceGeneratedWildernessEvent invoked.");
            if (!SettingsAllowEvents())
            {
                Show("Bannerlord Reign events are disabled in MCM.");
                return;
            }

            if (ReignBetaSettings.Instance != null && !ReignBetaSettings.Instance.GeneratedWildernessEventsEnabled)
            {
                Show("Generated wilderness events are disabled in MCM.");
                return;
            }

            _ = TryStartGeneratedWildernessEventAsync(CurrentCampaignDay(), true);
        }

        public IReadOnlyList<SocialEventRecord> GetLiveTestEventRecords()
        {
            return (_events ?? new List<SocialEventRecord>()).Where(record => record != null).ToList();
        }

        public ReignSocialEventSession OpenLiveTestSocialEvent(string eventId)
        {
            float now = CurrentCampaignDay();
            SocialEventRecord record = (_events ?? new List<SocialEventRecord>()).FirstOrDefault(candidate => candidate != null
                && string.Equals(candidate.EventId, eventId, StringComparison.OrdinalIgnoreCase)
                && (candidate.IsOpen(now) || candidate.Status == SocialEventStatus.Attended));
            if (record == null) return null;
            record.Status = SocialEventStatus.Attended;
            _currentSession = new ReignSocialEventSession(record);
            return _currentSession;
        }

        public ReignSocialEventSession CreateLiveTestSocialEvent(IEnumerable<Hero> requestedHeroes, string templateId, out string error)
        {
            error = string.Empty;
            Settlement settlement = Settlement.CurrentSettlement ?? MobileParty.MainParty?.CurrentSettlement;
            if (settlement?.Town == null || !settlement.IsTown)
            {
                error = "A scheduled social-event test requires the player to be in a town.";
                return null;
            }
            SocialEventTemplate template = !string.IsNullOrWhiteSpace(templateId) && SocialEventTemplateCatalog.HasTemplate(templateId)
                ? SocialEventTemplateCatalog.GetById(templateId)
                : SocialEventTemplateCatalog.GetTournamentCelebrationForCulture(settlement.Culture?.StringId ?? "empire", "live-test-" + settlement.StringId);
            List<Hero> heroes = (requestedHeroes ?? Enumerable.Empty<Hero>()).Where(hero => hero != null && hero != Hero.MainHero && hero.IsAlive)
                .GroupBy(hero => hero.StringId, StringComparer.OrdinalIgnoreCase).Select(group => group.First()).Take(MaxEventAttendees).ToList();
            if (heroes.Count == 0) heroes = ReignPartyChatSession.GetAvailableConversationHeroes().Take(5).ToList();
            if (heroes.Count == 0)
            {
                error = "No living NPC was available for the social-event test.";
                return null;
            }
            float now = CurrentCampaignDay();
            SocialEventRecord record = new SocialEventRecord
            {
                EventId = "reign_live_test_event_" + Guid.NewGuid().ToString("N"),
                TemplateId = template.TemplateId,
                DisplayName = template.DisplayName,
                SettlementStringId = settlement.StringId,
                HostHeroStringId = heroes[0].StringId,
                AttendeeHeroStringIds = heroes.Select(hero => hero.StringId).ToList(),
                MovedHeroStringIds = new List<string>(),
                AnnouncementDay = now,
                ExpiresDay = now + 0.25f,
                RequiredPlayerClanTier = 0,
                Status = SocialEventStatus.Attended,
                AnnouncementSent = true
            };
            _events.Add(record);
            _currentSession = new ReignSocialEventSession(record);
            return _currentSession;
        }

        public async Task<ReignSocialEventSession> StartLiveTestWildernessEventAsync(
            IEnumerable<Hero> preferredParticipants = null,
            int minimumParticipants = 1)
        {
            bool started = await TryStartGeneratedWildernessEventAsync(
                CurrentCampaignDay(),
                false,
                false,
                preferredParticipants,
                minimumParticipants).ConfigureAwait(false);
            return started ? _currentSession : null;
        }

        public void ResolveLiveTestSession()
        {
            ResolveCurrentSession();
        }

        private async Task<bool> TryStartGeneratedWildernessEventAsync(
            float now,
            bool forced,
            bool openScreen = true,
            IEnumerable<Hero> preferredParticipants = null,
            int minimumParticipants = 1)
        {
            if (_generatedWildernessRequestRunning)
            {
                if (forced)
                {
                    Show("A generated wilderness event is already being prepared.");
                }

                return false;
            }

            string rejection;
            if (!CanStartGeneratedWildernessEvent(out rejection))
            {
                if (forced)
                {
                    Show(rejection);
                }

                ReignLog.Info("Generated wilderness rejected: " + rejection);
                return false;
            }

            if (!TryGetGeneratedWildernessTerrain(out string terrainKey, out string locationText))
            {
                if (forced)
                {
                    Show("Generated wilderness events are not available on this map terrain.");
                }

                return false;
            }

            List<Hero> participants =
                PickGeneratedWildernessPartyParticipants(
                    preferredParticipants,
                    minimumParticipants);
            if (participants.Count == 0)
            {
                if (forced)
                {
                    Show("No eligible companion or party hero was found for a wilderness event.");
                }

                return false;
            }

            _lastGeneratedWildernessEventAttemptDay = now;
            _generatedWildernessRequestRunning = true;
            if (forced)
            {
                Show("Generating a Bannerlord Reign wilderness event...");
            }

            ReignGeneratedWildernessScenario scenario;
            try
            {
                ReignGeneratedWildernessRequest request = new ReignGeneratedWildernessRequest
                {
                    TerrainKey = terrainKey,
                    LocationText = locationText,
                    TimeOfDayText = GetGeneratedWildernessTimeOfDayText(now),
                    Participants = participants.Select(x => new ReignGeneratedWildernessParticipant
                    {
                        Hero = x,
                        Role = GetGeneratedPartyRole(x),
                        IsOutsideNpc = false
                    }).ToList()
                };

                scenario = await ReignServerClient.GenerateWildernessScenarioAsync(request);
            }
            finally
            {
                _generatedWildernessRequestRunning = false;
            }

            if (scenario == null || !scenario.Ok)
            {
                ReignLog.Warn("Generated wilderness server plan failed: " + (scenario?.Error ?? "unknown"));
                scenario = BuildFallbackGeneratedWildernessScenario(terrainKey, locationText, participants);
            }

            List<Hero> selectedParticipants =
                ApplyGeneratedWildernessParticipantSelection(
                    scenario,
                    participants,
                    minimumParticipants);
            SocialEventRecord record = CreateGeneratedWildernessEventRecord(now, terrainKey, locationText, scenario, selectedParticipants);
            if (record == null)
            {
                if (forced)
                {
                    Show("Could not create the wilderness event record.");
                }

                return false;
            }

            _events.Add(record);
            _currentSession = new ReignSocialEventSession(record);
            Show("A Bannerlord Reign wilderness event begins.");
            ReignLog.Info("Generated wilderness event opened id=" + record.EventId + " participants=" + record.AttendeeHeroStringIds.Count);
            if (openScreen) ReignSocialEventScreenManager.Open(_currentSession, ResolveCurrentSession);
            return true;
        }

        private static bool CanStartGeneratedWildernessEvent(out string rejection)
        {
            rejection = "Generated wilderness events are not available right now.";
            if (TaleWorlds.CampaignSystem.Campaign.Current == null || Hero.MainHero == null || MobileParty.MainParty == null)
            {
                rejection = "Generated wilderness events are only available inside an active campaign.";
                return false;
            }

            if (ReignSocialEventScreenManager.IsOpen || ReignPartyChatScreenManager.IsOpen)
            {
                rejection = "Another Bannerlord Reign screen is already open.";
                return false;
            }

            MobileParty mainParty = MobileParty.MainParty;
            if (mainParty.CurrentSettlement != null || Settlement.CurrentSettlement != null)
            {
                rejection = "Generated wilderness events only appear while traveling outside settlements.";
                return false;
            }

            if (mainParty.MapEvent != null || MapEvent.PlayerMapEvent != null || mainParty.SiegeEvent != null)
            {
                rejection = "Generated wilderness events cannot start during battles, encounters, or sieges.";
                return false;
            }

            MapState mapState = Game.Current?.GameStateManager?.ActiveState as MapState;
            if (mapState == null || mapState.AtMenu || mapState.MapConversationActive || mapState.IsSimulationActive)
            {
                rejection = "Generated wilderness events can only start on the open campaign map.";
                return false;
            }

            if (TaleWorlds.CampaignSystem.Campaign.Current.CurrentMenuContext != null)
            {
                rejection = "Generated wilderness events cannot start while a campaign menu is open.";
                return false;
            }

            return true;
        }

        private static List<Hero> PickGeneratedWildernessPartyParticipants(
            IEnumerable<Hero> preferredParticipants = null,
            int minimumParticipants = 1)
        {
            List<Hero> candidates = ReignPartyChatSession.GetMainPartyHeroes()
                .Where(x => x != null && x != Hero.MainHero && x.IsAlive && !x.IsPrisoner && !x.IsWounded)
                .OrderBy(x => MBRandom.RandomFloat)
                .ToList();

            if (candidates.Count == 0)
            {
                return new List<Hero>();
            }

            int max = Math.Min(MaxGeneratedWildernessParticipants, candidates.Count);
            int minimum = Math.Max(1, Math.Min(max, minimumParticipants));
            int count = minimum
                + (max > minimum
                    ? MBRandom.RandomInt(max - minimum + 1)
                    : 0);
            HashSet<string> eligibleIds = new HashSet<string>(
                candidates.Select(hero => hero.StringId),
                StringComparer.OrdinalIgnoreCase);
            List<Hero> preferred = (preferredParticipants
                    ?? Enumerable.Empty<Hero>())
                .Where(hero => hero != null
                    && eligibleIds.Contains(hero.StringId))
                .GroupBy(
                    hero => hero.StringId,
                    StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();
            return preferred.Concat(candidates)
                .GroupBy(
                    hero => hero.StringId,
                    StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .Take(count)
                .ToList();
        }

        private static List<Hero> ApplyGeneratedWildernessParticipantSelection(
            ReignGeneratedWildernessScenario scenario,
            List<Hero> fallback,
            int minimumParticipants = 1)
        {
            Dictionary<string, Hero> allowed = (fallback ?? new List<Hero>())
                .Where(x => x != null && !string.IsNullOrWhiteSpace(x.StringId))
                .GroupBy(x => x.StringId)
                .ToDictionary(x => x.Key, x => x.First());

            List<Hero> selected = new List<Hero>();
            foreach (string id in scenario?.ParticipantHeroIds ?? new List<string>())
            {
                if (!string.IsNullOrWhiteSpace(id) && allowed.TryGetValue(id, out Hero hero) && selected.All(x => x.StringId != hero.StringId))
                {
                    selected.Add(hero);
                }
            }

            int minimum = Math.Max(
                1,
                Math.Min(allowed.Count, minimumParticipants));
            foreach (Hero hero in (fallback ?? new List<Hero>()))
            {
                if (selected.Count >= minimum)
                {
                    break;
                }
                if (hero != null
                    && allowed.ContainsKey(hero.StringId)
                    && selected.All(existing =>
                        !existing.StringId.Equals(
                            hero.StringId,
                            StringComparison.OrdinalIgnoreCase)))
                {
                    selected.Add(hero);
                }
            }
            return selected.Count == 0
                ? allowed.Values.ToList()
                : selected;
        }

        private static SocialEventRecord CreateGeneratedWildernessEventRecord(float now, string terrainKey, string locationText, ReignGeneratedWildernessScenario scenario, List<Hero> participants)
        {
            if (participants == null || participants.Count == 0)
            {
                return null;
            }

            string title = string.IsNullOrWhiteSpace(scenario?.Title) ? "Wilderness Encounter" : scenario.Title;
            return new SocialEventRecord
            {
                EventId = GeneratedWildernessEventIdPrefix + SafeId(terrainKey) + "_" + ((int)(now * 100f)) + "_" + MBRandom.RandomInt(1000000),
                TemplateId = "generated_wilderness",
                DisplayName = title,
                SettlementStringId = string.Empty,
                HostHeroStringId = string.Empty,
                AttendeeHeroStringIds = participants.Select(x => x.StringId).Distinct().ToList(),
                MovedHeroStringIds = new List<string>(),
                AnnouncementDay = now,
                ExpiresDay = now + 0.25f,
                RequiredPlayerClanTier = 0,
                Status = SocialEventStatus.Attended,
                AnnouncementSent = true,
                IsGeneratedWildernessEvent = true,
                GeneratedTerrainKey = terrainKey ?? string.Empty,
                GeneratedLocationText = locationText ?? string.Empty,
                GeneratedTimeOfDayText = GetGeneratedWildernessTimeOfDayText(now),
                GeneratedTitle = title,
                GeneratedApproachText = scenario?.ApproachDescription ?? string.Empty,
                GeneratedOpeningText = scenario?.OpeningText ?? string.Empty,
                GeneratedPlayerHook = scenario?.PlayerHook ?? string.Empty,
                GeneratedExternalHeroStringId = scenario?.ExternalHeroId ?? string.Empty,
                GeneratedExternalContextText = scenario?.ExternalContext ?? string.Empty,
                GeneratedEmotionalPressure = scenario?.EmotionalPressure ?? string.Empty,
                GeneratedSurfaceClues = scenario?.SurfaceClues ?? string.Empty,
                GeneratedHiddenContext = scenario?.HiddenContext ?? string.Empty,
                GeneratedDiscoveryRoutes = scenario?.DiscoveryRoutes ?? string.Empty
            };
        }

        private static ReignGeneratedWildernessScenario BuildFallbackGeneratedWildernessScenario(string terrainKey, string locationText, List<Hero> participants)
        {
            string names = string.Join(", ", (participants ?? new List<Hero>()).Select(x => x.Name?.ToString()).Where(x => !string.IsNullOrWhiteSpace(x)));
            return new ReignGeneratedWildernessScenario
            {
                Ok = true,
                Title = "Something Ahead",
                ApproachDescription = "Something draws your attention " + (locationText ?? "on the road") + ". " + (string.IsNullOrWhiteSpace(names) ? "Someone in the party" : names) + " slows, watching the scene with unusual care.",
                OpeningText = "The road falls briefly quiet. Whatever this moment is, it feels personal rather than dangerous.",
                PlayerHook = "You have a chance to ask what has caught their eye.",
                ParticipantHeroIds = (participants ?? new List<Hero>()).Where(x => x != null).Select(x => x.StringId).ToList(),
                TerrainKey = terrainKey ?? "plain",
                EmotionalPressure = "uncertainty",
                SurfaceClues = "hesitation; watchfulness; a pause in travel",
                HiddenContext = "The participant has a private association with what they noticed, but they will only reveal it if the player draws it out.",
                DiscoveryRoutes = "Ask directly; offer reassurance; challenge the visible hesitation."
            };
        }

        private static bool TryGetGeneratedWildernessTerrain(out string terrainKey, out string locationText)
        {
            terrainKey = "plain";
            locationText = "on the open road";
            MobileParty mainParty = MobileParty.MainParty;
            var campaign = TaleWorlds.CampaignSystem.Campaign.Current;
            if (mainParty == null || campaign?.MapSceneWrapper == null)
            {
                return false;
            }

            TerrainType terrain;
            try
            {
                terrain = campaign.MapSceneWrapper.GetFaceTerrainType(mainParty.CurrentNavigationFace);
            }
            catch (Exception ex)
            {
                ReignLog.Warn("Generated wilderness terrain lookup failed: " + ex.Message);
                terrain = TerrainType.Plain;
            }

            switch (terrain)
            {
                case TerrainType.Desert:
                case TerrainType.Dune:
                    terrainKey = "desert";
                    locationText = "among desert tracks, wind-scoured stones, and open sand";
                    return true;
                case TerrainType.Forest:
                    terrainKey = "forest";
                    locationText = "beneath forest shade near the party's path";
                    return true;
                case TerrainType.Steppe:
                    terrainKey = "steppe";
                    locationText = "on the open steppe where the road thins into grass and dust";
                    return true;
                case TerrainType.Snow:
                    terrainKey = "snow";
                    locationText = "along a cold road cut through snow and hard ground";
                    return true;
                case TerrainType.Swamp:
                    terrainKey = "swamp";
                    locationText = "near wet lowland paths, reeds, and uncertain footing";
                    return true;
                case TerrainType.Beach:
                    terrainKey = "coast";
                    locationText = "near the coast where road grit mixes with salt air";
                    return true;
                case TerrainType.Water:
                case TerrainType.River:
                case TerrainType.UnderBridge:
                case TerrainType.OpenSea:
                case TerrainType.CoastalSea:
                case TerrainType.Lake:
                    terrainKey = "water";
                    locationText = "while traveling by water, with shorelines, riverbanks, or open waves nearby";
                    return true;
                default:
                    terrainKey = "plain";
                    locationText = "on the open road across settled plains";
                    return true;
            }
        }

        private static string GetGeneratedWildernessTimeOfDayText(float campaignDay)
        {
            double fraction = campaignDay - Math.Floor(campaignDay);
            if (fraction < 0d)
            {
                fraction += 1d;
            }

            int hour = (int)Math.Floor(fraction * CampaignTime.HoursInDay);
            if (hour < 3) return "deep night";
            if (hour < 5) return "pre-dawn";
            if (hour < 7) return "dawn";
            if (hour < 11) return "morning";
            if (hour < 14) return "midday";
            if (hour < 17) return "afternoon";
            if (hour < 20) return "dusk";
            return "night";
        }

        private static string GetGeneratedPartyRole(Hero hero)
        {
            if (hero?.IsWanderer == true)
            {
                return "party_companion";
            }

            if (hero?.IsLord == true)
            {
                return "party_lord";
            }

            return "party_member";
        }

        private bool AttendEventOnCondition(MenuCallbackArgs args)
        {
            if (!SettingsAllowEvents())
            {
                return false;
            }

            SocialEventRecord record = GetOpenEventForCurrentSettlement();
            if (record == null)
            {
                return false;
            }

            args.optionLeaveType = GameMenuOption.LeaveType.Submenu;
            int playerTier = Clan.PlayerClan?.Tier ?? 0;
            if (playerTier < record.RequiredPlayerClanTier)
            {
                args.IsEnabled = false;
                args.Tooltip = new TextObject("{=!}Requires clan tier " + record.RequiredPlayerClanTier + ". Your clan is tier " + playerTier + ".");
            }

            return true;
        }

        private void AttendEventOnConsequence(MenuCallbackArgs args)
        {
            SocialEventRecord record = GetOpenEventForCurrentSettlement();
            if (record == null)
            {
                Show("No open Bannerlord Reign social event was found here.");
                return;
            }

            record.Status = SocialEventStatus.Attended;
            _currentSession = new ReignSocialEventSession(record);
            Show("You attend the " + record.DisplayName + ".");
            ReignSocialEventScreenManager.Open(_currentSession, ResolveCurrentSession);
        }

        private bool ReturnCurrentEventOnCondition(MenuCallbackArgs args)
        {
            if (_currentSession?.Record == null)
            {
                return false;
            }

            Settlement settlement = Settlement.CurrentSettlement ?? MobileParty.MainParty?.CurrentSettlement;
            if (settlement == null || settlement.StringId != _currentSession.Record.SettlementStringId)
            {
                return false;
            }

            args.optionLeaveType = GameMenuOption.LeaveType.Submenu;
            return true;
        }

        private void ReturnCurrentEventOnConsequence(MenuCallbackArgs args)
        {
            if (_currentSession == null)
            {
                return;
            }

            ReignSocialEventScreenManager.Open(_currentSession, ResolveCurrentSession);
        }

        private void ResolveCurrentSession()
        {
            if (_currentSession?.Record != null)
            {
                _currentSession.Record.Status = SocialEventStatus.Resolved;
                Show("The " + _currentSession.Record.DisplayName + " comes to an end.");
                ReignLog.Info("Resolved social event id=" + _currentSession.Record.EventId + ".");
            }

            _currentSession = null;
        }

        private bool TryCreateEventForTown(Town town, float now, bool forced)
        {
            if (town?.Settlement == null)
            {
                return false;
            }

            SocialEventTemplate template = SocialEventTemplateCatalog.GetRandomForCulture(town.Culture?.StringId);
            List<Hero> attendees = SelectEventAttendees(town);
            if (attendees.Count < 1)
            {
                if (forced)
                {
                    Show("Not enough eligible attendees were found in this town.");
                }

                return false;
            }

            Hero host = PickHost(town, attendees);
            SocialEventRecord record = new SocialEventRecord
            {
                EventId = "reign_event_" + template.TemplateId + "_" + town.Settlement.StringId + "_" + ((int)now) + "_" + MBRandom.RandomInt(1000000),
                TemplateId = template.TemplateId,
                DisplayName = template.DisplayName,
                SettlementStringId = town.Settlement.StringId,
                HostHeroStringId = host?.StringId ?? string.Empty,
                AttendeeHeroStringIds = attendees.Select(x => x.StringId).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().ToList(),
                AnnouncementDay = now,
                ExpiresDay = now + Math.Max(1f, template.OpportunityDays),
                RequiredPlayerClanTier = 0,
                Status = SocialEventStatus.Pending,
                AnnouncementSent = true
            };

            _events.Add(record);
            Show("The " + template.AnnouncementNoun + " will soon be held in " + town.Settlement.Name + ".");
            ReignLog.Info("Created social event id=" + record.EventId + " template=" + record.TemplateId + " attendees=" + record.AttendeeHeroStringIds.Count + ".");
            _ = ReignServerClient.IngestSocialEventAnnouncementAsync(record);
            return true;
        }

        private bool TryCreateTournamentCelebrationForTown(Town town, float now)
        {
            TournamentGame tournament = GetTournamentGame(town);
            if (tournament == null || town?.Settlement == null)
            {
                return false;
            }

            string tournamentKey = BuildTournamentCelebrationKey(town, tournament);
            if (HasTournamentCelebrationForKey(tournamentKey))
            {
                return false;
            }

            SocialEventTemplate template = SocialEventTemplateCatalog.GetTournamentCelebrationForCulture(town.Culture?.StringId);

            List<Hero> attendees = SelectEventAttendees(town);
            if (attendees.Count < 1)
            {
                return false;
            }

            Hero host = PickHost(town, attendees);
            SocialEventRecord record = new SocialEventRecord
            {
                EventId = TournamentCelebrationEventIdPrefix + tournamentKey + "_" + template.TemplateId + "_" + MBRandom.RandomInt(1000000),
                TemplateId = template.TemplateId,
                DisplayName = template.DisplayName,
                SettlementStringId = town.Settlement.StringId,
                HostHeroStringId = host?.StringId ?? string.Empty,
                AttendeeHeroStringIds = attendees.Select(x => x.StringId).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().ToList(),
                AnnouncementDay = now,
                ExpiresDay = GetActiveTournamentCelebrationExpiryDay(now, template, tournament),
                RequiredPlayerClanTier = 0,
                Status = SocialEventStatus.Pending,
                AnnouncementSent = true
            };

            _events.Add(record);
            Show("A " + template.DisplayName + " is gathering around the tournament in " + town.Settlement.Name + ".");
            ReignLog.Info("Created tournament celebration id=" + record.EventId + " template=" + record.TemplateId + " attendees=" + record.AttendeeHeroStringIds.Count + ".");
            _ = ReignServerClient.IngestSocialEventAnnouncementAsync(record);
            return true;
        }

        private void OnTournamentStarted(Town town)
        {
            if (!SettingsAllowEvents() || ReignBetaSettings.Instance?.TournamentCelebrationsEnabled == false)
            {
                return;
            }

            TryCreateTournamentCelebrationForTown(town, CurrentCampaignDay());
        }

        private void OnTournamentFinished(CharacterObject winner, MBReadOnlyList<CharacterObject> participants, Town town, ItemObject prize)
        {
            BeginTournamentCelebrationWindDown(town, CurrentCampaignDay(), winner, prize);
        }

        private void OnTournamentCancelled(Town town)
        {
            ExpireTournamentCelebrationsInTown(town, "tournament cancelled");
        }

        private void TryScheduleTournamentCelebrations(float now)
        {
            foreach (Town town in Town.AllTowns)
            {
                if (town?.IsTown == true && GetTournamentGame(town) != null)
                {
                    TryCreateTournamentCelebrationForTown(town, now);
                }
            }
        }

        private void RefreshTournamentCelebrations(float now)
        {
            foreach (SocialEventRecord record in _events)
            {
                if (record == null || record.Status != SocialEventStatus.Pending || !IsActiveTournamentCelebrationRecord(record))
                {
                    continue;
                }

                Settlement settlement = record.GetSettlement();
                Town town = settlement?.Town;
                if (town == null || town.IsUnderSiege || settlement.SiegeEvent != null)
                {
                    record.Status = SocialEventStatus.Expired;
                    continue;
                }

                TournamentGame tournament = GetTournamentGame(town);
                if (tournament == null)
                {
                    BeginTournamentCelebrationWindDown(record, now, "tournament no longer active");
                    continue;
                }

                SocialEventTemplate template = SocialEventTemplateCatalog.GetById(record.TemplateId);
                record.ExpiresDay = Math.Max(record.ExpiresDay, GetActiveTournamentCelebrationExpiryDay(now, template, tournament));
            }
        }

        private void BeginTournamentCelebrationWindDown(Town town, float now, CharacterObject winner = null, ItemObject prize = null)
        {
            if (town?.Settlement == null)
            {
                return;
            }

            foreach (SocialEventRecord record in _events)
            {
                if (record == null
                    || record.Status != SocialEventStatus.Pending
                    || record.SettlementStringId != town.Settlement.StringId
                    || !IsActiveTournamentCelebrationRecord(record))
                {
                    continue;
                }

                BeginTournamentCelebrationWindDown(record, now, "tournament finished");
                string winnerName = winner?.HeroObject?.Name?.ToString() ?? winner?.Name?.ToString();
                if (!string.IsNullOrWhiteSpace(winnerName))
                {
                    record.GeneratedOpeningText = "The tournament has ended, and the talk turns toward " + winnerName + "'s victory.";
                }
            }
        }

        private static void BeginTournamentCelebrationWindDown(SocialEventRecord record, float now, string reason)
        {
            if (record == null || !IsActiveTournamentCelebrationRecord(record))
            {
                return;
            }

            record.EventId = TournamentCelebrationAfterEventIdPrefix + record.EventId.Substring(TournamentCelebrationEventIdPrefix.Length);
            record.ExpiresDay = now + TournamentVictoryCelebrationDays;
            ReignLog.Info("Tournament celebration wind-down id=" + record.EventId + " reason=" + reason + ".");
        }

        private void ExpireTournamentCelebrationsInTown(Town town, string reason)
        {
            if (town?.Settlement == null)
            {
                return;
            }

            foreach (SocialEventRecord record in _events)
            {
                if (record != null
                    && record.Status == SocialEventStatus.Pending
                    && record.SettlementStringId == town.Settlement.StringId
                    && IsActiveTournamentCelebrationRecord(record))
                {
                    record.Status = SocialEventStatus.Expired;
                    ReignLog.Info("Expired tournament celebration id=" + record.EventId + " reason=" + reason + ".");
                }
            }
        }

        private static TournamentGame GetTournamentGame(Town town)
        {
            try
            {
                return town == null || !town.IsTown ? null : TaleWorlds.CampaignSystem.Campaign.Current?.TournamentManager?.GetTournamentGame(town);
            }
            catch (Exception ex)
            {
                ReignLog.Warn("Tournament lookup failed: " + ex.Message);
                return null;
            }
        }

        private bool HasTournamentCelebrationForKey(string tournamentKey)
        {
            string activePrefix = TournamentCelebrationEventIdPrefix + tournamentKey + "_";
            string afterPrefix = TournamentCelebrationAfterEventIdPrefix + tournamentKey + "_";
            return _events.Any(x => x?.EventId != null && (x.EventId.StartsWith(activePrefix) || x.EventId.StartsWith(afterPrefix)));
        }

        private static string BuildTournamentCelebrationKey(Town town, TournamentGame tournament)
        {
            int creationDay = tournament != null ? (int)tournament.CreationTime.ToDays : (int)CurrentCampaignDay();
            return town.StringId + "_" + creationDay;
        }

        private static float GetActiveTournamentCelebrationExpiryDay(float now, SocialEventTemplate template, TournamentGame tournament)
        {
            float templateExpiry = now + Math.Max(1f, template?.OpportunityDays ?? 1f);
            if (tournament == null)
            {
                return templateExpiry;
            }

            float tournamentExpiry = (float)tournament.CreationTime.ToDays + tournament.RemoveTournamentAfterDays + TournamentVictoryCelebrationDays;
            return Math.Max(templateExpiry, tournamentExpiry);
        }

        private static bool IsActiveTournamentCelebrationRecord(SocialEventRecord record)
        {
            return record?.EventId != null
                && record.EventId.StartsWith(TournamentCelebrationEventIdPrefix)
                && !record.EventId.StartsWith(TournamentCelebrationAfterEventIdPrefix);
        }

        private void RepairAttendeeRosters()
        {
            string playerId = Hero.MainHero?.StringId;
            if (string.IsNullOrWhiteSpace(playerId))
            {
                return;
            }

            foreach (SocialEventRecord record in _events.Where(x => x != null))
            {
                List<string> original = record.AttendeeHeroStringIds ?? new List<string>();
                List<string> repaired = original
                    .Where(x => !string.IsNullOrWhiteSpace(x)
                        && !string.Equals(x, playerId, StringComparison.OrdinalIgnoreCase))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                if (repaired.Count != original.Count)
                {
                    record.AttendeeHeroStringIds = repaired;
                    ReignLog.Info("Removed the player from social-event NPC roster id=" + (record.EventId ?? "") + ".");
                }

                if (string.Equals(record.HostHeroStringId, playerId, StringComparison.OrdinalIgnoreCase))
                {
                    record.HostHeroStringId = record.GetAttendees().FirstOrDefault()?.StringId ?? string.Empty;
                }
            }
        }

        private void RepairTournamentCelebrationPresentation()
        {
            foreach (SocialEventRecord record in _events.Where(x => x?.EventId != null
                && (x.EventId.StartsWith(TournamentCelebrationEventIdPrefix) || x.EventId.StartsWith(TournamentCelebrationAfterEventIdPrefix))))
            {
                bool genericName = string.Equals(record.DisplayName, "Tournament Celebration", StringComparison.OrdinalIgnoreCase);
                bool nonLegacyTemplate = string.IsNullOrWhiteSpace(record.TemplateId)
                    || (!record.TemplateId.StartsWith("feast_") && !record.TemplateId.StartsWith("fair_") && !record.TemplateId.StartsWith("dance_"));
                if (!genericName && !nonLegacyTemplate) continue;

                Settlement settlement = record.GetSettlement();
                SocialEventTemplate replacement = SocialEventTemplateCatalog.GetTournamentCelebrationForCulture(
                    settlement?.Culture?.StringId,
                    record.EventId);
                record.TemplateId = replacement.TemplateId;
                record.DisplayName = replacement.DisplayName;
                ReignLog.Info("Restored tournament celebration presentation id=" + record.EventId + " template=" + replacement.TemplateId + ".");
            }
        }

        private static string CultureSuffix(string cultureId)
        {
            string value = (cultureId ?? string.Empty).ToLowerInvariant();
            if (value.Contains("vland")) return "vlandia";
            if (value.Contains("sturg")) return "sturgia";
            if (value.Contains("battan")) return "battania";
            if (value.Contains("aserai")) return "aserai";
            if (value.Contains("khuz")) return "khuzait";
            if (value.Contains("nord")) return "nord";
            return "empire";
        }

        private SocialEventRecord GetOpenEventForCurrentSettlement()
        {
            Settlement settlement = Settlement.CurrentSettlement ?? MobileParty.MainParty?.CurrentSettlement;
            if (settlement == null)
            {
                return null;
            }

            float now = CurrentCampaignDay();
            return _events.FirstOrDefault(x => x.SettlementStringId == settlement.StringId && x.IsOpen(now) && SocialEventTemplateCatalog.HasTemplate(x.TemplateId));
        }

        private void ExpireOldEvents(float now)
        {
            foreach (SocialEventRecord record in _events)
            {
                if (record.HasTimedOut(now))
                {
                    record.Status = SocialEventStatus.Expired;
                }
            }
        }

        private void PruneClosedEvents()
        {
            float cutoff = CurrentCampaignDay() - 20f;
            _events.RemoveAll(x => x != null && x.Status != SocialEventStatus.Pending && x.ExpiresDay < cutoff);
        }

        private static List<Hero> FindSafeAttendees(Settlement settlement)
        {
            List<Hero> attendees = new List<Hero>();
            if (settlement == null)
            {
                return attendees;
            }

            attendees.AddRange(settlement.HeroesWithoutParty.Where(hero => IsSafeAttendee(hero, settlement)));
            attendees.AddRange(settlement.Notables.Where(hero => IsSafeAttendee(hero, settlement)));
            foreach (MobileParty party in settlement.Parties)
            {
                if (party?.CurrentSettlement != settlement)
                {
                    continue;
                }

                if (party.LeaderHero != null && IsSafeAttendee(party.LeaderHero, settlement))
                {
                    attendees.Add(party.LeaderHero);
                }

                foreach (var element in party.MemberRoster.GetTroopRoster())
                {
                    Hero hero = element.Character?.HeroObject;
                    if (element.Number > 0 && IsSafeAttendee(hero, settlement))
                    {
                        attendees.Add(hero);
                    }
                }
            }

            return attendees.Where(x => x != Hero.MainHero).Distinct().ToList();
        }

        private List<Hero> SelectEventAttendees(Town town)
        {
            if (town?.Settlement == null)
            {
                return new List<Hero>();
            }

            return FindSafeAttendees(town.Settlement)
                .Where(x => x != null && x != Hero.MainHero)
                .Distinct()
                .Select(hero => new
                {
                    Hero = hero,
                    Score = GetInvitationScore(hero, town) - GetRecentAttendancePenalty(hero, town) + MBRandom.RandomInt(0, 91)
                })
                .OrderByDescending(x => x.Hero.IsLord)
                .ThenByDescending(x => x.Score)
                .ThenBy(x => x.Hero.StringId, StringComparer.OrdinalIgnoreCase)
                .Take(MaxEventAttendees)
                .Select(x => x.Hero)
                .ToList();
        }

        private int GetRecentAttendancePenalty(Hero hero, Town town)
        {
            if (hero == null || town?.Settlement == null || string.IsNullOrWhiteSpace(hero.StringId))
            {
                return 0;
            }

            int recentAppearances = _events
                .Where(x => x != null
                    && !x.IsGeneratedWildernessEvent
                    && string.Equals(x.SettlementStringId, town.Settlement.StringId, StringComparison.OrdinalIgnoreCase)
                    && (x.AttendeeHeroStringIds ?? new List<string>()).Contains(hero.StringId))
                .OrderByDescending(x => x.AnnouncementDay)
                .Take(3)
                .Count();
            return recentAppearances * 180;
        }

        private static bool IsSafeAttendee(Hero hero, Settlement settlement)
        {
            if (!ReignConversationEligibility.IsAdultLivingNpc(hero) || hero.IsPrisoner || hero.IsWounded)
            {
                return false;
            }

            if (settlement == null)
            {
                return false;
            }

            if (hero.PartyBelongedTo != null)
            {
                if (hero.PartyBelongedTo.CurrentSettlement != settlement)
                {
                    return false;
                }
            }
            else if (hero.CurrentSettlement != settlement
                && !settlement.HeroesWithoutParty.Contains(hero)
                && !settlement.Notables.Contains(hero))
            {
                return false;
            }

            return hero.IsLord || hero.IsNotable || hero.IsWanderer;
        }

        private static Hero PickHost(Town town, List<Hero> attendees)
        {
            Hero owner = town?.OwnerClan?.Leader;
            if (owner != null && attendees.Contains(owner))
            {
                return owner;
            }

            return attendees
                .Where(x => x != Hero.MainHero)
                .OrderByDescending(x => x.Clan?.Tier ?? 0)
                .FirstOrDefault()
                ?? attendees.FirstOrDefault();
        }

        private static int GetInvitationScore(Hero hero, Town town)
        {
            int score = 0;
            if (hero == Hero.MainHero)
            {
                score += 500;
            }

            if (hero?.Clan != null)
            {
                score += hero.Clan.Tier * 20;
            }

            if (town?.OwnerClan != null && hero?.Clan == town.OwnerClan)
            {
                score += 150;
            }

            if (hero?.MapFaction != null && town?.MapFaction != null && hero.MapFaction == town.MapFaction)
            {
                score += 100;
            }

            if (hero?.Culture != null && town?.Culture != null && hero.Culture == town.Culture)
            {
                score += 40;
            }

            return score;
        }

        private static Town PickScheduleTown()
        {
            Settlement current = Settlement.CurrentSettlement ?? MobileParty.MainParty?.CurrentSettlement;
            if (current?.Town != null && current.IsTown)
            {
                return current.Town;
            }

            return Settlement.All
                .Where(x => x?.Town != null && x.IsTown && !x.IsUnderSiege)
                .OrderByDescending(x => x.Town.Prosperity)
                .FirstOrDefault()
                ?.Town;
        }

        private static bool SettingsAllowEvents()
        {
            ReignBetaSettings settings = ReignBetaSettings.Instance;
            return settings == null || (settings.Enabled && settings.EventsEnabled);
        }

        private static float CurrentCampaignDay()
        {
            return (float)CampaignTime.Now.ToDays;
        }

        private static void Show(string message)
        {
            InformationManager.DisplayMessage(new InformationMessage("[Bannerlord Reign] " + message, Color.FromUint(0xFFFFD36A)));
        }

        private static string SafeId(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "unknown";
            }

            return new string(value.Select(x => char.IsLetterOrDigit(x) ? x : '_').ToArray()).Trim('_').ToLowerInvariant();
        }
    }
}
