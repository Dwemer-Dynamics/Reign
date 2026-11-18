using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Reign.Core.Contracts.Court;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;

namespace ReignBeta.Court
{
    public sealed partial class ReignCourtCampaignBehavior
    {
        public ReignCourtLifeMatter TryCreateNobleVisitorMatterForSlot(string campaignId, string timelineId, int day, int slot, string requestedTemplateId = null)
        {
            Kingdom kingdom = Clan.PlayerClan?.Kingdom;
            Settlement host = FindCurrentCapital();
            Hero ruler = Hero.MainHero;
            if (kingdom == null || host == null || ruler == null || kingdom.Leader != ruler || host.IsUnderSiege) return null;
            NormalizeNobleVisitorState();
            string seed = campaignId + "|" + timelineId + "|visitors|" + day + "|" + slot;
            var candidates = Hero.AllAliveHeroes.Where(h => IsEligibleNobleVisitor(h, kingdom))
                .OrderBy(h => ReignCourtLifeRules.StableRoll(seed + h.StringId, int.MaxValue)).ToList();
            if (candidates.Count == 0) return null;
            int kind = ReignCourtLifeRules.StableRoll(seed + "|kind", 4);
            if (!string.IsNullOrWhiteSpace(requestedTemplateId))
            {
                if (requestedTemplateId == "visitor-family-introduction") kind = 0;
                else if (requestedTemplateId == "visitor-solo-introduction") kind = 1;
                else if (requestedTemplateId == "visitor-courtesy") kind = 2;
                else return null;
            }
            Hero candidate = null, initiator = null;
            var group = new List<Hero>();
            if (kind == 0)
            {
                candidate = candidates.FirstOrDefault(h => IsNobleVisitorRomanticCandidate(h, ruler)
                    && candidates.Any(p => p == h.Father || p == h.Mother));
                if (candidate != null)
                {
                    List<Hero> parents = candidates.Where(p => p == candidate.Father || p == candidate.Mother).Take(2).ToList();
                    initiator = parents[0]; group.AddRange(parents); group.Add(candidate);
                    if (ReignCourtLifeRules.StableRoll(seed + "|sibling", 2) == 0)
                    {
                        Hero sibling = candidates.FirstOrDefault(h => h != candidate && !group.Contains(h)
                            && parents.Any(p => h.Father == p || h.Mother == p));
                        if (sibling != null && group.Count < ReignNobleVisitorRules.MaximumVisitors) group.Add(sibling);
                    }
                }
            }
            if (group.Count == 0 && requestedTemplateId == "visitor-family-introduction") return null;
            if (group.Count == 0)
            {
                initiator = kind == 1 ? candidates.FirstOrDefault(h => IsNobleVisitorRomanticCandidate(h, ruler)) : null;
                candidate = initiator;
                if (candidate == null && requestedTemplateId == "visitor-solo-introduction") return null;
                if (initiator == null) { initiator = candidates[0]; candidate = null; }
                group.Add(initiator);
            }
            string id = "noble_visit_" + ReignCourtTerms.Hash(seed);
            if (EnsureRulerDocketState().NobleVisitorStays.Any(s => s.StayId == id)) return null;
            int duration = ReignNobleVisitorRules.StayDays(ReignCourtLifeRules.StableRoll(seed + "|stay", 5));
            double arrival = CurrentCourtLifeDay();
            bool matchmaking = group.Count > 1 && candidate != null;
            string purpose = matchmaking ? "Introduce an adult child to the ruler while paying a respectful family visit."
                : ReignNobleVisitorRules.Purpose(ReignCourtLifeRules.StableRoll(seed + "|purpose", 6));
            var stay = new ReignNobleVisitorStay { StayId = id, MatterId = id + "_introduction", CampaignId = campaignId, TimelineId = timelineId,
                ReignId = CurrentReignId(), RulerHeroId = ruler.StringId, HostKingdomId = kingdom.StringId, HostSettlementId = host.StringId,
                Purpose = purpose, CandidateHeroId = candidate?.StringId ?? string.Empty, InitiatorHeroId = initiator.StringId,
                IsMatchmaking = matchmaking, IsSoloRomanticApproach = !matchmaking && candidate != null,
                ArrivalDay = arrival, DepartureDay = arrival + duration, StayDays = duration };
            var matter = new ReignCourtLifeMatter { MatterId = stay.MatterId, CampaignId = campaignId, TimelineId = timelineId, ReignId = stay.ReignId,
                Source = ReignDocketSource.Visitor, TemplateId = matchmaking ? "visitor-family-introduction" : candidate != null ? "visitor-solo-introduction" : "visitor-courtesy",
                Title = group.Count > 1 ? "A Visiting Household" : "A Noble Visitor", Summary = purpose + " Planned stay: " + duration + " days.",
                ReceivedDay = day, AvailableDay = arrival, ExpiresDay = stay.DepartureDay, State = ReignCourtLifeMatterState.Pending,
                SettlementId = host.StringId, KingdomId = kingdom.StringId };
            foreach (Hero hero in group)
            {
                string role = matchmaking ? (hero == candidate ? "adult_candidate" : hero == candidate.Father || hero == candidate.Mother ? "parent" : "adult_sibling")
                    : candidate != null ? "independent_visitor" : "courtesy_visitor";
                stay.Members.Add(new ReignNobleVisitorMember { HeroId = hero.StringId, Name = hero.Name?.ToString() ?? hero.StringId,
                    Role = role, HomeSettlementId = (hero.HomeSettlement ?? hero.Clan?.InitialHomeSettlement ?? hero.CurrentSettlement)?.StringId ?? string.Empty,
                    OriginalSettlementId = hero.CurrentSettlement?.StringId ?? string.Empty });
                matter.Participants.Add(new ReignCourtLifeParticipant { ActorId = hero.StringId, HeroId = hero.StringId, Name = hero.Name?.ToString() ?? hero.StringId,
                    Role = role, Age = hero.Age, IsFemale = hero.IsFemale,
                    PrivateContext = "You are an actual noble visiting for " + duration + " days. Announce that planned stay during introductions. "
                        + purpose + " Your role is " + role + ". Behave according to your own profile and the shared family context. "
                        + "The server's personality and romance eligibility rules determine private-meeting initiative. No meeting or romance has happened merely because it was suggested." });
            }
            matter.PayloadJson = NobleVisitorContext(stay, ruler, null).ToString(Formatting.None);
            matter.Options.Add(new ReignCourtLifeOption { OptionId = "welcome", Label = "Welcome the visitors", Description = "Conclude the introduction. The visitors remain available during their announced stay." });
            matter.Options.Add(new ReignCourtLifeOption { OptionId = "conclude", Label = "Conclude the audience", Description = "End the introduction without changing the visitors' residence." });
            EnsureRulerDocketState().NobleVisitorStays.Add(stay);
            // Publish a visit only after the complete group is physically at court.
            if (!TryPlaceNobleVisitors(stay, matter, host, arrival))
            {
                DepartNobleVisitors(stay, matter, "arrival_failed", arrival);
                return null;
            }
            return matter;
        }

        public bool IsNobleVisitorReserved(Hero hero)
        {
            if (hero == null) return false;
            return (_rulerDocketState?.NobleVisitorStays ?? new List<ReignNobleVisitorStay>()).Any(x => x != null && !x.Departed
                && (x.Members ?? new List<ReignNobleVisitorMember>()).Any(m => m.HeroId == hero.StringId && !m.Released));
        }

        public void OnCanNobleVisitorMoveToSettlement(Hero hero, ref bool result)
        {
            if (result && IsNobleVisitorReserved(hero)) result = false;
        }

        private bool IsEligibleNobleVisitor(Hero hero, Kingdom host, string ownStayId = "")
        {
            if (hero == null || hero.Clan?.Kingdom == null || host == null) return false;
            bool reserved = (EnsureRulerDocketState().NobleVisitorStays ?? new List<ReignNobleVisitorStay>()).Any(s => s != null && s.StayId != ownStayId
                && !s.Departed && s.Members.Any(m => !m.Released && m.HeroId == hero.StringId));
            bool assigned = reserved || _ambassadors.Any(a => a.HeroStringId == hero.StringId && a.Status != "recalled")
                || _foreignAmbassadors.Any(a => a.IsActive && a.HeroStringId == hero.StringId)
                || EnsureRulerDocketState().NobleMatters.Any(m => m.IsPending && m.Participants.Any(p => p.HeroId == hero.StringId))
                || EnsureRulerDocketState().CourtStayLeases.Any(l => !l.Released && l.HeroId == hero.StringId)
                || _regents.Any(r => r.IsActive && r.HeroStringId == hero.StringId);
            var campaign = TaleWorlds.CampaignSystem.Campaign.Current;
            bool quest = hero.Issue != null || campaign?.QuestManager?.IsQuestGiver(hero) == true
                || campaign?.QuestManager?.TrackedObjects?.ContainsKey(hero) == true
                || campaign?.IssueManager?.IssueSolvingCompanionList?.Contains(hero) == true;
            bool officer = _offices.Any(o => o.IsActive && o.HeroStringId == hero.StringId)
                || (!Chancellor.IsVacant && Chancellor.HeroId == hero.StringId);
            return ReignNobleVisitorRules.Eligible(hero.IsLord, hero.IsAlive, hero.IsActive && !hero.IsDisabled, hero.IsPrisoner,
                hero.Age, Math.Max(18, campaign?.Models.AgeModel.HeroComesOfAge ?? 18), host.IsAtWarWith(hero.Clan.Kingdom),
                hero.PartyBelongedTo != null || hero.PartyBelongedToAsPrisoner != null || hero.IsTraveling,
                hero.GovernorOf != null, officer, assigned, quest, hero.Clan.Kingdom.Leader == hero || hero == Hero.MainHero)
                && hero != Hero.MainHero?.Spouse && !(Hero.MainHero?.Children.Contains(hero) ?? false);
        }

        private static bool IsNobleVisitorRomanticCandidate(Hero hero, Hero ruler)
        {
            if (hero == null || ruler == null || hero.Age < 18 || ruler.Age < 18 || hero.IsFemale == ruler.IsFemale || hero.Spouse != null) return false;
            // Family introductions never recruit a close relative as a romantic candidate.
            var ancestors = new HashSet<Hero>(VisitorAncestors(ruler));
            if (ancestors.Contains(hero) || VisitorAncestors(hero).Contains(ruler)) return false;
            return !VisitorAncestors(hero).Any(ancestors.Contains);
        }

        private static IEnumerable<Hero> VisitorAncestors(Hero hero)
        {
            if (hero == null) return Enumerable.Empty<Hero>();
            return new[] { hero.Father, hero.Mother, hero.Father?.Father, hero.Father?.Mother, hero.Mother?.Father, hero.Mother?.Mother }
                .Where(x => x != null).Distinct();
        }

        public void ProcessNobleVisitorStays(double now)
        {
            NormalizeNobleVisitorState();
            foreach (ReignNobleVisitorStay stay in EnsureRulerDocketState().NobleVisitorStays.Where(x => !x.Departed).ToList())
            {
                Settlement host = Settlement.Find(stay.HostSettlementId);
                Kingdom kingdom = Kingdom.All.FirstOrDefault(k => k.StringId == stay.HostKingdomId);
                ReignCourtLifeMatter matter = EnsureRulerDocketState().CourtLifeMatters.FirstOrDefault(m => m.MatterId == stay.MatterId);
                if (!string.IsNullOrEmpty(stay.DepartureReason))
                { DepartNobleVisitors(stay, matter, stay.DepartureReason, now); continue; }
                string invalid = host == null || kingdom == null || host.IsUnderSiege || host.OwnerClan?.Kingdom != kingdom ? "host_unavailable"
                    : FindCurrentCapital()?.StringId != stay.HostSettlementId || Hero.MainHero?.StringId != stay.RulerHeroId
                        || kingdom.Leader != Hero.MainHero ? "court_changed"
                    : stay.Members.Any(m => !IsEligibleNobleVisitor(FindHero(m.HeroId), kingdom, stay.StayId)) ? "visitor_assignment_or_status_changed" : string.Empty;
                if (!string.IsNullOrEmpty(invalid) || (stay.Arrived && now >= stay.DepartureDay))
                { DepartNobleVisitors(stay, matter, string.IsNullOrEmpty(invalid) ? "planned_departure" : invalid, now); continue; }
                // Legacy saves may contain a queued visit. Revalidate and place it now;
                // its former scheduled travel date no longer delays the visit.
                if (!TryPlaceNobleVisitors(stay, matter, host, now))
                    DepartNobleVisitors(stay, matter, "arrival_failed", now);
            }
        }

        private bool TryPlaceNobleVisitors(ReignNobleVisitorStay stay, ReignCourtLifeMatter matter, Settlement host, double now)
        {
            try
            {
                foreach (ReignNobleVisitorMember member in stay.Members)
                {
                    Hero hero = FindHero(member.HeroId);
                    if (hero?.CurrentSettlement != host) TeleportPartylessHero(hero, host);
                    member.Arrived = hero != null && hero.CurrentSettlement == host;
                }
                if (stay.Members.Count == 0 || stay.Members.Any(x => !x.Arrived))
                { stay.LastError = "Native arrival did not establish every visitor at the court."; return false; }
                if (!stay.Arrived)
                {
                    stay.Arrived = true;
                    stay.ArrivalDay = stay.ActualArrivalDay = now;
                    stay.DepartureDay = now + stay.StayDays;
                    if (matter != null)
                    { matter.AvailableDay = now; matter.ExpiresDay = stay.DepartureDay; matter.State = ReignCourtLifeMatterState.Pending; }
                    StateChanged?.Invoke();
                }
                stay.LastError = string.Empty;
                if (matter != null) matter.PayloadJson = NobleVisitorContext(stay, Hero.MainHero, null).ToString(Formatting.None);
                return true;
            }
            catch (Exception ex)
            {
                // Native movement may have succeeded before an event callback failed.
                // Capture every actual move so cancellation can return those members.
                foreach (ReignNobleVisitorMember member in stay.Members)
                    member.Arrived = member.Arrived || FindHero(member.HeroId)?.CurrentSettlement == host;
                stay.LastError = "Visitor movement failed: " + ex.Message;
                return false;
            }
        }

        private void DepartNobleVisitors(ReignNobleVisitorStay stay, ReignCourtLifeMatter matter, string reason, double now)
        {
            // Persist cancellation before returning members; a failed return must never
            // turn back into an arrival attempt after a retry or reload.
            stay.DepartureReason = reason;
            foreach (ReignNobleVisitorMember member in stay.Members.Where(x => !x.Released))
            {
                Hero hero = FindHero(member.HeroId);
                if (hero == null || !hero.IsAlive || !hero.IsActive || hero.IsPrisoner || hero.PartyBelongedTo != null || hero.GovernorOf != null || hero.IsTraveling)
                { member.Released = true; member.ReleaseReason = "native_status_retained"; continue; }
                if (!member.Arrived) { member.Released = true; member.ReleaseReason = "arrival_cancelled"; continue; }
                // External assignments win. Do not move a newly appointed officer or a quest-bound visitor home.
                var campaign = TaleWorlds.CampaignSystem.Campaign.Current;
                if (_offices.Any(o => o.IsActive && o.HeroStringId == hero.StringId)
                    || (!Chancellor.IsVacant && Chancellor.HeroId == hero.StringId)
                    || _regents.Any(r => r.IsActive && r.HeroStringId == hero.StringId)
                    || _foreignAmbassadors.Any(a => a.IsActive && a.HeroStringId == hero.StringId)
                    || _ambassadors.Any(a => a.Status != "recalled" && a.HeroStringId == hero.StringId)
                    || campaign?.QuestManager?.TrackedObjects?.ContainsKey(hero) == true || hero.Issue != null
                    || campaign?.IssueManager?.IssueSolvingCompanionList?.Contains(hero) == true)
                { member.Released = true; member.ReleaseReason = "external_assignment_retained"; continue; }
                Kingdom origin = hero.Clan?.Kingdom;
                Settlement target = Settlement.Find(member.OriginalSettlementId);
                if (!IsSafeNobleVisitorReturn(target, origin)) target = Settlement.Find(member.HomeSettlementId);
                if (!IsSafeNobleVisitorReturn(target, origin))
                    target = ClosestFriendlyFortification(hero.CurrentSettlement, origin);
                if (target == null)
                { member.Released = true; member.ReleaseReason = "no_safe_home_ai_released"; continue; }
                try
                {
                    TeleportPartylessHero(hero, target);
                    if (hero.CurrentSettlement != target) { stay.LastError = "Native return has not reached a safe home."; continue; }
                    member.ReturnSettlementId = target.StringId; member.Released = true; member.ReleaseReason = reason;
                }
                catch (Exception ex) { stay.LastError = "Native visitor return failed: " + ex.Message; }
            }
            if (stay.Members.Any(x => !x.Released)) return;
            stay.Departed = true; stay.DepartureReason = reason; stay.LastError = string.Empty;
            if (matter != null && matter.IsPending) matter.State = reason == "planned_departure" ? ReignCourtLifeMatterState.Expired : ReignCourtLifeMatterState.Invalidated;
            EnsureRulerDocketState().History.Add(new ReignDocketHistoryRecord { RecordId = stay.StayId + "_departure", ReignId = stay.ReignId,
                Type = "noble_visitor_departure", Outcome = reason, PetitionId = stay.MatterId, Day = (int)Math.Floor(now),
                PetitionerName = string.Join(", ", stay.Members.Select(x => x.Name)), SettlementId = stay.HostSettlementId,
                Summary = "The visiting household's stay ended. Its members were released to their current native duties or returned to safe homes.",
                Detail = JsonConvert.SerializeObject(stay) });
            StateChanged?.Invoke();
        }

        private static bool IsSafeNobleVisitorReturn(Settlement target, Kingdom origin)
            => target != null && !target.IsUnderSiege && origin != null && target.OwnerClan?.Kingdom != null
                && !origin.IsAtWarWith(target.OwnerClan.Kingdom);

        public bool ApplyNobleVisitorDecision(ReignCourtLifeMatter matter, JObject decision, out string summary)
        {
            summary = string.Empty;
            if (matter == null || matter.Source != ReignDocketSource.Visitor || decision == null) return false;
            if (matter.EffectsCommitted) { summary = matter.DecisionSummary; return true; }
            if (!HasRoyalCommandAccess || !AreNobleVisitorsAvailable(matter, out summary)) return false;
            ReignNobleVisitorStay stay = EnsureRulerDocketState().NobleVisitorStays.First(x => x.MatterId == matter.MatterId);
            string option = decision.Value<string>("optionId") ?? string.Empty;
            if (option != "welcome" && option != "conclude") return false;
            summary = "The introduction concluded. The visitors remain available until campaign day " + stay.DepartureDay.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + ".";
            matter.State = ReignCourtLifeMatterState.Resolved; matter.EffectsCommitted = true;
            matter.ResolutionReceiptId = stay.StayId + "_introduced"; matter.DecisionSummary = summary;
            StateChanged?.Invoke(); return true;
        }

        public bool AreNobleVisitorsAvailable(ReignCourtLifeMatter matter, out string reason)
        {
            reason = string.Empty;
            NormalizeNobleVisitorState();
            ReignNobleVisitorStay stay = EnsureRulerDocketState().NobleVisitorStays.FirstOrDefault(x => x.MatterId == matter?.MatterId);
            if (stay == null || !stay.Arrived || stay.Departed || !string.IsNullOrEmpty(stay.DepartureReason)
                || CurrentCourtLifeDay() >= stay.DepartureDay)
            { reason = "These visitors are not currently available at court."; return false; }
            if (stay.Members.Any(m => m.Released || FindHero(m.HeroId)?.CurrentSettlement?.StringId != stay.HostSettlementId))
            { reason = "Every member of this visiting group must be present before the audience begins."; return false; }
            return true;
        }

        public JObject BuildNobleVisitorConversationContext(string heroId)
        {
            NormalizeNobleVisitorState();
            ReignNobleVisitorStay stay = EnsureRulerDocketState().NobleVisitorStays.FirstOrDefault(x => !x.Departed && x.Arrived
                && string.IsNullOrEmpty(x.DepartureReason)
                && x.Members.Any(m => m.HeroId == heroId && !m.Released));
            return stay == null ? new JObject() : NobleVisitorContext(stay, Hero.MainHero, heroId);
        }

        public bool RecordNobleVisitorInvitation(string matterId, string heroId, string turnId, string agreedWords, bool npcAccepted)
        {
            if (string.IsNullOrWhiteSpace(turnId) || string.IsNullOrWhiteSpace(agreedWords) || !npcAccepted) return false;
            NormalizeNobleVisitorState();
            ReignNobleVisitorStay stay = EnsureRulerDocketState().NobleVisitorStays.FirstOrDefault(x => x.MatterId == matterId
                && !x.Departed && x.Arrived && string.IsNullOrEmpty(x.DepartureReason)
                && CurrentCourtLifeDay() < x.DepartureDay && x.Members.Any(m => m.HeroId == heroId && !m.Released));
            Hero hero = FindHero(heroId);
            if (stay == null || hero == null || !hero.IsAlive || hero.IsPrisoner || hero.Age < Math.Max(18, TaleWorlds.CampaignSystem.Campaign.Current?.Models.AgeModel.HeroComesOfAge ?? 18)) return false;
            string id = stay.StayId + "_invitation_" + heroId + "_" + turnId;
            if (stay.Invitations.Any(x => x.InvitationId == id)) return true;
            stay.Invitations.Add(new ReignNobleVisitorInvitation { InvitationId = id, HeroId = heroId, TurnId = turnId,
                AgreedWords = agreedWords.Length > 1200 ? agreedWords.Substring(0, 1200) : agreedWords, NpcAccepted = true, CreatedDay = CurrentCourtLifeDay() });
            EnsureRulerDocketState().PendingMemoryJobs.Add(new ReignDocketMemoryJob { JobId = id, HeroId = heroId,
                EventType = "noble_visitor_invitation", Summary = "During the court visit, a later conversation was agreed: " + agreedWords
                    + " This is a remembered invitation only; it does not establish a completed private meeting or romantic consent.", WorldDay = CurrentDay() });
            StateChanged?.Invoke(); return true;
        }

        private static JObject NobleVisitorContext(ReignNobleVisitorStay stay, Hero ruler, string heroId)
        {
            return new JObject { ["schema"] = "reign-noble-visitor-context-v1", ["stayId"] = stay.StayId, ["matterId"] = stay.MatterId,
                ["enabled"] = !stay.Departed, ["purpose"] = stay.Purpose, ["stayDays"] = stay.StayDays, ["departureDay"] = stay.DepartureDay,
                ["isMatchmaking"] = stay.IsMatchmaking, ["isSoloRomanticApproach"] = stay.IsSoloRomanticApproach,
                ["candidateHeroId"] = stay.CandidateHeroId, ["initiatorHeroId"] = stay.InitiatorHeroId,
                ["conversationHeroId"] = heroId ?? string.Empty, ["rulerHeroId"] = stay.RulerHeroId, ["rulerMarried"] = ruler?.Spouse != null,
                ["rulerIsFemale"] = ruler?.IsFemale ?? false,
                ["romanceEligibleNow"] = IsNobleVisitorRomanticCandidate(FindHero(stay.CandidateHeroId), ruler),
                ["members"] = new JArray(stay.Members.Select(m => new JObject { ["heroId"] = m.HeroId, ["name"] = m.Name, ["role"] = m.Role,
                    ["age"] = FindHero(m.HeroId)?.Age ?? 0, ["isFemale"] = FindHero(m.HeroId)?.IsFemale ?? false })),
                ["agreedInvitations"] = new JArray(stay.Invitations.Where(i => heroId == null || i.HeroId == heroId).Select(i => JObject.FromObject(i))),
                ["noAutomaticPrivateMeeting"] = true, ["noAutomaticRomanticConsent"] = true };
        }

        private void NormalizeNobleVisitorState()
        {
            ReignRulerDocketState state = EnsureRulerDocketState();
            state.NobleVisitorStays = (state.NobleVisitorStays ?? new List<ReignNobleVisitorStay>()).Where(x => x != null).ToList();
            foreach (ReignNobleVisitorStay s in state.NobleVisitorStays)
            {
                s.Members = (s.Members ?? new List<ReignNobleVisitorMember>()).Where(x => x != null).GroupBy(x => x.HeroId).Select(x => x.First()).Take(4).ToList();
                s.Invitations = (s.Invitations ?? new List<ReignNobleVisitorInvitation>()).Where(x => x != null).GroupBy(x => x.InvitationId).Select(x => x.First()).ToList();
            }
        }
    }
}
