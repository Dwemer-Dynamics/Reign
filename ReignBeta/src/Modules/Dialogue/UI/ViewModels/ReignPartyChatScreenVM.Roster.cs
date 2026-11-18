using System;
using Reign.Core.Contracts.Dialogue;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using ReignBeta.Court;
using ReignBeta.CastleChat;
using ReignBeta.Campaign;
using ReignBeta.PartyAgency;
using ReignBeta.World;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Core.ViewModelCollection.ImageIdentifiers;
using TaleWorlds.Library;
using static ReignBeta.Dialogue.UI.ReignChatText;
using ReignBeta.Characters.Campaign;
using ReignBeta.Characters.Integration;
using ReignBeta.Dialogue.Integration;
using ReignBeta.Dialogue.Runtime;
using ReignBeta.Platform.Campaign;
using ReignBeta.Platform.Integration;
using ReignBeta.Portraits.Integration;
using ReignBeta.UI.ViewModels;
using ReignBeta.WorldSimulation.Campaign;
using ReignBeta.WorldSimulation.PartyAgency;
using ReignBeta.WorldSimulation.World;
#if !REIGN_EXCLUDE_COURT
using ReignBeta.Court.Campaign;
using ReignBeta.Court.UI;
#endif
using ReignBeta.Platform.Runtime;

namespace ReignBeta.Dialogue.UI
{
    public sealed partial class ReignPartyChatScreenVM
    {
        private void ToggleMember(ReignPartyChatMemberVM attendee)
        {
            if (IsHomesMode)
            {
                if (IsBusy || attendee?.Hero == null) return;
                if (!IsResidentAvailable(attendee.Hero))
                { AddSystemLine("That resident is no longer here.", false); RefreshPartyMembers(); return; }
                ExecuteClose();
                ReignPartyChatScreenManager.OpenHomes(_homesSettlement, attendee.Hero);
                return;
            }
            if (attendee == null)
            {
                return;
            }

            attendee.IsActive = !attendee.IsActive;
            ReignLog.Info("Party chat toggle hero=" + attendee.Hero?.StringId + " active=" + attendee.IsActive);
            RefreshLabels();
        }

        private void PreviewPortrait(ReignPartyChatMemberVM attendee)
        {
            if (attendee == null || string.IsNullOrWhiteSpace(attendee.PortraitId) || string.IsNullOrWhiteSpace(attendee.PortraitTextureProviderName))
            {
                return;
            }

            PortraitPreviewName = attendee.Name;
            PortraitPreviewId = attendee.PortraitId;
            PortraitPreviewAdditionalArgs = attendee.PortraitAdditionalArgs ?? string.Empty;
            PortraitPreviewTextureProviderName = attendee.PortraitTextureProviderName;
            _portraitPreviewCacheKey = attendee.PortraitCacheKey;
            OnPropertyChanged(nameof(PortraitPreviewCacheKey));
            IsPortraitPreviewVisible = true;
        }

        private void OpenEncyclopedia(ReignPartyChatMemberVM attendee)
        {
            if (attendee?.Hero == null)
            {
                return;
            }

            if (_openEncyclopediaOverlay != null)
            {
                _openEncyclopediaOverlay(attendee.Hero);
                return;
            }

            ReignPortraitBridge.OpenHeroEncyclopedia(attendee.Hero);
        }

        private void GenerateAIPortrait(ReignPartyChatMemberVM attendee)
        {
            if (attendee?.Hero == null)
            {
                return;
            }

            if (_requestPortraitOverlay != null)
            {
                _requestPortraitOverlay(attendee.Hero);
                return;
            }

            ReignPortraitBridge.RequestPortrait(attendee.Hero);
        }

        private void RefreshPartyMembers()
        {
            if (IsHomesMode)
            {
                PartyMembers.Clear();
                foreach (Hero hero in ReignServices.EncounteredResidents.ResidentsAt(_homesSettlement))
                {
                    string role = ReignServices.EncounteredResidents.Find(hero)?.Role ?? "Resident";
                    role = Regex.Replace(role, "(?<=[a-z])([A-Z])", " $1");
                    PartyMembers.Add(new ReignPartyChatMemberVM(hero, hero == _homeResident, ToggleMember,
                        PreviewPortrait, OpenEncyclopedia, GenerateAIPortrait, VisitResidentInPerson, role));
                }
                return;
            }
            if (_targetedReviewMode) return;
            if (_castleMode) { RefreshCastleMembers(); return; }
            HashSet<string> selected = new HashSet<string>(PartyMembers.Where(x => x.IsActive).Select(x => x.Hero?.StringId));
            PartyMembers.Clear();
            foreach (Hero hero in ReignPartyChatSession.GetAvailableConversationHeroes())
            {
                PartyMembers.Add(new ReignPartyChatMemberVM(hero, selected.Contains(hero.StringId), ToggleMember, PreviewPortrait, OpenEncyclopedia, GenerateAIPortrait));
            }
        }

        private void RefreshLabels()
        {
            RefreshContinueAvailability();
            if (IsHomesMode)
            {
                LocationText = _homeResident == null ? "People you have met here" : "At home with " + _homeResident.Name;
                SelectedSummary = _homeResident == null ? PartyMembers.Count + " known residents at home"
                    : "Private conversation";
                return;
            }
            if (_castleMode)
            {
                LocationText = _castleRoomName;
                SelectedSummary = _castleRoomName;
                return;
            }
            LocationText = _session.BuildLocationText();
            List<Hero> selected = GetSelectedHeroes();
            SelectedSummary = selected.Count == 0
                ? "No characters selected"
                : "Selected: " + string.Join(", ", selected.Select(x => x.Name?.ToString()).Take(5)) + (selected.Count > 5 ? " +" + (selected.Count - 5) : string.Empty);
        }

        private List<Hero> GetSelectedHeroes()
        {
            if (IsHomesMode) return IsResidentAvailable(_homeResident)
                ? new List<Hero> { _homeResident } : new List<Hero>();
            return PartyMembers
                .Where(x => x.IsActive)
                .Select(x => x.Hero)
                .Where(x => x != null)
                .Distinct()
                .ToList();
        }

        private List<Hero> GetAutomationSelectedHeroes()
        {
            List<Hero> selected = GetSelectedHeroes();
            Dictionary<string, Hero> byId = selected
                .Where(hero => !string.IsNullOrWhiteSpace(hero.StringId))
                .GroupBy(hero => hero.StringId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
            List<Hero> ordered = _automationHeroOrder
                .Where(byId.ContainsKey)
                .Select(id => byId[id])
                .ToList();
            ordered.AddRange(selected.Where(hero => !ordered.Any(existing =>
                string.Equals(existing.StringId, hero.StringId, StringComparison.OrdinalIgnoreCase))));
            return ordered;
        }

        private bool IsStillSelected(Hero hero)
        {
            if (IsHomesMode) return hero == _homeResident && IsResidentAvailable(hero);
            return hero != null && PartyMembers.Any(x => x.IsActive && x.Hero?.StringId == hero.StringId);
        }

        private bool IsResidentAvailable(Hero hero) => hero != null && Settlement.CurrentSettlement == _homesSettlement
            && ReignServices.EncounteredResidents?.ResidentsAt(_homesSettlement).Contains(hero) == true;

        private void VisitResidentInPerson(ReignPartyChatMemberVM member)
        {
            if (IsBusy || !IsResidentAvailable(member?.Hero)) return;
            Hero hero = member.Hero;
            ExecuteClose();
            ReignServices.EncounteredResidents.VisitInPerson(hero);
        }

        private string SanitizeReplyText(string text, Hero speaker, IEnumerable<Hero> activeHeroes)
        {
            string clean = (text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(clean))
            {
                return string.Empty;
            }

            List<Hero> knownHeroes = new List<Hero>();
            AddKnownHero(knownHeroes, Hero.MainHero);
            AddKnownHero(knownHeroes, speaker);
            foreach (Hero hero in activeHeroes ?? Enumerable.Empty<Hero>())
            {
                AddKnownHero(knownHeroes, hero);
            }

            foreach (ReignPartyChatMemberVM member in PartyMembers)
            {
                AddKnownHero(knownHeroes, member.Hero);
            }

            foreach (Hero hero in knownHeroes)
            {
                string displayName = GetHeroDisplayName(hero, null);
                clean = ReplaceTechnicalId(clean, hero.StringId, displayName);
                clean = ReplaceTechnicalId(clean, hero.CharacterObject?.StringId, displayName);
            }

            string playerName = GetHeroDisplayName(Hero.MainHero, "the player character");
            clean = Regex.Replace(clean, @"\bthe player character\b", playerName, RegexOptions.IgnoreCase);
            clean = Regex.Replace(clean, @"\bthe player\b", playerName, RegexOptions.IgnoreCase);
            clean = Regex.Replace(clean, @"\bPlayer\b", playerName);
            clean = Regex.Replace(clean, @"\bmain_hero\b", playerName, RegexOptions.IgnoreCase);
            clean = Regex.Replace(clean, @"\baddress:[A-Za-z0-9_]+\b", string.Empty, RegexOptions.IgnoreCase);
            clean = Regex.Replace(clean, @"\bCharacterObject_\d+\b", "someone", RegexOptions.IgnoreCase);
            clean = Regex.Replace(clean, @"[ \t]+", " ");
            clean = Regex.Replace(clean, @"[ \t]*\r?\n[ \t]*", "\n");
            clean = Regex.Replace(clean, @"\n{3,}", "\n\n").Trim();
            return clean;
        }

        private static bool IsSilence(string text)
        {
            string clean = (text ?? string.Empty).Trim().Trim('*').Trim();
            return string.Equals(clean, "silence", StringComparison.OrdinalIgnoreCase)
                || string.Equals(clean, "silent", StringComparison.OrdinalIgnoreCase);
        }

        private static void AddKnownHero(List<Hero> heroes, Hero hero)
        {
            if (hero == null || heroes.Any(x => x == hero || x.StringId == hero.StringId))
            {
                return;
            }

            heroes.Add(hero);
        }

        private static string ReplaceTechnicalId(string text, string technicalId, string displayName)
        {
            if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(technicalId) || string.IsNullOrWhiteSpace(displayName))
            {
                return text ?? string.Empty;
            }

            string escaped = Regex.Escape(technicalId);
            string clean = Regex.Replace(text, @"\baddress:" + escaped + @"\b", displayName, RegexOptions.IgnoreCase);
            return Regex.Replace(clean, @"\b" + escaped + @"\b", displayName, RegexOptions.IgnoreCase);
        }

        private static string GetHeroDisplayName(Hero hero, string fallback)
        {
            string name = hero?.Name?.ToString();
            return string.IsNullOrWhiteSpace(name) ? fallback : name.Trim();
        }

        internal static string CastleOpeningProgressText(string speakerName, int current, int total)
        {
            int safeTotal = Math.Max(1, total);
            int safeCurrent = Math.Min(Math.Max(1, current), safeTotal);
            string displayName = string.IsNullOrWhiteSpace(speakerName) ? "the next guest" : speakerName.Trim();
            return "Waiting for " + displayName + " to speak... (" + safeCurrent + " of " + safeTotal + ")";
        }

        private void AddSystemLine(string text, bool recordTranscript)
        {
            if (_isFinalized) return;
            AddChatLine("Party", text, "system");
            if (recordTranscript)
            {
                _session.RecordTranscriptLine("Party", text);
            }
        }

        private void AddChatLine(string speaker, string text, string role, string exchangeId = null)
        {
            if (_isFinalized) return;
            ChatLines.Add(new ReignChatLineVM(speaker, text, role) { ExchangeId = exchangeId ?? _session?.CurrentSceneTurnId ?? "" });
            ChatScrollVersion++;
        }
    }
}
