using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;
using ReignBeta.Actions;
using ReignBeta.Events;
using ReignBeta.Integration;
using ReignBeta.Runtime;
using ReignBeta.Shared.Characters;
using ReignBeta.UI;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace ReignBeta.Campaign
{
    public sealed partial class ReignNpcLivesBehavior
    {
        private void AddLifeMenus(CampaignGameStarter starter)
        {
            foreach (string menu in new[] { "town", "castle", "village", "hideout_place" })
                starter.AddGameMenuOption(menu, "reign_story_locations_" + menu, "Story Locations",
                    args => { args.optionLeaveType = GameMenuOption.LeaveType.Submenu;
                        return StoryLocationsAvailable(); },
                    args => { args.MapState?.ExitMenuMode(); OpenLifeJournal(true); }, false, 6);
        }
        private bool StoryLocationsAvailable() => _initialized && StoriesAtCurrentPlace().Any();
        private void RefreshStoryLocationMenu(bool wasAvailable)
        {
            if (wasAvailable == StoryLocationsAvailable()) return;
            var campaign = TaleWorlds.CampaignSystem.Campaign.Current;
            var context = campaign?.CurrentMenuContext;
            string menu = context?.GameMenu?.StringId ?? "";
            if (!new[] { "town", "castle", "village", "hideout_place" }.Contains(menu)) return;
            // Repaint the existing menu after asynchronous state initialization or
            // a new/completed story; re-entering it would disturb native context.
            campaign.GameMenuManager.RefreshMenuOptions(context);
        }
        private bool WithPlayer(string id)
        {
            Hero hero = ReignActionService.HeroById(id);
            return Free(hero) && hero.PartyBelongedTo == MobileParty.MainParty
                && MobileParty.MainParty.MemberRoster.Contains(hero.CharacterObject);
        }
        private IEnumerable<NpcStoryInstance> StoriesAtCurrentPlace() => State.Stories.Where(s =>
            s.Status == "active" && s.PhaseIndex < s.Definition.Phases.Count && WithPlayer(s.HeroId)
            && (s.LocationId.Length == 0 || s.LocationId == (Settlement.CurrentSettlement?.StringId ?? "")));
        internal void OpenLifeJournal(bool locationsOnly = false)
        {
            if (!_initialized) { InformationManager.DisplayMessage(new InformationMessage("Personal lives are still loading.")); return; }
            var entries = new List<InquiryElement>();
            foreach (var story in (locationsOnly ? StoriesAtCurrentPlace() : State.Stories.AsEnumerable()).Reverse())
            {
                string hero = ReignActionService.HeroById(story.HeroId)?.Name?.ToString() ?? "Companion";
                string phase = story.PhaseIndex < story.Definition.Phases.Count ? story.Definition.Phases[story.PhaseIndex].Title : story.Definition.Title;
                string place = Settlement.Find(story.LocationId)?.Name?.ToString() ?? "your current surroundings";
                entries.Add(LifeJournalEntry("story:" + story.Id, hero + " — " + phase + " (" + story.Status.Replace('_', ' ') + ")\n" + place));
            }
            if (!locationsOnly)
            {
                foreach (var failure in _failedOperations)
                    entries.Add(LifeJournalEntry("failure:" + failure.Key, "Needs review — " + failure.Value));
                foreach (var leave in State.Leaves.Where(l => l.Status != "cancelled").Reverse())
                    entries.Add(LifeJournalEntry("leave:" + leave.Id,
                        (ReignActionService.HeroById(leave.HeroId)?.Name?.ToString() ?? "Companion") + " — personal business (" + leave.Status + ")"
                        + (leave.ReturnDay.HasValue ? "\nExpected return: day " + leave.ReturnDay.Value.ToString("0.##", CultureInfo.InvariantCulture) : "")));
                foreach (var a in State.Announcements.Where(a => a.PlayerId == Hero.MainHero.StringId).Reverse())
                    entries.Add(LifeJournalEntry("announcement:" + a.Id, "Party announcement — day " + a.WorldDay.ToString("0.##", CultureInfo.InvariantCulture)
                        + "\n" + a.Text));
                foreach (var journey in _meetingTravel.Values.Where(j => j.ControlAgreementId.Length == 0).Reverse())
                {
                    var m = journey.Meeting;
                    entries.Add(LifeJournalEntry("meeting:" + m.Id, "Meeting — " + m.Purpose + " (" + m.Status + ")\n"
                        + MeetingPlaceName(m.DestinationId)));
                }
            }
            if (entries.Count == 0) { InformationManager.DisplayMessage(new InformationMessage("There are no personal matters to review yet.")); return; }
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                locationsOnly ? "Story Locations" : "Personal lives", "Choose an experience or commitment to review.",
                entries, true, 1, 1, "Open", "Close", selected => {
                    string key = selected?.FirstOrDefault()?.Identifier as string;
                    if (key == null) return;
                    if (key.StartsWith("failure:", StringComparison.Ordinal)) { ShowLifeNote("Personal matter needs review", _failedOperations[key.Substring(8)]); return; }
                    if (key.StartsWith("announcement:", StringComparison.Ordinal))
                    {
                        var announcement = State.Announcements.FirstOrDefault(a => "announcement:" + a.Id == key);
                        if (announcement != null) ShowLifeNote("Party announcement", announcement.Text + "\n\nHeard by: "
                            + string.Join(", ", announcement.RecipientIds.Select(id => ReignActionService.HeroById(id)?.Name?.ToString() ?? id)));
                        return;
                    }
                    if (key.StartsWith("story:", StringComparison.Ordinal)) OpenStory(State.Stories.FirstOrDefault(s => "story:" + s.Id == key));
                    else if (key.StartsWith("leave:", StringComparison.Ordinal))
                    {
                        var leave = State.Leaves.FirstOrDefault(l => "leave:" + l.Id == key);
                        Hero hero = ReignActionService.HeroById(leave?.HeroId);
                        if (leave != null && WithPlayer(leave.HeroId)) ReignIndividualChatScreenManager.OpenForHero(hero, new JObject { ["npcLifeRequestId"] = leave.Id }, null);
                        else if (leave != null) ShowLifeNote("Personal leave", "Status: " + leave.Status
                            + ". Time away: " + leave.AbsenceDays.ToString("0.##", CultureInfo.InvariantCulture) + " days after departure.");
                    }
                    else
                    {
                        var journey = _meetingTravel.Values.FirstOrDefault(m => "meeting:" + m.Meeting.Id == key && m.ControlAgreementId.Length == 0);
                        if (journey == null) return;
                        var m = journey.Meeting;
                        ShowLifeNote("Summoned meeting", m.Purpose + "\n" + (m.ScheduledArrivalDay.HasValue
                            ? "Agreed arrival: day " + m.ScheduledArrivalDay.Value.ToString("0.##", CultureInfo.InvariantCulture)
                                + " (" + m.ArrivalDelayDays.Value.ToString("0.##", CultureInfo.InvariantCulture) + " days from agreement).\n" : "") + (m.ArrivalDay.HasValue
                            ? "Arrival: day " + m.ArrivalDay.Value.ToString("0.##", CultureInfo.InvariantCulture)
                                + ". Both remain until day " + m.ReleaseDay.Value.ToString("0.##", CultureInfo.InvariantCulture) + "."
                            : "The leader waits for the summoned person. Both will then stay " + m.StayDays.ToString("0.##", CultureInfo.InvariantCulture)
                                + " days from actual arrival. A battle or unsafe relocation can postpone arrival.")
                            + (m.Interruption.Length > 0 ? "\n" + m.Interruption : "")
                            + (!journey.Terminal ? "\nTo change the place or duration, or cancel, approach the leader and use the corresponding meeting action. Both sides must accept the new terms." : ""));
                    }
                }, null, "", false), true, false);
        }
        private static InquiryElement LifeJournalEntry(string id, string details)
        {
            // Native inquiry rows have a fixed height. Keep their label compact;
            // the full title, status and location remain available in the hint.
            string label = details.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ').Trim();
            int[] characters = StringInfo.ParseCombiningCharacters(label);
            if (characters.Length > 32) label = label.Substring(0, characters[31]).TrimEnd() + "…";
            return new InquiryElement(id, label, null, true, details);
        }
        private static void ShowLifeNote(string title, string text) =>
            InformationManager.ShowInquiry(new InquiryData(title, text, true, false, "Close", "", null, null), true);
        private void OpenStory(NpcStoryInstance story)
        {
            if (story == null) return;
            Hero hero = ReignActionService.HeroById(story.HeroId);
            if (!WithPlayer(story.HeroId)) { ShowLifeNote(story.Definition.Title, "Your companion must be present to continue."); return; }
            if (story.Status == "invited")
            { ReignIndividualChatScreenManager.OpenForHero(hero, new JObject { ["npcStoryId"] = story.Id, ["npcLifeRequestId"] = story.Id,
                ["npcLivesRevision"] = story.Revision, ["npcLifeInvitationEntry"] = true }, null); return; }
            if (story.Status != "active" || story.PhaseIndex >= story.Definition.Phases.Count)
            { ShowLifeNote(story.Definition.Title, "Status: " + story.Status.Replace('_', ' ') + ". Completed chapters: " + story.CompletedPhaseIds.Count + "."); return; }
            if (!StoriesAtCurrentPlace().Contains(story))
            { ShowLifeNote(story.Definition.Title, "Continue at " + (Settlement.Find(story.LocationId)?.Name?.ToString() ?? "the agreed place") + " with " + hero.Name + "."); return; }
            var phase = story.Definition.Phases[story.PhaseIndex];
            var native = phase.Objectives.FirstOrDefault(o => (o.Kind == "duel" || o.Kind == "hideout") && !story.Evidence.Any(e => e.ObjectiveId == o.Id));
            if (native != null)
            {
                InformationManager.ShowInquiry(new InquiryData("Story Encounter — " + phase.Title,
                    native.Kind == "duel" ? "Begin your agreed practice duel with " + hero.Name + "?" : "Enter the agreed bandit hideout with " + hero.Name + "?",
                    true, true, "Begin", "Later", () => StartStoryEncounter(story.Id, native.Id), null), true);
                return;
            }
            var record = new SocialEventRecord {
                EventId = "npc_story_scene_" + story.Id + "_" + story.Revision, TemplateId = "generated_wilderness",
                DisplayName = "Story Encounter — " + phase.Title, IsGeneratedWildernessEvent = true,
                HostHeroStringId = hero.StringId, AttendeeHeroStringIds = new List<string> { hero.StringId },
                SettlementStringId = story.LocationId, GeneratedTerrainKey = "plain",
                GeneratedLocationText = Settlement.Find(story.LocationId)?.Name?.ToString() ?? "your camp",
                GeneratedOpeningText = phase.OpeningText.Replace("{npc}", hero.Name.ToString()),
                GeneratedPlayerHook = "", GeneratedTimeOfDayText = CampaignTime.Now.CurrentHourInDay.ToString("0") + ":00"
            };
            var session = new ReignSocialEventSession(record) { PersonalStory = story };
            ReignSocialEventScreenManager.Open(session, null);
        }
    }
}
