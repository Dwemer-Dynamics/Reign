using System;
using System.Collections.Generic;
using System.Linq;
using Reign.Core.Contracts.Government;
using ReignBeta.Integration;
#if !REIGN_EXCLUDE_COURT
using ReignBeta.Court;
using ReignBeta.UI;
using TaleWorlds.CampaignSystem.GameMenus;
#endif
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace ReignBeta.Government
{
    public sealed partial class ReignGovernmentCampaignBehavior : CampaignBehaviorBase
    {
        private const int RetainedResolvedResolutions = 160;
        private const int RetainedMeetings = 64;
        private const int RetainedPressures = 128;
        private const int RetainedLobbyRecords = 128;

        private List<ReignGovernmentStateRecord> _governments = new List<ReignGovernmentStateRecord>();
        private List<ReignGovernmentPartyRecord> _parties = new List<ReignGovernmentPartyRecord>();
        private List<ReignGovernmentSeatRecord> _seats = new List<ReignGovernmentSeatRecord>();
        private List<ReignGovernmentResolutionRecord> _resolutions = new List<ReignGovernmentResolutionRecord>();
        private List<ReignGovernmentPressureRecord> _pressures = new List<ReignGovernmentPressureRecord>();
        private List<ReignGovernmentLobbyRecord> _lobbyRecords = new List<ReignGovernmentLobbyRecord>();
        private List<ReignGovernmentMeetingRecord> _meetings = new List<ReignGovernmentMeetingRecord>();
        private int _lastDailyTick = -1;

        public static ReignGovernmentCampaignBehavior Instance { get; private set; }
        public IReadOnlyList<ReignGovernmentStateRecord> Governments => _governments;
        public IReadOnlyList<ReignGovernmentPartyRecord> Parties => _parties;
        public IReadOnlyList<ReignGovernmentSeatRecord> Seats => _seats;
        public IReadOnlyList<ReignGovernmentResolutionRecord> Resolutions => _resolutions;
        public IReadOnlyList<ReignGovernmentPressureRecord> Pressures => _pressures;
        public IReadOnlyList<ReignGovernmentLobbyRecord> LobbyRecords => _lobbyRecords;
        public IReadOnlyList<ReignGovernmentMeetingRecord> Meetings => _meetings;

        public ReignGovernmentCampaignBehavior()
        {
            Instance = this;
        }

        public override void RegisterEvents()
        {
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
            CampaignEvents.KingdomDecisionAdded.AddNonSerializedListener(this, OnNativeKingdomDecisionAdded);
            CampaignEvents.KingdomDecisionConcluded.AddNonSerializedListener(this, OnNativeKingdomDecisionConcluded);
            RegisterResolutionProgressEvents();
            RegisterGovernmentAttendanceEvents();
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnGovernmentSessionLaunched);
        }

        private void OnGovernmentSessionLaunched(CampaignGameStarter starter)
        {
            PruneIneligibleGovernments();
            MigrateNativeGovernmentBusiness();
            ImportSeasonalBusiness();
#if !REIGN_EXCLUDE_COURT
            starter.AddGameMenuOption("town_keep", "reign_explore_town_keep", "{=!}Explore the keep",
                ExploreKeepCondition, ExploreKeepConsequence, false, 1);
#endif
        }

#if !REIGN_EXCLUDE_COURT
        private static bool ExploreKeepCondition(MenuCallbackArgs args)
        {
            bool available = Settlement.CurrentSettlement?.IsTown == true
                && TaleWorlds.CampaignSystem.Campaign.Current?.GetCampaignBehavior<ReignCourtCampaignBehavior>() != null;
            args.optionLeaveType = GameMenuOption.LeaveType.Wait;
            args.IsEnabled = available;
            return available;
        }

        private static void ExploreKeepConsequence(MenuCallbackArgs args)
        {
            ReignCourtCampaignBehavior court = TaleWorlds.CampaignSystem.Campaign.Current
                ?.GetCampaignBehavior<ReignCourtCampaignBehavior>();
            if (court == null) return;
            args.MapState?.ExitMenuMode();
            ReignCastleLayoutScreenManager.OpenFromKeep(court);
        }
#endif

        public override void SyncData(IDataStore dataStore)
        {
            SyncGovernmentBusiness(dataStore);
            SyncGovernmentHearingData(dataStore);
            dataStore.SyncData("_reignGovernment_governments", ref _governments);
            dataStore.SyncData("_reignGovernment_parties", ref _parties);
            dataStore.SyncData("_reignGovernment_seats", ref _seats);
            dataStore.SyncData("_reignGovernment_resolutions", ref _resolutions);
            dataStore.SyncData("_reignGovernment_pressures", ref _pressures);
            dataStore.SyncData("_reignGovernment_lobbyRecords", ref _lobbyRecords);
            dataStore.SyncData("_reignGovernment_meetings", ref _meetings);
            dataStore.SyncData("_reignGovernment_lastDailyTick", ref _lastDailyTick);
            _governments = Clean(_governments, x => !string.IsNullOrWhiteSpace(x.KingdomStringId));
            _parties = Clean(_parties, x => !string.IsNullOrWhiteSpace(x.KingdomStringId) && !string.IsNullOrWhiteSpace(x.PartyId));
            _seats = Clean(_seats, x => !string.IsNullOrWhiteSpace(x.KingdomStringId) && !string.IsNullOrWhiteSpace(x.HeroStringId));
            _resolutions = Clean(_resolutions, x => !string.IsNullOrWhiteSpace(x.ResolutionId));
            _pressures = Clean(_pressures, x => !string.IsNullOrWhiteSpace(x.PressureId));
            _lobbyRecords = Clean(_lobbyRecords, x => !string.IsNullOrWhiteSpace(x.LobbyId));
            _meetings = Clean(_meetings, x => !string.IsNullOrWhiteSpace(x.MeetingId));
            if (dataStore.IsSaving || dataStore.IsLoading) PruneHistory();
        }

        public ReignGovernmentStateRecord GetGovernment(Kingdom kingdom)
        {
            return kingdom == null ? null : GetGovernment(kingdom.StringId);
        }

        public ReignGovernmentStateRecord GetGovernment(string kingdomStringId)
        {
            return _governments.FirstOrDefault(x => Same(x.KingdomStringId, kingdomStringId));
        }

        public IReadOnlyList<ReignGovernmentPartyRecord> GetParties(string kingdomStringId)
        {
            return _parties.Where(x => Same(x.KingdomStringId, kingdomStringId))
                .OrderByDescending(x => x.SeatCount).ThenBy(x => x.PartyId, StringComparer.Ordinal).ToList();
        }

        public IReadOnlyList<ReignGovernmentSeatRecord> GetSeats(string kingdomStringId)
        {
            return _seats.Where(x => Same(x.KingdomStringId, kingdomStringId))
                .OrderBy(x => x.PartyId, StringComparer.Ordinal).ThenBy(x => x.HeroStringId, StringComparer.Ordinal).ToList();
        }

        public IReadOnlyList<ReignGovernmentPartyRecord> GetGoverningCoalition(string kingdomStringId)
        {
            ReignGovernmentStateRecord state = GetGovernment(kingdomStringId);
            HashSet<string> ids = ParseIds(state?.CoalitionPartyIdsCsv);
            List<ReignGovernmentPartyRecord> result = _parties.Where(x => Same(x.KingdomStringId, kingdomStringId)
                    && (x.IsGoverningCoalition || ids.Contains(x.PartyId)))
                .OrderByDescending(x => x.SeatCount).ThenBy(x => x.PartyId, StringComparer.Ordinal).ToList();
            if (result.Count == 0)
            {
                ReignGovernmentPartyRecord dominant = FindParty(kingdomStringId, state?.DominantPartyId);
                if (dominant != null) result.Add(dominant);
            }
            return result;
        }

        public IReadOnlyList<ReignGovernmentResolutionRecord> GetActiveResolutions(string kingdomStringId)
        {
            return _resolutions.Where(x => Same(x.KingdomStringId, kingdomStringId)
                    && (Same(x.Status, "debate") || Same(x.Status, "proposed") || Same(x.Status, "active")))
                .OrderBy(x => x.DueDay < 0f ? float.MaxValue : x.DueDay).ToList();
        }

        public ReignGovernmentStateRecord EnsureGovernment(Kingdom kingdom)
        {
            if (!IsEligibleKingdom(kingdom)) return null;
            ReignGovernmentStateRecord state = GetGovernment(kingdom.StringId);
            if (state == null)
            {
                ReignGovernmentInstitutionProfile profile = ReignGovernmentRules.InstitutionForCulture(kingdom.Culture?.StringId);
                float day = CurrentDay();
                state = new ReignGovernmentStateRecord
                {
                    KingdomStringId = kingdom.StringId,
                    CultureStringId = kingdom.Culture?.StringId ?? string.Empty,
                    InstitutionName = profile.Name,
                    InstitutionKindValue = (int)profile.Kind,
                    Level = ReignGovernmentRules.SelectStartingLevel(
                        ReignCampaignIdentity.CurrentCampaignId() + "|" + kingdom.StringId),
                    Trust = 50,
                    InitializedDay = day,
                    LastMeetingDay = -1f,
                    NextMeetingDay = day + 1f + ReignGovernmentRules.StableHash("meeting|" + kingdom.StringId) % ReignGovernmentRules.SeasonalMeetingDays,
                    RulerHeroStringId = kingdom.Leader?.StringId ?? string.Empty,
                    InitialBenefitsSuppressed = true,
                    Revision = 1
                };
                _governments.Add(state);
                ReconcileMembership(kingdom, state, true);
            }
            else
            {
                bool changed = false;
                if (!Same(state.RulerHeroStringId, kingdom.Leader?.StringId))
                {
                    state.RulerHeroStringId = kingdom.Leader?.StringId ?? string.Empty;
                    changed = true;
                }
                if (!Same(state.CultureStringId, kingdom.Culture?.StringId))
                {
                    ReignGovernmentInstitutionProfile profile = ReignGovernmentRules.InstitutionForCulture(kingdom.Culture?.StringId);
                    state.CultureStringId = kingdom.Culture?.StringId ?? string.Empty;
                    state.InstitutionName = profile.Name;
                    state.InstitutionKindValue = (int)profile.Kind;
                    changed = true;
                }
                state.Level = Clamp(state.Level, ReignGovernmentRules.MinimumLevel, ReignGovernmentRules.MaximumLevel);
                if (changed) state.Revision++;
            }
            return state;
        }

        public bool IncreaseGovernmentLevel(Kingdom kingdom, out string result)
        {
            result = string.Empty;
            ReignGovernmentStateRecord state = EnsureGovernment(kingdom);
            if (state == null)
            {
                result = "That kingdom has no eligible government.";
                return false;
            }
            if (state.Level >= ReignGovernmentRules.MaximumLevel)
            {
                result = state.InstitutionName + " already holds as much governing authority as it can.";
                return false;
            }

            int priorLevel = state.Level;
            ReignGovernmentAuthorityTransition transition = ReignGovernmentRules.IncreaseTransition(priorLevel);
            state.Level++;
            ClearReductionConsents(state);
            state.TemporaryBenefitEndDay = CurrentDay() + transition.DurationDays;
            state.TemporaryLoyaltyPerDay = (float)transition.TemporaryLoyaltyPerDay;
            state.TemporaryProsperityPerDay = (float)transition.TemporaryProsperityPerDay;
            state.TemporaryHearthPerDay = (float)transition.TemporaryHearthPerDay;
            state.InitialBenefitsSuppressed = false;
            ApplySettlementChange(kingdom, transition.Loyalty, transition.Prosperity, transition.Hearth);
            foreach (ReignGovernmentSeatRecord seat in GetSeats(kingdom.StringId))
            {
                Hero member = FindHero(seat.HeroStringId);
                ReignGovernmentPartyRecord party = FindParty(kingdom.StringId, seat.PartyId);
                if (member == null || party == null) continue;
                bool royalist = ParsePlanks(party.PlanksCsv).Contains(ReignGovernmentPlank.RoyalAuthority);
                ApplyRelation(kingdom.Leader, member, royalist ? transition.RoyalistRelation : transition.SupportiveRelation);
                seat.GovernmentLoyalty = Clamp(seat.GovernmentLoyalty + (royalist ? transition.RoyalistRelation : transition.SupportiveRelation), 0, 100);
                seat.Revision++;
            }
            RecalculatePartyState(kingdom, state);
            state.Revision++;
            result = state.InstitutionName + " was granted a greater share of governing control. "
                + "The realm received the promised immediate and 30-day public benefits.";
            return true;
        }

        public bool ReduceGovernmentLevel(Kingdom kingdom, bool force, out string result)
        {
            result = string.Empty;
            ReignGovernmentStateRecord state = EnsureGovernment(kingdom);
            if (state == null)
            {
                result = "That kingdom has no eligible government.";
                return false;
            }
            if (state.Level <= ReignGovernmentRules.MinimumLevel)
            {
                result = state.InstitutionName + " already holds only an advisory role.";
                return false;
            }

            List<ReignGovernmentSeatRecord> voters = GetSeats(kingdom.StringId).ToList();
            int votesFor = 0;
            foreach (ReignGovernmentSeatRecord seat in voters)
            {
                ReignGovernmentVoteResult vote = EvaluateReductionVote(kingdom, state, seat);
                seat.LastReductionVoteScore = vote.Score;
                seat.LastReductionVoteSupported = vote.Supports;
                seat.LastVoteDay = CurrentDay();
                seat.Revision++;
                if (vote.Supports) votesFor++;
            }
            bool ratified = ReignGovernmentRules.ReductionRatified(votesFor, voters.Count);
            if (!ratified && !force)
            {
                result = state.InstitutionName + " rejected the reduction " + votesFor + " to " + voters.Count + ".";
                return false;
            }

            int priorLevel = state.Level;
            ReignGovernmentReductionPenalty penalty = ratified
                ? ReignGovernmentRules.ApprovedReductionPenalty(priorLevel)
                : ReignGovernmentRules.ForcedReductionPenalty(priorLevel);
            state.Level--;
            state.TemporaryBenefitEndDay = -1f;
            state.TemporaryLoyaltyPerDay = 0f;
            state.TemporaryProsperityPerDay = 0f;
            state.TemporaryHearthPerDay = 0f;
            ApplySettlementChange(kingdom, penalty.SettlementLoyalty, 0, 0);

            if (ratified)
            {
                foreach (ReignGovernmentSeatRecord dissenter in voters.Where(x => !x.LastReductionVoteSupported))
                {
                    Hero member = FindHero(dissenter.HeroStringId);
                    if (IsReductionPenaltyExempt(state, member, priorLevel)) continue;
                    ApplyRelation(kingdom.Leader, member, penalty.NonLandholdingDissenterRelation);
                    dissenter.GovernmentLoyalty = Clamp(dissenter.GovernmentLoyalty + penalty.NonLandholdingDissenterRelation, 0, 100);
                    dissenter.Revision++;
                }
            }
            else
            {
                ApplyForcedReductionRelations(kingdom, state, priorLevel, voters, penalty);
            }

            ClearReductionConsents(state);
            RecalculatePartyState(kingdom, state);
            state.Revision++;
            result = ratified
                ? state.InstitutionName + " ratified the reduction " + votesFor + " to " + voters.Count
                    + "; its governing control was reduced by a small amount with only the fixed consented penalties."
                : "The ruler forced a small reduction in " + state.InstitutionName
                    + "'s governing control. Fixed realm loyalty and relationship penalties were applied without changing Public Standing or rebellion probabilities.";
            return true;
        }

        public bool TryRecordReductionConsent(Hero ruler, Hero member, out string result)
        {
            result = string.Empty;
            Kingdom kingdom = ruler?.Clan?.Kingdom;
            ReignGovernmentStateRecord state = GetGovernment(kingdom);
            if (ruler == null || member == null || kingdom == null || state == null
                || kingdom.Leader != ruler || ruler != Hero.MainHero)
            {
                result = "Only the player ruler can secure consent for their realm's government reduction.";
                return false;
            }
            bool belongsToRealm = member?.Clan?.Kingdom == kingdom
                || GetSeats(kingdom.StringId).Any(x => Same(x.HeroStringId, member?.StringId));
            if (!IsEligibleMember(member) || member == ruler || !belongsToRealm)
            {
                result = "The consenting NPC must be an eligible member of the ruler's kingdom.";
                return false;
            }
            if (state.Level <= ReignGovernmentRules.MinimumLevel)
            {
                result = state.InstitutionName + " already holds only an advisory role.";
                return false;
            }

            EnsureReductionConsentLevel(state);
            bool clanWide = member.Clan?.Leader == member;
            if (clanWide)
            {
                HashSet<string> clans = ParseIds(state.ReductionConsentClanIdsCsv);
                clans.Add(member.Clan.StringId);
                state.ReductionConsentClanIdsCsv = JoinIds(clans);
            }
            else
            {
                HashSet<string> heroes = ParseIds(state.ReductionConsentHeroIdsCsv);
                heroes.Add(member.StringId);
                state.ReductionConsentHeroIdsCsv = JoinIds(heroes);
            }
            state.Revision++;
            result = clanWide
                ? member.Name + " consented for all of " + member.Clan.Name
                    + " to a small reduction in government control."
                : member.Name + " consented personally to a small reduction in government control.";
            return true;
        }

        internal float GetLoyaltyDelta(Town town)
        {
            ReignGovernmentStateRecord state = GetGovernment(town?.OwnerClan?.Kingdom);
            if (state == null) return 0f;
            ReignGovernmentDailyEffects effects = ReignGovernmentRules.DailyEffects(state.Level);
            return (float)effects.Loyalty + ActiveTemporary(state, state.TemporaryLoyaltyPerDay);
        }

        internal float GetProsperityDelta(Town town)
        {
            ReignGovernmentStateRecord state = GetGovernment(town?.OwnerClan?.Kingdom);
            if (state == null) return 0f;
            ReignGovernmentDailyEffects effects = ReignGovernmentRules.DailyEffects(state.Level);
            return (float)effects.Prosperity + ActiveTemporary(state, state.TemporaryProsperityPerDay);
        }

        internal float GetHearthDelta(Village village)
        {
            ReignGovernmentStateRecord state = GetGovernment(village?.Settlement?.MapFaction as Kingdom);
            if (state == null) return 0f;
            ReignGovernmentDailyEffects effects = ReignGovernmentRules.DailyEffects(state.Level);
            return (float)effects.Hearth + ActiveTemporary(state, state.TemporaryHearthPerDay);
        }

        private void OnDailyTick()
        {
            ImportSeasonalBusiness();
            TickGovernmentBusiness();
            int day = (int)Math.Floor(CurrentDay());
            if (_lastDailyTick >= day) return;
            _lastDailyTick = day;
            PruneIneligibleGovernments();
            foreach (Kingdom kingdom in Kingdom.All.Where(IsEligibleKingdom).OrderBy(x => x.StringId, StringComparer.Ordinal).ToList())
            {
                ReignGovernmentStateRecord state = EnsureGovernment(kingdom);
                if (state == null) continue;
                if (state.TemporaryBenefitEndDay >= 0f && CurrentDay() >= state.TemporaryBenefitEndDay)
                {
                    state.TemporaryBenefitEndDay = -1f;
                    state.TemporaryLoyaltyPerDay = 0f;
                    state.TemporaryProsperityPerDay = 0f;
                    state.TemporaryHearthPerDay = 0f;
                    state.Revision++;
                }
                if (day % 7 == 0) ReconcileMembership(kingdom, state, false);
                EvaluateResolutions(kingdom, state);
                EvaluateLobbyRecords(kingdom, state);
                if (state.NextMeetingDay < 0f || CurrentDay() >= state.NextMeetingDay)
                    RunSeasonalMeeting(kingdom, state, false);
            }
            PruneHistory();
        }

        private void PruneIneligibleGovernments()
        {
            HashSet<string> eligibleKingdomIds = new HashSet<string>(Kingdom.All
                .Where(IsEligibleKingdom).Select(x => x.StringId), StringComparer.OrdinalIgnoreCase);
            HashSet<string> removedKingdomIds = new HashSet<string>(_governments
                .Where(x => !eligibleKingdomIds.Contains(x.KingdomStringId))
                .Select(x => x.KingdomStringId), StringComparer.OrdinalIgnoreCase);
            if (removedKingdomIds.Count == 0) return;

            _governments.RemoveAll(x => removedKingdomIds.Contains(x.KingdomStringId));
            _parties.RemoveAll(x => removedKingdomIds.Contains(x.KingdomStringId));
            _seats.RemoveAll(x => removedKingdomIds.Contains(x.KingdomStringId));
            _resolutions.RemoveAll(x => removedKingdomIds.Contains(x.KingdomStringId));
            _pressures.RemoveAll(x => removedKingdomIds.Contains(x.KingdomStringId));
            _lobbyRecords.RemoveAll(x => removedKingdomIds.Contains(x.KingdomStringId));
            _meetings.RemoveAll(x => removedKingdomIds.Contains(x.KingdomStringId));
        }

        private void ReconcileMembership(Kingdom kingdom, ReignGovernmentStateRecord state, bool initializeParties)
        {
            ReignGovernmentInstitutionProfile profile = ReignGovernmentRules.InstitutionForCulture(state.CultureStringId);
            List<SeatCandidate> candidates = SelectSeatCandidates(kingdom, profile);
            List<ReignGovernmentSeatRecord> current = _seats.Where(x => Same(x.KingdomStringId, kingdom.StringId)).ToList();
            HashSet<string> candidateIds = new HashSet<string>(candidates.Select(x => x.Hero.StringId), StringComparer.OrdinalIgnoreCase);
            _seats.RemoveAll(x => Same(x.KingdomStringId, kingdom.StringId) && !candidateIds.Contains(x.HeroStringId));

            List<ReignGovernmentPartyRecord> kingdomParties = _parties.Where(x => Same(x.KingdomStringId, kingdom.StringId)).ToList();
            if (initializeParties || kingdomParties.Count == 0)
            {
                _parties.RemoveAll(x => Same(x.KingdomStringId, kingdom.StringId));
                foreach (ReignGovernmentPartyBlueprint blueprint in ReignGovernmentRules.SelectParties(kingdom.StringId, candidates.Count))
                {
                    _parties.Add(new ReignGovernmentPartyRecord
                    {
                        KingdomStringId = kingdom.StringId,
                        PartyId = blueprint.Id,
                        Name = blueprint.Name,
                        PlanksCsv = string.Join(",", blueprint.Planks),
                        PartyLoyalty = 50,
                        Revision = 1
                    });
                }
                kingdomParties = _parties.Where(x => Same(x.KingdomStringId, kingdom.StringId)).ToList();
            }

            foreach (SeatCandidate candidate in candidates)
            {
                ReignGovernmentSeatRecord seat = current.FirstOrDefault(x => Same(x.HeroStringId, candidate.Hero.StringId));
                if (seat == null)
                {
                    ReignGovernmentPartyBlueprint selected = ReignGovernmentRules.SelectPartyForMember(
                        candidate.Hero.StringId,
                        Personality(candidate.Hero),
                        kingdomParties.Select(ToBlueprint).ToList());
                    int relation = kingdom.Leader == null ? 0 : candidate.Hero.GetRelation(kingdom.Leader);
                    seat = new ReignGovernmentSeatRecord
                    {
                        KingdomStringId = kingdom.StringId,
                        HeroStringId = candidate.Hero.StringId,
                        SettlementStringId = candidate.Settlement?.StringId ?? string.Empty,
                        ClanStringId = candidate.Hero.Clan?.StringId ?? string.Empty,
                        SeatSourceValue = (int)candidate.Source,
                        PartyId = selected?.Id ?? kingdomParties.FirstOrDefault()?.PartyId ?? string.Empty,
                        PartyLoyalty = Clamp(55 + StableOffset(candidate.Hero.StringId, "party-loyalty", 16), 20, 90),
                        GovernmentLoyalty = Clamp(50 + relation / 4 + StableOffset(candidate.Hero.StringId, "government-loyalty", 11), 10, 90),
                        Revision = 1
                    };
                    _seats.Add(seat);
                }
                else
                {
                    seat.SettlementStringId = candidate.Settlement?.StringId ?? string.Empty;
                    seat.ClanStringId = candidate.Hero.Clan?.StringId ?? string.Empty;
                    seat.SeatSourceValue = (int)candidate.Source;
                    if (!kingdomParties.Any(x => Same(x.PartyId, seat.PartyId)))
                        seat.PartyId = kingdomParties.FirstOrDefault()?.PartyId ?? string.Empty;
                    seat.Revision++;
                }
            }
            RecalculatePartyState(kingdom, state);
        }

        private void RecalculatePartyState(Kingdom kingdom, ReignGovernmentStateRecord state)
        {
            List<ReignGovernmentSeatRecord> seats = _seats.Where(x => Same(x.KingdomStringId, kingdom.StringId)).ToList();
            List<ReignGovernmentPartyRecord> parties = _parties.Where(x => Same(x.KingdomStringId, kingdom.StringId)).ToList();
            foreach (ReignGovernmentPartyRecord party in parties)
            {
                List<ReignGovernmentSeatRecord> partySeats = seats.Where(x => Same(x.PartyId, party.PartyId)).ToList();
                party.SeatCount = partySeats.Count;
                party.SpeakerHeroStringId = partySeats
                    .Select(x => new { Seat = x, Hero = FindHero(x.HeroStringId) })
                    .Where(x => x.Hero != null)
                    .OrderByDescending(x => ReignGovernmentRules.SpeakerScore(Personality(x.Hero), x.Seat.GovernmentLoyalty,
                        kingdom.Leader == null ? 0 : x.Hero.GetRelation(kingdom.Leader)))
                    .ThenBy(x => x.Hero.StringId, StringComparer.Ordinal)
                    .Select(x => x.Hero.StringId).FirstOrDefault() ?? string.Empty;
                party.PartyLoyalty = partySeats.Count == 0 ? 0 : Clamp((int)Math.Round(partySeats.Average(x => x.PartyLoyalty)), 0, 100);
                party.IsDominant = false;
                party.IsGoverningCoalition = false;
                party.Revision++;
            }
            ReignGovernmentPartyRecord dominant = parties.OrderByDescending(x => x.SeatCount)
                .ThenBy(x => x.PartyId, StringComparer.Ordinal).FirstOrDefault();
            if (dominant != null)
            {
                dominant.IsDominant = true;
                state.DominantPartyId = dominant.PartyId;
                List<ReignGovernmentPartyRecord> coalition = SelectGoverningCoalition(parties, dominant, seats.Count);
                foreach (ReignGovernmentPartyRecord party in coalition)
                {
                    party.IsGoverningCoalition = true;
                    party.Revision++;
                }
                state.CoalitionPartyIdsCsv = string.Join(",", coalition.Select(x => x.PartyId));
                int coalitionSeats = Math.Max(1, coalition.Sum(x => x.SeatCount));
                double weightedStance = coalition.Sum(party => PartyStanceTowardRuler(
                    kingdom, party, seats) * party.SeatCount) / (double)coalitionSeats;
                state.StanceTowardRuler = Clamp((int)Math.Round(weightedStance), -100, 100);
            }
            else
            {
                state.DominantPartyId = string.Empty;
                state.CoalitionPartyIdsCsv = string.Empty;
                state.StanceTowardRuler = 0;
            }
            state.Revision++;
        }

        private static List<ReignGovernmentPartyRecord> SelectGoverningCoalition(
            IReadOnlyList<ReignGovernmentPartyRecord> parties,
            ReignGovernmentPartyRecord dominant,
            int occupiedSeats)
        {
            var coalition = new List<ReignGovernmentPartyRecord>();
            if (dominant == null || dominant.SeatCount <= 0) return coalition;
            coalition.Add(dominant);
            int coalitionSeats = dominant.SeatCount;
            List<ReignGovernmentPlank> anchor = ParsePlanks(dominant.PlanksCsv);
            foreach (ReignGovernmentPartyRecord candidate in parties.Where(x => x != dominant && x.SeatCount > 0)
                .OrderByDescending(x => CoalitionCompatibility(anchor, ParsePlanks(x.PlanksCsv)))
                .ThenByDescending(x => x.SeatCount)
                .ThenBy(x => ReignGovernmentRules.StableHash("coalition|" + dominant.PartyId + "|" + x.PartyId)))
            {
                if (coalitionSeats * 2 > occupiedSeats) break;
                coalition.Add(candidate);
                coalitionSeats += candidate.SeatCount;
            }
            return coalition;
        }

        private static int CoalitionCompatibility(IReadOnlyList<ReignGovernmentPlank> anchor,
            IReadOnlyList<ReignGovernmentPlank> candidate)
        {
            int score = anchor.Count(candidate.Contains) * 20;
            if (anchor.Contains(ReignGovernmentPlank.RoyalAuthority)
                && candidate.Contains(ReignGovernmentPlank.RepresentativeAuthority)
                || anchor.Contains(ReignGovernmentPlank.RepresentativeAuthority)
                && candidate.Contains(ReignGovernmentPlank.RoyalAuthority)) score -= 40;
            if (anchor.Contains(ReignGovernmentPlank.Peace) && candidate.Contains(ReignGovernmentPlank.Expansion)
                || anchor.Contains(ReignGovernmentPlank.Expansion) && candidate.Contains(ReignGovernmentPlank.Peace)) score -= 30;
            if (anchor.Contains(ReignGovernmentPlank.Trade) && candidate.Contains(ReignGovernmentPlank.Infrastructure)) score += 10;
            if (anchor.Contains(ReignGovernmentPlank.Agriculture) && candidate.Contains(ReignGovernmentPlank.PopularWelfare)) score += 10;
            return score;
        }

        private static int PartyStanceTowardRuler(Kingdom kingdom,
            ReignGovernmentPartyRecord party,
            IReadOnlyList<ReignGovernmentSeatRecord> seats)
        {
            int stance = 0;
            foreach (ReignGovernmentPlank plank in ParsePlanks(party?.PlanksCsv))
            {
                if (plank == ReignGovernmentPlank.RoyalAuthority) stance += 35;
                if (plank == ReignGovernmentPlank.RepresentativeAuthority) stance -= 35;
                if (plank == ReignGovernmentPlank.NoblePrivilege || plank == ReignGovernmentPlank.ClanPrivilege) stance -= 10;
            }
            if (kingdom?.Leader == null || party == null || party.SeatCount <= 0) return stance;
            double relation = seats.Where(x => Same(x.PartyId, party.PartyId)).Select(x => FindHero(x.HeroStringId))
                .Where(x => x != null).Select(x => x.GetRelation(kingdom.Leader)).DefaultIfEmpty(0).Average();
            return stance + (int)Math.Round(relation / 2d);
        }

        private ReignGovernmentVoteResult EvaluateReductionVote(
            Kingdom kingdom,
            ReignGovernmentStateRecord state,
            ReignGovernmentSeatRecord seat)
        {
            ReignGovernmentPartyRecord party = FindParty(kingdom.StringId, seat.PartyId);
            List<ReignGovernmentPlank> planks = ParsePlanks(party?.PlanksCsv);
            ReignGovernmentPlank primary = planks.Count == 0 ? ReignGovernmentPlank.LocalAutonomy : planks[0];
            Hero member = FindHero(seat.HeroStringId);
            Hero speaker = FindHero(party?.SpeakerHeroStringId);
            int occupied = Math.Max(1, _seats.Count(x => Same(x.KingdomStringId, kingdom.StringId)));
            int partySeats = _seats.Count(x => Same(x.KingdomStringId, kingdom.StringId) && Same(x.PartyId, seat.PartyId));
            int relation = member == null || kingdom.Leader == null ? 0 : member.GetRelation(kingdom.Leader);
            int personality = ReductionPersonalityModifier(member);
            bool speakerSupports = SpeakerSupportsReduction(kingdom, state, party, speaker, partySeats / (double)occupied);
            int grievances = Clamp(50 - seat.GovernmentLoyalty, 0, 30);
            return ReignGovernmentRules.EvaluateLevelReductionVote(new ReignGovernmentVoteInput
            {
                PrimaryPlank = primary,
                SecondaryPlanks = planks.Skip(1).ToArray(),
                RulerRelation = relation,
                CurrentLevel = state.Level,
                PartySeatShare = partySeats / (double)occupied,
                PersonalityModifier = personality,
                PartyLoyalty = seat.PartyLoyalty,
                SpeakerSupportsReduction = speakerSupports,
                GrievancePenalty = grievances,
                StableVariance = ReignGovernmentRules.StableVariance(seat.HeroStringId,
                    "reduce-" + state.Level + "-day-" + (int)Math.Floor(CurrentDay()))
            });
        }

        private bool SpeakerSupportsReduction(Kingdom kingdom, ReignGovernmentStateRecord state,
            ReignGovernmentPartyRecord party, Hero speaker, double share)
        {
            List<ReignGovernmentPlank> planks = ParsePlanks(party?.PlanksCsv);
            if (planks.Count == 0) return false;
            int relation = speaker == null || kingdom.Leader == null ? 0 : speaker.GetRelation(kingdom.Leader);
            return ReignGovernmentRules.EvaluateLevelReductionVote(new ReignGovernmentVoteInput
            {
                PrimaryPlank = planks[0],
                SecondaryPlanks = planks.Skip(1).ToArray(),
                RulerRelation = relation,
                CurrentLevel = state.Level,
                PartySeatShare = share,
                PersonalityModifier = ReductionPersonalityModifier(speaker),
                PartyLoyalty = 0,
                SpeakerSupportsReduction = false,
                GrievancePenalty = 0,
                StableVariance = 0
            }).Supports;
        }

        private static int ReductionPersonalityModifier(Hero hero)
        {
            if (hero == null) return 0;
            int calculating = hero.GetTraitLevel(DefaultTraits.Calculating);
            int honor = hero.GetTraitLevel(DefaultTraits.Honor);
            int generosity = hero.GetTraitLevel(DefaultTraits.Generosity);
            return Clamp(calculating * 4 + honor * 3 - generosity * 2, -15, 15);
        }

        private static ReignGovernmentPersonality Personality(Hero hero)
        {
            if (hero == null) return new ReignGovernmentPersonality();
            return new ReignGovernmentPersonality
            {
                Valor = hero.GetTraitLevel(DefaultTraits.Valor),
                Mercy = hero.GetTraitLevel(DefaultTraits.Mercy),
                Generosity = hero.GetTraitLevel(DefaultTraits.Generosity),
                Honor = hero.GetTraitLevel(DefaultTraits.Honor),
                Calculating = hero.GetTraitLevel(DefaultTraits.Calculating),
                Charm = hero.GetSkillValue(DefaultSkills.Charm),
                Leadership = hero.GetSkillValue(DefaultSkills.Leadership),
                IsRulerClanMember = hero.Clan?.Kingdom?.RulingClan == hero.Clan,
                IsLandholdingLord = hero.Clan?.Fiefs?.Count > 0,
                IsMerchant = hero.Occupation == Occupation.Merchant || hero.Occupation == Occupation.Artisan,
                IsRuralNotable = hero.Occupation == Occupation.RuralNotable || hero.Occupation == Occupation.Headman
            };
        }

        private static List<SeatCandidate> SelectSeatCandidates(Kingdom kingdom, ReignGovernmentInstitutionProfile profile)
        {
            var rows = new Dictionary<string, SeatCandidate>(StringComparer.OrdinalIgnoreCase);
            foreach (ReignGovernmentSeatSource source in profile.SeatSources)
            {
                if (source == ReignGovernmentSeatSource.LandholdingClanLeader)
                {
                    foreach (Clan clan in kingdom.Clans.Where(x => x != null && x != kingdom.RulingClan && !x.IsEliminated
                        && !x.IsUnderMercenaryService && x.Fiefs.Count > 0 && IsEligibleMember(x.Leader)))
                        rows[clan.Leader.StringId] = new SeatCandidate(clan.Leader, clan.Fiefs.FirstOrDefault()?.Settlement, source);
                    continue;
                }

                IEnumerable<Settlement> settlements = source == ReignGovernmentSeatSource.VillageNotable
                    || source == ReignGovernmentSeatSource.VillageHeadmanOrLandowner
                    ? Settlement.All.Where(x => x?.IsVillage == true && x.MapFaction == kingdom)
                    : Settlement.All.Where(x => x?.IsTown == true && x.MapFaction == kingdom);
                foreach (Settlement settlement in settlements.OrderBy(x => x.StringId, StringComparer.Ordinal))
                {
                    Hero notable = settlement.Notables.Where(IsEligibleMember)
                        .Where(x => OccupationMatches(x, source))
                        .OrderByDescending(x => x.Power).ThenBy(x => x.StringId, StringComparer.Ordinal).FirstOrDefault();
                    if (notable != null) rows[notable.StringId] = new SeatCandidate(notable, settlement, source);
                }
            }
            return rows.Values.OrderBy(x => x.Hero.StringId, StringComparer.Ordinal).ToList();
        }

        private static bool OccupationMatches(Hero hero, ReignGovernmentSeatSource source)
        {
            if (hero == null) return false;
            if (source == ReignGovernmentSeatSource.TownMerchantOrArtisan)
                return hero.Occupation == Occupation.Merchant || hero.Occupation == Occupation.Artisan;
            if (source == ReignGovernmentSeatSource.VillageHeadmanOrLandowner)
                return hero.Occupation == Occupation.Headman || hero.Occupation == Occupation.RuralNotable;
            if (source == ReignGovernmentSeatSource.TownMerchant)
                return hero.Occupation == Occupation.Merchant;
            return true;
        }

        private static bool IsEligibleMember(Hero hero)
        {
            return hero != null && hero.IsAlive && !hero.IsChild && !hero.IsPrisoner && hero.HeroState != Hero.CharacterStates.Disabled;
        }

        private static bool IsEligibleKingdom(Kingdom kingdom)
        {
            return kingdom != null && !kingdom.IsEliminated && kingdom.Leader != null && kingdom.Fiefs.Count > 0;
        }

        private void ApplyForcedReductionRelations(Kingdom kingdom, ReignGovernmentStateRecord state, int fromLevel,
            IReadOnlyList<ReignGovernmentSeatRecord> voters, ReignGovernmentReductionPenalty penalty)
        {
            HashSet<string> landholdingLeaderIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Clan clan in kingdom.Clans.Where(x => x != null && x != kingdom.RulingClan
                && x != Clan.PlayerClan && !x.IsEliminated && x.Fiefs.Count > 0))
            {
                Hero leader = clan.Leader;
                if (leader == null) continue;
                landholdingLeaderIds.Add(leader.StringId);
                if (IsReductionPenaltyExempt(state, leader, fromLevel)) continue;
                ApplyRelation(kingdom.Leader, leader, penalty.LandholdingClanLeaderRelation);
            }
            foreach (ReignGovernmentSeatRecord dissenter in voters.Where(x => !x.LastReductionVoteSupported
                && !landholdingLeaderIds.Contains(x.HeroStringId)))
            {
                Hero member = FindHero(dissenter.HeroStringId);
                if (IsReductionPenaltyExempt(state, member, fromLevel)) continue;
                ApplyRelation(kingdom.Leader, member, penalty.NonLandholdingDissenterRelation);
                dissenter.GovernmentLoyalty = Clamp(dissenter.GovernmentLoyalty + penalty.NonLandholdingDissenterRelation, 0, 100);
                dissenter.Revision++;
            }
        }

        internal static bool IsReductionPenaltyExempt(ReignGovernmentStateRecord state, Hero member, int fromLevel)
        {
            if (member == null) return false;
            if (member.Clan != null && member.Clan == Clan.PlayerClan) return true;
            if (state == null || state.ReductionConsentFromLevel != fromLevel) return false;
            if (ParseIds(state.ReductionConsentHeroIdsCsv).Contains(member.StringId)) return true;
            return member.Clan != null && ParseIds(state.ReductionConsentClanIdsCsv).Contains(member.Clan.StringId);
        }

        private static void EnsureReductionConsentLevel(ReignGovernmentStateRecord state)
        {
            if (state == null || state.ReductionConsentFromLevel == state.Level) return;
            ClearReductionConsents(state);
            state.ReductionConsentFromLevel = state.Level;
        }

        private static void ClearReductionConsents(ReignGovernmentStateRecord state)
        {
            if (state == null) return;
            state.ReductionConsentFromLevel = 0;
            state.ReductionConsentHeroIdsCsv = string.Empty;
            state.ReductionConsentClanIdsCsv = string.Empty;
        }

        private static void ApplySettlementChange(Kingdom kingdom, int loyalty, int prosperity, int hearth)
        {
            foreach (Town town in kingdom.Fiefs.Where(x => x?.Settlement?.IsFortification == true))
            {
                if (loyalty != 0) town.Loyalty = MBMath.ClampFloat(town.Loyalty + loyalty, 0f, 100f);
                if (prosperity != 0) town.Prosperity = MathF.Max(0f, town.Prosperity + prosperity);
            }
            foreach (Village village in Settlement.All.Where(x => x?.IsVillage == true && x.MapFaction == kingdom)
                .Select(x => x.Village).Where(x => x != null))
            {
                if (hearth != 0) village.Hearth = MathF.Max(0f, village.Hearth + hearth);
            }
        }

        private static void ApplyRelation(Hero ruler, Hero member, int delta)
        {
            if (ruler == null || member == null || ruler == member || delta == 0) return;
            ChangeRelationAction.ApplyRelationChangeBetweenHeroes(ruler, member, delta, false);
        }

        private float ActiveTemporary(ReignGovernmentStateRecord state, float value)
        {
            return state != null && state.TemporaryBenefitEndDay > CurrentDay() ? value : 0f;
        }

        private ReignGovernmentPartyRecord FindParty(string kingdomStringId, string partyId)
        {
            return _parties.FirstOrDefault(x => Same(x.KingdomStringId, kingdomStringId) && Same(x.PartyId, partyId));
        }

        private static ReignGovernmentPartyBlueprint ToBlueprint(ReignGovernmentPartyRecord party)
        {
            return new ReignGovernmentPartyBlueprint(party.PartyId, party.Name, ParsePlanks(party.PlanksCsv).ToArray());
        }

        internal static List<ReignGovernmentPlank> ParsePlanks(string csv)
        {
            var result = new List<ReignGovernmentPlank>();
            foreach (string value in (csv ?? string.Empty).Split(','))
            {
                if (Enum.TryParse(value.Trim(), true, out ReignGovernmentPlank plank)) result.Add(plank);
            }
            return result;
        }

        private static HashSet<string> ParseIds(string csv)
        {
            return new HashSet<string>((csv ?? string.Empty).Split(',')
                .Select(x => x.Trim()).Where(x => x.Length > 0), StringComparer.OrdinalIgnoreCase);
        }

        private static string JoinIds(IEnumerable<string> ids)
        {
            return string.Join(",", (ids ?? Enumerable.Empty<string>())
                .Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase));
        }

        private static Hero FindHero(string id)
        {
            return string.IsNullOrWhiteSpace(id) ? null : Hero.FindFirst(x => Same(x.StringId, id));
        }

        private static float CurrentDay()
        {
            return TaleWorlds.CampaignSystem.Campaign.Current == null ? 0f : (float)CampaignTime.Now.ToDays;
        }

        private static int StableOffset(string id, string salt, int radius)
        {
            int width = radius * 2 + 1;
            return (int)(ReignGovernmentRules.StableHash((salt ?? string.Empty) + "|" + (id ?? string.Empty)) % (uint)width) - radius;
        }

        private static bool Same(string left, string right) => string.Equals(left ?? string.Empty, right ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        private static int Clamp(int value, int minimum, int maximum) => Math.Max(minimum, Math.Min(maximum, value));

        private static List<T> Clean<T>(List<T> source, Func<T, bool> keep) where T : class
        {
            return (source ?? new List<T>()).Where(x => x != null && keep(x)).ToList();
        }

        private void PruneHistory()
        {
            _resolutions = _resolutions.Where(x => Same(x.Status, "debate") || Same(x.Status, "proposed") || Same(x.Status, "active"))
                .Concat(_resolutions.Where(x => !Same(x.Status, "debate") && !Same(x.Status, "proposed") && !Same(x.Status, "active"))
                    .OrderByDescending(x => x.ResolvedDay).Take(RetainedResolvedResolutions)).ToList();
            _meetings = _meetings.OrderByDescending(x => x.MeetingDay).Take(RetainedMeetings).ToList();
            _pressures = _pressures.Where(x => Same(x.Status, "pending") || Same(x.Status, "delayed"))
                .Concat(_pressures.Where(x => !Same(x.Status, "pending") && !Same(x.Status, "delayed"))
                    .OrderByDescending(x => x.RequestedDay).Take(RetainedPressures)).ToList();
            _lobbyRecords = _lobbyRecords.Where(x => !x.Fulfilled && x.ExpiresDay >= CurrentDay())
                .Concat(_lobbyRecords.Where(x => x.Fulfilled || x.ExpiresDay < CurrentDay())
                    .OrderByDescending(x => x.CreatedDay).Take(RetainedLobbyRecords)).ToList();
        }

        private sealed class SeatCandidate
        {
            internal SeatCandidate(Hero hero, Settlement settlement, ReignGovernmentSeatSource source)
            {
                Hero = hero;
                Settlement = settlement;
                Source = source;
            }

            internal Hero Hero { get; }
            internal Settlement Settlement { get; }
            internal ReignGovernmentSeatSource Source { get; }
        }
    }
}
