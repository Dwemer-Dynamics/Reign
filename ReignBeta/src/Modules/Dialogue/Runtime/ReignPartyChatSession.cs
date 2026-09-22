using System;
using System.Collections.Generic;
using System.Linq;
using ReignBeta.Integration;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.Settlements.Locations;

namespace ReignBeta.Runtime
{
    public sealed class ReignPartyChatSession
    {
        private const int MaximumTranscriptLines = 36;
        private readonly List<ReignPartyChatTranscriptLine> _recentTranscriptLines = new List<ReignPartyChatTranscriptLine>();
        private int _transcriptSequence;

        public string SessionId { get; private set; } = "party_chat_" + Guid.NewGuid().ToString("N");
        public string CurrentSceneTurnId { get; private set; } = string.Empty;

        public IReadOnlyList<string> RecentTranscriptLines => _recentTranscriptLines
            .Select(line => line.Speaker + ": " + line.Text).ToList();
        public IReadOnlyList<ReignPartyChatTranscriptLine> RecentTranscriptEntries => _recentTranscriptLines;
        public int PlayerInputCount { get; private set; }

        public void RestoreIdentity(string sessionId)
        {
            if (!string.IsNullOrWhiteSpace(sessionId)) SessionId = sessionId.Trim();
        }

        public void BeginOpeningTurn()
        {
            CurrentSceneTurnId = SessionId + "_opening";
        }

        public void RestoreTranscript(IEnumerable<ReignPartyChatTranscriptLine> lines)
        {
            _recentTranscriptLines.Clear(); _transcriptSequence = 0; PlayerInputCount = 0;
            foreach (ReignPartyChatTranscriptLine line in lines ?? Enumerable.Empty<ReignPartyChatTranscriptLine>())
            {
                if (line == null || string.IsNullOrWhiteSpace(line.Text)) continue;
                _recentTranscriptLines.Add(line);
                _transcriptSequence = Math.Max(_transcriptSequence, line.Sequence);
                string prefix = SessionId + "_turn_";
                if (!string.IsNullOrWhiteSpace(line.ExchangeId)
                    && line.ExchangeId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                    && int.TryParse(line.ExchangeId.Substring(prefix.Length), out int turnIndex)
                    && turnIndex > 0)
                {
                    PlayerInputCount = Math.Max(PlayerInputCount, turnIndex);
                }
            }
            while (_recentTranscriptLines.Count > MaximumTranscriptLines) _recentTranscriptLines.RemoveAt(0);
        }

        public void RegisterPlayerInput()
        {
            PlayerInputCount++;
            CurrentSceneTurnId = SessionId + "_turn_" + PlayerInputCount;
        }

        public void RecordTranscriptLine(string speaker, string text, string speakerHeroStringId = "", string role = "")
        {
            string cleanText = CleanText(text);
            if (string.IsNullOrWhiteSpace(cleanText))
            {
                return;
            }

            string cleanSpeaker = string.IsNullOrWhiteSpace(speaker) ? "Unknown" : speaker.Trim();
            string normalizedRole = (role ?? string.Empty).Trim().ToLowerInvariant();
            if (normalizedRole != "player" && normalizedRole != "npc" && normalizedRole != "system")
            {
                normalizedRole = string.Equals(speakerHeroStringId, Hero.MainHero?.StringId, StringComparison.OrdinalIgnoreCase)
                    ? "player"
                    : string.IsNullOrWhiteSpace(speakerHeroStringId) ? "system" : "npc";
            }
            _recentTranscriptLines.Add(new ReignPartyChatTranscriptLine
            {
                Sequence = ++_transcriptSequence,
                SessionId = SessionId,
                ExchangeId = CurrentSceneTurnId,
                SpeakerHeroStringId = speakerHeroStringId ?? string.Empty,
                Speaker = cleanSpeaker,
                Role = normalizedRole,
                Text = cleanText
            });
            while (_recentTranscriptLines.Count > MaximumTranscriptLines)
            {
                _recentTranscriptLines.RemoveAt(0);
            }
        }

        public string BuildLocationText()
        {
            Settlement settlement = GetCurrentSettlementForPartyChat();
            return settlement != null ? "Location: " + settlement.Name : "Location: with your party";
        }

        public string BuildSceneContext(IEnumerable<Hero> activeHeroes)
        {
            Settlement settlement = GetCurrentSettlementForPartyChat();
            string active = string.Join(", ", (activeHeroes ?? new List<Hero>())
                .Where(x => x != null)
                .Select(x => x.Name?.ToString())
                .Where(x => !string.IsNullOrWhiteSpace(x)));

            return "Conversation mode: party chat\n"
                + ReignCalendarService.BuildPromptCalendarContext() + "\n"
                + BuildLocationText() + "\n"
                + "Player: " + (Hero.MainHero?.Name?.ToString() ?? "Player") + "\n"
                + "Selected speakers: " + (string.IsNullOrWhiteSpace(active) ? "none" : active) + "\n"
                + "This is a living group conversation. Each NPC should answer only as themselves, aware that others may hear them.";
        }

        public static List<Hero> GetAvailableConversationHeroes()
        {
            List<Hero> result = new List<Hero>();
            HashSet<string> seen = new HashSet<string>();
            int partyCount = AddMainPartyHeroes(result, seen);
            Settlement settlement = GetCurrentSettlementForPartyChat();
            int notableCount = 0;
            int keepCount = 0;

            if (ShouldIncludeSettlementNotables(settlement))
            {
                notableCount = AddNotables(result, seen, settlement);
            }

            if (ShouldIncludeKeepHeroes(settlement))
            {
                keepCount = AddKeepHeroes(result, seen, settlement);
            }

            if (settlement != null && !settlement.IsUnderSiege)
                foreach (Hero resident in ReignBeta.Campaign.ReignEncounteredResidentsCampaignBehavior.Instance?.ResidentsAt(settlement) ?? new List<Hero>())
                    if (seen.Add(resident.StringId)) result.Add(resident);

            List<Hero> ordered = result
                .OrderBy(x => GetRosterSortGroup(x, settlement))
                .ThenByDescending(x => x.IsLord)
                .ThenByDescending(x => x.IsNotable)
                .ThenByDescending(x => x.IsWanderer)
                .ThenBy(x => x.Name?.ToString() ?? string.Empty)
                .ToList();

            ReignLog.Info("Party chat roster total=" + ordered.Count
                + " party=" + partyCount
                + " keep=" + keepCount
                + " notables=" + notableCount
                + " settlement=" + (settlement?.StringId ?? "none"));

            return ordered;
        }

        public static List<Hero> GetMainPartyHeroes()
        {
            List<Hero> result = new List<Hero>();
            HashSet<string> seen = new HashSet<string>();
            AddMainPartyHeroes(result, seen);

            return result
                .OrderByDescending(x => x.IsLord)
                .ThenByDescending(x => x.IsWanderer)
                .ThenBy(x => x.Name?.ToString() ?? string.Empty)
                .ToList();
        }

        private static int AddMainPartyHeroes(List<Hero> result, HashSet<string> seen)
        {
            MobileParty party = MobileParty.MainParty;
            if (party?.MemberRoster == null)
            {
                return 0;
            }

            int added = 0;
            foreach (TroopRosterElement element in party.MemberRoster.GetTroopRoster())
            {
                Hero hero = element.Character?.HeroObject;
                if (!IsValidPartyChatHero(hero))
                {
                    continue;
                }

                if (seen.Add(hero.StringId))
                {
                    result.Add(hero);
                    added++;
                }
            }

            return added;
        }

        private static int AddNotables(List<Hero> result, HashSet<string> seen, Settlement settlement)
        {
            if (settlement?.Notables == null)
            {
                return 0;
            }

            int added = 0;
            foreach (Hero hero in settlement.Notables)
            {
                if (!IsValidSettlementConversationHero(hero, settlement) || !hero.IsNotable)
                {
                    continue;
                }

                if (seen.Add(hero.StringId))
                {
                    result.Add(hero);
                    added++;
                }
            }

            return added;
        }

        private static int AddKeepHeroes(List<Hero> result, HashSet<string> seen, Settlement settlement)
        {
            int added = 0;
            foreach (Hero hero in GetLordHallLocationHeroes(settlement).Concat(GetLordHallFallbackHeroes(settlement)))
            {
                if (!IsValidSettlementConversationHero(hero, settlement) || !hero.IsLord)
                {
                    continue;
                }

                if (seen.Add(hero.StringId))
                {
                    result.Add(hero);
                    added++;
                }
            }

            return added;
        }

        private static IEnumerable<Hero> GetLordHallLocationHeroes(Settlement settlement)
        {
            if (settlement?.LocationComplex == null)
            {
                yield break;
            }

            IEnumerable<LocationCharacter> locationCharacters;
            try
            {
                locationCharacters = settlement.LocationComplex.GetListOfCharactersInLocation("lordshall").ToList();
            }
            catch
            {
                yield break;
            }

            foreach (LocationCharacter locationCharacter in locationCharacters)
            {
                Hero hero = locationCharacter?.Character?.HeroObject;
                if (hero != null)
                {
                    yield return hero;
                }
            }
        }

        private static IEnumerable<Hero> GetLordHallFallbackHeroes(Settlement settlement)
        {
            if (settlement == null)
            {
                yield break;
            }

            if (settlement.HeroesWithoutParty != null)
            {
                foreach (Hero hero in settlement.HeroesWithoutParty)
                {
                    if (CanBeInLordHall(hero, settlement))
                    {
                        yield return hero;
                    }
                }
            }

            if (settlement.Parties != null)
            {
                foreach (MobileParty party in settlement.Parties)
                {
                    Hero hero = party?.LeaderHero;
                    if (CanBeInLordHall(hero, settlement))
                    {
                        yield return hero;
                    }
                }
            }

            Hero factionLeader = GetSettlementFactionLeader(settlement);
            if (CanBeInLordHall(factionLeader, settlement))
            {
                yield return factionLeader;
            }

            Hero spouse = factionLeader?.Spouse;
            if (CanBeInLordHall(spouse, settlement))
            {
                yield return spouse;
            }
        }

        private static bool CanBeInLordHall(Hero hero, Settlement settlement)
        {
            return hero != null
                && hero.IsLord
                && hero.CurrentSettlement == settlement
                && (hero.PartyBelongedTo == null || hero.PartyBelongedTo.CurrentSettlement == settlement);
        }

        private static Hero GetSettlementFactionLeader(Settlement settlement)
        {
            if (settlement?.MapFaction is Kingdom kingdom)
            {
                return kingdom.Leader;
            }

            return settlement?.OwnerClan?.Leader;
        }

        private static Settlement GetCurrentSettlementForPartyChat()
        {
            return Settlement.CurrentSettlement ?? MobileParty.MainParty?.CurrentSettlement;
        }

        private static bool ShouldIncludeSettlementNotables(Settlement settlement)
        {
            return settlement != null
                && !settlement.IsUnderSiege
                && (settlement.IsFortification || settlement.IsVillage);
        }

        private static bool ShouldIncludeKeepHeroes(Settlement settlement)
        {
            return settlement != null && settlement.IsFortification && !settlement.IsUnderSiege;
        }

        private static int GetRosterSortGroup(Hero hero, Settlement settlement)
        {
            if (hero?.PartyBelongedTo == MobileParty.MainParty)
            {
                return 0;
            }

            if (hero != null && settlement != null && hero.IsLord && hero.CurrentSettlement == settlement)
            {
                return 1;
            }

            if (hero?.IsNotable == true)
            {
                return 2;
            }

            return 3;
        }

        private static bool IsValidPartyChatHero(Hero hero)
        {
            return ReignConversationEligibility.IsAdultLivingNpc(hero)
                && !hero.IsPrisoner;
        }

        private static bool IsValidSettlementConversationHero(Hero hero, Settlement settlement)
        {
            return IsValidPartyChatHero(hero)
                && settlement != null
                && hero.CurrentSettlement == settlement
                && !hero.IsWounded;
        }

        private static string CleanText(string text)
        {
            return string.IsNullOrWhiteSpace(text)
                ? string.Empty
                : text.Replace("\r", " ").Replace("\n", " ").Trim();
        }
    }

    public sealed class ReignPartyChatTranscriptLine
    {
        public int Sequence { get; set; }
        public string SessionId { get; set; } = string.Empty;
        public string ExchangeId { get; set; } = string.Empty;
        public string SpeakerHeroStringId { get; set; } = string.Empty;
        public string Speaker { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
    }
}
