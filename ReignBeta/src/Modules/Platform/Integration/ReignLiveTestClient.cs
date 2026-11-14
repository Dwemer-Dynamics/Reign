using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using ReignBeta.Campaign;
using ReignBeta.Runtime;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;

namespace ReignBeta.Integration
{
    public static partial class ReignServerClient
    {
        private static DateTime _nextLiveTargetCountRefreshUtc = DateTime.MinValue;
        private static int _cachedLivePartyTargetCount;
        private static int _cachedLiveEventCount;

        public static async Task<JObject> SubmitLiveTestHeartbeatAsync(string gameInstanceId, string activeRunId, string activeMode, bool busy)
        {
            long performanceGeneration = ReignLog.CapturePerformanceGeneration(TaleWorlds.CampaignSystem.Campaign.Current);
            JObject heartbeat;
            using (ReignLog.MeasureWork("diagnostics.heartbeat", 0f))
                heartbeat = BuildLiveTestHeartbeat(gameInstanceId, activeRunId, activeMode, busy);
            heartbeat["performance"] = await ReignLog.PerformanceSnapshotAsync(performanceGeneration).ConfigureAwait(false);
            return await PostJsonAsync("/tests/live/game/heartbeat", heartbeat).ConfigureAwait(false);
        }

        public static Task<JObject> PollLiveTestCommandAsync(string gameInstanceId)
        {
            return PostJsonAsync("/tests/live/game/poll", new JObject
            {
                ["campaignId"] = GetCampaignId(), ["gameInstanceId"] = gameInstanceId ?? string.Empty
            });
        }

        public static Task<JObject> GetLiveTestRunStatusAsync(string runId)
        {
            return GetJsonAsync("/tests/live/run/status?campaignId="
                + Uri.EscapeDataString(GetCampaignId()) + "&runId="
                + Uri.EscapeDataString(runId ?? string.Empty));
        }

        public static Task<JObject> AcknowledgeLiveTestCommandAsync(string gameInstanceId, string runId, string commandId, string status, string message)
        {
            return PostJsonAsync("/tests/live/game/ack", new JObject
            {
                ["campaignId"] = GetCampaignId(), ["gameInstanceId"] = gameInstanceId ?? string.Empty,
                ["runId"] = runId ?? string.Empty, ["commandId"] = commandId ?? string.Empty,
                ["status"] = status ?? "accepted", ["message"] = message ?? string.Empty
            });
        }

        public static Task<JObject> ReportLiveTestCommandAsync(
            string gameInstanceId,
            string runId,
            string commandId,
            string status,
            string message,
            string error,
            JObject result,
            IEnumerable<string> correlationIds)
        {
            return PostJsonAsync("/tests/live/game/result", new JObject
            {
                ["campaignId"] = GetCampaignId(), ["gameInstanceId"] = gameInstanceId ?? string.Empty,
                ["runId"] = runId ?? string.Empty, ["commandId"] = commandId ?? string.Empty,
                ["status"] = status ?? "completed", ["message"] = message ?? string.Empty,
                ["error"] = error ?? string.Empty, ["result"] = result ?? new JObject(),
                ["correlationIds"] = new JArray((correlationIds ?? Enumerable.Empty<string>()).Where(value => !string.IsNullOrWhiteSpace(value)))
            });
        }

        private static T HeartbeatSection<T>(JObject sections, string name, Func<T> build)
        {
            var timer = Stopwatch.StartNew();
            int g0 = GC.CollectionCount(0), g1 = GC.CollectionCount(1), g2 = GC.CollectionCount(2);
            try { return build(); }
            finally
            {
                sections[name] = new JObject { ["durationMs"] = timer.Elapsed.TotalMilliseconds,
                    ["gen0Collections"] = GC.CollectionCount(0) - g0, ["gen1Collections"] = GC.CollectionCount(1) - g1,
                    ["gen2Collections"] = GC.CollectionCount(2) - g2, ["mainThread"] = ReignMainThread.IsMainThread };
            }
        }

        private static JObject BuildLiveTestHeartbeat(string gameInstanceId, string activeRunId, string activeMode, bool busy)
        {
            var sections = new JObject();
            Settlement settlement = Settlement.CurrentSettlement ?? MobileParty.MainParty?.CurrentSettlement;
            ReignSocialEventsCampaignBehavior events = ReignSocialEventsCampaignBehavior.Instance;
            DateTime now = DateTime.UtcNow;
            if (now >= _nextLiveTargetCountRefreshUtc)
            {
                HeartbeatSection(sections, "capabilityHints", () => {
                    _cachedLivePartyTargetCount = ReignPartyChatSession.GetAvailableConversationHeroes().Count;
                    _cachedLiveEventCount = events?.GetLiveTestEventRecords().Count ?? 0;
                    return 0;
                });
                _nextLiveTargetCountRefreshUtc = now.AddSeconds(30);
            }
            Process process = Process.GetCurrentProcess();
            long privateBytes = process.PrivateMemorySize64;
            long workingSetBytes = process.WorkingSet64;
            process.Dispose();
            return new JObject
            {
                ["campaignId"] = GetCampaignId(),
                ["gameInstanceId"] = gameInstanceId ?? string.Empty,
                ["capturedUtc"] = DateTime.UtcNow.ToString("o"),
                ["worldDay"] = CampaignTime.Now.ToDays,
                ["activeSaveName"] = ActiveNativeSaveName(),
                ["playerHeroId"] = Hero.MainHero?.StringId ?? string.Empty,
                ["playerName"] = Hero.MainHero?.Name?.ToString() ?? string.Empty,
                ["playerClanId"] =
                    Hero.MainHero?.Clan?.StringId
                    ?? string.Empty,
                ["playerClanTier"] =
                    Hero.MainHero?.Clan?.Tier
                    ?? 0,
                ["playerKingdomId"] =
                    Hero.MainHero?.Clan?.Kingdom?.StringId
                    ?? string.Empty,
                ["playerIsRuler"] =
                    Hero.MainHero != null
                    && Hero.MainHero.Clan?.Kingdom?.Leader
                        == Hero.MainHero,
                ["playerIsLord"] =
                    Hero.MainHero?.IsLord == true,
                ["playerIsFemale"] =
                    Hero.MainHero?.IsFemale == true,
                ["playerIsNotable"] =
                    Hero.MainHero?.IsNotable == true,
                ["playerIsWanderer"] =
                    Hero.MainHero?.IsWanderer == true,
                ["playerOccupation"] =
                    Hero.MainHero?.Occupation.ToString()
                    ?? string.Empty,
                ["playerGovernorOfSettlementId"] =
                    Hero.MainHero?.GovernorOf?.Settlement?.StringId
                    ?? string.Empty,
                ["location"] = new JObject
                {
                    ["settlementId"] = settlement?.StringId ?? string.Empty,
                    ["settlementName"] = settlement?.Name?.ToString() ?? string.Empty,
                    ["inTown"] = settlement?.Town != null && settlement.IsTown,
                    ["onOpenMap"] = settlement == null && MobileParty.MainParty?.MapEvent == null && MobileParty.MainParty?.SiegeEvent == null
                },
                ["saveSync"] = new JObject
                {
                    ["alignmentPending"] = ReignSaveSyncCoordinator.IsAlignmentPending,
                    ["ready"] = ReignSaveSyncCoordinator.IsReadyForCampaign(GetCampaignId()),
                    ["timelineId"] = ReignWorldHistoryCampaignBehavior.Instance?.TimelineId ?? "main",
                    ["saveFinalization"] = HeartbeatSection(sections, "saveFinalization", () => ReignSaveSyncCoordinator.SaveFinalizationSnapshot())
                },
                ["diplomacy"] = BuildDiplomacyDiagnostics(),
                ["government"] = HeartbeatSection(sections, "government", () => ReignBeta.Government.ReignGovernmentCampaignBehavior.Instance?.PoliticalRuntimeStatus() ?? new JObject()),
                ["rulers"] = HeartbeatSection(sections, "rulers", () => ReignRulerCampaignBehavior.Instance?.RuntimeStatus() ?? new JObject()),
                ["clanAccords"] = new JObject
                {
                    ["schema"] = "reign-clan-accords-runtime-v1",
                    ["available"] = ReignBeta.ClanAccords.ReignClanAccordsCampaignBehavior.Instance != null,
                    ["pending"] = ReignBeta.ClanAccords.ReignClanAccordsCampaignBehavior.Instance?.HasPendingWork == true,
                    ["snapshot"] = HeartbeatSection(sections, "clanAccords", () => ReignBeta.ClanAccords.ReignClanAccordsCampaignBehavior.Instance?.Snapshot() ?? new JObject())
                },
                ["encounteredResidents"] = HeartbeatSection(sections, "residents", () => ReignEncounteredResidentsCampaignBehavior.Instance?.RuntimeSnapshot() ?? new JObject()),
                ["wandererPopulation"] = HeartbeatSection(sections, "wanderers", () => ReignWandererPopulationCampaignBehavior.Instance?.RuntimeSnapshot() ?? new JObject()),
                ["tavernHousePopulation"] = ReignTavernHouseCampaignBehavior.Instance?.RuntimeStatus() ?? new JObject(),
                ["courtLife"] = new JObject
                {
                    ["schema"] = "reign-court-life-runtime-v1",
                    ["deliveryPending"] = ReignBeta.Court.ReignCourtCampaignBehavior.Instance?.HasPendingCourtLifeDeliveryWork == true,
                    ["internationalPending"] = ReignBeta.Court.ReignCourtCampaignBehavior.Instance?.HasPendingInternationalCourtLifeWork == true,
                    ["familyAttentionPending"] = ReignBeta.Court.ReignCourtCampaignBehavior.Instance?.HasPendingFamilyVisitWork == true,
                    ["familyVisitOutbox"] = ReignBeta.Court.ReignCourtCampaignBehavior.Instance?.RulerDocketState.FamilyVisitOutbox?.Count ?? 0
                },
                ["courtUi"] = HeartbeatSection(sections, "courtUi", () => JObject.FromObject(ReignBeta.UI.ReignCourtScreenManager.AutomationLayoutSnapshot)),
                ["relationshipClient"] = HeartbeatSection(sections, "relationships", () => BuildRelationshipOutboxDiagnostics()),
                ["personalGossip"] = HeartbeatSection(sections, "gossip", () => PersonalGossipRuntimeStatus()),
                ["worldHistory"] = new JObject
                {
                    ["pendingCount"] = ReignWorldHistoryTransport.PendingCount(),
                    ["uploadInFlight"] = ReignWorldHistoryTransport.UploadInFlight,
                    ["sequence"] =
                        ReignWorldHistoryCampaignBehavior.Instance?.Sequence ?? 0L,
                    ["upload"] = HeartbeatSection(sections, "worldHistory", () => ReignWorldHistoryTransport.UploadDiagnostics())
                },
                ["nativeSave"] = HeartbeatSection(sections, "nativeSave", () => BuildNativeSaveDiagnostics()),
                ["mainThreadDispatch"] = new JObject
                {
                    ["pendingCount"] = ReignMainThread.PendingCount,
                    ["executedCount"] = ReignMainThread.ExecutedCount,
                    ["lastDrainUtc"] = UtcText(ReignMainThread.LastDrainUtc),
                    ["lastExecutedUtc"] = UtcText(ReignMainThread.LastExecutedUtc),
                    ["slowItemCount"] = ReignMainThread.SlowItemCount,
                    ["lastSlowItemDurationMs"] = ReignMainThread.LastSlowItemDurationMs,
                    ["lastSlowItemSource"] = ReignMainThread.LastSlowItemSource
                },
                ["processMemory"] = new JObject
                {
                    ["privateBytes"] = privateBytes,
                    ["workingSetBytes"] = workingSetBytes,
                    ["managedBytes"] = GC.GetTotalMemory(false),
                    ["gen0Collections"] = GC.CollectionCount(0),
                    ["gen1Collections"] = GC.CollectionCount(1),
                    ["gen2Collections"] = GC.CollectionCount(2),
                    ["completedCommandCacheCount"] = ReignLiveInteractionTestHost.CompletedCommandCacheCount
                },
                ["httpTransport"] = HttpTransportDiagnostics(),
                ["characterInitialization"] = new JObject
                {
                    ["startingChildren"] = HeartbeatSection(sections, "startingChildren", () => ReignStartingChildrenCampaignBehavior.Instance?.Snapshot() ?? new JObject()),
                    ["notableBackgroundReady"] =
                        ReignCharacterEditorCampaignBehavior.Instance?.NotableBackgroundInitializationComplete == true,
                    ["notableBackgroundInFlight"] =
                        ReignCharacterEditorCampaignBehavior.Instance?.NotableBackgroundInitializationInFlight == true,
                    ["policy"] = "campaign_day_1_before_reign_systems",
                    ["gate"] = ReignCampaignInitializationGate.Snapshot(),
                    ["preparation"] =
                        ReignCampaignPreparationCampaignBehavior.Instance?.Snapshot()
                        ?? new JObject()
                },
                ["activeRunId"] = activeRunId ?? string.Empty,
                ["activeMode"] = activeMode ?? string.Empty,
                ["busy"] = busy,
                // These are capability hints, not command-resolution evidence.
                // Search/open commands always resolve fresh native state.
                ["partyTargetCount"] = _cachedLivePartyTargetCount,
                ["eventCount"] = _cachedLiveEventCount,
                ["capabilities"] = new JArray(
                    ReignLiveInteractionTestHost.GetSupportedModes()),
                ["diagnosticSections"] = sections,
                ["headlessSupported"] = true,
                ["visibleSupported"] = true
            };
        }

        internal static string ActiveNativeSaveName()
        {
            try
            {
                object value = typeof(MBSaveLoad).GetProperty("ActiveSaveSlotName",
                    BindingFlags.Public | BindingFlags.Static)?.GetValue(null, null);
                return Convert.ToString(value) ?? string.Empty;
            }
            catch { return string.Empty; }
        }

        private static JObject BuildNativeSaveDiagnostics()
        {
            JObject observation = ReignSaveSyncCampaignBehavior.NativeSaveObservation();
            observation["isSaving"] = TaleWorlds.CampaignSystem.Campaign.Current?.SaveHandler?.IsSaving == true;
            return observation;
        }

        private static JObject BuildDiplomacyDiagnostics()
        {
            ReignWorldDiplomacyCampaignBehavior behavior =
                ReignWorldDiplomacyCampaignBehavior.Instance;
            return new JObject
            {
                ["evaluationInFlight"] = behavior?.EvaluationInFlight == true,
                ["announcementPollInFlight"] = behavior?.AnnouncementPollInFlight == true,
                ["pressurePollInFlight"] = behavior?.PressurePollInFlight == true,
                ["inFlight"] = behavior?.HasInFlightRequest == true
            };
        }

        private static string UtcText(DateTime value)
        {
            return value == DateTime.MinValue ? string.Empty : value.ToUniversalTime().ToString("o");
        }
    }
}
