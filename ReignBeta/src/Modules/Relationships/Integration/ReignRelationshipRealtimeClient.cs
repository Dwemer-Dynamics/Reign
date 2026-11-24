using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using ReignBeta.Campaign;
using TaleWorlds.CampaignSystem;
using static ReignBeta.Platform.Integration.ReignServerClient;
using ReignBeta.Platform.Integration;
using ReignBeta.Relationships.Campaign;
using ReignBeta.WorldSimulation.Campaign;
using ReignBeta.Platform.Runtime;

namespace ReignBeta.Relationships.Integration
{
    internal static class ReignRelationshipRealtimeClient
    {
        private const int RelationshipNativePullBatchSize = 1024;
        internal static bool RelationshipOutboxUploadInFlight;
        private static bool RelationshipNativePullInFlight;
        private static bool RelationshipNativeReceiptInFlight;
        private static DateTime RelationshipLastOutboxAttemptUtc = DateTime.MinValue;
        private static int RelationshipOutboxIntervalMs = 1000;
        private static DateTime RelationshipLastNativePullUtc = DateTime.MinValue;
        private static int RelationshipNativePullIntervalMs = 1000;

        public static void RelationshipRealtimeTick(float deltaTime)
        {
            if (TaleWorlds.CampaignSystem.Campaign.Current == null || Hero.MainHero == null) return;
            DateTime now = DateTime.UtcNow;
            if (!RelationshipOutboxUploadInFlight
                && now - RelationshipLastOutboxAttemptUtc >= TimeSpan.FromMilliseconds(RelationshipOutboxIntervalMs))
            {
                RelationshipLastOutboxAttemptUtc = now;
                RelationshipOutboxUploadInFlight = true;
                _ = PumpRelationshipOutboxAsync();
            }
            if (!RelationshipNativePullInFlight
                && now - RelationshipLastNativePullUtc >= TimeSpan.FromMilliseconds(RelationshipNativePullIntervalMs))
            {
                RelationshipLastNativePullUtc = now;
                RelationshipNativePullInFlight = true;
                _ = PullRelationshipNativeTargetsAsync();
            }
            if (!RelationshipNativeReceiptInFlight)
            {
                JArray receipts = ReignServices.Relationship?.DrainNativeSyncReceipts();
                if (receipts != null && receipts.Count > 0)
                {
                    RelationshipNativeReceiptInFlight = true;
                    _ = ReportRelationshipNativeReceiptsAsync(receipts);
                }
            }
        }

        private static async Task PumpRelationshipOutboxAsync()
        {
            try
            {
                ReignAmbientRelationshipRunResult result =
                    await ReignRelationshipDirectorClient.SubmitAmbientRelationshipSnapshotAsync().ConfigureAwait(false);
                RelationshipOutboxIntervalMs = result.Ok && !result.Idempotent ? 100 : 1000;
            }
            catch (Exception ex)
            {
                RelationshipOutboxIntervalMs = 2000;
                ReignLog.Warn("Real-time relationship outbox upload failed: " + ex.Message);
            }
            finally
            {
                RelationshipOutboxUploadInFlight = false;
            }
        }

        internal static async Task PullRelationshipNativeTargetsAsync(Func<bool> requestOwner = null)
        {
            try
            {
                var scope = await ReignMainThread.InvokeAsync(() =>
                {
                    ReignRequestOwnership.Ensure(requestOwner, "capturing native relationship targets");
                    return (Fence: RequestFence.Capture(), Behavior: ReignServices.Relationship,
                        Campaign: GetCampaignId(), Timeline: ReignServices.WorldHistory?.TimelineId ?? "main",
                        Endpoint: ReignServerEndpoint.ConfiguredBaseUrl(),
                        WorldDay: ReignRelationshipDirectorClient.CurrentDirectorDay());
                }).ConfigureAwait(false);

                bool Owns() => scope.Behavior != null && ReferenceEquals(scope.Behavior, ReignServices.Relationship)
                    && scope.Fence.IsCurrentActionPoll()
                    && string.Equals(scope.Endpoint, ReignServerEndpoint.ConfiguredBaseUrl(), StringComparison.Ordinal)
                    && ReignRequestOwnership.IsCurrent(requestOwner);
                JObject response = await PostOwnedJsonAsync("/relationships/ambient/native_sync/pull", new JObject
                {
                    ["campaignId"] = scope.Campaign,
                    ["timelineId"] = scope.Timeline,
                    ["limit"] = RelationshipNativePullBatchSize,
                    ["worldDay"] = scope.WorldDay
                }, Owns).ConfigureAwait(false);
                if (response?.Value<bool?>("ok") != true)
                    throw new InvalidOperationException(response?.Value<string>("error")
                        ?? "Native relationship pull was rejected.");
                List<ReignNativeRelationSyncTarget> targets =
                    (response["targets"] as JArray ?? new JArray()).OfType<JObject>()
                    .Select(row => new ReignNativeRelationSyncTarget
                    {
                        PairKey = row.Value<string>("pairKey") ?? string.Empty,
                        HeroAId = row.Value<string>("heroAId") ?? string.Empty,
                        HeroBId = row.Value<string>("heroBId") ?? string.Empty,
                        TargetRelation = row.Value<int?>("targetRelation") ?? 0,
                        Revision = row.Value<int?>("revision") ?? 1
                    }).ToList();
                await ReignMainThread.InvokeAsync(() =>
                {
                    ReignRequestOwnership.Ensure(Owns, "applying native relationship targets");
                    scope.Behavior.MergeNativeSyncTargets(scope.Timeline, targets);
                    RelationshipNativePullIntervalMs = targets.Count >= RelationshipNativePullBatchSize
                        ? 15
                        : targets.Count > 0 ? 50 : 10000;
                }).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                RelationshipNativePullIntervalMs = 2000;
                ReignLog.Warn("Real-time native relationship pull failed: " + ex.Message);
            }
            finally
            {
                RelationshipNativePullInFlight = false;
            }
        }

        private static async Task ReportRelationshipNativeReceiptsAsync(JArray receipts)
        {
            try
            {
                JObject response = await PostJsonAsync("/relationships/ambient/native_sync/receipts", new JObject
                {
                    ["campaignId"] = GetCampaignId(),
                    ["timelineId"] = ReignServices.WorldHistory?.TimelineId ?? "main",
                    ["worldDay"] = ReignRelationshipDirectorClient.CurrentDirectorDay(),
                    ["receipts"] = receipts
                }).ConfigureAwait(false);
                if (response.Value<bool?>("ok") != true)
                    throw new InvalidOperationException(response.Value<string>("error") ?? "Native receipt batch was rejected.");
            }
            catch (Exception ex)
            {
                ReignLog.Warn("Real-time native relationship receipt failed: " + ex.Message);
                // A claimed target becomes pullable again after the server lease.
            }
            finally
            {
                RelationshipNativeReceiptInFlight = false;
            }
        }

        internal static async Task<bool> PumpInitializationNativeReceiptsAsync()
        {
            ReignRelationshipCampaignBehavior behavior =
                ReignServices.Relationship;
            JArray receipts = behavior?.DrainNativeSyncReceipts() ?? new JArray();
            if (receipts.Count == 0) return true;
            try
            {
                JObject response = await PostJsonAsync(
                    "/relationships/ambient/native_sync/receipts",
                    new JObject
                    {
                        ["campaignId"] = GetCampaignId(),
                        ["timelineId"] =
                            ReignServices.WorldHistory?.TimelineId ?? "main",
                        ["worldDay"] = ReignRelationshipDirectorClient.CurrentDirectorDay(),
                        ["receipts"] = receipts
                    }).ConfigureAwait(false);
                if (response.Value<bool?>("ok") != true)
                    throw new InvalidOperationException(
                        response.Value<string>("error")
                        ?? "Native receipt batch was rejected.");
                return true;
            }
            catch
            {
                behavior?.RequeueNativeSyncReceipts(receipts);
                throw;
            }
        }
    }
}
