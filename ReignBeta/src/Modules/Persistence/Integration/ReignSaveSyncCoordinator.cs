using System;
using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ReignBeta.Runtime;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace ReignBeta.Integration
{
    internal static class ReignSaveSyncCoordinator
    {
        private static readonly object Gate = new object();
        private static readonly HttpClient Client = new HttpClient(new Reign.Core.Contracts.Platform.ReignProtocolHandler(new HttpClientHandler { UseProxy = false })) { Timeout = TimeSpan.FromMinutes(6) };
        private static TaskCompletionSource<bool> _ready = ReadySource(true);
        private static int _generation;
        private static string _campaignId = string.Empty;
        private static bool _alignmentInitialized;
        private static int _saveFinalizationsInFlight;
        private static string _lastFinalizeRequestedSaveName = string.Empty;
        private static string _lastFinalizedSaveName = string.Empty;
        private static string _lastFinalizeResult = string.Empty;
        private static DateTime _lastFinalizeCompletedUtc = DateTime.MinValue;
        private static bool _lastFinalizeSucceeded;
        private static bool _lastFinalizeStorageWarning;
        private static int _lastFinalizeUniqueStateCount;
        private static int _lastFinalizeUniqueStateLimit;
        private static int _lastFinalizeRemainingUniqueStates;
        private static bool _automationSaveNotificationSuppressed;
        private static string _lastObservedCampaignId = string.Empty;
        private static readonly System.Collections.Generic.HashSet<string> CapacityWarningsShown =
            new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);

        internal static bool IsAlignmentPending
        {
            get
            {
                lock (Gate) return !_ready.Task.IsCompleted;
            }
        }

        internal static bool IsReadyForCampaign(string campaignId)
        {
            lock (Gate)
            {
                return _alignmentInitialized
                    && _ready.Task.IsCompleted
                    && (string.IsNullOrWhiteSpace(_campaignId)
                        || string.Equals(_campaignId, campaignId ?? string.Empty, StringComparison.OrdinalIgnoreCase));
            }
        }

        internal static JObject SaveFinalizationSnapshot()
        {
            lock (Gate)
            {
                return new JObject
                {
                    ["inFlight"] = _saveFinalizationsInFlight,
                    ["requestedSaveName"] = _lastFinalizeRequestedSaveName,
                    ["completedSaveName"] = _lastFinalizedSaveName,
                    ["result"] = _lastFinalizeResult,
                    ["completedUtc"] = _lastFinalizeCompletedUtc == DateTime.MinValue
                        ? string.Empty
                        : _lastFinalizeCompletedUtc.ToUniversalTime().ToString("o"),
                    ["succeeded"] = _lastFinalizeSucceeded,
                    ["storageWarning"] = _lastFinalizeStorageWarning,
                    ["uniqueStateCount"] = _lastFinalizeUniqueStateCount,
                    ["uniqueStateLimit"] = _lastFinalizeUniqueStateLimit,
                    ["remainingUniqueStates"] = _lastFinalizeRemainingUniqueStates
                };
            }
        }

        internal static void SetAutomationSaveNotificationSuppressed(bool suppressed)
        {
            lock (Gate) _automationSaveNotificationSuppressed = suppressed;
        }

        internal static void MarkLoadedSavePending()
        {
            lock (Gate)
            {
                _generation++;
                _campaignId = string.Empty;
                _alignmentInitialized = false;
                _ready = ReadySource(false);
            }
        }

        internal static void MarkReadyForNewCampaign() { MarkReadyWithoutSync(); }

        internal static void MarkReadyWithoutSync()
        {
            lock (Gate)
            {
                _generation++;
                _campaignId = string.Empty;
                _alignmentInitialized = true;
                _ready.TrySetResult(true);
                _ready = ReadySource(true);
            }
        }

        internal static void BeginLoadedSave(JObject payload)
        {
            if (payload == null) { MarkReadyWithoutSync(); return; }
            int generation;
            JObject loadPayload = (JObject)payload.DeepClone();
            // The same native load may outlive the local HTTP timeout while the
            // server restores a large PostgreSQL snapshot. Preserve one identity
            // across retries so the server can replay the completed receipt
            // instead of performing the destructive rollback a second time.
            loadPayload["loadSessionId"] = Guid.NewGuid().ToString("N");
            lock (Gate)
            {
                generation = ++_generation;
                _campaignId = payload.Value<string>("campaignId") ?? string.Empty;
                _alignmentInitialized = true;
                _ready = ReadySource(false);
            }
            _ = Task.Run(() => RunLoadHandshakeAsync(generation, loadPayload));
        }

        internal static async Task WaitForReadyAsync(string route)
        {
            if (IsSaveSyncRoute(route)) return;
            Task task;
            lock (Gate) task = _ready.Task;
            if (task.IsCompleted) { await task.ConfigureAwait(false); return; }
            Task completed = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(15))).ConfigureAwait(false);
            if (completed != task) throw new HttpRequestException("Save Sync is still aligning Reign with the loaded Bannerlord save.");
            await task.ConfigureAwait(false);
        }

        internal static void RunWhenReadyOnMainThread(Action action)
        {
            if (action == null) return;
            _ = Task.Run(async () =>
            {
                try
                {
                    await WaitForReadyAsync("calendar_initialize").ConfigureAwait(false);
                    await ReignMainThread.InvokeAsync(action).ConfigureAwait(false);
                }
                catch (Exception ex) { ReignLog.Warn("Save Sync readiness action deferred: " + ex.Message); }
            });
        }

        internal static void RunAfterCurrentAlignmentOnMainThread(Action action)
        {
            if (action == null) return;
            int generation;
            lock (Gate) generation = _generation;
            _ = Task.Run(async () =>
            {
                try
                {
                    while (true)
                    {
                        bool ready;
                        lock (Gate)
                        {
                            if (generation != _generation) return;
                            ready = _ready.Task.IsCompleted;
                        }
                        if (ready) break;
                        await Task.Delay(250).ConfigureAwait(false);
                    }
                    lock (Gate) if (generation != _generation) return;
                    await ReignMainThread.InvokeAsync(action).ConfigureAwait(false);
                }
                catch (Exception ex) { ReignLog.Warn("Save Sync completion action failed: " + ex.Message); }
            });
        }

        internal static Task<JObject> RegisterBeforeSaveAsync(JObject payload)
        {
            return PostDirectAsync("/save-sync/register", payload);
        }

        internal static void FinalizeSavePoint(string campaignId, string savePointId, bool successful, string nativeSaveName)
        {
            lock (Gate)
            {
                _saveFinalizationsInFlight++;
                _lastFinalizeRequestedSaveName = nativeSaveName ?? string.Empty;
            }
            _ = Task.Run(async () =>
            {
                try
                {
                    JObject response = await PostDirectAsync("/save-sync/finalize", new JObject
                    {
                        ["campaignId"] = campaignId ?? string.Empty, ["savePointId"] = savePointId ?? string.Empty,
                        ["successful"] = successful, ["nativeSaveName"] = nativeSaveName ?? string.Empty
                    }).ConfigureAwait(false);
                    lock (Gate)
                    {
                        _lastFinalizedSaveName = nativeSaveName ?? string.Empty;
                        _lastFinalizeResult = response?.Value<string>("result") ?? string.Empty;
                        _lastFinalizeSucceeded = response?.Value<bool?>("ok") == true && successful;
                        _lastFinalizeCompletedUtc = DateTime.UtcNow;
                        _lastFinalizeStorageWarning = response?.Value<bool?>("storageWarning") == true;
                        _lastFinalizeUniqueStateCount = response?.Value<int?>("uniqueStateCount") ?? 0;
                        _lastFinalizeUniqueStateLimit = response?.Value<int?>("uniqueStateLimit") ?? 15;
                        _lastFinalizeRemainingUniqueStates = Math.Max(
                            0,
                            response?.Value<int?>("remainingUniqueStates")
                                ?? (_lastFinalizeUniqueStateLimit - _lastFinalizeUniqueStateCount));
                    }
                    if (successful && response?.Value<bool?>("storageWarning") == true)
                    {
                        int count = response.Value<int?>("uniqueStateCount") ?? 0;
                        int limit = response.Value<int?>("uniqueStateLimit") ?? 15;
                        int remaining = Math.Max(0, response.Value<int?>("remainingUniqueStates") ?? (limit - count));
                        bool show;
                        lock (Gate)
                        {
                            show = !_automationSaveNotificationSuppressed
                                && CapacityWarningsShown.Add(campaignId ?? string.Empty);
                        }
                        if (show)
                        {
                            await ReignMainThread.InvokeAsync(() =>
                            {
                                InformationManager.ShowInquiry(new InquiryData(
                                    "Bannerlord Reign Save Sync Storage",
                                    "Save Sync is retaining " + count + " of " + limit + " unique campaign states. Only "
                                        + remaining + " snapshot slots remain.\n\nDelete old save games from Bannerlord's native Load/Save screen. When Bannerlord confirms a save was deleted, Reign will automatically delete its matching server snapshot.",
                                    true,
                                    false,
                                    "Acknowledge",
                                    string.Empty,
                                    null,
                                    null),
                                true);
                            }).ConfigureAwait(false);
                        }
                        else
                        {
                            ReignLog.Info("Save Sync storage warning was recorded without opening a modal during an automated checkpoint: "
                                + count + " of " + limit + " unique states, " + remaining + " remaining.");
                        }
                    }
                }
                catch (Exception ex)
                {
                    lock (Gate)
                    {
                        _lastFinalizedSaveName = nativeSaveName ?? string.Empty;
                        _lastFinalizeResult = "failed: " + ex.Message;
                        _lastFinalizeSucceeded = false;
                        _lastFinalizeCompletedUtc = DateTime.UtcNow;
                        _lastFinalizeStorageWarning = false;
                    }
                    ReignLog.Warn("Save Sync save finalization will remain recoverable from its registered snapshot: " + ex.Message);
                }
                finally
                {
                    lock (Gate) _saveFinalizationsInFlight = Math.Max(0, _saveFinalizationsInFlight - 1);
                }
            });
        }

        internal static void DeleteNativeSaveSnapshot(string campaignId, string savePointId, string nativeSaveName)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    JObject response = await PostDirectAsync("/save-sync/delete-native-save", new JObject
                    {
                        ["campaignId"] = campaignId ?? string.Empty,
                        ["savePointId"] = savePointId ?? string.Empty,
                        ["nativeSaveName"] = nativeSaveName ?? string.Empty,
                        ["campaignLoaded"] = CurrentCampaignMatches(campaignId)
                    }).ConfigureAwait(false);
                    ReignLog.Info("Save Sync native deletion result="
                        + (response?.Value<string>("result") ?? "")
                        + " save=" + (nativeSaveName ?? "")
                        + " point=" + (savePointId ?? "")
                        + " campaignDeleted=" + (response?.Value<bool?>("campaignDeleted") ?? false)
                        + " retirementPending=" + (response?.Value<bool?>("campaignDeletionPending") ?? false));
                }
                catch (Exception ex)
                {
                    ReignLog.Warn("Bannerlord deleted native save '" + (nativeSaveName ?? "")
                        + "', but Reign could not delete its server snapshot yet: " + ex.Message);
                }
            });
        }

        internal static void ObserveCampaignLifecycle()
        {
            string currentCampaignId;
            try
            {
                currentCampaignId = ReignCampaignIdentity.CurrentCampaignId()
                    ?? string.Empty;
            }
            catch
            {
                currentCampaignId = string.Empty;
            }

            string unloadedCampaignId = string.Empty;
            lock (Gate)
            {
                if (string.Equals(_lastObservedCampaignId, currentCampaignId,
                    StringComparison.OrdinalIgnoreCase)) return;
                if (!string.IsNullOrWhiteSpace(_lastObservedCampaignId))
                    unloadedCampaignId = _lastObservedCampaignId;
                _lastObservedCampaignId = currentCampaignId;
            }
            if (string.IsNullOrWhiteSpace(unloadedCampaignId)) return;

            _ = Task.Run(async () =>
            {
                Exception last = null;
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    try
                    {
                        JObject response = await PostDirectAsync(
                            "/save-sync/campaign-unloaded", new JObject
                            {
                                ["campaignId"] = unloadedCampaignId
                            }).ConfigureAwait(false);
                        ReignLog.Info("Save Sync campaign unload acknowledged for "
                            + unloadedCampaignId + ": "
                            + (response?.Value<string>("result") ?? ""));
                        return;
                    }
                    catch (Exception ex)
                    {
                        last = ex;
                        if (attempt < 2)
                            await Task.Delay(500 * (attempt + 1)).ConfigureAwait(false);
                    }
                }
                ReignLog.Warn("Save Sync could not report campaign unload for "
                    + unloadedCampaignId
                    + ". A pending final-save retirement will retry after game exit or server startup: "
                    + (last?.Message ?? "unknown error"));
            });
        }

        private static async Task RunLoadHandshakeAsync(int generation, JObject payload)
        {
            int failures = 0;
            bool waitingForCampaignIdentityLogged = false;
            string campaignId = payload.Value<string>("campaignId") ?? string.Empty;
            while (IsCurrentGeneration(generation, campaignId))
            {
                if (!CurrentCampaignMatches(campaignId))
                {
                    if (!waitingForCampaignIdentityLogged)
                    {
                        waitingForCampaignIdentityLogged = true;
                        ReignLog.Info("Save Sync is waiting for Bannerlord's loaded campaign identity to stabilize before alignment.");
                    }
                    await Task.Delay(250).ConfigureAwait(false);
                    continue;
                }
                if (waitingForCampaignIdentityLogged)
                {
                    waitingForCampaignIdentityLogged = false;
                    ReignLog.Info("Save Sync detected the loaded campaign identity and is beginning alignment.");
                }

                try
                {
                    JObject response = await PostDirectAsync("/save-sync/load", payload).ConfigureAwait(false);
                    string result = response?.Value<string>("result") ?? string.Empty;
                    if (response?.Value<bool?>("ok") == true)
                    {
                        if (result == "rolled_back")
                        {
                            await ReignMainThread.InvokeAsync(() =>
                            {
                                ReignCalendarService.Reset();
                            }).ConfigureAwait(false);
                        }
                        CompleteIfCurrent(generation);
                        ReignLog.Info("Save Sync load result=" + result + " campaign=" + (payload.Value<string>("campaignId") ?? "") + " point=" + (payload.Value<string>("savePointId") ?? ""));
                        return;
                    }
                    if (result == "snapshot_missing" && payload.Value<bool?>("registrationConfirmed") != true)
                    {
                        CompleteIfCurrent(generation);
                        ReignLog.Warn("Save Sync released an unregistered native save as a degraded baseline; current Reign campaign data was left untouched.");
                        return;
                    }
                    failures++;
                    ReignLog.Warn("Save Sync load attempt did not complete safely: " + (response?.Value<string>("error") ?? result));
                }
                catch (Exception ex)
                {
                    failures++;
                    ReignLog.Warn("Save Sync load handshake deferred: " + ex.Message);
                }
                int seconds = Math.Min(120, 5 * (1 << Math.Min(4, Math.Max(0, failures - 1))));
                await Task.Delay(TimeSpan.FromSeconds(seconds)).ConfigureAwait(false);
            }
        }

        private static async Task<JObject> PostDirectAsync(string route, JObject payload)
        {
            string json = (payload ?? new JObject()).ToString(Formatting.None);
            Exception last = null;
            foreach (string baseUrl in ReignServerEndpoint.CandidateBaseUrls())
            {
                string url = baseUrl.TrimEnd('/') + (route.StartsWith("/", StringComparison.Ordinal) ? route : "/" + route);
                Stopwatch timer = Stopwatch.StartNew();
                try
                {
                    using (StringContent content = new StringContent(json, Encoding.UTF8, "application/json"))
                    using (HttpResponseMessage response = await Client.PostAsync(url, content).ConfigureAwait(false))
                    {
                        string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        timer.Stop();
                        ReignServerEndpoint.ReportSuccess(baseUrl);
                        ReignLog.Info("HTTP POST " + route + " status=" + (int)response.StatusCode + " clientMs=" + timer.ElapsedMilliseconds);
                        if (!response.IsSuccessStatusCode) throw new InvalidOperationException("HTTP " + (int)response.StatusCode + ": " + body);
                        return JObject.Parse(body);
                    }
                }
                catch (Exception ex) when (ReignServerEndpoint.IsTransportFailure(ex)) { last = ex; }
            }
            ReignServerEndpoint.ReportTransportFailure();
            throw last ?? new HttpRequestException("Local Bannerlord Reign server is unavailable.");
        }

        private static bool IsCurrentGeneration(int generation, string campaignId)
        {
            lock (Gate)
            {
                return generation == _generation && string.Equals(_campaignId, campaignId ?? string.Empty, StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(_campaignId);
            }
        }

        private static bool CurrentCampaignMatches(string campaignId)
        {
            return string.Equals(
                ReignCampaignIdentity.CurrentCampaignId(),
                campaignId ?? string.Empty,
                StringComparison.OrdinalIgnoreCase);
        }

        private static void CompleteIfCurrent(int generation)
        {
            lock (Gate) if (generation == _generation) _ready.TrySetResult(true);
        }

        private static bool IsSaveSyncRoute(string route)
        {
            return !string.IsNullOrWhiteSpace(route) && route.IndexOf("save-sync", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static TaskCompletionSource<bool> ReadySource(bool completed)
        {
            TaskCompletionSource<bool> source = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (completed) source.TrySetResult(true);
            return source;
        }
    }
}
