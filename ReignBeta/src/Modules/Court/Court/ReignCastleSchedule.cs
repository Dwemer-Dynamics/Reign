using System;
using System.Collections.Generic;
using System.Linq;
using ReignBeta.CastleChat;
using ReignBeta.Runtime;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.Core;

namespace ReignBeta.Court
{
    public sealed partial class ReignCourtCampaignBehavior
    {
        private static readonly HashSet<string> NativeCastleCultures = new HashSet<string>(
            new[] { "aserai", "battania", "empire", "khuzait", "nord", "sturgia", "vlandia" },
            StringComparer.OrdinalIgnoreCase);

        public IReadOnlyList<CastleRoomSessionRecord> EnsureCastleSchedule()
        {
            Settlement settlement = Settlement.CurrentSettlement ?? MobileParty.MainParty?.CurrentSettlement;
            if (settlement == null) return Array.Empty<CastleRoomSessionRecord>();
            int day = (int)Math.Floor(CampaignTime.Now.ToDays);
            CastleTimeBlock block = CastleScheduleEngine.GetTimeBlock(CampaignTime.Now.ToHours % 24d);
            string timeline = _session?.TimelineId ?? string.Empty;
            string campaign = _session?.CampaignId ?? string.Empty;

            List<CastleRoomSessionRecord> existing = _castleRoomSessions.Where(x =>
                x != null && x.CampaignDay == day && x.TimeBlock == (int)block &&
                string.Equals(x.TimelineId, timeline, StringComparison.Ordinal) &&
                string.Equals(x.SettlementStringId, settlement.StringId, StringComparison.OrdinalIgnoreCase) &&
                string.IsNullOrWhiteSpace(x.PrivateGuestHeroStringId)).ToList();
            if (existing.Count == 15)
            {
                RemoveUnavailableCastleOccupants(existing, settlement);
                ReconcileGovernmentCastleAttendees(existing, settlement);
                ReconcileNobleVisitorCastleAttendees(existing, settlement);
                return existing;
            }

            List<Hero> heroes = CollectCastleCandidates(settlement);
            List<CastleScheduleCandidate> candidates = heroes.Select(ToScheduleCandidate).ToList();
            CastleScheduleResult schedule = CastleScheduleEngine.Build(
                candidates,
                _castleBathHistory.Select(x => new CastleBathHistory
                {
                    HeroId = x.HeroStringId, SelectionCount = x.SelectionCount,
                    LastDay = x.LastSelectedDay, LastBlock = x.LastSelectedBlock
                }),
                campaign + "|" + timeline + "|" + settlement.StringId,
                day, block);

            string culture = NormalizeCastleCulture(settlement.Culture?.StringId);
            foreach (CastleRoom room in Enum.GetValues(typeof(CastleRoom)))
            {
                var record = new CastleRoomSessionRecord
                {
                    SessionKey = BuildCastleSessionKey(campaign, timeline, settlement.StringId, day, block, room, string.Empty),
                    CampaignId = campaign, TimelineId = timeline, SettlementStringId = settlement.StringId,
                    CultureId = culture, CampaignDay = day, TimeBlock = (int)block, Room = (int)room,
                    OccupantHeroIdsCsv = string.Join(",", schedule.Rooms[room]),
                    ServerConversationSessionId = "castle_" + CastleScheduleEngine.StableHash(
                        campaign + timeline, day, block, settlement.StringId, room.ToString()).ToString("x8")
                };
                _castleRoomSessions.Add(record);
                existing.Add(record);
            }
            RecordBathRotation(schedule.BathSelections, day, block);
            ReconcileGovernmentCastleAttendees(existing, settlement);
            ReconcileNobleVisitorCastleAttendees(existing, settlement);
            CompactCastleSessions(day);
            return existing;
        }

        public CastleRoomSessionRecord GetCastleRoomSession(CastleRoom room, string privateGuestHeroId = "")
        {
            List<CastleRoomSessionRecord> baseSchedule = EnsureCastleSchedule().ToList();
            CastleRoomSessionRecord parent = baseSchedule.FirstOrDefault(x => x.Room == (int)room);
            if (parent == null || room != CastleRoom.GuestBedrooms || string.IsNullOrWhiteSpace(privateGuestHeroId)) return parent;
            CastleRoomSessionRecord existing = _castleRoomSessions.FirstOrDefault(x =>
                x.CampaignDay == parent.CampaignDay && x.TimeBlock == parent.TimeBlock && x.Room == parent.Room &&
                string.Equals(x.TimelineId, parent.TimelineId, StringComparison.Ordinal) &&
                string.Equals(x.SettlementStringId, parent.SettlementStringId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(x.PrivateGuestHeroStringId, privateGuestHeroId, StringComparison.OrdinalIgnoreCase));
            if (existing != null) return existing;
            var created = new CastleRoomSessionRecord
            {
                CampaignId = parent.CampaignId, TimelineId = parent.TimelineId,
                SettlementStringId = parent.SettlementStringId, CultureId = parent.CultureId,
                CampaignDay = parent.CampaignDay, TimeBlock = parent.TimeBlock, Room = parent.Room,
                PrivateGuestHeroStringId = privateGuestHeroId, OccupantHeroIdsCsv = privateGuestHeroId,
                SessionKey = BuildCastleSessionKey(parent.CampaignId, parent.TimelineId, parent.SettlementStringId,
                    parent.CampaignDay, (CastleTimeBlock)parent.TimeBlock, room, privateGuestHeroId),
                ServerConversationSessionId = parent.ServerConversationSessionId + "_guest_" + privateGuestHeroId
            };
            created.ImagePromptSnapshot = parent.ImagePromptSnapshot;
            created.DialoguePromptSnapshot = parent.DialoguePromptSnapshot;
            created.PromptRevision = parent.PromptRevision;
            _castleRoomSessions.Add(created);
            return created;
        }

        internal static string CastleRoomPromptKey(CastleRoom room)
        {
            switch (room)
            {
                case CastleRoom.CastleGardens: return "castle_gardens";
                case CastleRoom.NobleSolar: return "noble_solar";
                case CastleRoom.TrainingYard: return "training_yard";
                case CastleRoom.InnerCourtyard: return "inner_courtyard";
                case CastleRoom.DiningChamber: return "small_dining_chamber";
                case CastleRoom.StableCourtyard: return "stable_courtyard";
                case CastleRoom.PortraitGallery: return "portrait_gallery";
                case CastleRoom.MainHall: return "main_hall";
                case CastleRoom.ThroneRoom: return "throne_room";
                case CastleRoom.GuestBedrooms: return "guest_bedrooms";
                case CastleRoom.RoyalBedroom: return "royal_bedroom";
                case CastleRoom.Battlements: return "battlements";
                case CastleRoom.Baths: return "baths";
                case CastleRoom.Chapel: return "chapel";
                default: return "library";
            }
        }

        public static string NormalizeCastleCulture(string cultureId)
        {
            string value = (cultureId ?? string.Empty).Trim().ToLowerInvariant();
            return NativeCastleCultures.Contains(value) ? value : "generic";
        }

        private List<Hero> CollectCastleCandidates(Settlement settlement)
        {
            var result = new Dictionary<string, Hero>(StringComparer.OrdinalIgnoreCase);
            foreach (Hero hero in ReignPartyChatSession.GetMainPartyHeroes()) AddCastleCandidate(result, hero, settlement, true);
            foreach (Hero hero in ReignPartyChatSession.GetAvailableConversationHeroes().Where(x => x?.IsLord == true))
                AddCastleCandidate(result, hero, settlement, false);
            foreach (Hero hero in ReignBeta.Government.ReignGovernmentCampaignBehavior.Instance?.GetAttendingGovernmentHeroes(settlement) ?? Array.Empty<Hero>())
                AddCastleCandidate(result, hero, settlement, false);
            foreach (ForeignAmbassadorPosting posting in _foreignAmbassadors.Where(x => x != null && x.IsResident &&
                string.Equals(x.CapitalSettlementStringId, settlement.StringId, StringComparison.OrdinalIgnoreCase)))
            {
                Hero hero = Hero.FindFirst(x => string.Equals(x.StringId, posting.HeroStringId, StringComparison.OrdinalIgnoreCase));
                AddCastleCandidate(result, hero, settlement, false);
            }
            return result.Values.OrderBy(x => x.StringId, StringComparer.Ordinal).ToList();
        }

        private static void AddCastleCandidate(IDictionary<string, Hero> result, Hero hero, Settlement settlement, bool fromParty)
        {
            if (hero == null || hero == Hero.MainHero || !hero.IsAlive || !hero.IsActive || hero.IsDisabled || hero.IsPrisoner) return;
            bool partyMember = fromParty || hero.PartyBelongedTo == MobileParty.MainParty;
            bool present = partyMember || (hero.CurrentSettlement == settlement &&
                (hero.PartyBelongedTo == null || hero.PartyBelongedTo.CurrentSettlement == settlement));
            if (present) result[hero.StringId] = hero;
        }

        private CastleScheduleCandidate ToScheduleCandidate(Hero hero)
        {
            return new CastleScheduleCandidate
            {
                HeroId = hero.StringId, IsFemale = hero.IsFemale, IsLord = hero.IsLord,
                IsPartyMember = hero.PartyBelongedTo == MobileParty.MainParty,
                IsAmbassador = _foreignAmbassadors.Any(x => x != null && x.IsResident && x.HeroStringId == hero.StringId),
                IsOfficeHolder = _offices.Any(x => x != null && x.IsActive && x.HeroStringId == hero.StringId),
                IsClanLeader = hero.Clan?.Leader == hero, IsSpouse = Hero.MainHero?.Spouse == hero,
                IsWounded = hero.IsWounded,
                HonorPercent = TraitPercent(hero.GetTraitLevel(DefaultTraits.Honor)),
                BoldnessPercent = TraitPercent(hero.GetTraitLevel(DefaultTraits.Valor)),
                GenerosityPercent = TraitPercent(hero.GetTraitLevel(DefaultTraits.Generosity)),
                MercyPercent = TraitPercent(hero.GetTraitLevel(DefaultTraits.Mercy)),
                Charm = hero.GetSkillValue(DefaultSkills.Charm), Steward = hero.GetSkillValue(DefaultSkills.Steward),
                Medicine = hero.GetSkillValue(DefaultSkills.Medicine), Engineering = hero.GetSkillValue(DefaultSkills.Engineering),
                Tactics = hero.GetSkillValue(DefaultSkills.Tactics), Athletics = hero.GetSkillValue(DefaultSkills.Athletics),
                Leadership = hero.GetSkillValue(DefaultSkills.Leadership), Trade = hero.GetSkillValue(DefaultSkills.Trade),
                Riding = hero.GetSkillValue(DefaultSkills.Riding), Scouting = hero.GetSkillValue(DefaultSkills.Scouting),
                Roguery = hero.GetSkillValue(DefaultSkills.Roguery)
            };
        }

        private static int TraitPercent(int nativeLevel) => Math.Max(0, Math.Min(100, (nativeLevel + 2) * 25));

        private static void ReconcileGovernmentCastleAttendees(IReadOnlyList<CastleRoomSessionRecord> sessions, Settlement settlement)
        {
            // Add newly arrived representatives to the existing hall without rerolling everyone else's
            // schedule, bath rotation, session identity, or conversations in the current time block.
            var attendees = ReignBeta.Government.ReignGovernmentCampaignBehavior.Instance?.GetAttendingGovernmentHeroes(settlement);
            var hall = sessions.FirstOrDefault(x => x.Room == (int)CastleRoom.MainHall);
            if (hall == null || attendees == null || attendees.Count == 0) return;
            var present = new HashSet<string>(sessions.SelectMany(x => SplitIds(x.OccupantHeroIdsCsv)), StringComparer.OrdinalIgnoreCase);
            var deliberatelyRemoved = new HashSet<string>(sessions.SelectMany(x => SplitIds(x.RemovedHeroIdsCsv)), StringComparer.OrdinalIgnoreCase);
            var added = attendees.Select(x => x.StringId).Where(x => !present.Contains(x) && !deliberatelyRemoved.Contains(x)).ToList();
            if (added.Count == 0) return;
            hall.OccupantHeroIdsCsv = string.Join(",", SplitIds(hall.OccupantHeroIdsCsv).Concat(added));
            hall.ImageStatus = "invalidated"; hall.ImageCacheKey = string.Empty;
            hall.ImageLocalPath = string.Empty; hall.Revision++;
        }

        private void ReconcileNobleVisitorCastleAttendees(IReadOnlyList<CastleRoomSessionRecord> sessions, Settlement settlement)
        {
            CastleRoomSessionRecord guestBedrooms = sessions.FirstOrDefault(x => x.Room == (int)CastleRoom.GuestBedrooms);
            if (guestBedrooms == null || settlement == null) return;

            List<string> visitors = (EnsureRulerDocketState().NobleVisitorStays ?? new List<ReignNobleVisitorStay>())
                .Where(stay => stay != null && stay.Arrived && !stay.Departed
                    && string.Equals(stay.HostSettlementId, settlement.StringId, StringComparison.OrdinalIgnoreCase))
                .SelectMany(stay => stay.Members ?? new List<ReignNobleVisitorMember>())
                .Where(member => member != null && member.Arrived && !member.Released)
                .Select(member => member.HeroId)
                .Where(id =>
                {
                    Hero hero = Hero.FindFirst(x => string.Equals(x.StringId, id, StringComparison.OrdinalIgnoreCase));
                    return hero != null && hero.IsAlive && hero.IsActive && !hero.IsDisabled && !hero.IsPrisoner
                        && hero.CurrentSettlement == settlement;
                })
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (visitors.Count == 0) return;

            var visitorIds = new HashSet<string>(visitors, StringComparer.OrdinalIgnoreCase);
            foreach (CastleRoomSessionRecord session in sessions)
            {
                List<string> original = SplitIds(session.OccupantHeroIdsCsv);
                List<string> updated = session == guestBedrooms
                    ? visitors.Concat(original.Where(id => !visitorIds.Contains(id)))
                        .Distinct(StringComparer.OrdinalIgnoreCase).Take(CastleScheduleEngine.Capacity(CastleRoom.GuestBedrooms)).ToList()
                    : original.Where(id => !visitorIds.Contains(id)).ToList();
                if (original.SequenceEqual(updated, StringComparer.OrdinalIgnoreCase)) continue;
                session.OccupantHeroIdsCsv = string.Join(",", updated);
                session.ImageStatus = "invalidated"; session.ImageCacheKey = string.Empty;
                session.ImageLocalPath = string.Empty; session.Revision++;
            }
        }

        private void RecordBathRotation(IEnumerable<string> selected, int day, CastleTimeBlock block)
        {
            foreach (string id in selected)
            {
                CastleBathHistoryRecord row = _castleBathHistory.FirstOrDefault(x =>
                    string.Equals(x.HeroStringId, id, StringComparison.OrdinalIgnoreCase));
                if (row == null) { row = new CastleBathHistoryRecord { HeroStringId = id }; _castleBathHistory.Add(row); }
                row.SelectionCount++; row.LastSelectedDay = day; row.LastSelectedBlock = (int)block;
            }
        }

        private static string BuildCastleSessionKey(string campaign, string timeline, string settlement, int day,
            CastleTimeBlock block, CastleRoom room, string guest)
        {
            return string.Join("|", new[] { campaign, timeline, settlement, day.ToString(), ((int)block).ToString(),
                ((int)room).ToString(), guest ?? string.Empty });
        }

        private void RemoveUnavailableCastleOccupants(IEnumerable<CastleRoomSessionRecord> sessions, Settlement settlement)
        {
            foreach (CastleRoomSessionRecord session in sessions)
            {
                List<string> original = SplitIds(session.OccupantHeroIdsCsv);
                List<string> valid = original.Where(id =>
                {
                    Hero hero = Hero.FindFirst(x => string.Equals(x.StringId, id, StringComparison.OrdinalIgnoreCase));
                    return hero != null && hero.IsAlive && hero.IsActive && !hero.IsDisabled && !hero.IsPrisoner &&
                        (hero.PartyBelongedTo == MobileParty.MainParty || hero.CurrentSettlement == settlement);
                }).ToList();
                if (valid.Count == original.Count) continue;
                List<string> removed = original.Except(valid, StringComparer.OrdinalIgnoreCase).ToList();
                session.OccupantHeroIdsCsv = string.Join(",", valid);
                session.RemovedHeroIdsCsv = string.Join(",", SplitIds(session.RemovedHeroIdsCsv)
                    .Concat(removed).Distinct(StringComparer.OrdinalIgnoreCase));
                session.ImageStatus = "invalidated"; session.ImageCacheKey = string.Empty;
                session.ImageLocalPath = string.Empty; session.Revision++;
            }
        }

        private void CompactCastleSessions(int currentDay)
        {
            _castleRoomSessions.RemoveAll(x => x == null || x.CampaignDay < currentDay - 30);
        }

        internal static List<string> SplitIds(string csv) => (csv ?? string.Empty).Split(',')
            .Select(x => x.Trim()).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }
}
