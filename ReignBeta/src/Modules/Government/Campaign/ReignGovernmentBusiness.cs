using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Reign.Core.Contracts.Government;
using ReignBeta.Save;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Election;

namespace ReignBeta.Government
{
    public sealed partial class ReignGovernmentCampaignBehavior
    {
        private List<ReignGovernmentBusinessRecord> _business = new List<ReignGovernmentBusinessRecord>();
        private List<string> _businessChunks = new List<string>();
        // KingdomDecision and List<KingdomDecision> are native save types. Preserve exact terms, including tribute.
        private List<KingdomDecision> _ownedNativeDecisions = new List<KingdomDecision>();
        private KingdomDecision _executingGovernmentDecision;
        public event Action<ReignGovernmentBusinessRecord> BusinessChanged;

        public IReadOnlyList<ReignGovernmentBusinessRecord> GetBusiness(string kingdomId)
        {
            ImportSeasonalBusiness();
            return _business.Where(x => Same(x.KingdomStringId, kingdomId)).OrderBy(x => GovernmentBusinessRules.IsClosed(x.Status))
                .ThenByDescending(x => x.IsUrgent).ThenBy(x => x.CreatedDay).ToList();
        }

        private void SyncGovernmentBusiness(IDataStore store)
        {
            if (store.IsSaving)
            {
                PruneGovernmentBusiness();
                _businessChunks = ReignSavePayloadCodec.Encode(JsonConvert.SerializeObject(_business));
            }
            store.SyncData("_reignGovernment_businessChunks_v1", ref _businessChunks);
            store.SyncData("_reignGovernment_ownedNativeDecisions_v1", ref _ownedNativeDecisions);
            if (store.IsLoading)
            {
                // Corrupt state must fail loading visibly, never silently return native authority or lose decisions.
                _business = _businessChunks == null || _businessChunks.Count == 0 ? new List<ReignGovernmentBusinessRecord>()
                    : JsonConvert.DeserializeObject<List<ReignGovernmentBusinessRecord>>(ReignSavePayloadCodec.Decode(_businessChunks))
                        ?? new List<ReignGovernmentBusinessRecord>();
                _ownedNativeDecisions = _ownedNativeDecisions ?? new List<KingdomDecision>();
                foreach (var record in _business.Where(x => x.SessionProtocolVersion == 0)) record.LegacySessionMigration = true;
            }
        }

        private void PruneGovernmentBusiness()
        {
            var removed = _business.Where(x => GovernmentBusinessRules.IsClosed(x.Status)).OrderByDescending(x => x.ResolvedDay)
                .Skip(160).ToList();
            if (removed.Count == 0) return;
            foreach (var record in removed) _business.Remove(record);
            var native = new List<KingdomDecision>();
            foreach (var record in _business)
            {
                var decision = NativeBusinessDecision(record);
                var aliases = (record.NativeDecisionAliasIndices ?? new List<int>())
                    .Where(i => i >= 0 && i < _ownedNativeDecisions.Count).Select(i => _ownedNativeDecisions[i]).ToList();
                record.NativeDecisionIndex = decision == null ? -1 : native.Count;
                if (decision != null) native.Add(decision);
                record.NativeDecisionAliasIndices = new List<int>();
                foreach (var alias in aliases)
                { record.NativeDecisionAliasIndices.Add(native.Count); native.Add(alias); }
            }
            _ownedNativeDecisions = native;
        }

        private void ChangedBusiness(ReignGovernmentBusinessRecord record)
        {
            InitializeBusinessSchedule(record);
            record.Revision++;
            BusinessChanged?.Invoke(record);
        }

        public bool RecommendBusiness(string businessId, string optionId, Hero actor, out string result)
        {
            ReignGovernmentBusinessRecord record = AuthorizedBusiness(businessId, actor, out result);
            if (record == null) return false;
            if (!GovernmentBusinessRules.CanRecommend(record.Status) || !BusinessOptions(record).Any(x => Same(x.Value<string>("id"), optionId)))
            { result = "That recommendation is not available at this stage."; return false; }
            if (!IsGovernmentRulerPresent(businessId)) { result = "The ruler must attend at the capital to declare the Crown position."; return false; }
            record.CrownActorHeroId = actor.StringId; record.CrownOptionId = optionId;
            record.CrownVoteSource = "ruler"; record.CrownVoteStatus = "recorded";
            record.RecommendedOptionId = optionId;
            record.RecommendationRulerHeroStringId = actor.StringId;
            ChangedBusiness(record);
            result = "The ruler's recommendation has been recorded. Members retain their own votes.";
            return true;
        }

        public bool AdoptBusinessPetition(string businessId, Hero actor, out string result)
        {
            var record = AuthorizedBusiness(businessId, actor, out result);
            if (record == null || record.Status != "awaiting_sponsorship") return false;
            record.Status = "queued";
            record.IntroductionPartyId = "crown";
            record.SponsorReason = "The ruler adopted this petition for government consideration.";
            ChangedBusiness(record);
            result = "The petition is now before the government.";
            return true;
        }

        public bool PostponeBusiness(string businessId, Hero actor, out string result)
        { result = "Regular business is decided at its next scheduled session; urgent business cannot be recessed."; return false; }

        public bool ReconveneBusiness(string businessId, Hero actor, out string result)
        { result = "Government sessions follow the published calendar."; return false; }

        public bool VoteBusiness(string businessId, Hero actor, out string result)
        {
            var record = AuthorizedBusiness(businessId, actor, out result);
            if (record == null) return false;
            if (!CanPlayerVoteBusiness(businessId) && (actor == Hero.MainHero || !IsGovernmentRulerPresent(businessId)))
            { result = "Attend the capital when this matter is due for decision."; return false; }
            return CastGovernmentWeightedVote(record, true, out result);
        }

        public bool ResolveBusiness(string businessId, string optionId, Hero actor, bool overrideOpposition, out string result)
        {
            result = "The weighted vote is final; ordinary government decisions have no later override.";
            return false;
        }

        private bool CastGovernmentWeightedVote(ReignGovernmentBusinessRecord record, bool rulerRequested, out string result)
        {
            result = "This matter is not due for a decision.";
            if (record.FrozenAuthorityLevel == 0 || !ReignGovernmentNativeSafety.IsDecisionDue(record, CampaignTime.Now.ToDays)
                || !GovernmentSessionRules.CanRecordBallots(record.Status, JArray.Parse(record.VotesJson ?? "[]").Count > 0, record.ExecutionApplied)) return false;
            if (WaitForPlayerMemberBallot(record)) { result = "The attending player must vote or explicitly abstain."; return false; }
            var frozenIds = JArray.Parse(record.FrozenSeatsJson).Select(x => x.Value<string>("memberHeroId")).ToList();
            if (GetSeats(record.KingdomStringId).Any(x => frozenIds.Contains(x.HeroStringId) && x.HeroStringId != Hero.MainHero?.StringId && AppliedPoliticalProfile(x.HeroStringId) == null))
            { QueuePoliticalProfileSync(); result = "Awaiting the members' saved political profiles before deciding."; return false; }
            if (!ValidateNativeBusiness(record, out result)) return false;
            var kingdom = FindKingdom(record.KingdomStringId);
            bool present = IsGovernmentRulerPresent(record.BusinessId);
            if (record.FrozenAuthorityLevel == 1 && (!present || !rulerRequested))
            { result = "The ruler must attend and personally approve this matter."; return false; }
            if (present && rulerRequested)
            {
                if (string.IsNullOrEmpty(record.RecommendedOptionId) && record.FrozenAuthorityLevel < 5)
                { result = "Declare the Crown's choice before calling the vote."; return false; }
                if (!string.IsNullOrEmpty(record.RecommendedOptionId))
                    RecordGovernmentCrownPosition(record.BusinessId, kingdom.Leader.StringId, record.RecommendedOptionId, "ruler", out result);
                else { record.CrownVoteStatus = "abstained"; record.CrownOptionId = ""; record.CrownVoteSource = "ruler"; record.CrownActorHeroId = kingdom.Leader.StringId; }
            }
            else if (!present)
            {
                if (record.CrownVoteSource == "ruler" || record.CrownVoteSource == "chancellor"
                    && !IsActiveGovernmentChancellor(record.KingdomStringId, record.CrownActorHeroId))
                { record.CrownOptionId = ""; record.CrownActorHeroId = ""; record.CrownVoteStatus = "unrecorded"; record.CrownVoteSource = ""; }
                if (record.FrozenAuthorityLevel >= 2 && (record.CrownVoteStatus == "unrecorded" || record.CrownVoteStatus == "pending")) RequestGovernmentCrownDeputy(record);
                if (record.CrownVoteStatus == "pending") { result = "Awaiting the Chancellor's recorded decision."; return false; }
            }
            var options = BusinessOptions(record);
            var ballots = new JArray();
            var currentSeats = GetSeats(record.KingdomStringId);
            foreach (var frozen in JArray.Parse(record.FrozenSeatsJson).OfType<JObject>())
            {
                string memberId = frozen.Value<string>("memberHeroId");
                var seat = currentSeats.FirstOrDefault(x => Same(x.HeroStringId, memberId));
                var member = FindHero(memberId);
                string option = "";
                if (seat != null && member?.IsAlive == true && member == Hero.MainHero)
                {
                    var submitted = _participation.Ballots.FirstOrDefault(x => x.BusinessId == record.BusinessId && x.HeroId == memberId);
                    option = submitted != null && !submitted.Abstain && options.Any(x => x.Value<string>("id") == submitted.OptionId) ? submitted.OptionId : "";
                }
                else if (seat != null && member?.IsAlive == true && !member.IsPrisoner && AppliedPoliticalProfile(memberId) != null)
                {
                    option = AgreedVoteOption(record.BusinessId, memberId);
                    if (string.IsNullOrEmpty(option)) option = options.OrderByDescending(x => BusinessOptionScore(record, seat, member, x, true))
                        .ThenBy(x => x.Value<string>("id"), StringComparer.Ordinal).FirstOrDefault()?.Value<string>("id") ?? "";
                }
                ballots.Add(new JObject { ["memberHeroId"] = memberId, ["partyId"] = frozen.Value<string>("partyId"),
                    ["optionId"] = option, ["weight"] = frozen.Value<decimal>("weight"),
                    ["eligible"] = seat != null && member?.IsAlive == true, ["castDay"] = CurrentDay(),
                    ["name"] = member?.Name?.ToString() ?? "Vacant seat",
                    ["absentee"] = !GetBusinessAttendance(record.BusinessId).Any(x => Same(x.HeroStringId, memberId) && x.Status == "present") });
            }
            if (record.CrownVoteStatus == "recorded") ballots.Add(new JObject { ["memberHeroId"] = "crown", ["actorHeroId"] = record.CrownActorHeroId,
                ["optionId"] = record.CrownOptionId, ["weight"] = GovernmentSessionRules.CrownWeight(record.FrozenAuthorityLevel), ["source"] = record.CrownVoteSource });
            var weighted = ballots.OfType<JObject>().Select(x => new GovernmentWeightedBallot { VoterId = x.Value<string>("memberHeroId"), OptionId = x.Value<string>("optionId"), Weight = x.Value<decimal>("weight") });
            string winner = GovernmentSessionRules.Winner(weighted, options.Select(x => x.Value<string>("id")), record.StatusQuoOptionId);
            if (string.IsNullOrEmpty(winner)) { result = "Every voting share abstained; the matter remains pending."; return false; }
            record.VotesJson = ballots.ToString(Formatting.None); record.WinningOptionId = winner;
            record.AuthorityLevelAtDecision = record.FrozenAuthorityLevel; record.DecisionRulerHeroStringId = kingdom.Leader.StringId;
            bool executed = ExecuteBusiness(record, winner, false, out result);
            if (!executed && (record.Status == "hearing" || record.Status == "presented"))
            {
                record.Status = "execution_failed";
                record.Outcome = "The recorded vote could not be implemented: " + GovernmentBusinessRules.PublicText(result)
                    + " The vote is preserved for review; it will not be retallied or retried automatically.";
                ChangedBusiness(record);
                result = record.Outcome;
            }
            // Adapters can enter quarantine themselves. Release hearing attendance and
            // private commitments in either path without retrying uncertain effects.
            if (!executed && record.Status == "execution_failed") NotifyGovernmentBusinessClosed(record);
            RecordGovernmentVoteNews(record);
            return executed;
        }

        private ReignGovernmentBusinessRecord AuthorizedBusiness(string businessId, Hero actor, out string result)
        {
            result = string.Empty;
            var record = _business.FirstOrDefault(x => Same(x.BusinessId, businessId));
            Kingdom kingdom = FindKingdom(record?.KingdomStringId);
            if (record == null || kingdom == null || actor == null || actor != kingdom.Leader)
            { result = "Only this kingdom's current ruler may direct the hearing."; return null; }
            if (GovernmentBusinessRules.IsClosed(record.Status))
            { result = "This matter has already concluded."; return null; }
            RefreshBusinessRuler(record, kingdom);
            NormalizeBusinessPublicText(record);
            return record;
        }

        public void TickGovernmentBusiness()
        {
            ReignGovernmentPatches.VerifyReady();
            foreach (var record in _business.Where(x => !GovernmentBusinessRules.IsClosed(x.Status)).ToList())
            {
                InitializeBusinessSchedule(record);
                if (record.SessionProtocolVersion == 0) continue;
                if (!string.IsNullOrEmpty(record.NativeDecisionType) && !record.ExecutionApplied
                    && !ValidateNativeBusiness(record, out _)) continue;
                ObserveGovernmentActionQueue(record);
                if (GovernmentBusinessRules.IsClosed(record.Status)) continue;
                Kingdom kingdom = FindKingdom(record.KingdomStringId);
                if (kingdom == null || !IsEligibleKingdom(kingdom))
                { CloseInvalidBusiness(record, "The kingdom no longer has an eligible government."); continue; }
                if (IsCharterPending(kingdom.StringId)) continue;
                RefreshBusinessRuler(record, kingdom);
                ApplyDueGovernmentDeclarations(record);
                ObserveCounterpartBusiness(record);
                if (record.Status == "awaiting_sponsorship")
                {
                    FindBusinessSponsor(record);
                    if (record.Status == "awaiting_sponsorship" && CurrentDay() >= record.CreatedDay + GovernmentBusinessRules.PetitionExpiryDays)
                    { record.Status = "expired"; record.Outcome = "The petition expired without sponsorship; no ruler refusal occurred."; NotifyGovernmentBusinessClosed(record); ChangedBusiness(record); }
                }
                if (record.Status == "queued" && record.IsUrgent && !record.LegacySessionMigration)
                    PresentGovernmentBusiness(record, CurrentDay(), true);
                if (record.Status == "presented" && ReignGovernmentNativeSafety.IsDecisionDue(record, CampaignTime.Now.ToDays))
                { record.Status = "hearing"; ChangedBusiness(record); }
                if (record.Status != "hearing" || kingdom.Leader == null) continue;
                bool present = IsGovernmentRulerPresent(record.BusinessId);
                if (present && kingdom.Leader == Hero.MainHero) continue;
                if (present)
                {
                    if (AppliedPoliticalProfile(kingdom.Leader.StringId) == null) { QueuePoliticalProfileSync(); continue; }
                    var decision = NativeBusinessDecision(record);
                    var rulerSeat = GetSeats(record.KingdomStringId).FirstOrDefault(x => Same(x.HeroStringId, kingdom.Leader.StringId))
                        ?? new ReignGovernmentSeatRecord { HeroStringId = kingdom.Leader.StringId, PartyId = GetGovernment(kingdom)?.DominantPartyId ?? "" };
                    var choice = BusinessOptions(record).OrderByDescending(x => decision != null ? NativeClanPreference(decision, kingdom.RulingClan, x) + PersonalBusinessAlignment(record, kingdom.Leader, x)
                        : BusinessOptionScore(record, rulerSeat, kingdom.Leader, x, false))
                        .ThenBy(x => x.Value<string>("id"), StringComparer.Ordinal).FirstOrDefault();
                    if (choice != null) RecordGovernmentCrownPosition(record.BusinessId, kingdom.Leader.StringId, choice.Value<string>("id"), "ruler", out _);
                }
                CastGovernmentWeightedVote(record, present, out _);
            }
        }

        private void RefreshBusinessRuler(ReignGovernmentBusinessRecord record, Kingdom kingdom)
        {
            if (record.AuthorityLevelAtDecision != 0 || string.IsNullOrEmpty(record.RecommendationRulerHeroStringId)
                || Same(record.RecommendationRulerHeroStringId, kingdom?.Leader?.StringId)) return;
            record.RecommendedOptionId = string.Empty;
            record.RecommendationRulerHeroStringId = string.Empty;
            record.CrownActorHeroId = ""; record.CrownOptionId = "";
            record.CrownVoteSource = ""; record.CrownVoteStatus = "unrecorded";
            ChangedBusiness(record);
        }

        private void FindBusinessSponsor(ReignGovernmentBusinessRecord record)
        {
            if (string.IsNullOrEmpty(record.PetitionerHeroStringId)) { record.Status = "queued"; return; }
            Kingdom kingdom = FindKingdom(record.KingdomStringId);
            Hero petitioner = FindHero(record.PetitionerHeroStringId);
            if (petitioner == null || !petitioner.IsAlive) { CloseInvalidBusiness(record, "The petitioner is no longer available."); return; }
            var seats = GetSeats(record.KingdomStringId);
            if (petitioner == kingdom?.Leader) { record.Status = "queued"; return; }
            if (seats.Any(x => Same(x.HeroStringId, petitioner.StringId))) record.SponsorHeroStringId = petitioner.StringId;
            else
            {
                var requested = BusinessOptions(record).FirstOrDefault(x => Same(x.Value<string>("id"), record.RequestedOptionId));
                var alternatives = BusinessOptions(record).Where(x => !Same(x.Value<string>("id"), record.RequestedOptionId)).ToList();
                record.SponsorHeroStringId = GovernmentBusinessRules.SelectSponsor(seats.Select(seat =>
                {
                    Hero member = FindHero(seat.HeroStringId);
                    int support = member == null || requested == null ? -1000 : BusinessOptionScore(record, seat, member, requested, false)
                        - (alternatives.Count == 0 ? 0 : alternatives.Max(x => BusinessOptionScore(record, seat, member, x, false)));
                    return new GovernmentSponsorCandidate { HeroId = seat.HeroStringId, Eligible = member?.IsAlive == true && member != Hero.MainHero && AppliedPoliticalProfile(member.StringId) != null,
                        IssueSupport = support, PetitionerRelation = member?.GetRelation(petitioner) ?? 0 };
                }));
            }
            if (!string.IsNullOrEmpty(record.SponsorHeroStringId))
            {
                record.Status = "queued";
                record.SponsorReason = "A seated representative supports the requested outcome and has agreed to introduce it.";
                ChangedBusiness(record);
            }
        }

        private int BusinessOptionScore(ReignGovernmentBusinessRecord record, ReignGovernmentSeatRecord seat, Hero member, JObject option, bool includeRecommendation)
        {
            Kingdom kingdom = FindKingdom(record.KingdomStringId);
            string optionId = option.Value<string>("id");
            bool affirmative = option.Value<bool?>("affirmative") ?? false;
            string agreedPlatform = AgreedPlatformParty(member.StringId);
            var planks = ParsePlanks(FindParty(record.KingdomStringId, agreedPlatform.Length > 0 ? agreedPlatform : seat.PartyId)?.PlanksCsv);
            int alignment = ActionPlankAlignment(MapNativeDecisionKind(NativeBusinessDecision(record)), planks);
            int interest = 0;
            int personality = 0;
            if (record.Kind == "edict") { alignment = ActionPlankAlignment(ReignGovernmentActionKind.Policy, planks); interest = EdictOptionInterest(record, member); }
            if (record.Kind == "admission" || record.Kind == "recall") { alignment = 0; interest = MandateOptionInterest(record, member); }
            if (record.Kind == "world_action") alignment = ActionPlankAlignment((ReignGovernmentActionKind)record.ActionKindValue, planks);
            if (record.Kind == "seasonal")
            {
                var resolution = _resolutions.FirstOrDefault(x => Same(x.ResolutionId, record.TargetId));
                var template = ReignGovernmentResolutionCatalog.Find(resolution?.TemplateId);
                interest = template == null ? 0 : planks.Count(x => template.SupportingPlanks.Contains(x)) * 15;
                if (Same(seat.PartyId, resolution?.PartyId)) interest += 20;
                if (optionId == "route:0" || optionId == "route:1")
                {
                    var route = optionId == "route:0" ? template?.FirstRoute : template?.SecondRoute;
                    if (route != null && UsesImmediateGold(route.Action) && (kingdom?.Leader?.Gold ?? 0) < route.Target) interest -= 60;
                }
            }
            if (record.Kind == "policy")
            {
                var policy = (NativeBusinessDecision(record) as KingdomPolicyDecision)?.Policy;
                if (policy != null)
                {
                    float weight = (planks.Contains(ReignGovernmentPlank.RoyalAuthority) ? policy.AuthoritarianWeight : 0f)
                        + (planks.Contains(ReignGovernmentPlank.NoblePrivilege) || planks.Contains(ReignGovernmentPlank.ClanPrivilege) ? policy.OligarchicWeight : 0f)
                        + (planks.Contains(ReignGovernmentPlank.PopularWelfare) || planks.Contains(ReignGovernmentPlank.RepresentativeAuthority) ? policy.EgalitarianWeight : 0f);
                    interest = (int)Math.Round(weight * 24f);
                    if (seat.SeatSourceValue != (int)ReignGovernmentSeatSource.LandholdingClanLeader)
                        interest += (int)Math.Round((policy.EgalitarianWeight - policy.OligarchicWeight) * 18f);
                    if (record.IsRepeal) interest = -interest;
                    alignment = 0; // Policy identity/effects replace generic royal-authority scoring.
                }
            }
            else if (record.Kind == "fief_allocation")
            {
                Clan candidate = Clan.All.FirstOrDefault(x => Same(x.StringId, option.Value<string>("targetId")));
                if (candidate != null)
                {
                    interest = candidate == member.Clan ? 45 : 0;
                    interest += candidate.Settlements.Count == 0 ? 18 : -Math.Min(20, candidate.Settlements.Count * 3);
                    interest += candidate.Leader == null ? 0 : member.GetRelation(candidate.Leader) / 5;
                    alignment = 0;
                }
            }
            else if (record.Kind == "war" || record.Kind == "call_to_war") personality = member.GetTraitLevel(DefaultTraits.Valor) * 6 - member.GetTraitLevel(DefaultTraits.Mercy) * 3;
            else if (record.Kind == "peace") personality = member.GetTraitLevel(DefaultTraits.Mercy) * 6 - member.GetTraitLevel(DefaultTraits.Valor) * 3;
            if (record.Kind != "fief_allocation")
            {
                int direction = affirmative ? 1 : -1;
                interest *= direction; alignment *= direction; personality *= direction;
            }
            return PersonalBusinessAlignment(record, member, option) + GovernmentBusinessRules.OptionScore(interest, alignment,
                kingdom?.Leader == null ? 0 : member.GetRelation(kingdom.Leader),
                includeRecommendation && Same(record.RecommendedOptionId, optionId),
                includeRecommendation ? GetGovernmentCommitmentShift(record.BusinessId, member.StringId, optionId) : 0,
                personality, ReignGovernmentRules.StableVariance(member.StringId, record.BusinessId + "|" + optionId));
        }

        private static List<JObject> BusinessOptions(ReignGovernmentBusinessRecord record) => JArray.Parse(record.OptionsJson ?? "[]").OfType<JObject>().ToList();
        private KingdomDecision NativeBusinessDecision(ReignGovernmentBusinessRecord record) => record.NativeDecisionIndex >= 0 && record.NativeDecisionIndex < _ownedNativeDecisions.Count
            ? _ownedNativeDecisions[record.NativeDecisionIndex] : null;

        private void CloseInvalidBusiness(ReignGovernmentBusinessRecord record, string reason)
        {
            record.Status = "invalidated";
            record.Outcome = reason;
            record.ResolvedDay = CurrentDay();
            NotifyGovernmentBusinessClosed(record);
            ChangedBusiness(record);
        }
    }
}
