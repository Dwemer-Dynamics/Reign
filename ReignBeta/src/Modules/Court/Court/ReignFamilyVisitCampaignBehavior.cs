using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Reign.Core.Contracts.Court;
using ReignBeta.Campaign;
using ReignBeta.Integration;
using ReignBeta.Runtime;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;

namespace ReignBeta.Court
{
    public sealed partial class ReignRulerDocketState
    {
        public string FamilyAttentionMirrorJson { get; set; } = "{}";
        public List<string> FamilyVisitOutbox { get; set; } = new List<string>();
    }

    public sealed partial class ReignCourtCampaignBehavior
    {
        private volatile bool _familyVisitSyncInFlight;
        private double _lastFamilyVisitSyncDay = -1;
        private string _familyVisitSyncIdentity = "";

        public bool HasPendingFamilyVisitWork => _familyVisitSyncInFlight || (EnsureRulerDocketState().FamilyVisitOutbox?.Count ?? 0) > 0;

        public int? GetFamilyDirectionalRelation(Hero hero)
        {
            if (hero == null || !hero.IsChild) return null;
            var row = FamilyMirror()["members"]?.OfType<JObject>().FirstOrDefault(x => x.Value<string>("hero_id") == hero.StringId);
            return row?.Value<int?>("directional_relation");
        }

        public bool HasUnmigratedChildAttention(Hero hero) => hero != null && (FamilyMirror()["members"]?.OfType<JObject>()
            .Any(x => x.Value<string>("hero_id") == hero.StringId && x.Value<int?>("child_relation_owned") == 1 && x.Value<int?>("adult_migrated") == 0) ?? false);

        private JObject FamilyMirror()
        {
            try
            {
                JObject mirror = JObject.Parse(EnsureRulerDocketState().FamilyAttentionMirrorJson ?? "{}");
                return MatchesCurrentFamilyScope(mirror) ? mirror : new JObject();
            }
            catch { return new JObject(); }
        }

        private static bool MatchesCurrentFamilyScope(JObject snapshot) => snapshot != null
            && snapshot.Value<string>("playerId") == Hero.MainHero?.StringId
            && snapshot.Value<string>("campaignId") == ReignCampaignIdentity.CurrentCampaignId()
            && snapshot.Value<string>("timelineId") == (ReignWorldHistoryCampaignBehavior.Instance?.TimelineId ?? "main");

        private static bool FamilyLocallyPresent(Hero hero)
        {
            Settlement court = Instance?.CurrentCapital;
            return hero != null && (hero.PartyBelongedTo == MobileParty.MainParty
                || (court != null && (hero.CurrentSettlement == court || hero.PartyBelongedTo?.CurrentSettlement == court)));
        }

        private static List<Hero> RulerFamily()
        {
            var player = Hero.MainHero;
            if (player == null) return new List<Hero>();
            return (player.Children ?? new List<Hero>()).Concat(new[] { player.Spouse })
                .Where(x => x != null).GroupBy(x => x.StringId).Select(x => x.First()).ToList();
        }

        private static JObject FamilyVisitProfile(Hero hero)
        {
            return new JObject { ["heroStringId"] = hero.StringId, ["name"] = hero.Name?.ToString() ?? hero.StringId,
                ["age"] = hero.Age, ["isFemale"] = hero.IsFemale, ["isChild"] = hero.IsChild,
                ["isAlive"] = hero.IsAlive, ["isPrisoner"] = hero.IsPrisoner, ["isLocal"] = FamilyLocallyPresent(hero),
                ["spouseId"] = hero.Spouse?.StringId ?? "", ["fatherId"] = hero.Father?.StringId ?? "", ["motherId"] = hero.Mother?.StringId ?? "",
                ["relationToPlayer"] = hero.GetRelation(Hero.MainHero) };
        }

        private static JObject FamilyCommand(string campaign, string timeline, string action, double now)
        {
            return new JObject { ["campaignId"] = campaign, ["timelineId"] = timeline, ["action"] = action,
                ["worldDay"] = now, ["playerId"] = Hero.MainHero?.StringId ?? "" };
        }

        public ReignCourtLifeMatter TryCreateFamilyVisitMatterForSlot(string campaignId, string timelineId, int day, int slot,
            string requestedHeroId = null, string requestedTemplateId = null)
        {
            var state = EnsureRulerDocketState();
            var members = FamilyMirror()["members"]?.OfType<JObject>().ToList() ?? new List<JObject>();
            var choices = RulerFamily().Where(x => x.IsAlive && !x.IsPrisoner && x.Age >= ReignFamilyVisitRules.MinimumVisitorAge && FamilyLocallyPresent(x)
                && members.Any(row => row.Value<string>("hero_id") == x.StringId && row.Value<int?>("neglected") == 0)
                && !state.CourtLifeMatters.Any(m => m.Source == ReignDocketSource.Family && m.IsPending && m.Participants.Any(p => p.HeroId == x.StringId)))
                .OrderBy(x => FamilyStableSeed(campaignId + "|" + timelineId + "|" + day + "|" + slot + "|" + x.StringId)).ToList();
            Hero hero = string.IsNullOrWhiteSpace(requestedHeroId) ? choices.FirstOrDefault() : choices.FirstOrDefault(x => x.StringId == requestedHeroId);
            if (hero == null) return null;
            double now = CampaignTime.Now.ToDays;
            var member = members.First(row => row.Value<string>("hero_id") == hero.StringId);
            string[] plots = hero == Hero.MainHero.Spouse
                ? new[] { "share household news", "suggest some private time together later", "ask for reassurance", "recall a shared family memory", "discuss a personal worry", "invite a quiet walk later" }
                : hero.Age < 9f ? new[] { "show a harmless insect discovered nearby", "show an unusual stone", "share a drawing", "ask a curious question about the world", "proudly describe a small new skill", "retell a little adventure in the household" }
                : hero.IsChild ? new[] { "ask to watch the arena later with the ruler", "ask to watch training", "share a learning achievement", "ask advice about a friendship", "ask a question about growing up", "suggest spending time together later" }
                : new[] { "seek personal advice", "share news about a friendship", "recall a family memory", "suggest an outing later", "discuss a personal ambition", "ask for time with their parent" };
            int pick = (int)(FamilyStableSeed(campaignId + "|" + timelineId + "|" + day + "|" + slot + "|plot") % (uint)plots.Length);
            string context = "Visit your spouse or parent at court to " + plots[pick] + ". Match your exact age and personality. This is a conversational template, not a completed story. Later activities are invitations for roleplay in Family Chambers, never automatically performed. ";
            var jealousy = member["jealousy"] as JObject;
            bool jealous = jealousy?.Value<bool?>("eligible") == true && FamilyStableSeed(day + "|" + slot + "|family-jealousy") / (double)uint.MaxValue < jealousy.Value<double>("weight") * 0.3d;
            string baseTemplate = "family_" + (hero.IsChild ? (hero.Age < 9f ? "young" : "teen") : hero == Hero.MainHero.Spouse ? "spouse" : "adult_child") + "_";
            if (!string.IsNullOrWhiteSpace(requestedTemplateId))
            {
                if (requestedTemplateId == "family_attention_jealousy")
                {
                    if (jealousy?.Value<bool?>("eligible") != true) return null;
                    jealous = true;
                }
                else
                {
                    int requestedIndex;
                    if (!requestedTemplateId.StartsWith(baseTemplate, StringComparison.Ordinal)
                        || !int.TryParse(requestedTemplateId.Substring(baseTemplate.Length), out requestedIndex)
                        || requestedIndex < 0 || requestedIndex >= plots.Length) return null;
                    pick = requestedIndex; jealous = false;
                    context = "Visit your spouse or parent at court to " + plots[pick] + ". Match your exact age and personality. Later activities are invitations for roleplay in Family Chambers, never automatically performed. ";
                }
            }
            if (jealous) context = jealousy.Value<string>("context") + " Start a natural age-appropriate conversation about wanting attention. ";
            string id = "family_visit_" + FamilyStableSeed(campaignId + "|" + timelineId + "|" + day + "|" + slot).ToString("x8");
            var command = FamilyCommand(campaignId, timelineId, "register", now);
            command["visitId"] = id; command["hero"] = FamilyVisitProfile(hero); command["deadline"] = ReignFamilyVisitRules.NextCourtMorning(now);
            state.FamilyVisitOutbox = state.FamilyVisitOutbox ?? new List<string>();
            state.FamilyVisitOutbox.Add(command.ToString(Formatting.None));
            return new ReignCourtLifeMatter { MatterId = id, CampaignId = campaignId, TimelineId = timelineId, ReignId = CurrentReignId(),
                Source = ReignDocketSource.Family, TemplateId = jealous ? "family_attention_jealousy" : baseTemplate + pick,
                Title = "A visit from " + hero.Name, Summary = "A member of your family wishes to spend a little time with you.",
                SettlementId = (Settlement.CurrentSettlement ?? MobileParty.MainParty?.CurrentSettlement)?.StringId ?? "",
                KingdomId = Clan.PlayerClan?.Kingdom?.StringId ?? "", ReceivedDay = day, AvailableDay = now,
                ExpiresDay = ReignFamilyVisitRules.NextCourtMorning(now), State = ReignCourtLifeMatterState.Pending,
                PayloadJson = new JObject { ["familyVisit"] = true, ["familyVisitDocket"] = true }.ToString(Formatting.None),
                Participants = new List<ReignCourtLifeParticipant> { new ReignCourtLifeParticipant { ActorId = hero.StringId, HeroId = hero.StringId,
                    Name = hero.Name?.ToString() ?? hero.StringId, Role = hero == Hero.MainHero.Spouse ? "spouse" : "child", Age = hero.Age,
                    IsFemale = hero.IsFemale, PrivateContext = context + member.Value<string>("context") } } };
        }

        public ReignCourtLifeMatter TryCreateCourtJealousyMatterForSlot(string campaignId, string timelineId, int day, int slot)
        {
            if (FamilyStableSeed(campaignId + "|" + timelineId + "|" + day + "|" + slot + "|jealousy-subroll") % 5 != 0) return null;
            var candidates = FamilyMirror()["jealousyCandidates"]?.OfType<JObject>().ToList() ?? new List<JObject>();
            var local = Hero.AllAliveHeroes.Where(h => h.IsLord && !h.IsChild && !h.IsPrisoner && FamilyLocallyPresent(h)
                && h != Hero.MainHero && h.Clan?.Kingdom == Clan.PlayerClan?.Kingdom).ToDictionary(h => h.StringId);
            var picked = candidates.Where(x => local.ContainsKey(x.Value<string>("heroId") ?? ""))
                .Where(x => FamilyStableSeed(campaignId + "|" + timelineId + "|" + day + "|" + slot + "|" + x.Value<string>("heroId")) / (double)uint.MaxValue < x.Value<double>("weight"))
                .OrderBy(x => FamilyStableSeed(day + "|" + slot + "|" + x.Value<string>("heroId"))).FirstOrDefault();
            if (picked == null) return null;
            Hero hero = local[picked.Value<string>("heroId")];
            if (EnsureRulerDocketState().CourtLifeMatters.Any(m => m.IsPending && m.Participants.Any(p => p.HeroId == hero.StringId))) return null;
            double now = CampaignTime.Now.ToDays;
            return new ReignCourtLifeMatter { MatterId = "court_jealousy_" + FamilyStableSeed(campaignId + "|" + timelineId + "|" + day + "|" + slot).ToString("x8"),
                CampaignId = campaignId, TimelineId = timelineId, ReignId = CurrentReignId(), Source = ReignDocketSource.DomesticNoble,
                TemplateId = "court_attention_rivalry", Title = "A private concern from " + hero.Name, Summary = "A noble wishes to speak about their place at court.",
                ReceivedDay = day, AvailableDay = now, SettlementId = hero.CurrentSettlement?.StringId ?? "", KingdomId = Clan.PlayerClan?.Kingdom?.StringId ?? "",
                Participants = new List<ReignCourtLifeParticipant> { new ReignCourtLifeParticipant { ActorId = hero.StringId, HeroId = hero.StringId,
                    Name = hero.Name?.ToString() ?? hero.StringId, Role = "courtier", Age = hero.Age, IsFemale = hero.IsFemale, PrivateContext = picked.Value<string>("context") } },
                PayloadJson = new JObject { ["socialOnly"] = true, ["jealousy"] = picked }.ToString(Formatting.None) };
        }

        public void ProcessFamilyVisits(double now)
        {
            if (_familyVisitSyncInFlight || Hero.MainHero == null) return;
            string identity = ReignCampaignIdentity.CurrentCampaignId() + "|" + (ReignWorldHistoryCampaignBehavior.Instance?.TimelineId ?? "main") + "|" + Hero.MainHero.StringId;
            if (identity != _familyVisitSyncIdentity || now < _lastFamilyVisitSyncDay)
            { _familyVisitSyncIdentity = identity; _lastFamilyVisitSyncDay = -1; }
            var state = EnsureRulerDocketState();
            state.FamilyVisitOutbox = state.FamilyVisitOutbox ?? new List<string>();
            bool crossedCourtMorning = ReignFamilyVisitRules.CourtDay(now) > ReignFamilyVisitRules.CourtDay(_lastFamilyVisitSyncDay);
            if (state.FamilyVisitOutbox.Count == 0 && !crossedCourtMorning && now - _lastFamilyVisitSyncDay < 1d / 48d) return;
            foreach (var matter in state.CourtLifeMatters.Where(m => m.Source == ReignDocketSource.Family && m.IsPending))
            {
                var hero = RulerFamily().FirstOrDefault(h => matter.Participants.Any(p => p.HeroId == h.StringId));
                if ((matter.TechnicalFailure && now >= matter.ExpiresDay) || hero == null || !hero.IsAlive || hero.IsPrisoner || !FamilyLocallyPresent(hero))
                    InvalidateFamilyVisit(matter);
            }
            string campaign = ReignCampaignIdentity.CurrentCampaignId(), timeline = ReignWorldHistoryCampaignBehavior.Instance?.TimelineId ?? "main";
            var tick = FamilyCommand(campaign, timeline, "tick", now);
            tick["familyProfiles"] = new JArray(RulerFamily().Select(FamilyVisitProfile));
            tick["visitIds"] = new JArray(state.CourtLifeMatters.Where(m => m.Source == ReignDocketSource.Family && m.IsPending)
                .OrderBy(m => m.ReceivedDay).Take(256).Select(m => m.MatterId));
            tick["includeJealousy"] = FamilyMirror().Value<int?>("jealousyDay") != ReignFamilyVisitRules.CourtDay(now);
            tick["courtUnavailable"] = CurrentCapital == null || Clan.PlayerClan?.Kingdom?.Leader != Hero.MainHero || Chancellor.SuppressesPetitions;
            _familyVisitSyncInFlight = true;
            _ = FlushFamilyVisitOutboxAsync(state, tick, now);
        }

        public Task RecordFamilyVisitTurnAsync(ReignCourtLifeMatter matter, string completedPlayerTurnId)
        {
            if (matter == null || matter.Source != ReignDocketSource.Family || string.IsNullOrWhiteSpace(completedPlayerTurnId)) return Task.CompletedTask;
            var command = FamilyCommand(matter.CampaignId, matter.TimelineId, "turn", CampaignTime.Now.ToDays);
            command["visitId"] = matter.MatterId; command["turnId"] = completedPlayerTurnId;
            var state = EnsureRulerDocketState();
            state.FamilyVisitOutbox = state.FamilyVisitOutbox ?? new List<string>();
            string serialized = command.ToString(Formatting.None);
            if (!state.FamilyVisitOutbox.Any(raw => JObject.Parse(raw).Value<string>("turnId") == completedPlayerTurnId)) state.FamilyVisitOutbox.Add(serialized);
            _lastFamilyVisitSyncDay = -1;
            ProcessFamilyVisits(CampaignTime.Now.ToDays);
            return Task.CompletedTask;
        }

        public void InvalidateFamilyVisit(ReignCourtLifeMatter matter)
        {
            if (matter == null || matter.Source != ReignDocketSource.Family || !matter.IsPending) return;
            var command = FamilyCommand(matter.CampaignId, matter.TimelineId, "invalidate", CampaignTime.Now.ToDays);
            command["visitId"] = matter.MatterId;
            var state = EnsureRulerDocketState(); state.FamilyVisitOutbox = state.FamilyVisitOutbox ?? new List<string>();
            state.FamilyVisitOutbox.Add(command.ToString(Formatting.None));
            matter.State = ReignCourtLifeMatterState.Invalidated;
        }

        private async Task FlushFamilyVisitOutboxAsync(ReignRulerDocketState state, JObject tick, double now)
        {
            try
            {
                var sent = state.FamilyVisitOutbox.ToList();
                foreach (string raw in sent)
                {
                    var result = await ReignServerClient.PostFamilyVisitAsync(JObject.Parse(raw)).ConfigureAwait(false);
                    if (result.Value<bool?>("ok") != true)
                    {
                        string error = result.Value<string>("error") ?? "";
                        JObject command = JObject.Parse(raw);
                        bool obsoleteRegistration = command.Value<string>("action") == "register"
                            && (error == "family_visit_unavailable" || error == "ineligible_family_visitor" || error == "family_visit_deadline_invalid");
                        if (!obsoleteRegistration) return;
                        await ReignMainThread.InvokeAsync(() =>
                        {
                            if (!ReferenceEquals(state, _rulerDocketState)) return;
                            var unavailable = state.CourtLifeMatters.FirstOrDefault(m => m.MatterId == command.Value<string>("visitId"));
                            if (unavailable != null) unavailable.State = ReignCourtLifeMatterState.Invalidated;
                        }).ConfigureAwait(false);
                    }
                    await ReignMainThread.InvokeAsync(() => { if (ReferenceEquals(state, _rulerDocketState)) state.FamilyVisitOutbox.Remove(raw); }).ConfigureAwait(false);
                }
                JObject snapshot = await ReignServerClient.PostFamilyVisitAsync(tick).ConfigureAwait(false);
                if (snapshot.Value<bool?>("ok") != true) return;
                await ReignMainThread.InvokeAsync(() =>
                {
                    if (!ReferenceEquals(state, _rulerDocketState) || !ReferenceEquals(Instance, this) || !MatchesCurrentFamilyScope(snapshot)) return;
                    if (snapshot["jealousyCandidates"] == null)
                    {
                        var priorMirror = FamilyMirror();
                        snapshot["jealousyCandidates"] = priorMirror["jealousyCandidates"]?.DeepClone() ?? new JArray();
                        snapshot["jealousyDay"] = priorMirror["jealousyDay"]?.DeepClone();
                    }
                    state.FamilyAttentionMirrorJson = snapshot.ToString(Formatting.None);
                    foreach (var row in snapshot["visits"]?.OfType<JObject>() ?? Enumerable.Empty<JObject>())
                    {
                        var matter = state.CourtLifeMatters.FirstOrDefault(m => m.MatterId == row.Value<string>("visit_id"));
                        if (matter == null || !matter.IsPending) continue;
                        string status = row.Value<string>("status");
                        if (status == "attended" && now >= matter.ExpiresDay) matter.State = ReignCourtLifeMatterState.Resolved;
                        else if (status == "invalidated") matter.State = ReignCourtLifeMatterState.Invalidated;
                        else if (status == "dismissed") matter.State = ReignCourtLifeMatterState.Expired;
                    }
                    _lastFamilyVisitSyncDay = now;
                    StateChanged?.Invoke();
                }).ConfigureAwait(false);
            }
            catch (Exception ex) { ReignLog.Warn("Family attention synchronization will retry: " + ex.Message); }
            finally { _familyVisitSyncInFlight = false; }
        }

        private static uint FamilyStableSeed(string value)
        {
            unchecked { uint hash = 2166136261; foreach (char c in value ?? "") hash = (hash ^ c) * 16777619; return hash; }
        }
    }
}
