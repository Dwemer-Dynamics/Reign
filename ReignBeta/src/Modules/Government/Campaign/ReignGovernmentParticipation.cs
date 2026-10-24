using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Reign.Core.Contracts.Government;
using ReignBeta.Integration;
using ReignBeta.Runtime;
using ReignBeta.Save;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Settlements;

namespace ReignBeta.Government
{
    public sealed partial class ReignGovernmentCampaignBehavior
    {
        private GovernmentParticipationState _participation = new GovernmentParticipationState();
        private List<string> _participationChunks = new List<string>();
        public GovernmentParticipationState Participation => _participation;
        private bool _accreditingSession;

        private void SyncParticipation(IDataStore store)
        {
            if (store.IsSaving) _participationChunks = ReignSavePayloadCodec.Encode(JsonConvert.SerializeObject(_participation));
            store.SyncData("_reignGovernment_participation_v1", ref _participationChunks);
            if (!store.IsLoading) return;
            _participation = _participationChunks == null || _participationChunks.Count == 0 ? new GovernmentParticipationState()
                : JsonConvert.DeserializeObject<GovernmentParticipationState>(ReignSavePayloadCodec.Decode(_participationChunks));
            if (_participation == null || _participation.Version != 1
                || _participation.Constitutions.GroupBy(x => x.KingdomId).Any(x => x.Count() > 1)
                || _participation.Constituencies.GroupBy(x => x.KingdomId + "|" + x.Id).Any(x => x.Count() > 1)
                || _participation.Petitions.GroupBy(x => x.Id).Any(x => x.Count() > 1)
                || _participation.Profiles.Any(x => !GovernmentPoliticalBeliefs.Valid(x)))
                throw new InvalidOperationException("Government participation save identity validation failed. No appointments, funds or votes were replayed.");
            foreach (var party in _participation.Elections.GroupBy(x => x.KingdomId + "|" + x.PartyId))
            {
                int round = 0;
                foreach (var election in party)
                {
                    round++;
                    if (string.IsNullOrEmpty(election.Id)) election.Id = party.Key + "|" + round;
                }
            }
        }

        private static int GovernmentInterval(float nativeDay)
        {
            return ReignCalendarService.TryGetAbsoluteCalendarDay(GovernmentCampaignTime(nativeDay), out double day)
                ? GovernmentParticipationRules.Interval(day) : -1;
        }

        public GovernmentConstitution GetConstitution(string kingdomId) => _participation.Constitutions.FirstOrDefault(x => Same(x.KingdomId, kingdomId));
        private GovernmentConstitution EnsureConstitution(ReignGovernmentStateRecord state)
        {
            var constitution = GetConstitution(state.KingdomStringId);
            if (constitution != null) return constitution;
            constitution = new GovernmentConstitution { KingdomId = state.KingdomStringId,
                InstitutionId = ((ReignGovernmentInstitutionKind)state.InstitutionKindValue).ToString(), DraftAuthority = state.Level };
            _participation.Constitutions.Add(constitution); return constitution;
        }
        private ReignGovernmentInstitutionProfile Institution(ReignGovernmentStateRecord state) =>
            GovernmentParticipationRules.Institution(EnsureConstitution(state).InstitutionId);

        private void RegisterParticipationEvents()
        {
            CampaignEvents.KingdomCreatedEvent.AddNonSerializedListener(this, OnParticipationKingdomCreated);
            CampaignEvents.OnClanChangedKingdomEvent.AddNonSerializedListener(this, OnParticipationClanChanged);
        }

        private void OnParticipationKingdomCreated(Kingdom kingdom)
        {
            if (kingdom == null || GetConstitution(kingdom.StringId) != null) return;
            var recommended = ReignGovernmentRules.InstitutionForCulture(kingdom.Culture?.StringId);
            _participation.Constitutions.Add(new GovernmentConstitution { KingdomId = kingdom.StringId,
                InstitutionId = recommended.Kind.ToString(), Source = "new_kingdom", Confirmed = false });
        }

        private void OnParticipationClanChanged(Clan clan, Kingdom oldKingdom, Kingdom newKingdom,
            ChangeKingdomAction.ChangeKingdomActionDetail detail, bool showNotification)
        {
            foreach (var kingdom in new[] { oldKingdom, newKingdom }.Where(x => x != null).Distinct())
            { var state = GetGovernment(kingdom); if (state != null) ReconcileMembership(kingdom, state, false); }
        }

        private void ReconcileConstitutions()
        {
            foreach (var constitution in _participation.Constitutions.Where(x => !x.Confirmed && x.Source == "new_kingdom").ToList())
                ReconcileFoundingConstitution(FindKingdom(constitution.KingdomId), constitution);
        }

        private void ReconcileFoundingConstitution(Kingdom kingdom, GovernmentConstitution constitution)
        {
            if (constitution == null || constitution.Confirmed || constitution.Source != "new_kingdom"
                || kingdom?.Leader == null || kingdom.RulingClan == null) return;
            // Native creation can finish while time is paused. Resolve the real ruler
            // before exposing the charter, rather than requiring an hourly tick.
            if (kingdom.Leader == Hero.MainHero)
            {
                constitution.Source = "player_foundation";
                var state = GetGovernment(kingdom);
                if (state != null) { state.Level = 1; state.InstitutionName = "Founding charter pending"; }
            }
            else { constitution.Source = "ai_foundation"; constitution.Confirmed = true; }
            constitution.Revision++;
        }

        public bool IsCharterPending(string kingdomId) => GetConstitution(kingdomId)?.Confirmed == false;
        public JArray GetFoundingInstitutionChoices(string kingdomId) => new JArray(GovernmentParticipationRules.Institutions.Select(profile =>
        {
            var kingdom = FindKingdom(kingdomId);
            var candidates = kingdom == null ? new List<SeatCandidate>() : SelectSeatCandidates(kingdom, profile);
            return new JObject { ["id"] = profile.Kind.ToString(), ["label"] = profile.Name,
                ["title"] = GovernmentParticipationRules.Title(profile.Kind), ["memberCount"] = candidates.Count,
                ["description"] = string.Join("; ", profile.SeatSources.Select(SeatEntitlementDescription))
                    + ". New constituencies gain seats after one stable day and accreditation at the next regular session.",
                ["members"] = new JArray(candidates.Select(x => new JObject { ["heroId"] = x.Hero.StringId,
                    ["name"] = x.Hero.Name.ToString(), ["constituency"] = x.Settlement?.Name?.ToString() ?? x.Hero.Clan?.Name?.ToString() })) };
        }));

        private static string SeatEntitlementDescription(ReignGovernmentSeatSource source)
        {
            switch (source)
            {
                case ReignGovernmentSeatSource.LandholdingClanLeader: return "One seat per non-ruling landholding clan";
                case ReignGovernmentSeatSource.VillageNotable: return "One notable constituency per village";
                case ReignGovernmentSeatSource.VillageHeadmanOrLandowner: return "One headman or landowner constituency per village";
                case ReignGovernmentSeatSource.TownMerchantOrArtisan: return "One merchant or artisan constituency per town";
                case ReignGovernmentSeatSource.TownMerchant: return "One merchant constituency per town";
                default: return "One notable constituency per town";
            }
        }

        public bool SetCharterDraft(string kingdomId, string institutionId, int level, Hero actor, bool confirm, out string result)
        {
            result = "Only the founding ruler can complete this kingdom's pending charter.";
            var kingdom = FindKingdom(kingdomId); var constitution = GetConstitution(kingdomId);
            ReconcileFoundingConstitution(kingdom, constitution);
            if (actor == null || actor != kingdom?.Leader || constitution == null || constitution.Confirmed || constitution.Source != "player_foundation") return false;
            var profile = GovernmentParticipationRules.Institutions.FirstOrDefault(x => x.Kind.ToString() == institutionId);
            if (profile == null || level < 1 || level > 5) { result = "Choose a valid institution and authority level."; return false; }
            int count = SelectSeatCandidates(kingdom, profile).Count;
            if (confirm && !GovernmentParticipationRules.CanConfirmCharter(level, count))
            { result = "Level 5 requires at least one eligible member. Choose a representative institution or another authority level."; return false; }
            constitution.InstitutionId = institutionId; constitution.DraftAuthority = level; constitution.Revision++;
            if (!confirm) { result = "Charter draft saved."; return true; }
            constitution.Confirmed = true;
            var state = EnsureGovernment(kingdom);
            state.InstitutionKindValue = (int)profile.Kind; state.InstitutionName = profile.Name; state.Level = level;
            state.InitialBenefitsSuppressed = true; state.Revision++;
            ReconcileMembership(kingdom, state, true);
            result = profile.Name + " established. Crown " + GovernmentSessionRules.CrownWeight(level) + "%; members "
                + (100 - GovernmentSessionRules.CrownWeight(level)) + "%. No authority-change reward was granted.";
            RecordParticipationNews(kingdomId, "founding", result);
            return true;
        }

        public GovernmentAccess GetParticipationAccess(string kingdomId, Hero actor, string businessId = null)
        {
            var kingdom = FindKingdom(kingdomId);
            var access = new GovernmentAccess { Inspect = kingdom != null && GetGovernment(kingdom) != null,
                CharterPending = IsCharterPending(kingdomId) };
            if (!access.Inspect || actor == null || !actor.IsAlive || actor.IsChild)
            { access.Reason = "An adult living character and an existing government are required."; return access; }
            access.Crown = actor == kingdom.Leader;
            if (access.CharterPending) { access.Reason = "Complete the founding charter before government business begins."; return access; }
            var seat = GetSeats(kingdomId).FirstOrDefault(x => Same(x.HeroStringId, actor.StringId));
            bool foreign = actor.Clan?.Kingdom != null && actor.Clan.Kingdom != kingdom;
            access.Prepare = !foreign && actor.Clan?.IsUnderMercenaryService != true;
            access.Member = !foreign && seat != null && !access.Crown;
            access.Speaker = access.Member && GetParties(kingdomId).Any(x => x.SpeakerHeroStringId == actor.StringId);
            var record = _business.FirstOrDefault(x => Same(x.BusinessId, businessId));
            access.Ballot = access.Member && record != null && record.KingdomStringId == kingdomId && record.FrozenAuthorityLevel > 0
                && JArray.Parse(record.FrozenSeatsJson).Any(x => x.Value<string>("memberHeroId") == actor.StringId)
                && GovernmentSessionRules.CanRecordBallots(record.Status, JArray.Parse(record.VotesJson ?? "[]").Count > 0, record.ExecutionApplied);
            access.Reason = foreign ? "Foreign governments are available for public inspection." : access.Member ? "Your office permits member ballots and petitions; Crown powers remain with the ruler."
                : access.Crown ? "You hold the Crown introduction and its constitutional share." : "You may prepare petitions and seek a civic mandate or qualify for a clan seat.";
            return access;
        }

        private void RestoreParticipationSeats()
        {
            // Run after calendar restoration and before the campaign readiness seal. A
            // restored realm must expose its existing constituencies before the first tick
            // or ownership callback; those callbacks must never turn growth into migration.
            foreach (var state in _governments.ToList())
            {
                var kingdom = FindKingdom(state.KingdomStringId);
                if (IsEligibleKingdom(kingdom) && GetSeats(kingdom.StringId).Count > 0
                    && !_participation.Constituencies.Any(row => row.KingdomId == kingdom.StringId))
                    ReconcileMembership(kingdom, state, false);
            }
        }

        private void ReconcileParticipationSeats(Kingdom kingdom, ReignGovernmentStateRecord state, bool initial)
        {
            var constitution = EnsureConstitution(state);
            if (!constitution.Confirmed) return;
            var profile = Institution(state);
            var all = _participation.Constituencies.Where(x => x.KingdomId == kingdom.StringId).ToList();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var source in profile.SeatSources)
            {
                if (source == ReignGovernmentSeatSource.LandholdingClanLeader)
                {
                    foreach (var clan in kingdom.Clans.Where(x => x != null && x != kingdom.RulingClan && !x.IsEliminated && !x.IsUnderMercenaryService && x.Fiefs.Count > 0))
                        ReconcileConstituency(kingdom, source, null, clan, all, seen, initial);
                }
                else foreach (var settlement in Settlement.All.Where(x => x.MapFaction == kingdom
                    && ((source == ReignGovernmentSeatSource.VillageNotable || source == ReignGovernmentSeatSource.VillageHeadmanOrLandowner) ? x.IsVillage : x.IsTown)))
                    ReconcileConstituency(kingdom, source, settlement, null, all, seen, initial);
            }
            foreach (var lost in all.Where(x => !seen.Contains(x.Id)))
            { if (lost.Status != "suspended") RecordParticipationNews(kingdom.StringId, "representation", "Representation suspended for " + ConstituencyLabel(lost) + ". Its old voting shares abstain."); lost.Status = "suspended"; }
            var active = _participation.Constituencies.Where(x => x.KingdomId == kingdom.StringId && x.Status == "active" && x.IncumbentId.Length > 0).ToList();
            _seats.RemoveAll(x => x.KingdomStringId == kingdom.StringId && !active.Any(c => c.IncumbentId == x.HeroStringId));
            var parties = GetParties(kingdom.StringId).ToList();
            if (parties.Count == 0)
            {
                foreach (var blueprint in ReignGovernmentRules.SelectParties(kingdom.StringId, active.Count))
                    _parties.Add(new ReignGovernmentPartyRecord { KingdomStringId = kingdom.StringId, PartyId = blueprint.Id,
                        Name = blueprint.Name, PlanksCsv = string.Join(",", blueprint.Planks), Revision = 1 });
                parties = GetParties(kingdom.StringId).ToList();
            }
            foreach (var constituency in active)
            {
                var hero = FindHero(constituency.IncumbentId);
                var seat = _seats.FirstOrDefault(x => x.KingdomStringId == kingdom.StringId && x.HeroStringId == hero.StringId);
                if (seat != null) continue;
                var beliefs = AppliedPoliticalProfile(hero.StringId);
                string partyId = beliefs == null ? "" : parties.OrderByDescending(x => GovernmentPoliticalBeliefs.PartyCompatibility(beliefs, ParsePlanks(x.PlanksCsv)))
                    .ThenBy(x => ReignGovernmentRules.StableHash(hero.StringId + "|" + x.PartyId)).Select(x => x.PartyId).FirstOrDefault() ?? "";
                // An unsynchronized newcomer has a real seat but cannot take ideological action
                // until the canonical profile arrives; do not manufacture a neutral ideology.
                _seats.Add(new ReignGovernmentSeatRecord { KingdomStringId = kingdom.StringId, HeroStringId = hero.StringId,
                    SettlementStringId = constituency.SettlementId, ClanStringId = hero.Clan?.StringId ?? "", SeatSourceValue = (int)constituency.Source,
                    PartyId = partyId, Revision = 1 });
            }
            RecalculatePartyState(kingdom, state);
        }

        private void ReconcileConstituency(Kingdom kingdom, ReignGovernmentSeatSource source, Settlement settlement, Clan clan,
            List<GovernmentConstituency> existing, HashSet<string> seen, bool initial)
        {
            string id = GovernmentParticipationRules.ConstituencyId(source, settlement?.StringId ?? "", clan?.StringId ?? ""); seen.Add(id);
            var row = existing.FirstOrDefault(x => x.Id == id);
            if (row == null)
            {
                var migrated = GetSeats(kingdom.StringId).FirstOrDefault(x => x.SeatSourceValue == (int)source
                    && (clan != null ? x.ClanStringId == clan.StringId : x.SettlementStringId == settlement.StringId));
                row = new GovernmentConstituency { Id = id, KingdomId = kingdom.StringId, Source = source,
                    SettlementId = settlement?.StringId ?? "", ClanId = clan?.StringId ?? "", EligibleSinceDay = CurrentDay(),
                    IncumbentId = migrated?.HeroStringId ?? "", Status = initial || migrated != null ? "active" : "pending" };
                _participation.Constituencies.Add(row);
            }
            if (row.Status == "suspended") { row.Status = "pending"; row.EligibleSinceDay = CurrentDay(); }
            Hero incumbent = FindHero(row.IncumbentId);
            bool valid = EligibleOfficeHolder(incumbent, kingdom) && (clan != null ? incumbent == clan.Leader
                : row.Mandate == "civic" || QualifiesAsConstituencyResident(incumbent, settlement, kingdom) && OccupationMatches(incumbent, source));
            if (!valid && row.Mandate != "vacant")
            {
                if (row.Mandate == "civic")
                {
                    row.IncumbentId = ""; row.Mandate = "vacant"; row.AdmissionPetitionId = "";
                }
                else
                {
                row.Mandate = "ordinary"; row.AdmissionPetitionId = "";
                var candidate = clan?.Leader ?? settlement?.Notables.Where(x => EligibleOfficeHolder(x, kingdom) && OccupationMatches(x, source)
                    && QualifiesAsConstituencyResident(x, settlement, kingdom))
                    .OrderByDescending(x => x.Power).ThenBy(x => x.StringId, StringComparer.Ordinal).FirstOrDefault();
                row.IncumbentId = EligibleOfficeHolder(candidate, kingdom) ? candidate.StringId : "";
                if (!initial && row.IncumbentId.Length > 0)
                { row.Status = "pending"; row.EligibleSinceDay = CurrentDay(); }
                }
            }
            if (_participation.Constituencies.Any(x => x != row && x.Status == "active" && x.IncumbentId.Length > 0 && x.IncumbentId == row.IncumbentId)) row.IncumbentId = "";
            if (row.Status == "pending" && _accreditingSession && CurrentDay() >= row.EligibleSinceDay + GovernmentParticipationBalance.TerritorialStabilityDays)
            {
                row.Status = "active"; row.AccreditedDay = CurrentDay(); row.AccreditedInterval = GovernmentInterval(CurrentDay());
                RecordParticipationNews(kingdom.StringId, "representation", ConstituencyLabel(row) + " now has representation in " + GetGovernment(kingdom).InstitutionName + ".");
            }
            if (initial && row.AccreditedInterval < 0) { row.AccreditedDay = CurrentDay(); row.AccreditedInterval = GovernmentInterval(CurrentDay()); }
        }

        private static bool EligibleOfficeHolder(Hero hero, Kingdom kingdom) => kingdom != null && !kingdom.IsEliminated && hero != null && hero.IsAlive && !hero.IsChild
            && hero.HeroState != Hero.CharacterStates.Disabled && hero != kingdom.Leader
            && (hero.Clan?.Kingdom == null || hero.Clan.Kingdom == kingdom) && hero.Clan?.IsUnderMercenaryService != true;
        public string ConstituencyLabel(GovernmentConstituency row) => row.Source == ReignGovernmentSeatSource.LandholdingClanLeader
            ? FindClan(row.ClanId)?.Name?.ToString() ?? row.ClanId : FindSettlement(row.SettlementId)?.Name?.ToString() ?? row.SettlementId;
        public string GovernmentOfficeTitle(string heroId)
        {
            var seat = _seats.FirstOrDefault(x => x.HeroStringId == heroId);
            if (seat == null || !EligibleOfficeHolder(FindHero(heroId), FindKingdom(seat.KingdomStringId))) return "";
            var state = GetGovernment(seat.KingdomStringId); if (state == null) return "";
            string title = GovernmentParticipationRules.Title(Institution(state).Kind);
            return _parties.Any(x => x.KingdomStringId == seat.KingdomStringId && x.SpeakerHeroStringId == heroId) ? title + " and party speaker" : title;
        }
        public string GetOfficeKingdomId(string heroId) => GovernmentOfficeTitle(heroId).Length == 0 ? ""
            : _seats.First(x => x.HeroStringId == heroId).KingdomStringId;
        public bool RecognizesClanSeats(string kingdomId)
        {
            var state = GetGovernment(kingdomId);
            return state != null && Institution(state).SeatSources.Contains(ReignGovernmentSeatSource.LandholdingClanLeader);
        }
        public bool RecognizeClanSeat(string kingdomId, Hero actor, out string result)
        {
            var kingdom = FindKingdom(kingdomId); var state = GetGovernment(kingdomId);
            result = "Complete the kingdom's founding charter before claiming representation.";
            if (state == null || IsCharterPending(kingdomId)) return false;
            if (!RecognizesClanSeats(kingdomId))
            {
                result = state.InstitutionName + " draws its members from settlement constituencies. Prepare a civic membership petition for an existing mandate.";
                return false;
            }
            result = "This constitution requires you to lead a non-ruling landholding clan of this kingdom.";
            if (actor?.Clan == null || actor.Clan.Leader != actor
                || actor.Clan.Kingdom != kingdom || actor.Clan == kingdom.RulingClan || actor.Clan.Fiefs.Count == 0
                || !EligibleOfficeHolder(actor, kingdom)) return false;
            ReconcileMembership(kingdom, state, false);
            var seat = _participation.Constituencies.FirstOrDefault(x => x.KingdomId == kingdomId && x.ClanId == actor.Clan.StringId
                && x.Source == ReignGovernmentSeatSource.LandholdingClanLeader);
            result = seat?.Status == "active" ? "Your clan's constitutional seat is already recognized."
                : "Your entitlement is recorded. After one stable day, accreditation occurs at the next regular session. No payment or admission vote is required.";
            return seat != null;
        }
        public string ParticipationSummary(string kingdomId, Hero actor, string businessId)
        {
            var state = GetGovernment(kingdomId); if (state == null || actor == null) return "No current government record.";
            var seats = GetSeats(kingdomId);
            var seat = seats.FirstOrDefault(x => x.HeroStringId == actor.StringId);
            var constituency = _participation.Constituencies.FirstOrDefault(x => x.KingdomId == kingdomId && x.Status == "active" && x.IncumbentId == actor.StringId);
            var party = GetParties(kingdomId).FirstOrDefault(x => x.PartyId == seat?.PartyId);
            var business = _business.FirstOrDefault(x => x.BusinessId == businessId && x.KingdomStringId == kingdomId);
            var frozen = business == null ? null : JArray.Parse(business.FrozenSeatsJson).OfType<JObject>().FirstOrDefault(x => x.Value<string>("memberHeroId") == actor.StringId);
            string role = actor == FindKingdom(kingdomId)?.Leader ? "Ruler" : seat == null ? "Petitioner / public visitor" : GovernmentOfficeTitle(actor.StringId);
            return state.InstitutionName + "\n" + role + (constituency == null ? "" : " · " + ConstituencyLabel(constituency))
                + "\nParty: " + (party?.Name ?? "Unaffiliated") + "\nCurrent chamber: " + seats.Count + " members."
                + (seat == null ? "" : " New matters: " + GovernmentSessionRules.MemberWeight(state.Level, seats.Count).ToString("0.###") + "% per member.")
                + (business == null ? "" : "\nSelected matter: " + (frozen == null ? "you are not on its voting roll." : frozen.Value<decimal>("weight").ToString("0.###") + "% recorded share."))
                + "\nNext session: " + FormatGovernmentDate(state.NextMeetingDay) + ". Existing rolls do not change when territory or membership changes.";
        }
        public bool ResignCivicMandate(string kingdomId, Hero actor, out string result)
        {
            result = "Only your own exceptional civic mandate can be resigned here. Clan entitlement follows native clan leadership and land.";
            var row = _participation.Constituencies.FirstOrDefault(x => x.KingdomId == kingdomId && x.Status == "active" && x.IncumbentId == actor?.StringId && x.Mandate == "civic");
            if (row == null) return false;
            row.IncumbentId = ""; row.Mandate = "vacant"; row.AdmissionPetitionId = "";
            ReconcileMembership(FindKingdom(kingdomId), GetGovernment(kingdomId), false);
            result = "You resigned the civic mandate. Previously frozen voting weight does not transfer to a successor.";
            RecordParticipationNews(kingdomId, "representation", actor.Name + " resigned the mandate for " + ConstituencyLabel(row) + ".");
            return true;
        }

        private void RecordParticipationNews(string kingdomId, string kind, string text)
        {
            RecordParticipationNewsCore(kingdomId, kind, text);
        }
        partial void RecordParticipationNewsCore(string kingdomId, string kind, string text);
        private GovernmentPoliticalProfile AppliedPoliticalProfile(string heroId) => _participation.Profiles.FirstOrDefault(x => x.HeroId == heroId);
    }
}
