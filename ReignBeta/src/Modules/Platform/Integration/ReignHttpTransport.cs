using System;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace ReignBeta.Integration
{
    // The one HTTP transport to the local Reign server: pooled clients per route class,
    // candidate endpoint failover with the shared circuit breaker, transport diagnostics,
    // response provenance and the pinned terminal-report channel. Readiness gates,
    // conversation preparation and module routes stay with the callers (ReignServerClient).
    internal static class ReignHttpTransport
    {
        internal const int InteractiveProviderTimeoutSeconds = 900;
        internal static readonly HttpClient Client = CreateLocalServerHttpClient(8);
        internal static readonly HttpClient PriorityClient = CreateLocalServerHttpClient(4);
        internal static readonly HttpClient InteractiveClient =
            CreateLocalServerHttpClient(4, InteractiveProviderTimeoutSeconds);
        internal static readonly HttpClient LiveTestClient = CreateLocalServerHttpClient(4, 5);
        // Terminal action report v1 only: one proven origin, never redirected.
        internal static readonly HttpClient PinnedClient = CreateLocalServerHttpClient(2, 30, false);
        // Local response provenance for the two protocol import routes. A value is the
        // proven origin; an empty value marks a qualifying response whose origin is unproven.
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<JObject, string> ResponseOrigins =
            new System.Runtime.CompilerServices.ConditionalWeakTable<JObject, string>();
        private static long _streamedPostRequests;
        private static long _streamedPostBytes;
        private static long _largeStreamedPostRequests;
        private static long _peakStreamedPostBytes;
        private static long _totalPostSerializationMs;
        private static long _peakPostSerializationMs;

        internal static HttpClient CreateLocalServerHttpClient(int maximumConnections, int timeoutSeconds = 360, bool allowAutoRedirect = true)
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
                UseProxy = false,
                AllowAutoRedirect = allowAutoRedirect
            };
            return new HttpClient(new Reign.Core.Contracts.Platform.ReignProtocolHandler(handler)) { Timeout = TimeSpan.FromSeconds(Math.Max(2, timeoutSeconds)) };
        }

        internal static bool IsPriorityServerRoute(string route)
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

        internal static bool IsProviderBackedInteractiveRoute(string route)
        {
            string value = route ?? string.Empty;
            return value.Equals("/dialogue/respond", StringComparison.OrdinalIgnoreCase)
                || value.Equals("/party-chat/respond", StringComparison.OrdinalIgnoreCase)
                || value.Equals("/tavern-house/respond", StringComparison.OrdinalIgnoreCase)
                || value.Equals("/tavern-house/scene", StringComparison.OrdinalIgnoreCase)
                || value.Equals("/family-chambers/scene/generate", StringComparison.OrdinalIgnoreCase)
                || value.Equals("/portraits/generate", StringComparison.OrdinalIgnoreCase);
        }

        internal static bool IsHighFrequencyControlRoute(string route)
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

        internal static void RecordStreamedPost(ReignJsonHttpContent content)
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

        internal static void UpdateMaximum(ref long target, long value)
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

        internal static async Task<JObject> GetJsonAsync(string route, HttpClient requestClient)
        {
            if (!ReignServerEndpoint.TryBeginRequest(out string unavailable, out long probeClaim))
            {
                throw new HttpRequestException(unavailable);
            }

            try
            {
                Exception lastTransportFailure = null;
                foreach (string baseUrl in ReignServerEndpoint.CandidateBaseUrls())
                {
                    string url = baseUrl.TrimEnd('/') + (route.StartsWith("/", StringComparison.Ordinal) ? route : "/" + route);
                    try
                    {
                        using (HttpResponseMessage response = await (requestClient ?? Client).GetAsync(url).ConfigureAwait(false))
                        {
                            string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                            ReignServerEndpoint.ReportSuccess(baseUrl, probeClaim);
                            if (!response.IsSuccessStatusCode)
                            {
                                throw new InvalidOperationException("HTTP " + (int)response.StatusCode + ": " + body);
                            }

                            return RecordResponseOrigin(route, baseUrl, url, response, JObject.Parse(body));
                        }
                    }
                    catch (Exception ex) when (ReignServerEndpoint.IsTransportFailure(ex))
                    {
                        lastTransportFailure = ex;
                    }
                }

                ReignServerEndpoint.ReportTransportFailure(probeClaim);
                throw lastTransportFailure ?? new HttpRequestException("Local Bannerlord Reign server is unavailable.");
            }
            finally
            {
                // Unconditional matching release: a no-op once this operation's
                // report settled its claim, never able to clear a newer probe.
                ReignServerEndpoint.ReleaseRequest(probeClaim);
            }
        }

        // Records local provenance only for the two terminal-report import routes. A
        // redirected response, a path-prefixed base URL or an unobservable final target
        // proves no origin; such a record stays a pending candidate, never another server's.
        internal static JObject RecordResponseOrigin(string route, string baseUrl, string requestUrl, HttpResponseMessage response, JObject parsed)
        {
            if (parsed == null || !(route == "/dialogue/respond" || route.StartsWith("/actions/next?", StringComparison.Ordinal))) return parsed;
            string origin = PinnableOrigin(baseUrl);
            Uri final = response?.RequestMessage?.RequestUri;
            if (final == null || !Uri.TryCreate(requestUrl, UriKind.Absolute, out Uri requested)
                || !string.Equals(final.AbsoluteUri, requested.AbsoluteUri, StringComparison.Ordinal)) origin = string.Empty;
            ResponseOrigins.Remove(parsed);
            ResponseOrigins.Add(parsed, origin);
            return parsed;
        }

        // Null when the token's response did not come through a qualifying import route.
        internal static string ResponseOrigin(JToken token)
        {
            JObject root = token?.Root as JObject;
            return root != null && ResponseOrigins.TryGetValue(root, out string origin) ? origin : null;
        }

        internal static string PinnableOrigin(string baseUrl)
        {
            string trimmed = (baseUrl ?? string.Empty).Trim().TrimEnd('/');
            if (!Uri.TryCreate(trimmed, UriKind.Absolute, out Uri uri) || uri.UserInfo.Length > 0
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)) return string.Empty;
            string origin = uri.GetLeftPart(UriPartial.Authority);
            return string.Equals(origin, trimmed, StringComparison.OrdinalIgnoreCase) ? origin : string.Empty;
        }

        // A pinned origin stays usable only while it is one of the currently allowed endpoints.
        internal static bool IsAllowedPinnedOrigin(string origin) => !string.IsNullOrEmpty(origin)
            && ReignServerEndpoint.CandidateBaseUrls().Any(baseUrl => string.Equals(PinnableOrigin(baseUrl), origin, StringComparison.Ordinal));

        // A definite HTTP answer from the exact pinned origin. It stays an
        // InvalidOperationException, with the same message, for existing callers.
        internal sealed class PinnedHttpRefusalException : InvalidOperationException
        {
            internal PinnedHttpRefusalException(int statusCode)
                : base("HTTP " + statusCode + " from the pinned terminal report endpoint.") { StatusCode = statusCode; }
            internal int StatusCode { get; }
            // 408 Request Timeout and 429 Too Many Requests are transient answers that keep
            // the caller's bounded retry.
            internal bool IsDefiniteClientRefusal => StatusCode >= 400 && StatusCode < 500
                && StatusCode != 408 && StatusCode != 429;
        }

        // Terminal action report v1 transport: the exact pinned origin only, no redirects,
        // no candidate failover and no shared circuit-breaker state. The protocol handler
        // still performs its handshake and adds its header. Failures throw without the
        // response body; the caller keeps its exact report pending. requestOwner is a
        // required game-thread predicate, rechecked here (after the caller's readiness gates)
        // and again immediately before dispatch (an abandoned owner sends nothing).
        internal static async Task<JObject> SendPinnedJsonAsync(string origin, string pathAndQuery, JObject payload, Func<bool> requestOwner)
        {
            if (requestOwner == null) throw new ArgumentNullException(nameof(requestOwner));
            await EnsureRequestOwnerAsync(requestOwner, "pinned request preparation").ConfigureAwait(false);
            if (!IsAllowedPinnedOrigin(origin))
                throw new InvalidOperationException("The pinned terminal report origin is no longer an allowed local endpoint.");
            string url = origin + pathAndQuery;
            using (var request = new HttpRequestMessage(payload == null ? HttpMethod.Get : HttpMethod.Post, url))
            {
                if (payload != null) request.Content = new ReignJsonHttpContent(payload);
                await EnsureRequestOwnerAsync(requestOwner, "pinned transport dispatch").ConfigureAwait(false);
                using (HttpResponseMessage response = await PinnedClient.SendAsync(request).ConfigureAwait(false))
                {
                    Uri final = response.RequestMessage?.RequestUri;
                    if (final == null || !string.Equals(final.AbsoluteUri, new Uri(url).AbsoluteUri, StringComparison.Ordinal))
                        throw new InvalidOperationException("The pinned terminal report response came from a different endpoint.");
                    if (!response.IsSuccessStatusCode)
                        throw new PinnedHttpRefusalException((int)response.StatusCode);
                    return JObject.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
                }
            }
        }

        // One POST to the local server: pooled client per route class, candidate endpoint
        // failover and the shared circuit breaker. isCurrentRequest and requestOwner are
        // rechecked before encoding and again before dispatch. encodeOffThread moves large
        // payload serialization off the game thread. onAccepted runs after a successful
        // response is parsed, before it is returned.
        internal static async Task<JObject> PostAsync(string route, JObject payload,
            Func<Task<bool>> isCurrentRequest, Func<bool> requestOwner, bool encodeOffThread,
            Func<JObject, Task> onAccepted)
        {
            bool liveTestRoute = route.StartsWith(
                "/tests/live/", StringComparison.OrdinalIgnoreCase);
            // The production bridge is itself the recovery/control plane. An
            // unrelated passive-system timeout must not place heartbeat, polling,
            // acknowledgement, or lifecycle traffic behind the shared endpoint
            // circuit breaker. Live routes use their own short-timeout client and
            // always probe loopback directly; a successful probe still clears the
            // shared breaker because it proves the server is reachable.
            long probeClaim = 0;
            if (!liveTestRoute
                && !ReignServerEndpoint.TryBeginRequest(out string unavailable, out probeClaim))
            {
                throw new HttpRequestException(unavailable);
            }

            try
            {
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
                        if (isCurrentRequest != null && !await isCurrentRequest().ConfigureAwait(false))
                            throw new OperationCanceledException("Request belongs to a previous loaded save.");
                        await EnsureRequestOwnerAsync(requestOwner, "request encoding").ConfigureAwait(false);
                        content = encodeOffThread
                            ? await Task.Run(() => new ReignJsonHttpContent(payload)).ConfigureAwait(false)
                            : new ReignJsonHttpContent(payload ?? new JObject());
                        using (content)
                        using (HttpRequestMessage request = new HttpRequestMessage(
                            HttpMethod.Post,
                            url))
                        {
                            if (isCurrentRequest != null && !await isCurrentRequest().ConfigureAwait(false))
                                throw new OperationCanceledException("Request belongs to a previous loaded save after encoding.");
                            await EnsureRequestOwnerAsync(requestOwner, "transport dispatch").ConfigureAwait(false);
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
                                // Live routes carry no claim (zero): explicit live
                                // reachability success, as before.
                                ReignServerEndpoint.ReportSuccess(baseUrl, probeClaim);
                                if (!response.IsSuccessStatusCode)
                                {
                                    throw new InvalidOperationException("HTTP " + (int)response.StatusCode + ": " + body);
                                }

                                JObject parsedResponse = RecordResponseOrigin(route, baseUrl, url, response, JObject.Parse(body));
                                // The caller's acceptance work runs here, inside the attempt, exactly where
                                // it ran before the transport was extracted.
                                if (onAccepted != null) await onAccepted(parsedResponse).ConfigureAwait(false);
                                return parsedResponse;
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
                    ReignServerEndpoint.ReportTransportFailure(probeClaim);
                throw lastTransportFailure ?? new HttpRequestException("Local Bannerlord Reign server is unavailable.");
            }
            finally
            {
                // Owner abandonment, cancellation and non-transport exceptions
                // release only this operation's own claim, without penalty; after
                // a report this matching release is a no-op.
                ReignServerEndpoint.ReleaseRequest(probeClaim);
            }
        }

        // One answered direct POST: the HTTP status and body of the first candidate endpoint
        // that answered, its base URL and request URL, and the final request URI after redirects.
        internal sealed class DirectJsonResponse
        {
            internal int StatusCode;
            internal bool IsSuccessStatusCode;
            internal string Body;
            internal string BaseUrl;
            internal string RequestUrl;
            internal Uri FinalRequestUri;
            internal long ElapsedMilliseconds;
        }

        // Direct JSON POST for callers that own their gating (Save Sync, the Court client):
        // the caller's HttpClient, compact JSON as UTF-8 application/json, candidate endpoint
        // failover, and the legacy (unclaimed) shared-breaker success/failure reports. No
        // readiness gate, probe claim, log line or status check here; the caller adds those.
        // Throws the last transport failure when no candidate answered.
        internal static async Task<DirectJsonResponse> PostDirectJsonAsync(HttpClient client, string route, string json)
        {
            Exception last = null;
            foreach (string baseUrl in ReignServerEndpoint.CandidateBaseUrls())
            {
                string url = baseUrl.TrimEnd('/') + (route.StartsWith("/", StringComparison.Ordinal) ? route : "/" + route);
                Stopwatch timer = Stopwatch.StartNew();
                try
                {
                    using (StringContent content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"))
                    using (HttpResponseMessage response = await client.PostAsync(url, content).ConfigureAwait(false))
                    {
                        string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        timer.Stop();
                        ReignServerEndpoint.ReportSuccess(baseUrl);
                        return new DirectJsonResponse
                        {
                            StatusCode = (int)response.StatusCode,
                            IsSuccessStatusCode = response.IsSuccessStatusCode,
                            Body = body,
                            BaseUrl = baseUrl,
                            RequestUrl = url,
                            FinalRequestUri = response.RequestMessage?.RequestUri,
                            ElapsedMilliseconds = timer.ElapsedMilliseconds
                        };
                    }
                }
                catch (Exception ex) when (ReignServerEndpoint.IsTransportFailure(ex)) { last = ex; }
            }
            ReignServerEndpoint.ReportTransportFailure();
            throw last ?? new HttpRequestException("Local Bannerlord Reign server is unavailable.");
        }

        private static Task EnsureRequestOwnerAsync(Func<bool> requestOwner, string stage) =>
            ReignRequestOwnership.EnsureAsync(requestOwner, stage);
    }
}
