using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Reign.Core.Contracts.Government;
using ReignBeta.Campaign;
using ReignBeta.Integration;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Election;

namespace ReignBeta.Government
{
    public sealed partial class ReignGovernmentCampaignBehavior
    {
        private bool _politicalSyncInFlight;
        private string _politicalSyncError = "";
        private DateTime _politicalSyncRetryUtc;
        private readonly Dictionary<string, DateTime> _politicalLastSync = new Dictionary<string, DateTime>();
        private readonly GovernmentPoliticalRefreshCycle _politicalRefreshCycle = new GovernmentPoliticalRefreshCycle();
        public int CurrentPoliticalInterval => GovernmentInterval(CurrentDay());
        public JObject PoliticalRuntimeStatus()
        {
            var profiles = _participation.Profiles.Where(GovernmentPoliticalBeliefs.Valid)
                .ToDictionary(x => x.HeroId, x => x, StringComparer.Ordinal);
            var living = new HashSet<string>(Hero.AllAliveHeroes.Where(x => x != Hero.MainHero).Select(x => x.StringId), StringComparer.Ordinal);
            int missing = living.Count(id => !profiles.ContainsKey(id));
            int evidence = _participation.PoliticalEvents.Count(x => x.AffectedHeroes.Any(id =>
                living.Contains(id) && profiles.TryGetValue(id, out var profile) && !profile.SeenEvidence.Contains(x.Id)));
            return new JObject {
                ["schema"] = "reign-government-runtime-v1", ["available"] = true,
                ["calendarReady"] = ReignBeta.Runtime.ReignCalendarService.IsReady,
                ["syncInFlight"] = _politicalSyncInFlight, ["missingProfiles"] = missing,
                ["pendingEvidence"] = evidence, ["syncError"] = _politicalSyncError
            };
        }
        public void TickPoliticalProfileSync()
        {
            if (TaleWorlds.CampaignSystem.Campaign.Current != null && Hero.MainHero != null) QueuePoliticalProfileSync();
        }
        public JObject PoliticalProfileForServer(string heroId)
        {
            var profile = AppliedPoliticalProfile(heroId);
            return profile == null ? null : JObject.FromObject(profile);
        }
        public JArray PoliticalEventsForServer(string heroId)
        {
            var profile = AppliedPoliticalProfile(heroId);
            return JArray.FromObject(_participation.PoliticalEvents.Where(x => x.AffectedHeroes.Contains(heroId)
                && (profile == null || !profile.SeenEvidence.Contains(x.Id))).OrderBy(x => x.WorldDay).ThenBy(x => x.Id).Take(64));
        }
        private void RecordPoliticalWorldEffect(ReignGovernmentBusinessRecord record, string optionId)
        {
            if (!record.ExecutionApplied || !BusinessOptions(record).Any(x => x.Value<string>("id") == optionId && x.Value<bool?>("affirmative") == true)
                || _participation.PoliticalEvents.Any(x => x.Id == "government:" + record.BusinessId)) return;
            if (record.Kind != "edict" && record.Kind != "policy" && record.Kind != "war" && record.Kind != "peace") return;
            var kingdom = FindKingdom(record.KingdomStringId);
            if (kingdom == null) return;
            var evidence = new GovernmentPoliticalWorldEvidence {
                Id = "government:" + record.BusinessId, CampaignId = ReignCampaignIdentity.CurrentCampaignId(),
                TimelineId = ReignWorldHistoryCampaignBehavior.Instance?.TimelineId ?? "main",
                KingdomId = record.KingdomStringId, Kind = record.Kind, TargetId = record.TargetId,
                OptionId = optionId, Applied = true, IsRepeal = record.IsRepeal,
                Interval = GovernmentInterval(CurrentDay()), WorldDay = CurrentDay(),
                AffectedHeroes = Hero.AllAliveHeroes.Where(x => x != Hero.MainHero
                    && (x.Clan?.Kingdom == kingdom || x.HomeSettlement?.MapFaction == kingdom)).Select(x => x.StringId).Distinct().ToList()
            };
            if (record.Kind == "edict") evidence.TargetId = JObject.Parse(record.ActionPayloadJson).Value<string>("edictId") ?? "";
            var policy = (NativeBusinessDecision(record) as KingdomPolicyDecision)?.Policy;
            if (record.Kind == "policy")
            {
                if (policy == null) return;
                evidence.TargetId = policy.StringId; evidence.Authoritarian = policy.AuthoritarianWeight;
                evidence.Oligarchic = policy.OligarchicWeight; evidence.Egalitarian = policy.EgalitarianWeight;
            }
            _participation.PoliticalEvents.Add(evidence);
            foreach (string id in evidence.AffectedHeroes) _politicalLastSync.Remove(id);
        }
        private void QueuePoliticalProfileSync()
        {
            if (_politicalSyncInFlight || DateTime.UtcNow < _politicalSyncRetryUtc) return;
            DateTime utc = DateTime.UtcNow;
            var living = Hero.AllAliveHeroes.Where(x => x != Hero.MainHero).ToList();
            var profiles = _participation.Profiles.Where(GovernmentPoliticalBeliefs.Valid)
                .ToDictionary(x => x.HeroId, x => x, StringComparer.Ordinal);
            var pendingEvidence = new HashSet<string>(_participation.PoliticalEvents.SelectMany(e => e.AffectedHeroes.Where(id =>
                !profiles.TryGetValue(id, out var profile) || !profile.SeenEvidence.Contains(e.Id))), StringComparer.Ordinal);
            var members = new HashSet<string>(_seats.Select(x => x.HeroStringId), StringComparer.Ordinal);
            _politicalRefreshCycle.Observe(utc, CurrentPoliticalInterval);
            _politicalRefreshCycle.CompleteIfAcknowledged(living.Select(x =>
                _politicalLastSync.TryGetValue(x.StringId, out DateTime last) ? (DateTime?)last : null), utc);
            var heroes = living.Where(x => _politicalRefreshCycle.NeedsRefresh(
                _politicalLastSync.TryGetValue(x.StringId, out DateTime last) ? (DateTime?)last : null,
                !profiles.ContainsKey(x.StringId) || pendingEvidence.Contains(x.StringId)))
                .OrderByDescending(x => !profiles.ContainsKey(x.StringId)).ThenByDescending(x => pendingEvidence.Contains(x.StringId))
                .ThenByDescending(x => members.Contains(x.StringId))
                .ThenBy(x => _politicalLastSync.TryGetValue(x.StringId, out DateTime last) ? last : DateTime.MinValue).ThenBy(x => x.StringId, StringComparer.Ordinal).Take(64).ToList();
            if (heroes.Count == 0) { _politicalSyncRetryUtc = DateTime.UtcNow.AddSeconds(15); return; }
            var payload = ReignServerClient.BuildPoliticalSyncPayload(heroes);
            var state = _participation;
            _politicalSyncInFlight = true;
            _ = CompletePoliticalProfileSync(payload, state);
        }
        private async Task CompletePoliticalProfileSync(JObject payload, GovernmentParticipationState expectedState)
        {
            JObject response = null;
            try { response = await ReignServerClient.SyncPoliticalProfilesAsync(payload).ConfigureAwait(false); }
            catch (Exception ex) { _politicalSyncError = ex.GetType().Name + ": political profile synchronization failed."; }
            await ReignMainThread.InvokeAsync(() =>
            {
                _politicalSyncInFlight = false; _politicalSyncRetryUtc = DateTime.UtcNow.AddSeconds(response?.Value<bool?>("ok") == true ? 1 : 30);
                _politicalSyncError = response?.Value<bool?>("ok") == true ? "" : "Political profile synchronization did not complete.";
                if (Instance != this || !ReferenceEquals(_participation, expectedState) || response?.Value<bool?>("ok") != true
                    || response.Value<string>("campaignId") != ReignCampaignIdentity.CurrentCampaignId()
                    || response.Value<string>("timelineId") != (ReignWorldHistoryCampaignBehavior.Instance?.TimelineId ?? "main")) return false;
                bool politicalStateChanged = false;
                foreach (var item in (response["profiles"] as JArray ?? new JArray()).OfType<JObject>())
                {
                    var profile = item.ToObject<GovernmentPoliticalProfile>();
                    if (!GovernmentPoliticalBeliefs.Valid(profile)) continue;
                    var current = AppliedPoliticalProfile(profile.HeroId);
                    var sent = (payload["heroes"] as JArray)?.OfType<JObject>().FirstOrDefault(x => x.Value<string>("heroStringId") == profile.HeroId);
                    var sentProfile = sent?["appliedPoliticalProfile"];
                    if (sent == null || current != null && !JToken.DeepEquals(JObject.FromObject(current), sentProfile)
                        || current != null && profile.Revision < current.Revision
                        || profile.CampaignId != response.Value<string>("campaignId") || profile.TimelineId != response.Value<string>("timelineId")) continue;
                    // An unchanged refresh is a cache acknowledgement, not a political
                    // change. Keep the saved profile order and government revisions.
                    if (current == null)
                    { _participation.Profiles.Add(profile); politicalStateChanged = true; }
                    else if (!JToken.DeepEquals(JObject.FromObject(current), JObject.FromObject(profile)))
                    {
                        _participation.Profiles[_participation.Profiles.IndexOf(current)] = profile;
                        politicalStateChanged = true;
                    }
                    _politicalLastSync[profile.HeroId] = DateTime.UtcNow;
                }
                foreach (var seat in _seats.Where(x => string.IsNullOrEmpty(x.PartyId)).ToList())
                {
                    var profile = AppliedPoliticalProfile(seat.HeroStringId); if (profile == null) continue;
                    seat.PartyId = GetParties(seat.KingdomStringId).OrderByDescending(x => GovernmentPoliticalBeliefs.PartyCompatibility(profile, ParsePlanks(x.PlanksCsv)))
                        .ThenBy(x => ReignGovernmentRules.StableHash(seat.HeroStringId + "|" + x.PartyId)).FirstOrDefault()?.PartyId ?? "";
                    if (!string.IsNullOrEmpty(seat.PartyId)) politicalStateChanged = true;
                }
                if (politicalStateChanged)
                    foreach (var state in _governments) { var kingdom = FindKingdom(state.KingdomStringId); if (kingdom != null) RecalculatePartyState(kingdom, state); }
                return true;
            }).ConfigureAwait(false);
        }

        private int PersonalBusinessAlignment(ReignGovernmentBusinessRecord record, Hero hero, JObject option)
        {
            var beliefs = AppliedPoliticalProfile(hero.StringId);
            if (beliefs == null) return 0; // Callers with no applied profile must defer, never cast this as a neutral ballot.
            var impacts = new Dictionary<ReignGovernmentPlank, int>();
            bool affirmative = option.Value<bool?>("affirmative") ?? false;
            if (record.Kind == "edict")
                impacts = GovernmentPoliticalBeliefs.EdictImpact(JObject.Parse(record.ActionPayloadJson).Value<string>("edictId"), record.IsRepeal).ToDictionary(x => x.Key, x => x.Value);
            else if (record.Kind == "policy")
            {
                var policy = (NativeBusinessDecision(record) as KingdomPolicyDecision)?.Policy;
                if (policy != null) impacts = GovernmentPoliticalBeliefs.NativePolicyImpact(policy.AuthoritarianWeight, policy.OligarchicWeight, policy.EgalitarianWeight, record.IsRepeal).ToDictionary(x => x.Key, x => x.Value);
            }
            else if (record.Kind == "admission" || record.Kind == "recall")
                impacts = GovernmentPoliticalBeliefs.MandateImpact(record.Kind,
                    _participation.Constituencies.Any(x => x.KingdomId == record.KingdomStringId && x.Id == record.TargetId && x.Mandate == "civic"))
                    .ToDictionary(x => x.Key, x => x.Value);
            else
            {
                var kind = record.Kind == "world_action" ? (ReignGovernmentActionKind)record.ActionKindValue : MapNativeDecisionKind(NativeBusinessDecision(record));
                foreach (ReignGovernmentPlank plank in Enum.GetValues(typeof(ReignGovernmentPlank)))
                    impacts[plank] = Math.Max(-100, Math.Min(100, ActionPlankAlignment(kind, new List<ReignGovernmentPlank> { plank }) * 5));
                if (record.Kind == "seasonal")
                {
                    var resolution = _resolutions.FirstOrDefault(x => x.ResolutionId == record.TargetId);
                    var template = ReignGovernmentResolutionCatalog.Find(resolution?.TemplateId);
                    if (template != null) foreach (var plank in template.SupportingPlanks) impacts[plank] = 75;
                }
            }
            if (!affirmative) impacts = impacts.ToDictionary(x => x.Key, x => -x.Value);
            return GovernmentPoliticalBeliefs.Alignment(beliefs, impacts);
        }
    }
}
