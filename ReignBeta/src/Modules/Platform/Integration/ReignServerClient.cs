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
using ReignBeta.Characters.Campaign;
using ReignBeta.Characters.Integration;
using ReignBeta.Dialogue.Integration;
using ReignBeta.Dialogue.Runtime;
using ReignBeta.Diplomacy.Campaign;
using ReignBeta.Government.Campaign;
using ReignBeta.Persistence.Integration;
using ReignBeta.Platform.Campaign;
using ReignBeta.Platform.Runtime;
using ReignBeta.Platform.Settings;
using ReignBeta.Portraits.Integration;
using ReignBeta.Relationships.Campaign;
using ReignBeta.Relationships.Integration;
using ReignBeta.WorldSimulation.Campaign;
using ReignBeta.WorldSimulation.Integration;
using ReignBeta.WorldSimulation.News;
using ReignBeta.WorldSimulation.Runtime;
using ReignBeta.WorldSimulation.World;
#if !REIGN_EXCLUDE_COURT
using ReignBeta.Court.Campaign;
#endif

namespace ReignBeta.Platform.Integration
{
    public static partial class ReignServerClient
    {
        private static readonly object NativeDescriptionLock = new object();
        private static readonly Dictionary<string, string> NativeDescriptions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static readonly object PortraitRosterSyncLock = new object();
        private static readonly Dictionary<string, string> SyncedPortraitRosterFingerprints =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
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

        internal static string NewCorrelationId(string mode, string subjectId)
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

        internal static async Task<List<ReignWorldActionRecord>> FetchActionCommandsAsync(RequestFence fence)
        {
            ReignBetaSettings settings = ReignBetaSettings.Instance;
            if (settings != null && (!settings.UseLocalServer || !settings.ReceiveServerActionCommands))
            {
                return new List<ReignWorldActionRecord>();
            }

            string campaignId = GetCampaignId();
            string route = "/actions/next?campaignId=" + Uri.EscapeDataString(campaignId) + "&limit=10";
            JObject response = await GetJsonAsync(route).ConfigureAwait(false);
            if (response?["ok"]?.Type != JTokenType.Boolean || response.Value<bool>("ok") != true
                || response["campaignId"]?.Type != JTokenType.String || response.Value<string>("campaignId") != campaignId
                || !(response["actions"] is JArray actions))
                throw new InvalidOperationException("Invalid server action poll response for the requested campaign.");

            return await ReignMainThread.InvokeAsync(() =>
            {
                if (!fence.IsCurrentActionPoll())
                {
                    ReignLog.Warn("Dropped server action poll from an earlier loaded campaign scope.");
                    return new List<ReignWorldActionRecord>();
                }

                string origin = ResponseOrigin(response) ?? string.Empty;
                var parsed = ReignActionPollRows.Read(actions, (row, id) =>
                {
                    ReignWorldActionRecord record = ParseActionRecord(row, id);
                    if (record == null || record.Type == ReignWorldActionType.Unknown
                        || !Enum.IsDefined(typeof(ReignWorldActionType), record.Type)) return null;
                    if (float.IsNaN(record.ExecuteAfterDay) || float.IsInfinity(record.ExecuteAfterDay)
                        || float.IsNaN(record.AcceptedDay) || float.IsInfinity(record.AcceptedDay))
                        throw new FormatException("Non-finite campaign day.");

                    record.ImportOrigin = origin;
                    record.ImportFence = fence;
                    return Tuple.Create(record, IsStaleUndeliveredAction(row));
                }, (index, id, reason) => ReignLog.Warn("Rejected server action poll row index=" + index
                    + " action=" + (id == null ? "(unavailable)" : id.Substring(0, Math.Min(id.Length, 128)))
                    + " reason=" + reason));

                var records = new List<ReignWorldActionRecord>(parsed.Count);
                foreach (var row in parsed)
                {
                    if (row.Item2)
                    {
                        ReignLog.Warn("Rejected stale server action " + row.Item1.ActionId + " type=" + row.Item1.Type);
                        _ = ReportActionAsync(row.Item1, "expired", "Undelivered action exceeded the six-hour delivery window.");
                    }
                    else records.Add(row.Item1);
                }
                return records;
            }).ConfigureAwait(false);
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

                // Reports also originate from workers (outbox expiry, debug
                // harnesses). Capture the native payload, hero profiles and the
                // current load fence together on the Bannerlord thread; this runs
                // inline, exactly as before, for callers already on that thread.
                RequestFence fence = null;
                JObject payload = await ReignMainThread.InvokeAsync(() =>
                {
                    fence = RequestFence.Capture();
                    return BuildActionReportPayload(action, status, message, result);
                }).ConfigureAwait(false);

                JObject response = await PostJsonAsync("/actions/report", payload).ConfigureAwait(false);
                await ApplyLegacyReportRelationChangesAsync(response, fence, action.ActionId).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                ReignLog.Warn("Local server action report failed: " + ex.Message);
            }
        }

        // Existing legacy report effect: raw native deltas, applied once under the
        // fence captured before the report was sent.
        internal static async Task ApplyLegacyReportRelationChangesAsync(JObject response, RequestFence fence, string actionId,
            Func<bool> requestOwner = null)
        {
            JArray relationChanges = response?["nativeRelationChanges"] as JArray ?? new JArray();
            if (relationChanges.Count == 0) return;
            await ReignMainThread.InvokeAsync(() =>
            {
                if (fence == null || !fence.IsCurrent() || !RequestOwnerCurrent(requestOwner))
                {
                    ReignLog.Warn("Action report relation changes skipped: the campaign changed action=" + (actionId ?? string.Empty));
                    return;
                }
                foreach (JObject change in relationChanges.OfType<JObject>())
                {
                    Hero subject = ReignObjectResolver.FindHero(change.Value<string>("subjectId") ?? string.Empty);
                    Hero target = ReignObjectResolver.FindHero(change.Value<string>("targetId") ?? string.Empty);
                    int delta = change.Value<int?>("delta") ?? 0;
                    if (subject != null && target != null && delta != 0)
                    {
                        TaleWorlds.CampaignSystem.Actions.ChangeRelationAction.ApplyRelationChangeBetweenHeroes(subject, target, delta, true);
                    }
                }
            }).ConfigureAwait(false);
        }

        // Game thread only: the exact legacy-shaped report a terminal protocol POST files.
        internal static string BuildTerminalReportJson(ReignWorldActionRecord action, string status, string message, ReignActionResult result) =>
            BuildActionReportPayload(action, status, message, result).ToString(Formatting.None);

        private static JObject BuildActionReportPayload(ReignWorldActionRecord action, string status, string message, ReignActionResult result)
        {
            JObject payload = new JObject
            {
                ["campaignId"] = GetCampaignId(),
                ["serverActionId"] = action.ActionId ?? string.Empty,
                ["timelineId"] = ReignServices.WorldHistory?.TimelineId ?? "main",
                ["playerHeroStringId"] = Hero.MainHero?.StringId ?? string.Empty,
                ["sharedActivityGeneration"] = ReignServices.WorldHistory?.SharedActivityGeneration ?? string.Empty,
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

            return payload;
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

        // Readiness gates, then the shared transport.
        internal static async Task<JObject> GetJsonAsync(string route, System.Net.Http.HttpClient requestClient = null)
        {
            await ReignSaveSyncCoordinator.WaitForReadyAsync(route).ConfigureAwait(false);
            await ReignCampaignInitializationGate.WaitForReadyAsync(route).ConfigureAwait(false);
            return await ReignHttpTransport.GetJsonAsync(route, requestClient).ConfigureAwait(false);
        }

        // Null when the token's response did not come through a qualifying import route.
        internal static string ResponseOrigin(JToken token) => ReignHttpTransport.ResponseOrigin(token);

        // Terminal action report v1: readiness gates, then the exact pinned origin only (see
        // ReignHttpTransport.SendPinnedJsonAsync). requestOwner is a required game-thread predicate.
        internal static async Task<JObject> SendPinnedJsonAsync(string origin, string pathAndQuery, JObject payload, Func<bool> requestOwner)
        {
            if (requestOwner == null) throw new ArgumentNullException(nameof(requestOwner));
            await ReignSaveSyncCoordinator.WaitForReadyAsync(pathAndQuery).ConfigureAwait(false);
            await ReignCampaignInitializationGate.WaitForReadyAsync(pathAndQuery).ConfigureAwait(false);
            return await ReignHttpTransport.SendPinnedJsonAsync(origin, pathAndQuery, payload, requestOwner).ConfigureAwait(false);
        }

        internal static JObject HttpTransportDiagnostics() => ReignHttpTransport.HttpTransportDiagnostics();

        // ---- Individual Chat view-insertion receipt v1 (client). ----
        // Attests only that the guarded game-thread AddChatLine inserted the exact accepted
        // NPC line into its matching individual view: never delivery, render, audio or a
        // human observation. Unknown, older or ineligible descriptors stay normal replies.
        private const string IndividualViewInsertionSchema = "reign-individual-view-insertion-v1";
        private const string IndividualViewInsertionOptInField = "viewInsertionReceipt";
        private const string IndividualViewInsertionAckSchema = "reign-individual-view-insertion-ack-v1";
        private const string IndividualViewInsertionResultSchema = "reign-individual-view-insertion-result-v1";
        private const string IndividualViewInsertionRoute = "/conversation/view-insertion";
        private static readonly string[] IndividualViewInsertionAckFields = { "campaignId", "timelineId", "unitId",
            "sessionId", "sourceTurnId", "role", "speakerHeroStringId", "playerHeroStringId", "restoreGeneration", "textSha256" };

        // One captured insertion: its proven response origin and immutable 11-string body.
        internal sealed class IndividualViewInsertionAck
        {
            internal IndividualViewInsertionAck(string origin, JObject body) { Origin = origin; Body = body; }
            internal string Origin { get; }
            internal JObject Body { get; }
        }

        private static string ExactJsonString(JObject source, string key) =>
            source?[key] is JValue value && value.Type == JTokenType.String ? (string)value.Value : null;

        // Game thread, in the same callback that just inserted lineText. Returns null unless
        // the exact current reply carries an eligible v1 descriptor bound to this request's
        // live scope, its accepted turn IDs, the raw line hash and a proven response origin.
        internal static IndividualViewInsertionAck CaptureIndividualViewInsertion(JObject response, string lineText,
            string campaignId, string timelineId, string sessionId, string playerHeroId, string npcHeroId,
            string restoreGeneration)
        {
            JObject exchange = response?["conversationExchange"] as JObject;
            JObject unit = exchange?["acceptedUnit"] as JObject;
            JObject descriptor = unit?["viewInsertion"] as JObject;
            string origin = ResponseOrigin(response);
            if (descriptor == null || string.IsNullOrEmpty(lineText) || !ReignHttpTransport.IsAllowedPinnedOrigin(origin)
                || ExactJsonString(descriptor, "schema") != IndividualViewInsertionSchema
                || ExactJsonString(descriptor, "status") != "eligible") return null;
            var body = new JObject { ["schema"] = IndividualViewInsertionAckSchema };
            foreach (string field in IndividualViewInsertionAckFields)
            {
                string value = ExactJsonString(descriptor, field);
                if (field != "role" && field != "textSha256"
                    && !Reign.Core.Contracts.Actions.TerminalActionReportProtocol.IsTrustedIdentifier(value)) return null;
                body[field] = value;
            }
            string turn = (string)body["sourceTurnId"];
            bool InTurnIds(JToken ids) => ids is JArray array && array.Any(id => id.Type == JTokenType.String && (string)id == turn);
            if ((string)body["role"] != "npc"
                || (string)body["textSha256"] != ReignConversationInputJournal.Hash(lineText)
                || (string)body["campaignId"] != campaignId || (string)body["timelineId"] != timelineId
                || (string)body["sessionId"] != sessionId || ExactJsonString(exchange, "sessionId") != sessionId
                || (string)body["playerHeroStringId"] != playerHeroId || (string)body["speakerHeroStringId"] != npcHeroId
                || (string)body["unitId"] != ExactJsonString(unit, "unitId")
                || (string)body["restoreGeneration"] != ExactJsonString(unit, "restoreGeneration")
                || string.IsNullOrEmpty(restoreGeneration) || (string)body["restoreGeneration"] != restoreGeneration
                || !InTurnIds(exchange["turnIds"]) || !InTurnIds(unit["sourceIds"])) return null;
            return new IndividualViewInsertionAck(origin, body);
        }

        // Delivers one captured insertion to its exact pinned origin. owner is the original
        // load's game-thread guard, rechecked before every dispatch and retry. Transport
        // ambiguity retries the identical body (the server records an exact replay once);
        // any definite non-inserted answer stops. Never regenerates, never fails the reply.
        internal static async Task<bool> SendIndividualViewInsertionAsync(IndividualViewInsertionAck ack, Func<bool> owner)
        {
            if (ack == null || owner == null) return false;
            for (int attempt = 1; ; attempt++)
            {
                JObject result;
                try { result = await SendPinnedJsonAsync(ack.Origin, IndividualViewInsertionRoute, ack.Body, owner).ConfigureAwait(false); }
                catch (ReignRequestOwnerAbandonedException) { return false; }
                catch (ReignHttpTransport.PinnedHttpRefusalException ex) when (ex.IsDefiniteClientRefusal)
                {
                    // A definite 4xx refusal is not transport ambiguity; resending the
                    // identical body cannot change it.
                    ReignLog.Info("View insertion receipt refused status=" + ex.StatusCode);
                    return false;
                }
                catch (Exception ex) when (attempt < 3)
                {
                    ReignLog.Info("View insertion receipt retry attempt=" + attempt + " error=" + ex.GetType().Name);
                    await Task.Delay(TimeSpan.FromSeconds(2 * attempt)).ConfigureAwait(false);
                    continue;
                }
                catch (Exception ex)
                {
                    ReignLog.Info("View insertion receipt unknown error=" + ex.GetType().Name);
                    return false;
                }
                bool inserted = result?["ok"] is JValue okValue && okValue.Type == JTokenType.Boolean && okValue.Value is bool okFlag && okFlag
                    && ExactJsonString(result, "schema") == IndividualViewInsertionResultSchema
                    && ExactJsonString(result, "disposition") == "inserted"
                    && new[] { "campaignId", "unitId", "sourceTurnId", "restoreGeneration", "textSha256" }
                        .All(field => ExactJsonString(result, field) == (string)ack.Body[field])
                    && result["acknowledgedTs"] is JValue tsValue && tsValue.Type == JTokenType.Integer
                    && (tsValue.Value is long ts ? ts > 0 : tsValue.Value is int tsInt && tsInt > 0);
                if (!inserted)
                    ReignLog.Info("View insertion receipt not recorded disposition="
                        + (ExactJsonString(result, "disposition") ?? "") + " reason=" + (ExactJsonString(result, "reason") ?? ""));
                return inserted;
            }
        }

        public static Task<JObject> SetSocialBalanceUnderlyingAffinityAsync(JObject payload)
        {
            return PostJsonAsync("/tests/social-balance/affinity/set", payload ?? new JObject());
        }

        public static Task<JObject> RankSocialBalanceAffairCandidatesAsync(JObject payload)
        {
            return PostJsonAsync("/tests/social-balance/affair/candidates", payload ?? new JObject());
        }

        internal static Task<JObject> PostJsonAsync(string route, JObject payload, Func<Task<bool>> isCurrentRequest = null)
        {
            return PostJsonCoreAsync(route, payload, isCurrentRequest, null);
        }

        // requestOwner is a local Bannerlord-thread predicate (for example an
        // accepted Individual Chat turn). It is rechecked before every owned
        // native preflight effect and before transport dispatch. A request that
        // already left may finish: its original-payload conversation journal
        // acknowledgement is durable and does not depend on the owner.
        internal static Task<JObject> PostOwnedJsonAsync(string route, JObject payload, Func<bool> requestOwner)
        {
            return PostJsonCoreAsync(route, payload, null, requestOwner);
        }

        private static async Task<JObject> PostJsonCoreAsync(string route, JObject payload,
            Func<Task<bool>> isCurrentRequest, Func<bool> requestOwner)
        {
            bool conversationInput = payload != null && (route == "/dialogue/respond" || route == "/party-chat/respond"
                || route == "/events/social/respond" || route == "/events/social/turn" || route == "/events/social/turn-plan"
                || route == "/court/life/respond" || route == "/court/royal-council/respond" || route == "/court/ruler-petition/respond"
                || route == "/castle-chat/scene/generate" || route == "/family-chambers/scene/generate");
            bool approachObservation = payload != null && route == "/npc-lives/open";
            if (conversationInput) ReignConversationInputJournal.Prepare(payload);
            await ReignSaveSyncCoordinator.WaitForReadyAsync(route).ConfigureAwait(false);
            await ReignCampaignInitializationGate.WaitForReadyAsync(route).ConfigureAwait(false);
            if (isCurrentRequest != null && !await isCurrentRequest().ConfigureAwait(false))
                throw new OperationCanceledException("Request belongs to a previous loaded save.");
            await EnsureRequestOwnerAsync(requestOwner, "pregnancy context synchronization").ConfigureAwait(false);
            if (conversationInput || approachObservation || route == "/correspondence/send" || route == "/memory/conversation/start" || route == "/party-banter/start")
                await ReignPregnancyContextClient.SynchronizePregnancyContextAsync(route, payload, requestOwner).ConfigureAwait(false);
            if (route == "/world-history/ingest-batch" && !ReignWorldHistoryTransport.IsCurrentRequest(payload))
                throw new OperationCanceledException("World-history request belongs to a previous loaded save.");
            ReignPersonalGossipClient.PersonalGossipPresenceSnapshot gossipSnapshot = null;
            if (conversationInput || approachObservation || payload != null && route == "/correspondence/send")
            {
                await EnsureRequestOwnerAsync(requestOwner, "public announcement synchronization").ConfigureAwait(false);
                if (ReignServices.WorldDiplomacy != null)
                    await ReignServices.WorldDiplomacy.SynchronizePublicAnnouncementsForDialogueAsync().ConfigureAwait(false);
                await EnsureRequestOwnerAsync(requestOwner, "personal gossip refresh").ConfigureAwait(false);
                gossipSnapshot = await ReignPersonalGossipClient.RefreshPersonalGossipConversationAsync(payload).ConfigureAwait(false);
                if (isCurrentRequest != null && !await isCurrentRequest().ConfigureAwait(false))
                    throw new OperationCanceledException("Request belongs to a previous loaded save.");
                double nativeDay = await ReignMainThread.InvokeAsync(() =>
                {
                    // Same game-thread callback as the shared party-contact capture.
                    EnsureRequestOwner(requestOwner, "shared party contact capture");
                    if ((ReignServices.News?.PreparePublicHistoryForDialogue() ?? 0) > 0)
                        throw new InvalidOperationException("Public news could not be synchronized yet. Please try the conversation again shortly.");
                    ReignServices.WorldHistory?.CaptureSharedPartyContact();
                    if (ReignServices.WorldHistory != null)
                        payload["worldHistorySequence"] = ReignServices.WorldHistory.Sequence;
                    var partySnapshot = BuildConversationPartyRoster();
                    payload["partyContext"] = partySnapshot;
                    if (payload["localPresence"] is JObject presence)
                        presence["partyContext"] = partySnapshot.DeepClone();
                    if (ReignCalendarService.TryGetCalendarPosition(CampaignTime.Now, out int year, out _, out int season, out int day, out _))
                    {
                        double hour = (CampaignTime.Now.ToDays % 1d) * 24d;
                        payload["conversationDateLabel"] = year + " " + new[] { "Spring", "Summer", "Autumn", "Winter" }[season]
                            + " " + (day + 1) + " " + (hour < 6 || hour >= 21 ? "Night" : hour < 12 ? "Morning" : hour < 18 ? "Afternoon" : "Evening");
                    }
                    return TaleWorlds.CampaignSystem.Campaign.Current == null ? 0d : CampaignTime.Now.ToDays;
                }).ConfigureAwait(false);
                if (nativeDay > 0d) payload["worldDay"] = nativeDay;
                var publicationSyncTimer = System.Diagnostics.Stopwatch.StartNew();
                JObject synchronization;
                do
                {
                    synchronization = await Task.Run(() => ReignWorldHistoryTransport.SynchronizeSharedActivitiesAsync()).ConfigureAwait(false);
                    if ((synchronization.Value<int?>("pendingPublicNews") ?? 0) > 0)
                        await Task.Delay(200).ConfigureAwait(false);
                    // Drain newly published facts before the first writer call.
                    // This is idempotent history transport, never LLM regeneration.
                } while ((synchronization.Value<int?>("pendingPublicNews") ?? 0) > 0 && publicationSyncTimer.Elapsed.TotalSeconds < 8);
                payload["sharedActivitySync"] = synchronization;
                if ((synchronization.Value<int?>("pendingPublicNews") ?? 0) > 0)
                    throw new InvalidOperationException("Public news is still synchronizing. Please try the conversation again shortly.");
            }
            else if (payload?["gossipPresence"] is JObject pendingGossip && pendingGossip.Count == 0)
            {
                await EnsureRequestOwnerAsync(requestOwner, "personal gossip refresh").ConfigureAwait(false);
                gossipSnapshot = await ReignPersonalGossipClient.RefreshPersonalGossipConversationAsync(payload).ConfigureAwait(false);
            }
            if (gossipSnapshot != null)
            {
                var callerGuard = isCurrentRequest;
                isCurrentRequest = async () => (callerGuard == null || await callerGuard().ConfigureAwait(false))
                    && await ReignMainThread.InvokeAsync(() => ReignPersonalGossipClient.PersonalGossipSnapshotCurrent(gossipSnapshot)).ConfigureAwait(false);
            }
            return await ReignHttpTransport.PostAsync(route, payload, isCurrentRequest, requestOwner,
                payload?["gossipPresence"] != null,
                async parsedResponse =>
                {
                    if (conversationInput)
                    {
                        string receiptWarning = ReignConversationInputJournal.Acknowledge(payload, parsedResponse);
                        if (!string.IsNullOrEmpty(receiptWarning)) ReignLog.Warn("Conversation delivery reconciliation: " + receiptWarning);
                    }
                    if (conversationInput && parsedResponse.Value<bool?>("ok") == true)
                    {
                        _ = ReignPersonalGossipClient.TickPersonalGossipAsync();
                        // Accepted disclosures are now durable. Refresh menu eligibility after publication.
                        // An owner closed after acceptance skips only this refresh;
                        // the journal ack and accepted response keep their meaning.
                        try { await ReignPregnancyContextClient.SynchronizePregnancyContextAsync(route, payload, requestOwner).ConfigureAwait(false); }
                        catch (ReignRequestOwnerAbandonedException) { }
                        catch (Exception ex) { ReignLog.Warn("Pregnancy knowledge refresh deferred: " + ex.GetType().Name); }
                    }
                }).ConfigureAwait(false);
        }

        // Request-owner helpers for the route clients (see ReignRequestOwnership).
        private static Task EnsureRequestOwnerAsync(Func<bool> requestOwner, string stage) =>
            ReignRequestOwnership.EnsureAsync(requestOwner, stage);

        internal static void EnsureRequestOwner(Func<bool> requestOwner, string stage) =>
            ReignRequestOwnership.Ensure(requestOwner, stage);

        internal static bool RequestOwnerCurrent(Func<bool> requestOwner) => ReignRequestOwnership.IsCurrent(requestOwner);

        // Forwarders for the live/world test partials, whose heartbeat sources call these by simple name.
        internal static JObject BuildRelationshipOutboxDiagnostics() => ReignRelationshipDirectorClient.BuildRelationshipOutboxDiagnostics();

        internal static JObject PersonalGossipRuntimeStatus() => ReignPersonalGossipClient.PersonalGossipRuntimeStatus();

        // Captured on the Bannerlord thread: the exact loaded campaign instance,
        // Save Sync load generation, campaign-initialization generation, campaign
        // id and timeline. IsCurrent must also run on the Bannerlord thread.
        internal sealed class RequestFence
        {
            private readonly object _campaign;
            private readonly int _saveGeneration;
            private readonly string _initializationGeneration;
            private readonly string _campaignId;
            private readonly string _timelineId;

            // Shared activity generation observed at capture. Informational only: IsCurrent
            // is unchanged; terminal report admission binds to this value, never a later one.
            internal string SharedActivityGeneration { get; }

            private RequestFence(object campaign, int saveGeneration, string initializationGeneration, string campaignId, string timelineId,
                string sharedActivityGeneration)
            {
                SharedActivityGeneration = sharedActivityGeneration ?? string.Empty;
                _campaign = campaign;
                _saveGeneration = saveGeneration;
                _initializationGeneration = initializationGeneration;
                _campaignId = campaignId;
                _timelineId = timelineId;
            }

            internal static RequestFence Capture() => new RequestFence(TaleWorlds.CampaignSystem.Campaign.Current,
                ReignSaveSyncCoordinator.CampaignGeneration, InitializationGeneration(), GetCampaignId(), CurrentTimeline(),
                ReignServices.WorldHistory?.SharedActivityGeneration);

            internal bool IsCurrent() => _campaign != null
                && ReferenceEquals(_campaign, TaleWorlds.CampaignSystem.Campaign.Current)
                && _saveGeneration == ReignSaveSyncCoordinator.CampaignGeneration
                && string.Equals(_initializationGeneration, InitializationGeneration(), StringComparison.Ordinal)
                && string.Equals(_campaignId, GetCampaignId(), StringComparison.Ordinal)
                && string.Equals(_timelineId, CurrentTimeline(), StringComparison.Ordinal);

            internal bool IsCurrentActionPoll() => !ReignSaveSyncCoordinator.IsAlignmentPending && IsCurrent()
                && string.Equals(SharedActivityGeneration, ReignServices.WorldHistory?.SharedActivityGeneration ?? string.Empty,
                    StringComparison.Ordinal);

            private static string InitializationGeneration() =>
                ReignCampaignInitializationGate.Snapshot().Value<string>("generationId") ?? string.Empty;

            private static string CurrentTimeline() => ReignServices.WorldHistory?.TimelineId ?? "main";
        }

        internal static ReignWorldActionRecord ParseActionRecord(JObject obj, string pollActionId = null)
        {
            if (obj == null)
            {
                return null;
            }

            float now = TaleWorlds.CampaignSystem.Campaign.Current == null ? 0f : (float)CampaignTime.Now.ToDays;
            ReignWorldActionRecord action = new ReignWorldActionRecord
            {
                ActionId = pollActionId ?? ReadString(obj, "actionId", ReadString(obj, "serverActionId", Guid.NewGuid().ToString("N"))),
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

        // Callers without a request-time fence parse on the game thread after their own
        // owner check, so a capture here binds the same loaded owner.
        internal static List<ReignWorldActionRecord> ParseQueuedActionRows(JToken token)
            => ParseQueuedActionRows(token, ResponseOrigin(token) == null ? null : RequestFence.Capture());

        // Dialogue passes its original request fence so a response cannot rebind to a
        // newer server generation while it was waiting.
        internal static List<ReignWorldActionRecord> ParseQueuedActionRows(JToken token, RequestFence fence)
        {
            List<ReignWorldActionRecord> results = new List<ReignWorldActionRecord>();
            JArray rows = token as JArray;
            if (rows == null)
            {
                return results;
            }

            string origin = ResponseOrigin(token);
            RequestFence importFence = origin == null ? null : fence;
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
                    record.ImportOrigin = origin;
                    record.ImportFence = importFence;
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
                case "follow":
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
            request["timelineId"] = ReignServices.WorldHistory
                ?.TimelineId ?? "main";
            return PostJsonAsync("/arrests/case/apply", request);
        }

        public static Task<JObject> EvaluateArrestEvidenceAsync(JObject payload)
        {
            JObject request = payload == null ? new JObject() : new JObject(payload);
            request["campaignId"] = GetCampaignId();
            request["timelineId"] = ReignServices.WorldHistory
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

        internal static string ActiveNativeSaveName()
        {
            try
            {
                object value = typeof(TaleWorlds.Core.MBSaveLoad).GetProperty("ActiveSaveSlotName",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)?.GetValue(null, null);
                return Convert.ToString(value) ?? string.Empty;
            }
            catch { return string.Empty; }
        }

        // Product forwarder: ReignAICampaignBehavior schedules the daily World Test heartbeat owned by the test harness.
        public static Task<double?> SubmitWorldTestHeartbeatAsync(double deltaAfterDay) => ReignBeta.Platform.Runtime.ReignTestHarness.Current.SubmitWorldTestHeartbeatAsync(deltaAfterDay);

        internal static string ReadString(JObject obj, string key, string fallback)
        {
            JToken token = obj?[key];
            return token == null || token.Type == JTokenType.Null ? fallback : token.ToString();
        }

        private static int ReadInt(JObject obj, string key, int fallback)
        {
            JToken token = obj?[key];
            return token == null || token.Type == JTokenType.Null || !int.TryParse(token.ToString(), out int value) ? fallback : value;
        }

        internal static float ReadFloat(JObject obj, string key, float fallback)
        {
            JToken token = obj?[key];
            return token == null || token.Type == JTokenType.Null || !float.TryParse(token.ToString(), out float value) ? fallback : value;
        }

        internal static bool ReadBool(JObject obj, string key, bool fallback)
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
}
