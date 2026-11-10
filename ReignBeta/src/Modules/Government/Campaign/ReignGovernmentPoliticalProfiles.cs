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
        private PoliticalCapture _politicalCapture;
        private long _politicalCapturedHeroCount, _politicalAcknowledgedHeroCount, _politicalCompletedBatchCount, _politicalCompletedSweepCount;
        private DateTime _politicalLastCompletedSweepUtc;
        private sealed class PoliticalBase
        {
            internal GovernmentPoliticalProfile Profile;
            internal long Revision;
        }
        private sealed class PoliticalCapture
        {
            internal object Campaign;
            internal GovernmentParticipationState State;
            internal string CampaignId, TimelineId;
            internal int Interval, NextHero;
            internal List<Hero> Heroes;
            internal readonly JArray Rows = new JArray();
            internal readonly Dictionary<string, PoliticalBase> Bases = new Dictionary<string, PoliticalBase>(StringComparer.Ordinal);
        }
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
                ["pendingEvidence"] = evidence, ["syncError"] = _politicalSyncError,
                ["refreshActive"] = _politicalRefreshCycle.Active,
                ["refreshStartedUtc"] = _politicalRefreshCycle.StartedUtc == DateTime.MinValue ? "" : _politicalRefreshCycle.StartedUtc.ToString("o"),
                ["lastCompletedSweepUtc"] = _politicalLastCompletedSweepUtc == DateTime.MinValue ? "" : _politicalLastCompletedSweepUtc.ToString("o"),
                ["capturedHeroCount"] = _politicalCapturedHeroCount, ["acknowledgedHeroCount"] = _politicalAcknowledgedHeroCount,
                ["completedBatchCount"] = _politicalCompletedBatchCount, ["completedSweepCount"] = _politicalCompletedSweepCount
            };
        }
        public void TickPoliticalProfileSync()
        {
            if (TaleWorlds.CampaignSystem.Campaign.Current == null || Hero.MainHero == null) return;
            if (_politicalCapture != null) { ContinuePoliticalCapture(); return; }
            QueuePoliticalProfileSync();
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
            List<Hero> heroes;
            using (ReignLog.MeasureWork("government.political.select", 0f))
            {
                DateTime utc = DateTime.UtcNow;
                var living = Hero.AllAliveHeroes.Where(x => x != Hero.MainHero).ToList();
                var profiles = _participation.Profiles.Where(GovernmentPoliticalBeliefs.Valid)
                    .ToDictionary(x => x.HeroId, x => x, StringComparer.Ordinal);
                var pendingEvidence = new HashSet<string>(_participation.PoliticalEvents.SelectMany(e => e.AffectedHeroes.Where(id =>
                    !profiles.TryGetValue(id, out var profile) || !profile.SeenEvidence.Contains(e.Id))), StringComparer.Ordinal);
                var members = new HashSet<string>(_seats.Select(x => x.HeroStringId), StringComparer.Ordinal);
                _politicalRefreshCycle.Observe(utc, CurrentPoliticalInterval);
                if (_politicalRefreshCycle.CompleteIfAcknowledged(living.Select(x =>
                    _politicalLastSync.TryGetValue(x.StringId, out DateTime last) ? (DateTime?)last : null), utc))
                { _politicalLastCompletedSweepUtc = utc; _politicalCompletedSweepCount++; }
                heroes = living.Where(x => _politicalRefreshCycle.NeedsRefresh(
                    _politicalLastSync.TryGetValue(x.StringId, out DateTime last) ? (DateTime?)last : null,
                    !profiles.ContainsKey(x.StringId) || pendingEvidence.Contains(x.StringId)))
                    .OrderByDescending(x => !profiles.ContainsKey(x.StringId)).ThenByDescending(x => pendingEvidence.Contains(x.StringId))
                    .ThenByDescending(x => members.Contains(x.StringId))
                    .ThenBy(x => _politicalLastSync.TryGetValue(x.StringId, out DateTime last) ? last : DateTime.MinValue).ThenBy(x => x.StringId, StringComparer.Ordinal).Take(64).ToList();
            }
            if (heroes.Count == 0) { _politicalSyncRetryUtc = DateTime.UtcNow.AddSeconds(15); return; }
            _politicalSyncInFlight = true;
            _politicalCapture = new PoliticalCapture {
                Campaign = TaleWorlds.CampaignSystem.Campaign.Current, State = _participation,
                CampaignId = ReignCampaignIdentity.CurrentCampaignId(),
                TimelineId = ReignWorldHistoryCampaignBehavior.Instance?.TimelineId ?? "main",
                Interval = CurrentPoliticalInterval, Heroes = heroes
            };
            // Selection and capture do not share a frame. Keep the wire batch
            // at 64; spread only its native capture work across application ticks.
        }

        private bool PoliticalScopeCurrent(PoliticalCapture capture) => Instance == this
            && ReferenceEquals(capture.Campaign, TaleWorlds.CampaignSystem.Campaign.Current)
            && ReferenceEquals(capture.State, _participation)
            && capture.CampaignId == ReignCampaignIdentity.CurrentCampaignId()
            && capture.TimelineId == (ReignWorldHistoryCampaignBehavior.Instance?.TimelineId ?? "main");

        private void ContinuePoliticalCapture()
        {
            var capture = _politicalCapture;
            if (!PoliticalScopeCurrent(capture) || capture.Interval != CurrentPoliticalInterval)
            {
                _politicalCapture = null; _politicalSyncInFlight = false;
                _politicalSyncRetryUtc = DateTime.UtcNow;
                return;
            }
            try
            {
                var timer = System.Diagnostics.Stopwatch.StartNew();
                int count = 0;
                using (ReignLog.MeasureWork("government.political.capture", 0f))
                {
                    while (capture.NextHero < capture.Heroes.Count && count < 8
                        && (count == 0 || timer.Elapsed.TotalMilliseconds < 4d))
                    {
                        Hero hero = capture.Heroes[capture.NextHero++];
                        count++;
                        if (!hero.IsAlive || hero == Hero.MainHero) continue;
                        try
                        {
                            var current = AppliedPoliticalProfile(hero.StringId);
                            capture.Rows.Add(ReignServerClient.BuildPoliticalSyncHero(hero));
                            _politicalCapturedHeroCount++;
                            capture.Bases[hero.StringId] = new PoliticalBase { Profile = current, Revision = current?.Revision ?? 0 };
                        }
                        catch (Exception ex)
                        {
                            // Leave this hero unacknowledged for a later retry,
                            // without discarding healthy rows in the same batch.
                            ReignLog.Exception("Political profile hero capture", ex);
                        }
                    }
                }
                if (capture.NextHero < capture.Heroes.Count) return;
                if (capture.Rows.Count == 0)
                {
                    _politicalCapture = null; _politicalSyncInFlight = false;
                    _politicalSyncRetryUtc = DateTime.UtcNow.AddSeconds(30);
                    _politicalSyncError = "No political profiles could be captured.";
                    return;
                }
                var payload = ReignServerClient.BuildPoliticalSyncPayload(capture.Rows);
                _politicalCapture = null;
                // All native fields are detached before the worker touches JSON
                // or transport. There is still only one batch in flight.
                _ = Task.Run(() => CompletePoliticalProfileSync(payload, capture));
            }
            catch (Exception ex)
            {
                _politicalCapture = null; _politicalSyncInFlight = false;
                _politicalSyncRetryUtc = DateTime.UtcNow.AddSeconds(30);
                _politicalSyncError = "Political profile capture did not complete.";
                ReignLog.Exception("Political profile capture", ex);
            }
        }

        private async Task CompletePoliticalProfileSync(JObject payload, PoliticalCapture capture)
        {
            JObject response = null;
            IReadOnlyList<GovernmentPoliticalPreparedProfile> prepared = null;
            string failure = "Political profile synchronization did not complete.";
            try
            {
                response = await ReignServerClient.SyncPoliticalProfilesAsync(payload).ConfigureAwait(false);
                if (response?.Value<bool?>("ok") == true) prepared = GovernmentPoliticalBatchPreparation.Prepare(payload, response);
            }
            catch (Exception ex)
            {
                failure = ex.GetType().Name + ": political profile synchronization failed.";
                ReignLog.Exception("Political profile synchronization", ex);
            }
            await ReignMainThread.InvokeAsync(() =>
            {
                bool succeeded = response?.Value<bool?>("ok") == true && prepared != null;
                _politicalSyncInFlight = false; _politicalSyncRetryUtc = _politicalRefreshCycle.RetryAfterBatch(DateTime.UtcNow, succeeded);
                _politicalSyncError = succeeded ? "" : failure;
                if (!PoliticalScopeCurrent(capture) || capture.Interval != CurrentPoliticalInterval || !succeeded
                    || response.Value<string>("campaignId") != ReignCampaignIdentity.CurrentCampaignId()
                    || response.Value<string>("timelineId") != (ReignWorldHistoryCampaignBehavior.Instance?.TimelineId ?? "main")) return false;
                try
                {
                  using (ReignLog.MeasureWork("government.political.apply", 0f))
                  {
                    bool acknowledgedProgress = false;
                    bool politicalStateChanged = false;
                    foreach (var item in prepared)
                    {
                        var profile = item.Profile;
                        var current = AppliedPoliticalProfile(profile.HeroId);
                        if (!capture.Bases.TryGetValue(profile.HeroId, out PoliticalBase sent)
                            || !GovernmentPoliticalBatchPreparation.BaseStillCurrent(current, sent.Profile, sent.Revision)
                            || current != null && profile.Revision < current.Revision) continue;
                        // An unchanged refresh is a cache acknowledgement, not a political
                        // change. Keep the saved profile order and government revisions.
                        if (current == null)
                        { _participation.Profiles.Add(profile); politicalStateChanged = true; }
                        else if (item.ChangedFromSent)
                        {
                            _participation.Profiles[_participation.Profiles.IndexOf(current)] = profile;
                            politicalStateChanged = true;
                        }
                        acknowledgedProgress |= _politicalRefreshCycle.AcknowledgementAdvancesSweep(
                            _politicalLastSync.TryGetValue(profile.HeroId, out DateTime previousAck) ? (DateTime?)previousAck : null);
                        _politicalLastSync[profile.HeroId] = DateTime.UtcNow;
                        _politicalAcknowledgedHeroCount++;
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
                    _politicalCompletedBatchCount++;
                    _politicalSyncRetryUtc = _politicalRefreshCycle.RetryAfterBatch(DateTime.UtcNow, true,
                        acknowledgedProgress);
                    return true;
                  }
                }
                catch (Exception ex)
                {
                    _politicalSyncRetryUtc = DateTime.UtcNow.AddSeconds(30);
                    _politicalSyncError = "Political profile commit did not complete.";
                    ReignLog.Exception("Political profile commit", ex);
                    return false;
                }
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
