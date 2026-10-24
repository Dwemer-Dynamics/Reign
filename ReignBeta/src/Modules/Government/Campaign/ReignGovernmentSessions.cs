using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Reign.Core.Contracts.Government;
using TaleWorlds.CampaignSystem;
using ReignBeta.Runtime;

namespace ReignBeta.Government
{
    public sealed partial class ReignGovernmentCampaignBehavior
    {
        partial void RequestGovernmentCrownDeputy(ReignGovernmentBusinessRecord record);
        partial void RecordGovernmentSessionNews(ReignGovernmentStateRecord state, float sessionDay);
        partial void RecordGovernmentPresentationNews(ReignGovernmentBusinessRecord record);
        partial void RecordGovernmentDecisionNews(ReignGovernmentBusinessRecord record);
        partial void RecordGovernmentVoteNews(ReignGovernmentBusinessRecord record);
        partial void SyncGovernmentSessionNews(IDataStore store);
        partial void TickGovernmentSessionNews();

        private static float NextGovernmentSessionDay(float nativeDay)
        {
            if (!ReignCalendarService.TryGetAbsoluteCalendarDay(GovernmentCampaignTime(nativeDay), out double calendarDay)
                || !ReignCalendarService.TryFromAbsoluteCalendarDay(GovernmentSessionRules.NextSession(calendarDay), out CampaignTime next)) return -1;
            float result = (float)next.ToDays;
            // Legacy domain timestamps are floats. Round forward by one representable value when
            // needed; rounding backward could schedule the same session twice or display day14.
            if ((double)result < next.ToDays)
                result = BitConverter.ToSingle(BitConverter.GetBytes(BitConverter.ToInt32(BitConverter.GetBytes(result), 0) + 1), 0);
            return result;
        }

        private static CampaignTime GovernmentCampaignTime(float nativeDay) =>
            CampaignTime.Milliseconds((long)Math.Round((double)nativeDay * CampaignTime.Days(1f).ToMilliseconds));

        public static string FormatGovernmentDate(float nativeDay, bool includeYear = true)
        {
            if (nativeDay < 0 || !ReignCalendarService.TryGetCalendarPosition(GovernmentCampaignTime(nativeDay), out int year, out _, out int season, out int day, out _)) return "Awaiting calendar";
            return new[] { "Spring", "Summer", "Autumn", "Winter" }[season] + " " + (day + 1) + (includeYear ? ", " + year : "");
        }

        private void OnGovernmentSessionClock()
        {
            ReconcileConstitutions();
            TickParticipation();
            foreach (var kingdom in Kingdom.All.Where(IsEligibleKingdom).ToList())
            {
                var state = EnsureGovernment(kingdom);
                if (state == null || IsCharterPending(kingdom.StringId)) continue;
                EnsureSessionCalendar(state);
                if (state.SessionProtocolVersion > 0 && CampaignTime.Now.ToDays >= state.NextMeetingDay) RunSeasonalMeeting(kingdom, state, kingdom.Leader == Hero.MainHero && Hero.MainHero.CurrentSettlement == GovernmentCapital(kingdom));
            }
            TickGovernmentBusiness(); TickGovernmentSessionNews();
        }

        private void EnsureSessionCalendar(ReignGovernmentStateRecord state)
        {
            if (state.SessionProtocolVersion == GovernmentSessionRules.ProtocolVersion) return;
            float next = NextGovernmentSessionDay(CurrentDay());
            if (next < 0) return;
            state.SessionProtocolVersion = GovernmentSessionRules.ProtocolVersion;
            state.NextMeetingDay = next;
            state.Revision++;
        }

        private void InitializeBusinessSchedule(ReignGovernmentBusinessRecord record, bool migrating = false)
        {
            if (record.SessionProtocolVersion != 0) return;
            record.LegacySessionMigration |= migrating || record.NativeCaptureSource == "unresolved";
            if (NextGovernmentSessionDay(CurrentDay()) < 0) return;
            migrating = record.LegacySessionMigration;
            record.SessionProtocolVersion = GovernmentSessionRules.ProtocolVersion;
            if (GovernmentBusinessRules.IsClosed(record.Status) || record.ExecutionApplied || record.NativeEffectStarted
                || record.Status == "authorized" || record.Status == "awaiting_counterpart" || record.Status == "execution_failed") return;
            if (migrating)
            {
                record.LegacySessionMigration = true;
                record.VotesJson = "[]"; record.WinningOptionId = "";
                record.AuthorityLevelAtDecision = 0; record.DecisionRulerHeroStringId = "";
            }
            record.PresentationDay = NextGovernmentSessionDay(CurrentDay());
            record.DecisionDay = NextGovernmentSessionDay(record.PresentationDay);
            if (record.Status != "awaiting_sponsorship") record.Status = "queued";
            if (record.IsUrgent && !migrating && record.Status != "awaiting_sponsorship") PresentGovernmentBusiness(record, CurrentDay(), true);
            else if (record.Status == "queued") AssignDocket(record);
        }

        private void MigrateGovernmentSessions()
        {
            foreach (var state in _governments) EnsureSessionCalendar(state);
            foreach (var record in _business) InitializeBusinessSchedule(record, true);
        }

        internal void RestoreSessionCalendar()
        {
            MigrateGovernmentSessions();
            RestoreParticipationSeats();
            CaptureGovernmentLoadFingerprintPhase("session_calendar_restored");
        }

        private string BusinessIntroductionSlot(ReignGovernmentBusinessRecord record)
        {
            if (!string.IsNullOrEmpty(record.IntroductionPartyId)) return record.IntroductionPartyId;
            var kingdom = FindKingdom(record.KingdomStringId);
            if (Same(record.PetitionerHeroStringId, kingdom?.Leader?.StringId)) return "crown";
            if (record.Kind == "seasonal")
                return _resolutions.FirstOrDefault(x => Same(x.ResolutionId, record.TargetId))?.PartyId ?? "crown";
            // Official incoming business has no invented domestic sponsor and uses the Crown's
            // agenda allowance; it cannot create an additional unbudgeted petition slot.
            return GetSeats(record.KingdomStringId).FirstOrDefault(x => Same(x.HeroStringId, record.SponsorHeroStringId))?.PartyId ?? "crown";
        }

        private void PresentGovernmentBusiness(ReignGovernmentBusinessRecord record, float day, bool extraordinary)
        {
            if (record.FrozenAuthorityLevel > 0) return;
            var petition = _participation.Petitions.FirstOrDefault(x => x.BusinessId == record.BusinessId);
            if (petition != null && (!ValidatePetitionTerms(petition, out string petitionReason)
                || (record.Kind == "admission" || record.Kind == "recall") && !ValidateMandateBusiness(record, out petitionReason)))
            {
                petition.Status = "blocked"; petition.BlockReason = petitionReason;
                CloseInvalidBusiness(record, petitionReason); return;
            }
            var state = GetGovernment(record.KingdomStringId);
            if (state == null) return;
            record.SessionProtocolVersion = GovernmentSessionRules.ProtocolVersion;
            record.PresentationDay = day;
            record.DecisionDay = extraordinary ? day : NextGovernmentSessionDay(day);
            record.FrozenAuthorityLevel = state.Level;
            record.IntroductionPartyId = BusinessIntroductionSlot(record);
            var seats = GetSeats(record.KingdomStringId).Where(x => FindHero(x.HeroStringId)?.IsAlive == true)
                .GroupBy(x => x.HeroStringId, StringComparer.Ordinal).Select(x => x.First()).ToList();
            decimal weight = GovernmentSessionRules.MemberWeight(state.Level, seats.Count);
            record.FrozenSeatsJson = new JArray(seats.Select(x => new JObject { ["memberHeroId"] = x.HeroStringId,
                ["partyId"] = x.PartyId, ["weight"] = weight })).ToString(Formatting.None);
            record.Status = extraordinary ? "hearing" : "presented";
            AssignDocket(record);
            var positions = new JArray();
            foreach (var seat in seats)
            {
                var member = FindHero(seat.HeroStringId);
                var ranked = BusinessOptions(record).OrderByDescending(x => BusinessOptionScore(record, seat, member, x, true)).ToList();
                string optionId = member != Hero.MainHero && AppliedPoliticalProfile(member.StringId) != null && ranked.Count > 0 && (ranked.Count == 1 || BusinessOptionScore(record, seat, member, ranked[0], true)
                    - BusinessOptionScore(record, seat, member, ranked[1], true) >= 8) ? ranked[0].Value<string>("id") : "";
                string agreed = AgreedVoteOption(record.BusinessId, seat.HeroStringId);
                if (ranked.Any(x => x.Value<string>("id") == agreed)) optionId = agreed;
                positions.Add(new JObject { ["memberHeroId"] = seat.HeroStringId, ["optionId"] = optionId,
                    ["position"] = optionId.Length == 0 ? "Undecided" : optionId == record.StatusQuoOptionId ? "Against" : "For",
                    ["declaredDay"] = day, ["source"] = "public_session_statement" });
            }
            record.PublicPositionsJson = positions.ToString(Formatting.None);
            foreach (var commitment in _governmentCommitments.Where(x => x.Status == "active" && Same(x.BusinessId, record.BusinessId)))
                commitment.ExpiresDay = record.DecisionDay;
            ChangedBusiness(record);
            RecordGovernmentPresentationNews(record);
        }

        private void AdvanceRegularGovernmentSession(Kingdom kingdom, ReignGovernmentStateRecord state, float day)
        {
            ImportSeasonalBusiness();
            foreach (var record in _business.Where(x => Same(x.KingdomStringId, kingdom.StringId))) InitializeBusinessSchedule(record);
            var used = new HashSet<string>(_business.Where(x => Same(x.KingdomStringId, kingdom.StringId)
                && x.FrozenAuthorityLevel > 0 && x.DecisionDay > x.PresentationDay && Math.Abs(x.PresentationDay - day) < 0.01f).Select(BusinessIntroductionSlot));
            foreach (var record in _business.Where(x => Same(x.KingdomStringId, kingdom.StringId) && x.Status == "queued" && x.PresentationDay <= day + .01f)
                .OrderBy(x => x.CreatedDay).ThenBy(x => x.BusinessId, StringComparer.Ordinal).ToList())
            {
                string slot = BusinessIntroductionSlot(record);
                if (used.Contains(slot))
                {
                    record.PresentationDay = NextGovernmentSessionDay(day);
                    record.DecisionDay = NextGovernmentSessionDay(record.PresentationDay);
                    AssignDocket(record);
                    continue;
                }
                used.Add(slot); PresentGovernmentBusiness(record, day, false);
            }
            TickGovernmentBusiness();
            RecordGovernmentSessionNews(state, day);
        }

        public bool IsGovernmentRulerPresent(string businessId)
        {
            var record = _business.FirstOrDefault(x => Same(x.BusinessId, businessId));
            var kingdom = FindKingdom(record?.KingdomStringId);
            var ruler = kingdom?.Leader;
            var capital = GovernmentCapital(kingdom);
            return ruler != null && capital != null && ruler.CurrentSettlement == capital && GovernmentAttendanceBlock(ruler, false).Length == 0;
        }

        public bool CanPlayerVoteBusiness(string businessId)
        {
            var record = _business.FirstOrDefault(x => Same(x.BusinessId, businessId));
            return record != null && FindKingdom(record.KingdomStringId)?.Leader == Hero.MainHero && IsGovernmentRulerPresent(businessId)
                && GovernmentSessionRules.CanRecordBallots(record.Status, JArray.Parse(record.VotesJson ?? "[]").Count > 0, record.ExecutionApplied)
                && record.FrozenAuthorityLevel > 0 && ReignGovernmentNativeSafety.IsDecisionDue(record, CampaignTime.Now.ToDays);
        }

        public bool RecordGovernmentCrownPosition(string businessId, string actorHeroId, string optionId, string source, out string reason)
        {
            reason = "The Crown position cannot be recorded for this matter.";
            var record = _business.FirstOrDefault(x => Same(x.BusinessId, businessId));
            if (record == null || GovernmentBusinessRules.IsClosed(record.Status) || record.ExecutionApplied
                || !GovernmentBusinessRules.CanRecommend(record.Status) || !BusinessOptions(record).Any(x => x.Value<string>("id") == optionId)) return false;
            var kingdom = FindKingdom(record.KingdomStringId);
            bool ruler = source == "ruler" && Same(kingdom?.Leader?.StringId, actorHeroId) && IsGovernmentRulerPresent(businessId);
            bool deputy = source == "chancellor" && record.FrozenAuthorityLevel >= 2 && !IsGovernmentRulerPresent(businessId)
                && IsActiveGovernmentChancellor(record.KingdomStringId, actorHeroId);
            if (!ruler && !deputy) return false;
            record.CrownActorHeroId = actorHeroId; record.CrownOptionId = optionId;
            record.CrownVoteSource = source; record.CrownVoteStatus = "recorded";
            record.RecommendedOptionId = optionId; record.RecommendationRulerHeroStringId = kingdom.Leader.StringId;
            ChangedBusiness(record); reason = "The Crown's declared position is recorded."; return true;
        }

        public bool DeclareBusinessPosition(string businessId, string memberHeroId, string optionId, out string result)
        {
            result = "A declaration cannot be changed after the vote.";
            var record = _business.FirstOrDefault(x => Same(x.BusinessId, businessId));
            if (record == null || !GovernmentBusinessRules.CanRecommend(record.Status) || record.FrozenAuthorityLevel == 0
                || !GetSeats(record.KingdomStringId).Any(x => Same(x.HeroStringId, memberHeroId))) return false;
            if (!string.IsNullOrEmpty(optionId) && !BusinessOptions(record).Any(x => x.Value<string>("id") == optionId)) return false;
            var positions = JArray.Parse(record.PublicPositionsJson);
            var row = positions.OfType<JObject>().FirstOrDefault(x => x.Value<string>("memberHeroId") == memberHeroId);
            if (row == null) return false;
            row["optionId"] = optionId ?? "";
            row["position"] = string.IsNullOrEmpty(optionId) ? "Undecided" : optionId == record.StatusQuoOptionId ? "Against" : "For";
            row["declaredDay"] = CurrentDay(); row["source"] = "individual_declaration";
            record.PublicPositionsJson = positions.ToString(Formatting.None); ChangedBusiness(record);
            result = "The member's revocable declaration is recorded."; return true;
        }

        public JArray GetBusinessPublicPositions(string businessId)
        {
            var record = _business.FirstOrDefault(x => Same(x.BusinessId, businessId));
            if (record == null) return new JArray();
            var ballots = JArray.Parse(record.VotesJson ?? "[]").OfType<JObject>().ToList();
            if (ballots.Count > 0)
            {
                // Historical rows use the exact ballots, never today's roster or pre-vote claims.
                // Vacated seats remain visible with their frozen share and recorded abstention.
                return new JArray(ballots.Where(x => x.Value<string>("memberHeroId") != "crown").Select(ballot =>
                {
                    string id = ballot.Value<string>("memberHeroId"), optionId = ballot.Value<string>("optionId") ?? "";
                    bool eligible = ballot.Value<bool?>("eligible") ?? !string.IsNullOrEmpty(optionId);
                    string label = BusinessOptions(record).FirstOrDefault(x => x.Value<string>("id") == optionId)?.Value<string>("label") ?? "";
                    return new JObject {
                        ["memberHeroId"] = id, ["name"] = ballot.Value<string>("name") ?? FindHero(id)?.Name?.ToString() ?? "Former member",
                        ["partyId"] = ballot.Value<string>("partyId"), ["weight"] = ballot.Value<decimal>("weight"),
                        ["position"] = optionId.Length == 0 ? "Abstained" : optionId == record.StatusQuoOptionId ? "Against" : "For",
                        ["optionId"] = optionId, ["declaredDay"] = ballot["castDay"]?.DeepClone(),
                        ["statement"] = optionId.Length == 0 ? "Recorded abstention." : "Recorded vote: " + label + ".",
                        ["isFinal"] = true, ["eligible"] = eligible, ["vacant"] = !eligible };
                }));
            }
            var frozen = JArray.Parse(record.FrozenSeatsJson).OfType<JObject>().ToList();
            var occupied = new HashSet<string>(GetSeats(record.KingdomStringId).Where(x => FindHero(x.HeroStringId)?.IsAlive == true).Select(x => x.HeroStringId), StringComparer.Ordinal);
            return new JArray(JArray.Parse(record.PublicPositionsJson).OfType<JObject>().Select(position => new JObject
            {
                ["memberHeroId"] = position.Value<string>("memberHeroId"), ["name"] = FindHero(position.Value<string>("memberHeroId"))?.Name?.ToString() ?? "Vacant seat",
                ["position"] = occupied.Contains(position.Value<string>("memberHeroId")) ? position.Value<string>("position") : "Undecided",
                ["optionId"] = occupied.Contains(position.Value<string>("memberHeroId")) ? position.Value<string>("optionId") : "",
                ["declaredDay"] = position.Value<float>("declaredDay"),
                ["isFinal"] = false, ["eligible"] = occupied.Contains(position.Value<string>("memberHeroId")),
                ["vacant"] = !occupied.Contains(position.Value<string>("memberHeroId")),
                ["partyId"] = frozen.FirstOrDefault(x => x.Value<string>("memberHeroId") == position.Value<string>("memberHeroId"))?.Value<string>("partyId"),
                ["weight"] = frozen.FirstOrDefault(x => x.Value<string>("memberHeroId") == position.Value<string>("memberHeroId"))?.Value<decimal>("weight") ?? 0m
            }));
        }

        public JObject GetBusinessForecast(string businessId)
        {
            var record = _business.FirstOrDefault(x => Same(x.BusinessId, businessId));
            if (record == null) return new JObject();
            var positions = GetBusinessPublicPositions(businessId).OfType<JObject>().ToList();
            bool final = JArray.Parse(record.VotesJson ?? "[]").Count > 0;
            int crownWeight = record.FrozenAuthorityLevel > 0 ? GovernmentSessionRules.CrownWeight(record.FrozenAuthorityLevel) : 0;
            var options = new JArray(BusinessOptions(record).Select(option => new JObject {
                ["optionId"] = option.Value<string>("id"), ["label"] = option.Value<string>("label"),
                ["weight"] = positions.Where(x => x.Value<string>("optionId") == option.Value<string>("id")).Sum(x => x.Value<decimal>("weight"))
                    + (record.CrownVoteStatus == "recorded" && record.CrownOptionId == option.Value<string>("id") ? crownWeight : 0),
                ["names"] = new JArray(positions.Where(x => x.Value<string>("optionId") == option.Value<string>("id")).Select(x => x.Value<string>("name"))
                    .Concat(record.CrownVoteStatus == "recorded" && record.CrownOptionId == option.Value<string>("id")
                        ? new[] { (FindHero(record.CrownActorHeroId)?.Name?.ToString() ?? "Crown") + " (Crown)" } : Array.Empty<string>())) }));
            decimal uncastWeight = Math.Max(0m, 100m - options.OfType<JObject>().Sum(x => x.Value<decimal>("weight")));
            decimal vacantWeight = Math.Min(uncastWeight, positions.Where(x => x.Value<bool?>("vacant") == true).Sum(x => x.Value<decimal>("weight")));
            return new JObject { ["options"] = options, ["isFinal"] = final,
                ["undecidedWeight"] = final ? 0m : uncastWeight - vacantWeight,
                ["abstainedWeight"] = final ? uncastWeight - vacantWeight : 0m, ["vacantWeight"] = vacantWeight,
                ["crownWeight"] = crownWeight, ["crownActorHeroId"] = record.CrownActorHeroId, ["crownVoteSource"] = record.CrownVoteSource,
                ["crownVoteStatus"] = record.CrownVoteStatus, ["presentationDay"] = record.PresentationDay, ["decisionDay"] = record.DecisionDay,
                ["presentationDate"] = FormatGovernmentDate(record.PresentationDay), ["decisionDate"] = FormatGovernmentDate(record.DecisionDay),
                ["canPlayerVote"] = CanPlayerVoteBusiness(businessId), ["rule"] = "Declared positions may change. The largest cast weighted share wins; abstentions are not redistributed." };
        }

        public JObject GetBusinessSessionSummary(string kingdomId)
        {
            var state = GetGovernment(kingdomId);
            float day = state?.NextMeetingDay ?? NextGovernmentSessionDay(CurrentDay());
            var queued = _business.Where(x => Same(x.KingdomStringId, kingdomId) && x.Status == "queued").ToList();
            var slots = new JArray(GetParties(kingdomId).Select(party => new JObject { ["partyId"] = party.PartyId, ["label"] = party.Name,
                ["used"] = queued.Any(x => BusinessIntroductionSlot(x) == party.PartyId), ["limit"] = 1,
                ["proposalTitle"] = queued.FirstOrDefault(x => BusinessIntroductionSlot(x) == party.PartyId)?.Title ?? "" }));
            return new JObject { ["nextSessionDay"] = day, ["nextSessionDate"] = FormatGovernmentDate(day), ["seasonLengthDays"] = GovernmentSessionRules.SeasonDays,
                ["sessionDays"] = new JArray(15, 30), ["slots"] = slots, ["rulerSlotUsed"] = queued.Any(x => BusinessIntroductionSlot(x) == "crown"),
                ["rule"] = "Introduce at one regular session; decide at the next. Each party and the ruler may introduce one matter per session." };
        }
    }
}
