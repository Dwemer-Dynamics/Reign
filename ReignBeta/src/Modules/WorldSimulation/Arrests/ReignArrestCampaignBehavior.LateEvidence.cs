using System;
using System.Globalization;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using TaleWorlds.CampaignSystem;
using ReignBeta.Campaign;
using ReignBeta.Persistence.Campaign;
using ReignBeta.Persistence.Integration;
using ReignBeta.Platform.Integration;
using ReignBeta.Platform.Runtime;
using ReignBeta.WorldSimulation.Campaign;
using ReignBeta.WorldSimulation.Integration;

namespace ReignBeta.WorldSimulation.Arrests
{
    public sealed partial class ReignArrestCampaignBehavior
    {
        private const string OwnedReclassificationRoute = "/arrests/case/reclassify";

        internal string LateEvidenceFixtureCaseId(string fixtureRunId) =>
            "arrest_test_" + CompactId(fixtureRunId) + "_native_late_evidence";

        internal bool PrepareLateEvidenceFixture(ReignArrestCase item, Hero target, string fixtureRunId)
        {
            if (item == null || item.CaseId != LateEvidenceFixtureCaseId(fixtureRunId)
                || !_cases.Contains(item) || !ActiveCapturedCase(item) || item.CauseEstablished
                || !item.RelationshipEffectSynchronized || !item.ReputationSynchronized
                || target?.StringId != item.AccusedHeroStringId || !string.IsNullOrEmpty(item.DailyLateEvidenceIntentJson))
                return false;
            RecordOwnedLateEvidence(item, target, "fixture:late_evidence:" + CompactId(fixtureRunId));
            return CapturedString(ReadLateEvidenceIntent(item.DailyLateEvidenceIntentJson ?? ""), "phase") == "observed";
        }

        internal Task<bool> AdvanceLateEvidenceFixtureAsync(ReignArrestCase item, string fixtureRunId, Func<bool> ownsFixture) =>
            item?.CaseId == LateEvidenceFixtureCaseId(fixtureRunId)
                ? AdvanceOwnedLateEvidenceAsync(item, ownsFixture) : Task.FromResult(false);

        private static void RecordOwnedLateEvidence(ReignArrestCase item, Hero accused, string evidence, string origin = null)
        {
            if (!string.IsNullOrEmpty(item.DailyLateEvidenceIntentJson)
                || string.IsNullOrWhiteSpace(evidence) || accused == null || Hero.MainHero?.StringId != item.PlayerHeroStringId) return;
            var history = ReignServices.WorldHistory;
            string preferredOrigin = ReignSaveSyncCoordinator.CurrentVerifiedLoad?.Origin
                ?? ReignHttpTransport.PinnableOrigin(ReignServerEndpoint.ConfiguredBaseUrl());
            origin = origin ?? preferredOrigin;
            if (history?.TimelineReady != true || origin != preferredOrigin || !ReignHttpTransport.IsAllowedPinnedOrigin(origin))
                return;

            var location = Hero.MainHero.CurrentSettlement ?? accused.CurrentSettlement;
            string locationName = location?.Name?.ToString() ?? string.Empty;
            if (locationName.Length > 120) locationName = locationName.Substring(0, 120);
            item.DailyLateEvidenceIntentJson = new JObject
            {
                ["schema"] = 1, ["phase"] = "observed", ["origin"] = origin,
                ["campaignId"] = ReignServerClient.GetCampaignId(), ["timelineId"] = history.TimelineId,
                ["chargeCategory"] = item.ChargeCategory ?? string.Empty, ["reputationTagId"] = item.ReputationTagId ?? string.Empty,
                ["facts"] = new JObject
                {
                    ["eventId"] = "arrest_discovery_" + Guid.NewGuid().ToString("N"),
                    ["caseId"] = item.CaseId, ["playerHeroId"] = item.PlayerHeroStringId, ["accusedHeroId"] = accused.StringId,
                    ["worldDay"] = (double)CurrentDay(), ["locationId"] = location?.StringId ?? string.Empty,
                    ["locationName"] = locationName, ["evidenceIds"] = new JArray(evidence)
                }
            }.ToString(Newtonsoft.Json.Formatting.None);
        }

        private async Task<bool> ReviewOwnedLateEvidenceAsync(ReignArrestCase item, Func<bool> ownsPoint)
        {
            for (int step = 0; step < 6; step++)
            {
                string prior = await ReignMainThread.InvokeAsync(() => item.DailyLateEvidenceIntentJson).ConfigureAwait(false);
                if (await AdvanceOwnedLateEvidenceAsync(item, ownsPoint).ConfigureAwait(false)) return true;
                if (!await ReignMainThread.InvokeAsync(() => ownsPoint() && item.DailyLateEvidenceIntentJson != prior).ConfigureAwait(false))
                    return false;
            }
            return false;
        }

        private async Task<bool> AdvanceOwnedLateEvidenceAsync(ReignArrestCase item, Func<bool> ownsPoint)
        {
            var work = await ReignMainThread.InvokeAsync(() =>
            {
                var history = ReignServices.WorldHistory;
                string saved = item.DailyLateEvidenceIntentJson ?? string.Empty;
                var journal = ReadLateEvidenceIntent(saved);
                string origin = CapturedString(journal, "origin");
                string campaign = CapturedString(journal, "campaignId");
                string timeline = CapturedString(journal, "timelineId");
                string generation = history?.SharedActivityGeneration;
                string phase = CapturedString(journal, "phase");
                if (!ownsPoint() || !_cases.Contains(item) || history?.TimelineReady != true || journal == null
                    || !(journal["facts"] is JObject facts) || campaign != ReignServerClient.GetCampaignId()
                    || timeline != history.TimelineId || CapturedString(facts, "caseId") != item.CaseId
                    || CapturedString(facts, "playerHeroId") != item.PlayerHeroStringId
                    || CapturedString(facts, "accusedHeroId") != item.AccusedHeroStringId
                    || CapturedString(journal, "chargeCategory") != item.ChargeCategory || CapturedString(journal, "reputationTagId") != item.ReputationTagId
                    || !ReignHttpTransport.IsAllowedPinnedOrigin(origin)
                    || !ReignSaveSyncCoordinator.TryCaptureOwnedHistoryAuthority(campaign, generation, origin, out int alignment, out string session))
                    return null;
                if (!ActiveCapturedCase(item) && (phase is "observed" or "staged" or "history_proven"))
                {
                    item.DailyLateEvidenceIntentJson = string.Empty;
                    return null;
                }
                return new LateEvidenceWork(saved, journal, facts, history, campaign, timeline, generation, origin, alignment, session);
            }).ConfigureAwait(false);
            if (work == null) return false;

            bool Current() => ownsPoint() && _cases.Contains(item) && item.DailyLateEvidenceIntentJson == work.Saved
                && Hero.MainHero?.StringId == item.PlayerHeroStringId
                && ReferenceEquals(work.History, ReignServices.WorldHistory)
                && work.History.TimelineId == work.Timeline && work.History.SharedActivityGeneration == work.Generation
                && ReignSaveSyncCoordinator.CampaignGeneration == work.Alignment;

            switch (CapturedString(work.Journal, "phase"))
            {
                case "observed":
                    await StageOwnedLateHistoryAsync(item, work, work.Facts, "facts", "staged",
                        () => Current() && ActiveCapturedCase(item) && !item.CauseEstablished).ConfigureAwait(false);
                    return false;
                case "staged":
                    if (!await ObserveOwnedLateHistoryAsync(work, work.Facts, Current).ConfigureAwait(false))
                        return false;
                    await ReignMainThread.InvokeAsync(() =>
                    {
                        if (!Current() || !ActiveCapturedCase(item) || item.CauseEstablished || !(work.Facts["evidenceIds"] is JArray evidence)
                            || evidence.Count != 1 || evidence[0].Type != JTokenType.String
                            || !TryReadCapturedDay(work.Facts, out double observedDay)) return;
                        work.Journal["phase"] = "history_proven";
                        string retained = work.Journal.ToString(Newtonsoft.Json.Formatting.None);
                        MarkEstablishedEvidence(item, "established_after_arrest", (string)evidence[0]);
                        item.RelationshipEffectSynchronized = false;
                        item.ReputationSynchronized = false;
                        AddHistory(item, "evidence_reclassified", "Later authoritative evidence established cause.", observedDay);
                        item.DailyLateEvidenceIntentJson = retained;
                    }).ConfigureAwait(false);
                    return false;
                case "history_proven":
                    await PrepareOwnedLateEffectAsync(item, work, Current).ConfigureAwait(false);
                    return false;
                case "effect_pending":
                    await DispatchOwnedLateEffectAsync(item, work, work.Saved, ownsPoint).ConfigureAwait(false);
                    return false;
                case "effect_proven":
                    if (work.Journal["effectReceipt"] is not JObject receipt) return false;
                    var settlement = (JObject)receipt.DeepClone();
                    foreach (string field in new[] { "caseId", "playerHeroId", "accusedHeroId", "locationId", "locationName" })
                        settlement[field] = work.Facts[field].DeepClone();
                    settlement["discoveryEventId"] = work.Facts["eventId"].DeepClone();
                    settlement["reputationTagId"] = work.Journal["reputationTagId"].DeepClone();
                    await StageOwnedLateHistoryAsync(item, work, settlement, "settlement", "settlement_staged", Current).ConfigureAwait(false);
                    return false;
                case "settlement_staged":
                    if (work.Journal["settlement"] is not JObject confirmed
                        || !await ObserveOwnedLateHistoryAsync(work, confirmed, Current).ConfigureAwait(false))
                        return false;
                    return await ReignMainThread.InvokeAsync(() =>
                    {
                        if (!Current()) return false;
                        item.DailyLateEvidenceIntentJson = string.Empty;
                        return true;
                    }).ConfigureAwait(false);
                default:
                    return false;
            }
        }

        private static bool ActiveCapturedCase(ReignArrestCase item) =>
            item.Phase == ReignArrestPhase.Captured && item.AccusationActive;

        private static async Task StageOwnedLateHistoryAsync(ReignArrestCase item, LateEvidenceWork work,
            JObject facts, string journalProperty, string phase, Func<bool> current)
        {
            var boundary = await ReignMainThread.InvokeAsync(() => (work.History.Sequence, work.History.HeadEventId,
                Epoch: ReignWorldHistoryTransport.CaptureQueueEpoch())).ConfigureAwait(false);
            if (!ReignWorldHistoryTransport.FlushAndWaitThroughSequence(boundary.Sequence, TimeSpan.FromSeconds(5))) return;
            if (!await ReignWorldHistoryClient.ObserveOwnedJusticePredecessorAsync(work.Origin, work.Alignment, work.Campaign,
                work.Timeline, work.Generation, work.Session, boundary.Sequence, boundary.HeadEventId).ConfigureAwait(false))
                return;
            await ReignMainThread.InvokeAsync(() =>
            {
                if (!current() || ReignSaveSyncCampaignBehavior.IsNativeSaveInProgress || boundary.Sequence == long.MaxValue) return;
                facts["sequence"] = boundary.Sequence + 1;
                work.Journal[journalProperty] = facts;
                work.Journal["phase"] = phase;
                string staged = work.Journal.ToString(Newtonsoft.Json.Formatting.None);
                work.History.TryPublishOwnedJusticeHistory(facts, work.Campaign, work.Timeline, work.Generation,
                    work.Session, work.Origin, work.Alignment, boundary.Epoch, boundary.Sequence, boundary.HeadEventId,
                    () => item.DailyLateEvidenceIntentJson = staged);
            }).ConfigureAwait(false);
        }

        private static async Task<bool> ObserveOwnedLateHistoryAsync(LateEvidenceWork work, JObject facts, Func<bool> current)
        {
            long sequence = facts.Value<long>("sequence");
            if (sequence <= 0 || !ReignWorldHistoryTransport.FlushAndWaitThroughSequence(sequence, TimeSpan.FromSeconds(5)))
                return false;
            JObject projection = ReignWorldHistoryClient.BuildOwnedJusticeHistoryEnvelope(facts);
            return ReignWorldHistoryEventProof.TryCreate(work.Origin, work.Campaign, work.Timeline, work.Generation, projection, out var proof)
                && await ReignWorldHistoryClient.MatchesWorldHistoryEventAtOriginAsync(proof, current, work.Session).ConfigureAwait(false);
        }

        private async Task<string> PrepareOwnedLateEffectAsync(ReignArrestCase item, LateEvidenceWork work, Func<bool> current)
        {
            bool CanPrepare() => current() && ActiveCapturedCase(item) && item.CauseEstablished;
            var request = await ReignMainThread.InvokeAsync(() =>
            {
                if (!CanPrepare() || FindHero(item.AccusedHeroStringId) is not Hero accused) return null;
                return BuildCapturedRepairMaterial(item, accused, CurrentDay(), work.Campaign, work.Timeline, work.Generation);
            }).ConfigureAwait(false);
            if (request == null) return null;
            var inspection = (JObject)request.DeepClone();
            inspection["operation"] = "inspect";
            inspection["observePredecessorVersion"] = 1;
            JObject observed = await ReignServerClient.SendPinnedJsonAsync(work.Origin, CapturedRepairRoute, inspection, CanPrepare)
                .ConfigureAwait(false);
            if (!CapturedInspectionMatches(inspection, observed) || !TryReadCapturedRevision(observed, "materialRevision", out long revision)
                || revision == long.MaxValue) return null;
            string status = CapturedString(observed, "status");
            string predecessor = CapturedString(observed["predecessor"] as JObject, "materialSha256");
            if (revision == 0)
            {
                if (status != "ready" || !IsCapturedBoolean(observed, "ok", true)) return null;
                predecessor = string.Empty;
            }
            else if (status != "material_mismatch" || !Reign.Core.Contracts.Actions.TerminalActionReportProtocol.IsSha256Hex(predecessor))
                return null;

            return await ReignMainThread.InvokeAsync(() =>
            {
                if (!CanPrepare() || FindHero(item.AccusedHeroStringId) is not Hero accused
                    || !TryReadCapturedDay(request, out double capturedDay)
                    || !JToken.DeepEquals(request, BuildCapturedRepairMaterial(item, accused, capturedDay,
                        work.Campaign, work.Timeline, work.Generation))) return null;
                request["lateEvidenceEffectVersion"] = 1;
                request["discoveryEventId"] = work.Facts["eventId"].DeepClone();
                request["nativeLoadSessionId"] = work.Session;
                request["expectedMaterialRevision"] = revision;
                request["effectIntentId"] = revision.ToString(CultureInfo.InvariantCulture) + "_" + Guid.NewGuid().ToString("N");
                request["predecessorMaterialSha256"] = predecessor;
                work.Journal["phase"] = "effect_pending";
                work.Journal["effectRequest"] = request;
                string pending = work.Journal.ToString(Newtonsoft.Json.Formatting.None);
                item.DailyLateEvidenceIntentJson = pending;
                return pending;
            }).ConfigureAwait(false);
        }

        private async Task DispatchOwnedLateEffectAsync(ReignArrestCase item, LateEvidenceWork work,
            string pending, Func<bool> ownsPoint)
        {
            if (ReadLateEvidenceIntent(pending)?["effectRequest"] is not JObject saved
                || !TryReadCapturedIntentIdentity(saved, out long revision, out string intent)
                || !TryReadCapturedDay(saved, out double day)) return;
            var request = (JObject)saved.DeepClone();
            request["sharedActivityGeneration"] = work.Generation;
            request["nativeLoadSessionId"] = work.Session;
            bool activeCaptured = await ReignMainThread.InvokeAsync(() => ActiveCapturedCase(item)).ConfigureAwait(false);
            request["receiptOnly"] = !activeCaptured;
            bool Current() => ownsPoint() && _cases.Contains(item) && item.DailyLateEvidenceIntentJson == pending
                && ActiveCapturedCase(item) == activeCaptured && Hero.MainHero?.StringId == item.PlayerHeroStringId
                && ReferenceEquals(work.History, ReignServices.WorldHistory)
                && work.History.TimelineId == work.Timeline && work.History.SharedActivityGeneration == work.Generation
                && ReignSaveSyncCoordinator.CampaignGeneration == work.Alignment;
            JObject result = await ReignServerClient.SendPinnedJsonAsync(work.Origin, OwnedReclassificationRoute, request, Current)
                .ConfigureAwait(false);
            await ReignMainThread.InvokeAsync(() =>
            {
                if (!Current() || !CapturedReplyMatches(request, result) || CapturedString(result, "effectIntentId") != intent)
                    return;
                if (!activeCaptured && (CapturedString(result, "status") is "terminal_rescinded" or "receipt_missing"))
                {
                    item.DailyLateEvidenceIntentJson = string.Empty;
                    return;
                }
                if (!IsCapturedBoolean(result, "ok", true) || !IsCapturedBoolean(result, "relationshipEffectResolved", true)
                    || CapturedString(result, "status") != "committed"
                    || !TryReadCapturedRevision(result, "lateEvidenceEffectVersion", out long v) || v != 1
                    || !TryReadCapturedRevision(result, "materialRevision", out long measuredRevision) || measuredRevision != revision + 1
                    || (revision != 0 && !MatchesCaseEffectIdentity(request, result, "predecessorMaterialSha256"))
                    || !MatchesCaseEffectIdentity(request, result, "discoveryEventId")
                    || !MatchesCaseEffectIdentity(request, result, "campaignId") || !MatchesCaseEffectIdentity(request, result, "timelineId")
                    || !MatchesCaseEffectIdentity(request, result, "accusedHeroId") || !MatchesCaseEffectIdentity(request, result, "playerHeroId")
                    || !TryReadOwnedEffectAmount(result, "appliedRelationshipDelta", 100, out int applied)
                    || !TryReadOwnedEffectAmount(result, "measuredRelationshipChange", 200, out int change)
                    || !TryReadOwnedEffectAmount(result, "reputationValue", 100, out int reputation)
                    || result["effectStillCurrent"]?.Type != JTokenType.Boolean)
                    return;
                if (ActiveCapturedCase(item) && result.Value<bool>("effectStillCurrent"))
                    ApplyMeasuredCapturedRepair(item, result);
                work.Journal["phase"] = "effect_proven";
                work.Journal["effectReceipt"] = new JObject
                {
                    ["eventId"] = "arrest_settlement_" + Guid.NewGuid().ToString("N"),
                    ["effectIntentId"] = intent, ["worldDay"] = day, ["appliedRelationshipDelta"] = applied,
                    ["measuredRelationshipChange"] = change, ["reputationValue"] = reputation
                };
                item.DailyLateEvidenceIntentJson = work.Journal.ToString(Newtonsoft.Json.Formatting.None);
            }).ConfigureAwait(false);
        }

        private static bool TryReadOwnedEffectAmount(JObject result, string field, int limit, out int value)
        {
            value = 0;
            return result?[field]?.Type == JTokenType.Integer && int.TryParse(result[field].ToString(Newtonsoft.Json.Formatting.None),
                NumberStyles.Integer, CultureInfo.InvariantCulture, out value) && value >= -limit && value <= limit;
        }

        private static JObject ReadLateEvidenceIntent(string saved)
        {
            if (saved.Length == 0) return null;
            try
            {
                var value = JObject.Parse(saved, new Newtonsoft.Json.Linq.JsonLoadSettings
                    { DuplicatePropertyNameHandling = Newtonsoft.Json.Linq.DuplicatePropertyNameHandling.Error });
                return TryReadCapturedRevision(value, "schema", out long version) && version == 1 ? value : null;
            }
            catch (Newtonsoft.Json.JsonException) { return null; }
        }

        private sealed class LateEvidenceWork
        {
            internal LateEvidenceWork(string saved, JObject journal, JObject facts, ReignWorldHistoryCampaignBehavior history,
                string campaign, string timeline, string generation, string origin, int alignment, string session)
            {
                Saved = saved; Journal = journal; Facts = facts; History = history;
                Campaign = campaign; Timeline = timeline; Generation = generation; Origin = origin;
                Alignment = alignment; Session = session;
            }
            internal string Saved { get; }
            internal JObject Journal { get; }
            internal JObject Facts { get; }
            internal ReignWorldHistoryCampaignBehavior History { get; }
            internal string Campaign { get; }
            internal string Timeline { get; }
            internal string Generation { get; }
            internal string Origin { get; }
            internal int Alignment { get; }
            internal string Session { get; }
        }
    }
}
