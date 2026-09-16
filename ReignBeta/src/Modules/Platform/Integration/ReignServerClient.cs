using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ReignBeta.Campaign;
using ReignBeta.Court;
using ReignBeta.Events;
using ReignBeta.Government;
using ReignBeta.Runtime;
using ReignBeta.Settings;
using ReignBeta.World;
using ReignPortraits;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.Settlements.Workshops;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;

namespace ReignBeta.Integration
{
    public static partial class ReignServerClient
    {
        private const int InteractiveProviderTimeoutSeconds = 900;
        private static readonly HttpClient Client = CreateLocalServerHttpClient(8);
        private static readonly HttpClient PriorityClient = CreateLocalServerHttpClient(4);
        private static readonly HttpClient InteractiveClient =
            CreateLocalServerHttpClient(4, InteractiveProviderTimeoutSeconds);
        private static readonly HttpClient LiveTestClient = CreateLocalServerHttpClient(4, 5);
        private static readonly object NativeDescriptionLock = new object();
        private static readonly Dictionary<string, string> NativeDescriptions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static readonly object PortraitRosterSyncLock = new object();
        private static readonly Dictionary<string, string> SyncedPortraitRosterFingerprints =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static long _streamedPostRequests;
        private static long _streamedPostBytes;
        private static long _largeStreamedPostRequests;
        private static long _peakStreamedPostBytes;
        private static long _totalPostSerializationMs;
        private static long _peakPostSerializationMs;
        private static readonly string[] ContextPullIds =
        {
            "check_inventory_appearance",
            "check_player_appearance_status",
            "nearby_settlements",
            "nearby_bandit_parties",
            "nearby_lord_parties",
            "current_settlement_facts",
            "kingdom_diplomacy_status",
            "clan_wealth_and_influence",
            "appraise_trade_offer",
            "verify_world_history",
            "relevant_memory",
            "relationship_history"
        };

        private static HttpClient CreateLocalServerHttpClient(int maximumConnections, int timeoutSeconds = 360)
        {
            // Bannerlord runs this assembly on .NET Framework, whose default HTTP
            // connection limit is only two. At accelerated campaign speed that
            // allowed redundant hourly work to occupy both connections for minutes,
            // starving daily relationship and World Test telemetry before it ever
            // reached the local server.
            ServicePointManager.DefaultConnectionLimit = Math.Max(
                ServicePointManager.DefaultConnectionLimit,
                16);
            HttpClientHandler handler = new HttpClientHandler
            {
                MaxConnectionsPerServer = Math.Max(2, maximumConnections),
                UseProxy = false
            };
            return new HttpClient(new Reign.Core.Contracts.Platform.ReignProtocolHandler(handler)) { Timeout = TimeSpan.FromSeconds(Math.Max(2, timeoutSeconds)) };
        }

        private static bool IsPriorityServerRoute(string route)
        {
            string value = route ?? string.Empty;
            return value.StartsWith("/relationships/ambient/", StringComparison.OrdinalIgnoreCase)
                || value.Equals("/world-test/heartbeat", StringComparison.OrdinalIgnoreCase)
                || value.StartsWith("/political-pressure/", StringComparison.OrdinalIgnoreCase)
                || value.StartsWith("/save-sync/", StringComparison.OrdinalIgnoreCase)
                || value.Equals("/portraits/generate/status", StringComparison.OrdinalIgnoreCase)
                || value.StartsWith("/initialization/readiness/", StringComparison.OrdinalIgnoreCase)
                || value.StartsWith("/tests/live/", StringComparison.OrdinalIgnoreCase)
                || value.Equals("/world-history/timeline/open", StringComparison.OrdinalIgnoreCase)
                || value.StartsWith("/tests/npc-dialogue/request", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsProviderBackedInteractiveRoute(string route)
        {
            string value = route ?? string.Empty;
            return value.Equals("/dialogue/respond", StringComparison.OrdinalIgnoreCase)
                || value.Equals("/party-chat/respond", StringComparison.OrdinalIgnoreCase)
                || value.Equals("/tavern-house/respond", StringComparison.OrdinalIgnoreCase)
                || value.Equals("/tavern-house/scene", StringComparison.OrdinalIgnoreCase)
                || value.Equals("/family-chambers/scene/generate", StringComparison.OrdinalIgnoreCase)
                || value.Equals("/portraits/generate", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsHighFrequencyControlRoute(string route)
        {
            string value = route ?? string.Empty;
            return value.Equals("/tests/live/game/heartbeat", StringComparison.OrdinalIgnoreCase)
                || value.Equals("/tests/live/game/poll", StringComparison.OrdinalIgnoreCase)
                || value.Equals("/relationships/ambient/native_sync/pull", StringComparison.OrdinalIgnoreCase)
                || value.Equals("/tests/npc-dialogue/request/poll", StringComparison.OrdinalIgnoreCase)
                || value.Equals("/tests/party-dialogue/request/poll", StringComparison.OrdinalIgnoreCase);
        }

        internal static JObject HttpTransportDiagnostics()
        {
            return new JObject
            {
                ["mode"] = "pooled_segmented_utf8_json",
                ["interactiveProviderTimeoutSeconds"] = InteractiveProviderTimeoutSeconds,
                ["interactiveProviderRoutes"] = new JArray(
                    "/dialogue/respond",
                    "/party-chat/respond",
                    "/family-chambers/scene/generate",
                    "/portraits/generate"),
                ["pooledRequests"] = Interlocked.Read(ref _streamedPostRequests),
                ["serializedBytes"] = Interlocked.Read(ref _streamedPostBytes),
                ["largeRequests"] = Interlocked.Read(ref _largeStreamedPostRequests),
                ["peakRequestBytes"] = Interlocked.Read(ref _peakStreamedPostBytes),
                ["totalSerializationMs"] = Interlocked.Read(ref _totalPostSerializationMs),
                ["peakSerializationMs"] = Interlocked.Read(ref _peakPostSerializationMs)
            };
        }

        private static void RecordStreamedPost(ReignJsonHttpContent content)
        {
            if (content == null) return;
            long bytes = Math.Max(0L, content.SerializedBytes);
            long durationMs = Math.Max(0L, content.SerializationDurationMs);
            Interlocked.Increment(ref _streamedPostRequests);
            Interlocked.Add(ref _streamedPostBytes, bytes);
            Interlocked.Add(ref _totalPostSerializationMs, durationMs);
            if (bytes >= 85L * 1024L)
                Interlocked.Increment(ref _largeStreamedPostRequests);
            UpdateMaximum(ref _peakStreamedPostBytes, bytes);
            UpdateMaximum(ref _peakPostSerializationMs, durationMs);
        }

        private static void UpdateMaximum(ref long target, long value)
        {
            long observed = Interlocked.Read(ref target);
            while (value > observed)
            {
                long prior = Interlocked.CompareExchange(
                    ref target,
                    value,
                    observed);
                if (prior == observed) return;
                observed = prior;
            }
        }

        private static string NewCorrelationId(string mode, string subjectId)
        {
            string safeMode = string.IsNullOrWhiteSpace(mode) ? "request" : mode.Trim().Replace(' ', '_');
            string safeSubject = string.IsNullOrWhiteSpace(subjectId) ? "world" : subjectId.Trim().Replace(' ', '_');
            return safeMode + "-" + safeSubject + "-" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
        }

        public static async Task<bool> IngestDailyWorldSnapshotAsync()
        {
            try
            {
                ReignBetaSettings settings = ReignBetaSettings.Instance;
                if (settings != null && !settings.UseLocalServer)
                {
                    return false;
                }

                JObject payload = new JObject
                {
                    ["campaignId"] = GetCampaignId(),
                    ["eventType"] = "daily_world_snapshot",
                    ["locationId"] = MobileParty.MainParty?.CurrentSettlement?.StringId ?? string.Empty,
                    ["summary"] = BuildSummary(),
                    ["worldDay"] = TaleWorlds.CampaignSystem.Campaign.Current == null ? 0d : CampaignTime.Now.ToDays,
                    ["volatileCurrentState"] = true,
                    ["worldState"] = BuildDailyWorldSnapshotData(),
                    ["actorIds"] = new JArray(GetActorIds())
                };

                JObject response = await PostJsonAsync("/events/ingest", payload).ConfigureAwait(false);
                return response.Value<bool?>("ok") == true;
            }
            catch (Exception ex)
            {
                ReignLog.Warn("Local server ingest failed: " + ex.Message);
                return false;
            }
        }

        public static async Task<List<ReignWorldActionRecord>> FetchActionCommandsAsync()
        {
            List<ReignWorldActionRecord> results = new List<ReignWorldActionRecord>();
            try
            {
                ReignBetaSettings settings = ReignBetaSettings.Instance;
                if (settings != null && (!settings.UseLocalServer || !settings.ReceiveServerActionCommands))
                {
                    return results;
                }

                string route = "/actions/next?campaignId=" + Uri.EscapeDataString(GetCampaignId()) + "&limit=10";
                JObject response = await GetJsonAsync(route).ConfigureAwait(false);
                if (response.Value<bool?>("ok") != true)
                {
                    return results;
                }

                JArray actions = response["actions"] as JArray;
                if (actions == null)
                {
                    return results;
                }

                foreach (JToken token in actions)
                {
                    JObject obj = token as JObject;
                    ReignWorldActionRecord record = ParseActionRecord(obj);
                    if (record != null && record.Type != ReignWorldActionType.Unknown)
                    {
                        if (IsStaleUndeliveredAction(obj))
                        {
                            ReignLog.Warn("Rejected stale server action " + record.ActionId + " type=" + record.Type + " correlationId=" + ReadString(obj, "correlationId", ""));
                            await ReportActionAsync(record, "expired", "Undelivered action exceeded the six-hour delivery window.").ConfigureAwait(false);
                            continue;
                        }
                        results.Add(record);
                    }
                }
            }
            catch (Exception ex)
            {
                ReignLog.Warn("Local server action poll failed: " + ex.Message);
            }

            return results;
        }

        private static bool IsStaleUndeliveredAction(JObject obj)
        {
            string correlationId = ReadString(obj, "correlationId", "");
            if (string.IsNullOrWhiteSpace(correlationId))
            {
                return false;
            }

            string[] parts = correlationId.Split('-');
            foreach (string part in parts)
            {
                if (part.Length == 13 && long.TryParse(part, out long unixMs))
                {
                    long ageMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - unixMs;
                    return ageMs > 6L * 60L * 60L * 1000L;
                }
            }

            return false;
        }

        public static async Task ReportActionAsync(ReignWorldActionRecord action, string status, string message, ReignActionResult result = null)
        {
            try
            {
                ReignBetaSettings settings = ReignBetaSettings.Instance;
                if (settings != null && !settings.UseLocalServer)
                {
                    return;
                }

                if (action == null)
                {
                    return;
                }

                JObject payload = new JObject
                {
                    ["campaignId"] = GetCampaignId(),
                    ["serverActionId"] = action.ActionId ?? string.Empty,
                    ["actionId"] = action.ActionId ?? string.Empty,
                    ["status"] = status ?? "reported",
                    ["type"] = action.Type.ToString(),
                    ["message"] = message ?? string.Empty,
                    ["attemptCount"] = action.AttemptCount,
                    ["maxAttempts"] = action.MaxAttempts,
                    ["statusValue"] = action.StatusValue,
                    ["source"] = action.Source ?? string.Empty,
                    ["actorHeroStringId"] = action.ActorHeroStringId ?? string.Empty,
                    ["actorKingdomStringId"] = action.ActorKingdomStringId ?? string.Empty,
                    ["actorClanStringId"] = action.ActorClanStringId ?? string.Empty,
                    ["targetHeroStringId"] = action.TargetHeroStringId ?? string.Empty,
                    ["targetKingdomStringId"] = action.TargetKingdomStringId ?? string.Empty,
                    ["targetClanStringId"] = action.TargetClanStringId ?? string.Empty,
                    ["targetSettlementStringId"] = action.TargetSettlementStringId ?? string.Empty,
                    ["planStage"] = action.PlanStage.ToString(),
                    ["authorizationMode"] = action.AuthorizationMode ?? string.Empty,
                    ["negotiationId"] = action.NegotiationId ?? string.Empty,
                    ["termsHash"] = action.TermsHash ?? string.Empty,
                    ["acceptedByHeroStringId"] = action.AcceptedByHeroStringId ?? string.Empty,
                    ["acceptedDay"] = action.AcceptedDay,
                    ["executionPhase"] = action.ExecutionPhase ?? string.Empty,
                    ["negotiatedCommand"] = action.NegotiatedCommand ?? string.Empty,
                    ["failureReason"] = action.FailureReason ?? string.Empty,
                    ["worldDay"] = TaleWorlds.CampaignSystem.Campaign.Current == null ? 0d : CampaignTime.Now.ToDays
                };

                Hero actorHero = ReignObjectResolver.FindHero(action.ActorHeroStringId);
                Hero targetHero = ReignObjectResolver.FindHero(action.TargetHeroStringId);
                if (actorHero != null) payload["actorHero"] = BuildHeroProfile(actorHero);
                if (targetHero != null) payload["targetHero"] = BuildHeroProfile(targetHero);

                if (result != null)
                {
                    payload["result"] = BuildActionResultReport(action, status, message, result);
                }

                JObject response = await PostJsonAsync("/actions/report", payload).ConfigureAwait(false);
                foreach (JObject change in (response["nativeRelationChanges"] as JArray ?? new JArray()).OfType<JObject>())
                {
                    Hero subject = ReignObjectResolver.FindHero(change.Value<string>("subjectId") ?? string.Empty);
                    Hero target = ReignObjectResolver.FindHero(change.Value<string>("targetId") ?? string.Empty);
                    int delta = change.Value<int?>("delta") ?? 0;
                    if (subject != null && target != null && delta != 0)
                    {
                        await ReignMainThread.InvokeAsync(() => TaleWorlds.CampaignSystem.Actions.ChangeRelationAction.ApplyRelationChangeBetweenHeroes(subject, target, delta, true)).ConfigureAwait(false);
                    }
                }
            }
            catch (Exception ex)
            {
                ReignLog.Warn("Local server action report failed: " + ex.Message);
            }
        }

        public static async Task IngestAuditAsync(string correlationId, string mode, string phase, string status, string summary, JObject data = null, string heroId = "", string actionId = "", string eventId = "", long durationMs = 0)
        {
            try
            {
                ReignBetaSettings settings = ReignBetaSettings.Instance;
                if (settings != null && !settings.UseLocalServer)
                {
                    return;
                }

                JObject payload = new JObject
                {
                    ["campaignId"] = GetCampaignId(),
                    ["correlationId"] = correlationId ?? string.Empty,
                    ["source"] = "game_client",
                    ["mode"] = string.IsNullOrWhiteSpace(mode) ? "action_gauntlet" : mode,
                    ["phase"] = string.IsNullOrWhiteSpace(phase) ? "gauntlet.client" : phase,
                    ["status"] = string.IsNullOrWhiteSpace(status) ? "completed" : status,
                    ["summary"] = summary ?? string.Empty,
                    ["heroId"] = heroId ?? string.Empty,
                    ["actionId"] = actionId ?? string.Empty,
                    ["eventId"] = eventId ?? string.Empty,
                    ["durationMs"] = durationMs,
                    ["data"] = data ?? new JObject()
                };

                await PostJsonAsync("/audit/ingest", payload).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                ReignLog.Warn("Local server audit ingest failed: " + ex.Message);
            }
        }

        private static JObject BuildActionResultReport(ReignWorldActionRecord action, string status, string message, ReignActionResult result)
        {
            return new JObject
            {
                ["actionId"] = action?.ActionId ?? string.Empty,
                ["type"] = action == null ? string.Empty : action.Type.ToString(),
                ["status"] = status ?? "reported",
                ["message"] = message ?? result?.Message ?? string.Empty,
                ["success"] = result != null && result.Success,
                ["completed"] = result != null && result.Completed,
                ["resultCode"] = result?.ResultCode ?? string.Empty,
                ["outcome"] = result?.Outcome ?? string.Empty,
                ["failureKind"] = result?.FailureKind ?? string.Empty,
                ["retryable"] = result != null && result.Retryable,
                ["nextAttemptDelayDays"] = result?.NextAttemptDelayDays ?? 0f,
                ["debugMessage"] = result?.DebugMessage ?? string.Empty,
                ["attemptCount"] = action?.AttemptCount ?? 0,
                ["maxAttempts"] = action?.MaxAttempts ?? 0,
                ["changedEntities"] = ToJArray(result?.ChangedEntities),
                ["effects"] = ToJArray(result?.Effects),
                ["diagnostics"] = ToJArray(result?.Diagnostics)
            };
        }

        private static JArray ToJArray(List<Dictionary<string, string>> rows)
        {
            JArray array = new JArray();
            if (rows == null)
            {
                return array;
            }

            foreach (Dictionary<string, string> row in rows)
            {
                JObject obj = new JObject();
                if (row != null)
                {
                    foreach (KeyValuePair<string, string> pair in row)
                    {
                        obj[pair.Key ?? string.Empty] = pair.Value ?? string.Empty;
                    }
                }

                array.Add(obj);
            }

            return array;
        }

        public static async Task<bool> UpsertHeroAsync(Hero hero)
        {
            try
            {
                ReignBetaSettings settings = ReignBetaSettings.Instance;
                if (settings != null && !settings.UseLocalServer)
                {
                    return false;
                }

                if (hero == null || string.IsNullOrWhiteSpace(hero.StringId))
                {
                    return false;
                }

                JObject payload = new JObject
                {
                    ["campaignId"] = GetCampaignId(),
                    ["hero"] = BuildHeroProfile(hero)
                };
                Stopwatch timer = Stopwatch.StartNew();
                JObject response = await PostJsonAsync("/characters/upsert", payload).ConfigureAwait(false);
                timer.Stop();
                ReignLog.Info("HTTP /characters/upsert hero=" + hero.StringId + " clientMs=" + timer.ElapsedMilliseconds + " ok=" + (response.Value<bool?>("ok") == true));
                return response.Value<bool?>("ok") == true;
            }
            catch (Exception ex)
            {
                ReignLog.Warn("Hero upsert failed: " + ex.Message);
                return false;
            }
        }

        public static async Task<ReignHeroGenerationResult> UpsertAllHeroesAsync()
        {
            ReignHeroGenerationResult result = new ReignHeroGenerationResult();
            try
            {
                ReignBetaSettings settings = ReignBetaSettings.Instance;
                if (settings != null && !settings.UseLocalServer)
                {
                    result.Error = "Local server is disabled.";
                    return result;
                }

                JArray heroes = new JArray();
                foreach (Hero hero in Hero.AllAliveHeroes.Where(IsFixedNobleHero))
                {
                    if (hero == null || string.IsNullOrWhiteSpace(hero.StringId))
                    {
                        continue;
                    }

                    heroes.Add(BuildHeroProfile(hero));
                }

                JObject payload = new JObject
                {
                    ["campaignId"] = GetCampaignId(),
                    ["heroes"] = heroes
                };
                JObject response = await PostJsonAsync("/characters/upsert-batch", payload).ConfigureAwait(false);
                result.Ok = response.Value<bool?>("ok") == true;
                result.Created = response.Value<int?>("created") ?? 0;
                result.Updated = response.Value<int?>("updated") ?? 0;
                result.Failed = response.Value<int?>("failed") ?? 0;
                result.Total = response.Value<int?>("total") ?? heroes.Count;
                result.Error = response.Value<string>("error") ?? string.Empty;
                return result;
            }
            catch (Exception ex)
            {
                result.Error = ex.Message;
                ReignLog.Warn("Generate all hero folders failed: " + ex.Message);
                return result;
            }
        }

        public static async Task<JObject> SynchronizeIdentityNetworkAsync(
            string initializationGenerationId = "")
        {
            try
            {
                ReignBetaSettings settings = ReignBetaSettings.Instance;
                if (settings != null && !settings.UseLocalServer)
                {
                    return new JObject { ["ok"] = false, ["error"] = "Local server is disabled." };
                }

                JArray heroes = new JArray();
                JArray financeSnapshots = new JArray();
                JArray portraitCharacters = new JArray();
                double observedWorldDay = TaleWorlds.CampaignSystem.Campaign.Current == null
                    ? 0d
                    : CampaignTime.Now.ToDays;
                foreach (Hero hero in Hero.AllAliveHeroes
                    .Where(x => x != null && !string.IsNullOrWhiteSpace(x.StringId))
                    .GroupBy(x => x.StringId, StringComparer.OrdinalIgnoreCase)
                    .Select(x => x.First())
                    .OrderBy(x => x.StringId, StringComparer.OrdinalIgnoreCase))
                {
                    heroes.Add(BuildIdentityRosterEntry(hero));
                    if (IsFinanceSnapshotEligible(hero))
                    {
                        financeSnapshots.Add(BuildFinanceSnapshotEntry(hero));
                    }
                    if (IsPortraitRosterEligible(hero))
                    {
                        portraitCharacters.Add(BuildPortraitRosterEntry(hero));
                    }
                }

                string campaignId = GetCampaignId();
                string rosterFingerprint = BuildPortraitRosterFingerprint(portraitCharacters);
                JObject response = await PostJsonAsync("/identity/synchronize", new JObject
                {
                    ["campaignId"] = campaignId,
                    ["correlationId"] = NewCorrelationId("identity_sync", Hero.MainHero?.StringId),
                    ["worldDay"] = observedWorldDay,
                    ["timelineId"] = ReignBeta.Campaign.ReignWorldHistoryCampaignBehavior.Instance?.TimelineId ?? "main",
                    ["deferRelationshipProjection"] = !string.IsNullOrWhiteSpace(initializationGenerationId),
                    ["initializationGenerationId"] = initializationGenerationId ?? string.Empty,
                    ["portraitRosterFingerprint"] = rosterFingerprint,
                    ["heroes"] = heroes,
                    ["financeSnapshots"] = financeSnapshots
                }).ConfigureAwait(false);
                try
                {
                    bool alreadySynchronized;
                    lock (PortraitRosterSyncLock)
                    {
                        alreadySynchronized = SyncedPortraitRosterFingerprints.TryGetValue(campaignId, out string previousFingerprint)
                            && string.Equals(previousFingerprint, rosterFingerprint, StringComparison.Ordinal)
                            && response.Value<bool?>("portraitRosterSyncRequired") == false;
                    }

                    if (alreadySynchronized)
                    {
                        response["portraitRosterOk"] = true;
                        response["portraitRosterUnchanged"] = true;
                        response["portraitCharacterCount"] = portraitCharacters.Count;
                    }
                    else
                    {
                        JObject portraitRosterResponse = await PostJsonAsync("/portrait-roster/upsert-batch", new JObject
                        {
                            ["campaignId"] = campaignId,
                            ["campaignLabel"] = ReignCampaignIdentity.CurrentCampaignLabel(),
                            ["rosterFingerprint"] = rosterFingerprint,
                            ["characters"] = portraitCharacters
                        }).ConfigureAwait(false);
                        bool portraitRosterOk = portraitRosterResponse.Value<bool?>("ok") == true;
                        response["portraitRosterOk"] = portraitRosterOk;
                        response["portraitRosterUnchanged"] = portraitRosterResponse.Value<bool?>("unchanged") == true;
                        response["portraitCharacterCount"] = portraitRosterResponse.Value<int?>("characterCount") ?? portraitCharacters.Count;
                        if (portraitRosterOk)
                        {
                            lock (PortraitRosterSyncLock)
                            {
                                SyncedPortraitRosterFingerprints[campaignId] = rosterFingerprint;
                            }
                        }
                    }
                }
                catch (Exception portraitException)
                {
                    response["portraitRosterOk"] = false;
                    response["portraitRosterError"] = portraitException.Message;
                    ReignLog.Warn("Portrait roster synchronization failed: " + portraitException.Message);
                }
                ReignLog.Info("Identity synchronization ok=" + (response.Value<bool?>("ok") == true)
                    + " heroes=" + heroes.Count
                    + " finances=" + financeSnapshots.Count
                    + " changed=" + (response.Value<bool?>("rosterChanged") == true)
                    + " seeded=" + (response.Value<int?>("seeded") ?? 0)
                    + " upgraded=" + (response.Value<int?>("upgraded") ?? 0));
                return response;
            }
            catch (Exception ex)
            {
                ReignLog.Warn("Identity synchronization failed: " + ex.Message);
                return new JObject { ["ok"] = false, ["error"] = ex.Message };
            }
        }

        public static async Task<JObject> MakePlayerKnowEveryoneAsync()
        {
            try
            {
                Hero player = Hero.MainHero;
                if (player == null || string.IsNullOrWhiteSpace(player.StringId))
                {
                    return new JObject { ["ok"] = false, ["error"] = "The player hero is unavailable." };
                }

                ReignBetaSettings settings = ReignBetaSettings.Instance;
                if (settings != null && !settings.UseLocalServer)
                {
                    return new JObject { ["ok"] = false, ["error"] = "Local server is disabled." };
                }

                JArray heroes = new JArray();
                foreach (Hero hero in Hero.AllAliveHeroes
                    .Where(x => x != null && x != player && !string.IsNullOrWhiteSpace(x.StringId))
                    .GroupBy(x => x.StringId, StringComparer.OrdinalIgnoreCase)
                    .Select(x => x.First())
                    .OrderBy(x => x.StringId, StringComparer.OrdinalIgnoreCase))
                {
                    heroes.Add(BuildIdentityRosterEntry(hero));
                }

                return await PostJsonAsync("/identity/debug/know-everyone", new JObject
                {
                    ["campaignId"] = GetCampaignId(),
                    ["correlationId"] = NewCorrelationId("identity_debug_know_everyone", player.StringId),
                    ["worldDay"] = TaleWorlds.CampaignSystem.Campaign.Current == null ? 0d : CampaignTime.Now.ToDays,
                    ["testMode"] = true,
                    ["observer"] = BuildIdentityRosterEntry(player),
                    ["heroes"] = heroes
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                ReignLog.Warn("Make player know everyone failed: " + ex.Message);
                return new JObject { ["ok"] = false, ["error"] = ex.Message };
            }
        }

        public static async Task<JObject> QueryIdentityAsync(Hero observer, Hero subject)
        {
            if (observer == null || subject == null) return new JObject { ["ok"] = false, ["error"] = "Observer and subject are required." };
            return await PostJsonAsync("/identity/query", new JObject
            {
                ["campaignId"] = GetCampaignId(),
                ["observerHeroStringId"] = observer.StringId,
                ["subjectHeroStringId"] = subject.StringId,
                ["limit"] = 10
            }).ConfigureAwait(false);
        }

        public static async Task<JObject> ResetIdentityAsync(Hero observer, Hero subject)
        {
            if (observer == null || subject == null) return new JObject { ["ok"] = false, ["error"] = "Observer and subject are required." };
            return await PostJsonAsync("/identity/reset", new JObject
            {
                ["campaignId"] = GetCampaignId(),
                ["correlationId"] = NewCorrelationId("identity_reset", observer.StringId),
                ["observerHeroStringId"] = observer.StringId,
                ["subjectHeroStringId"] = subject.StringId,
                ["testMode"] = true
            }).ConfigureAwait(false);
        }

        public static async Task<ReignCharacterConstructionResult> ConstructAllHeroesAsync(bool force = false)
        {
            ReignCharacterConstructionResult result = new ReignCharacterConstructionResult();
            try
            {
                ReignBetaSettings settings = ReignBetaSettings.Instance;
                if (settings != null && !settings.UseLocalServer)
                {
                    result.Error = "Local server is disabled.";
                    return result;
                }

                JArray heroes = new JArray();
                foreach (Hero hero in Hero.AllAliveHeroes.Where(IsFixedNobleHero))
                {
                    if (hero == null || string.IsNullOrWhiteSpace(hero.StringId))
                    {
                        continue;
                    }

                    heroes.Add(BuildHeroProfile(hero));
                }

                JObject payload = new JObject
                {
                    ["campaignId"] = GetCampaignId(),
                    ["heroes"] = heroes,
                    ["force"] = force,
                    ["useLlm"] = true
                };
                JObject response = await PostJsonAsync("/characters/construct-batch", payload).ConfigureAwait(false);
                result.Ok = response.Value<bool?>("ok") == true;
                result.Constructed = response.Value<int?>("constructed") ?? 0;
                result.Failed = response.Value<int?>("failed") ?? 0;
                result.Total = response.Value<int?>("total") ?? heroes.Count;
                result.LlmUsed = response.Value<int?>("llmUsed") ?? 0;
                result.Error = response.Value<string>("error") ?? string.Empty;
                return result;
            }
            catch (Exception ex)
            {
                result.Error = ex.Message;
                ReignLog.Warn("Generate full living characters failed: " + ex.Message);
                return result;
            }
        }

        public static async Task<JObject> RetryFailedCharactersAsync(Hero hero = null)
        {
            try
            {
                ReignBetaSettings settings = ReignBetaSettings.Instance;
                if (settings != null && !settings.UseLocalServer)
                {
                    return new JObject
                    {
                        ["ok"] = false,
                        ["error"] = "Local server is disabled."
                    };
                }

                JObject payload = new JObject
                {
                    ["campaignId"] = GetCampaignId()
                };
                if (hero != null && !string.IsNullOrWhiteSpace(hero.StringId))
                {
                    payload["heroStringId"] = hero.StringId;
                    payload["hero"] = BuildHeroProfile(hero);
                }

                Stopwatch timer = Stopwatch.StartNew();
                JObject response = await PostJsonAsync("/characters/retry-failed", payload).ConfigureAwait(false);
                timer.Stop();
                ReignLog.Info("HTTP /characters/retry-failed hero=" + (hero?.StringId ?? "<all>")
                    + " clientMs=" + timer.ElapsedMilliseconds
                    + " ok=" + (response.Value<bool?>("ok") == true));
                return response;
            }
            catch (Exception ex)
            {
                ReignLog.Warn("Retry failed character generation call failed: " + ex.Message);
                return new JObject
                {
                    ["ok"] = false,
                    ["error"] = ex.Message
                };
            }
        }

        public static async Task<JObject> DiagnosePortraitAsync(Hero hero, string cacheKey, string appearanceKey)
        {
            try
            {
                ReignBetaSettings settings = ReignBetaSettings.Instance;
                if (settings != null && !settings.UseLocalServer)
                {
                    return new JObject
                    {
                        ["ok"] = false,
                        ["error"] = "Local server is disabled."
                    };
                }

                JObject payload = new JObject
                {
                    ["campaignId"] = GetCampaignId(),
                    ["heroStringId"] = hero?.StringId ?? string.Empty,
                    ["characterObjectId"] = hero?.CharacterObject?.StringId ?? string.Empty,
                    ["cacheKey"] = cacheKey ?? string.Empty,
                    ["appearanceKey"] = appearanceKey ?? string.Empty,
                    ["textureFactoryHas"] = AIPortraits.TextureFactory.Has(cacheKey),
                    ["activeWidgetIds"] = new JArray(
                        "ReignChatPlayerPortrait",
                        "ReignChatNpcPortrait",
                        "ReignChatZoomPortrait",
                        "AIPortraitsNpcPortrait",
                        "AIPortraitsPlayerPortrait",
                        "AIPortraitsZoomPortrait")
                };

                if (hero != null)
                {
                    payload["hero"] = BuildHeroProfile(hero);
                }

                Stopwatch timer = Stopwatch.StartNew();
                JObject response = await PostJsonAsync("/portraits/diagnose", payload).ConfigureAwait(false);
                timer.Stop();
                ReignLog.Info("HTTP /portraits/diagnose hero=" + (hero?.StringId ?? "")
                    + " cacheKey=" + ShortLog(cacheKey)
                    + " clientMs=" + timer.ElapsedMilliseconds
                    + " ok=" + (response.Value<bool?>("ok") == true));
                return response;
            }
            catch (Exception ex)
            {
                ReignLog.Warn("Portrait diagnosis server call failed: " + ex.Message);
                return new JObject
                {
                    ["ok"] = false,
                    ["error"] = ex.Message
                };
            }
        }

        public static async Task<bool> IngestSocialEventAnnouncementAsync(SocialEventRecord record)
        {
            try
            {
                if (record == null)
                {
                    return false;
                }

                JObject payload = new JObject
                {
                    ["campaignId"] = GetCampaignId(),
                    ["eventType"] = "social_event_announced",
                    ["socialEventId"] = record.EventId ?? string.Empty,
                    ["templateId"] = record.TemplateId ?? string.Empty,
                    ["displayName"] = record.DisplayName ?? string.Empty,
                    ["locationId"] = record.SettlementStringId ?? string.Empty,
                    ["summary"] = "A Bannerlord Reign social event was announced: " + (record.DisplayName ?? "social event") + ".",
                    ["actorIds"] = new JArray(record.AttendeeHeroStringIds ?? new List<string>())
                };

                JObject response = await PostJsonAsync("/events/ingest", payload).ConfigureAwait(false);
                return response.Value<bool?>("ok") == true;
            }
            catch (Exception ex)
            {
                ReignLog.Warn("Social event announcement ingest failed: " + ex.Message);
                return false;
            }
        }

        public static async Task<bool> StartSocialEventAsync(ReignSocialEventSession session)
        {
            try
            {
                if (session?.Record == null)
                {
                    return false;
                }

                JObject payload = BuildSocialEventPayload(session, null, string.Empty);
                payload["correlationId"] = NewCorrelationId("social_event_start", session.Record.EventId);
                payload["mode"] = "social_event";
                Stopwatch timer = Stopwatch.StartNew();
                JObject response = await PostJsonAsync("/events/social/start", payload).ConfigureAwait(false);
                timer.Stop();
                ReignLog.Info("HTTP /events/social/start eventId=" + session.Record.EventId + " clientMs=" + timer.ElapsedMilliseconds + " ok=" + (response.Value<bool?>("ok") == true));
                return response.Value<bool?>("ok") == true;
            }
            catch (Exception ex)
            {
                ReignLog.Warn("Social event start failed: " + ex.Message);
                return false;
            }
        }

        public static async Task<bool> ResolveSocialEventAsync(ReignSocialEventSession session)
        {
            JObject response = await ResolveSocialEventDetailedAsync(session).ConfigureAwait(false);
            return response.Value<bool?>("ok") == true;
        }

        public static async Task<JObject> ResolveSocialEventDetailedAsync(ReignSocialEventSession session)
        {
            try
            {
                if (session?.Record == null)
                {
                    return new JObject { ["ok"] = false, ["error"] = "No social event is active." };
                }

                JObject payload = BuildSocialEventPayload(session, null, string.Empty);
                payload["correlationId"] = NewCorrelationId("social_event_resolve", session.Record.EventId);
                payload["mode"] = "social_event";
                Stopwatch timer = Stopwatch.StartNew();
                JObject response = await PostJsonAsync("/events/social/resolve", payload).ConfigureAwait(false);
                timer.Stop();
                ReignLog.Info("HTTP /events/social/resolve eventId=" + session.Record.EventId + " clientMs=" + timer.ElapsedMilliseconds + " ok=" + (response.Value<bool?>("ok") == true));
                return response;
            }
            catch (Exception ex)
            {
                ReignLog.Warn("Social event resolve failed: " + ex.Message);
                return new JObject { ["ok"] = false, ["error"] = ex.Message };
            }
        }

        public static async Task<bool> FinishSocialEventPhaseAsync(ReignSocialEventSession session)
        {
            if (session?.Record == null) return false;
            string phaseKey = session.XpPhaseKey;
            ReignXpInteraction xp = await BeginXpInteractionAsync().ConfigureAwait(false);
            await ReignMainThread.InvokeAsync(() => xp.Owner?.FinishSocialPhase(phaseKey, xp.Options)).ConfigureAwait(false);
            return true;
        }

        public static async Task<JObject> RollSocialEventApproachesAsync(ReignSocialEventSession session, IEnumerable<Hero> candidates)
        {
            try
            {
                ReignBetaSettings settings = ReignBetaSettings.Instance;
                if (settings != null && !settings.UseLocalServer)
                {
                    return new JObject { ["ok"] = false, ["error"] = "Local server is disabled." };
                }

                if (session?.Record == null)
                {
                    return new JObject { ["ok"] = false, ["error"] = "No social event is active." };
                }

                List<Hero> safeCandidates = (candidates ?? Enumerable.Empty<Hero>())
                    .Where(x => x != null && x != Hero.MainHero && !string.IsNullOrWhiteSpace(x.StringId))
                    .Distinct()
                    .ToList();
                JObject payload = BuildSocialEventPayload(session, null, string.Empty);
                payload["correlationId"] = NewCorrelationId("social_event_approach", session.Record.EventId);
                payload["approachCandidates"] = new JArray(safeCandidates.Select(BuildHeroProfile));
                payload["remainingActiveSlots"] = session.RemainingActiveSlots;
                payload["playerIsFemale"] = Hero.MainHero?.IsFemale ?? false;
                Stopwatch timer = Stopwatch.StartNew();
                JObject response = await PostJsonAsync("/events/social/approaches/roll", payload).ConfigureAwait(false);
                timer.Stop();
                ReignLog.Info("HTTP /events/social/approaches/roll eventId=" + session.Record.EventId
                    + " candidates=" + safeCandidates.Count
                    + " selected=" + (response["approachHeroStringIds"] as JArray)?.Count
                    + " clientMs=" + timer.ElapsedMilliseconds
                    + " ok=" + (response.Value<bool?>("ok") == true));
                return response;
            }
            catch (Exception ex)
            {
                ReignLog.Warn("Social event approach roll failed: " + ex.Message);
                return new JObject { ["ok"] = false, ["error"] = ex.Message };
            }
        }

        public static async Task<ReignEventReply> RequestSocialEventResponseAsync(
            ReignSocialEventSession session,
            Hero speaker,
            string playerText,
            bool approachOpening = false)
        {
            ReignEventReply reply = new ReignEventReply();
            try
            {
                ReignBetaSettings settings = ReignBetaSettings.Instance;
                if (settings != null && !settings.UseLocalServer)
                {
                    reply.Error = "Local server is disabled.";
                    return reply;
                }

                if (session?.Record == null || speaker == null || string.IsNullOrWhiteSpace(speaker.StringId))
                {
                    reply.Error = "No social event speaker found.";
                    return reply;
                }

                JObject payload = BuildSocialEventPayload(session, speaker, playerText);
                payload["correlationId"] = NewCorrelationId("social_event", speaker.StringId);
                payload["mode"] = "social_event";
                payload["turnType"] = approachOpening ? "npc_approach_opening" : "player_reply";
                payload["approachOpening"] = approachOpening;
                await AttachContextBundlesAsync(payload, "social_event", speaker, session.Record.GetAttendees()).ConfigureAwait(false);
                Stopwatch timer = Stopwatch.StartNew();
                JObject response = await PostWandererConversationAsync("/events/social/respond", payload, speaker, approachOpening ? string.Empty : playerText).ConfigureAwait(false);
                timer.Stop();
                reply.Ok = response.Value<bool?>("ok") == true;
                reply.HeroStringId = speaker.StringId;
                reply.Text = response.Value<string>("reply") ?? string.Empty;
                reply.Participation = response.Value<string>("participation") ?? "speak";
                reply.ReactionTargetHeroStringId = response.Value<string>("reactionTargetHeroStringId") ?? string.Empty;
                reply.Emotion = response.Value<string>("emotion") ?? string.Empty;
                reply.Intent = response.Value<string>("intent") ?? string.Empty;
                reply.RelationshipSignal = response.Value<string>("relationshipSignal") ?? string.Empty;
                reply.RelationshipAssessments = response["relationshipAssessments"] as JArray ?? new JArray();
                reply.Error = response.Value<string>("error") ?? string.Empty;
                reply.EncyclopediaText = response.Value<string>("encyclopediaText") ?? string.Empty;
                ApplyHeroEncyclopediaText(speaker, reply.EncyclopediaText);
                reply.ClientTotalMs = timer.ElapsedMilliseconds;
                reply.TimingSummary = FormatTimingSummary(response["timing"] as JObject);
                if (!reply.Ok && string.IsNullOrWhiteSpace(reply.Error))
                {
                    reply.Error = response.ToString(Formatting.None);
                }

                if (reply.Ok && Hero.MainHero != null)
                {
                    await ApplyConversationRelationshipAssessmentsAsync(
                        response["conversationExchange"]?["exchangeId"]?.Value<string>() ?? payload.Value<string>("sceneTurnId") ?? string.Empty,
                        approachOpening ? "social_event_npc_initiative" : "social_event_conversation",
                        session.Record.GetAttendees(), new[] { response }, payload.Value<string>("correlationId") ?? string.Empty).ConfigureAwait(false);
                    if (!approachOpening)
                    {
                        _ = ObservePlayerHistoricalClaimForListenersSafelyAsync(speaker, session.Record.GetAttendees(), playerText, "social_event");
                    }

                    _ = ObserveNpcHistoricalClaimForListenersSafelyAsync(speaker, session.Record.GetAttendees(), reply.Text, "social_event");
                }

                return reply;
            }
            catch (Exception ex)
            {
                reply.Error = ex.Message;
                ReignLog.Warn("Social event response failed: " + ex.Message);
                return reply;
            }
        }

        public static async Task<ReignPartyChatReply> RequestPartyChatResponseAsync(
            ReignPartyChatSession session,
            Hero speaker,
            List<Hero> activeHeroes,
            string playerText,
            string correlationId = "",
            string auditRunId = "",
            int auditSceneIndex = -1,
            int auditTurnIndex = -1,
            string castleDialoguePrompt = "",
            string castleRoomName = "",
            bool castleOpening = false,
            bool familyChambers = false,
            string childHeroIdsCsv = "",
            string familyScenePlanJson = "{}",
            string residentHomeVenue = "")
        {
            ReignPartyChatReply reply = new ReignPartyChatReply
            {
                SpeakerHeroStringId = speaker?.StringId ?? string.Empty,
                SpeakerName = speaker?.Name?.ToString() ?? string.Empty
            };
            try
            {
                ReignBetaSettings settings = ReignBetaSettings.Instance;
                if (settings != null && !settings.UseLocalServer)
                {
                    reply.Error = "Local server is disabled.";
                    return reply;
                }

                if (session == null || speaker == null || string.IsNullOrWhiteSpace(speaker.StringId))
                {
                    reply.Error = "No party chat speaker found.";
                    return reply;
                }

                JObject payload = BuildPartyChatPayload(session, speaker, activeHeroes ?? new List<Hero>(), playerText);
                JObject visitorContext = ReignCourtCampaignBehavior.Instance?.BuildNobleVisitorConversationContext(speaker.StringId);
                if (visitorContext?.Value<bool?>("enabled") == true) payload["nobleVisitorContext"] = visitorContext;
                payload["correlationId"] = string.IsNullOrWhiteSpace(correlationId)
                    ? NewCorrelationId("party_chat", speaker.StringId)
                    : correlationId;
                payload["mode"] = "party_chat";
                if (!string.IsNullOrWhiteSpace(castleDialoguePrompt))
                {
                    payload["mode"] = "castle_chat";
                    payload["sceneInterface"] = "castle_keep_location";
                    payload["castleDialoguePrompt"] = castleDialoguePrompt;
                    payload["castleRoomName"] = castleRoomName ?? string.Empty;
                    payload["castleOpening"] = castleOpening;
                    payload["playerText"] = castleOpening ? string.Empty : playerText ?? string.Empty;
                }
                if (familyChambers)
                {
                    payload["mode"] = "family_chambers";
                    payload["familyChambers"] = true;
                    payload["childParticipantIds"] = new JArray((childHeroIdsCsv ?? string.Empty).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries));
                    payload["familyScenePlan"] = string.IsNullOrWhiteSpace(familyScenePlanJson) ? "{}" : familyScenePlanJson;
                    payload["timelineId"] = ReignWorldHistoryCampaignBehavior.Instance?.TimelineId ?? "main";
                }
                if (!string.IsNullOrWhiteSpace(residentHomeVenue))
                {
                    payload["sceneInterface"] = "settlement_home";
                    payload["sceneOpportunity"] = new JObject { ["private"] = true, ["exposure"] = 0.10d, ["witnessIds"] = new JArray() };
                    foreach (JObject participant in ((JArray)payload["sceneParticipants"]).OfType<JObject>())
                    {
                        participant["nativeLocationDescription"] = residentHomeVenue;
                        participant["nativeLocationClass"] = "private";
                    }
                }
                if (!string.IsNullOrWhiteSpace(auditRunId))
                {
                    payload["auditRunId"] = auditRunId;
                    payload["auditSceneIndex"] = auditSceneIndex;
                    payload["auditTurnIndex"] = auditTurnIndex;
                }
                await AttachContextBundlesAsync(payload, "party_chat", speaker, activeHeroes ?? new List<Hero>()).ConfigureAwait(false);
                Stopwatch timer = Stopwatch.StartNew();
                JObject response = await PostWandererConversationAsync("/party-chat/respond", payload, speaker, castleOpening ? string.Empty : playerText).ConfigureAwait(false);
                timer.Stop();
                reply.Ok = response.Value<bool?>("ok") == true;
                reply.Text = response.Value<string>("reply") ?? string.Empty;
                reply.Emotion = response.Value<string>("emotion") ?? string.Empty;
                reply.Intent = response.Value<string>("intent") ?? string.Empty;
                reply.RelationshipSignal = response.Value<string>("relationshipSignal") ?? string.Empty;
                reply.RelationshipAssessments = response["relationshipAssessments"] as JArray ?? new JArray();
                reply.RawResponse = response;
                reply.Participation = response.Value<string>("participation") ?? "speak";
                reply.ReactionTargetHeroStringId = response.Value<string>("reactionTargetHeroStringId") ?? string.Empty;
                reply.Error = response.Value<string>("error") ?? string.Empty;
                reply.EncyclopediaText = response.Value<string>("encyclopediaText") ?? string.Empty;
                reply.CorrelationId = payload.Value<string>("correlationId") ?? string.Empty;
                JObject exchange = response["conversationExchange"] as JObject;
                reply.ConversationSessionId = exchange?.Value<string>("sessionId") ?? session.SessionId ?? string.Empty;
                reply.ExchangeId = exchange?.Value<string>("exchangeId") ?? session.CurrentSceneTurnId ?? string.Empty;
                reply.SelectedContextPulls = response["selectedContextPulls"] as JArray ?? new JArray();
                reply.QueuedActions = response["queuedActions"] as JArray ?? new JArray();
                ApplyHeroEncyclopediaText(speaker, reply.EncyclopediaText);
                reply.ClientTotalMs = timer.ElapsedMilliseconds;
                reply.TimingSummary = FormatTimingSummary(response["timing"] as JObject);
                ReignLog.Info("HTTP /party-chat/respond hero=" + speaker.StringId
                    + " clientMs=" + reply.ClientTotalMs
                    + " ok=" + reply.Ok
                    + " timing=" + reply.TimingSummary);
                if (!reply.Ok && string.IsNullOrWhiteSpace(reply.Error))
                {
                    reply.Error = response.ToString(Formatting.None);
                }

                if (reply.Ok && Hero.MainHero != null && !familyChambers)
                {
                    _ = ObserveNpcHistoricalClaimForListenersSafelyAsync(speaker, activeHeroes, reply.Text, "party_chat");
                    _ = ObservePlayerHistoricalClaimForListenersSafelyAsync(speaker, activeHeroes, playerText, "party_chat");
                }

                return reply;
            }
            catch (Exception ex)
            {
                reply.Error = ex.Message;
                ReignLog.Warn("Party chat response failed: " + ex.Message);
                return reply;
            }
        }

        public static async Task<ReignGeneratedWildernessScenario> GenerateWildernessScenarioAsync(ReignGeneratedWildernessRequest request)
        {
            ReignGeneratedWildernessScenario result = new ReignGeneratedWildernessScenario();
            try
            {
                ReignBetaSettings settings = ReignBetaSettings.Instance;
                if (settings != null && !settings.UseLocalServer)
                {
                    result.Error = "Local server is disabled.";
                    return result;
                }

                JObject payload = new JObject
                {
                    ["campaignId"] = GetCampaignId(),
                    ["playerName"] = Hero.MainHero?.Name?.ToString() ?? "Player",
                    ["terrainKey"] = request?.TerrainKey ?? "plain",
                    ["locationText"] = request?.LocationText ?? "on the road",
                    ["timeOfDayText"] = request?.TimeOfDayText ?? "unknown time of day",
                    ["externalHeroId"] = request?.ExternalHeroId ?? string.Empty,
                    ["externalContext"] = request?.ExternalContext ?? string.Empty
                };

                JArray participants = new JArray();
                foreach (ReignGeneratedWildernessParticipant participant in request?.Participants ?? new List<ReignGeneratedWildernessParticipant>())
                {
                    if (participant?.Hero == null || string.IsNullOrWhiteSpace(participant.Hero.StringId))
                    {
                        continue;
                    }

                    JObject profile = BuildHeroProfile(participant.Hero);
                    profile["generatedRole"] = participant.Role ?? string.Empty;
                    profile["isOutsideNpc"] = participant.IsOutsideNpc;
                    participants.Add(profile);
                }

                payload["participants"] = participants;
                Stopwatch timer = Stopwatch.StartNew();
                JObject response = await PostJsonAsync("/events/generated-wilderness/plan", payload).ConfigureAwait(false);
                timer.Stop();

                result.Ok = response.Value<bool?>("ok") == true;
                result.Error = response.Value<string>("error") ?? string.Empty;
                result.Title = response.Value<string>("title") ?? string.Empty;
                result.ApproachDescription = response.Value<string>("approachDescription") ?? response.Value<string>("approach_description") ?? string.Empty;
                result.OpeningText = response.Value<string>("openingText") ?? response.Value<string>("opening_text") ?? string.Empty;
                result.PlayerHook = response.Value<string>("playerHook") ?? response.Value<string>("player_hook") ?? string.Empty;
                result.ExternalHeroId = response.Value<string>("externalHeroId") ?? response.Value<string>("external_hero_id") ?? string.Empty;
                result.ExternalContext = response.Value<string>("externalContext") ?? response.Value<string>("external_context") ?? string.Empty;
                result.TerrainKey = response.Value<string>("terrainKey") ?? response.Value<string>("terrain_key") ?? string.Empty;
                result.EmotionalPressure = response.Value<string>("emotionalPressure") ?? response.Value<string>("emotional_pressure") ?? string.Empty;
                result.SurfaceClues = response.Value<string>("surfaceClues") ?? response.Value<string>("surface_clues") ?? string.Empty;
                result.HiddenContext = response.Value<string>("hiddenContext") ?? response.Value<string>("hidden_context") ?? string.Empty;
                result.DiscoveryRoutes = response.Value<string>("discoveryRoutes") ?? response.Value<string>("discovery_routes") ?? string.Empty;
                result.ParticipantHeroIds = ReadStringArray(response["participantHeroIds"] ?? response["participant_ids"]);
                result.ClientTotalMs = timer.ElapsedMilliseconds;
                result.TimingSummary = FormatTimingSummary(response["timing"] as JObject);
                ReignLog.Info("HTTP /events/generated-wilderness/plan ok=" + result.Ok
                    + " participants=" + participants.Count
                    + " clientMs=" + result.ClientTotalMs
                    + " timing=" + result.TimingSummary);
                return result;
            }
            catch (Exception ex)
            {
                result.Error = ex.Message;
                ReignLog.Warn("Generated wilderness scenario failed: " + ex.Message);
                return result;
            }
        }

        public static async Task<bool> RegisterPortraitFileAsync(string cacheKey, string filePath, string source)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(cacheKey) || string.IsNullOrWhiteSpace(filePath))
                {
                    return false;
                }

                JObject payload = new JObject
                {
                    ["campaignId"] = GetCampaignId(),
                    ["cacheKey"] = cacheKey,
                    ["filePath"] = filePath,
                    ["source"] = source ?? "game",
                    ["characterObjectId"] = ExtractCharacterObjectId(cacheKey)
                };

                JObject response = await PostJsonAsync("/portraits/register", payload).ConfigureAwait(false);
                return response.Value<bool?>("ok") == true;
            }
            catch (Exception ex)
            {
                ReignLog.Warn("Portrait registration failed: " + ex.Message);
                return false;
            }
        }

        public static async Task<ReignPortraitGenerationResult> GeneratePortraitAsync(string cacheKey, string heroStringId, string prompt, byte[] sourceImagePng, string outputSizeOverride = null, AIPortraits.PortraitPromptContext promptContext = null, string promptPurpose = "portrait", string generationOperationId = null)
        {
            ReignPortraitGenerationResult result = new ReignPortraitGenerationResult();
            bool portraitProduct = string.IsNullOrWhiteSpace(promptPurpose)
                || string.Equals(promptPurpose, "portrait", StringComparison.OrdinalIgnoreCase);
            if (portraitProduct && string.IsNullOrWhiteSpace(generationOperationId))
                generationOperationId = Guid.NewGuid().ToString("N");
            result.GenerationOperationId = generationOperationId ?? string.Empty;
            try
            {
				byte[] compatibleSource = portraitProduct ? null : AIPortraits.PngReencode.NormalizeImageEditSource(sourceImagePng);
				if (!portraitProduct && (compatibleSource == null || compatibleSource.Length == 0))
				{
					result.Ok = false;
					result.Error = "Portrait source could not be normalized to an opaque PNG within the 384-5000 pixel and 10 MB image-edit limits.";
					return result;
				}
				sourceImagePng = compatibleSource;
                ReignBetaSettings settings = ReignBetaSettings.Instance;
                if (settings != null && !settings.UseLocalServer)
                {
                    result.Error = "Local Bannerlord Reign server is disabled.";
                    return result;
                }

                if (!portraitProduct && (sourceImagePng == null || sourceImagePng.Length == 0))
                {
                    result.Error = "No source image was captured.";
                    return result;
                }

                JObject payload = new JObject
                {
                    ["campaignId"] = GetCampaignId(),
                    ["cacheKey"] = cacheKey ?? string.Empty,
                    ["heroStringId"] = heroStringId ?? string.Empty,
                    ["characterObjectId"] = ExtractCharacterObjectId(cacheKey),
                    ["prompt"] = prompt ?? string.Empty,
                    ["promptPurpose"] = string.IsNullOrWhiteSpace(promptPurpose) ? "portrait" : promptPurpose,
                    ["sourceImageBase64"] = sourceImagePng == null ? null : Convert.ToBase64String(sourceImagePng)
                };
                if (portraitProduct)
                {
                    payload["nativeCharacterSnapshot"] = PortraitRequestScope.Current?.Snapshot.DeepClone();
                    payload["generationOperationId"] = generationOperationId;
                    payload["generationStartedUtc"] = DateTime.UtcNow.ToString("o");
                }
                if (promptContext != null)
                {
                    payload["characterName"] = promptContext.CharacterName ?? string.Empty;
                    payload["ageYears"] = promptContext.AgeYears.HasValue ? (JToken)promptContext.AgeYears.Value : JValue.CreateNull();
                    payload["cultureId"] = promptContext.CultureId ?? string.Empty;
                    payload["cultureName"] = promptContext.CultureName ?? string.Empty;
                    payload["gender"] = promptContext.Gender ?? string.Empty;
					payload["clanTier"] = promptContext.ClanTier.HasValue ? (JToken)promptContext.ClanTier.Value : JValue.CreateNull();
					payload["socialStation"] = promptContext.SocialStation ?? string.Empty;
					payload["occupation"] = promptContext.Occupation ?? string.Empty;
					payload["isNotable"] = promptContext.IsNotable;
                }
                if (!string.IsNullOrWhiteSpace(outputSizeOverride))
                {
                    payload["outputSize"] = outputSizeOverride;
                }

                string url = BuildUrl("/portraits/generate");
                int requestChars = payload.ToString(Formatting.None).Length;
                ReignLog.Info("HTTP /portraits/generate starting url=" + url
                    + " cacheKey=" + ShortLog(cacheKey)
                    + " hero=" + (heroStringId ?? "")
                    + " sourceBytes=" + (sourceImagePng?.Length ?? 0)
                    + " requestChars=" + requestChars);

                Stopwatch timer = Stopwatch.StartNew();
                JObject response = await PostJsonAsync("/portraits/generate", payload).ConfigureAwait(false);
                timer.Stop();
                PopulatePortraitGenerationResult(result, response);
                result.ClientTotalMs = timer.ElapsedMilliseconds;

                ReignLog.Info("HTTP /portraits/generate cacheKey=" + ShortLog(cacheKey)
                    + " hero=" + (heroStringId ?? "")
                    + " provider=" + result.Provider
                    + " model=" + result.Model
                    + " adapter=" + result.Adapter
                    + " clientMs=" + result.ClientTotalMs
                    + " serverMs=" + result.ServerDurationMs
                    + " ok=" + result.Ok
                    + " timing=" + result.TimingSummary);
                return result;
            }
            catch (Exception ex)
            {
                if (portraitProduct && ex is OperationCanceledException
                    && !string.IsNullOrWhiteSpace(generationOperationId))
                {
                    ReignPortraitGenerationResult recovered = await RecoverPortraitGenerationAsync(
                        cacheKey, heroStringId, generationOperationId, 180000).ConfigureAwait(false);
                    if (recovered != null && recovered.Ok && recovered.ProductAccepted)
                    {
                        ReignLog.Info("Recovered completed portrait operation after the generation response timed out operation="
                            + generationOperationId + " hero=" + (heroStringId ?? ""));
                        return recovered;
                    }
                    if (recovered != null && recovered.GenerationMayStillComplete)
                    {
                        result.GenerationStatus = recovered.GenerationStatus;
                        result.GenerationMayStillComplete = true;
                    }
                }
                string error = BuildPortraitServerError(ex);
                result.Error = error;
                ReignLog.Warn("Portrait generation server call failed url=" + BuildUrl("/portraits/generate")
                    + " cacheKey=" + ShortLog(cacheKey)
                    + " hero=" + (heroStringId ?? "")
                    + " sourceBytes=" + (sourceImagePng == null ? 0 : sourceImagePng.Length)
                    + " error=" + DescribeExceptionChain(ex));
                return result;
            }
        }

        public static async Task<ReignPortraitGenerationResult> RecoverPortraitGenerationAsync(
            string cacheKey,
            string heroStringId,
            string generationOperationId,
            int maxWaitMs = 0)
        {
            ReignPortraitGenerationResult result = new ReignPortraitGenerationResult
            {
                GenerationOperationId = generationOperationId ?? string.Empty
            };
            Stopwatch timer = Stopwatch.StartNew();
            try
            {
                do
                {
                    JObject response = await PostJsonAsync("/portraits/generate/status", new JObject
                    {
                        ["campaignId"] = GetCampaignId(),
                        ["cacheKey"] = cacheKey ?? string.Empty,
                        ["heroStringId"] = heroStringId ?? string.Empty,
                        ["generationOperationId"] = generationOperationId ?? string.Empty
                    }).ConfigureAwait(false);
                    PopulatePortraitGenerationResult(result, response);
                    result.ClientTotalMs = timer.ElapsedMilliseconds;
                    if (string.Equals(result.GenerationStatus, "completed", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(result.GenerationStatus, "failed", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(result.GenerationStatus, "not_found", StringComparison.OrdinalIgnoreCase)
                        || maxWaitMs <= 0)
                    {
                        return result;
                    }
                    result.GenerationMayStillComplete = true;
                    await Task.Delay(2500).ConfigureAwait(false);
                }
                while (timer.ElapsedMilliseconds < maxWaitMs);

                result.GenerationStatus = "pending";
                result.GenerationMayStillComplete = true;
                result.Error = "Reign is still waiting for the portrait provider. The completed portrait will be recovered instead of generating it again.";
                return result;
            }
            catch (Exception ex)
            {
                result.GenerationStatus = "unavailable";
                result.GenerationMayStillComplete = true;
                result.Error = "Reign could not check the portrait operation yet: " + DescribeExceptionChain(ex);
                return result;
            }
        }

        private static void PopulatePortraitGenerationResult(ReignPortraitGenerationResult result, JObject response)
        {
            response = response ?? new JObject();
            result.Ok = response.Value<bool?>("ok") == true;
            result.Error = response.Value<string>("error") ?? string.Empty;
            result.Provider = response.Value<string>("provider") ?? string.Empty;
            result.Model = response.Value<string>("model") ?? string.Empty;
            result.Adapter = response.Value<string>("adapter") ?? string.Empty;
            result.ServerDurationMs = response.Value<long?>("durationMs") ?? 0;
            result.TimingSummary = FormatTimingSummary(response["timing"] as JObject);
            result.GenerationOperationId = response.Value<string>("generationOperationId")
                ?? result.GenerationOperationId ?? string.Empty;
            result.GenerationStatus = response.Value<string>("generationStatus") ?? string.Empty;
            result.Recovered = response.Value<bool?>("recovered") == true;
            result.GenerationMayStillComplete = string.Equals(result.GenerationStatus, "pending", StringComparison.OrdinalIgnoreCase);
            string imageBase64 = response.Value<string>("imageBase64") ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(imageBase64)) result.ImageBytes = Convert.FromBase64String(imageBase64);
            result.EffectivePrompt = response.Value<string>("effectivePrompt") ?? string.Empty;
            string sourceBase64 = response.Value<string>("sourceImageBase64") ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(sourceBase64)) result.SourceImageBytes = Convert.FromBase64String(sourceBase64);

            JObject receipt = response["portraitProductReceipt"] as JObject;
            JObject portraitInput = response["portraitInput"] as JObject;
            result.PortraitProductReceiptJson = receipt?.ToString(Formatting.Indented) ?? string.Empty;
            result.PortraitInputJson = portraitInput?.ToString(Formatting.Indented) ?? string.Empty;
            result.ProductSchema = receipt?.Value<string>("schema") ?? string.Empty;
            result.ProductVersion = receipt?.Value<int?>("version") ?? 0;
            result.ProductAccepted = receipt?.Value<bool?>("accepted") == true;
            JObject master = receipt?["master"] as JObject;
            JObject source = receipt?["source"] as JObject;
            JObject promptReceipt = receipt?["prompt"] as JObject;
            JObject focus = receipt?["focus"] as JObject;
            result.PortraitWidth = master?.Value<int?>("width") ?? 0;
            result.PortraitHeight = master?.Value<int?>("height") ?? 0;
            result.PortraitSha256 = master?.Value<string>("sha256") ?? string.Empty;
            result.SourceWidth = source?.Value<int?>("width") ?? 0;
            result.SourceHeight = source?.Value<int?>("height") ?? 0;
            result.SourceSha256 = source?.Value<string>("sha256") ?? string.Empty;
            result.PromptSha256 = promptReceipt?.Value<string>("sha256") ?? string.Empty;
            if (focus != null)
            {
                result.FaceFocus = new PortraitFaceFocus
                {
                    Method = focus.Value<string>("method") ?? string.Empty,
                    Model = focus.Value<string>("model") ?? string.Empty,
                    Confidence = focus.Value<double?>("confidence") ?? 0d,
                    CandidateCount = focus.Value<int?>("candidateCount") ?? 0,
                    X = focus.Value<double?>("x") ?? 0d,
                    Y = focus.Value<double?>("y") ?? 0d,
                    Width = focus.Value<double?>("width") ?? 0d,
                    Height = focus.Value<double?>("height") ?? 0d
                };
            }
        }

        private static string BuildPortraitServerError(Exception ex)
        {
            string details = DescribeExceptionChain(ex);
            string baseUrl = ReignBetaSettings.Instance?.LocalServerUrl;
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                baseUrl = "http://127.0.0.1:5101";
            }

            if (details.IndexOf("actively refused", StringComparison.OrdinalIgnoreCase) >= 0
                || details.IndexOf("10061", StringComparison.OrdinalIgnoreCase) >= 0
                || details.IndexOf("No connection could be made", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "Local Bannerlord Reign server is not running at " + baseUrl.TrimEnd('/') + ". Start the server, then try Look at them again.";
            }

            if (ex is OperationCanceledException)
            {
                return "The game stopped waiting for the portrait response. Reign will retain and recover the completed provider result instead of charging for another generation.";
            }

            if (details.IndexOf("timed out", StringComparison.OrdinalIgnoreCase) >= 0
                || details.IndexOf("timeout", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "Local Bannerlord Reign server did not answer in time at " + baseUrl.TrimEnd('/') + ". Check the server window/logs, then try again.";
            }

            return string.IsNullOrWhiteSpace(details)
                ? "Portrait generation could not reach the local Bannerlord Reign server."
                : "Portrait generation server call failed: " + details;
        }

        private static string DescribeExceptionChain(Exception ex)
        {
            if (ex == null)
            {
                return string.Empty;
            }

            List<string> parts = new List<string>();
            Exception current = ex;
            while (current != null)
            {
                string message = current.GetType().Name + ": " + current.Message;
                if (!parts.Contains(message))
                {
                    parts.Add(message);
                }
                current = current.InnerException;
            }

            return string.Join(" | ", parts);
        }

        public static async Task<List<ReignDialogueLine>> GetDialogueHistoryAsync(Hero hero, int limit = 80, JObject conversationContext = null)
        {
            List<ReignDialogueLine> lines = new List<ReignDialogueLine>();
            try
            {
                if (hero == null || string.IsNullOrWhiteSpace(hero.StringId))
                {
                    return lines;
                }

                JObject payload = new JObject
                {
                    ["campaignId"] = GetCampaignId(),
                    ["correlationId"] = NewCorrelationId("dialogue", hero.StringId),
                    ["mode"] = "dialogue",
                    ["heroStringId"] = hero.StringId,
                    ["limit"] = limit
                };
                if (conversationContext != null)
                {
                    payload["conversationMode"] = conversationContext.Value<string>("conversationMode") ?? string.Empty;
                    payload["postingId"] = conversationContext.Value<string>("postingId") ?? string.Empty;
                    payload["representedKingdomId"] = conversationContext.Value<string>("representedKingdomId") ?? string.Empty;
                }
                Stopwatch timer = Stopwatch.StartNew();
                JObject response = await PostJsonAsync("/dialogue/history", payload).ConfigureAwait(false);
                timer.Stop();
                ReignLog.Info("HTTP /dialogue/history hero=" + hero.StringId + " clientMs=" + timer.ElapsedMilliseconds + " ok=" + (response.Value<bool?>("ok") == true));
                if (response.Value<bool?>("ok") != true)
                {
                    return lines;
                }

                JArray array = response["lines"] as JArray;
                if (array == null)
                {
                    return lines;
                }

                foreach (JToken token in array)
                {
                    JObject obj = token as JObject;
                    if (obj == null)
                    {
                        continue;
                    }

                    lines.Add(new ReignDialogueLine(
                        obj.Value<string>("speaker") ?? string.Empty,
                        obj.Value<string>("text") ?? string.Empty,
                        obj.Value<string>("role") ?? string.Empty,
                        obj.Value<string>("channel") ?? "in_person",
                        obj.Value<string>("letterId") ?? string.Empty,
                        obj.Value<string>("threadId") ?? string.Empty,
                        obj.Value<string>("direction") ?? string.Empty,
                        obj.Value<double?>("worldDay") ?? 0d));
                }
            }
            catch (Exception ex)
            {
                ReignLog.Warn("Dialogue history load failed: " + ex.Message);
            }

            return lines;
        }

        public static async Task<string> StartConversationSessionAsync(Hero hero, JObject conversationContext = null)
        {
            try
            {
                if (hero == null || string.IsNullOrWhiteSpace(hero.StringId))
                {
                    return string.Empty;
                }
                bool official = IsOfficialAmbassadorContext(conversationContext);
                string representedRulerId = conversationContext?.Value<string>("representedRulerId") ?? string.Empty;
                JObject payload = new JObject
                {
                    ["campaignId"] = GetCampaignId(),
                    ["timelineId"] = ReignBeta.Campaign.ReignWorldHistoryCampaignBehavior.Instance?.TimelineId ?? "main",
                    ["npcId"] = hero.StringId,
                    ["playerId"] = Hero.MainHero?.StringId ?? string.Empty,
                    ["channel"] = official ? "ambassador_official" : "in_person",
                    ["worldDay"] = TaleWorlds.CampaignSystem.Campaign.Current == null ? 0d : CampaignTime.Now.ToDays,
                    ["locationId"] = ResolveDialogueLocationId(hero),
                    ["participants"] = new JArray(new[] { hero.StringId, Hero.MainHero?.StringId ?? string.Empty, official ? representedRulerId : string.Empty }.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.OrdinalIgnoreCase))
                };
                if (official) payload["ambassadorContext"] = conversationContext.DeepClone();
                JObject response = await PostJsonAsync("/memory/conversation/start", payload).ConfigureAwait(false);
                return response.Value<bool?>("ok") == true ? response.Value<string>("sessionId") ?? string.Empty : string.Empty;
            }
            catch (Exception ex)
            {
                ReignLog.Warn("Conversation session start failed: " + ex.Message);
                return string.Empty;
            }
        }

        public static async Task<ReignConversationFinishResult> FinishConversationSessionAsync(Hero hero, string sessionId, string reason = "closed_by_game", JObject conversationContext = null)
        {
            ReignConversationFinishResult result = new ReignConversationFinishResult { SessionId = sessionId ?? string.Empty };
            try
            {
                if (string.IsNullOrWhiteSpace(sessionId))
                {
                    result.Error = "Session id is empty.";
                    return result;
                }
                JObject payload = new JObject
                {
                    ["campaignId"] = GetCampaignId(),
                    ["timelineId"] = ReignBeta.Campaign.ReignWorldHistoryCampaignBehavior.Instance?.TimelineId ?? "main",
                    ["sessionId"] = sessionId,
                    ["npcId"] = hero?.StringId ?? string.Empty,
                    ["playerId"] = Hero.MainHero?.StringId ?? string.Empty,
                    ["worldDay"] = TaleWorlds.CampaignSystem.Campaign.Current == null ? 0d : CampaignTime.Now.ToDays,
                    ["locationId"] = ResolveDialogueLocationId(hero),
                    ["reason"] = reason ?? "closed_by_game"
                };
                if (IsOfficialAmbassadorContext(conversationContext))
                    payload["ambassadorContext"] = conversationContext.DeepClone();
                JObject response = await PostJsonAsync("/memory/conversation/finish", payload).ConfigureAwait(false);
                result.Ok = response.Value<bool?>("ok") == true;
                result.SceneSummaryId = response.Value<string>("sceneSummaryId") ?? string.Empty;
                result.Raw = response;
                result.Error = response.Value<string>("error") ?? string.Empty;
                ReignLog.Info("Conversation session finish id=" + sessionId + " ok=" + result.Ok);
            }
            catch (Exception ex)
            {
                result.Error = ex.Message;
                ReignLog.Warn("Conversation session finish failed: " + ex.Message);
            }
            return result;
        }

        public static async Task<string> StartPartyChatConversationSessionAsync(ReignPartyChatSession session, IEnumerable<Hero> activeHeroes)
        {
            try
            {
                List<Hero> heroes = (activeHeroes ?? Enumerable.Empty<Hero>())
                    .Where(hero => hero != null && !string.IsNullOrWhiteSpace(hero.StringId))
                    .GroupBy(hero => hero.StringId, StringComparer.OrdinalIgnoreCase)
                    .Select(group => group.First())
                    .ToList();
                Hero anchor = heroes.FirstOrDefault();
                if (session == null || anchor == null || string.IsNullOrWhiteSpace(session.SessionId))
                {
                    return string.Empty;
                }

                JArray participants = new JArray(heroes.Select(hero => hero.StringId)
                    .Concat(new[] { Hero.MainHero?.StringId ?? string.Empty })
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Distinct(StringComparer.OrdinalIgnoreCase));
                Settlement conversationSettlement = Settlement.CurrentSettlement
                    ?? MobileParty.MainParty?.CurrentSettlement
                    ?? anchor.CurrentSettlement
                    ?? anchor.PartyBelongedTo?.CurrentSettlement;
                JObject response = await PostJsonAsync("/memory/conversation/start", new JObject
                {
                    ["campaignId"] = GetCampaignId(),
                    ["sessionId"] = session.SessionId,
                    ["conversationSessionId"] = session.SessionId,
                    ["npcId"] = anchor.StringId,
                    ["playerId"] = Hero.MainHero?.StringId ?? string.Empty,
                    ["channel"] = "party_chat",
                    ["worldDay"] = TaleWorlds.CampaignSystem.Campaign.Current == null ? 0d : CampaignTime.Now.ToDays,
                    ["locationId"] = ResolveDialogueLocationId(anchor),
                    ["locationName"] = conversationSettlement?.Name?.ToString() ?? string.Empty,
                    ["sceneContext"] = session.BuildSceneContext(heroes),
                    ["participants"] = participants
                }).ConfigureAwait(false);
                return response.Value<bool?>("ok") == true
                    ? response.Value<string>("sessionId") ?? string.Empty
                    : string.Empty;
            }
            catch (Exception ex)
            {
                ReignLog.Warn("Party chat conversation session start failed: " + ex.Message);
                return string.Empty;
            }
        }

        public static async Task<ReignConversationFinishResult> FinishPartyChatConversationSessionAsync(
            ReignPartyChatSession session,
            IEnumerable<Hero> activeHeroes,
            string reason = "closed_by_player")
        {
            string sessionId = session?.SessionId ?? string.Empty;
            ReignConversationFinishResult result = new ReignConversationFinishResult { SessionId = sessionId };
            try
            {
                if (string.IsNullOrWhiteSpace(sessionId))
                {
                    result.Error = "Session id is empty.";
                    return result;
                }

                List<Hero> heroes = (activeHeroes ?? Enumerable.Empty<Hero>())
                    .Where(hero => hero != null && !string.IsNullOrWhiteSpace(hero.StringId))
                    .GroupBy(hero => hero.StringId, StringComparer.OrdinalIgnoreCase)
                    .Select(group => group.First())
                    .ToList();
                Hero anchor = heroes.FirstOrDefault();
                JObject response = await PostJsonAsync("/memory/conversation/finish", new JObject
                {
                    ["campaignId"] = GetCampaignId(),
                    ["sessionId"] = sessionId,
                    ["npcId"] = anchor?.StringId ?? string.Empty,
                    ["playerId"] = Hero.MainHero?.StringId ?? string.Empty,
                    ["worldDay"] = TaleWorlds.CampaignSystem.Campaign.Current == null ? 0d : CampaignTime.Now.ToDays,
                    ["locationId"] = ResolveDialogueLocationId(anchor),
                    ["reason"] = reason ?? "closed_by_player"
                }).ConfigureAwait(false);
                result.Ok = response.Value<bool?>("ok") == true;
                result.SceneSummaryId = response.Value<string>("sceneSummaryId") ?? string.Empty;
                result.Raw = response;
                result.Error = response.Value<string>("error") ?? string.Empty;
                ReignLog.Info("Party chat conversation session finish id=" + sessionId + " ok=" + result.Ok);
            }
            catch (Exception ex)
            {
                result.Error = ex.Message;
                ReignLog.Warn("Party chat conversation session finish failed: " + ex.Message);
            }
            return result;
        }

        public static async Task RecoverConversationSessionsAsync()
        {
            try
            {
                JObject response = await PostJsonAsync("/memory/conversation/recover", new JObject
                {
                    ["campaignId"] = GetCampaignId(),
                    ["worldDay"] = TaleWorlds.CampaignSystem.Campaign.Current == null ? 0d : CampaignTime.Now.ToDays
                }).ConfigureAwait(false);
                ReignLog.Info("Conversation recovery ok=" + (response.Value<bool?>("ok") == true) + " count=" + (response.Value<int?>("recoveredCount") ?? 0));
            }
            catch (Exception ex)
            {
                ReignLog.Warn("Conversation recovery failed: " + ex.Message);
            }
        }

        public static async Task<ReignDialogueReply> RequestDialogueResponseAsync(
            Hero hero,
            string playerText,
            string conversationSessionId = "",
            string playerPromptLabel = "",
            string correlationId = "",
            string dialogueAuditRunId = "",
            int dialogueAuditSceneIndex = -1,
            int dialogueAuditTurnIndex = -1,
            bool applyNativeEncyclopediaText = true,
            JObject conversationContext = null,
            bool guardedPromptOverride = false,
            string guardedPromptOverrideDirective = "",
            string guardedPromptOverrideOwnerCommandId = "")
        {
            ReignDialogueReply reply = new ReignDialogueReply();
            try
            {
                ReignBetaSettings settings = ReignBetaSettings.Instance;
                if (settings != null && !settings.UseLocalServer)
                {
                    reply.Error = "Local server is disabled.";
                    return reply;
                }

                if (hero == null || string.IsNullOrWhiteSpace(hero.StringId))
                {
                    reply.Error = "No conversation hero found.";
                    return reply;
                }

                // Hero, clan, party, settlement, equipment, and campaign collections are
                // native Bannerlord state. Live-test continuations run on a worker after
                // their first await, so reading that graph directly here can deadlock the
                // campaign thread for a particular hero. Capture one immutable request
                // snapshot on the game thread, then do all network work from the worker.
                JObject payload = await ReignMainThread.InvokeAsync(() =>
                    BuildDialogueRequestPayload(
                        hero,
                        playerText,
                        conversationSessionId,
                        playerPromptLabel,
                        settings?.McmTestModeEnabled ?? false,
                        conversationContext)).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(correlationId)) payload["correlationId"] = correlationId;
                if (!string.IsNullOrWhiteSpace(dialogueAuditRunId))
                {
                    payload["dialogueAuditRunId"] = dialogueAuditRunId;
                    payload["dialogueAuditSceneIndex"] = dialogueAuditSceneIndex;
                    payload["dialogueAuditTurnIndex"] = dialogueAuditTurnIndex;
                }
                if (guardedPromptOverride)
                {
                    payload["guardedPromptOverride"] = true;
                    payload["guardedPromptOverrideDirective"] =
                        (guardedPromptOverrideDirective ?? string.Empty).Trim();
                    payload["guardedPromptOverrideOwnerCommandId"] =
                        (guardedPromptOverrideOwnerCommandId ?? string.Empty).Trim();
                }
                string conceptionAttemptId = "player_intimacy_" + Guid.NewGuid().ToString("N");
                bool officialAmbassador = IsOfficialAmbassadorContext(conversationContext);
                bool suppressRelationshipAssessment =
                    conversationContext?.Value<bool?>("suppressRelationshipAssessment") == true;
                if (!officialAmbassador)
                    await AttachContextBundlesAsync(payload, "dialogue", hero, new List<Hero> { hero }).ConfigureAwait(false);
                Stopwatch timer = Stopwatch.StartNew();
                JObject response = await PostWandererConversationAsync("/dialogue/respond", payload, hero, playerText).ConfigureAwait(false);
                timer.Stop();
                reply.Ok = response.Value<bool?>("ok") == true;
                reply.Text = response.Value<string>("reply") ?? string.Empty;
                reply.Emotion = response.Value<string>("emotion") ?? string.Empty;
                reply.Intent = response.Value<string>("intent") ?? string.Empty;
                reply.Error = response.Value<string>("error") ?? string.Empty;
                reply.EncyclopediaText = response.Value<string>("encyclopediaText") ?? string.Empty;
                reply.ActionShadowPreview = response.Value<string>("actionShadowPreview") ?? string.Empty;
                JObject conceptionGate = response["conceptionGate"] as JObject;
                reply.ConceptionGateNeeded = conceptionGate?.Value<bool?>("needed") == true
                    && conceptionGate?.Value<bool?>("completed") == true;
                if (applyNativeEncyclopediaText)
                {
                    ApplyHeroEncyclopediaText(hero, reply.EncyclopediaText);
                }
                reply.QueuedActions = ParseQueuedActionRows(response["queuedTestDirectiveActions"]);
                reply.QueuedActions.AddRange(ParseQueuedActionRows(response["queuedDialogueActions"]));
                reply.ClientTotalMs = timer.ElapsedMilliseconds;
                reply.TimingSummary = FormatTimingSummary(response["timing"] as JObject);
                reply.CorrelationId = response.Value<string>("correlationId") ?? correlationId ?? string.Empty;
                reply.SelectedContextPulls = response["selectedContextPulls"] as JArray ?? new JArray();
                reply.ContextBundles = response["contextBundles"] as JArray ?? new JArray();
                reply.MemoryWrites = response["memoryWrites"] as JArray ?? new JArray();
                reply.ChancellorDecision = response["chancellorDecision"] as JObject ?? new JObject();
                reply.RawResponse = response;
                if (reply.Ok && payload["governmentPrivateContext"] is JObject governmentPrivateContext)
                    response["governmentCommitmentReceipt"] = await ReignMainThread.InvokeAsync(() =>
                        ReignBeta.Government.ReignGovernmentCampaignBehavior.Instance
                            ?.ApplyGovernmentPrivateConversationResult(governmentPrivateContext, response)
                        ?? new JObject()).ConfigureAwait(false);
                JObject exchange = response["conversationExchange"] as JObject;
                reply.ConversationSessionId = exchange?.Value<string>("sessionId") ?? conversationSessionId ?? string.Empty;
                reply.ExchangeId = exchange?.Value<string>("exchangeId") ?? string.Empty;
                reply.TurnIds = exchange?["turnIds"] as JArray ?? new JArray();
                ReignLog.Info("HTTP /dialogue/respond hero=" + hero.StringId
                    + " clientMs=" + reply.ClientTotalMs
                    + " ok=" + reply.Ok
                    + " timing=" + reply.TimingSummary);
                if (!reply.Ok && string.IsNullOrWhiteSpace(reply.Error))
                {
                    reply.Error = response.ToString(Formatting.None);
                }

                if (reply.Ok && Hero.MainHero != null && !officialAmbassador
                    && !suppressRelationshipAssessment)
                {
                    await ApplyConversationRelationshipAssessmentsAsync(
                        reply.ExchangeId, "in_person_conversation", new[] { hero }, new[] { response }, reply.CorrelationId).ConfigureAwait(false);
                    if (reply.ConceptionGateNeeded && string.IsNullOrWhiteSpace(playerPromptLabel))
                    {
                        ReignConceptionAttemptResult preview = await PreviewPlayerConceptionAttemptAsync(hero, reply.Text, conceptionAttemptId).ConfigureAwait(false);
                        if (preview.Ok && preview.Verified && preview.Eligible)
                        {
                            reply.ConceptionAttempt = preview;
                        }
                        else
                        {
                            reply.ConceptionGateNeeded = false;
                        }
                    }
                }

                return reply;
            }
            catch (Exception ex)
            {
                reply.Error = ex.Message;
                ReignLog.Exception("Dialogue response failed", ex);
                return reply;
            }
        }

        private static JObject BuildDialogueRequestPayload(
            Hero hero,
            string playerText,
            string conversationSessionId,
            string playerPromptLabel,
            bool mcmTestMode,
            JObject conversationContext = null)
        {
            List<Hero> participants = new List<Hero> { hero };
            bool official = IsOfficialAmbassadorContext(conversationContext);
            string requestedMode = conversationContext?.Value<string>("conversationMode")
                ?? string.Empty;
            bool rulerPetition = string.Equals(requestedMode, "ruler_petition",
                StringComparison.OrdinalIgnoreCase);
            bool nobleDocket = string.Equals(requestedMode, "noble_docket",
                StringComparison.OrdinalIgnoreCase);
            bool courtLife = string.Equals(requestedMode, "court_life", StringComparison.OrdinalIgnoreCase);
            string sceneContext = BuildSceneContext(hero);
            string sceneContextDirective = conversationContext
                ?.Value<string>("sceneContextDirective") ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(sceneContextDirective))
                sceneContext = sceneContext + "\n\n" + sceneContextDirective.Trim();
            JObject payload = new JObject
            {
                ["campaignId"] = GetCampaignId(),
                ["timelineId"] = ReignBeta.Campaign.ReignWorldHistoryCampaignBehavior.Instance?.TimelineId ?? "main",
                ["heroStringId"] = hero?.StringId ?? string.Empty,
                ["hero"] = BuildHeroProfile(hero),
                ["mcmTestMode"] = mcmTestMode,
                ["playerHeroStringId"] = Hero.MainHero?.StringId ?? string.Empty,
                ["playerIdentity"] = BuildPlayerIdentityContext(),
                ["playerClanId"] = Clan.PlayerClan?.StringId ?? string.Empty,
                ["playerKingdomId"] = Clan.PlayerClan?.Kingdom?.StringId ?? string.Empty,
                ["speakerClanId"] = hero?.Clan?.StringId ?? string.Empty,
                ["speakerKingdomId"] = hero?.Clan?.Kingdom?.StringId ?? hero?.MapFaction?.StringId ?? string.Empty,
                ["playerName"] = Hero.MainHero?.Name?.ToString() ?? "Player",
                ["playerText"] = playerText ?? string.Empty,
                ["playerPromptLabel"] = playerPromptLabel ?? string.Empty,
                ["suppressConceptionGate"] = official || rulerPetition || nobleDocket || courtLife
                    || string.Equals(playerPromptLabel, "pull_out", StringComparison.OrdinalIgnoreCase),
                ["conversationSessionId"] = conversationSessionId ?? string.Empty,
                ["sceneTurnId"] = string.IsNullOrWhiteSpace(conversationSessionId)
                    ? "individual_" + (hero?.StringId ?? "unknown") + "_" + Guid.NewGuid().ToString("N")
                    : conversationSessionId + "_" + Guid.NewGuid().ToString("N"),
                ["sceneParticipants"] = BuildSceneParticipants(participants),
                ["channel"] = official ? "ambassador_official"
                    : rulerPetition ? "ruler_petition"
                    : nobleDocket ? "noble_docket" : courtLife ? "court_life" : "in_person",
                ["conversationMode"] = official ? "ambassador_official"
                    : rulerPetition ? "ruler_petition"
                    : nobleDocket ? "noble_docket" : courtLife ? "court_life" : "in_person",
                ["officialMemoryFirewall"] = official,
                ["worldDay"] = TaleWorlds.CampaignSystem.Campaign.Current == null ? 0d : CampaignTime.Now.ToDays,
                ["locationId"] = ResolveDialogueLocationId(hero),
                ["sceneContext"] = sceneContext,
                ["nativePoliticalContext"] = BuildNativePoliticalContext(hero),
                ["clanAccords"] = ReignBeta.ClanAccords.ReignClanAccordsCampaignBehavior.Instance?.ConversationContext(hero) ?? new JObject(),
                ["arrestContext"] = ReignBeta.Campaign.ReignArrestCampaignBehavior.Instance
                    ?.BuildConversationContext(hero) ?? new JObject(),
                ["temporaryPartyGuest"] = ReignBeta.PartyAgency.ReignTemporaryPartyGuestCampaignBehavior.Instance
                    ?.BuildConversationContext(hero) ?? new JObject(),
                ["actionResolutionIndex"] = rulerPetition || courtLife
                    ? new JObject()
                    : BuildActionResolutionIndex(hero, participants,
                        RequiresFullActionResolutionIndex(playerText))
            };
            JObject chancellorContext = ReignCourtCampaignBehavior.Instance
                ?.BuildChancellorConversationContext(hero) ?? new JObject();
            if (chancellorContext.Value<bool?>("enabled") == true)
                payload["chancellorOfficeContext"] = chancellorContext;
            if (official) payload["ambassadorContext"] = conversationContext.DeepClone();
            JObject visitorContext = ReignCourtCampaignBehavior.Instance?.BuildNobleVisitorConversationContext(hero?.StringId ?? "");
            if (visitorContext?.Value<bool?>("enabled") == true) payload["nobleVisitorContext"] = visitorContext;
            if (!official && !rulerPetition && !nobleDocket && !courtLife)
            {
                JObject governmentContext = ReignBeta.Government.ReignGovernmentCampaignBehavior.Instance
                    ?.BuildGovernmentPrivateConversationContext(hero, payload.Value<string>("sceneTurnId"), playerText ?? "");
                if (governmentContext?.Value<bool?>("enabled") == true)
                    payload["governmentPrivateContext"] = governmentContext;
            }
            if (courtLife)
            {
                payload["courtLifeContext"] = conversationContext.DeepClone();
                payload["courtLifePhase"] = conversationContext.Value<string>("phase") ?? "";
                foreach (string key in new[] { "phase", "actualPlayerTurn", "matterId", "templateId", "source", "sceneTurnId" })
                    if (conversationContext[key] != null) payload[key] = conversationContext[key].DeepClone();
            }
            if (rulerPetition)
                payload["rulerPetitionContext"] = conversationContext.DeepClone();
            if (nobleDocket)
            {
                payload["nobleDocketContext"] = conversationContext.DeepClone();
                foreach (string property in new[]
                         {
                             "matterId", "templateId", "phase", "activeSpeakerHeroId",
                             "speakerRole", "activeParticipants", "revealedEvidence",
                             "discoverableEvidence", "audienceTranscript"
                         })
                {
                    JToken value = conversationContext[property];
                    if (value != null) payload[property] = value.DeepClone();
                }
            }
            return payload;
        }

        private static bool IsOfficialAmbassadorContext(JObject conversationContext)
        {
            return string.Equals(conversationContext?.Value<string>("conversationMode"), "ambassador_official", StringComparison.OrdinalIgnoreCase);
        }

        internal static async Task<JObject> EvaluateHistoricalClaimBetweenHeroesAsync(Hero claimant, Hero target, string claim, string mode)
        {
            if (claimant == null || target == null || !LooksLikeHistoricalWorldClaim(claim))
            {
                return new JObject { ["ok"] = true, ["outcome"] = "not_checkable" };
            }
            float worldDay = TaleWorlds.CampaignSystem.Campaign.Current == null ? 0f : (float)CampaignTime.Now.ToDays;
            return await CheckWorldHistoryLieAsync(claim, claimant, target, worldDay,
                ReignWorldHistoryCampaignBehavior.Instance?.TimelineId ?? "main", mode).ConfigureAwait(false);
        }

        private static async Task ObserveNpcHistoricalClaimForListenersSafelyAsync(Hero claimant, IEnumerable<Hero> listeners, string claim, string mode)
        {
            try
            {
                if (claimant == null || !LooksLikeHistoricalWorldClaim(claim)) return;
                List<Hero> targets = (listeners ?? Enumerable.Empty<Hero>()).Where(x => x != null && x != claimant && x != Hero.MainHero)
                    .GroupBy(x => x.StringId, StringComparer.OrdinalIgnoreCase).Select(x => x.First()).Take(12).ToList();
                foreach (Hero target in targets)
                {
                    await EvaluateHistoricalClaimBetweenHeroesAsync(claimant, target, claim, mode + "_npc_to_npc").ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                ReignLog.Warn("NPC-to-NPC historical claim observation failed: " + ex.Message);
            }
        }

        private static async Task ObservePlayerHistoricalClaimForListenersSafelyAsync(Hero respondingTarget, IEnumerable<Hero> listeners, string claim, string mode)
        {
            try
            {
                Hero player = Hero.MainHero;
                if (player == null || !LooksLikeHistoricalWorldClaim(claim)) return;
                List<Hero> targets = (listeners ?? Enumerable.Empty<Hero>()).Where(x => x != null && x != player && x != respondingTarget)
                    .GroupBy(x => x.StringId, StringComparer.OrdinalIgnoreCase).Select(x => x.First()).Take(12).ToList();
                foreach (Hero target in targets)
                {
                    await EvaluateHistoricalClaimBetweenHeroesAsync(player, target, claim, mode + "_player_to_listener").ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                ReignLog.Warn("Player historical claim listener observation failed: " + ex.Message);
            }
        }

        private static bool LooksLikeHistoricalWorldClaim(string text)
        {
            string raw = (text ?? string.Empty).Trim();
            string q = raw.ToLowerInvariant();
            if (q.Length < 6) return false;
            bool namedCurrentLocationAssertion = Regex.IsMatch(raw,
                @"\b(?:I|we)\s+(?:am|are|'m|'re)\s+(?:(?:currently|presently)\s+)?(?:in|at)\s+(?:the\s+)?\p{Lu}[\p{L}'-]{2,}",
                RegexOptions.CultureInvariant);
            bool namedForceStrengthAssertion = Regex.IsMatch(raw,
                @"\b\p{Lu}[\p{L}'-]{2,}\s+(?:has|commands|leads|fields)\s+(?:(?:exactly|about|roughly|nearly|only)\s+)?\d+\s+(?:troops|men|soldiers)\b",
                RegexOptions.CultureInvariant);
            if (namedCurrentLocationAssertion || namedForceStrengthAssertion) return true;
            string[] actors = { "i ", "i've ", "we ", "we've ", "my clan ", "our clan ", "my party ", "our army " };
            string[] acts =
            {
                "fought", "led", "commanded", "won", "defeated", "rescued", "freed", "captured", "killed", "wounded",
                "raided", "besieged", "conquered", "defended", "released", "paid", "bought", "sold", "married",
                "was at", "were at", "entered", "arrived", "never went to", "never been to", "not presently in",
                "not currently in", "took the castle", "took the town", "won the tournament"
            };
            return actors.Any(q.Contains) && acts.Any(q.Contains);
        }

        private static bool LooksLikeWorldHistoryLookupQuestion(string text)
        {
            string q = (text ?? string.Empty).Trim().ToLowerInvariant();
            if (q.Length < 6 || LooksLikeHistoricalWorldClaim(q)) return false;
            string[] explicitHistory =
            {
                "world history", "campaign history", "campaign record", "canonical history", "canonical campaign",
                "historical record", "according to the record", "the record says"
            };
            string[] eventQuestions =
            {
                "what happened to", "who was responsible", "whose party was responsible", "who destroyed",
                "who defeated", "who captured", "who killed", "who conquered", "who besieged", "who rescued",
                "who led", "who fought"
            };
            string[] reportedClaims = { "traveler claims", "traveller claims", "he claims", "she claims", "they claim", "reportedly", "was said to have" };
            return explicitHistory.Any(q.Contains) || eventQuestions.Any(q.Contains) || reportedClaims.Any(q.Contains);
        }

        private static async Task<JObject> GetJsonAsync(string route, HttpClient requestClient = null)
        {
            await ReignSaveSyncCoordinator.WaitForReadyAsync(route).ConfigureAwait(false);
            await ReignCampaignInitializationGate.WaitForReadyAsync(route).ConfigureAwait(false);
            if (!ReignServerEndpoint.TryBeginRequest(out string unavailable))
            {
                throw new HttpRequestException(unavailable);
            }

            Exception lastTransportFailure = null;
            foreach (string baseUrl in ReignServerEndpoint.CandidateBaseUrls())
            {
                string url = baseUrl.TrimEnd('/') + (route.StartsWith("/", StringComparison.Ordinal) ? route : "/" + route);
                try
                {
                    using (HttpResponseMessage response = await (requestClient ?? Client).GetAsync(url).ConfigureAwait(false))
                    {
                        string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        ReignServerEndpoint.ReportSuccess(baseUrl);
                        if (!response.IsSuccessStatusCode)
                        {
                            throw new InvalidOperationException("HTTP " + (int)response.StatusCode + ": " + body);
                        }

                        return JObject.Parse(body);
                    }
                }
                catch (Exception ex) when (ReignServerEndpoint.IsTransportFailure(ex))
                {
                    lastTransportFailure = ex;
                }
            }

            ReignServerEndpoint.ReportTransportFailure();
            throw lastTransportFailure ?? new HttpRequestException("Local Bannerlord Reign server is unavailable.");
        }

        public static Task<JObject> SetSocialBalanceUnderlyingAffinityAsync(JObject payload)
        {
            return PostJsonAsync("/tests/social-balance/affinity/set", payload ?? new JObject());
        }

        public static Task<JObject> RankSocialBalanceAffairCandidatesAsync(JObject payload)
        {
            return PostJsonAsync("/tests/social-balance/affair/candidates", payload ?? new JObject());
        }

        internal static async Task<JObject> PostJsonAsync(string route, JObject payload)
        {
            await ReignSaveSyncCoordinator.WaitForReadyAsync(route).ConfigureAwait(false);
            await ReignCampaignInitializationGate.WaitForReadyAsync(route).ConfigureAwait(false);
            bool liveTestRoute = route.StartsWith(
                "/tests/live/", StringComparison.OrdinalIgnoreCase);
            // The production bridge is itself the recovery/control plane. An
            // unrelated passive-system timeout must not place heartbeat, polling,
            // acknowledgement, or lifecycle traffic behind the shared endpoint
            // circuit breaker. Live routes use their own short-timeout client and
            // always probe loopback directly; a successful probe still clears the
            // shared breaker because it proves the server is reachable.
            if (!liveTestRoute
                && !ReignServerEndpoint.TryBeginRequest(out string unavailable))
            {
                throw new HttpRequestException(unavailable);
            }

            HttpClient routeClient = liveTestRoute
                ? LiveTestClient
                : IsProviderBackedInteractiveRoute(route) ? InteractiveClient
                : IsPriorityServerRoute(route) ? PriorityClient : Client;
            Exception lastTransportFailure = null;
            foreach (string baseUrl in ReignServerEndpoint.CandidateBaseUrls())
            {
                string url = baseUrl.TrimEnd('/') + (route.StartsWith("/", StringComparison.Ordinal) ? route : "/" + route);
                Stopwatch timer = Stopwatch.StartNew();
                ReignJsonHttpContent content = null;
                try
                {
                    content = new ReignJsonHttpContent(payload ?? new JObject());
                    using (content)
                    using (HttpRequestMessage request = new HttpRequestMessage(
                        HttpMethod.Post,
                        url))
                    {
                        request.Content = content;
                        using (HttpResponseMessage response = await routeClient.SendAsync(
                            request,
                            HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false))
                        {
                            string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                            timer.Stop();
                            RecordStreamedPost(content);
                            // Successful empty control-plane polls are intentionally
                            // quiet. Logging several of them per second was itself a
                            // large source of transient strings and file I/O during
                            // unattended runs. Slow calls and all substantive routes
                            // remain visible.
                            if (!IsHighFrequencyControlRoute(route)
                                || timer.ElapsedMilliseconds >= 250
                                || !response.IsSuccessStatusCode)
                            {
                                ReignLog.Info("HTTP POST " + route
                                    + " status=" + (int)response.StatusCode
                                    + " clientMs=" + timer.ElapsedMilliseconds
                                    + " requestBytes=" + content.SerializedBytes
                                    + " serializationMs=" + content.SerializationDurationMs
                                    + " responseChars=" + body.Length);
                            }
                            ReignServerEndpoint.ReportSuccess(baseUrl);
                            if (!response.IsSuccessStatusCode)
                            {
                                throw new InvalidOperationException("HTTP " + (int)response.StatusCode + ": " + body);
                            }

                            return JObject.Parse(body);
                        }
                    }
                }
                catch (Exception ex) when (ReignServerEndpoint.IsTransportFailure(ex))
                {
                    RecordStreamedPost(content);
                    content?.Dispose();
                    lastTransportFailure = ex;
                }
            }

            if (!liveTestRoute)
                ReignServerEndpoint.ReportTransportFailure();
            throw lastTransportFailure ?? new HttpRequestException("Local Bannerlord Reign server is unavailable.");
        }

        public static Task<JObject> PostCastleSceneAsync(JObject payload)
        {
            return PostJsonAsync("/castle-chat/scene/generate", payload ?? new JObject());
        }

        public static Task<JObject> PostFamilyChambersSceneAsync(JObject payload)
        {
            return PostJsonAsync("/family-chambers/scene/generate", payload ?? new JObject());
        }

        public static async Task<bool> ReconcileChildhoodMaturityAsync(Hero hero)
        {
            if (hero == null || hero.IsChild || string.IsNullOrWhiteSpace(hero.StringId)) return false;
            JObject response = await PostJsonAsync("/family-chambers/childhood/mature", new JObject
            {
                ["campaignId"] = GetCampaignId(), ["timelineId"] = ReignWorldHistoryCampaignBehavior.Instance?.TimelineId ?? "main",
                ["hero"] = BuildHeroProfile(hero), ["worldDay"] = CampaignTime.Now.ToDays
            }).ConfigureAwait(false);
            return response.Value<bool?>("ok") == true;
        }

        internal static JObject BuildFamilyChambersHeroProfile(Hero hero)
        {
            JObject profile = BuildHeroProfile(hero);
            int? familyRelation = ReignCourtCampaignBehavior.Instance?.GetFamilyDirectionalRelation(hero);
            if (familyRelation.HasValue) profile["relationToPlayer"] = familyRelation.Value;
            if (hero?.CharacterObject == null) return profile;

            FaceGenerationParams face = FaceGenerationParams.Create();
            MBBodyProperties.GetParamsFromKey(ref face, hero.BodyProperties, earsAreHidden: false, mouthHidden: false);
            profile["familyVisualAppearance"] = new JObject
            {
                ["ancestry"] = FamilyCultureAncestry(hero.Culture?.StringId),
                ["nativeRace"] = hero.CharacterObject.Race == 0 ? "human" : "human of a native Calradian ancestry",
                ["hairColor"] = FamilyHairColor(face.CurrentHairColorOffset),
                ["skinTone"] = FamilySkinTone(face.CurrentSkinColorOffset),
                ["eyeColor"] = FamilyEyeColor(face.CurrentEyeColorOffset)
            };
            return profile;
        }

        private static string FamilyCultureAncestry(string cultureId)
        {
            switch ((cultureId ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "sturgia": return "Sturgian";
                case "vlandia": return "Vlandian";
                case "battania": return "Battanian";
                case "aserai": return "Aserai";
                case "khuzait": return "Khuzait";
                case "empire": return "Imperial Calradian";
                default: return "Calradian";
            }
        }

        private static string FamilyHairColor(float value)
        {
            if (value < 0.2f) return "black";
            if (value < 0.4f) return "dark brown";
            if (value < 0.6f) return "chestnut brown";
            if (value < 0.8f) return "light brown";
            return "fair blond";
        }

        private static string FamilySkinTone(float value)
        {
            if (value < 0.2f) return "deep brown";
            if (value < 0.4f) return "warm brown";
            if (value < 0.6f) return "olive";
            if (value < 0.8f) return "light olive";
            return "fair";
        }

        private static string FamilyEyeColor(float value)
        {
            if (value < 0.25f) return "dark brown";
            if (value < 0.5f) return "hazel";
            if (value < 0.75f) return "gray";
            return "blue";
        }

        public static Task<JObject> GenerateSharedPortraitAsync(string cacheKey)
        {
            return PostJsonAsync("/portraits/shared-generation/generate-one", new JObject
            {
                ["cacheKey"] = cacheKey ?? string.Empty,
                ["force"] = false
            });
        }

        private static async Task AttachContextBundlesAsync(JObject payload, string mode, Hero speaker, IEnumerable<Hero> activeHeroes)
        {
            if (payload == null)
            {
                return;
            }

            List<Hero> participants = (activeHeroes ?? Enumerable.Empty<Hero>()).Where(x => x != null).Distinct().ToList();
            payload["opportunitySnapshot"] = await ReignMainThread.InvokeAsync(() =>
                BuildConversationOpportunitySnapshot(speaker, mode, participants)).ConfigureAwait(false);

            Stopwatch selectorTimer = Stopwatch.StartNew();
            try
            {
                JObject selectPayload = new JObject
                {
                    ["mode"] = mode ?? "dialogue",
                    ["campaignId"] = payload.Value<string>("campaignId") ?? GetCampaignId(),
                    ["correlationId"] = payload.Value<string>("correlationId") ?? NewCorrelationId(mode, speaker?.StringId),
                    ["heroStringId"] = speaker?.StringId
                        ?? payload.Value<string>("speakerHeroStringId")
                        ?? payload.Value<string>("heroStringId")
                        ?? string.Empty,
                    ["playerText"] = payload.Value<string>("playerText") ?? string.Empty,
                    ["sceneContext"] = payload.Value<string>("sceneContext") ?? string.Empty,
                    ["availablePullIds"] = new JArray(ContextPullIds)
                };
                if (string.IsNullOrWhiteSpace(payload.Value<string>("correlationId")))
                {
                    payload["correlationId"] = selectPayload.Value<string>("correlationId") ?? NewCorrelationId(mode, speaker?.StringId);
                    selectPayload["correlationId"] = payload.Value<string>("correlationId");
                }

                JObject selection = await PostJsonAsync("/context/select", selectPayload).ConfigureAwait(false);
                selectorTimer.Stop();
                JArray selectedPulls = selection["selectedPulls"] as JArray ?? new JArray();
                Stopwatch pullTimer = Stopwatch.StartNew();
                JArray bundles = await ReignMainThread.InvokeAsync(() =>
                    BuildContextBundles(selectedPulls, speaker, participants, mode)).ConfigureAwait(false);
                if (selectedPulls.OfType<JObject>().Any(x => string.Equals(x.Value<string>("id") ?? x.Value<string>("pullId"), "verify_world_history", StringComparison.OrdinalIgnoreCase)))
                {
                    Stopwatch historyTimer = Stopwatch.StartNew();
                    try
                    {
                        string playerText = payload.Value<string>("playerText") ?? string.Empty;
                        float worldDay = TaleWorlds.CampaignSystem.Campaign.Current == null ? 0f : (float)CampaignTime.Now.ToDays;
                        bool lookupOnly = LooksLikeWorldHistoryLookupQuestion(playerText);
                        JObject verification = lookupOnly
                            ? await VerifyWorldHistoryAsync(playerText, string.Empty, speaker?.StringId ?? string.Empty,
                                speaker?.Clan?.Kingdom?.StringId ?? speaker?.MapFaction?.StringId ?? string.Empty, worldDay,
                                ReignWorldHistoryCampaignBehavior.Instance?.TimelineId ?? "main").ConfigureAwait(false)
                            : await CheckWorldHistoryLieAsync(playerText, Hero.MainHero, speaker, worldDay,
                                ReignWorldHistoryCampaignBehavior.Instance?.TimelineId ?? "main", mode).ConfigureAwait(false);
                        historyTimer.Stop();
                        JObject knowledgeSafe;
                        if (lookupOnly)
                        {
                            JObject speakerKnowledge = verification["speakerKnowledge"] as JObject ?? new JObject();
                            knowledgeSafe = new JObject
                            {
                                ["lookupOutcome"] = verification.Value<string>("objectiveVerdict") ?? "not_found",
                                ["speakerVerdict"] = verification.Value<string>("speakerVerdict") ?? "unknown",
                                ["knowledgeBasis"] = speakerKnowledge.Value<string>("basis") ?? "none",
                                ["knownEvidenceEventIds"] = (speakerKnowledge["eventIds"] ?? new JArray()).DeepClone(),
                                ["knownEvents"] = (speakerKnowledge["events"] ?? new JArray()).DeepClone(),
                                ["explanation"] = verification.Value<string>("explanation") ?? string.Empty,
                                ["privacyRule"] = "Use only these speaker-visible canonical events. Do not reveal objective events omitted here.",
                                ["behaviorRule"] = "Answer from the listed canonical events. Preserve unknown locations, motives, cargo, and other details as unknown."
                            };
                        }
                        else
                        {
                            knowledgeSafe = verification["promptPacket"] as JObject ?? new JObject
                            {
                                ["detectionOutcome"] = "not_adjudicable",
                                ["speakerVerdict"] = "unknown",
                                ["behaviorRule"] = "The character lacks reliable evidence. Do not accuse anyone from missing evidence."
                            };
                        }
                        knowledgeSafe["lieCheckId"] = verification.Value<string>("lieCheckId") ?? string.Empty;
                        bundles.Add(ContextBundle("verify_world_history", ContextTitle("verify_world_history"), knowledgeSafe, historyTimer.ElapsedMilliseconds, verification.Value<bool?>("ok") == true, verification.Value<string>("error") ?? string.Empty));
                    }
                    catch (Exception ex)
                    {
                        historyTimer.Stop();
                        bundles.Add(ContextBundle("verify_world_history", ContextTitle("verify_world_history"), new JObject
                        {
                            ["speakerVerdict"] = "unknown", ["explanation"] = "World-history evidence was unavailable. Missing evidence is not proof of a lie."
                        }, historyTimer.ElapsedMilliseconds, false, ex.Message));
                    }
                }
                pullTimer.Stop();

                payload["selectedContextPulls"] = selectedPulls;
                payload["contextBundles"] = bundles;
                payload["contextSelection"] = selection;
                payload["clientContextPullTiming"] = new JObject
                {
                    ["selectorMs"] = selectorTimer.ElapsedMilliseconds,
                    ["pullMs"] = pullTimer.ElapsedMilliseconds,
                    ["bundleCount"] = bundles.Count,
                    ["bundleChars"] = bundles.ToString(Formatting.None).Length
                };

                ReignLog.Info("Context selector mode=" + (mode ?? "")
                    + " hero=" + (speaker?.StringId ?? "")
                    + " selected=" + string.Join(",", selectedPulls.OfType<JObject>().Select(x => x.Value<string>("id")).Where(x => !string.IsNullOrWhiteSpace(x)))
                    + " selectorMs=" + selectorTimer.ElapsedMilliseconds
                    + " pullMs=" + pullTimer.ElapsedMilliseconds
                    + " bundles=" + bundles.Count
                    + " bundleChars=" + bundles.ToString(Formatting.None).Length
                    + " fallback=" + (selection.Value<bool?>("fallback") == true));
            }
            catch (Exception ex)
            {
                selectorTimer.Stop();
                JArray selectedPulls = new JArray
                {
                };
                JArray bundles = await ReignMainThread.InvokeAsync(() =>
                    BuildContextBundles(selectedPulls, speaker, participants, mode)).ConfigureAwait(false);
                payload["selectedContextPulls"] = selectedPulls;
                payload["contextBundles"] = bundles;
                payload["contextSelection"] = new JObject
                {
                    ["ok"] = false,
                    ["fallback"] = true,
                    ["error"] = ex.Message,
                    ["durationMs"] = selectorTimer.ElapsedMilliseconds
                };
                payload["clientContextPullTiming"] = new JObject
                {
                    ["selectorMs"] = selectorTimer.ElapsedMilliseconds,
                    ["pullMs"] = 0,
                    ["bundleCount"] = bundles.Count,
                    ["bundleChars"] = bundles.ToString(Formatting.None).Length
                };
                ReignLog.Warn("Context selector failed; continuing without live context pulls: " + ex.Message);
            }
        }

        private static JArray BuildContextBundles(JArray selectedPulls, Hero speaker, IEnumerable<Hero> activeHeroes, string mode)
        {
            JArray bundles = new JArray();
            if (selectedPulls == null)
            {
                return bundles;
            }

            foreach (JObject pull in selectedPulls.OfType<JObject>())
            {
                string id = pull.Value<string>("id") ?? pull.Value<string>("pullId") ?? string.Empty;
                if (string.IsNullOrWhiteSpace(id))
                {
                    continue;
                }

                JObject arguments = pull["arguments"] as JObject ?? new JObject();
                Stopwatch timer = Stopwatch.StartNew();
                try
                {
                    JObject data = null;
                    switch (id)
                    {
                        case "check_inventory_appearance":
                            data = BuildInventoryAppearanceBundle(speaker);
                            break;
                        case "check_player_appearance_status":
                            data = BuildPlayerAppearanceStatusBundle();
                            break;
                        case "nearby_settlements":
                            data = BuildNearbySettlementsBundle(arguments, speaker);
                            break;
                        case "nearby_bandit_parties":
                            data = BuildNearbyPartiesBundle(arguments, speaker, true);
                            break;
                        case "nearby_lord_parties":
                            data = BuildNearbyPartiesBundle(arguments, speaker, false);
                            break;
                        case "current_settlement_facts":
                            data = BuildCurrentSettlementFactsBundle(speaker);
                            break;
                        case "kingdom_diplomacy_status":
                            data = BuildKingdomDiplomacyBundle(speaker);
                            break;
                        case "clan_wealth_and_influence":
                            data = BuildClanWealthInfluenceBundle(speaker);
                            break;
                        case "appraise_trade_offer":
                            data = BuildTradeAppraisalBundle(speaker);
                            break;
                        case "relevant_memory":
                        case "relationship_history":
                        case "verify_world_history":
                            data = null;
                            break;
                        default:
                            data = new JObject { ["note"] = "Unknown pull id requested by selector." };
                            break;
                    }

                    timer.Stop();
                    if (data != null)
                    {
                        bundles.Add(ContextBundle(id, ContextTitle(id), data, timer.ElapsedMilliseconds, true, string.Empty));
                    }
                }
                catch (Exception ex)
                {
                    timer.Stop();
                    bundles.Add(ContextBundle(id, ContextTitle(id), new JObject(), timer.ElapsedMilliseconds, false, ex.Message));
                    ReignLog.Warn("Context pull failed id=" + id + ": " + ex.Message);
                }
            }

            return bundles;
        }

        private static JObject BuildInventoryAppearanceBundle(Hero speaker)
        {
            Hero target = speaker ?? Hero.MainHero;
            return new JObject
            {
                ["subject"] = HeroReference(target),
                ["inventory"] = BuildInventoryProfile(target),
                ["appearance"] = BuildAppearanceProfile(target),
                ["wealth"] = BuildWealthProfile(target)
            };
        }

        private static JObject BuildPlayerAppearanceStatusBundle()
        {
            Hero player = Hero.MainHero;
            return new JObject
            {
                ["subject"] = HeroReference(player),
                ["appearance"] = CompactAppearanceProfile(player),
                ["wealth"] = CompactWealthProfile(player),
                ["interpretationRule"] = "NPCs should react first to visible presentation, clothing, weapons, and apparent station. True lordship or clan status may be missed if the presentation is poor or disguised."
            };
        }

        private static JObject BuildConversationOpportunitySnapshot(Hero speaker, string mode, IEnumerable<Hero> activeHeroes)
        {
            Hero player = Hero.MainHero;
            List<Hero> witnesses = (activeHeroes ?? Enumerable.Empty<Hero>())
                .Where(x => x != null && x != speaker && x != player && !string.IsNullOrWhiteSpace(x.StringId)).Distinct().Take(16).ToList();
            bool adults = speaker != null && player != null && speaker.Age >= 18f && player.Age >= 18f;
            bool closeKin = AreCloseKin(speaker, player);
            bool nativeSuitable = adults && speaker != player && speaker.IsFemale != player.IsFemale && !closeKin;
            string normalizedMode = string.IsNullOrWhiteSpace(mode) ? "dialogue" : mode.ToLowerInvariant();
            bool privateScene = normalizedMode == "correspondence";
            double exposure = privateScene ? 0.08d : normalizedMode == "social_event" || normalizedMode == "party_chat" ? 0.85d : 0.40d;
            exposure = Math.Min(1d, exposure + witnesses.Count * 0.03d);
            bool coercive = speaker?.IsPrisoner == true || player?.IsPrisoner == true;
            JObject snapshot = new JObject
            {
                ["model"] = "reign_relative_opportunity_v1",
                ["observer"] = BuildOpportunityHeroSnapshot(speaker),
                ["target"] = BuildOpportunityHeroSnapshot(player),
                ["identityKnown"] = false,
                ["suitability"] = new JObject
                {
                    ["adults"] = adults,
                    ["nativeSuitable"] = nativeSuitable,
                    ["closeKin"] = closeKin,
                    ["marriageBlocks"] = false
                },
                ["scene"] = new JObject
                {
                    ["private"] = privateScene,
                    ["exposure"] = exposure,
                    ["coercive"] = coercive,
                    ["witnessIds"] = new JArray(witnesses.Select(x => x.StringId))
                }
            };
            ReignLiveInteractionTestHost
                .ApplyManipulationFixtureOpportunityEvidence(snapshot);
            return snapshot;
        }

        private static JObject BuildOpportunityHeroSnapshot(Hero hero)
        {
            JObject wealth = CompactWealthProfile(hero);
            JObject appearance = CompactAppearanceProfile(hero);
            float clanStrength = 0f;
            float influence = 0f;
            try
            {
                clanStrength = hero?.Clan?.CurrentTotalStrength ?? 0f;
                influence = hero?.Clan?.Influence ?? 0f;
            }
            catch
            {
                clanStrength = 0f;
                influence = 0f;
            }
            int leadership = 0;
            try { leadership = hero?.GetSkillValue(DefaultSkills.Leadership) ?? 0; } catch { leadership = 0; }
            return new JObject
            {
                ["heroStringId"] = hero?.StringId ?? string.Empty,
                ["name"] = hero?.Name?.ToString() ?? string.Empty,
                ["age"] = hero?.Age ?? 0f,
                ["isFemale"] = hero?.IsFemale ?? false,
                ["isRuler"] = hero != null && hero.Clan?.Kingdom?.Leader == hero,
                ["isLord"] = hero?.IsLord ?? false,
                ["isPrisoner"] = hero?.IsPrisoner ?? false,
                ["spouseId"] = hero?.Spouse?.StringId ?? string.Empty,
                ["fatherId"] = hero?.Father?.StringId ?? string.Empty,
                ["motherId"] = hero?.Mother?.StringId ?? string.Empty,
                ["childrenIds"] = new JArray((hero?.Children ?? new List<Hero>()).Where(x => x != null).Select(x => x.StringId)),
                ["clanTier"] = hero?.Clan?.Tier ?? 0,
                ["fiefCount"] = hero?.Clan?.Fiefs?.Count ?? 0,
                ["renown"] = hero?.Clan?.Renown ?? 0f,
                ["influence"] = influence,
                ["leadership"] = leadership,
                ["partyStrength"] = HeroPartyStrength(hero),
                ["clanStrength"] = clanStrength,
                ["wealth"] = wealth,
                ["appearance"] = appearance
            };
        }

        private static JObject CompactWealthProfile(Hero hero)
        {
            JObject full = BuildNpcObservableWealthProfile(hero);
            return new JObject
            {
                ["version"] = full["version"], ["economicCapacityKnown"] = full["economicCapacityKnown"],
                ["partyInventoryState"] = full["partyInventoryState"],
                ["gold"] = full["gold"], ["personalWealthTier"] = full["personalWealthTier"],
                ["visibleWealthTier"] = full["visibleWealthTier"], ["partyInventoryValue"] = full["partyInventoryValue"],
                ["clanGold"] = full["clanGold"], ["clanWealthTier"] = full["clanWealthTier"],
                ["clanTier"] = full["clanTier"], ["clanRenown"] = full["clanRenown"],
                ["clanFiefCount"] = full["clanFiefCount"], ["clanSocialCredit"] = full["clanSocialCredit"]
            };
        }

        private static JObject CompactAppearanceProfile(Hero hero)
        {
            JObject full = BuildAppearanceProfile(hero);
            return new JObject
            {
                ["trueStatusScore"] = full["trueStatusScore"], ["trueStatusLabel"] = full["trueStatusLabel"],
                ["visibleStatusScore"] = full["visibleStatusScore"], ["visibleStatusLabel"] = full["visibleStatusLabel"],
                ["statusMismatch"] = full["statusMismatch"], ["presentationEffect"] = full["presentationEffect"],
                ["firstView"] = full["firstView"], ["civilianEquipmentValue"] = full["civilianEquipmentValue"],
                ["battleEquipmentValue"] = full["battleEquipmentValue"], ["attractiveness"] = 50
            };
        }

        private static bool AreCloseKin(Hero first, Hero second)
        {
            if (first == null || second == null) return false;
            if (first.Father == second || first.Mother == second || second.Father == first || second.Mother == first) return true;
            if (first.Father != null && first.Father == second.Father) return true;
            if (first.Mother != null && first.Mother == second.Mother) return true;
            return (first.Children ?? new List<Hero>()).Contains(second) || (second.Children ?? new List<Hero>()).Contains(first);
        }

        private static JObject BuildNearbySettlementsBundle(JObject arguments, Hero speaker)
        {
            int radius = ReadInt(arguments, "radius", 80);
            int maxCount = ReadInt(arguments, "maxCount", 8);
            bool hasOrigin = TryGetReferencePosition(speaker, out Vec2 origin);
            JArray settlements = new JArray();
            if (hasOrigin)
            {
                float radiusSq = radius <= 0 ? float.MaxValue : radius * radius;
                foreach (Settlement settlement in Settlement.All
                    .Where(x => x != null && !x.IsHideout)
                    .Select(x => new { Settlement = x, DistanceSq = origin.DistanceSquared(x.GetPosition2D) })
                    .Where(x => radiusSq == float.MaxValue || x.DistanceSq <= radiusSq)
                    .OrderBy(x => x.DistanceSq)
                    .Take(Math.Max(1, maxCount))
                    .Select(x => x.Settlement))
                {
                    settlements.Add(SettlementSummary(settlement, origin, true));
                }
            }

            return new JObject
            {
                ["originKnown"] = hasOrigin,
                ["radius"] = radius,
                ["maxCount"] = maxCount,
                ["items"] = settlements
            };
        }

        private static JObject BuildNearbyPartiesBundle(JObject arguments, Hero speaker, bool banditsOnly)
        {
            int radius = ReadInt(arguments, "radius", banditsOnly ? 60 : 90);
            int maxCount = ReadInt(arguments, "maxCount", banditsOnly ? 8 : 10);
            bool hasOrigin = TryGetReferencePosition(speaker, out Vec2 origin);
            JArray parties = new JArray();
            if (hasOrigin)
            {
                float radiusSq = radius <= 0 ? float.MaxValue : radius * radius;
                IEnumerable<MobileParty> query = MobileParty.All
                    .Where(x => x != null && x.IsActive && x != MobileParty.MainParty)
                    .Where(x => banditsOnly ? PartyLooksBandit(x) : x.LeaderHero != null && x.LeaderHero.IsLord);

                foreach (MobileParty party in query
                    .Select(x => new { Party = x, DistanceSq = origin.DistanceSquared(x.GetPosition2D) })
                    .Where(x => radiusSq == float.MaxValue || x.DistanceSq <= radiusSq)
                    .OrderBy(x => x.DistanceSq)
                    .Take(Math.Max(1, maxCount))
                    .Select(x => x.Party))
                {
                    parties.Add(PartySummary(party, origin));
                }
            }

            return new JObject
            {
                ["originKnown"] = hasOrigin,
                ["radius"] = radius,
                ["maxCount"] = maxCount,
                ["kind"] = banditsOnly ? "bandits_or_outlaws" : "lord_parties",
                ["items"] = parties
            };
        }

        private static JObject BuildCurrentSettlementFactsBundle(Hero speaker)
        {
            Settlement settlement = Settlement.CurrentSettlement ?? MobileParty.MainParty?.CurrentSettlement ?? speaker?.CurrentSettlement;
            if (settlement == null)
            {
                return new JObject { ["available"] = false, ["reason"] = "No current settlement is known to the client." };
            }

            return new JObject
            {
                ["available"] = true,
                ["settlement"] = SettlementSummary(settlement, Vec2.Zero, false)
            };
        }

        private static JObject BuildKingdomDiplomacyBundle(Hero speaker)
        {
            Kingdom playerKingdom = Clan.PlayerClan?.Kingdom;
            Kingdom speakerKingdom = speaker?.Clan?.Kingdom;
            JObject result = new JObject
            {
                ["playerKingdom"] = KingdomSummary(playerKingdom),
                ["speakerKingdom"] = KingdomSummary(speakerKingdom),
                ["playerGovernment"] = ReignGovernmentCampaignBehavior.Instance?.BuildPublicSnapshot(playerKingdom)
                    ?? new JObject { ["available"] = false, ["authoritative"] = true },
                ["speakerGovernment"] = ReignGovernmentCampaignBehavior.Instance?.BuildPublicSnapshot(speakerKingdom)
                    ?? new JObject { ["available"] = false, ["authoritative"] = true },
                ["sameKingdom"] = playerKingdom != null && speakerKingdom != null && playerKingdom == speakerKingdom,
                ["atWarWithEachOther"] = playerKingdom != null && speakerKingdom != null && playerKingdom.IsAtWarWith(speakerKingdom)
            };

            result["playerWars"] = KingdomWars(playerKingdom, 12);
            result["speakerWars"] = KingdomWars(speakerKingdom, 12);
            return result;
        }

        private static JObject BuildNativePoliticalContext(Hero observer)
        {
            Kingdom playerKingdom = Clan.PlayerClan?.Kingdom;
            Kingdom observerKingdom = observer?.Clan?.Kingdom;
            Settlement settlement = Settlement.CurrentSettlement
                ?? MobileParty.MainParty?.CurrentSettlement
                ?? observer?.CurrentSettlement;
            // Transient civic evidence never enters the formal identity roster.
            // Keep this inline to preserve Bannerlord's generated type inventory.
            Settlement civicHome = null;
            string civicSource = "";
            if (observer != null && observer.IsNotable)
            {
                civicHome = observer.HomeSettlement;
                if (civicHome != null) civicSource = "native_home_settlement";
                else if (settlement != null && settlement.Notables.Contains(observer))
                {
                    civicHome = settlement;
                    civicSource = "native_notable_roster";
                }
            }
            Kingdom civicRealm = civicHome?.OwnerClan?.Kingdom;
            JObject civicAffiliation = new JObject
            {
                ["authoritative"] = true, ["available"] = civicHome != null,
                ["source"] = civicSource, ["heroStringId"] = observer?.StringId ?? "",
                ["settlementId"] = civicHome?.StringId ?? "", ["kingdomId"] = civicRealm?.StringId ?? "",
                ["kingdomName"] = civicRealm?.Name?.ToString() ?? "",
                ["sovereignHeroStringId"] = civicRealm?.Leader?.StringId ?? "",
                ["ownerClanId"] = civicHome?.OwnerClan?.StringId ?? "",
                ["governorHeroId"] = civicHome?.Town?.Governor?.StringId ?? "",
                ["localEncounter"] = civicHome != null && civicHome == settlement
            };
            return new JObject
            {
                ["authoritative"] = true,
                ["observedWorldDay"] = TaleWorlds.CampaignSystem.Campaign.Current == null
                    ? 0d
                    : CampaignTime.Now.ToDays,
                ["observer"] = BuildIdentityRosterEntry(observer),
                ["observerCivicAffiliation"] = civicAffiliation,
                ["subject"] = BuildPlayerIdentityContext(),
                ["playerGovernment"] = ReignGovernmentCampaignBehavior.Instance?.BuildPublicSnapshot(playerKingdom)
                    ?? new JObject { ["available"] = false, ["authoritative"] = true },
                ["observerGovernment"] = ReignGovernmentCampaignBehavior.Instance?.BuildGovernmentKnowledgeForSpeaker(observer)
                    ?? new JObject { ["available"] = false, ["authoritative"] = true },
                ["settlement"] = settlement == null
                    ? new JObject { ["available"] = false }
                    : SettlementSummary(settlement, Vec2.Zero, false),
                ["diplomacy"] = new JObject
                {
                    ["volatileCurrentState"] = true,
                    ["playerKingdom"] = KingdomSummary(playerKingdom),
                    ["observerKingdom"] = KingdomSummary(observerKingdom),
                    ["playerEnemyKingdoms"] = KingdomWars(playerKingdom, 32),
                    ["observerEnemyKingdoms"] = KingdomWars(observerKingdom, 32),
                    ["sameKingdom"] = playerKingdom != null
                        && observerKingdom != null
                        && playerKingdom == observerKingdom
                }
            };
        }

        private static JObject BuildClanWealthInfluenceBundle(Hero speaker)
        {
            JObject playerWealth =
                BuildNpcObservableWealthProfile(Hero.MainHero);
            JObject playerClan = ClanStanding(Clan.PlayerClan);
            ReignLiveInteractionTestHost
                .ApplyNpcObservableClanEvidence(
                    Hero.MainHero, playerClan);
            return new JObject
            {
                ["player"] = new JObject
                {
                    ["hero"] = HeroReference(Hero.MainHero),
                    ["wealth"] = playerWealth,
                    ["clan"] = playerClan
                },
                ["speaker"] = new JObject
                {
                    ["hero"] = HeroReference(speaker),
                    ["wealth"] = BuildWealthProfile(speaker),
                    ["clan"] = ClanStanding(speaker?.Clan)
                }
            };
        }

        private static JObject BuildTradeAppraisalBundle(Hero speaker)
        {
            JArray workshops = new JArray();
            foreach (JObject workshop in BuildResolverWorkshops().OfType<JObject>()
                .Where(x =>
                    string.Equals(x.Value<string>("ownerHeroStringId"), Hero.MainHero?.StringId, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(x.Value<string>("ownerHeroStringId"), speaker?.StringId, StringComparison.OrdinalIgnoreCase))
                .Take(24))
            {
                workshops.Add(workshop);
            }

            Settlement current = Settlement.CurrentSettlement ?? MobileParty.MainParty?.CurrentSettlement ?? speaker?.CurrentSettlement;
            return new JObject
            {
                ["pricingAuthority"] = "Bannerlord Reign game-side value service. The LLM may discuss value, but resolver/validator/executor make final price and legality decisions.",
                ["fairnessRule"] = "Normal gameplay rejects unfair exchanges unless the NPC truly offers better terms. Beta prompt override can bypass fairness for testing but not missing ownership/assets.",
                ["player"] = new JObject
                {
                    ["hero"] = HeroReference(Hero.MainHero),
                    ["wealth"] =
                        BuildNpcObservableWealthProfile(Hero.MainHero),
                    ["inventory"] = BuildInventoryProfile(Hero.MainHero),
                    ["appearance"] = BuildAppearanceProfile(Hero.MainHero),
                    ["prisoners"] = BuildPrisonerArray(MobileParty.MainParty, 24)
                },
                ["speaker"] = new JObject
                {
                    ["hero"] = HeroReference(speaker),
                    ["wealth"] = BuildWealthProfile(speaker),
                    ["inventory"] = BuildInventoryProfile(speaker),
                    ["appearance"] = BuildAppearanceProfile(speaker),
                    ["prisoners"] = BuildPrisonerArray(speaker?.PartyBelongedTo, 24)
                },
                ["currentSettlement"] = current == null ? new JObject { ["available"] = false } : SettlementSummary(current, Vec2.Zero, false),
                ["ownedWorkshopsInvolved"] = workshops
            };
        }

        private static JObject ContextBundle(string id, string title, JObject data, long durationMs, bool ok, string error)
        {
            JObject bundle = new JObject
            {
                ["id"] = id ?? string.Empty,
                ["title"] = title ?? string.Empty,
                ["ok"] = ok,
                ["source"] = "game_client",
                ["durationMs"] = durationMs,
                ["data"] = data ?? new JObject()
            };
            if (!ok && !string.IsNullOrWhiteSpace(error))
            {
                bundle["error"] = error;
            }

            return bundle;
        }

        private static JObject HeroReference(Hero hero)
        {
            return new JObject
            {
                ["heroStringId"] = hero?.StringId ?? string.Empty,
                ["name"] = hero?.Name?.ToString() ?? string.Empty,
                ["clanId"] = hero?.Clan?.StringId ?? string.Empty,
                ["clanName"] = hero?.Clan?.Name?.ToString() ?? string.Empty,
                ["kingdomId"] = hero?.Clan?.Kingdom?.StringId ?? hero?.MapFaction?.StringId ?? string.Empty,
                ["kingdomName"] = hero?.Clan?.Kingdom?.InformalName?.ToString() ?? hero?.MapFaction?.Name?.ToString() ?? string.Empty,
                ["occupation"] = hero?.Occupation.ToString() ?? string.Empty,
                ["spouseId"] = hero?.Spouse?.StringId ?? string.Empty,
                ["fatherId"] = hero?.Father?.StringId ?? string.Empty,
                ["motherId"] = hero?.Mother?.StringId ?? string.Empty,
                ["currentSettlementId"] = hero?.CurrentSettlement?.StringId ?? string.Empty,
                ["homeSettlementId"] = hero?.HomeSettlement?.StringId ?? string.Empty,
                ["partyId"] = hero?.PartyBelongedTo?.StringId ?? string.Empty,
                ["governorOfSettlementId"] = hero?.GovernorOf?.Settlement?.StringId ?? string.Empty,
                ["prisonerHolderPartyId"] = hero?.PartyBelongedToAsPrisoner?.Id ?? string.Empty,
                ["isKingdomLeader"] = hero?.IsKingdomLeader ?? false,
                ["isClanLeader"] = hero?.IsClanLeader ?? false,
                ["isLord"] = hero?.IsLord ?? false,
                ["isNotable"] = hero?.IsNotable ?? false,
                ["isWanderer"] = hero?.IsWanderer ?? false,
                ["isPrisoner"] = hero?.IsPrisoner ?? false
            };
        }

        private static bool TryGetReferencePosition(Hero speaker, out Vec2 origin)
        {
            origin = Vec2.Zero;
            try
            {
                if (MobileParty.MainParty != null)
                {
                    origin = MobileParty.MainParty.GetPosition2D;
                    return true;
                }

                if (speaker?.PartyBelongedTo != null)
                {
                    origin = speaker.PartyBelongedTo.GetPosition2D;
                    return true;
                }

                Settlement settlement = Settlement.CurrentSettlement ?? speaker?.CurrentSettlement;
                if (settlement != null)
                {
                    origin = settlement.GetPosition2D;
                    return true;
                }
            }
            catch
            {
                origin = Vec2.Zero;
            }

            return false;
        }

        private static JObject SettlementSummary(Settlement settlement, Vec2 origin, bool includeDistance)
        {
            JObject result = new JObject
            {
                ["settlementId"] = settlement?.StringId ?? string.Empty,
                ["name"] = settlement?.Name?.ToString() ?? string.Empty,
                ["type"] = SettlementType(settlement),
                ["cultureId"] = settlement?.Culture?.StringId ?? string.Empty,
                ["ownerClanId"] = settlement?.OwnerClan?.StringId ?? string.Empty,
                ["ownerClanName"] = settlement?.OwnerClan?.Name?.ToString() ?? string.Empty,
                ["factionId"] = settlement?.MapFaction?.StringId ?? string.Empty,
                ["factionName"] = settlement?.MapFaction?.Name?.ToString() ?? string.Empty,
                ["kingdomId"] = settlement?.MapFaction?.StringId ?? settlement?.OwnerClan?.Kingdom?.StringId ?? string.Empty,
                ["kingdomName"] = settlement?.MapFaction?.Name?.ToString() ?? settlement?.OwnerClan?.Kingdom?.InformalName?.ToString() ?? string.Empty,
                ["isTown"] = settlement?.IsTown ?? false,
                ["isCastle"] = settlement?.IsCastle ?? false,
                ["isVillage"] = settlement?.IsVillage ?? false,
                ["isHideout"] = settlement?.IsHideout ?? false,
                ["isFortification"] = settlement?.IsFortification ?? false,
                ["isUnderSiege"] = settlement?.IsUnderSiege ?? false,
                ["isUnderRaid"] = settlement?.IsUnderRaid ?? false,
                ["militia"] = settlement?.Militia ?? 0f
            };

            if (includeDistance && settlement != null)
            {
                result["distance"] = Math.Sqrt(origin.DistanceSquared(settlement.GetPosition2D));
            }

            Town town = settlement?.Town;
            if (town != null)
            {
                result["prosperity"] = town.Prosperity;
                result["loyalty"] = town.Loyalty;
                result["security"] = town.Security;
                result["garrison"] = town.GarrisonParty?.MemberRoster?.TotalManCount ?? 0;
                result["boundVillages"] = settlement.BoundVillages?.Count ?? 0;
                result["governorHeroId"] = town.Governor?.StringId ?? string.Empty;
                result["governorName"] = town.Governor?.Name?.ToString() ?? string.Empty;
            }

            Village village = settlement?.Village;
            if (village != null)
            {
                result["boundSettlementId"] = village.Bound?.StringId ?? string.Empty;
                result["boundSettlementName"] = village.Bound?.Name?.ToString() ?? string.Empty;
                result["villageState"] = village.VillageState.ToString();
                result["isDeserted"] = village.IsDeserted;
            }

            return result;
        }

        private static string SettlementType(Settlement settlement)
        {
            if (settlement == null)
            {
                return "unknown";
            }

            if (settlement.IsTown)
            {
                return "town";
            }

            if (settlement.IsCastle)
            {
                return "castle";
            }

            if (settlement.IsVillage)
            {
                return "village";
            }

            if (settlement.IsHideout)
            {
                return "hideout";
            }

            return "settlement";
        }

        private static bool PartyLooksBandit(MobileParty party)
        {
            if (party == null)
            {
                return false;
            }

            if (party.IsBandit)
            {
                return true;
            }

            string text = ((party.StringId ?? "") + " "
                + SafePartyName(party) + " "
                + (party.MapFaction?.StringId ?? "") + " "
                + SafeFactionName(party.MapFaction)).ToLowerInvariant();
            return text.Contains("bandit")
                || text.Contains("looter")
                || text.Contains("raider")
                || text.Contains("outlaw")
                || text.Contains("sea_raider")
                || text.Contains("mountain_bandit")
                || text.Contains("forest_bandit")
                || text.Contains("steppe_bandit")
                || text.Contains("desert_bandit");
        }

        private static JObject PartySummary(MobileParty party, Vec2 origin)
        {
            return new JObject
            {
                ["partyId"] = party?.StringId ?? string.Empty,
                ["name"] = SafePartyName(party),
                ["leaderHeroId"] = party?.LeaderHero?.StringId ?? string.Empty,
                ["leaderName"] = party?.LeaderHero?.Name?.ToString() ?? string.Empty,
                ["factionId"] = party?.MapFaction?.StringId ?? string.Empty,
                ["factionName"] = SafeFactionName(party?.MapFaction),
                ["isBandit"] = party?.IsBandit ?? false,
                ["isLordParty"] = party?.IsLordParty ?? false,
                ["troops"] = party?.MemberRoster?.TotalManCount ?? 0,
                ["wounded"] = party?.MemberRoster?.TotalWounded ?? 0,
                ["distance"] = party == null ? 0d : Math.Sqrt(origin.DistanceSquared(party.GetPosition2D))
            };
        }

        private static string SafePartyName(MobileParty party)
        {
            try
            {
                return party?.Name?.ToString() ?? party?.StringId ?? string.Empty;
            }
            catch
            {
                return party?.StringId ?? string.Empty;
            }
        }

        private static string SafeFactionName(IFaction faction)
        {
            try
            {
                return faction?.Name?.ToString() ?? string.Empty;
            }
            catch
            {
                return faction?.StringId ?? string.Empty;
            }
        }

        private static JObject KingdomSummary(Kingdom kingdom)
        {
            if (kingdom == null)
            {
                return new JObject { ["available"] = false };
            }

            JArray enemyKingdoms = KingdomWars(kingdom, 32);
            return new JObject
            {
                ["available"] = true,
                ["kingdomId"] = kingdom.StringId ?? string.Empty,
                ["name"] = kingdom.InformalName?.ToString() ?? kingdom.Name?.ToString() ?? string.Empty,
                ["leaderHeroId"] = kingdom.Leader?.StringId ?? string.Empty,
                ["leaderName"] = kingdom.Leader?.Name?.ToString() ?? string.Empty,
                ["clanCount"] = kingdom.Clans?.Count ?? 0,
                ["townCount"] = kingdom.Towns?.Count ?? 0,
                ["armyCount"] = kingdom.Armies?.Count ?? 0,
                ["warCount"] = enemyKingdoms.Count,
                ["enemyKingdomIds"] = new JArray(enemyKingdoms
                    .OfType<JObject>()
                    .Select(x => x.Value<string>("kingdomId") ?? string.Empty)
                    .Where(x => !string.IsNullOrWhiteSpace(x))),
                ["strength"] = kingdom.CurrentTotalStrength
            };
        }

        private static JArray KingdomWars(Kingdom kingdom, int limit)
        {
            JArray wars = new JArray();
            if (kingdom?.FactionsAtWarWith == null)
            {
                return wars;
            }

            foreach (var faction in kingdom.FactionsAtWarWith
                .Where(x => x != null && x.IsKingdomFaction)
                .GroupBy(x => x.StringId ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .Select(x => x.First())
                .Take(Math.Max(1, limit)))
            {
                wars.Add(new JObject
                {
                    ["kingdomId"] = faction?.StringId ?? string.Empty,
                    ["factionId"] = faction?.StringId ?? string.Empty,
                    ["name"] = faction?.Name?.ToString() ?? string.Empty,
                    ["isKingdom"] = faction?.IsKingdomFaction ?? false,
                    ["strength"] = faction?.CurrentTotalStrength ?? 0f
                });
            }

            return wars;
        }

        private static JObject ClanStanding(Clan clan)
        {
            if (clan == null)
            {
                return new JObject { ["available"] = false };
            }

            return new JObject
            {
                ["available"] = true,
                ["clanId"] = clan.StringId ?? string.Empty,
                ["name"] = clan.Name?.ToString() ?? string.Empty,
                ["leaderHeroId"] = clan.Leader?.StringId ?? string.Empty,
                ["leaderName"] = clan.Leader?.Name?.ToString() ?? string.Empty,
                ["tier"] = clan.Tier,
                ["gold"] = clan.Gold,
                ["wealthTier"] = WealthTier(clan.Gold),
                ["renown"] = clan.Renown,
                ["influence"] = clan.Influence,
                ["fiefCount"] = clan.Fiefs?.Count ?? 0,
                ["kingdomId"] = clan.Kingdom?.StringId ?? string.Empty,
                ["kingdomName"] = clan.Kingdom?.InformalName?.ToString() ?? string.Empty
            };
        }

        private static string ContextTitle(string id)
        {
            switch (id ?? string.Empty)
            {
                case "check_inventory_appearance":
                    return "Check Inventory And Appearance";
                case "check_player_appearance_status":
                    return "Check Player Appearance And Status";
                case "nearby_settlements":
                    return "Nearby Settlements";
                case "nearby_bandit_parties":
                    return "Nearby Bandit Parties";
                case "nearby_lord_parties":
                    return "Nearby Lord Parties";
                case "current_settlement_facts":
                    return "Current Settlement Facts";
                case "kingdom_diplomacy_status":
                    return "Kingdom Diplomacy Status";
                case "clan_wealth_and_influence":
                    return "Clan Wealth And Influence";
                case "appraise_trade_offer":
                    return "Appraise Trade Offer";
                case "verify_world_history":
                    return "Verify World History";
                case "relevant_memory":
                    return "Relevant Memory";
                case "relationship_history":
                    return "Relationship History";
                default:
                    return id ?? string.Empty;
            }
        }

        private static string FormatTimingSummary(JObject timing)
        {
            if (timing == null)
            {
                return string.Empty;
            }

            List<string> parts = new List<string>();
            foreach (JProperty property in timing.Properties())
            {
                parts.Add(property.Name + "=" + property.Value);
            }

            return string.Join(" ", parts);
        }

        private static string BuildUrl(string route)
        {
            return ReignServerEndpoint.BuildUrl(route);
        }

        private static JObject BuildSocialEventPayload(ReignSocialEventSession session, Hero speaker, string playerText)
        {
            SocialEventRecord record = session.Record;
            JArray attendees = new JArray();
            foreach (Hero hero in record.GetAttendees())
            {
                attendees.Add(BuildHeroProfile(hero));
            }

            return new JObject
            {
                ["campaignId"] = GetCampaignId(),
                ["eventId"] = record.EventId ?? string.Empty,
                ["timelineId"] = ReignWorldHistoryCampaignBehavior.Instance?.TimelineId ?? "main",
                ["templateId"] = record.TemplateId ?? string.Empty,
                ["displayName"] = record.DisplayName ?? string.Empty,
                ["settlementId"] = record.SettlementStringId ?? string.Empty,
                ["hostHeroStringId"] = record.HostHeroStringId ?? string.Empty,
                ["speakerHeroStringId"] = speaker?.StringId ?? string.Empty,
                ["speaker"] = speaker == null ? null : BuildHeroProfile(speaker),
                ["mainHeroStringId"] = Hero.MainHero?.StringId ?? string.Empty,
                ["playerHeroStringId"] = Hero.MainHero?.StringId ?? string.Empty,
                ["playerIdentity"] = BuildPlayerIdentityContext(),
                ["playerName"] = Hero.MainHero?.Name?.ToString() ?? "Player",
                ["playerText"] = playerText ?? string.Empty,
                ["worldDay"] = TaleWorlds.CampaignSystem.Campaign.Current == null ? 0d : CampaignTime.Now.ToDays,
                ["phaseId"] = session.CurrentPhase.PhaseId ?? string.Empty,
                ["phaseTitle"] = session.CurrentPhase.Title ?? string.Empty,
                ["phaseDescription"] = session.CurrentPhase.SettingSummary ?? string.Empty,
                ["sceneContext"] = session.BuildSceneContext(),
                ["nativePoliticalContext"] = BuildNativePoliticalContext(speaker),
                ["temporaryPartyGuest"] = ReignBeta.PartyAgency.ReignTemporaryPartyGuestCampaignBehavior.Instance
                    ?.BuildConversationContext(speaker) ?? new JObject(),
                ["clanAccords"] = ReignBeta.ClanAccords.ReignClanAccordsCampaignBehavior.Instance?.ConversationContext(speaker) ?? new JObject(),
                ["sceneTurnId"] = record.EventId + "_" + session.CurrentPhaseIndex + "_" + session.PhaseExchangeCount,
                ["sceneParticipants"] = BuildSceneParticipants(record.GetAttendees()),
                ["isDancePhase"] = session.Template.TemplateId.StartsWith("dance_", StringComparison.OrdinalIgnoreCase)
                    && (session.CurrentPhase.PhaseId == "first_dance" || session.CurrentPhase.PhaseId == "refreshments_and_second_dance"),
                ["playerIsFemale"] = Hero.MainHero?.IsFemale ?? false,
                ["activeHeroIds"] = new JArray(session.ActiveHeroStringIds),
                ["attendees"] = attendees,
                ["transcript"] = new JArray(session.RecentTranscriptLines ?? new List<string>()),
                ["actionResolutionIndex"] = BuildActionResolutionIndex(
                    speaker,
                    record.GetAttendees(),
                    RequiresFullActionResolutionIndex(playerText))
            };
        }

        private static JObject BuildPartyChatPayload(ReignPartyChatSession session, Hero speaker, List<Hero> activeHeroes, string playerText)
        {
            JArray attendees = new JArray();
            foreach (Hero hero in activeHeroes.Where(x => x != null).Distinct())
            {
                attendees.Add(BuildHeroProfile(hero));
            }

            JArray structuredTranscript = new JArray();
            foreach (ReignPartyChatTranscriptLine line in session.RecentTranscriptEntries ?? new List<ReignPartyChatTranscriptLine>())
            {
                structuredTranscript.Add(new JObject
                {
                    ["sequence"] = line.Sequence,
                    ["sessionId"] = line.SessionId ?? string.Empty,
                    ["exchangeId"] = line.ExchangeId ?? string.Empty,
                    ["speakerHeroStringId"] = line.SpeakerHeroStringId ?? string.Empty,
                    ["speaker"] = line.Speaker ?? string.Empty,
                    ["role"] = line.Role ?? string.Empty,
                    ["text"] = line.Text ?? string.Empty
                });
            }
            int speakerIndex = activeHeroes.FindIndex(hero => hero?.StringId == speaker?.StringId);

            return new JObject
            {
                ["campaignId"] = GetCampaignId(),
                ["eventId"] = "party_chat",
                ["timelineId"] = ReignWorldHistoryCampaignBehavior.Instance?.TimelineId ?? "main",
                ["conversationSessionId"] = session.SessionId,
                ["templateId"] = "party_chat",
                ["displayName"] = "Party Chat",
                ["speakerHeroStringId"] = speaker?.StringId ?? string.Empty,
                ["speaker"] = speaker == null ? null : BuildHeroProfile(speaker),
                ["mainHeroStringId"] = Hero.MainHero?.StringId ?? string.Empty,
                ["playerHeroStringId"] = Hero.MainHero?.StringId ?? string.Empty,
                ["playerIdentity"] = BuildPlayerIdentityContext(),
                ["playerName"] = Hero.MainHero?.Name?.ToString() ?? "Player",
                ["playerText"] = playerText ?? string.Empty,
                ["worldDay"] = TaleWorlds.CampaignSystem.Campaign.Current == null ? 0d : CampaignTime.Now.ToDays,
                ["channel"] = "party_chat",
                ["locationId"] = ResolveDialogueLocationId(speaker),
                ["phaseId"] = "party_chat",
                ["phaseTitle"] = "Party Chat",
                ["phaseDescription"] = "A group conversation with selected party or local characters.",
                ["sceneContext"] = session.BuildSceneContext(activeHeroes),
                ["nativePoliticalContext"] = BuildNativePoliticalContext(speaker),
                ["clanAccords"] = ReignBeta.ClanAccords.ReignClanAccordsCampaignBehavior.Instance?.ConversationContext(speaker) ?? new JObject(),
                ["sceneTurnId"] = string.IsNullOrWhiteSpace(session.CurrentSceneTurnId)
                    ? session.SessionId + "_turn_" + session.PlayerInputCount
                    : session.CurrentSceneTurnId,
                ["turnId"] = string.IsNullOrWhiteSpace(session.CurrentSceneTurnId)
                    ? session.SessionId + "_turn_" + session.PlayerInputCount
                    : session.CurrentSceneTurnId,
                ["sceneParticipants"] = BuildSceneParticipants(activeHeroes),
                ["activeHeroIds"] = new JArray(activeHeroes.Where(x => x != null).Select(x => x.StringId)),
                ["participants"] = new JArray(activeHeroes.Where(x => x != null)
                    .Select(x => x.StringId)
                    .Concat(new[] { Hero.MainHero?.StringId ?? string.Empty })
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Distinct(StringComparer.OrdinalIgnoreCase)),
                ["attendees"] = attendees,
                ["transcript"] = new JArray(session.RecentTranscriptLines ?? new List<string>()),
                ["groupTranscript"] = structuredTranscript,
                ["partySpeakerIndex"] = speakerIndex,
                ["partySpeakerCount"] = activeHeroes.Count,
                ["actionResolutionIndex"] = BuildActionResolutionIndex(
                    speaker,
                    activeHeroes,
                    RequiresFullActionResolutionIndex(playerText))
            };
        }

        private static JArray BuildSceneParticipants(IEnumerable<Hero> heroes)
        {
            List<Hero> roster = (heroes ?? Enumerable.Empty<Hero>())
                .Where(x => x != null && !string.IsNullOrWhiteSpace(x.StringId))
                .Concat(Hero.MainHero == null ? Enumerable.Empty<Hero>() : new[] { Hero.MainHero })
                .Distinct()
                .ToList();
            JArray result = new JArray();
            foreach (Hero hero in roster)
            {
                Hero sovereign = hero.Clan?.Kingdom?.Leader;
                Settlement settlement = hero.CurrentSettlement ?? hero.PartyBelongedTo?.CurrentSettlement;
                if (hero == Hero.MainHero)
                    settlement = Settlement.CurrentSettlement ?? MobileParty.MainParty?.CurrentSettlement ?? settlement;
                string locationClass = settlement != null && (settlement.IsTown || settlement.IsCastle) ? "formal" : "travel";
                string location = settlement == null
                    ? (hero.PartyBelongedTo?.Name?.ToString() == null ? "on the campaign map" : "traveling with " + hero.PartyBelongedTo.Name)
                    : (settlement.IsCastle ? "at the castle of " : settlement.IsVillage ? "at the village of " : "in the town of ") + settlement.Name;
                try
                {
                    string interior = settlement?.LocationComplex?.GetLocationOfCharacter(hero)?.Name?.ToString();
                    if (!string.IsNullOrWhiteSpace(interior)) location += ", in " + interior;
                    if (hero == Hero.MainHero && CampaignMission.Current?.Location != null)
                        location = location + ", in " + CampaignMission.Current.Location.Name;
                }
                catch
                {
                    // LocationComplex is not populated for remote settlements.
                }
                result.Add(new JObject
                {
                    ["heroStringId"] = hero.StringId,
                    ["name"] = hero.Name?.ToString() ?? hero.StringId,
                    ["role"] = hero == Hero.MainHero ? "player" : "npc",
                    ["isFemale"] = hero.IsFemale,
                    ["isRuler"] = sovereign == hero,
                    ["clanId"] = hero.Clan?.StringId ?? string.Empty,
                    ["clanName"] = hero.Clan?.Name?.ToString() ?? string.Empty,
                    ["kingdomId"] = hero.Clan?.Kingdom?.StringId ?? hero.MapFaction?.StringId ?? string.Empty,
                    ["kingdomName"] = hero.Clan?.Kingdom?.InformalName?.ToString() ?? hero.MapFaction?.Name?.ToString() ?? string.Empty,
                    ["spouseId"] = hero.Spouse?.StringId ?? string.Empty,
                    ["spouseName"] = hero.Spouse?.Name?.ToString() ?? string.Empty,
                    ["fatherId"] = hero.Father?.StringId ?? string.Empty,
                    ["fatherName"] = hero.Father?.Name?.ToString() ?? string.Empty,
                    ["motherId"] = hero.Mother?.StringId ?? string.Empty,
                    ["motherName"] = hero.Mother?.Name?.ToString() ?? string.Empty,
                    ["childrenIds"] = new JArray((hero.Children ?? new List<Hero>()).Where(x => x != null).Select(x => x.StringId)),
                    ["childrenNames"] = new JArray((hero.Children ?? new List<Hero>()).Where(x => x != null).Select(x => x.Name?.ToString() ?? x.StringId)),
                    ["sovereignHeroStringId"] = sovereign?.StringId ?? string.Empty,
                    ["sovereignName"] = sovereign?.Name?.ToString() ?? string.Empty,
                    ["nativeLocationDescription"] = location,
                    ["nativeLocationClass"] = locationClass,
                    ["travelClothingDescription"] = BuildCivilianTravelClothingDescription(hero),
                    ["civilianEquipmentFingerprint"] = BuildCivilianEquipmentFingerprint(hero),
                    ["appearance"] = BuildSceneAppearanceProfile(hero)
                });
            }
            return result;
        }

        private static JObject BuildSceneAppearanceProfile(Hero hero)
        {
            JObject full = BuildAppearanceProfile(hero);
            JObject compact = new JObject();
            foreach (string key in new[]
            {
                "source", "trueStatusScore", "trueStatusLabel", "visibleStatusScore", "visibleStatusLabel",
                "statusMismatch", "presentationEffect", "firstView", "civilianEquipmentValue", "battleEquipmentValue"
            })
            {
                JToken value = full[key];
                if (value != null)
                {
                    compact[key] = value.DeepClone();
                }
            }
            return compact;
        }

        private static string BuildCivilianTravelClothingDescription(Hero hero)
        {
            Equipment equipment = hero?.CivilianEquipment ?? hero?.CharacterObject?.FirstCivilianEquipment ?? hero?.CharacterObject?.Equipment;
            JArray items = BuildEquipmentArray(equipment, "civilian");
            List<string> garments = items.OfType<JObject>()
                .Where(x => x.Value<bool?>("isArmorOrClothing") == true)
                .Select(x =>
                {
                    string modifier = x.Value<string>("modifier") ?? string.Empty;
                    string name = x.Value<string>("name") ?? string.Empty;
                    return string.IsNullOrWhiteSpace(modifier) ? name : modifier + " " + name;
                })
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            return garments.Count == 0
                ? "ordinary civilian traveling clothes"
                : string.Join(", ", garments);
        }

        private static string BuildCivilianEquipmentFingerprint(Hero hero)
        {
            string text = BuildCivilianTravelClothingDescription(hero);
            unchecked
            {
                int hash = 17;
                foreach (char c in text) hash = hash * 31 + c;
                return hash.ToString("X8");
            }
        }

        public static async Task TickConversationSceneStateAsync()
        {
            try
            {
                await PostJsonAsync("/conversation-scene/hourly-tick", new JObject
                {
                    ["campaignId"] = GetCampaignId(),
                    ["worldDay"] = TaleWorlds.CampaignSystem.Campaign.Current == null ? 0d : CampaignTime.Now.ToDays
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                ReignLog.Warn("Conversation scene-state hourly tick failed: " + ex.Message);
            }
        }

        private static string ExtractCharacterObjectId(string cacheKey)
        {
            if (string.IsNullOrWhiteSpace(cacheKey))
            {
                return string.Empty;
            }

            int open = cacheKey.LastIndexOf('(');
            int close = cacheKey.LastIndexOf(')');
            if (open < 0 || close <= open)
            {
                return string.Empty;
            }

            return cacheKey.Substring(open + 1, close - open - 1).Trim();
        }

        private static string ShortLog(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            return text.Length <= 32 ? text : text.Substring(0, 32) + "...";
        }

        private static JObject BuildActionResolutionIndex(
            Hero speaker,
            IEnumerable<Hero> activeHeroes,
            bool includeWorldCatalog = true)
        {
            List<Hero> activeHeroList = (activeHeroes ?? Enumerable.Empty<Hero>()).Where(x => x != null).ToList();
            bool hasOrigin = TryGetReferencePosition(speaker, out Vec2 origin);
            Settlement currentSettlement =
                Settlement.CurrentSettlement
                ?? MobileParty.MainParty?.CurrentSettlement
                ?? speaker?.CurrentSettlement;
            HashSet<string> relevantSettlementIds = new HashSet<string>(
                new[]
                {
                    currentSettlement?.StringId,
                    Hero.MainHero?.HomeSettlement?.StringId,
                    speaker?.HomeSettlement?.StringId,
                    Hero.MainHero?.GovernorOf?.Settlement?.StringId,
                    speaker?.GovernorOf?.Settlement?.StringId
                }.Where(x => !string.IsNullOrWhiteSpace(x)),
                StringComparer.OrdinalIgnoreCase);
            JArray settlements = new JArray();
            foreach (Settlement settlement in Settlement.All.Where(x => x != null
                && (includeWorldCatalog || relevantSettlementIds.Contains(x.StringId))))
            {
                JObject entry = new JObject
                {
                    ["settlementId"] = settlement.StringId ?? string.Empty,
                    ["name"] = settlement.Name?.ToString() ?? string.Empty,
                    ["type"] = SettlementType(settlement),
                    ["cultureId"] = settlement.Culture?.StringId ?? string.Empty,
                    ["ownerClanId"] = settlement.OwnerClan?.StringId ?? string.Empty,
                    ["ownerClanName"] = settlement.OwnerClan?.Name?.ToString() ?? string.Empty,
                    ["factionId"] = settlement.MapFaction?.StringId ?? string.Empty,
                    ["factionName"] = settlement.MapFaction?.Name?.ToString() ?? string.Empty,
                    ["kingdomId"] = settlement.MapFaction?.StringId ?? settlement.OwnerClan?.Kingdom?.StringId ?? string.Empty,
                    ["kingdomName"] = settlement.MapFaction?.Name?.ToString() ?? settlement.OwnerClan?.Kingdom?.InformalName?.ToString() ?? string.Empty,
                    ["isTown"] = settlement.IsTown,
                    ["isCastle"] = settlement.IsCastle,
                    ["isVillage"] = settlement.IsVillage,
                    ["isHideout"] = settlement.IsHideout,
                    ["isFortification"] = settlement.IsFortification,
                    ["isUnderSiege"] = settlement.IsUnderSiege,
                    ["isUnderRaid"] = settlement.IsUnderRaid,
                    ["militia"] = settlement.Militia,
                    ["distance"] = hasOrigin ? Math.Sqrt(origin.DistanceSquared(settlement.GetPosition2D)) : 0d
                };

                Town town = settlement.Town;
                if (town != null)
                {
                    entry["prosperity"] = town.Prosperity;
                    entry["loyalty"] = town.Loyalty;
                    entry["security"] = town.Security;
                    entry["garrison"] = town.GarrisonParty?.MemberRoster?.TotalManCount ?? 0;
                    entry["boundVillages"] = settlement.BoundVillages?.Count ?? 0;
                    entry["governorHeroId"] = town.Governor?.StringId ?? string.Empty;
                    entry["governorName"] = town.Governor?.Name?.ToString() ?? string.Empty;
                }

                Village village = settlement.Village;
                if (village != null)
                {
                    entry["boundSettlementId"] = village.Bound?.StringId ?? string.Empty;
                    entry["boundSettlementName"] = village.Bound?.Name?.ToString() ?? string.Empty;
                    entry["villageState"] = village.VillageState.ToString();
                    entry["isDeserted"] = village.IsDeserted;
                }

                settlements.Add(entry);
            }

            HashSet<string> relevantKingdomIds = new HashSet<string>(
                new[]
                {
                    Clan.PlayerClan?.Kingdom?.StringId,
                    speaker?.Clan?.Kingdom?.StringId,
                    currentSettlement?.OwnerClan?.Kingdom?.StringId
                }.Where(x => !string.IsNullOrWhiteSpace(x)),
                StringComparer.OrdinalIgnoreCase);
            JArray kingdoms = new JArray();
            foreach (Kingdom kingdom in Kingdom.All.Where(x => x != null
                && (includeWorldCatalog || relevantKingdomIds.Contains(x.StringId))))
            {
                kingdoms.Add(new JObject
                {
                    ["kingdomId"] = kingdom.StringId ?? string.Empty,
                    ["name"] = kingdom.InformalName?.ToString() ?? kingdom.Name?.ToString() ?? string.Empty,
                    ["formalName"] = kingdom.Name?.ToString() ?? string.Empty,
                    ["leaderHeroId"] = kingdom.Leader?.StringId ?? string.Empty,
                    ["leaderName"] = kingdom.Leader?.Name?.ToString() ?? string.Empty,
                    ["rulingClanId"] = kingdom.RulingClan?.StringId ?? string.Empty,
                    ["rulingClanName"] = kingdom.RulingClan?.Name?.ToString() ?? string.Empty,
                    ["capitalSettlementId"] = KingdomCapital(kingdom)?.StringId ?? string.Empty,
                    ["capitalName"] = KingdomCapital(kingdom)?.Name?.ToString() ?? string.Empty,
                    ["clanCount"] = kingdom.Clans?.Count ?? 0,
                    ["townCount"] = kingdom.Towns?.Count ?? 0,
                    ["armyCount"] = kingdom.Armies?.Count ?? 0,
                    ["strength"] = kingdom.CurrentTotalStrength,
                    ["isAtWarWithPlayerKingdom"] = Clan.PlayerClan?.Kingdom != null && kingdom.IsAtWarWith(Clan.PlayerClan.Kingdom)
                });
            }

            HashSet<string> relevantClanIds = new HashSet<string>(
                activeHeroList
                    .Concat(new[] { Hero.MainHero, speaker })
                    .Where(x => x?.Clan != null && !string.IsNullOrWhiteSpace(x.Clan.StringId))
                    .Select(x => x.Clan.StringId),
                StringComparer.OrdinalIgnoreCase);
            if (currentSettlement?.OwnerClan != null)
            {
                relevantClanIds.Add(currentSettlement.OwnerClan.StringId);
            }
            JArray clans = new JArray();
            foreach (Clan clan in Clan.All.Where(x => x != null
                && (includeWorldCatalog || relevantClanIds.Contains(x.StringId))))
            {
                clans.Add(new JObject
                {
                    ["clanId"] = clan.StringId ?? string.Empty,
                    ["name"] = clan.Name?.ToString() ?? string.Empty,
                    ["leaderHeroId"] = clan.Leader?.StringId ?? string.Empty,
                    ["leaderName"] = clan.Leader?.Name?.ToString() ?? string.Empty,
                    ["kingdomId"] = clan.Kingdom?.StringId ?? string.Empty,
                    ["kingdomName"] = clan.Kingdom?.InformalName?.ToString() ?? string.Empty,
                    ["tier"] = clan.Tier,
                    ["gold"] = clan.Gold,
                    ["renown"] = clan.Renown,
                    ["influence"] = clan.Influence,
                    ["fiefCount"] = clan.Fiefs?.Count ?? 0,
                    ["initialHomeSettlementId"] = clan.InitialHomeSettlement?.StringId ?? string.Empty,
                    ["initialHomeSettlementName"] = clan.InitialHomeSettlement?.Name?.ToString() ?? string.Empty
                });
            }

            List<Hero> heroes = new List<Hero>();
            AddHeroIfMissing(heroes, Hero.MainHero);
            AddHeroIfMissing(heroes, speaker);
            AddHeroRelations(heroes, Hero.MainHero);
            AddHeroRelations(heroes, speaker);
            // Known local residents are valid named action targets (for example
            // asking their commander to release them), even without a clan.
            // Resolver references do not add them to the conversation hearing set.
            foreach (Hero resident in ReignEncounteredResidentsCampaignBehavior.Instance?.ResidentsAt(currentSettlement)
                ?? new List<Hero>()) AddHeroIfMissing(heroes, resident);
            foreach (Hero hero in activeHeroList)
            {
                AddHeroIfMissing(heroes, hero);
                AddHeroRelations(heroes, hero);
            }

            if (includeWorldCatalog)
            {
                foreach (Kingdom kingdom in Kingdom.All.Where(x => x != null))
                {
                    AddHeroIfMissing(heroes, kingdom.Leader);
                }

                foreach (Clan clan in Clan.All.Where(x => x != null))
                {
                    AddHeroIfMissing(heroes, clan.Leader);
                }

                foreach (Town town in Town.AllTowns.Where(x => x != null))
                {
                    AddHeroIfMissing(heroes, town.Governor);
                }

                foreach (Hero hero in Hero.AllAliveHeroes.Where(x => x != null && (x.IsLord || x.IsNotable || x.IsWanderer || x.IsPrisoner)))
                {
                    AddHeroIfMissing(heroes, hero);
                }
            }

            JArray heroRefs = new JArray();
            foreach (Hero hero in heroes)
            {
                heroRefs.Add(HeroReference(hero));
            }

            JArray aliases = new JArray
            {
                new JObject { ["alias"] = "player", ["entityType"] = "hero", ["id"] = Hero.MainHero?.StringId ?? string.Empty },
                new JObject { ["alias"] = "main_hero", ["entityType"] = "hero", ["id"] = Hero.MainHero?.StringId ?? string.Empty },
                new JObject { ["alias"] = "speaker", ["entityType"] = "hero", ["id"] = speaker?.StringId ?? string.Empty },
                new JObject { ["alias"] = "current_npc", ["entityType"] = "hero", ["id"] = speaker?.StringId ?? string.Empty },
                new JObject { ["alias"] = "that lord", ["entityType"] = "hero", ["id"] = speaker?.StringId ?? string.Empty },
                new JObject { ["alias"] = "that lady", ["entityType"] = "hero", ["id"] = speaker?.StringId ?? string.Empty },
                new JObject { ["alias"] = "the lord", ["entityType"] = "hero", ["id"] = speaker?.StringId ?? string.Empty },
                new JObject { ["alias"] = "the lady", ["entityType"] = "hero", ["id"] = speaker?.StringId ?? string.Empty },
                new JObject { ["alias"] = "me", ["entityType"] = "hero", ["id"] = speaker?.StringId ?? string.Empty },
                new JObject { ["alias"] = "myself", ["entityType"] = "hero", ["id"] = speaker?.StringId ?? string.Empty },
                new JObject { ["alias"] = "you", ["entityType"] = "hero", ["id"] = Hero.MainHero?.StringId ?? string.Empty },
                new JObject { ["alias"] = "player_spouse", ["entityType"] = "hero", ["id"] = Hero.MainHero?.Spouse?.StringId ?? string.Empty },
                new JObject { ["alias"] = "speaker_spouse", ["entityType"] = "hero", ["id"] = speaker?.Spouse?.StringId ?? string.Empty },
                new JObject { ["alias"] = "my wife", ["entityType"] = "hero", ["id"] = speaker?.Spouse?.StringId ?? string.Empty },
                new JObject { ["alias"] = "my husband", ["entityType"] = "hero", ["id"] = speaker?.Spouse?.StringId ?? string.Empty },
                new JObject { ["alias"] = "your wife", ["entityType"] = "hero", ["id"] = Hero.MainHero?.Spouse?.StringId ?? string.Empty },
                new JObject { ["alias"] = "your husband", ["entityType"] = "hero", ["id"] = Hero.MainHero?.Spouse?.StringId ?? string.Empty },
                new JObject { ["alias"] = "player_clan", ["entityType"] = "clan", ["id"] = Clan.PlayerClan?.StringId ?? string.Empty },
                new JObject { ["alias"] = "player_kingdom", ["entityType"] = "kingdom", ["id"] = Clan.PlayerClan?.Kingdom?.StringId ?? string.Empty },
                new JObject { ["alias"] = "speaker_clan", ["entityType"] = "clan", ["id"] = speaker?.Clan?.StringId ?? string.Empty },
                new JObject { ["alias"] = "speaker_kingdom", ["entityType"] = "kingdom", ["id"] = speaker?.Clan?.Kingdom?.StringId ?? speaker?.MapFaction?.StringId ?? string.Empty },
                new JObject { ["alias"] = "my king", ["entityType"] = "hero", ["id"] = speaker?.Clan?.Kingdom?.Leader?.StringId ?? string.Empty },
                new JObject { ["alias"] = "my queen", ["entityType"] = "hero", ["id"] = speaker?.Clan?.Kingdom?.Leader?.StringId ?? string.Empty },
                new JObject { ["alias"] = "my ruler", ["entityType"] = "hero", ["id"] = speaker?.Clan?.Kingdom?.Leader?.StringId ?? string.Empty },
                new JObject { ["alias"] = "your king", ["entityType"] = "hero", ["id"] = Clan.PlayerClan?.Kingdom?.Leader?.StringId ?? string.Empty },
                new JObject { ["alias"] = "your queen", ["entityType"] = "hero", ["id"] = Clan.PlayerClan?.Kingdom?.Leader?.StringId ?? string.Empty },
                new JObject { ["alias"] = "your ruler", ["entityType"] = "hero", ["id"] = Clan.PlayerClan?.Kingdom?.Leader?.StringId ?? string.Empty },
                new JObject { ["alias"] = "current_settlement", ["entityType"] = "settlement", ["id"] = Settlement.CurrentSettlement?.StringId ?? MobileParty.MainParty?.CurrentSettlement?.StringId ?? speaker?.CurrentSettlement?.StringId ?? string.Empty },
                new JObject { ["alias"] = "this city", ["entityType"] = "settlement", ["id"] = Settlement.CurrentSettlement?.StringId ?? MobileParty.MainParty?.CurrentSettlement?.StringId ?? speaker?.CurrentSettlement?.StringId ?? string.Empty },
                new JObject { ["alias"] = "this town", ["entityType"] = "settlement", ["id"] = Settlement.CurrentSettlement?.StringId ?? MobileParty.MainParty?.CurrentSettlement?.StringId ?? speaker?.CurrentSettlement?.StringId ?? string.Empty },
                new JObject { ["alias"] = "that city", ["entityType"] = "settlement", ["id"] = Settlement.CurrentSettlement?.StringId ?? MobileParty.MainParty?.CurrentSettlement?.StringId ?? speaker?.CurrentSettlement?.StringId ?? string.Empty },
                new JObject { ["alias"] = "that town", ["entityType"] = "settlement", ["id"] = Settlement.CurrentSettlement?.StringId ?? MobileParty.MainParty?.CurrentSettlement?.StringId ?? speaker?.CurrentSettlement?.StringId ?? string.Empty },
                new JObject { ["alias"] = "capital", ["entityType"] = "settlement", ["id"] = KingdomCapital(Clan.PlayerClan?.Kingdom)?.StringId ?? string.Empty },
                new JObject { ["alias"] = "our capital", ["entityType"] = "settlement", ["id"] = KingdomCapital(Clan.PlayerClan?.Kingdom)?.StringId ?? string.Empty },
                new JObject { ["alias"] = "their capital", ["entityType"] = "settlement", ["id"] = KingdomCapital(speaker?.Clan?.Kingdom)?.StringId ?? string.Empty },
                new JObject { ["alias"] = "player_party", ["entityType"] = "party", ["id"] = MobileParty.MainParty?.StringId ?? string.Empty },
                new JObject { ["alias"] = "speaker_party", ["entityType"] = "party", ["id"] = speaker?.PartyBelongedTo?.StringId ?? string.Empty },
                new JObject { ["alias"] = "my party", ["entityType"] = "party", ["id"] = speaker?.PartyBelongedTo?.StringId ?? string.Empty },
                new JObject { ["alias"] = "your party", ["entityType"] = "party", ["id"] = MobileParty.MainParty?.StringId ?? string.Empty }
            };

            return new JObject
            {
                ["version"] = "2",
                ["settlements"] = settlements,
                ["kingdoms"] = kingdoms,
                ["clans"] = clans,
                ["heroes"] = heroRefs,
                ["parties"] = BuildResolverParties(speaker, origin, includeWorldCatalog ? 120 : 0),
                ["armies"] = includeWorldCatalog ? BuildResolverArmies(origin) : new JArray(),
                ["workshops"] = BuildResolverWorkshops(includeWorldCatalog ? null : currentSettlement),
                ["assets"] = BuildResolverAssets(speaker),
                ["concepts"] = BuildResolverConcepts(),
                ["recentContext"] = BuildResolverRecentContext(speaker, activeHeroList),
                ["aliases"] = aliases
            };
        }

        private static bool RequiresFullActionResolutionIndex(string playerText)
        {
            string text = (playerText ?? string.Empty).Trim();
            if (text.Length == 0)
            {
                return false;
            }

            // The compact index always contains the player, speaker, active
            // participants, their families, the current settlement, relevant
            // clans/kingdoms, aliases, and participant assets. Expand to the
            // expensive all-world catalog only when the player's line could
            // reasonably request a native action against a remote entity.
            bool strongAction = Regex.IsMatch(
                text,
                @"\b(attack|assault|raid|besiege|capture|conquer|kill|execute|duel|fight|escort|patrol|recruit|hire|dismiss|transfer|give|gift|pay|buy|sell|trade|loan|subsid|tribute|reparation|marry|wed|divorce|declare|peace|war|ally|alliance|vassal|mercenar|defect|rebel|exile|restore|surrender|release|imprison|take prisoner|hand over|deliver)\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (strongAction)
            {
                return true;
            }

            return Regex.IsMatch(
                text,
                @"\b(follow|join|challenge)\s+(me|us|my|our|you|him|her|them)\b|\b(go|travel|move)\s+(to|toward|into|near)\b|\b(wait|stay)\s+(here|there|at|near|outside|inside)\b|\b(form|create)\s+(an?\s+)?(army|party|alliance|kingdom|clan)\b|\bleave\s+(my|our|the|this|your)\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        private static void AddHeroRelations(List<Hero> heroes, Hero hero)
        {
            if (hero == null)
            {
                return;
            }

            AddHeroIfMissing(heroes, hero.Spouse);
            AddHeroIfMissing(heroes, hero.Father);
            AddHeroIfMissing(heroes, hero.Mother);
            if (hero.Children != null)
            {
                foreach (Hero child in hero.Children)
                {
                    AddHeroIfMissing(heroes, child);
                }
            }

            if (hero.Siblings != null)
            {
                foreach (Hero sibling in hero.Siblings)
                {
                    AddHeroIfMissing(heroes, sibling);
                }
            }
        }

        private static void AddHeroIfMissing(List<Hero> heroes, Hero hero)
        {
            if (hero == null || string.IsNullOrWhiteSpace(hero.StringId) || heroes.Any(x => x.StringId == hero.StringId))
            {
                return;
            }

            heroes.Add(hero);
        }

        private static Settlement KingdomCapital(Kingdom kingdom)
        {
            if (kingdom == null)
            {
                return null;
            }

            return kingdom.InitialHomeSettlement
                ?? kingdom.Towns?.FirstOrDefault()?.Settlement
                ?? kingdom.Settlements?.FirstOrDefault(x => x != null && x.IsFortification)
                ?? kingdom.Settlements?.FirstOrDefault();
        }

        private static JArray BuildResolverParties(Hero speaker, Vec2 origin, int additionalPartyLimit = 120)
        {
            JArray parties = new JArray();
            HashSet<string> seen = new HashSet<string>();
            AddPartyIfMissing(parties, seen, MobileParty.MainParty, origin);
            AddPartyIfMissing(parties, seen, speaker?.PartyBelongedTo, origin);

            foreach (MobileParty party in MobileParty.All
                .Where(x => x != null && x.IsActive)
                .OrderBy(x => SafeDistanceSquared(x, origin))
                .Take(Math.Max(0, additionalPartyLimit)))
            {
                AddPartyIfMissing(parties, seen, party, origin);
            }

            return parties;
        }

        private static void AddPartyIfMissing(JArray parties, HashSet<string> seen, MobileParty party, Vec2 origin)
        {
            if (party == null || string.IsNullOrWhiteSpace(party.StringId) || seen.Contains(party.StringId))
            {
                return;
            }

            seen.Add(party.StringId);
            JObject entry = PartySummary(party, origin);
            entry["type"] = PartyType(party);
            entry["isMainParty"] = party.IsMainParty;
            entry["isCaravan"] = party.IsCaravan;
            entry["isVillager"] = party.IsVillager;
            entry["isMilitia"] = party.IsMilitia;
            entry["homeSettlementId"] = party.HomeSettlement?.StringId ?? string.Empty;
            entry["homeSettlementName"] = party.HomeSettlement?.Name?.ToString() ?? string.Empty;
            entry["currentSettlementId"] = party.CurrentSettlement?.StringId ?? string.Empty;
            entry["currentSettlementName"] = party.CurrentSettlement?.Name?.ToString() ?? string.Empty;
            entry["targetSettlementId"] = party.TargetSettlement?.StringId ?? string.Empty;
            entry["targetSettlementName"] = party.TargetSettlement?.Name?.ToString() ?? string.Empty;
            entry["attachedToPartyId"] = party.AttachedTo?.StringId ?? string.Empty;
            entry["armyLeaderPartyId"] = party.Army?.LeaderParty?.StringId ?? string.Empty;
            entry["isArmyLeader"] = party.Army?.LeaderParty == party;
            entry["defaultBehavior"] = party.DefaultBehavior.ToString();
            entry["shortTermBehavior"] = party.ShortTermBehavior.ToString();
            entry["prisonerCount"] = party.PrisonRoster?.TotalManCount ?? 0;
            parties.Add(entry);
        }

        private static string PartyType(MobileParty party)
        {
            if (party == null) return "unknown";
            if (party.IsMainParty) return "player_party";
            if (party.IsLordParty) return "lord_party";
            if (party.IsCaravan) return "caravan";
            if (party.IsVillager) return "villager";
            if (party.IsMilitia) return "militia";
            if (party.IsBandit) return "bandit";
            return "mobile_party";
        }

        private static float SafeDistanceSquared(MobileParty party, Vec2 origin)
        {
            try
            {
                return party == null ? float.MaxValue : origin.DistanceSquared(party.GetPosition2D);
            }
            catch
            {
                return float.MaxValue;
            }
        }

        private static JArray BuildResolverArmies(Vec2 origin)
        {
            JArray armies = new JArray();
            foreach (Army army in Kingdom.All.Where(x => x != null && x.Armies != null).SelectMany(x => x.Armies).Where(x => x != null))
            {
                MobileParty leader = army.LeaderParty;
                Settlement target = army.AiBehaviorObject as Settlement ?? leader?.TargetSettlement;
                armies.Add(new JObject
                {
                    ["armyId"] = "army:" + (leader?.StringId ?? army.ArmyOwner?.StringId ?? string.Empty),
                    ["name"] = army.Name?.ToString() ?? string.Empty,
                    ["leaderPartyId"] = leader?.StringId ?? string.Empty,
                    ["leaderHeroId"] = army.ArmyOwner?.StringId ?? leader?.LeaderHero?.StringId ?? string.Empty,
                    ["leaderName"] = army.ArmyOwner?.Name?.ToString() ?? leader?.LeaderHero?.Name?.ToString() ?? string.Empty,
                    ["kingdomId"] = army.Kingdom?.StringId ?? leader?.MapFaction?.StringId ?? string.Empty,
                    ["kingdomName"] = army.Kingdom?.InformalName?.ToString() ?? leader?.MapFaction?.Name?.ToString() ?? string.Empty,
                    ["type"] = army.ArmyType.ToString(),
                    ["cohesion"] = army.Cohesion,
                    ["morale"] = army.Morale,
                    ["totalManCount"] = army.TotalManCount,
                    ["estimatedStrength"] = army.EstimatedStrength,
                    ["partyCount"] = army.LeaderPartyAndAttachedPartiesCount,
                    ["targetSettlementId"] = target?.StringId ?? string.Empty,
                    ["targetSettlementName"] = target?.Name?.ToString() ?? string.Empty,
                    ["distance"] = leader == null ? 0d : Math.Sqrt(origin.DistanceSquared(leader.GetPosition2D))
                });
            }

            return armies;
        }

        private static JArray BuildResolverWorkshops(Settlement settlementFilter = null)
        {
            JArray workshops = new JArray();
            foreach (Town town in Town.AllTowns.Where(x => x != null
                && x.Workshops != null
                && (settlementFilter == null || x.Settlement == settlementFilter)))
            {
                for (int i = 0; i < town.Workshops.Length; i++)
                {
                    Workshop workshop = town.Workshops[i];
                    if (workshop == null || workshop.WorkshopType == null)
                    {
                        continue;
                    }

                    string id = ReignObjectResolver.WorkshopId(workshop, i);
                    workshops.Add(new JObject
                    {
                        ["workshopId"] = id,
                        ["name"] = workshop.Name?.ToString() ?? workshop.WorkshopType?.Name?.ToString() ?? id,
                        ["tag"] = workshop.Tag ?? string.Empty,
                        ["typeId"] = workshop.WorkshopType?.StringId ?? string.Empty,
                        ["typeName"] = workshop.WorkshopType?.Name?.ToString() ?? string.Empty,
                        ["settlementId"] = workshop.Settlement?.StringId ?? string.Empty,
                        ["settlementName"] = workshop.Settlement?.Name?.ToString() ?? string.Empty,
                        ["ownerHeroStringId"] = workshop.Owner?.StringId ?? string.Empty,
                        ["ownerName"] = workshop.Owner?.Name?.ToString() ?? string.Empty,
                        ["capital"] = workshop.Capital,
                        ["profitMade"] = workshop.ProfitMade
                    });
                }
            }

            return workshops;
        }

        private static JObject BuildResolverAssets(Hero speaker)
        {
            return new JObject
            {
                ["currency"] = new JArray
                {
                    new JObject { ["alias"] = "denars", ["kind"] = "gold" },
                    new JObject { ["alias"] = "gold", ["kind"] = "gold" },
                    new JObject { ["alias"] = "tribute", ["kind"] = "recurring_gold" },
                    new JObject { ["alias"] = "reparations", ["kind"] = "gold_or_recurring_gold" }
                },
                ["player"] = new JObject
                {
                    ["hero"] = HeroReference(Hero.MainHero),
                    ["wealth"] = BuildWealthProfile(Hero.MainHero),
                    ["inventory"] = BuildInventoryProfile(Hero.MainHero),
                    ["appearance"] = BuildAppearanceProfile(Hero.MainHero),
                    ["prisoners"] = BuildPrisonerArray(MobileParty.MainParty, 24)
                },
                ["speaker"] = new JObject
                {
                    ["hero"] = HeroReference(speaker),
                    ["wealth"] = BuildWealthProfile(speaker),
                    ["inventory"] = BuildInventoryProfile(speaker),
                    ["appearance"] = BuildAppearanceProfile(speaker),
                    ["prisoners"] = BuildPrisonerArray(speaker?.PartyBelongedTo, 24)
                }
            };
        }

        private static JArray BuildPrisonerArray(MobileParty party, int limit)
        {
            JArray prisoners = new JArray();
            if (party?.PrisonRoster == null)
            {
                return prisoners;
            }

            foreach (TroopRosterElement element in party.PrisonRoster.GetTroopRoster().Where(x => x.Character?.HeroObject != null).Take(Math.Max(1, limit)))
            {
                Hero prisoner = element.Character.HeroObject;
                prisoners.Add(new JObject
                {
                    ["heroStringId"] = prisoner.StringId ?? string.Empty,
                    ["name"] = prisoner.Name?.ToString() ?? string.Empty,
                    ["clanId"] = prisoner.Clan?.StringId ?? string.Empty,
                    ["clanName"] = prisoner.Clan?.Name?.ToString() ?? string.Empty,
                    ["kingdomId"] = prisoner.Clan?.Kingdom?.StringId ?? prisoner.MapFaction?.StringId ?? string.Empty,
                    ["kingdomName"] = prisoner.Clan?.Kingdom?.InformalName?.ToString() ?? prisoner.MapFaction?.Name?.ToString() ?? string.Empty,
                    ["count"] = element.Number
                });
            }

            return prisoners;
        }

        private static JArray BuildResolverConcepts()
        {
            return new JArray
            {
                Concept("peace", "diplomacy", "make_peace", "end war; settle hostilities; cease fighting"),
                Concept("tribute", "diplomacy", "offer_tribute_peace", "daily payment; pay for peace; buy time"),
                Concept("reparations", "diplomacy", "demand_reparations_peace", "compensation; indemnity; payment for damage"),
                Concept("settlement surrender", "diplomacy", "demand_settlement_peace", "give town; surrender city; hand over castle; cede fief; grant city; transfer settlement"),
                Concept("full surrender", "diplomacy", "demand_surrender_peace", "capitulation; submit kingdom; give all towns and castles"),
                Concept("naval assets", "regular", "trade_package", "ships; fleet; flagship; vessels; boats; naval transfer; War Sails"),
                Concept("trade agreement", "diplomacy", "sign_trade_agreement", "commerce pact; trade treaty"),
                Concept("non-aggression pact", "diplomacy", "sign_non_aggression_pact", "promise not to attack; pact of restraint"),
                Concept("alliance", "diplomacy", "sign_alliance", "ally; mutual alliance"),
                Concept("defensive pact", "diplomacy", "sign_defensive_pact", "defend each other; protection pact"),
                Concept("ultimatum", "diplomacy", "record_promise", "threat; final warning; obligation"),
                Concept("gift", "social", "record_promise", "favor; present; improve relations"),
                Concept("gold transfer", "regular", "give_gold_to_player", "give gold; pay denars; hand over money; gift coin"),
                Concept("trade package", "regular", "trade_package", "buy; sell; barter; price; denars for item; gold for prisoner; ships for gold; settlement sale"),
                Concept("item transfer", "regular", "transfer_item", "give armor; give weapon; hand over item; transfer equipment"),
                Concept("travel order", "regular", "go_to_settlement", "go to town; ride to city; travel to castle; return to settlement"),
                Concept("duel", "regular", "duel_player", "friendly duel; training duel; lethal duel; duel of honor"),
                Concept("feast", "social", "social_event_feast", "dinner; banquet; gathering"),
                Concept("private audience", "social", "social_event_private_audience", "speak privately; closed meeting"),
                Concept("public insult", "social", "social_action_humiliate", "humiliate; shame; denounce before court")
            };
        }

        private static JObject Concept(string phrase, string family, string action, string aliases)
        {
            return new JObject
            {
                ["phrase"] = phrase,
                ["family"] = family,
                ["action"] = action,
                ["aliases"] = aliases
            };
        }

        private static JArray BuildResolverRecentContext(Hero speaker, List<Hero> activeHeroes)
        {
            JArray recent = new JArray();
            recent.Add(new JObject
            {
                ["label"] = "current_conversation_target",
                ["entityType"] = "hero",
                ["id"] = speaker?.StringId ?? string.Empty,
                ["name"] = speaker?.Name?.ToString() ?? string.Empty
            });

            Settlement settlement = Settlement.CurrentSettlement ?? MobileParty.MainParty?.CurrentSettlement ?? speaker?.CurrentSettlement;
            if (settlement != null)
            {
                recent.Add(new JObject
                {
                    ["label"] = "current_settlement",
                    ["entityType"] = "settlement",
                    ["id"] = settlement.StringId ?? string.Empty,
                    ["name"] = settlement.Name?.ToString() ?? string.Empty
                });
            }

            foreach (Hero hero in (activeHeroes ?? new List<Hero>()).Take(12))
            {
                recent.Add(new JObject
                {
                    ["label"] = "active_conversation_participant",
                    ["entityType"] = "hero",
                    ["id"] = hero.StringId ?? string.Empty,
                    ["name"] = hero.Name?.ToString() ?? string.Empty
                });
            }

            foreach (ReignDiplomaticAgreementRecord agreement in (ReignAICampaignBehavior.Instance?.Agreements ?? new List<ReignDiplomaticAgreementRecord>())
                .Where(x => x != null && x.IsActive)
                .OrderByDescending(x => x.CreatedDay)
                .Take(16))
            {
                recent.Add(new JObject
                {
                    ["label"] = "active_agreement",
                    ["entityType"] = "agreement",
                    ["id"] = agreement.AgreementId ?? string.Empty,
                    ["kind"] = agreement.Kind ?? string.Empty,
                    ["actorKingdomId"] = agreement.ActorKingdomStringId ?? string.Empty,
                    ["targetKingdomId"] = agreement.TargetKingdomStringId ?? string.Empty,
                    ["targetSettlementId"] = agreement.TargetSettlementStringId ?? string.Empty,
                    ["targetHeroId"] = agreement.TargetHeroStringId ?? string.Empty,
                    ["reason"] = agreement.Reason ?? string.Empty
                });
            }

            return recent;
        }

        private static JObject BuildHeroProfile(Hero hero)
        {
            Kingdom heroKingdom = hero?.Clan?.Kingdom;
            Hero sovereign = heroKingdom?.Leader;
            Kingdom playerKingdom = Clan.PlayerClan?.Kingdom;
            Hero playerSovereign = playerKingdom?.Leader;
            JObject profile = new JObject
            {
                ["campaignId"] = GetCampaignId(),
                ["campaignLabel"] = ReignCampaignIdentity.CurrentCampaignLabel(),
                ["heroStringId"] = hero?.StringId ?? string.Empty,
                ["mainHeroStringId"] = Hero.MainHero?.StringId ?? string.Empty,
                ["mainHeroName"] = Hero.MainHero?.Name?.ToString() ?? string.Empty,
                ["playerClanId"] = Clan.PlayerClan?.StringId ?? string.Empty,
                ["playerClanName"] = Clan.PlayerClan?.Name?.ToString() ?? string.Empty,
                ["playerKingdomId"] = Clan.PlayerClan?.Kingdom?.StringId ?? string.Empty,
                ["playerKingdomName"] = Clan.PlayerClan?.Kingdom?.InformalName?.ToString() ?? string.Empty,
                ["characterObjectId"] = hero?.CharacterObject?.StringId ?? string.Empty,
                ["name"] = hero?.Name?.ToString() ?? string.Empty,
                ["cultureId"] = hero?.Culture?.StringId ?? string.Empty,
                ["clanId"] = hero?.Clan?.StringId ?? string.Empty,
                ["clanName"] = hero?.Clan?.Name?.ToString() ?? string.Empty,
                ["clanTier"] = hero?.Clan?.Tier ?? 0,
                ["clanRenown"] = hero?.Clan?.Renown ?? 0f,
                ["clanFiefCount"] = hero?.Clan?.Fiefs?.Count ?? 0,
                ["kingdomId"] = hero?.Clan?.Kingdom?.StringId ?? hero?.MapFaction?.StringId ?? string.Empty,
                ["kingdomName"] = hero?.Clan?.Kingdom?.InformalName?.ToString() ?? hero?.MapFaction?.Name?.ToString() ?? string.Empty,
                ["currentSettlementId"] = hero?.CurrentSettlement?.StringId ?? string.Empty,
                ["currentSettlementName"] = hero?.CurrentSettlement?.Name?.ToString() ?? string.Empty,
                ["partyId"] = hero?.PartyBelongedTo?.StringId ?? string.Empty,
                ["occupation"] = hero?.Occupation.ToString() ?? string.Empty,
                ["age"] = hero?.Age ?? 0f,
                ["isFemale"] = hero?.IsFemale ?? false,
                ["isAlive"] = hero?.IsAlive ?? false,
                ["isPrisoner"] = hero?.IsPrisoner ?? false,
                ["isLord"] = hero?.IsLord ?? false,
                ["isRuler"] = hero != null && hero.Clan?.Kingdom?.Leader == hero,
                ["isClanLeader"] = hero != null && hero.Clan?.Leader == hero,
                ["isNotable"] = hero?.IsNotable ?? false,
                ["isWanderer"] = hero?.IsWanderer ?? false,
                ["governorOfSettlementId"] = hero?.GovernorOf?.Settlement?.StringId ?? string.Empty,
                ["governorOfSettlementName"] = hero?.GovernorOf?.Settlement?.Name?.ToString() ?? string.Empty,
                ["currentSettlementId"] = hero?.CurrentSettlement?.StringId ?? string.Empty,
                ["currentPartyId"] = hero?.PartyBelongedTo?.StringId ?? string.Empty,
                ["isChild"] = hero?.IsChild ?? false,
                ["isPregnant"] = hero?.IsPregnant ?? false,
                ["childrenCount"] = hero?.Children?.Count ?? 0,
                ["childrenIds"] = new JArray((hero?.Children ?? new List<Hero>()).Where(child => child != null).Select(child => child.StringId)),
                ["childrenNames"] = new JArray((hero?.Children ?? new List<Hero>()).Where(child => child != null).Select(child => child.Name?.ToString() ?? child.StringId)),
                ["bodyWeight"] = hero == null ? 50f : hero.Weight * 100f,
                ["bodyBuild"] = hero == null ? 50f : hero.Build * 100f,
                ["spouseId"] = hero?.Spouse?.StringId ?? string.Empty,
                ["spouseName"] = hero?.Spouse?.Name?.ToString() ?? string.Empty,
                ["fatherId"] = hero?.Father?.StringId ?? string.Empty,
                ["fatherName"] = hero?.Father?.Name?.ToString() ?? string.Empty,
                ["motherId"] = hero?.Mother?.StringId ?? string.Empty,
                ["motherName"] = hero?.Mother?.Name?.ToString() ?? string.Empty,
                ["childrenIds"] = new JArray(hero?.Children?.Select(child => child.StringId) ?? Enumerable.Empty<string>()),
                ["childrenNames"] = new JArray(hero?.Children?.Select(child => child.Name?.ToString() ?? string.Empty) ?? Enumerable.Empty<string>()),
                ["encyclopediaText"] = hero?.EncyclopediaText?.ToString() ?? string.Empty,
                ["nativeEncyclopediaText"] = GetNativeEncyclopediaText(hero),
                ["narrativeVersion"] = ReignBeta.Campaign.ReignCampaignPreparationCampaignBehavior.Instance?.NarrativeVersion ?? 0,
                ["relationToPlayer"] = hero != null && Hero.MainHero != null && hero != Hero.MainHero ? hero.GetRelation(Hero.MainHero) : 0,
                ["sovereignHeroStringId"] = sovereign?.StringId ?? string.Empty,
                ["sovereignName"] = sovereign?.Name?.ToString() ?? string.Empty,
                ["relationToSovereign"] = hero != null && sovereign != null && hero != sovereign
                    ? hero.GetRelation(sovereign) : 0,
                ["playerSovereignHeroStringId"] = playerSovereign?.StringId ?? string.Empty,
                ["playerSovereignName"] = playerSovereign?.Name?.ToString() ?? string.Empty,
                ["sharesPlayerSovereign"] = sovereign != null && sovereign == playerSovereign,
                ["traits"] = BuildTraitProfile(hero),
                ["attributes"] = new JObject { ["endurance"] = hero?.GetAttributeValue(DefaultCharacterAttributes.Endurance) ?? 0 },
                ["skills"] = BuildSkillProfile(hero),
                ["wealth"] = BuildWealthProfile(hero),
                ["inventory"] = BuildInventoryProfile(hero),
                ["appearance"] = BuildAppearanceProfile(hero)
            };
            JObject wanderer = ReignBeta.Campaign.ReignWandererPopulationCampaignBehavior.Instance?.Metadata(hero);
            if (wanderer != null) profile["wandererIdentity"] = wanderer;
            JObject resident = ReignBeta.Campaign.ReignEncounteredResidentsCampaignBehavior.Instance?.Metadata(hero);
            if (resident != null)
            {
                profile["encounteredResident"] = resident;
                // Residence is public context, not formal access to a ruler's/clan's private knowledge.
                profile["kingdomId"] = hero?.Clan?.Kingdom?.StringId ?? string.Empty;
                profile["kingdomName"] = hero?.Clan?.Kingdom?.InformalName?.ToString() ?? string.Empty;
                profile["nativeEncyclopediaText"] = profile["encyclopediaText"] = hero.Name + " is a "
                    + System.Text.RegularExpressions.Regex.Replace((string)resident["occupation"] ?? "resident", "([a-z])([A-Z])", "$1 $2").ToLowerInvariant()
                    + " from " + (string)resident["homeSettlementName"] + ".";
            }
            JObject tavern = ReignBeta.Campaign.ReignTavernHouseCampaignBehavior.Instance?.Metadata(hero);
            if (tavern != null)
            {
                profile["tavernHouse"] = tavern;
                // The authored biography/personality is native canon, including after recruitment.
                // Existing server memories and constructed traits remain separate evolving state.
                string biography = tavern.Value<string>("biography") ?? string.Empty;
                string personality = tavern.Value<string>("personality") ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(personality) && biography.IndexOf(personality, StringComparison.Ordinal) < 0)
                    biography = (biography + " " + personality).Trim();
                if (!string.IsNullOrWhiteSpace(biography)) profile["nativeEncyclopediaText"] = biography;
                profile["kingdomId"] = hero?.Clan?.Kingdom?.StringId ?? string.Empty;
                profile["kingdomName"] = hero?.Clan?.Kingdom?.InformalName?.ToString() ?? string.Empty;
            }
            return profile;
        }

        private static bool IsFinanceSnapshotEligible(Hero hero)
        {
            return hero != null
                && hero.IsAlive
                && !hero.IsChild
                && hero.Age >= 18f
                && (hero.IsLord || hero == Hero.MainHero || hero.Clan?.Leader == hero);
        }

        private static JObject BuildFinanceSnapshotEntry(Hero hero)
        {
            return new JObject
            {
                ["heroStringId"] = hero?.StringId ?? string.Empty,
                ["name"] = hero?.Name?.ToString() ?? string.Empty,
                ["clanId"] = hero?.Clan?.StringId ?? string.Empty,
                ["clanName"] = hero?.Clan?.Name?.ToString() ?? string.Empty,
                ["kingdomId"] = hero?.Clan?.Kingdom?.StringId ?? string.Empty,
                ["isLord"] = hero?.IsLord ?? false,
                ["isRuler"] = hero != null && hero.Clan?.Kingdom?.Leader == hero,
                ["isClanLeader"] = hero != null && hero.Clan?.Leader == hero,
                ["wealth"] = BuildWealthProfile(hero)
            };
        }

        private static JObject BuildIdentityRosterEntry(Hero hero)
        {
            return new JObject
            {
                ["encounteredResident"] = ReignBeta.Campaign.ReignEncounteredResidentsCampaignBehavior.Instance?.Metadata(hero),
                ["heroStringId"] = hero?.StringId ?? string.Empty,
                ["name"] = hero?.Name?.ToString() ?? string.Empty,
                ["occupation"] = hero?.Occupation.ToString() ?? string.Empty,
                ["isAlive"] = hero?.IsAlive ?? false,
                ["isLord"] = hero?.IsLord ?? false,
                ["isRuler"] = hero != null && hero.Clan?.Kingdom?.Leader == hero,
                ["isClanLeader"] = hero != null && hero.Clan?.Leader == hero,
                ["isMercenaryClan"] = hero?.Clan?.IsUnderMercenaryService ?? false,
                ["isNotable"] = hero?.IsNotable ?? false,
                ["isWanderer"] = hero?.IsWanderer ?? false,
                ["governorOfSettlementId"] = hero?.GovernorOf?.Settlement?.StringId ?? string.Empty,
                ["governorOfSettlementName"] = hero?.GovernorOf?.Settlement?.Name?.ToString() ?? string.Empty,
                ["isAdult"] = hero != null && hero.IsAlive && !hero.IsChild && hero.Age >= 18f,
                ["isChild"] = hero?.IsChild ?? false,
                ["isPlayer"] = hero == Hero.MainHero,
                ["isFemale"] = hero?.IsFemale ?? false,
                ["sex"] = hero?.IsFemale == true ? "female" : "male",
                ["clanTier"] = hero?.Clan?.Tier ?? 0,
                ["currentCharm"] = hero?.GetSkillValue(DefaultSkills.Charm) ?? 0,
                ["clanId"] = hero?.Clan?.StringId ?? string.Empty,
                // Identity synchronization is about formal kingdom membership. MapFaction can
                // group notables and transient heroes under settlement factions and previously
                // caused enormous false same-kingdom acquaintance networks.
                ["kingdomId"] = hero?.Clan?.Kingdom?.StringId ?? string.Empty,
                ["sovereignHeroStringId"] = hero?.Clan?.Kingdom?.Leader?.StringId ?? string.Empty,
                ["sovereignName"] = hero?.Clan?.Kingdom?.Leader?.Name?.ToString() ?? string.Empty,
                ["spouseId"] = hero?.Spouse?.StringId ?? string.Empty,
                ["spouseName"] = hero?.Spouse?.Name?.ToString() ?? string.Empty,
                ["fatherId"] = hero?.Father?.StringId ?? string.Empty,
                ["fatherName"] = hero?.Father?.Name?.ToString() ?? string.Empty,
                ["motherId"] = hero?.Mother?.StringId ?? string.Empty,
                ["motherName"] = hero?.Mother?.Name?.ToString() ?? string.Empty,
                ["childrenIds"] = new JArray((hero?.Children ?? new List<Hero>()).Where(x => x != null).Select(x => x.StringId)),
                ["childrenNames"] = new JArray((hero?.Children ?? new List<Hero>()).Where(x => x != null).Select(x => x.Name?.ToString() ?? x.StringId))
            };
        }

        internal static JObject BuildNativePortraitSnapshot(Hero hero)
        {
            JObject row = BuildPortraitRosterEntry(hero);
            row["schema"] = "reign-native-portrait-snapshot-v1";
            row["campaignId"] = GetCampaignId();
            row["age"] = hero.Age;
            row["bodyWeight"] = hero.Weight;
            row["bodyBuild"] = hero.Build;
            if (ReignBeta.Campaign.ReignTavernHouseCampaignBehavior.Instance?.GetPerson(hero) != null)
                row["portraitSourceProfile"] = "ai_source_resident_full_outfit_v1";
            var resident = ReignBeta.Campaign.ReignEncounteredResidentsCampaignBehavior.Instance?.Find(hero);
            if (resident != null)
            {
                var colors = ReignBeta.Campaign.ReignEncounteredResidentsCampaignBehavior.Instance.PortraitColors(hero);
                row["clothingColor1"] = colors.Item1;
                row["clothingColor2"] = colors.Item2;
            }
            return row;
        }

        private static JObject BuildPortraitRosterEntry(Hero hero)
        {
            CharacterObject character = hero?.CharacterObject;
            return new JObject
            {
                ["encounteredResident"] = ReignBeta.Campaign.ReignEncounteredResidentsCampaignBehavior.Instance?.Metadata(hero),
                ["heroStringId"] = hero?.StringId ?? string.Empty,
                ["characterObjectId"] = character?.StringId ?? string.Empty,
                ["name"] = hero?.Name?.ToString() ?? character?.Name?.ToString() ?? string.Empty,
                ["cultureId"] = hero?.Culture?.StringId ?? character?.Culture?.StringId ?? string.Empty,
                ["occupation"] = hero?.Occupation.ToString() ?? string.Empty,
                ["isAlive"] = hero?.IsAlive ?? false,
                ["isFemale"] = hero?.IsFemale ?? character?.IsFemale ?? false,
                ["isLord"] = hero?.IsLord ?? false,
                ["isNotable"] = hero?.IsNotable ?? false,
                ["isWanderer"] = hero?.IsWanderer ?? false,
                ["bodyKey"] = hero == null ? string.Empty : StaticBodyKey(hero.StaticBodyProperties),
                ["age"] = hero?.Age ?? 30f,
                ["bodyWeight"] = hero?.Weight ?? .5f,
                ["bodyBuild"] = hero?.Build ?? .5f,
                ["civilianEquipment"] = BuildPortraitCivilianEquipment(hero),
                ["portraitCacheKey"] = AIPortraits.CharacterCacheId.ForHero(hero) ?? string.Empty
            };
        }

        private static JArray BuildPortraitCivilianEquipment(Hero hero)
        {
            var residents = ReignBeta.Campaign.ReignEncounteredResidentsCampaignBehavior.Instance;
            if (residents?.Find(hero) != null)
            {
                // A resident's actual worn outfit is authoritative even if a slot is empty.
                // Never fall through to a mechanics template or invented renderer clothing.
                Equipment worn = residents.PortraitEquipment(hero);
                return new JArray(Enumerable.Range(0, 10).Where(i => worn?[(EquipmentIndex)i].Item != null)
                    .Select(i => new JObject { ["slot"] = ((EquipmentIndex)i).ToString(),
                        ["itemId"] = worn[(EquipmentIndex)i].Item.StringId,
                        ["modifierId"] = worn[(EquipmentIndex)i].ItemModifier?.StringId ?? string.Empty }));
            }
            CharacterObject character = hero?.CharacterObject;
            foreach (Equipment equipment in new[]
            {
                hero?.CivilianEquipment,
                character?.FirstCivilianEquipment,
                character?.Equipment
            }.Where(candidate => candidate != null))
            {
                JArray items = new JArray();
                bool hasRenderableClothing = false;
                HashSet<int> visitedSlots = new HashSet<int>();
                foreach (EquipmentIndex index in Enum.GetValues(typeof(EquipmentIndex)))
                {
                    int slot = (int)index;
                    bool preserveEncountered = ReignBeta.Campaign.ReignEncounteredResidentsCampaignBehavior.Instance?.Find(hero) != null;
                    if (!visitedSlots.Add(slot) || slot < (preserveEncountered ? 0 : 5) || slot > 9)
                    {
                        continue;
                    }

                    try
                    {
                        ItemObject item = equipment[index].Item;
                        if (item == null || string.IsNullOrWhiteSpace(item.StringId))
                        {
                            continue;
                        }

                        items.Add(new JObject
                        {
                            ["slot"] = index.ToString(),
                            ["itemId"] = item.StringId,
                            ["modifierId"] = equipment[index].ItemModifier?.StringId ?? string.Empty
                        });
                        if (slot >= 6)
                        {
                            hasRenderableClothing = true;
                        }
                    }
                    catch
                    {
                        // EquipmentIndex contains aliases and version-specific sentinels.
                    }
                }

                if (hasRenderableClothing)
                {
                    return items;
                }
            }

            return new JArray();
        }

        private static bool IsPortraitRosterEligible(Hero hero)
        {
            return hero != null
                && hero.IsAlive
                && !hero.IsChild
                && hero.Age >= 18f
                && hero.CharacterObject != null
                && !string.IsNullOrWhiteSpace(hero.StringId);
        }

        private static string BuildPortraitRosterFingerprint(JArray characters)
        {
            StringBuilder builder = new StringBuilder();
            foreach (JObject character in (characters ?? new JArray()).OfType<JObject>()
                .OrderBy(x => x.Value<string>("heroStringId") ?? string.Empty, StringComparer.OrdinalIgnoreCase))
            {
                builder.Append(character.Value<string>("heroStringId") ?? string.Empty).Append('|')
                    .Append(character.Value<string>("characterObjectId") ?? string.Empty).Append('|')
                    .Append(character.Value<string>("name") ?? string.Empty).Append('|')
                    .Append(character.Value<string>("cultureId") ?? string.Empty).Append('|')
                    .Append(character.Value<string>("occupation") ?? string.Empty).Append('|')
                    .Append(character.Value<bool?>("isFemale") == true ? '1' : '0').Append('|')
                    .Append(character.Value<bool?>("isLord") == true ? '1' : '0').Append('|')
                    .Append(character.Value<bool?>("isNotable") == true ? '1' : '0').Append('|')
                    .Append(character.Value<bool?>("isWanderer") == true ? '1' : '0').Append('|')
                    .Append(character.Value<string>("bodyKey") ?? string.Empty).Append('|');
                foreach (JObject item in (character["civilianEquipment"] as JArray ?? new JArray())
                    .OfType<JObject>()
                    .OrderBy(x => x.Value<string>("slot") ?? string.Empty, StringComparer.OrdinalIgnoreCase))
                {
                    builder.Append(item.Value<string>("slot") ?? string.Empty).Append('=')
                        .Append(item.Value<string>("itemId") ?? string.Empty).Append(';');
                }
                builder.Append('|')
                    .Append(character.Value<string>("portraitCacheKey") ?? string.Empty)
                    .Append('\n');
            }

            using (SHA256 sha = SHA256.Create())
            {
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(builder.ToString())))
                    .Replace("-", string.Empty)
                    .ToLowerInvariant();
            }
        }

        private static string StaticBodyKey(StaticBodyProperties properties)
        {
            return properties.KeyPart1.ToString("X16") + properties.KeyPart2.ToString("X16")
                + properties.KeyPart3.ToString("X16") + properties.KeyPart4.ToString("X16")
                + properties.KeyPart5.ToString("X16") + properties.KeyPart6.ToString("X16")
                + properties.KeyPart7.ToString("X16") + properties.KeyPart8.ToString("X16");
        }

        private static JObject BuildPlayerIdentityContext()
        {
            Hero player = Hero.MainHero;
            return new JObject
            {
                ["heroStringId"] = player?.StringId ?? string.Empty,
                ["name"] = player?.Name?.ToString() ?? string.Empty,
                ["isFemale"] = player?.IsFemale ?? false,
                ["isPrisoner"] = player?.IsPrisoner ?? false,
                ["isLord"] = player?.IsLord ?? false,
                ["isRuler"] = player != null && player.Clan?.Kingdom?.Leader == player,
                ["isNotable"] = player?.IsNotable ?? false,
                ["isWanderer"] = player?.IsWanderer ?? false,
                ["occupation"] = player?.Occupation.ToString() ?? string.Empty,
                ["clanTier"] = player?.Clan?.Tier ?? 0,
                ["currentCharm"] = player?.GetSkillValue(DefaultSkills.Charm) ?? 0,
                ["clanId"] = player?.Clan?.StringId ?? string.Empty,
                ["clanName"] = player?.Clan?.Name?.ToString() ?? string.Empty,
                ["kingdomId"] = player?.Clan?.Kingdom?.StringId ?? string.Empty,
                ["kingdomName"] = player?.Clan?.Kingdom?.InformalName?.ToString() ?? string.Empty,
                ["governorOfSettlementId"] = player?.GovernorOf?.Settlement?.StringId ?? string.Empty,
                ["governorOfSettlementName"] = player?.GovernorOf?.Settlement?.Name?.ToString() ?? string.Empty,
                ["appearance"] = BuildAppearanceProfile(player)
            };
        }

        private static JObject BuildHeroFoundationProfile(Hero hero)
        {
            return new JObject
            {
                ["narrativeVersion"] = ReignBeta.Campaign.ReignCampaignPreparationCampaignBehavior.Instance?.NarrativeVersion ?? 0,
                ["campaignId"] = GetCampaignId(),
                ["campaignLabel"] = ReignCampaignIdentity.CurrentCampaignLabel(),
                ["heroStringId"] = hero?.StringId ?? string.Empty,
                ["mainHeroStringId"] = Hero.MainHero?.StringId ?? string.Empty,
                ["mainHeroName"] = Hero.MainHero?.Name?.ToString() ?? string.Empty,
                ["characterObjectId"] = hero?.CharacterObject?.StringId ?? string.Empty,
                ["name"] = hero?.Name?.ToString() ?? string.Empty,
                ["cultureId"] = hero?.Culture?.StringId ?? string.Empty,
                ["clanId"] = hero?.Clan?.StringId ?? string.Empty,
                ["clanName"] = hero?.Clan?.Name?.ToString() ?? string.Empty,
                ["clanTier"] = hero?.Clan?.Tier ?? 0,
                ["kingdomId"] = hero?.Clan?.Kingdom?.StringId ?? hero?.MapFaction?.StringId ?? string.Empty,
                ["kingdomName"] = hero?.Clan?.Kingdom?.InformalName?.ToString() ?? hero?.MapFaction?.Name?.ToString() ?? string.Empty,
                ["currentSettlementId"] = hero?.CurrentSettlement?.StringId ?? string.Empty,
                ["currentSettlementName"] = hero?.CurrentSettlement?.Name?.ToString() ?? string.Empty,
                ["occupation"] = hero?.Occupation.ToString() ?? string.Empty,
                ["age"] = hero?.Age ?? 0f,
                ["isFemale"] = hero?.IsFemale ?? false,
                ["isAlive"] = hero?.IsAlive ?? false,
                ["isPrisoner"] = hero?.IsPrisoner ?? false,
                ["isLord"] = hero?.IsLord ?? false,
                ["isNotable"] = hero?.IsNotable ?? false,
                ["isWanderer"] = hero?.IsWanderer ?? false,
                ["isChild"] = hero?.IsChild ?? false,
                ["isPregnant"] = hero?.IsPregnant ?? false,
                ["spouseId"] = hero?.Spouse?.StringId ?? string.Empty,
                ["fatherId"] = hero?.Father?.StringId ?? string.Empty,
                ["motherId"] = hero?.Mother?.StringId ?? string.Empty,
                ["encyclopediaText"] = hero?.EncyclopediaText?.ToString() ?? string.Empty,
                ["nativeEncyclopediaText"] = GetNativeEncyclopediaText(hero),
                ["traits"] = BuildTraitProfile(hero),
                ["attributes"] = new JObject { ["endurance"] = hero?.GetAttributeValue(DefaultCharacterAttributes.Endurance) ?? 0 },
                ["skills"] = BuildSkillProfile(hero)
            };
        }

        private static JObject BuildWealthProfile(Hero hero)
        {
            int gold = hero?.Gold ?? 0;
            int clanGold = 0;
            int clanTier = 0;
            float clanRenown = 0f;
            int clanFiefCount = 0;
            int? partyInventoryValue = null;
            string partyInventoryState = "not_applicable";
            MobileParty party = hero == Hero.MainHero ? MobileParty.MainParty : hero?.PartyBelongedTo;
            try
            {
                if (party != null)
                {
                    partyInventoryValue = party.ItemRoster?.TotalValue;
                    partyInventoryState = partyInventoryValue.HasValue ? "observed" : "unknown";
                }
            }
            catch
            {
                partyInventoryValue = null;
                partyInventoryState = "unknown";
            }

            try
            {
                clanGold = hero?.Clan?.Gold ?? 0;
                clanTier = hero?.Clan?.Tier ?? 0;
                clanRenown = hero?.Clan?.Renown ?? 0f;
                clanFiefCount = hero?.Clan?.Fiefs?.Count ?? 0;
            }
            catch
            {
                clanGold = 0;
                clanTier = 0;
                clanRenown = 0f;
                clanFiefCount = 0;
            }

            JObject wealth = new JObject
            {
                ["version"] = 3,
                ["dataState"] = "observed",
                ["source"] = "bannerlord_native",
                ["observedWorldDay"] = TaleWorlds.CampaignSystem.Campaign.Current == null ? 0d : CampaignTime.Now.ToDays,
                ["observedUtc"] = DateTime.UtcNow.ToString("o"),
                ["gold"] = gold,
                ["personalWealthTier"] = WealthTier(gold),
                ["partyInventoryValue"] = partyInventoryValue.HasValue ? new JValue(partyInventoryValue.Value) : JValue.CreateNull(),
                ["partyInventoryState"] = partyInventoryState,
                ["visibleWealthTier"] = "unknown", // Deprecated: appearance is supplied separately.
                ["clanGold"] = clanGold,
                ["clanWealthTier"] = WealthTier(clanGold),
                ["clanTier"] = clanTier,
                ["clanRenown"] = clanRenown,
                ["clanFiefCount"] = clanFiefCount,
                ["clanSocialCredit"] = ClanSocialCredit(clanGold, clanTier, clanRenown, clanFiefCount, hero?.IsLord ?? false),
                ["canLeanOnClanReputation"] = clanGold >= 25000 || clanTier >= 3 || clanRenown >= 200f || clanFiefCount > 0 || (hero?.IsLord ?? false),
                ["economicCapacityKnown"] = true,
                ["wealthEvidenceBasis"] = "bannerlord_native",
                ["goldSemantics"] = hero?.Clan != null && gold == clanGold
                    ? "native_hero_wallet_matches_clan_treasury"
                    : "native_hero_wallet",
                ["notes"] = "Observed directly from the loaded Bannerlord campaign. Cargo does not describe visible dress or personal cash. An absent party is not an empty observed inventory. Hero.Gold and Clan.Gold are retained separately; Bannerlord may expose the same shared clan wallet through both values for clan members."
            };
            ReignLiveInteractionTestHost
                .ApplyManipulationFixtureWealthEvidence(hero, wealth);
            return wealth;
        }

        private static JObject BuildNpcObservableWealthProfile(
            Hero hero)
        {
            JObject wealth = BuildWealthProfile(hero);
            ReignLiveInteractionTestHost
                .ApplyNpcObservableWealthEvidence(hero, wealth);
            return wealth;
        }

        private static JObject BuildInventoryProfile(Hero hero)
        {
            MobileParty party = hero == Hero.MainHero ? MobileParty.MainParty : hero?.PartyBelongedTo;
            ItemRoster roster = party?.ItemRoster;
            JObject result = new JObject
            {
                ["source"] = party == null ? "none" : "party_item_roster",
                ["partyId"] = party?.StringId ?? string.Empty,
                ["totalValue"] = roster?.TotalValue ?? 0,
                ["tradeGoodsValue"] = roster?.TradeGoodsTotalValue ?? 0,
                ["totalFood"] = roster?.TotalFood ?? 0,
                ["foodVariety"] = roster?.FoodVariety ?? 0,
                ["mounts"] = roster?.NumberOfMounts ?? 0,
                ["packAnimals"] = roster?.NumberOfPackAnimals ?? 0,
                ["livestock"] = roster?.NumberOfLivestockAnimals ?? 0
            };

            JArray items = new JArray();
            if (roster != null)
            {
                IEnumerable<ItemRosterElement> ordered = roster.OrderByDescending(x => ItemValue(x) * Math.Max(1, x.Amount));
                IEnumerable<ItemRosterElement> selected = ordered.Take(32);

                foreach (ItemRosterElement element in selected
                    .GroupBy(x => x.EquipmentElement.Item?.StringId ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                    .Select(x => x.First()))
                {
                    ItemObject item = element.EquipmentElement.Item;
                    if (item == null || element.Amount <= 0)
                    {
                        continue;
                    }

                    string valueSource;
                    int appraisedValue = ReignValueService.EstimateItemValue(item, element.EquipmentElement, element.Amount, party, out valueSource);
                    items.Add(new JObject
                    {
                        ["itemId"] = item.StringId ?? string.Empty,
                        ["name"] = item.Name?.ToString() ?? item.StringId ?? string.Empty,
                        ["amount"] = element.Amount,
                        ["value"] = item.Value,
                        ["totalValue"] = item.Value * element.Amount,
                        ["appraisedValue"] = appraisedValue,
                        ["valueSource"] = valueSource,
                        ["type"] = item.Type.ToString(),
                        ["isFood"] = item.IsFood,
                        ["isTradeGood"] = item.IsTradeGood,
                        ["isMountable"] = item.IsMountable,
                        ["modifier"] = element.EquipmentElement.ItemModifier?.Name?.ToString() ?? string.Empty
                    });
                }
            }

            result["topItems"] = items;
            return result;
        }

        private static bool IsLiveDialogueTestInventoryItem(ItemRosterElement element)
        {
            ItemObject item = element.EquipmentElement.Item;
            if (item == null || element.Amount <= 0)
            {
                return false;
            }

            if (item.IsTradeGood || item.IsMountable)
            {
                return true;
            }

            string type = item.Type.ToString();
            return string.Equals(type, "BodyArmor", StringComparison.OrdinalIgnoreCase)
                || string.Equals(type, "HeadArmor", StringComparison.OrdinalIgnoreCase)
                || string.Equals(type, "OneHandedWeapon", StringComparison.OrdinalIgnoreCase)
                || string.Equals(type, "TwoHandedWeapon", StringComparison.OrdinalIgnoreCase)
                || string.Equals(type, "Bow", StringComparison.OrdinalIgnoreCase)
                || string.Equals(type, "Crossbow", StringComparison.OrdinalIgnoreCase)
                || string.Equals(type, "Shield", StringComparison.OrdinalIgnoreCase);
        }

        private static JObject BuildAppearanceProfile(Hero hero)
        {
            CharacterObject character = hero?.CharacterObject;
            Equipment civilian = hero?.CivilianEquipment
                ?? character?.FirstCivilianEquipment
                ?? character?.Equipment;
            Equipment battle = hero?.BattleEquipment ?? character?.Equipment;
            JArray civilianItems = BuildEquipmentArray(civilian, "civilian");
            JArray battleItems = BuildEquipmentArray(battle, "battle");
            int civilianValue = SumEquipmentValue(civilianItems);
            int battleValue = SumEquipmentValue(battleItems);
            int trueStatusScore = TrueStatusScore(hero);
            int visibleStatusScore = VisibleStatusScore(hero, civilianValue, battleValue, civilianItems);
            string presentation = ApparentStatusLabel(visibleStatusScore);
            string trueStatus = ApparentStatusLabel(trueStatusScore);
            return new JObject
            {
                ["source"] = "hero_equipment",
                ["trueStatusScore"] = trueStatusScore,
                ["trueStatusLabel"] = trueStatus,
                ["visibleStatusScore"] = visibleStatusScore,
                ["visibleStatusLabel"] = presentation,
                ["statusMismatch"] = visibleStatusScore - trueStatusScore,
                ["presentationEffect"] = PresentationEffect(trueStatusScore, visibleStatusScore),
                ["firstView"] = BuildFirstViewText(hero, civilianItems, presentation, trueStatus, visibleStatusScore - trueStatusScore),
                ["civilianEquipmentValue"] = civilianValue,
                ["battleEquipmentValue"] = battleValue,
                ["civilianEquipment"] = civilianItems,
                ["battleEquipment"] = battleItems
            };
        }

        private static JArray BuildEquipmentArray(Equipment equipment, string equipmentSet)
        {
            JArray items = new JArray();
            if (equipment == null)
            {
                return items;
            }

            HashSet<int> visitedSlots = new HashSet<int>();
            foreach (EquipmentIndex index in Enum.GetValues(typeof(EquipmentIndex)))
            {
                if (!visitedSlots.Add((int)index))
                {
                    continue;
                }

                try
                {
                    EquipmentElement element = equipment[index];
                    ItemObject item = element.Item;
                    if (item == null)
                    {
                        continue;
                    }

                    string valueSource;
                    int appraisedValue = ReignValueService.EstimateItemValue(item, element, 1, MobileParty.MainParty, out valueSource);
                    items.Add(new JObject
                    {
                        ["sourceEquipmentSet"] = equipmentSet ?? string.Empty,
                        ["slot"] = index.ToString(),
                        ["itemId"] = item.StringId ?? string.Empty,
                        ["name"] = item.Name?.ToString() ?? item.StringId ?? string.Empty,
                        ["value"] = item.Value,
                        ["appraisedValue"] = appraisedValue,
                        ["valueSource"] = valueSource,
                        ["type"] = item.Type.ToString(),
                        ["isWeapon"] = IsWeaponSlot(index),
                        ["isArmorOrClothing"] = IsArmorOrClothingSlot(index),
                        ["isMount"] = item.IsMountable,
                        ["modifier"] = element.ItemModifier?.Name?.ToString() ?? string.Empty
                    });
                }
                catch
                {
                    // Some EquipmentIndex values are sentinels on some Bannerlord versions.
                }
            }

            return items;
        }

        private static int ItemValue(ItemRosterElement element)
        {
            return element.EquipmentElement.Item?.Value ?? 0;
        }

        private static int SumEquipmentValue(JArray items)
        {
            int total = 0;
            foreach (JObject item in items.OfType<JObject>())
            {
                total += item.Value<int?>("value") ?? 0;
            }

            return total;
        }

        private static int TrueStatusScore(Hero hero)
        {
            if (hero == null)
            {
                return 0;
            }

            int score = 0;
            if (hero.IsLord) score += 2;
            if (hero.Clan != null && hero.Clan.Tier >= 4) score += 1;
            if (hero.Clan?.Kingdom != null) score += 1;
            if (hero.Gold > 80000) score += 1;
            if (hero.IsNotable) score += 1;
            if (hero.IsWanderer) score -= 1;
            if (hero.IsPrisoner) score -= 2;
            return ClampScore(score, -3, 5);
        }

        private static int VisibleStatusScore(Hero hero, int civilianValue, int battleValue, JArray civilianItems)
        {
            int score = 0;
            if (civilianValue >= 50000) score += 4;
            else if (civilianValue >= 20000) score += 3;
            else if (civilianValue >= 8000) score += 2;
            else if (civilianValue >= 2500) score += 1;
            else if (civilianValue <= 400) score -= 2;
            else if (civilianValue <= 1000) score -= 1;

            bool armed = civilianItems.OfType<JObject>().Any(x => x.Value<bool?>("isWeapon") == true && (x.Value<int?>("value") ?? 0) > 400);
            bool armored = civilianItems.OfType<JObject>().Any(x => x.Value<bool?>("isArmorOrClothing") == true && (x.Value<int?>("value") ?? 0) > 2000);
            if (armed) score += 1;
            if (armored) score += 1;
            if (battleValue > civilianValue * 3 && battleValue > 12000) score += 1;
            if (hero?.IsPrisoner == true) score -= 3;
            return ClampScore(score, -3, 5);
        }

        private static string BuildFirstViewText(Hero hero, JArray civilianItems, string visibleStatus, string trueStatus, int mismatch)
        {
            List<string> notable = civilianItems.OfType<JObject>()
                .OrderByDescending(x => x.Value<int?>("value") ?? 0)
                .Take(5)
                .Select(x => x.Value<string>("name"))
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            string clothes = notable.Count == 0 ? "no notable visible gear" : string.Join(", ", notable);
            string twist = mismatch <= -2
                ? "Their clothes understate their true station."
                : mismatch >= 2
                    ? "Their presentation may make them seem more important than they are."
                    : "Their presentation broadly matches their station.";
            return (hero?.Name?.ToString() ?? "This person") + " appears " + visibleStatus + " at first glance, while their true station reads as " + trueStatus + ". Visible details: " + clothes + ". " + twist;
        }

        private static bool IsWeaponSlot(EquipmentIndex index)
        {
            return index == EquipmentIndex.Weapon0 || index == EquipmentIndex.Weapon1 || index == EquipmentIndex.Weapon2 || index == EquipmentIndex.Weapon3;
        }

        private static bool IsArmorOrClothingSlot(EquipmentIndex index)
        {
            return index == EquipmentIndex.Head || index == EquipmentIndex.Body || index == EquipmentIndex.Cape || index == EquipmentIndex.Gloves || index == EquipmentIndex.Leg;
        }

        private static string WealthTier(int value)
        {
            if (value >= 200000) return "great wealth";
            if (value >= 80000) return "wealthy";
            if (value >= 25000) return "comfortable";
            if (value >= 5000) return "modest";
            if (value >= 1000) return "poor";
            return "destitute";
        }

        private static string ClanSocialCredit(int clanGold, int clanTier, float clanRenown, int clanFiefCount, bool isLord)
        {
            int score = 0;
            if (isLord) score += 1;
            if (clanGold >= 200000) score += 4;
            else if (clanGold >= 80000) score += 3;
            else if (clanGold >= 25000) score += 2;
            else if (clanGold >= 5000) score += 1;
            if (clanTier >= 5) score += 3;
            else if (clanTier >= 3) score += 2;
            else if (clanTier >= 1) score += 1;
            if (clanRenown >= 600f) score += 2;
            else if (clanRenown >= 200f) score += 1;
            if (clanFiefCount >= 3) score += 2;
            else if (clanFiefCount >= 1) score += 1;

            if (score >= 8) return "great-house backing";
            if (score >= 5) return "strong clan backing";
            if (score >= 3) return "credible clan backing";
            if (score >= 1) return "minor clan backing";
            return "little clan backing";
        }

        private static string ApparentStatusLabel(int score)
        {
            if (score >= 5) return "princely or ruling-class";
            if (score >= 3) return "noble or very wealthy";
            if (score >= 1) return "respectable or prosperous";
            if (score == 0) return "ordinary";
            if (score <= -2) return "poor or diminished";
            return "plain or low-status";
        }

        private static string PresentationEffect(int trueStatusScore, int visibleStatusScore)
        {
            int mismatch = visibleStatusScore - trueStatusScore;
            if (mismatch <= -3) return "major_understatement";
            if (mismatch <= -1) return "understated";
            if (mismatch >= 3) return "major_overstatement";
            if (mismatch >= 1) return "overstated";
            return "matched";
        }

        private static int ClampScore(int value, int min, int max)
        {
            return Math.Max(min, Math.Min(max, value));
        }

        private static JObject BuildTraitProfile(Hero hero)
        {
            JObject traits = new JObject();
            if (hero == null)
            {
                return traits;
            }

            traits["valor"] = hero.GetTraitLevel(DefaultTraits.Valor);
            traits["generosity"] = hero.GetTraitLevel(DefaultTraits.Generosity);
            traits["honor"] = hero.GetTraitLevel(DefaultTraits.Honor);
            traits["mercy"] = hero.GetTraitLevel(DefaultTraits.Mercy);
            traits["calculating"] = hero.GetTraitLevel(DefaultTraits.Calculating);
            return traits;
        }

        private static JObject BuildSkillProfile(Hero hero)
        {
            JObject skills = new JObject();
            if (hero == null)
            {
                return skills;
            }

            skills["oneHanded"] = hero.GetSkillValue(DefaultSkills.OneHanded);
            skills["twoHanded"] = hero.GetSkillValue(DefaultSkills.TwoHanded);
            skills["polearm"] = hero.GetSkillValue(DefaultSkills.Polearm);
            skills["bow"] = hero.GetSkillValue(DefaultSkills.Bow);
            skills["crossbow"] = hero.GetSkillValue(DefaultSkills.Crossbow);
            skills["throwing"] = hero.GetSkillValue(DefaultSkills.Throwing);
            skills["riding"] = hero.GetSkillValue(DefaultSkills.Riding);
            skills["athletics"] = hero.GetSkillValue(DefaultSkills.Athletics);
            skills["smithing"] = hero.GetSkillValue(DefaultSkills.Crafting);
            skills["scouting"] = hero.GetSkillValue(DefaultSkills.Scouting);
            skills["tactics"] = hero.GetSkillValue(DefaultSkills.Tactics);
            skills["roguery"] = hero.GetSkillValue(DefaultSkills.Roguery);
            skills["charm"] = hero.GetSkillValue(DefaultSkills.Charm);
            skills["leadership"] = hero.GetSkillValue(DefaultSkills.Leadership);
            skills["trade"] = hero.GetSkillValue(DefaultSkills.Trade);
            skills["steward"] = hero.GetSkillValue(DefaultSkills.Steward);
            skills["medicine"] = hero.GetSkillValue(DefaultSkills.Medicine);
            skills["engineering"] = hero.GetSkillValue(DefaultSkills.Engineering);
            return skills;
        }

        private static JObject BuildLieSceneRisk(Hero claimant, Hero target)
        {
            int claimantTier = claimant?.Clan?.Tier ?? 0;
            int targetTier = target?.Clan?.Tier ?? 0;
            float claimantStrength = HeroPartyStrength(claimant);
            float targetStrength = HeroPartyStrength(target);
            double powerDisadvantage = 0d;
            if (claimantTier >= targetTier + 2) powerDisadvantage += 1d;
            if (claimantStrength > 0f && claimantStrength >= Math.Max(1f, targetStrength) * 2f) powerDisadvantage += 1d;
            if (target?.IsPrisoner == true) powerDisadvantage += 1d;
            return new JObject
            {
                ["claimantClanTier"] = claimantTier, ["targetClanTier"] = targetTier,
                ["claimantPartyStrength"] = claimantStrength, ["targetPartyStrength"] = targetStrength,
                ["targetIsPrisoner"] = target?.IsPrisoner ?? false, ["powerDisadvantage"] = Math.Min(2d, powerDisadvantage)
            };
        }

        private static float HeroPartyStrength(Hero hero)
        {
            try
            {
                MobileParty party = hero == Hero.MainHero ? MobileParty.MainParty : hero?.PartyBelongedTo;
                return party?.Party?.EstimatedStrength ?? 0f;
            }
            catch
            {
                return 0f;
            }
        }

        private static string GetNativeEncyclopediaText(Hero hero)
        {
            if (hero == null || string.IsNullOrWhiteSpace(hero.StringId))
            {
                return string.Empty;
            }

            lock (NativeDescriptionLock)
            {
                if (NativeDescriptions.TryGetValue(hero.StringId, out string preserved))
                {
                    return preserved;
                }

                string current = hero.EncyclopediaText?.ToString() ?? string.Empty;
                NativeDescriptions[hero.StringId] = current;
                return current;
            }
        }

        private static string BuildSceneContext(Hero hero)
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("Conversation mode: individual one-to-one conversation.");
            if (TaleWorlds.CampaignSystem.Campaign.Current != null)
            {
                builder.AppendLine(ReignCalendarService.BuildPromptCalendarContext());
            }
            Settlement settlement = Settlement.CurrentSettlement ?? MobileParty.MainParty?.CurrentSettlement ?? hero?.CurrentSettlement;
            if (settlement != null)
            {
                builder.AppendLine("Current settlement: " + settlement.Name + " (`" + settlement.StringId + "`)");
            }

            if (MobileParty.MainParty != null)
            {
                builder.AppendLine("Main party: " + (MobileParty.MainParty.Name?.ToString() ?? "the player's party"));
            }

            if (hero?.Clan != null)
            {
                builder.AppendLine("NPC clan: " + hero.Clan.Name);
            }

            if (hero?.Clan?.Kingdom != null)
            {
                builder.AppendLine("NPC kingdom: " + hero.Clan.Kingdom.InformalName);
            }

            return builder.ToString().Trim();
        }

        private static string ResolveDialogueLocationId(Hero hero)
        {
            Settlement settlement = Settlement.CurrentSettlement
                ?? MobileParty.MainParty?.CurrentSettlement
                ?? hero?.CurrentSettlement
                ?? hero?.PartyBelongedTo?.CurrentSettlement;
            if (settlement != null)
            {
                return settlement.StringId ?? string.Empty;
            }

            // A remote audit can converse with a hero whose party is travelling on
            // the campaign map. Preserve a native party anchor instead of dropping
            // the turn's location entirely.
            return hero?.PartyBelongedTo?.StringId
                ?? MobileParty.MainParty?.StringId
                ?? string.Empty;
        }

        private static bool IsFixedNobleHero(Hero hero)
        {
            return hero != null
                && hero != Hero.MainHero
                && !string.IsNullOrWhiteSpace(hero.StringId)
                && (hero.IsLord || string.Equals(hero.Occupation.ToString(), "Lord", StringComparison.OrdinalIgnoreCase));
        }

        private static void ApplyHeroEncyclopediaText(Hero hero, string encyclopediaText)
        {
            try
            {
                if (hero == null || string.IsNullOrWhiteSpace(encyclopediaText))
                {
                    return;
                }

                GetNativeEncyclopediaText(hero);

                string current = hero.EncyclopediaText?.ToString() ?? string.Empty;
                if (string.Equals(current.Trim(), encyclopediaText.Trim(), StringComparison.Ordinal))
                {
                    return;
                }

                hero.EncyclopediaText = new TextObject(encyclopediaText);
                ReignLog.Info("Applied Bannerlord Reign encyclopedia backstory hero=" + hero.StringId + " chars=" + encyclopediaText.Length);
            }
            catch (Exception ex)
            {
                ReignLog.Warn("Failed to apply encyclopedia backstory: " + ex.Message);
            }
        }

        private static ReignWorldActionRecord ParseActionRecord(JObject obj)
        {
            if (obj == null)
            {
                return null;
            }

            float now = TaleWorlds.CampaignSystem.Campaign.Current == null ? 0f : (float)CampaignTime.Now.ToDays;
            ReignWorldActionRecord action = new ReignWorldActionRecord
            {
                ActionId = ReadString(obj, "actionId", ReadString(obj, "serverActionId", Guid.NewGuid().ToString("N"))),
                Source = ReadString(obj, "source", "server_llm_or_manual"),
                ActorHeroStringId = ReadString(obj, "actorHeroStringId", ReadString(obj, "actorHeroId", "")),
                ActorKingdomStringId = ReadString(obj, "actorKingdomStringId", ReadString(obj, "actorKingdomId", "")),
                ActorClanStringId = ReadString(obj, "actorClanStringId", ReadString(obj, "actorClanId", "")),
                TargetHeroStringId = ReadString(obj, "targetHeroStringId", ReadString(obj, "targetHeroId", "")),
                TargetKingdomStringId = ReadString(obj, "targetKingdomStringId", ReadString(obj, "targetKingdomId", "")),
                TargetClanStringId = ReadString(obj, "targetClanStringId", ReadString(obj, "targetClanId", "")),
                TargetSettlementStringId = ReadString(obj, "targetSettlementStringId", ReadString(obj, "targetSettlementId", "")),
                Reason = ReadString(obj, "reason", "Server action from Bannerlord Reign."),
                TermsJson = ReadString(obj, "termsJson", ""),
                SupporterClanIdsCsv = ReadSupporterClanIds(obj),
                AuthorizationMode = ReadString(obj, "authorizationMode", ""),
                NegotiationId = ReadString(obj, "negotiationId", ""),
                TermsHash = ReadString(obj, "termsHash", ""),
                ExecutionPhase = ReadString(obj, "executionPhase", "idle"),
                ExecutionSnapshotJson = ReadString(obj, "executionSnapshotJson", "{}"),
                NegotiatedCommand = ReadString(obj, "command", ""),
                MinimumTroops = ReadInt(obj, "minimumTroops", 40),
                DesiredStrength = ReadInt(obj, "desiredStrength", 350),
                MaxAttempts = ReadInt(obj, "maxAttempts", 3),
                RequiresAcceptance = ReadBool(obj, "requiresAcceptance", false),
                AcceptedByHeroStringId = ReadString(obj, "acceptedByHeroStringId", ""),
                AcceptedDay = ReadFloat(obj, "acceptedDay", now),
                CreatedDay = now,
                ExecuteAfterDay = now + ReadFloat(obj, "executeAfterDays", 0f)
            };

            action.Type = ParseActionType(ReadString(obj, "type", ReadString(obj, "command", "")), ReadInt(obj, "typeValue", 0));
            if (action.Type == ReignWorldActionType.StrategyCaptureSettlement && action.MaxAttempts <= 3)
            {
                action.MaxAttempts = 24;
            }

            return action;
        }

        private static List<ReignWorldActionRecord> ParseQueuedActionRows(JToken token)
        {
            List<ReignWorldActionRecord> results = new List<ReignWorldActionRecord>();
            JArray rows = token as JArray;
            if (rows == null)
            {
                return results;
            }

            foreach (JToken rowToken in rows)
            {
                JObject row = rowToken as JObject;
                if (row == null)
                {
                    continue;
                }

                JObject recordObject = row["record"] as JObject ?? row;
                ReignWorldActionRecord record = ParseActionRecord(recordObject);
                if (record != null && record.Type != ReignWorldActionType.Unknown)
                {
                    results.Add(record);
                }
                else
                {
                    string command = ReadString(recordObject, "command", ReadString(recordObject, "type", ""));
                    ReignLog.Warn("Dropped queued server action with unknown type/command: " + command);
                }
            }

            return results;
        }

        private static ReignWorldActionType ParseActionType(string text, int typeValue)
        {
            if (typeValue > 0)
            {
                return (ReignWorldActionType)typeValue;
            }

            string value = (text ?? string.Empty).Replace("-", "_").Replace(" ", "_").ToLowerInvariant();
            switch (value)
            {
                case "diplomacydeclarewar":
                case "diplomacy_declare_war":
                case "declare_war":
                    return ReignWorldActionType.DiplomacyDeclareWar;
                case "diplomacymakepeace":
                case "diplomacy_make_peace":
                case "make_peace":
                    return ReignWorldActionType.DiplomacyMakePeace;
                case "diplomacyoffertributepeace":
                case "offer_tribute_peace":
                    return ReignWorldActionType.DiplomacyOfferTributePeace;
                case "diplomacyrecordpromise":
                case "record_promise":
                    return ReignWorldActionType.DiplomacyRecordPromise;
                case "diplomacydemandreparationspeace":
                case "demand_reparations_peace":
                    return ReignWorldActionType.DiplomacyDemandReparationsPeace;
                case "diplomacydemandsettlementpeace":
                case "demand_settlement_peace":
                    return ReignWorldActionType.DiplomacyDemandSettlementPeace;
                case "diplomacydemandsurrenderpeace":
                case "demand_surrender_peace":
                    return ReignWorldActionType.DiplomacyDemandSurrenderPeace;
                case "diplomacysigntradeagreement":
                case "sign_trade_agreement":
                    return ReignWorldActionType.DiplomacySignTradeAgreement;
                case "diplomacysignnonaggressionpact":
                case "sign_non_aggression_pact":
                    return ReignWorldActionType.DiplomacySignNonAggressionPact;
                case "diplomacysignalliance":
                case "sign_alliance":
                    return ReignWorldActionType.DiplomacySignAlliance;
                case "diplomacysigndefensivepact":
                case "sign_defensive_pact":
                    return ReignWorldActionType.DiplomacySignDefensivePact;
                case "diplomacysigntemporarytruce":
                case "sign_temporary_truce":
                    return ReignWorldActionType.DiplomacySignTemporaryTruce;
                case "diplomacybreaktreaty":
                case "break_treaty":
                    return ReignWorldActionType.DiplomacyBreakTreaty;
                case "diplomacyexchangeprisoners":
                case "exchange_prisoners":
                    return ReignWorldActionType.DiplomacyExchangePrisoners;
                case "diplomacyransompackage":
                case "ransom_package":
                    return ReignWorldActionType.DiplomacyRansomPackage;
                case "diplomacyhostageguarantee":
                case "hostage_guarantee":
                    return ReignWorldActionType.DiplomacyHostageGuarantee;
                case "diplomacywarindemnity":
                case "war_indemnity":
                    return ReignWorldActionType.DiplomacyWarIndemnity;
                case "diplomacyrecognizeconquest":
                case "recognize_conquest":
                    return ReignWorldActionType.DiplomacyRecognizeConquest;
                case "diplomacyreturnoccupiedsettlement":
                case "return_occupied_settlement":
                    return ReignWorldActionType.DiplomacyReturnOccupiedSettlement;
                case "diplomacydemilitarizedborder":
                case "demilitarized_border":
                    return ReignWorldActionType.DiplomacyDemilitarizedBorder;
                case "diplomacytradeembargo":
                case "trade_embargo":
                    return ReignWorldActionType.DiplomacyTradeEmbargo;
                case "diplomacycaravanprotectionagreement":
                case "caravan_protection_agreement":
                    return ReignWorldActionType.DiplomacyCaravanProtectionAgreement;
                case "diplomacysupplyagreement":
                case "supply_agreement":
                    return ReignWorldActionType.DiplomacySupplyAgreement;
                case "diplomacyloanorsubsidy":
                case "loan_or_subsidy":
                    return ReignWorldActionType.DiplomacyLoanOrSubsidy;
                case "diplomacypaytostayneutral":
                case "pay_to_stay_neutral":
                    return ReignWorldActionType.DiplomacyPayToStayNeutral;
                case "diplomacypaytojoinwar":
                case "pay_to_join_war":
                    return ReignWorldActionType.DiplomacyPayToJoinWar;
                case "diplomacyguaranteeindependence":
                case "guarantee_independence":
                    return ReignWorldActionType.DiplomacyGuaranteeIndependence;
                case "diplomacyprotectorateorvassalage":
                case "protectorate_or_vassalage":
                    return ReignWorldActionType.DiplomacyProtectorateOrVassalage;
                case "diplomacypackage":
                case "diplomatic_package":
                    return ReignWorldActionType.DiplomacyPackage;
                case "diplomacybackrebellion":
                case "back_rebellion":
                    return ReignWorldActionType.DiplomacyBackRebellion;
                case "strategyrecruitandrecover":
                case "recruit_and_recover":
                    return ReignWorldActionType.StrategyRecruitAndRecover;
                case "strategyformarmy":
                case "form_army":
                    return ReignWorldActionType.StrategyFormArmy;
                case "strategyattacksettlement":
                case "attack_settlement":
                    return ReignWorldActionType.StrategyAttackSettlement;
                case "strategycapturesettlement":
                case "capture_settlement":
                case "capture_settlement_plan":
                    return ReignWorldActionType.StrategyCaptureSettlement;
                case "politicsstartrulingclanrebellion":
                case "start_ruling_clan_rebellion":
                    return ReignWorldActionType.PoliticsStartRulingClanRebellion;
                case "politicsinstallrulingclan":
                case "install_ruling_clan":
                    return ReignWorldActionType.PoliticsInstallRulingClan;
                case "politicsmarriagealliance":
                case "marriage_alliance":
                    return ReignWorldActionType.PoliticsMarriageAlliance;
                case "politicssupportclaimant":
                case "support_claimant":
                    return ReignWorldActionType.PoliticsSupportClaimant;
                case "politicsencourageclandefection":
                case "encourage_clan_defection":
                    return ReignWorldActionType.PoliticsEncourageClanDefection;
                case "politicsexileclan":
                case "exile_clan":
                    return ReignWorldActionType.PoliticsExileClan;
                case "politicsrestoreexiledclan":
                case "restore_exiled_clan":
                    return ReignWorldActionType.PoliticsRestoreExiledClan;
                case "politicsmediateclandispute":
                case "mediate_clan_dispute":
                    return ReignWorldActionType.PoliticsMediateClanDispute;
                case "politicsresolvecivilwar":
                case "resolve_civil_war":
                    return ReignWorldActionType.PoliticsResolveCivilWar;
                case "politicsrecruitlordtorebellion":
                case "recruit_lord_to_rebellion":
                    return ReignWorldActionType.PoliticsRecruitLordToRebellion;
                case "politicsjoinrebellion":
                case "join_rebellion":
                    return ReignWorldActionType.PoliticsJoinRebellion;
                case "politicssurrenderrebellion":
                case "surrender_rebellion":
                    return ReignWorldActionType.PoliticsSurrenderRebellion;
                case "politicsresolverebellionpledge":
                case "resolve_rebellion_pledge":
                    return ReignWorldActionType.PoliticsResolveRebellionPledge;
                case "politicsresolverebellionsummons":
                case "resolve_rebellion_summons":
                    return ReignWorldActionType.PoliticsResolveRebellionSummons;
                case "politicsconsentgovernmentreduction":
                case "consent_government_reduction":
                    return ReignWorldActionType.PoliticsConsentGovernmentReduction;
                case "regularfollowonmap":
                case "follow_on_map":
                    return ReignWorldActionType.RegularFollowOnMap;
                case "regularfollowinscene":
                case "follow_in_scene":
                    return ReignWorldActionType.RegularFollowInScene;
                case "regularstopfollowing":
                case "stop_following":
                    return ReignWorldActionType.RegularStopFollowing;
                case "regulargotosettlement":
                case "go_to_settlement":
                    return ReignWorldActionType.RegularGoToSettlement;
                case "regularpatrolaroundsettlement":
                case "patrol_around_settlement":
                    return ReignWorldActionType.RegularPatrolAroundSettlement;
                case "regularwaitnearsettlement":
                case "wait_near_settlement":
                    return ReignWorldActionType.RegularWaitNearSettlement;
                case "regularraidvillage":
                case "raid_village":
                    return ReignWorldActionType.RegularRaidVillage;
                case "regularbesiegesettlement":
                case "besiege_settlement":
                    return ReignWorldActionType.RegularBesiegeSettlement;
                case "regularcreateparty":
                case "create_party":
                    return ReignWorldActionType.RegularCreateParty;
                case "regularshowtheway":
                case "show_the_way":
                    return ReignWorldActionType.RegularShowTheWay;
                case "regularattackparty":
                case "attack_party":
                    return ReignWorldActionType.RegularAttackParty;
                case "regularattackplayerparty":
                case "attack_player_party":
                    return ReignWorldActionType.RegularAttackPlayerParty;
                case "regularsurrendertoplayer":
                case "surrender_to_player":
                    return ReignWorldActionType.RegularSurrenderToPlayer;
                case "regularleaveplayeralone":
                case "leave_player_alone":
                    return ReignWorldActionType.RegularLeavePlayerAlone;
                case "regularkillcharacter":
                case "kill_character":
                    return ReignWorldActionType.RegularKillCharacter;
                case "regularduelplayer":
                case "duel_player":
                    return ReignWorldActionType.RegularDuelPlayer;
                case "regulargivegoldtoplayer":
                case "give_gold_to_player":
                    return ReignWorldActionType.RegularGiveGoldToPlayer;
                case "regulartransfergold":
                case "transfer_gold":
                    return ReignWorldActionType.RegularTransferGold;
                case "regulartransferitem":
                case "transfer_item":
                    return ReignWorldActionType.RegularTransferItem;
                case "regulartransferworkshop":
                case "transfer_workshop":
                    return ReignWorldActionType.RegularTransferWorkshop;
                case "regulartransferprisoner":
                case "transfer_prisoner":
                    return ReignWorldActionType.RegularTransferPrisoner;
                case "regularpreparearrest":
                case "prepare_arrest":
                    return ReignWorldActionType.RegularPrepareArrest;
                case "regularconfirmarrest":
                case "confirm_arrest":
                    return ReignWorldActionType.RegularConfirmArrest;
                case "regularreleasearrestedcharacter":
                case "release_arrested_character":
                    return ReignWorldActionType.RegularReleaseArrestedCharacter;
                case "regularrescindarrestaccusation":
                case "rescind_arrest_accusation":
                    return ReignWorldActionType.RegularRescindArrestAccusation;
                case "regularplayerattackparty":
                case "player_attack_party":
                    return ReignWorldActionType.RegularPlayerAttackParty;
                case "regularissuecampaignorder":
                case "issue_campaign_order":
                    return ReignWorldActionType.RegularIssueCampaignOrder;
                case "regularrevisecampaignorder":
                case "revise_campaign_order":
                    return ReignWorldActionType.RegularReviseCampaignOrder;
                case "regularrespondtoorderreport":
                case "respond_to_order_report":
                    return ReignWorldActionType.RegularRespondToOrderReport;
                case "regularcancelcampaignorder":
                case "cancel_campaign_order":
                    return ReignWorldActionType.RegularCancelCampaignOrder;
                case "regularcreateclanaccord":
                case "create_clan_accord":
                case "createclanaccord":
                    return ReignWorldActionType.RegularCreateClanAccord;
                case "regularcancelclanaccord":
                case "cancel_clan_accord":
                case "cancelclanaccord":
                    return ReignWorldActionType.RegularCancelClanAccord;
                case "regularaccepttemporarypartyguest":
                case "accept_temporary_party_guest":
                    return ReignWorldActionType.RegularAcceptTemporaryPartyGuest;
                case "regularrecruitencounteredresident":
                case "recruit_encountered_resident":
                    return ReignWorldActionType.RegularRecruitEncounteredResident;
                case "regularreleaseresidentfromduty":
                case "release_resident_from_duty":
                    return ReignWorldActionType.RegularReleaseResidentFromDuty;
                case "regularrenewtemporarypartyguest":
                case "renew_temporary_party_guest":
                    return ReignWorldActionType.RegularRenewTemporaryPartyGuest;
                case "regularendtemporarypartyguest":
                case "end_temporary_party_guest":
                    return ReignWorldActionType.RegularEndTemporaryPartyGuest;
                case "regularacknowledgeownfactioncombatrisk":
                case "acknowledge_own_faction_combat_risk":
                    return ReignWorldActionType.RegularAcknowledgeOwnFactionCombatRisk;
                case "regulartradepackage":
                case "trade_package":
                case "tradepackage":
                    return ReignWorldActionType.RegularTradePackage;
                case "regularhireplayerasmercenary":
                case "hire_player_as_mercenary":
                    return ReignWorldActionType.RegularHirePlayerAsMercenary;
                case "regulardismissplayermercenary":
                case "dismiss_player_mercenary":
                    return ReignWorldActionType.RegularDismissPlayerMercenary;
                case "regularofferplayervassalage":
                case "offer_player_vassalage":
                    return ReignWorldActionType.RegularOfferPlayerVassalage;
                case "regulardismissplayervassal":
                case "dismiss_player_vassal":
                    return ReignWorldActionType.RegularDismissPlayerVassal;
                case "regularjoinclan":
                case "join_clan":
                    return ReignWorldActionType.RegularJoinClan;
                case "regularleaveclan":
                case "leave_clan":
                    return ReignWorldActionType.RegularLeaveClan;
                case "regularjoinkingdom":
                case "join_kingdom":
                    return ReignWorldActionType.RegularJoinKingdom;
                case "regularleavekingdom":
                case "leave_kingdom":
                    return ReignWorldActionType.RegularLeaveKingdom;
                case "regularhiremercenaryclan":
                case "hire_mercenary_clan":
                    return ReignWorldActionType.RegularHireMercenaryClan;
                default:
                    return ReignWorldActionType.Unknown;
            }
        }

        public static Task<JObject> ApplyArrestCaseEffectAsync(JObject payload)
        {
            JObject request = payload == null ? new JObject() : new JObject(payload);
            request["campaignId"] = GetCampaignId();
            request["timelineId"] = ReignBeta.Campaign.ReignWorldHistoryCampaignBehavior.Instance
                ?.TimelineId ?? "main";
            return PostJsonAsync("/arrests/case/apply", request);
        }

        public static Task<JObject> EvaluateArrestEvidenceAsync(JObject payload)
        {
            JObject request = payload == null ? new JObject() : new JObject(payload);
            request["campaignId"] = GetCampaignId();
            request["timelineId"] = ReignBeta.Campaign.ReignWorldHistoryCampaignBehavior.Instance
                ?.TimelineId ?? "main";
            return PostJsonAsync("/arrests/evidence/evaluate", request);
        }

        private static string ReadSupporterClanIds(JObject obj)
        {
            JToken token = obj?["supporterClanIds"];
            if (token is JArray array)
            {
                return string.Join(",", array.Select(x => x.ToString()).Where(x => !string.IsNullOrWhiteSpace(x)));
            }

            return ReadString(obj, "supporterClanIdsCsv", "");
        }

        private static List<string> ReadStringArray(JToken token)
        {
            List<string> result = new List<string>();
            if (token is JArray array)
            {
                foreach (JToken item in array)
                {
                    string value = item?.ToString();
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        result.Add(value.Trim());
                    }
                }
            }
            else
            {
                string value = token?.ToString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    result.Add(value.Trim());
                }
            }

            return result;
        }

        private static string BuildSummary()
        {
            Kingdom playerKingdom = Clan.PlayerClan?.Kingdom;
            if (playerKingdom == null)
            {
                return "The player clan is independent. Bannerlord Reign daily world snapshot recorded.";
            }

            JArray enemyKingdoms = KingdomWars(playerKingdom, 32);
            return "The player kingdom is " + playerKingdom.InformalName
                + ", active enemy kingdoms: " + enemyKingdoms.Count
                + ", active armies: " + playerKingdom.Armies.Count
                + ", clans: " + playerKingdom.Clans.Count + ".";
        }

        private static JObject BuildDailyWorldSnapshotData()
        {
            Kingdom playerKingdom = Clan.PlayerClan?.Kingdom;
            return new JObject
            {
                ["snapshotKind"] = "volatile_current_state",
                ["observedWorldDay"] = TaleWorlds.CampaignSystem.Campaign.Current == null
                    ? 0d
                    : CampaignTime.Now.ToDays,
                ["playerKingdom"] = KingdomSummary(playerKingdom),
                ["enemyKingdoms"] = KingdomWars(playerKingdom, 32)
            };
        }

        private static IEnumerable<string> GetActorIds()
        {
            if (Hero.MainHero != null)
            {
                yield return Hero.MainHero.StringId;
            }

            Kingdom playerKingdom = Clan.PlayerClan?.Kingdom;
            if (playerKingdom?.Leader != null && playerKingdom.Leader != Hero.MainHero)
            {
                yield return playerKingdom.Leader.StringId;
            }
        }

        internal static string GetCampaignId()
        {
            return PortraitRequestScope.Current?.CampaignId ?? ReignCampaignIdentity.CurrentCampaignId();
        }

        private static string ReadString(JObject obj, string key, string fallback)
        {
            JToken token = obj?[key];
            return token == null || token.Type == JTokenType.Null ? fallback : token.ToString();
        }

        private static int ReadInt(JObject obj, string key, int fallback)
        {
            JToken token = obj?[key];
            return token == null || token.Type == JTokenType.Null || !int.TryParse(token.ToString(), out int value) ? fallback : value;
        }

        private static float ReadFloat(JObject obj, string key, float fallback)
        {
            JToken token = obj?[key];
            return token == null || token.Type == JTokenType.Null || !float.TryParse(token.ToString(), out float value) ? fallback : value;
        }

        private static bool ReadBool(JObject obj, string key, bool fallback)
        {
            JToken token = obj?[key];
            if (token == null || token.Type == JTokenType.Null)
            {
                return fallback;
            }

            if (token.Type == JTokenType.Boolean)
            {
                return token.Value<bool>();
            }

            return bool.TryParse(token.ToString(), out bool value) ? value : fallback;
        }
    }

    public sealed class ReignDialogueLine
    {
        public ReignDialogueLine(string speaker, string text, string role, string channel = "in_person", string letterId = "", string threadId = "", string direction = "", double worldDay = 0d)
        {
            Speaker = speaker ?? string.Empty;
            Text = text ?? string.Empty;
            Role = role ?? string.Empty;
            Channel = string.IsNullOrWhiteSpace(channel) ? "in_person" : channel;
            LetterId = letterId ?? string.Empty;
            ThreadId = threadId ?? string.Empty;
            Direction = direction ?? string.Empty;
            WorldDay = worldDay;
        }

        public string Speaker { get; }
        public string Text { get; }
        public string Role { get; }
        public string Channel { get; }
        public string LetterId { get; }
        public string ThreadId { get; }
        public string Direction { get; }
        public double WorldDay { get; }
        public bool IsCorrespondence => string.Equals(Channel, "correspondence", StringComparison.OrdinalIgnoreCase);
        public string DisplaySpeaker => IsCorrespondence
            ? Speaker + " (Letter" + (WorldDay > 0d ? ", Day " + WorldDay.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) : string.Empty) + ")"
            : Speaker;
    }

    public sealed class ReignDialogueReply
    {
        public bool Ok { get; set; }
        public string Text { get; set; } = string.Empty;
        public string Emotion { get; set; } = string.Empty;
        public string Intent { get; set; } = string.Empty;
        public string Error { get; set; } = string.Empty;
        public string EncyclopediaText { get; set; } = string.Empty;
        public string ActionShadowPreview { get; set; } = string.Empty;
        public bool ConceptionGateNeeded { get; set; }
        public ReignConceptionAttemptResult ConceptionAttempt { get; set; }
        public List<ReignWorldActionRecord> QueuedActions { get; set; } = new List<ReignWorldActionRecord>();
        public long ClientTotalMs { get; set; }
        public string TimingSummary { get; set; } = string.Empty;
        public string CorrelationId { get; set; } = string.Empty;
        public string ConversationSessionId { get; set; } = string.Empty;
        public string ExchangeId { get; set; } = string.Empty;
        public JArray TurnIds { get; set; } = new JArray();
        public JArray SelectedContextPulls { get; set; } = new JArray();
        public JArray ContextBundles { get; set; } = new JArray();
        public JArray MemoryWrites { get; set; } = new JArray();
        public JObject ChancellorDecision { get; set; } = new JObject();
        public JObject RawResponse { get; set; } = new JObject();
    }

    public sealed class ReignConversationFinishResult
    {
        public bool Ok { get; set; }
        public string SessionId { get; set; } = string.Empty;
        public string SceneSummaryId { get; set; } = string.Empty;
        public string Error { get; set; } = string.Empty;
        public JObject Raw { get; set; } = new JObject();
    }

    public sealed class ReignEventReply
    {
        public bool Ok { get; set; }
        public string HeroStringId { get; set; } = string.Empty;
        public string CorrelationId { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
        public string Participation { get; set; } = "speak";
        public string ReactionTargetHeroStringId { get; set; } = string.Empty;
        public string Emotion { get; set; } = string.Empty;
        public string Intent { get; set; } = string.Empty;
        public string RelationshipSignal { get; set; } = string.Empty;
        public JArray RelationshipAssessments { get; set; } = new JArray();
        public JArray SelectedContextPulls { get; set; } = new JArray();
        public JArray ContextBundles { get; set; } = new JArray();
        public JArray QueuedActions { get; set; } = new JArray();
        public JObject RawResponse { get; set; } = new JObject();
        public string Error { get; set; } = string.Empty;
        public string EncyclopediaText { get; set; } = string.Empty;
        public long ClientTotalMs { get; set; }
        public string TimingSummary { get; set; } = string.Empty;
    }

    public sealed class ReignPartyChatReply
    {
        public bool Ok { get; set; }
        public string SpeakerHeroStringId { get; set; } = string.Empty;
        public string SpeakerName { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
        public string Emotion { get; set; } = string.Empty;
        public string Intent { get; set; } = string.Empty;
        public string RelationshipSignal { get; set; } = string.Empty;
        public JArray RelationshipAssessments { get; set; } = new JArray();
        public string Participation { get; set; } = "speak";
        public string ReactionTargetHeroStringId { get; set; } = string.Empty;
        public string Error { get; set; } = string.Empty;
        public string EncyclopediaText { get; set; } = string.Empty;
        public string CorrelationId { get; set; } = string.Empty;
        public string ConversationSessionId { get; set; } = string.Empty;
        public string ExchangeId { get; set; } = string.Empty;
        public JArray SelectedContextPulls { get; set; } = new JArray();
        public JArray QueuedActions { get; set; } = new JArray();
        public long ClientTotalMs { get; set; }
        public string TimingSummary { get; set; } = string.Empty;
        public JObject RawResponse { get; set; } = new JObject();
    }

    public sealed class ReignHeroGenerationResult
    {
        public bool Ok { get; set; }
        public int Created { get; set; }
        public int Updated { get; set; }
        public int Failed { get; set; }
        public int Total { get; set; }
        public string Error { get; set; } = string.Empty;
    }

    public sealed class ReignCharacterConstructionResult
    {
        public bool Ok { get; set; }
        public int Constructed { get; set; }
        public int Failed { get; set; }
        public int Total { get; set; }
        public int LlmUsed { get; set; }
        public string Error { get; set; } = string.Empty;
    }

    public sealed class ReignGeneratedWildernessRequest
    {
        public string TerrainKey { get; set; } = "plain";
        public string LocationText { get; set; } = "on the road";
        public string TimeOfDayText { get; set; } = "unknown time of day";
        public string ExternalHeroId { get; set; } = string.Empty;
        public string ExternalContext { get; set; } = string.Empty;
        public List<ReignGeneratedWildernessParticipant> Participants { get; set; } = new List<ReignGeneratedWildernessParticipant>();
    }

    public sealed class ReignGeneratedWildernessParticipant
    {
        public Hero Hero { get; set; }
        public string Role { get; set; } = string.Empty;
        public bool IsOutsideNpc { get; set; }
    }

    public sealed class ReignGeneratedWildernessScenario
    {
        public bool Ok { get; set; }
        public string Error { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string ApproachDescription { get; set; } = string.Empty;
        public string OpeningText { get; set; } = string.Empty;
        public string PlayerHook { get; set; } = string.Empty;
        public List<string> ParticipantHeroIds { get; set; } = new List<string>();
        public string ExternalHeroId { get; set; } = string.Empty;
        public string ExternalContext { get; set; } = string.Empty;
        public string TerrainKey { get; set; } = string.Empty;
        public string EmotionalPressure { get; set; } = string.Empty;
        public string SurfaceClues { get; set; } = string.Empty;
        public string HiddenContext { get; set; } = string.Empty;
        public string DiscoveryRoutes { get; set; } = string.Empty;
        public long ClientTotalMs { get; set; }
        public string TimingSummary { get; set; } = string.Empty;
    }

    public sealed class ReignPortraitGenerationResult
    {
        public bool Ok { get; set; }
        public byte[] ImageBytes { get; set; }
        public string Provider { get; set; } = string.Empty;
        public string Model { get; set; } = string.Empty;
        public string Adapter { get; set; } = string.Empty;
        public string Error { get; set; } = string.Empty;
        public long ClientTotalMs { get; set; }
        public long ServerDurationMs { get; set; }
        public string TimingSummary { get; set; } = string.Empty;
        public string ProductSchema { get; set; } = string.Empty;
        public int ProductVersion { get; set; }
        public bool ProductAccepted { get; set; }
        public int PortraitWidth { get; set; }
        public int PortraitHeight { get; set; }
        public int SourceWidth { get; set; }
        public int SourceHeight { get; set; }
        public string PortraitSha256 { get; set; } = string.Empty;
        public string SourceSha256 { get; set; } = string.Empty;
        public string PromptSha256 { get; set; } = string.Empty;
        public string EffectivePrompt { get; set; } = string.Empty;
        public string PortraitProductReceiptJson { get; set; } = string.Empty;
        public string PortraitInputJson { get; set; } = string.Empty;
        public byte[] SourceImageBytes { get; set; }
        public PortraitFaceFocus FaceFocus { get; set; }
        public string GenerationOperationId { get; set; } = string.Empty;
        public string GenerationStatus { get; set; } = string.Empty;
        public bool GenerationMayStillComplete { get; set; }
        public bool Recovered { get; set; }
    }
}
