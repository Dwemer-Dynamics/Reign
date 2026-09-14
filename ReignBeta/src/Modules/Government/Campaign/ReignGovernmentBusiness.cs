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
                record.NativeDecisionIndex = decision == null ? -1 : native.Count;
                if (decision != null) native.Add(decision);
            }
            _ownedNativeDecisions = native;
        }

        private void ChangedBusiness(ReignGovernmentBusinessRecord record)
        {
            record.Revision++;
            BusinessChanged?.Invoke(record);
        }

        public bool RecommendBusiness(string businessId, string optionId, Hero actor, out string result)
        {
            ReignGovernmentBusinessRecord record = AuthorizedBusiness(businessId, actor, out result);
            if (record == null) return false;
            if (!GovernmentBusinessRules.CanRecommend(record.Status) || !BusinessOptions(record).Any(x => Same(x.Value<string>("id"), optionId)))
            { result = "That recommendation is not available at this stage."; return false; }
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
            record.Status = "hearing";
            record.SponsorReason = "The ruler adopted this petition for government consideration.";
            ChangedBusiness(record);
            result = "The petition is now before the government.";
            return true;
        }

        public bool PostponeBusiness(string businessId, Hero actor, out string result)
        {
            var record = AuthorizedBusiness(businessId, actor, out result);
            if (record == null) return false;
            if (!GovernmentBusinessRules.CanPostpone(record.Status, record.IsUrgent, record.Postponed))
            { result = "Only a nonurgent hearing may take its one seven-day recess."; return false; }
            record.Postponed = true;
            record.RecessUntilDay = CurrentDay() + GovernmentBusinessRules.RecessDays;
            record.Status = "postponed";
            ChangedBusiness(record);
            result = "The hearing is postponed for seven days. Available members will attend at the capital.";
            return true;
        }

        public bool ReconveneBusiness(string businessId, Hero actor, out string result)
        {
            var record = AuthorizedBusiness(businessId, actor, out result);
            if (record == null || record.Status != "postponed") return false;
            record.Status = "hearing";
            ChangedBusiness(record);
            result = "The hearing has reconvened; the recess cannot be repeated.";
            return true;
        }

        public bool VoteBusiness(string businessId, Hero actor, out string result)
        {
            var record = AuthorizedBusiness(businessId, actor, out result);
            if (record == null) return false;
            if (!GovernmentBusinessRules.CanVote(record.Status))
            { result = "This matter is not awaiting a vote."; return false; }
            if (!ValidateNativeBusiness(record, out result)) return false;
            Kingdom kingdom = FindKingdom(record.KingdomStringId);
            ReignGovernmentStateRecord state = EnsureGovernment(kingdom);
            var options = BusinessOptions(record);
            var ballots = new JArray();
            foreach (var seat in GetSeats(record.KingdomStringId))
            {
                Hero member = FindHero(seat.HeroStringId);
                if (member == null || !member.IsAlive) continue;
                var selected = options.OrderByDescending(x => BusinessOptionScore(record, seat, member, x, true))
                    .ThenBy(x => x.Value<string>("id"), StringComparer.Ordinal).FirstOrDefault();
                if (selected == null) continue;
                var attendance = GetBusinessAttendance(record.BusinessId).FirstOrDefault(x => Same(x.HeroStringId, member.StringId));
                ballots.Add(new JObject { ["memberHeroId"] = member.StringId, ["partyId"] = seat.PartyId,
                    ["optionId"] = selected.Value<string>("id"), ["absentee"] = attendance == null || attendance.Status != "present" });
            }
            string winner = GovernmentBusinessRules.SelectWinner(ballots.OfType<JObject>().Select(x => x.Value<string>("optionId")),
                options.Select(x => x.Value<string>("id")), record.StatusQuoOptionId);
            if (string.IsNullOrEmpty(winner))
            { result = "There are no eligible voting members. The hearing remains pending until representation is restored."; return false; }
            record.VotesJson = ballots.ToString(Formatting.None);
            record.WinningOptionId = winner;
            if (state.Level == 5) return ExecuteBusiness(record, winner, false, out result);
            bool opposed = !string.IsNullOrEmpty(record.RecommendedOptionId) && !Same(winner, record.RecommendedOptionId);
            record.Status = state.Level == 3 && opposed ? "reconsideration" : "awaiting_ruler";
            if (record.Status == "reconsideration") record.ReconsiderUntilDay = record.IsUrgent ? CurrentDay() : CurrentDay() + 7f;
            ChangedBusiness(record);
            result = record.Status == "reconsideration"
                ? record.IsUrgent ? "Members oppose the recommendation. Reconsider now, or explicitly uphold it with consequences."
                    : "Members oppose the recommendation. The seven-day reconsideration period has begun."
                : "Individual votes are recorded. The ruler may now respond under the current authority level.";
            return true;
        }

        public bool ResolveBusiness(string businessId, string optionId, Hero actor, bool overrideOpposition, out string result)
        {
            var record = AuthorizedBusiness(businessId, actor, out result);
            if (record == null) return false;
            var state = GetGovernment(record.KingdomStringId);
            if (state == null || state.Level == 5)
            { result = "Under binding government authority the vote decides; the ruler cannot replace it."; return false; }
            if (record.Status != "awaiting_ruler" && record.Status != "reconsideration")
            { result = "Record the government vote before making the ruler's decision."; return false; }
            bool opposed = !Same(record.WinningOptionId, optionId);
            if (opposed && state.Level == 3 && record.Status == "awaiting_ruler")
            {
                record.Status = "reconsideration";
                record.ReconsiderUntilDay = record.IsUrgent ? CurrentDay() : CurrentDay() + 7f;
                ChangedBusiness(record);
                result = record.IsUrgent ? "Reconsider the government's objection before explicitly upholding this decision."
                    : "The government opposed this outcome. Its seven-day reconsideration period has begun.";
                return false;
            }
            if (opposed && state.Level >= 3 && !overrideOpposition)
            { result = "Upholding an opposed decision requires an explicit override."; return false; }
            if (opposed && state.Level == 3 && CurrentDay() < record.ReconsiderUntilDay)
            { result = "The reconsideration period has not ended."; return false; }
            return ExecuteBusiness(record, optionId, opposed, out result);
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
            foreach (var record in _business.Where(x => !GovernmentBusinessRules.IsClosed(x.Status)).ToList())
            {
                ObserveGovernmentActionQueue(record);
                if (GovernmentBusinessRules.IsClosed(record.Status)) continue;
                Kingdom kingdom = FindKingdom(record.KingdomStringId);
                if (kingdom == null || !IsEligibleKingdom(kingdom))
                { CloseInvalidBusiness(record, "The kingdom no longer has an eligible government."); continue; }
                RefreshBusinessRuler(record, kingdom);
                ObserveCounterpartBusiness(record);
                if (record.Status == "awaiting_sponsorship")
                {
                    FindBusinessSponsor(record);
                    if (record.Status == "awaiting_sponsorship" && CurrentDay() >= record.CreatedDay + GovernmentBusinessRules.PetitionExpiryDays)
                    { record.Status = "expired"; record.Outcome = "The petition expired without sponsorship; no ruler refusal occurred."; NotifyGovernmentBusinessClosed(record); ChangedBusiness(record); }
                }
                if (record.Status == "postponed" && CurrentDay() >= record.RecessUntilDay)
                { record.Status = "hearing"; ChangedBusiness(record); }
                if (kingdom.Leader == null || kingdom.Leader == Hero.MainHero) continue;
                if (record.Status == "hearing")
                {
                    // NPC rulers recommend independently; members still cast their own ballots.
                    var options = BusinessOptions(record);
                    var decision = NativeBusinessDecision(record);
                    var choice = options.OrderByDescending(x => NativeClanPreference(decision, kingdom.RulingClan, x))
                        .ThenBy(x => x.Value<string>("id"), StringComparer.Ordinal).FirstOrDefault();
                    if (choice == null) continue;
                    record.RecommendedOptionId = choice.Value<string>("id");
                    record.RecommendationRulerHeroStringId = kingdom.Leader.StringId;
                    VoteBusiness(record.BusinessId, kingdom.Leader, out _);
                }
                if (record.Status == "awaiting_ruler" || record.Status == "reconsideration" && CurrentDay() >= record.ReconsiderUntilDay)
                {
                    var state = GetGovernment(kingdom);
                    bool overrideVote = state.Level < 5 && !Same(record.WinningOptionId, record.RecommendedOptionId)
                        && (kingdom.Leader.GetTraitLevel(DefaultTraits.Calculating) + kingdom.Leader.GetTraitLevel(DefaultTraits.Valor)) > 1;
                    ResolveBusiness(record.BusinessId, overrideVote ? record.RecommendedOptionId : record.WinningOptionId,
                        kingdom.Leader, overrideVote, out _);
                }
            }
        }

        private void RefreshBusinessRuler(ReignGovernmentBusinessRecord record, Kingdom kingdom)
        {
            if (record.AuthorityLevelAtDecision != 0 || string.IsNullOrEmpty(record.RecommendationRulerHeroStringId)
                || Same(record.RecommendationRulerHeroStringId, kingdom?.Leader?.StringId)) return;
            record.RecommendedOptionId = string.Empty;
            record.RecommendationRulerHeroStringId = string.Empty;
            ChangedBusiness(record);
        }

        private void FindBusinessSponsor(ReignGovernmentBusinessRecord record)
        {
            if (string.IsNullOrEmpty(record.PetitionerHeroStringId)) { record.Status = "hearing"; return; }
            Kingdom kingdom = FindKingdom(record.KingdomStringId);
            Hero petitioner = FindHero(record.PetitionerHeroStringId);
            if (petitioner == null || !petitioner.IsAlive) { CloseInvalidBusiness(record, "The petitioner is no longer available."); return; }
            var seats = GetSeats(record.KingdomStringId);
            if (petitioner == kingdom?.Leader) { record.Status = "hearing"; return; }
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
                    return new GovernmentSponsorCandidate { HeroId = seat.HeroStringId, Eligible = member?.IsAlive == true,
                        IssueSupport = support, PetitionerRelation = member?.GetRelation(petitioner) ?? 0 };
                }));
            }
            if (!string.IsNullOrEmpty(record.SponsorHeroStringId))
            {
                record.Status = "hearing";
                record.SponsorReason = "A seated representative supports the requested outcome and has agreed to introduce it.";
                ChangedBusiness(record);
            }
        }

        private int BusinessOptionScore(ReignGovernmentBusinessRecord record, ReignGovernmentSeatRecord seat, Hero member, JObject option, bool includeRecommendation)
        {
            Kingdom kingdom = FindKingdom(record.KingdomStringId);
            string optionId = option.Value<string>("id");
            bool affirmative = option.Value<bool?>("affirmative") ?? false;
            var planks = ParsePlanks(FindParty(record.KingdomStringId, seat.PartyId)?.PlanksCsv);
            int alignment = ActionPlankAlignment(MapNativeDecisionKind(NativeBusinessDecision(record)), planks);
            int interest = 0;
            int personality = 0;
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
            return GovernmentBusinessRules.OptionScore(interest, alignment,
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
