using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ReignBeta.Integration;
using ReignBeta.Save;
using TaleWorlds.CampaignSystem;

namespace ReignBeta.Campaign
{
    /// <summary>
    /// Save-backed receipts and reversible test overrides for the explicitly
    /// enrolled social-balance campaign.  Nothing is enabled by merely loading
    /// the module: all access is through an armed live-test command whose
    /// campaign, timeline, and main-hero boundary matches the loaded save.
    /// </summary>
    public sealed class ReignSocialBalanceHarnessCampaignBehavior : CampaignBehaviorBase
    {
        private const int StateVersion = 2;
        private const int MaxCommandReceipts = 256;
        private const int MaxOverrideAuditRows = 128;
        private string _stateJson = string.Empty;
        private List<string> _stateChunks = new List<string>();
        private JObject _state;

        public static ReignSocialBalanceHarnessCampaignBehavior Instance { get; private set; }

        public override void RegisterEvents()
        {
            Instance = this;
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, _ => EnsureState());
        }

        public override void SyncData(IDataStore dataStore)
        {
            if (dataStore.IsSaving)
            {
                _stateChunks = ReignSavePayloadCodec.Encode(EnsureState().ToString(Formatting.None));
                _stateJson = string.Empty;
            }
            dataStore.SyncData("_reign_socialBalanceHarnessState", ref _stateJson);
            dataStore.SyncData("_reign_socialBalanceHarnessStateChunks", ref _stateChunks);
            if (dataStore.IsLoading)
            {
                string serialized = _stateJson;
                try
                {
                    if (_stateChunks != null && _stateChunks.Count > 0)
                        serialized = ReignSavePayloadCodec.Decode(_stateChunks);
                }
                catch
                {
                    serialized = string.Empty;
                }
                _state = ParseState(serialized);
            }
        }

        public bool ValidateEnrollment(JObject command, out string error)
        {
            error = string.Empty;
            if (command == null)
            {
                error = "The social-balance command is missing.";
                return false;
            }
            JObject enrollment = command["enrollment"] as JObject;
            if (enrollment == null)
            {
                error = "The command has no isolated campaign enrollment.";
                return false;
            }
            string expectedCampaign = enrollment.Value<string>("campaignId") ?? string.Empty;
            string expectedTimeline = enrollment.Value<string>("timelineId") ?? string.Empty;
            string expectedHero = enrollment.Value<string>("mainHeroId") ?? string.Empty;
            string savePrefix = enrollment.Value<string>("savePrefix") ?? string.Empty;
            string campaignTestRunId = enrollment.Value<string>("campaignTestRunId") ?? string.Empty;
            string disposableSaveName = enrollment.Value<string>("disposableSaveName") ?? string.Empty;
            string protectedBaselineSaveName = enrollment.Value<string>("protectedBaselineSaveName") ?? string.Empty;
            string actualCampaign = ReignCampaignIdentity.CurrentCampaignId();
            string actualTimeline = ReignWorldHistoryCampaignBehavior.Instance?.TimelineId ?? "main";
            string actualHero = Hero.MainHero?.StringId ?? string.Empty;
            if (string.IsNullOrWhiteSpace(expectedCampaign)
                || !string.Equals(expectedCampaign, actualCampaign, StringComparison.OrdinalIgnoreCase))
            {
                error = "Campaign enrollment mismatch; native mutation was refused.";
                return false;
            }
            if (string.IsNullOrWhiteSpace(expectedTimeline)
                || !string.Equals(expectedTimeline, actualTimeline, StringComparison.OrdinalIgnoreCase))
            {
                error = "Timeline enrollment mismatch; native mutation was refused.";
                return false;
            }
            if (string.IsNullOrWhiteSpace(expectedHero)
                || !string.Equals(expectedHero, actualHero, StringComparison.OrdinalIgnoreCase))
            {
                error = "Main-hero enrollment mismatch; native mutation was refused.";
                return false;
            }
            bool legacyNamespace = savePrefix.StartsWith("Reign_SocialBalance_", StringComparison.OrdinalIgnoreCase);
            bool guardedCampaignNamespace = !string.IsNullOrWhiteSpace(campaignTestRunId)
                && !string.IsNullOrWhiteSpace(protectedBaselineSaveName)
                && !string.Equals(disposableSaveName, protectedBaselineSaveName, StringComparison.OrdinalIgnoreCase)
                && string.Equals(disposableSaveName, savePrefix + "_Current", StringComparison.OrdinalIgnoreCase)
                && string.Equals(disposableSaveName, ReignServerClient.ActiveNativeSaveName(), StringComparison.OrdinalIgnoreCase);
            if (!legacyNamespace && !guardedCampaignNamespace)
            {
                error = "The enrolled save is neither a legacy Social Balance disposable nor the exact guarded campaign-test Current save.";
                return false;
            }
            JObject state = EnsureState();
            JObject savedEnrollment = state["enrollment"] as JObject;
            if (savedEnrollment == null || !savedEnrollment.HasValues)
            {
                state["enrollment"] = new JObject(enrollment);
            }
            else if (!SameEnrollment(savedEnrollment, enrollment))
            {
                bool guardedReenrollment = guardedCampaignNamespace
                    && SameCampaignIdentity(savedEnrollment, enrollment);
                if (!SameImmutableBoundary(savedEnrollment, enrollment) && !guardedReenrollment)
                {
                    error = "This save is already enrolled to a different social-balance campaign boundary"
                        + " (mismatch: " + string.Join(",", ImmutableBoundaryMismatches(savedEnrollment, enrollment)) + ").";
                    return false;
                }

                string priorRunId = savedEnrollment.Value<string>("runId") ?? string.Empty;
                string nextRunId = enrollment.Value<string>("runId") ?? string.Empty;
                state["enrollment"] = new JObject(enrollment);
                state["receipts"] = new JObject();
                state["receiptOrder"] = new JArray();
                state["overrideAudit"] = new JArray
                {
                    new JObject
                    {
                        ["kind"] = "run_reenrollment",
                        ["worldDay"] = CampaignTime.Now.ToDays,
                        ["evidence"] = new JObject
                        {
                            ["priorRunId"] = priorRunId,
                            ["nextRunId"] = nextRunId,
                            ["scope"] = guardedReenrollment
                                ? "guarded_campaign_test_reenrollment"
                                : "same_immutable_campaign_boundary",
                            ["staleRunReceiptsCleared"] = true,
                            ["staleRunOverridesCleared"] = true
                        }
                    }
                };
            }
            return true;
        }

        public bool TryGetReceipt(string commandId, out JObject result)
        {
            result = null;
            if (string.IsNullOrWhiteSpace(commandId)) return false;
            JObject receipt = (EnsureState()["receipts"] as JObject)?[commandId] as JObject;
            if (receipt == null) return false;
            result = receipt["result"] as JObject;
            return result != null;
        }

        public void RememberReceipt(string commandId, JObject result)
        {
            if (string.IsNullOrWhiteSpace(commandId) || result == null) return;
            JObject state = EnsureState();
            JObject receipts = state["receipts"] as JObject;
            JArray order = state["receiptOrder"] as JArray;
            receipts[commandId] = new JObject
            {
                ["commandId"] = commandId,
                ["recordedUtc"] = DateTime.UtcNow.ToString("o"),
                ["result"] = new JObject(result)
            };
            for (int i = order.Count - 1; i >= 0; i--)
                if (string.Equals(order[i]?.ToString(), commandId, StringComparison.OrdinalIgnoreCase))
                    order.RemoveAt(i);
            order.Add(commandId);
            while (order.Count > MaxCommandReceipts)
            {
                string oldest = order[0]?.ToString() ?? string.Empty;
                order.RemoveAt(0);
                if (!string.IsNullOrWhiteSpace(oldest)) receipts.Remove(oldest);
            }
        }

        public void RecordOverride(string kind, JObject evidence)
        {
            JObject state = EnsureState();
            JArray rows = state["overrideAudit"] as JArray;
            rows.Add(new JObject
            {
                ["kind"] = kind ?? string.Empty,
                ["worldDay"] = CampaignTime.Now.ToDays,
                ["evidence"] = evidence == null ? new JObject() : new JObject(evidence)
            });
            while (rows.Count > MaxOverrideAuditRows) rows.RemoveAt(0);
        }

        public JObject Snapshot()
        {
            JObject state = EnsureState();
            return new JObject
            {
                ["version"] = StateVersion,
                ["enrollment"] = state["enrollment"] == null ? new JObject() : state["enrollment"].DeepClone(),
                ["receiptCount"] = (state["receiptOrder"] as JArray)?.Count ?? 0,
                ["overrideAudit"] = state["overrideAudit"] == null ? new JArray() : state["overrideAudit"].DeepClone(),
                ["persistenceMarker"] = state["persistenceMarker"] == null
                    ? new JObject() : state["persistenceMarker"].DeepClone()
            };
        }

        public JObject StagePersistenceMarker(JObject command, JObject courtState)
        {
            JObject state = EnsureState();
            JObject enrollment = command?["enrollment"] as JObject ?? new JObject();
            JObject marker = new JObject
            {
                ["markerId"] = "social-persistence-" + Guid.NewGuid().ToString("N"),
                ["runId"] = enrollment.Value<string>("runId") ?? string.Empty,
                ["campaignId"] = enrollment.Value<string>("campaignId") ?? string.Empty,
                ["timelineId"] = enrollment.Value<string>("timelineId") ?? string.Empty,
                ["mainHeroId"] = enrollment.Value<string>("mainHeroId") ?? string.Empty,
                ["savePrefix"] = enrollment.Value<string>("savePrefix") ?? string.Empty,
                ["campaignTestRunId"] = enrollment.Value<string>("campaignTestRunId") ?? string.Empty,
                ["disposableSaveName"] = enrollment.Value<string>("disposableSaveName") ?? string.Empty,
                ["protectedBaselineSaveName"] = enrollment.Value<string>("protectedBaselineSaveName") ?? string.Empty,
                ["preparedGameInstanceId"] = command?.Value<string>("gameInstanceId") ?? string.Empty,
                ["preparedWorldDay"] = CampaignTime.Now.ToDays,
                ["preparedReceiptCount"] = (state["receiptOrder"] as JArray)?.Count ?? 0,
                ["preparedOverrideAuditCount"] = (state["overrideAudit"] as JArray)?.Count ?? 0,
                ["courtState"] = courtState == null ? new JObject() : courtState.DeepClone()
            };
            state["persistenceMarker"] = marker;
            return new JObject(marker);
        }

        public JObject VerifyPersistenceMarker(JObject command, JObject currentCourtState)
        {
            JObject state = EnsureState();
            JObject enrollment = command?["enrollment"] as JObject ?? new JObject();
            JObject marker = state["persistenceMarker"] as JObject ?? new JObject();
            string currentGameInstanceId = command?.Value<string>("gameInstanceId") ?? string.Empty;
            bool exactRun = !string.IsNullOrWhiteSpace(marker.Value<string>("markerId"))
                && string.Equals(marker.Value<string>("runId") ?? string.Empty,
                    enrollment.Value<string>("runId") ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            bool exactBoundary = SameImmutableBoundary(marker, enrollment);
            bool reloaded = !string.IsNullOrWhiteSpace(marker.Value<string>("preparedGameInstanceId"))
                && !string.IsNullOrWhiteSpace(currentGameInstanceId)
                && !string.Equals(marker.Value<string>("preparedGameInstanceId"),
                    currentGameInstanceId, StringComparison.OrdinalIgnoreCase);
            bool courtStatePersisted = JToken.DeepEquals(marker["courtState"] ?? new JObject(),
                currentCourtState ?? new JObject());
            int currentReceiptCount = (state["receiptOrder"] as JArray)?.Count ?? 0;
            bool receiptsPersisted = currentReceiptCount >= (marker.Value<int?>("preparedReceiptCount") ?? 0);
            return new JObject
            {
                ["verified"] = exactRun && exactBoundary && reloaded && courtStatePersisted && receiptsPersisted,
                ["marker"] = marker.DeepClone(),
                ["currentGameInstanceId"] = currentGameInstanceId,
                ["currentReceiptCount"] = currentReceiptCount,
                ["currentCourtState"] = currentCourtState == null ? new JObject() : currentCourtState.DeepClone(),
                ["assertions"] = new JObject
                {
                    ["exactPreparedRun"] = exactRun,
                    ["exactImmutableBoundary"] = exactBoundary,
                    ["differentGameInstanceAfterReload"] = reloaded,
                    ["saveBackedCourtStatePersisted"] = courtStatePersisted,
                    ["saveBackedHarnessReceiptsPersisted"] = receiptsPersisted
                }
            };
        }

        public bool ClearPersistenceMarker(string runId)
        {
            JObject state = EnsureState();
            JObject marker = state["persistenceMarker"] as JObject;
            if (marker == null || !string.Equals(marker.Value<string>("runId") ?? string.Empty,
                    runId ?? string.Empty, StringComparison.OrdinalIgnoreCase))
                return false;
            state["persistenceMarker"] = new JObject();
            return true;
        }

        private JObject EnsureState()
        {
            if (_state == null) _state = ParseState(_stateJson);
            return _state;
        }

        private static JObject ParseState(string json)
        {
            try
            {
                JObject state = string.IsNullOrWhiteSpace(json) ? new JObject() : JObject.Parse(json);
                state["version"] = StateVersion;
                if (!(state["enrollment"] is JObject)) state["enrollment"] = new JObject();
                if (!(state["receipts"] is JObject)) state["receipts"] = new JObject();
                if (!(state["receiptOrder"] is JArray)) state["receiptOrder"] = new JArray();
                if (!(state["overrideAudit"] is JArray)) state["overrideAudit"] = new JArray();
                if (!(state["persistenceMarker"] is JObject)) state["persistenceMarker"] = new JObject();
                JObject receipts = state["receipts"] as JObject;
                JArray order = state["receiptOrder"] as JArray;
                while (order.Count > MaxCommandReceipts)
                {
                    string oldest = order[0]?.ToString() ?? string.Empty;
                    order.RemoveAt(0);
                    if (!string.IsNullOrWhiteSpace(oldest)) receipts.Remove(oldest);
                }
                JArray audit = state["overrideAudit"] as JArray;
                while (audit.Count > MaxOverrideAuditRows) audit.RemoveAt(0);
                return state;
            }
            catch
            {
                return ParseState(string.Empty);
            }
        }

        private static bool SameEnrollment(JObject left, JObject right)
        {
            string[] keys = { "campaignId", "timelineId", "savePrefix", "mainHeroId", "runId",
                "campaignTestRunId", "disposableSaveName", "protectedBaselineSaveName" };
            return keys.All(key => string.Equals(left.Value<string>(key) ?? string.Empty,
                right.Value<string>(key) ?? string.Empty, StringComparison.OrdinalIgnoreCase));
        }

        private static bool SameImmutableBoundary(JObject left, JObject right)
        {
            string[] keys = { "campaignId", "timelineId", "savePrefix", "mainHeroId",
                "campaignTestRunId", "disposableSaveName", "protectedBaselineSaveName" };
            return keys.All(key => string.Equals(left.Value<string>(key) ?? string.Empty,
                right.Value<string>(key) ?? string.Empty, StringComparison.OrdinalIgnoreCase));
        }

        private static IEnumerable<string> ImmutableBoundaryMismatches(JObject left, JObject right)
        {
            string[] keys = { "campaignId", "timelineId", "savePrefix", "mainHeroId",
                "campaignTestRunId", "disposableSaveName", "protectedBaselineSaveName" };
            return keys.Where(key => !string.Equals(left.Value<string>(key) ?? string.Empty,
                right.Value<string>(key) ?? string.Empty, StringComparison.OrdinalIgnoreCase));
        }

        private static bool SameCampaignIdentity(JObject left, JObject right)
        {
            string[] keys = { "campaignId", "timelineId", "mainHeroId" };
            return keys.All(key => string.Equals(left.Value<string>(key) ?? string.Empty,
                right.Value<string>(key) ?? string.Empty, StringComparison.OrdinalIgnoreCase));
        }
    }
}
