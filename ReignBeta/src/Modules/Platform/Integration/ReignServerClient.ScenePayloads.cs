using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ReignBeta.Campaign;
using ReignBeta.Court;
using ReignBeta.Events;
using ReignBeta.Government;
using ReignBeta.World;
using ReignPortraits;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.Settlements.Workshops;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;
using ReignBeta.Characters.Campaign;
using ReignBeta.Characters.Integration;
using ReignBeta.Dialogue.Integration;
using ReignBeta.Dialogue.Runtime;
using ReignBeta.Diplomacy.Campaign;
using ReignBeta.Government.Campaign;
using ReignBeta.Persistence.Integration;
using ReignBeta.Platform.Campaign;
using ReignBeta.Platform.Runtime;
using ReignBeta.Platform.Settings;
using ReignBeta.Portraits.Integration;
using ReignBeta.Relationships.Campaign;
using ReignBeta.Relationships.Integration;
using ReignBeta.WorldSimulation.Campaign;
using ReignBeta.WorldSimulation.Integration;
using ReignBeta.WorldSimulation.News;
using ReignBeta.WorldSimulation.Runtime;
using ReignBeta.WorldSimulation.World;
#if !REIGN_EXCLUDE_COURT
using ReignBeta.Court.Campaign;
#endif

namespace ReignBeta.Platform.Integration
{
    public static partial class ReignServerClient
    {
        private static string FriendlyConversationFailure(JObject response, string correlationId)
        {
            string code = response?["llm"]?["errorCode"]?.Value<string>() ?? string.Empty;
            string message = code == "structured_response_incomplete_after_retries"
                ? "The AI could not complete this reply. Please try again."
                : code == "roleplay_continuity_repair_unusable"
                    ? "The AI reply could not pass the conversation checks. Please try again."
                    : "The AI could not complete this reply. Please try again.";
            string reference = string.IsNullOrWhiteSpace(correlationId)
                ? response?.Value<string>("correlationId") ?? string.Empty
                : correlationId;
            return string.IsNullOrWhiteSpace(reference) ? message : message + " Reference: " + reference.Trim();
        }

        private static string FormatTimingSummary(JObject timing)
        {
            if (timing == null)
            {
                return string.Empty;
            }

            List<string> parts = new List<string>();
            foreach (JProperty property in timing.Properties())
            {
                parts.Add(property.Name + "=" + property.Value);
            }

            return string.Join(" ", parts);
        }

        private static string BuildUrl(string route)
        {
            return ReignServerEndpoint.BuildUrl(route);
        }

        private static JObject BuildGroupHistoryContext(IEnumerable<Hero> attendees)
        {
            var player = Hero.MainHero;
            var listeners = new JArray();
            foreach (Hero listener in attendees.Where(hero => hero != null && hero != player).Distinct())
                listeners.Add(new JObject
                {
                    ["heroStringId"] = listener.StringId,
                    ["kingdomId"] = listener.Clan?.Kingdom?.StringId ?? listener.MapFaction?.StringId ?? string.Empty,
                    ["skills"] = BuildGroupHistorySkills(listener),
                    ["sceneRisk"] = BuildLieSceneRisk(player, listener)
                });
            return new JObject { ["claimantSkills"] = BuildGroupHistorySkills(player), ["listeners"] = listeners };
        }

        private static JObject BuildGroupHistorySkills(Hero hero) => hero == null ? null : new JObject
        {
            ["roguery"] = hero.GetSkillValue(DefaultSkills.Roguery),
            ["charm"] = hero.GetSkillValue(DefaultSkills.Charm)
        };

        private static JObject BuildSocialEventPayload(ReignSocialEventSession session, Hero speaker, string playerText)
        {
            SocialEventRecord record = session.Record;
            JArray attendees = new JArray();
            var present = record.GetAttendees().ToList();
            foreach (Hero hero in present)
            {
                attendees.Add(BuildHeroProfile(hero));
            }

            return new JObject
            {
                ["campaignId"] = GetCampaignId(),
                ["eventId"] = record.EventId ?? string.Empty,
                ["timelineId"] = ReignServices.WorldHistory?.TimelineId ?? "main",
                ["templateId"] = record.TemplateId ?? string.Empty,
                ["wildernessInitiatorHeroStringId"] = record.IsGeneratedWildernessEvent ? record.GeneratedInitiatorHeroStringId ?? string.Empty : string.Empty,
                ["wildernessApproachText"] = record.IsGeneratedWildernessEvent ? record.GeneratedApproachText ?? string.Empty : string.Empty,
                ["displayName"] = record.DisplayName ?? string.Empty,
                ["settlementId"] = record.SettlementStringId ?? string.Empty,
                ["hostHeroStringId"] = record.HostHeroStringId ?? string.Empty,
                ["speakerHeroStringId"] = speaker?.StringId ?? string.Empty,
                ["speaker"] = speaker == null ? null : BuildHeroProfile(speaker),
                ["mainHeroStringId"] = Hero.MainHero?.StringId ?? string.Empty,
                ["playerHeroStringId"] = Hero.MainHero?.StringId ?? string.Empty,
                ["playerIdentity"] = BuildPlayerIdentityContext(),
                ["playerName"] = Hero.MainHero?.Name?.ToString() ?? "Player",
                ["playerText"] = playerText ?? string.Empty,
                ["worldDay"] = TaleWorlds.CampaignSystem.Campaign.Current == null ? 0d : CampaignTime.Now.ToDays,
                ["phaseId"] = session.CurrentPhase.PhaseId ?? string.Empty,
                ["phaseTitle"] = session.CurrentPhase.Title ?? string.Empty,
                ["phaseDescription"] = session.CurrentPhase.SettingSummary ?? string.Empty,
                ["sceneContext"] = session.BuildSceneContext(),
                ["nativePoliticalContext"] = BuildNativePoliticalContext(speaker),
                ["temporaryPartyGuest"] = ReignServices.TemporaryPartyGuest
                    ?.BuildConversationContext(speaker) ?? new JObject(),
                ["nativeSceneMovement"] = ReignSceneActionMissionBehavior.Context(),
                ["playerPartyId"] = MobileParty.MainParty?.StringId ?? string.Empty,
                ["campaignCalendar"] = new JObject { ["daysPerSeason"] = ReignCalendarService.DaysPerSeason,
                    ["daysPerWeek"] = CampaignTime.DaysInWeek, ["daysPerYear"] = ReignCalendarService.DaysPerYear },
                ["clanAccords"] = ReignServices.ClanAccords?.ConversationContext(speaker) ?? new JObject(),
                ["sceneTurnId"] = record.EventId + "_" + session.CurrentPhaseIndex + "_" + session.PhaseExchangeCount,
                ["playerAttentionPresence"] = ReignPlayerApproachClient.BuildPlayerAttentionPresence(speaker),
                ["sceneParticipants"] = BuildSceneParticipants(record.GetAttendees()),
                ["localPresence"] = BuildLocalPresenceSnapshot(record.GetAttendees()),
                ["gossipPresence"] = new JObject(), // Materialized once after readiness, before sending.
                ["isDancePhase"] = session.Template.TemplateId.StartsWith("dance_", StringComparison.OrdinalIgnoreCase)
                    && (session.CurrentPhase.PhaseId == "first_dance" || session.CurrentPhase.PhaseId == "refreshments_and_second_dance"),
                ["playerIsFemale"] = Hero.MainHero?.IsFemale ?? false,
                ["activeHeroIds"] = new JArray(session.ActiveHeroStringIds),
                ["attendees"] = attendees,
                ["groupHistoryContext"] = BuildGroupHistoryContext(present),
                ["transcript"] = new JArray(session.RecentTranscriptLines ?? new List<string>()),
                ["actionResolutionIndex"] = BuildActionResolutionIndex(
                    speaker,
                    record.GetAttendees(),
                    RequiresFullActionResolutionIndex(playerText))
            };
        }

        internal static JObject BuildPartyChatPayload(ReignPartyChatSession session, Hero speaker, List<Hero> activeHeroes, string playerText)
        {
            JArray attendees = new JArray();
            foreach (Hero hero in activeHeroes.Where(x => x != null).Distinct())
            {
                attendees.Add(BuildHeroProfile(hero));
            }

            JArray structuredTranscript = new JArray();
            foreach (ReignPartyChatTranscriptLine line in session.RecentTranscriptEntries ?? new List<ReignPartyChatTranscriptLine>())
            {
                structuredTranscript.Add(new JObject
                {
                    ["sequence"] = line.Sequence,
                    ["sessionId"] = line.SessionId ?? string.Empty,
                    ["exchangeId"] = line.ExchangeId ?? string.Empty,
                    ["speakerHeroStringId"] = line.SpeakerHeroStringId ?? string.Empty,
                    ["speaker"] = line.Speaker ?? string.Empty,
                    ["role"] = line.Role ?? string.Empty,
                    ["audienceHeroStringId"] = line.AudienceHeroStringId ?? string.Empty,
                    ["text"] = line.Text ?? string.Empty
                });
            }
            int speakerIndex = activeHeroes.FindIndex(hero => hero?.StringId == speaker?.StringId);

            return new JObject
            {
                ["campaignId"] = GetCampaignId(),
                ["eventId"] = "party_chat",
                ["timelineId"] = ReignServices.WorldHistory?.TimelineId ?? "main",
                ["conversationSessionId"] = session.SessionId,
                ["templateId"] = "party_chat",
                ["npcLifeContext"] = session.NpcLifeContext,
                ["npcLifeAudienceIds"] = new JArray(activeHeroes.Where(h => h != null).Select(h => h.StringId).Concat(new[] { Hero.MainHero?.StringId ?? "" })),
                ["displayName"] = "Party Chat",
                ["speakerHeroStringId"] = speaker?.StringId ?? string.Empty,
                ["speaker"] = speaker == null ? null : BuildHeroProfile(speaker),
                ["mainHeroStringId"] = Hero.MainHero?.StringId ?? string.Empty,
                ["playerHeroStringId"] = Hero.MainHero?.StringId ?? string.Empty,
                ["playerIdentity"] = BuildPlayerIdentityContext(),
                ["playerName"] = Hero.MainHero?.Name?.ToString() ?? "Player",
                ["playerText"] = playerText ?? string.Empty,
                ["worldDay"] = TaleWorlds.CampaignSystem.Campaign.Current == null ? 0d : CampaignTime.Now.ToDays,
                ["channel"] = "party_chat",
                ["locationId"] = ResolveDialogueLocationId(speaker),
                ["phaseId"] = "party_chat",
                ["phaseTitle"] = "Party Chat",
                ["phaseDescription"] = "A group conversation with selected party or local characters.",
                ["sceneContext"] = session.BuildSceneContext(activeHeroes),
                ["nativePoliticalContext"] = BuildNativePoliticalContext(speaker),
                ["clanAccords"] = ReignServices.ClanAccords?.ConversationContext(speaker) ?? new JObject(),
                ["sceneTurnId"] = string.IsNullOrWhiteSpace(session.CurrentSceneTurnId)
                    ? session.SessionId + "_turn_" + session.PlayerInputCount
                    : session.CurrentSceneTurnId,
                ["turnId"] = string.IsNullOrWhiteSpace(session.CurrentSceneTurnId)
                    ? session.SessionId + "_turn_" + session.PlayerInputCount
                    : session.CurrentSceneTurnId,
                ["playerAttentionPresence"] = ReignPlayerApproachClient.BuildPlayerAttentionPresence(speaker),
                ["approachRequestId"] = session.ApproachRequestId,
                ["approachOpening"] = !string.IsNullOrWhiteSpace(session.ApproachRequestId) && session.PlayerInputCount == 0,
                ["sceneParticipants"] = BuildSceneParticipants(activeHeroes),
                ["localPresence"] = BuildLocalPresenceSnapshot(activeHeroes),
                ["gossipPresence"] = new JObject(), // Materialized once after readiness, before sending.
                ["activeHeroIds"] = new JArray(activeHeroes.Where(x => x != null).Select(x => x.StringId)),
                ["participants"] = new JArray(activeHeroes.Where(x => x != null)
                    .Select(x => x.StringId)
                    .Concat(new[] { Hero.MainHero?.StringId ?? string.Empty })
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Distinct(StringComparer.OrdinalIgnoreCase)),
                ["attendees"] = attendees,
                ["groupHistoryContext"] = BuildGroupHistoryContext(activeHeroes),
                ["transcript"] = new JArray(session.RecentTranscriptLines ?? new List<string>()),
                ["groupTranscript"] = structuredTranscript,
                ["partySpeakerIndex"] = speakerIndex,
                ["partySpeakerCount"] = activeHeroes.Count,
                ["actionResolutionIndex"] = BuildActionResolutionIndex(
                    speaker,
                    activeHeroes,
                    RequiresFullActionResolutionIndex(playerText))
            };
        }

        internal static JObject BuildLocalPresenceSnapshot(IEnumerable<Hero> heroes)
        {
            var participants = (heroes ?? Enumerable.Empty<Hero>()).Where(x => x != null)
                .Concat(Hero.MainHero == null ? Enumerable.Empty<Hero>() : new[] { Hero.MainHero }).Distinct().ToList();
            Settlement settlement = Settlement.CurrentSettlement ?? MobileParty.MainParty?.CurrentSettlement;
            string room = CampaignMission.Current?.Location?.StringId ?? string.Empty;
            var missionHeroes = Mission.Current?.Agents?.Select(agent => (agent.Character as CharacterObject)?.HeroObject)
                .Where(hero => hero != null).Distinct().ToList() ?? new List<Hero>();
            var people = participants.Concat(participants.SelectMany(hero => new[] { hero.Father, hero.Mother, hero.Spouse }))
                .Concat(missionHeroes)
                .Concat(settlement == null ? Enumerable.Empty<Hero>() : settlement.HeroesWithoutParty.AsEnumerable())
                .Where(hero => hero != null).Distinct().ToList();
            var rows = new JArray();
            foreach (Hero hero in people.Take(96))
            {
                Settlement actual = hero == Hero.MainHero ? settlement : hero.CurrentSettlement ?? hero.PartyBelongedTo?.CurrentSettlement;
                bool inScene = missionHeroes.Contains(hero);
                string townPresence = !hero.IsAlive ? "absent" : settlement == null ? "unknown" : actual == settlement ? "present" : actual == null ? "unknown" : "absent";
                string roomPresence = inScene && !string.IsNullOrWhiteSpace(room) ? "present" : townPresence == "absent" ? "absent" : "unknown";
                rows.Add(new JObject
                {
                    ["heroStringId"] = hero.StringId, ["name"] = hero.Name?.ToString() ?? hero.StringId,
                    ["settlementPresence"] = townPresence, ["roomPresence"] = roomPresence,
                    ["conversationWitness"] = hero.IsAlive && participants.Contains(hero),
                    ["alive"] = townPresence == "present" || inScene || participants.Contains(hero) ? new JValue(hero.IsAlive) : JValue.CreateNull(),
                    ["prisoner"] = townPresence == "present" || inScene || participants.Contains(hero) ? new JValue(hero.IsPrisoner) : JValue.CreateNull(),
                    ["source"] = inScene ? "current_mission_agent" : actual == settlement && settlement != null ? "native_settlement" : "native_presence_constraint"
                });
            }
            return new JObject
            {
                ["schema"] = "reign-local-presence-v1", ["settlementId"] = settlement?.StringId ?? string.Empty,
                ["partyContext"] = BuildConversationPartyRoster(),
                ["roomId"] = room, ["worldDay"] = CampaignTime.Now.ToDays,
                ["partyPositionX"] = Math.Round((MobileParty.MainParty?.GetPosition2D ?? Vec2.Zero).X, 2),
                ["partyPositionY"] = Math.Round((MobileParty.MainParty?.GetPosition2D ?? Vec2.Zero).Y, 2),
                ["rosterComplete"] = false, ["omittedCount"] = Math.Max(0, people.Count - 96), ["people"] = rows
            };
        }

        private static JObject BuildConversationPartyRoster()
        {
            var main = MobileParty.MainParty;
            var members = new JArray();
            var prisoners = new JArray();
            int heroCount = 0, troopCount = 0, prisonerHeroCount = 0, prisonerTroopCount = 0;
            bool complete = main?.MemberRoster != null && main.PrisonRoster != null;
            if (complete)
            {
                foreach (var row in main.MemberRoster.GetTroopRoster())
                {
                    if (row.Number <= 0) continue;
                    if (row.Character == null) { complete = false; continue; }
                    Hero hero = row.Character?.HeroObject;
                    if (hero == null) { troopCount += row.Number; continue; }
                    heroCount += row.Number;
                    var guest = ReignServices.TemporaryPartyGuest?.Records
                        .FirstOrDefault(record => record.HeroStringId == hero.StringId && record.IsLive
                            && (record.Phase == ReignBeta.PartyAgency.ReignTemporaryGuestPhase.Active
                                || record.Phase == ReignBeta.PartyAgency.ReignTemporaryGuestPhase.ReviewDue
                                || record.Phase == ReignBeta.PartyAgency.ReignTemporaryGuestPhase.Departing));
                    var roles = new JArray();
                    foreach (string role in new[] { "Scout", "Surgeon", "Engineer", "Quartermaster" })
                    {
                        var property = main.GetType().GetProperty("Effective" + role);
                        if (property?.GetValue(main, null) is Hero holder && holder == hero) roles.Add(role.ToLowerInvariant());
                    }
                    members.Add(new JObject { ["heroStringId"] = hero.StringId, ["name"] = hero.Name?.ToString() ?? hero.StringId,
                        ["membership"] = guest == null ? "member" : "temporary_guest", ["source"] = "native_main_party_member_roster",
                        ["roleEvidence"] = roles });
                }
                foreach (var row in main.PrisonRoster.GetTroopRoster())
                {
                    if (row.Number <= 0) continue;
                    if (row.Character == null) { complete = false; continue; }
                    if (row.Character.HeroObject == null) prisonerTroopCount += row.Number;
                    else { prisonerHeroCount += row.Number; prisoners.Add(row.Character.HeroObject.StringId); }
                }
                complete &= heroCount + troopCount == main.MemberRoster.TotalManCount
                    && prisonerHeroCount + prisonerTroopCount == main.PrisonRoster.TotalManCount;
            }
            var result = new JObject { ["schema"] = "reign-party-context-v2", ["partyId"] = main?.StringId ?? "",
                ["worldDay"] = CampaignTime.Now.ToDays, ["captureId"] = Guid.NewGuid().ToString("N"),
                ["capturedUtc"] = DateTime.UtcNow.ToString("o"), ["complete"] = complete, ["members"] = members,
                ["prisoners"] = prisoners, ["source"] = "native_main_party_rosters" };
            if (complete)
            {
                result["memberCount"] = heroCount + troopCount;
                result["heroCount"] = heroCount;
                result["regularTroopCount"] = troopCount;
                result["woundedMemberCount"] = main.MemberRoster.TotalWounded;
                result["prisonerCount"] = prisonerHeroCount + prisonerTroopCount;
                result["prisonerHeroCount"] = prisonerHeroCount;
                result["regularPrisonerCount"] = prisonerTroopCount;
                result["armyLeaderPartyId"] = main.Army?.LeaderParty?.StringId ?? "";
                result["attachedToArmy"] = main.Army != null;
            }
            return result;
        }

        internal static JArray BuildSceneParticipants(IEnumerable<Hero> heroes)
        {
            List<Hero> roster = (heroes ?? Enumerable.Empty<Hero>())
                .Where(x => x != null && !string.IsNullOrWhiteSpace(x.StringId))
                .Concat(Hero.MainHero == null ? Enumerable.Empty<Hero>() : new[] { Hero.MainHero })
                .Distinct()
                .ToList();
            JArray result = new JArray();
            foreach (Hero hero in roster)
            {
                Hero sovereign = hero.Clan?.Kingdom?.Leader;
                Settlement settlement = hero.CurrentSettlement ?? hero.PartyBelongedTo?.CurrentSettlement;
                if (hero == Hero.MainHero)
                    settlement = Settlement.CurrentSettlement ?? MobileParty.MainParty?.CurrentSettlement ?? settlement;
                string locationClass = settlement != null && (settlement.IsTown || settlement.IsCastle) ? "formal" : "travel";
                string location = settlement == null
                    ? (hero.PartyBelongedTo?.Name?.ToString() == null ? "on the campaign map" : "traveling with " + hero.PartyBelongedTo.Name)
                    : (settlement.IsCastle ? "at the castle of " : settlement.IsVillage ? "at the village of " : "in the town of ") + settlement.Name;
                try
                {
                    string interior = settlement?.LocationComplex?.GetLocationOfCharacter(hero)?.Name?.ToString();
                    if (!string.IsNullOrWhiteSpace(interior)) location += ", in " + interior;
                    if (hero == Hero.MainHero && CampaignMission.Current?.Location != null)
                        location = location + ", in " + CampaignMission.Current.Location.Name;
                }
                catch
                {
                    // LocationComplex is not populated for remote settlements.
                }
                result.Add(new JObject
                {
                    ["heroStringId"] = hero.StringId,
                    ["name"] = hero.Name?.ToString() ?? hero.StringId,
                    ["role"] = hero == Hero.MainHero ? "player" : "npc",
                    ["age"] = Math.Round(hero.Age, 2),
                    ["isChild"] = hero.IsChild,
                    ["isFemale"] = hero.IsFemale,
                    ["isRuler"] = sovereign == hero,
                    ["clanId"] = hero.Clan?.StringId ?? string.Empty,
                    ["clanName"] = hero.Clan?.Name?.ToString() ?? string.Empty,
                    ["kingdomId"] = hero.Clan?.Kingdom?.StringId ?? hero.MapFaction?.StringId ?? string.Empty,
                    ["kingdomName"] = hero.Clan?.Kingdom?.InformalName?.ToString() ?? hero.MapFaction?.Name?.ToString() ?? string.Empty,
                    ["spouseId"] = hero.Spouse?.StringId ?? string.Empty,
                    ["spouseName"] = hero.Spouse?.Name?.ToString() ?? string.Empty,
                    ["fatherId"] = hero.Father?.StringId ?? string.Empty,
                    ["fatherName"] = hero.Father?.Name?.ToString() ?? string.Empty,
                    ["motherId"] = hero.Mother?.StringId ?? string.Empty,
                    ["motherName"] = hero.Mother?.Name?.ToString() ?? string.Empty,
                    ["childrenIds"] = new JArray((hero.Children ?? new List<Hero>()).Where(x => x != null).Select(x => x.StringId)),
                    ["childrenNames"] = new JArray((hero.Children ?? new List<Hero>()).Where(x => x != null).Select(x => x.Name?.ToString() ?? x.StringId)),
                    ["sovereignHeroStringId"] = sovereign?.StringId ?? string.Empty,
                    ["sovereignName"] = sovereign?.Name?.ToString() ?? string.Empty,
                    ["nativeLocationDescription"] = location,
                    ["nativeLocationClass"] = locationClass,
                    ["travelClothingDescription"] = BuildCivilianTravelClothingDescription(hero),
                    ["civilianEquipmentFingerprint"] = BuildCivilianEquipmentFingerprint(hero),
                    ["appearance"] = BuildSceneAppearanceProfile(hero)
                });
            }
            return result;
        }

        private static JObject BuildSceneAppearanceProfile(Hero hero)
        {
            JObject full = BuildAppearanceProfile(hero);
            JObject compact = new JObject();
            foreach (string key in new[]
            {
                "source", "trueStatusScore", "trueStatusLabel", "visibleStatusScore", "visibleStatusLabel",
                "statusMismatch", "presentationEffect", "firstView", "civilianEquipmentValue", "battleEquipmentValue"
            })
            {
                JToken value = full[key];
                if (value != null)
                {
                    compact[key] = value.DeepClone();
                }
            }
            compact["equippedMounts"] = new JArray(
                (full["civilianEquipment"] as JArray ?? new JArray()).OfType<JObject>()
                    .Where(x => x.Value<bool?>("isMount") == true)
                    .Select(x => new JObject
                    {
                        ["itemId"] = x.Value<string>("itemId") ?? string.Empty,
                        ["name"] = x.Value<string>("name") ?? string.Empty,
                        ["type"] = x.Value<string>("type") ?? string.Empty
                    }));
            return compact;
        }

        private static string BuildCivilianTravelClothingDescription(Hero hero)
        {
            Equipment equipment = hero?.CivilianEquipment ?? hero?.CharacterObject?.FirstCivilianEquipment ?? hero?.CharacterObject?.Equipment;
            JArray items = BuildEquipmentArray(equipment, "civilian");
            List<string> garments = items.OfType<JObject>()
                .Where(x => x.Value<bool?>("isArmorOrClothing") == true)
                .Select(x =>
                {
                    string modifier = x.Value<string>("modifier") ?? string.Empty;
                    string name = x.Value<string>("name") ?? string.Empty;
                    return string.IsNullOrWhiteSpace(modifier) ? name : modifier + " " + name;
                })
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            return garments.Count == 0
                ? "ordinary civilian traveling clothes"
                : string.Join(", ", garments);
        }

        private static string BuildCivilianEquipmentFingerprint(Hero hero)
        {
            string text = BuildCivilianTravelClothingDescription(hero);
            unchecked
            {
                int hash = 17;
                foreach (char c in text) hash = hash * 31 + c;
                return hash.ToString("X8");
            }
        }

        public static async Task TickConversationSceneStateAsync()
        {
            try
            {
                await PostJsonAsync("/conversation-scene/hourly-tick", new JObject
                {
                    ["campaignId"] = GetCampaignId(),
                    ["worldDay"] = TaleWorlds.CampaignSystem.Campaign.Current == null ? 0d : CampaignTime.Now.ToDays
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                ReignLog.Warn("Conversation scene-state hourly tick failed: " + ex.Message);
            }
        }

        private static string ExtractCharacterObjectId(string cacheKey)
        {
            if (string.IsNullOrWhiteSpace(cacheKey))
            {
                return string.Empty;
            }

            int open = cacheKey.LastIndexOf('(');
            int close = cacheKey.LastIndexOf(')');
            if (open < 0 || close <= open)
            {
                return string.Empty;
            }

            return cacheKey.Substring(open + 1, close - open - 1).Trim();
        }

        private static string ShortLog(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            return text.Length <= 32 ? text : text.Substring(0, 32) + "...";
        }

        internal static JObject BuildActionResolutionIndex(
            Hero speaker,
            IEnumerable<Hero> activeHeroes,
            bool includeWorldCatalog = true)
        {
            List<Hero> activeHeroList = (activeHeroes ?? Enumerable.Empty<Hero>()).Where(x => x != null).ToList();
            bool hasOrigin = TryGetReferencePosition(speaker, out Vec2 origin);
            Settlement currentSettlement =
                Settlement.CurrentSettlement
                ?? MobileParty.MainParty?.CurrentSettlement
                ?? speaker?.CurrentSettlement;
            HashSet<string> relevantSettlementIds = new HashSet<string>(
                new[]
                {
                    currentSettlement?.StringId,
                    Hero.MainHero?.HomeSettlement?.StringId,
                    speaker?.HomeSettlement?.StringId,
                    Hero.MainHero?.GovernorOf?.Settlement?.StringId,
                    speaker?.GovernorOf?.Settlement?.StringId
                }.Where(x => !string.IsNullOrWhiteSpace(x)),
                StringComparer.OrdinalIgnoreCase);
            JArray settlements = new JArray();
            foreach (Settlement settlement in Settlement.All.Where(x => x != null
                && (includeWorldCatalog || relevantSettlementIds.Contains(x.StringId))))
            {
                JObject entry = new JObject
                {
                    ["settlementId"] = settlement.StringId ?? string.Empty,
                    ["name"] = settlement.Name?.ToString() ?? string.Empty,
                    ["type"] = SettlementType(settlement),
                    ["cultureId"] = settlement.Culture?.StringId ?? string.Empty,
                    ["ownerClanId"] = settlement.OwnerClan?.StringId ?? string.Empty,
                    ["ownerClanName"] = settlement.OwnerClan?.Name?.ToString() ?? string.Empty,
                    ["factionId"] = settlement.MapFaction?.StringId ?? string.Empty,
                    ["factionName"] = settlement.MapFaction?.Name?.ToString() ?? string.Empty,
                    ["kingdomId"] = settlement.MapFaction?.StringId ?? settlement.OwnerClan?.Kingdom?.StringId ?? string.Empty,
                    ["kingdomName"] = settlement.MapFaction?.Name?.ToString() ?? settlement.OwnerClan?.Kingdom?.InformalName?.ToString() ?? string.Empty,
                    ["isTown"] = settlement.IsTown,
                    ["isCastle"] = settlement.IsCastle,
                    ["isVillage"] = settlement.IsVillage,
                    ["isHideout"] = settlement.IsHideout,
                    ["isFortification"] = settlement.IsFortification,
                    ["isUnderSiege"] = settlement.IsUnderSiege,
                    ["isUnderRaid"] = settlement.IsUnderRaid,
                    ["militia"] = settlement.Militia,
                    ["distance"] = hasOrigin ? Math.Sqrt(origin.DistanceSquared(settlement.GetPosition2D)) : 0d
                };

                Town town = settlement.Town;
                if (town != null)
                {
                    entry["prosperity"] = town.Prosperity;
                    entry["loyalty"] = town.Loyalty;
                    entry["security"] = town.Security;
                    entry["garrison"] = town.GarrisonParty?.MemberRoster?.TotalManCount ?? 0;
                    entry["boundVillages"] = settlement.BoundVillages?.Count ?? 0;
                    entry["governorHeroId"] = town.Governor?.StringId ?? string.Empty;
                    entry["governorName"] = town.Governor?.Name?.ToString() ?? string.Empty;
                }

                Village village = settlement.Village;
                if (village != null)
                {
                    entry["boundSettlementId"] = village.Bound?.StringId ?? string.Empty;
                    entry["boundSettlementName"] = village.Bound?.Name?.ToString() ?? string.Empty;
                    entry["villageState"] = village.VillageState.ToString();
                    entry["isDeserted"] = village.IsDeserted;
                }

                settlements.Add(entry);
            }

            HashSet<string> relevantKingdomIds = new HashSet<string>(
                new[]
                {
                    Clan.PlayerClan?.Kingdom?.StringId,
                    speaker?.Clan?.Kingdom?.StringId,
                    currentSettlement?.OwnerClan?.Kingdom?.StringId
                }.Where(x => !string.IsNullOrWhiteSpace(x)),
                StringComparer.OrdinalIgnoreCase);
            JArray kingdoms = new JArray();
            foreach (Kingdom kingdom in Kingdom.All.Where(x => x != null
                && (includeWorldCatalog || relevantKingdomIds.Contains(x.StringId))))
            {
                kingdoms.Add(new JObject
                {
                    ["kingdomId"] = kingdom.StringId ?? string.Empty,
                    ["name"] = kingdom.InformalName?.ToString() ?? kingdom.Name?.ToString() ?? string.Empty,
                    ["formalName"] = kingdom.Name?.ToString() ?? string.Empty,
                    ["leaderHeroId"] = kingdom.Leader?.StringId ?? string.Empty,
                    ["leaderName"] = kingdom.Leader?.Name?.ToString() ?? string.Empty,
                    ["rulingClanId"] = kingdom.RulingClan?.StringId ?? string.Empty,
                    ["rulingClanName"] = kingdom.RulingClan?.Name?.ToString() ?? string.Empty,
                    ["capitalSettlementId"] = KingdomCapital(kingdom)?.StringId ?? string.Empty,
                    ["capitalName"] = KingdomCapital(kingdom)?.Name?.ToString() ?? string.Empty,
                    ["clanCount"] = kingdom.Clans?.Count ?? 0,
                    ["townCount"] = kingdom.Towns?.Count ?? 0,
                    ["armyCount"] = kingdom.Armies?.Count ?? 0,
                    ["strength"] = kingdom.CurrentTotalStrength,
                    ["isAtWarWithPlayerKingdom"] = Clan.PlayerClan?.Kingdom != null && kingdom.IsAtWarWith(Clan.PlayerClan.Kingdom)
                });
            }

            HashSet<string> relevantClanIds = new HashSet<string>(
                activeHeroList
                    .Concat(new[] { Hero.MainHero, speaker })
                    .Where(x => x?.Clan != null && !string.IsNullOrWhiteSpace(x.Clan.StringId))
                    .Select(x => x.Clan.StringId),
                StringComparer.OrdinalIgnoreCase);
            if (currentSettlement?.OwnerClan != null)
            {
                relevantClanIds.Add(currentSettlement.OwnerClan.StringId);
            }
            JArray clans = new JArray();
            foreach (Clan clan in Clan.All.Where(x => x != null
                && (includeWorldCatalog || relevantClanIds.Contains(x.StringId))))
            {
                clans.Add(new JObject
                {
                    ["clanId"] = clan.StringId ?? string.Empty,
                    ["name"] = clan.Name?.ToString() ?? string.Empty,
                    ["leaderHeroId"] = clan.Leader?.StringId ?? string.Empty,
                    ["leaderName"] = clan.Leader?.Name?.ToString() ?? string.Empty,
                    ["kingdomId"] = clan.Kingdom?.StringId ?? string.Empty,
                    ["kingdomName"] = clan.Kingdom?.InformalName?.ToString() ?? string.Empty,
                    ["tier"] = clan.Tier,
                    ["gold"] = clan.Gold,
                    ["renown"] = clan.Renown,
                    ["influence"] = clan.Influence,
                    ["fiefCount"] = clan.Fiefs?.Count ?? 0,
                    ["initialHomeSettlementId"] = clan.InitialHomeSettlement?.StringId ?? string.Empty,
                    ["initialHomeSettlementName"] = clan.InitialHomeSettlement?.Name?.ToString() ?? string.Empty
                });
            }

            List<Hero> heroes = new List<Hero>();
            AddHeroIfMissing(heroes, Hero.MainHero);
            AddHeroIfMissing(heroes, speaker);
            AddHeroRelations(heroes, Hero.MainHero);
            AddHeroRelations(heroes, speaker);
            // Known local residents are valid named action targets (for example
            // asking their commander to release them), even without a clan.
            // Resolver references do not add them to the conversation hearing set.
            foreach (Hero resident in ReignServices.EncounteredResidents?.ResidentsAt(currentSettlement)
                ?? new List<Hero>()) AddHeroIfMissing(heroes, resident);
            foreach (Hero hero in activeHeroList)
            {
                AddHeroIfMissing(heroes, hero);
                AddHeroRelations(heroes, hero);
            }

            if (includeWorldCatalog)
            {
                foreach (Kingdom kingdom in Kingdom.All.Where(x => x != null))
                {
                    AddHeroIfMissing(heroes, kingdom.Leader);
                }

                foreach (Clan clan in Clan.All.Where(x => x != null))
                {
                    AddHeroIfMissing(heroes, clan.Leader);
                }

                foreach (Town town in Town.AllTowns.Where(x => x != null))
                {
                    AddHeroIfMissing(heroes, town.Governor);
                }

                foreach (Hero hero in Hero.AllAliveHeroes.Where(x => x != null && (x.IsLord || x.IsNotable || x.IsWanderer || x.IsPrisoner)))
                {
                    AddHeroIfMissing(heroes, hero);
                }
            }

            JArray heroRefs = new JArray();
            foreach (Hero hero in heroes)
            {
                heroRefs.Add(HeroReference(hero));
            }

            JArray aliases = new JArray
            {
                new JObject { ["alias"] = "player", ["entityType"] = "hero", ["id"] = Hero.MainHero?.StringId ?? string.Empty },
                new JObject { ["alias"] = "main_hero", ["entityType"] = "hero", ["id"] = Hero.MainHero?.StringId ?? string.Empty },
                new JObject { ["alias"] = "speaker", ["entityType"] = "hero", ["id"] = speaker?.StringId ?? string.Empty },
                new JObject { ["alias"] = "current_npc", ["entityType"] = "hero", ["id"] = speaker?.StringId ?? string.Empty },
                new JObject { ["alias"] = "that lord", ["entityType"] = "hero", ["id"] = speaker?.StringId ?? string.Empty },
                new JObject { ["alias"] = "that lady", ["entityType"] = "hero", ["id"] = speaker?.StringId ?? string.Empty },
                new JObject { ["alias"] = "the lord", ["entityType"] = "hero", ["id"] = speaker?.StringId ?? string.Empty },
                new JObject { ["alias"] = "the lady", ["entityType"] = "hero", ["id"] = speaker?.StringId ?? string.Empty },
                new JObject { ["alias"] = "me", ["entityType"] = "hero", ["id"] = speaker?.StringId ?? string.Empty },
                new JObject { ["alias"] = "myself", ["entityType"] = "hero", ["id"] = speaker?.StringId ?? string.Empty },
                new JObject { ["alias"] = "you", ["entityType"] = "hero", ["id"] = Hero.MainHero?.StringId ?? string.Empty },
                new JObject { ["alias"] = "player_spouse", ["entityType"] = "hero", ["id"] = Hero.MainHero?.Spouse?.StringId ?? string.Empty },
                new JObject { ["alias"] = "speaker_spouse", ["entityType"] = "hero", ["id"] = speaker?.Spouse?.StringId ?? string.Empty },
                new JObject { ["alias"] = "my wife", ["entityType"] = "hero", ["id"] = speaker?.Spouse?.StringId ?? string.Empty },
                new JObject { ["alias"] = "my husband", ["entityType"] = "hero", ["id"] = speaker?.Spouse?.StringId ?? string.Empty },
                new JObject { ["alias"] = "your wife", ["entityType"] = "hero", ["id"] = Hero.MainHero?.Spouse?.StringId ?? string.Empty },
                new JObject { ["alias"] = "your husband", ["entityType"] = "hero", ["id"] = Hero.MainHero?.Spouse?.StringId ?? string.Empty },
                new JObject { ["alias"] = "player_clan", ["entityType"] = "clan", ["id"] = Clan.PlayerClan?.StringId ?? string.Empty },
                new JObject { ["alias"] = "player_kingdom", ["entityType"] = "kingdom", ["id"] = Clan.PlayerClan?.Kingdom?.StringId ?? string.Empty },
                new JObject { ["alias"] = "speaker_clan", ["entityType"] = "clan", ["id"] = speaker?.Clan?.StringId ?? string.Empty },
                new JObject { ["alias"] = "speaker_kingdom", ["entityType"] = "kingdom", ["id"] = speaker?.Clan?.Kingdom?.StringId ?? speaker?.MapFaction?.StringId ?? string.Empty },
                new JObject { ["alias"] = "my king", ["entityType"] = "hero", ["id"] = speaker?.Clan?.Kingdom?.Leader?.StringId ?? string.Empty },
                new JObject { ["alias"] = "my queen", ["entityType"] = "hero", ["id"] = speaker?.Clan?.Kingdom?.Leader?.StringId ?? string.Empty },
                new JObject { ["alias"] = "my ruler", ["entityType"] = "hero", ["id"] = speaker?.Clan?.Kingdom?.Leader?.StringId ?? string.Empty },
                new JObject { ["alias"] = "your king", ["entityType"] = "hero", ["id"] = Clan.PlayerClan?.Kingdom?.Leader?.StringId ?? string.Empty },
                new JObject { ["alias"] = "your queen", ["entityType"] = "hero", ["id"] = Clan.PlayerClan?.Kingdom?.Leader?.StringId ?? string.Empty },
                new JObject { ["alias"] = "your ruler", ["entityType"] = "hero", ["id"] = Clan.PlayerClan?.Kingdom?.Leader?.StringId ?? string.Empty },
                new JObject { ["alias"] = "current_settlement", ["entityType"] = "settlement", ["id"] = Settlement.CurrentSettlement?.StringId ?? MobileParty.MainParty?.CurrentSettlement?.StringId ?? speaker?.CurrentSettlement?.StringId ?? string.Empty },
                new JObject { ["alias"] = "this city", ["entityType"] = "settlement", ["id"] = Settlement.CurrentSettlement?.StringId ?? MobileParty.MainParty?.CurrentSettlement?.StringId ?? speaker?.CurrentSettlement?.StringId ?? string.Empty },
                new JObject { ["alias"] = "this town", ["entityType"] = "settlement", ["id"] = Settlement.CurrentSettlement?.StringId ?? MobileParty.MainParty?.CurrentSettlement?.StringId ?? speaker?.CurrentSettlement?.StringId ?? string.Empty },
                new JObject { ["alias"] = "that city", ["entityType"] = "settlement", ["id"] = Settlement.CurrentSettlement?.StringId ?? MobileParty.MainParty?.CurrentSettlement?.StringId ?? speaker?.CurrentSettlement?.StringId ?? string.Empty },
                new JObject { ["alias"] = "that town", ["entityType"] = "settlement", ["id"] = Settlement.CurrentSettlement?.StringId ?? MobileParty.MainParty?.CurrentSettlement?.StringId ?? speaker?.CurrentSettlement?.StringId ?? string.Empty },
                new JObject { ["alias"] = "capital", ["entityType"] = "settlement", ["id"] = KingdomCapital(Clan.PlayerClan?.Kingdom)?.StringId ?? string.Empty },
                new JObject { ["alias"] = "our capital", ["entityType"] = "settlement", ["id"] = KingdomCapital(Clan.PlayerClan?.Kingdom)?.StringId ?? string.Empty },
                new JObject { ["alias"] = "their capital", ["entityType"] = "settlement", ["id"] = KingdomCapital(speaker?.Clan?.Kingdom)?.StringId ?? string.Empty },
                new JObject { ["alias"] = "player_party", ["entityType"] = "party", ["id"] = MobileParty.MainParty?.StringId ?? string.Empty },
                new JObject { ["alias"] = "speaker_party", ["entityType"] = "party", ["id"] = speaker?.PartyBelongedTo?.StringId ?? string.Empty },
                new JObject { ["alias"] = "my party", ["entityType"] = "party", ["id"] = speaker?.PartyBelongedTo?.StringId ?? string.Empty },
                new JObject { ["alias"] = "your party", ["entityType"] = "party", ["id"] = MobileParty.MainParty?.StringId ?? string.Empty }
            };

            return new JObject
            {
                ["version"] = "2",
                ["settlements"] = settlements,
                ["kingdoms"] = kingdoms,
                ["clans"] = clans,
                ["heroes"] = heroRefs,
                ["parties"] = BuildResolverParties(speaker, origin, includeWorldCatalog ? 120 : 0),
                ["armies"] = includeWorldCatalog ? BuildResolverArmies(origin) : new JArray(),
                ["workshops"] = BuildResolverWorkshops(includeWorldCatalog ? null : currentSettlement),
                ["assets"] = BuildResolverAssets(speaker),
                ["concepts"] = BuildResolverConcepts(),
                ["recentContext"] = BuildResolverRecentContext(speaker, activeHeroList),
                ["aliases"] = aliases
            };
        }

        private static bool RequiresFullActionResolutionIndex(string playerText)
        {
            string text = (playerText ?? string.Empty).Trim();
            if (text.Length == 0)
            {
                return false;
            }

            // The compact index always contains the player, speaker, active
            // participants, their families, the current settlement, relevant
            // clans/kingdoms, aliases, and participant assets. Expand to the
            // expensive all-world catalog only when the player's line could
            // reasonably request a native action against a remote entity.
            bool strongAction = Regex.IsMatch(
                text,
                @"\b(attack|assault|raid|besiege|capture|conquer|kill|execute|duel|fight|escort|patrol|recruit|hire|dismiss|transfer|give|gift|pay|buy|sell|trade|loan|subsid|tribute|reparation|marry|wed|divorce|declare|peace|war|ally|alliance|vassal|mercenar|defect|rebel|exile|restore|surrender|release|imprison|take prisoner|hand over|deliver)\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (strongAction)
            {
                return true;
            }

            return Regex.IsMatch(
                text,
                @"\b(follow|join|challenge)\s+(me|us|my|our|you|him|her|them)\b|\b(go|travel|move)\s+(to|toward|into|near)\b|\b(wait|stay)\s+(here|there|at|near|outside|inside)\b|\b(form|create)\s+(an?\s+)?(army|party|alliance|kingdom|clan)\b|\bleave\s+(my|our|the|this|your)\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        private static void AddHeroRelations(List<Hero> heroes, Hero hero)
        {
            if (hero == null)
            {
                return;
            }

            AddHeroIfMissing(heroes, hero.Spouse);
            AddHeroIfMissing(heroes, hero.Father);
            AddHeroIfMissing(heroes, hero.Mother);
            if (hero.Children != null)
            {
                foreach (Hero child in hero.Children)
                {
                    AddHeroIfMissing(heroes, child);
                }
            }

            if (hero.Siblings != null)
            {
                foreach (Hero sibling in hero.Siblings)
                {
                    AddHeroIfMissing(heroes, sibling);
                }
            }
        }

        private static void AddHeroIfMissing(List<Hero> heroes, Hero hero)
        {
            if (hero == null || string.IsNullOrWhiteSpace(hero.StringId) || heroes.Any(x => x.StringId == hero.StringId))
            {
                return;
            }

            heroes.Add(hero);
        }

        private static Settlement KingdomCapital(Kingdom kingdom)
        {
            if (kingdom == null)
            {
                return null;
            }

            return kingdom.InitialHomeSettlement
                ?? kingdom.Towns?.FirstOrDefault()?.Settlement
                ?? kingdom.Settlements?.FirstOrDefault(x => x != null && x.IsFortification)
                ?? kingdom.Settlements?.FirstOrDefault();
        }

        private static JArray BuildResolverParties(Hero speaker, Vec2 origin, int additionalPartyLimit = 120)
        {
            JArray parties = new JArray();
            HashSet<string> seen = new HashSet<string>();
            AddPartyIfMissing(parties, seen, MobileParty.MainParty, origin);
            AddPartyIfMissing(parties, seen, speaker?.PartyBelongedTo, origin);

            foreach (MobileParty party in MobileParty.All
                .Where(x => x != null && x.IsActive)
                .OrderBy(x => SafeDistanceSquared(x, origin))
                .Take(Math.Max(0, additionalPartyLimit)))
            {
                AddPartyIfMissing(parties, seen, party, origin);
            }

            return parties;
        }

        private static void AddPartyIfMissing(JArray parties, HashSet<string> seen, MobileParty party, Vec2 origin)
        {
            if (party == null || string.IsNullOrWhiteSpace(party.StringId) || seen.Contains(party.StringId))
            {
                return;
            }

            seen.Add(party.StringId);
            JObject entry = PartySummary(party, origin);
            entry["type"] = PartyType(party);
            entry["isMainParty"] = party.IsMainParty;
            entry["isCaravan"] = party.IsCaravan;
            entry["isVillager"] = party.IsVillager;
            entry["isMilitia"] = party.IsMilitia;
            entry["homeSettlementId"] = party.HomeSettlement?.StringId ?? string.Empty;
            entry["homeSettlementName"] = party.HomeSettlement?.Name?.ToString() ?? string.Empty;
            entry["currentSettlementId"] = party.CurrentSettlement?.StringId ?? string.Empty;
            entry["currentSettlementName"] = party.CurrentSettlement?.Name?.ToString() ?? string.Empty;
            entry["targetSettlementId"] = party.TargetSettlement?.StringId ?? string.Empty;
            entry["targetSettlementName"] = party.TargetSettlement?.Name?.ToString() ?? string.Empty;
            entry["attachedToPartyId"] = party.AttachedTo?.StringId ?? string.Empty;
            entry["armyLeaderPartyId"] = party.Army?.LeaderParty?.StringId ?? string.Empty;
            entry["isArmyLeader"] = party.Army?.LeaderParty == party;
            entry["defaultBehavior"] = party.DefaultBehavior.ToString();
            entry["shortTermBehavior"] = party.ShortTermBehavior.ToString();
            entry["prisonerCount"] = party.PrisonRoster?.TotalManCount ?? 0;
            parties.Add(entry);
        }

        private static string PartyType(MobileParty party)
        {
            if (party == null) return "unknown";
            if (party.IsMainParty) return "player_party";
            if (party.IsLordParty) return "lord_party";
            if (party.IsCaravan) return "caravan";
            if (party.IsVillager) return "villager";
            if (party.IsMilitia) return "militia";
            if (party.IsBandit) return "bandit";
            return "mobile_party";
        }

        private static float SafeDistanceSquared(MobileParty party, Vec2 origin)
        {
            try
            {
                return party == null ? float.MaxValue : origin.DistanceSquared(party.GetPosition2D);
            }
            catch
            {
                return float.MaxValue;
            }
        }

        private static JArray BuildResolverArmies(Vec2 origin)
        {
            JArray armies = new JArray();
            foreach (Army army in Kingdom.All.Where(x => x != null && x.Armies != null).SelectMany(x => x.Armies).Where(x => x != null))
            {
                MobileParty leader = army.LeaderParty;
                Settlement target = army.AiBehaviorObject as Settlement ?? leader?.TargetSettlement;
                armies.Add(new JObject
                {
                    ["armyId"] = "army:" + (leader?.StringId ?? army.ArmyOwner?.StringId ?? string.Empty),
                    ["name"] = army.Name?.ToString() ?? string.Empty,
                    ["leaderPartyId"] = leader?.StringId ?? string.Empty,
                    ["leaderHeroId"] = army.ArmyOwner?.StringId ?? leader?.LeaderHero?.StringId ?? string.Empty,
                    ["leaderName"] = army.ArmyOwner?.Name?.ToString() ?? leader?.LeaderHero?.Name?.ToString() ?? string.Empty,
                    ["kingdomId"] = army.Kingdom?.StringId ?? leader?.MapFaction?.StringId ?? string.Empty,
                    ["kingdomName"] = army.Kingdom?.InformalName?.ToString() ?? leader?.MapFaction?.Name?.ToString() ?? string.Empty,
                    ["type"] = army.ArmyType.ToString(),
                    ["cohesion"] = army.Cohesion,
                    ["morale"] = army.Morale,
                    ["totalManCount"] = army.TotalManCount,
                    ["estimatedStrength"] = army.EstimatedStrength,
                    ["partyCount"] = army.LeaderPartyAndAttachedPartiesCount,
                    ["targetSettlementId"] = target?.StringId ?? string.Empty,
                    ["targetSettlementName"] = target?.Name?.ToString() ?? string.Empty,
                    ["distance"] = leader == null ? 0d : Math.Sqrt(origin.DistanceSquared(leader.GetPosition2D))
                });
            }

            return armies;
        }

        private static JArray BuildResolverWorkshops(Settlement settlementFilter = null)
        {
            JArray workshops = new JArray();
            foreach (Town town in Town.AllTowns.Where(x => x != null
                && x.Workshops != null
                && (settlementFilter == null || x.Settlement == settlementFilter)))
            {
                for (int i = 0; i < town.Workshops.Length; i++)
                {
                    Workshop workshop = town.Workshops[i];
                    if (workshop == null || workshop.WorkshopType == null)
                    {
                        continue;
                    }

                    string id = ReignObjectResolver.WorkshopId(workshop, i);
                    workshops.Add(new JObject
                    {
                        ["workshopId"] = id,
                        ["name"] = workshop.Name?.ToString() ?? workshop.WorkshopType?.Name?.ToString() ?? id,
                        ["tag"] = workshop.Tag ?? string.Empty,
                        ["typeId"] = workshop.WorkshopType?.StringId ?? string.Empty,
                        ["typeName"] = workshop.WorkshopType?.Name?.ToString() ?? string.Empty,
                        ["settlementId"] = workshop.Settlement?.StringId ?? string.Empty,
                        ["settlementName"] = workshop.Settlement?.Name?.ToString() ?? string.Empty,
                        ["ownerHeroStringId"] = workshop.Owner?.StringId ?? string.Empty,
                        ["ownerName"] = workshop.Owner?.Name?.ToString() ?? string.Empty,
                        ["capital"] = workshop.Capital,
                        ["profitMade"] = workshop.ProfitMade
                    });
                }
            }

            return workshops;
        }

        private static JObject BuildResolverAssets(Hero speaker)
        {
            return new JObject
            {
                ["currency"] = new JArray
                {
                    new JObject { ["alias"] = "denars", ["kind"] = "gold" },
                    new JObject { ["alias"] = "gold", ["kind"] = "gold" },
                    new JObject { ["alias"] = "tribute", ["kind"] = "recurring_gold" },
                    new JObject { ["alias"] = "reparations", ["kind"] = "gold_or_recurring_gold" }
                },
                ["player"] = new JObject
                {
                    ["hero"] = HeroReference(Hero.MainHero),
                    ["wealth"] = BuildWealthProfile(Hero.MainHero),
                    ["inventory"] = BuildInventoryProfile(Hero.MainHero),
                    ["appearance"] = BuildAppearanceProfile(Hero.MainHero),
                    ["prisoners"] = BuildPrisonerArray(MobileParty.MainParty, 24)
                },
                ["speaker"] = new JObject
                {
                    ["hero"] = HeroReference(speaker),
                    ["wealth"] = BuildWealthProfile(speaker),
                    ["inventory"] = BuildInventoryProfile(speaker),
                    ["appearance"] = BuildAppearanceProfile(speaker),
                    ["prisoners"] = BuildPrisonerArray(speaker?.PartyBelongedTo, 24)
                }
            };
        }

        private static JArray BuildPrisonerArray(MobileParty party, int limit)
        {
            JArray prisoners = new JArray();
            if (party?.PrisonRoster == null)
            {
                return prisoners;
            }

            foreach (TroopRosterElement element in party.PrisonRoster.GetTroopRoster().Where(x => x.Character?.HeroObject != null).Take(Math.Max(1, limit)))
            {
                Hero prisoner = element.Character.HeroObject;
                prisoners.Add(new JObject
                {
                    ["heroStringId"] = prisoner.StringId ?? string.Empty,
                    ["name"] = prisoner.Name?.ToString() ?? string.Empty,
                    ["clanId"] = prisoner.Clan?.StringId ?? string.Empty,
                    ["clanName"] = prisoner.Clan?.Name?.ToString() ?? string.Empty,
                    ["kingdomId"] = prisoner.Clan?.Kingdom?.StringId ?? prisoner.MapFaction?.StringId ?? string.Empty,
                    ["kingdomName"] = prisoner.Clan?.Kingdom?.InformalName?.ToString() ?? prisoner.MapFaction?.Name?.ToString() ?? string.Empty,
                    ["count"] = element.Number
                });
            }

            return prisoners;
        }

        private static JArray BuildResolverConcepts()
        {
            return new JArray
            {
                Concept("peace", "diplomacy", "make_peace", "end war; settle hostilities; cease fighting"),
                Concept("tribute", "diplomacy", "offer_tribute_peace", "daily payment; pay for peace; buy time"),
                Concept("reparations", "diplomacy", "demand_reparations_peace", "compensation; indemnity; payment for damage"),
                Concept("settlement surrender", "diplomacy", "demand_settlement_peace", "give town; surrender city; hand over castle; cede fief; grant city; transfer settlement"),
                Concept("full surrender", "diplomacy", "demand_surrender_peace", "capitulation; submit kingdom; give all towns and castles"),
                Concept("naval assets", "regular", "trade_package", "ships; fleet; flagship; vessels; boats; naval transfer; War Sails"),
                Concept("trade agreement", "diplomacy", "sign_trade_agreement", "commerce pact; trade treaty"),
                Concept("non-aggression pact", "diplomacy", "sign_non_aggression_pact", "promise not to attack; pact of restraint"),
                Concept("alliance", "diplomacy", "sign_alliance", "ally; mutual alliance"),
                Concept("defensive pact", "diplomacy", "sign_defensive_pact", "defend each other; protection pact"),
                Concept("ultimatum", "diplomacy", "record_promise", "threat; final warning; obligation"),
                Concept("gift", "social", "record_promise", "favor; present; improve relations"),
                Concept("gold transfer", "regular", "give_gold_to_player", "give gold; pay denars; hand over money; gift coin"),
                Concept("trade package", "regular", "trade_package", "buy; sell; barter; price; denars for item; gold for prisoner; ships for gold; settlement sale"),
                Concept("item transfer", "regular", "transfer_item", "give armor; give weapon; hand over item; transfer equipment"),
                Concept("travel order", "regular", "go_to_settlement", "go to town; ride to city; travel to castle; return to settlement"),
                Concept("duel", "regular", "duel_player", "practice duel; sparring; friendly duel; training duel; lethal duel; duel of honor"),
                Concept("feast", "social", "social_event_feast", "dinner; banquet; gathering"),
                Concept("private audience", "social", "social_event_private_audience", "speak privately; closed meeting"),
                Concept("public insult", "social", "social_action_humiliate", "humiliate; shame; denounce before court")
            };
        }

        private static JObject Concept(string phrase, string family, string action, string aliases)
        {
            return new JObject
            {
                ["phrase"] = phrase,
                ["family"] = family,
                ["action"] = action,
                ["aliases"] = aliases
            };
        }

        private static JArray BuildResolverRecentContext(Hero speaker, List<Hero> activeHeroes)
        {
            JArray recent = new JArray();
            recent.Add(new JObject
            {
                ["label"] = "current_conversation_target",
                ["entityType"] = "hero",
                ["id"] = speaker?.StringId ?? string.Empty,
                ["name"] = speaker?.Name?.ToString() ?? string.Empty
            });

            Settlement settlement = Settlement.CurrentSettlement ?? MobileParty.MainParty?.CurrentSettlement ?? speaker?.CurrentSettlement;
            if (settlement != null)
            {
                recent.Add(new JObject
                {
                    ["label"] = "current_settlement",
                    ["entityType"] = "settlement",
                    ["id"] = settlement.StringId ?? string.Empty,
                    ["name"] = settlement.Name?.ToString() ?? string.Empty
                });
            }

            foreach (Hero hero in (activeHeroes ?? new List<Hero>()).Take(12))
            {
                recent.Add(new JObject
                {
                    ["label"] = "active_conversation_participant",
                    ["entityType"] = "hero",
                    ["id"] = hero.StringId ?? string.Empty,
                    ["name"] = hero.Name?.ToString() ?? string.Empty
                });
            }

            foreach (ReignDiplomaticAgreementRecord agreement in (ReignServices.AI?.Agreements ?? new List<ReignDiplomaticAgreementRecord>())
                .Where(x => x != null && x.IsActive)
                .OrderByDescending(x => x.CreatedDay)
                .Take(16))
            {
                recent.Add(new JObject
                {
                    ["label"] = "active_agreement",
                    ["entityType"] = "agreement",
                    ["id"] = agreement.AgreementId ?? string.Empty,
                    ["kind"] = agreement.Kind ?? string.Empty,
                    ["actorKingdomId"] = agreement.ActorKingdomStringId ?? string.Empty,
                    ["targetKingdomId"] = agreement.TargetKingdomStringId ?? string.Empty,
                    ["targetSettlementId"] = agreement.TargetSettlementStringId ?? string.Empty,
                    ["targetHeroId"] = agreement.TargetHeroStringId ?? string.Empty,
                    ["reason"] = agreement.Reason ?? string.Empty
                });
            }

            return recent;
        }
    }
}
