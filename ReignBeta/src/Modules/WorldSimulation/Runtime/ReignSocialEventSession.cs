using System.Collections.Generic;
using System.Linq;
using ReignBeta.Events;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace ReignBeta.Runtime
{
    public sealed class ReignSocialEventSession
    {
        public const int MaxActiveParticipants = 5;
        public const int TurnsPerAutomaticPhase = 7;

        private readonly List<string> _activeHeroStringIds = new List<string>();
        private readonly List<string> _approachedHeroStringIds = new List<string>();
        private readonly HashSet<string> _wanderedHeroStringIds = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _appliedRelationshipReceiptIds = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> _ignoredTurnCounts = new Dictionary<string, int>(System.StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _transcriptLines = new List<string>();
        private int _currentPhaseTranscriptStartIndex;

        public ReignSocialEventSession(SocialEventRecord record)
        {
            Record = record;
            Template = SocialEventTemplateCatalog.GetById(record.TemplateId);

            if (record.IsGeneratedWildernessEvent)
            {
                foreach (Hero hero in record.GetAttendees().Where(x => x != null && x != Hero.MainHero))
                {
                    ActivateHero(hero);
                }
            }
        }

        public SocialEventRecord Record { get; }
        public SocialEventTemplate Template { get; }
        public int CurrentPhaseIndex { get; private set; }
        public SocialEventPhase CurrentPhase => Template.Phases[CurrentPhaseIndex];
        public IReadOnlyList<string> ActiveHeroStringIds => _activeHeroStringIds;
        public IReadOnlyCollection<string> WanderedHeroStringIds => _wanderedHeroStringIds;
        public IReadOnlyCollection<string> AutomaticApproachBlockedHeroStringIds => _approachedHeroStringIds.Concat(_wanderedHeroStringIds).Distinct(System.StringComparer.OrdinalIgnoreCase).ToList();
        public IReadOnlyDictionary<string, int> IgnoredTurnCounts => _ignoredTurnCounts;
        public int PhaseExchangeCount { get; private set; }
        public int RemainingActiveSlots => System.Math.Max(0, MaxActiveParticipants - _activeHeroStringIds.Count);
        public IReadOnlyList<string> RecentTranscriptLines => _transcriptLines;
        public IReadOnlyList<string> CurrentPhaseTranscriptLines => _transcriptLines.Skip(_currentPhaseTranscriptStartIndex).ToList();

        public IEnumerable<Hero> ActiveHeroes()
        {
            return Record.GetAttendees().Where(x => x != null && x != Hero.MainHero && _activeHeroStringIds.Contains(x.StringId));
        }

        public bool ActivateHero(Hero hero, bool manual = false)
        {
            if (hero == null || hero == Hero.MainHero || string.IsNullOrWhiteSpace(hero.StringId))
            {
                return false;
            }

            if (!_activeHeroStringIds.Contains(hero.StringId))
            {
                if (_activeHeroStringIds.Count >= MaxActiveParticipants)
                {
                    return false;
                }

                _activeHeroStringIds.Add(hero.StringId);
            }

            if (manual)
            {
                _wanderedHeroStringIds.Remove(hero.StringId);
            }
            _ignoredTurnCounts[hero.StringId] = 0;
            return true;
        }

        public void DeactivateHero(Hero hero)
        {
            if (hero == null || string.IsNullOrWhiteSpace(hero.StringId))
            {
                return;
            }

            _activeHeroStringIds.Remove(hero.StringId);
            _ignoredTurnCounts.Remove(hero.StringId);
        }

        public bool AdvancePhase()
        {
            if (CurrentPhaseIndex >= Template.Phases.Count - 1)
            {
                return false;
            }

            CurrentPhaseIndex++;
            _currentPhaseTranscriptStartIndex = _transcriptLines.Count;
            PhaseExchangeCount = 0;
            _approachedHeroStringIds.Clear();
            _wanderedHeroStringIds.Clear();
            return true;
        }

        public bool RegisterCompletedExchange()
        {
            PhaseExchangeCount = System.Math.Min(TurnsPerAutomaticPhase, PhaseExchangeCount + 1);
            if (CurrentPhaseIndex >= Template.Phases.Count - 1)
            {
                return false;
            }

            return PhaseExchangeCount >= TurnsPerAutomaticPhase;
        }

        public bool TryRegisterRelationshipReceipt(string receiptId)
        {
            return !string.IsNullOrWhiteSpace(receiptId) && _appliedRelationshipReceiptIds.Add(receiptId);
        }

        public bool HasRelationshipReceipt(string receiptId)
        {
            return !string.IsNullOrWhiteSpace(receiptId) && _appliedRelationshipReceiptIds.Contains(receiptId);
        }

        public void ApplyTurnResolution(IEnumerable<string> addressedHeroStringIds, IEnumerable<string> wanderedHeroStringIds, IDictionary<string, int> nextIdleCounts)
        {
            HashSet<string> addressed = new HashSet<string>((addressedHeroStringIds ?? Enumerable.Empty<string>()).Where(x => !string.IsNullOrWhiteSpace(x)), System.StringComparer.OrdinalIgnoreCase);
            foreach (string heroId in _activeHeroStringIds.ToList())
            {
                int count;
                if (nextIdleCounts != null && nextIdleCounts.TryGetValue(heroId, out count))
                {
                    _ignoredTurnCounts[heroId] = System.Math.Max(0, count);
                }
                else
                {
                    _ignoredTurnCounts[heroId] = addressed.Contains(heroId) ? 0 : (_ignoredTurnCounts.ContainsKey(heroId) ? _ignoredTurnCounts[heroId] + 1 : 1);
                }
            }

            foreach (string heroId in (wanderedHeroStringIds ?? Enumerable.Empty<string>()).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct())
            {
                _activeHeroStringIds.Remove(heroId);
                _ignoredTurnCounts.Remove(heroId);
                _wanderedHeroStringIds.Add(heroId);
            }
        }

        public List<Hero> GetApproachCandidates()
        {
            List<Hero> candidates = Record.GetAttendees()
                .Where(x => x != null
                    && x != Hero.MainHero
                    && !_activeHeroStringIds.Contains(x.StringId)
                    && !_wanderedHeroStringIds.Contains(x.StringId))
                .ToList();
            bool hasUnapproachedHero = candidates.Any(x => !_approachedHeroStringIds.Contains(x.StringId));
            return candidates
                .Where(x => !hasUnapproachedHero || !_approachedHeroStringIds.Contains(x.StringId))
                .OrderBy(x => MBRandom.RandomFloat)
                .ThenByDescending(x => x.Clan?.Tier ?? 0)
                .ThenByDescending(x => x.GetRelation(Hero.MainHero))
                .ToList();
        }

        public List<Hero> ActivateApproachHeroes(IEnumerable<Hero> heroes)
        {
            List<Hero> activated = new List<Hero>();
            foreach (Hero hero in (heroes ?? Enumerable.Empty<Hero>())
                .Where(x => x != null && x != Hero.MainHero && !_activeHeroStringIds.Contains(x.StringId))
                .Distinct())
            {
                if (!ActivateHero(hero))
                {
                    break;
                }
                if (!_approachedHeroStringIds.Contains(hero.StringId))
                {
                    _approachedHeroStringIds.Add(hero.StringId);
                }
                activated.Add(hero);
            }

            return activated;
        }

        public void RecordTranscriptLine(string speaker, string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            string cleanSpeaker = string.IsNullOrWhiteSpace(speaker) ? "Event" : speaker.Trim();
            _transcriptLines.Add(cleanSpeaker + ": " + text.Trim());
            while (_transcriptLines.Count > 40)
            {
                _transcriptLines.RemoveAt(0);
            }
        }

        public string BuildSceneContext()
        {
            string settlement = Record.GetSettlement()?.Name?.ToString() ?? Record.SettlementStringId ?? "unknown settlement";
            string context = "Social event: " + Template.DisplayName
                + "\nEvent id: " + Record.EventId
                + "\n" + ReignCalendarService.BuildPromptCalendarContext()
                + "\nSettlement: " + settlement
                + "\nPhase: " + CurrentPhase.Title
                + "\nPhase setting: " + CurrentPhase.SettingSummary
                + "\nHost: " + (Record.GetHost()?.Name?.ToString() ?? "unknown")
                + "\nAttendees: " + string.Join(", ", Record.GetAttendees().Select(x => x.Name?.ToString()).Where(x => !string.IsNullOrWhiteSpace(x)));

            if (Record.IsGeneratedWildernessEvent)
            {
                context += "\nGenerated wilderness terrain: " + (Record.GeneratedTerrainKey ?? "")
                    + "\nGenerated location: " + (Record.GeneratedLocationText ?? "")
                    + "\nGenerated time of day: " + (Record.GeneratedTimeOfDayText ?? "")
                    + "\nVisible approach: " + (Record.GeneratedApproachText ?? "")
                    + "\nOpening: " + (Record.GeneratedOpeningText ?? "")
                    + "\nPlayer hook: " + (Record.GeneratedPlayerHook ?? "")
                    + "\nSurface clues: " + (Record.GeneratedSurfaceClues ?? "")
                    + "\nEmotional pressure: " + (Record.GeneratedEmotionalPressure ?? "")
                    + "\nPrivate hidden context for server only: " + (Record.GeneratedHiddenContext ?? "")
                    + "\nDiscovery routes: " + (Record.GeneratedDiscoveryRoutes ?? "");
            }

            return context;
        }
    }
}
